# Task 49 — independent integration review

Date: 2026-10-29. Reviewer: GPT-6.1 Sol, High, supplied runtime; independent source review, no delegates or worktrees.

## Findings and verdict

No concrete correctness, security, resource-ownership or regression finding requiring correction was identified in this cumulative candidate. **PASS for this scoped Task 49 integration review.** This is not Task 54's independent phase gate, deployed-host acceptance, distribution approval or authority to start Task 50.

No Blocker/High/Medium findings are raised. The disclosed limits below remain real limits; this verdict does not certify their resolution or infer live-host evidence from fixtures.

## Pinned scope and identity

- Source baseline: `fbf1835589946d5bcf67de2df9fedd154fddbd78`.
- Candidate HEAD: `cfeb0ef21f436c3a1152b3cb6fecd04222b60c42`.
- Entry: `git rev-parse HEAD` matched the candidate; `git status --porcelain` was empty.
- Reviewed the cumulative 45-file diff, including earlier OwnedProcessWork, HelperProcessWork, execution lifetime and metadata propagation, rather than only the last commit. Inspected the production call chain and relevant existing/new fixtures.
- Read AGENTS, STATUS, Task 49 prompt, Phase C, shared task contract, accepted ADR-025/Q1/Q3 decisions, implementation report and relevant council allocation/ownership records. Consulted only the cited product requirement rows. No prior implementation conversation or author test pass was used as source proof.
- Before report creation, all 35 changed source/test paths matched their HEAD Git blob identities (`git hash-object` versus `git rev-parse HEAD:<path>`); tracked tree remained clean. The protocol codec and canonical Build/Test scripts have no baseline-to-candidate diff.
- The sole review edit is this new report. No production, test, common documentation, configuration or Git identity edits; no commits or external actions.

## Source reasoning and meaningful evidence

| Requirements / concern | Reviewed evidence and conclusion |
|---|---|
| FR-UPD-14; retained FR-UPD-01–11: admission and ownership | `UpdateBatchWorkflow.cs:138–149` acquires the production executor's stable instance lifetime before recovery or Queued. `ApprovedUpdateExecutor.cs:11` supplies that lifetime in actual Desktop composition. `UpdateBatchWorkflow.cs:204,257–265` retains native completion plus cancellation-monitor cleanup before releasing admission, including exceptional history paths. `UpdateOwnedWork.cs` retains incomplete roots across lease disposal, observes failures and rejects a second owner. This is current-instance update admission only. |
| FR-UPD-14; NFR-OBS-01–03: fault/cancel/result races | `UpdateProgress.cs` sets the one-shot capacity signal under the same lock used by StopOffers; cancellation runs asynchronously outside that lock. `UpdateBatchWorkflow.cs:247–255` stops offers before selecting the final fault outcome and writing history. Thus a pre-close overflow remains visible even if executor/verification already returned; a post-close flood cannot cancel a finished result. Internal capacity fault is separately attributed and prevents dispatch of later items. Caller cancellation and capacity cancellation remain distinct. |
| FR-UPD-08–09/14: durable terminal and uncertainty | `UpdateBatchWorkflow.cs:290–296` awaits the final history write before publishing Recorded. Persistence exceptions propagate and the outer finally supplies RecordingFailed with no recorded status. An observed possible invocation converts a product-start failure into VerificationRequired; cancellation after dispatch is conservative. Verification is awaited rather than abandoned. Successful exit alone does not become Success. Existing preparation/verification/history-race fixtures exercise these decisions, not merely string presence. |
| FR-UPD-02–04/07/14: actual milestones | `WingetInstalledInventoryProvider.cs:472–490` publishes ProcessStarted only after constrained argument construction, validation in the runner and successful native Start; no stdout percentage parsing. `WindowsElevatedHelperTransport.cs:124–127` publishes ElevationRequested after final trust and immediately before the launcher. `:175–187` authenticates the peer, writes the request, then publishes HandedToHelper. Its label claims only request transmission, not helper acceptance or provider start. Synthetic milestone tests exercise runner/adapter plumbing and final-trust/denied/pre-cancelled launch paths; they do not execute the real provider. |
| FR-UPD-14; resource lifecycle | `OwnedProcessWork.cs` separates direct exit, EOF, partial output, cancellation, overflow and unresolved work; private reader cancellation and one teardown-attempt budget precede retained final cleanup. Timer cancellation and registration disposal are observed. Real synthetic redirected-process fixtures cover surviving inherited writers, cancellation/fresh reads, clean EOF, timeout, overflow and failed kill. `HelperProcessWork.cs` requires both successful exit observation and transport completion before disposal; transport results retain that task. An observation failure stays unresolved. No reader disposal is substituted for proof that its pending read completed. |
| FR-UPD-14; Q1 identity/late events | Mailbox construction/production are internal, and the producer handle captures immutable job identity. Review confirmation rejects substituted identity before approval; the coordinator checks identity before history. Closed/foreign/illegal offers cannot create execution authority. Coalescing retains only one nonterminal plus the reserved terminal; emitted sequence is contiguous despite coalescing. Capacity includes rejected matching attempts and preserves the final outcome. Late offers after StopOffers are closed. |
| FR-UI-01–07; FR-UPD-14 | `MainWindow.cs:1434–1465,1501–1520` owns a single 250 ms pull timer, captures mailbox/presenter/epoch, guards detached labels and closes/unsubscribes on unload, final/degraded state, finally and window close. The background operation captures review/preparation data rather than mutable control state. `UpdateProgressPresentation.cs` rejects foreign/gapped/regressing/route-invalid/final-following events and never provides a percentage. The outcome dialog consumes the real batch when terminal delivery is rate limited; Render follows dialog closure, preserving XamlRoot ordering. Pure presenter and source-contract tests support this; actual WinUI event/render behavior remains untested. |
| FR-UPD-05–06/14; Q3 | `RunningApplicationDetection.cs:77–116` counts inspected observations before projection, bounds names/candidates, reports inaccessible/exited/invalid/unavailable counts and conservatively reports saturation without requesting entry 4,097. `:118–154` disposes the entire materialized Process array, including uninspected entries and cancellation. Review and late warning disclose false negatives and check-to-start uncertainty. A late unacknowledged match returns before dispatch. No forced-close capability was introduced. |
| NFR-OBS-01–05 | Correlation identity persists through events/history. Coordinator event emission follows durable record and catches sink errors. Rejection/capacity counters are emitted once at detach rather than logging every offer. Existing sanitized diagnostic/history export is retained; progress has no arbitrary text/arguments. The test throwing a sink is specifically a coordinator-sink test, not proof that every historical logging seam tolerates exceptions. |
| Protocol 1 / retained trust | Codec is unchanged against baseline. Local task/lifetime/progress objects do not enter wire serialization. New compatibility fixtures exercise literal golden requests/responses and malformed schema/version/operation/identity/nonce/freshness/size rejection. Helper-side outcome mapping and shared lifecycle/artifact hashes do change, as disclosed. No new protocol safety, mode enforcement, privileged authority or deployment equivalence is inferred. |

## Independently executed checks

Host: native Windows PowerShell 5.1; process-local `DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet` and PATH prefix; approved SDK 10.0.403. Each invocation used `--configuration Release --no-restore`; no network, restore or package installation. Only synthetic selected tests ran.

| Project / exact filter | Result | Unique TEMP log |
|---|---|---|
| Application.IntegrationTests: `FullyQualifiedName~UpdateProgress\|FullyQualifiedName~UpdateExecutionLifetime\|FullyQualifiedName~RunningApplicationCoverage\|FullyQualifiedName~RedirectedProcessPipeEvidence` | Exit 0; **65/65**, zero skipped | `%TEMP%\pc-steward-task49-independent-app-a71da94f9226439b99473390a5b4209c.log` |
| Security.FixtureTests: `FullyQualifiedName~Task49\|FullyQualifiedName~HelperProcessOwnership\|FullyQualifiedName~UpdateRunnerSecurity` | Exit 0; **49/49**, zero skipped | `%TEMP%\pc-steward-task49-independent-security-5a69abfa7cc3431ca77f3057eb66c8e2.log` |
| ArchitectureTests: `FullyQualifiedName~UpdateProgressUiContract\|FullyQualifiedName~UpdateOutcomeUsesTheExistingXamlRoot` | Exit 0; **3/3**, zero skipped | `%TEMP%\pc-steward-task49-independent-architecture-6b25f1c0c3bd46a2ae1c5064cdcf87eb.log` |

Total independently rerun: **117 passed, zero failed/skipped**. Tests compile their relevant Release project dependencies; this is not a fresh complete Desktop build. Author-reported unchanged canonical Release build (zero warnings/errors) and full 407/407 are context only, not independently rerun. `git diff --check` passed. No need to repeat the complete suite merely for redundancy.

## Residual limits and optional follow-ups

1. Local admission covers incomplete work returned through this executor instance. It does not cover another process, restart, orphaned/external installers or a durable barrier. In particular, protected provider projection omits its helper-local Task; protocol 1 cannot report residual helper-local native ownership. Helper process exit must not be interpreted as external installer quiescence. This is expressly disclosed, not a finding that Task 49 secretly solves Task 52.
2. Normal direct exit with inherited writers can truncate diagnostics after the bounded drain, and closing readers can affect descendant writers. Synthetic reader evidence establishes this runtime path, not arbitrary installer cancellation or termination.
3. Process.GetProcesses still materializes a native array before bounded name inspection. Q3 counts/disclosure and heuristic false negatives remain explicit; bounded native enumeration is not claimed.
4. No actual app launch, WinGet/Chocolatey update, UAC, protected replacement deployment, installed-host cleanup, WinUI timer/render/accessibility measurement, or live resource benchmark was performed. Source-contract tests cannot establish actual WinUI behavior. Optional AC-UX-01–09 are not treated as blockers.
5. Optional future strengthening: a synthetic production-transport successful authenticated-write/disconnect fixture would complement the current final-trust/denial and pure ownership fixtures; the present tests do not directly exercise the successful real transport HandedToHelper observation. Source ordering is reviewed above. This is not a request to run UAC or widen Task 49.

Task 50/52/53/54 and distribution/host gates retain their own scope and authority. No commit was made because this review permits only a new report and expressly forbids commits.

## Exit identity check

After writing this report, HEAD was rechecked against the candidate, all 35 changed source/test blob identities were rechecked, tracked files remained unchanged, and status contained only this untracked review report. `git diff --check` remained clean. These checks establish review source identity, not installed artifact identity.

## Post-review correction — GitHub finding supersedes this PASS for machine ownership

The owner supplied a valid Codex review finding after this report: the protected WinGet scalar projection discarded pending provider-owned completion, allowing normal helper exit and admission release too early. Residual limit item 1 above incorrectly treated that owned-work loss as only a disclosed limit; it must be repaired. This report's original PASS missed the defect and is not acceptance of it. See [Task 90 repair and regression evidence](TASK_90_TASK_49_MACHINE_OWNERSHIP.md). Fresh independent repair review and fixing commit identity are recorded there after verification; no original source-review result is rewritten as if the defect had been found here.