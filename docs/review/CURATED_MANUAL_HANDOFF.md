# Curated manual release shutdown checkpoint

Dan requested an immediate durable checkpoint before shutting down this Windows PC. All local implementation work is paused. Task 3's implementer was interrupted; do not start another worker or cloud editor concurrently. Cloud continuation requires separate assessment. No system/deployment actions are authorized by this checkpoint.

Repository: `DanDG923/pc-steward` (private). Feature: `feat/curated-manual-release`, draft PR https://github.com/DanDG923/pc-steward/pull/3 , target `ccr-e4805080-7gyz6n`. Product safety base `e6339762a0e72abcb782338df9ce1cd3295aac7f`. Approved spec commit `0761fc1cb23a23393e55748e0834903b0356a574`; approved 15-task plan commit `764df330da1efd626b194d2d1f5fd1fe1b58087e`. Preserve both documents and all dependency/SDK pins.

## Verified Task 2 state

Task 2 code/review/CI gates complete at **`54eab743669ce0488ea757bf3af63c8a6ef40b9e`**, exact tree `8395c6cb748bcfabe460e1ffcec67c1f32679127`.

Independent initial review of `924212bc` found 0 Critical / 2 Important / 0 Minor: I1 clean awaited scans omitted repeated setup positive absence; I2 the default new Chocolatey choice path reused permissive layout trust at query launch. Original implementer repaired both. Fresh scoped re-review approved spec compliance and task quality, I1/I2 addressed, zero new findings. Full reports are preserved under [checkpoints/2026-10-09-curated-manual](checkpoints/2026-10-09-curated-manual).

Exact repair [Windows CI 38002369017](https://github.com/DanDG923/pc-steward/actions/runs/38002369017), job `114063206386`, completed successfully. Canonical `pwsh -NoProfile -File ./scripts/Restore.ps1`, `Build.ps1`, and `Test.ps1` passed; zero warnings/errors. Architecture 38, Catalog 97, Domain 87, Security 72, Application 381: **675 passed / 0 failed / 0 skipped**. This evidence belongs to Task 2's repair head, not this unvalidated Task 3 checkpoint.

Task 1 remains complete at `b96a5485`, 621 green tests. Its recorded Minor M1 (late typed health trust-failure catches bypass cancellation before returning an Untrusted report) remains for final whole-change triage. Later callers check after awaited health; do not claim M1 repaired.

## Interrupted Task 3 draft

Task 3 base is `54eab743669ce0488ea757bf3af63c8a6ef40b9e`. It is a **work-in-progress inert regression draft**, not READY_RED, production, reviewed, or validated. No Task 3 build/test pass or intended assertion-red is claimed. The implementer had not written its report when interrupted.

Draft task-owned paths:

- `src/PCSteward.Application/CuratedMembership.cs` (new).
- `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs`.
- `tests/PCSteward.Application.IntegrationTests/CuratedMembershipTests.cs` (new).
- `tests/PCSteward.Application.IntegrationTests/SqliteDatabaseTests.cs`.
- `tests/PCSteward.Application.IntegrationTests/FactoryResetTests.cs`.

Controller completion-evidence changes are `docs/STATUS.md`, `docs/requirements/TRACEABILITY.md`, and `docs/review/CURATED_MANUAL_PROGRESS.md`. This checkpoint additionally preserves this handoff and the evidence/ledger archive. Any automatic CI on this snapshot is provisional and must be inspected; it does not release production unless compiling genuine assertion-red is confirmed.

Proposed Task 3 layering: `ManagedAppMembership(CatalogEntryId, AddedAt)` data rows; required `ICuratedMembershipStore` methods on SQLite; service with fresh validated composed-catalog supplier, retained inventory store, active-entry guard and clock; service read wrapper exposes orphan/invalid rows as withheld. Derive only unique associated policy keys internally. Actual incomplete files, approved Task 3 text and tests are the recovery authority; this description is not source approval.

## Resume and limits

Task 3 update (2026-10-18): Windows CI 38004192067 at `817cb57` confirmed the expected red. Production `5e36d0c` and fixture repair `12310e1` then passed CI 39079096574 (703/0/0, 0 warnings). See [task-3-report.md](checkpoints/2026-10-09-curated-manual/task-3-report.md). The review requested changes (I1/M1, [task-3-review.md](checkpoints/2026-10-09-curated-manual/task-3-review.md)). The repair is in `111b448` and `bf99db1`, green in CI 39080312565 (704/0/0); see [task-3-repair-report.md](checkpoints/2026-10-09-curated-manual/task-3-repair-report.md). Next action: scoped Task 3 re-review of `82ec758..HEAD`. Task 4 must read membership through `CuratedMembershipService.GetManagedAppsAsync`, not the raw store. Task 6 must join membership and Off-marker writes to its batch transaction through `SqliteDatabase.AddManagedAppInTransactionAsync`. Continue Tasks 4–15 sequentially with fresh focused implementers and independent task reviews. Final whole-change review must triage M1 and all remaining findings; do not merge/deploy/install/UAC/update apps automatically.

Task 7/12 handoffs: use `ChocolateyInstallPreflightAuthority.CreateStrict()` for final selected installs/revalidation; bind setup verification to the actual batch/job; enforce fresh setup positive absence at execution revalidation; safely distinguish independently observed optional-provider absence from ambiguous inventory warnings. Preparation ports alone do not establish execution integration. Preserve after-await/last-precheck/pre-UAC cancellation and fresh running checks before each elevated item.

Local checkout: `C:\Users\swigg\Documents\Codex\2026-10-09\task\pc-steward-validation`. Original per-plan ignored ledger: `.superpowers/sdd/curated-manual-2026-10-09/progress.md`; tracked copy: `docs/review/checkpoints/2026-10-09-curated-manual/progress.md`. [Progress matrix](CURATED_MANUAL_PROGRESS.md) records Tasks 1–15. Full diff packages can be regenerated from the ledger's exact commit ranges.

Local .NET host/runtimes are 8.0.31; SDK list is empty, required SDK `10.0.302`/latestPatch unchanged. No successful local/focused run is claimed. Read-only normal-host process observation previously read 332 processes/125 unique names/0 errors; point-in-time only, not an OS lock. Native UI/performance/owner live-use evidence remains unmeasured. No real provider or installed-client commands/network probes, installer/UAC/application close/update/scheduler mutation, system software installation, deployment or merge occurred.

Publication is only confirmed by the controller's verified feature ref, fetched commit and exact tree. The commit containing this handoff is an authorized shutdown snapshot; obtain its exact SHA from Git/PR metadata, never infer it from the Task 2 validation hash above.
