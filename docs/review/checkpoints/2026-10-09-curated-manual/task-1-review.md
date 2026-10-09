# Independent Task 1 review

Candidate: `03adbd0325112ecbf7b6477d713bff94bb39cdcb`.
Recorded base: `764df330da1efd626b194d2d1f5fd1fe1b58087e`.
Requirements: CRD-08; Task 1 health prerequisites for FR-PRV-01-04 and AC-V3-11.

## Verdicts

- **Spec compliance: PASS WITH ONE MINOR EXCEPTION.** The requested typed contract, evidence/consent separation, canonical absence observations, strict Chocolatey discovery and pinned launch validation, and bounded read-only evidence paths are implemented. Cancellation propagation has the exception described in M1. This is a source-review verdict, not a completed Task 1 validation gate.
- **Task code quality: CHANGES REQUIRED.** The change is cohesive and scoped, preserves the existing default process branches, and includes meaningful fake-based behavioral fixtures, but the controller subsequently supplied a candidate CI build failure (I1). Correct that blocker and M1, then obtain passing candidate validation and scoped repair review.
- **Counts: Critical 0 / Important 1 / Minor 1.** I1 is the confirmed analyzer/build blocker; M1 is a cancellation defect. Native behavior and performance remain unverified here.

## Finding I1 — candidate does not pass the required build

**Severity: Important (validation blocker).**

Location: `src/PCSteward.Provider.WinGet/WingetProviderHealthProbe.cs:74:42`.

The controller supplied candidate Windows CI run `37994976065`, job `114038578802`: restore succeeded, but build failed on CA1859 because private `SnapshotSources` returns `IReadOnlyList<string>` rather than its concrete `ReadOnlyCollection<string>` result. CI reported 0 warnings / 1 error; tests were skipped. The task requires a passing build and tests, so the pinned candidate is not ready for completion even though the issue is a small return-type repair. Preserve the public report/interface contracts and behavior, apply the concrete private return type required by the analyzer, then rebuild and run the required tests. Root has already assigned this repair to the original implementer. This finding records controller-provided CI evidence; the reviewer did not run or independently fetch CI.

## Finding M1 — cancellation is swallowed by late trust-failure returns

**Severity: Minor.**

Locations at the reviewed candidate:

- `src/PCSteward.Provider.WinGet/WingetProviderHealthProbe.cs:54` and `:63`.
- `src/PCSteward.Provider.Chocolatey/ChocolateyProviderHealthProbe.cs:61` and `:72`.

Trigger: the caller cancels while a version/source read is in progress, and that read completes by throwing the provider's `*HealthTrustException`. Each specialized catch returns an Untrusted/Unavailable report immediately, bypassing the cancellation check immediately below it. This also has a native path: cancellation can occur during synchronous trust revalidation, followed by an I/O/access/trust failure wrapped by the health process adapter. The adapter throws before reaching its pre-launch cancellation check.

Expected: cancellation propagates as `OperationCanceledException`, consistent with the explicit after-await cancellation checks and the task's cancellation requirement. Actual: `ProbeAsync` completes successfully with a new timestamped report. A caller relying on task cancellation can treat a canceled refresh as completed and publish its evidence. The result is still fail-closed, no client is launched by this failure, and no consent/execution authority is granted; those limits make this Minor rather than an execution-safety defect.

Correction: call `cancellationToken.ThrowIfCancellationRequested()` in these catch blocks before returning the failure report (a filter alone would leak the trust exception when canceled). Add fake-only tests for both providers in which a version/source read cancels the supplied token and throws its typed trust exception; assert cancellation and that no subsequent read occurs. The existing `CancellationAfterAwaitStopsRemainingReads` fixture only exercises WinGet's successful version-return path, so it cannot detect this case. This issue is not mandated by the plan.

## Review evidence and requirement assessment

Read the task brief first, then AGENTS.md, the exact repeated global constraints, task report, review package, current status, approved design section 5, and the directly cited product requirement rows. Inspected the complete candidate source/test changes and the unchanged trust/process implementations needed to assess them. Local HEAD matched the requested candidate, with no tracked working changes at the beginning of review.

| Area | Assessment |
|---|---|
| Contract and authority | `ProviderHealth.cs` matches the requested enum/report/probe shape. Reports have no executable, arguments, approval or source-consent inputs. Configured source identifiers are display evidence; no URL or identifier is used to construct commands. Reasons are fixed text, not raw exceptions. Native probes copy successful source lists into read-only snapshots. |
| Absence versus observation error | New discovery uses bounded directory observations and error-aware attribute reads; it does not interpret the existing resolver's `File.Exists == false` as absence. Missing path components establish absence only after successful accessible parent enumeration. Access/I/O/truncation/malformed-name failures remain Unknown or propagate to Unknown/Unavailable. A discovered untrusted executable does not become Absent. |
| Registered WinGet and root access | A registered canonical directory with a positively observed client can proceed without listing WindowsApps itself. Root listing is needed to establish absence when registered observations do not establish presence. The native resolver's existing signature and canonical checks remain in use; a catalog miss alone does not establish absence. The test at `ProviderHealthTests.cs:197` exercises the root-list-denied positive-registration case through injected observations. |
| Strict Chocolatey | Health explicitly requests strict discovery and explicit strict unchanged-client validation. The native pinned-client constructor retains the captured identity and calls `ValidateUnchanged(..., requireCallerCannotModifyLayout: true)` immediately before constructing/starting the read-only process. The same identity's digest and path are compared after a fresh strict resolution. Default `ResolveExecutable()` and `ValidateUnchanged()` calls remain permissive for existing callers, as before. |
| Commands and bounds | WinGet health arguments are fixed `--version` and `source list --disable-interactivity`; Chocolatey uses fixed `--version --no-color`. No install/upgrade/source modification/agreement/elevation command is added. Health processes have 65,536-character capture limits per stream and 15-second command timeouts. Enumeration, source count/name size, and version size have explicit bounds. Capture is character-bounded, so the report's shorthand “64 KiB output” should not be interpreted as an exact memory-byte measurement. |
| Hostile source/XML input | Chocolatey opens only the internally derived canonical config path after protected-layout/no-reparse checks; reads at most 65,537 bytes to detect overflow; prohibits DTDs and external resolution; caps XML characters; rejects wrong root, duplicate/missing source sections and malformed IDs. WinGet rejects unsuccessful/truncated output, missing table structure and invalid/bounded source names. No configured endpoint is activated. These parsers provide local evidence and do not claim reachability or approval. |
| Failure states | Observation failures return Unknown/Unavailable; positive absence is Current with Unknown trust; positive rejected trust is Unavailable; incomplete version/source evidence is Partial and retains successful fields. Typed late trust failures discard fields and return Untrusted/Unavailable. M1 is the cancellation exception to this otherwise conservative behavior. |
| Tests | New tests exercise accessible absence, observation failure, no launch/source reads for untrusted evidence, explicit strict discovery/revalidation, changed client rejection, root access, bounded evidence, exact arguments, XXE/wrong-root rejection, immutable source snapshots and after-await cancellation. They invoke injected clients/observations and parser functions, not native provider processes. Architecture source guards supplement the behavioral tests; they are not behavioral proof of the native launch boundary. |
| Preservation/scope | The default process constructors retain the 1,000,000-character cap and existing resolver/cancellation/termination behavior. The new launch checks are confined to health constructors. No SDK/dependency pins, settings, membership, queue, provider selection, setup, helper/UAC, scheduled execution, icons, UI lifetime, installed applications or saved user data are changed by the product diff. Documentation changes are within the requested evidence/progress scope. |

## Plan boundaries and cannot-verify items

- **No plan-mandated safety conflict identified.** The explicit strict caller-cannot-modify policy may reject a writable Chocolatey layout; that is the approved readiness rule and is not an automatic repair/setup justification. No automatic adoption, reinstall or bootstrap path is added.
- Failed-refresh retention and stale presentation are not implemented in these stateless provider probes. The report explicitly leaves them to later presentation composition, consistent with the task's separation of stale qualification from the health contract. The later UI must retain the prior report and its original timestamp; this review does not certify that future behavior.
- FR-PRV-02-04 and AC-V3-11 are preserved prerequisite boundaries here, not complete setup/preference/UI deliveries. No claim of their complete delivery is made.
- **Candidate build is blocked; candidate tests have not run.** The controller supplied Windows CI run `37994976065`, job `114038578802`: restore succeeded; build failed only CA1859 at `WingetProviderHealthProbe.cs:74:42` (0 warnings / 1 error); tests skipped. This is I1 above. Root owns its minimal repair, subsequent passing build/tests and scoped re-review. The established red run at `2108285` was accepted from the supplied evidence (596 pass / 13 intended fail / 0 skip) and was not rerun. The known missing local SDK was not reprobed and no blocked local tests were rerun.
- `git diff --check 764df330da1efd626b194d2d1f5fd1fe1b58087e 03adbd0325112ecbf7b6477d713bff94bb39cdcb` passed with no output. This is whitespace verification, not a build/test pass.
- Native Windows catalog/ACL/signature outcomes, actual installed-client version/source-output formats (including localized/empty-source output), side effects internal to trusted clients, cancellation latency and process/resource targets require controlled Windows evidence. No real client, network, UAC, installer, task or application process was invoked here. Synchronous native discovery/trust calls have no measured timing bound in this review; the command timeout is not proof of end-to-end latency compliance.
- Review output is this file only. No source/tests were edited, no commit/push/configuration change was performed, and no subagents were spawned. The controller owns remediation and the CI completion gate.
