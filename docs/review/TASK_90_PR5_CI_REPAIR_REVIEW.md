# Task 90 — PR5 CI repair independent review

## Findings and outcome

**No concrete findings. Scoped PASS** for the seven-file repair diff. This is a fresh CI-repair source/test review, not Task 54, deployment acceptance, final Task 90 closure, or a hosted CI pass. Source reasoning and the independent focused execution below support the verdict; council agreement and author-green claims alone do not.

## Identity and authority

- Reviewed HEAD: `74c40ccfeb5068ae537085d8835163de48a4f83d`.
- Base: `6e26191296f3cfe50618ba79a966d6337ec94b9c`; `git merge-base --is-ancestor 7bde99b <base>` exited 0. The protected-machine ownership repair is already present and unchanged.
- Initial `git status --porcelain=v1` was empty. The diff contains exactly the five task-document changes, `OwnedProcessWork.cs`, and `RedirectedProcessPipeEvidenceTests.cs` listed by the task. Final identity and sole-edit checks are recorded below.
- Read AGENTS/STATUS, Task 90 and Task 49 prompts, Phase C, shared task contract, relevant Q1 planning decisions, ADR-025, the allocated ownership contract/design dialogue, current PLANS and repair report. Inspected actual collector, adapter/protected projection, lifetime and fixed fixture-process source. No root CONTEXT/CONTEXT-MAP or nested AGENTS files were found.
- No parent-thread history, other agent, worktree, network, restore, package installation, Git mutation, real app/provider/helper/UAC, deployment, account/ACL or user-data action. Only fixed synthetic fixture processes were started/controlled by the authorized focused tests. Status/traceability/final closure remain main-owned.

## Source and test assessment

1. **Production defaults and boundaries:** `src/PCSteward.Provider.WinGet/OwnedProcessWork.cs:11–17,33–36,116–117,163–185` adds an internal optional delegate, forwards the existing buffer/private token, and defaults to the same `StreamReader.ReadAsync(buffer, token)`. Source-wide search finds only the definition and the production three-argument call at `WingetInstalledInventoryProvider.cs:491`; no production override exists. Teardown math, cancellation/termination policy, bounded capture, operation deadlines and root disposal are unchanged. No argument, authentication, wire, authority or dependency change appears. Shared provider/helper artifact hashes necessarily change, as disclosed.

2. **Contract versus timing:** the existing ownership allocation in `TASK_49_DESIGN_DIALOGUE.md` explicitly retains a root and undisposed resources when a bounded attempt cannot finish. `OwnedProcessWork.cs:55–83,119–147` retains read-cancellation work, both collector tasks, exit observation and timer work until the root settles; false snapshot I/O completion does not dispose them. The change therefore removes a stronger timing assumption rather than waiving actual completion. ADR-025's stronger new-operation cross-session/restart barrier is not claimed for this local Update guard.

3. **Native evidence preserved:** `RedirectedProcessPipeEvidenceTests.cs:20–57` still requires pending native stdout/stderr cancellation within the original 500 ms probe, followed by a fresh read consuming the holder's byte. The revised inherited-writer case (`120–138`) retains 600 ms teardown, under-2 s elapsed, observed direct exit, incomplete output/no EOF and root completion within the existing 2 s, with the writer alive on both sides. The kill-failure case (`141–170`) retains failed kill, unknown direct exit, live resources/pending root, and observes both terminal native reads within 2 s while the holder lives; it explicitly proves reader completion cannot release the still-pending exit root, then observes controlled exit and disposal. These are meaningful native assertions, not warnings or skipped failures.

4. **Forced fallback evidence:** both rows at `173–240` block two fake reads beyond the same bounded attempt. Their start signal and shared token make cancellation-requested-at-return observable; the reads do not silently complete from cancellation. They assert I/O false/no EOF/incomplete result, the appropriate exit/kill facts, readable resources, pending root and refusal by the actual `UpdateExecutionLifetime` after lease disposal. Controlled release throws OCE carrying the private owned token through the real collector catch filter (`OwnedProcessWork.cs:185`); root completion, resource disposal and resumed admission are subsequently observed. The inherited-holder row verifies completion while its external writer still lives. The kill-failure native test separately proves exit retention after reader completion. The new rows' finally block releases fake reads, stops/observes the fixture and awaits the returned ownership root even after assertion failure. The fixed fixture limits control/cleanup to its verified executable and has a 15 s self-exit bound.

5. **Measurement limits:** `RedirectedProcessPipeEvidenceTests.cs:268–316` uses interlocked timestamps/counters and a terminal signal; latest-read timestamp is monotonic even when continuations race. The logged delta starts at a token observation callback, not the internal cancellation-call instant. Callback scheduling, negative ordering and unavailable values are explicitly disclosed. Holder-alive assertions and fixed fixture behavior support the cancellation observations. This supplies local diagnostic evidence, not proof of the hosted failure's cause or guaranteed cancellation latency.

6. **Retained safety:** the pre-existing protected projection at `WingetInstalledInventoryProvider.cs:427–428` still awaits owned completion without caller cancellation before scalar projection; this diff neither duplicates nor undoes 7bde99b. `UpdateOwnedWork.cs` still retains the pending task after lease disposal, observes faults and clears admission only on completion. Scripts, fixture executable source and that lifetime implementation have no base-to-head changes. The five documentation diffs preserve hosted-pending, forced-termination/external/restart and host/distribution limits.

## Requirements

`FR-UPD-14`: honest incomplete output/EOF/exit distinctions and retained owned-work evidence. Retained `FR-UPD-03`: no new command or execution authority; sole production caller defaults. Retained `FR-UPD-09`: neither exit nor cancelled I/O is promoted to verified update success. Retained `NFR-OBS-01–05`: no changes to job correlation, structured/sanitized bounded production diagnostics or cancellation/failure classification; new timing output is test diagnostics. This narrow review does not establish fresh end-to-end evidence for every retained requirement.

## Independent validation

Native Windows PowerShell **5.1.26100.9711**, command-only environment:

```powershell
$env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
dotnet --version
dotnet test tests\PCSteward.Application.IntegrationTests\PCSteward.Application.IntegrationTests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~RedirectedProcessPipeEvidenceTests|FullyQualifiedName~UpdateExecutionLifetimeTests' --logger 'console;verbosity=detailed'
```

SDK **10.0.403**; runtime **.NET 10.0.14**. Exit **0**, **16/16 passed**, no skips/failures: 12 redirected-process cases plus four lifetime cases; total test time **8.7143 s**. The command built the focused Release project graph without reported warnings/errors. Independent local inherited-writer/kill-failure callback-to-last-read deltas were **0.621 ms / 0.249 ms**; each logged two cancelled terminal reads and snapshot I/O true while its writer remained alive. Deterministic delayed-read rows both passed with I/O false. Both fresh-read probes passed; disposal-only probes again remained pending for 2 s until holder closure.

Inspected existing `%TEMP%\pc-steward-pr5-build.log` and `pc-steward-pr5-test.log`: they support the author's zero-warning/error Release build and **415/415**, zero-skipped suite totals (59 Domain, 252 Application, 30 Architecture, 74 Security). These are author-run evidence, not a fresh independent full-suite run. Canonical scripts were unchanged and no redundant full suite or hosted-admin `Test-CI.ps1` was run. `git diff --check <base> HEAD` passed.

## Remaining limits and handoff

Supplied hosted run **40968192718/job123777956319** remains a baseline failure: Windows Server 2025, SDK 10.0.403/runtime 10.0.14, 413 cases with only the two snapshot assertions failing. No network was used to refresh it. Scheduler/grace-window pressure remains an inference; these local deltas do not measure hosted cancellation. A new hosted run of the repaired commit is still required before claiming CI repair validated there.

No new owner decision or implementation blocker identified. No live provider/helper/installer, forced host termination, external installer quiescence, restart/cross-session barrier, UAC, installed artifact or distribution acceptance is established by these fixtures. Existing Task 48 installation evidence does not validate changed helper artifacts.

Final verification: HEAD remained `74c40ccfeb5068ae537085d8835163de48a4f83d`; tracked source remained clean, with only this new review report untracked. No commit made: review authority permits only this new report, and main owns status/traceability/closure.
