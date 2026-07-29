# Task 90 — final Phase 3 review remediation evidence (2026-07-29)

**Branch:** `phase/3-execution`  
**Starting review commit:** `9bc817f5a2da74385b31eb9412086c9e5f112a28`  
**Fixing commit:** this Task 90 commit  
**Model/effort:** GPT-5.6 Sol / Extra High  
**Disposition:** implementation remediation complete; Phase 3 remains gated on a fresh independent Task 25 review.

## Finding disposition

| Finding | Remediation and regression evidence |
|---|---|
| `P3R-15` Blocker | `WindowsElevatedHelperTransport` resolves only `%ProgramFiles%\PC Steward\PCSteward.ElevatedHelper.exe` from the exact sibling desktop path. Before any launcher/`runas` call it rejects missing/reparse components, effective current-token write/delete/DACL/owner access over both files and every ancestor, invalid Authenticode, or exact-signer mismatch; it repeats validation immediately before launch and retains post-connect PID/path/session/SID/signature verification. `PreLaunchTrustRejectsUnsignedWrongSignerWritableReparseAndNonFixedLayouts`, `PreLaunchRejectionAndReplacementNeverInvokeRunasLauncher`, and `CurrentCallerWritableUnsignedFixtureFailsTheProductionTrustChecks` cover every rejection and assert zero launches for initial/replacement failure. |
| `P3R-14` High | `InventoryWorkflow` now requires fresh exact `IStructuredUpdateCandidateAuthority` evidence bound to source, package and a bounded concordant newer version before constructing `UpdateJobSelection`. The table connector does not implement that authority, and actual desktop composition supplies none. `DisplayTableCandidateCannotCreateAJobWithoutExactStructuredAuthority` uses the real WinGet table connector with extra attacker-controlled columns and proves no review/job/runner call; structured wrong-source, wrong-ID, overlong, non-newer and display-mismatch cases fail closed. Localized/extra-column table parsing remains presentation evidence only. |
| `P3R-13` Medium | `UpdateBatchItem` carries the explicit warning acknowledgement. A newly detected final pre-provider target records the warning and returns target-specific `AcknowledgementRequired` before queued/running events or provider execution. `TargetFirstDetectedAtFinalPreProviderSnapshotRequiresANewTargetSpecificReview` proves false/false/true stops, exposes the process name, and only a fresh review with explicit acknowledgement can run. No close/kill authority exists. |

## Reproducible evidence

| Command/evidence | Result |
|---|---|
| `dotnet test .\tests\PCSteward.Application.IntegrationTests\PCSteward.Application.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~InventoryWorkflowUpdateSourceTests|FullyQualifiedName~ReviewedUpdateExecutionWorkflowTests|FullyQualifiedName~SequentialUpdateBatchTests|FullyQualifiedName~WingetInstalledInventoryProviderTests|FullyQualifiedName~DownloadedArtifactCacheTests"` | **56 passed** |
| `dotnet test .\tests\PCSteward.Security.FixtureTests\PCSteward.Security.FixtureTests.csproj --configuration Release` | **19 passed** |
| `dotnet test .\tests\PCSteward.ArchitectureTests\PCSteward.ArchitectureTests.csproj --configuration Release` | **4 passed** |
| `pwsh -NoProfile -File .\scripts\Build.ps1` | **Succeeded; 0 warnings, 0 errors** |
| `pwsh -NoProfile -File .\scripts\Test.ps1` | **176 passed:** Security 19, Architecture 4, Domain 51, Application integration 102 |
| `pwsh -NoProfile -File .\spikes\security-boundaries\tests\SecurityBoundarySpike.Tests.ps1` | **42 assertions passed; 1 approved elevated-only symbolic-link case skipped** |
| `pwsh -NoProfile -File .\spikes\winget-packaging\tests\WingetSpike.Tests.ps1` | **11 assertions passed** |
| Read-only Release trust inspection | `%ProgramFiles%\PC Steward` absent; Release desktop/helper and helper output directory non-reparse, binaries `NotSigned`, each path had one current-token caller-write allow. Production therefore fails before `runas`; no UAC was invoked. |
| `git diff --check` | Passed before commit. |

No WinGet update, installer, UAC prompt, certificate mutation, VHD, provider-cache mutation or non-fixture deletion was invoked.

## Self-audit and residual risk

No Blocker or High issue remains in the remediation diff: remote/display/database text cannot supply a candidate-version argument, and untrusted/caller-writable helper code cannot reach elevation. No remaining Medium finding was identified in the three remediated paths.

The clean signed protected-root/UAC replay requested by `P3R-15` was not safely available. The current build fails closed, and ADR-002/018 now make that installed layout mandatory; the fresh Task 25 reviewer must decide the remaining evidence gate. Existing clean-image, provider-disconnect, same-signer rollback, temporary-export substitution, unconstrained race, process-name correlation and outcome-dialog residuals remain. Do not merge Phase 3 or start Task 26.
