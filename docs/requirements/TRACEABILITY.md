# Task 50 — Part C re-run plan (revision 1, draft; docs and tests only)

`FR-UPD-15`, `TM-03/04/07/13`; [re-run plan](../review/TASK_50_PART_C_RERUN_PLAN.md); [handover](../review/TASK_50_HANDOVER_2026-11-06.md) §5 items 1–2.

- **Plan.** D2 read-only config check, the owner's own `cacheLocation` change (H1, outside PC Steward), CB2 redeploy of `5606da2` with refreshed pins, and C2 (Part C again). Nothing is approved.
- **Tests.** Security `PartCRerunPlanBlockTests`: every PowerShell block is ASCII and parses under Windows PowerShell 5.1 (Windows-only); D2 contains no mutating command and never prints the `cacheLocation` value; the CB2 blocks carry build commit `5606da2…` and old signer `67B59B4C…`, not the previous pins.

# Task 50 — Profile V2 revision 2.1: `containsLegacyPackageInstalls` (C1 fix)

`FR-UPD-15`, `TM-03/04/07/13`; [revision record](../review/TASK_50_PROFILE_V2_LEGACY_KEY_REVISION.md); [Profile V2](../review/TASK_50_CHOCOLATEY_PROFILE_V2.md) revision note; [Part C plan §13](../review/TASK_50_PART_C_PLAN.md).

- **Code.** `ChocolateyConfigurationParser` admits `containsLegacyPackageInstalls` only absent or with exactly `true` (the pinned 2.7.5 template value); any other value refuses. No observation or fingerprint field is added.
- **Tests.**
  - Application `ChocolateyConfigurationParserTests`: 3 admitted forms and 13 refused forms.
  - Application `ChocolateyCommonProfileFingerprintTests`: the key keeps the golden `C179F864…EE77`.
  - Security `ChocolateyProfileRulesTests` (portable, 7 cases): a real-shape 2.7.5 file (`Chocolatey275ConfigurationFixture`) passes the parser and the portable step-6 rules; the other values refuse; empty `cacheLocation` still fails the cache rule.
  - Security `ChocolateyProfileObserverTests` (Windows-only, 3): the real shape is Observed and the key is not a fingerprint field; `false` and an empty `cacheLocation` each refuse at step 6 with `StepRule`.

# Task 50 — Part C step C-A: activation change (ADR-031 decision 7)

`FR-UPD-15`, `FR-UPD-01/03–09`, `FR-HIS-01`, `TM-03/04/07/13`, `AC-UPD-07`; ADR-031 decision 7, ADR-025, ADR-022; [record](../review/TASK_50_ACTIVATION_CHANGE.md) (gate inventory §1).

- **Code.**
  - `ReviewedChocolateyRouteAdmission` (exact shapes) as the single second term of `ReviewedUpdateReview.HasSupportedMode`; `ReviewedRouteTestAdmission` is removed.
  - `DeploymentCapabilityPolicy.CapabilityOperationAllowlist` gains the client route; `PCSteward.DeploymentCapability.txt` (149 B) and the `OwnerOnlyPayloadIntegrity.ps1` token list follow it.
  - `ChocolateyRuntimeLimitsRouteDispatcher.Supports` uses `TryGetRouteProfileDigest`.
  - `New-OwnerOnlyCandidate.ps1`: build record v2 with the probe-build mark. `Test-OwnerOnlyCandidateBuilderCI.ps1` requires it, and the CI job uses `fetch-depth: 0`.
  - New `scripts/evidence/Task50-QualificationProbe.ps1` (S7, C1–C7).
- **Tests.**
  - Architecture: `ChocolateyRouteActivationBoundaryTests` (5); the changed pins in `ClientRunFaultBoundaryTests`, `DesktopProducersBoundaryTests`, `AdmissionGateSeamBoundaryTests` and `DeploymentCapabilityBoundaryTests`.
  - Gate tests on the production rule: `ClientRouteWorkflowAdmissionGateTests`, `ClientRouteSqliteAdmissionGateTests`, `ClientRouteNativeEndToEndGateTests`.
  - Admission behaviour: `AdapterGatesPartATests`, `ReviewedUpdateDispatchWorkflowTests`, `ChocolateyReviewDisclosureTests`, `ReviewedRouteViewModelTests`, `ChocolateyClientUpdatePresentationTests`, `ChocolateyReviewedUpdateArgumentsTests`, `ChocolateyDesktopProducersTests`, `ReviewedHelperProtocol2TransportTests`.
  - Capability: `DeploymentCapabilityPolicyTests`, `DeploymentCapabilityScriptTests`, `DeploymentCapabilityReaderTests`.
  - Probe on the two-token contract: `QualificationProbeWireCodecTests`, `QualificationProbeHelperEndpointTests`, `QualificationProbeDesktopEntryTests`, `QualificationProbeResultFileTests`.
  - Builder: `OwnerOnlyCandidateBuilderTests` (probe-mark rows, CI job, git invocation count).
  - New `QualificationProbeHostToolingTests` with `Fixtures/QualificationProbeHostTooling.ps1`.
- **Review fixes** (record §8; M1, M2, L4–L9):
  - `ReviewedUpdateOperation.IsChocolateyClientIdentity`, shared with `ChocolateyClientReleasePolicy.IsClientTarget`.
  - New `RuntimeLimitRouteActivationTests`: the composition record's L1 obligation through SQLite, near-miss rows and predicate agreement.
  - Probe-script E5, result-folder, capability-pin and build-record-hash rows in `QualificationProbeHostToolingTests`.
  - Builder Git isolation and lineage exit-code rows in `OwnerOnlyCandidateBuilderTests`.
- **Not claimed.** Windows CI evidence for the Windows-only rows; any deployment, probe run or owner admission approval, including the owner's obligation-8 confirmation. Nothing in `src` composes a Chocolatey review or dispatch.

# Task 50 — R2 tooling: fresh install, packaged-process refusal, installer diagnostics (owner decision D-R2)

`FR-UPD-15`, `TM-03`, `TM-04`, `TM-07`, `TM-13`; ADR-022; [recovery plan §3 R2](../review/TASK_50_RECOVERY_PLAN.md).

- Code: `Invoke-OwnerOnlyRedeployCaller.ps1` (fresh-install NewPlan/CheckOnly; packaged refusal before the one-use claim; CheckOnly reports it), `Invoke-OwnerOnlyApprovedRedeployment.ps1` (fresh-install schema, preflight, no removal, re-check; packaged refusal before any mutation), `OwnerOnlyExecution.ps1` (`Assert-OwnerOnlyRedeployPlanV3` also validates `pc-steward-exact-fresh-install-v1`; `Get-OwnerOnlyPackageFullNameResult`, `Assert-OwnerOnlyUnpackagedProcess`, `Assert-OwnerOnlyFreshInstallBaseline`), `Install-OwnerOnlyProtectedTestDeployment.ps1` (`Get-SigningFailureText`; acceptance unchanged), `Copy-OwnerOnlyUserDatabase.ps1` (helper pin).
- Tests: `RedeployToolingScriptTests` (`RedeployToolingFunctionsHoldTheirContracts` fresh/packaged/diagnostic rows, `PackagedProcessRefusalAndFreshBaselineAreBoundAsReviewed`, `TemplateConfinesV3ToPreserveUserDataAndKeepsV1V2DataAbsence`, `CallerAndCopyConstantsPinTheCommittedHelperWaiterAndTemplate`, `ToolingScriptsArePrintableAsciiAndNeverLoadSqlite`) with `Fixtures/RedeployTooling.ps1`; Windows-only rows (real package query, fresh NewPlan/CheckOnly end to end, real fresh program non-elevated, caller main dispatch) on Windows CI.

# Task 50 — data-preserving redeploy tooling (activation gaps G1, G2, G4)

`FR-UPD-15`, `TM-03`, `TM-04`, `TM-06`, `TM-07`, `TM-13`; ADR-022; activation plan §11 G1/G2/G4.

- [Record](../review/TASK_50_REDEPLOY_TOOLING_G1_G2_G4.md): plan schema v3 (`Invoke-OwnerOnlyApprovedRedeployment.ps1`, `OwnerOnlyExecution.ps1`), the caller `Invoke-OwnerOnlyRedeployCaller.ps1`, `Copy-OwnerOnlyUserDatabase.ps1`, and the remover's `PCSteward*` process refusal.
- Tests: `RedeployToolingScriptTests` with `Fixtures/RedeployTooling.ps1` (pwsh and Windows PowerShell 5.1 rows; Windows-only rows on CI), `DeploymentCapabilityPolicyTests`, `DeploymentCapabilityScriptTests`.

# Task 50 — candidate builder (activation gap G3)

`FR-UPD-15`, `TM-03`, `TM-13`; ADR-022 and its protocol-2 alignment note; activation plan §11 G3.

- [Record](../review/TASK_50_CANDIDATE_BUILDER_G3.md): `scripts/New-OwnerOnlyCandidate.ps1` builds the unsigned v2 payload and `candidate-manifest.json`, then self-validates with `Read-OwnerOnlyCandidateManifest` in Windows PowerShell 5.1. It is non-elevated, makes no deterministic-build claim, and the candidate is pinned once by its manifest hash.
- Security `OwnerOnlyCandidateBuilderTests` covers parsing, AST policy, the real build-step, SDK, output-root, manifest-writer and copy functions, and installer-reader acceptance (Windows row). The Windows CI job `owner-only-candidate-builder` runs the builder end to end; first pass on `19bc296` (103 files, manifest `DEB99FF9…4FD7`). Review findings M1–M3 and L1–L5 are dispositioned in the record.
- No host action. B1 (the owner's build on the chosen commit) remains host evidence.

# Task 50 — activation host-session plan (draft)

`FR-UPD-15`, `FR-UPD-01`, `FR-HIS-01`, `TM-03/04/07/13`; ADR-031 decision 7; composition record admission obligations 1–3.

- [Plan rev 1](../review/TASK_50_ACTIVATION_HOST_SESSION_PLAN.md): Part A (confirming Default run, optional H3) reuses script SHA-256 `0A1C4377…84D4` and is ready for approval; Parts B (v2 redeploy) and C (exact-artifact test) are blocked by gaps G1–G9.
- Part A: PASS (result 3, 2026-11-03).
- Plan rev 2 (2026-11-03): Part B (`FR-UPD-15`, `TM-03/04/06/07/13`, ADR-022) as exact blocks on build commit `febf1ac` with LF/CRLF pins for the six protected scripts, template, caller, copy script and builder; approval 1a (B-prep, complete) and 1b (B-exec, host-placeholder template); [B-prep handover](../review/TASK_50_PART_B_LOCAL_AGENT_HANDOVER.md). G6 decided, G7 settled. Docs only; no host evidence yet for Part B.
- Recovery R2/R3 (2026-11-06): `FR-UPD-15`, `TM-03/04/07/13`, ADR-022. The fresh v2 install is Validated and R3 passed (signer `473DB701…`, database unchanged). This closes Part B.
- Recovery plan §6 (2026-11-05): `FR-UPD-15`, `TM-03/04/07/13`, ADR-022. Exact R2-prep/R2-exec/R3 blocks pinned to `385efe0`; R1 PASS recorded.
- B-exec recovery plan rev 1 (2026-11-05): `FR-UPD-15`, `TM-03/04/07/13`, ADR-022 — [plan](../review/TASK_50_RECOVERY_PLAN.md). CI evidence: trusted-root signing is Valid, untrusted is `UnknownError`. Stages R1 (remover, owner console), R2 (fresh-install mode, packaged-process refusal, installer status diagnostics) and R3 (read-only checks); not approved.
- B-exec signing diagnosis (2026-11-05): `FR-UPD-15`, `TM-03/TM-13`, ADR-022. The informational CI job `owner-only-signing-diagnostic` (`scripts/Test-OwnerOnlySigningDiagnosticCI.ps1`) is guarded by `OwnerOnlySigningDiagnosticCiTests`; the hypotheses are in [the failure record](../failures/task-50-part-b-exec.md).
- Plan rev 2.1 / handover rev 2 (2026-11-03): B-prep review disposition (`FR-UPD-15`, `TM-03/04/06/07/13`): child stdin isolation, B0.2 Git stderr capture, B0.1 Git-hook stop, NuGetAudit B1 stop, §10 Git list and approval 1a effects. Docs only.

# Task 50 — ADR-031 client-route admission gates (design §8.6/§8.7)

`FR-UPD-15`, `FR-UPD-01/03–09`, `FR-HIS-01`, `TM-03/04/07/13`, `AC-UPD-07`; ADR-031 decisions 4, 5 and 7.

- Security `ClientRouteWorkflowAdmissionGateTests` (portable 30), Application `ClientRouteSqliteAdmissionGateTests` (portable 4, real SQLite store and history reader), Security `ClientRouteNativeEndToEndGateTests` (3, Windows-only), `ClientRunFaultInterpreterWorkflowTests` (client reply carries no helper check, review M1), architecture `AdmissionGateSeamBoundaryTests`.
- Not claimed: route admission, Windows CI evidence for the Windows-only cases, or owner-host evidence.

# Task 50 — qualification probe slice S4 (desktop side)

`FR-UPD-15`, `TM-03/04/07/13`; [probe spec rev 4](../review/TASK_50_QUALIFICATION_PROBE_SPEC.md) §5.1–§5.6, §6, §7 D1–D9, §11.5.

- Security `QualificationProbeDesktopEntryTests` (portable 14): D7 `Classify` (exact line, flag elsewhere, case/whitespace variants), folder before gate, a folder failure takes no gate and writes no file, GateBusy/GateUnavailable/GateRecoveryRequired/GateResetPending in the file, write before release on the acquiring thread, probe on a worker, faults record nothing, §5.4 exit codes.
- Security `QualificationProbeDesktopTransportTests` (portable 38): steps 3–7 over fakes (D8 process check before any trust call; every token held; no launch before every check passes; a failed final trust after agreeing holds is `LaunchTrustFailed` with every binding released), hold agreement, D3 probe line against all three helper parsers, steps 8–10 over a real pipe pair including the real S3 endpoint (nothing written).
- Security `QualificationProbeResultFileTests` (portable 23, Windows-only 3): D6 closed `:2` document (order, privacy, 4,096-B bound), location rules (8.3, UNC/device/relative, reparse, existing folder); real-folder, junction and short-name writes (Windows).
- Security `QualificationProbeDesktopNativeTests` (Windows-only 5): D9 ledger SHA-256 and signer values; every token held through the real reader to a declined prompt with all bindings released.
- Application `QualificationProbeGateTests` (Windows-only 9): D7 held, abandoned (one acquirer) and thread-affine gate; every non-`None` marker state unsettled with no write.
- Security `DeploymentCapabilityReaderTests` (portable +1): the D9 signer read returns null for any failure and never throws.
- Architecture `QualificationProbeDesktopBoundaryTests` (7) for D1, D2, D3, D4, D5, D7 (including the exact `Run(QualificationProbeLaunchGate.TryAcquire)` call) and the seams; existing pins admit the probe explicitly (`DeploymentCapabilityBoundaryTests`, `HelperEndpointBoundaryTests`, `QualificationProbeWireBoundaryTests`, `QualificationProbeHelperBoundaryTests`); D4 admission-pin tests unmodified.
- S6 integrated review (spec §11.6): Architecture `QualificationProbeReachabilityTests` D1 desktop closure from `QualificationProbeEntry.Run`/`Classify` (the helper bans plus the update transport and pipe writes; exactly one launch, in `LaunchProbeHelper`); Security `QualificationProbeDesktopTransportTests` (+1: `TryTerminate` reports `AggregateException` and the other expected failures as false; the budget margin is computed from `ReviewedHelperProbeBudgets.Production`).
- Not claimed: the real Desktop build, Windows CI evidence for the 17 Windows-only cases, D4 (owner decision), deployment or any host action.

# Task 50 — qualification probe slice S3 (helper side)

`FR-UPD-15`, `TM-03/04/07/13`; [probe spec rev 4](../review/TASK_50_QUALIFICATION_PROBE_SPEC.md) §2, §3, §4.4, §7 H1–H12.

- Security `QualificationProbeHelperEndpointTests` (portable 44): every Q13 row over a real pipe pair, H3 disposal order, H7 inbound byte (27), EOF during A3/B′ (24), A3 cap and spent budget, A3 abandonment ordering (an interrupted run starts no evaluation; an interruption during one refuses the protection call without reaching the item), H10 in-flight disposal and the 26 write window.
- Security `QualificationProbeHelperBootstrapTests` (portable 39, Windows-only 4): H6 grammar, 120 permutations and mutual refusal with all three existing parsers; H8 exits 20/21 (portable) and 22/23 plus native probe-port verdicts (Windows).
- Security `QualificationProbeHelperNativeTests` (portable 7, Windows-only 6): §4.4 native/wire enum pins, H11 one-entry release list, H9 per-instance saturating sinks, probe value shapes, native item seam ordering; H12 walker/ancestry/observer/G1 unchanged with and without the sink and native B′ binding disposal (Windows).
- Architecture `QualificationProbeHelperBoundaryTests` (11) for H1, H2 (including the binding disposed in a `finally`), H3 (Stop order in compiled IL and source), H4, H5, H6, H9/H12, H10 and S4-not-wired; `QualificationProbeReachabilityTests` (1) for H1a, the pinned transitive closure from `RunProbeAsync`/`SelectsProbe`; existing allow-lists grown additively per H4 (`HelperEndpointBoundaryTests`, `NativePrimitivesBoundaryTests`, `QualificationProbeWireBoundaryTests`).
- Not claimed: Windows CI evidence for the 10 Windows-only cases, desktop wiring (S4), deployment or any host action.

# Task 50 — qualification probe slice S2 (probe response codec)

`FR-UPD-15`, `TM-03/04/07/13`; [probe spec rev 4](../review/TASK_50_QUALIFICATION_PROBE_SPEC.md) §4.1–§4.4, §7 S2.

- Security `QualificationProbeWireCodecTests` (87): goldens, round-trips, Q1–Q16 negatives, property mutations, update-frame cross-rejection and byte pins, the 18 `TokenVerdict` wire-name pin, the §4.3 L8 allow-list bound.
- Architecture `QualificationProbeWireBoundaryTests` (4): probe types unreferenced outside the codec, test-only contract factory unused in `src`, no probe request codec, no native/transport reference.
- Not claimed: helper or desktop wiring (S3/S4), Windows CI evidence, deployment.

# Task 50 — host evidence (O5, A-Q1, A2-L3)

`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`; fact extension §10 N5 (O5); adapter gates §A.4 (A-Q1) and §R A2-L3.

- Owner-run probe under [plan rev 2.2](../review/TASK_50_HOST_EVIDENCE_PLAN.md) (script SHA-256 `0A1C4377…84D4`).
- [Raw evidence](../review/TASK_50_HOST_EVIDENCE_RESULT.json) and [interpretation](../review/TASK_50_HOST_EVIDENCE_INTERPRETATION.md).
- Verdicts:
  - O5 PASS-READABLE (spec scope);
  - AQ1N, AQ1M and AQ1C PASS (held main images, managed lazy loads);
  - A2L3S PASS (analogue).
- Eligibility: the installed Chocolatey does not match the pinned 2.7.5 release.
- Still open before admission: the items in the interpretation's open list.

# Task 50 — N5 desktop producers

`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`, `AC-UPD-07`; [design rev 6](../review/TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) §7 and §8 N5; adapter gates §A.6.1 (A2-L6); fact extension rev 3 §4.3 (L5); Task 90 M2.

- Security `ChocolateyDesktopProducersTests` (portable 61): F′ null for every observer refusal kind and inconsistent observation, evidence identity from the fresh fingerprint; review intent as UserCacheIntent with the Profile V2 disclosure; verifier file-first ordering, ClientChanged/Unverifiable details, Success only for the proven expected version, never restart-pending. `ChocolateyProfileRulesTests.TheRegistrationDigestMatchesTheIndependentGoldenVector` (portable). `ChocolateyDesktopProducersNativeTests` (Windows-only 5) over `N2ProfileFixture`.
- Integration `ReviewedUpdateDispatchWorkflowTests`: verification detail → Unverified with the fixed message; the runtime-limit route's restart guard.
- Architecture `DesktopProducersBoundaryTests` (5) and the extended `HelperEndpointBoundaryTests` pins (`Observe`, `TryFromCurrentProcess`).
- Not claimed: Desktop composition, route admission, or Windows CI evidence for the 5 Windows-only tests.

# Task 50 — Desktop review and history UI and the intent resolver (admission obligations 5 and 6)

`FR-UPD-15`, `FR-UPD-01`, `FR-HIS-01`, `TM-03/04/07/13`; [scope](../review/TASK_50_DESKTOP_REVIEW_UI_SCOPE.md); [composition record](../review/TASK_50_REVIEWED_DISPATCH_COMPOSITION.md).

- Application `ReviewedRouteViewModelTests` (portable; includes the disabled-confirm invariant and the accessible-name label rule); Security resolver tests (portable with fakes); architecture pins for the resolver and the Desktop surfaces. Windows CI is green at `58a736c`, including the Desktop build.
- Not claimed: runtime rendering evidence on a real desktop, host evidence, or admission.

# Task 50 — N5 desktop producers and N6 review disclosure and history

`FR-UPD-15`, `FR-UPD-01`, `FR-HIS-01`, `TM-03/04/07/13`; [composition record](../review/TASK_50_REVIEWED_DISPATCH_COMPOSITION.md) N5 and N6; [N6 scope](../review/TASK_50_N6_SCOPE.md).

- N5: `ChocolateyDesktopProducersTests` (portable), `ChocolateyDesktopProducersNativeTests` (Windows-only, 5), the registration-digest golden vector in `ChocolateyProfileRulesTests`, and `DesktopProducersBoundaryTests`. Windows CI is green at `a749dba` and at `edb1d19` (after the review fixes).
- N6: `ChocolateyReviewDisclosureTests`, the history and workflow tests in `ReviewedUpdateDispatchWorkflowTests`, and the non-admission pins in `DesktopProducersBoundaryTests`. Windows CI is green at `83377bc`.
- Not claimed: Desktop UI rendering (N6 review M1), a production intent resolver, host evidence, or admission. See "Admission obligations" in the composition record.

# Task 50 — N4b native port, Helper-role G1 and protocol-2 bootstrap

`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`; [design rev 6](../review/TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) §4, §7, §8, §10 and §11–§15; [composition record](../review/TASK_50_REVIEWED_DISPATCH_COMPOSITION.md) N4b.

- Security `NativeHelperItemTests` (portable 24), `ReviewedHelperBootstrapTests` (portable 26, Windows-only 7), `HelperDeploymentCapabilityCheckTests` (portable 8, Windows-only 12), `NativeHelperItemEndToEndTests` (Windows-only 4). Architecture `HelperEndpointBoundaryTests` and `ClientQueriesBoundaryTests` pin the native port, the composition root and protocol-2 dispatch.
- Windows CI on `c91a0e0` is green (Security 1023/1023, including the 23 Windows-only N4b tests). Independent review: PASS with fixes.
- Review remediation `576063f..10daa47` (Windows CI green, Security 1031/1031): `StandInChocolateyEngineTests`, the native-chain end-to-end test (stage C to Launch with no stage replaced), A3 cap tests, anchored-locator tests, and signature pins in `HelperEndpointBoundaryTests`.
- Not claimed: host evidence with a real Chocolatey release, or admission.

# Task 50 — N0 native launcher

`FR-UPD-15`, `TM-03/07/13`; [design rev 5](../review/TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) §4.1 (H4, L1, rev-3 M-C, rev-4 M1/L4) and §8 N0.

- Security `NativeCommandLineTests` (**portable**, 36): MSVCRT quoting goldens and a 4,000-case round trip through a reference parser; the image is always quoted; NUL, unquotable images and oversized command lines are refused; the environment block is sorted, NUL-separated and double-terminated, and duplicate (case-insensitive), `=`-bearing, empty, control-character or NUL-valued entries are refused; `NativeLaunchSpec.ForTests` copies its inputs and refuses relative, UNC, device, `..`/`.`, forward-slash, quoted, stream, wildcard and trailing-space paths and control-character arguments; `RunCoreAsync` refuses a null spec or non-positive deadline, and a pre-cancelled launch is `NotStarted`+`Cancelled`+settled without any native call.
- Security `NativeLaunchJobTests` (**Windows-only**, 13; `cmd.exe`/`PING.EXE` fixture trees with late-marker grandchildren):
  - a completed child settles with its exit code and both streams, before the settle window;
  - cancellation and the deadline kill the child and its grandchild (`AggregateSettled`, termination exit code, no late marker);
  - a lingering grandchild after a normal exit delays settlement until zero and is killed after the 10 s window (`LingeringTerminated`); the window is clipped to the deadline (`TimedOut`);
  - concurrent launches are independent (cancelling one never affects the other), and a later launch gets a fresh job;
  - a missing image is `NotStarted`; an inheritable test handle outside the handle list is not inherited;
  - output beyond 1,000,000 characters gives `OutputLimitExceeded` with teardown and settlement;
  - the child environment printed by `set` equals the spec exactly (a parent-only sentinel is absent);
  - the test-only fault hook: assign or resume failure gives `NotStarted` with no user code run; teardown failure gives `TerminationFailed`, unsettled, and the kill-on-close backstop still ends the tree.
- Architecture `NativeLauncherBoundaryTests`: no `src` caller of `LaunchJob.RunCoreAsync`, `NativeLaunchSpec.ForTests`, `RunCoreWithFaultForTestsAsync` or `LaunchJobFault` (definitions counted once); Execution never names the launcher; `Native/` uses no environment-variable or registry API, declares only internal types and private launcher/spec constructors, declares no breakaway flag, and keeps the suspended/handle-list/Unicode-environment creation and the exact kill-on-close readback.
- Not claimed: the typed N3 wrapper, `ChocolateyCompiledCommand`, any `src` caller, Windows CI evidence for the 13 Windows-only tests.

# Task 50 — protocol-2 desktop transport and Chocolatey route dispatcher

`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`; adapter gates §A.2, §A.4 steps 1–3 (3.1, 3.4, 3.5), §A.5; reviewed fact extension rev 3 §4.1 (three phases, H) and §6 (budgets, single frame then EOF); review-16 L2.

- Security `ReviewedHelperProtocol2TransportTests`:
  - **Portable** (29): the protocol-2 pipe locator (accepted by the factory, rejected by the protocol-1 bootstrap), and the exchange session over a real named-pipe pair:
    - one frame, then EOF, then exit gives `ResponseFrameReceived` with H, and the helper receives a codec-valid request bound to the operation digest and nonce;
    - a trailing byte, a second frame, closing without a response, an oversized length or a truncated frame gives `RequestWrittenNoValidResponse` (Uncertain);
    - an execution-budget lapse terminates the helper (Uncertain);
    - a valid frame without an observed exit leaves H false (Uncertain), and ownership never completes;
    - a refused or throwing peer check, cancellation after the peer check, or a failed request build writes nothing (the helper sees EOF), giving NotStarted;
    - helper exit before connection, and connection timeout, give NotStarted;
    - a termination failure gives Uncertain.
  - **Portable**, route dispatcher:
    - `Supports` makes no capability call for off-route operations;
    - every non-Supported status gives false, and a throwing seam gives false;
    - one exchange per dispatch with the fresh nonce; the interpreter's workflow observation is returned, including Confirmed;
    - a frame for another nonce is rejected;
    - each pre-request outcome maps correctly;
    - an off-route ticket starts nothing.
  - **Windows-only** (13; reported as failed off Windows):
    - pre-hold failures never launch;
    - with a real held binding: final-trust rejection, helper-path mismatch, pre-launch cancellation, UAC denial, no process, or a launcher exception all release the binding with no helper running; launch happens only after hold and final trust, with the binding held;
    - with a real child process, the binding stays held while the child runs and is released only after the ownership task (exit observed and transport sealed);
    - a non-helper client (different PID) is refused by the real peer check and receives 0 bytes.
- Architecture `DeploymentCapabilityBoundaryTests`:
  - `HeldHelperBinding.TryCreate` only in the reader;
  - the binding is referenced only by four Execution files;
  - exactly two release points in the transport, release-without-helper only before ownership, no `ReleaseHandles` or using-scoped binding, the ownership-continuation release registered before the exchange, and `GC.KeepAlive` after it;
  - the transport and dispatcher are not composed in `src`;
  - no protocol-1 or `ImmutableUpdateJob` use, no database, registry, environment-variable or network input;
  - the dispatcher does no I/O, and `Supports` checks route and evidence before the seam.
- Record: [composition record, protocol-2 transport slice](../review/TASK_50_REVIEWED_DISPATCH_COMPOSITION.md). The helper endpoint (stages A–D, G1) is not implemented, so a real helper rejects `--protocol 2` before connecting. No route admission. No host evidence is claimed.

# Task 50 — adapter gates Part A (Windows slice)

`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`; adapter gates §A.2, §A.4 steps 2–6 and 3.1/3.4/3.5, §A.5, §A.7 (Ledger, Manifest/capability, Binding, Phases no-cache, Peer identity), §R A2-L3/A2-L5.

- Security `DeploymentCapabilityReaderTests`:
  - **Windows-only** (32 cases; reported as failed off Windows):
    - the share-read hold refuses delete, rename, replacement and write, while reads stay possible;
    - `FileIdInfo` same file / replaced / copy / move;
    - the A2-L3 probe while held;
    - the reader end to end: Supported, all leaves held, peer identity and fallback, release on dispose;
    - no cache;
    - 25 fixture-tree status cases;
    - the production protection on a caller-writable tree;
    - a symbolic-link leaf.
  - **Portable:**
    - out-of-allowlist route without I/O;
    - fail-closed layouts;
    - incomplete handle set.
- Security `DeploymentCapabilityPolicyTests`: staged `PassesThrough` agrees with `Evaluate` (11 scenarios) and refuses misuse.
- Architecture `DeploymentCapabilityBoundaryTests`:
  - the single seam implementation;
  - reader input and static-state purity;
  - the protocol-1 transport is not wired, and the reader is not constructed in `src`.
- Record: [composition record, Windows slice](../review/TASK_50_REVIEWED_DISPATCH_COMPOSITION.md). No host evidence is claimed. A-Q1 and G1 remain open.

# Task 50 — deployment capability scripts (ADR-022 alignment note follow-up)

`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`; ADR-022 alignment note; adapter gates §A.3, §A.7, §R A2-L1/A2-L2; follow-up items 1–7.

- Security `DeploymentCapabilityScriptTests` covers:
  - the checked-in capability file equals `CanonicalCapabilityBytes()` and is published at the root as `-text`;
  - the built helper deps.json project closure equals `BindingSet`;
  - the real-writer fixture (pwsh 7; Windows PowerShell 5.1 on Windows CI): every pending, rollback (0/1/several stores), v1 and v2 ledger parses, and they evaluate as Incomplete, Absent and Supported;
  - 30 capability vectors agree between the script parser and `TryParseCapability`;
  - manifest-implied origin and refusals;
  - the profile and provenance refusals;
  - the frozen pre-change validator throws on v2;
  - zero parse errors.
- `DeploymentCapabilityPolicyTests.ExistingV1PlanStaysV1OnlyAndThePreChangeValidatorFailsClosedOnAV2Ledger` covers the A2-L2 template and the installer ordering.
- Windows-only `Fixtures/PayloadIntegrity.ps1` covers v2 read, hold and provenance with real files.
- Record: [TASK_50_DEPLOYMENT_CAPABILITY_SCRIPTS.md](../review/TASK_50_DEPLOYMENT_CAPABILITY_SCRIPTS.md).
- No host or redeploy evidence is claimed.

# Task 50 — adapter gates Part A (portable slice)

`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`; adapter gates §A.2, §A.3, §A.4 steps 3–6 (pure part), §A.6, §A.6.1, §A.6.2, §A.7, and §R A2-L1/A2-L4/A2-L6.

- Domain `ReviewedOperationProfileEvidenceTests` covers the O2 constructor and `TryRestore` negatives (63/65 digits, lowercase, mixed case, v1 prefix, prefix only, trailing character, non-hex, opaque), the 81-character positive, and other routes unchanged.
- Security `DeploymentCapabilityPolicyTests` covers:
  - writer-variant ledger fixtures (pending with a null thumbprint, pending with a certificate, rollback with null/one/several stores, legacy-v6, v1, v2), each with a closed key set and the expected Incomplete, Absent or Supported;
  - ledger negatives, giving Unreadable or Mismatch;
  - manifest and capability pinning, and 22 malformed capability statements;
  - generator bytes (103 B);
  - binding leaves, including Desktop.dll, giving Mismatch or Unreadable;
  - size bounds;
  - the binding set against the helper `ProjectReference` closure;
  - fixed diagnostics;
  - no caching;
  - the v1-only scripts failing closed on a v2 ledger (superseded by the deployment capability scripts entry above: now the frozen pre-change validator).
- Security interpreter tests now show that a malformed runtime-limit EvidenceId cannot be constructed.
- Application `AdapterGatesPartATests` covers:
  - a throwing `Supports` giving NotStarted with no gate or claim;
  - two fresh `Supports` evaluations;
  - post-claim removal giving Cancelled;
  - the A.6.1 matrix, with WinGet `fresh:1` accepted;
  - three-site agreement;
  - A.6.2 stored-row behaviour (draft corrupt; DispatchClaimed and Uncertain block; Final untrusted and non-blocking).
- Architecture `DeploymentCapabilityBoundaryTests` covers:
  - capability types appear only in Execution;
  - pure policy sources that the helper can reuse;
  - every workflow `Supports` call is wrapped;
  - the `Matches` conjunct;
  - no evidence producer reads `.EvidenceId`.

# Task 50 — adapter gates Part B (helper-check history)

`FR-HIS-01`, `FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`; adapter gates §B.2–§B.7, B-M1, B-L1–B-L3, §R B2-L1/B2-L2. Three suites cover this slice:

- `ReviewedHelperCheckHistoryTests` covers:
  - factory R1–R4 and the version form;
  - v2 byte identity against a frozen writer;
  - the maximal v3 frame round trip;
  - every allowed (kind, outcome, settlement), written through the store and read back byte-exactly;
  - store refusal of P-rule, outcome-matrix, WinGet-route and no-grammar writes;
  - forged rows: enumerations, R/P rules, outcome matrix, facts, missing, partial and corrupt;
  - O-none;
  - single-byte tampering with and without a recomputed digest;
  - the version corpus;
  - frozen-v2-parser rollback;
  - store rollback (state, recovery, barrier);
  - exact texts and whole-word forbidden words.
- `ReviewedUpdateDispatchWorkflowTests` (three added) shows that every kind gives an identical outcome and settlement, that the marker survives truncation, and that diagnostics or dispatcher exceptions give NotObserved.
- Security `ReviewedHelperCheckAdapterTests` and the added composition test cover:
  - name mapping;
  - verbatim versions;
  - `WorkflowObservation` appending only the check;
  - the O9 codec/reader differential plus writer/reader agreement;
  - a reflection check that nothing converts to or from provenance;
  - unchanged outcomes through the real workflow.

`HelperCheckHistoryBoundaryTests` checks:
- a single `FromDesktopDerived` call site;
- that `TryRestore` and the terminal record stay in history files;
- that no review, admission, evidence, reconciliation or Domain file references the check;
- grammar injection in every production `SqliteDatabase` and an internal no-grammar constructor.

The existing interpreter boundary test is unchanged. Part A is not covered.

# Task 50 — protocol-2 desktop interpreter (passive)

`FR-UPD-15`, `FR-UPD-01`, `AC-UPD-05/07`, `TM-03/04/07/13`: `ReviewedUpdateProtocol2InterpreterTests` and `ReviewedUpdateProtocol2WorkflowCompositionTests` cover each P-predicate violation (including recomputed values), Matched with each mismatching field, refusal predicates, flag precedence, settled-without-H, Unknown invocation, launch failure, N1–N4, the "confirmed" phrase in a diagnostic, RestartReported always false, correlation, pre-request phases, and Success only after verification. `Protocol2CandidateCheckBoundaryTests` checks construction confinement. Interpreter only; no endpoint, history format, verifier or admission.

# Task 50 — protocol-2 reviewed fact extension: passive codec slice

`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`: `ReviewedUpdateWireCodecReviewedFactTests` (126 cases) and the updated `ReviewedUpdateWireCodecTests` (23) cover the codec-level §8 cases of the [specification](../review/TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md):
- the exact 20-field order and encoding;
- rejection of an explicit 15-field legacy protocol-2 frame (O1);
- each field missing, swapped or null;
- non-canonical and mistyped frames: escapes, whitespace, comments, BOM, lone surrogates, numbers and strings swapped, the `exitCode` forms `2147483648`, `0.0`, `-0` and `1e0`, booleans as `"false"`, `0` or `1`, trailing bytes and a second frame;
- exact enum round-trip, with case, numeric, flag-list and unknown aliases rejected;
- 128/64 boundaries and +1, non-normalized versions and malformed fingerprints;
- one negative case per S1–S13 predicate with its exact violation set, plus positive cases;
- a byte-exact 5,083 B largest valid frame and rejection of over-bound frames;
- the O9 differential against `ChocolateyNativeVersion`.

See the [slice note](../review/TASK_50_PROTOCOL2_CODEC_SLICE.md) for the O9 choice and the deferred cases. This is a passive codec only: no endpoint, adapter, route admission or `AC-UPD` closure.

# Task 50 — protocol-2 reviewed fact extension (design gate)

`FR-UPD-15`, `FR-UPD-01`, `AC-UPD-01–11`, `TM-03/04/07/13`: [specification](../review/TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md) passed independent design review at revision 3 after two CONDITIONAL rounds. §8 lists the negative test matrix the implementation must satisfy, and §10 the binding obligations. This is design evidence only: no endpoint, route admission or `AC-UPD` closure.

# Task 50 — common-gate caller conversion

`FR-UPD-15`, `TM-04/06/07`, reset design §4.3: `DataOperationLeaseTests` covers mutation needing a clear reconciliation while reads proceed, an unreadable barrier blocking mutation only, pending reset refusing before reconciliation, missing configuration failing closed, reset-control semantics, concurrent-holder busy/release, and bootstrap session reuse without recursive acquisition. The rewritten `AsyncDataOperationLeaseBlocksResetUntilTheOperationCompletes` uses test gates. `DesktopBootstrapAndResetUseTheCommonGateWithReconciliation` checks the ordering marker → SQLite → reconcile → MainWindow. Windows CI is authoritative for the native gate and Desktop build.

# Task 90 — Task 50 dispatch review remediation

`FR-UPD-15`, `AC-UPD-05`, retained `FR-UPD-01/14`, `TM-03/04/06/13`: [remediation](../review/TASK_90_TASK_50_DISPATCH_REVIEW_REMEDIATION.md) maps each independent finding to its fix and test. Covered: ElevationDenied/Refused distinct outcomes, started-timeout/failure as Unverified, RestartReported verification, a 20,000-char diagnostic bound, committed-without-permit finalized NotInvoked (expired/stale/cancelled), unreadable state as retained, gate-exit failure preserving the result, unreconciled ownership, Chocolatey routes unreachable, and store rejection of NotInvoked Unverified. Linux focused 124/124; Windows CI and fresh re-review pending.

# Task 50 — reviewed history projection (mode survives history)

`FR-HIS-01`, `FR-UPD-15`, `TM-07/13`: `ReviewedUpdateHistoryTests` (10) covers frozen mode/route/guard/disclosure and outcome restored from claim rows, claimed/Uncertain rows blocking, new-route-only disclosure and provisional-intent wording, tamper/corruption rows as unreadable-and-blocking, strict terminal parser round-trip/malformed rejection, and never displaying helper qualification. Linux focused 10/10; Windows CI pending. Passive projection only.

# Task 50 — reviewed dispatch composition (application layer)

`FR-UPD-15`, retained `FR-UPD-01/08/09/14`, `TM-03–04/06/07/13`: `ReviewedUpdateDispatchWorkflowTests` (35 cases over real SQLite claims with fake dispatcher/evidence/verifier) covers: verification-only Success/RestartRequired/Unverified classification; unsupported route never entering the gate; mode/route/selection/native/actionable/stale fresh-evidence mutation rejected before claim with the approval spent; expired review; busy gate; cancellation; review-deadline-bounded wait; lost permit retaining the claim barrier; unknown invocation and nine unsettled-work variants recording Uncertain without awaiting the root; settled non-success never Success; proven not-started refusals Final without verification; verification failure Unverified; final-write failure Uncertain with retained claim; and one approval dispatching at most once. Linux focused 99/99; Windows CI pending. Composition evidence only: no production dispatcher, native eligibility, `AC-UPD-01–11` or Task 50 exit claimed.

# Task 50 — runtime-limit route, guard contract and disclosure (cloud; Windows gate pending)

`FR-UPD-15`, retained `FR-UPD-01`, `TM-07/13`: `ReviewedProviderGuardContractTests` covers canonical identity/digest distinction from the older provisional route, restore, and provider/source/scope plus schema-2 recomputed-integrity negatives. `ChocolateyReviewedUpdateArgumentsTests` covers new-route-only formatting, older provisional rejection, frozen-clock disclosure that still cannot confirm, and older/legacy disclosure negatives. This resolves the earlier "owner choice pending" wording below: the owner accepted the disclosed runtime limits for this route only, conditional on the fixed official Community feed. Linux focused Domain 67/67, Application 42/42; canonical Windows Release/full not run. Passive data/disclosure only: no native eligibility, `AC-UPD-01–11` or Task 50 exit is claimed.

# Task 50 — revised mapping policy and concrete guard gap

`FR-UPD-15`, retained `FR-UPD-01/08/09/14`, `TM-03–04/06/07/13`: [Task90 record](../review/TASK_90_TASK_50_CHOCOLATEY_PROFILE_V2_REMEDIATION.md) documents enabled-source precedence, fixed immutable argument formatting, fingerprint golden encoding and first-file-check client/registration drift handling with63 focused cases. Release clean/full900/900 zero skipped. [Native refresh guard packet](../failures/task-50-chocolatey-refresh-guard.md) records the source-verified runtime checksum/argument bypass and joint dialogue; required owner choice remains pending. No native qualification/route or full `AC-UPD-01–11`/Task50 acceptance follows from passive/fake positives. Remaining common-gate/claim/helper/runner/UI/history and native observations stay open.

## Earlier revised native mapping and release identity data

`FR-UPD-15`, retained `FR-UPD-01/08/09/14`, `TM-03–04/07/13`: [proposed native mapping revision2](../review/TASK_50_CHOCOLATEY_PROFILE_V2.md) incorporates the explicit owner candidate amendment and conditional-review corrections. [Release manifest](../review/TASK_50_CHOCOLATEY_RELEASE_2_7_5.json) records56 mapped root/helpers/tools files,15,258,758 bytes, rechecked against the exact public asset. This is source/artifact mapping data, not source-build attestation or host eligibility. No code changes since repaired5418c84's clean Release/full837/837; native/mapping review, production observation/composition and full `AC-UPD-01–11`/Task50 exit remain open.

## Earlier provenance/archive/ScheduledHost review repair

`FR-UPD-15`, retained `FR-UPD-01/14`, `TM-03–04/07/13`: [Task90 evidence](../review/TASK_90_TASK_50_PREREQUISITE_REMEDIATION.md) records conditional review at74e1803, OPC escaped-payload support with decoded alias refusal, shared bounded native ID grammar, scoped provisional route labels, distinct unresolved scheduler code and added layout negatives. Focused98/8/5, Release clean/full837/837 zero skipped. Corrects the review's a_ and ID-trimming examples against pinned source. No native registration/candidate authority, fresh review pass or full `AC-UPD-01–11`/Task50 acceptance follows; integration remains open.

## Earlier ScheduledHost common-gate composition

`FR-UPD-15`, retained `FR-UPD-01/14`, `TM-03–04/07/13`: [composition evidence](../review/TASK_90_TASK_50_CHOCOLATEY_MAPPING_REMEDIATION.md) converts the scan-only ScheduledHost to the common owner-thread session, reset-before-SQLite preflight and durable claim reconciliation. Existing behavioral gate/claim fixtures72 and production source-boundary fixtures5 pass; Release clean/full806/806 zero skipped. No host scheduler execution was performed. Desktop/reset callers and endpoint/native-profile/UI/history integration remain open; this is not complete cross-session enforcement or full `AC-UPD-01–11` acceptance.

## Earlier bounded native package archive identity

`FR-UPD-15`, retained `FR-UPD-01/14`, `TM-07/13`: [native mapping evidence](../review/TASK_90_TASK_50_CHOCOLATEY_MAPPING_REMEDIATION.md) adds bounded ZIP structural/entry selection and nuspec-byte parsing without extraction. Stored/deflate/descriptor positives; ambiguity, duplicates, encoding, record/count/offset/size/CRC/XML and decompression negatives pass25/25. Release clean/full805/805 zero skipped. Parser success is neither a package-signature claim nor installed/source/candidate eligibility; native/profile/endpoint/UI/history integration and full `AC-UPD-01–11` remain open.

## Earlier provisional candidate contract and bounded nuspec parsing

`FR-UPD-15`, retained `FR-UPD-01/14`, `TM-03–04/07/13`: the [owner amendment](../failures/task-50-pre-uac-candidate-evidence.md) permits provisional candidate intent only for the new Chocolatey route, with independently qualified exact-match helper execution required. A distinct canonical route/closed data label prevents silent reinterpretation; current confirmation/fresh-evidence flags cannot admit it. Nuspec24/provenance3/admission1 focused cases, Release clean, full780/780 zero skipped. [Evidence](../review/TASK_90_TASK_50_CHOCOLATEY_MAPPING_REMEDIATION.md). These are passive prerequisites; full `AC-UPD-01–11`, native profile, endpoint/UI/history integration and Task50 exit remain open.

## Earlier native version/store comparison data

`FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13`: [native-source mapping remediation](../review/TASK_90_TASK_50_CHOCOLATEY_MAPPING_REMEDIATION.md) records normalization, native legacy ToString semantics and ordered store alternatives, with unsupported/metadata/path/device negatives. Focused23/23, Release clean, full752/752. It does not observe filesystem state, merge duplicate registrations or create execution authority; full `AC-UPD-01–11`/Task50 acceptance remains open.

## Earlier bounded native configuration parsing

`FR-UPD-15`, retained `FR-UPD-01/14`, `TM-03–04/07/13`: [mapping remediation](../review/TASK_90_TASK_50_CHOCOLATEY_MAPPING_REMEDIATION.md) records exact native XML shape/default/explicit semantics, immutable passive observations, secret-attribute omission and strict ambiguity/resource negatives. Focused28/28, Release clean, full729/729. Parser success is not native trust or compatible policy; no production caller, full `AC-UPD-01–11` or Task50 exit is claimed.

## Earlier compiled Chocolatey bootstrap environment

`FR-UPD-15`, retained `FR-UPD-01/14`, `TM-03–04/07/13`: [conditional mapping remediation](../review/TASK_90_TASK_50_CHOCOLATEY_MAPPING_REMEDIATION.md) records a fixed environment set, complete inherited-block replacement and format/injection negatives. Focused14/14, Release clean, full701/701. Public release ZIP/engine hashes are artifact evidence only, not source-build attestation or host eligibility. No production caller, native trust/profile or integrated `AC-UPD-01–11`/Task50 acceptance follows.

## Earlier fixed reviewed User arguments

`FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13`: [fixed mapping](../review/TASK_50_PROVIDER_MAPPING_V2.md) pins Silent/MSIX and closed user/source/architecture values. Fifteen focused cases cover exact arguments and recomputed-digest mode/provider/source/scope/architecture/route/schema negatives. Release clean/full687/687. This formatter is passive with no process or approval API; native targeting/Update-only/identity proof and integrated `AC-UPD-01–11`/Task50 acceptance remain open.

[Proposed Chocolatey native profile](../review/TASK_50_CHOCOLATEY_PROFILE_V1.md) now allocates concrete numeric observer/archive/XML bounds and independent producer/helper responsibilities for the same requirements. It awaits mapping review and remaining primary-source facts; it supplies no additional implementation or host evidence.

## Earlier observed native query handoff

`FR-UPD-15`, retained `FR-UPD-01/14`, `TM-03–04/07/13`: [entry evidence](../review/TASK_50_ENTRY_ASSESSMENT.md) records passed independent prerequisite recheck, preserved Unicode redactions, and real synthetic-process proof that source queries retain complete or uncertain ownership through the existing bounded reader. Focused codec23/query42; Release clean/full672/672. Neither codec nor guard is connected to an app update route; source/client qualification and full AC-UPD/Task50 acceptance remain open.

## Earlier bounded CLI source observation

`FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/13`: [mapping evidence](../review/TASK_50_PROVIDER_MAPPING_V2.md) traces export and execution to the same pinned effective-source lookup and records structured endpoint/identity checks rather than name-only trust. Fixed arguments, byte/depth bounds, source substitution/ambiguity, cancellation and retained unsettled roots are covered by34 focused fixtures. Release clean/full661/661. The isolated guard has no production caller and grants no execution authority; source-change race, native provenance and remaining `AC-UPD-01–11`/Task50 integration evidence remain open.

## Earlier conditional prerequisite review remediation

`FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13`: [R1–R5 remediation](../review/TASK_90_TASK_50_WIRE_REMEDIATION.md) covers diagnostic confidentiality/idempotence, exception-safe rejection of malformed JSON names, true result flags, exit-code/observation consistency and historical-data semantics. Synthetic red reproduced the defects; focused20/20, Release clean, full627/627. Conditional independent review still needs its fresh recheck; no native or full `AC-UPD-01–11`/Task50 acceptance follows.

## Earlier typed architecture/schema prerequisite

`FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13`: [mapping contract](../review/TASK_50_PROVIDER_MAPPING_V2.md) records explicit architecture through canonical review, separately fresh evidence, draft generation and claim CAS. Schema-2 data is passive-only; migration rollback and unresolved-claim preservation are covered. Initial red 12/52; focused Domain62/App94; final Release zero warnings/errors, full620/620. These are prerequisite fixtures, not native qualification, provider enforcement, history UI or `AC-UPD-01–11`/Task 50 exit evidence.

## Earlier isolated canonical wire prerequisite

`FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13`: [revision-2 mapping](../review/TASK_50_PROVIDER_MAPPING_V2.md) and [entry evidence](../review/TASK_50_ENTRY_ASSESSMENT.md) record fresh-review corrections, deterministic frame parsing, bounded ASCII diagnostics, exact correlation and contradictory/unknown invocation/settlement handling. Focused 13/13, Release clean, full 603/603. No endpoint consumes the new passive codec; schema-3 and native profile/runner/helper/common-gate/UI integration remain prerequisites. `AC-UPD-01–11` and Task 50 exit are not closed.

# MVP requirement traceability

**Task 50 — native observation handoff:** `FR-UPD-15`, retained `FR-UPD-01/08/09`, `TM-03–04/07/13`: [entry record](../review/TASK_50_ENTRY_ASSESSMENT.md) demonstrates immutable direct scan-to-review/verification data instead of re-reading writable display rows. Meaningful storage-substitution negatives cover changed reviewed installed version and fabricated verified target version; focused25/25, Release clean, full590/590. [Proposed mapping revision1](../review/TASK_50_PROVIDER_MAPPING_V1.md) awaits fresh review; no provider/profile/helper/AC-UPD closure inferred.

**Task 50 — settlement prerequisite:** `FR-UPD-15`, retained `FR-UPD-01` and honest-outcome safety, `TM-03–04/07/13`: [entry evidence](../review/TASK_50_ENTRY_ASSESSMENT.md) records one-use claim/ownership-bound finalization, immutable native-observation snapshots, unresolved Final rejection and durable-barrier retention after persistence failure/late completion. Focused 94/94; Release clean; full 587/587. Native fact production, provider execution and full AC-UPD/Task 50 acceptance remain open.

**Task 50 — shared lease composition prerequisite:** `FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13`: [entry record](../review/TASK_50_ENTRY_ASSESSMENT.md) covers Global-then-Local acquisition, owner-thread async lifecycle, mandatory reconciliation, raw/unreconciled refusal and marker-before-store preflight. Focused 80/80; Release zero warnings/errors; full 573/573. Same-session synthetic evidence does not establish cross-logon integration; existing production callers remain unchanged. No Task 50 exit/AC-UPD closure is claimed.

**Task 50 — draft/claim lifecycle prerequisites:** `FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13`: [entry evidence](../review/TASK_50_ENTRY_ASSESSMENT.md) records generation-bound review, exact-byte/generation CAS, separately fresh evidence, full synchronous commit, bounded terminal reservation, process-local one-use permit and Uncertain recovery/barrier preservation. Focused 61/61; Release zero warnings/errors; full 560/560, zero skipped. These support immutable review/dispatch safety; `AC-UPD-01–11` and full Task 50 execution/history exit remain unclosed pending production gate/runner/helper/provider/UI integration and independent evidence. No host action or approval inferred.

**Task 50 — live ownership/confirmation prerequisites:** `FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13`: active current-SID production-gate context, recovery/release rejection, explicit non-restorable confirmation, complete original-object binding, one-use claim attempt and nonrenewing 30-minute UTC/monotonic lifetime. [Evidence](../review/TASK_50_ENTRY_ASSESSMENT.md): combined 36/36, codec 52/52, Release zero warnings/errors, full 523/523. Scope clarification is accepted; claims/permits and execution integration are still absent, so no replay/helper/provider criterion closes. Actual native evidence and [Chocolatey contract](../review/TASK_50_CHOCOLATEY_CAPABILITY.md) remain gated.

**Task 50 — accepted gate population clarification:** `FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13`: owner selected the single protected deployed build, with legacy standalone coexistence unsupported. [Decision](V2_PLANNING_DECISIONS.md) / ADR-025 scope the participating population without claiming legacy exclusion or waiving common ownership, dispatch claims, replacement/scheduled-entry and exact-host evidence. Prerequisite checkpoint 57d36b6 remains partial; no execution acceptance closes from this choice.

**Task 50 — canonical persistence prerequisite:** `FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13`: strict restore validates bounded UTF-8, exactly 24 canonical fields, schema/discriminator, closed mode shape and digest without creating consent. Migration 9/passive store preserves legacy rows and refuses corruption, substituted job identity, conflicting saves, partial migration and future-schema migration/new-store writes. [Evidence](../review/TASK_50_ENTRY_ASSESSMENT.md): codec 51/51, new store 10/10, combined storage 29/29, Release zero warnings/errors, full 502/502. No claim, permit, runner/helper mode enforcement or history-UI acceptance is inferred; actual data/host gates remain unchanged.

**Task 50 — canonical reviewed identity prerequisite:** `FR-UPD-15`, retained immutable review `FR-UPD-01`, `TM-03–04/07/13`: `Domain/ReviewedUpdateOperation.cs` deterministically binds schema/Update discriminator, job/correlation, requester/review time, full selection, native-instance/context identity, observed evidence and mode/route. [Mutation/encoding evidence](../review/TASK_50_ENTRY_ASSESSMENT.md): canonical 35/35, gate 16/16, mode 10/10; Release zero warnings/errors, full 476/476. Recomputed digests of changed identities compare unequal; delimiter/length/UTC/byte-ownership checks pass. This type is passive data: no persisted approval, runner/helper enforcement or live dispatch permission is inferred, and all integration acceptance remains open.

**Task 50 — secured gate prerequisite:** `FR-UPD-15`, retained `FR-UPD-01`, `TM-03–04/07/13` / ADR-025: `Storage/CurrentUserOperationGate.cs` validates explicit current-user Global-object owner/protected DACL before ownership, bounds/cancels acquisition and distinguishes recovery-required abandonment. [Source/fixture evidence](../review/TASK_50_ENTRY_ASSESSMENT.md): gate 16/16, Release zero warnings/errors, full 441/441. Synthetic same-session two-process contention is proven; actual cross-logon coverage, mixed-version exclusion, production entrypoint migration, reconciliation/claims and execution integration are not proven or enabled. No requirement closes merely from this isolated primitive.

**Task 50 — closed mode data prerequisite:** `FR-UPD-15` / retained immutable-review safety: `Domain/ReviewedInstallerMode.cs` has only Silent/Interactive selections or explicit provider-script-controlled absence of mode control, with private construction/get-only fields and unknown-enum rejection. [Evidence and integration limits](../review/TASK_50_ENTRY_ASSESSMENT.md): red 9 failed/1 passed, green 10/10, Release zero warnings/errors, full 425/425. No provider mapping, runner/helper enforcement, serialization/history migration, live approval or dispatch claim is implemented by this standalone type; acceptance stays open.

**Task 50 — entry/design assessment:** `FR-UPD-15`, retained `FR-UPD-01`, `AC-UPD-01–11`, `TM-03–04/07/13`: [source/provider assessment and consultation](../review/TASK_50_ENTRY_ASSESSMENT.md) records missing immutable mode, existing protocol-1 payload and unresolved supported-mode/legacy-authority interpretation. Baseline Release zero warnings/errors, focused 6/6 and full 415/415 pass, zero skipped; no Task 50 behavior criterion is closed. Concrete mapping/schema review, implementation/migration/mutation fixtures and actual host evidence remain open. No host action or new provider capability is authorized by this assessment.

**Task 49 bounded launch:** `FR-UPD-14`, retained update/UI/observability and `TM-04/TM-06`: [host evidence](../review/TASK_49_BOUNDED_LAUNCH.md) records one normal exit0/38,007ms/zero residual, owner-attested Overview, unchanged live/copied DB/integrity ok/FK0/schema1–8/empty non-migration tables and final installed validator Pass. Automated visual capture unavailable; original observer FailedOrUnknown retained. This is bounded owner observation, not broad FR-UI/accessibility or real helper-UAC/update-progress acceptance.

**Task 49 actual protected recovery:** `FR-UPD-14`, retained `FR-UPD-01–11`, `FR-UI-01–07`, `NFR-OBS-01–05`, `TM-04/TM-06`: [host evidence](../review/TASK_49_RECOVERY_EXECUTION.md) records one native/protected exit0, independent full validator Pass, 102 files/100 exact non-signed/two signed images/six XBFs, signer replacement and unchanged database/backup. This closes the partial-deployment recovery only; app rendering, real update/helper-UAC/progress/cancellation and phase/distribution gates remain open.

**Task 49 host attempt 2:** `FR-UPD-14`, retained update/preservation controls `TM-04/TM-06`: [failure evidence](../failures/task-49-host-replacement.md) records exact protected phase27 removal failure and a partial old install (94 retained hashes match; eight resources missing). Database/backup/ledger unchanged; no candidate installation/launch/update. [Exact recovery preparation](../review/TASK_49_PARTIAL_RECOVERY_PLAN.md) passes 11/11 recovery and 19/19 wrapper fixtures; loaded-image/deny-delete probes reject with unchanged hashes/LastWriteTimeUtc and Opus dialogue conditions are met. Recovery and actual-host checks remain open; no retry authorized.

**Task 49 host attempt:** `FR-UPD-14`, retained `FR-UPD-01–11`, `TM-04/TM-06`: [stopped replacement](../failures/task-49-host-replacement.md) records native exit 1, offline pre-removal ACL rejection and revalidated unchanged old deployment/database. Corrected local caller/wrapper pass 19/19 schema/wrapper and 15/15 result-ACL checks; three-round Opus dialogue resolved the correction. No replacement/launch/helper/provider evidence or retry; host gate remains open pending a fresh one-attempt approval.

**Task 49 host preparation:** `FR-UPD-14`, retained `FR-UPD-01–11`, `FR-UI-01–07`, `NFR-OBS-01–05`, `TM-04/TM-06`: [host-check plan](../review/TASK_49_HOST_CHECK_PLAN.md) pins the reviewed replacement candidate, current installed validator and unchanged copied-database preservation. Release zero warnings/errors; 415/415 pass. Actual replacement/WinUI/UAC/provider/cancellation observations remain unproven and separately approval-gated; no Task 53/54 or distribution gate closes.

**Task 49 — upgrade/helper ownership and local admission:** `FR-UPD-14`, retained `FR-UPD-01–11`, `NFR-OBS-01–05`: [red/green ownership checkpoint](../review/TASK_49_DESIGN_DIALOGUE.md) maps partial-output/EOF/cancellation/timeout/overflow, kill failure and retained cleanup roots to atomic local admission before Queued. Helper disposal requires observed exit plus transport completion; metadata reaches Application without entering helper protocol. Incomplete/uncertain zero-exit results remain verification-required. Release zero warnings/errors, full 335/335, zero skipped. Progress integration/UI/Q3/compatibility and full Task 49 completion remain open; no cross-session/restart barrier, privileged-host execution or distribution gate is claimed.

**Task 49 — redirected reader prerequisites:** `FR-UPD-14` / cancellation/teardown: [synthetic process evidence](../review/TASK_49_DESIGN_DIALOGUE.md) covers real redirected stdout/stderr FileStream reads with a surviving writer after direct-parent exit. Cancellation completes the read and releases the subsequent byte to a fresh read; disposal alone does not complete it within two seconds. Focused 4/4, Release zero warnings/errors, full 313/313 pass. This verifies a local runtime prerequisite, not the unfinished production teardown/ownership/latch or external installer termination; Task 49 remains incomplete.

**Task 49 — bounded mailbox component:** `FR-UPD-14` / Q1: [component evidence](../review/TASK_49_DESIGN_DIALOGUE.md) covers typed job/correlation isolation, route phases, terminal preservation, conservative canonical-byte reservation, 255/256 attempt boundary, 16 KiB/1 MiB/32-pending quotas and monotonic four-per-half-open-second consumption. Focused 14/14, Release zero warnings/errors, full 309/309 pass. This is a pure component exercised through direct API fixtures, not helper-wire or provider capability; runner/coordinator/Desktop integration, overflow operation cancellation, owned-work safety, Q3, logging and full Task 49 completion remain open.

**Task 49 — empirical pipe evidence:** `FR-UPD-14` / cancellation/teardown validation: [dialogue record](../review/TASK_49_DESIGN_DIALOGUE.md) separates Opus/Fable/Astra design agreement from actual anonymous-pipe cancellation evidence. An empty read remained pending for 100 ms and completed Cancelled before writer closure on the installed .NET 10 runtime. Focused 1/1, Release zero warnings/errors, full 295/295 pass, zero skipped. This is a prerequisite probe, not proof of redirected process readers, provider/helper exit, external work stopped, production ownership or full Task 49 completion. Existing protocol and installed artifact remain unchanged.

**Task 49 — first implementation correction:** `FR-UPD-14` / retained `FR-UPD-01–11`, `FR-UI-01–07`, `NFR-OBS-01–05`: [dialogue and evidence](../review/TASK_49_DESIGN_DIALOGUE.md) supersedes the categorical new-protocol blocker. Existing protocol 1 can carry unchanged execution while desktop-local milestones remain explicitly indeterminate. A failed-dispatch regression exposed a premature observed-start diagnostic; corrected new Running entries describe dispatch request only. Focused 14/14, Release zero warnings/errors, full 294/294 pass. No schema/old-history/protocol change; Task 49 remains incomplete for telemetry/UI/Q3/teardown, and no review/host/distribution gate closes.

**Consultation participation clarification (2026-10-29):** Owner instruction keeps Opus 5.5 involved alongside the main agent, Fable 5.1 and ChatGPT Astra 6 throughout escalation, including replies to findings and validation results. `AGENTS.md` is the workflow authority. No product requirement or Task 49 gate is closed.

**Consultation escalation update (2026-10-29):** Owner-requested `AGENTS.md` amendment adds both Fable 5.1 and ChatGPT Astra 6 to the joint dialogue when the main agent and Opus 5.5 remain unable to solve a problem. Evidence exchange, validation and retained approval boundaries apply to every participant. Engineering workflow only; no product requirement or Task 49 acceptance result changes.

**Project workflow update (2026-10-29):** Owner-requested `AGENTS.md` rule requires iterative Opus 5.5 consultation when any agent gets stuck, with recorded evidence/validation and existing safety/owner gates preserved. This is an engineering instruction, not a product requirement amendment or acceptance result; Task 49 remains incomplete.

**Task 49 — entry assessment (2026-10-29):** `FR-UPD-14`, retained `FR-UPD-01–11`, `FR-UI-01–07`, `NFR-OBS-01–05`: [capability assessment](../review/TASK_49_ENTRY_ASSESSMENT.md) confirms accepted planning but records unproven ADR-025 independent new-protocol machine Update evidence/mode enforcement. Task 49 is started/incomplete, superseding unstarted wording below; no feature criterion closes. Baseline `fbf1835`: helper/runner 21/21, Release zero warnings/errors, full 293/293 pass. No production/test/protocol/deployment change; Task 50 and distribution gates remain unchanged.

**Task 90 follow-up — current:** `TM-04`/`TM-06` production guards remain unchanged. Hosted standard-user logon succeeded; the payload fixture's missing Utility function is addressed through explicit host-module import and a red/green missing-function regression. [Evidence](../review/TASK_90_PR4_CI_REPAIR.md): focused 3/3, Release zero warnings/errors, full 293/293 pass; hosted rerun pending approved push. Earlier pending-logon wording below is historical.

**Task 90 — PR #4 CI repair:** `TM-04`/`TM-06` privileged-compilation protections stay unchanged. [Evidence](../review/TASK_90_PR4_CI_REPAIR.md) maps the hosted administrator-token failure to the standard-user CI launcher and local-host refusal regression. Local payload 85 assertions, focused 2/2, Release zero warnings/errors and full 292/292 pass; hosted logon/rerun awaits approved push. No product/distribution gate closes.

**Task 48 COMPLETE — current authoritative result:** [Completion/evidence](../review/TASK_48_COMPLETION.md) closes this exact owner-only task after fresh explicit launch approval: expected Overview/Ready screenshot/accessibility, **45 responsive samples**, accepted normal close, **native exit 0 / 22,826 ms**, no residual PCSteward process. Approved fresh DB is **159,744 bytes**, original/copy SHA `CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`; copy-only integrity **ok**, foreign-key errors **0**, migrations **1–8**, all **16 non-migration tables empty**, originals unchanged. Final installed validator **Pass / pinned-unsigned-v1**, unchanged ledger and all **102 paths / 100 non-signed hashes / two signed-image reconstructions**; all16 code/test/program pins unchanged. Existing 85 assertions/full291/Release0warnings remain code-gate evidence. Requirements `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16` are addressed within retained owner-only scope, without populated-data/destructive-reset or distribution claims. Earlier pending/failed launch records below are historical; both separately approved attempts remain preserved. Shared-runtime/account trust, temporary signer expiry and compiler audit limits remain disclosed. **Task 49 is unstarted; no distribution gate closes.**

**Current Task 48 second-launch readiness:** Completed Opus consultation agrees with missing .NET host diagnosis and unchanged fresh launch. [Independent runtime record/exact proposal](../review/TASK_48_LAUNCH_RUNTIME_STOP.md): registered global x64 NETCore.App 10.0.14; three Valid Microsoft runtime signatures; six targeted ACL checks; no DOTNET override names across three scopes; Windows App Runtime 1.8 x64 status Ok. Global runtimes are outside pinned app provenance; Main/DDLM/bootstrap execution remains uncertain and a prerequisite dialog fails/stops without installation/retry. App validator still Pass, data absent/no processes; original failed run retained. New observer hash `36E7266125C69A49D53A0F33721602DB640561B94F2009CDFB68B4A15422C0BA` and fresh evidence path prepared, **not executed or yet approved**. Fresh approval is the remaining authority gate; successful overview/normal-close/copied-database evidence is still uncollected. Earlier consultation-pending passages are historical. No Task 49 or distribution gate closes.

**Latest Task 48 launch result:** After separate approval, the exact non-elevated apphost exited itself in **8,385 ms**, native **`0x80008096`**, before the PC Steward overview or data creation. No normal-close proof, retry or forced termination; per-user root/database still absent, zero PCSteward processes. Strict installed validation remains **Pass / pinned-unsigned-v1**. The owner subsequently installed .NET; global .NETCore/Desktop **10.0.14** are confirmed. [Launch stop/evidence and exact next proposal](../review/TASK_48_LAUNCH_RUNTIME_STOP.md) preserve the failed attempt and unchanged artifact. Requested Opus dialogue and fresh one-run approval precede a second launch. Earlier unattempted-launch wording below is historical. `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16` remain owner-only evidence; Task 48 incomplete, no Task 49 or distribution gate closure.

**Current Task 48 host result — deployment PASS; separate launch pending:** The owner approved the exact pinned removal/redeployment plan. [Actual execution](../review/TASK_48_PINNED_REDEPLOY_EXECUTION.md) records attempt 1/native exit **0**, protected phase Validated and strict independent **Pass / pinned-unsigned-v1**. All **102 paths / 100 non-signed hashes / two signed-image reconstructions / six XBFs** pass; approved desktop DLL/PDB identity is restored, and full install/state ACL/reparse checks pass. Fresh RSA-2048/SHA-256 signer `49D695BA9CA2DEA922659699E5F40710F2B5B264` expires `2026-11-12T19:15:42Z`; old owned signer entries are absent. Per-user data/database remains absent, zero PCSteward processes, no launch/retry/update. Existing local gates: 85 assertions, Release 0 warnings/errors, full 291/291. Compiler descendant inheritance and live expiry remain unproven as disclosed; trusted owner-account assumption remains. [Bounded launch plan](../review/TASK_48_RESPONSIVE_LAUNCH_PLAN.md) is prepared for separate explicit approval and first-run local data creation. Earlier readiness/retained-old-install passages below are historical; this result supersedes them. `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16` evidence is owner-only; no public distribution gate or Task 49 is closed/started.

**Final Task 48 host-test readiness:** [Fresh Opus re-check](../review/TASK_48_PAYLOAD_OPUS_REREVIEW.md) found no bootstrap blocker; parent addressed its exact-caller and residual-trust wording conditions. [Final readiness](../review/TASK_48_FINAL_HOST_READINESS.md)/[exact plan](../review/TASK_48_EXACT_REDEPLOY_PLAN.md) supersede pending-recheck passages below. Local 85 assertions/full291 tests pass; prepared caller CheckOnly verifies unchanged 102-file candidate and no configured CLR overrides/enforced policy. No elevated run occurred. Exact removal/redeployment authority, live new-profile evidence and later separate launch approval remain open, with the trusted owner-account assumption disclosed. Current install/data state and all distribution boundaries remain unchanged.

**Latest Task 48 review-closure state:** [M1/L1–L5 response](../review/TASK_48_PAYLOAD_REVIEW_CLOSURE.md) and [exact prepared host plan](../review/TASK_48_EXACT_REDEPLOY_PLAN.md) supersede earlier 65-assertion/pending-first-review wording. Local protected-execution/compiler-temp, held-hash, signer, commit-order and parser/profile remedies pass **85 assertions** and full **291/291**, Release 0 warnings/errors. First independent review is preserved; substantive new bootstrap/plan re-check is pending before exact removal/redeploy authority. Existing installed state and absent per-user data remain unchanged; no new elevated run or launch is claimed.

**Current Task 48 amendment state:** owner accepted the pinned-payload route by **“I also accept the changes”**. [Local implementation/evidence](../review/TASK_48_PAYLOAD_FIX_IMPLEMENTATION.md): 65 focused script assertions, Release build 0 warnings/errors and full **291/291** tests, zero skipped; no runtime source or new host mutation. Pinned staged bytes/signing checks/provenance replace privileged compilation. Fresh independent Opus review remains pending; elevated ACL/SHA-256 signing integration and exact removal/redeployment approval remain open, with launch separate. Earlier Proposed/acceptance-pending paragraphs below are historical and superseded by this record. The retained failed-identity installation is still only `legacy-v6`, not a new-route pass.

This matrix maps every mandatory, partial-MVP, and desirable-MVP item to at least one numbered Codex task. Review tasks verify the whole phase even when not repeated below.

Every independent phase/design/release gate is preceded by a Terra/High evidence-readiness task (`14A`, `19A`, `24A`, `26A`, or `30A`). Its committed readiness report maps mandatory criteria to reproducible sanitized evidence, a recorded product-owner exception, or a blocking next action; a missing/stale report is itself unproven gate evidence.

## PC Steward 2.0 accepted deltas — Task 44

Task 48 consultation update: Opus 5.5's completed analysis and the parent's exit-0 metadata/PDB rerun disprove output-path differences and identify three differing CsWinRT-generated source documents. The detailed execution paragraph below retains the stopped-run evidence; its consultation-requested wording is superseded here. The [concrete ADR-022 amendment proposal](../review/TASK_48_PINNED_PAYLOAD_FIX_PROPOSAL.md) defines pinned unsigned artifact consumption, protected staging, signed-image identity, provenance, negative fixtures and later host authority. It is not accepted or implemented; the installation/signer remain untouched and launch remains blocked.

### Task 47 planning/evidence ownership — current working-tree state

**Task 47/A2 complete.** The [A2 package](V2_PHASE_A2.md), [shared contract](V2_TASK_CONTRACT.md), [host-test plans](V2_HOST_TEST_PLANS.md) and [25 finalized numbered prompts](../../prompts/INDEX.md#pc-steward-20--task-47-planning-package) supply bounded instructions for Tasks 48–72. The owner accepted [Q1 catalogue/progress, Q3 warning and Q5 favourites/history defaults](V2_PLANNING_DECISIONS.md), then explicitly answered **“Accept the corrected ADRs and final choices”** after both Opus reviews and disclosed final author N1–N6 follow-ups. ADR-023–025 are Accepted; ADR-026 is Accepted C (Defer). Accepted final policies include 30-minute frozen review, separate two-minute helper request, exclusive cross-session gate and new install/uninstall 10-minute deadline/at most five-second teardown attempt. Original findings were closed by the re-check; final small follow-ups were disclosed and owner-accepted, not independently re-reviewed. Static checks and Release build/full 290-test suite pass; no source changed after that run. These are planning/design artifacts, not future feature/live evidence. Historical Task 44–46 pending-Q/ADR/next-task statements below are superseded by this record. Q4 route/host proof and exact action gates remain open; no future task starts automatically.

| Requirement scope | Implementation owner | Readiness / independent review | Additional evidence gate |
|---|---|---|---|
| FR-UPD-14 / retained progress, UI and history | 49, 52 | 53 / 54 | Correlated observable progress, bounds, late warning, frozen selection/cancel/recovery |
| FR-UPD-15 / retained immutable job and helper | 50 | 53 / 54 | Per-provider mode enforcement, substitution/replay/old-new protocol negatives |
| Retained restart/postcondition criteria FR-UPD-05/08–11, AC-UPD-06–11 | 51 | 53 / 54 | Reliable provider restart, distinct unknown and targeted verification |
| FR-CAT-01–02/06 | 55 | 57 / 58 | Native bounds/source consent, honest fields/tags, favourites and Q5 lifecycle |
| FR-CAT-03–06 | 56 | 57 / 58 | 59 exact-route live install; fresh complete absence, helper and operation identity |
| FR-UNI-01–04 | Q4 owner-reviewed route/fixture proof, then 60 | 61 / 62 | 63 selected absence/other-instance preservation; unsupported routes deferred |
| FR-DIA-01–05 | 64 | 65 / 66 | Separate approved exact-host read coverage; complete privacy/lifecycle/reset proof |
| FR-AI-01–06 | 67, integrated 68 | 69 / 70 | 71 exact approved payload/model/profile/route; advisory isolation and no secret/prompt sinks |
| FR-BUD-01–04 | 68 | 69 / 70 | 71 complete reservation/usage/uncertainty within cumulative $1, no automatic promotion |
| FR-DAT-06–07/12–15; privacy/security; NFR-PERF/REL/MNT/OBS | Each affected implementation | Every readiness/review | Legacy migration, captured-copy/provenance clears, async-safe shared lease, physical resources |
| FR-UPD-12; FR-SCH-01–05; FR-RSC-04 | ADR-026 proposed Defer; 53/54 verify boundary | Decision only | No automation implementation; 72 only reconsiders named deferred items |
| FRA-14–16; owner data/deployment | 48 COMPLETE for exact owner-only task | Pinned deployment/strict final validator Pass; exact102 paths/100 non-signed hashes/two signed-image reconstructions; fresh signer/ACLs; separately approved Overview/normal close native0/22.826s; fresh copied DB hash/integrity/schema pass. [Completion](../review/TASK_48_COMPLETION.md) | Failed first launch preserved; owner supplied .NET, fresh approval succeeded. External-runtime/account/compiler/expiry limits disclosed; no distribution gate closure or Task49 |

All 27 new IDs retain their existing requirement text/markers below. Existing original matrix rows remain unchanged. [Task 47 report](../review/TASK_47_PHASE_BRIEFS_AND_PROMPTS.md) records review, corrections, validation and unresolved acceptance; no current 2.0 implementation or paid/live result is inferred.

**Task 48 / retained `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`: approved installation, failed exact artifact gate.** [Execution evidence](../review/TASK_48_OWNER_ONLY_REDEPLOY.md) and [failure record](../failures/task-48.md) separate passing protection validation from candidate identity failure. Preflight pinned Task 42/current inputs and the 102-file manifest after preparation's 2/2 focused tests, Release build and 290/290 suite. One owner-approved installation created the protected roots and temporary signer; independent validator returned exit 0. Complete path set matches; 98 non-signed hashes match; desktop DLL/PDB hashes differ despite zero input drift. Installed manifest SHA-256: `39A08C3396E23868DD894A581072E6FC2847F1488DFF09E7B912495D92D16F70`. Per-user state/database remains absent; no fabricated DB integrity pass. Stopped without launch/retry/removal/cleanup. Opus 5.5 consultation is owner-requested; launch remains separately unapproved and blocked. Destructive data controls are not expanded and distribution gates remain open.

**Requirement authority, not implementation evidence.** Task 44 promotes only the owner-accepted Task 43 deltas and D1–D5 into [specification v0.3](../product/PC_STEWARD_PRODUCT_SPEC.md), sections 9.13–9.18 and 11.5. Existing MVP rows below retain their IDs, wording and markers. The [amendment record](../review/TASK_44_ACCEPTED_REQUIREMENTS.md) records scope, checks and remaining gates. Task 43 entries are historical completion-time evidence; their proposal-only wording does not override this promotion.

| ID | Accepted requirement summary (full acceptance in specification) | 2.0 scope | Documentation / planned implementation and evidence tasks |
|---|---|---|---|
| `FR-UPD-14` | Truthful observable progress, running-app warning and per-item final/cancel outcomes. Spec §9.13. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 49, 53–54 pending |
| `FR-UPD-15` | Enforceable typed silent/interactive mode frozen in review and revalidated by runner/helper. Spec §9.13. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 50, 53–54 pending |
| `FR-CAT-01` | Bounded consented WinGet COM catalogue search with honest metadata; no Store install. Spec §9.14. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 55, 57–59 pending |
| `FR-CAT-02` | Tags only; unsupported icons/categories/sizes unavailable; no curated map (D1). Spec §9.14. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 55, 57–58 pending |
| `FR-CAT-03` | Fresh not-installed proof and exact source/ID/version/scope/architecture install review. Spec §9.14. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 56–59 pending |
| `FR-CAT-04` | Distinct install operation, unelevated user route and independently validated protected machine route. Spec §9.14. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 56–59 pending |
| `FR-CAT-05` | Sequential reviewed install queue, cancellation, partial outcomes and verified installed state. Spec §9.14. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 56–59 pending |
| `FR-CAT-06` | Local reversible favourites and verified recent installs; Q5 lifecycle before persistence. Spec §9.14. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 55–59 pending |
| `FR-UNI-01` | One freshly resolved provider/instance/scope/architecture/user binding; ambiguity blocks. Spec §9.15. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 60–63 pending |
| `FR-UNI-02` | Enforceable WinGet/current-user MSIX routes only; exclusion proof, no registry commands or leftovers. Spec §9.15. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 60–63 pending |
| `FR-UNI-03` | In-use warning, manual confirmation and distinct independently validated uninstall elevation. Spec §9.15. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 60–63 pending |
| `FR-UNI-04` | Selected-instance absence verification, other-instance preservation and honest restart/unknown outcomes. Spec §9.15. | Yes — 2.0; gated | 44 requirements; 45 ADRs; 47 briefs/prompts; 60–63 pending |
| `FR-DIA-01` | Consented bounded unelevated OS/own-operation readers with visible denied/missing sources. Spec §9.16. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 64–66 pending |
| `FR-DIA-02` | Deterministic nearby-change timeline; no causal claim from Kernel-Power 41. Spec §9.16. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 64–66 pending |
| `FR-DIA-03` | 30-day local snapshots (D4); Q1 limits/fields and redaction before persistence. Spec §9.16. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 64–66 pending |
| `FR-DIA-04` | Scan clear removes snapshots, preserves update history; reset preserves OS records/exports. Spec §9.16. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 64–66 pending |
| `FR-DIA-05` | Requested minimal bounded redacted diagnostic export with preview and sensitivity warning. Spec §9.16. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 64–66 pending |
| `FR-AI-01` | OpenAI first, official SDK/provider abstraction, configurable model/pricing; advisory explanation only. Spec §9.17. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 67, 69–71 pending |
| `FR-AI-02` | Exact editable payload approval/redaction and residual-secret warning; no dumps/full logs. Spec §9.17. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 67, 69–71 pending |
| `FR-AI-03` | Current-user protected key, excluded from config/export/backup/logs; disconnect/reset deletion. Spec §9.17. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 67, 69–71 pending |
| `FR-AI-04` | Approved TLS/no cross-origin credentials; editable tokens coupled to compatible response/saved-item byte allowances within verified finite resource/model and configured spending bounds; effective limits and fresh unsent review/reservation. Settle reliable terminal usage independently of answer completeness. Spec §9.17, Task 46 owner clarification. | Yes — 2.0; gated | 44 requirements; 46 ADRs/clarification; 47 briefs/prompts; 67–71 pending |
| `FR-AI-05` | Hostile text remains untrusted; no tools, commands or AI-derived action eligibility. Spec §9.17. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 67, 69–71 pending |
| `FR-AI-06` | Keyless core; mocks; separate live approval/$1 cap; 30-day explanations and no saved prompts. Spec §9.17. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 67–71 pending |
| `FR-BUD-01` | Editable USD month/request caps, initially $200/$0.25, without extra product dollar maxima; finite checked amounts, request <= month when both positive, either zero disables without clearing settings. Explicit prospective saves preserve commitments/periods/test ledger. Separate $1 test cap; unknown pricing blocks. Spec §9.18, Task 46 owner clarification. | Yes — 2.0; gated | 44 requirements; 46 ADRs/clarification; 47 briefs/prompts; 68–71 pending |
| `FR-BUD-02` | Transactional conservative pre-send reservation; concurrency, retries and crash-window safety. Spec §9.18. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 68–71 pending |
| `FR-BUD-03` | Provider-usage reconciliation; uncertain reservations retained; accepted rollover/currency semantics. Spec §9.18. | Yes — 2.0; gated | 44 requirements; 46 ADRs; 47 briefs/prompts; 68–71 pending |
| `FR-BUD-04` | Never purchase credits, enable recharge or assume promotional credits increase app budget. Spec §9.18. | Never | 44 requirements; 46 ADRs; 47 briefs/prompts; 68–71 pending |

All 27 new IDs are specified only; no implementing source, passing feature test or live result is claimed for them. Task numbers are the accepted delta outline and do not start those tasks. Future ADRs require owner acceptance; each host install/uninstall/paid call requires separate exact action-time approval.

| Retained IDs / boundary | Accepted clarification and planned evidence |
|---|---|
| `FR-UPD-01–11`, `AC-UPD-01–11`, `FR-UI-01–07`, `FR-HIS-01–06` | Existing update semantics remain. G3 reliable restart propagation (51) and G4 frozen reviewed multi-select/Update All, sequential partial results and cancel-before-next (52) complete existing criteria; no duplicate IDs. Existing running-app warning also applies if a target appears before execution. Q2/Q3 remain open; no new skip/version-pin/process-control feature. |
| `FR-POL-01–05`, `FR-SRC-02/07–10`, `FR-ID-09` | Preserve Partial/Yes/Later/Never markers and meanings. Tags only (D1); Store information only; constrained Chocolatey update scope unchanged. AI diagnostic explanation does not promote AI matching. |
| `FR-UPD-12`, `FR-SCH-01–05`, `FR-RSC-04` | WS5 is Task 45 ADR decision only; automation remains Later. Durable allowlist/source/version-ceiling consent, integrity, freshness, revocation, windows/back-off/notifications and installer limits require a decision; no per-run confirmation exception or ScheduledHost execution is introduced. |
| `FR-DAT-06–07/12–15`, `AC-DAT-06–07/12–16` | Add collected-snapshot deletion to scan clear while preserving update history, and all app-owned diagnostic/AI/credential categories to reset. Original criteria remain. Task 46 lifecycle/manifest decisions and future 64/67/68 tests must prove fixed roots/credential identity, idempotence, honest residuals and OS-record/export preservation. Q5 new history/favourites lifecycle remains a gate. |
| `AC-DAT-04`, spec §§2.3/12.3/13.1/16.2 | Narrow optional AI egress/credential amendment: existing AI-off network check plus exact edited/redacted-payload approval to the approved TLS provider only; no cross-origin credentials. Task 46 ADRs, 67–70 mock/security matrix and separately approved 71 live evidence are pending. No default upload, account requirement or general cloud authority. |

**Owner decisions recorded:** D1 tags only; D2 OpenAI first; D3 `7zip.7zip` as a candidate only; D4 30-day local snapshots/saved redacted explanations, no saved submitted prompts, separate minimal accounting/reservations. D5 originally selects USD $200/month and $0.25/request; the owner's subsequent Task 46 “token & spending limits should be editable” clarification makes these initial defaults for editable normal spending limits. The separate $1 total first-live-test authority is retained. No live credit balance is assumed. The full deferred list remains Later; prohibitions and owner-only/distribution gates remain effective.

**Remaining gates:** Task 46's diagnostic/AI/budget policies under ADR-027–029 are owner-Accepted; actual reader/model/local-token/serialization/profile/resource/migration/security/host evidence remains future work. Q1 catalogue/search bounds, Q2 skip meaning, Q3 running-app coverage, Q4 removal/host feasibility, Q5 favourites/new operation-history lifecycle and Q9 exact deployment authority remain with their applicable tasks. Task 45 ADR-023–026/WS5 decisions are still pending. Task 47 needs those applicable decisions/resolved inputs and separate instruction. Tasks 44/45 remain committed locally; Task 46 design acceptance does not implement features or close release/live-action gates.

### Task 46 Accepted decision coverage

**Owner acceptance, 2026-10-29:** **“Yes accepted”** to ADR-027/028/029, including final R1 snapshot-preservation/deletion disclosure and the stated v1 engineering defaults. All three ADRs are **Accepted**. The [Task 46 record](../review/TASK_46_PROPOSED_DIAGNOSTIC_AI_BUDGET_ADRS.md) retains earlier question rounds as history, the changes-needed first review, the Claude Opus 5.5 re-check clearing F1–F13 and final owner acceptance. Its historical filename is retained.

The accepted policy covers seven-day consented bounded collection, visible partial coverage, inclusive +/-15-minute non-causal correlation, whole-snapshot retention, source-specific explanation clearing, exact current-user Credential Manager lifecycle, preview-bound advisory OpenAI with bounded coupled token/byte/save profiles, manual retry/one-in-flight/45-second behavior, and transactional UTC-month accounting with 24-hour owner-confirmed rates.

The owner explicitly chose editable input/output tokens and normal request/month USD spending, so specification v0.4 and the same FR-AI-04/FR-BUD-01 summary rows are narrowly amended. Defaults are 8,192/1,024 tokens and $0.25/request/$200/month; no extra product dollar maxima, checked finite settings, request <= nonzero month, explicit prospective saves and zero disabling without erasing settings/commitments. The independent cumulative $1 first-development-test authority is retained. No new IDs or implementation evidence is inferred.

Accepted R1: Clear update history deletes live history/FailedUpdate explanations but preserves whole diagnostic snapshots with captured minimal history copies and DiagnosticTimeline explanations, with an explicit preservation warning. Clear scan data removes those snapshots/diagnostic explanations. Both explanation kinds have normal expiry/reset rules. Full reset deletes all app state and the exact key, with no surviving marker; first AI setup is a prior-usage declaration, recognized app recovery is gated and external restores may be undetectable.

| Accepted record | Requirement coverage | Accepted design and remaining implementation proof |
|---|---|---|
| [ADR-027](../adr/ADR-027-DIAGNOSTIC-TIMELINE-AND-DATA-LIFECYCLE.md) | `FR-DIA-01–05`, `FR-HIS-01–06`, `FR-DAT-06–07/12–15`, `AC-DAT-06–07/12–17`, `NFR-REL-02`, `NFR-MNT-01–04`, `NFR-OBS-01–05` | Q1 diagnostic policy/schema/limits/export/partial coverage and Q6 deterministic time/correlation accepted; lifecycle, copy-preserving R1, source-provenance clears and 30-day whole snapshots accepted. Tasks 64–66 and relevant 67–70 still require reader/schema/resource/migration/reset/host evidence. Q5 is separate. |
| [ADR-028](../adr/ADR-028-ADVISORY-AI-PRIVACY-EGRESS-AND-CREDENTIALS.md) | `FR-AI-01–06`, `FR-BUD-01–04`, `FR-DIA-03–05`, `FR-DAT-06–07/12–15`, `AC-DAT-04/12–17`, `NFR-MNT-01–04`, `NFR-OBS-01–05` | Q1 AI limits/route/preview/body/header/local-token policy and Q8 exact Credential Manager contract accepted. Coupled 4 MiB profile bounds/8 MiB content, editable settings and source-specific saved content are design policy, not model/SDK support evidence. Tasks 67/69–70 mock/security/profile checks precede separately approved Task 71. |
| [ADR-029](../adr/ADR-029-AI-BUDGET-RESERVATION-AND-RECONCILIATION.md) | `FR-BUD-01–04`, `FR-AI-01/04/06`, `FR-DAT-06–07/12–15`, `AC-DAT-01/04/12–16`, `NFR-REL-02`, `NFR-MNT-01–04` | Q7 UTC periods/checked USD/configuration/reservation/usage/uncertainty/compaction/manual tariff and honest continuity accepted. Editable normal caps are not the separate fixed $1 development-test authority. Tasks 68–70 must prove concurrency/crash/usage/migration/recovery; no live call authority follows. |

These ADRs accept additive 2.0 amendments to ADR-010/013/014/015/016/019 and the reset design, carried as constraints into Task 47/relevant implementation tasks. Earlier documents remain baseline descriptions of existing code until detailed alignment accompanies implementation. Content expiry/clear never refunds spend; OS records, provider caches, installed apps and user exports remain preserved. Current test passes validate existing code only. Task 46's design-acceptance gate is closed; Task 47 still waits for applicable Task 45 ADR-023–026 decisions/resolved inputs and its own instruction.

### Task 45 Proposed decision coverage

These links add design proposals only. They do not change any requirement row or effective ADR status. The author has accepted no ADR; Task 45 began against Task 44's working-tree requirements, and the subsequent authorized build recovery closes required validation without accepting the designs.

| Proposal | Requirement coverage | Decision and evidence boundary |
|---|---|---|
| [ADR-023](../adr/ADR-023-CATALOGUE-INSTALL-OPERATIONS.md) | `FR-CAT-01–06`, source consent, operation history and data lifecycle | Bounded COM discovery separated from install authority; exact reviewed version/scope/architecture, fresh complete absence, independent helper, sequential verified results; proposed favourites/recent/history ownership. Q1/Q5 acceptance and provider/host evidence pending. |
| [ADR-024](../adr/ADR-024-INSTANCE-BOUND-MANUAL-UNINSTALL.md) | `FR-UNI-01–04`, operation history and data lifecycle | One enforceable instance, complete exclusions, separate manual confirmation, other-instance-preserving verification. WinGet/current-user MSIX route feasibility remains Q4; unsupported routes stay blocked/deferred. |
| [ADR-025](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md) | `FR-UPD-01–11/14–15`, `AC-UPD-01–11`, `FR-CAT-03–06`, `FR-UNI-01–04`, `FR-UI-01–07`, `FR-HIS-01–06`, `TM-03–04/07/13` | Closed typed variants, strict protocol compatibility, independently revalidated helper evidence, immutable mode, truthful progress/restart, frozen update batch and no crash replay. Records proposed ADR-003/015/017/018/022 amendments only; current updates unchanged. Q1/Q2/Q3/Q4/Q5 and security/host gates remain. |
| [ADR-026](../adr/ADR-026-WS5-AUTOMATION-AUTHORIZATION-DECISION.md) | `FR-UPD-01/12`, `FR-SCH-01–05`, `FR-RSC-04`, `NFR-PERF-07`, `AC-NFR-11`, `FR-DAT-12–15` | Recommend Defer; owner accept/reject/defer choice pending. Documents durable grant integrity, source/ID/ceiling, freshness/revocation/reset, windows/back-off/notifications and installer limits required for reconsideration. Later/per-run/scan-only rules unchanged. |

The [Task 45 record](../review/TASK_45_PROPOSED_OPERATION_ADRS.md) contains scope, owner choices and static checks. [Project recovery](../review/TASK_45_PROJECT_RESTORE.md) supports `NFR-MNT-01–04`, `NFR-REL-02` and `AC-DAT-01`: exact project restoration, source-directory ignore exception, 18/18 restored projects, SQLite 19/19, Architecture 28/28, Release build 0 warnings/errors and full suite 290/290. The [failure record](../failures/task-45.md) records both validation and commit identity blockers as resolved by owner-authorized recovery and per-command authorship. The owner's removal of SDK 10.0.303 is verified; older runtime 10.0.11 remains, and no deployed application is claimed patched. Existing-suite passes do not implement or accept new 2.0 capabilities. Task 46 owns diagnostic/AI/budget ADRs; Task 47 waits for applicable owner ADR acceptance and resolved inputs.

## Phase 0 evidence links

| Task | Requirement IDs informed | Evidence |
|---|---|---|
| 02 — packaging and WinGet spike | `FR-DAT-12`, `AC-DAT-18`, `FR-INV-05`, `FR-SRC-01–05`, `FR-UPD-02–04`, `NFR-MNT-03` | `spikes/winget-packaging/SPIKE_REPORT.md`; informs Provisional ADR-002 and ADR-003. |
| 03 — privilege and safe-deletion spike | `FR-UPD-04`, `FR-DAT-08`, `FR-DAT-11–15`, `AC-DAT-11–16`, `AC-UPD-03` | `spikes/security-boundaries/SPIKE_REPORT.md`; 42 standard-host assertions passed. Task 90's approved isolated local-PC fixture adds 14 assertions against the production cache for symbolic links, scratch-VHD mount, and deterministic root/ancestor races; informs ADR-018/019 and Provisional ADR-011/013. |
| 04 — performance baseline and fixture matrix | `NFR-PERF-01–02`, `NFR-PERF-06`, `NFR-PERF-08–09`, `AC-NFR-01–03`, `AC-NFR-07`, `AC-NFR-09` | `spikes/performance-fixtures/BASELINE.md`; 320 fixtures, 30 local and 20 simulated-online samples; supports Accepted ADR-006 measurement protocol, not final compliance. |
| 05 — architecture decision lock | `OD-01–13`, `NFR-MNT-01–05`, `NFR-OBS-01–05`, `FR-UPD-01–04`, `FR-DAT-06–15`, `FR-SCH-01–05` | `docs/review/TASK_05_ARCHITECTURE_DECISION_REVIEW.md`; ADR-001–020 leave no Proposed decisions and define four Provisional evidence gates. |
| 06 — solution and guardrail scaffold | `NFR-MNT-01–05`, `NFR-OBS-01–05`, `NFR-PERF-01–10`, `FR-UPD-02–04`, `FR-DAT-06–15`, `FR-SCH-02–04` | `PCSteward.slnx`, central build settings, architecture-test project, Windows-only CI and command scripts establish scaffold evidence only; no feature requirement is marked complete. |
| 07 — core domain and SQLite persistence | `NFR-REL-02`, `NFR-MNT-04`, `FR-ID-01`, `AC-DAT-01`, `AC-DAT-06–07` | Domain lifecycle records, SQLite migration, atomic inventory promotion and stable-decision preservation tests; inventory and UI requirements remain unimplemented. |
| 08 — registry inventory providers | `FR-INV-01–03`, `FR-INV-06`, `FR-INV-08–09`, `AC-INV-01`, `AC-INV-03–04`, `AC-INV-06–07`, `NFR-REL-05` | Injectable read-only uninstall-registry provider with 64-bit, 32-bit and current-user fixtures; no elevation, MSI, WMI or unrelated registry enumeration. |
| 09 — MSIX/AppX inventory provider | `FR-INV-04`, `FR-INV-06`, `FR-INV-08`, `AC-INV-01/04/06/07` | Current-user package catalog boundary with retained identities, conservative framework/resource/system classification, warnings and fixtures. |
| 10 — WinGet installed inventory | `FR-INV-05–06/08`, `AC-INV-01/04/05/06/07` | Read-only fixed-executable list adapter with bounded output, timeout/exit classification and safe parser fixtures; no source refresh or update command. |
| 11 — normalization and conservative deduplication | `FR-INV-07/09`, `FR-ID-01`, `AC-INV-02`, `NFR-MNT-02` | Deterministic provider-identity fingerprints and fixture matrix preserve raw provenance while rejecting scope, architecture, version, channel and near-name over-grouping. |
| 12 — scan orchestration and measurement | `FR-RSC-01–03/05`, `FR-HIS-01`, `NFR-REL-02`, `NFR-MNT-01/04`, `NFR-OBS-01–02`, `AC-NFR-03–04/09` | Exclusive cancellable local orchestration, provider timing/warning isolation, and last-known-good publish tests; final UI/process performance evidence remains unmeasured. |
| 13 — app shell, onboarding and inventory experience | `FR-ONB-01–04`, `FR-UI-02–03/05/07`, `FR-RSC-01`, `AC-UX-01–04/06/09` | Local-only onboarding, connector trust/toggle model, accessible scan controls, local inventory search and raw-provider installation detail. Desktop accessibility inspection passed; a clean local interactive scan completion retest remains outstanding. |
| 14 — exports and Clear scan data | `FR-DAT-01–03/06–07/10/13–15`, `AC-DAT-01–03/05–07/14–15/17`, `AC-UX-02/07` | Snapshot-backed CSV/JSON export with privacy warning and Phase 3 history placeholder; confirmed, transactionally idempotent clear-data lifecycle preserves decisions, mappings, update history and downloaded artifacts. Integration tests cover export fidelity, offline persistence, no-scan reset and preserve/delete semantics. |
| 15 — Phase 1 review gate | All Phase 1 requirements and criteria | Fresh review through `5fcaf3e` passes the Phase 1 gate with the product-owner-approved temporary `AC-NFR-06` exception. P1RRR-01–04 are closed by final-app cancellation/resource, active-termination recovery, measured concurrency/second-scan rejection and 150% text-scaling evidence. See `docs/review/PHASE_1_REVIEW.md`. |
| 90 — Phase 1 review remediation | `FR-INV-04–09`, `FR-RSC-03/05`, `FR-HIS-01`, `FR-UI-02/05/07`, `FR-DAT-10`, `NFR-REL-02`, `NFR-MNT-01/05`, `NFR-OBS-01–02`, `AC-INV-02/05`, `AC-NFR-03/04/07/09`, `AC-UX-01/02/04/06/09` | Explicit WinGet approval/fail-closed persistence, contained timeout recovery, typed metadata/raw diagnostics/events, presentation boundary, stable filters and final-app baseline evidence are recorded in `docs/review/TASK_90_P1RR_EVIDENCE.md`. P1RRR evidence records 236-ms keyboard cancellation recovery, a 2.3173%/194.7-MB active-scan sample, active-scan termination recovery, measured single-provider concurrency/second-scan rejection, and a restored 150% text-scale exercise. The five-minute CPU result remains a temporary product-owner exception. |
| 16 — version comparison engine | `FR-VER-01–05`, `NFR-MNT-02`, `AC-MAT-04–05` | Pure bounded numeric-dotted parser and comparison table returns explicit newer/equal/older/incomparable/connector-deferred outcomes. Unit cases cover omitted components, leading zeros, recognized prereleases, stable-to-preview blocking, vendor suffixes, malformed values, connector deferral and downgrade blocking. |
| 17 — deterministic matching and confidence engine | `FR-ID-01–07`, `AC-MAT-01–05/07` | Pure matching resolves only unique high-confidence exact package/family/MSI identity or supported trusted user/imported mappings. Publisher-qualified aliases remain review-only; architecture, scope, channel, version, ambiguity, trust and manual-target validation fail closed. Curated true-match, non-match and near-name/publisher fixtures assert one actionable match and zero false-positive actionable mappings. |
| 18 — WinGet update intelligence, source cache and freshness | `FR-SRC-01–05/10`, `FR-UI-01/04–06`, `NFR-PERF-02`, `NFR-REL-01/04`, `NFR-MNT-01/03`, `AC-MAT-01/03–06`, `AC-DAT-04`, `AC-NFR-02/05`, `AC-UX-06` | Fixed WinGet/Store-only source connectors use bounded field/output parsing, explicit online refresh, cache timestamps and independently fail closed. Cached source results remain visibly stale on failure/offline; deterministic intelligence emits current, verified, review, unmanaged, pinned, ignored and unavailable states. Fixture tests cover healthy/stale/malformed/timeout/compatibility/offline paths and a 20-run fixture online benchmark. An approved elevated ETW run now supplies the event-based enabled/disabled, process-attributed `AC-DAT-04` evidence with zero lost events; real-row parser evidence remains pending. See `docs/review/TASK_90_P2R_EVIDENCE.md`. |
| 32 — Chocolatey Community source and protected execution | `FR-SRC-07`, `FR-SRC-03–05/10`, `FR-UPD-01–09`, `AC-MAT-01/03–06`, `AC-UPD-01–08`, `NFR-MNT-01/03`, `TM-04` | Product-owner-approved scope expansion from Later. A disabled-by-default connector permits only the fixed Community feed through a ProgramData-root, WinTrust-validated Chocolatey client with an exact signer identity. Because local Chocolatey registrations do not reliably reveal their original source, they remain review-only until a user confirms an exact Community mapping. Then bounded local/outdated parsers, exact immutable source/ID/version evidence, machine-scope helper dispatch and exact post-update local re-inventory reject malformed, stale, custom-source, unsigned-client and non-exact evidence. The original desktop process rejects caller-writable/reparse client layouts before `runas`; the helper rechecks canonical path, reparse state, signer and SHA-256 identity before launch. Deterministic fixtures cover fixed arguments, hostile rows, signing rejection, replacement detection, wrong-version verification and zero helper launches on preflight rejection. A protected-path rejection is presented as an update failure, never a completion. Task 37 records one live Chocolatey Community source refresh; live Chocolatey update execution remains uncollected. |
| 33 — owner-only protected test deployment | `FR-UPD-01–09`, `AC-UPD-01–08`, `TM-04` | An explicit owner-only installation script can publish the existing desktop/helper pair, stage and require the compiled WinUI XBF/PRI resources, journal the installing Windows SID and temporary CurrentUser code-signing trust chain, install only to the fixed Program Files root, apply protected ACLs, verify fixed/non-reparse/same-signer signatures and report without that script launching the app or an update. The desktop retains its active WinUI window. The installer and independent read-only validator passed on the owner machine, and the corrected protected build rendered the normal Overview UI with Chocolatey Community evidence. In a separate explicitly authorized action, the owner reported a successful exact WinGet update of `AntibodySoftware.WizTree` 4.31 → 4.32; the product classified 4.32 as current and an independent exact WinGet inventory confirmed the installed 4.32 postcondition. This does not independently prove every desktop/helper process step. Exact-manifest removal includes guarded recovery for an interrupted owner-only transaction. This is not public/private distribution or proof of Chocolatey execution/healthy online refresh; each further update still requires separately approved exact evidence. |
| 34 — administrator-requirement indicator | `FR-UI-03–04`, `FR-UPD-04`, `AC-UPD-03–04`, `AC-UX-03`, `TM-04` | The detail page discloses that administrator requirements remain unknown until fresh review. The final review derives a text-only required/not-expected explanation solely from the validated immutable provider/scope selection before confirmation or execution: WinGet/User is not expected to request administrator approval, while WinGet/Machine and Chocolatey/Machine require the on-demand UAC path. The domain rejects Chocolatey/User instead of displaying a false no-UAC promise. Focused 3/3, Architecture 19/19, non-native-storage Application 126/126 and Release build pass; independent code review passes. The owner-only protected deployment passed its ACL/signature/manifest validator, and Task 37 captured the real protected Vortex detail card with the installed pre-review status and safe explanation simultaneously visible. The update-review control was not invoked. See `docs/review/TASK_37_TASK_34_VISUAL_AND_SOURCE_REFRESH.md`. |
| 35 — production UI/UX redesign | `FR-UI-01–07`, `FR-RSC-01–03/05`, `FR-HIS-01–06`, `FR-POL-01–05`, `FR-SCH-01–05`, `AC-UX-01–09`, `NFR-PERF-02/08`, `NFR-MNT-04` | **Implementation and automated validation complete.** The selected Steward Ledger direction replaces the broad peer-route shell with five stable task destinations while retaining every supported contextual workflow. Production WinUI now provides decision-first Overview, a structured virtualized inventory, evidence-led Updates, a local Activity ledger, grouped Settings, shared semantic components, themed selected navigation, plain-language status, and progress updates that do not rebuild the shell per event. Current official-source research, audit, alternatives, design-system map, change log, five real-data screenshots, and exact validation boundary are recorded in `docs/`. Debug/Release builds pass with 0 warnings/errors, UI Architecture 24/24 and the UI-facing Application slice 23/23 pass; Task 36 closes the environment-only native-storage blocker with Application integration 167/167 and the official full suite 267/267 on a normal Windows host. The 2,500-record presentation workload is median 1.27 ms / p95 4.68 ms. ADR-021 makes `AC-UX-01–09` Optional; the owner has excluded the remaining manual accessibility matrix from this owner-only scope, and accessibility acceptance is not claimed. |
| 36 — native-storage test-host remediation | `FR-DAT-08–10/12–15`, `AC-DAT-08–09/12–16/18`, `AC-NFR-10`, `NFR-REL-02` | **Complete.** The exact cache quota case reproduced `NTSTATUS=C0000022` only inside the managed filesystem sandbox and passed on the normal Windows host. The unchanged handle-bound implementation then passed downloaded-cache 16/16, factory-reset 25/25, Application integration 167/167, and the official repository suite 267/267 (55 Domain, 167 Application, 24 Architecture, 21 Security). No path-based fallback, security-test skip, or deletion-authority change was introduced. See `docs/review/TASK_36_NATIVE_STORAGE_TEST_HOST.md`. |
| 37 — Task 34 protected visual and live-source validation | `FR-SRC-01–05/07/10`, `FR-UI-03–04`, `FR-UPD-04`, `AC-UPD-03–04`, `AC-UX-03`, `TM-04` | **Pass for the authorized scope.** The exact protected owner-only installation passed its validator, displayed the Task 34 pre-review administrator card in the real Vortex detail page, and produced hashed local visual evidence. One `Refresh connected repositories` action returned live Chocolatey Community and WinGet results; Microsoft Store failed closed independently and retained visibly stale cached evidence. Update-history, approved-job, execution-state, and scan-run counts were unchanged; no exact scheduled task or residual process was present after normal close. No update review, install/update, schedule, clear-data, reset, redeployment, or source-setting change occurred. See `docs/review/TASK_37_TASK_34_VISUAL_AND_SOURCE_REFRESH.md`. |
| 38 — review-required decision actions | `FR-ID-04–06`, `FR-POL-01–03/05`, `FR-UI-03–04`, `AC-MAT-02/05/07`, `AC-UX-05` | **Implementation and automated validation pass; live visual pending.** Inventory results, Review actions, and detail expose the applicable safe controls: refresh cached/offline exact evidence, confirm one live exact identity, choose among typed trusted candidates, pin, ignore, and inspect details. Follow-up screenshots closed the initially missed Results path, stale-source remediation, and a distinct live Microsoft Store partial-result case. An exact package identity with no repository-supplied available version is now `Update evidence unavailable`, not a user decision; it appears separately, discloses the source warning, and offers refresh without map/pin/ignore decision controls. Refresh rebinds the open detail but never confirms automatically; non-null incomparable versions remain non-executable when live. Package identity values remain structured and revalidated; no review decision creates an update job or invokes update-provider/elevation/execution authority. Focused presentation **20/20**, three-surface UI contract **6/6**, Architecture **25/25**, Release build, and corrected full normal-host suite **271/271** pass. See `docs/review/TASK_38_REVIEW_DECISION_ACTIONS.md`. |
| 39 — owner-only update-capable rollout | `FR-UPD-01–09`, `FR-UI-01–07`, `FR-ID-04–06`, `FR-POL-01–05`, `AC-UPD-01–08`, `AC-MAT-02/05/07`, `TM-04` | **Pass for the exact owner-only protected installation.** Commit `1298d4ae5a747d7ee489c32746f5154049800263` passed the Release build and 271/271 tests, then replaced the prior ADR-022 deployment under explicit authority. Installer and separate read-only validation passed protected ACL, same-signer, required WinUI resource, and complete 102-file manifest checks. The installed desktop DLL matches the local commit build, the preserved database hash is unchanged, the exact scheduled task remains absent, and deployment launched no app or update. This is not distribution or release-readiness evidence. See `docs/review/TASK_39_OWNER_ONLY_UPDATE_CAPABLE_ROLLOUT.md`. |
| 40 — private GitHub prerelease backup | `AC-DAT-18`, `AC-UPD-03/06`, `TM-04/18` | **Pass for the explicitly authorized private owner-only copy.** Private repository `DanDG923/pc-steward` contains release source/tag commit `d6d5caa`; prerelease `v0.1.0-owner-only.1` contains the exact 721-entry source archive and checksum. A remote re-download matched SHA-256 `AA95ACB4836632CB8B5E51D41548E2647E46BE8456A77E7CBBD0B9C6591E7E91`. The archive includes the guarded protected installer and no key/certificate file. It is not a portable signed binary, public/stable release, automatic update channel, or production-signing/readiness claim. See `docs/review/TASK_40_PRIVATE_GITHUB_PRERELEASE.md`. |
| 41 — Codex Security findings remediation | `FR-ID-01/04/07`, `FR-SRC-04–05`, `FR-VER-02/04`, `FR-UPD-02–04`, `FR-DAT-01/12/15`, `FR-SCH-03/05`, `NFR-REL-01–02/05`, `AC-INV-02/04/05`, `AC-MAT-01–06`, `AC-UPD-02/04`, `AC-DAT-02/13/16`, `TM-01–06/10–13/15/18` | **Implementation and automated validation pass.** Twenty-one imported reports are catalogued as 17 confirmed security/safety defects, 2 confirmed non-security product defects and 2 not actionable against the current checkout. Remediation makes incomplete/conflicting source evidence non-actionable; preserves source scoping and last-good cache; rejects malformed WinGet, SQLite, registry, version, path and fingerprint inputs; neutralizes CSV formulas; makes unified connector consent transactional; honors scheduled AppX state; restores exact Chocolatey helper authority and resumable reset onboarding; and deletes orphan directories deepest-first. Focused **213/213**, complete normal-host **288/288**, and Release build with 0 warnings/errors pass. No app/update/deployment/external finding action occurred. See `docs/review/TASK_41_SECURITY_FINDINGS_REMEDIATION.md`. |
| 42 — confirmed findings remediation | `FR-SRC-04–05`, `NFR-REL-01–02`, `AC-INV-05`, `TM-09/10/17` | **Implementation and automated validation pass.** Follow-up current-code reproduction confirmed residual F8 and F10 defects. One shared fail-closed predicate now normalizes legacy asymmetric trusted-source consent for desktop display/preparation and scheduled providers; malformed short WinGet update rows now fail before live cache promotion. Pre-fix regressions failed on both paths; post-fix focused **6/6**, complete normal-host **290/290**, and Release build with 0 warnings/errors pass. See `docs/review/TASK_42_CONFIRMED_FINDINGS_REMEDIATION.md`. |
| 43 — PC Steward 2.0 scope reconciliation | Existing `FR-UI-01–07`, `FR-UPD-01–13`, `FR-POL-01–05`, `FR-HIS-01–06`, `FR-SCH-01–05`, `FR-DAT-06–15`, `FR-ID-09`, `FR-SRC-02/07–10`; **proposal-only** `FR-UPD-14–15`, `FR-CAT-01–06`, `FR-UNI-01–04`, `FR-DIA-01–05`, `FR-AI-01–06`, `FR-BUD-01–04` | **Documentation complete; owner accepted delta and answered D1–D5.** [Agreed roadmap](../product/PC_STEWARD_2_ROADMAP.md) was copied unchanged under owner direction and committed as `2f0494b`; [delta reconciliation](../product/PC_STEWARD_2_DELTA.md) is based on Task 42 implementation `52b4313`. It maps every WS/deferred item, audits all eight WS2 checklist items against code, identifies four confirmed updater follow-ups, proposes retained/new requirement criteria and explicit decision/privacy amendments, lists required ADR work without drafting it, outlines tasks 44–72 and three controlled package candidates, and records D1–D5/additional questions. Existing IDs/meanings and the authoritative specification/ADRs are unchanged; proposed IDs are not implementation or acceptance evidence. No app/build/test/deployment/update/source-refresh/package-manager/host action occurred; build/test exemption is explicit for this documentation-only task. Owner decisions: tags only, OpenAI first, 7-Zip test candidate, 30-day local diagnostic/redacted-explanation retention with no saved prompts, USD $200/month and $0.25/request with a separate $1 total first-live-test cap. Spending records/reservations remain separate from content retention. Public documentation confirms eligible $200 provider-credit benefits, not a current account balance. A1 is complete; next is Task 44 (not started). No future ADR acceptance, implementation or live action is implied. |
| 24 — downloaded-artifact cache and safe cleanup | `FR-DAT-08–11/13–15`, `NFR-PERF-10`, `AC-DAT-08–11/14–17`, `AC-NFR-10`, `AC-UX-07` | SQLite ownership ledger persists relative path, size, hash, job association, outcome, active lease and last-cleaned time. A literal per-user PC Steward download root uses canonical containment, ADS/UNC/device rejection, reparse-component rejection, and a non-following handle-bound delete. Retention (seven-day verified / 24-hour failed-incomplete), 2 GB soft quota, oldest-inactive eviction, locked residuals, idempotence and traversal preservation are covered by focused fixtures. Settings and update-completion cleanup disclose and preserve all provider caches. |
| 24A — Phase 3 evidence readiness | `FR-UPD-01–11`, `FR-DAT-08–15`, `NFR-REL-03`, `NFR-OBS-01–05`, `NFR-PERF-10`, `AC-UPD-*`, `AC-DAT-*`, `AC-NFR-10`, `TM-03–06/12/15` | `docs/review/PHASE_3_EVIDENCE_READINESS.md` records passing static, hostile-fixture, security, architecture, cache/batch/diagnostic, build and full-suite evidence. The product-owner-approved [host-test exception](../review/PHASE_3_HOST_TEST_EXCEPTION.md) adds valid temporary signing, no-op UAC acceptance/denial and pre-provider teardown, plus 10 isolated symbolic-link/race/scratch-VHD assertions with complete cleanup. A separately approved Firefox exercise proved a real reviewed machine-scope `152.0.6` → `153.0.1` update by exact post-update WinGet re-inventory; the fixture and temporary certificate were removed afterward. The app crash before outcome presentation is documented with packaging/dialog-lifecycle remediations and green regression evidence; no second live replay was performed. Task 24A is ready for the required Task 25 review, which must run under Sol / Extra High. |
| 19 — Manual decisions and update-intelligence UX | `FR-POL-01–05`, `FR-UI-01–07`, `FR-HIS-02/04`, `AC-MAT-02/07`, `AC-UX-01–06` | Durable user-decision service stores optional reasons and ignore expiry, allows only exact package IDs supplied by enabled trusted connectors, supports reversal and policy reapplication, and preserves decisions independently of scan data. The presentation model exposes counts, attention, freshness, per-product safety evidence and a disabled-update explanation; desktop details support current-version pin and indefinite ignore without creating execution authority. |
| 90 — Phase 3 review remediation | `FR-UPD-02–06/08–09`, `FR-DAT-08/10/15`, `NFR-MNT-03`, `NFR-REL-03`, `NFR-OBS-03`, `NFR-PERF-10`, `AC-UPD-01–02/04/06/08/10`, `AC-DAT-08/11/16`, `AC-NFR-10`, `TM-01–06/10–13/15` | [Final P3R-14/P3R-15 evidence](../review/TASK_90_P3R14_P3R15_EVIDENCE.md) composes the official WinGet COM structured-candidate authority in the actual desktop, binds exact trusted source/ID/installed/candidate versions through review and targeted verification, and leaves display tables non-executable. The approved [raw host record](../review/TASK_90_P3R15_HOST_EVIDENCE.json) proves the real fixed-root validator/default transport with exact protected ACLs, valid same-signer binaries and a completed signed no-op UAC path; real tamper, trusted-wrong-signer, caller-writable, helper-reparse and between-validation replacement layouts all have zero helper launches, followed by verified root/certificate teardown. The final running-target gate remains closed. |
| 25 — Phase 3 security review gate | All Phase 3 requirements and criteria | Fresh GPT-5.6 Sol / Extra High review at exact implementation commit `5013c942ae2fa0dc62c9bf71258bcbffedd03347` passes the gate with `P3R-01–15` closed and no remaining Blocker, High or Medium finding. Focused 61, security 19, architecture 5, Release build, full suite 182, hostile 42/1 approved skip, WinGet 11, dependency and read-only installed-helper teardown checks pass. See [Phase 3 security review](../review/PHASE_3_SECURITY_REVIEW.md). Phase 3 was subsequently merged through `b473792`. |
| Design visual foundation — supporting Task 26 | `AC-UX-02–04` (Optional — best effort) | Central WinUI semantic light/dark/high-contrast resources, typography, reusable control styles, and the approved PC Steward identity assets are integrated into the desktop host. This is implementation plumbing only: it does not claim accessibility acceptance from appearance, alter existing workflows, or implement Task 26 scheduling/notifications. |
| 26 — scheduled checks and Windows notifications | `FR-SCH-01–05`, `FR-RSC-04`, `NFR-PERF-07`, `AC-NFR-11`, `AC-UX-02/04/05` | Persisted Manual/Daily/Weekly settings, a fixed-action current-user Task Scheduler adapter, a scan-only low-priority one-shot host, overlap mutex, bounded exit, repeated-failure/attention-only notification policy, and the NavigationView Settings surface are covered by 111 focused integration tests and the 187-test full suite. The host has no execution/elevation reference and receives no task arguments. A live schedule was not created because the required explicit approval was not supplied; the visual foundation remains optional best-effort evidence, not accessibility acceptance. |
| 26B — native shell, onboarding and scan experience | `FR-ONB-01–04`, `FR-UI-02–03/05/07`, `FR-RSC-01–03/05`, `FR-HIS-01`, `AC-UX-01–04/06/09` | Navigation routes retain the active operation banner and present a dedicated scan screen with local-only guidance, explicit read-only/no-elevation scope, cancellation, prior-snapshot retention and text provider-warning status. The state matrix includes partial/failed scan warnings with prior freshness retained. Release build passed with 0 warnings/errors and 188 tests passed; visual/accessibility evidence remains best effort only. |
| 26C — applications, findings and update review experience | `FR-UI-01–07`, `FR-ID-01–07`, `FR-SRC-01–05/10`, `FR-POL-01–05`, `FR-UPD-01–11`, `AC-MAT-01–07`, `AC-UPD-01–11`, `AC-UX-02–06` | Updates route presents only typed application evidence, with source, package ID, versions, architecture, channel, confidence and freshness; non-verified/stale/unavailable evidence has an explicit non-actionable explanation. Existing immutable reviewed-update workflow remains the sole execution path. Release build passed with 0 warnings/errors and 188 tests passed. |
| 26A — factory-reset design evidence readiness | `FR-DAT-04/12–15`, `AC-DAT-12–16/18`, `AC-UX-07–08`, ADR-013/019 | `review/FACTORY_RESET_DESIGN_EVIDENCE_READINESS.md` inventories fixed roots, retention, active-work locks, scheduler ownership, next-launch marker/handle sequence, hostile-path coverage and the two-dialog UI contract. It records the reset-manifest hostile fixture as Missing with a precise Task 28 action; no reset was implemented or exercised. |
| 27 — factory-reset design gate | `FR-DAT-04/12–15`, `AC-DAT-12–16/18`, `AC-UX-02/04/07–08`, ADR-013/019 | `architecture/FACTORY_RESET_DESIGN.md` accepts the fixed next-launch marker/no-helper mechanism after inspecting actual SQLite, logging, cache and scheduled-host lifetimes. It freezes the logical/physical manifest, shared cross-process lease, marker-before-store ordering, transaction/failure/verification rules, advanced controls, uninstall interaction, hostile fixture matrix and two-dialog native UI contract. ADR-013 is Accepted; Release build passed with 0 warnings/errors and the full suite passed 188/188. Task 28 implementation evidence remains mandatory. |
| 28 — factory reset and advanced data controls | `FR-DAT-04/12–15`, `AC-DAT-12–16/18`, `AC-UX-02/04/07–08`, ADR-013/019 | Native controls independently clear source metadata, logs, update history, and mappings/policies with transaction boundaries and Cancel-default reviews. Task 90 adds a fail-closed post-reset onboarding decision: a failed acknowledgement or residual marker cannot load inventory or navigate to Scan. Executable failed/no-marker and successful/residual-marker scenarios pass; focused reset is 25/25, cleanup contract passes, Architecture is 17/17, and the serialized component suite is 251/251. Destructive supported-host observations remain unproven. |
| 29 — accessibility, reliability, and performance hardening | `NFR-PERF-01–10`, `NFR-REL-01–05`, `AC-NFR-01–11`, `AC-UX-01–09` | **Accepted for exact-hash owner-only non-public evaluation.** Earlier fixture/UI Automation/WPT and measured 3XS / `3XS-Overclock` CPU/memory evidence remains retained. `PO-EXC-31-FRA14-OWNER-ONLY-PERFORMANCE` leaves current final-app repeatability, live-source, idle I/O/network, process-tree, update responsiveness and scheduled final-timeout observations `Unverified` but non-blocking only within the Task 31 owner-only boundary. Thresholds are unchanged; another recipient or any distribution/release claim expires the exception. |
| 29A — prototype-parity UI completion | `FR-UI-01–07`, `FR-ONB-03–04`, `FR-DAT-01/03–04/06–15`, `FR-SCH-01–05`, `AC-UX-01–09` | **Complete.** Existing Overview, Scan, Applications, Updates, Activity, Privacy & data, and Settings surfaces now follow the `design/native-prototype/` visual/interaction reference without adding future modules or changing scan, update, deletion, scheduling, or elevation authority. Cross-page Overview/Updates counts now use unfiltered local inventory so Apps filters remain view-only. Release build passed with 0 warnings/errors; full suite 191 passed; Release UI Automation review observed named existing controls and normal close without invoking a state-changing action. See `docs/review/TASK_29A_PROTOTYPE_PARITY_EVIDENCE.md`. Task 30 packaging follows only after its required model/reasoning setting is confirmed. |
| 29B — complete supported native-prototype parity | `FR-UI-01–07`, `FR-ONB-03–04`, `FR-DAT-01/03–04/06–15`, `FR-SCH-01–05`, `AC-UX-01–09` | **Complete.** Added real-data Scan results, Review actions, and Storage surfaces; completed prototype-style Apps/Updates information layouts; and combined the two existing WinGet connectors behind one disclosure-gated `WinGet trusted sources` toggle. Prototype-only test/future concepts are intentionally excluded at the product owner's direction. The combined toggle itself performs no scan, source refresh, download, or update. Release build passed with 0 warnings/errors; full suite 192 passed; UI Automation verified the named combined toggle and production navigation. See `docs/review/TASK_29B_COMPLETE_PROTOTYPE_PARITY_EVIDENCE.md`. Task 30 remains gated by its required model/reasoning setting. |
| 29C — repository-current application evidence | `FR-SRC-03–04`, `FR-UI-01/03–05`, `NFR-REL-01/04`, `AC-UX-02–06` | **Complete.** Up to date lists only installed applications with `Current` exact-match intelligence and a named source/package identity; live/cached freshness remains visible and local-only rows are excluded. Refresh is explicit and only checks the enabled sources; it cannot start an update. Release build passed with 0 warnings/errors; full suite 193 passed; UI Automation observed the named navigation and Overview entry point, then normal close with no residual process. See `docs/review/TASK_29C_UP_TO_DATE_REPOSITORY_EVIDENCE.md`. Task 30 remains gated by its required model/reasoning setting. |
| 29D — repository-managed application status | `FR-SRC-03–04`, `FR-UI-01/03–05`, `NFR-REL-01/04`, `AC-UX-02–06` | **Complete.** Repository apps broadens the Task 29C current-only presentation to concrete, enabled-source package identities with an explicit `Managed · up to date`, `Managed · update available`, or `Repository match · review required` state. Local-only and candidate-only rows remain excluded; disabling the source hides prior cached claims. Release build passed with 0 warnings/errors; full suite 193 passed. See `docs/review/TASK_29D_REPOSITORY_MANAGED_STATUS.md`. Task 30 remains gated by its required model/reasoning setting. |
| 29E — repository evidence, provider resilience, and readable freshness | `FR-SRC-01–04`, `FR-UI-01/03–05`, `NFR-REL-01/04`, `AC-UX-02–06` | **Complete.** A source refresh now merges each enabled source's managed installed-package list with available updates, giving explicit equal-version current evidence as well as newer-version availability. WinGet source-warning preambles no longer discard the following valid table; the AppX catalog skips inaccessible package metadata and reports an unavailable catalog rather than a generic provider failure; timestamps use readable local date/time. Host inspection found Community `winget` configured and the registered `msstore` source unavailable on lookup. Release build passed with 0 warnings/errors; full suite 196 passed. See `docs/review/TASK_29E_REPOSITORY_PROVIDER_EVIDENCE.md`. Task 30 remains gated by its required model/reasoning setting. |
| 29F — WinGet inventory scan follow-up | `FR-SRC-01–04`, `FR-UI-01/03–06`, `NFR-REL-01/04`, `AC-UX-02–06` | **Offline verification complete; recorded-host recheck pending.** The recorded-host failure showed that a non-elevated desktop cannot enumerate protected WindowsApps. WinGet discovery uses the registered App Installer package catalog entry with the expected Microsoft publisher ID, then preserves fixed-root, reparse-point, and Authenticode validation. A source-scoped refresh can fall back only to exact source-labelled global inventory; unknown availability is unavailable/non-executable, not current or a user decision. Review-required items remain visible separately from approved actions, and application activation uses a product-detail route with a technical disclosure. Release build passed with 0 warnings/errors; full suite 201 passed. The enabled-source host scan still requires the user's in-app confirmation. See `docs/review/TASK_29F_WINGET_INVENTORY_FOLLOW_UP.md`. Task 30 remains gated by its required model/reasoning setting. |
| 30/30A — standalone packaging and final evidence readiness | `AC-DAT-18`, `AC-DAT-01/12–13`, `AC-UPD-03/06`, ADR-002/008–010/018, `TM-04/18` | **Task 31 passes only for product-owner-only non-public evaluation.** Archive SHA-256 `16DE2AFB0CBF26BAE167DFABFD7A4FCAE619ABA317BFE97076D4FDD16ADE0523` mechanically validates 94 payload files / 49,955,522 bytes, 15 components, 16 SBOM nodes/32 edges, hashes/licences and notices. `PO-EXC-31-FRA15-OWNER-ONLY-LIFECYCLE` and `PO-EXC-31-FRA16-LOCAL-UNSIGNED` leave current lifecycle, signer, channel, recipient, support and distributable rollback evidence `Unverified`; they are non-blocking only for the exact owner-only local candidate. External/private distribution and public release remain prohibited. |
| 21 — immutable update jobs and constrained non-elevated runner | `FR-UPD-01–04/07`, `NFR-MNT-03`, `NFR-OBS-01–03`, `AC-UPD-01–05/07/11` | Explicit confirmation creates a get-only typed job snapshot with a canonical digest; SQLite appends it and rejects mutated rows. Fresh exact evidence is revalidated immediately before execution. The WinGet adapter owns the fixed executable and exact argument construction, accepts no command/path/environment input, runs user-scope jobs only, and returns machine scope as elevation-required without execution. Results distinguish denial, elevation, cancellation, timeout, output overflow, product failure, provider failure and awaiting verification; exit zero is never Success. Release build passed with zero warnings/errors; 109 tests passed, including injection, mutation, fixed-surface, sanitization and non-zero-exit fixtures. |
| 22 — short-lived elevated helper and secure IPC | `FR-UPD-04`, `AC-UPD-03–05`, `TM-04` | Machine scope alone reaches an on-demand `runas` launch after fresh immutable-job revalidation. A current-user/SYSTEM, local-only, single-instance random pipe binds both process IDs, user SID, session, fixed paths, trusted Authenticode signatures and the same signer. Bootstrap carries only protocol/PID/pipe/nonce; the one-shot bounded request rejects replay, expiry, mutation, unknown/duplicate fields, paths and free-form authority. The machine-only WinGet adapter reconstructs fixed arguments; denial, cancellation, timeout, crash/failure, provider failure and completion remain distinct, sanitized, and never Success before Task 23 verification. The approved host substitute recorded valid temporary signatures, UAC acceptance to pre-provider exit `24`, UAC denial without helper launch, and zero residual process/pipe/certificate state; it does not prove per-user no-prompt, provider-disconnect cancellation, or clean-image behavior. |
| 23 — sequential batches, verification, history and diagnostics | `FR-UPD-05–06/08–11`, `FR-HIS-01–06`, `NFR-REL-03`, `NFR-OBS-01–05`, `AC-UPD-06–11`, `AC-UX-05` | Typed sequential batch coordinator records append-only queued/running/outcome events and never exposes force-close authority. Cancellation marks only unstarted jobs cancelled; a started item whose provider/verification state is interrupted remains `VerificationRequired`, including crash recovery, rather than being relabelled queued/cancelled. A successful provider result is verified by targeted re-inventory before it becomes Success; otherwise it is Restart required, Inconclusive, or Verification failed. SQLite retains per-product timelines and bounded verbose events. Sanitized copyable JSON diagnostics redact credentials, paths and control characters. Controlled fixtures cover independent batches, cancellation, crash recovery, negative source/download/installer/elevation/timeout outcomes, persistence, retention and diagnostics. |

| ID | Requirement / criterion | MVP marker | Task(s) |
|---|---|---|---|
| `FR-ONB-01` | Explain local inventory collection, network source access, permissions and data storage before the first scan. | Yes | 13 |
| `FR-ONB-02` | Permit a local-only first scan. | Yes | 13 |
| `FR-ONB-03` | Show all enabled connectors and their trust category. | Yes | 13 |
| `FR-ONB-04` | Allow the user to disable nonessential connectors. | Yes | 13 |
| `FR-INV-01` | Read machine-wide 64-bit uninstall registry entries. | Yes | 08 |
| `FR-INV-02` | Read machine-wide 32-bit uninstall registry entries. | Yes | 08 |
| `FR-INV-03` | Read current-user uninstall registry entries. | Yes | 08 |
| `FR-INV-04` | Enumerate installed MSIX/AppX packages visible to the current user. | Yes | 09 |
| `FR-INV-05` | Import installed-application information reported by WinGet. | Yes | 10 |
| `FR-INV-06` | Preserve raw provider records for diagnosis while presenting normalized products in the UI. | Yes | 08, 09, 10 |
| `FR-INV-07` | Deduplicate equivalent records without merging genuinely separate user/machine or architecture instances. | Yes | 11 |
| `FR-INV-08` | Capture name, version, publisher, install scope, architecture, source provider, install date and path where safely available. | Yes | 08, 09, 10 |
| `FR-INV-09` | Classify runtimes, redistributables, drivers, system components and applications separately where evidence allows. | Partial | 08, 11 |
| `FR-ID-01` | Maintain a canonical product identity separate from each install instance. | Yes | 11, 17 |
| `FR-ID-02` | Store exact package IDs returned by trusted connectors. | Yes | 17 |
| `FR-ID-03` | Compute a deterministic confidence level and explanation for each mapping. | Yes | 17 |
| `FR-ID-04` | Prevent low-confidence candidates from becoming executable update actions. | Yes | 17, 38 |
| `FR-ID-05` | Allow a user to create, review and delete a manual mapping to a trusted connector package. | Yes | 17, 38 |
| `FR-ID-06` | Preserve mapping origin: automatic, provider-supplied, user-confirmed or imported. | Yes | 17, 38 |
| `FR-ID-07` | Detect channel conflicts such as Stable versus Beta where source metadata permits. | Partial | 17 |
| `FR-SRC-01` | Support the WinGet default package source through a connector abstraction. | Yes | 18 |
| `FR-SRC-02` | Support Microsoft Store package information available through WinGet. | Yes | 18 |
| `FR-SRC-03` | Refresh source metadata on demand and expose the last successful refresh time. | Yes | 18 |
| `FR-SRC-04` | Cache source results so previous data remains visible during a temporary outage. | Yes | 18 |
| `FR-SRC-05` | Treat source metadata as untrusted and validate field lengths, formats and allowed values. | Yes | 18 |
| `FR-SRC-06` | Permit a user-confirmed mapping to an official GitHub Releases repository for read-only version checks. | Desirable MVP | 18B |
| `FR-SRC-10` | Let the user add only connector types supported by the product; no raw command connector. | Yes | 18 |
| `FR-VER-01` | Compare numeric dotted versions correctly when both sides provide them. | Yes | 16 |
| `FR-VER-02` | Handle prerelease labels and avoid offering Beta/Preview builds to Stable users by default. | Yes | 16 |
| `FR-VER-03` | Defer to connector-specific version semantics where generic comparison is unsafe. | Yes | 16 |
| `FR-VER-04` | Mark a result as unknown rather than inventing an ordering for incomparable versions. | Yes | 16 |
| `FR-VER-05` | Prevent a downgrade unless the user enters a dedicated future downgrade workflow. | Yes | 16 |
| `FR-UI-01` | Show counts for verified updates, review-required updates, current, unmanaged, pinned, ignored and failed items. | Yes | 18, 19, 35 |
| `FR-UI-02` | Support search, sorting and filtering by status, publisher, source and scope. | Yes | 13, 19, 35 |
| `FR-UI-03` | Show an application detail page with installed and candidate metadata side by side. | Yes | 13, 19, 35 |
| `FR-UI-04` | Display source provenance, package ID and confidence explanation before an update action. | Yes | 18, 19, 35 |
| `FR-UI-05` | Show last scan time and whether the displayed result is live or cached. | Yes | 13, 18, 19, 35 |
| `FR-UI-06` | Provide a compact “attention needed” view. | Yes | 18, 19, 35 |
| `FR-UI-07` | Provide a detailed diagnostics view without exposing unnecessary implementation noise by default. | Yes | 13, 19, 35 |
| `FR-UPD-01` | Require explicit selection and confirmation before every update or batch. | Yes | 21 |
| `FR-UPD-02` | Execute only provider-defined, structured operations against an immutable package ID. | Yes | 21 |
| `FR-UPD-03` | Never execute a command string retrieved from the local database or remote metadata. | Yes | 21 |
| `FR-UPD-04` | Request elevation only when the provider determines it is needed. | Yes | 21, 22 |
| `FR-UPD-05` | Warn when a target application appears to be running. | Yes | 23 |
| `FR-UPD-06` | Do not force-close an application without explicit consent. | Yes | 23 |
| `FR-UPD-07` | Capture start time, end time, provider, package ID, target version, exit code and sanitized output. | Yes | 21 |
| `FR-UPD-08` | Re-inventory the target after the installer exits. | Yes | 23 |
| `FR-UPD-09` | Report success only when the expected version is detected or the provider supplies a reliable restart-pending result. | Yes | 23 |
| `FR-UPD-10` | Run MVP batch updates sequentially. | Yes | 23 |
| `FR-UPD-11` | Allow cancellation before the next queued update begins. | Yes | 23 |
| `FR-POL-01` | Pin a product to its current version, a major/minor branch or a source channel where supported. | Partial MVP: current version/source package | 19, 38 |
| `FR-POL-02` | Ignore an application indefinitely or until a selected date. | Yes | 19, 38 |
| `FR-POL-03` | Record an optional reason for pinning, ignoring or manually mapping. | Yes | 19, 38 |
| `FR-POL-04` | Show all active decisions in one settings view. | Yes | 19 |
| `FR-POL-05` | Make all decisions reversible. | Yes | 19, 38 |
| `FR-HIS-01` | Store scan summaries and update-job history locally. | Yes | 12, 23 |
| `FR-HIS-02` | Show a timeline for each product. | Yes | 19, 23 |
| `FR-HIS-03` | Distinguish source, download, installer, elevation, timeout and verification failures. | Yes | 23 |
| `FR-HIS-04` | Apply configurable retention limits to verbose logs and source caches. | Yes | 19, 23 |
| `FR-HIS-05` | Allow a user to copy a sanitized diagnostic bundle. | Yes | 23 |
| `FR-HIS-06` | Never include credentials, tokens or full environment dumps in exported diagnostics. | Yes | 23 |
| `FR-SCH-01` | Allow an optional daily, weekly or manual-only scan schedule. | Yes | 26 |
| `FR-SCH-02` | Use Windows Task Scheduler rather than an always-running service. | Yes | 26 |
| `FR-SCH-03` | Scheduled scans may check but must not install updates in the MVP. | Yes | 26 |
| `FR-SCH-04` | Show a Windows notification only when attention is required or a scan fails repeatedly. | Yes | 26 |
| `FR-SCH-05` | Allow the schedule and notifications to be disabled independently. | Yes | 26 |
| `FR-DAT-01` | Export current inventory to CSV and JSON. | Yes | 14 |
| `FR-DAT-02` | Optionally include update history in JSON export. | Yes | 14 |
| `FR-DAT-03` | Warn that exports reveal installed software and potentially identifying machine metadata. | Yes | 14 |
| `FR-DAT-04` | Provide advanced controls to clear source metadata, logs, update history or manual mappings/policies independently. | Yes | 28 |
| `FR-DAT-06` | Provide a dedicated **Clear scan data** button that removes current inventory, raw provider records, update candidates and scan-run records. | Yes | 14 |
| `FR-DAT-07` | **Clear scan data** must preserve global settings, enabled connectors, schedule, update-job history and app-owned downloaded update files. User-created mappings, pins and ignores must be retained in a stable decision store and reapplied when the same product is rediscovered. | Yes | 14 |
| `FR-DAT-08` | Provide a dedicated **Clear downloaded files** button in Settings that deletes only PC Steward-owned update payloads and temporary extraction files not in active use. | Yes | 24 |
| `FR-DAT-09` | Show **Delete downloaded files** on the update-completion view whenever PC Steward-owned files remain, together with the space recoverable. | Yes | 24 |
| `FR-DAT-10` | Display the current size, last-cleaned time and deletion scope for scan data and downloaded update files. | Yes | 14, 24 |
| `FR-DAT-11` | Do not delete Windows, WinGet, Microsoft Store or another provider's cache unless that provider exposes a documented, safe cleanup operation; disclose this boundary in the UI. | Yes | 24 |
| `FR-DAT-12` | Provide a dedicated **Factory reset PC Steward** button that removes all local application data, settings, decisions, caches, logs, consent records and the current-user scheduled task, then returns the next launch to first-run onboarding. The installed app binaries remain. | Yes | 28 |
| `FR-DAT-13` | Require an explicit confirmation for each destructive action and a second confirmation for factory reset. | Yes | 14, 24, 28 |
| `FR-DAT-14` | Disable destructive controls while a dependent scan or update is active, or require the user to cancel the operation first. | Yes | 14, 24, 28 |
| `FR-DAT-15` | Deletion operations must be idempotent, report locked or undeletable items accurately and never claim success while relevant app-owned data remains. | Yes | 14, 24, 28 |
| `FR-RSC-01` | Display scan phase, elapsed time and a **Cancel scan** action for manual scans. | Yes | 12, 13 |
| `FR-RSC-02` | Permit only one active full scan and prevent it from overlapping update execution, except for a controlled post-update verification scan. | Yes | 12 |
| `FR-RSC-03` | Apply bounded connector concurrency, per-provider timeouts and cancellation tokens so one source cannot consume unbounded resources or block the whole scan. | Yes | 12 |
| `FR-RSC-04` | Run scheduled scans through Task Scheduler without an always-running process, at lower priority where supported, and exit after completion, cancellation or timeout. | Yes | 26 |
| `FR-RSC-05` | Record scan duration and provider timings locally for performance diagnosis without collecting unrelated application-usage telemetry. | Yes | 12 |
| `NFR-PERF-01` | Local inventory must complete within 15 seconds at the 95th percentile on the agreed baseline PC with 300 provider records. | Mandatory | 04, 12, 29 |
| `NFR-PERF-02` | A full online check must complete within 90 seconds at the 95th percentile when sources are healthy; source timeouts must not block the whole application indefinitely. | Mandatory | 04, 18, 29 |
| `NFR-PERF-03` | The UI must remain responsive during scans and updates. | Mandatory | 12, 29 |
| `NFR-PERF-04` | The application should cold-start to an interactive shell within five seconds on the baseline PC. | Mandatory | 29 |
| `NFR-PERF-05` | With the UI open and no active job for five minutes, average PC Steward CPU usage must remain at or below 1% of total system CPU, working set should remain at or below 300 MB, and the app must not perform recurring disk or network activity. | Mandatory | 12, 29 |
| `NFR-PERF-06` | During a local scan on the baseline PC, the combined PC Steward process tree must average no more than 20% total CPU and remain below 500 MB peak working set. App-owned work must not sustain more than 50% total CPU for over five seconds. | Mandatory | 04, 12, 29 |
| `NFR-PERF-07` | Closing the UI must leave no resident PC Steward process when no approved update or scheduled scan is active. A scheduled scan process must exit within 30 seconds after completion, cancellation or final timeout handling. | Mandatory | 26, 29 |
| `NFR-PERF-08` | A scan cancellation request must be acknowledged in the UI within two seconds and return the application to an operable state within five seconds, including termination of cancellable child-provider work. | Mandatory | 04, 12, 29 |
| `NFR-PERF-09` | Only one full scan may run at a time. Online connector concurrency defaults to a maximum of three and must be configurable internally without a code rewrite. | Mandatory | 04, 12, 29 |
| `NFR-PERF-10` | PC Steward-owned downloaded update files must be bounded by a configurable soft quota with a 2 GB default, excluding a file actively required by an update. | Mandatory | 24, 29 |
| `NFR-REL-01` | A failed connector must not discard successful results from other connectors. | Mandatory | 18, 29 |
| `NFR-REL-02` | Database writes must be transactional so interrupted scans do not corrupt the last known good inventory. | Mandatory | 12, 29 |
| `NFR-REL-03` | A cancelled or crashed batch must preserve completed job results and leave unstarted jobs clearly queued or cancelled. | Mandatory | 23, 29 |
| `NFR-REL-04` | Cached results must always show their timestamp and must not be presented as a live successful check. | Mandatory | 18, 29 |
| `NFR-REL-05` | The application must recover gracefully from malformed or missing registry values. | Mandatory | 08, 29 |
| `NFR-MNT-01` | Inventory providers and update connectors must implement stable interfaces and fail independently. | Mandatory | 12, 18, 29 |
| `NFR-MNT-02` | Matching and version rules must be unit-testable without launching the UI. | Mandatory | 11, 16, 29 |
| `NFR-MNT-03` | Provider commands, argument construction and result parsing must be isolated from presentation code. | Mandatory | 18, 21, 29 |
| `NFR-MNT-04` | Database migrations must be versioned and reversible during development. | Mandatory | 12, 29 |
| `NFR-MNT-05` | Architecture decisions affecting security or deployment must be recorded under `docs/adr`. | Mandatory | 29 |
| `NFR-OBS-01` | Each scan and update job receives a correlation ID. | Mandatory | 12, 21, 23, 29 |
| `NFR-OBS-02` | Logs use structured events and severity levels. | Mandatory | 12, 21, 23, 29 |
| `NFR-OBS-03` | Diagnostics distinguish user cancellation from product or installer failure. | Mandatory | 21, 23, 29 |
| `NFR-OBS-04` | Logs are bounded by retention and file-size policy. | Mandatory | 23, 29 |
| `NFR-OBS-05` | User-facing diagnostics can be exported in sanitized form. | Mandatory | 23, 29 |
| `AC-INV-01` | Given applications registered in each supported inventory surface, a scan creates a record for every fixture entry expected from that surface. | Mandatory | 08, 09, 10 |
| `AC-INV-02` | Duplicate raw records for the same install instance are presented once, while distinct user/machine or x86/x64 instances remain separately inspectable. | Mandatory | 11 |
| `AC-INV-03` | The scan does not invoke `Win32_Product` and does not trigger MSI repair operations. | Mandatory | 08 |
| `AC-INV-04` | A malformed or incomplete registry entry does not crash the scan; it is either represented with unknown fields or recorded as a provider warning. | Mandatory | 08, 09, 10 |
| `AC-INV-05` | A local-only scan makes no update-source network request. | Mandatory | 10 |
| `AC-INV-06` | A normal scan does not require or request administrator elevation. | Mandatory | 08, 09, 10 |
| `AC-INV-07` | The application shows the provider and freshness of every displayed inventory record. | Mandatory | 08, 09, 10 |
| `AC-MAT-01` | Every actionable update displays a stable package ID, source, candidate version, confidence and explanation. | Mandatory | 17, 18 |
| `AC-MAT-02` | No low-confidence or unmatched candidate exposes an enabled Update button. | Mandatory | 17, 19, 38 |
| `AC-MAT-03` | The curated fixture set produces zero false-positive executable update mappings. | Mandatory | 17, 18 |
| `AC-MAT-04` | Architecture or channel incompatibility prevents an update from being actionable. | Mandatory | 16, 17, 18 |
| `AC-MAT-05` | Version strings that cannot be ordered safely produce an Unknown/Review state rather than a guessed update. | Mandatory | 16, 17, 18, 38 |
| `AC-MAT-06` | A temporary source failure leaves the last cached result visible with a stale timestamp and a source-unavailable warning. | Mandatory | 18 |
| `AC-MAT-07` | The user can create and later delete a manual mapping to a trusted package ID. | Mandatory | 17, 19, 38 |
| `AC-UPD-01` | No installer runs without the user selecting it and confirming the individual or batch review screen. | Mandatory | 21 |
| `AC-UPD-02` | The update runner accepts only approved provider operations and rejects arbitrary executable paths or command strings. | Mandatory | 21 |
| `AC-UPD-03` | A per-user update that does not require elevation completes without a UAC prompt. | Mandatory | 21, 22 |
| `AC-UPD-04` | A machine-wide update requests UAC only when execution begins, not during scanning or review. | Mandatory | 21, 22 |
| `AC-UPD-05` | If the user declines UAC, the job is recorded as cancelled/denied rather than failed or successful. | Mandatory | 21, 22 |
| `AC-UPD-06` | After a successful test update, the post-update scan detects the expected newer version. | Mandatory | 23 |
| `AC-UPD-07` | A non-zero provider or installer result is never displayed as success. | Mandatory | 21, 23 |
| `AC-UPD-08` | If the installer reports success but the expected version is not detected, the result is Inconclusive or Restart required, not Success. | Mandatory | 23 |
| `AC-UPD-09` | Batch execution runs sequentially and reports each item's independent result. | Mandatory | 23 |
| `AC-UPD-10` | Cancelling a batch prevents the next unstarted item from running and preserves completed results. | Mandatory | 23 |
| `AC-UPD-11` | Update jobs record timestamps, provider, package ID, versions, exit code and sanitized diagnostic output. | Mandatory | 21, 23 |
| `AC-DAT-01` | Closing and reopening PC Steward preserves inventory, mappings, decisions and update history. | Mandatory | 14 |
| `AC-DAT-02` | CSV and JSON exports match the displayed current inventory and identify export time. | Mandatory | 14 |
| `AC-DAT-03` | The product functions for inventory and history while offline. | Mandatory | 14 |
| `AC-DAT-04` | Network inspection confirms that the MVP contacts only enabled connectors and the optional PC Steward update endpoint. | Mandatory | 18, 90 |
| `AC-DAT-05` | No account, email address or cloud sign-in is required. | Mandatory | 14 |
| `AC-DAT-06` | Selecting **Clear scan data** removes current inventory, raw scan/provider records, update candidates and scan-run records, and the dashboard returns to a no-scan state. | Mandatory | 14 |
| `AC-DAT-07` | After **Clear scan data**, global settings, enabled sources, schedule, update-job history and app-owned downloaded files remain unchanged. A subsequent scan can reapply a previously saved pin, ignore or manual mapping to the same product. | Mandatory | 14 |
| `AC-DAT-08` | Selecting **Clear downloaded files** removes all inactive files in the PC Steward-owned download-cache root, updates the displayed size to zero where no locked item remains, and does not alter installed applications, scan data, source metadata or update history. | Mandatory | 24 |
| `AC-DAT-09` | When an update completes and app-owned downloaded files remain, the result view shows **Delete downloaded files** and reports the correct recoverable size. | Mandatory | 24 |
| `AC-DAT-10` | Provider-owned Windows, WinGet or Store caches remain untouched unless a documented provider cleanup adapter is explicitly implemented and tested. | Mandatory | 24 |
| `AC-DAT-11` | A locked or protected app-owned file produces an accurate partial-cleanup result and is not silently reported as deleted. | Mandatory | 24 |
| `AC-DAT-12` | Factory reset removes the SQLite database, scan data, source metadata, logs, update history, app-owned downloaded files, settings, consent records, user decisions and current-user scheduled task after two confirmations. | Mandatory | 28 |
| `AC-DAT-13` | After factory reset and restart, first-run onboarding appears and no previous inventory, history, settings, mappings, pins or ignores is visible. PC Steward remains installed. | Mandatory | 28 |
| `AC-DAT-14` | Clear and reset controls require no administrator elevation for the per-user MVP data model. | Mandatory | 14, 24, 28 |
| `AC-DAT-15` | Clear and reset actions are unavailable during an active update or scan until the user cancels or completes it. | Mandatory | 14, 24, 28 |
| `AC-DAT-16` | Cleanup tests prove that a junction, symbolic link or crafted metadata value cannot cause deletion outside the allowlisted PC Steward data roots. | Mandatory | 24, 28 |
| `AC-DAT-17` | Default logs contain no credentials, licence keys, browser data or document contents. | Mandatory | 14, 24 |
| `AC-DAT-18` | Removing the standalone package deletes extracted binaries and the exact scheduled task; data is retained by default and the existing factory reset is the explicit remove-data choice. | Mandatory | 28, 30 |
| `AC-UX-01` | A first-time user can complete the first scan and identify available updates without using a command line. | Optional — best effort | 13, 19, 29, 35 |
| `AC-UX-02` | All primary navigation, scan, review, update, clear-data and factory-reset actions are keyboard accessible. | Optional — best effort | 13, 14, 19, 26, 28, 29, 35 |
| `AC-UX-03` | Status is expressed through text/icons as well as colour. | Optional — best effort | 13, 19, 29, 35 |
| `AC-UX-04` | Windows text scaling and high-contrast mode do not hide primary controls or status text. | Optional — best effort | 13, 19, 26, 28, 29, 35 |
| `AC-UX-05` | Every failure view contains a plain-language summary and a safe next action. | Optional — best effort | 19, 23, 26, 29, 35, 38 |
| `AC-UX-06` | The UI always distinguishes live, cached and local-only scan results. | Optional — best effort | 13, 18, 19, 29, 35 |
| `AC-UX-07` | The Data & reset page visibly separates routine cleanup from factory reset and shows what each action deletes, preserves and can recover in storage. | Optional — best effort | 14, 24, 28, 29, 35 |
| `AC-UX-08` | Factory reset requires a second explicit confirmation and cannot be triggered by a single accidental click or keyboard activation. | Optional — best effort | 28, 29, 35 |
| `AC-UX-09` | During a scan, the user can see elapsed time, current phase and an enabled **Cancel scan** action. | Optional — best effort | 13, 29, 35 |
| `AC-NFR-01` | Local inventory meets NFR-PERF-01 on the agreed baseline test system over at least 30 repeated runs after warm-up. | Mandatory | 04, 12, 29 |
| `AC-NFR-02` | Full online scan meets NFR-PERF-02 when test sources are healthy over at least 20 repeated runs. | Mandatory | 04, 18, 29 |
| `AC-NFR-03` | Cancelling a scan or connector request returns the UI to an operable state without data corruption and meets NFR-PERF-08. | Mandatory | 04, 12, 29 |
| `AC-NFR-04` | Terminating the application during a scan leaves the previous successful inventory readable on next launch. | Mandatory | 12, 29 |
| `AC-NFR-05` | A connector timeout does not block results from other connectors. | Mandatory | 18, 29 |
| `AC-NFR-06` | Five-minute idle measurement meets the CPU, memory, disk and network limits in NFR-PERF-05. | Mandatory | 12, 29 |
| `AC-NFR-07` | Local-scan process-tree measurement meets the CPU and memory limits in NFR-PERF-06. | Mandatory | 04, 12, 29 |
| `AC-NFR-08` | Closing PC Steward with no active work leaves no PC Steward process after 30 seconds. | Mandatory | 26, 29 |
| `AC-NFR-09` | Attempts to start a second scan are rejected or routed to the existing scan, and measured connector concurrency never exceeds the configured bound. | Mandatory | 04, 12, 29 |
| `AC-NFR-10` | Filling the app-owned download cache beyond its soft quota evicts only inactive oldest files and never blocks or deletes the artifact required by an active update. | Mandatory | 24, 29 |
| `AC-NFR-11` | A scheduled scan runs without launching a persistent tray app and exits within the NFR-PERF-07 limit. | Mandatory | 26, 29 |

## Repository maintenance note (2026-10-29)

[Git repair verification](../review/GIT_PUSH_REPAIR.md) records fresh validation before publishing the pending planning/Task 48 snapshot. No new product requirement is introduced; existing requirement mappings above remain authoritative.


**PR5 Task90 COMPLETE / hosted verified:** FR-UPD-14; retained FR-UPD-03/09,NFR-OBS-01–05: [final evidence](../review/TASK_90_PR5_CI_REPAIR.md). Approved PRheadffcf47b passes hosted run40970089353/job123784375538, merge9d1e181 againstbase4aa0298: standard-user415/415, zero skipped, Release0warnings/errors. Fresh CI-repair review16/16/no findings and existing protected-machine fix preserved. Original thread resolved after verification. No hosted latency/cause proof, merge, deployment, Task50 or phase53/54/distribution closure inferred; final results documentation is local only.

**PR #5 Task90 repair locally reviewed:** FR-UPD-14; retained FR-UPD-03/09,NFR-OBS-01–05: 74c40cc [fresh review](../review/TASK_90_PR5_CI_REPAIR_REVIEW.md) PASS/no findings, independent16/16; full415/415/zero-warning Release retained. All36 source/test identities rechecked. [Hosted validation](../review/TASK_90_PR5_CI_REPAIR.md) remains pending because remote head6e26191 has not received this repair; explicit push authority required. No remote CI success or thread resolution is claimed.

**PR #5 / Task 90 CI repair candidate:** FR-UPD-14; retained FR-UPD-03/09, NFR-OBS-01–05: [hosted failure and corrected evidence](../review/TASK_90_PR5_CI_REPAIR.md). Existing ownership fix 7bde99b verified on live head 6e26191; two snapshot-timing CI failures addressed by stronger actual-root/native-read evidence and two deterministic delayed-read cases. Same 600 ms/under-2 s bounds, no production seam override; 12/12 focused and 415/415 full, zero-warning Release. Main/Opus agreement; scoped review and new hosted CI pending. No hosted pass/deployment/thread resolution claimed.

**Task 90 COMPLETE / Task 49 finding addressed locally:** FR-UPD-14; retained FR-UPD-03/09, NFR-OBS-01–05: fix **7bde99b**, [repair](../review/TASK_90_TASK_49_MACHINE_OWNERSHIP.md) and [fresh review](../review/TASK_90_TASK_49_MACHINE_OWNERSHIP_REVIEW.md) prove the protected provider retains OwnedCompletion before scalar projection/normal helper exit. Four behavioral red failures; focused 40/40; canonical full 413/413 and zero-warning Release; independent 40/40/no findings. All 36 source/test identities rechecked. Task 49 local completion restored with forced-termination/external/restart/host limits retained; no wire Task, GitHub resolution or deployment gate change.

**Task 49 review reopened / Task 90 repair candidate:** FR-UPD-14; retained FR-UPD-03/09, NFR-OBS-01–05: [owned-work projection repair](../review/TASK_90_TASK_49_MACHINE_OWNERSHIP.md) corrects early normal helper exit/admission release by observing WinGet OwnedCompletion before returning the protected scalar result. Four behavioral red failures, focused green 40/40, Release zero warnings/errors, full 413/413. Fresh repair review pending; the earlier complete/PASS records below are superseded for this defect. Wire protocol 1 unchanged; no host/redeployment authority.

**Task 49 — local implementation COMPLETE:** FR-UPD-14; retained FR-UPD-01–11, FR-UI-01–07 and NFR-OBS-01–05: [completion matrix](../review/TASK_49_IMPLEMENTATION.md) and [independent integration review](../review/TASK_49_INDEPENDENT_REVIEW.md). Pinned source cfeb0ef, 35 unchanged source/test identities; Release zero warnings/errors, full 407/407 and independent 117/117, zero skipped/no concrete findings. Q1/Q3, typed opaque progress, durable terminals, owned-work admission, UI contract and unchanged protocol compatibility are covered within the reported local scope. Actual host/provider/WinUI resource evidence and Tasks 53/54/distribution gates stay open; Task 50 is unstarted. Older incomplete/candidate entries below are historical.

**Task 49 — integrated candidate:** FR-UPD-14; retained FR-UPD-01–11, FR-UI-01–07 and NFR-OBS-01–05 map to [implementation/evidence](../review/TASK_49_IMPLEMENTATION.md): review-bound local milestones, opaque indeterminate UI, Q1 cancellation/quotas/terminal ordering, retained owned-work admission, Q3 disclosed coverage and unchanged protocol-1 golden/schema fixtures. Release zero warnings/errors; 407/407, zero skipped. Candidate review pending; no privileged-host/redeployment/Task 53/54/distribution gate is closed.
Task 49 workflow clarification (2026-10-29): AGENTS.md applies the same main-owned Opus/Fable/Astra council protocol to stuck subagents; Opus remains involved through escalation. See docs/review/TASK_49_DESIGN_DIALOGUE.md. No product requirement or gate is newly satisfied by this instruction change.

**Dependency servicing planning integration (2026-10-30):** `NFR-MNT-01/03/05`, retained `NFR-REL-01–05`, `FR-SRC-01–05`, `FR-CAT-01–06`, `FR-UPD-02–04`: [accepted plan and review](../review/V2_DEPENDENCY_SERVICING_PROPOSAL.md) wires 53A/54A/conditional 54B to the existing readiness/review gates and 72 candidate listing. Main/Opus recheck PASS; Release zero warnings/errors/full 415/415 zero skipped. Planning evidence only; no package, effective ADR, source authority, runtime or host acceptance claimed.
