# ADR-034 — Security review of the signed installer route (2026-11-11)

Requirements: `FR-UPD-04/15`, `TM-03/04/13`; [ADR-018](../adr/ADR-018-HELPER-IPC-AND-ELEVATION.md), [ADR-022](../adr/ADR-022-OWNER-ONLY-PROTECTED-TEST-DEPLOYMENT.md), [ADR-033](../adr/ADR-033-NATIVE-AOT-ELEVATED-HELPER.md), [ADR-034](../adr/ADR-034-SIGNED-PROTECTED-MSI-WITH-PINNED-RELEASE-KEY.md).

ADR-034's build order asks for a security review of the whole route once it is built, with fixes made before the key session or any install on the owner's PC. This is that review. Base: `phase/4-beta` at `b6214b0` (steps 1 to 4 merged: #42, #44, #46, #47).

## 1. Scope and method

The whole route from key to installed program:

- **Keys:** `scripts/New-PCStewardReleaseKeys.ps1` (primary on the YubiKey, backup to two removable drives).
- **Payload:** `.github/workflows/release-payload.yml` and the candidate builder's `-ReleasePayload` mode.
- **Signing:** `scripts/New-PCStewardRelease.ps1`, `scripts/PCStewardReleaseSigning.ps1`, `scripts/New-PCStewardReleaseManifest.ps1`, `scripts/New-PCStewardMsi.ps1`.
- **Package:** `packaging/msi/` (WiX 5.0.2 source, permissions row, launch conditions, upgrade schedule).
- **In the app:** the pinned check (`AuthenticodeIdentity.TryReadPinnedSigner` and `HaveSamePinnedSigner`, the transition rule in the pre-launch and peer checks), `ReleaseSigningPins`, the release manifest policy and `OwnerOnlyDeploymentCapabilityReader.ReadRelease`.
- **Tests:** the Windows CI job `msi-install-test` and the architecture boundary tests.

Read in full, with the CI evidence of #46 and #47.

Threat model: the product's own. The attacker is any program running as the owner's account without elevation (the "same-user" attacker of ADR-018, ADR-025 and ADR-033). Administrator changes and independently started elevated programs stay outside it. A same-user attacker can write the owner's profile, `HKCU`, the owner's temp folder and any clone in the profile. It can start processes and read or inject into the owner's unelevated processes, including an unelevated PowerShell window.

## 2. Findings

| ID | Severity | Finding | Status |
|---|---|---|---|
| H1 | High | Release signing runs unelevated in user-writable folders, so a same-user program can change what the YubiKey signs or what is installed | **Open, fix designed:** the elevated install-and-verify step (next change) installs only bytes equal to the CI build. The updater must keep that rule (§7 note in ADR-034) |
| M1 | Medium | `ykman` was taken from `PATH`, which a same-user program can change | **Fixed** here |
| M2 | Medium | The signing script trusted the pins in the payload record without reading them from the committed source | **Fixed** here |
| M3 | Medium | The pins printed by the key script are only as trustworthy as the session that ran it | **Narrowed** here (YubiKey attestation, checked away from the PC); backup residual for the owner |
| L1 | Low | The release workflow's actions were pinned by moving tag | **Fixed** here |
| L2 | Low | The backup password was copied into managed strings that are never cleared | **Fixed** here |
| L3 | Low | The backup key exists briefly in the owner's key store, and is loaded into a user key set when used | Residual, covered by H1/M3 |
| I1 | Info | A standard user can start a repair of the per-machine MSI | No change: no custom action runs code |
| I2 | Info | The release-install reader relies on the protected folder, not on its own signature check | No change: by design, with the pinned pre-launch check |
| I3 | Info | Timestamps come from an HTTP RFC 3161 service | No change: the response is signed; a missing timestamp only shortens validity to the certificate's ten years |

### H1 — signing and install run where a same-user program can write (High)

**What.** `New-PCStewardRelease.ps1` runs unelevated, by design, so that the YubiKey's smart-card stack works normally and nothing elevated touches downloaded files. Everything it handles is writable by a same-user program:

- the clone it runs from (its own script files, checked by `git status` only at start);
- the extracted payload between re-validation and signing;
- the MSI between signing and the owner's install;
- the GitHub API answers, through the owner's proxy settings or `CurrentUser\Root`.

The YubiKey stops a copied key, not a changed input. PIN policy ALWAYS and touch ALWAYS mean each signature needs the owner, but the owner touches when the key blinks, without seeing what is being signed.

**Impact.** Two ways to code running as administrator:

1. **At install.** The owner runs `msiexec` on an MSI the program has swapped or rebuilt from changed files and had signed. The prompt says "Unknown publisher" for a genuine release too, so it looks the same.
2. **Later, through the updater.** A changed MSI signed by the real key carries the real pin. If the updater accepted any MSI with a compiled pin (ADR-034 §7 as written), one tampered signing session would give a program a release the updater installs after an ordinary owner approval, for as long as the key is pinned.

The pinned check is not weakened: it still needs the fixed Program Files paths, which only an administrator can write.

**Fix (designed, next change, before any install on the owner's PC).** An elevated install-and-verify step that trusts nothing the signing session wrote:

- Run from the protected execution bundle (`Assert-OwnerOnlyExecutionEnvironment`, as the ADR-022 installer does), never from the clone.
- Take its trust anchor from the owner's paste, not from disk or the network: the run id, the artefact's SHA-256 digest (read from GitHub's public API in the cloud when the paste is prepared), and the source commit.
- Copy the artefact zip and the MSI into an administrator-only folder; check the zip's digest; re-validate the candidate manifest there with `Read-OwnerOnlyCandidateManifest`.
- Check the MSI copy with the pinned rule (one self-signed certificate, the committed primary pin) and its tables as `New-PCStewardMsi.ps1` does.
- Run `msiexec` on that copy only, with fixed arguments and a log in the protected folder.
- After install, compare the installed tree with the CI candidate manifest: each executable's unsigned image (`PayloadIntegrity::GetUnsignedImage`) equals the CI executable, every other file is byte-equal, the release manifest matches, and both executables carry the pinned signature. On any difference, uninstall and fail.

The installed program is then the CI build of a reviewed commit, whatever happened during signing. Signatures stay meaningful as "made with the owner's key", not as "built from the reviewed commit".

**For the updater.** ADR-034 §7 now says the updater must apply the same content rule, not the pin alone. A release must match a CI-anchored manifest before the helper starts `msiexec`. The ADR-032 revision decides how the anchor reaches the app.

### M1 — `ykman` from `PATH` (Medium, fixed)

**What.** The key script ran the first `ykman.exe` on `PATH`. A same-user program can put a folder first on the owner's `PATH` (`HKCU\Environment`), so a fake `ykman` would see the new PIN, PUK and management key and could generate the key outside the YubiKey.

**Fix.** `ykman` comes only from the two YubiKey Manager folders under Program Files (administrator-only) or from `-YkmanPath`. In both cases the file must carry a valid Authenticode signature whose signer is `O=Yubico AB`, checked before `ykman` first runs. Test: `ReleaseKeyScriptBoundaryTests` (no `Get-Command` or `PATH` lookup; the signature check comes before the first call).

### M2 — pins taken from the payload record only (Medium, fixed)

**What.** The signing script compared the pins in `release-payload.json` with those compiled into the payload's `PCSteward.Execution.dll`. Both come from the same CI run, so they agree with each other, but nothing tied them to the pins committed in the clean clone at the run's commit.

**Fix.** The script reads both pins from `src/PCSteward.Execution/ReleaseSigningPins.cs` in the clone, which it has already checked is at the run's commit with no changes, and refuses unless they equal the record's pins. The check comes before the first signature. Test: `ReleaseKeyScriptBoundaryTests`.

### M3 — trusting the printed pins (Medium, narrowed)

**What.** The owner commits the pins the key script prints. A same-user program that changed the script or its output could print the pin of a key it holds. The app would then trust that key in the pre-launch check and, later, in the updater. The backup key's private key also passes through the owner's session while it is created and exported (see L3).

**Narrowed here.** The primary key run now also prints Yubico's attestation of slot 9c and the YubiKey's attestation certificate (slot f9). The script checks that the attestation is for the new key. Before the pin is committed, it is checked away from the PC:

- the attestation chains to Yubico's published PIV attestation root;
- its public key gives the printed pin;
- it records PIN policy ALWAYS and touch policy ALWAYS;
- its serial number is the owner's YubiKey's.

A program on the PC cannot make a Yubico-signed attestation for a key it holds outside a YubiKey. The attestation names the YubiKey's serial number, so it is used for this check only and never committed.

**Residual (owner).** The backup key has no hardware attestation. Its pin and its private key are only as safe as the session that creates it. Recommendation: create both keys in a fresh sign-in with nothing else started, as the host session plan will say. The owner accepts or rejects this residual when the key session is proposed.

### L1 — actions pinned by tag (Low, fixed)

`release-payload.yml` used `actions/checkout@v7`, `actions/setup-dotnet@v6` and `actions/upload-artifact@v7`. Whoever controls a tag can move it, and the payload is built by these actions. They are now pinned to full commit SHAs, with the tag recorded in a comment. A boundary test refuses any `uses:` in that workflow that is not a 40-character commit.

### L2 — backup password in managed strings (Low, fixed)

The two password entries were turned into managed strings to compare them, and those strings cannot be cleared. They are now compared character by character in the unmanaged BSTRs, which are zeroed and freed at once. Neither becomes a managed string. Test: no `PtrToStringBSTR`, two `ZeroFreeBSTR`.

### L3 — the backup key on the PC (Low, residual)

`New-SelfSignedCertificate` creates the backup key in the owner's key store as exportable, until the script exports it and deletes it. At signing time, `-BackupPfx` loads it into a temporary user key set for the run. In both windows a same-user program can use or copy it. This is the session-trust residual of H1 and M3. The backup is used only to move the primary pin to a new key.

### I1 — standard-user repair (Info)

Windows Installer lets a standard user start a repair of a per-machine product from its cached package, and the repair runs with system rights. The package has no `Binary` rows, no service tables and only type 19 custom actions (a message, no code), checked by `New-PCStewardMsi.ps1` after every build. A repair can only put back the signed files and the permissions row. No change.

### I2 — the release-install reader (Info)

`ReadRelease` accepts a release install by reading the release manifest in the protected install folder and comparing files. It does not check signatures itself. That is by design: the folder is administrator-only, enforced by the MSI's `MsiLockPermissionsEx` row and proven by the CI job, and the pinned pre-launch check runs before every elevation. No change.

### I3 — timestamps over HTTP (Info)

RFC 3161 responses are signed by the timestamp authority and checked by Windows, so HTTP gives no tampering route. If the service does not answer, signing goes on without a timestamp. The signature then stays valid until the certificate expires in ten years. No change.

## 3. Checked and sound

- **Pinned check (#42).** `WinVerifyTrust` must return success or `CERT_E_UNTRUSTEDROOT` only. The signer comes from the trust provider's state, the chain is exactly one certificate, and the SPKI SHA-256 must equal a compiled pin. Adding the key to `CurrentUser\Root` turns the result into success but changes nothing, because the pin is still required. With no pins compiled, the pinned branch accepts nothing.
- **Transition rule.** The ADR-018 branch is unchanged and is removed at step 7 with the owner-only reader.
- **MSI (#46).**
  - Per-machine only.
  - `MajorUpgrade` is scheduled after `InstallInitialize`, and a failed upgrade rolls back to the old version (proven in CI).
  - Downgrade is refused.
  - The install folder gets the ADR-022 DACL through one permissions row.
  - Both launch conditions block install next to the ADR-022 test deployment.
- **Build isolation (#47).** The MSI builds from copies of tracked files, with every MSBuild, NuGet and SDK hook name refused in ancestor folders, and `--no-incremental`.
- **Workflow.** It runs only when started by hand, with a read-only token, `persist-credentials: false`, and no secrets. CI never signs a release.

## 4. Tests

Architecture (`ReleaseKeyScriptBoundaryTests`):

- `ykman` is never taken from `PATH`, and the Yubico signature check comes before its first use.
- The attestation is fetched and checked against the new key.
- The password comparison never makes a managed string.
- The committed pins are read and compared before the first signature.
- Every workflow action is pinned to a full commit.

Run in the cloud with the architecture test runner: these tests pass, and the run's only failures are the five known WinUI-dependent tests.
