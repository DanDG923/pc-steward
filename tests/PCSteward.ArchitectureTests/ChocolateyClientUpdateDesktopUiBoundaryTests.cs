using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UI-03/04, TM-03/04/07/13; ADR-031 (owner decisions 1–5), ADR-030: the Desktop's "Update Chocolatey to X"
/// surfaces stay thin, read-only and non-dispatching, also after the Task 50 activation admitted the <c>ChocolateyClientSelfUpdateV1</c> shape.
/// <list type="bullet">
/// <item>the only new Desktop reach into the provider is the read-only release catalog, composed once in App.xaml.cs, whose
/// compiled IL reads only the compiled list's versions;</item>
/// <item>the view model holds no review, draft, permit, ticket, dispatcher or workflow and carries the not-admitted reason exactly
/// while the compiled owner admission (<c>ChocolateyClientUpdateAdmission.OwnerApproved</c>, Chocolatey Confirm design §2 C1) is
/// false, so <c>CanConfirm</c> is false; the constant has one definition that nothing configurable reaches;</item>
/// <item>the review is opened (and so probed) once per click inside its handler, renders every view-model line and binds the
/// primary button to the view model; only <c>ChocolateyClientUpdateReviewSession</c> confirms the review and runs the workflow
/// (design §5 item 2), and the Desktop reaches it only through the session factory composed in App.xaml.cs;</item>
/// <item>design §8.6 gate 3: the offer is computed only from the verified installed-client check, whose single carrier of a
/// release (<c>ChocolateyInstalledClientVerification.Verified</c>) is called only by the native Desktop-role observation; the
/// Desktop reaches it only through the public façade composed in App.xaml.cs, never the Native namespace, and runs it only
/// inside the review handler, off the UI thread, bounded and cancelled when the dialog closes; the check reaches no helper,
/// launcher, held binding, workflow or dispatcher.</item>
/// </list>
/// The admission pins themselves (<c>HasSupportedMode</c>, <c>CapabilityOperationAllowlist</c>, the dispatcher's
/// <c>Supports</c>, which since the Task 50 activation admit the exact client shape) are in
/// <see cref="ClientRunFaultBoundaryTests.TheClientSelfUpdateRouteIsAdmittedOnlyThroughTheThreeActivationPoints"/> and
/// <see cref="DesktopProducersBoundaryTests"/>. The pins below keep the Desktop offer display only after activation (design §8.6
/// gate 3): the view model's not-admitted reason is unconditional and nothing in the Desktop composes a client review.
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdateDesktopUiBoundaryTests
{
    private const string Catalog = "PCSteward.Provider.Chocolatey.ChocolateyClientReleaseCatalog";
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string ViewModel = "PCSteward.Application.ChocolateyClientUpdateViewModel";
    private const string Verification = "PCSteward.Application.ChocolateyInstalledClientVerification";
    private const string Probe = "PCSteward.Provider.Chocolatey.ChocolateyInstalledClientProbe";
    private const string InstalledClientCheck = Native + "ChocolateyDesktopObservation.ObserveInstalledClientAsync";
    private static readonly string[] CatalogNativeTargets =
    [
        Native + "ChocolateyReleaseList.get_Listed", Native + "ChocolateyReleaseList.get_Releases", Native + "ChocolateyReviewedRelease.get_Version",
    ];
    private static readonly string[] ForbiddenViewModelTargets =
    [
        "PCSteward.Application.ReviewedUpdateReview", "PCSteward.Application.ReviewedUpdateDraft", "PCSteward.Application.LiveApprovedUpdate",
        "PCSteward.Application.ReviewedUpdateDispatchWorkflow", "PCSteward.Application.ReviewedUpdateExecutionWorkflow",
        "PCSteward.Application.ReviewedUpdateDispatchTicket", "PCSteward.Application.IReviewedUpdateRouteDispatcher",
    ];

    [TestMethod]
    public void TheDesktopReadsOnlyTheCompiledReleaseCatalogComposedOnce()
    {
        string desktop = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Desktop");
        string all = string.Concat(Directory.EnumerateFiles(desktop, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(File.ReadAllText));
        Assert.AreEqual(1, Regex.Count(all, @"\bChocolateyClientReleaseCatalog\b"), "one composition site of the catalog");
        // Independent review L5: the single read is wrapped so a fault building the compiled list never fails startup.
        StringAssert.Contains(Desktop("App.xaml.cs"),
            "ChocolateyReviewedRouteProbe.ProbeSplitTokenState, ChocolateyClientReleaseCatalogFacts.ReadOrUnavailable(() => ChocolateyClientReleaseCatalog.Current), ChocolateyInstalledClientProbe.ObserveAsync, chocolateyClientUpdates.CreateSession, chocolateyPackageUpdates.CreateSession, CreateDiagnostics(database), showPostResetOnboarding");

        // The catalog's compiled IL reads only the compiled list's versions in the native layer (the signer is a compiled constant).
        string chocolatey = CompiledIlScanner.BuiltSrcAssemblies().Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        CompiledIlScanner.IlReference[] references = CompiledIlScanner.References(chocolatey).Where(reference => reference.Caller == Catalog).ToArray();
        Assert.IsTrue(references.Length > 0, "the catalog was not found in the built assembly");
        string[] nativeTargets = references.Where(reference => reference.TargetType.StartsWith(Native, StringComparison.Ordinal))
            .Select(reference => reference.TargetType + "." + reference.TargetMember).Distinct().ToArray();
        CollectionAssert.AreEquivalent(CatalogNativeTargets, nativeTargets, string.Join(", ", nativeTargets));
        string[] other = references.Select(reference => reference.TargetType)
            .Where(type => !type.StartsWith(Native, StringComparison.Ordinal) && !type.StartsWith("System.", StringComparison.Ordinal) &&
                type is not (Catalog or "PCSteward.Application.ChocolateyClientReleaseCatalogFacts"))
            .Distinct().ToArray();
        Assert.AreEqual(0, other.Length, string.Join(", ", other));
    }

    [TestMethod]
    public void TheViewModelHasNoConfirmPermitOrDispatchPathAndIsUnavailableWhileNotAdmitted()
    {
        string application = CompiledIlScanner.BuiltSrcAssemblies().Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Application");
        CompiledIlScanner.IlReference[] references = CompiledIlScanner.References(application).Where(reference => reference.Caller == ViewModel).ToArray();
        Assert.IsTrue(references.Length > 0, "the view model was not found in the built assembly");
        string[] forbidden = references.Where(reference => ForbiddenViewModelTargets.Contains(reference.TargetType))
            .Select(reference => reference.TargetType + "." + reference.TargetMember).Distinct().ToArray();
        Assert.AreEqual(0, forbidden.Length, string.Join(", ", forbidden));

        string source = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Application", "ChocolateyClientUpdateViewModel.cs"));
        // Chocolatey Confirm design §2 C1: the not-admitted reason is the first entry exactly while owner admission is not compiled in.
        StringAssert.Contains(source, "List<ChocolateyClientUpdateUnavailableReason> reasons = [];");
        StringAssert.Contains(source, "if (!Admitted) reasons.Add(ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview);");
        Assert.AreEqual(1, Regex.Count(source, @"\bAdmitted\s*=(?!=)"), "a single assignment of the admission");
        StringAssert.Contains(source, "Admitted = admitted;");
        // The one public factory passes the compiled C1; the admission-taking factory is for tests only.
        StringAssert.Contains(source, "OpenView(ChocolateyClientUpdateAdmission.IsOwnerApproved, catalog, verification, reportedInstalledVersion, probeSplitToken);");
        Assert.AreEqual(2, Regex.Count(source, @"\bIsOwnerApproved\b(?!""|</)"), "the public factory and the static entry detail copy only");
        Assert.AreEqual(1, Regex.Count(source, @"NotAdmittedPendingReview\);"), "the reason is added at one site");
        int notAdmitted = source.IndexOf("if (!Admitted) reasons.Add(", StringComparison.Ordinal);
        Assert.IsTrue(notAdmitted > 0 && notAdmitted < source.IndexOf("reasons.Add(verification.Check switch", StringComparison.Ordinal), "the not-admitted reason is added first");
        StringAssert.Contains(source, "public bool CanConfirm => UnavailableReasons.Count == 0;");
        StringAssert.Contains(source, "public bool IsConfirmEnabled => CanConfirm;");
        StringAssert.Contains(source, "public bool IsPrimaryActionEnabled => CanConfirm || OffersCheckAgain;");
        Assert.IsFalse(Regex.IsMatch(source, @"\b(TryConfirm|ConfirmAndRunAsync|LiveApprovedUpdate|ReviewedUpdateDraft|ConfirmAsync|PrepareAsync)\b"), "no confirm path or permit");
        // Once a reason is added it is never shrunk, reordered, filtered or replaced (review of 9a789ea, M). The behavioural
        // proof is ChocolateyClientUpdateViewModelTests; this pins the source shape.
        Assert.IsFalse(Regex.IsMatch(source, @"\breasons\s*\.\s*(Remove\w*|Clear|Insert\w*|Reverse|Sort)\s*\(|\breasons\s*\[[^\]]*\]\s*=(?!=)"),
            "the not-admitted reason is never removed, replaced or moved");
        Assert.AreEqual(1, Regex.Count(source, @"\bUnavailableReasons\s*=(?!=)"), "a single assignment of the reasons");
        StringAssert.Contains(source, "UnavailableReasons = reasons.AsReadOnly();");

        // C1 is one compiled constant, read only through its property, and nothing configurable reaches it.
        string root = CompiledIlScanner.FindRepositoryRoot();
        string admission = ApplicationSource("ChocolateyClientUpdateAdmission.cs");
        Assert.AreEqual(1, Regex.Count(admission, @"public const bool OwnerApproved = (true|false);"), "one compiled definition");
        StringAssert.Contains(admission, "public static bool IsOwnerApproved => OwnerApproved;");
        Assert.IsFalse(Regex.IsMatch(admission, @"\b(Registry|Environment|File|Directory|Sqlite|Configuration|AppContext|GetEnvironmentVariable|static\s+bool\s+\w+\s*[;=]|set\s*;)\b"),
            "the admission reads no configuration, registry, environment, file or database value and has no mutable state");
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !path.EndsWith("ChocolateyClientUpdateAdmission.cs", StringComparison.Ordinal) &&
                !path.EndsWith("ChocolateyPackageUpdateAdmission.cs", StringComparison.Ordinal)))
            Assert.IsFalse(Regex.IsMatch(File.ReadAllText(path), @"\bOwnerApproved\b"), $"{path} names the constant (read IsOwnerApproved)");

        // Design §5 item 2: only the review session confirms a review for the client route and runs the dispatch workflow. Package
        // Confirm addendum §5: the package session is the only other confirm and run site (ChocolateyPackageUpdateBoundaryTests).
        const string Session = "PCSteward.Application.ChocolateyClientUpdateReviewSession";
        const string PackageSession = "PCSteward.Application.ChocolateyPackageUpdateReviewSession";
        List<string> confirmCallers = [], runCallers = [], admissionFactoryCallers = [];
        foreach (string path in CompiledIlScanner.BuiltSrcAssemblies())
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                // C1 has no production override: the admission-taking factories are reached only from tests.
                if (reference.TargetMember is "OpenWithAdmissionForTests" or "CreateWithAdmissionForTests")
                    admissionFactoryCallers.Add(reference.Caller);
                if (reference.TargetType == "PCSteward.Application.ReviewedUpdateReview" && reference.TargetMember == "Confirm")
                    confirmCallers.Add(reference.Caller);
                if (reference.TargetType == "PCSteward.Application.ReviewedUpdateDispatchWorkflow" && reference.TargetMember == "RunAsync")
                    runCallers.Add(reference.Caller);
            }
        // The package-route review model's TryConfirm is the other caller; nothing composes a draft for it (ReviewedRouteDesktopUiBoundaryTests).
        CollectionAssert.AreEquivalent(new[] { Session, PackageSession, "PCSteward.Application.ChocolateyReviewedRouteReviewViewModel" },
            confirmCallers.Select(caller => caller.Split('+')[0]).Distinct().ToArray(), string.Join(", ", confirmCallers));
        CollectionAssert.AreEquivalent(new[] { Session, PackageSession }, runCallers.Select(caller => caller.Split('+')[0]).Distinct().ToArray(), string.Join(", ", runCallers));
        Assert.AreEqual(0, admissionFactoryCallers.Count, string.Join(", ", admissionFactoryCallers));
        string session = ApplicationSource("ChocolateyClientUpdateReviewSession.cs");
        StringAssert.Contains(session, ": this(ChocolateyClientUpdateAdmission.IsOwnerApproved, observeForReview,");
        Assert.AreEqual(1, Regex.Count(session, @"\bIsOwnerApproved\b(?!""|</)"), "the public constructor passes the compiled C1");
        Assert.AreEqual(1, Regex.Count(session, @"\bthis\.admitted\s*=(?!=)"), "a single assignment of the admission");
        StringAssert.Contains(session, "if (admitted && OfferFor(verifiedInstalledRelease) is { } target)");
        StringAssert.Contains(session, "if (!admitted) return ChocolateyClientUpdateRefusal.NotAdmitted;");
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            Assert.IsFalse(Regex.IsMatch(File.ReadAllText(path), @"\b(OpenWithAdmissionForTests|CreateWithAdmissionForTests)\s*\(") &&
                !path.EndsWith("ChocolateyClientUpdateViewModel.cs", StringComparison.Ordinal) && !path.EndsWith("ChocolateyClientUpdateReviewSession.cs", StringComparison.Ordinal) &&
                !path.EndsWith("ChocolateyPackageUpdateViewModel.cs", StringComparison.Ordinal) && !path.EndsWith("ChocolateyPackageUpdateReviewSession.cs", StringComparison.Ordinal),
                $"{path} reaches a test-only admission factory");
    }

    [TestMethod]
    public void TheReviewIsReadOnlyProbesAfreshAndBindsConfirmToTheViewModel()
    {
        string window = Desktop("MainWindow.cs");
        string surfaces = Desktop("ChocolateyClientUpdateSurfaces.cs");

        // Entry points: the Settings card, and Installed apps detail only for the chocolatey client row; each click opens the review.
        string settings = Between(window, "private StackPanel BuildSettingsPage()", "// Scan scope / AppX.");
        StringAssert.Contains(settings, "ChocolateyClientUpdateViewModel.SettingsCardTitle");
        StringAssert.Contains(settings, "ChocolateyClientUpdateEntry(chocolateyClientSummary)");
        int guard = window.IndexOf("if (ChocolateyClientUpdateViewModel.IsClientRow(row))", StringComparison.Ordinal);
        int detailEntry = window.IndexOf("clientUpdate.Children.Add(ChocolateyClientUpdateEntry(clientSummary));", StringComparison.Ordinal);
        Assert.IsTrue(guard >= 0 && detailEntry > guard, "the detail entry point is guarded by the client row");
        Assert.AreEqual(2, Regex.Count(window, @"ChocolateyClientUpdateEntry\(\w+\)\)"), "exactly two entry points");
        Assert.AreEqual(1, Regex.Count(window, @"ChocolateyClientUpdateSurfaces\.EntryButton\("));
        StringAssert.Contains(window, "entry.Click += async (_, _) => await ShowChocolateyClientUpdateAsync();");

        // Opened (and so probed) inside the handler; summaries never probe; nothing is cached in a field.
        string region = Between(window, "private ChocolateyClientUpdateViewModel ChocolateyClientUpdateView(", DesktopWindowSource.EndOf("MainWindow.Chocolatey.cs"));
        string handler = Between(region, "private async Task ShowChocolateyClientUpdateAsync()", "\n    }\n");
        StringAssert.Contains(handler, "ChocolateyClientUpdateViewModel OpenChecking() => ChocolateyClientUpdateView(probeReviewedRouteSplitToken, ChocolateyInstalledClientVerification.Checking);");
        StringAssert.Contains(handler, "ChocolateyClientUpdateViewModel model = OpenChecking();");
        StringAssert.Contains(handler, "ChocolateyClientUpdateSurfaces.ReviewDialog(Content.XamlRoot, model);");
        StringAssert.Contains(handler, "using ChocolateyClientUpdateReviewDialog review = new(dialog, model, OpenChecking, checkInstalledChocolateyClient,");
        Assert.AreEqual(1, Regex.Count(window, @"ChocolateyClientUpdateViewModel\.Open\("), "one view-model construction site");
        Assert.AreEqual(1, Regex.Count(window, @"ChocolateyClientUpdateView\(probeReviewedRouteSplitToken\b"), "only the review probes");
        StringAssert.Contains(region, "probe ?? (() => ReviewedRouteSplitTokenState.Unknown)");
        Assert.IsFalse(Regex.IsMatch(window, @"private\s+(readonly\s+)?ChocolateyClientUpdateViewModel\s+\w+\s*(;|=[^>])"), "no cached view model");

        // Nothing in the entry, view or handler starts, confirms or dispatches anything, or re-enables Confirm.
        Assert.IsFalse(Regex.IsMatch(region, @"\b(updates\.|workflow\.|ConfirmAndRunAsync|DataOperationLease\.Run|Dispatch|Execute|TryConfirm|ReviewedUpdateReview|LiveApprovedUpdate|IsPrimaryButtonEnabled|PrimaryButtonText)"),
            "the client update surfaces start nothing");

        // Thin rendering: every line, then Confirm bound to IsConfirmEnabled only.
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in model.Fields)");
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in model.DisclosureFields)");
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in model.UnavailableFields)");
        StringAssert.Contains(surfaces, "dialog.IsPrimaryButtonEnabled = model.IsPrimaryActionEnabled;");
        StringAssert.Contains(surfaces, "dialog.PrimaryButtonText = model.PrimaryActionText;");
        // The progress view turns the primary button off; the result view offers only "Check again".
        Assert.AreEqual(3, Regex.Count(surfaces, @"IsPrimaryButtonEnabled\s*="), "the review, progress and result renders");
        string progress = Between(surfaces, "internal static void RenderProgress(", "\n    }\n");
        StringAssert.Contains(progress, "dialog.IsPrimaryButtonEnabled = false;");
        // Design §4.3: Close is hidden while the run is in flight, and the owner sees why closing is refused.
        StringAssert.Contains(progress, "dialog.CloseButtonText = string.Empty;");
        StringAssert.Contains(progress, "[change, ChocolateyClientUpdateRunPresentation.RunningCloseGuardText]");
        StringAssert.Contains(Between(surfaces, "internal static void RenderResult(", "\n    }\n"), "dialog.CloseButtonText = ChocolateyClientUpdateViewModel.CloseText;");
        string result = Between(surfaces, "internal static void RenderResult(", "\n    }\n");
        StringAssert.Contains(result, "dialog.PrimaryButtonText = ChocolateyClientUpdateViewModel.CheckAgainText;");
        StringAssert.Contains(surfaces, "DefaultButton = ContentDialogButton.Close,");

        // Design §4.1/§5 item 8: the dialog controller confirms only through the view model's state and the session, never closes on
        // the primary button, refuses Close while a run is in flight, and names no workflow, dispatcher, review or permit.
        string controller = Desktop("ChocolateyClientUpdateReviewDialog.cs");
        StringAssert.Contains(controller, "args.Cancel = true;\n        if (running || checking) return;");
        StringAssert.Contains(controller, "else if (current.CanConfirm && session is { } confirmed) _ = RunAsync(confirmed);");
        StringAssert.Contains(controller, "if (running) args.Cancel = true;");
        StringAssert.Contains(controller, "ChocolateyClientUpdateRunResult run = await confirmed.ConfirmAsync(checkStartedAt, progress, CancellationToken.None);");
        Assert.AreEqual(1, Regex.Count(controller, @"\.ConfirmAsync\("), "one confirm site");
        Assert.AreEqual(1, Regex.Count(controller, @"\.PrepareAsync\("), "one prepare site");
        StringAssert.Contains(controller, "if (current.UnavailableReasons is not [ChocolateyClientUpdateUnavailableReason.PreparingReview]) return;");
        Assert.IsFalse(Regex.IsMatch(controller, @"\b(ReviewedUpdateDispatchWorkflow|IReviewedUpdateRouteDispatcher|ReviewedUpdateReview|LiveApprovedUpdate|DataOperationLease|ConfirmAndRunAsync|PCSteward\.Execution)\b"),
            "the controller reaches the route only through the session");
        StringAssert.Contains(window, "AppWindow.Closing += (_, args) => { if (activeChocolateyClientUpdate?.IsRunning == true || activeChocolateyPackageUpdate?.IsRunning == true) args.Cancel = true; };");
        Assert.IsFalse(Regex.IsMatch(surfaces, @"\b(TryConfirm|Click|DataOperationLease|ReviewedUpdateReview|ConfirmAndRunAsync|Dispatch\w*|LiveApprovedUpdate)\b"),
            "the surfaces hold no action");

        // Accessibility (Phase 5 helpers, AC-UX best effort): level-3 headings, label-in-name entry, named lines.
        StringAssert.Contains(surfaces, "StewardVisuals.SectionHeading(text, AutomationHeadingLevel.Level3)");
        StringAssert.Contains(surfaces, "AutomationProperties.SetName(entry, model.EntryAutomationName);");
        // Independent review L4: the summary changes when the check ends, so it is a polite live region announced through the
        // Phase 5 helper; the re-render keeps the ScrollViewer and its offset and replaces only the lines below the summary.
        string render = Between(surfaces, "internal static void Render(", "\n    }\n");
        Assert.AreEqual(1, Regex.Count(surfaces, @"AutomationProperties\.SetLiveSetting\(summary, AutomationLiveSetting\.Polite\);"), "one live region: the summary");
        StringAssert.Contains(render, "if (dialog.Content is ScrollViewer { Content: StackPanel { Children.Count: > 0 } rendered } host && rendered.Children[0] is TextBlock liveSummary)");
        StringAssert.Contains(render, "double offset = host.VerticalOffset;");
        StringAssert.Contains(render, "while (rendered.Children.Count > 1) rendered.Children.RemoveAt(rendered.Children.Count - 1);");
        StringAssert.Contains(render, "StewardVisuals.SetLiveText(liveSummary, model.Summary, announce: true);");
        StringAssert.Contains(render, "host.ChangeView(null, offset, null, true);");
        Assert.AreEqual(1, Regex.Count(render, @"dialog\.Content\s*="), "the ScrollViewer is created once, on the first render only");
        Assert.IsTrue(render.IndexOf("host.ChangeView(", StringComparison.Ordinal) < render.IndexOf("dialog.Content =", StringComparison.Ordinal),
            "the re-render branch restores the offset; only the first render creates the ScrollViewer");
        StringAssert.Contains(surfaces, "AutomationProperties.SetName(line, field.AutomationName);");
        StringAssert.Contains(surfaces, "AutomationProperties.SetHelpText(dialog, model.ConfirmAutomationName);");
    }

    /// <summary>
    /// Design §8.6 gate 3 (Task 73 UI review L3): the offer is computed only from the verified check. The view model derives its
    /// installed release only from a Verified result, calls the offer rule once with it, never with the scan's version; and the
    /// only <c>src</c> caller of <c>ChocolateyInstalledClientVerification.Verified</c> is the native installed-client observation.
    /// </summary>
    [TestMethod]
    public void TheOfferComesOnlyFromTheVerifiedCheck()
    {
        string source = ApplicationSource("ChocolateyClientUpdateViewModel.cs");
        StringAssert.Contains(source, "string? installed = verification.Check == ChocolateyInstalledClientCheck.Verified ? verification.InstalledRelease : null;");
        StringAssert.Contains(source, "string? offer = OfferTarget(installed, listed);");
        Assert.AreEqual(2, Regex.Count(source, @"\bOfferTarget\("), "the rule's definition and its one call");
        Assert.AreEqual(1, Regex.Count(source, @"OfferVersion\s*=\s*offer;"), "one assignment of the offer");
        Assert.IsFalse(Regex.IsMatch(source, @"OfferTarget\([^)]*(reported|Reported)"), "the scan's version never reaches the offer rule");
        Assert.IsFalse(Regex.IsMatch(source, @"ChocolateyInstalledClientVerification\.Verified\("), "the view model never fabricates a verified release");
        string verification = ApplicationSource("ChocolateyInstalledClientVerification.cs");
        StringAssert.Contains(verification, "private ChocolateyInstalledClientVerification(ChocolateyInstalledClientCheck check, string? installedRelease)");
        Assert.AreEqual(1, Regex.Count(verification, @"new\(ChocolateyInstalledClientCheck\.Verified,"), "only Verified constructs a verified result");

        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        Assert.IsTrue(assemblies.Count >= 5, "src assemblies were not built");
        string chocolatey = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        Assert.IsTrue(CompiledIlScanner.MethodDefinitions(assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Application"))
            .Contains((Verification, "Verified")), "Verified is not defined; update the pin");
        List<string> callers = [], violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (reference.TargetType == Verification && reference.TargetMember == "Verified") callers.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller}");
                if (reference.TargetType == Verification && reference.TargetMember == ".ctor" && reference.Caller != Verification)
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the verification");
            }
        CollectionAssert.AreEquivalent(new[] { "PCSteward.Provider.Chocolatey.dll: " + InstalledClientCheck }, callers.Distinct().ToArray(), string.Join(", ", callers));
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        // The check observes the installed client only through the existing production Desktop-role, client-mode observation.
        string observation = ProviderSource(Path.Combine("Native", "ChocolateyDesktopObservation.cs"));
        StringAssert.Contains(observation, "ObserveInstalledClientAsync(ProductionObserveClient, ChocolateyReleaseList.Listed, Budget, TimeProvider.System, cancellationToken);");
        StringAssert.Contains(observation, "? ChocolateyInstalledClientVerification.Verified(expected.Version)");
        StringAssert.Contains(observation, "releases.Contains(expected) && ReferenceEquals(observation.Release, expected) &&");
        Assert.IsTrue(CompiledIlScanner.References(chocolatey).Any(reference => reference.LogicalCaller == InstalledClientCheck &&
            reference.TargetType == Native + "ChocolateyDesktopObservation" && reference.TargetMember == "ProductionObserveClient"),
            "the production check uses the existing client-mode Desktop observation");
    }

    /// <summary>
    /// The Desktop never names the Native namespace: it reaches the check only through the public façade, which calls only the
    /// production installed-client check, composed once in App.xaml.cs (a method group: nothing runs at startup) and run only
    /// inside the review handler, off the UI thread, under the outer bound, and cancelled when the dialog closes.
    /// </summary>
    [TestMethod]
    public void TheDesktopReachesTheCheckOnlyThroughTheFacadeOnDemand()
    {
        string desktop = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Desktop");
        string[] files = Directory.EnumerateFiles(desktop, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToArray();
        Assert.IsTrue(files.Length > 5, "Desktop sources not found");
        foreach (string file in files)
            Assert.IsFalse(File.ReadAllText(file).Contains("PCSteward.Provider.Chocolatey.Native", StringComparison.Ordinal), $"{file} names the Native namespace");
        string all = string.Concat(files.Select(File.ReadAllText)).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.AreEqual(1, Regex.Count(all, @"\bChocolateyInstalledClientProbe\b"), "one composition site of the façade");
        // Independent review L6(b): a cheap source pin beside the IL pin (which covers the Desktop only where it is built, on
        // Windows CI): no Desktop source fabricates a verified installed release.
        Assert.IsFalse(Regex.IsMatch(all, @"ChocolateyInstalledClientVerification\s*\.\s*Verified\s*\("), "the Desktop never calls ChocolateyInstalledClientVerification.Verified(");
        Assert.AreEqual(0, Regex.Count(all, @"ChocolateyInstalledClientProbe\.ObserveAsync\("), "composed as a method group, never invoked at startup");
        Assert.IsFalse(Regex.IsMatch(all, @"\bChocolateyDesktopObservation\b|\bChocolateyProfileObserver\b"), "the Desktop names no native observation");

        // Chocolatey Confirm build: the review dialog controller runs the check (MainWindow passes the composed check through).
        string window = Desktop("MainWindow.cs");
        string controller = Desktop("ChocolateyClientUpdateReviewDialog.cs");
        Assert.AreEqual(0, Regex.Count(window, @"\bcheckInstalledChocolateyClient\("), "the window never runs the check itself");
        Assert.AreEqual(1, Regex.Count(controller, @"\bcheckInstalled\("), "the check runs at one site");
        string check = Between(controller, "private async Task CheckAndPrepareAsync(", "\n    }\n");
        StringAssert.Contains(check, "ChocolateyInstalledClientVerification.RunAsync(");
        StringAssert.Contains(check, "cancellation => Task.Run(() => checkInstalled(cancellation), CancellationToken.None)");
        StringAssert.Contains(check, "ChocolateyInstalledClientVerification.DisplayCheckTimeout, token);");
        StringAssert.Contains(check, "if (token.IsCancellationRequested || windowClosed()) return;");
        StringAssert.Contains(check, "current = opened.WithVerification(verification);");
        // Closing the dialog cancels the check and any preparation.
        string show = Between(controller, "internal async Task ShowAsync()", "\n    }\n");
        StringAssert.Contains(show, "lifetime.Cancel();");
        StringAssert.Contains(check, "CancellationToken token = lifetime.Token;");
        Assert.AreEqual(3, Regex.Count(controller, @"CheckAndPrepareAsync\("), "the definition, the first check when the dialog opens, and Check again") ;
        Assert.IsFalse(Regex.IsMatch(check, @"\b(updates\.|workflow\.|ConfirmAndRunAsync|DataOperationLease\.Run|Dispatch|Execute|TryConfirm|ReviewedUpdateReview|LiveApprovedUpdate|ConfirmAsync)"),
            "the check and preparation start nothing");

        // The façade's compiled IL reaches the Native namespace only through the production installed-client check.
        string chocolatey = CompiledIlScanner.BuiltSrcAssemblies().Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        string[] nativeTargets = CompiledIlScanner.References(chocolatey).Where(reference => reference.LogicalCaller.StartsWith(Probe + ".", StringComparison.Ordinal) &&
                reference.TargetType.StartsWith(Native, StringComparison.Ordinal))
            .Select(reference => reference.TargetType + "." + reference.TargetMember + reference.TargetSignature).Distinct().ToArray();
        CollectionAssert.AreEquivalent(new[] { InstalledClientCheck + "(System.Threading.CancellationToken)" }, nativeTargets, string.Join(", ", nativeTargets));
    }

    /// <summary>
    /// The check has no dispatch path: neither the façade nor the installed-client check reaches the helper, its transport, the
    /// launcher, a held client binding, the N3 client queries, an update workflow, the review gate or a dispatcher, and the check
    /// observes only in the Desktop role. The route's non-admission pins are unchanged (see the class summary).
    /// </summary>
    [TestMethod]
    public void TheCheckHasNoDispatchPath()
    {
        string[] banned =
        [
            "PCSteward.Execution.", "PCSteward.ElevatedHelper.", "PCSteward.Application.ReviewedUpdateReview", "PCSteward.Application.ReviewedUpdateDraft",
            "PCSteward.Application.LiveApprovedUpdate", "PCSteward.Application.ReviewedUpdateDispatchWorkflow",
            "PCSteward.Application.ReviewedUpdateExecutionWorkflow", "PCSteward.Application.IReviewedUpdateRouteDispatcher",
            "PCSteward.Application.ReviewedUpdateDispatchTicket", "PCSteward.Application.IReviewedUpdateDispatchEvidenceSource",
            Native + "ChocolateyHeldClientBinding", Native + "ChocolateyHeldProfileFiles", Native + "ChocolateyClientQueries", Native + "LaunchJob",
            Native + "NativeLaunchSpec", Native + "NativeChocolateyHelperItem", Native + "ChocolateyReviewedRouteComposition",
            Native + "ChocolateyProfileEvidenceSource", Native + "ChocolateyReviewedUpdateVerifier", Native + "ChocolateyDispatchBaselines",
            "System.Diagnostics.Process",
        ];
        string chocolatey = CompiledIlScanner.BuiltSrcAssemblies().Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        List<string> violations = [];
        int scanned = 0;
        foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(chocolatey))
        {
            if (!(reference.LogicalCaller.StartsWith(Probe + ".", StringComparison.Ordinal) || reference.LogicalCaller == InstalledClientCheck)) continue;
            scanned++;
            if (banned.Any(prefix => reference.TargetType.StartsWith(prefix, StringComparison.Ordinal)))
                violations.Add($"{reference.LogicalCaller} references {reference.TargetType}.{reference.TargetMember}");
        }
        Assert.IsTrue(scanned > 10, $"only {scanned} references resolved");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        string facade = ProviderSource("ChocolateyInstalledClientProbe.cs");
        Assert.IsFalse(Regex.IsMatch(facade, @"\b(ChocolateyObserverRole|Helper|runas|Verb\s*=|UseShellExecute|Dispatch\w*|Confirm\w*)\b"),
            "the façade names no role, helper, elevation, dispatch or confirmation");
    }

    // Normalised to LF: Windows checkouts use CRLF, and the handler boundary below is matched on "\n    }\n".
    private static string Desktop(string file) =>
        DesktopWindowSource.ReadDesktopFile(CompiledIlScanner.FindRepositoryRoot(), file).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ApplicationSource(string file) =>
        File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Application", file)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ProviderSource(string file) =>
        File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey", file)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        int to = source.IndexOf(end, from + 1, StringComparison.Ordinal);
        Assert.IsTrue(from >= 0 && to > from, $"{start} .. {end} not found");
        return source[from..to];
    }
}
