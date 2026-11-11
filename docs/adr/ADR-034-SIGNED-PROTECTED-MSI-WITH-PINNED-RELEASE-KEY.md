# ADR-034 — Signed protected MSI install with a pinned release key

**Status:** Proposed. The owner chose the direction on 2026-11-11: "installer first, then updater"; "Do a self signed cert for now, I don't plan to pay for a certificate"; and "App pins the key only". The other plan recommendations (MSI, CI builds and the owner's PC signs, first builds on the owner's PC only) were accepted on the same day. Nothing is built or installed by this ADR.
**Date:** 2026-11-11
**Decision owners:** Product owner and security reviewer
**Related requirements:** `FR-UPD-04`, `FR-UPD-15`, `FR-DAT-12`, `AC-UPD-03–05`, `TM-04`, `TM-13`
**Would supersede, once accepted and its evidence passes:** ADR-002's choice of the standalone ZIP (Option A) in favour of its Option B, and the ADR-022 owner-only test route as the way PC Steward reaches `%ProgramFiles%\PC Steward`.
**Amends, once accepted:** ADR-018's "untrusted signature" and "signer mismatch" rules (Decision 3 below). Everything else in ADR-018 stays.
**Related:** ADR-009, ADR-013, ADR-019, ADR-020, ADR-022, ADR-025, ADR-033; draft ADR-032 (PR #20); parked updater check core (draft PR #39).

## Context

PC Steward's purpose is to apply owner-approved updates, including machine-wide ones. Only a protected `%ProgramFiles%\PC Steward` install can do that safely, because ADR-018 lets the admin helper run only from a fixed folder the signed-in user cannot change (P3R-15, Task 90).

Today the only such install is the ADR-022 owner-only test deployment. It signs both executables with a self-made certificate that lasts fourteen days and is added to the owner's Root and TrustedPublisher stores. Its signatures carry no timestamp, so when the certificate expires the pre-UAC check refuses the helper as untrusted. The fourteen-day life is deliberate: a certificate in the Root store makes anything signed with its key appear as a verified publisher everywhere on the PC, and any program running as the owner can use a non-exportable key in `CurrentUser\My` without exporting it.

The owner wants an installed copy that lasts, as the base for the later self-updater, and does not want to pay for a certificate. Publicly trusted certificates were reviewed (plan document, 2026-11-11): Azure Artifact Signing is not offered to individuals in the UK, SignPath Foundation declines projects without an established user base, and the cheapest individual certificate costs money and shows the owner's legal name.

Facts this design rests on:

- `InstalledHelperLaunchTrust.Validate` checks the fixed paths, reparse points, caller write authority, `WinVerifyTrust` success for both executables and raw signer-certificate equality. The helper's and desktop's peer checks call the same `AuthenticodeIdentity.IsTrustedAndSameSigner`.
- Machine-scope updates are offered only when `OwnerOnlyDeploymentCapabilityReader` accepts the ADR-022 ledger, which records the test certificate's store entries. A release install has no such ledger.
- The helper is a Native AOT image (ADR-033); the desktop is framework-dependent.

## Decision drivers

- Nothing a same-user program can write decides what runs elevated (ADR-018, ADR-033).
- Trust exactly one key, for exactly PC Steward, rather than adding a self-made root to Windows.
- No cost, no personal data in the certificate or the source.
- A lasting install, upgraded and removed through standard Windows mechanisms.
- The later updater must replace the installed copy with one UAC prompt.
- Keep the reviewed helper, protocol and pre-UAC checks; change only what trust anchor they use.

## Options considered

### Certificate trust

- **A. Pinned release key, nothing added to Windows trust (chosen).** A self-signed code-signing certificate whose public key is compiled into PC Steward. Windows does not trust it, so nothing else on the PC treats it as a verified publisher. The key can last for years. UAC shows "Unknown publisher".
- **B. Self-signed certificate in Root and TrustedPublisher for a year.** Smallest code change, but for a year anything signed with that key appears verified on the PC, and same-user code can use the key.
- **C. Keep the fourteen-day certificate.** No trust change; the owner re-signs and reinstalls every two weeks.
- **D. A publicly trusted certificate.** Declined by the owner on cost; see Context.

### Installer technology

- **MSI built with the WiX Toolset (chosen).** Standard install, transactional rollback, major upgrade with downgrade blocking, repair, uninstall and an Add/Remove Programs entry, all declarative.
- **Own setup program grown from the ADR-022 script.** Re-implements upgrade, rollback and uninstall in privileged code.
- **MSIX.** Installs under `WindowsApps`, so the ADR-018 fixed-path model would need redesigning, and App Installer can update in the background.

## Decision

### 1. One install route: a per-machine MSI

- The MSI installs to `%ProgramFiles%\PC Steward` only. There is no folder choice.
- It installs the ADR-033 payload unchanged: the framework-dependent desktop and its assemblies, the Native AOT `PCSteward.ElevatedHelper.exe`, `PCSteward.DeploymentCapability.txt`, and the new release manifest (section 4).
- The install folder gets explicit, non-inherited permissions matching ADR-022: SYSTEM and Administrators full control, Users read and execute, nothing else. WiX sets them declaratively.
- The MSI has **no custom actions**: no installer code runs, no service or elevated scheduled task is created, and nothing is launched at the end. The per-user scheduled check stays as ADR-020 defines it.
- Upgrades use one fixed `UpgradeCode` and a major upgrade. A lower version is refused.
- The MSI refuses to install while the ADR-022 test root or `%ProgramFiles%\PC Steward OwnerOnlyTest State` exists. The move is: run the existing `Remove-OwnerOnlyProtectedTestDeployment.ps1 -Remove` (which also removes the test certificate from the Root and TrustedPublisher stores), then install the MSI. Per-user data is kept.
- Uninstall removes program files only. Per-user data stays; the in-app factory reset remains the only path that deletes data (ADR-013).
- The standalone ZIP is retired once the MSI has passed on the owner's PC (section 7). Until then it stays as ADR-002 describes it.

### 2. The release key

- **Primary key on the owner's YubiKey (owner, 2026-11-11: "I owned a Yubikey").** The key pair is generated inside the YubiKey's PIV Digital Signature slot (9c), so the private key never exists outside the device and cannot be exported. The slot's PIN policy requires the PIN for every signature and its touch policy is "always", so signing needs the owner present. The self-signed certificate (subject `CN=PC Steward Release Signing`, code-signing EKU only, no CA rights, valid for ten years, no personal data) is stored in the same slot. RSA 3072 where the YubiKey firmware supports it (5.7 or later), otherwise RSA 2048 or ECDSA P-256; SHA-256 throughout. Signing uses the Windows smart-card stack (the YubiKey minidriver), so the public certificate appears in the owner's personal store only while the key is plugged in; it is never added to Root or TrustedPublisher.
- **Backup key offline (owner, 2026-11-11: "Agreed").** A second key (`CN=PC Steward Release Signing (Backup)`) is generated off the YubiKey by a reviewed script, written once to a password-protected PFX on two separate removable drives kept apart from the YubiKey, and deleted from the PC. Its pin is compiled in from the start, so losing or breaking the YubiKey never strands an installed copy. It is used only to sign a release that moves the primary pin to a new key.
- Neither key is placed in GitHub, CI or cloud storage.
- Setting the YubiKey's PIN, PUK and management key, generating the slot 9c key and creating the backup PFX are part of the host session in section "Build order" step 6, approved separately at the time.
- Signatures are timestamped by a public RFC 3161 timestamp service when one is reachable, so a signature's time is recorded. The pin check (section 3) does not depend on Windows trusting the certificate or the timestamp.

### 3. Pinned signature check (amends ADR-018)

`AuthenticodeIdentity` gains a pinned mode that replaces "trusted by Windows and same raw certificate" for PC Steward's own executables:

1. `WinVerifyTrust` must report either success or exactly `CERT_E_UNTRUSTEDROOT`. Any other result, including a bad digest, no signature, a malformed signature or a revoked certificate, is a rejection.
2. The signer is read from the same `WinVerifyTrust` state data that was verified, not from a second parse of the file.
3. The signer certificate's SHA-256 SubjectPublicKeyInfo hash must equal one of the compiled pins (primary, backup). The chain must be that single self-signed certificate.
4. Both executables must carry the same pin. This replaces raw-certificate equality.

Fixture tests must prove, on Windows CI, that a tampered file, a file signed by another self-signed key, an unsigned file, a file signed by a publicly trusted but unpinned certificate and a correctly pinned file give the expected results, before this replaces the current check. If `WinVerifyTrust` result codes prove unreliable for any case, the implementation falls back to computing the Authenticode digest and checking the PKCS #7 signature in code, and this ADR is revised.

The pre-launch order, the repeated check immediately before launch, the per-request pipe, both peer verifications and the "no caller-writable path" rule are unchanged. `ChocolateyEngineTrust` and any check of third-party publishers keep using Windows trust.

### 4. Release capability reader

A new read-only `ReleaseDeploymentCapabilityReader` serves release installs next to the existing owner-only reader:

- It reads `PCSteward.ReleaseManifest.json` from the protected install root: format version, product version, and the path and SHA-256 of every installed file, including the capability statement. Bounds and strict parsing follow `DeploymentCapabilityPolicy` (closed key sets, size limits, no duplicates).
- It checks the ADR-033 binding set against the manifest, the install-root protection, and the pinned signatures of both executables.
- It reads no registry, environment, database or remote input, caches nothing and fails closed, like the owner-only reader. ADR-018 launch trust stays the security boundary.
- The owner-only reader is removed when the ADR-022 route is retired.

### 5. Build and sign

- GitHub Actions builds the unsigned payload (desktop and Native AOT helper) on a pinned toolchain and publishes it with its SHA-256 as a workflow artefact. This changes ADR-033 choice 5 from (a) to (b) for releases; the owner's PC no longer needs the build toolchain for a release.
- One reviewed script on the owner's PC downloads the artefact, checks its hash against the workflow run, signs the two executables with the release key, writes the release manifest, builds the MSI with a pinned WiX version, signs the MSI, and verifies every signature with the section 3 check.
- A Windows CI job builds the same MSI signed with a throwaway key whose pin exists only in a test build, and proves: install, the folder permissions, the ADR-018 checks passing, upgrade, downgrade refusal, refusal next to the ADR-022 test root, and uninstall keeping per-user data.

### 6. Distribution

The first signed MSIs are installed only on the owner's PC from a local file. Publishing releases on GitHub is a separate later decision, taken with the updater. A file built and signed locally carries no internet mark, so SmartScreen does not apply.

### 7. How the later updater uses this

The updater (ADR-032, to be revised) hands a verified, signed MSI to Windows Installer through the existing elevated helper:

- It checks the release (ADR-032 manifest), downloads the MSI, and checks its hash and that the MSI's signature carries a compiled pin.
- After the owner approves, the desktop starts the helper as today (one UAC prompt) with one new typed operation. The helper copies the MSI into an administrator-only folder, checks the hash and pin again on that copy, starts `msiexec` on that copy only with fixed arguments, and exits. The desktop exits too, so its files can be replaced. A failed install rolls back automatically.
- The new version verifies its own install (section 4) before reporting success.
- The helper route is required, not optional. Because UAC shows "Unknown publisher" for a pinned-key MSI, starting `msiexec` directly on a file in a user-writable folder would let a same-user program swap in any MSI behind a prompt the owner expects; the helper's copy-then-check closes that.
- Because the release key is owner-held and offline, it can also serve as ADR-032's release key; the ADR-032 revision decides that. ADR-032's per-user `versions\` layout is dropped in favour of this route.
- **Content, not only the pin (security review, 2026-11-11, H1).** Release signing runs unelevated on the owner's PC, so a same-user program present at that time could have the real key sign changed files. The updater therefore installs a release only when its files match a manifest anchored in the CI build of a reviewed commit (executables compared without their signatures), checked by the helper on its administrator-only copy before `msiexec` and again after install. The first install uses the same rule through an elevated install-and-verify script ([review](../review/ADR_034_SIGNED_INSTALLER_SECURITY_REVIEW.md)). The ADR-032 revision decides how the anchor reaches the app.

## Consequences

### Positive

- A lasting protected install with machine-wide updates, at no cost.
- Nothing is added to Windows trust stores. A misused key cannot make other software look verified, and the existing ADR-022 Root and TrustedPublisher entries are removed.
- No personal data in the certificate, the binaries or the source.
- Standard install, upgrade, rollback and uninstall, with no installer code to review.
- A clear, one-prompt path for the updater.

### Negative / trade-offs

- UAC shows "Unknown publisher" for the helper and for MSI upgrades. The owner must recognise PC Steward's prompt by its timing and program name.
- ADR-018's signature rule changes, so the pre-UAC check needs a fresh security review.
- Release signing depends on the owner keeping the YubiKey, its PIN and the backup PFX and its password safe. Losing both keys means uninstalling and installing a build with new pins by hand.
- Other people installing a release see SmartScreen and "Unknown publisher" warnings. This is acceptable while releases are for the owner only.
- New build tooling (WiX) and a signing script on the owner's PC. WiX 6 and later ask users to accept FireGiant's Open Source Maintenance Fee terms; whether a free, no-revenue project owes anything is confirmed before the packaging PR.

### Follow-up controls and tests

- Fixture tests for the pinned check (section 3) and for the release reader (section 4), including oversized, duplicate-key, missing-file and wrong-hash manifests.
- Architecture tests: the desktop and helper use only the pinned check for PC Steward binaries; the pins are compiled constants; the MSI source declares no custom actions.
- The Windows CI MSI job (section 5).
- An independent security review of the pinned check, the reader, the MSI and the signing script, with fixes made before any host step.

## Build order

1. This ADR reviewed and accepted.
2. Code PR: pinned check, release reader, tests, and the CI payload job.
3. Packaging PR: WiX project and the CI MSI job.
4. Signing and key-creation scripts.
5. Security review and fixes.
6. Host session (an administrator step, approved separately at the time): create the keys, remove the ADR-022 test deployment, install the signed MSI, run the read-only post-check, then one real machine-wide update.
7. Retire the ZIP and the owner-only reader; revise ADR-032 and resume the updater.

The ADR-022 test install stops offering machine-wide updates on 2026-11-24 unless it is redeployed with a fresh fourteen-day certificate; that is the owner's call and is not part of this ADR.

## Owner answers (2026-11-11)

1. **Backup key:** "Agreed": an offline backup key with its pin compiled in from the start.
2. **Key storage:** "I owned a Yubikey": the primary key lives on the owner's YubiKey (slot 9c); the backup is a password-protected PFX on two removable drives.
3. **WiX version (2026-11-11):** "WiX 5.0.2 (no EULA)": the MSI is built with WiX 5.0.2, which asks for no maintenance-fee terms.

## Implementation notes

### Build order step 2a — pinned signature check (2026-11-11)

- `ReleaseSigningPins` holds the two compiled pins (primary, backup): the SHA-256 of each certificate's DER SubjectPublicKeyInfo, as 64 uppercase hex characters. Both are empty until the owner's key session creates the keys; an empty pin matches nothing, so no file passes the pinned check yet.
- `AuthenticodeIdentity.TryReadPinnedSigner` implements section 3 steps 1–3: `WinVerifyTrust` (no UI, no revocation, cache-only URL retrieval, as before) must return success or exactly `CERT_E_UNTRUSTEDROOT`; the signer is read from the same state data before it is closed; the chain must have exactly one certificate; its key hash must equal a compiled pin (constant-time compare). `HaveSamePinnedSigner` is step 4.
- **Transition (deliberate):** the pre-launch check and both peer checks accept a pair that carries the same compiled pin, **or** a pair that passes the existing ADR-018 rule (trusted by Windows, same raw certificate). The second branch keeps the owner's ADR-022 test install working until the MSI replaces it; step 7 removes it together with the owner-only reader. Accepting the union adds nothing an attacker can use: with no pins compiled the new branch accepts nothing, and the old branch is unchanged.
- Tests: `PinnedReleaseSignatureTests` (Windows CI signs real PE copies with two throwaway self-signed keys that are never trusted, and proves the five section 3 cases plus same-pin pairing) and architecture `ReleaseSigningPinBoundaryTests` (pins are constants, the status and chain rules, and every production caller uses the compiled pins).

### Build order step 2b — release capability reader (2026-11-11)

- Manifest format (`PCSteward.ReleaseManifest.json`, install root): one JSON object with exactly `format` (`pc-steward-release-manifest:1`), `productVersion` (`major.minor.patch` with an optional pre-release label) and `files`: 1–4,096 objects of exactly `path` (backslash-separated, relative to the install root, printable ASCII, no `.`/`..`, drive, colon, slash or wildcard), `bytes` and `sha256`. At most 1 MiB, strict UTF-8 without BOM, no duplicate keys or paths (case-insensitive), and the manifest never lists itself. The signing script writes it after signing, so its hashes are of the signed files.
- `ReleaseManifestPolicy` is the pure check: every ADR-033 binding-set leaf listed exactly once at the root by its exact name, the capability statement equal to its entry and allowing the route, every binding leaf's length and hash equal to its entry, and both executables carrying the same compiled pin.
- The reader is the existing `OwnerOnlyDeploymentCapabilityReader` (still the only `IHelperProtocolCapability`, so every composition site, the helper-side check and the held-binding lifetime are unchanged). The release manifest alone selects the release path; a release manifest beside the ADR-022 state root is a mismatch. The release path reuses the same protection checks, bounded reads and share-read holds, and records no ledger hash. The class is renamed when step 7 retires the owner-only path.
- The pinned check of the two held executables is `AuthenticodeIdentity.HaveSamePinnedSigner` over `ReleaseSigningPins.Compiled`; a fourth constructor argument lets fixture tests substitute it, and no production code uses that constructor.
- Tests: `ReleaseManifestPolicyTests` (pure: strict parsing, missing and wrong-hash files, stages), `ReleaseDeploymentCapabilityReaderTests` (Windows: real handles, both roles, mismatch beside the state root, production pin check refuses unsigned files), architecture `ReleaseCapabilityReaderBoundaryTests`, and the constructor-chain pin in `HelperEndpointBoundaryTests` updated deliberately.

### Build order step 3 — WiX MSI and the CI install test (2026-11-11)

- `packaging/msi/Package.wxs` with `PCSteward.Installer.wixproj` (`WixToolset.Sdk` 5.0.2, no extension packages, not in `PCSteward.slnx`): a per-machine package, `Name` and `Manufacturer` "PC Steward", fixed UpgradeCode `2AD17330-BAD5-46BE-A409-20814B26673C`, a major upgrade scheduled after `InstallInitialize` (the old version is removed inside the install transaction, so a failed upgrade rolls back to it), and a refused downgrade. It installs the whole payload folder (signed executables and the release manifest already written) to `%ProgramFiles%\PC Steward` and adds one all-users Start menu shortcut on the desktop executable's component. Windows Installer validation runs with three checks suppressed: ICE43 and ICE57 (they treat the Start menu as per-user, which it is not with `ALLUSERS=1`) and ICE60 (a language value for harvested versioned files). Nothing else: no custom action that runs code, service, scheduled task, environment change, UI sequence or launch at the end.
- **Permissions.** One `MsiLockPermissionsEx` row on the install folder sets the protected ADR-022 DACL `D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)`; every file and subfolder inherits it.
- **Refusals.** Two launch conditions: the MSI does not install while the ADR-022 owner-only state folder exists, and does not install over a `PC Steward` folder that no earlier version of this MSI owns. Remove the owner-only test deployment first.
- `scripts/New-PCStewardReleaseManifest.ps1` writes the release manifest after signing (Windows PowerShell 5.1 and pwsh 7 write the same bytes). `scripts/New-PCStewardMsi.ps1` builds the MSI and then reads its tables: no service tables or `Binary` rows, custom actions only of type 19 (the downgrade message, which runs nothing), the fixed UpgradeCode, the asked-for version, `ALLUSERS=1` and exactly the one permissions row. It signs and installs nothing.
- **CI job `msi-install-test`** (`scripts/Test-PCStewardMsiCI.ps1`, GitHub-hosted runner only): builds this commit's payload as a disposable standard user, creates a throwaway self-signed key in the runner's personal store (never trusted), writes its pin into the job's own checkout only and puts the committed file back at the end, signs both executables and three MSIs (0.1.1, 0.1.2, and 0.1.3 with one extra file), then proves: both refusals; install; the exact protected DACL and inherited file ACLs; installed files equal to the manifest; the shortcut; as a standard user, ADR-018 pre-launch trust, the same compiled pin on both executables, Windows not trusting the key, and the capability reader reporting every allow-listed route Supported in both roles (`tests/PCSteward.InstallCheck`, never shipped); a 0.1.3 upgrade made to fail (a folder blocks its extra file) rolling back to an intact 0.1.1; a major upgrade; a refused downgrade; and uninstall removing the program files while per-user data stays. Evidence and msiexec logs are uploaded as `msi-install-evidence`.
- **WiX version (owner, 2026-11-11: "WiX 5.0.2 (no EULA)").** WiX 6 and later ask their users to accept the Open Source Maintenance Fee EULA. The owner chose WiX 5.0.2, which has no EULA, so the build accepts no terms. Moving to a later WiX is a separate owner decision.
- The helper operation that copies an MSI into an administrator-only folder and checks it again (section 7) is built with the updater, where it is first used; adding an unused elevated route now would only widen the helper.
- Tests: architecture `MsiPackageBoundaryTests` (package source, launch conditions, pinned SDK, the build script's table checks, the CI job and its pin handling) and `ReleaseManifestScriptTests` (Windows CI: the script's bytes under both PowerShell editions, accepted by `ReleaseManifestPolicy`, and its refusals).

### Build order step 4 — key creation, release payload and signing scripts (2026-11-11)

- **`scripts/New-PCStewardReleaseKeys.ps1`** (owner's PC, Windows PowerShell 5.1, not elevated; makes no network call):
  - `-Key Primary` uses the YubiKey Manager CLI (`ykman`). It first replaces a factory PIN, PUK or management key (the new management key is random and kept on the YubiKey behind the PIN). It refuses if slot 9c already holds a key or certificate. It then generates the key in slot 9c with PIN policy ALWAYS and touch policy ALWAYS: RSA 3072 on firmware 5.7 or later, otherwise RSA 2048.
  - It then has the slot key sign its own certificate: `CN=PC Steward Release Signing`, code signing only, not a CA, ten years. A one-day placeholder certificate from `ykman` lets Windows reach the key for that one signature. The certificate is stored in the slot, and the script proves the key signs before printing its pin.
  - `-Key Backup` creates an RSA 3072 key (`CN=PC Steward Release Signing (Backup)`) and exports it to a password-protected PFX (AES-256, a password of at least 12 characters) on each of two removable drives. It never overwrites a backup. It reads each copy back and checks that it signs, then deletes the key from the PC.
  - Each run prints only the pin, the subject and the expiry date, plus the line to put in `ReleaseSigningPins.cs`.
- **`.github/workflows/release-payload.yml`** runs only when started by hand, with a read-only token. It runs the candidate builder with `-ReleasePayload`, which:
  - refuses a commit without both compiled pins;
  - builds clean (no `-AllowDirty`) as a disposable standard user and re-validates the candidate;
  - uploads the payload, the candidate manifest and `release-payload.json` (commit, run, product version from `Directory.Build.props`, both pins) as `pc-steward-release-payload`.
  
  CI never sees a release key.
- **`scripts/New-PCStewardRelease.ps1`** (owner's PC, Windows PowerShell 5.1, not elevated):
  - **Before signing, it checks:**
    - through GitHub's public API, with no sign-in, that the run is this repository's hand-started `release-payload.yml` run on `phase/4-beta` or `main`, and that it finished green;
    - that the downloaded zip's SHA-256 equals the artefact digest GitHub publishes;
    - that the local clone is at the run's commit with no changes to tracked files, and no untracked or ignored files under `scripts` or `packaging`;
    - that `release-payload.json` names that run and commit;
    - that the candidate re-validates with the builder's own validator;
    - that both pins are compiled into the payload's `PCSteward.Execution.dll`.
  - **Then it signs** with the YubiKey certificate, or with `-BackupPfx` the backup key, through `Invoke-PCStewardReleaseSigning`. That function signs both executables, writes the release manifest, builds the MSI, signs it, and checks every signature.
  - **Timestamps.** Signatures are timestamped by DigiCert's public RFC 3161 service when it answers. Signing does not depend on it.
  - **Output.** It writes the MSI and `release.json` (run, commit, hashes, which key signed) and installs nothing.
- **`scripts/PCStewardReleaseSigning.ps1`** holds the shared functions. The CI MSI job now signs through the same function, so CI exercises it on every PR:
  - `Get-PCStewardKeyPin` walks the certificate's DER to the SubjectPublicKeyInfo. It works under Windows PowerShell 5.1, which has no SubjectPublicKeyInfo export.
  - `Test-PCStewardPinnedSignature` is the §3 check as a script can make it before installing:
    - Windows has verified the digest, and the only chain problem is the untrusted root;
    - the chain is one self-signed certificate valid for code signing;
    - the key carries a given pin.
  - `Invoke-PCStewardReleaseSigning` is the signing step described above.
- **`scripts/New-PCStewardMsi.ps1`** now builds from copies of the four tracked installer project files and `global.json` in a new temporary folder, and refuses to build if any MSBuild, NuGet or .NET SDK hook file (`Directory.Build.*`, `Directory.Packages.props`, `Directory.Solution.*`, `NuGet.Config`, `global.json`) sits in a folder above it. No other file in the clone, tracked or not, can change the package before it is signed.
- Tests: `ReleaseSigningScriptTests` (Windows CI, both PowerShell editions) and architecture `ReleaseKeyScriptBoundaryTests`.
  - The first shows the script's pin equals .NET's for RSA 2048, RSA 3072 and ECDSA P-256. It also shows the check accepts a pinned signature and refuses tampered, other-key, unsigned and non-code-signing files.
  - The second shows that no release script names a trust store; that the YubiKey policies, the slot refusal, the backup's drives and the single key deletion hold; that the workflow runs on demand only; and that the signing script makes every check before it signs.

## Reconsider when

- The owner chooses a publicly trusted certificate, or SignPath Foundation accepts the project. A public certificate can then be added as a further accepted pin.
- `WinVerifyTrust` cannot be made to give reliable, tested results for the pinned check.
- PC Steward is distributed to people other than the owner, where "Unknown publisher" and SmartScreen warnings would undermine trust.
- A release key is lost or suspected compromised.
