# ADR-032 — In-app self-update from GitHub Releases

**Status:** Proposed (design only, for a release after 2.0; nothing here is implemented or authorised)
**Date:** 2026-11-06
**Decision owners:** Product owner and security reviewer
**Related requirements:** spec §14.3–14.4, `FR-ONB-05` (Later), `FR-DAT-12–15`, `TM-09`, `TM-13`, `TM-18`
**Related open decision:** `OD-09`
**Would supersede, if accepted:** the "no built-in self-update" part of ADR-009 and the "replace the package manually" bullet of ADR-002. Both stay in force until this ADR is accepted and its gates pass.
**Numbering note:** ADR-030 and ADR-031 are taken by open pull requests, so this note uses 032. Renumber at merge if needed.

## Context

The owner wants PC Steward to be able to update itself to the newest release published on its GitHub repository (`DanDG923/pc-steward`), from inside the app.

ADR-009 deliberately chose a manual signed upgrade for the MVP because an automatic updater "creates a second executable supply chain before signing/distribution is settled". That reason still holds, so this ADR does not reverse ADR-009 now. It sets out what a self-updater must look like to fit the product's trust rules, and the conditions that must be true before it is built.

Facts this design depends on:

- **Packaging (ADR-002).** The beta is a standard-user-only `win-x64` ZIP that runs from wherever it was extracted. It ships no elevated helper and its own binaries are not protected from same-user modification. Per-user data lives at `%LocalAppData%\PCSteward` and survives package replacement.
- **Signing (Task 30, ADR-022).** No production code-signing certificate exists. The only signer used so far is the owner-only, fourteen-day, non-exportable test certificate from ADR-022. Windows Authenticode alone therefore cannot anchor trust in a public release today.
- **Release evidence (Task 30).** The package script already emits a portable SHA-256 manifest of every payload file, a CycloneDX SBOM and a licence inventory, and the evidence validator recomputes every hash. The release, not the app, is where that evidence is produced.
- **Jobs and verification (ADR-017, AGENTS.md).** Owner-approved work is an immutable job with append-only events. A provider exit code is evidence, not Success. Success needs post-operation verification.
- **No resident process (ADR-020).** The scheduled task runs a scan-only host that cannot install anything. Its action is a fixed path to `PCSteward.ScheduledHost`.
- **Owned roots (ADR-019) and factory reset (ADR-013).** Deletion is limited to compiled, app-owned roots. Factory reset deletes per-user state but leaves installed binaries in place.
- **Migrations (ADR-015).** Schema migrations are forward-only in release builds. Recovery uses a pre-migration backup.
- **Repository.** The repository is public. Anyone with write access to it, or a stolen GitHub session or token, can create a release and upload assets.

## Decision drivers

- The app must never run code it cannot prove came from the owner's release key, whatever GitHub, the network or the local database say.
- No account, no token, no cloud service other than reading public release data, and nothing that runs in the background with admin rights.
- The owner approves each update and can always decline, defer or roll back.
- "Updated" means verified: the new version is running, its files match the signed manifest, and the user's data opened cleanly.
- A failed or interrupted update leaves a working PC Steward, never a half-replaced one.
- Smallest new attack surface, no new third-party dependencies, and testable rules.

## Options considered

### Option A — Keep manual replacement only (ADR-009 as is)

The app shows its version and links to the release page. The owner downloads, checks and extracts the ZIP.

- For: zero new attack surface.
- Against: the owner has to verify hashes by hand, which in practice is skipped. Nothing stops a user extracting a tampered ZIP. Data-preserving replacement relies on the user following steps.

### Option B — Notify only

The app checks GitHub, verifies the signed release manifest, and tells the owner that a verified newer version exists, with a link. Installation stays manual.

- For: removes the "I didn't know there was an update" problem with almost no execution risk. It is a strict subset of Option C, so it can ship first.
- Against: the owner still downloads and extracts by hand and gets no tamper protection on the ZIP they actually run.

### Option C — Owner-approved, signed, side-by-side self-update (recommended end state)

The app discovers the release, verifies an owner-signed release manifest against a public key compiled into the app, downloads the ZIP only after approval, verifies every file, installs it beside the current version in a per-user app-owned folder, hands over to it, and counts the update as successful only after the new version passes a health check. The previous version stays as the rollback target.

- For: the strongest tamper protection of any option, because the trust anchor is a key the owner holds offline, not GitHub. It needs no elevation for the standalone package. Rollback is a pointer change, not a reinstall.
- Against: the most code, a key-management duty for the owner, and a change to where the app lives on disk (see "Install location").

### Option D — MSIX package with an App Installer (`.appinstaller`) feed hosted on GitHub

Windows performs the update and checks the package signature.

- For: the OS owns replacement and rollback, and the signature check is Windows', not ours.
- Against: needs a code-signing certificate that Windows trusts (paid or Azure Trusted Signing), which does not exist. App Installer can update in the background on its own schedule, which conflicts with "applies only the updates the owner approves" unless configured carefully. It changes the packaging model chosen in ADR-002. Worth revisiting if a Store or MSIX deployment is ever chosen.

### Option E — Trust GitHub's own signals only (HTTPS, release author, GitHub artifact attestations)

- Against: all of these are controlled by whoever controls the GitHub account or the release workflow. Verifying Sigstore-based attestations properly needs an online transparency-log and trust-root refresh, which is a cloud dependency. Useful as extra evidence to display, never as the trust decision.

## Decision (proposed)

Adopt **Option B first, then Option C**, behind the gates in "Before implementation". Reject D for now and E as a trust anchor. The rest of this section describes Option C; Option B is the "Discover" and "Verify the manifest" steps only, ending in a notification.

### 1. Trust anchor: an owner-held release key

- PC Steward compiles in one or two **release public keys** (ECDSA P-256, which .NET supports natively, so no new dependency). The key list is code, never configuration, database or downloaded data.
- Every release carries a small **release manifest** (`pcsteward-release.json`) and a detached signature (`pcsteward-release.json.sig`) made with the matching private key.
- The private key **never lives in GitHub**: not in Actions secrets, not in the repository, not on the CI runner. The owner signs on their own machine, ideally with a hardware-backed or non-exportable key. A GitHub compromise can then publish assets, but it cannot publish a release that PC Steward will accept.
- Once a production Authenticode certificate exists, release executables are also Authenticode-signed and the updater additionally requires a valid signature from the pinned publisher. That is a second, independent check, not a replacement for the manifest key.
- Key rotation: a new key is introduced by shipping it in a release signed by the old key. If a key is believed compromised, the next release removes it, and owners on older versions are told to update manually from the verified release page. The app cannot revoke a key by itself without an online authority, and this ADR accepts that limit rather than adding one.

### 2. Release manifest contents

The manifest is the only thing the updater trusts, and only after its signature verifies. It is canonical JSON with a fixed schema; unknown fields reject the manifest. It holds:

| Field | Purpose |
|---|---|
| `schema` | Fixed manifest schema version. |
| `product` | Literal `PCSteward`. Rejects manifests for anything else. |
| `channel` | `stable` (only value accepted until `FR-ONB-05` Beta channel is in scope). |
| `version` | Semantic version of the release. Must match the Git tag and the version stamped in the binaries. |
| `sequence` | Strictly increasing integer across all releases, used for downgrade and replay protection. |
| `publishedUtc` | When the owner signed the release. |
| `minimumUpdaterVersion` | Oldest PC Steward that may apply this release directly; older ones are told to update in steps or manually. |
| `assetName`, `assetSize`, `assetSha256` | The one ZIP to download, its exact size and hash. |
| `payloadManifestSha256` | Hash of the per-file SHA-256 manifest inside the ZIP (the one the package script already produces). |
| `sbomSha256` | Hash of the SBOM in the ZIP, so the evidence shown matches what is installed. |
| `schemaVersionAfter` | Database schema version the release migrates to, so the review screen can say whether rollback needs the data backup. |
| `keyId` | Which compiled public key signed it. |

The manifest holds no URL, command, path, argument or script. Release notes are not in the signed manifest and are shown as untrusted plain text.

### 3. Discover

- Only on the owner's request ("Check for PC Steward updates"), and optionally as a notify-only check inside the existing scheduled scan if the owner turns that on. The scheduled host gains no download or install capability, consistent with ADR-020.
- One fixed, compiled, unauthenticated HTTPS request to the GitHub REST API for the latest releases of the fixed repository. No token, no account, a generic user agent (`PCSteward/<version>`), bounded response size, timeout and one request per check. GitHub's anonymous rate limit is far above what this needs.
- Drafts and pre-releases are ignored. The repository identity, API host and allowed asset download hosts are compiled allowlists. Redirects go only to the GitHub asset host allowlist.
- Everything returned is untrusted (`TM-01`, `FR-SRC-05`): it only tells the updater where to fetch the manifest and signature. Release title, body and tag are display text at most.
- What the check reveals: the user's IP address and PC Steward's version reach GitHub. This is disclosed in the settings screen. There is no telemetry.

### 4. Verify the manifest (before the owner is asked anything)

The updater downloads the manifest and signature (small, size-capped) and accepts the release as a candidate only if all of these hold:

1. The signature verifies against a compiled key whose `keyId` matches.
2. `product`, `channel` and `schema` are the expected literals.
3. `version` is greater than the running version, **and** `sequence` is greater than both the running build's own compiled sequence and the highest sequence this machine has accepted before (stored in the per-user database). Equal or lower is a downgrade or replay and is rejected (`TM-09`).
4. The tag that pointed to it equals `version`.
5. `minimumUpdaterVersion` is not above the running version.
6. Optional **cooling-off window**: `publishedUtc` is at least N hours old (see decisions). This gives the owner time to notice and withdraw a release made with a stolen key before other machines accept it.

A failure in any step is shown as "Update found but not trusted" with the reason, and nothing is downloaded. It is never shown as "up to date".

### 5. Owner review and approval

The review screen shows: current and new version, signed publish date, signing key ID, ZIP size and SHA-256, whether the release migrates the database, the rollback plan, and the untrusted release notes clearly labelled as such. It states that PC Steward will close and reopen.

Approval creates an **immutable self-update job** in the ADR-017 style, containing only: job ID, from-version, to-version, sequence, manifest SHA-256, asset SHA-256 and approval time. No URL, path or command. The job is revalidated at execution; if a newer manifest appears or anything changed, the job becomes non-actionable and the owner reviews again.

Nothing is ever downloaded or applied without this approval. There is no "install automatically" setting in this ADR.

### 6. Download and verify the package

- The ZIP is downloaded into the app-owned downloads root (ADR-019), with the exact `assetSize` as a hard cap and an active lease so cleanup cannot remove it mid-job.
- Its SHA-256 must equal `assetSha256`. Then it is extracted into a fresh staging folder under the same owned root, with ZIP-slip, absolute-path, alternate-stream, reparse-point, duplicate-entry and entry-count/size limits enforced.
- Every extracted file must appear in the payload manifest with a matching hash, the payload manifest's own hash must equal `payloadManifestSha256`, and no extra files may exist.
- If Authenticode signing is in force, both executables must carry a valid signature from the pinned publisher.
- Only then is the staged folder promoted.

### 7. Install location and hand-over (no elevation needed)

The extracted-anywhere ZIP cannot safely replace its own running files in a folder the app does not own. Self-update therefore moves PC Steward into a fixed per-user, app-owned binaries root:

`%LocalAppData%\Programs\PCSteward\versions\<version>\`

This is separate from the per-user data root, so factory reset still leaves binaries in place (ADR-013), and deletion of old versions is a new compiled ADR-019 category with the same reparse and containment rules.

The hand-over is:

1. Back up the SQLite database (the ADR-015 pre-migration backup) if `schemaVersionAfter` is newer than the current schema.
2. Promote the verified staging folder to `versions\<new>\`.
3. Close the current desktop; the scheduled host is blocked by the same data-operation lease used for factory reset.
4. Start `versions\<new>\PCSteward.Desktop.exe` with one fixed, typed "complete self-update" flag and the job ID. No other arguments.
5. The new version runs its health check (section 8). If it passes, it repoints the current-user Start menu shortcut and re-registers the exact scheduled task action to the new fixed path (ADR-020), then records Success.
6. The previous version folder is kept as the last-known-good. Older folders beyond that are removed through the owned-root deletion path.

The first self-update from a loose ZIP "adopts" the app into the managed root. The old extracted folder is left untouched, and the owner is told they can delete it.

No step requests UAC. A same-user attacker can already modify the standalone package (ADR-002), so moving it to a per-user folder does not weaken the boundary. A future protected `%ProgramFiles%` deployment (ADR-002 Option B, ADR-018) would need a separate, typed, one-off elevated "replace PC Steward" operation and its own review; that is out of scope here.

### 8. Verification before Success

The self-update job is Success only when the new process proves all of:

- its own assembly version equals the job's to-version;
- every file in `versions\<new>\` still matches the payload manifest;
- the database opened, any migration completed, and `PRAGMA integrity_check` returned ok;
- the scheduled task (if enabled) and Start menu shortcut point to the new fixed path and nothing else changed in them;
- the desktop window reached its normal first screen.

Anything else is Failed or Inconclusive, never Success (`TM-12`).

### 9. Rollback

- **Automatic, before Success.** If the health check fails or the new version does not report within a bounded time, the next launch (through the shortcut, which still points at the old version until Success) offers to restore. Restoring means restoring the database backup if a migration ran, and leaving the old version as current. Because the old version never stopped being the target of the shortcut and task, an interrupted update cannot leave the app unlaunchable.
- **Owner-initiated, after Success.** Settings offers "Go back to version X" while the previous folder exists. If the update migrated the database, the screen says that changes made since the update will be lost, because migrations are forward-only (ADR-015). Rolling back records the higher sequence as still seen, so the rolled-back version will offer the newer release again but will not accept an older one.

## Threats

Proposed additions to `docs/architecture/THREAT_MODEL.md` once accepted:

| ID | Threat | Controls in this design |
|---|---|---|
| TM-19 | **Compromised release**: attacker with GitHub access publishes a malicious ZIP, edits an existing release or swaps an asset | Trust anchor is the owner's offline key, never GitHub. Manifest signature, asset hash and per-file hashes. Signing key not in GitHub. Optional cooling-off window. Authenticode publisher pinning once a certificate exists. |
| TM-20 | **Stolen release signing key** | Hardware or non-exportable key, kept off CI. Cooling-off window lets the owner withdraw a bad release. Rotation via signed key list. Residual risk accepted and disclosed: no online revocation. |
| TM-21 | **Downgrade or replay**: serving an older, genuinely signed release with known bugs | Strictly increasing `sequence` compared with the compiled sequence and the highest accepted sequence; version must increase; tag must match. |
| TM-22 | **Freeze attack**: network or mirror hides newer releases | Last successful check time is always shown. "Could not check" is never shown as "up to date". Residual risk accepted: without an online timestamp service a freeze cannot be fully detected. |
| TM-23 | **Malicious package contents**: path traversal, reparse points, extra files | Hardened extraction into an owned root, exact file set and hashes, size and count limits, verify before promote. |
| TM-24 | **Interrupted or failed update** leaves no working app | Side-by-side install, shortcut and task repointed only after Success, database backup, automatic restore offer. |
| TM-25 | **Updater becomes an unattended installer or persistence** | No auto-install setting; scheduled host stays notify-only; one immutable job per approval; no service, tray or resident updater. |
| TM-26 | **Remote metadata becomes execution authority** (URL, tag, notes) | Manifest has no URL, path or command; compiled host and repo allowlists; release notes are plain untrusted text. |
| TM-27 | **Privacy**: the check reveals use of PC Steward | Off unless the owner turns it on (see decisions); no token or identifier; disclosed in settings. |

Existing `TM-09`, `TM-12`, `TM-13` and `TM-18` also apply.

## Consequences

### Positive

- The owner gets tamper-checked updates without verifying hashes by hand, and a GitHub compromise alone cannot push code onto their PC.
- No elevation, service or background updater is added for the standalone package.
- Rollback is cheap and an interrupted update cannot leave PC Steward broken.

### Negative / trade-offs

- New code that handles untrusted downloads, plus a key the owner must protect and back up. Losing the key means owners must update manually once to a release carrying a new key.
- PC Steward moves to a fixed per-user binaries folder on first self-update, which changes ADR-002's "run from anywhere" model for owners who use the updater.
- The release process gets an extra, manual signing step that cannot be automated in CI without weakening the design.
- Rolling back after a database migration loses changes made since the update.

### Follow-up controls and tests (when implemented)

- Fixture tests for every manifest rejection: bad signature, unknown key, wrong product or channel, unknown field, lower or equal sequence, tag mismatch, too-new minimum updater version, inside cooling-off window.
- Download tests: size overrun, hash mismatch, redirect to a non-allowlisted host, truncated transfer, cancellation.
- Extraction tests: ZIP-slip, absolute paths, reparse points, alternate streams, duplicates, extra and missing files.
- Hand-over tests: crash at each step, health-check failure, database migration failure, rollback with and without migration, shortcut and scheduled-task repointing, adoption from a loose ZIP.
- Architecture tests that `PCSteward.ScheduledHost` has no reference to download, install or self-update capabilities.

## Before implementation (gates)

1. Owner accepts this ADR (and the decisions below).
2. The release key exists, is held as decided, and a reproducible release build plus the signing step are documented and tested (Task 30 follow-up). Task 48 showed the desktop build is not yet byte-identical across build environments (CsWinRT-generated code differed), so the owner signs the exact artefacts CI produced after checking them, or builds locally; this is decision 3.
3. Option B (notify only) ships and runs for at least one release cycle before Option C install code is merged.
4. Independent security review of the implementation, and an isolated-environment test of update, interrupted update and rollback.

## Decisions for the owner

1. **Scope and order.** Accept Option B (notify only) first and Option C (in-app install) after it, as recommended? Or only B, or go straight to C?
2. **Trust anchor.** Use an owner-held release signing key compiled into the app (recommended), and add Authenticode publisher pinning once a certificate exists? Or wait for a paid or Azure Trusted Signing certificate and rely on that alone?
3. **Where the key lives and how releases are signed.** Hardware key or non-exportable key on your own PC (recommended), never in GitHub. Do you sign the CI-built artefacts after checking them, or build and sign locally?
4. **Install location.** Accept moving PC Steward to `%LocalAppData%\Programs\PCSteward\versions\` on first self-update, keeping one previous version for rollback?
5. **Checking.** Manual "Check for updates" only, or also a notify-only check during the scheduled scan? Should checking be off until you turn it on (recommended, since it contacts GitHub)?
6. **Cooling-off window.** Only offer releases signed at least N hours ago (for example 24 hours) so a bad release can be withdrawn first, or offer immediately?
7. **Pre-release / Beta channel.** Stable only for now (recommended), with `FR-ONB-05` Beta channel left as Later?
8. **Rollback after a database migration.** Allow it with a clear "changes since the update will be lost" warning (recommended), or block rollback once the new version has migrated data?

## Reconsider when

- A trusted production code-signing certificate exists and an MSIX or Store deployment is chosen (Option D becomes attractive).
- A protected `%ProgramFiles%` deployment is restored, which needs a separately reviewed elevated replace operation.
- Key management proves impractical for the owner.
