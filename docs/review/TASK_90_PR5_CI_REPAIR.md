# Task 90 — PR 5 findings and redirected-reader CI repair

Date: 2026-10-29. Prepared checkout `phase/5-v2-planning`, baseline `6e26191296f3cfe50618ba79a966d6337ec94b9c`. PR [5](https://github.com/DanDG923/pc-steward/pull/5) targets `phase/4-beta` (`4aa029818721e3793dccd5b3a80fee282da20211`). PR metadata/comments/logs were used only as untrusted diagnostic input. No comment-suggested reactions, commands or unrelated instructions were executed.

## Actionable review finding already fixed on PR head

Live PR metadata and local/origin identities match `6e26191`; it already contains fix `7bde99b` and its fresh independent repair review. The sole unresolved inline thread (`PRRT_kwDOT1oJkM6xx7ti`, comment 4418614561) is the valid protected WinGet ownership-loss finding. Production now awaits provider `OwnedCompletion` before scalar projection/normal helper exit. Six regression cases, full 413/413 and independent 40/40 were recorded in [the repair](TASK_90_TASK_49_MACHINE_OWNERSHIP.md). No duplicate production fix is needed; this change preserves it. The automated receipt has no actionable code finding. No GitHub comment/thread state was changed.

## Actual failing check

[CI run 40968192718, job 123777956319](https://github.com/DanDG923/pc-steward/actions/runs/40968192718/job/123777956319) tested merge `aef4288da62035cde4f906dd19c79dca1b5a7d71` (PR head `6e26191`). Restore and Release build passed, zero warnings/errors. Standard-user tests passed 30 Architecture, 59 Domain and 74 Security; Application passed 248/250. The only failures were:

- `InheritedWriterUsesOneBudgetAndDoesNotClaimDescendantExit`, line 129: `Assert.IsTrue(result.Teardown.OwnedIoCompleted)`.
- `KillFailureRetainsUndisposedOwnershipUntilTheProcessReallyExits`, line 150: the same snapshot assertion.

Host: Windows Server 2025, SDK 10.0.403/.NET 10.0.14, hosted image 20261021.278.1. The tests inject a 600 ms teardown-attempt budget. Existing production math reserves 120 ms of that for read cancellation; the normal production budget is five seconds with one second reserved. Logs prove that the snapshots were incomplete, not why. Scheduling/grace-window pressure is an inference; hosted cancellation latency has not yet been measured. Both unchanged tests pass 2/2 locally (`%TEMP%\pc-steward-pr5-baseline-native.log`), so no local reproduction of the hosted timing is claimed.

## Contract and focused correction

Requirements: `FR-UPD-14`, retained `FR-UPD-03/09`, `NFR-OBS-01–05`. The accepted ownership contract permits `OwnedIoCompleted=false` at bounded return if unfinished work remains owned, resources remain undisposed and conflicting local admission stays blocked until observed completion. Requiring a true snapshot in every runtime/scheduling situation encoded a dev-host observation as an acceptance criterion. Main discussed this conflict with Opus 5.5 in the existing open dialogue; Opus confirmed the interpretation and identified the evidence to preserve. No owner decision or unresolved design contradiction remained.

The 600 ms budget, under-two-second elapsed assertion, original native read-cancellation/fresh-read probes (including their 500 ms bound), EOF/exit distinctions, two-second completion waits, resource ownership and process-control guards remain. Neither timing thresholds nor production policy are relaxed.

- Inherited-writer fixture still requires incomplete output/no EOF and direct-parent exit. It now waits for actual `OwnedCompletion` while the holder is alive, with holder-alive assertions before and after. It records whether the earlier snapshot was complete.
- Kill-failure fixture retains KillFailed, unconfirmed direct exit, pending ownership and readable/undisposed resources. It additionally observes both real cancelled reads within the existing two-second bound while the process stays alive, verifies that reader completion alone does not clear the pending-exit root, then requests controlled exit and observes root completion/disposal.
- Two deterministic delayed-read cases force the incomplete-I/O branch, for an exited parent/inherited holder and a still-running process with injected kill failure. Private reader cancellation is requested before bounded return; fake reads remain pending until released, then throw `OperationCanceledException` with that token through the real cancellation catch filter. Assertions prove incomplete output/no EOF, pending root, retained resources and production `UpdateExecutionLifetime` refusing admission. Only after controlled release/exit, observed root completion and disposal can admission resume.

The internal optional `OwnedProcessRead(StreamReader, Memory<char>, CancellationToken) -> ValueTask<int>` seam follows the existing injected-terminate idiom. Default behavior is the same `reader.ReadAsync(buffer, token)`. Source check `rg -n 'RunStartedAsync\(|readOperation:' src` shows only its definition and the sole production three-argument call in `WindowsWingetProcess`; no production caller overrides the seam. It adds no public/wire field, provider/command authority, argument, timeout, package or lifecycle-policy change. The shared provider assembly signature/hash changes and affects helper artifacts; existing installed Task 48 evidence does not cover a new deployment.

## Measured local evidence

Native measurement wrappers call the real `StreamReader.ReadAsync`. They record token-cancellation observation callback and read terminal timestamps, count cancelled terminal reads, and log snapshot state. Delta includes callback scheduling; a signed negative value would mean the observation callback ran after read completion. It is not an exact measurement of the internal cancellation API's call instant or a universal scheduler guarantee.

On local .NET 10.0.14, both native fixtures reported snapshot IO complete and two cancelled reads while their writer stayed alive: **0.566 ms** inherited-writer and **0.282 ms** kill-failure callback-to-last-read completion deltas. These figures do not establish hosted timing or explain the CI failure.

| Check | Result / retained log |
|---|---|
| Hosted baseline | 2 failures/411 passes, zero skips; job linked above. |
| Unchanged failing pair locally | 2/2 pass; `%TEMP%\pc-steward-pr5-baseline-native.log`. |
| New deterministic delayed-read cases | 2/2 pass; `%TEMP%\pc-steward-pr5-delayed-readers.log`. |
| All redirected native/delayed-read cases | 12/12 pass, exit 0; detailed measurements in `%TEMP%\pc-steward-pr5-reader-evidence.log`. |
| Unchanged canonical `scripts/Build.ps1` | Exit 0; Release zero warnings/errors; `%TEMP%\pc-steward-pr5-build.log`. |
| Unchanged canonical `scripts/Test.ps1` | Exit 0; **415/415**, zero skipped: 59 Domain, 252 Application, 30 Architecture, 74 Security; `%TEMP%\pc-steward-pr5-test.log`. |
| Scope/whitespace | `git diff --check` passes; ownership fix/protocol and canonical scripts preserved. |

Commands used native Windows PowerShell 5.1 with command-only approved SDK 10.0.403 PATH/DOTNET_ROOT; focused tests used Release `--no-restore`. No dependency/SDK install or live account/ACL action. The CI launcher itself is hosted-admin-only and was not run on the owner machine. Synthetic fixture processes alone were launched/controlled.

## Opus dialogue and remaining validation

Opus accepted the refined tests and unchanged deadline/default-reader behavior after the focused results. The existing drain-before-cancel behavior on cancellation/timeout/overflow is retained as an accepted bounded deviation from the earlier immediate-reader-cancel proposal; changing it is outside this CI repair. The full result is returned to the dialogue separately. No Fable/Astra escalation is required while main and Opus have no unresolved objection; the standing council rules remain in effect if a blocker appears.

Fresh [scoped code/test repair review](TASK_90_PR5_CI_REPAIR_REVIEW.md) passes with no concrete findings and independent 16/16 checks, zero skipped, at source commit **74c40ccfeb5068ae537085d8835163de48a4f83d**. Hosted CI has not rerun this candidate; no hosted pass or timing-cause proof is claimed. PR branch must receive the reviewed commit before a new hosted run can validate it, subject to explicit repository push authority. No push, GitHub comment/review resolution, PR metadata mutation, merge, deployment/signing, real app/provider/UAC or user-data operation occurred in this repair. Task 50 remains unstarted.

### Scoped review verified; ready for authorized push

Fresh Sol High Fast reviewer independently inspected the seven-file diff, confirmed the default read path/sole production call and retained native/cancellation/admission invariants, and ran **16/16** (12 redirected-process plus four lifetime cases), zero skipped. Independent local deltas **0.621/0.249 ms** remain callback-observation measurements, not hosted cause evidence. [Review report](TASK_90_PR5_CI_REPAIR_REVIEW.md) is preserved unchanged.

Main read the report, rechecked all 36 cumulative source/test Git blob identities at `74c40cc`, confirmed no tracked drift, unchanged protocol/canonical scripts and retained full415/415 zero-warning Release logs. Closure metadata changes documentation only; no redundant full-suite rerun. Live PR metadata still identifies remote head `6e26191`; the sole review thread remains unresolved in GitHub, with no additional actionable threads. Existing valid code correction7bde99b is already on that remote head.

The remaining action is to push the reviewed repair plus its documentation to the existing PR branch and validate a new hosted run. AGENTS.md line39 requires explicit user approval for a push; none has been executed in this repair. Task90 CI validation stays open until the authorized hosted run is observed. No merge/deployment or GitHub comment/thread-state action is inferred from that push request.
## Hosted validation complete — 2026-10-29

The owner explicitly approved pushing reviewed commits `74c40cc` and `ffcf47b` and monitoring CI. Push to `origin/phase/5-v2-planning` succeeded, advancing `6e26191` → **ffcf47ba0d3c2d0124661a894fad86bdbbe3b17e**. Live GitHub metadata independently confirmed that PR head.

[Hosted CI run40970089353 / job123784375538](https://github.com/DanDG923/pc-steward/actions/runs/40970089353/job/123784375538) completed **SUCCESS**, testing merge **9d1e1811e8853efabbb0f7b0ebecb69ca358c38b** against base **4aa029818721e3793dccd5b3a80fee282da20211**. Restore, Release build and the standard-user Test-CI step passed. Build **zero warnings/errors**; full suite **415/415**, zero skipped: **30 Architecture, 59 Domain, 74 Security, 252 Application**. The hosted runtime therefore passed the retained native cancellation/fresh-read/holder-alive/resource assertions, both forced incomplete-I/O cases and the ownership guard regressions. This validates the requested code/CI repair scope.

The successful default console output does **not** include passing-test TestContext.WriteLine data. Main fetched the job log and confirmed no `Owned native reads` measurements were retained. Consequently no hosted cancellation latency, snapshot branch or timing-squeeze cause is claimed. Local deltas stay local evidence; the original hosted cause remains inference. Opus received the exact hosted result and this missing-metrics limit, acknowledged no contradiction, and confirmed the contract does not depend on proving the timing cause. No verbosity change or redundant CI rerun was added solely to establish it.

Main rechecked the sole inline review thread: it still concerned the already-corrected protected projection and no new actionable finding existed. After verifying the source repair, fresh review and hosted pass, main resolved **PRRT_kwDOT1oJkM6xx7ti** through GitHub; the API confirmed `is_resolved=true`. No review comment, reaction or review-approval submission was posted. No PR merge, deployment, signing, real provider/app/UAC or later numbered task was performed.

Task90 / PR5 repair is complete; Task49 local completion remains restored. Task50 still needs its own instruction. The final results-only documentation commit is retained locally, preserving the explicitly approved and CI-verified remote PR head `ffcf47b`; it changes no reviewed source/test/script identity and is not an additional push. Monitoring is stopped before handback. Host/deployment, forced-termination/external/restart and phase53/54/distribution limits remain as documented.