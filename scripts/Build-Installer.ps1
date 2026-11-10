[CmdletBinding()]
param(
    [string] $IsccPath,
    [string] $ExpectedTag,
    [switch] $NoRestore,
    [string] $SigningThumbprint,
    [string] $SignToolPath,
    [string] $TimestampUrl
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src/MindDump/MindDump.csproj'
[xml] $projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]($projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw 'Set a three-part release Version in MindDump.csproj before building.'
}
if ($ExpectedTag -and $ExpectedTag -cne "v$version") {
    throw "Tag $ExpectedTag must match project version v$version."
}

$signArguments = @()
if ($SigningThumbprint) {
    . (Join-Path $PSScriptRoot 'DevelopmentSignature.ps1')
    if ($SigningThumbprint -notmatch '^[0-9a-fA-F]{40}$') { throw 'SigningThumbprint must be a certificate SHA-1 thumbprint.' }
    $certificate = Get-Item "Cert:\CurrentUser\My\$SigningThumbprint" -ErrorAction Stop
    if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or
        $certificate.NotBefore -gt (Get-Date) -or
        $certificate.Subject -ne 'CN=MindDump Development (Self-Signed)' -or
        $certificate.Subject -ne $certificate.Issuer -or
        $certificate.EnhancedKeyUsageList.ObjectId -notcontains '1.3.6.1.5.5.7.3.3') {
        throw 'Select a current MindDump self-signed development code-signing certificate with a private key.'
    }
    if (-not $SignToolPath) {
        $SignToolPath = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
            Sort-Object { [version] $_.Directory.Parent.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath)) { throw 'Install Windows SDK SignTool or pass -SignToolPath.' }
    $signArguments = @('sign', '/s', 'My', '/sha1', $SigningThumbprint, '/fd', 'SHA256')
    if ($TimestampUrl) {
        if ($TimestampUrl -notmatch '^https?://[a-zA-Z0-9./:_-]+$') { throw 'TimestampUrl must be a simple HTTP(S) URL.' }
        $signArguments += @('/tr', $TimestampUrl, '/td', 'SHA256')
    }
}
elseif ($TimestampUrl -or $SignToolPath) { throw 'SigningThumbprint is required for signing options.' }

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
    $restoreArguments = @()
    if ($NoRestore) { $restoreArguments += '--no-restore' }
    & dotnet publish $project -c Release -r win-x64 --self-contained true "-p:PublishDir=$publish/" -p:UseSharedCompilation=false @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    foreach ($name in @('MindDump.exe', 'App.xbf', 'MainWindow.xbf', 'MindDump.pri', 'coreclr.dll', 'Microsoft.UI.Xaml.dll')) {
        $file = Join-Path $publish $name
        if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -eq 0) {
            throw "Required publish file missing or empty: $name"
        }
    }
    $actualVersion = (Get-Item (Join-Path $publish 'MindDump.exe')).VersionInfo.ProductVersion.Split('+')[0]
    if ($actualVersion -ne $version) { throw "App version $actualVersion differs from installer version $version." }

    $compilerSigning = @()
    if ($SigningThumbprint) {
        foreach ($name in @('MindDump.exe', 'MindDump.dll', 'MindDump.Core.dll')) {
            $file = Join-Path $publish $name
            & $SignToolPath @signArguments $file
            if ($LASTEXITCODE -ne 0) { throw "Signing failed: $name" }
            Assert-DevelopmentSignature $file -Thumbprint $SigningThumbprint -RequireTimestamp:([bool] $TimestampUrl)
        }
        # Inno signs its setup, temporary copies, and uninstaller itself.
        # $q and $f are Inno placeholders, not PowerShell interpolation.
        $signCommand = '$q' + $SignToolPath + '$q ' + ($signArguments -join ' ') + ' $f'
        $signCache = Join-Path $output "signed-uninstaller-$SigningThumbprint"
        New-Item -ItemType Directory -Path $signCache -Force | Out-Null
        $compilerSigning = @('/DDevelopmentSigning=1', "/DSigningCache=$signCache", "/Smddevelopment=$signCommand")
    }

    # Inventory every input. ISCC's build log enumerates the files it embeds.
    Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($publish, $_.FullName)
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    } | Set-Content -LiteralPath (Join-Path $output 'publish-SHA256SUMS.txt') -Encoding utf8

    & $IsccPath "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$output" @compilerSigning (Join-Path $repo 'installer/MindDump.iss') 2>&1 |
        Tee-Object -FilePath (Join-Path $output 'installer-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
    $buildLog = Get-Content -LiteralPath (Join-Path $output 'installer-build.log') -Raw
    $payloadFiles = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
    foreach ($payloadFile in $payloadFiles) {
        if (-not $buildLog.Contains('Compressing: \\?\' + $payloadFile.FullName, [StringComparison]::OrdinalIgnoreCase) -and
            -not $buildLog.Contains('Compressing: ' + $payloadFile.FullName, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Installer compiler did not report including: $($payloadFile.FullName)"
        }
    }
    Write-Host "Verified installer inclusion of all $($payloadFiles.Count) published files."
    $installer = Join-Path $output "MindDump-$version-win-x64-Setup.exe"
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer output missing.' }
    if ($SigningThumbprint) {
        Assert-DevelopmentSignature $installer -Thumbprint $SigningThumbprint -RequireTimestamp:([bool] $TimestampUrl)
        $uninstallers = @(Get-ChildItem -LiteralPath $signCache -Filter '*.e32' -File)
        if ($uninstallers.Count -eq 0) { throw 'Signed uninstaller cache missing.' }
        foreach ($uninstaller in $uninstallers) {
            Assert-DevelopmentSignature $uninstaller.FullName -Thumbprint $SigningThumbprint -RequireTimestamp:([bool] $TimestampUrl)
        }
    }
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Installer: $installer"
    Write-Host "SHA256: $hash"
    # Return paths for the release builder; keep human output on the host stream.
    [pscustomobject]@{ Version = $version; Publish = $publish; Installer = $installer; Output = $output }
}
finally { Pop-Location }
