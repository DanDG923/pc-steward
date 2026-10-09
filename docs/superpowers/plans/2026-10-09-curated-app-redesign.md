# Curated app manual release implementation plan

> Work task by task: write a failing executable test, confirm the failure, make the smallest change, pass focused tests, obtain an independent code review, then commit. Review the complete change before owner acceptance.

**Goal:** Deliver the first complete manual curated-app experience: provider readiness, My Apps, Add Apps, an optional inspector, explicit batch review/progress, Activity, protected Settings, and recognizable real app icons.

**Architecture:** Keep the existing Domain/Application/provider/SQLite/execution/Storage boundaries and native WinUI 3 shell. Add a durable membership store and small application presentation/workflow components; connect them to existing exact preflight, immutable batches, one-helper execution and verification. Icons are bounded display data and never enter execution or privileged code.

**Tech stack:** C#, .NET 10, WinUI 3, Microsoft.Data.Sqlite, existing WinGet COM/client and fixed Chocolatey Community adapters, MSTest, PowerShell, existing Windows CI. No new package or framework is planned.

**Approved specification:** `docs/superpowers/specs/2026-10-09-curated-app-redesign-design.md`, commit `0761fc1cb23a23393e55748e0834903b0356a574`; owner approved it on 2026-10-09. Product base is safety merge `e6339762a0e72abcb782338df9ce1cd3295aac7f`. Approval covers the design; this plan still requires review and an execution choice before product changes.

## Global constraints

- Windows 11 x64, personal owner-only, local-first, no account/distribution. Preserve `global.json` SDK `10.0.302`, `rollForward: latestPatch`, and dependency pins.
- Select A's clean My Apps list + B's Add Apps cards/review tray + C's optional inspector in one native interface. My Apps is the default; All installed software is secondary.
- Membership, detected installation, temporary queue and automatic-update permission stay independent. Adding/removing management never installs/uninstalls. New managed entries default to automatic permission Off.
- Update all reviews only eligible managed installed upgrades. It cannot submit queued installs, unmanaged apps or a provider-wide upgrade-all command. New installs require explicit selection, live exact resolution, licensing/agreements and final confirmation; Cancel is default.
- Installed apps retain their managing provider. No automatic adoption, reinstall, provider switch, installer fallback, forced closure or reboot. A separately consented graceful close may precede a new review; the helper never closes apps.
- Preserve last-precheck, immediately-pre-UAC and after-await cancellation, strict/fresh process checks before each elevated item, digest/expiry/replay controls and post-operation verification. A snapshot is not an OS lock preventing subsequent app startup.
- Readiness and consent are distinct. Only trusted WinGet and the fixed Chocolatey Community feed are actionable. Setup is only a reviewed machine WinGet Install of `Chocolatey.Chocolatey`; never a PowerShell bootstrap. Missing WinGet offers the fixed Microsoft Store App Installer destination, not repair/install code.
- Preserve settings, source consent, decisions, mappings, exclusions, history, downloaded files, last good scans, reset/onboarding and window lifetime. Invalid catalog disables catalog actions while inventory remains usable. DB/display/search/icon data never supplies commands or executable paths.
- Real publisher icons are in this release: vetted bundled raster assets, then safely identified installed local resources, then deterministic fallback. No runtime remote icon discovery/fetch or arbitrary path/URL activation.
- Icon limits: 1 MiB encoded image/resource input, 512 x 512 decoded pixels, two concurrent extraction/decoding tasks, 16 MiB memory and 32 MiB disk LRU caches; logical 32px list / 64px cards with DPI variants. No idle polling.
- Keep 900 epx compact / 1100 epx expanded navigation thresholds and the 900px minimum layout; preserve light/dark/high contrast. Keyboard reachability, focus retention, readable status and explicit confirmation are required; the broader optional accessibility matrix remains best effort.
- Preserve resource targets: local inventory p95 <=15s, healthy online check p95 <=90s, cold interactive shell <=5s target; five-minute idle <=1% total CPU / <=300 MB working set with no recurring disk/network; local scan average <=20% CPU / peak <500 MB with no >50% CPU sustained >5s; cancellation acknowledged <=2s / operable <=5s; no unnecessary process after closing; scheduled host exits <=30s after completion/cancel/timeout; connector concurrency defaults to three. Measure rather than claim compliance.
- Initial release preserves scan-only scheduling and saved opt-ins, reports automatic execution Unavailable, and enables no new execution task. Stage A and Stage B need separate plans; no unattended initial installs.

## Review focus

Five likely failures receive explicit tests in the owning tasks:

1. A provider scan fails or returns partial evidence while an app looks missing: retain last good data and withhold Install (Tasks 1, 4, 6).
2. A removed entry reappears, or legacy opt-ins bind to a different product after migration: do not resurrect membership or automatic permission; retain raw legacy choices and expose unresolved associations (Tasks 3, 4).
3. An app/selection/agreement changes between review and confirmation, or membership persistence fails: no old consent reuse and no provider start (Tasks 6, 7).
4. A local icon source is replaced, oversized, malformed or a reparse escape: fallback only, bounded resource use, no execution or source deletion (Tasks 8, 9).
5. Long names, text scaling, rapid filtering/navigation or late progress updates lose focus or imply confirmation: preserve entry-key selection/focus and require the explicit confirm button (Tasks 5, 10, 11, 12).

## Delivery phases and dependencies

| Phase | Tasks | Independently reviewable outcome |
|---|---|---|
| Readiness and state | 1-4 | Trusted provider reports/setup preparation; compatible durable membership and truthful curated snapshots. |
| Manual operation contracts | 5-7 | Temporary queue; live review; atomic confirmed membership/batch persistence; protected execution and recovery through injected fixtures. |
| Real icon pipeline | 8-9 | Vetted recognizable assets plus bounded safe local extraction/cache. Depends on Task 4 identity association, never on helper code. |
| Native experience | 10-13 | My Apps/Add Apps/inspector, deliberate repository search, one review/progress flow, retained Settings/inventory/Activity. |
| Acceptance | 14-15 | Resource/accessibility evidence, protected regressions, whole-change review and owner review/live-use decision. |

Tasks 1 -> 2; 3 -> 4; 1 + 4 -> 5 -> 6 -> 7; 4 -> 8 -> 9; 5 + 8 -> 10; 1 + 4 + 10 -> 11; 2 + 7 + 9 + 10 + 11 -> 12; 3 + 12 -> 13; 9 + 12 + 13 -> 14 -> 15. Independent tasks may proceed only after their contracts pass review; no concurrent edits to shared composition/SQLite files. Do not ship partial actionable UI before Task 12 wiring and regression checks.

## Existing files and proposed ownership

All paths below are repository-relative. **Existing** files are grounded in the inspected base; **new** files are proposed, not implemented APIs. Task file lists are the intended change boundaries. Reopen a contract in review if implementation reveals a mismatch; do not silently invent a neighboring method.

| Area | Existing reuse / modification | New files and responsibility |
|---|---|---|
| Provider readiness | `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs` (`MachineTrustedWinget`); `src/PCSteward.Provider.Chocolatey/ChocolateyProvider.cs` (`MachineTrustedChocolatey`) | `src/PCSteward.Application/ProviderHealth.cs`; `src/PCSteward.Provider.WinGet/WingetProviderHealthProbe.cs`; `src/PCSteward.Provider.Chocolatey/ChocolateyProviderHealthProbe.cs`: typed read-only reports, fixed trusted discovery. |
| Setup / install choices | `catalog/pcsteward-catalog.v1.json`, `src/PCSteward.Application/InventoryWorkflow.cs`, `src/PCSteward.Application/InstallPreflight.cs`, `src/PCSteward.Provider.WinGet/WingetInstallOperations.cs` | `src/PCSteward.Application/ChocolateySetupPreparation.cs`; `src/PCSteward.Provider.WinGet/WingetInstallChoices.cs`; `src/PCSteward.Provider.Chocolatey/ChocolateyInstallChoices.cs`: exact setup and supported live choices, no host-architecture guessing. |
| Membership / association | `src/PCSteward.Application/PersonalCatalogMatching.cs`, `src/PCSteward.Application/UserDecisionService.cs`, `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs` | `src/PCSteward.Application/CuratedMembership.cs`, `CuratedAppModels.cs`, `CuratedWorkspaceService.cs` in that Application directory: stable membership, restrictive decision adapter, immutable read models. |
| Queue / review / run | `src/PCSteward.Domain/OperationBatches.cs`, `src/PCSteward.Application/UpdateJobWorkflow.cs`, `BatchExecution.cs`, `UpdateBatchWorkflow.cs`, `src/PCSteward.Execution/BatchOperationExecutors.cs`, `ElevatedHelperBatchSession.cs`, `src/PCSteward.Storage/FactoryReset.cs` (`DataOperationLease`) | Application: `CuratedReviewQueue.cs`, `ManualBatchReviewService.cs`, `ManualBatchSession.cs`, `CuratedManualController.cs`, `UpgradeReviewPreflight.cs`; WinGet: `WingetUpgradeReviewPreflight.cs`, `WingetReviewedUpgradeExecutor.cs`; Chocolatey: `ChocolateyUpgradeReviewPreflight.cs` in their existing provider directories. |
| Icons | `src/PCSteward.Storage/DownloadedArtifactCache.cs` (`PathSafety`), `FactoryReset.cs`; `src/PCSteward.Inventory.Windows/PCSteward.Inventory.Windows.csproj` | `src/PCSteward.Application/AppIcons.cs`, `AppIconResolutionService.cs`; `src/PCSteward.Inventory.Windows/TrustedLocalAppIconSource.cs`, `WindowsRasterIconDecoder.cs`; `src/PCSteward.Storage/AppIconCache.cs`; `src/PCSteward.Desktop/AppIconResolver.cs`; `design/assets/apps/icons.manifest.v1.json`, `design/assets/apps/png/`, `design/assets/apps/README.md`. |
| Native presentation | `src/PCSteward.Desktop/MainWindow.cs`, `App.xaml.cs`, `StewardPresentation.cs`, `PCSteward.Desktop.csproj`, `Resources/DataTemplates.xaml`, `ControlStyles.xaml`, `ThemeResources.xaml`; `src/PCSteward.Application/InventoryExperienceViewModel.cs` | Application: `CuratedExperienceViewModel.cs`, `RepositoryCatalogSearch.cs`. Desktop: `CuratedDesktopComposition.cs`, `CuratedShellController.cs`, `Pages/MyAppsPage.cs`, `AddAppsPage.cs`, `AllInstalledPage.cs`, `ActivityPage.cs`, `SettingsPage.cs`, `Controls/AppInspector.cs`, `ReviewTray.cs`, `BatchReviewDialog.cs`, `OperationStrip.cs`. Provider search adapters in each provider directory (Task 11). |
| Evidence | Existing `tests/PCSteward.Application.IntegrationTests`, `PCSteward.Catalog.UnitTests`, `PCSteward.Security.FixtureTests`, `PCSteward.ArchitectureTests`, `PCSteward.Performance/Program.cs`; `.github/workflows/ci.yml`, `scripts/Restore.ps1`, `Build.ps1`, `Test.ps1`, `Benchmark.ps1` | New named tests per task; `docs/review/CURATED_MANUAL_RELEASE.md`, `docs/review/raw/curated-manual-performance.json`. Update `docs/STATUS.md`, `docs/requirements/TRACEABILITY.md` with actual implementation results. |

## Shared contract decisions

Use existing enums at boundaries: `CatalogProviderKind`, `CatalogSource`, `CatalogAppState` from Catalog; `UpdateProvider`, `UpdateSource`, `UpdateScope`, `UpdateArchitecture`, `PackageOperationKind` from Domain. Convert in one validated adapter: WinGet/WinGet -> WinGet/WinGet; Chocolatey/ChocolateyCommunity -> Chocolatey/ChocolateyCommunity. Do not parse display strings into enums. Catalog IDs use `StringComparer.Ordinal`; provider package comparison uses each existing provider rule. Upgrade `StableProductKey` stays the inventory product key; Install `StableProductKey` stays the catalog entry ID. Never substitute one for the other in history or verification; read models keep the association explicitly.

Proposed Application records: `ManagedAppMembership` is defined in `CuratedMembership.cs` during Task 3; the remaining records are defined in `CuratedAppModels.cs` / `CuratedReviewQueue.cs` by Tasks 4-5:

```csharp
public sealed record ManagedAppMembership(string CatalogEntryId, DateTimeOffset AddedAt);
public sealed record InstallChoice(CatalogProviderKind Provider, UpdateScope Scope, UpdateArchitecture Architecture);
public sealed record CuratedAppSnapshot(
    CatalogEntry Entry, bool IsManaged, CatalogAppState State, string? StableProductKey,
    CatalogProviderIdentity? ActiveProvider, string? InstalledVersion, string? AvailableVersion,
    UpdateScope? Scope, UpdateArchitecture? Architecture, ProductUpdatePolicy Policy,
    bool StoredAutomaticOptIn, bool? AppearsToBeRunning,
    DateTimeOffset? LastCheckedAt, string Freshness, string Reason);
public sealed record CuratedWorkspaceSnapshot(
    IReadOnlyList<CuratedAppSnapshot> Apps, IReadOnlyList<string> RejectedEntryIds,
    DateTimeOffset? LastCompletedCheckAt, bool SourceConsentEnabled);
public sealed record QueuedCuratedOperation(string CatalogEntryId, PackageOperationKind Operation, InstallChoice? InstallChoice);
public enum ManualReviewKind { SelectedOperations, UpdateAll, ProviderSetup }
public sealed record CuratedActionIssue(string CatalogEntryId, string Reason);
public sealed record ManualReviewItem(Guid JobId, string CatalogEntryId, UpdateJobSelection Selection,
    IReadOnlyList<PackageAgreementText> Agreements, RunningApplicationDetection RunningApplication);
public sealed record ManualBatchPreparation(OperationBatchReview? Review, ManualReviewKind Kind,
    IReadOnlyList<ManualReviewItem> Items, IReadOnlyList<CuratedActionIssue> Withheld);
```

These records are display/query data. Only fresh `UpdateJobSelection` from existing authorities enters `OperationBatchReview`. Nullable scope/architecture/provider/running state means unknown, never a host-derived default. Running observations are timestamped display hints; Task 6 detects again during live preparation and Task 7 checks before execution. Bind `ManualReviewItem.JobId` to `OperationBatchReview.Items[].JobId`, preserving its unelevated-first ordering. Fixtures inject recording fakes, clocks/IDs and temporary stores; no real installer, UAC, task registration or app closer.

## Windows verification workflow

This host's `C:\Program Files\dotnet\dotnet.exe` reports no SDKs with `dotnet --list-sdks`; the required SDK is 10.0.302. Product tests were not run while writing this plan. Do not install software or change the pin to bypass that blocker. The existing `.github/workflows/ci.yml` uses `windows-latest`, `actions/setup-dotnet@v5` with `10.0.x`, and the canonical scripts below. The approved workflow remains the verification route once execution/push is separately authorized; this planning task dispatches or pushes nothing. No workflow-dispatch trigger currently exists. Use the existing PR-triggered CI and preserve the new tests' expected failing evidence before the fix, then passing evidence after it.

```powershell
dotnet --list-sdks
pwsh -NoProfile -File .\scripts\Restore.ps1
pwsh -NoProfile -File .\scripts\Build.ps1
pwsh -NoProfile -File .\scripts\Test.ps1
pwsh -NoProfile -File .\scripts\Benchmark.ps1 -OutputPath docs/review/raw/curated-manual-performance.json
```

Every focused command below assumes restored/built Release output. Use these complete commands with the named task filter (substitute only the filter string); the abbreviated suite references below mean these exact project paths:

```powershell
dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-build --filter 'FullyQualifiedName~NamedTests'
dotnet test tests/PCSteward.Security.FixtureTests/PCSteward.Security.FixtureTests.csproj -c Release --no-build --filter 'FullyQualifiedName~NamedTests'
dotnet test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj -c Release --no-build --filter 'FullyQualifiedName~NamedTests'
dotnet test tests/PCSteward.Catalog.UnitTests/PCSteward.Catalog.UnitTests.csproj -c Release --no-build --filter 'FullyQualifiedName~NamedTests'
```

Run canonical restore/build first when outputs are absent; rebuild after edits before `--no-build`. New types may first fail compilation; add minimal signatures and require an assertion failure for red behavioral evidence. Record exact commit/SDK/assertion/counts. An authorized tests-only checkpoint may provide red Windows CI evidence; final product commits require focused/full CI passing. Baseline CI passed all 593 tests before and after the safety merge; new totals must increase. The assertion snippets below specify fixture outcomes; their recording-fake variables belong to the named test files, not undeclared production APIs.

## Task 1: Typed provider health and strict read-only probes

**Depends on:** existing trusted provider adapters. **Requirements:** CRD-08; FR-PRV-01-04, AC-V3-11.

**Files:** Create the three provider-health files in the ownership table and `tests/PCSteward.Application.IntegrationTests/ProviderHealthTests.cs`; modify the two existing trusted-discovery files only to expose narrowly typed internal outcomes/seams. Add `ProviderHealthBoundaryTests.cs` under `tests/PCSteward.ArchitectureTests`.

**Interfaces:** In `ProviderHealth.cs`, define `ProviderPresence { Unknown, Absent, Present }`, `ProviderTrust { Unknown, Untrusted, Trusted }`, `ProviderProbeState { Checking, Current, Partial, Unavailable }`; `ProviderHealthReport(CatalogProviderKind Provider, ProviderPresence Presence, ProviderTrust Trust, ProviderProbeState State, string? Version, IReadOnlyList<string> ConfiguredSources, DateTimeOffset CheckedAt, string Reason)`; `IProviderHealthProbe.ProbeAsync(CancellationToken) -> Task<ProviderHealthReport>`. Reports contain no executable path/arguments and are not approval. A separate presentation stale qualifier derives from timestamps; failed refresh retains timestamped last-known report.

- [ ] Write `AbsentRequiresPositiveFilesystemEvidence`, `ProbeFailureIsUnknownNotAbsent`, `UntrustedClientIsNeverRunForVersion`, `ChocolateyRequiresProtectedLayout`, `ConsentDoesNotBecomeHealthOrReachability`. Assert absence only after accessible canonical-root enumeration, an ACL/signature/reparse failure cannot be Trusted, and fake process/network/elevation call counts are zero on untrusted/failed probes.
  ```csharp
  Assert.AreEqual(ProviderPresence.Unknown, failedProbe.Presence);
  Assert.AreEqual(0, fakeClient.LaunchCount);
  ```
- [ ] Run `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-build --filter FullyQualifiedName~ProviderHealthTests`; retain expected red assertions.
- [ ] Implement provider-owned probes using existing fixed discovery/signature/no-reparse checks. Chocolatey must explicitly request `requireCallerCannotModifyLayout: true`, including unchanged-client validation before a permitted version probe. Replace reliance on `File.Exists == false` as positive absence with error-aware directory/file observation. Configured source enumeration is bounded, non-mutating and never implies remote connectivity or consent; no untrusted version execution.
- [ ] Build and rerun the focused command, then Architecture tests filtered `FullyQualifiedName~ProviderHealthBoundaryTests`. Expected all pass; no installed app/task/provider state changed.
- [ ] Independent reviewer checks strict-vs-permissive resolver choice, absence/error semantics and read-only process arguments. Fix findings, pass full CI, commit `feat: report trusted provider readiness`.

## Task 2: Exact reviewed Chocolatey setup and live install choices

**Depends on:** 1; existing install preflight and batch engine. **Requirements:** CRD-03/05/08; FR-INS-02-07, FR-PRV-01-04.

**Files:** Create `src/PCSteward.Application/ChocolateySetupPreparation.cs`, both provider `*InstallChoices.cs` files and `tests/PCSteward.Application.IntegrationTests/ProviderSetupTests.cs`, `InstallChoiceTests.cs`; modify `InstallPreflight.cs`, `InventoryWorkflow.cs`, `catalog/pcsteward-catalog.v1.json`, existing `WingetInstallOperations.cs` only for shared query reuse, and `tests/PCSteward.Catalog.UnitTests/CatalogPatternsAndSeedTests.cs`. Extend `tests/PCSteward.ArchitectureTests/ProviderOperationBoundaryTests.cs` for no-bootstrap rules.

**Interfaces:** Add `IInstallChoiceAuthority.PreflightChoicesAsync(CatalogEntry entry, CatalogProviderIdentity provider, CancellationToken) -> Task<IReadOnlyList<InstallPreflightResult>>`; `CompositeInstallChoiceAuthority(IReadOnlyDictionary<CatalogProviderKind,IInstallChoiceAuthority>)` routes it. Append optional `IInstallChoiceAuthority? installChoices = null` to the existing `InventoryWorkflow` constructor. `PrepareInstallChoicesAsync(string catalogEntryId, CatalogProviderKind? providerOverride, CancellationToken) -> Task<IReadOnlyList<InstallPreflightResult>>` validates composed catalog, persisted consent and reliable local absence before discovery. Existing `PrepareInstallSelectionAsync(string, CatalogProviderKind?, UpdateScope, UpdateArchitecture, CancellationToken)` remains final choice/revalidation. `ChocolateySetupPreparation.PrepareAsync(CancellationToken) -> Task<InstallSelectionPreparation>` resolves fixed setup after fresh health gates.

- [ ] Write `SetupRequiresPositiveAbsenceConsentAndMachineCapability`, `SetupUsesExactWingetMachineX86Selection`, `ExistingOrUntrustedChocolateyCannotOfferReinstall`, `SetupVerificationRequiresStrictPostInstallTrust`, `ChoicesUseLiveApplicableInstallerNotHostArchitecture`, `PartialLocalInventoryCannotOfferInstall`. Assert fixed WinGet source/id, Machine/X86 wrapper, unchecked agreement review, zero bootstrap/installer/UAC calls during preparation, no scope/architecture weakening, and post-install trust failure remains Needs attention.
  ```csharp
  Assert.AreEqual("Chocolatey.Chocolatey", prepared.Selection!.PackageId);
  Assert.AreEqual(UpdateArchitecture.X86, prepared.Selection.Architecture);
  Assert.AreEqual(UpdateScope.Machine, prepared.Selection.Scope);
  ```
- [ ] Build and run Application tests with `--filter "FullyQualifiedName~ProviderSetupTests|FullyQualifiedName~InstallChoiceTests"`; capture red assertions.
- [ ] Add reserved bundled entry `pcs:chocolatey-setup`, WinGet/winget exact `Chocolatey.Chocolatey`, Machine only, Stable, automation disabled; exclude this entry from ordinary My Apps/Add Apps/membership paths. Validate it with the existing catalog validator; update starter-entry count tests without treating the seed list as owner-approved. Preserve it in the composed catalog so existing install verification can identify it. Request X86 for this reviewed wrapper; do not permit a general architecture exception. For ordinary apps discover supported X64/X86/Neutral choices on this x64 host via existing live COM filtering, deduplicate identical selections, bound discovery to two concurrent queries, and refuse unsupported/uncertain choices. Chocolatey offers only Machine/Neutral with strict trust. The fixed Microsoft Store App Installer URI is an allowlisted UI link, never a runtime installer URL.
- [ ] Pass focused tests plus existing `InstallWorkflowTests`, `InstallOperationTests`, `CatalogPatternsAndSeedTests`, `ProviderOperationBoundaryTests`. Task 12 will attach setup preparation to the common review dialog and batch flow; setup success requires ordinary verification followed by a fresh strict health report before any Chocolatey use. Consent remains unchanged.
- [ ] Independent reviewer checks wrapper architecture, reserved-entry exclusion, COM applicability and no-bootstrap boundaries. Pass full CI; commit `feat: prepare explicit provider setup and install choices`.

## Task 3: Additive membership persistence and restrictive removal

**Depends on:** existing SQLite v10 and stable-decision stores. **Requirements:** CRD-01/04/11; FR-CAT-04/06, FR-AUT-01, FR-RSC-01-05.

**Files:** Create `src/PCSteward.Application/CuratedMembership.cs`, `tests/PCSteward.Application.IntegrationTests/CuratedMembershipTests.cs`; modify `SqliteDatabase.cs`, `UserDecisionService.cs`, `tests/PCSteward.Application.IntegrationTests/SqliteDatabaseTests.cs`, `FactoryResetTests.cs` only for new-state compatibility assertions.

**Interfaces:** `ICuratedMembershipStore.GetManagedAppsAsync(CancellationToken) -> Task<IReadOnlyList<ManagedAppMembership>>`; `AddManagedAppAsync(string catalogEntryId, DateTimeOffset addedAt, CancellationToken) -> Task`; `RemoveManagedAppAndDisableAutomaticAsync(string catalogEntryId, IReadOnlyList<string> uniquelyAssociatedPolicyKeys, CancellationToken) -> Task`. `CuratedMembershipService.AddAsync(string, CancellationToken)` / `RemoveAsync(string, CancellationToken)` resolve/revalidate the catalog and association and reject entries in active work via an injected `Func<string,bool>` guard. Mutations run within the existing data-operation lease in desktop composition. Setup ID is not manageable.

- [ ] Write `MigrationIsAdditiveAndIdempotent`, `NoInventoryOrUserCatalogAutoMembership`, `NewMembershipDefaultsAutomaticOff`, `RemoveDoesNotUninstallOrDeleteHistory`, `RemovedEntryDoesNotReappearAfterScan`, `ClearScanPreservesMembership`, `ActiveBatchBlocksRemoval`. Seed DB v10 with consent/schedules/pins/skips/ignores/mappings/history/jobs/acceptances/user additions and raw legacy opt-ins; assert byte-equivalent logical retained records after two migrations and zero executor calls on membership operations.
  ```csharp
  CollectionAssert.AreEquivalent(before.HistoryIds, after.HistoryIds);
  Assert.AreEqual(0, fakeExecutor.CallCount);
  ```
- [ ] Build and run Application tests `--filter "FullyQualifiedName~CuratedMembershipTests|FullyQualifiedName~SqliteDatabaseTests"`; capture red assertions.
- [ ] Add transactional schema version 11: `managed_apps(catalog_entry_id TEXT PRIMARY KEY, added_at TEXT NOT NULL)`, no FK to disposable scan/product rows. Start an empty membership set; never infer membership from catalog inclusion/detection. Validate IDs against freshly composed catalog on every read/write; invalid/orphaned rows are visible as withheld entries, not actionable. Use an existing `stable_decisions` record kind `CuratedAutomaticUpdatesOff`, value `Off`, keyed to catalog-entry ID as a restrictive override for newly added/removed entries, rather than copying/enabling old `AutomaticUpdates=On` rows. Preserve legacy records; Task 4 interprets them as stored/unavailable. Removal transaction deletes membership and records the Off restriction; reverses active On decisions only for the exact catalog key/uniquely associated current product keys, leaving pins, skips, other products and operation history intact. Cancellation rolls back both changes. Stage B policy mutation is not implemented here.
- [ ] Pass tests; prove Clear scan leaves membership/restrictions intact, Factory reset removes them through current guarded state-root deletion/fresh-store verification, and advanced Mappings and policies clear explicitly includes membership/restrictions in its disclosed scope. Update fresh-reset verification counts accordingly, without widening downloaded-file cleanup.
- [ ] Independent reviewer checks migration rollback/idempotence, orphan/tamper rejection, exact-key association and removal restrictions. Pass full CI; commit `feat: persist independent curated membership`.

## Task 4: Curated snapshots and compatible decision association

**Depends on:** 3; uses 1 reports. **Requirements:** CRD-01/04/11; FR-CAT-01-06, FR-PRV-03/04, FR-POL-06.

**Files:** Create `src/PCSteward.Application/CuratedAppModels.cs`, `CuratedWorkspaceService.cs`, `tests/PCSteward.Application.IntegrationTests/CuratedWorkspaceTests.cs`; modify `PersonalCatalogMatching.cs` only for association/provenance. Add `InventoryWorkflow.GetCuratedWorkspaceAsync(ICuratedMembershipStore memberships, IReadOnlyDictionary<CatalogProviderKind,ProviderHealthReport> health, CancellationToken) -> Task<CuratedWorkspaceSnapshot>` using its private composed-catalog/snapshot/decision machinery; do not expose a mutable catalog.

**Interfaces:** `CuratedWorkspaceService(InventoryWorkflow inventory, ICuratedMembershipStore memberships, Func<IReadOnlyDictionary<CatalogProviderKind,ProviderHealthReport>> healthSnapshot)`; `ReadAsync(CancellationToken) -> Task<CuratedWorkspaceSnapshot>` delegates to that query, with no scan/check/start. Existing matching/policy/source evidence plus membership/health compose the snapshot. Unique exact association yields one product key/managing provider; ambiguity yields null/Needs review.

- [ ] Write `FourStatesVaryIndependently`, `CatalogNotInstalledIsNotReliableAbsence`, `ManagingProviderSurvivesConflictingPreference`, `LegacyRestrictiveChoicesFollowOnlyUniqueExactIdentity`, `UnknownOrDuplicateAssociationInheritsNoAutomaticPermission`, `StoredOptInNeverDisplaysEnabledWithoutHost`, `InvalidCatalogKeepsInventoryRouteUsable`. Assert detection alone changes no membership; partial/unavailable/cached evidence cannot produce an actionable Missing row; conflicting products/providers remain withheld with both evidence choices shown.
  ```csharp
  Assert.IsNull(ambiguous.StableProductKey);
  Assert.IsFalse(newManaged.Policy.AutomaticUpdates);
  Assert.IsTrue(legacy.StoredAutomaticOptIn);
  ```
- [ ] Build and run Application tests `--filter FullyQualifiedName~CuratedWorkspaceTests`; retain red assertions.
- [ ] Compose validated bundled/user catalogs and exact catalog-to-product associations; preserve installed provider before considering a supported preference. Merge restrictive pin/ignore/skip decisions from the entry and uniquely associated product without copying rows, choosing the stricter result on conflict. Preserve raw legacy automatic/close choices in details/history; initial effective curated automatic permission is Off (new entry restriction) or Unavailable (stored opt-in), never Enabled. Do not enable a host or migrate scan schedules. Expose original exact product keys for upgrade preparation/history; missing Install uses entry ID. Preserve unresolved choices in All installed/details and require explicit resolution rather than guessed identity.
- [ ] Pass focused and existing `PersonalCatalogWorkflowTests`, `UserDecisionServiceTests`, `CatalogDetectorAndCompositionTests`, `InventoryWorkflowUpdateSourceTests`. Cover catalog IDs removed/changed between reads and mixed user/machine installations.
- [ ] Independent reviewer checks no catalog-default override of managing provider, no cached absence authority, no implicit legacy permission activation and no lost decisions. Pass full CI; commit `feat: project truthful curated app state`.

## Task 5: Temporary queue and updates-only selection

**Depends on:** 1, 4. **Requirements:** CRD-01/02/03/10.

**Files:** Create `src/PCSteward.Application/CuratedReviewQueue.cs`, `CuratedExperienceViewModel.cs`, `tests/PCSteward.Application.IntegrationTests/CuratedQueueTests.cs`, `CuratedExperienceViewModelTests.cs`.

**Interfaces:** `CuratedReviewQueue.Set(QueuedCuratedOperation)` / `Remove(string catalogEntryId)` / `Clear()`; `Snapshot() -> IReadOnlyList<QueuedCuratedOperation>`. Queue is memory-only and carries entry IDs/choices, no approved jobs. `CuratedExperienceViewModel.Load(CuratedWorkspaceSnapshot)`; `SetFilter(string search, CatalogAppState? state)`; `VisibleManagedApps` and `LibraryApps` are read-only lists; `GetUpdateAllRequests() -> IReadOnlyList<QueuedCuratedOperation>` creates a separate list of Upgrade requests and never reads the tray. Estimated count is labelled pending live review.

- [ ] Write `AddManageAndSelectionStartNothing`, `UpdateAllIgnoresQueuedInstalls`, `UpdateAllExcludesMissingCurrentUnmanagedRunningPinnedSkippedIgnoredStaleUnavailableConflicting`, `DuplicateIdentityCannotCreateTwoQueueItems`, `FilterAndRefreshRetainSelectionByEntryId`, `RemovingMembershipPurgesQueue`. Test exclusion using projected provider identity/product key, not just row labels; running detection remains mandatory again in live preparation.
  ```csharp
  Assert.IsTrue(updateAll.All(item => item.Operation == PackageOperationKind.Upgrade));
  CollectionAssert.AreEqual(trayBefore, queue.Snapshot().ToArray());
  ```
- [ ] Build and run Application tests `--filter "FullyQualifiedName~CuratedQueueTests|FullyQualifiedName~CuratedExperienceViewModelTests"`; retain red assertions.
- [ ] Implement stable-key diff updates/filtering, explicit queue operations and separate Update all projection. `InstallChoice` is required before final install preparation; Upgrade must not carry an Install choice. Unknown/Needs review rows offer Resolve, not an enabled checkbox. Queue duplicates/conflicting identities are explained; app removal drops requests; navigation/Enter/addition do not prepare/approve/execute work.
- [ ] Pass focused tests, including rapid refresh/filter with one removed/orphaned row; create no background timer, DB queue persistence or provider call.
- [ ] Independent reviewer checks selection-vs-approval separation and Update all tray isolation. Pass full CI; commit `feat: add explicit curated review queue`.

## Task 6: Live manual review and atomic confirmation persistence

**Depends on:** 2, 3, 4, 5. **Requirements:** CRD-02/03/04/05; FR-INS-02/03, FR-BAT-01-04.

**Files:** Create `src/PCSteward.Application/ManualBatchReviewService.cs`, `UpgradeReviewPreflight.cs`, `src/PCSteward.Provider.WinGet/WingetUpgradeReviewPreflight.cs`, `src/PCSteward.Provider.Chocolatey/ChocolateyUpgradeReviewPreflight.cs`, `tests/PCSteward.Application.IntegrationTests/CuratedBatchReviewTests.cs`, `CuratedBatchApprovalTests.cs`; modify `InventoryWorkflow.cs`, `CuratedMembership.cs`, `UpdateJobWorkflow.cs`, `SqliteDatabase.cs` for exact disclosure/approval. Preserve Domain batch digest rules; share provider query machinery without treating display metadata as authority.

**Interfaces:** `ManualBatchReviewService.PrepareAsync(IReadOnlyList<QueuedCuratedOperation>, ManualReviewKind, CancellationToken) -> Task<ManualBatchPreparation>`; `ConfirmAsync(OperationBatchReview, IReadOnlySet<Guid> explicitlyAcceptedAgreementJobIds, DateTimeOffset approvedAt, CancellationToken) -> Task<ApprovedOperationBatch>`. Retain an immutable entry/job/selection mapping per review; reject a discarded/replaced preparation. `ICuratedBatchApprovalStore.ConfirmCuratedBatchAsync(OperationBatchReview, DateTimeOffset, IReadOnlyList<string> confirmedInstallEntryIds, CancellationToken) -> Task<ApprovedOperationBatch>` checks exact acceptances and atomically persists batch/jobs/membership/Off markers. Add `OperationBatchApprovalService.ConfirmCuratedAsync(OperationBatchReview review, DateTimeOffset approvedAt, IReadOnlyList<string> confirmedInstallEntryIds, CancellationToken) -> Task<ApprovedOperationBatch>` via that port, preserving old callers. Setup membership IDs are empty.

**Upgrade disclosure contract:** Existing update preparation has no agreement text and execution is plain-upgrade-only. In `UpgradeReviewPreflight.cs`: `UpgradeReviewPreflightResult(bool IsActionable, UpdateJobSelection? Selection, IReadOnlyList<PackageAgreementText> Agreements, string Reason)`; `IUpgradeReviewPreflightAuthority.PreflightAsync(UpdateJobSelection candidate, CancellationToken) -> Task<UpgradeReviewPreflightResult>`; `CompositeUpgradeReviewPreflightAuthority(IReadOnlyDictionary<UpdateProvider,IUpgradeReviewPreflightAuthority>)` routes fixed provider/source pairs. Append optional `IUpgradeReviewPreflightAuthority? upgradeReviewPreflight = null` injection to `InventoryWorkflow`; curated composition requires it. Extend `UpdateSelectionPreparation` with optional `IReadOnlyList<PackageAgreementText>? Agreements = null` for legacy compatibility; curated review rejects null. Live preflight validates installed exact package, target/source/scope/architecture/channel and agreements, computing WinGet hash before review. Chocolatey explicitly reports no provider-required agreement set plus fixed Community disclosure. Unknown metadata/unsupported elevation route is withheld. Task 7 repeats this authority before execution.

- [ ] Write `UpdateAllIsLiveRecheckedAndCannotGainInstalls`, `MixedSelectionUsesExactAuthorityAndOneReview`, `CancelAndUncheckedAgreementsApproveNothing`, `ChangedAgreementOrChoiceInvalidatesAcceptance`, `UpgradeRequiredAgreementsAreLiveAndDigestBound`, `RemovedMembershipOrConflictingProviderWithholdsApproval`, `MembershipFailureRollsBackApprovedBatch`, `SetupConfirmationAddsNoManagedApp`, `DuplicatePackageOrProductRefused`. An Install adds membership only at successful final confirmation; a later failed provider leaves the entry visible. Inject agreement/version changes, store failures and cancellation after awaited checks.
  ```csharp
  Assert.AreEqual(0, cancelledStore.ApprovedBatchCount);
  Assert.AreEqual(0, cancelledStore.NewMembershipCount);
  Assert.IsFalse(changedSelectionApproved);
  ```
- [ ] Build and run Application tests `--filter "FullyQualifiedName~CuratedBatchReviewTests|FullyQualifiedName~CuratedBatchApprovalTests"`; capture assertion failures.
- [ ] Resolve each Upgrade via existing `PrepareApprovedUpdateSelectionAsync(stableProductKey, token)`, each Install via `PrepareInstallSelectionAsync(entryId, provider, scope, architecture, token)`; current inventory/source/provider failures and incomplete process snapshots withhold the item. Keep no automatic fallback; show withheld reasons and require a new displayed review of the surviving exact set. For UpdateAll reject any Install request regardless of caller; for ProviderSetup accept only the fixed setup selection. Check duplicates again across source/provider/package and stable product; bind exact job/entry/selection/agreements in server-side memory, never trust dialog fields. Display required agreement text unchecked; confirmation records only explicit exact acceptance then atomically persists the batch and install memberships. Recheck cancellation and current membership/provider/consent gates after awaits and before persistence; execution will revalidate the immutable selection again. Do not adapt changed target/version/scope under old consent.
- [ ] Pass focused and existing `OperationBatchPersistenceTests`, `InstallWorkflowTests`, `InstallOperationTests`. Verify SQL rollback for every injected write failure and no executor reference/call from preparation or confirmation. Retry is a newly prepared review.
- [ ] Independent reviewer checks exact acceptance/hash binding, review substitution/races, transactional membership and trust provenance. Pass full CI; commit `feat: prepare and persist exact manual batches`.

## Task 7: Batch session, progress, cancellation and recovery

**Depends on:** 6. **Requirements:** CRD-05/06/07; FR-BAT-04-08, FR-INS-05/07, AC-V3-05-07.

**Files:** Create `src/PCSteward.Application/ManualBatchSession.cs`, `src/PCSteward.Provider.WinGet/WingetReviewedUpgradeExecutor.cs`, `tests/PCSteward.Application.IntegrationTests/ManualBatchSessionTests.cs`; modify `InventoryWorkflow.cs`, `src/PCSteward.Execution/BatchOperationExecutors.cs` (desktop/helper executor and helper revalidator), extend `BatchExecutionCoordinatorTests.cs`, `tests/PCSteward.Security.FixtureTests/BatchExecutorSecurityTests.cs`, `ElevatedBatchStartSafetyTests.cs`, `ElevatedHelperSecurityTests.cs`, `HelperProtocolV2SecurityTests.cs`. Use existing injected fixtures; a test project reference to Execution is allowed only if needed for actual adapter behavior.

**Interfaces:** `ManualBatchSession.RunAsync(ApprovedOperationBatch, BatchRunOptions, IProgress<BatchItemProgress>?, CancellationToken cancelRemaining, CancellationToken abort) -> Task<BatchRunResult>` delegates to the existing `BatchExecutionCoordinator.RunAsync` signature. It exposes `IsActive` / `ActiveCatalogEntryIds` from the review's retained entry/job association and clears state on every finish/failure. `RecoverAsync(CancellationToken) -> Task` calls existing `IUpdateHistoryStore.RecoverInterruptedAsync`. It holds no WinUI object/OS mutex; Task 12 owns one surrounding `DataOperationLease.RunAsync` for a run, including controlled verification scans, without nested leases.

- [ ] Write `SingleActiveSessionAndOneFakeUac`, `CloseConsentNeverEnablesForce`, `CancelDuringLastPrecheckHistoryTrustOrPreflightStartsNothing`, `FreshRunningCheckBeforeEveryElevatedItem`, `IncompleteSnapshotSkips`, `ExitZeroWithoutVerificationIsNotVerified`, `AbortAndCrashRecoverToVerificationRequired`, `RetryMustPrepareNewReview`. Reuse merged regressions for last/only elevated precheck, pre-UAC cancellation, after awaited helper revalidation, app opening/reopening during UAC or previous item and truncated snapshots.
  ```csharp
  Assert.AreEqual(0, cancelledLauncher.LaunchCount);
  Assert.AreEqual(1, mixedBatchLauncher.LaunchCount);
  Assert.AreEqual(1, result.Summary.NotVerified.Count);
  ```
- [ ] Build and run Application tests `--filter "FullyQualifiedName~ManualBatchSessionTests|FullyQualifiedName~BatchExecutionCoordinatorTests|FullyQualifiedName~StrictRunningApplicationDetectorTests"`; then Security tests with `--filter "FullyQualifiedName~ElevatedBatchStartSafetyTests|FullyQualifiedName~ElevatedHelperSecurityTests|FullyQualifiedName~HelperProtocolV2SecurityTests"`. Record red for newly added adapter cases; existing safety cases must stay green.
- [ ] Adapt the existing coordinator, `DesktopOperationExecutor`, `ElevatedBatchExecutor`, strict `PackageDerivedRunningApplicationDetector(failClosed: true)`, history and `InventoryWorkflow` revalidator/verifier. UI composition cannot set `ForceCloseForThisRun`; any separately consented graceful close has the existing 30-second bound and requires a new review after closing. Ordinary reviewed runs default to skip running apps; per-run options and mapped per-app close consent are explicit, never inferred from automation flags. Preserve existing v2 one-shot helper and fresh per-item checks; no icon/DB/UI authority enters it.
- Extend the same code step with `WingetReviewedUpgradeExecutor.ExecuteAsync(ImmutableUpdateJob, UpdateJobExecutionEvidence, CancellationToken) -> Task<ProviderExitAssessment>` for agreement-bearing upgrades. It accepts only usable/digest-valid Upgrade jobs matched to freshly revalidated exact agreement evidence; builds fixed exact id/version/source/scope/architecture argv in code, accepting agreements only for that matched recorded hash. Keep legacy plain-upgrade route compatible. Desktop and helper repeat Task 6 upgrade preflight even for a formerly empty agreement set, so newly required/changed terms skip rather than inherit approval. Add `ChangedUpgradeAgreementStartsNothing`, `ReviewedUpgradeArgumentsAreExact` and `LegacyPlainUpgradeStillVerifies` to the named fixture suites; no raw metadata URL/command or forced restart argument.
- [ ] Pass focused plus all Security and existing sequential/reviewed-update tests. Confirm cancel remaining does not kill a started provider; abort means verification required; reliable restart outcome is distinct; no forced reboot, automatic retry or replay. Startup recovery finishes before retry is offered.
- [ ] Independent reviewer checks every start boundary, options propagation, progress ordering and recovery. Pass full CI; commit `feat: connect curated manual batch sessions`.

## Task 8: Vetted bundled icons and display-only resolver contracts

**Depends on:** 4. **Requirements:** CRD-09; real-icon requirement, NFR-PERF.

**Files:** Create `src/PCSteward.Application/AppIcons.cs`, `AppIconResolutionService.cs`, `src/PCSteward.Desktop/AppIconResolver.cs`, `design/assets/apps/icons.manifest.v1.json`, `design/assets/apps/README.md`; assets follow internally derived `design/assets/apps/png/<catalog-entry-token>-32.png`, `-64.png`, `-128.png` names. Modify `src/PCSteward.Desktop/PCSteward.Desktop.csproj`; create `tests/PCSteward.Application.IntegrationTests/AppIconManifestTests.cs`, `tests/PCSteward.ArchitectureTests/AppIconBoundaryTests.cs`. Application owns testable resolution rules; Desktop bridges fixed bundled resources and WinUI images, not policy.

**Interfaces:** In `AppIcons.cs`: `AppIconKey(string CatalogEntryId, CatalogProviderKind Provider, CatalogSource Source, string PackageId, string SourceHash, string AssetVersion)`; `AppIconCandidate(AppIconKey Key, ReadOnlyMemory<byte> EncodedBytes, string Provenance)`; `DecodedAppIcon(ReadOnlyMemory<byte> PngBytes, int PixelWidth, int PixelHeight, long WorkingBytes)`; `AppIconResult(DecodedAppIcon? Image, string Provenance, string FallbackText)`. Both `IBundledAppIconSource` and `ILocalAppIconSource` expose `ReadAsync(CuratedAppSnapshot, CancellationToken) -> Task<AppIconCandidate?>`. `IRasterIconDecoder.DecodeAsync(ReadOnlyMemory<byte>, int logicalPixels, double rasterScale, CancellationToken) -> Task<DecodedAppIcon?>`; `IAppIconResolver.ResolveAsync(string catalogEntryId, int logicalPixels, double rasterScale, CancellationToken) -> Task<AppIconResult>`. Define Task 9 cache port now, with a no-cache fake until Task 9. `AppIconResolutionService : IAppIconResolver` uses those injected ports/workspace. `AppIconManifest.TryLoad(ReadOnlySpan<byte> json, PersonalCatalog catalog, out AppIconManifest? manifest) -> bool` validates bindings/resources. Logical size is 32/64, decode <=512 x 512; no WinUI/path/URL/handle in application results.

- [ ] Write `ReviewedIdentityGetsRecognizableBundledIcon`, `ManifestWrongIdentityOrHashGetsFallback`, `MissingAssetAndUserAddedEntryUseFallback`, `ManifestRejectsRuntimeUrlsPathsAndDuplicateBindings`, `PublisherProvenanceIsDisplayOnly`. Use at least recognizable Firefox and 7-Zip fixtures with owner-reviewed source/permission notes; verify actual pixels/source hashes, not only filenames. Keep synthetic hostile-image fixtures distinct from real publisher assets.
  ```csharp
  Assert.IsNotNull(reviewedIcon.Image);
  Assert.IsNull(wrongIdentityIcon.Image);
  Assert.AreEqual(0, fakeNetwork.RequestCount);
  ```
- [ ] Build and run Application tests `--filter FullyQualifiedName~AppIconManifestTests`; Architecture tests `--filter FullyQualifiedName~AppIconBoundaryTests`. Record expected red assertions.
- [ ] Review the current 20 starter catalog identities as a proposed owner list, not an implicit approved final list. Collect vetted publisher assets during development only; record exact catalog/provider/source/package binding, publisher, official source, review date, content SHA-256, asset version and permission/source-use notes in the manifest. Normalize to PNG/DPI variants at development time; no runtime vector/HTML/animation/network fetch. If use permission/provenance cannot be established, use safe local extraction/fallback and disclose that row. Keep manifest asset IDs mapped through fixed bundled resources; reject arbitrary paths/URLs. Do not extend execution catalog fields with image paths or commands.
- [ ] Pass focused tests, inspect representative app/card pixels in both themes and verify missing assets affect only icons. Confirm every proposed included curated app has either an identifiable vetted icon, safe local option or explicitly marked fallback; ask the owner only for the final app list/asset exceptions during the phase review.
- [ ] Independent reviewer checks identity binding, provenance, asset licensing notes and lack of execution/network capability. Pass full CI; commit `feat: add vetted curated application icons`.

## Task 9: Safe local resource extraction, bounded cache and reset integration

**Depends on:** 4, 8. **Requirements:** CRD-09/12; FR-RSC-01-05, NFR-PERF.

**Files:** Create `src/PCSteward.Inventory.Windows/TrustedLocalAppIconSource.cs`, `WindowsRasterIconDecoder.cs`, `src/PCSteward.Storage/AppIconCache.cs`, `tests/PCSteward.Application.IntegrationTests/LocalAppIconTests.cs`, `AppIconCacheTests.cs`; modify `AppIcons.cs`, `AppIconResolver.cs`, `src/PCSteward.Storage/FactoryReset.cs`, `DownloadedArtifactCache.cs` only to reuse/export the necessary internal path-safety primitive and fixed-root reset verification; extend `FactoryResetTests.cs`, `DownloadedArtifactCacheTests.cs` and `AppIconBoundaryTests.cs`.

**Interfaces:** `IAppIconCache.TryGetAsync(AppIconKey, CancellationToken) -> Task<DecodedAppIcon?>`; `PutAsync(AppIconKey, DecodedAppIcon, CancellationToken) -> Task`; `InvalidateEntryAsync(string catalogEntryId, CancellationToken) -> Task`; `ClearDerivedAsync(CancellationToken) -> Task`. `AppIconCache` implements these methods using a production fixed root `%LOCALAPPDATA%\PCSteward\icons`; test-only alternate root remains internal. Local source and decoder implement Task 8 ports. Resolver source order is vetted bundle -> trusted local -> fallback; a corrupt bundle also records a diagnostic and safely proceeds/falls back, never blocks app operations.

- [ ] Write `DisplayIconHintMustMatchVerifiedInstallIdentity`, `WrongSignerPublisherOrResourcePathFallsBack`, `RejectUncDeviceAdsReparseAndShortcutSources`, `ResourceReplacementInvalidatesIdentity`, `MalformedOrOversizeRasterFallsBack`, `LimitsAndLruEvictionAreExact`, `TwoDecodeTasksDeduplicateAndCancel`, `NegativeCacheEndsOnIdentityChangeOrExplicitRefresh`, `CacheTamperIsNotImageAuthority`, `CleanupNeverDeletesSourceResource`, `ResetRemovesIconsButDownloadClearDoesNot`. Use a harmless PE resource fixture and recording native-loader seam: only data-resource mapping flags; zero ordinary module load, entry-point or GetProcAddress calls.
  ```csharp
  Assert.IsTrue(cache.MemoryBytes <= 16L * 1024 * 1024);
  Assert.IsTrue(cache.DiskBytes <= 32L * 1024 * 1024);
  Assert.IsTrue(fakeDecoder.MaximumConcurrency <= 2);
  Assert.IsTrue(File.Exists(sourceFixturePath));
  ```
- [ ] Build and run Application tests `--filter "FullyQualifiedName~LocalAppIconTests|FullyQualifiedName~AppIconCacheTests|FullyQualifiedName~FactoryResetTests|FullyQualifiedName~DownloadedArtifactCacheTests"`; record red assertions.
- [ ] Resolve resources only from independently verified exact installed app/package identity, not a UI/DB `InstallPath` alone. Parse ARP `DisplayIcon` resource identifiers as data; require canonical permitted local path under that verified installation, no UNC/device/ADS/reparse, signature/publisher validation for PE sources and hash/identity recheck before reading. Use a non-executing Windows resource mapping (data/image-resource flags only; no normal DLL load, entry point, executable launch, shell extension or shortcut activation). Read at most 1 MiB of the encoded resource/image; the limit is not an instruction to load a whole executable into memory. Decode bounded single-frame raster with existing Windows APIs after dimensions validation; reject SVG/HTML/animation and decompression bombs. Release all mappings/streams/native resources even on cancellation.
- [ ] Implement lazy visible/near-visible requests, shared-request deduplication with independent consumer cancellation, exactly two extraction/decode workers, LRU byte accounting (decoded working memory counts, not compressed PNG size), hash-checked cache reads and atomic fixed-root writes. Pin in-use buffers until consumers release them without exceeding budgets; fallback under pressure. Use source identity/content hash/asset version keys, bounded negative cache invalidated on source change/explicit refresh, and no idle work. Remove unused derived icons after membership removal; Clear scan may invalidate mappings only. Extend reset's existing OwnedRoot stage to delete/verify only the fixed `icons` leaf using its non-following deletion primitive; do not change marker schema/category authority or delete bundled/source assets. Failed deletion is residual/partial reset, not success. Clear downloaded files stays installer-cache-only.
- [ ] Pass focused plus hostile path/reset/security architecture tests. Independently inspect native data-resource flags, handle lifetime, source replacement races, decoded memory accounting and reset ownership. Pass full CI; commit `feat: resolve and cache safe local app icons`.

## Task 10: Native My Apps/Add Apps/tray and optional inspector

**Depends on:** 5, 8; Task 9 enhances the same icon port. **Requirements:** CRD-01/02/03/04/09/10; selected A+B+optional C.

**Files:** Create `src/PCSteward.Desktop/Pages/MyAppsPage.cs`, `AddAppsPage.cs`, `Controls/ReviewTray.cs`, `AppInspector.cs`, `CuratedShellController.cs`; modify `MainWindow.cs`, `StewardPresentation.cs`, `Resources/DataTemplates.xaml`, `ControlStyles.xaml`, `ThemeResources.xaml`. Extend `tests/PCSteward.Application.IntegrationTests/CuratedExperienceViewModelTests.cs`; add `CuratedShellInteractionTests.cs` and `tests/PCSteward.ArchitectureTests/CuratedUiContractTests.cs`; update old `UiRedesignContractTests.cs` source-location/navigation assertions to the approved structure while retaining their behavioral invariants.

**Interfaces:** Each page/control takes the Task 5 view model/read-only snapshots and named callbacks; it calls no provider/process/store directly. `CuratedShellController` owns route state and commands; a testable `CuratedNavigationState` in `CuratedExperienceViewModel.cs` has `Navigate(CuratedRoute)` / `OpenInspector(string entryId)` / `CloseInspector()`, with `CuratedRoute { MyApps, AddApps, AllInstalled, Activity, Settings }`. Its `SelectedEntryId` and queue remain stable across navigation. Desktop callbacks later bind to Task 12 services; until then they use recording fakes and action controls cannot launch production operations.

- [ ] Write `StartupOpensMyAppsExceptRequiredOnboarding`, `CardsDistinguishManageInstallAndInstalledNotManaged`, `EnterOpensInspectorAndSpaceOnlyQueues`, `NavigationAndFilteringRetainEntryFocus`, `UnknownStateOffersResolveOnly`, `InspectDetailsExposeExactIdentityFreshnessPolicyHistory`. Assert no approval/executor calls for Enter/navigation/filter/card manage and no provider-source string is executed.
  ```csharp
  Assert.AreEqual(CuratedRoute.MyApps, navigation.Route);
  Assert.AreEqual(0, fakeApproval.ConfirmCount);
  Assert.AreEqual(selectedEntryId, navigation.SelectedEntryId);
  ```
- [ ] Build and run Application tests `--filter "FullyQualifiedName~CuratedShellInteractionTests|FullyQualifiedName~CuratedExperienceViewModelTests"` and Architecture tests `--filter "FullyQualifiedName~CuratedUiContractTests|FullyQualifiedName~UiRedesignContractTests"`; record red behavior/contract evidence.
- [ ] Use virtualized native rows/cards and retained page/control instances; diff row changes by entry ID rather than rebuilding the entire shell. My Apps shows icon, versions/provider/scope/admin/status, independent queue checkbox and truthful automatic state. Add Apps shows categories/identities and Manage vs explicit Install choices from Task 2. Show branded real icons through the port, neutral fallback explicitly. Keep All installed contextual and About contextual. Reserve one compact tray/global operation area. Inspector side pane on wide layout becomes a detail page/overlay on narrow layout; disclose unknown/conflicting evidence without forcing routine users into it.
- [ ] Verify the actual Windows UI at 900px minimum, 900/1100 epx transitions, light/dark/high contrast, long names, 100/150/200% DPI and Windows text scaling; collapse secondary columns, reflow cards and keep tray/Cancel/review actions reachable. Name buttons/checkboxes, preserve focus, use status text plus glyph/colour; decorative icons are excluded from duplicate spoken app names. Record screenshots/keyboard observations in Task 14 evidence; source checks alone are insufficient.
- [ ] Independent reviewer checks native resource reuse, row virtualization, retained focus/window and callback authority. Pass full CI; commit `feat: add curated native app pages and inspector`.

## Task 11: Deliberate source search and reviewed user catalog additions

**Depends on:** 1, 4, 10. **Requirements:** CRD-03/04/08/10; FR-CAT-06, FR-PRV-01-04.

**Files:** Create `src/PCSteward.Application/RepositoryCatalogSearch.cs`, `src/PCSteward.Provider.WinGet/WingetCatalogSearch.cs`, `src/PCSteward.Provider.Chocolatey/ChocolateyCatalogSearch.cs`, `tests/PCSteward.Application.IntegrationTests/CuratedCatalogSearchTests.cs`; modify `Pages/AddAppsPage.cs`, extend `PersonalCatalogWorkflowTests.cs`, `tests/PCSteward.ArchitectureTests/ProviderOperationBoundaryTests.cs`.

**Interfaces:** `CuratedSearchResult(CatalogProviderKind Provider, CatalogSource Source, string PackageId, string Name, string Publisher, bool IsExact, string Reason)`; `ICuratedCatalogSearch.SearchAsync(CatalogProviderKind, string query, CancellationToken) -> Task<IReadOnlyList<CuratedSearchResult>>`; application service guards trusted health and persisted source consent, delegates to fixed provider adapters, then projects distinct Not in my catalog results. `AddReviewedAsync(CuratedSearchResult, CancellationToken) -> Task<(bool Added, string Message)>` validates current exact identity/publisher/source and builds the existing `UserCatalogEntryRecord` for `InventoryWorkflow.AddUserCatalogEntryAsync`. It changes catalog only; Manage remains Task 3, Install remains Task 6.

- [ ] Write `TypingOrConsentToggleDoesNotSearch`, `SearchRequiresExplicitCommandAndConsent`, `FixedSourcesOnlyAndBoundedResults`, `AmbiguousMismatchOrUnavailableCannotAdd`, `TamperedSearchResultCannotChooseArguments`, `AddFromSearchDoesNotManageApproveOrInstall`. Snapshot argv for Chocolatey search and fixture structured COM results for WinGet; hostile query text must remain one validated data argument, never a shell string.
  ```csharp
  Assert.AreEqual(0, beforeSearch.ProviderQueryCount);
  Assert.AreEqual(0, afterAdd.ExecutorCallCount);
  Assert.AreEqual(0, afterAdd.NewMembershipCount);
  ```
- [ ] Build and run Application tests `--filter "FullyQualifiedName~CuratedCatalogSearchTests|FullyQualifiedName~PersonalCatalogWorkflowTests"`; retain red assertions.
- [ ] Implement a labelled repository Search action, not search-on-type. Validate trimmed query 1-128 characters and exclude control characters; max 50 results, 45-second deadline, cancellation and no query persistence containing raw private text. WinGet uses the predefined trusted winget catalog; Chocolatey uses the exact fixed Community source and structured argument lists through its trusted adapter, never bootstrap/alternate sources. Show source, exact ID/publisher and curated-vs-search origin before accepting. Requery/revalidate chosen metadata before addition; reject ambiguity, duplicate package aliases and source mismatches. Catalog validator still revalidates every DB read; remote metadata is only data. Search results get safe local icon/fallback, no remote favicon/image loading.
- [ ] Pass focused and provider boundary tests; verify search failure does not replace last good inventory/catalog or enable actions. Adding a result does not grant mappings/provider adoption, automatic permission or execute an operation.
- [ ] Independent reviewer checks consent, bounded query/results, structured args and catalog validation. Pass full CI; commit `feat: add deliberate reviewed catalog search`.

## Task 12: Desktop composition, shared review dialog and truthful progress

**Depends on:** 2, 7, 9, 10, 11. **Requirements:** CRD-02/03/05/06/07/08/10.

**Files:** Create `src/PCSteward.Application/CuratedManualController.cs`, `src/PCSteward.Desktop/CuratedDesktopComposition.cs`, `Controls/BatchReviewDialog.cs`, `OperationStrip.cs`, `tests/PCSteward.Application.IntegrationTests/CuratedDesktopWorkflowTests.cs`; modify `App.xaml.cs`, `MainWindow.cs`, `CuratedShellController.cs`, `AppIconResolver.cs`, `PCSteward.Desktop.csproj`; extend `CuratedUiContractTests.cs`, `ReferenceBoundaryTests.cs`, actual execution adapter fixtures.

**Interfaces:** `CuratedDesktopComposition.Create(SqliteDatabase database, InventoryExperienceViewModel inventoryViewModel, UpdateExecutionCapabilities capabilities, ScheduledCheckRegistrationService scheduling, bool showPostResetOnboarding, Func<FactoryResetResult>? acknowledgeResetOnboarding) -> CuratedShellController` composes migrated stores/probes/catalog/preflight/membership/review/session/icon ports. Controller `ManageAsync(string, CancellationToken)` / `RemoveManagementAsync(string, CancellationToken) -> Task`; `PrepareSelectedAsync(CancellationToken)` / `PrepareUpdateAllAsync(CancellationToken)` / `PrepareChocolateySetupAsync(CancellationToken) -> Task<ManualBatchPreparation>`; `ConfirmAndRunAsync(OperationBatchReview, IReadOnlySet<Guid> acceptedAgreementJobIds, CancellationToken) -> Task<BatchRunResult>`; `RequestCancelRemaining() -> void`. Separate abort token. Behavioral orchestration lives in Application `CuratedManualController.cs` with these commands/injected ports; Desktop `CuratedShellController` forwards them and owns routing/dialog presentation, so fixtures test production orchestration without referencing WinUI.

- [ ] Write `ProductionCompositionUsesCatalogInstallAndBatchPath`, `OnlyFinalConfirmCallsAtomicApprovalThenExecutor`, `UpdateAllLeavesInstallTrayUntouched`, `MixedUserMachineStartsOneFakeHelper`, `AgreementBoxesAndConfirmAreNeverDefault`, `ProviderSetupSharesReviewAndNeedsPostTrust`, `GlobalLeaseBlocksScanSettingsMembershipAndResetDuringRun`, `ProgressDoesNotRebuildOrStealFocus`, `StartupRecoversBeforeRetry`. Fake call order must be prepare -> display -> explicit accept/confirm -> persisted batch -> start -> verify -> result; Cancel has zero start calls.
  ```csharp
  CollectionAssert.AreEqual(expectedCallOrder, recordedCallOrder);
  Assert.AreEqual(1, elevatedFake.LaunchCount);
  Assert.IsFalse(confirmIsDefault);
  ```
- [ ] Build and run Application tests `--filter FullyQualifiedName~CuratedDesktopWorkflowTests`, then Architecture tests `--filter "FullyQualifiedName~CuratedUiContractTests|FullyQualifiedName~ReferenceBoundaryTests"`; record red adapter assertions.
- [ ] Replace upgrade-only desktop wiring: pass validated `catalog`, `userCatalog: database`, composite `installPreflight`, `installChoices`, `upgradeReviewPreflight` to `InventoryWorkflow`, preserving its existing stores/providers/verifier/capabilities. Compose `BatchExecutionCoordinator(workflow, new DesktopOperationExecutor(), capabilities.MachineScopeEnabled ? new ElevatedBatchExecutor() : null, workflow, database, strictRunningDetector, consentedCloser)`. Machine capability derives from existing approved protected deployment, never arbitrary debug output; do not deploy here. Workspace gets membership/health through Task 4's query; application controller owns explicit orchestration. Keep legacy engine tests while migrating callers; no unrelated refactor.
- [ ] Build one review sheet from Task 6's immutable retained preparation: exact old/target versions (Not installed for Install), source/id, scope/architecture, admin grouping, running/withheld reasons, required licensing/agreements and Chocolatey administrator-script disclosure. Cancel default, explicit confirmation button; Enter/navigation does not confirm. Final checkbox acceptance is job/hash/version bound. Hold one existing data lease across confirm/start/controlled verification; release on all paths. During active work disable conflicting commands with reasons while allowing navigation and cancel remaining. Operation strip/per-item progress uses queued/preflight/installing/verifying/result labels, no invented percent. Announce progress/errors without repeated focus movement. Show Succeeded, Restart required, Failed, Not verified, Skipped/deferred; Retry clears old approval and prepares a new review. No forced close/reboot controls or automatic retry.
- [ ] Pass focused, all merged start-boundary regressions, existing protected review/administrator/reset/window tests and full CI. Independent reviewer checks real composition (not merely existing engine classes), UAC count, lease nesting, confirmation input and progress ordering; commit `feat: connect explicit curated batch review and progress`.

## Task 13: Retained inventory, Activity, Settings and data controls

**Depends on:** 3, 12. **Requirements:** CRD-01/07/10/11; FR-RSC-01-05, protected UI behavior, FR-AUT-01.

**Files:** Create `src/PCSteward.Desktop/Pages/AllInstalledPage.cs`, `ActivityPage.cs`, `SettingsPage.cs`, `tests/PCSteward.Application.IntegrationTests/CuratedProtectedBehaviorTests.cs`; modify `MainWindow.cs`, `CuratedShellController.cs`, `InventoryExperienceViewModel.cs` only for retained route adapters; extend `InventoryExportServiceTests.cs`, `FactoryResetTests.cs`, `PostResetOnboardingFlowTests.cs`, `ScheduledChecksTests.cs`, `UiRedesignContractTests.cs`.

**Interfaces:** Retain existing `InventoryWorkflow.CreateExportAsync(bool, CancellationToken)`, `ClearScanDataAsync`, advanced clear methods; `DownloadedArtifactCache.ClearInactiveAsync`; `ScheduledCheckRegistrationService.ApplyAsync` and existing settings/status records; `FactoryReset` guarded operations. Inspector/Activity query original catalog-entry AND associated product histories as appropriate and keep old unmapped history accessible. Initial automatic presentation has no enabled execution toggle; stored opt-in is shown with capability Unavailable and explanation that scheduled checks only check/notify.

- [ ] Write `InventoryFiltersEvidenceAndExportsSurvive`, `SourceToggleDisclosesAndStartsNothing`, `ScanFailureKeepsLastGoodSnapshot`, `DataClearsKeepDistinctScopes`, `FactoryResetKeepsTwoConfirmationsResetTokenAndNextLaunchVerification`, `StoredScheduleAndOptInDoNotActivateExecutor`, `ActivityRetainsInstallAndProductKeyHistory`, `WindowLifetimeAndBusyRecoverySurvive`. Assert scan clear preserves membership/choices/history; download clear leaves icons/installed apps alone; advanced mappings clear has its explicit disclosed membership scope; factory reset removes membership/derived icons only through existing canonical safeguards.
  ```csharp
  Assert.AreEqual(before.ScanSchedule, after.ScanSchedule);
  Assert.AreEqual(0, sourceToggle.ExecutionCount);
  Assert.IsFalse(resetAcceptedWithLowercaseToken);
  ```
- [ ] Build and run Application tests `--filter "FullyQualifiedName~CuratedProtectedBehaviorTests|FullyQualifiedName~FactoryResetTests|FullyQualifiedName~PostResetOnboardingFlowTests|FullyQualifiedName~ScheduledChecksTests|FullyQualifiedName~InventoryExportServiceTests"`; retain red new adapter assertions.
- [ ] Move existing pages by responsibility without weakening protected functions. All installed retains filters/evidence/raw details/export warning; Activity is readable outcomes with raw diagnostics optional. Settings sections Providers / Scheduled checks / Automatic updates / Notifications / Data & reset retain their current consent/task/settings behavior. Keep `PCSteward.ScheduledCheck` LIMITED interactive fixed scan-host action; no new task or execution schedule. Preserve factory reset's exact case-sensitive `RESET`, two confirmations, Cancel default/no Enter activation, active-work lockout and next-launch validation before onboarding. Retain active WinUI window reference and startup/busy/reset recovery windows. Pause-all automatic control is absent until execution capability exists.
- [ ] Pass focused plus protected architecture/security/reset tests. Independently compare every invariant in `docs/PROTECTED_UI_BEHAVIOURS.md` with the migrated route and an executable test; update `docs/STATUS.md` / `docs/requirements/TRACEABILITY.md` with actual task evidence.
- [ ] Independent reviewer checks retained behavior/deletion scopes/scheduling honesty and semantic resources. Pass full CI; commit `feat: retain protected controls in curated shell`.

## Task 14: Measure resource use and verify accessibility interactions

**Depends on:** 9, 12, 13. **Requirements:** CRD-09/10/12; NFR-PERF-01-10.

**Files:** Modify `tests/PCSteward.Performance/Program.cs`; create `tests/PCSteward.Application.IntegrationTests/CuratedPerformanceFixtureTests.cs`; extend `CuratedExperienceViewModelTests.cs`, `AppIconCacheTests.cs`; create `docs/review/CURATED_MANUAL_RELEASE.md` and `docs/review/raw/curated-manual-performance.json`. Existing `scripts/Benchmark.ps1` remains the entry point. Add no benchmark package or installed profiler requirement.

**Interfaces:** Extend the harness with deterministic 300-record inventory + 1,000-entry curated fixtures, alongside the existing 2,500-record inventory workload. Results distinguish application-fixture timings from production WinUI/process-tree measurements. Include commit/build/SDK/host, fixture size, cold/warm/empty/warm icon cache, repetition count, median/p95, resource peaks and cancellation latency in the JSON/report.

- [ ] Write `LargeCuratedFixtureUsesBoundedLazyIcons`, `AbandonedFilterCancelsOnlyUnusedRequests`, `SelectionAndFocusSurviveProgressAndRefresh`, `NoIdleOrClosedWindowBackgroundWork`; assert two decode tasks, byte quotas, no full-startup extraction, no idle repeating work and no retained consumers after close. Expand interaction fixtures for long/localized text, right-to-left layout, keyboard Space/Enter, stale result generation and late progress after navigation.
  ```csharp
  Assert.IsTrue(iconJobs.StartedCount < fixture.TotalEntryCount);
  Assert.AreEqual(0, afterClose.ActiveConsumerCount);
  Assert.AreEqual(0, fiveMinuteIdle.RecurringNetworkCalls);
  ```
- [ ] Build and run Application tests `--filter "FullyQualifiedName~CuratedPerformanceFixtureTests|FullyQualifiedName~CuratedExperienceViewModelTests|FullyQualifiedName~AppIconCacheTests"`; confirm red for new regressions, then minimal targeted performance/layout fixes with focused tests (no broad redesign).
- [ ] Run canonical Restore/Build/Test/Benchmark on Windows CI and compare the same base fixtures to the new commit. Retain 30 local/20 online fixture repeats already used by the harness; add 20 curated load/filter/queue runs with medians/p95. Do not label synthetic online fixtures as live provider p95. Preserve concurrency three for connectors and two for icons; include multi-item review preparation overhead rather than hiding repeated preflight scans. Do not optimize away fresh safety checks to meet a timing number.
- [ ] On an authorized isolated Windows interactive fixture build, measure cold shell <=5s target, five-minute idle <=1% CPU and <=300 MB working set/no recurring disk-network, scan <=20% average CPU / <500 MB peak / no >50% sustained >5s, cancel acknowledged <=2s and operable <=5s, shutdown with no unnecessary UI process and scheduled host <=30s exit. Record real healthy provider timings only when read-only source consent is authorized; local/online p95 limits are <=15s/<=90s. Verify scrolling/filter/resize/progress at 900px minimum, 900/1100 epx transitions, 100/150/200% DPI/text scaling, light/dark/high contrast, keyboard and screen-reader names. No installed PC Steward update, installer, UAC, task mutation or app closing is needed for fixture evidence. An interactive build/test host missing from CI is a reported limitation, not a fabricated pass.
- [ ] Independent reviewer checks resource accounting, results provenance, focus/confirmation behavior and benchmark comparison. Broader optional accessibility criteria get proportionate observations, not a new blanket gate. Pass full CI and record unresolved measurements honestly; commit `test: measure curated manual resource and interaction behavior`.

## Task 15: Final whole-change review and owner acceptance package

**Depends on:** every earlier task. **Requirements:** CRD-01-12 initial/manual scope; AC-V3-01-11 and protected boundaries.

**Files:** Update `docs/review/CURATED_MANUAL_RELEASE.md`, `docs/STATUS.md`, `docs/requirements/TRACEABILITY.md`; fixes modify only the owning task's source/test files. No release packaging, signing, system installation, distribution or automation host.

- [ ] Run focused integration/security suites for every new component and all preserved start-boundary regressions; then canonical Restore/Build/Test and Benchmark at the exact final commit. Record passed/failed/skipped counts per project, warnings/errors, artifact links and measured-vs-unmeasured results. Expected: all executable tests pass, no new warnings/errors, no unreviewed safety skips. Increased totals replace the 593 baseline; do not count assertion/compilation failures as tests passed.
- [ ] Have a fresh independent reviewer inspect the complete diff against the approved spec and protected behaviors, beyond individual tasks. Focus on DB/icon/search data authority, source consent, membership migration/removal, Update all isolation, exact agreements, one-helper start boundaries, verification/reset/resource/accessibility and actual desktop composition. No self-review substitutes for this final independent review. Block completion for actionable security/workflow findings; fix under an owning task and rerun affected/full checks.
- [ ] Produce one compact acceptance matrix below with exact tests/CI commits and the real icon/UI/performance observations. Review representative My Apps/Add Apps/inspector/review/progress/Activity/Settings screenshots with the owner; record app-list/icon provenance decisions and exceptions. State known snapshot-race and unmeasured host limits plainly.
- [ ] Owner reviews the complete manual experience and separately authorizes any protected live-use targets/install/UAC/deployment actions. Phase closure uses that single owner review plus authorized live use; no invented additional audit phase. This plan gives no automatic installation or deployment approval.
- [ ] Commit final evidence/docs after passing required checks with the authorized author identity; execution/push/merge/deployment remain separately authorized. Close manual scope; prepare Stage A only under its own reviewed plan later.

## Coverage and acceptance matrix

| Design item | Owning tasks | Required evidence |
|---|---|---|
| CRD-01 four states / compatible migration / manage-remove | 3-5, 6, 13 | Migration v10 -> v11, independent-state and no-installer fixtures, removal restrictions/history, scan clear and reset scopes. |
| CRD-02 updates-only Update all | 5-6, 12 | Queued Install remains untouched; all exclusions/duplicate checks; live changed evidence withheld; no provider upgrade-all path. |
| CRD-03 exact explicit Install | 2, 5-6, 11-12 | Reliable absence, applicable choices, unchecked agreements/Cancel/no approval, source/search/cached-data refusal. |
| CRD-04 exact provider/identity and untrusted-data boundary | 4, 6, 11-12 | Managing provider retained, conflicting preference resolved, tamper/duplicate/user-catalog/search and exact product-entry association tests. |
| CRD-05 immutable review/batch/one helper | 6-7, 12 | Atomic acceptance/membership persistence, digest/version/hash binding, one fake UAC, expiry/replay/substitution/protocol fixtures. |
| CRD-06 cancellation/running apps | 7, 12 | Existing safety regressions plus real adapter tests; fresh helper per-item snapshot, incomplete snapshot skip, no force/reboot UI. |
| CRD-07 verification/retry/recovery | 7, 12-13 | Exit-zero unverified, reliable restart result, interrupted-history recovery, retry with new review, exact history identity. |
| CRD-08 health/setup/consent | 1-2, 11-12 | Positive absence vs Unknown, strict trust/version probe, reviewed fixed WinGet setup X86/Machine, ordinary verification + post-trust, no bootstrap. |
| CRD-09 recognizable safe icons | 8-10, 14 | Real pixel/provenance evidence, trusted local native resource fixture, hostile images/paths/signatures/cache, quotas/eviction/DPI/cancel/reset. |
| CRD-10 usable native interaction | 5, 10-14 | Behavior fixtures plus actual narrow/scaled/themes/keyboard/screen-reader/focus evidence; Cancel/explicit confirm, readable withheld actions. |
| CRD-11 initial automation honesty | 3-4, 13 | New Off restriction, legacy choices retained, no active execution from stored opt-in/scan schedule; Stage A/B items explicitly deferred below. |
| CRD-12 resources | 9, 14-15 | Identical-base fixture comparison, exact host/UI/idle/scan/cancel/shutdown measurements and honest limits. |

Specification section coverage: sections 1-3 -> Tasks 3-7 and 10-13; section 4 -> ownership/contracts and Task 12 wiring; section 5 -> Tasks 1-2; section 6 -> Tasks 8-9; section 7 -> Tasks 10/12/14; section 8 -> initial-retention tests in Tasks 3/4/13 and separate milestones below; section 9 -> Tasks 1/4/6/7/9/14; section 10 -> this matrix/Task 15; section 11 -> manual boundary/owner handoff below. Preserve existing requirement IDs in tests and evidence: FR-CAT-*, FR-INS-*, FR-BAT-*, FR-PRV-01-04, FR-POL-06, FR-RSC-01-05, NFR-PERF-01-10, AC-V3-01-11; FR-AUT-01-10 is addressed only for initial compatibility/defaults here, not claimed implemented automation.

## Separate later milestones

**Stage A automatic updates:** Separate reviewed implementation plan after manual acceptance. Current-user capability-minimal, nonresident, no elevation; user-scope WinGet upgrades only, per-app opt-in + explicit actual schedule review before any execution host is enabled. Its plan must cover exact mapping, compatible Stable scope/architecture, newer live evidence, pin/skip/ignore, catalog permission, required agreements, target-version backoff, running-app/battery/metered/window/cap deferral, verification/history and truthful outcomes. Preserve existing scan cadence; suggest existing non-Manual check time or Daily 09:00 local only in the explicit execution-schedule review. Retained legacy On rows and new Off restrictions cannot silently activate execution. No initial installs/reinstall/adoption. This plan creates none of those hosts/tasks/rules.

**Stage B elevated automatic updates:** Deferred until about two weeks of clean Stage A use and a separate secure plan/review. Admin-protected ProgramData policy, one-shot UAC-approved changes, fixed highest-privilege host while owner is signed in/no stored password, bundled catalog/live provider evidence only/no per-user DB authority, restrictive per-user pause, separate Chocolatey consent, removal/reset pending/partial behavior on declined elevation. No Stage B store/host/policy-write implementation is included in manual tasks.

Other deferred work remains deferred: unattended initial/reinstall/desired-state reconciliation, provider adoption, uninstall, forced close/restart, restore points/New PC profiles, generic expanded feeds, direct/custom installers, runtime remote icon discovery, distribution and framework replacement.

## Owner decisions and execution recommendation

Routine technical choices are fixed in this plan; no extra input is needed to write it. Before implementation, owner review must accept the plan and choose execution method. Recommended: one implementer per small task with a fresh independent reviewer before the next task, followed by a fresh whole-change reviewer. These interfaces touch consent, SQLite transactions and privileged start boundaries; early independent review is worth the additional contexts. Sequential native execution is an alternative, with the same test-first/per-phase review and final independent whole-change review.

During the first readiness/state phase, present the existing 20 starter entries and any proposed corrections as a concrete app list with icon provenance; the owner chooses the actual included list. Default migration is empty membership with retained legacy choices, requiring explicit Manage/Install; no mass adoption. Review fallback-icon exceptions if a safe recognizable source is unavailable. Before phase closure, select any real live-use targets and approve deployment/UAC/install actions separately; fixture and read-only evidence comes first. Later automatic-stage schedules/permissions remain separate decisions, not blockers for manual delivery.

Actual execution blockers at planning time: this local machine lacks SDK 10.0.302, so it cannot compile/run product tests; existing Windows PR CI is the verification route after separately authorized execution/push. Interactive native resource/accessibility/performance evidence is still future work and cannot be inferred from fixture CI. This document has been self-reviewed for section coverage, exact existing-vs-proposed paths, shared type/signature consistency, all five review-focus tests and manual-vs-automation scope; no unresolved placeholders. No product code, dependencies, push, merge, installed app or deployment was changed while preparing it.
