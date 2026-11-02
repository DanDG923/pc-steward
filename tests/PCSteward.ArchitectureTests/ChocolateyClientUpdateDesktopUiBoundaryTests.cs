using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UI-03/04, TM-03/04/07/13; ADR-031 (owner decisions 1–5), ADR-030: the Desktop's "Update Chocolatey to X"
/// surfaces stay thin, read-only and non-dispatching while <c>ChocolateyClientSelfUpdateV1</c> is not admitted.
/// <list type="bullet">
/// <item>the only new Desktop reach into the provider is the read-only release catalog, composed once in App.xaml.cs, whose
/// compiled IL reads only the compiled list's versions;</item>
/// <item>the view model holds no review, draft, permit, ticket, dispatcher or workflow and always carries the not-admitted
/// reason, so <c>CanConfirm</c> is false;</item>
/// <item>the review is opened (and so probed) once per click inside its handler, renders every view-model line, binds Confirm
/// to <c>IsConfirmEnabled</c>, and its handler never calls <c>ConfirmAndRunAsync</c>, a workflow or a dispatcher;</item>
/// <item>design §8.6 gate 3: the offer is computed only from the verified installed-client check, whose single carrier of a
/// release (<c>ChocolateyInstalledClientVerification.Verified</c>) is called only by the native Desktop-role observation; the
/// Desktop reaches it only through the public façade composed in App.xaml.cs, never the Native namespace, and runs it only
/// inside the review handler, off the UI thread, bounded and cancelled when the dialog closes; the check reaches no helper,
/// launcher, held binding, workflow or dispatcher.</item>
/// </list>
/// The non-admission pins themselves (<c>HasSupportedMode</c>, <c>CapabilityOperationAllowlist</c>, the dispatcher's
/// <c>Supports</c>) are in <see cref="ClientRunFaultBoundaryTests.NothingInSrcAdmitsTheClientSelfUpdateRoute"/> and
/// <see cref="DesktopProducersBoundaryTests"/> and are unchanged.
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
            "ChocolateyReviewedRouteProbe.ProbeSplitTokenState, ChocolateyClientReleaseCatalogFacts.ReadOrUnavailable(() => ChocolateyClientReleaseCatalog.Current), ChocolateyInstalledClientProbe.ObserveAsync, showPostResetOnboarding");

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
    public void TheViewModelHasNoConfirmPermitOrDispatchPathAndIsAlwaysUnavailable()
    {
        string application = CompiledIlScanner.BuiltSrcAssemblies().Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Application");
        CompiledIlScanner.IlReference[] references = CompiledIlScanner.References(application).Where(reference => reference.Caller == ViewModel).ToArray();
        Assert.IsTrue(references.Length > 0, "the view model was not found in the built assembly");
        string[] forbidden = references.Where(reference => ForbiddenViewModelTargets.Contains(reference.TargetType))
            .Select(reference => reference.TargetType + "." + reference.TargetMember).Distinct().ToArray();
        Assert.AreEqual(0, forbidden.Length, string.Join(", ", forbidden));

        string source = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Application", "ChocolateyClientUpdateViewModel.cs"));
        StringAssert.Contains(source, "List<ChocolateyClientUpdateUnavailableReason> reasons = [ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview];");
        StringAssert.Contains(source, "public bool CanConfirm => UnavailableReasons.Count == 0;");
        StringAssert.Contains(source, "public bool IsConfirmEnabled => CanConfirm;");
        Assert.IsFalse(Regex.IsMatch(source, @"\b(TryConfirm|ConfirmAndRunAsync|LiveApprovedUpdate|ReviewedUpdateDraft)\b"), "no confirm path or permit");
        Assert.IsFalse(Regex.IsMatch(source, @"NotAdmittedPendingReview\s*\)\s*;?\s*\n\s*reasons\.Remove|reasons\.Clear\("), "the not-admitted reason is never removed");
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
        string region = Between(window, "private ChocolateyClientUpdateViewModel ChocolateyClientUpdateView(", "private async Task ShowDetailsAsync");
        string handler = Between(region, "private async Task ShowChocolateyClientUpdateAsync()", "\n    }\n");
        StringAssert.Contains(handler, "ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateView(probeReviewedRouteSplitToken, ChocolateyInstalledClientVerification.Checking);");
        StringAssert.Contains(handler, "ChocolateyClientUpdateSurfaces.ReviewDialog(Content.XamlRoot, model);");
        StringAssert.Contains(handler, "await ShowUpdateOutcomeAsync(\"Update not started\", ChocolateyClientUpdateViewModel.NotStartedText);");
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
        StringAssert.Contains(surfaces, "dialog.IsPrimaryButtonEnabled = model.IsConfirmEnabled;");
        Assert.AreEqual(1, Regex.Count(surfaces, @"IsPrimaryButtonEnabled\s*="), "Confirm is enabled only from the view model");
        StringAssert.Contains(surfaces, "DefaultButton = ContentDialogButton.Close,");
        Assert.IsFalse(Regex.IsMatch(surfaces, @"\b(TryConfirm|Click|DataOperationLease|ReviewedUpdateReview|ConfirmAndRunAsync|Dispatch\w*|LiveApprovedUpdate)\b"),
            "the surfaces hold no action");

        // Accessibility (Phase 5 helpers, AC-UX best effort): level-3 headings, label-in-name entry, named lines. The dialog's
        // summary is static text, so it is not a live region (independent review Info).
        StringAssert.Contains(surfaces, "StewardVisuals.SectionHeading(text, AutomationHeadingLevel.Level3)");
        StringAssert.Contains(surfaces, "AutomationProperties.SetName(entry, model.EntryAutomationName);");
        Assert.IsFalse(surfaces.Contains("SetLiveSetting", StringComparison.Ordinal), "no live region on the static summary");
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
        string all = string.Concat(files.Select(File.ReadAllText));
        Assert.AreEqual(1, Regex.Count(all, @"\bChocolateyInstalledClientProbe\b"), "one composition site of the façade");
        Assert.AreEqual(0, Regex.Count(all, @"ChocolateyInstalledClientProbe\.ObserveAsync\("), "composed as a method group, never invoked at startup");
        Assert.IsFalse(Regex.IsMatch(all, @"\bChocolateyDesktopObservation\b|\bChocolateyProfileObserver\b"), "the Desktop names no native observation");

        string window = Desktop("MainWindow.cs");
        Assert.AreEqual(1, Regex.Count(window, @"\bcheckInstalledChocolateyClient\("), "the check runs at one site");
        string check = Between(window, "private async Task CheckInstalledChocolateyClientAsync(", "\n    }\n");
        StringAssert.Contains(check, "ChocolateyInstalledClientVerification.RunAsync(");
        StringAssert.Contains(check, "token => Task.Run(() => checkInstalledChocolateyClient(token), CancellationToken.None)");
        StringAssert.Contains(check, "ChocolateyInstalledClientVerification.DisplayCheckTimeout, cancellationToken);");
        StringAssert.Contains(check, "if (cancellationToken.IsCancellationRequested || windowClosed) return;");
        StringAssert.Contains(check, "ChocolateyClientUpdateSurfaces.Render(dialog, opened.WithVerification(verification));");
        string handler = Between(window, "private async Task ShowChocolateyClientUpdateAsync()", "\n    }\n");
        StringAssert.Contains(handler, "Task checking = CheckInstalledChocolateyClientAsync(dialog, model, check.Token);");
        StringAssert.Contains(handler, "dialog.Closed += CancelCheck;");
        StringAssert.Contains(handler, "check.Cancel();");
        Assert.AreEqual(1, Regex.Count(window, @"CheckInstalledChocolateyClientAsync\(dialog"), "only the review handler starts the check");
        Assert.IsFalse(Regex.IsMatch(check + handler, @"\b(updates\.|workflow\.|ConfirmAndRunAsync|DataOperationLease\.Run|Dispatch|Execute|TryConfirm|ReviewedUpdateReview|LiveApprovedUpdate)"),
            "the check starts nothing");

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
        File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Desktop", file)).Replace("\r\n", "\n", StringComparison.Ordinal);

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
