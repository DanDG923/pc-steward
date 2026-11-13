using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03, TM-03/04/13. Phase D stage D7 (design note PHASE_D7_PER_USER_VENDOR_UPDATE_DESIGN §3; owner answers
/// 2026-11-13: Q1-B, Q3-A, Q4-A): a per-user <c>winget</c>-source review reads the installer WinGet would use, binds its type and
/// declared elevation need, reads it again before approval, freezes it as <c>wgu1</c> evidence and pins <c>--installer-type</c>. MSIX
/// and the Store keep the stage C4 evidence. Installers that ask for administrator permission are offered with a warning.
/// </summary>
[TestClass]
[TestCategory("FR-UPD-15")]
public sealed class WingetUserVendorInstallerTests
{
    private static readonly string[] WidgetProcess = ["Widget"];
    private const string Sid = "S-1-5-21-11-22-33-1001";
    private static readonly WinGetUserInstaller ExeInstaller = new(WinGetUserInstallerType.Exe, WinGetUserElevation.Unknown);

    [TestMethod]
    [DataRow("Exe", WinGetUserInstallerType.Exe, "exe")]
    [DataRow("Inno", WinGetUserInstallerType.Inno, "inno")]
    [DataRow("Msi", WinGetUserInstallerType.Msi, "msi")]
    [DataRow("Wix", WinGetUserInstallerType.Wix, "wix")]
    [DataRow("Nullsoft", WinGetUserInstallerType.Nullsoft, "nullsoft")]
    [DataRow("Burn", WinGetUserInstallerType.Burn, "burn")]
    public async Task AVendorInstallerIsBoundAtReviewFrozenAsWgu1AndPinsItsType(string comType, WinGetUserInstallerType type, string argument)
    {
        UpdateJobSelection selection = Selection();
        RecordingStore store = new();
        RecordingExecutor executor = new();
        ScriptedSource source = new(WingetUserInstallerRules.Evaluate(Observation(comType), selection));
        ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, store, executor, source);

        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        Assert.AreEqual(new WinGetUserInstaller(type, WinGetUserElevation.Unknown), preparation.Review!.UserInstaller);

        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(preparation.Review, false, CancellationToken.None);

        Assert.IsTrue(result.Started, result.Message);
        Assert.AreEqual(2, source.Calls, "read at review and again before approval");
        ReviewedUpdateOperation reviewed = executor.Reviewed.Single()!;
        Assert.IsTrue(WingetUserReviewedRoute.Admits(reviewed));
        Assert.IsTrue(WinGetUserEvidence.TryParse(reviewed.EvidenceId, out WinGetUserEvidence? evidence), reviewed.EvidenceId);
        Assert.AreEqual(type, evidence!.InstallerType);
        Assert.AreEqual(WinGetUserObservationKind.Inventory, evidence.ObservationKind);
        Assert.AreEqual(FixtureObservationId, evidence.ObservationId, "the fresh observation the job was re-prepared from");
        CollectionAssert.AreEqual(new[]
        {
            "upgrade", "--id", "Contoso.Widget", "--version", "1.1.0", "--source", "winget", "--exact",
            "--scope", "user", "--architecture", "x64", "--disable-interactivity", "--silent", "--installer-type", argument
        }, WingetReviewedUpdateArguments.Build(reviewed).ToArray());
    }

    [TestMethod]
    [DataRow("ElevationRequired", WinGetUserElevation.Required)]
    [DataRow("ElevatesSelf", WinGetUserElevation.ElevatesSelf)]
    [DataRow("ElevationProhibited", WinGetUserElevation.Prohibited)]
    [DataRow("Unknown", WinGetUserElevation.Unknown)]
    public async Task EveryDeclaredElevationNeedIsOfferedAndBound(string comElevation, WinGetUserElevation elevation)
    {
        // Owner answer Q1-B: installers that ask for administrator permission are offered, with a warning, not refused.
        UpdateJobSelection selection = Selection();
        RecordingStore store = new();
        RecordingExecutor executor = new();
        ScriptedSource source = new(WingetUserInstallerRules.Evaluate(Observation("Exe", elevation: comElevation), selection));
        ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, store, executor, source);

        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        Assert.AreEqual(elevation, preparation.Review!.UserInstaller!.Elevation);
        Assert.IsTrue((await workflow.ConfirmAndRunAsync(preparation.Review, false, CancellationToken.None)).Started);
        Assert.IsTrue(WinGetUserEvidence.TryParse(executor.Reviewed.Single()!.EvidenceId, out WinGetUserEvidence? evidence));
        Assert.AreEqual(elevation, evidence!.Elevation);
    }

    [TestMethod]
    public async Task AStoreTypeInstallerKeepsTheC4EvidenceAndCommandLine()
    {
        UpdateJobSelection selection = Selection();
        RecordingExecutor executor = new();
        ScriptedSource source = new(WingetUserInstallerRules.Evaluate(Observation("MSStore", scope: "Unknown"), selection));
        ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, new RecordingStore(), executor, source);

        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        Assert.IsNull(preparation.Review!.UserInstaller);
        Assert.IsTrue((await workflow.ConfirmAndRunAsync(preparation.Review, false, CancellationToken.None)).Started);
        ReviewedUpdateOperation reviewed = executor.Reviewed.Single()!;
        StringAssert.StartsWith(reviewed.EvidenceId, "inventory:");
        Assert.AreEqual("--silent", WingetReviewedUpdateArguments.Build(reviewed)[^1], "no installer-type argument");
    }

    [TestMethod]
    public async Task AnMsixInstallerIsPinnedToMsixWithoutTheVendorChecks()
    {
        // MSIX stage residual (accepted on this condition): an MSIX app's update runs only an MSIX installer.
        UpdateJobSelection selection = Selection();
        RecordingExecutor executor = new();
        RecordingInventory inventory = new();
        ScriptedSource source = new(WingetUserInstallerRules.Evaluate(Observation("Msix", scope: "Unknown"), selection));
        ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, new RecordingStore(), executor, source, inventory);

        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        Assert.AreEqual(new WinGetUserInstaller(WinGetUserInstallerType.Msix, WinGetUserElevation.Unknown), preparation.Review!.UserInstaller);
        Assert.IsTrue((await workflow.ConfirmAndRunAsync(preparation.Review, false, CancellationToken.None)).Started);
        ReviewedUpdateOperation reviewed = executor.Reviewed.Single()!;
        StringAssert.StartsWith(reviewed.EvidenceId, "wgu1:msix:");
        CollectionAssert.AreEqual(MsixTail, WingetReviewedUpdateArguments.Build(reviewed).TakeLast(3).ToArray());
        Assert.IsNull(inventory.Baselines.Single(), "no whole-PC comparison for an MSIX package");
        Assert.AreEqual(AdministratorRequirementPresenter.ForSelection(selection),
            AdministratorRequirementPresenter.ForSelection(selection, preparation.Review.UserInstaller), "no vendor disclosure");
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    public async Task AnOpenMsixAppIsNeverUpdatedEvenWhenTheWarningWasAcknowledged()
    {
        // MSIX design note §3.6 (owner host check 2026-11-13): Windows registers an app package's update only once the app closes.
        UpdateJobSelection selection = Selection();
        RecordingExecutor executor = new();
        ScriptedSource source = new(WingetUserInstallerRules.Evaluate(Observation("Msix", scope: "Unknown"), selection));
        ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, new RecordingStore(), executor, source, runningProcesses: WidgetProcess);

        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(preparation.Review!, true, CancellationToken.None,
            acknowledgedRunningProcesses: WidgetProcess);

        Assert.IsFalse(result.Started);
        StringAssert.Contains(result.Message, "Windows finishes updating an app package only after the app is closed");
        Assert.IsEmpty(executor.Reviewed, "nothing ran");
        Assert.IsTrue((await workflow.DetectRunningApplicationAsync(selection, CancellationToken.None)).AppearsToBeRunning, "Check again reads the same detection");

        // Another installer type keeps the acknowledged "update anyway".
        RecordingExecutor exeExecutor = new();
        ReviewedUpdateExecutionWorkflow exe = Workflow(selection, new RecordingStore(), exeExecutor,
            new ScriptedSource(WingetUserInstallerRules.Evaluate(Observation("Exe"), selection)), runningProcesses: WidgetProcess);
        UpdateReviewPreparation exeReview = await exe.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        Assert.IsTrue((await exe.ConfirmAndRunAsync(exeReview.Review!, true, CancellationToken.None, acknowledgedRunningProcesses: WidgetProcess)).Started);

        // Codex review (PR #103): a copy open only in another user's session doesn't hold back this account's registration, so it
        // is the usual warning, which can be acknowledged.
        RecordingExecutor otherExecutor = new();
        ReviewedUpdateExecutionWorkflow other = Workflow(selection, new RecordingStore(), otherExecutor,
            new ScriptedSource(WingetUserInstallerRules.Evaluate(Observation("Msix", scope: "Unknown"), selection)), runningProcesses: WidgetProcess,
            runningOnlyInOtherSessions: true);
        UpdateReviewPreparation otherReview = await other.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        Assert.IsFalse((await other.ConfirmAndRunAsync(otherReview.Review!, false, CancellationToken.None)).Started, "still a warning to acknowledge");
        UpdateReviewPreparation otherAgain = await other.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        Assert.IsTrue((await other.ConfirmAndRunAsync(otherAgain.Review!, true, CancellationToken.None, acknowledgedRunningProcesses: WidgetProcess)).Started);
    }

    [TestMethod]
    public async Task TheStoreSourceIsNotRead()
    {
        UpdateJobSelection store = Selection(UpdateSource.MicrosoftStore);
        RecordingExecutor executor = new();
        ScriptedSource source = new(WingetUserInstallerCheck.Vendor(ExeInstaller));
        ReviewedUpdateExecutionWorkflow workflow = Workflow(store, new RecordingStore(), executor, source);

        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(store.StableProductKey, CancellationToken.None);
        Assert.IsNull(preparation.Review!.UserInstaller);
        Assert.IsTrue((await workflow.ConfirmAndRunAsync(preparation.Review, false, CancellationToken.None)).Started);
        Assert.AreEqual(0, source.Calls);
    }

    [TestMethod]
    public async Task AnUnreadableInstallerIsNotReviewed()
    {
        UpdateJobSelection selection = Selection();
        foreach (ScriptedSource source in new[]
                 {
                     new ScriptedSource(WingetUserInstallerCheck.Refused(WingetUserInstallerText.UnreadableMessage)),
                     new ScriptedSource(null),
                     new ScriptedSource(WingetUserInstallerCheck.Vendor(ExeInstaller)) { Throws = true },
                 })
        {
            RecordingStore store = new();
            RecordingExecutor executor = new();
            UpdateReviewPreparation preparation = await Workflow(selection, store, executor, source).BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
            Assert.IsNull(preparation.Review);
            Assert.AreEqual(WingetUserInstallerText.UnreadableMessage, preparation.Message);
            Assert.HasCount(0, store.Jobs);
            Assert.HasCount(0, executor.Reviewed);
        }
    }

    [TestMethod]
    public async Task AnInstallerThatChangedOrCantBeReadBeforeApprovalStartsNothing()
    {
        UpdateJobSelection selection = Selection();
        foreach (WingetUserInstallerCheck? atConfirm in new WingetUserInstallerCheck?[]
                 {
                     WingetUserInstallerCheck.Vendor(new(WinGetUserInstallerType.Inno, WinGetUserElevation.Unknown)),
                     WingetUserInstallerCheck.Vendor(new(WinGetUserInstallerType.Exe, WinGetUserElevation.Required)),
                     WingetUserInstallerCheck.Msix(),
                     WingetUserInstallerCheck.Refused(WingetUserInstallerText.UnreadableMessage),
                     null,
                 })
        {
            RecordingStore store = new();
            RecordingExecutor executor = new();
            ScriptedSource source = new(WingetUserInstallerCheck.Vendor(ExeInstaller)) { Then = atConfirm };
            ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, store, executor, source);
            UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);

            ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(preparation.Review!, false, CancellationToken.None);

            Assert.IsFalse(result.Started);
            Assert.AreEqual(WingetUserInstallerText.ChangedMessage, result.Message);
            Assert.HasCount(0, store.Jobs, "nothing was approved");
            Assert.HasCount(0, executor.Reviewed, "nothing was started");
        }
    }

    [TestMethod]
    public async Task AReviewThatBoundAnInstallerIsRefusedByAWorkflowThatCantReadItAgain()
    {
        UpdateJobSelection selection = Selection();
        RecordingStore store = new();
        RecordingExecutor executor = new();
        ReviewedUpdateExecutionWorkflow without = Workflow(selection, store, executor, source: null);
        UpdateJobReview review = new(Guid.NewGuid(), Guid.NewGuid(), selection, ReviewedInstallerMode.Selected(InstallerMode.Silent)) { UserInstaller = ExeInstaller };

        ReviewedUpdateExecutionResult result = await without.ConfirmAndRunAsync(review, false, CancellationToken.None);

        Assert.IsFalse(result.Started);
        Assert.AreEqual(WingetUserInstallerText.ChangedMessage, result.Message);
        Assert.HasCount(0, store.Jobs);
    }

    [TestMethod]
    public void TheRulesMapWhatWinGetReports()
    {
        UpdateJobSelection selection = Selection();
        Assert.IsTrue(WingetUserInstallerRules.Evaluate(Observation("MSStore", scope: "Unknown"), selection).IsMsix);
        Assert.AreEqual(new WinGetUserInstaller(WinGetUserInstallerType.Msix, WinGetUserElevation.Unknown),
            WingetUserInstallerRules.Evaluate(Observation("Msix", scope: "Unknown"), selection).Installer, "WinGet places MSIX packages itself");
        Assert.AreEqual(new WinGetUserInstaller(WinGetUserInstallerType.Portable, WinGetUserElevation.Unknown),
            WingetUserInstallerRules.Evaluate(Observation("Portable", scope: "Unknown"), selection).Installer, "WinGet places portable apps itself");
        Assert.AreEqual(new WinGetUserInstaller(WinGetUserInstallerType.Zip, WinGetUserElevation.Unknown),
            WingetUserInstallerRules.Evaluate(Observation("Zip", nested: "Portable", scope: "Unknown"), selection).Installer);
        Assert.AreEqual(new WinGetUserInstaller(WinGetUserInstallerType.Zip, WinGetUserElevation.Unknown),
            WingetUserInstallerRules.Evaluate(Observation("Zip", nested: "Exe"), selection).Installer);

        foreach (WinGetUserInstallerObservation unsupported in new[]
                 {
                     Observation("Font"), Observation("Unknown"), Observation("SomethingNew"),
                     Observation("Zip", nested: "Msix"), Observation("Zip", nested: "Unknown"), Observation("Zip", nested: null),
                 })
            Assert.AreEqual(WingetUserInstallerText.UnsupportedTypeMessage, WingetUserInstallerRules.Evaluate(unsupported, selection).Message, unsupported.ToString());

        foreach (WinGetUserInstallerObservation? unreadable in new[]
                 {
                     null,
                     Observation("Exe") with { CatalogIsPredefined = false },
                     Observation("Exe") with { CatalogIsTrusted = false },
                     Observation("Exe") with { ExactIdMatch = false },
                     Observation("Exe") with { MatchCount = 2 },
                     Observation("Exe") with { WasLimitExceeded = true },
                     Observation("Exe") with { TargetVersionMatches = 0 },
                     Observation("Exe") with { TargetVersionMatches = 2 },
                     Observation("Exe") with { PackageId = "Contoso.Other" },
                     Observation("Exe") with { RequestedPackageId = "Contoso.Other" },
                     Observation("Exe") with { InstallerType = null },
                     Observation("Exe", scope: "Unknown"),
                     Observation("Exe", scope: "System"),
                     Observation("Zip", nested: "Exe", scope: "Unknown"),
                     Observation("Exe", elevation: "SomethingNew"),
                     Observation("Exe", elevation: null),
                 })
        {
            WingetUserInstallerCheck check = WingetUserInstallerRules.Evaluate(unreadable, selection);
            Assert.IsFalse(check.IsMsix);
            Assert.IsNull(check.Installer);
            Assert.AreEqual(WingetUserInstallerText.UnreadableMessage, check.Message, unreadable?.ToString());
        }
    }

    [TestMethod]
    public async Task TheSourceFailsClosed()
    {
        UpdateJobSelection selection = Selection();
        Assert.AreEqual(WingetUserInstallerText.UnreadableMessage,
            (await new WingetUserInstallerEvidenceSource(new FixtureQuery(() => throw new InvalidOperationException("COM"))).ReadAsync(selection, CancellationToken.None)).Message);
        WingetUserInstallerEvidenceSource slow = new(new FixtureQuery(() => Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => (WinGetUserInstallerObservation?)null, TaskScheduler.Default)))
        {
            QueryBudget = TimeSpan.FromMilliseconds(50)
        };
        Assert.AreEqual(WingetUserInstallerText.UnreadableMessage, (await slow.ReadAsync(selection, CancellationToken.None)).Message);
        FixtureQuery never = new(() => throw new AssertFailedException("not queried"));
        foreach (UpdateJobSelection other in new[] { Selection(UpdateSource.MicrosoftStore), Selection(scope: UpdateScope.Machine) })
            Assert.AreEqual(WingetUserInstallerText.UnreadableMessage, (await new WingetUserInstallerEvidenceSource(never).ReadAsync(other, CancellationToken.None)).Message);
        Assert.AreEqual(ExeInstaller,
            (await new WingetUserInstallerEvidenceSource(new FixtureQuery(() => Task.FromResult<WinGetUserInstallerObservation?>(Observation("Exe")))).ReadAsync(selection, CancellationToken.None)).Installer);
    }

    [TestMethod]
    public void Wgu1IsAStrictIdentity()
    {
        Guid id = Guid.Parse("0d7e0000-0000-4000-8000-000000000001");
        string identity = new WinGetUserEvidence(WinGetUserInstallerType.Inno, WinGetUserElevation.ElevatesSelf, WinGetUserObservationKind.Inventory, id).ToIdentity();
        Assert.AreEqual("wgu1:inno:elevatesself:inventory:0d7e0000-0000-4000-8000-000000000001", identity);
        Assert.IsTrue(WinGetUserEvidence.TryParse(identity, out WinGetUserEvidence? parsed));
        Assert.AreEqual(new WinGetUserEvidence(WinGetUserInstallerType.Inno, WinGetUserElevation.ElevatesSelf, WinGetUserObservationKind.Inventory, id), parsed);

        DateTimeOffset at = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection()).Confirm(at);
        ReviewedInstallerMode silent = ReviewedInstallerMode.Selected(InstallerMode.Silent);
        Assert.IsNotNull(WingetUserReviewedRoute.Freeze(job, silent, Sid, identity, at));
        foreach (string bad in new[]
                 {
                     "WGU1:inno:elevatesself:inventory:0d7e0000-0000-4000-8000-000000000001",
                     "wgu1:Inno:elevatesself:inventory:0d7e0000-0000-4000-8000-000000000001",
                     "wgu1:msstore:unknown:inventory:0d7e0000-0000-4000-8000-000000000001",
                     "wgu1:inno:required:inventory:0D7E0000-0000-4000-8000-000000000001",
                     "wgu1:inno:required:inventory:00000000-0000-0000-0000-000000000000",
                     "wgu1:inno:required:cache:0d7e0000-0000-4000-8000-000000000001",
                     "wgu1:inno:required:inventory:0d7e0000-0000-4000-8000-000000000001:extra",
                     "wgu1:inno:required",
                     "wgu1",
                 })
        {
            Assert.IsFalse(WinGetUserEvidence.TryParse(bad, out _), bad);
            Assert.IsNull(WingetUserReviewedRoute.Freeze(job, silent, Sid, bad, at), bad);
        }

        ImmutableUpdateJob storeJob = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection(UpdateSource.MicrosoftStore)).Confirm(at);
        Assert.IsNull(WingetUserReviewedRoute.Freeze(storeJob, silent, Sid, identity, at), "wgu1 is limited to the winget source");
    }

    [TestMethod]
    public void TheReviewSheetShowsTheInstallerAndTheOwnerChosenWarning()
    {
        UpdateJobSelection selection = Selection();
        WinGetUserInstaller elevating = new(WinGetUserInstallerType.Inno, WinGetUserElevation.Required);

        AdministratorRequirementPresentation warned = AdministratorRequirementPresenter.ForSelection(selection, elevating);
        Assert.AreEqual(AdministratorRequirementStatus.Required, warned.Status);
        Assert.AreEqual(WingetUserInstallerText.ElevationWarning, warned.Explanation);
        AdministratorRequirementPresentation quiet = AdministratorRequirementPresenter.ForSelection(selection, ExeInstaller);
        Assert.AreEqual(AdministratorRequirementStatus.NotExpected, quiet.Status);
        Assert.AreEqual(WingetUserInstallerText.NoElevationDisclosure, quiet.Explanation);
        Assert.AreEqual(AdministratorRequirementPresenter.ForSelection(selection), AdministratorRequirementPresenter.ForSelection(selection, null));

        UpdateReviewSheet single = UpdateReviewSheet.CreateBatch([new(selection, "Contoso Widget", "Contoso", null, RunningApplicationDetection.NotDetected, elevating)]);
        Assert.AreEqual(WingetUserInstallerText.ElevationWarning, single.AdministratorRequirement.Explanation);
        CollectionAssert.Contains(single.Items.Single().ConfidenceEvidence.ToArray(), "Installer: Vendor installer (Inno Setup).");

        UpdateJobSelection other = new("winget:contoso.other", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Other", "2.0", "2.1",
            UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Unknown, "winget-com-v1");
        AdministratorRequirementPresentation batch = AdministratorRequirementPresenter.ForBatch([selection, other], [elevating, ExeInstaller]);
        Assert.AreEqual(AdministratorRequirementStatus.Required, batch.Status);
        Assert.AreEqual("Administrator approval required for 1 of 2 apps", batch.Label);
        AdministratorRequirementPresentation quietBatch = AdministratorRequirementPresenter.ForBatch([selection, other], [ExeInstaller, null]);
        Assert.AreEqual(AdministratorRequirementStatus.NotExpected, quietBatch.Status);
        StringAssert.Contains(quietBatch.Explanation, "choose No");
        Assert.AreEqual(AdministratorRequirementPresenter.ForBatch([selection, other]), AdministratorRequirementPresenter.ForBatch([selection, other], null));
    }

    [TestMethod]
    public async Task AVendorUpdateIsComparedWithTheWholePcEntriesTakenJustBeforeLaunch()
    {
        UpdateJobSelection selection = Selection();
        RecordingExecutor executor = new();
        RecordingInventory inventory = new();
        ScriptedSource source = new(WingetUserInstallerCheck.Vendor(ExeInstaller));
        ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, new RecordingStore(), executor, source, inventory);
        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);

        Assert.IsTrue((await workflow.ConfirmAndRunAsync(preparation.Review!, false, CancellationToken.None)).Started);

        WholePcRegistrationSnapshot baseline = inventory.Baselines.Single()!;
        Assert.AreEqual(2, baseline.Count, "the fixture's two whole-PC entries; the per-user one is not counted");

        // A Store-type installer keeps the C4 path: no baseline is taken.
        RecordingInventory msixInventory = new();
        ReviewedUpdateExecutionWorkflow msix = Workflow(selection, new RecordingStore(), new RecordingExecutor(),
            new ScriptedSource(WingetUserInstallerCheck.Msix()), msixInventory);
        UpdateReviewPreparation msixReview = await msix.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        Assert.IsTrue((await msix.ConfirmAndRunAsync(msixReview.Review!, false, CancellationToken.None)).Started);
        Assert.IsNull(msixInventory.Baselines.Single());
    }

    [TestMethod]
    public async Task WithoutALiveObservationAVendorUpdateStartsNothing()
    {
        UpdateJobSelection selection = Selection();
        RecordingStore store = new();
        RecordingExecutor executor = new();
        ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, store, executor, new ScriptedSource(WingetUserInstallerCheck.Vendor(ExeInstaller)),
            withObservation: false);
        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);

        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(preparation.Review!, false, CancellationToken.None);

        Assert.IsFalse(result.Started);
        Assert.AreEqual(WingetUserInstallerText.NoBaselineMessage, result.Message);
        Assert.HasCount(0, store.Jobs);
        Assert.HasCount(0, executor.Reviewed);
    }

    [TestMethod]
    public async Task AnIncompleteWholePcBaselineStartsNothing()
    {
        // Codex P2 on #91: a whole-PC registry view or entry the scan couldn't read is never comparison evidence.
        UpdateJobSelection selection = Selection();
        foreach (string warning in new[]
                 {
                     "Windows uninstall registry:Machine64:access-denied", "Windows uninstall registry:Machine32:{KEY}:value-too-large",
                     "Windows uninstall registry:timeout", "Windows uninstall registry:failed",
                 })
        {
            RecordingStore store = new();
            RecordingExecutor executor = new();
            LiveInventoryObservation partial = new(FixtureObservationId, new DateTimeOffset(2026, 11, 13, 14, 0, 0, TimeSpan.Zero), [], [], [warning]);
            Assert.IsFalse(WholePcRegistrationSnapshot.From(partial).IsComplete, warning);
            ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, store, executor, new ScriptedSource(WingetUserInstallerCheck.Vendor(ExeInstaller)),
                observation: partial);
            UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
            ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(preparation.Review!, false, CancellationToken.None);
            Assert.IsFalse(result.Started, warning);
            Assert.AreEqual(WingetUserInstallerText.NoBaselineMessage, result.Message);
            Assert.HasCount(0, store.Jobs);
            Assert.HasCount(0, executor.Reviewed);
        }
        foreach (string harmless in new[] { "Windows uninstall registry:CurrentUser:access-denied", "Current-user AppX/MSIX catalog:timeout" })
            Assert.IsTrue(WholePcRegistrationSnapshot.From(new LiveInventoryObservation(FixtureObservationId, DateTimeOffset.UtcNow, [], [], [harmless])).IsComplete, harmless);
    }

    [TestMethod]
    public void TheSnapshotSeesNewGoneAndReVersionedWholePcEntriesOnly()
    {
        WholePcRegistrationSnapshot before = WholePcRegistrationSnapshot.From(Observation(
            Machine("{AAA}", "1.0", "x64", "Alpha"), Machine("{BBB}", "2.0", "x86", "Beta"), Machine("{CCC}", "3.0", "x64", "Gamma"),
            User("Widget", "1.0.0")));
        WholePcRegistrationSnapshot same = WholePcRegistrationSnapshot.From(Observation(
            Machine("{CCC}", "3.0", "x64", "Gamma"), Machine("{aaa}", "1.0", "x64", "Alpha"), Machine("{BBB}", "2.0", "x86", "Beta"),
            User("Widget", "1.1.0"), User("Other", "9")));
        Assert.HasCount(0, same.ChangedSince(before), "per-user entries, order and key case don't count");

        WholePcRegistrationSnapshot after = WholePcRegistrationSnapshot.From(Observation(
            Machine("{AAA}", "1.1", "x64", "Alpha"), Machine("{BBB}", "2.0", "x64", "Beta"), Machine("{DDD}", "1.0", "x64", "Delta"),
            User("Widget", "1.1.0")));
        CollectionAssert.AreEqual(ExpectedChanged, after.ChangedSince(before).ToArray(),
            "re-versioned, moved between views, new, and gone");
    }

    [TestMethod]
    public async Task TheCoordinatorNeverRecordsSuccessAfterWholePcChanges()
    {
        WholePcRegistrationSnapshot baseline = WholePcRegistrationSnapshot.From(Observation(Machine("{AAA}", "1.0", "x64", "Alpha")));
        string[] many = ["One", "Two;x", "Three", "Four", "Five", "Six", "Seven"];
        foreach ((PostUpdateInventoryResult verification, UpdateProviderAttemptStatus attempt, UpdateHistoryStatus expected, string diagnostic) in new[]
                 {
                     (new PostUpdateInventoryResult(true, false) { WholePcChanges = [] }, UpdateProviderAttemptStatus.AwaitingVerification,
                         UpdateHistoryStatus.Success, "target-version-verified-by-post-update-inventory"),
                     (new PostUpdateInventoryResult(true, false) { WholePcChanges = ["Alpha"] }, UpdateProviderAttemptStatus.AwaitingVerification,
                         UpdateHistoryStatus.VerificationFailed, "unexpected-whole-pc-changes; target-version-detected; changed: Alpha"),
                     (new PostUpdateInventoryResult(false, false) { WholePcChanges = many }, UpdateProviderAttemptStatus.AwaitingVerification,
                         UpdateHistoryStatus.VerificationFailed, "unexpected-whole-pc-changes; target-version-not-detected; changed: One, Two x, Three, Four, Five and 2 more"),
                     (new PostUpdateInventoryResult(true, false), UpdateProviderAttemptStatus.AwaitingVerification,
                         UpdateHistoryStatus.VerificationFailed, "whole-pc-change-check-unavailable; target-version-detected"),
                     (new PostUpdateInventoryResult(true, false) { WholePcChanges = ["Alpha"] }, UpdateProviderAttemptStatus.RestartReported,
                         UpdateHistoryStatus.RestartRequired,
                         "provider-reported-restart-required; target-version-detected-by-post-update-inventory; unexpected-whole-pc-changes; target-version-detected; changed: Alpha; exit-3010"),
                 })
        {
            InMemoryHistory history = new();
            RunningDetector running = new();
            SequentialUpdateBatchCoordinator coordinator = new(new FixedAttemptExecutor(attempt), new FixedInventory(verification), history, running);
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection()).Confirm(DateTimeOffset.UtcNow);
            await coordinator.RunAsync([new UpdateBatchItem(job, new UpdateJobExecutionEvidence(job.Selection, true, true), WholePcBaseline: baseline)],
                CancellationToken.None);
            UpdateHistoryEntry final = history.Entries.Last();
            Assert.AreEqual(expected, final.Status, diagnostic);
            Assert.AreEqual(diagnostic, final.Diagnostic);
        }
    }

    [TestMethod]
    public void TheOutcomeSaysWhatChangedForEveryUser()
    {
        UpdateOutcomePresentation changed = UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.VerificationFailed,
            "unexpected-whole-pc-changes; target-version-detected; changed: Alpha, Beta", 0, "1.1.0");
        Assert.AreEqual(UpdateOutcomeKind.InstalledNotVerified, changed.Kind);
        StringAssert.Contains(changed.Summary, "PC Steward found version 1.1.0, but the update also changed apps installed for every user of this PC (Alpha, Beta).");
        StringAssert.Contains(UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.VerificationFailed,
            "unexpected-whole-pc-changes; target-version-not-detected; changed: Alpha", 0, "1.1.0").Summary, "The update also changed apps");
        StringAssert.Contains(UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.VerificationFailed,
            "whole-pc-change-check-unavailable; target-version-detected", 0, "1.1.0").Summary, "couldn't check whether it also changed");
    }

    private static readonly string[] MsixTail = ["--silent", "--installer-type", "msix"];
    private static readonly string[] ExpectedChanged = ["Alpha", "Beta", "Delta", "Gamma"];
    private static readonly Guid FixtureObservationId = Guid.Parse("0d7e0000-0000-4000-8000-0000000000aa");

    private static LiveInventoryObservation Observation(params (CanonicalProduct Product, InstallationInstance Instance)[] entries) =>
        new(FixtureObservationId, new DateTimeOffset(2026, 11, 13, 14, 0, 0, TimeSpan.Zero), entries.Select(entry => entry.Product),
            entries.Select(entry => entry.Instance), []);

    private static (CanonicalProduct, InstallationInstance) Machine(string key, string version, string architecture, string name)
    {
        CanonicalProduct product = new(Guid.NewGuid(), $"registry:{name}", name);
        return (product, new InstallationInstance(Guid.NewGuid(), product.Id, "registry", key, version, "Machine", architecture));
    }

    private static (CanonicalProduct, InstallationInstance) User(string key, string version)
    {
        CanonicalProduct product = new(Guid.NewGuid(), $"registry:user:{key}", key);
        return (product, new InstallationInstance(Guid.NewGuid(), product.Id, "registry", key, version, "User", "x64"));
    }

    private static WinGetUserInstallerObservation Observation(string? type, string? nested = "Unknown", string? scope = "User", string? elevation = "Unknown") =>
        new("Contoso.Widget", true, true, true, 1, false, "Contoso.Widget", 1, type, nested, scope, elevation);

    private static ReviewedUpdateExecutionWorkflow Workflow(UpdateJobSelection selection, RecordingStore store, RecordingExecutor executor,
        IWingetUserInstallerEvidenceSource? source, IPostUpdateInventory? inventory = null, bool withObservation = true,
        LiveInventoryObservation? observation = null, string[]? runningProcesses = null, bool runningOnlyInOtherSessions = false)
    {
        RunningDetector running = new() { Running = runningProcesses ?? [], OnlyInOtherSessions = runningOnlyInOtherSessions };
        SequentialUpdateBatchCoordinator coordinator = new(executor, inventory ?? new RecordingInventory(), new InMemoryHistory(), running);
        observation ??= withObservation
            ? Observation(Machine("{AAA}", "1.0", "x64", "Alpha"), Machine("{BBB}", "2.0", "x86", "Beta"), User("Contoso.Widget", "1.0.0"))
            : null;
        return new ReviewedUpdateExecutionWorkflow((_, _) => Task.FromResult(new UpdateSelectionPreparation(selection, "fresh", observation)),
            new UpdateJobApprovalService(store), coordinator, running, requestingUserSid: () => Sid, userInstallerEvidence: source);
    }

    private static UpdateJobSelection Selection(UpdateSource source = UpdateSource.WinGet, UpdateScope scope = UpdateScope.User) =>
        new("winget:contoso.widget", UpdateProvider.WinGet, source, "Contoso.Widget", "1.0.0", "1.1.0", scope, UpdateArchitecture.X64,
            UpdateChannel.Unknown, "winget-com-v1");

    private sealed class ScriptedSource : IWingetUserInstallerEvidenceSource
    {
        private readonly WingetUserInstallerCheck? first;

        public ScriptedSource(WingetUserInstallerCheck? first)
        {
            this.first = first;
            Then = first;
        }

        public int Calls { get; private set; }
        public bool Throws { get; init; }
        public WingetUserInstallerCheck? Then { get; init; }

        public Task<WingetUserInstallerCheck> ReadAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throws) throw new InvalidOperationException("COM failed");
            return Task.FromResult((Calls == 1 ? first : Then)!);
        }
    }

    private sealed class FixtureQuery(Func<Task<WinGetUserInstallerObservation?>> read) : IWinGetUserInstallerQuery
    {
        public Task<WinGetUserInstallerObservation?> QueryAsync(UpdateJobSelection selection, CancellationToken cancellationToken) => read();
    }

    private sealed class RecordingExecutor : IApprovedUpdateExecutor
    {
        public List<ReviewedUpdateOperation?> Reviewed { get; } = [];

        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken) =>
            throw new AssertFailedException("The coordinator must pass the reviewed operation.");

        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, ReviewedUpdateOperation? reviewed, UpdateJobExecutionEvidence currentEvidence,
            UpdateItemProgress progress, CancellationToken cancellationToken)
        {
            Reviewed.Add(reviewed);
            return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "provider-completed"));
        }
    }

    private sealed class RecordingStore : IUpdateJobStore
    {
        public List<ImmutableUpdateJob> Jobs { get; } = [];
        public Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) { Jobs.Add(job); return Task.CompletedTask; }
        public Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult<ImmutableUpdateJob?>(Jobs.SingleOrDefault(job => job.Id == jobId));
    }

    private sealed class RecordingInventory : IPostUpdateInventory
    {
        public List<WholePcRegistrationSnapshot?> Baselines { get; } = [];

        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
        {
            Baselines.Add(null);
            return Task.FromResult(new PostUpdateInventoryResult(true, false));
        }

        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, ExpectedUninstallEntry? expected, WholePcRegistrationSnapshot? wholePcBaseline,
            CancellationToken cancellationToken)
        {
            Baselines.Add(wholePcBaseline);
            return Task.FromResult(new PostUpdateInventoryResult(true, false) { WholePcChanges = [] });
        }
    }

    private sealed class FixedInventory(PostUpdateInventoryResult result) : IPostUpdateInventory
    {
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(result);
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, ExpectedUninstallEntry? expected, WholePcRegistrationSnapshot? wholePcBaseline,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class FixedAttemptExecutor(UpdateProviderAttemptStatus status) : IApprovedUpdateExecutor
    {
        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken) =>
            Task.FromResult(new UpdateProviderAttempt(status, status == UpdateProviderAttemptStatus.RestartReported ? 3010 : 0,
                status == UpdateProviderAttemptStatus.RestartReported ? "exit-3010" : "provider-completed"));
    }

    private sealed class InMemoryHistory : IUpdateHistoryStore
    {
        public List<UpdateHistoryEntry> Entries { get; } = [];
        public Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>(Entries.Where(entry => entry.StableProductKey == stableProductKey).ToArray());
        public Task RecoverInterruptedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RunningDetector : IRunningApplicationDetector
    {
        public string[] Running { get; set; } = [];
        public bool OnlyInOtherSessions { get; set; }

        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken) =>
            Task.FromResult(Running.Length == 0 ? RunningApplicationDetection.NotDetected
                : new RunningApplicationDetection(true, Running) { OnlyInOtherSessions = OnlyInOtherSessions ? Running : [] });
    }
}
