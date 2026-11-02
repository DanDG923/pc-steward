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
/// to <c>IsConfirmEnabled</c>, and its handler never calls <c>ConfirmAndRunAsync</c>, a workflow or a dispatcher.</item>
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
        StringAssert.Contains(Desktop("App.xaml.cs"), "ChocolateyReviewedRouteProbe.ProbeSplitTokenState, ChocolateyClientReleaseCatalog.Current, showPostResetOnboarding");

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
        StringAssert.Contains(handler, "ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateView(probeReviewedRouteSplitToken);");
        StringAssert.Contains(handler, "ChocolateyClientUpdateSurfaces.ReviewDialog(Content.XamlRoot, model);");
        StringAssert.Contains(handler, "await ShowUpdateOutcomeAsync(\"Update not started\", ChocolateyClientUpdateViewModel.NotStartedText);");
        Assert.AreEqual(1, Regex.Count(window, @"ChocolateyClientUpdateViewModel\.Open\("), "one view-model construction site");
        Assert.AreEqual(1, Regex.Count(window, @"ChocolateyClientUpdateView\(probeReviewedRouteSplitToken\)"), "only the review probes");
        StringAssert.Contains(region, "probe ?? (() => ReviewedRouteSplitTokenState.Unknown)");
        Assert.IsFalse(Regex.IsMatch(window, @"private\s+(readonly\s+)?ChocolateyClientUpdateViewModel\s+\w+\s*(;|=[^>])"), "no cached view model");

        // Nothing in the entry, view or handler starts, confirms or dispatches anything, or re-enables Confirm.
        Assert.IsFalse(Regex.IsMatch(region, @"\b(updates\.|workflow\.|ConfirmAndRunAsync|DataOperationLease\.Run|Dispatch|Execute|TryConfirm|ReviewedUpdateReview|LiveApprovedUpdate|IsPrimaryButtonEnabled|PrimaryButtonText)"),
            "the client update surfaces start nothing");

        // Thin rendering: every line, then Confirm bound to IsConfirmEnabled only.
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in model.Fields)");
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in model.DisclosureFields)");
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in model.UnavailableFields)");
        StringAssert.Contains(surfaces, "IsPrimaryButtonEnabled = model.IsConfirmEnabled,");
        Assert.AreEqual(1, Regex.Count(surfaces, @"IsPrimaryButtonEnabled\s*="), "Confirm is enabled only from the view model");
        StringAssert.Contains(surfaces, "DefaultButton = ContentDialogButton.Close,");
        Assert.IsFalse(Regex.IsMatch(surfaces, @"\b(TryConfirm|Click|DataOperationLease|ReviewedUpdateReview|ConfirmAndRunAsync|Dispatch\w*|LiveApprovedUpdate)\b"),
            "the surfaces hold no action");

        // Accessibility (Phase 5 helpers, AC-UX best effort): level-3 headings, label-in-name entry, polite live summary, named lines.
        StringAssert.Contains(surfaces, "StewardVisuals.SectionHeading(text, AutomationHeadingLevel.Level3)");
        StringAssert.Contains(surfaces, "AutomationProperties.SetName(entry, model.EntryAutomationName);");
        StringAssert.Contains(surfaces, "AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);");
        StringAssert.Contains(surfaces, "AutomationProperties.SetName(line, field.AutomationName);");
        StringAssert.Contains(surfaces, "AutomationProperties.SetHelpText(dialog, model.ConfirmAutomationName);");
    }

    // Normalised to LF: Windows checkouts use CRLF, and the handler boundary below is matched on "\n    }\n".
    private static string Desktop(string file) =>
        File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Desktop", file)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        int to = source.IndexOf(end, from + 1, StringComparison.Ordinal);
        Assert.IsTrue(from >= 0 && to > from, $"{start} .. {end} not found");
        return source[from..to];
    }
}
