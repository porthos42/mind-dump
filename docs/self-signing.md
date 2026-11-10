# Self-signed builds

Self-signing is the selected interim option. It provides a consistent local
signing identity but does not establish public publisher trust or remove
SmartScreen warnings on other PCs. Keep GitHub Actions unsigned for now: its
ephemeral runners do not have the local private key, and creating a new identity
on each run would defeat continuity. No credentials are uploaded.

## Create once and build locally

Run in your normal PowerShell terminal from the repository, without elevation:

```powershell
$thumbprint = ./scripts/New-DevelopmentCertificate.ps1
./scripts/Build-Release.ps1 -SigningThumbprint $thumbprint
```

The creation script reuses an eligible existing certificate or creates a one-year
RSA/SHA-256 code-signing certificate in `Cert:\CurrentUser\My`, subject
`CN=MindDump Development (Self-Signed)`. Its private key is non-exportable and
stays in the Windows user profile; the exported `artifacts/signing/MindDump-Development.cer`
contains only the public certificate. Keep using this identity on this PC.
Losing the profile/key means creating a new identity; no PFX backup is generated.

Install Windows SDK SignTool and Inno Setup first. Use `-SignToolPath` or
`-IsccPath` for nonstandard locations. For an offline rehearsal with restored
packages, append `-NoRestore`.

The build signs our `MindDump.exe`, `MindDump.dll` and `MindDump.Core.dll` before
packaging. Inno's signing hook signs setup, temporary setup copies and the
uninstaller. Third-party binaries are left as supplied. Verification requires the
requested certificate and rejects missing signatures, hash mismatches and other
errors; the specific Windows `CERT_E_UNTRUSTEDROOT` result is permitted because
this is a self-signed identity. PowerShell may label that result `UnknownError`;
other unknown errors are rejected. This
check is not a claim of public trust. ZIP and checksum generation follow signing.
The release directory includes a public `.cer` and a self-signed note; the checksum
file covers the ZIP and setup only.

## Timestamping

Default local tests omit timestamps to permit offline builds. Such signatures
should not be relied on after the certificate expires. For timestamped builds:

```powershell
./scripts/Build-Release.ps1 -SigningThumbprint $thumbprint -TimestampUrl http://timestamp.acs.microsoft.com
```

The build uses SHA-256 and RFC 3161 timestamping and fails on signing/timestamp
errors rather than silently producing an unsigned build. Timestamping does not
make a self-signed publisher publicly trusted. Timestamped signing is unverified
in this environment.

## On another PC

Users may still receive unknown-publisher/SmartScreen warnings. Do not tell public
users to disable protection or install arbitrary certificates. For a controlled
test PC, independently confirm the public certificate thumbprint with the owner
before making any explicit trust decision. Never distribute private keys. The
installer does not import a certificate or change trust settings.

## Verification status

PowerShell parsing and rejection of a malformed certificate thumbprint passed.
The user's signed executable was accepted with Windows result `0x800B0109`
(untrusted root). A deliberately modified copy was rejected with `0x80096010`
(digest mismatch); the temporary copy was removed. No trust stores were changed.
Certificate creation failed in the agent sandbox with Windows access denied
(`0x80070005`); the user subsequently created the certificate and reported the
self-signed build working in their normal terminal.

On 2026-11-10, the user confirmed the self-signed installer checks on another
Windows PC: exit the existing app, install over the existing installation,
launch MindDump, retain the vault selection, capture a note, and uninstall with
settings and notes preserved. This establishes same-version reinstall behavior,
not a distinct-version upgrade. This latest test did not separately establish
running-app protection while a draft is open or timestamped signing; those must
not be inferred from the successful install/uninstall report.

Sources: [Microsoft self-signed certificates](https://learn.microsoft.com/en-us/powershell/module/pki/new-selfsignedcertificate),
[SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool),
[Inno signing](https://jrsoftware.org/ishelp/topic_setup_signtool.htm).
