using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

[TestClass]
public sealed class ReferenceBoundaryTests
{
    private static readonly string[] RequiredVisualFoundationBrushes = [
        "StewardCanvasBrush", "StewardSurfaceBrush", "StewardTextBrush", "StewardBrandBrush",
        "StewardFocusBrush", "StewardSuccessBrush", "StewardWarningBrush", "StewardDangerBrush"
    ];
    private static readonly string[] SupportedThemeKeys = ["Default", "Light", "Dark", "HighContrast"];

    [TestMethod]
    public void ScheduledHostDoesNotReferenceExecutionOrElevatedHelper()
    {
        string projectPath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ScheduledHost", "PCSteward.ScheduledHost.csproj");
        IEnumerable<string?> references = XDocument.Load(projectPath).Descendants("ProjectReference").Select(element => (string?)element.Attribute("Include"));

        CollectionAssert.DoesNotContain(references.Where(reference => reference is not null).ToArray(), "../PCSteward.Execution/PCSteward.Execution.csproj");
        CollectionAssert.DoesNotContain(references.Where(reference => reference is not null).ToArray(), "../PCSteward.ElevatedHelper/PCSteward.ElevatedHelper.csproj");
    }

    [TestMethod]
    public void ElevatedHelperDoesNotReferenceForbiddenCapabilities()
    {
        string projectPath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ElevatedHelper", "PCSteward.ElevatedHelper.csproj");
        IEnumerable<string?> references = XDocument.Load(projectPath).Descendants("ProjectReference").Select(element => (string?)element.Attribute("Include"));

        foreach (string forbidden in new[] { "Persistence.Sqlite", "Inventory.Windows", "Scheduling.Windows", "Storage", "Desktop" })
        {
            Assert.IsFalse(references.Any(reference => reference?.Contains(forbidden, StringComparison.Ordinal) == true));
        }
    }

    [TestMethod]
    public void ElevatedHelperIsAOneShotEntryPointWithoutServiceOrPersistenceCode()
    {
        string sourcePath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ElevatedHelper", "Program.cs");
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "ElevatedHelperEntryPoint.RunAsync(args)");
        foreach (string forbidden in new[] { "while (", "ServiceBase", "Sqlite", "Registry", "TaskScheduler", "File." })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void MainWindowUsesTheApplicationWorkflowInsteadOfAdapters()
    {
        string sourcePath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "MainWindow.cs");
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "InventoryWorkflow");
        foreach (string forbidden in new[] { "SqliteDatabase", "new RegistryLocalInventoryProvider", "new MsixLocalInventoryProvider", "new WingetLocalInventoryProvider" }) Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal));
    }

    [TestMethod]
    public void DesktopCompositionWiresTheProductionStructuredWinGetAuthority()
    {
        string sourcePath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "App.xaml.cs");
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "new WingetStructuredUpdateCandidateAuthority()");
        StringAssert.Contains(source, "new WingetPostUpdatePackageInventory()");
        Assert.IsFalse(source.Contains("EvidenceDelayInventoryProvider", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ScheduledHostHonoursThePersistedAppxConnectorState()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ScheduledHost", "Program.cs"));

        StringAssert.Contains(source, "Current-user AppX/MSIX catalog");
        StringAssert.Contains(source, "new ScheduledMsixInventoryProvider(() => appxInventory.Enabled)");
    }

    [TestMethod]
    public void UnifiedTrustedSourcesSettingUsesOneAtomicStoreOperation()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "MainWindow.cs"));

        StringAssert.Contains(source, "SaveConnectorStatesAsync");
        Assert.IsFalse(source.Contains("await workflow.SaveConnectorStateAsync(\"WinGet installed inventory\", enabled, enabled, token); await workflow.SaveConnectorStateAsync(\"WinGet package sources\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ScheduledHostUsesTheUnifiedFailClosedTrustedSourceConsent()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ScheduledHost", "Program.cs"));

        StringAssert.Contains(source, "TrustedSourceConnectorConsent.IsEnabled(wingetInventory, sourceState)");
        StringAssert.Contains(source, "new WingetLocalInventoryProvider(() => updateSourcesEnabled)");
        StringAssert.Contains(source, "new ChocolateyLocalInventoryProvider(() => updateSourcesEnabled)");
    }

    [TestMethod]
    public void StructuredWinGetAuthorityUsesCompositeCatalogForInstalledUpdateCorrelation()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "PCSteward.Provider.WinGet",
            "WingetStructuredUpdateCandidateAuthority.cs"));

        StringAssert.Contains(source, "CompositeSearchBehavior = CompositeSearchBehavior.RemotePackagesFromAllCatalogs");
        Assert.IsFalse(source.Contains("CompositeSearchBehavior.LocalCatalogs", StringComparison.Ordinal));
        StringAssert.Contains(source, "InstalledScope = PackageInstallScope.Any");
        StringAssert.Contains(source, "compositeOptions.Catalogs.Add(reference)");
        StringAssert.Contains(source, "manager.CreateCompositePackageCatalog(compositeOptions)");
        StringAssert.Contains(source, "compositeReference.ConnectAsync()");
        Assert.IsFalse(source.Contains("reference.ConnectAsync()", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DesktopPublishCarriesTheNativeWinGetStructuredAuthorityRuntime()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(
            root,
            "src",
            "PCSteward.Desktop",
            "PCSteward.Desktop.csproj"));
        XNamespace msbuild = project.Root?.Name.Namespace ?? XNamespace.None;

        string[] directPackages = project
            .Descendants(msbuild + "PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => include is not null)
            .Cast<string>()
            .ToArray();
        string? deploymentPlatform = project
            .Descendants(msbuild + "MicrosoftManagementDeployment-Platform")
            .Select(element => element.Value)
            .SingleOrDefault();
        XElement? comInteropReference = project
            .Descendants(msbuild + "PackageReference")
            .SingleOrDefault(reference =>
                string.Equals(reference.Attribute("Include")?.Value,
                    "Microsoft.WindowsPackageManager.ComInterop",
                    StringComparison.Ordinal));
        string packagingScript = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "New-PrivateBetaPackage.ps1"));

        CollectionAssert.Contains(directPackages, "Microsoft.WindowsPackageManager.ComInterop");
        CollectionAssert.Contains(directPackages, "Microsoft.WindowsPackageManager.InProcCom");
        Assert.AreEqual("x64", deploymentPlatform);
        Assert.IsNotNull(comInteropReference);
        Assert.AreEqual("true", comInteropReference.Attribute("GeneratePathProperty")?.Value);
        StringAssert.Contains(packagingScript, "Microsoft.Management.Deployment.CsWinRTProjection.dll");
        StringAssert.Contains(packagingScript, "Microsoft.Management.Deployment.dll");
        StringAssert.Contains(packagingScript, "Microsoft.Management.Deployment.winmd");
        StringAssert.Contains(packagingScript, "Microsoft.Management.Deployment.dll.manifest");
        StringAssert.Contains(packagingScript, "WindowsPackageManager.dll");
    }

    [TestMethod]
    public void DesktopStartupCompletesEveryValidPendingResetStateBeforeComposition()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "App.xaml.cs"));

        StringAssert.Contains(source, "markerState is FactoryResetMarkerState.ResetPending or FactoryResetMarkerState.Partial or FactoryResetMarkerState.TransitionPending");
        StringAssert.Contains(source, "FactoryReset.CompletePendingAsync");
        StringAssert.Contains(source, "showPostResetOnboarding = true;");
        StringAssert.Contains(source, "IsFreshResetStoreAsync");
        StringAssert.Contains(source, "ActivateWindow(new DataOperationBusyWindow());");
        StringAssert.Contains(source, "private Window? activeWindow;");
        StringAssert.Contains(source, "Title = \"PC Steward is busy\";");
    }

    [TestMethod]
    public void PostResetOnboardingReloadsConfirmedFreshStateBeforeOpeningScan()
    {
        string desktopSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "MainWindow.cs"));
        string flowSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Storage", "PostResetOnboardingFlow.cs"));
        int acknowledgement = desktopSource.IndexOf("PostResetOnboardingFlow.Evaluate(", StringComparison.Ordinal);
        int failedAcknowledgement = desktopSource.IndexOf("if (!outcome.AcknowledgementSucceeded)", acknowledgement, StringComparison.Ordinal);
        int reloadOrMarkerFailure = desktopSource.IndexOf("if (!outcome.ScanNavigationRequested)", failedAcknowledgement, StringComparison.Ordinal);
        // Task 73 Phase 1: onboarding opens Scan through the shell navigation and clears Back so onboarding is never revisited.
        int openScan = desktopSource.IndexOf("navigation.Reset(ShellPage.Scan); Render();", reloadOrMarkerFailure, StringComparison.Ordinal);

        Assert.IsTrue(acknowledgement >= 0 && failedAcknowledgement > acknowledgement && reloadOrMarkerFailure > failedAcknowledgement && openScan > reloadOrMarkerFailure);
        StringAssert.Contains(flowSource, "if (!acknowledgementResult.Succeeded)");
        StringAssert.Contains(flowSource, "if (markerStateValue != FactoryResetMarkerState.None)");
        StringAssert.Contains(flowSource, "return new(loaded, 1, true, markerStateValue, acknowledgementResult);");
        StringAssert.Contains(desktopSource, "catch (InvalidOperationException) { return false; }");
        StringAssert.Contains(desktopSource, "if (startupStateLoadFailed) { RenderStartupStateLoadFailure(); return; }");
        StringAssert.Contains(desktopSource, "string details = string.Join(\", \", outcome.AcknowledgementResult.Residuals.Select(item => item.Reason));");
    }

    [TestMethod]
    public void ScheduledHostTreatsAnAbandonedScanMutexAsAnOwnedLease()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ScheduledHost", "Program.cs"));

        StringAssert.Contains(source, "try { scanLeaseHeld = mutex.WaitOne(0); }");
        StringAssert.Contains(source, "catch (AbandonedMutexException) { scanLeaseHeld = true; }");
        StringAssert.Contains(source, "finally { mutex.ReleaseMutex(); }");
    }

    [TestMethod]
    public void ScheduledHostUsesCommonOwnershipAndChecksResetBeforeOpeningSqlite()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ScheduledHost", "Program.cs"));
        StringAssert.Contains(source, "CurrentUserDataOperationSession.TryAcquire(TimeSpan.Zero, timeout.Token)");
        StringAssert.Contains(source, "session.Reconcile(new UpdateDispatchReconciliation(database).ReconcileAsync, timeout.Token)");
        StringAssert.Contains(source, "!session.Context.CanReconcile || session.Context.RecoveryRequired");
        Assert.IsLessThan(source.IndexOf("Directory.CreateDirectory(stateDirectory)", StringComparison.Ordinal),
            source.IndexOf("FactoryReset.GetMarkerState()", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("new(false, FactoryReset.DataOperationMutexName)", StringComparison.Ordinal));
        StringAssert.Contains(source, "if (!session.Reconcile(new UpdateDispatchReconciliation(database).ReconcileAsync, timeout.Token)) return UnresolvedOperationExitCode;");
        StringAssert.Contains(source, "private const int UnresolvedOperationExitCode = 78;");
    }

    [TestMethod]
    public void DesktopBootstrapAndResetUseTheCommonGateWithReconciliation()
    {
        string root = FindRepositoryRoot();
        string app = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Desktop", "App.xaml.cs"));
        string window = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Desktop", "MainWindow.cs"));
        Assert.IsFalse(app.Contains("new(false, FactoryReset.DataOperationMutexName)", StringComparison.Ordinal));
        StringAssert.Contains(app, "CurrentUserDataOperationSession.TryAcquire(TimeSpan.Zero, CancellationToken.None)");
        StringAssert.Contains(app, "DataOperationLease.EnterBootstrap(session)");
        StringAssert.Contains(app, "DataOperationLease.ConfigureReconciliation(new UpdateDispatchReconciliation(database).ReconcileAsync)");
        // Reset marker recovery precedes SQLite; reconciliation follows migration and precedes the initial UI load.
        Assert.IsLessThan(app.IndexOf("SqliteDatabase database", StringComparison.Ordinal),
            app.IndexOf("FactoryReset.GetMarkerState(stateDirectory)", StringComparison.Ordinal));
        Assert.IsLessThan(app.IndexOf("DataOperationLease.ReconcileBootstrap(", StringComparison.Ordinal), app.IndexOf("MigrateAsync(", StringComparison.Ordinal));
        Assert.IsLessThan(app.IndexOf("new(workflow, viewModel", StringComparison.Ordinal), app.IndexOf("DataOperationLease.ReconcileBootstrap(", StringComparison.Ordinal));
        StringAssert.Contains(app, "DataOperationLease.RunResetControlSynchronous(() => FactoryReset.AcknowledgeOnboarding(stateDirectory))");
        StringAssert.Contains(window, "DataOperationLease.RunSynchronous(() => FactoryReset.Arm())");
        Assert.IsFalse(window.Contains("try { FactoryReset.Arm();", StringComparison.Ordinal));
        StringAssert.Contains(window, "DataOperationLease.RunReadSynchronous(() =>");
    }

    [TestMethod]
    public void DesktopComposesProtectedMachineUpdatesOnlyThroughTheExistingElevatedHelper()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Desktop", "App.xaml.cs"));
        string project = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Desktop", "PCSteward.Desktop.csproj"));

        StringAssert.Contains(source, "UpdateExecutionCapabilities.UserAndMachine");
        StringAssert.Contains(source, "new ElevatedUpdateRunner()");
        Assert.IsFalse(project.Contains("PCSteward.ElevatedHelper", StringComparison.Ordinal));
    }

    [TestMethod]
    public void StandaloneRemovalScriptHasFixedTaskAuthorityAndNoDataDeletion()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "packaging", "Remove-PCSteward.ps1"));

        StringAssert.Contains(script, "$taskName = 'PCSteward.ScheduledCheck'");
        StringAssert.Contains(script, "Schedule.Service");
        StringAssert.Contains(script, "Get-TaskSchedulerRootFolder");
        StringAssert.Contains(script, "Find-ExactScheduledTask");
        StringAssert.Contains(script, "Get-ExactScheduledTask");
        StringAssert.Contains(script, "Remove-ExactScheduledTask");
        StringAssert.Contains(script, "DeleteTask");
        StringAssert.Contains(script, "The exact PC Steward scheduled task still exists after removal.");
        Assert.IsFalse(script.Contains("TaskSchedulerAdapter", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("schtasks", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("/Query", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("Remove-Item", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("DelTree", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("LocalAppData", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void WinGetResolverUsesThePackageCatalogInsteadOfEnumeratingProtectedWindowsApps()
    {
        string sourcePath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Provider.WinGet", "WingetInstalledInventoryProvider.cs");
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "new PackageManager().FindPackagesForUser(string.Empty)");
        StringAssert.Contains(source, "package.Id.PublisherId, \"8wekyb3d8bbwe\"");
        int rootCheck = source.IndexOf("RejectReparse(root);", StringComparison.Ordinal);
        int catalogRead = source.IndexOf("new PackageManager().FindPackagesForUser(string.Empty)", StringComparison.Ordinal);
        Assert.IsTrue(rootCheck >= 0 && rootCheck < catalogRead, "The WindowsApps root must be rejected before package catalog paths are accepted.");
        Assert.IsFalse(source.Contains("Directory.EnumerateDirectories(root", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ApplicationDetailsUseThePrototypeStyleDetailRouteAndReviewPageShowsDecisionItems()
    {
        string sourcePath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "MainWindow.cs");
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "BuildProductDetailPage()");
        StringAssert.Contains(source, "NavigateTo(ShellPage.Detail)");
        StringAssert.Contains(source, "Header = \"Technical details\"");
        StringAssert.Contains(source, "InventoryRow[] decisions");
        StringAssert.Contains(source, "Needs a decision");
        StringAssert.Contains(source, "Confirm package identity");
        StringAssert.Contains(source, "Confirm all current package identities");
    }

    [TestMethod]
    public void UpdateReviewDisclosesAdministratorRequirementBeforeExecution()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "MainWindow.cs"));
        int detailRequirement = source.IndexOf("AdministratorRequirementPresenter.BeforeReview()", StringComparison.Ordinal);
        // Task 73 Phase 5 (WCAG 2.5.3): the name is built from the visible label, so it still reads "Review and update this verified package. …".
        int reviewButton = source.IndexOf("StewardVisuals.PrimaryButton(\"Review and update\", $\"this verified package. {administratorRequirement.Label}\")", detailRequirement, StringComparison.Ordinal);
        int confirmedRequirement = source.IndexOf("AdministratorRequirementPresenter.ForSelection(selection)", reviewButton, StringComparison.Ordinal);
        // Task 73 Phase 2 (ADR-030 decision 3; audit P-H6): the final confirmation is the review sheet page, awaited through the
        // user's decision, instead of the wall-of-text ContentDialog. The requirement still feeds the sheet before confirmation.
        int sheet = source.IndexOf("UpdateReviewSheet.Create(selection, row.Name, row.Publisher", confirmedRequirement, StringComparison.Ordinal);
        int showConfirmation = source.IndexOf("bool confirmed = await decision.Task;", sheet, StringComparison.Ordinal);
        int execute = source.IndexOf("updates.ConfirmAndRunAsync", showConfirmation, StringComparison.Ordinal);

        Assert.IsTrue(detailRequirement >= 0 && reviewButton > detailRequirement);
        Assert.IsTrue(confirmedRequirement > reviewButton && sheet > confirmedRequirement && showConfirmation > sheet && execute > showConfirmation);
        StringAssert.Contains(source, "preparation.RunningApplication, administratorRequirement);");
        StringAssert.Contains(source, "administratorRequirement.Label");
        StringAssert.Contains(source, "administratorRequirement.Explanation");
        StringAssert.Contains(source, "AutomationProperties.SetHelpText(review, administratorRequirement.Explanation)");
        string reviewStep = source[source.IndexOf("private void AddReviewStep(", StringComparison.Ordinal)..source.IndexOf("private void AddUpdateStep(", StringComparison.Ordinal)];
        StringAssert.Contains(reviewStep, "AutomationProperties.SetHelpText(confirm, sheet.AdministratorRequirement.Explanation);");
        StringAssert.Contains(reviewStep, "if (sheet.ShowsAdministratorShield) primaryContent.Children.Add(new FontIcon");
        Assert.AreEqual(1, reviewStep.Split("new FontIcon", StringSplitOptions.None).Length - 1, "the shield is the only icon and is gated on the requirement");
        string application = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Application", "UpdateJobPresentation.cs"));
        StringAssert.Contains(application, "public bool ShowsAdministratorShield => AdministratorRequirement.Status == AdministratorRequirementStatus.Required;");
        StringAssert.Contains(application, "risks.Add(new(ReviewRiskKind.Administrator, administratorRequirement.Label, administratorRequirement.Explanation,");
    }

    [TestMethod]
    public void DetailDecisionControlsAreReadOnTheUiThreadBeforeStartingADataOperationLease()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "MainWindow.cs"));

        Assert.IsFalse(source.Contains("RunAsync(token => workflow.PinCurrentVersionAsync(row.StableProductKey, row.Version, reason.Text", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("RunAsync(token => workflow.IgnoreAsync(row.StableProductKey, ignoreUntilEnabled.IsChecked", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("RunAsync(token => workflow.CreateManualMappingAsync(row.StableProductKey, detail.SourceId, detail.PackageId, true, reason.Text", StringComparison.Ordinal));
        StringAssert.Contains(source, "string decisionReason = reason.Text;");
        StringAssert.Contains(source, "DateTimeOffset? ignoredUntil = ignoreUntilEnabled.IsChecked == true ? ignoreUntil.Date : null;");
        // Task 73 Phase 5 (audit §4): the list shows display names; the index is read on the UI thread and maps to the same raw ID.
        StringAssert.Contains(source, "string sourceId = mappingSource.SelectedIndex >= 0 && mappingSource.SelectedIndex < mappingSources.Length ? mappingSources[mappingSource.SelectedIndex] : \"winget\";");
        StringAssert.Contains(source, "string[] mappingSources = [\"winget\", \"msstore\"];");
    }

    /// <summary>
    /// Task 73 Phase 2 (ADR-030 decision 3) deliberately replaces the outcome-dialog pin: the recorded outcome is shown on the job
    /// page's Verify step. Completion handling still runs only after the finally block has stopped progress, cleared
    /// <c>isExecuting</c> and reloaded inventory, and the finally block itself never re-renders.
    /// </summary>
    [TestMethod]
    public void UpdateOutcomeIsShownOnTheJobPageOnlyAfterExecutionFinishes()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "MainWindow.cs"));
        int reviewStart = source.IndexOf("private async Task ReviewAndExecuteAsync", StringComparison.Ordinal);
        Assert.IsTrue(reviewStart >= 0);
        // The execution finally block (the review-preparation finally only clears preparingReview).
        int completionStart = source.IndexOf("finally { StopUpdateProgressPolling();", reviewStart, StringComparison.Ordinal);
        Assert.IsTrue(completionStart >= 0);
        int outcome = source.IndexOf("job.Completion = UpdateOutcomePresenter.ForResult(result, selection, row.Name);", completionStart, StringComparison.Ordinal);
        Assert.IsTrue(outcome > completionStart);
        // Independent review: the result is shown where the user is (the band offers "View result" elsewhere); it never navigates.
        int show = source.IndexOf("jobResultPending = navigation.Current != ShellPage.UpdateJob;", outcome, StringComparison.Ordinal);

        Assert.IsTrue(show > outcome);
        string completion = source[completionStart..outcome];
        StringAssert.Contains(completion, "isExecuting = false;");
        StringAssert.Contains(completion, "LoadSavedInventory();");
        StringAssert.Contains(completion, "if (windowClosed) return;");
        Assert.IsFalse(completion.Contains("Render();", StringComparison.Ordinal), "the finally block never re-renders");
    }

    /// <summary>
    /// Task 73 Phase 2 (audit P-H3; UJ-04 step 8) deliberately replaces the "Update completed / Update failed" title pin: the
    /// Desktop no longer flattens outcomes. The portable presenter alone maps them, and only Success (verified re-inventory) is
    /// Verified and only RestartRequired (a reliable restart-pending result) is Restart required.
    /// </summary>
    [TestMethod]
    public void DesktopOnlyReportsCompletionAfterExactPostUpdateVerification()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Desktop", "MainWindow.cs"));
        string presenter = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Application", "UpdateJobPresentation.cs"));

        StringAssert.Contains(source, "UpdateOutcomePresenter.ForResult(result, selection, row.Name)");
        foreach (string flattened in new[] { "_ => \"Update failed\"", "GetUpdateOutcomeTitle", "\"Update completed\"", "result.Started ? \"Update completed\" : \"Update not started\"" })
            Assert.IsFalse(source.Contains(flattened, StringComparison.Ordinal), flattened);
        StringAssert.Contains(presenter, "UpdateHistoryStatus.Success => new(UpdateOutcomeKind.Verified, VerifiedLabel");
        StringAssert.Contains(presenter, "UpdateHistoryStatus.RestartRequired => new(UpdateOutcomeKind.RestartRequired, RestartRequiredLabel");
        Assert.AreEqual(1, presenter.Split("UpdateOutcomeKind.Verified, VerifiedLabel", StringSplitOptions.None).Length - 1, "only one path reads as Verified");
        Assert.AreEqual(1, presenter.Split("UpdateOutcomeKind.RestartRequired, RestartRequiredLabel", StringSplitOptions.None).Length - 1, "only one path reads as Restart required");
        StringAssert.Contains(presenter, "UpdateHistoryStatus.ElevationDenied => new(UpdateOutcomeKind.NotStarted");
        // Independent review: an unreadable outcome, an unrecorded run and an interrupted run with no evidence of installation read
        // as "Result not verified", never "Installed, not verified" or Verified.
        StringAssert.Contains(presenter, "_ => ResultNotVerified(");
        StringAssert.Contains(presenter, "UpdateOutcomeKind.ResultNotVerified => \"Result not verified\"");
        // The helper-missing copy still comes from the portable ElevationStory (audit P-H5).
        StringAssert.Contains(presenter, "Failed(ElevationStory.HelperMissingOutcome, technical)");
        StringAssert.Contains(presenter, "HelperMissingDiagnostic = \"elevated-helper-fixed-installed-path-invalid\"");
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, "src", "PCSteward.Application", "ElevationStory.cs")), "\"The update was blocked before it could run. ");
    }

    [TestMethod]
    public void DesktopVisualFoundationProvidesSemanticResourcesForEverySupportedTheme()
    {
        string desktopDirectory = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop");
        string appXaml = File.ReadAllText(Path.Combine(desktopDirectory, "App.xaml"));
        StringAssert.Contains(appXaml, "Resources/ColorTokens.xaml");
        StringAssert.Contains(appXaml, "Resources/ThemeResources.xaml");
        StringAssert.Contains(appXaml, "Resources/Typography.xaml");
        StringAssert.Contains(appXaml, "Resources/ControlStyles.xaml");

        XDocument tokens = XDocument.Load(Path.Combine(desktopDirectory, "Resources", "ColorTokens.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] themeKeys = tokens.Descendants(presentation + "ResourceDictionary")
            .Attributes(xaml + "Key")
            .Select(attribute => attribute.Value)
            .ToArray();
        CollectionAssert.AreEquivalent(SupportedThemeKeys, themeKeys);

        string[] brushKeys = tokens.Descendants(presentation + "SolidColorBrush")
            .Attributes(xaml + "Key")
            .Select(attribute => attribute.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string semanticBrush in RequiredVisualFoundationBrushes)
        {
            CollectionAssert.Contains(brushKeys, semanticBrush);
        }

        string project = File.ReadAllText(Path.Combine(desktopDirectory, "PCSteward.Desktop.csproj"));
        StringAssert.Contains(project, "logo-mark-64.png");
        StringAssert.Contains(project, "pc-steward.ico");

        string styles = File.ReadAllText(Path.Combine(desktopDirectory, "Resources", "ControlStyles.xaml"));
        StringAssert.Contains(styles, "StewardInputStyle");
        StringAssert.Contains(styles, "StewardSelectStyle");
        StringAssert.Contains(styles, "StewardStatusChipStyle");
        StringAssert.Contains(styles, "FocusVisualPrimaryBrush");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("PCSteward.slnx was not found above the test output directory.");
    }
}
