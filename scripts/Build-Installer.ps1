[CmdletBinding()]
param(
    [string] $IsccPath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src/MindDump/MindDump.csproj'
[xml] $projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]($projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Set a three-part release Version in MindDump.csproj before building.'
}

if (-not $IsccPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $IsccPath = $command.Source }
    else {
        $candidates = @(
            "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
            "$env:USERPROFILE\AppData\Local\Programs\Inno Setup 7\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "$env:USERPROFILE\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
        )
        $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Install Inno Setup 7 or 6 (https://jrsoftware.org/isdl.php), or pass -IsccPath <path to ISCC.exe>.'
}

# A fresh staging directory avoids accidentally shipping stale publish files.
$output = Join-Path $repo "artifacts/installer-$version"
$publish = Join-Path $output ('publish-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $publish -Force | Out-Null
Push-Location $repo
try {
    & dotnet publish $project -c Release -r win-x64 --self-contained true "-p:PublishDir=$publish/"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    foreach ($name in @('MindDump.exe', 'App.xbf', 'MainWindow.xbf', 'MindDump.pri', 'coreclr.dll', 'Microsoft.UI.Xaml.dll')) {
        $file = Join-Path $publish $name
        if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -eq 0) {
            throw "Required publish file missing or empty: $name"
        }
    }
    $actualVersion = (Get-Item (Join-Path $publish 'MindDump.exe')).VersionInfo.ProductVersion.Split('+')[0]
    if ($actualVersion -ne $version) { throw "App version $actualVersion differs from installer version $version." }

    # Inventory every input. ISCC's build log enumerates the files it embeds.
    Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($publish, $_.FullName)
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    } | Set-Content -LiteralPath (Join-Path $output 'publish-SHA256SUMS.txt') -Encoding utf8

    & $IsccPath "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$output" (Join-Path $repo 'installer/MindDump.iss') 2>&1 |
        Tee-Object -FilePath (Join-Path $output 'installer-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
    $installer = Join-Path $output "MindDump-$version-win-x64-Setup.exe"
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer output missing.' }
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Installer: $installer"
    Write-Host "SHA256: $hash"
}
finally { Pop-Location }
