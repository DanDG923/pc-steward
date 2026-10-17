# Task 3 independent review

Reviewer: GPT-6-Astra (High), independent of the implementer (Claude Opus 5.5). Read-only review of `817cb57..82ec758`.

Status: **CHANGES REQUESTED** for `82ec7588c25ca7cbf216b14ffb2d27455b78aa05`.

- **Findings: 0 Critical / 1 Important / 1 Minor.**
- The migration, transactional membership writes, Off override and data-control changes are sound within Task 3's scope. Removal's association predicate can delete another package's saved opt-in.
- Requirements reviewed: **CRD-01/04/11; FR-CAT-04/06; FR-AUT-01; FR-RSC-01–05**, with the approved design §2, Task 3 and Task 4/6 contracts.

## Important I1 — Removal can delete a different package's automatic-update opt-in

**Location:** `src/PCSteward.Application/CuratedMembership.cs:104–108`; deletion at `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs:130`. Supporting behavior: `src/PCSteward.Catalog/CatalogDetector.cs:37,59–74`.

**Concrete scenario:** The catalog entry `pcs:widget` identifies WinGet package `Contoso.Widget`, with anchored name/publisher patterns matching `Widget` and `Contoso`. Inventory contains a different package, `Contoso.Widget.Copy`, from the `winget` source, with that same name and publisher and its own saved `AutomaticUpdates=On`.

The different package ID fails identity matching, but `CatalogDetector` falls back to the name/publisher patterns and returns `NameAndPublisher`. Its `IsExact` property is true. The new removal predicate accepts that result, includes the different package's product key, and deletes its saved On decision when `pcs:widget` is removed.

Grouping by product key does not prevent this: the unrelated product has one match under its own key. The existing `RemovalDerivesOnlyExactCurrentProductKeys` fixture sets the near-match publisher to **null**, avoiding this path.

**Consequence:** Removing one managed app destroys another package's saved opt-in without a unique exact identity association. This does not authorize execution, but violates §2's association boundary and Task 3's requirement to preserve other products' decisions.

**Suggested fix:** Use an association predicate appropriate for policy mutation that requires unique identity evidence and rejects contradictory provider/source/package identities. Do not rely on the broader catalog-detection `IsExact` alone. Add a regression retaining `Publisher = "Contoso"` on the different-ID fixture, asserting its On row survives while the genuinely associated product's On row is removed.

This finding follows directly from source inspection; the reviewer did not execute a reproduction. The coordinator confirmed the fallback path in `CatalogDetector.Detect`.

## Minor M1 — Download-preservation assertion was narrowed unnecessarily

**Location:** `tests/PCSteward.Application.IntegrationTests/SqliteDatabaseTests.cs:338–339`.

The null-hash explanation is correct: `SaveDownloadedArtifactAsync` passes the hash through `Text`, which converts null to empty text. Repairing the original expected record was justified.

However, comparing only `(Id, RelativePath)` **does weaken the test**. Changes to size, category, timestamp, hash, job association, outcome or active status would now pass despite the preservation claim.

**Suggested fix:** Read the persisted complete artifact immediately before clearing mappings/policies, then compare the complete before/after records. This accommodates existing normalization without dropping preservation assertions.

The production clear operation does not modify downloads; this is a nonblocking regression-coverage finding.

## Verdicts on the implementer's four flagged points

| Point | Verdict |
|---|---|
| No cancellation check after committed write | **Accept.** Reporting cancellation after successful persistence would misrepresent the result. Pre-write checks and transactional failure handling remain necessary and are present. |
| Orphan rows removable only through Clear/reset | **Accept for Task 3.** This follows the explicit unknown-ID mutation rejection contract. Rows remain visible and withheld. Individual restrictive cleanup can be considered separately. |
| Off marker permanently dominates | **Accept.** Later or expired legacy On records cannot override it. A future enable operation must explicitly remove the restriction through its reviewed authority; this task adds no enable flow. |
| Removal uses the system clock | **Accept, nonblocking.** The timestamp does not control Off precedence, expiry or execution eligibility. Injecting the clock would improve deterministic timestamp testing, but no incorrect behavior follows here. |

## Other assessed boundaries

- **Migration:** v11 adds an empty table without scan-row foreign keys inside the migration transaction. Tests cover repeated migration, retained logical records and rollback when recording version 11 fails.
- **State separation:** membership operations introduce no installer, uninstall, queue or execution calls. Add preserves legacy decisions and writes the Off marker atomically. Removal retains installed inventory, restrictive decisions, jobs and outcome history.
- **Untrusted rows:** malformed timestamps, unknown IDs, rejected user entries and the reserved setup entry remain visible but withheld through the service.
- **Data controls:** scan clear preserves membership/restrictions; mappings/policies clear removes and discloses them; reset verification counts membership. No filesystem roots or deletion authority were added.
- **Safety/layering:** new production SQL values are parameterized; interpolated table names remain internal constants. No command construction, execution authority, dependency change or SQLite type in Application was introduced. Existing architecture guards were not weakened.
- **Forward compatibility:** Task 6 can call the private transaction helper from its persistence implementation inside `SqliteDatabase`. Task 4 can consume the validated membership read service. Neither later integration is already delivered.
- **Concurrency/cancellation limits:** tests cover active-work transitions during catalog/inventory awaits, pre-cancelled writes and injected SQL rollback. They do not demonstrate simultaneous mutations or cancellation during an already-started transaction. The externally held data-operation lease remains Task 12 composition work.
- **Documentation:** STATUS, TRACEABILITY and PROGRESS accurately describe code/CI success with independent review pending. Their exact-association claim requires I1's repair; no completed automation or desktop integration is established.

## Independently verified evidence

The checkout was clean on the requested branch at the pinned HEAD, and GitHub draft PR #3 reported the same head.

| CI run | Commit | Verified result |
|---|---|---|
| [38004192067](https://github.com/DanDG923/pc-steward/actions/runs/38004192067) | `817cb57` | Clean build; **681 passed / 22 failed / 0 skipped**. All 22 failures have assertion-failure headers: Architecture 1, Application 21. |
| [39079096574](https://github.com/DanDG923/pc-steward/actions/runs/39079096574) | `12310e1` | **703 passed / 0 failed / 0 skipped**, zero build warnings/errors. |
| [39079525983](https://github.com/DanDG923/pc-steward/actions/runs/39079525983) | `82ec758` | **703 passed / 0 failed / 0 skipped**, zero build warnings/errors. |

Both green runs report Architecture **39**, Catalog **97**, Domain **87**, Security **72**, Application **408**.

No files changed by the reviewer; no local builds/tests, commits, pushes, branch changes or application actions performed.
