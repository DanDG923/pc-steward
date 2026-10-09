# Task 1 - provider health

**Status: DONE_WITH_CONCERNS - production candidate implemented; Windows CI and independent review pending.**

Requirements: CRD-08, FR-PRV-01-04, AC-V3-11. This candidate covers health prerequisites only; setup, provider preference, UI and automation remain outside these changes.

## Files and contracts

- `src/PCSteward.Application/ProviderHealth.cs`: exact requested enums, `ProviderHealthReport` and `IProviderHealthProbe`. Report properties contain no executable path, arguments, operation approval, consent or reachability authority.
- `src/PCSteward.Provider.WinGet/WingetProviderHealthProbe.cs`: public probe, internal injectable client and clock, native version/source reads and bounded display parsing.
- `src/PCSteward.Provider.Chocolatey/ChocolateyProviderHealthProbe.cs`: public probe, internal injectable client and clock, explicit strict discovery/revalidation, pinned version read and bounded local XML source read.
- `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs`: internal typed discovery/error-aware observation and narrowly pinned read-only health process seam; existing default trust/process behavior preserved.
- `src/PCSteward.Provider.Chocolatey/ChocolateyProvider.cs`: equivalent observation/pinned-client seam, explicit strict unchanged-validation option and protected config check; existing default behavior preserved.
- `tests/PCSteward.Application.IntegrationTests/ProviderHealthTests.cs`: fake-only presence, failure, trust, unchanged-client, version, source and consent boundary fixtures.
- `tests/PCSteward.ArchitectureTests/ProviderHealthBoundaryTests.cs`: source guards for evidence-only contract, forbidden consent/execution/network dependencies and explicit strict pinned-client launch validation.
- `docs/STATUS.md`, `docs/requirements/TRACEABILITY.md`: current candidate/task/branch metadata and honest red/provisional implementation evidence, separate from historical Task 49 safety evidence.

## Expected-red checkpoint assertions (historical scaffold)

At the historical expected-red checkpoint, scaffolds returned Unknown/Unknown/Checking with the injected current timestamp and ran no clients. Native discovery did not inspect the OS then. The production candidate described below replaces those scaffolds.

- `AbsentRequiresPositiveFilesystemEvidence`: expected accessible enumeration count 1 (scaffold 0), followed by Absent and Current report assertions.
- `ObservedCanonicalClientIsPresent`: expected Present (scaffold Unknown).
- `ProbeFailureIsUnknownNotAbsent`: Unknown and zero fake calls hold; expected Unavailable fails against Checking.
- `UntrustedClientIsNeverRunForVersion`: zero fake calls hold; expected Present/Untrusted/Unavailable fails against Unknown/Unknown/Checking.
- `ChocolateyRequiresProtectedLayout`: strict discovery and zero fake calls hold; expected Present/Untrusted fails.
- `ChocolateyTrustFailureNeverRuns` (ACL/signature/reparse): zero fake calls hold; expected Untrusted fails.
- `ChocolateyRevalidatesStrictUnchangedClientBeforeVersion`: expected strict revalidation and discover/validate/version/sources ordering fails because no validation/version/source read occurs.
- `ChangedChocolateyClientNeverRuns`: zero fake calls hold; expected Untrusted/Unavailable fails.
- `ConsentDoesNotBecomeHealthOrReachability`: expected Current/Trusted/configured sources fails; report has no consent/reachability field.
- `FailedSourceReadIsPartialAndDoesNotEraseVersion`: expected Present/Trusted/Partial/version fails.
- `OversizedOrFailedVersionIsNeverCurrent`: null version holds; expected Partial fails against Checking.
- `InaccessibleCanonicalDirectoryIsUnknown`: expected to pass as a safety control in this checkpoint; production must actually attempt bounded enumeration and catch observation failures.

These are real behavioral assertions, not deliberate exceptions, ignored tests or compilation errors. Root supplied the successful Windows build and exact assertion-failure evidence below before production implementation started.

## Commands/results

- Repaired candidate `cbf2edc1bb769a5b8ccf6751e90f4ff286135d14`: [Windows CI 37995533489](https://github.com/DanDG923/pc-steward/actions/runs/37995533489), job `114040473719`. Build passed with 0 warnings/errors. Application 329 passed / 0 failed, Catalog 96, Domain 87, Security 72 passed; Architecture 36 passed / 1 failed / 0 skipped (37 total). Total 620 passed / 1 failed / 0 skipped (621 total). Failure: existing `WinGetResolverUsesThePackageCatalogInsteadOfEnumeratingProtectedWindowsApps` searched the whole file and compared the resolver's `RejectReparse(root);` to the earlier new health-discovery catalog read. Inspection confirms the unchanged `ResolveExecutable()` still rejects the canonical root before its own catalog read. Authorized directly caused repair: scope the existing guard's catalog/order/no-root-enumeration assertions to that resolver body, assert the resolver/body exist, retain publisher identity checking. No production edit is needed or made. Full-suite rerun and scoped independent re-review remain pending.
- Candidate `03adbd0325112ecbf7b6477d713bff94bb39cdcb`: [Windows CI 37994976065](https://github.com/DanDG923/pc-steward/actions/runs/37994976065), job `114038578802`. Restore passed; build failed only CA1859 at `WingetProviderHealthProbe.cs(74,42)`: private `SnapshotSources` return type `IReadOnlyList<string>` should be concrete `System.Collections.ObjectModel.ReadOnlyCollection<string>`. Tests were skipped; there is no candidate executable evidence. Minimal repair changes only the private return declaration to the exact concrete type; public `ProviderHealthReport` contract, `Array.AsReadOnly` snapshot and behavior are unchanged. Directly analogous helpers were inspected: internal source parsers and argument properties expose seam-facing interface contracts; private directory enumerators are passed as interface-returning delegates. CI reported no analogous analyzer errors, so those declarations remain unchanged. Root owns publication and repaired CI.
- **Proven expected-red:** [Windows CI run 37993415753](https://github.com/DanDG923/pc-steward/actions/runs/37993415753), job `114033167824`, checkpoint `2108285c9856fd543a88be8b0808329ae55963f3`: Release build passed with 0 warnings/errors. Architecture 36, Catalog 96, Domain 87, Security 72 passed. Application 305 passed / 13 failed / 0 skipped / 318 total. Every failure was a new `ProviderHealthTests` behavioral assertion mismatch; inaccessible-directory safety control passed. Total 596 passed / 13 failed / 0 skipped. Root supplied this evidence and explicitly released the implementation gate.
- Windows CI checkpoint `6e571b9154fe21d1cb7d37a4f353afa913e4d36f`: [run 37993036700](https://github.com/DanDG923/pc-steward/actions/runs/37993036700), job `114031856420`. Restore succeeded; build failed only `CA1861` at `ProviderHealthTests.cs:102` for the inline expected call-sequence array. Tests were skipped. This build failure is not expected-red behavioral evidence.
- Historical minimal correction, published at `2108285c9856fd543a88be8b0808329ae55963f3`: hoisted the same expected call sequence into `static readonly string[] ExpectedChocolateyProbeCalls`; assertion semantics and the then-inert scaffolds were unchanged. Root's publication and successful expected-red CI are recorded above; that gate is complete.
- `dotnet --list-sdks`: exit 0; no SDKs listed.
- `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-build --filter FullyQualifiedName~ProviderHealthTests`: exit 1, no compatible SDK; requested `10.0.302`, no installed SDK. No tests ran. This is a tooling blocker, not regression evidence.
- `git diff --check`: exit 0, no whitespace errors in tracked diff.
- Reviewed directly relevant source, AGENTS.md, current STATUS, approved design section 5, ownership row and brief. No whole-plan/history read.

## Self-review and limits

Changes stay within the ownership files, required status/traceability documentation and report. Existing default provider execution/trust behavior, SDK/dependency pins, settings and application/task state remain unchanged. New public probes are implemented candidates awaiting validation; their current behavior and remaining concerns are detailed below. Fake tests exercise the narrow internal seams; no real provider/client process, network access, UAC, install, app close, SDK installation, Git configuration, branch change, commit or push occurred. Network/elevation are absent from the health seam and guarded architecturally; fake launch/source counters assert zero for untrusted/failed evidence.

No commit: root owns connector commits/review and Windows CI. Task title API attempt returned `thread not found`; no app title was changed.

## Production candidate result

The scaffold implementations are replaced with canonical/error-aware native discovery and typed health states. Initial strict Chocolatey discovery and explicit unchanged validation precede version reads; the narrow pinned-client process constructor strictly revalidates the same captured identity again immediately before launch, with a 64 KiB output cap and cancellation check. Existing default process constructors retain their existing permissive layout/default execution behavior and 1,000,000-character cap. WinGet's health process seam pins the discovered canonical executable and re-runs the existing trust resolver before the fixed read-only launch. Both health reads time out at 15 seconds per command; sources are capped at 64 identifiers and 256 characters each, versions at 128 characters. Reports snapshot sources into a read-only collection.

WinGet's positively observed registered client can be trusted without broad WindowsApps root-list permission; absence requires accessible canonical directory enumeration. Package catalog and canonical directory enumeration are capped (4096 entries, 64 matching registrations). ACL/I/O/reparse failures never establish trusted evidence. Chocolatey sources are parsed from the fixed local `config/chocolatey.config` only after no-reparse and caller-cannot-modify configuration-layout checks; config file input is 64 KiB, DTD/entity resolution is prohibited, and malformed/duplicate source sections cause Partial instead of fabricated empty evidence. WinGet only runs fixed `source list --disable-interactivity`; there are no connect/update/reset/add/agreement operations.

Additional fake-only fixtures cover actual native discovery seams (strict policy request, parent observation, inaccessible directories, reparse/signature failure and registered WinGet without root-list access), oversized/malformed observation, exact fixed arguments, custom source names, external-entity/wrong-root XML, immutable source snapshots and cancellation after await. Architecture guards check the explicit strict pinned-client launch path. `docs/STATUS.md` and `docs/requirements/TRACEABILITY.md` record implementation-pending-validation honestly. The concurrent root-owned `docs/review/CURATED_MANUAL_PROGRESS.md` change is not this agent's edit.

Local validation after implementation:
- Architecture-guard repair: PowerShell read-only source-index check confirmed whole-file root/catalog offsets `40382/34236` (the false global ordering), while scoped resolver offsets are `588/689` (canonical root check precedes catalog read). This is static source evidence, not a compiled test execution. `git diff --check` passed after the scoped repair. No source/product behavior changed.
- `git diff --check`: passed; only Git LF-to-CRLF notices, no whitespace errors.
- Application focused command from the brief: cannot run; no installed .NET SDK, requested `10.0.302`; no tests executed.
- `dotnet test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj -c Release --no-build --filter FullyQualifiedName~ProviderHealthBoundaryTests`: same SDK blocker; no tests executed. The combined shell's final Git status returned exit 0, but the dotnet operation itself failed; this is not a test pass.

Self-review: no execution authority in DTOs, no paths/arguments or raw native exceptions in reasons, no use of `File.Exists == false` to assert absence, strict Chocolatey initial and immediate launch validation, fixed read-only process flags, local bounded XML/source data, unchanged operation default branches. All new execution tests use injected fakes; no real clients, network, UAC, provider install, setup, automation, app close, task changes, SDK/dependency changes, commit or push occurred.

Remaining concerns: candidate build/analyzers and Application/Catalog/Domain/Security tests have passed in the repaired CI above; the narrowly repaired Architecture guard requires a full-suite rerun and scoped independent review before completion. Native timing/resource behavior and protected owner-installation integration are unmeasured. A trust snapshot is not an OS-level file/process lock against a privileged writer changing the machine afterward. Failed refresh retention and stale presentation are intentionally separate caller/UI composition concerns; probes report current Unknown/Partial failures with their own observation timestamp and never fabricate old evidence as fresh.

## Historical production approach prepared before the expected-red gate cleared

The following checklist was prepared while red-fix commit `2108285c9856fd543a88be8b0808329ae55963f3` was restoring in CI run `37993415753`. No production/test edits occurred during that preparation step. Root subsequently supplied the successful build and actual expected-red assertions above, authorized implementation, and the production candidate is now complete pending validation.

1. **Observation:** implement `ObserveClientPresence` as bounded canonical-directory enumeration with exact case-insensitive client filename matching. A complete accessible enumeration with no exact client is Absent; access-denied, I/O failure, enumeration truncation or malformed evidence is Unknown. Native callers obtain canonical roots internally and check all relevant path components for reparses before enumerating; no path comes from reports, settings or environment override variables. Presence and trust remain independent.
2. **WinGet discovery:** use the existing current-user App Installer package catalog identity filter and fixed `ProgramFiles/WindowsApps` containment. A positively observed registered client can establish Present without requiring broad WindowsApps directory-list rights. If no eligible registered client is observed, absence requires complete, accessible enumeration of the canonical root and relevant App Installer directories; a failed catalog/root/directory observation is Unknown. Existing canonical-root/signature/signer/no-reparse resolver supplies trust only after positive presence. Existing resolver defaults and update callers stay unchanged.
3. **Chocolatey discovery:** observe the fixed `CommonApplicationData/chocolatey/bin/choco.exe` through exact-component enumeration, checking ancestor reparses before descending. Missing components establish Absent only from a successful accessible parent enumeration. A discovered file then goes through `ResolveExecutable(requireCallerCannotModifyLayout: true)`. Strict ACL/signature/signer/reparse rejection yields no version/source execution and never Trusted. Health never uses `ChocolateyInstall` environment overrides or PATH.
4. **Strict unchanged client:** extend the internal `ValidateUnchanged` seam with an optional explicit strict-layout parameter while preserving its existing default. Health passes true. Expose a narrow internal pinned-client native process seam so the captured identity is strictly revalidated immediately before its exact version launch; health must not call the generic process path that reacquires a permissively trusted different identity. WinGet health similarly pins the discovered trusted canonical executable and rechecks the existing resolver immediately before read-only native launches. Cancellation is checked before each read/launch.
5. **Read-only reads:** allow only fixed version arguments (`--version` for WinGet; `--version --no-color` for Chocolatey), with existing bounded process capture and timeout handling. WinGet configured-source enumeration uses only fixed `source list --disable-interactivity` arguments; parse names from a complete bounded table without connecting/updating/accepting agreements. Chocolatey reads only the fixed adjacent `config/chocolatey.config` locally with no-reparse validation, bounded encoded/XML input, prohibited DTD/entity resolution and bounded source IDs. Source names are display evidence; no configured URL becomes execution authority. No source-add/update/reset/test/connect operation is called.
6. **Report:** Unknown observation -> Unavailable; positive absence -> Current/Unknown trust; positive untrusted -> Present/Untrusted/Unavailable; trusted complete version/sources -> Current; trusted incomplete version/source read -> Partial retaining the successful fields. Exceptions never expose raw paths in the report reason. Cancel propagates. Bound source data to 64 identifiers of at most 256 non-control characters, bounded local source input/output to 64 KiB, version fields to 128 characters; copy accepted sources into a read-only snapshot. Failed presentation refresh retains the prior report and its timestamp through a separate presentation qualifier, not a fabricated Current report.

Historical checklist used during implementation: inaccessible WindowsApps root must not prevent trusted positively observed registered WinGet; neither `File.Exists == false` nor a swallowed resolver exception proves absence; strict Chocolatey policy must be used in initial discovery and the final pinned-client launch seam; XML and table failure cannot be silently interpreted as empty configured sources; configured source reads must not imply remote reachability/consent. Native process behavior was inspected statically only; all execution tests use injected fakes.

## Controller-collected repair validation evidence

Repaired candidate: b96a5485b7be9bcb888e12539e1d1c9d5f939ccf.
Windows CI: https://github.com/DanDG923/pc-steward/actions/runs/37996153500
Job: 114042651116. Controller fetched complete job logs using authenticated GitHub read-only tooling.

Commands: pwsh -NoProfile -File ./scripts/Restore.ps1; pwsh -NoProfile -File ./scripts/Build.ps1; pwsh -NoProfile -File ./scripts/Test.ps1.
Build script: dotnet build PCSteward.slnx --configuration Release --no-restore.
Test script: dotnet test PCSteward.slnx --configuration Release --no-build.
Restore succeeded. Release build succeeded, 0 warnings/errors.
Architecture 37 passed, Catalog 96, Domain 87, Security 72, Application 329; every suite 0 failed/0 skipped.
Total 621 passed/0 failed/0 skipped. Full suite includes ProviderHealthTests, ProviderHealthBoundaryTests and the repaired ReferenceBoundaryTests guard; no separate successful local/focused run is claimed.
This covers the concrete immutable return repair (I1) and the resolver-scoped source assertion. Production behavior has not changed since the candidate except that private return declaration; the source guard repair retains its canonical-root ordering, catalog, publisher and no-root-enumeration assertions.

Original independent review: 0 Critical/1 Important/1 Minor. I1 requires passing build/tests and scoped review; current CI has passed, scoped review is pending. M1 minor late trust-exception cancellation-reporting defect is retained in the controller ledger for final whole-change triage; it grants no launch/consent authority. Later UI must also retain last-known timestamp and check cancellation after awaiting probes. Native/client timing and owner-installation outcomes remain unmeasured.
