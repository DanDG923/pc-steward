# Task 3 report — additive membership persistence and restrictive removal

Status: **Code and Windows CI passed; independent review pending.** This implementer makes no task-completion claim. Task 4 has not started.

Requirements: CRD-01/04/11; FR-CAT-04/06; FR-AUT-01; FR-RSC-01–05.

Range: `817cb57` (expected-red tests) → `5e36d0c` (production) → `12310e1` (one test fixture repair). Base before the Task 3 draft: `54eab743`.

## Expected-red verification

Windows CI [38004192067](https://github.com/DanDG923/pc-steward/actions/runs/38004192067) at `817cb57`: restore/build succeeded with no compile errors. Domain 87, Catalog 97 and Security 72 passed. Architecture failed 1 of 39 and Application failed 21 of 408, so 22 failed in total. I checked every failure in `--log-failed`. Each one is a genuine assertion failure (`AreEqual`, `IsTrue`/`IsFalse`, `HasCount`, `ThrowsExactlyAsync` "no exception was thrown", `StringAssert.Contains`) against the inert seams. None was a crash, a compile error or a setup exception.

- Architecture: `AdvancedMappingsClearDisclosesMembershipAndAutomaticRestrictionsBeforeConfirmation` failed because the disclosure lacked "managed app membership".
- `CuratedMembershipTests` (19): migration v11 missing (`Expected:<11> Actual:<10>`, no rollback exception), no stored rows, removal no-op, no active/invalid/reserved rejection, no Off dominance, malformed rows invisible.
- `FactoryResetTests.FreshStoreVerificationRejectsMembershipWithoutOtherPriorState`: the fresh check ignored `managed_apps`.
- `SqliteDatabaseTests.MappingsAndPoliciesClearIncludesMembershipAndRestrictiveOverrides`: the clear left `managed_apps`.

One test fault was hidden behind the first failing assertion. `MappingsAndPoliciesClearIncludesMembershipAndRestrictiveOverrides` compared a whole `DownloadedArtifact` record. Existing persistence stores a null `Sha256` as empty text, so that record can never round-trip equal. It surfaced only after the production change, in CI [39078808118](https://github.com/DanDG923/pc-steward/actions/runs/39078808118): 702 passed, 1 failed. Commit `12310e1` now compares `(Id, RelativePath)`. The test still checks the same thing: downloads are untouched. I changed no other test.

## Implementation

- `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs`
  - **Schema v11:** migration v11 adds `managed_apps(catalog_entry_id TEXT PRIMARY KEY, added_at TEXT NOT NULL)` inside the existing migration transaction. It has no foreign key and starts empty; nothing is inferred.
  - **Reads:** `GetManagedAppsAsync` returns raw rows. A tampered type or a NULL id becomes empty text, and an unparseable time becomes `DateTimeOffset.MinValue`. Neither throws nor disappears.
  - **Add:** `AddManagedAppAsync` uses `INSERT OR IGNORE`, so the original timestamp is kept. In the same transaction it writes a `CuratedAutomaticUpdatesOff`/`Off` stable decision unless one already exists.
  - **Remove:** `RemoveManagedAppAndDisableAutomaticAsync` runs as one transaction. It deletes the membership. It deletes `AutomaticUpdates=On` rows only for the exact catalog key plus the keys it was given. It then makes sure the Off marker exists. Pins, skips, ignores, other products, history, jobs, downloads and inventory are untouched.
  - **Cancellation:** a pre-cancelled token throws `OperationCanceledException` before anything opens. Any failure rolls back both changes.
  - **Clear and reset:** "Clear mappings and policies" now also clears `managed_apps`, and its result message names membership and restrictions. `IsFreshResetStoreAsync` counts `managed_apps`. Factory reset already deletes the database file, and the existing reset test proves a fresh store is empty. I added no filesystem roots.
- `src/PCSteward.Application/CuratedMembership.cs`
  - **Marker constants:** `CuratedAutomaticUpdateRestriction` holds the kind and value.
  - **Every call:** `CuratedMembershipService` reads a freshly validated, composed catalog on each read and write. Matching is exact and ordinal. `pcs:chocolatey-setup` is reserved, and a null or invalid catalog rejects all mutations.
  - **Guards:** cancellation is checked after every await. The active-work guard runs before and after the catalog await, and once more just before the store write.
  - **Removal keys:** removal works out policy keys itself, using only exact, unconflicted current matches that name only this entry.
  - **Reads:** orphan, malformed, reserved and rejected-user rows are returned with `Entry = null` and a withheld reason.
- `src/PCSteward.Application/UserDecisionService.cs`: any Off marker forces `AutomaticUpdates` false for its exact key, whatever its time or expiry.
- `src/PCSteward.Desktop/MainWindow.cs`: the advanced-clear disclosure now says it deletes "managed app membership and automatic-update restrictions". The existing Cancel-default dialog is unchanged.

## Coordinator inputs

1. **Off marker:** delivered as `CuratedAutomaticUpdatesOff`/`Off`.
2. **Joining a Task 6 transaction:** membership and the Off marker are written by the private static `AddManagedAppInTransactionAsync(SqliteConnection, SqliteTransaction, …)`. The public method only opens the transaction and calls it. Task 6's batch persistence lives in the same `SqliteDatabase` class, so it can call the helper inside its own batch transaction without a SQLite type reaching the Application port.
3. **Read entry point for Task 4:** `CuratedMembershipService.GetManagedAppsAsync`, which returns `CuratedMembershipEntry` and is documented as the read entry point. Task 4 should use it, not the raw store.
4. **Clear and reset:** both are covered, with no new roots.

## Validation

Final [Windows CI 39079096574](https://github.com/DanDG923/pc-steward/actions/runs/39079096574), job `117532216292`, head `12310e1`. Canonical Restore, Build and Test all succeeded with 0 warnings and 0 errors. Architecture 39, Catalog 97, Domain 87, Security 72, Application 408: **703 passed / 0 failed / 0 skipped**. The local SDK list is empty, so no local run is claimed. No real provider, installer, UAC, scheduler or owner-data action took place.

## Self-review and open points for the reviewer

- I reviewed `git diff 817cb57..HEAD`. All SQL is parameterised; the table names are constants. No path, command or process construction was added, and no new dependencies.
- The service does not re-check cancellation *after* a committed write. That is deliberate: reporting a cancellation after the commit would misstate what was saved. The last check comes just before the write.
- An orphan or invalid-catalog membership row cannot be removed one at a time, because the approved tests require mutations to reject unknown IDs. It stays visible but withheld until "Clear mappings and policies" or Factory reset. Decide in Task 4 or Task 13 whether to add a restrictive orphan removal.
- The Off marker dominates permanently. Before any later task offers "turn automatic on" for a managed app, it must remove the marker explicitly. Task 3 adds no enable path.
- The removal marker uses the wall-clock `UtcNow`; the add marker uses the service clock.
- Commits used the branch's existing `DanDG923` noreply identity, supplied through per-process environment variables only. No Git config was changed.
- Minor M1 from Task 1 stays deferred to Task 15.
