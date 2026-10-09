# Task 1 scoped repair re-review

Candidate: `b96a5485b7be9bcb888e12539e1d1c9d5f939ccf`.
Repair base: `03adbd0325112ecbf7b6477d713bff94bb39cdcb`.
Requirements: CRD-08; Task 1 health prerequisites for FR-PRV-01-04 and AC-V3-11; binding global constraints from the task brief and `global-constraints.md`.

## Verdicts

- **I1: ADDRESSED.** The private snapshot return declaration now uses the exact concrete `System.Collections.ObjectModel.ReadOnlyCollection<string>` returned by `Array.AsReadOnly`. Controller-collected candidate Windows CI restores and builds successfully with 0 warnings/errors, then passes the complete suite.
- **Repair spec compliance: PASS.** Public health contracts, immutable snapshot behavior, evidence/consent separation and production trust/read-only boundaries are preserved. The architecture repair scopes the execution-resolver guard to its actual target while preserving its safety assertions.
- **Repair task quality: PASS.** Both repairs are minimal, directly caused by candidate validation failures and supported by passing candidate CI. No new Critical or Important breakage was found in this repair range.
- **Task 1 completion gate: PASS WITH RECORDED MINOR.** Required build/full-suite validation and scoped repair review are complete. Original M1 remains open in the controller ledger under the stated SDD minor policy for final whole-change triage; this verdict does not certify complete later setup/UI requirements, native performance, or the final manual-release phase gate.
- **New repair findings: Critical 0 / Important 0 / Minor 0.** Carried original findings: I1 closed; M1 Minor open (unchanged, outside repair scope).

## Scope and review evidence

Read `AGENTS.md` and `task-1-brief.md` first, then `task-1-report.md` including controller-collected repair evidence, the complete `task-1-repair-review-package.md`, original `task-1-review.md`, and exact repeated `global-constraints.md`. Local `git rev-parse HEAD` matched the requested candidate; initial `git status --short` was clean. Inspected the repair range's five-file scope and the directly relevant current snapshot helper, resolver and identity predicate. No whole original implementation re-review was performed.

The range changes three evidence/progress documents, one private production declaration and one architecture test. `WingetInstalledInventoryProvider.cs`, the actual trusted resolver and all other production behavior are unchanged in this range. SDK/dependency pins, provider commands, executable paths, setup/elevation, installed applications and user state have no repair changes.

## I1 closure and immutable snapshot

At `WingetProviderHealthProbe.cs:74`, changing only the private helper's declared return type from `IReadOnlyList<string>` to `ReadOnlyCollection<string>` satisfies the analyzer without changing the body. Input validation, case-insensitive deduplication, copying with `ToArray` and wrapping with `Array.AsReadOnly` remain identical. The caller still assigns the result to `IReadOnlyList<string>` and the public `ProviderHealthReport` contract remains unchanged. No mutability or execution-authority expansion is introduced. Passing candidate analyzer/build evidence closes the original Important validation blocker.

## Resolver guard repair

`ReferenceBoundaryTests.WinGetResolverUsesThePackageCatalogInsteadOfEnumeratingProtectedWindowsApps` now locates the exact `internal static string ResolveExecutable()` signature and bounds its source slice at the following `private static bool IsDesktopAppInstaller` declaration. Assertions require both markers before using the slice.

Current resolver inspection confirms `RejectReparse(root);` precedes its `new PackageManager().FindPackagesForUser(string.Empty)` call; the catalog filters through `IsDesktopAppInstaller`, which retains the exact `Microsoft.DesktopAppInstaller` name and `8wekyb3d8bbwe` publisher checks. The repaired test retains catalog presence, root-before-catalog ordering and prohibition on `Directory.EnumerateDirectories(root` within the trusted resolver. Its existing publisher assertion still checks the complete file because the predicate is outside the resolver body; this is unchanged coverage, and the resolver still invokes that predicate.

This corrects the false whole-file ordering comparison caused by the earlier health-discovery catalog read. Scoping the no-root-enumeration assertion also matches the intended protected execution resolver: read-only health discovery may enumerate a canonical root to establish positive absence under the brief's error-aware observation requirement. No production trust control was weakened or bypassed. These source assertions remain supplemental static guards, rather than behavioral proof of native trust outcomes.

## Validation accepted for this candidate

Controller-collected evidence in `task-1-report.md`, supplied for the exact candidate:

- Windows CI [37996153500](https://github.com/DanDG923/pc-steward/actions/runs/37996153500), job `114042651116`.
- `pwsh -NoProfile -File ./scripts/Restore.ps1`: succeeded.
- `pwsh -NoProfile -File ./scripts/Build.ps1` invokes `dotnet build PCSteward.slnx --configuration Release --no-restore`: succeeded, 0 warnings/errors.
- `pwsh -NoProfile -File ./scripts/Test.ps1` invokes `dotnet test PCSteward.slnx --configuration Release --no-build`: Architecture 37, Catalog 96, Domain 87, Security 72, Application 329 passed; total **621 passed / 0 failed / 0 skipped**.
- The full suite covers `ProviderHealthTests`, `ProviderHealthBoundaryTests` and the repaired resolver guard. No separate successful focused/local command is claimed.
- Reviewer ran `git diff --check 03adbd0325112ecbf7b6477d713bff94bb39cdcb b96a5485b7be9bcb888e12539e1d1c9d5f939ccf`: exit 0, no output.

CI was accepted from the controller's complete-log evidence; the reviewer did not independently fetch it, rerun CI/tests or repeat absent-SDK checks. Documentation in the repair commits describes earlier intermediate pending states honestly; the appended controller evidence supplies the current candidate result. Controller completion bookkeeping can now record this re-review and final CI result.

## Unresolved and out-of-scope observations

Original **M1 Minor remains open and unchanged**: typed late health trust-exception catches can return an Untrusted/Unavailable report before honoring concurrent cancellation. The repair changes neither those catches nor their tests. This remains a cancellation/reporting issue with no launch, consent or execution-authority leak, retained for final whole-change triage under the supplied minor policy. This report does not claim it is fixed.

Native client outcomes, timing/resource targets, controlled owner-installation/live-use evidence and later last-known timestamp/stale UI composition remain outside this scoped repair review. Existing original review qualifications continue to apply; no new deficiency in those areas is attributed to this repair range. FR-PRV-02-04 and AC-V3-11 remain prerequisite boundaries here, not complete later setup/preference/UI delivery.

Only this re-review report was written. No product/source/test changes, commit, push, Git configuration, network, real provider/application process, UAC, installation or subagent action occurred.
