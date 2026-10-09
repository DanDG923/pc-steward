# Task 2 scoped repair re-review

Pinned repair candidate: `54eab743669ce0488ea757bf3af63c8a6ef40b9e`.

- Repair spec compliance: **APPROVED for the scoped I1/I2 repairs**. Both Important findings are addressed in source against CRD-08 / FR-PRV-03 / FR-INS-04 and the binding consent, exact-selection and cancellation constraints.
- Task code quality: **APPROVED for this repair scope**. The small absence-gate move and provider-owned per-preflight process factory repair the reported boundaries without broadening execution authority or changing the legacy process branch.
- Unresolved original repair findings: **0 Critical / 0 Important / 0 Minor**. New findings caused by the repairs: **0 Critical / 0 Important / 0 Minor**.
- Overall Task 2 completion gate: **Pending repair CI test result and controller adjudication**. Source approval does not certify later Tasks 7/12 composition, native operation or release completion.

## Scope and evidence

Read `AGENTS.md`, then the full Task 2 brief and binding global constraints; read the original `task-2-review.md`, full `task-2-report.md` including fix round 1, and the full UTF-8 `task-2-repair-review-package.md` for `924212bca2578297c04ffc30c2d0e4a3736273fd..54eab743669ce0488ea757bf3af63c8a6ef40b9e`. Its filesystem size is **45,063 bytes**. Also read directly relevant approved design requirements concerning explicit review, cancellation, positive setup absence and strict protected-layout trust.

Checkout HEAD matches the pinned repair candidate. Inspected repair source and regressions directly, plus the reused setup callback, request/parser/argv controls and `WindowsChocolateyProcess(capturedClient)` launch boundary. The inspected source/test files have no working-tree difference from the pin. This is a repair re-review, not a new whole-original-implementation or whole-branch review. Controller-owned status/traceability/progress edits were read as evidence and not modified.

Original candidate `924212bc` passed controller-confirmed [Windows CI 37999775904](https://github.com/DanDG923/pc-steward/actions/runs/37999775904), job `114054736595`: clean restore/build, 662 passed / 0 failed / 0 skipped. That result predates these repairs and establishes no repair pass. For pinned repair [Windows CI 38002369017](https://github.com/DanDG923/pc-steward/actions/runs/38002369017), job `114063206386`, the controller supplied successful restore/build steps; tests were still running when this report was written. **No final repair test counts or CI success are claimed.** Source findings were assessed independently of CI.

No tests, known-red runs, SDK probes, provider/client/network calls, installers, UAC, application/task-state operations, source/test edits, subagents, Git identity/configuration changes, commits or pushes were performed. Local SDK absence remains known; there is no successful local/focused pass.

## I1 — addressed

`InventoryWorkflow.cs:472-493` now invokes the dedicated absence callback outside the warning-only branch, after every successful awaited reserved setup scan whose warnings are acceptable. Clean scans therefore repeat client absence before setup preflight. Failed scans, missing local providers and unacceptable warnings still refuse without querying setup. The only warning exception remains the two exact orchestrator-prefixed optional Chocolatey codes; ordinary install paths supply no callback and retain warning refusal.

The dedicated callback still comes from `ChocolateySetupPreparation`, whose `ProbeFreshAsync` checks cancellation after the awaited health probe and refuses failed, stale/future, wrong-provider, Present, Unknown, Partial or nonconforming trust evidence. The workflow checks cancellation immediately after the awaited scan and again after the callback. It subsequently retains the any-detection refusal, including weak detection; fresh client absence cannot authorize reinstall of a catalog-detected package. Fixed WinGet/winget `Chocolatey.Chocolatey`, Machine/X86, persisted source consent and capability controls are unchanged.

The four new clean-scan rows cover Present, Unknown, Partial and unchanged Current/Absent. Each asserts the repeated observation and zero preflight calls for refusal, with one exact preparation for the positive control. The fifth case rejects a failed post-scan observation before preflight. The existing exact setup control is strengthened from one to two Chocolatey observations and retains unchecked agreement/approval assertions. Existing optional-warning transitions, mixed/spoofed warnings, detected setup and cancellation-after-scan controls remain intact.

This repetition enforces the derived fresh-absence invariant after the intervening scan; the brief does not literally prescribe two probes. It does not lock the filesystem or prove that presence cannot change after the observation.

## I2 — addressed

`ChocolateyInstallChoices.cs:12-13` composes the default and injected-client choice paths through `ChocolateyInstallPreflightAuthority.CreateStrict()`. `ChocolateyInstallOperations.cs:28-32,68-70` creates a new `StrictChocolateyQueryProcess` for each validated preflight and uses that same instance for the awaited exact search and local-list calls. A reused authority therefore obtains a fresh capture on a new preflight, while concurrent/sequential preparations do not share captured-client state.

`StrictChocolateyQueryProcess` checks cancellation before resolution, resolves with `requireCallerCannotModifyLayout: true`, and validates the captured path/digest with the same strict flag before each query. It checks cancellation again before handing the captured identity to the launch port. It retains the first capture across awaited search/list rather than silently resolving and adopting a replacement client.

The production port constructs `WindowsChocolateyProcess(capturedClient)`. Its unchanged nonnull-client branch at `ChocolateyProvider.cs:217-229` repeats strict validation before constructing structured `ArgumentList` launch information, then checks cancellation immediately before `Process.Start`. `MachineTrustedChocolatey.ValidateUnchanged(..., true)` re-resolves with strict protected-layout, fixed path, signature/signer and no-reparse checks and compares path/digest. Thus the fake seam is supported by direct source inspection of the actual production launch boundary. Query output is bounded to 65,536 characters in this reused branch; overflow remains a nonactionable result rather than truncated approval or fallback.

The public legacy preflight constructor and injected `IChocolateyProcess` constructor still reuse their original process instance. The weaker null-client Windows branch is unchanged and is no longer the default choice-discovery composition. Fixed-feed search/local-list argv, ID/version validation, Machine/Neutral/Stable restrictions, parser failures, local-installed refusal and immutable evidence controls are unchanged. Existing literal argv snapshots and hostile/refusal controls remain; the new launch assertions complement them.

## Regression assessment

The **13 added fake cases** exercise the requested repairs:

| Boundary | Cases | Source assessment |
|---|---:|---|
| Clean setup scan: Present, Unknown, Partial, unchanged Absent | 4 | Repeated dedicated observation, selection outcome and preflight count asserted. |
| Failed clean post-scan observation | 1 | Zero setup preflight asserted. |
| Layout change after health, after capture, between awaited queries; unchanged control | 4 | Affected fake launch withheld; strict flags and healthy positive selection asserted. |
| Captured identity replacement between queries | 1 | One resolution, two validations and only search launch asserted. |
| Cancellation during validation and between queries | 2 | Cancellation propagated with zero/one launch respectively. |
| Shared capture within a preflight, fresh capture for a new preparation | 1 | Resolution/validation counts, two healthy selections and both exact argv asserted. |

The I2 fake search contains an actual asynchronous yield before transition hooks, so between-query cases exercise state changes across an await. The I1 fake local scan changes the observation during scanning; source inspection establishes callback placement after the awaited scan. These are meaningful deterministic regression controls, not native Windows process/ACL or timing proof. No desired existing refusal assertion was removed or weakened by the repairs.

## Remaining integration and cannot-verify limits

Tasks 7/12 still must compose review, initially unchecked agreements, final confirmation, batch/job identity, execution, ordinary verification and fresh strict post-setup health before Chocolatey use. Task 12 must select `CreateStrict()` for final Chocolatey selected-install preflight and exact revalidation; choosing the legacy constructor after earlier choice health would not deliver strict final composition. This repair supplies that factory and repairs default choices; it does not install it into the later desktop flow.

Reserved-entry exclusion from ordinary membership/cards, the fixed Store-link UI and whole-flow single-active-operation composition remain later delivery requirements. Ordinary optional-Chocolatey availability warnings still refuse install preparation conservatively; safe generalization requires separate independent absence composition. These existing handoffs are not new repair findings and are not claimed delivered.

Native COM/provider behavior, ACL/launch execution, actual wrapper installation/local detection, owner installation, cancellation/resource timings and UI behavior cannot be verified by this source review or fake cases. Health/launch observations remain snapshots. Task 1's separately recorded M1 late health trust-exception cancellation-reporting issue remains for final triage and is not counted as a new Task 2 repair defect.

Only this report was written. No commit was made, as required by the delegated review scope.

## Controller-collected final Task 2 repair validation

Exact repair candidate `54eab743669ce0488ea757bf3af63c8a6ef40b9e`, tree `8395c6cb748bcfabe460e1ffcec67c1f32679127`, Windows CI [38002369017](https://github.com/DanDG923/pc-steward/actions/runs/38002369017), job `114063206386`: complete decoded logs collected. Canonical Restore.ps1 / Build.ps1 / Test.ps1 all succeeded; zero warnings/errors. Architecture 38, Catalog 97, Domain 87, Security 72 and Application 381 passed; **675 passed / 0 failed / 0 skipped**. Independent scoped repair spec/quality review approved I1/I2, with zero new Critical/Important/Minor findings. Controller marks Task 2 code/review/CI gates complete; Tasks7/12 integration/native limits and Task1 M1 remain carried, no whole-release/owner-use completion claim. No local/focused successful run is claimed.
