# Factory reset and advanced data-control design

**Status:** Accepted design gate
**Decision:** ADR-013 Option B — fixed next-launch cleanup marker, no reset helper
**Requirements:** `FR-DAT-04`, `FR-DAT-12–15`, `AC-DAT-12–16`, `AC-DAT-18`, `AC-UX-02`, `AC-UX-04`, `AC-UX-07–08`

## 1. Scope and invariants

Factory reset returns the current Windows user's PC Steward state to first launch. It is not an uninstaller, Windows restore, registry cleaner, provider-cache cleaner, or application uninstaller.

The implementation must:

- run as the current standard user without UAC;
- derive `%LocalAppData%\PCSteward` from the Windows known-folder API plus literal components;
- accept only a compiled operation/category enum, never a path, command, package identifier, database value, log value, or user string;
- preserve PC Steward binaries, installed third-party applications, user-chosen exports, and Windows/WinGet/Store/other provider caches;
- use the production handle-relative, non-following deletion primitive for every file-system category;
- keep the reset marker until every category and the exact scheduled task are verified absent; and
- block all old state from loading whenever the marker exists, is malformed, or represents an incomplete attempt.

No security decision remains open. Task 28 must implement and prove this design before reset is enabled.

## 2. Current-state evidence and mechanism choice

`SqliteDatabase` uses `Pooling=false` and opens/disposes a connection inside each operation. The bounded scan and update event sinks are memory-only. `DownloadedArtifactCache` holds a semaphore but no persistent file handle. The Task Scheduler adapter starts a bounded `schtasks.exe` process and holds no scheduler handle.

Those facts make cleanup after process exit practical, but do not make in-process reset preferable:

- `App.OnLaunched` currently creates the state directory and migrates SQLite before the window is composed.
- The one-shot scheduled host independently creates/migrates the same database.
- UI, provider, update, and scheduled-host continuations can race a delete unless all state consumers share one lease.
- A process-lifetime or future logging change could reintroduce a held handle.

The accepted path is therefore a fixed marker written after the second confirmation. The desktop then exits without reopening state. On the next desktop launch, reset bootstrap runs before SQLite, logging, downloads, settings, connectors, providers, or the normal shell are composed. The scheduled host performs the same marker preflight before opening SQLite and exits without scanning when reset is pending.

An in-process reset is rejected because it cannot as simply prove that no old object was reopened after deletion. An unelevated helper is rejected because it adds executable/protocol/process authority without improving the next-launch guarantee.

## 3. Authority, roots, and manifest

### 3.1 Fixed technical authority

| Authority | Exact value |
|---|---|
| Per-user root | `%LocalAppData%\PCSteward` from the known-folder API and literal `PCSteward` |
| Reset control root | literal child `reset` |
| Reset sentinels | literal leaves `pending.v1.json` and atomic-write leaf `pending.v1.tmp` |
| Database | literal root leaf `pcsteward.db` |
| SQLite sidecars | literal leaves `pcsteward.db-wal`, `pcsteward.db-shm`, `pcsteward.db-journal` |
| Download root | literal child `downloads` |
| File-log root | literal child `logs` (may be absent while logs remain memory-only) |
| Scheduled task | `PCSteward.ScheduledCheck` |
| Cross-process lease | `Local\PCSteward.DataOperation.v1` |

The marker contains only a schema version, reset ID, requested UTC time, attempt count, fixed state enum, and fixed category/result enums. It contains no paths or deletion authority. Presence of either literal sentinel is authoritative even when its content is malformed. A crash that leaves only the flushed temporary leaf therefore resumes the fixed reset rather than loading old state; malformed or unknown-version content produces the blocking recovery state and never permits normal startup.

### 3.2 Deleted categories

| User-visible category | Physical/logical contents | Completion proof |
|---|---|---|
| Current and historical scan data | `canonical_products`, `installation_instances`, `raw_provider_records`, `scan_provider_timings`, `scan_runs`, `update_candidates`, scan metadata | Database artifact group absent |
| Source metadata | `source_snapshots`, `source_package_cache` and source freshness metadata | Database artifact group absent |
| Update jobs and history | `update_jobs`, `approved_update_jobs`, `update_job_history`, `update_job_execution_state` | Database artifact group absent |
| Mappings, pins, ignores, and decisions | `manual_mappings`, `stable_decisions` | Database artifact group absent |
| Settings and consent | connector state/disclosure approval, schedule/notification settings/status, retention/UI settings and all other `app_metadata` | Database artifact group absent |
| Download ledger | `downloaded_artifacts` and download cleanup metadata | Database artifact group absent |
| SQLite store | main database and the three compiled sidecar leaf names above | Every literal leaf absent |
| App-owned downloaded files | every non-reparse descendant of the fixed `downloads` root, including payloads and extraction files; active-file exclusions do not apply after the exclusive lease proves no active job | Safe walk reports no descendant or unsafe residual; root absent or empty |
| Logs | every non-reparse descendant of the fixed `logs` root; current in-memory events end with the desktop process | Safe walk reports no descendant or unsafe residual; root absent or empty |
| Current-user schedule | exact task `PCSteward.ScheduledCheck` only | Exact-name query reports absent |
| Reset control state | `reset\pending.v1.json`, its same-directory atomic-write temporary leaf, and then empty `reset` directory | Removed only after all preceding proofs pass |

The database is one physical transaction boundary for several user-visible categories. The UI still lists every logical category so a user is not misled by the storage layout.

An unclassified object directly under the owned root or an unsafe/reparse object under a compiled child is a residual, not permission to widen the manifest. It produces Partial. Adding another persistent root or leaf requires an ADR-013/ADR-019 manifest update before that artifact ships.

### 3.3 Preserved categories

| Preserved | Boundary |
|---|---|
| PC Steward standalone binaries/package files | Extracted package root, never `%LocalAppData%\PCSteward` |
| Installed applications, runtimes, and packages | Provider/Windows ownership; reset never invokes uninstall/update |
| Windows, WinGet, Microsoft Store, and other provider caches | Outside the app-owned roots; no provider cleanup adapter is called |
| User-chosen inventory or diagnostic exports | Picker-selected paths are never registered as deletion authority |
| Windows settings, registry, documents, browser data, and arbitrary temp content | Outside the compiled manifest |

The temporary WinGet export used during a live query remains bounded provider-process scratch and is removed by that operation. Reset never enumerates or deletes arbitrary OS temporary paths.

## 4. State machine and ordering

### 4.1 States

`Ready -> ConfirmingScope -> ConfirmingPhrase -> Arming -> ResetPending -> Cleaning -> Verifying -> Complete`

Any failure before the marker's atomic commit returns to `Ready` with no deletion. Any failure after that commit becomes `Partial`; `Partial -> Cleaning` is an idempotent retry. There is no transition from `ResetPending` or `Partial` to the old application state.

### 4.2 User-process initiation

1. Expose reset only when no scan, source refresh, update, verification, cleanup, scheduled scan, or other data operation is active.
2. If work is active, keep reset disabled and direct the user to its existing Cancel action. Cancellation must reach a terminal state and release all provider/helper/process and artifact leases before reset can be retried.
3. Show the scope-review dialog. Closing it is required before opening the phrase dialog.
4. Require exact ordinal, case-sensitive `RESET`. The final action has no access key, is disabled before the exact match, and is never the default/Enter action.
5. Recheck and acquire the exclusive cross-process data-operation lease. Failure to acquire it changes nothing and reports that work must finish.
6. Resolve and validate the fixed root without following reparse points. Create-new and flush `pending.v1.tmp`, then atomically rename it in the same directory to `pending.v1.json`. A handled failure before the rename removes the temporary leaf or fails closed if it cannot; a crash leaving either sentinel is resumed at bootstrap. No schedule or user-data deletion precedes a sentinel.
7. Once the marker commit succeeds, do not call a database, log, cache, provider, connector, settings, or normal-shell API again. Show the bounded pending message and close the desktop, releasing the lease.

Holding a lease during the two dialogs is deliberately avoided; confirmation may remain open indefinitely. The mandatory recheck at step 5 closes that race.

### 4.3 Bootstrap cleanup

The desktop bootstrap and scheduled host check both sentinel leaves before creating the root or opening any store. The scheduled host exits when either is present.

The desktop reset bootstrap:

1. acquires `Local\PCSteward.DataOperation.v1`; abandoned ownership is treated as available but recorded;
2. validates the known-folder root, marker, and every compiled target without consulting SQLite;
3. idempotently removes the exact scheduled task;
4. deletes the `downloads` safe walk, child-first;
5. deletes the `logs` safe walk, child-first;
6. deletes SQLite sidecars, then `pcsteward.db`;
7. verifies the exact task is absent and every compiled target is absent/empty with a fresh non-following walk;
8. records any fixed-category residual in the marker and shows Partial without composing old state; or
9. removes the marker/atomic temporary leaf and empty reset directory, releases the lease, creates a new migrated database, verifies default settings and zero prior-state counts, and enters onboarding.

All categories are attempted after a category failure so the residual report is complete. A schedule failure does not authorize loading old data.

### 4.4 Transaction boundaries

- Marker atomic commit is the point of no return. Before it, no destructive work occurs. After it, safe recovery is retry-to-completion, not rollback.
- Each advanced database control runs in one SQLite transaction: all selected tables/metadata commit together or the previous valid database remains.
- Each file-system object deletion is individually atomic by bound handle; the overall walk is not rollbackable and therefore reports Success/Partial with residuals.
- Reset deletes the closed database artifact group rather than issuing row deletes. Its durable transaction record is the marker.
- Marker progress replacement happens after each category result and before a retry UI is shown. A crash before progress persistence is safe because absence is rechecked on the next idempotent attempt.

## 5. Active-work and concurrency rules

Task 28 must make the desktop scan/source-refresh/update/verification/data-control paths and the scheduled host participate in the same cross-process lease. The current in-process flags remain presentation state, not the authority.

- Read/write jobs take a shared application-operation lease represented by the coordinator; reset requires its exclusive state plus the named mutex.
- The scheduled host takes the named mutex before opening SQLite and releases it on every exit.
- Final confirmation never cancels work implicitly. The user cancels from the active-work surface, observes its terminal result, and starts reset again.
- A started elevated/non-elevated update, child provider, post-update verification, or active download artifact prevents reset until its process and lease have ended.
- A second PC Steward desktop or scheduled host that sees the marker must not migrate/open the database or show normal state.

The named mutex is coordination, not deletion authority. Failure, abandonment, or hostile same-user denial cannot widen scope; it can only fail closed or delay reset.

## 6. Locked files, partial failure, and safe recovery

Deletion uses bounded immediate retry for transient sharing violations. Remaining items use fixed reason codes such as `LockedOrInUse`, `AccessDenied`, `UnsafeReparsePoint`, `UnexpectedOwnedObject`, `ScheduleRemovalFailed`, and `VerificationFailed`. User-facing details contain category plus sanitized relative path only.

`Partial` means:

- the marker stays present;
- onboarding and the normal shell stay unavailable;
- no SQLite migration, log sink, connector, provider, notification, or network request starts;
- the screen announces the blocking state assertively and lists what remains;
- `Try again` repeats the full idempotent manifest;
- `Exit` is always safe; and
- guidance may ask the user to close the named application holding a file or restart Windows, but must not offer elevation, force-close, arbitrary-path deletion, or a false Undo.

The design does not register arbitrary delayed-delete paths with Windows. The marker is the bounded next-launch retry queue. Already-absent categories count as success.

## 7. First-launch verification and safe failure

Onboarding is permitted only after all of these are true in one verification pass:

- exact scheduled task absent;
- database main/sidecar files absent;
- download and log roots absent or safely empty;
- no unsafe/unclassified compiled-root residual;
- reset marker successfully removed;
- a fresh database migrates successfully with zero inventory, source cache, update history/jobs, downloaded-artifact ledger, mappings, decisions, consent, or schedule state; and
- default connector/schedule settings are observed.

Failure to create the fresh store after completed deletion shows the existing local-store-unavailable state; it cannot expose prior data. The application binaries are outside the manifest and remain installed.

## 8. Advanced data controls

Routine actions remain independent; there is no `Clear all` shortcut.

| Control | Transaction/deletion | Preserves |
|---|---|---|
| Clear source metadata | One SQLite transaction deletes `source_package_cache`, `source_snapshots`, and source freshness metadata | Inventory, connector enablement/consent, schedule, jobs/history, downloads, decisions |
| Clear logs | Safe walk of fixed `logs`; current memory-only events end with the process and are not represented as recoverable storage | Update history, inventory, settings, decisions, downloads |
| Clear update history | One SQLite transaction deletes `update_job_history`, terminal `update_job_execution_state`, and history timestamps; unavailable while any update/verification is active | Approved/current job authority needed by active work, inventory, settings, decisions, downloads |
| Clear mappings and policies | One SQLite transaction deletes `manual_mappings` and `stable_decisions` | Inventory, source metadata, update history, settings, consent, downloads |

Every action has its own Deletes/Preserves review, Cancel default, active-work exclusion, idempotent result, and accurate Partial state. A mixed database/file control must report its boundaries separately and must not roll back a completed file deletion by implication.

## 9. Native UI contract

The **Data & reset** page uses `DataCategoryCard` grouping for Scan data, Downloaded update files, Source metadata, Logs & update history, Mappings/pins/ignores, and Settings & consent. Each card shows count/size where meaningful, last updated/cleaned, contents, preservation summary, action availability, and busy/partial state. Logs and update history may share a visual group but retain separate actions.

Factory reset is a bordered danger zone after all routine controls. It visibly states:

- Deletes: every category in section 3.2 and the current-user scheduled task.
- Preserves: every category in section 3.3.
- Requirement: no active work.
- Result: PC Steward closes; next launch verifies cleanup before onboarding.

The two dialogs follow this order:

1. scope review with `Continue to final confirmation` and default/focused `Cancel`;
2. `This cannot be undone`, exact `RESET` input, and disabled `Factory reset PC Steward` danger action until the match.

The first dialog closes before the second opens. Escape cancels. Enter in the phrase field does not trigger reset. Cancel or closing the second dialog restores focus to the invoking danger-zone button; after successful arming the pending state receives focus instead.

Partial reset is a blocking page, not a success dialog. It uses visible text/icon plus an assertive live-region announcement, sanitized category/reason details, `Try again`, and `Exit`. It never renders the normal shell behind the error.

At 200% text/display scaling, cards become one column; dialog bodies scroll while titles/actions remain visible; Deletes/Preserves and the final action remain reachable without horizontal page scrolling. High contrast uses system colours, 2 px essential boundaries, visible status icon/text, and focus distinct from selection. These are implementation obligations under Optional best-effort `AC-UX-02/04/07–08`, not evidence claims at this design gate.

## 10. Standalone package removal

Standalone package removal and factory reset remain separate operations:

- the bundled cleanup script refuses to proceed while the desktop or scheduled host is running and removes only the exact `PCSteward.ScheduledCheck` task;
- deleting the extracted package folder removes the binaries;
- per-user data is retained by default across package replacement;
- the explicit remove-data choice is the existing two-confirmation **Factory reset PC Steward** action, run before closing and deleting the package;
- the cleanup script never duplicates the application’s path-deletion logic and never deletes the per-user root; and
- factory reset never removes package binaries.

Task 30/30A must prove the standalone removal sequence, task-name isolation, retain-data replacement and remove-data factory-reset result on the approved test machine.

## 11. Task 28 implementation gate

Task 28 is not complete until tests prove:

- marker preflight precedes every SQLite/log opening in both executables;
- malformed/unknown marker blocks old state;
- every logical and physical manifest category, exact schedule, and preserve item;
- active UI scan, scheduled scan, source refresh, update/helper, verification, cleanup, and second-process races;
- crash before/after marker commit and after every cleanup category;
- locked file, access denial, schedule failure, idempotent retry, and fresh-store verification;
- relative/absolute/UNC/device/prefix/ADS/root-equality and metadata-injected path denial;
- junction, file/directory/root symbolic link, mounted scratch volume, final-component swap, and ancestor swap under approved disposable roots;
- no outside sentinel deletion and no provider-cache/export/binary deletion;
- two dialogs, exact phrase, Cancel/default/Enter/focus/live-region behavior; and
- 100/150/200% scale plus supported system high-contrast checks as best-effort UI evidence.

The reset control remains disabled until those implementation and hostile-fixture tests pass.
