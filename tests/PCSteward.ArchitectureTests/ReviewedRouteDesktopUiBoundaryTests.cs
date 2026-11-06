using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, FR-HIS-01, FR-UI-03/04, TM-03/04/07/13; Task 50 admission obligation 5 (N6 review M1): the Desktop's
/// review and history surfaces for the runtime-limit Chocolatey route stay thin, read-only and non-dispatching.
/// <list type="bullet">
/// <item>the Desktop reaches the provider only through the read-only split-token probe, which references only
/// <c>ProbeSplitTokenState</c>; it names no producer, composition, resolver, dispatch workflow or route dispatcher;</item>
/// <item>the review is opened from a Chocolatey Community row only, probes afresh on every review, renders every view-model
/// line, binds Confirm to <c>IsConfirmEnabled</c>, and its confirm handler only calls <c>TryConfirm</c>;</item>
/// <item>history is read through <c>IReviewedUpdateHistoryReader</c> and renders every history line.</item>
/// </list>
/// The route's non-admission pins (<c>DesktopProducersBoundaryTests</c>, <c>DeploymentCapabilityBoundaryTests</c>,
/// <c>HelperEndpointBoundaryTests</c>) are unchanged.
/// </summary>
[TestClass]
public sealed class ReviewedRouteDesktopUiBoundaryTests
{
    private const string Probe = "PCSteward.Provider.Chocolatey.ChocolateyReviewedRouteProbe";
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private static readonly string[] ProbeNativeTargets = [Native + "ChocolateyReviewedRouteComposition.ProbeSplitTokenState"];

    [TestMethod]
    public void TheDesktopReachesTheRouteOnlyThroughTheReadOnlySplitTokenProbe()
    {
        string desktop = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Desktop");
        string[] files = Directory.EnumerateFiles(desktop, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToArray();
        Assert.IsTrue(files.Length >= 5, "Desktop sources were not found");
        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            Assert.IsFalse(Regex.IsMatch(source, @"\b(ChocolateyReviewedRouteComposition|ChocolateyReviewIntentResolver|ChocolateyProfileEvidenceSource|ChocolateyReviewedUpdateVerifier|ChocolateyDispatchBaselines|ReviewedUpdateDispatchWorkflow|ChocolateyRuntimeLimitsRouteDispatcher|IReviewedUpdateRouteDispatcher|LiveApprovedUpdate)\b"),
                $"{Path.GetFileName(file)} names a route producer, composition, resolver, dispatch workflow, dispatcher or permit");
            Assert.IsFalse(source.Contains("PCSteward.Provider.Chocolatey.Native", StringComparison.Ordinal), $"{Path.GetFileName(file)} names the native layer");
        }

        string app = File.ReadAllText(Path.Combine(desktop, "App.xaml.cs"));
        StringAssert.Contains(app, "database, ChocolateyReviewedRouteProbe.ProbeSplitTokenState, showPostResetOnboarding");
        Assert.AreEqual(1, Regex.Count(string.Concat(files.Select(File.ReadAllText)), @"\bChocolateyReviewedRouteProbe\b"), "one composition site of the probe");

        // The probe references only the composition's static split-token probe in the native layer, never its constructor.
        string chocolatey = CompiledIlScanner.BuiltSrcAssemblies().Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        string[] nativeTargets = CompiledIlScanner.References(chocolatey)
            .Where(reference => reference.Caller == Probe && reference.TargetType.StartsWith(Native, StringComparison.Ordinal))
            .Select(reference => reference.TargetType + "." + reference.TargetMember).Distinct().ToArray();
        CollectionAssert.AreEquivalent(ProbeNativeTargets, nativeTargets, string.Join(", ", nativeTargets));
    }

    [TestMethod]
    public void TheReviewIsReadOnlyProbesAfreshAndBindsConfirmToTheViewModel()
    {
        string window = Desktop("MainWindow.cs");
        string surfaces = Desktop("ReviewedRouteSurfaces.cs");

        // Entry only on Chocolatey Community rows.
        int guard = window.IndexOf("if (ChocolateyReviewedRouteReviewViewModel.AppliesTo(detail?.SourceId))", StringComparison.Ordinal);
        int entry = window.IndexOf("ReviewedRouteSurfaces.ChocolateyReviewEntryButton()", StringComparison.Ordinal);
        Assert.IsTrue(guard >= 0 && entry > guard, "the entry point is guarded by the Chocolatey Community source");
        Assert.AreEqual(1, Regex.Count(window, @"ReviewedRouteSurfaces\.ChocolateyReviewEntryButton\(\)"));

        // Opened (and so probed) once per review, inside the handler; never cached in a field.
        string handler = Between(window, "private async Task ShowChocolateyReviewedRouteAsync()", "private async Task ShowDetailsAsync");
        StringAssert.Contains(handler, "ChocolateyReviewedRouteReviewViewModel review = ChocolateyReviewedRouteReviewViewModel.Open(null, null, probeReviewedRouteSplitToken);");
        Assert.AreEqual(1, Regex.Count(window, @"ChocolateyReviewedRouteReviewViewModel\.Open\("), "one review entry");
        Assert.IsFalse(Regex.IsMatch(window, @"private\s+(readonly\s+)?ChocolateyReviewedRouteReviewViewModel"), "no cached review");
        Assert.IsFalse(Regex.IsMatch(window, @"private\s+(readonly\s+)?ReviewedRouteSplitTokenState\b"), "no cached split-token state");

        // The confirm handler only goes through the view model's recheck and starts nothing.
        StringAssert.Contains(handler, "_ = review.TryConfirm();");
        Assert.IsFalse(Regex.IsMatch(handler, @"\b(updates\.|workflow\.|ConfirmAndRunAsync|DataOperationLease\.Run|Dispatch|Execute)"), "the review handler starts nothing");

        // Thin rendering: every line, then Confirm bound to IsConfirmEnabled.
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in model.Fields)");
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in model.UnavailableFields)");
        StringAssert.Contains(surfaces, "IsPrimaryButtonEnabled = model.IsConfirmEnabled,");
        Assert.AreEqual(1, Regex.Count(surfaces, @"IsPrimaryButtonEnabled\s*="), "Confirm is enabled only from the view model");
        Assert.IsFalse(Regex.IsMatch(handler, @"IsPrimaryButtonEnabled|PrimaryButtonText"), "the handler never re-enables Confirm");
        Assert.IsFalse(Regex.IsMatch(surfaces, @"\b(TryConfirm|Click|DataOperationLease|ReviewedUpdateReview)\b"), "the surfaces hold no action");
    }

    [TestMethod]
    public void HistoryIsReadThroughTheReaderAndRendersEveryLine()
    {
        string window = Desktop("MainWindow.cs");
        string surfaces = Desktop("ReviewedRouteSurfaces.cs");
        StringAssert.Contains(window, "private readonly IReviewedUpdateHistoryReader reviewedHistory;");
        StringAssert.Contains(window, "DataOperationLease.RunReadAsync(token => ReviewedUpdateHistoryListViewModel.LoadAsync(reviewedHistory, token), CancellationToken.None)");
        StringAssert.Contains(window, "ReviewedRouteSurfaces.RenderHistory(outcomes, history);");
        StringAssert.Contains(surfaces, "foreach (ReviewedUpdateHistoryItemViewModel item in model.Items)");
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in item.Fields)");
        string activity = Between(window, "private void RenderActivityPage()", "private void RenderPrivacyPage()");
        StringAssert.Contains(activity, "AddReviewedUpdateOutcomesSection(panel);");
    }

    private static string Desktop(string file) => File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Desktop", file));

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        int to = source.IndexOf(end, from + 1, StringComparison.Ordinal);
        Assert.IsTrue(from >= 0 && to > from, $"{start} .. {end} not found");
        return source[from..to];
    }
}
