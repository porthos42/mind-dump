[CmdletBinding()]
param(
    [string] $ExpectedTag,
    [string] $IsccPath,
    [switch] $Coverage,
    [switch] $NoRestore,
    [string] $SigningThumbprint,
    [string] $SignToolPath,
    [string] $TimestampUrl
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
# Validate before the expensive build, including on local release rehearsals.
[xml] $project = Get-Content (Join-Path $repo 'src/MindDump/MindDump.csproj') -Raw
$version = [string] $project.Project.PropertyGroup.Version
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Version must be three-part stable SemVer.' }
if ($ExpectedTag -and $ExpectedTag -cne "v$version") { throw "Tag must be v$version." }
$notes = Join-Path $repo "releases/$version.md"
if (-not (Test-Path -LiteralPath $notes)) { throw "Write release notes first: $notes" }

Push-Location $repo
try {
    if (-not $NoRestore) {
        & dotnet restore MindDump.slnx
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    }
    & dotnet build MindDump.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    # Direct MTP invocation also works where the dotnet test named pipe is blocked.
    $results = Join-Path $repo 'artifacts/test-results'
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    foreach ($report in @('tests.xml', 'coverage.cobertura.xml')) {
        $oldReport = Join-Path $results $report
        if (Test-Path -LiteralPath $oldReport) { Remove-Item -LiteralPath $oldReport }
    }
    $testExe = Join-Path $repo 'tests/MindDump.Tests/bin/x64/Release/net10.0/MindDump.Tests.exe'
    if ($Coverage) {
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'Coverage tool restore failed.' }
        & dotnet tool run dotnet-coverage -- collect -f cobertura -o (Join-Path $results 'coverage.cobertura.xml') "`"$testExe`" -result-junit `"$results/tests.xml`""
    }
    else { & $testExe -result-junit (Join-Path $results 'tests.xml') }
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    [xml] $testReport = Get-Content (Join-Path $results 'tests.xml') -Raw
    if ([int] $testReport.testsuites.tests -le 0 -or
        [int] $testReport.testsuites.failures -gt 0 -or
        [int] $testReport.testsuites.errors -gt 0) { throw 'Test report is empty or contains failures.' }
    if ($Coverage -and -not (Test-Path (Join-Path $results 'coverage.cobertura.xml'))) {
        throw 'Coverage report missing.'
    }
    # Compiler output is printed, not mixed with the returned build metadata.
    $build = & (Join-Path $PSScriptRoot 'Build-Installer.ps1') -IsccPath $IsccPath -ExpectedTag $ExpectedTag -NoRestore `
        -SigningThumbprint $SigningThumbprint -SignToolPath $SignToolPath -TimestampUrl $TimestampUrl |
        ForEach-Object {
            if ($_.PSObject.Properties.Name -contains 'Publish') { $_ }
            else { Write-Host $_ }
        }
    if (-not $build) { throw 'Installer build metadata missing.' }
    $stage = Join-Path $repo ('artifacts/release-' + $version + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    $zip = Join-Path $stage "MindDump-$version-win-x64-Portable.zip"
    Compress-Archive -Path (Join-Path $build.Publish '*') -DestinationPath $zip -CompressionLevel Optimal
    # Inspect every ZIP entry against the published payload, including bytes.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $files = @(Get-ChildItem $build.Publish -File -Recurse)
        $entries = @($archive.Entries | Where-Object { $_.Name })
        if ($files.Count -ne $entries.Count) { throw 'ZIP payload count differs.' }
        foreach ($file in $files) {
            $relative = [IO.Path]::GetRelativePath($build.Publish, $file.FullName).Replace('\', '/')
            $entry = $archive.GetEntry($relative)
            if (-not $entry) { throw "ZIP missing $relative" }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -ne (Get-FileHash $file.FullName -Algorithm SHA256).Hash) { throw "ZIP differs: $relative" }
        }
    }
    finally { $archive.Dispose() }
    Copy-Item -LiteralPath $build.Installer -Destination $stage
    Get-ChildItem $stage -File | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    } | Set-Content (Join-Path $stage 'SHA256SUMS.txt') -Encoding ascii
    Copy-Item -LiteralPath $notes -Destination (Join-Path $stage 'RELEASE-NOTES.md')
    if ($SigningThumbprint) {
        Export-Certificate -Cert (Get-Item "Cert:\CurrentUser\My\$SigningThumbprint") `
            -FilePath (Join-Path $stage 'MindDump-Development.cer') | Out-Null
        Add-Content (Join-Path $stage 'RELEASE-NOTES.md') "`nThis build is self-signed for testing; its publisher is not publicly trusted. Certificate thumbprint: $SigningThumbprint."
    }
    if ($env:GITHUB_OUTPUT) { "directory=$stage" | Add-Content $env:GITHUB_OUTPUT }
    Write-Host "Release assets: $stage"
}
finally { Pop-Location }
