# PC Steward build status

**Current phase:** Phase 5 — v0.3 re-baseline (personal Patch-style updater)

**Current task:** Task 51 - curated manual release (Task 1 provider health candidate implemented; Windows CI and independent review pending)

**Reviewed implementation:** Tasks 32–41 evidence retained; Task 42 closes the two confirmed residual findings without changing distribution authority

**Current branch:** `feat/curated-manual-release`

**Phase gate:** Distribution gate `FRA-14–16` retired for personal owner-only use (D-006, C-11). Each v0.3 phase closes with one owner review plus live use (C-12).

## Curated manual Task 1 — provider health candidate (2026-10-09)

**Requirements:** CRD-08, FR-PRV-01; prerequisite boundaries for FR-PRV-02-04 and AC-V3-11. Typed health reports grant no execution approval, source consent or reachability authority. Provider-owned probes use canonical, error-aware presence observations; failed observations are Unknown. Chocolatey explicitly requires protected caller-cannot-modify layout and strictly revalidates the captured client immediately before a read-only version launch. Configured sources are bounded local display evidence; no source setup, provider switching or automation is implemented here.

**Test-first evidence:** Windows CI [37993415753](https://github.com/DanDG923/pc-steward/actions/runs/37993415753), checkpoint `2108285`, job `114033167824`: Release build passed with 0 warnings/errors; Architecture 36, Catalog 96, Domain 87, Security 72 passed; Application 305 passed and exactly 13 new provider-health assertion regressions failed (0 skipped). The inaccessible-directory safety control passed. Total 596 passed / 13 failed.

**Candidate validation pending:** production probe/discovery/strict-launch implementation and additional fake-only native discovery, source-input, immutable-snapshot and cancellation fixtures are prepared for Windows CI and independent review. No SDK is installed locally; local tests cannot run, and no candidate pass is claimed. No real provider/client process, network probe, installation, UAC or application/task state change occurred. Last-known timestamp/stale presentation remains a caller presentation responsibility for subsequent UI composition.

## Task 49 safety follow-up — start boundaries (2026-10-09)

**Scope:** `FR-BAT-06/08`, `AC-V3-06/07`; separate draft PR targeting the Task 49 branch.

- Cancellation is rechecked after the last elevated desktop precheck, after crash-recovery history writes, immediately before the UAC launcher, and after awaited helper revalidation. The same pre-start rule applies to unelevated prevalidation. A provider already started is still allowed to finish.
- Every elevated item receives a fresh, read-only process check after provider revalidation and before `ItemStarted`. A running app or failed/incomplete snapshot skips the item. Process closing stays in the consent-controlled desktop path; the helper never closes an app.
- The strict process snapshot rejects truncation or unreadable process evidence. The preexisting desktop warning detector retains its permissive snapshot behavior.
- Regression fixtures cover an app opened while an earlier provider runs, reopening during UAC or after consented close, failed snapshots, snapshot truncation, last/only elevated prechecks, and cancellation during awaited helper checks/history writes.

**Test-first evidence:** Windows CI [37983462201](https://github.com/DanDG923/pc-steward/actions/runs/37983462201), commit `2835f65`, built with 0 warnings/errors. All 576 existing tests passed; exactly six new cancellation regressions failed at the expected zero-launch assertions before the fix. Windows CI [37984168679](https://github.com/DanDG923/pc-steward/actions/runs/37984168679), commit `21feb6a`, also built with 0 warnings/errors and reproduced all 14 intended failures across the cancellation, running-app and truncation regressions; the remaining 577 tests passed. Final fix validation is pending.

**Limits:** Process matching remains package-derived, as in Task 49. A snapshot is a last-moment guard, not an OS-level lock preventing a process from starting after the snapshot. No real package installer, UAC acceptance, desktop integration, merge, or deployment is part of this follow-up.

## Task 49 batch execution, helper protocol v2 and running-app policy

**Coordinator** (`FR-BAT-04–07`, `FR-INS-05/07`, `AC-V3-04/06`). `BatchExecutionCoordinator` runs one `ApprovedOperationBatch`:
- **Before starting:** it refuses a batch that fails its integrity check, has expired, or overlaps another run.
- **Order:** unelevated items run in the desktop first, then every elevated item in **one** helper session (one UAC prompt).
- **Before each item:** the running-app policy applies, then revalidation against fresh evidence. A changed invariant becomes the new `Skipped` history status and is never adapted.
- **After the provider exits:** the item only goes to verification:
  - verified → Success;
  - otherwise, with a provider- or inventory-reported restart → Restart required;
  - otherwise → Inconclusive.
- **Cancel remaining** is observed only between items. Abort while a provider runs → Verification required.
- **Crash safety:** elevated items are recorded as Running before UAC, so a desktop crash recovers to Verification required.
- **No prompt for nothing:** if the desktop pre-check withholds every elevated item, no UAC prompt is raised.
- **Summary:** `BatchCompletionSummary` separates Succeeded, Restart required, Failed, Not verified and Skipped. It lists retry candidates for a new reviewed batch.

**Helper protocol v2** (`AC-V3-05`, ADR-018 note):
- The helper receives only the batch's elevated items, bound to the batch by `ApprovedOperationBatch.ElevatedDigest`.
- Frames are strict, bounded JSON. The helper's one-shot receiver rejects substitution, reordering, appending, truncation, foreign or rebound items, replay, an expired request or batch, oversize frames, unknown or duplicate fields, numeric enum values, unelevated items and forged digests.
- The helper session revalidates each item against fresh provider evidence, builds the provider operation in code, and applies CancelRemaining between items. The overall deadline is 25 minutes per item, capped at 4 hours.
- The desktop reader accepts helper frames only in order and bound to the batch and nonce. If the session breaks, only the possibly running item needs verification.
- The bootstrap accepts protocol `2`, and the transport launches one helper per batch. Protocol v1 is unchanged for the existing single-update path.

**Running apps** (`FR-BAT-08`, `AC-V3-07`): `RunningApplicationPolicy` skips a running app by default.
- It closes an app only with per-app (`CloseAppConsent`) or per-run consent, gracefully with a 30-second bound.
- It forces termination only with the second per-run consent. Automation never closes an app.
- `WindowsRunningApplicationCloser` closes only current-session processes with allowlisted names. For an elevated item, a running app is withheld from the helper as a skip.

**Production pieces:**
- `DesktopOperationExecutor` runs unelevated WinGet installs and plain user-scope upgrades only.
- `HelperOperationExecutor` and `HelperOperationRevalidator` do the in-helper revalidation and execution and read no per-user database.
- `ElevatedBatchExecutor` adapts the transport.
- `InventoryWorkflow` implements `IOperationRevalidator`: upgrades repeat the full fresh selection, and installs repeat the exact preflight.

**Results (Linux SDK 10.0.112):**
- Domain **87/87**, Catalog **96/96**, Architecture **34/34**.
- Security **60/64** (39 new, all passing). Application **248/295** (14 new, all passing).
- The remaining 51 failures are identical to the HEAD baseline (Windows-only APIs).
- The in-memory end-to-end exchange tests passed in 5 repeated runs.
- ElevatedHelper, ScheduledHost, CatalogTool and Performance build cleanly. The WinUI desktop builds in CI.

**Not wired yet / follow-ups:**
- The desktop still uses the v0.2 single-update flow. The review sheet, progress and summary UI and Select all safe come in Task 52, which also composes the coordinator.
- Desktop revalidation of an upgrade repeats a full inventory scan per item. That is correct but slow for large batches; Task 52 should measure it.
- Full-scan versus batch exclusion (`FR-RSC-02`) is enforced by the desktop data-operation lease when the coordinator is composed.
- Whether in-process WinGet COM works from the elevated helper still needs the owner probe (spike §2.4). If it fails, helper revalidation of WinGet items fails closed (Skipped).

## Task 48 operations, batches and install building blocks

**Package-ID allowlist** (spike §5.5):
- WinGet IDs may contain `+` and `_`, for example `Notepad++.Notepad++` and `Microsoft.VCRedist.2015+.x64`.
- Chocolatey (NuGet) IDs accept `_` but never `+`.
- The domain also now enforces the allowlisted provider/source pairs.

**Operation kinds** (`FR-INS-01`, ADR-017 v0.3):
- `UpdateJobSelection` gains:
  - `Operation`, which is `Upgrade` or `Install`;
  - `AgreementHash`;
  - `RunsElevated`, which is true for machine scope and for `ElevationRequired`/`ElevatesSelf` installers.
- An Install has no installed version, and a Microsoft Store install is refused.
- Persisted v0.2 Upgrade digests are unchanged.
- Only plain upgrades may use the v0.2 runners, the upgrade argv builders and helper protocol v1. They deny anything else before any process or UAC prompt.

**Batches** (`FR-BAT-04/05`, `AC-V3-05` domain half):
- `OperationBatchReview` → `ApprovedOperationBatch`:
  - 1–50 items, unique by product and package;
  - every job bound to the batch correlation and approval time;
  - unelevated items first;
  - a chained digest over index, job ID and job digest;
  - a 30-minute lifetime.
- Substitution, reordering, appending, truncation, replay under another ID and clock rollback all fail.

**Persistence:**
- Migration 10 rebuilds `approved_update_jobs` in one transaction. Pre-v10 rows keep their digest and route.
- It adds `operation_batches`, `operation_batch_items` and `agreement_acceptances`.
- Batch reads recheck membership, order and every digest.

**Agreements** (`FR-INS-03`, `TM-22`):
- `PackageAgreementDigest` is a bounded, length-prefixed SHA-256 (at most 16 agreements of up to 100 000 characters).
- `OperationBatchApprovalService` will not confirm an item until acceptance of that exact hash, package and version is recorded.
- Acceptances are cleared with mappings and policies.

**Install preflight** (`FR-INS-02/04`, `AC-V3-02`):
- **WinGet:** `WingetInstallPreflightAuthority` uses COM (the ADR-003 amendment is applied). It needs:
  - one exact trusted `winget` match, not installed;
  - the default or the approved version;
  - no channel;
  - an applicable installer for the scope and architecture;
  - bounded agreements.
- **Chocolatey:** `ChocolateyInstallPreflightAuthority` runs `search <id> --exact [--version] --approved-only --not-broken` against the fixed feed. It needs exactly one strict `id|version` row and no local registration.
- On revalidation, a changed version, agreement set or elevation route is not actionable.
- `InventoryWorkflow.PrepareInstallSelectionAsync`:
  - requires source consent and a validated catalog entry;
  - uses the listed provider (the default unless overridden) and an allowed scope;
  - refuses any detected app, even a name-only match.

**Install argv** (exact snapshots):
- WinGet: `install --id --exact --version --source winget --scope --architecture --silent --disable-interactivity`, plus `--accept-package-agreements` only behind the recorded hash and matching fresh evidence.
- Chocolatey: `install <id> --version --source <feed> --yes --limit-output --no-progress --no-color --execution-timeout 300`.

**Exit codes:** WinGet and Chocolatey codes map onto the history taxonomy. Exit 0, restart and "already installed" only ever lead to verification.

**Install verification** (`FR-INS-05`): `ReInventoryAsync` routes Install jobs to two authorities:
1. the provider's exact listing at the target version;
2. a re-inventory product that the catalog detects exactly as the entry, with registry or MSIX evidence.

The provider's own list is not local evidence.

**Fix found by the new tests:** persistence stores a missing publisher as `""`, which turned name-only catalog detections into "not detected". `PersonalCatalogMatching` now treats a blank publisher as missing.

**Guards:** `ProviderOperationBoundaryTests` forbids, across all product sources:
- the spike §2.2 and §3.1 flags, including `--accept-source-agreements`;
- the dangerous WinGet `InstallOptions` members.

It allows exactly one guarded `--accept-package-agreements`.

**Results (Linux SDK 10.0.112):**
- Domain **87/87** (23 new), Catalog **96/96**, Architecture **34/34** (4 new).
- Application **234/281** (93 new, all passing).
- Security **21/25** (3 new, all passing).
- The 51 failures are identical to the HEAD baseline: Windows-only APIs (`wintrust.dll`, reparse points, NTFS streams, named pipes, Task Scheduler).
- ElevatedHelper, ScheduledHost, CatalogTool and Performance build cleanly. The WinUI desktop builds in CI.

**Not wired yet:**
- The batch coordinator, helper protocol v2 and per-item execution are Task 49.
- Review-sheet UI is Tasks 51–52.
- The catalog tool's live verification (ADR-023) can reuse `WingetInstallPreflightAuthority`. It needs the owner's Windows PC and is left for the owner probe.

## Task 47 catalog matching, states and policies

**Catalog detection in matching** (`FR-CAT-05`, `FR-ID-06`, `FR-INS-06`, `AC-V3-02`):
- `CatalogDetector` (in `PCSteward.Catalog`) checks evidence in order:
  - provider identity: WinGet only from the `winget` source, Chocolatey registration, or MSIX family;
  - anchored name plus publisher (Exact);
  - name only, which is never Exact.
- `PersonalCatalogMatching` combines per-instance results. Any conflict becomes Ambiguous, with no mapping.
- Exact matches become declared mappings with the new `MappingOrigin.Catalog`. These pass the same trusted-source, compatibility and newer-version validation as user-confirmed mappings.
- An installed app keeps its managing provider. A name-and-publisher match uses the preferred provider, or the catalog default (ADR-024).
- User-confirmed mappings always win over catalog mappings.

**Catalog states** (`FR-CAT-04`): `CatalogAppState` covers NotInstalled, UpToDate, UpdateAvailable, ReviewRequired, InstalledNotManaged, Unavailable, Skipped, Pinned and Ignored. `InventoryWorkflow.GetCatalogOverviewAsync` returns them.

**Per-app policies** (`FR-POL-06`, `FR-PRV-04`, `FR-AUT-01` and `FR-BAT-08` storage):
- Skip this version adds the new `UpdateIntelligenceStatus.Skipped`; a newer version is offered again. Preferred provider, automatic-updates opt-in and close-app consent are also recorded.
- They are stored as **stable-decision kinds** rather than a separate `app_policies` table. They therefore inherit the existing semantics: kept by Clear scan data, removed by the mappings/policies clear and factory reset, reversible, and listed with active decisions.
- Stored values outside the allowlist are ignored.

**User catalog entries** (`FR-CAT-06` storage):
- Migration 9 adds `user_catalog_entries`. Rows are revalidated on every read through `CatalogValidator.Compose`.
- Rules: `pcs:user-` prefix, one provider, provider-identity detection only, no reuse of a bundled ID or package, `msstore` refused.
- Rejected rows are reported and never disable the bundled catalog.
- Rows are kept by Clear scan data and removed by the mappings/policies clear and factory reset.

**Results (Linux SDK 10.0.112):**
- Domain **64/64** (5 new), Catalog **96/96** (22 new), Architecture **30/30**.
- Application integration: the 7 new catalog/workflow/persistence tests and the related decision, SQLite, workflow and view-model suites pass, 63/63.
- The full Application suite runs 188 tests. The 47 failures on Linux, plus 4 Security failures, all come from Windows-only APIs (`wintrust.dll`, NTFS alternate data streams, named pipes); Windows CI is the gate.
- The desktop gains only the `Skipped` status label. Its WinUI build runs in CI.

**Payload change:** `PCSteward.Application` now references `PCSteward.Catalog`, so the desktop output gains `PCSteward.Catalog.dll`, which carries the embedded catalog. The protected-install and package scripts discover `PCSteward.*.dll` dynamically and sign it.

**Not wired yet:** the desktop and scheduled host do not pass the bundled catalog to `InventoryWorkflow`. Catalog matching therefore has no effect in the installed app until the UI tasks (51/53) wire it, after the owner has reviewed the starter catalog.

## Task 46 personal catalog

**New `PCSteward.Catalog` project** (`FR-CAT-01–03`, `AC-V3-01`, `TM-19`). It has no project or package references, and the architecture tests forbid process, registry, network and file APIs. It contains:
- an immutable catalog model;
- a strict fail-closed validator:
  - size, schema and catalog-version checks;
  - unknown fields, duplicate JSON properties, comments and trailing commas refused;
  - unique IDs and provider identities; WinGet/Chocolatey identity, scope and default rules; `msstore` refused;
  - bounded process names (now accepting `+`);
  - optional SHA-256 check; every catalog is reported with its SHA-256;
- restricted-grammar detection patterns: an escape- and character-class-aware scanner, the interpreted engine and a 50 ms timeout. This applies the ADR-023 Task 45 amendment and D-008;
- the embedded bundled catalog (`catalog/pcsteward-catalog.v1.json`: 20 starter entries, not yet live-verified);
- a pure seed proposer.

**Owner tooling** (`FR-CAT-07`): `tools/PCSteward.CatalogTool` and `scripts/Catalog-Validate.ps1` / `Catalog-Seed.ps1`. Seeding opens the database read-only and never overwrites a file. Live verification stays with the Task 45 probe until Task 48.

**Results:**
- `PCSteward.Catalog.UnitTests` **74/74**: 39 hostile-catalog cases, grammar bypass cases (including `^(a*\\?)+$`), timeout backstop, immutability, a 300-entry catalog validated in under 1 s, and the seed proposer.
- Architecture **30/30** and Domain **59/59**.
- The tool validated the bundled catalog and produced a valid 4-entry draft from a realistic fake database without changing it.
- These ran on Linux with SDK 10.0.112, outside the `global.json` pin. The full Windows build and suite run in CI.

**Not changed:** the desktop does not reference the catalog yet (Task 47/53), so the protected-install payload is unchanged.

## Task 45 v0.3 spikes

`spikes/patch-style/SPIKE_REPORT.md` records the following.

- **WinGet COM API:** the shipped API supports install and upgrade with structured progress, exact `PackageVersionId`, agreement metadata and per-installer `ElevationRequirement`. `elevatesSelf` installers must route to the elevated helper to keep one UAC prompt per batch.
- **Helper protocol v2 prototype:** rejects all 11 tamper cases plus replay and expiry. The worst-case frame is 1,132 B against the 16 KiB bound.
- **Catalog prototype:**
  - All 18 hostile mutations disable the catalog.
  - `NonBacktracking` is too slow (3.5 s for 600 patterns). It is replaced by the interpreted engine with a restricted grammar and a 50 ms timeout: a 300-entry catalog loads in 12 ms and 700 records are checked in about 65 ms.
  - The illustrative detection set gave 0 false positives.
- **Allowlist gaps:** the production package-ID and process-name allowlists reject `+` (`Notepad++.Notepad++`).
- **Chocolatey setup:** `Chocolatey.Chocolatey` exists in WinGet as a machine-scope MSI that WinGet will not upgrade.

Spike tests: **50/50**. The probe compiles for Windows with 0 warnings.

Linux network policy blocked the Patch My PC, Microsoft Learn, Chocolatey docs and Chocolatey feed sites. Their documentation was read from GitHub sources or search results instead.

**Pending, owner-run and read-only:** `spikes/patch-style/Invoke-PatchStyleProbe.ps1`. It captures `choco search --limit-output` formats, `winget-preflight` data for the sample catalog, and detection counts from the local database. Its output folder is git-ignored.

Next is **Task 46** (catalog), which applies the ADR-023 Task 45 amendment.

## Task 44 v0.3 decisions and re-baseline

The owner accepted every recommendation (D-007). Applied documents:

- **Product specification v0.3:** new §27 (`FR-CAT-01–08`, `FR-INS-01–07`, `FR-BAT-01–08`, `FR-AUT-01–10`, `FR-PRV-01–04`, `FR-POL-06`, optional `FR-UNI-01–03`, `AC-V3-01–12`), plus amended `FR-UPD-01/12`, `FR-SCH-03`, `FR-INV-11`, `FR-ID-06`, principle 8, non-goals, Tier D, §11.1, §13.3, §14.3 and §19.8.
- **`AGENTS.md`:** safety boundaries and the personal-use process.
- **ADRs:** new ADR-023 (personal catalog), ADR-024 (provider precedence and Chocolatey setup through WinGet) and ADR-025 (Stage A/B automatic updates); v0.3 revisions to ADR-017 (operation kinds and batches), ADR-018 (one helper session per batch) and ADR-020 (separate automatic-update hosts).
- **Threat model:** TM-19–24 and an amended TM-14.
- **Records:** decision-log questions Q-004–Q-009 resolved; v0.3 traceability added.

No code changed. Next is **Task 45**: spikes on WinGet CLI versus COM for install, preflight, agreements and progress; Chocolatey install/search/info output; a helper v2 prototype; and catalog seed and detection precision on the owner inventory.

## Task 43 proposed overhaul (approved by D-007)

[`OVERHAUL_PLAN_PATCH_STYLE.md`](OVERHAUL_PLAN_PATCH_STYLE.md) proposes turning PC Steward into a Patch My PC–style updater for the owner's own PC, using WinGet and the fixed Chocolatey Community feed. It covers a personal catalog seeded from the owner's installed apps (with install support and in-app Add from search), a tick-box Updates checklist with one review sheet, one UAC prompt per immutable batch, and opt-in automatic updates: user-scope first, then unattended admin-level updates from an admin-protected policy store. The owner confirmed personal, owner-only use (D-006), so the plan retires `FRA-14–16` and uses one owner review per phase (C-11, C-12). It changes no requirement, ADR, safety boundary, code or test. The owner approved changes C-1…C-12 and decisions Q-004…Q-009 in Task 44 (D-007). Requirement families `FR-CAT/INS/BAT/AUT/PRV/UNI` are now defined in specification v0.3 §27 and traced. Implementation runs in Tasks 45–59.

**Repository fix (Task 43):** `src/PCSteward.Persistence.Sqlite/PCSteward.Persistence.Sqlite.csproj` had never been committed, so every clean clone failed `dotnet restore` (MSB3202). A reconstructed project file (Application reference, centrally versioned `Microsoft.Data.Sqlite`, `InternalsVisibleTo` for the integration tests) is now committed. On Linux with SDK 10.0.112 outside the 10.0.302 `global.json` pin, the full solution restored, the project built with 0 warnings/errors, `SQLitePCLRaw.lib.e_sqlite3` resolved to the pinned 2.1.12, and the Domain **59/59**, Architecture **28/28** and `SqliteDatabaseTests` **19/19** suites passed. The WinUI desktop build and the full Windows suite are left to CI.

## Task 35 scope and current boundary

The production desktop now implements the selected **Steward Ledger** direction: five stable task destinations, decision-first Overview, structured virtualized Applications, evidence-led Updates, a real local Activity ledger, grouped Settings, shared visual components, semantic status language, adaptive navigation, and a global operation band. Contextual scan/results/review/current/repository/storage/privacy/detail/onboarding/About routes preserve the supported workflows. No persisted schema, source authority, immutable update approval, elevation, execution, cleanup, reset, or scheduling contract changed.

Current official-source research covers ten comparable Windows inventory/update/maintenance products and is recorded with the audit, alternatives, score, selected direction, design-system map, change log, screenshots, performance result, and validation boundary under `docs/`. This work is a production implementation, not a prototype or a release claim.

## Task 32 scope and current boundary

The product owner explicitly promoted Chocolatey from the former Later connector scope and approved protected update execution. PC Steward now integrates only the fixed Chocolatey Community feed via a fixed ProgramData-root, Authenticode-validated `choco.exe`; custom configured sources, credentials, raw PowerShell/package scripts, installer paths and arbitrary source URLs cannot become PC Steward authority. Each Chocolatey update is machine-scope only, requires the existing review/UAC helper path, and is successful only after exact local re-inventory. No Chocolatey client was installed, queried, launched, or used to update an application during this task.

The prior Task 31 exception is expired by this code change. Every earlier `FRA-14–16` field/signing/distribution gap is again blocking, and Task 32 adds uncollected live Chocolatey discovery/update, UAC, timeout/cancellation and post-update evidence. The repository remains unsuitable for any distribution or release claim pending a new exact-artifact evidence task and independent audit.

## Prior audit outcome (superseded by Task 32 implementation change)

- `FRA-19` **Closed** — failed post-reset acknowledgement and successful acknowledgement with a residual marker both block before inventory load or Scan navigation; two executable scenarios pass.
- `FRA-20` **Closed** — historical lifecycle observations are bound to their complete old hash and no current-hash lifecycle claim is made.
- `FRA-18` **Closed** — focused reset evidence is accurately recorded as 25/25.
- `FRA-14` **Accepted exception** — current final-app/live/idle/process-tree/update/final-timeout performance remains `Unverified` but is non-blocking only for the exact owner-only candidate.
- `FRA-15` **Accepted exception** — current launch/update/replacement/task/reset/removal lifecycle remains `Unverified` but is non-blocking only for the exact owner-only candidate.
- `FRA-16` **Accepted exception** — signer/channel/recipient/support/distributable rollback inputs are omitted only because no distribution or other recipient is permitted.

The authoritative boundary is [Task 31 non-public evaluation exceptions](review/TASK_31_NONPUBLIC_EVALUATION_EXCEPTIONS.md). It expires on an implementation/hash change, another recipient, any distribution channel, upload/share/tag/publication, or a public/private-distribution/release-ready claim.

## Current verification

Task 32 focused Chocolatey fixtures: **6/6**. Release build: 0 warnings/errors. Full automated suite: **260/260** (55 Domain, 166 Application integration, 18 Architecture, 21 Security). The independent re-review passed: the original desktop process rejects a caller-writable/reparse/wrong-signer Chocolatey client before `runas`, while the helper revalidates canonical path, reparse state, WinTrust, exact signer and SHA-256 identity immediately before launch. The deterministic fixture workload also completed 30 local-inventory runs (median 1.85 ms, p95 2.12 ms) and 20 two-source online-check runs (median 15.54 ms, p95 16.10 ms); it is not final-app/live Chocolatey evidence. A user-initiated WizTree review on the preceding local beta was blocked by the required protected-helper layout before helper launch or provider execution; no application changed. The UI now classifies that `ProductFailed` result as failed rather than completed.

Task 33 owner-only deployment validation: **PASS**. The installer and independent read-only validator both accepted `%ProgramFiles%\PC Steward` with the same temporary owner-only signer, protected state/payload ACLs, required WinUI XBF/PRI resources and the complete hash manifest. A first protected launch exposed omitted compiled WinUI resources; the deployment now stages the resource-producing desktop build separately from the published desktop/helper payloads and fails closed if those resources are absent. The desktop also retains the active WinUI window for its lifetime. The corrected installed build opened to the normal Overview UI, displayed Chocolatey Community evidence and remained responsive during owner observation; the deployment-validation step itself did not start an update. An attempted full-suite rerun exposed reproducible pre-existing `PCSteward.Application.IntegrationTests` native-storage/reset and cache-quota failures (including `NTSTATUS=C0000022`) even with the affected quota test run in isolation. Those failures predate and are unrelated to the focused packaging/window-lifetime changes, so they remain a separate investigation item rather than Task 33 acceptance evidence.

Task 33 live WinGet update evidence: **OWNER-OBSERVED PASS for the exact tested candidate**. The owner reviewed `AntibodySoftware.WizTree` 4.31 → 4.32 in the corrected protected desktop, initiated the separately authorized update and reported success. The post-action product view identified WizTree 4.32 as installed/current, and a separate read-only `winget list --id AntibodySoftware.WizTree --exact` check returned version 4.32 with `winget` in the Source column. Together these observations support the owner-reported protected update and independently confirm the installed-version postcondition. They do not independently prove every desktop/helper IPC or process step. The repository result remained labelled cached/offline, so it is not evidence of a healthy online source refresh, Chocolatey execution or any other package/update path.

Task 34 administrator-requirement indicator: **IMPLEMENTATION, PROTECTED DEPLOYMENT, AND OWNER-MACHINE VISUAL EVIDENCE PASS**. An update detail now states that administrator requirements will be confirmed during fresh review rather than inferring from cached display scope. The confirmation dialog derives its definitive text only from the validated immutable selection: WinGet/User says administrator approval is not expected; WinGet/Machine and Chocolatey/Machine say administrator approval is required and that UAC appears only after confirmation. Chocolatey/User is now rejected as an invalid domain combination. Focused scenarios passed **3/3**, the full Architecture suite passed **19/19**, and the non-native-storage Application integration set passed **126/126**. Release build passed with 0 warnings/errors. The full repository command timed out after 180 seconds while reproducing the previously documented native-storage/reset `NTSTATUS=C0000022` and cache-quota failures; Domain **55/55** and Security **21/21** passed before timeout. Independent review found no remaining Task 34 issue. After exact owner approval, the prior generated root/certificate were removed, their absence verified, and a new signed owner-only protected package was installed. Its dedicated ACL/signature/manifest validator passed; read-only reflection against the installed assembly returned `Administrator requirement unknown` with the expected safe pre-review explanation, and the rebuilt process launched responsive with no new crash event. Task 37 then launched that exact protected installation under fresh owner authority and captured the real Vortex detail card with `Administrator requirement unknown` and the safe pre-review explanation simultaneously visible. This closes the protected-host visible-card evidence without claiming a separate personal observation by the owner; the update-review control was not invoked.

Task 35 production UI redesign: **IMPLEMENTATION AND AUTOMATED VALIDATION PASS; OPTIONAL MANUAL ACCESSIBILITY NOT CLAIMED**. Debug and Release desktop builds passed with 0 warnings/errors. UI Architecture passed **24/24** and the UI-facing Application integration slice passed **23/23**. Task 36 reproduced the prior cache failure only inside the managed filesystem sandbox, then ran the exact case successfully on the normal Windows host. The complete cache class passed **16/16**, factory reset passed **25/25**, Application integration passed **167/167**, and the official repository test script passed **267/267**: Domain **55/55**, Application **167/167**, Architecture **24/24**, Security **21/21**. No production storage code or safety boundary was weakened. The 2,500-record application presentation workload measured median **1.27 ms**, p95 **4.68 ms**; it is not WinUI frame timing. Read-only real-app inspection displayed 658 applications and the current 3 ready / 4 decision / 114 current / 537 inventoried-only counts. Five production screenshots were reviewed; selected-nav contrast, Settings/About selection, technical-value truncation, and timestamp ambiguity were fixed. Under ADR-021, `AC-UX-01–09` remain Optional; the owner has placed the manual keyboard/Narrator, Light/High Contrast, and 150%/200% text-scale matrix out of scope for owner-only use, so accessibility acceptance is not claimed.

Task 36 native-storage test-host remediation: **PASS**. The isolated quota scenario failed inside the Codex managed filesystem sandbox and passed immediately when rerun outside that sandbox. The full handle-bound cache and factory-reset fixtures then passed on the same normal Windows host, followed by all Application integration tests and the official full suite. Forty-one test-owned temporary fixtures left by earlier failed runs were removed with reparse-safe, exact-prefix cleanup; a clean full-suite rerun passed **267/267** and left zero new matching fixtures. This closes the `NTSTATUS=C0000022` test-environment blocker without adding a fallback path, skipping a security fixture, or changing deletion authority. See `docs/review/TASK_36_NATIVE_STORAGE_TEST_HOST.md`.

Task 37 protected visual and live-source validation: **PASS WITH INDEPENDENT MICROSOFT STORE UNAVAILABILITY**. The protected-install validator passed without launching the app. Under exact owner approval, the signed protected desktop displayed the Task 34 administrator card in the real Vortex detail page and produced a hashed screenshot. `Refresh connected repositories` was invoked exactly once: Chocolatey Community and WinGet returned live results, while Microsoft Store retained its prior package and timestamp with explicit unavailable/stale labels. Update-history **6**, approved-job **2**, execution-state **2**, and scan-run **21** counts were unchanged; the exact scheduled task remained absent. The app closed normally with no residual PC Steward, provider, or installer process. No update review, install/update, schedule, clear-data, reset, redeployment, or source-setting change occurred. See `docs/review/TASK_37_TASK_34_VISUAL_AND_SOURCE_REFRESH.md`.

Task 38 review-required decision actions: **IMPLEMENTATION AND AUTOMATED VALIDATION PASS; LIVE VISUAL PENDING**. Inventory results and Review actions now reuse one control set that exposes the real safe choices instead of only `Open evidence` or `Review evidence`: confirm a single live exact package identity, choose among structured trusted candidates without display-text parsing or automatic selection, pin the installed version, ignore indefinitely or until a date, and open full details. Follow-up screenshots caught the initially missed Inventory results path, missing remediation for exact matches backed by cached/offline evidence, and a distinct live Microsoft Store result whose package identity was exact but whose available version was not established. Cached/offline rows expose `Refresh source evidence` on result/review cards and detail; the open detail route rebinds after refresh. A successful live, unique, version-bound result may expose confirmation, while an offline retry stays refreshable and a non-null incomparable Store version remains non-executable even when live. A live exact identity with no available version is now `Update evidence unavailable`, appears in the separate Unavailable group, discloses the partial result, and offers refresh without pretending that map/pin/ignore is a required user decision. None creates an update job or starts update-provider, elevation, or execution activity. Focused presentation scenarios passed **20/20**, the three-surface UI contract passed **6/6**, Architecture passed **25/25**, Release build passed with 0 warnings/errors, and the corrected normal-host official suite passed **271/271**: Domain **55/55**, Application **170/170**, Architecture **25/25**, Security **21/21**. Task 39 redeployed this exact implementation; post-fix live visual evidence remains pending. See `docs/review/TASK_38_REVIEW_DECISION_ACTIONS.md`.

Task 39 owner-only update-capable rollout: **PROTECTED INSTALLATION PASS**. Commit `1298d4ae5a747d7ee489c32746f5154049800263` passed the canonical Release build with 0 warnings/errors and the complete normal-host suite **271/271**. Under explicit owner authority, the old ADR-022 deployment passed validation, its user database was copied and integrity checked, and the guarded removal removed only the protected install/state roots and installer-owned temporary certificate. The rebuilt candidate was installed at `%ProgramFiles%\PC Steward`; the new 102-file ledger, protected ACLs, same-signer desktop/helper, required WinUI resources and complete hashes passed both installer validation and the separate read-only validator. The installed desktop DLL exactly matches the local commit build, the database hash remained unchanged, the exact scheduled task remains absent, and no residual PC Steward process remains. The deployment did not launch PC Steward or update an application. It is an owner-only installed candidate with an intentionally 14-day local signer, not a permanent distribution or release-readiness claim. See `docs/review/TASK_39_OWNER_ONLY_UPDATE_CAPABLE_ROLLOUT.md`.

Task 40 private GitHub prerelease: **PASS FOR THE AUTHORIZED PRIVATE OWNER-ONLY COPY**. The clean committed source was pushed to private repository `DanDG923/pc-steward` at `d6d5caaadbd38050dc3c5fd712686c4b98b0b54f`. Published prerelease `v0.1.0-owner-only.1` contains a 3,311,204-byte exact source archive and checksum asset; downloading the remote ZIP produced SHA-256 `AA95ACB4836632CB8B5E51D41548E2647E46BE8456A77E7CBBD0B9C6591E7E91`, matching the local and uploaded checksum. The 721-entry archive contains the protected owner-only installer and no key/certificate file. It is explicitly labelled an owner-only source backup, not a portable signed binary, public/stable release, automatic-update channel, or production-signing/readiness claim. No PC Steward application or update action ran. See `docs/review/TASK_40_PRIVATE_GITHUB_PRERELEASE.md`.

Task 41 security findings remediation: **IMPLEMENTATION AND AUTOMATED VALIDATION PASS**. All 21 imported Codex Security reports were catalogued against commit `61ff38c6d39339a07005629aa057b68d4eba13f3`: 17 are confirmed security/safety defects, 2 are confirmed non-security product defects, and 2 are not actionable against the current checkout. The remediations fail closed on conflicting or incomplete package evidence, source-scoped WinGet failures, malformed source data, reparse roots, unsafe reset test roots, malformed persisted inventory, registry-entry failures and spreadsheet formulas; they also make paired connector consent transactional, scheduled AppX state authoritative, SQLite foreign keys universal, Chocolatey helper authority exact, reset onboarding resumable and orphan cleanup deepest-first. The focused security set passed **213/213**, the complete suite passed **288/288** (Domain **59/59**, Application **180/180**, Architecture **27/27**, Security **22/22**), and the Release build passed with 0 warnings/errors. The normal-host run was required for the existing handle-bound reset/cache fixtures; the managed sandbox predictably returned `NTSTATUS=C0000022`. No application was launched or updated, no local installation/state/schedule changed, and no GitHub finding was closed or published. See `docs/review/TASK_41_SECURITY_FINDINGS_REMEDIATION.md`.

Task 42 confirmed findings remediation: **IMPLEMENTATION AND AUTOMATED VALIDATION PASS**. Follow-up reproduction against commit `032c7dbb0842dff020c5b6746524671a4a1f0a9d` confirmed residual F8 and F10 defects. Legacy asymmetric trusted-source consent is now interpreted through one fail-closed predicate by desktop load, update preparation, and scheduled WinGet/Chocolatey/source-refresh paths; malformed short WinGet update rows now fail before a live snapshot can replace last-good cache. Both regressions failed before the fix. Focused scenarios passed **6/6**, the complete normal-host suite passed **290/290** (Domain **59/59**, Application **181/181**, Architecture **28/28**, Security **22/22**), and the Release build passed with 0 warnings/errors. The managed sandbox reproduced only the documented native-storage restriction. No app/update/deployment/external finding action occurred. See `docs/review/TASK_42_CONFIRMED_FINDINGS_REMEDIATION.md`.

The earlier unsigned archive hashes are superseded and not evidence for this working tree. The new unsigned local archive `1F88E89C15AA5401BC7EB508502F94059C2CD51CDD546834AC185ABF00E35056` mechanically validates 97 payload files / 50,063,593 bytes, 15 components and 16 SBOM nodes / 32 edges. It has not been launched.

## Authority boundary

One owner-run standalone-beta launch and a user-initiated WizTree review occurred after exact action-time consent. The protected helper rejected that non-installed beta path before helper launch or provider execution, so that earlier attempt changed no installed application. Task 33 later created the separately approved protected owner-only installation and temporary trust chain; the owner subsequently initiated the separately authorized WizTree 4.31 → 4.32 test described above. No real scheduled task was created or removed, no owner data was reset or deleted, and nothing was uploaded, shared or sent. These approvals do not grant further actions. Every future launch or state-changing host action requires exact action-time approval.

Task 37 used separate exact action-time authority to launch the current protected installation, observe the Task 34 card, invoke one live source refresh, capture local evidence, and close the app. Only source-snapshot/cache evidence changed. It granted no update, schedule, clear-data, reset, deployment, release, or distribution authority.

Task 39 used separate exact authority to remove and replace the old protected owner-only installation. That authority covered only the guarded deployment, local temporary signing trust chain, validation, and preservation backup; it did not authorize launching PC Steward, selecting or updating an application, changing a schedule, refreshing a source, clearing data, resetting state, or distributing the candidate.

Task 40 used explicit authority to create a private GitHub repository and upload one owner-only prerelease source backup. The owner separately approved the GitHub `workflow` scope needed to push the existing CI file unchanged. This authority did not permit public visibility, a stable/release-ready claim, another recipient, production signing, an automatic update channel, or any local application/update/data/schedule action.

Task 41 used workspace-write authority only. It made no external finding-state change, GitHub upload, application launch, update, deployment, schedule change, data clear or reset; those actions remain separately approval-gated.

Task 42 used workspace-write authority only. It made no external finding-state change, GitHub upload, application launch, update, deployment, schedule change, data clear or reset; those actions remain separately approval-gated.

## Task 33 boundary

The product owner selected an owner-only temporary-certificate route to enable a protected local installation for one real update test. It is a new, non-distribution deployment decision recorded in `ADR-022`; the standalone beta remains unchanged. The installation workflow may create only a temporary CurrentUser code-signing trust chain and `%ProgramFiles%\PC Steward` test root, and that script must not launch PC Steward or update an application. Launching the installed app and testing WizTree were separate, explicitly authorized owner actions. Any later update still requires fresh candidate evidence and separate exact action-time approval.

## Next task

Task 50 — provider health, and Chocolatey setup through a reviewed WinGet install of `Chocolatey.Chocolatey` (`docs/OVERHAUL_PLAN_PATCH_STYLE.md` §11). It covers absent, untrusted and present client states and adds an architecture test that no bootstrap-script path exists. Specification v0.3 and `AGENTS.md` are authoritative. Earlier distribution-gate wording below is historical (C-11).

The remediated Task 42 source is validated locally but is not deployed, published, or reflected in the protected owner-only installation or private prerelease. Any protected replacement, GitHub publication, finding closure, live rendering, source refresh, or selected application update is a separate owner-authorized task. A public/stable release or another recipient still requires fresh exact-artifact evidence, production signing/channel authority, and independent review for the applicable `FRA-14–16` boundaries. The owner has placed Task 35's optional manual accessibility matrix out of scope. The private backup does not close the distribution gate.

