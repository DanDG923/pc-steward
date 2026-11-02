# Task 47 — Catalogue, detection and operation-history decisions

**Status: All listed policies and final choices owner-Accepted, 2026-10-29.** The owner first answered **“Accept these defaults”** to catalogue/progress, existing-warning and Q5 policies, then **“Accept the corrected ADRs and final choices”** after both requested Opus 5.5 reviews and disclosed final N1–N6 author follow-ups. This explicitly accepts ADR-023–025, ADR-026 Option C (Defer), 30-minute review dispatch window, separate two-minute helper freshness, exclusive cross-session gate and new install/uninstall 10-minute deadline plus at most five seconds teardown attempt. Accepted ADR-027–029 remain authoritative for diagnostics, AI and accounting. No provider query or host action follows.

## Q1 — Accepted catalogue policy v1

| Boundary | Accepted value / behavior |
|---|---|
| Invocation | Explicit manual Search only; no typeahead, background refresh or automatic retries. Query enabled, consented, predefined WinGet catalogues only; Store remains information-only. |
| Query | 1–200 Unicode scalar values after trim; reject controls/invalid Unicode. Empty input does not enumerate a catalogue. |
| Count/concurrency | At most 100 matching candidates examined across the request, including duplicates/rejected items; one search at a time across current-user app processes. No automatic pagination. Count a candidate before projecting it; disclose omitted/truncated/unknown counts honestly. |
| Payload | At most 1 MiB of complete projected UTF-8 response, including coverage/status envelope, and 8 KiB per result. Reserve envelope space first. A supported source-side query/enumeration must itself be bounded; an output cap does not prove bounded COM/native allocation. Unsupported bounded enumeration blocks that source route. |
| Fields | Exact ID/source/version at most 256 scalars each, name/publisher at most 256 each, optional description at most 2,048, at most 20 tags of 64 each; still subject to byte limits. Oversize authority-bearing fields reject that candidate, never truncate its identity. Optional display text may truncate with a visible label. Missing data remains unavailable; no icon fetch, category inference or installer-URL authority. |
| Time | 15-second monotonic end-to-end deadline; one source at a time sharing remaining time equally. Cancellation acknowledged within 2 seconds and operable UI within 5 seconds, per existing NFR-PERF-08; a reader unable to meet bounded teardown stays unavailable. Results from completed sources may remain explicitly partial. |
| Execution evidence | Search results confer no authority and have no approval lifetime. Fresh exact resolution and complete relevant absence/instance proof are mandatory at review and again immediately before execution. No positive-result cache substitutes for these checks. |
| Progress/helper | Retain the existing 16 KiB maximum frame and two-minute helper-request admission lifetime. This is not a two-minute batch-review expiry. New protocol semantics require a new version and independent endpoint validation under ADR-025. Accepted progress bounds: 256 frames total per item, 1 MiB total encoded progress, 32 pending frames, at most 4 emitted frames/second. Coalesce nonterminal progress; never drop/overwrite terminal outcome. Reject duplicate/out-of-order terminal messages and boundedly cancel on overflow. These are separate from installer lifetime; no indefinite queue or free-form provider output. |

Tests: exact boundary and one-unit-over for counts/scalars/encoded bytes/deadlines; non-BMP/escaping expansion, malicious native enumeration, duplicates, all sources saturated, denied sources, cancellation, stalled COM, source revocation and late results. Search-only partial data never establishes absence or install eligibility. A source can show information while every action remains blocked.

## Q2/Q3 — Accepted existing-warning coverage

Keep existing pin/ignore/deselection semantics. (Superseded for skip-version by the owner decision of 2026-11-02 below: Skip this version is accepted.) Retain the package-derived process-name heuristic as a warning with explicit limitations: up to 4,096 inspected processes and 64-character names, bounded candidate derivation, inaccessible/exited processes and enumeration saturation can miss a running app. It is not proof that software is unused. Keep checks at review and before execution, cancellation/manual closure/explicit continuation and no force-close. Tasks 49/52 must disclose coverage and test a late start, ambiguous suffix, access denial and saturation. No universal executable mapping or process control is promised. This warning policy is owner-accepted; applicable ADR acceptance remains separate.

## Q4 — Capability and host-fixture decision

No WinGet instance-removal or current-user MSIX route is declared supported here. Before Task 60 implementation, prepare the exact supported API/typed-field matrix from primary documentation and synthetic fixtures; record any separately approved host-read evidence. The owner reviews targeting, user/scope/architecture enforcement, dependency/protection exclusions, preservation of other instances and a disposable fixture. Missing proof keeps that route deferred. If every route is blocked, Task 60 records the blocker; Phase E cannot close with mocks alone. D3's 7-Zip candidate does not prove a user-scope route or a safe MSIX fixture. Never remove an existing working installation to create evidence.

## Q5 — Accepted favourites and operation-history lifecycle

| Category | Retention and limits | Explicit clear/reset behavior |
|---|---|---|
| Favourites | Persist until individually removed/reset; at most 500 source/exact-ID preferences, at most 1 MiB serialized logical payload. At capacity, block adding another favourite; never silently evict preferences. No installed-state/approval authority. |
| New install/uninstall history | Preserve until the existing explicit Clear update history control (relabelled **Clear operation history** with listed operation kinds) or Factory reset. No automatic 30-day expiry. Use bounded sanitized typed rows: at most 16 KiB/item, 10,000 new-operation rows or 16 MiB logical payload. Capacity blocks new install/uninstall approval before launch until the user explicitly clears history; no automatic history loss. Existing update-history retention is unchanged. Physical DB/WAL/backup growth needs measurement before shipping. Reserve terminal-record capacity before invocation and handle crash recovery without claiming guaranteed persistence after disk failure. |
| Recent installs | Derived view of the newest 50 verified job-attributed PC Steward install outcomes still in operation history. No separate retention store. Unknown/restart-only/failed/queued/pre-existing results never appear as verified recent installs. A claimed item later found installed without durable job-bound invocation/result evidence is **Installed; PC Steward attribution unknown**, excluded from this view. Re-inventory alone cannot prove who installed it. |

Clear scan data, downloads, source metadata and logs preserve favourites and operation history. Removing one favourite preserves history. Clear operation history deletes the selected operation-history categories and their derived recent-install view, preserves favourites/accounting/keys and **preserves whole frozen diagnostic snapshots and DiagnosticTimeline explanations with an explicit captured-copy warning**, as accepted by ADR-027 R1. It deletes FailedUpdate explanations when their update history is selected. Factory reset deletes all these app-owned categories, the exact protected key and accounting, under accepted reset/continuity rules; it preserves OS records, installed software, provider caches and user exports. No new clear-all control or physical file root.

SQLite migration must preserve legacy update outcomes, no implicit approvals/replays and no scan-owned foreign-key cascade into history/preferences/accounting. Captured diagnostic history is a snapshot-owned sanitized copy; clearing live history never mutates frozen snapshots. History/provenance/content deletion never refunds budget. Update the clear confirmation and existing lifecycle manifest/tests together before shipping.

## Owner decision record

- ADR-023–025: owner-Accepted after both Opus 5.5 reviews and disclosed final follow-ups; ADR-026 owner-Accepted as Option C (Defer).
- Catalogue/progress bounds, warning coverage and Q5 defaults above: owner-Accepted via “Accept these defaults”; any material review correction is presented separately before acceptance.
- Q4 route evidence: implementation prerequisite; unsupported routes remain blocked even after policy acceptance.
- Exact deployment, launch, source refresh, OS-reader access, install, uninstall, credential action and paid test: each retains separate action-time authority.

## Accepted final review refinements — 2026-10-29

The fresh Opus 5.5 re-check closed the original findings, with no remaining Blocker/High. Its N1 found that extending helper freshness to queued review age would interrupt Update All after a slow first item. The owner accepted one frozen batch review valid for **30 elapsed minutes to dispatch each item**, with fresh evidence/source/consent/mode and late-warning checks at each start. Keep the separate **two-minute just-in-time helper request** freshness unchanged. A running item may finish within its own execution deadline; expired unstarted items require fresh review, never silent renewal. Single-target uninstall uses the same 30-minute review ceiling without batch semantics. This ceiling was explicitly accepted in the final decision, not silently added to the earlier defaults.

Also accepted: ADR-025's common **exclusive** current-user gate across processes/logon sessions for mutating scans/collection/operations/AI/clear/reset; readonly UI stays available. New install/uninstall items have a **10-minute overall deadline**, including UAC/provider/verification, then at most **5 seconds bounded teardown attempt**. Unknown residual work retains a durable app-owned unresolved-operation barrier until safe explicit reconciliation; it never becomes a false stopped/success state. Existing Update deadlines are unchanged; no resident service/reset-surviving marker. Implementation proof remains required. N2–N6 attribution, namespace/lock/expiry behavior, stale-reference and route/match-predicate clarifications are included in the owner-accepted corrected ADRs/prompts. They are disclosed author follow-ups after the re-check, not claimed independently re-reviewed.

## Task 50 — Accepted supported gate population, 2026-10-30

For `FR-UPD-15`, retained `FR-UPD-01` and `TM-03–04/07/13`, the owner answered the explicit ADR-025 population question: **“Single protected deployed build; legacy standalone coexistence unsupported”**. Support covers that build's mutating entrypoints across the current user's processes/logon sessions. All must use the common gate/claims; legacy standalone copies sharing state are unsupported and must not be described as coordinated or technically excluded. Exact replacement/quiescence and scheduled-entry evidence remain mandatory. The answer is a contract-scope clarification only: no deployment, launch, provider action, removal, new data root, application-control policy or distribution authority follows. Passive prerequisites are locally committed as `57d36b6`; Task 50 remains open for claims/integration/provider evidence.

## Dependency servicing planning addition — 2026-10-30

Under the owner's instruction to verify the component-update PR and add suitable changes if main and Opus 5.5 agree, both reviews accepted the corrected [PR #10 servicing plan](../review/V2_DEPENDENCY_SERVICING_PROPOSAL.md). Task 53A precedes 53; Task 54A and conditional 54B precede 55, with refreshed 53 evidence and independent affected 54 review after adoption. App SDK 2.x and any explicitly deferred projection are candidates for individually requested Task 72 reconsideration. The NuGet security-only policy is recorded for separately authorized application on the default branch; no config is added here. Retain the archived prototype/manifests.

This adds planning gates, not implementation authority. PR #8 package reconciliation, exact servicing/spike packages/network restore, the ADR-003 amendment, runtime prerequisites/installation, live actions, merge and publication retain their own approvals. ADR-003 remains Provisional. Branch-local and installed snapshot evidence stays separate; the plan does not close runtime, deployment or distribution gates or start Task 50.

## Task 50 — Accepted provisional Chocolatey candidate, 2026-10-31

The owner selected **Allow provisional review**: only the new-protocol Chocolatey Community machine Update candidate may originate as explicitly provisional signed-client/user-cache intent before UAC. This expressly amends ADR-025's desktop trusted-candidate responsibility; it does not alter trusted pre-UAC client/profile, installed-registration or explicit-mapping requirements. The helper must independently qualify the exact frozen candidate from protected machine data and refuse without updating on any mismatch or missing proof. Preserve a distinct route identity, closed provenance, truthful review/history, claim-before-UAC and owned-work settlement. No production route, deployment, host read or real update is approved. See [ADR-025 amendment](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md) and [evidence/decision](../failures/task-50-pre-uac-candidate-evidence.md).

## Task 50 — Accepted fixed-feed Community runtime limitations, 2026-10-31

Asked the explicit choice between retaining effective checksum/installer-argument guards throughout scripts (unsupported route) and accepting the disclosed provider limitation, the owner answered **“I think Chocolatey is fine as long it uses the official community repo”**. Record this as acceptance of the described runtime checksum/argument overrides and TEMP replacement risk for the new Chocolatey route only, conditional on the compiled official Community feed for package/dependency resolution. The source restriction does not eliminate these risks or sandbox vendor payload downloads. Retain all initial guard requests and every other native/helper/review/claim/gate/settlement check; no arbitrary request flags/environment/source or protocol fallback. A distinct canonical route/disclosure prevents older reviews acquiring the exception. See [ADR-025](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md) and [source/dialogue/decision](../failures/task-50-chocolatey-refresh-guard.md). This does not approve host reads, deployment, UAC or real updates, or complete Task50.

## Owner decision 2026-11-02: Skip this version accepted (closes the skip-version part of Q2/Q3)

The owner asked for "the update all available button & skip the version". This accepts a reversible, per-product **Skip this version** decision and closes only the skip-version part of Q2/Q3. Pin/ignore/deselection semantics, the running-app warning policy and the "no broader major/minor pin, universal process detector or forced closure" limits stay as accepted.

| Rule | Accepted semantics |
|---|---|
| What is skipped | One exact candidate version string V for one product. It is stored in `stable_decisions`, with kind `SkipVersion`, value V and an optional reason of up to 512 characters. There is no schema change. |
| When it applies | Only while the current candidate equals V, under the domain version comparer (or is the identical string). The product then leaves Ready and is shown under "Ignored and skipped" as "Skipped version V". |
| When it stops applying | A newer candidate than V returns the product to Ready automatically. An older or incomparable candidate is not skipped and stays subject to the existing actionability rules. |
| Authority | A skip only removes actionability; it never makes anything actionable. Ignore and pin take precedence. |
| Reversal and visibility | Listed on Decisions; reversed with "Stop skipping V". The wording is distinct from "Ignore updates for this app". Skipping again replaces the product's earlier skip. |

G4 Update All (SPEC §9.13) is implemented in the same task. It is not a new decision: per-item sequential approval and elevation are used, because the one-shot helper protocol has no multi-item job. The design is in [Task 73 Phase 6](../review/TASK_73_PHASE6_UPDATE_ALL_AND_SKIP_VERSION.md).
