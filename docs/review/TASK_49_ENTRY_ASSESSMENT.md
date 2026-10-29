# Task 49 — truthful update progress: entry assessment

Date: 2026-10-29. Source baseline: `fbf1835589946d5bcf67de2df9fedd154fddbd78`; initially clean working tree.

**Outcome: started; implementation incomplete. New-protocol machine-update capability is unproven.** This record is a prerequisite assessment, not completion, a failing product-test result, or approval to change the installed app. Task 50 has not started.

## Entry evidence

- Owner corrected “Start task 50” to “Sorry I meant 49”. This authorizes Task 49 only.
- `prompts/49_TRUTHFUL_UPDATE_PROGRESS.md` requires A2 acceptance, approved Phase C and accepted Q1/Q3 policies. Task 47's final acceptance record, finalized Phase C brief and `V2_PLANNING_DECISIONS.md` record those planning decisions as accepted. No additional ADR acceptance is missing.
- The prompt also says: “If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope.”
- `FR-UPD-14`, retained `FR-UPD-01–11`, `FR-UI-01–07` and `NFR-OBS-01–05` are assessed here; no implementation acceptance criterion is closed.

## Affected capability and source evidence

Task 49 requires changed helper semantics and a new protocol version. ADR-025, “Privilege and protocol boundary”, explicitly requires **new-protocol Update** to independently resolve the installed instance/source/candidate eligibility and enforceable mode/scope/architecture. It says an unproven machine Update route is unavailable, and prohibits silently using protocol 1 as the stronger capability. The shared contract repeats this requirement. Merely accepting the progress limits does not prove that route.

| Evidence at the pinned baseline | What it proves / does not prove |
|---|---|
| `src/PCSteward.Execution/ElevatedHelperProtocol.cs:12–46` | Protocol `1`; closed UpdatePackage request/result; 16 KiB messages and two-minute request admission. No progress stream or reviewed mode field. |
| `ElevatedHelperProtocol.cs:150–238` | Strict request admission reconstructs the immutable job and verifies its digest/time. It does not independently query installed/candidate evidence. A valid digest is not fresh target authority. |
| `src/PCSteward.Execution/WindowsElevatedHelperTransport.cs:497–507,570–585` | Authenticated helper admits the request and dispatches the fixed provider executor. There is no intervening independent operation-evidence resolver. Existing identity/ACL/nonce protections are real but do not supply the new required evidence. |
| `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs:389–438` | Machine executor constructs constrained upgrade arguments from the job and invokes the process. No independent installed-to-candidate query or reviewed-mode capability is established on this path. |
| `src/PCSteward.Application/UpdateBatchWorkflow.cs:149–154` | Existing Running history is written before executor invocation. It must not be re-labelled as observed installer execution or percentage evidence. |

Additional accepted new-operation infrastructure is also absent: no `DispatchClaimed` implementation was found by `rg` over `src`; the existing reset lease is the session-local thread-affine mutex at `src/PCSteward.Storage/FactoryReset.cs:13`. The shared contract expressly says it is insufficient for the common async cross-session gate. These are implementation gaps, not newly discovered defects in the historical protocol-1 deployment.

## Sequencing issue and safe continuation

Task 49's mandatory new protocol pulls in safety capabilities beyond progress framing. Task 50 owns enforceable reviewed mode and Task 52 owns frozen review/batch dispatch behavior; neither has run. Simply bumping `Version`, adding progress JSON, or forwarding stdout would not satisfy ADR-025. Conversely, disabling all machine updates to avoid proving the new route would be an observable capability reduction that must be made explicit in the implementation design and UI, never hidden behind passing protocol fixtures.

Before production edits, settle the concrete Task 49 implementation boundary: either include the required new-protocol Update safety prerequisites, with supported adapter/evidence mapping and tests, or explicitly design the permitted unavailable-machine-route behavior while retaining the unchanged deployed protocol-1 installation. This record does not select a new provider route, weaken ADR-025, start Task 50/52, or claim that a mock proves provider enforcement. No protocol version/schema or new consuming/producing interface is allocated by this assessment. Those remain required before implementation. No host read/query is necessary to document this source-level gap.

Task 49 remains open for that design and implementation, progress bounds (256 frames/item, 1 MiB encoded total, 32 pending, 4 frames/second, terminal preservation), opaque/observable fixtures, protocol rejection/teardown evidence, Q3 heuristic disclosures/late-warning tests, and UI integration. Tasks 53/54 retain their evidence/review gates. There were no two materially different failed implementation attempts; this is the prompt's capability-assessment stop, not a claim that Task 91's two-attempt trigger occurred.

## Verification

PowerShell 7 is absent. The shared contract permits installed Windows PowerShell with the approved local SDK. The global `dotnet --version` could not locate SDK 10.0.403; using `%LOCALAPPDATA%\Microsoft\dotnet` on this process's PATH and DOTNET_ROOT resolved **10.0.403** without installation or configuration changes.

| Command under that command-only environment | Result |
|---|---|
| `dotnet test tests\PCSteward.Security.FixtureTests\PCSteward.Security.FixtureTests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~UpdateRunnerSecurityTests'` | Exit 0; 6/6 passed, zero skipped. |
| `& .\scripts\Build.ps1` | Exit 0; Release, zero warnings/errors. This baseline build preceded focused tests; no production/test edit occurred. |
| `& .\scripts\Test.ps1` | Exit 0; 293/293 passed, zero skipped: Domain 59, Application 181, Architecture 28, Security 25. |
| Security command above with filter `FullyQualifiedName~ElevatedHelperSecurityTests\|FullyQualifiedName~UpdateRunnerSecurityTests` (literal filter separator `|`, without the Markdown escape) | Exit 0; 21/21 passed, zero skipped. |

Local logs: `%TEMP%\pc-steward-task49-build.log`, `pc-steward-task49-focused.log`, `pc-steward-task49-helper-focused.log`, `pc-steward-task49-test.log`. These checks validate the unchanged baseline only; they do not prove Task 49 telemetry, provider capability, new protocol compatibility or actual UI behavior. No new test was added because no production behavior changed. Diff review and whitespace checks cover only this assessment, STATUS and TRACEABILITY.

No production/source/test/dependency/ADR/prompt change, network request, delegation, worktree, application/provider launch, deployment/signing, live-data access or destructive action occurred. Owner-only/distribution gates remain unchanged.
