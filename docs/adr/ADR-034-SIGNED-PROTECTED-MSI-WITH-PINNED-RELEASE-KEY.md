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

- One self-signed code-signing certificate, subject `CN=PC Steward Release Signing`, RSA 3072, SHA-256, code-signing EKU only, no CA rights, valid for ten years. It contains no personal data.
- It is created on the owner's PC by a reviewed script and kept as a password-protected PFX on removable media, with a second copy stored separately. It is never imported into a Windows certificate store, never placed in GitHub, CI or cloud storage, and loaded only by the signing script for the duration of one signing session.
- A **backup key** (`CN=PC Steward Release Signing (Backup)`) is created at the same time, kept offline separately, and its pin is compiled in from the start, so losing the primary key never strands an installed copy. (Owner question 1.)
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
- Release signing depends on the owner keeping two offline keys and their passwords safe. Losing both means uninstalling and installing a build with new pins by hand.
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

## Owner questions

1. **Backup key.** Create and compile in a second offline key from the start (recommended), or a single key only?
2. **Key storage.** Password-protected PFX on two separate removable drives (recommended), or a hardware security key such as a YubiKey if you already own one?

## Reconsider when

- The owner chooses a publicly trusted certificate, or SignPath Foundation accepts the project. A public certificate can then be added as a further accepted pin.
- `WinVerifyTrust` cannot be made to give reliable, tested results for the pinned check.
- PC Steward is distributed to people other than the owner, where "Unknown publisher" and SmartScreen warnings would undermine trust.
- A release key is lost or suspected compromised.
