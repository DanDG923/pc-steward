# Task 90 — independent re-review of Task 49 machine ownership repair

Date: 2026-10-29. Scope: the owner's single protected WinGet owned-completion finding and its repair. Independent source inspection and narrow synthetic verification; no reliance on the earlier Task 49 PASS verdict.

## Findings and verdict

**No concrete findings in the scoped repair. PASS for this finding at the pinned repaired HEAD.** The original High defect was real: the protected projection could discard unresolved provider-owned exit/I/O, allowing normal helper response/exit and local admission release. It was not merely uncertainty about an external installer. The four added lines correctly await and observe that root before scalar projection, without an operation token, replacement timeout, or wire Task. No further fix is requested for this finding.

This verdict does not close Task 54, readiness, deployment, host, cross-process/restart admission or distribution gates. Main retains final closure and common-document updates.

## Snapshot and inspection boundary

- Baseline resolved with `git rev-parse d28d69d`: `d28d69d7b8035a2f2f4340d779c46174cd8db035`.
- Repaired HEAD, verified before and after inspection/tests: `7bde99bb7da5cc8b40a8abfc04c06fc158a30f9a`.
- Initial `git status --porcelain=v1` was empty. Immediately before report creation, both `git diff --exit-code HEAD -- .` and `git diff --cached --exit-code` passed; status remained empty. Final verification permits only this new report.
- HEAD identities retained: `src` tree `714113056cfb9e90ca48351f3fc0640c5978c771`; `tests` tree `2b4812a1956046524820f037be48de94a0c4dd98`; `scripts` tree `c1dd57dce169c2cac69cecb9abab95be1d9409ff`; `global.json` blob `ddcd6c772a44554bb20e1118efc84e42f824e694`.
- Read AGENTS, STATUS, Task 90 prompt, Task 49 prompt, Phase C/shared contract, ADR-025, Q1/Q3 decisions, cited requirement rows, PLANS current top plan and the machine-ownership remediation report. No context-map files were present in the repository file inventory. Historical review verdicts were not used as proof.
- Baseline-to-HEAD production diff is exactly the four-line ownership guard/comment in `WingetInstalledInventoryProvider.cs`; the other executable change is the new six-case fixture. No interface, protocol/schema or canonical script change.

## Ownership-chain evidence

| Evidence | Scenario and assessment |
|---|---|
| `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs:414–430` | The protected interface calls the actual machine executor, which calls `RunUpgradeAsync`. Lines 427–428 await any `OwnedCompletion` with `ConfigureAwait(false)` before lines 429–430 project the existing scalar fields. A pending root cannot produce a protected result, including after later caller cancellation. Null/completed roots preserve the fast path. Fault/cancellation propagates only when the root settles. |
| `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs:473–494` | Production upgrade starts a structured-argument process and returns the root from `OwnedProcessWork`; it does not use the metadata `RunAsync` path. Machine scope/provider/digest validation and compiled arguments at lines 416–448 are unchanged (`FR-UPD-03`). |
| `src/PCSteward.Provider.WinGet/OwnedProcessWork.cs:22–80` | Root construction includes direct exit, both output readers, reader-cancellation completion, timer stop/observation and settled local signals. Bounded teardown can expire while that root remains pending. Registration disposal precedes returning it; no outer helper completion is needed. |
| `src/PCSteward.Provider.WinGet/OwnedProcessWork.cs:110–141` | Direct exit observation has no operation token. Ownership awaits all constituent work before disposing streams, process and cancellation sources in `finally`. Failed exit observation retains an unresolved root. The new outer await creates no dependency on response, helper exit, Desktop admission release or disposal that would require the outer await to finish first. Intentional unresolved retention is not a dependency cycle. |
| `src/PCSteward.ElevatedHelper/Program.cs:7`; `src/PCSteward.Execution/WindowsElevatedHelperTransport.cs:504–511,583–638` | Main returns the entry-point Task. Production dispatch explicitly invokes the protected WinGet interface. Entry point awaits `executeProvider` at line 590 before mapping outcomes, cancelling/observing its disconnect monitor, writing the response at line 632 and returning. There is no `WhenAny`, token-based wait wrapper or early-response branch around that await. Cancellation handling at 609 and failure handling at 613 can run only after protected execution throws. A faulted root cannot cause a response while it is still pending. |
| `src/PCSteward.Execution/WindowsElevatedHelperTransport.cs:648–680` | Disconnect monitor cancels the provider token; it does not independently return from Main or cancel the ownership await. Its own cancellation/observation happens after protected execution settles. Disconnect alone therefore cannot bypass the new wait inside the helper. |
| `src/PCSteward.Execution/WindowsElevatedHelperTransport.cs:132–134,197–224`; `src/PCSteward.Execution/HelperProcessWork.cs:8–37` | Desktop retains a local root joining actual helper exit and transport completion, disposing helper metadata only afterwards. A transport result alone does not satisfy helper exit. The helper-exit wait is independent of the provider root. |
| `src/PCSteward.Execution/ElevatedUpdateRunner.cs:122`; `src/PCSteward.Execution/ApprovedUpdateExecutor.cs:11,42`; `src/PCSteward.Application/UpdateBatchWorkflow.cs:137–138,204,257–264`; `src/PCSteward.Application/UpdateOwnedWork.cs:11–62` | Helper ownership passes through runner and approved executor to the batch lease's combined completion. Disposing the active lease keeps pending ownership blocking another admission. Completion is observed before clearing the pending hold. Desktop constructs this executor at `src/PCSteward.Desktop/App.xaml.cs:94`. |

The bounded provider snapshot remains timed out/incomplete/uncertain even if cleanup later completes. The repair does not infer a successful update from cleanup or exit (`FR-UPD-09`). Existing sanitization, bounded output and cancellation/failure mapping remain unchanged; retained `NFR-OBS-01–05` are not newly implemented or comprehensively re-certified by this narrow review.

## Independent verification

Windows PowerShell **5.1.26100.9711**; approved SDK **10.0.403** confirmed using command-process-only `DOTNET_ROOT` and PATH. Command, exit **0**:

```powershell
$env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
dotnet --version
dotnet test tests/PCSteward.Security.FixtureTests/PCSteward.Security.FixtureTests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~MachineProviderOwnershipTests|FullyQualifiedName~HelperProcessOwnershipTests|FullyQualifiedName~Task49ProtocolCompatibilityTests'
```

**40 passed / 0 failed / 0 skipped**, reported test duration 145 ms; Release dependency/test compilation succeeded. `git diff d28d69d HEAD --check` passed. No restore, package installation, network or full-suite rerun was performed.

The six new cases call the actual explicit protected executor with a synthetic `IWingetProcess`: pending root; subsequent operation cancellation; helper-exit/admission composition; faulted/cancelled roots; absent/completed roots. They assert behavior across the changed projection rather than merely inspecting source. Their synchronous fixture result makes the pending assertions deterministic: execution reaches the added await before the caller receives its Task. The composition uses production `HelperProcessWork` and `UpdateExecutionLifetime`, but its `FinishHelperAsync` models helper exit; it does not launch or execute the real elevated entry point. Actual entry-point ordering was separately verified above. Existing helper fixtures cover ownership forwarding and transport/exit joining; compatibility fixtures pin protocol-1 bytes, closed fields, authority and compiled arguments. No Task property was added to the scalar protected result.

Author-reported behavioral red **4 failed / 2 passed** was not independently recreated because source/test edits for reproduction were prohibited. Inspected existing canonical log tails: build succeeded with **0 warnings/0 errors**; full suite **413/413**, zero skipped (**59 Domain / 250 Application / 30 Architecture / 74 Security**). Those are existing author-run logs, not a fresh independent full build/suite. Independent evidence is the 40-case run and source review above.

## Remaining limits

- Normal helper execution can intentionally remain pending beyond bounded teardown or the operation timeout while owned work remains unresolved. No timeout-and-abandon is warranted for this ownership wait.
- Existing Desktop caller cancellation/timeout paths dispose the pipe and call `TryTerminate` (`WindowsElevatedHelperTransport.cs:206–221,288–300`), which attempts forced process-tree termination. The repair does not prevent forced termination by Desktop, the OS or another actor, nor prove all external/detached installers are quiescent afterwards. It fixes controlled response/normal-exit ownership loss; it must not be described as a blanket guarantee across those termination paths.
- No actual elevated/helper/provider/app/UAC/deployment launch, native-reader rerun, installed-state verification, host action or live-data access occurred. Earlier native-reader fixtures remain separate evidence, not newly executed here. No new restart-surviving/cross-process barrier, helper protocol authority, provider/mode capability or Task 50/52 behavior is established.

## Before/after working-file identities

SHA-256 values checked before report creation and rechecked afterwards; all match. Clean tracked/index diffs additionally verify the remainder of the pinned source/test/config tree.

| File | SHA-256 (before = after) |
|---|---|
| `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs` | `24AA3F9F3E78F9A8F732FB1673E1B4E89999A3DE0D0071F31FB69245E20B054F` |
| `src/PCSteward.Provider.WinGet/OwnedProcessWork.cs` | `2F9194A7E9F984AB3D3696B9481F1E97233F8100ED68B383578C1145A3009023` |
| `src/PCSteward.Execution/WindowsElevatedHelperTransport.cs` | `D48BDA534F09C4DE00EC9E71D6F008C19D84F524DF888FE28E90377427F8A4A0` |
| `src/PCSteward.Execution/HelperProcessWork.cs` | `D6E6B61D73B79C77E49D6E71034AE933D1218BE410CFC3CAA65B55B07454971D` |
| `src/PCSteward.Application/UpdateOwnedWork.cs` | `720EBA924A46734D2428CC20AB0D8585205691306D27F3DDEF738E71C918EC70` |
| `tests/PCSteward.Security.FixtureTests/MachineProviderOwnershipTests.cs` | `B31A06675EC3B671B61BFDDB0FE0E7000ACDEA52CCEF4942B7B2EDB493149BEF` |

Only this new report is changed by the reviewer. No commit was authorized or made; fixing commit is the pinned HEAD above. No GitHub post or reviewer-thread resolution was performed.
