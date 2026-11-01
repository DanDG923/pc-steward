# Task 90 — remediation of the independent review of Task 50 dispatch composition

Independent reviewer: a separate Claude subagent with fresh context, given only the code and the contract. It reviewed `f45f6d9` and `40ceacf` read-only and reproduced findings with a scratch probe outside the repository. Its verdict found one High and five Medium issues; it confirmed Success/RestartRequired reachability, one-dispatch-per-approval, the unawaited owned root, gate lifetime and confinement of the runtime-limit exception. A fresh repeat of the same review follows this remediation.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | High | UAC denial recorded as `Failed` (AC-UPD-05, ADR-025 distinct denial); refusal collapsed into failure | Fixed. Appended `UpdateDispatchOutcome.ElevationDenied` and `Refused`, and mapped both for NotStarted and settled observations. History text added. Tests updated. |
| 2 | Medium | Started-then-timed-out/failed settled work recorded as `Failed` ("not updated") despite possible partial effect | Fixed. Started `TimedOut`/`Failed`/`Cancelled` now record `Unverified`; only `ProviderFailed` or a non-zero exit without a restart report records `Failed`. The history `Failed` text no longer claims "not updated". |
| 3 | Medium | Restart only reachable after exit 0; 3010 locked to `Failed` | Fixed within Task 50 scope. A typed `RestartReported` observation flag (set by a route adapter's fixed table) triggers verification. RestartRequired still needs the verifier's reliable restart-pending state, otherwise the result is `Unverified`. The reliable-restart classification itself remains Task 51. |
| 4 | Medium | Diagnostic over 4096 chars made the terminal constructor throw, so even a proven refusal stayed `DispatchClaimed` and later became a permanent Uncertain barrier | Fixed. Observation diagnostics are bounded to 2048 chars (the wire bound) and the terminal message to 4000 after sanitizing. Test covers a 20,000-char diagnostic for both NotStarted and started outcomes. |
| 5 | Medium | A claim committed without a permit (post-commit expiry/stale evidence/cancellation; ownership loss stays a barrier, see F3) was lost and recovered as false "invocation uncertain" | Fixed. New `UpdateDispatchAdmission.ClaimAsync` returns `UpdateDispatchAdmissionResult(Permit, CommittedClaim)`; the workflow finalizes such a claim `Cancelled` with a NotInvoked settlement. `TryClaimAsync` keeps its prior contract. Three-case test. |
| 6 | Low | State lookup failure reported "No update was started" | Fixed. A lookup failure is reported as `NotStartedClaimRetained`. |
| P1 | Low (plausible) | A gate-exit exception after work could relabel a recorded result | Fixed. The work result is captured and returned if the gate fails while exiting. |
| P2 | Low (plausible) | Store accepted `Unverified` with NotInvoked settlement | Fixed. The store now requires an owned-work-stopped settlement for `Unverified` too. Test added. |
| T | Low | Composition doc overstated workflow tests (recomputed digest, new-route disclosure); no Chocolatey-unreachable or unreconciled cases | Doc wording corrected. Added Chocolatey-route-unreachable and unreconciled-ownership tests. Recomputed-digest mutation remains covered by the Domain/Application operation and claim suites, not this workflow suite. |

Linux focused dispatch/claim/review/history: **124/124**. Windows CI on the pushed head is the authoritative gate.

## Fresh repeat review (Fable 5.1, pinned `f61b924`) and second remediation

A separate fresh-context reviewer, on a different model, repeated the review against the pinned snapshot and confirmed HEAD was unchanged before and after. It **verified** fixes 1, 2, 6, P1 and P2; 3 and 5 were verified with caveats; and 4 was **incomplete**.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| F1 | High | Char-index truncation split surrogate pairs, and `Sanitize` kept lone surrogates. Strict UTF-8 terminal encoding then threw, so a proven outcome stayed `DispatchClaimed`: a permanent barrier from one emoji at the boundary | Fixed at the root. `SanitizedDiagnosticBundle.Sanitize` replaces unpaired surrogates with U+FFFD and truncates on pair boundaries. A new `Truncate` keeps pairs whole and is used for observation (2048) and terminal (4000) bounds. The terminal constructor additionally validates strict UTF-8 encodability. Tests: lone high/low surrogate and astral characters at both bounds, NotStarted and settled. |
| F2 | Medium | `RestartReported` was trusted for any exit code | Fixed. It is honoured only with exit 3010 or 1641; any other non-zero exit is Failed without verification. Test with exits 1, 2 and -1. |
| F3 | Low | The fix-5 table claimed "lost ownership" coverage; if ownership lapses after commit, finalization is refused and the row stays a barrier | Documented limit. This conservative behaviour is intended: without current ownership nothing may write, so the claim remains for reconciliation. Fix 5 covers expiry, stale evidence and cancellation only. |
| F4 | Low | History accepted terminal rows the store could never write (e.g. Success with NotInvoked) | Fixed. The reader applies the store's write invariants; violating rows are integrity failures and blocking. Wording clarified: an unkeyed digest detects corruption only, not forgery by a same-user writer. Five-case test. |
| F5 | Low | Stale step-7 doc wording; Unverified history text; misleading message when only route support flipped | Fixed. |

Linux focused 152/152; full suites keep failure sets identical to the Windows-green baseline.

## Third fresh review (Opus, pinned `97c7b6d`) and third remediation

There were no Blocker or High findings. F1, F2 and F4 are verified, and the shared `Sanitize`/`Truncate` change has no regression in legacy callers. Windows CI on `97c7b6d`: 983/983, zero warnings.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| M1 | Medium | AC-UPD-07: exit 3010/1641 with the version detected was recorded as **Success** | Fixed. A restart-reported run is never Success: RestartRequired if the verifier sees a reliable restart-pending state, otherwise Unverified (the wording notes when the version was detected). Tests updated (four cases). How "version detected but restart requested" should eventually be presented is an owner/Task 51 question. |
| L1 | Low | History labelled a corrupt **Final** row as "blocking" although the store does not block on it | Fixed. `BlocksOtherUpdates` mirrors the store barrier (non-Final state); integrity failures say the outcome cannot be trusted. The tamper test asserts agreement with `HasUnresolvedDispatchesAsync`. |
| L2 | Low | Store/history accepted impossible combinations (Cancelled after start, exit code with NotInvoked, terminal before claim); Failed-with-NotInvoked text implied something ran | Fixed in both places. The store rejects Cancelled without NotInvoked and an exit code with NotInvoked. History additionally rejects a terminal earlier than the claim, and shows Failed+NotInvoked as "nothing was started". The settlement `Facts` field remains display-only. |
| L3 | Low | History `LIMIT` could hide an old blocking row | Fixed. Non-Final and unreadable-state rows are ordered first. Test added. |
| L4 | Low | Dispatcher contract traps: started work with a null exit recorded as Failed; a throwing second `Supports` after permit consumption left a false barrier | Fixed. Null exit on settled started work is Unverified; a `Supports` exception counts as unsupported and finalizes Cancelled/NotInvoked. **Kept conservative:** a NotStarted observation carrying an exit code is not proof of non-invocation and stays Uncertain. Adapters must report UAC denial as NotStarted without an exit code (documented contract). |

Linux focused 159/159. Failure-set diff against the Windows-green baseline shows only the new Windows-only `DataOperationLeaseTests`.

## Fourth fresh review (Fable 5.1): gate conversion `af41a0c` and third remediation `c037576`

There were no Blocker or High findings. The bootstrap ordering, thread affinity, the read/mutation classification of every MainWindow call and the same-thread Local v1 re-entrancy inside FactoryReset were all verified. Third-round claims M1 and L1–L4 are **verified**. Windows CI: `38314b6` green (the one `af41a0c`/`c037576` failure was the test assumption fixed in `38314b6`).

| # | Severity | Finding | Disposition |
|---|---|---|---|
| G1 | Medium | "No recursive acquisition" was convention only: a fresh gate handle on the owning thread re-enters silently | Fixed. `CurrentUserDataOperationSession` keeps a per-thread held count and refuses any nested acquisition on a thread that already holds a session. Test added. |
| G2 | Low | Gate-open `Win32Exception`/`UnauthorizedAccessException` could escape UI handlers that catch only `InvalidOperationException` | Fixed centrally. `DataOperationLease` wraps gate-open failures (Win32, access, IO) as a handled fail-closed refusal. Three-case test. |
| G3 | Low | Second marker recheck after reconciliation was untested | Test added. |
| G4 | Low | `RestartReported` with exit 0 was silently ignored and could still yield Success | Fixed. The inconsistent report is Unverified without verification. Test added. |
| G5 | Low | Sync-over-async reconciliation on the UI thread is safe only because the dispatch store uses `ConfigureAwait(false)` and SQLite completes synchronously | Recorded limit. It matches the pre-existing `MigrateAsync` bootstrap pattern. A future genuinely asynchronous reconciliation step must move bootstrap reconciliation to the dedicated-thread path. |
| G6 | Low (by design) | An Uncertain barrier, never auto-cleared and with no explicit-reconciliation UI yet, also blocks factory reset from the UI | **Owner note.** This matches the accepted entry-assessment decision (Arm needs proof of no unresolved claim). Safe explicit reconciliation remains open work. |
