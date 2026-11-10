[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$subject = 'CN=MindDump Development (Self-Signed)'
# Reuse the local identity. Never export its private key or import it into trust stores.
$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) -and
    $_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3'
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $certificate) {
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
        -FriendlyName 'MindDump development signing only' -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable `
        -NotAfter (Get-Date).AddYears(1)
}
$directory = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/signing'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
Export-Certificate -Cert $certificate -FilePath (Join-Path $directory 'MindDump-Development.cer') -Force | Out-Null
Write-Host 'Self-signed development certificate; no trust-store changes were made.'
Write-Host "Public certificate: $directory/MindDump-Development.cer"
Write-Output $certificate.Thumbprint
