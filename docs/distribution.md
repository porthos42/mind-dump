# Public distribution

## Local verification (2026-11-10)

- Phase 2 commit `3d7b318` confirmed on live GitHub main through connected read access.
  Git transport cannot reach GitHub here; local GitHub CLI tokens remain invalid.
- Release solution build passed with zero warnings/errors; 15/15 tests passed
  through the freshly built x64 executable, producing a JUnit report.
- Full `Build-Release.ps1 -ExpectedTag v0.1.0 -NoRestore` rehearsal passed:
  self-contained publish, executable/resource validation, Inno Setup 7.1.1,
  portable ZIP, entry-by-entry payload hashes, release notes, and asset checksums.
- An incorrect tag was rejected before building. PowerShell parsing and
  `git diff --check` passed. App version remains 0.1.0; no existing tag was changed.
- Online restore failed in this sandbox. Coverage-tool restore/collection,
  hosted workflow execution, and release-draft creation have not been verified.
  Subsequent user testing confirmed the local self-signed build and installation,
  launch, capture, retained vault selection and data-preserving uninstall on
  another Windows PC; see [self-signing](self-signing.md). Timestamped signing
  and a true newer-version upgrade remain pending. No commit, push,
  release creation/publication, account purchase or security-setting change occurred.

## Recommendation

Keep the tested Inno Setup installer and portable ZIP. The owner selected
[self-signed local builds](self-signing.md) as the interim signing option.
GitHub Actions remains unsigned because it has no local private key. Consider
Azure Artifact Signing when public adoption justifies its recurring cost.
No signing service account or credentials are configured.
Keep updates manual for this phase. Do not migrate to MSIX solely to obtain signing.

## Version and release procedure

`src/MindDump/MindDump.csproj` is the version source. Use a stable three-part
version such as `0.1.1`; tags must be exactly `v0.1.1`. The installer, filenames,
executable product version, and release title derive from it. No version bump is
included in this phase. Do not move or recreate the existing `v0.1.0` tag.

1. Change Version and add `releases/<version>.md` with changes, requirements,
   unsigned/signed status, update instructions, and known limitations.
2. Run `pwsh -NoProfile -File scripts/Build-Release.ps1`. Add `-Coverage` to
   restore the pinned coverage tool and collect Cobertura results as CI does.
   `-NoRestore` is available for offline rehearsals with previously restored
   packages; it does not establish that a clean online restore works.
   The script restores, builds Release, runs tests directly, builds the installer,
   verifies required resources/product version, and checks every portable ZIP
   entry by SHA-256 against the same publish tree used by the installer.
3. Test the new installer over the installed previous version on another PC,
   including unsaved-input protection, retained settings/notes, and uninstall.
   Record results in installer/README.md. This remains necessary for 0.1.1.
4. With explicit authorization, commit/push and wait for branch CI to pass.
   Then create and push the matching version tag with explicit authorization.
   A tag push creates a **draft**, never an automatically published release.
5. Review the draft's source commit, release notes, ZIP, setup, checksums, and CI
   test/coverage artifacts. Publish only after explicit authorization. Existing
   releases are never overwritten by the workflow; a duplicate release fails.

Main/PR/manual runs only upload Actions artifacts; tag runs additionally create
the draft. CI uses Windows 2025, the SDK selected by global.json, Inno Setup 7.1.1,
and a pinned coverage tool. Hosted runner and dependency feeds can change; this
is a repeatable procedure, not a claim of byte-for-byte reproducible binaries.
CI downloads Inno Setup 7.1.1 x64 from its official GitHub release and checks its
pinned SHA-256 digest before installation. The first hosted run found that
Chocolatey did not carry the requested version; the direct-download correction
still requires a successful hosted run. Action major versions are maintained references.

Each build stages into a unique ignored `artifacts/release-<version>-<id>/`
directory so a rerun cannot accidentally upload stale assets. SHA256SUMS.txt
covers the ZIP and installer. The installer build also records payload hashes and
compiler inclusion logs in `artifacts/installer-<version>/`. Checksums detect
corruption, not independent publisher identity or malicious asset replacement.

## Code signing options (official sources reviewed 2026-11-10)

| Option | Eligibility and cost | Fit for MindDump |
| --- | --- | --- |
| Azure Artifact Signing (formerly Trusted Signing) | Individuals in US/Canada; organizations in US/Canada/EU/UK; identity validation required. Basic $9.99/account/month includes 5,000 signatures; excess $0.005/signature. | Preferred paid option; managed keys and GitHub Actions integration. Hawaii location fits the geographic criterion; approval is not guaranteed. |
| SignPath Foundation | Free for approved, maintained and released open-source projects using OSI-approved licenses; review, signing policy, MFA and manual signing approval required. | Investigate only after owner chooses a license and confirms bundled-component eligibility. No license file currently exists; do not claim acceptance. |
| Traditional CA OV certificate | Microsoft lists an indicative $150–300/year; actual vendor, identity, hardware token/cloud custody and renewal fees vary. | Obtain an official vendor quote if Azure is unavailable. Never assume an exportable PFX or CI-compatible USB token. |
| Microsoft Store MSIX | Microsoft lists free Store distribution/signing; onboarding and certification still required. | Worth revisiting if Store discovery and managed updates become priorities. |
| Self-signed certificate | Free; users must explicitly trust it. | Development/testing only; unsuitable as general public trust. |

Sources: [Microsoft comparison](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options),
[Azure pricing tiers](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-change-sku),
[identity setup](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart),
[SignPath conditions](https://signpath.org/terms).
Signing establishes publisher identity; it does not guarantee immediate removal
of SmartScreen warnings. Do not promise warning-free direct downloads.

### Future CI signing sequence

Use Azure federated OIDC credentials scoped to this repository's release context,
short-lived tokens, and the certificate-profile signer role at the narrowest
scope. Keep signing out of PR jobs. Use an owner-approved release environment
and reviewed workflow/action revisions; creating protection rules requires
separate authorization. Do not store private keys, PFX passwords or long-lived
client secrets in source, artifacts, logs, or PR-accessible jobs. SignPath instead
requires its own project configuration, protected API credential and approval.

After choosing a provider, refactor the build into explicit stages: publish;
sign only our app EXE/DLLs; verify signatures; compile the installer with signed
uninstall support; sign/verify setup; package the signed portable payload;
then hash final bytes. Preserve upstream signatures and never sign third-party
DLLs as our own. Signing only setup leaves portable downloads unsigned. Configure
Inno's SignTool and SignedUninstaller together rather than merely signing setup
after compilation. Fail closed if any required signature/timestamp is missing.

Use SHA-256 file digests and RFC 3161 SHA-256 timestamping; Azure recommends
`http://timestamp.acs.microsoft.com`. Timestamping is essential for its short-lived
certificates. Verify with `signtool verify /pa /all /v` and check the expected
publisher and timestamp. Hash only after signing; signatures change file bytes.
Do not enable a pretend signing switch before credentials and end-to-end tests exist.

Sources: [official GitHub action](https://github.com/Azure/artifact-signing-action),
[OIDC setup](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect),
[timestamp guidance](https://learn.microsoft.com/en-us/azure/artifact-signing/concept-certificate-management),
[Inno signed uninstaller](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm).

## Updates

Use the releases page now. Save/copy input, exit from the tray, then run setup or
replace the extracted portable tree. A future **Check for updates** command is a
reasonable small feature: user-initiated HTTPS request to the fixed GitHub latest
stable release endpoint, numeric version comparison, clear timeout/rate-limit/
offline feedback, and an explicit button opening the fixed repository release
page. No automatic download, execution, restart or draft loss. Never follow an
API-provided executable URL into an unattended install. Requesting GitHub discloses
the user's IP; explain it before introducing background checks. Exclude drafts
and prereleases; retain a browser-link fallback. Automatic updates add signing,
rollback, in-use replacement and data-loss obligations without clear value yet.

## MSIX decision

| Concern | Existing Inno/ZIP | MSIX |
| --- | --- | --- |
| Signing | Sign app, setup and uninstaller with a chosen provider | Direct distribution also requires trusted signing; Store re-signs accepted packages |
| Updates | Manual; existing process guard preserves drafts | App Installer supports scheduled checks and update policies; in-use behavior needs deliberate configuration |
| Distribution | Current self-contained tray/vault workflow already tested | Store discovery or hosted .appinstaller can be useful |
| Maintenance | One tested installer and stable AppId | New identity/manifest, packaging, lifecycle, data-path and migration tests |

Keep Inno now. Reconsider MSIX if Store distribution or managed package updates
become explicit goals. A prototype must verify tray/hotkey behavior, arbitrary
vault access, settings migration, unsaved drafts, in-use updates, and uninstall
before replacing the current delivery path. No migration is implemented.
Sources: [distribution comparison](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path),
[App Installer overview](https://learn.microsoft.com/en-us/windows/msix/app-installer/app-installer-file-overview),
[in-use guidance](https://learn.microsoft.com/en-us/windows/msix/msix-troubleshooting-guide).
