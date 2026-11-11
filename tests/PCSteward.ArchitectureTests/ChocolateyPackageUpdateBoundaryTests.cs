using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UI-03/04, TM-03/04/07/13; Chocolatey package Confirm addendum §3–§5: the package Confirm path stays as narrow as
/// the client screen's.
/// <list type="bullet">
/// <item>C1 for packages is one compiled constant, read only through its property, that nothing configurable reaches; the session and
/// the view model read it once each, and their admission-taking factories have no <c>src</c> caller (the IL scan in
/// <see cref="ChocolateyClientUpdateDesktopUiBoundaryTests"/>);</item>
/// <item>the session freezes only the exact package shape from the checked intent, never a client identity, and is (with the client
/// session) the only confirm and run site;</item>
/// <item>the Desktop opens the package review only on Chocolatey Community rows and only while C1 is compiled in (otherwise today's
/// read-only disclosure), probes the split token and reads the trusted-sources setting inside the handler, prepares only when the
/// view model says so, binds the primary button to the view model, and refuses to close while a run is in flight.</item>
/// </list>
/// The composition's own boundaries are in <see cref="DesktopProducersBoundaryTests"/>, <see cref="DeploymentCapabilityBoundaryTests"/>
/// and <see cref="ReviewedRouteIntentResolverBoundaryTests"/>.
/// </summary>
[TestClass]
public sealed class ChocolateyPackageUpdateBoundaryTests
{
    [TestMethod]
    public void PackageAdmissionIsOneCompiledConstantReadOnceBySessionAndViewModel()
    {
        string admission = ApplicationSource("ChocolateyPackageUpdateAdmission.cs");
        Assert.AreEqual(1, Regex.Count(admission, @"public const bool OwnerApproved = (true|false);"), "one compiled definition");
        StringAssert.Contains(admission, "public static bool IsOwnerApproved => OwnerApproved;");
        Assert.IsFalse(Regex.IsMatch(admission, @"\b(Registry|Environment|File|Directory|Sqlite|Configuration|AppContext|GetEnvironmentVariable|static\s+bool\s+\w+\s*[;=]|set\s*;)\b"),
            "the admission reads no configuration, registry, environment, file or database value and has no mutable state");

        string session = ApplicationSource("ChocolateyPackageUpdateReviewSession.cs");
        StringAssert.Contains(session, ": this(ChocolateyPackageUpdateAdmission.IsOwnerApproved, checkIntent,");
        Assert.AreEqual(1, Regex.Count(session, @"\bIsOwnerApproved\b(?!""|</)"), "the public constructor passes the compiled C1");
        Assert.AreEqual(1, Regex.Count(session, @"\bthis\.admitted\s*=(?!=)"), "a single assignment of the admission");
        StringAssert.Contains(session, "if (admitted && IsSelectablePackage(packageId))");
        StringAssert.Contains(session, "if (!admitted) return ChocolateyClientUpdateRefusal.NotAdmitted;");
        Assert.IsFalse(Regex.IsMatch(session, @"\bChocolateyClientUpdateAdmission\b"), "the package session never reads the client admission");

        string model = ApplicationSource("ChocolateyPackageUpdateViewModel.cs");
        StringAssert.Contains(model, "OpenView(ChocolateyPackageUpdateAdmission.IsOwnerApproved, packageId, trustedSourcesEnabled, probeSplitToken);");
        Assert.AreEqual(1, Regex.Count(model, @"\bIsOwnerApproved\b(?!""|</)"), "the public factory only");
        Assert.IsFalse(Regex.IsMatch(model, @"\b(TryConfirm|ConfirmAsync|PrepareAsync|LiveApprovedUpdate|ReviewedUpdateDraft|ReviewedUpdateReview\b(?!\.))"),
            "the view model holds no confirm path, permit or draft");
    }

    [TestMethod]
    public void TheSessionFreezesOnlyTheExactPackageShapeFromTheCheckedIntent()
    {
        string session = ApplicationSource("ChocolateyPackageUpdateReviewSession.cs");
        string freeze = Between(session, "private ReviewedUpdateOperation? Freeze(ChocolateyReviewIntentFacts facts)", "\n    }\n");
        StringAssert.Contains(freeze, "UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3,");
        StringAssert.Contains(freeze, "frozen.ProviderGuardContract == ReviewedProviderGuardContract.CommunityScriptRuntimeLimits &&");
        StringAssert.Contains(freeze, "!ReviewedUpdateOperation.IsChocolateyClientIdentity(frozen.Selection.PackageId) &&");
        StringAssert.Contains(freeze, "ChocolateyPackageUpdateAdmission.AdmitsPackage(frozen.Selection.PackageId) && ReviewedUpdateReview.HasSupportedMode(frozen) &&");
        StringAssert.Contains(freeze, "ChocolateyReviewDisclosurePresentation.Present(frozen, facts, ReviewedRouteSplitTokenState.SplitTokenLimited).CanConfirm");
        Assert.IsFalse(freeze.Contains("ChocolateyClientSelfUpdateV1", StringComparison.Ordinal), "the package session never freezes the client route");
        // Versions, IDs and the requester come only from the checked intent (never a scan value).
        foreach (Match use in Regex.Matches(freeze, @"\b(InstalledVersion|CandidateVersion|PackageId|RequestingUserSid|ProfileEvidenceId)\b"))
        {
            int start = Math.Max(0, use.Index - 7);
            string prefix = freeze[start..use.Index];
            Assert.IsTrue(prefix.EndsWith("facts.", StringComparison.Ordinal) || prefix.EndsWith("ection.", StringComparison.Ordinal),
                $"{use.Value} is read from the checked intent or the frozen selection only");
        }
        StringAssert.Contains(session, "string.Equals(facts.PackageId, packageId, StringComparison.Ordinal) &&");
        StringAssert.Contains(session, "!string.IsNullOrWhiteSpace(packageId) && !ReviewedUpdateOperation.IsChocolateyClientIdentity(packageId) &&");
        StringAssert.Contains(session, "ChocolateyPackageUpdateAdmission.AdmitsPackage(packageId);");
        // C1a: exactly one compiled admitted package, the G9 test package, compared ordinally.
        string admission = ApplicationSource("ChocolateyPackageUpdateAdmission.cs");
        Assert.AreEqual(1, Regex.Count(admission, @"public const string AdmittedPackageId = ""ripgrep"";"), "only the G9 test package");
        StringAssert.Contains(admission, "public static bool AdmitsPackage(string? packageId) => string.Equals(packageId, AdmittedPackageId, StringComparison.Ordinal);");
        // One workflow run per accepted press, off the UI thread and never cancelled from the screen.
        Assert.AreEqual(1, Regex.Count(session, @"workflow\.RunAsync\("));
        StringAssert.Contains(session, "await Task.Run(() => workflow.RunAsync(approval, CancellationToken.None), CancellationToken.None)");
        Assert.AreEqual(1, Regex.Count(session, @"\.Confirm\(\)"), "one confirmation site");
    }

    [TestMethod]
    public void TheDesktopOpensThePackageReviewOnlyWhenAdmittedAndBindsEveryActionToTheViewModel()
    {
        string window = Desktop("MainWindow.cs");
        string dialog = Desktop("ChocolateyPackageUpdateReviewDialog.cs");
        string surfaces = Desktop("ChocolateyPackageUpdateSurfaces.cs");

        // Entry: Chocolatey Community rows only, only with C1 compiled in and a selectable package; otherwise the read-only disclosure.
        int admitted = window.IndexOf("if (ChocolateyReviewedRouteReviewViewModel.AppliesTo(detail?.SourceId) && ChocolateyPackageUpdateAdmission.IsOwnerApproved &&", StringComparison.Ordinal);
        int entry = window.IndexOf("ChocolateyPackageUpdateSurfaces.EntryButton(chocolateyPackageId)", StringComparison.Ordinal);
        int locked = window.IndexOf("else if (ChocolateyReviewedRouteReviewViewModel.AppliesTo(detail?.SourceId))", StringComparison.Ordinal);
        Assert.IsTrue(admitted >= 0 && entry > admitted && locked > entry, "the admitted entry is guarded, and the locked disclosure is its else branch");
        StringAssert.Contains(window, "detail?.PackageId is { } chocolateyPackageId && ChocolateyPackageUpdateReviewSession.IsSelectablePackage(chocolateyPackageId))");
        Assert.AreEqual(1, Regex.Count(window, @"ChocolateyPackageUpdateSurfaces\.EntryButton\("), "one entry point");
        Assert.AreEqual(1, Regex.Count(window, @"await ShowChocolateyPackageUpdateAsync\("));

        // Opened (and so probed, with the trusted-sources setting read now) inside the handler; never cached.
        string handler = Between(window, "private async Task ShowChocolateyPackageUpdateAsync(string packageId)", "\n    }\n");
        StringAssert.Contains(handler, "ChocolateyPackageUpdateViewModel OpenChecking() => ChocolateyPackageUpdateViewModel.Open(packageId, viewModel.IsWinGetTrustedSourcesEnabled, probeReviewedRouteSplitToken);");
        Assert.AreEqual(1, Regex.Count(window, @"ChocolateyPackageUpdateViewModel\.Open\("));
        Assert.IsFalse(Regex.IsMatch(window, @"private\s+(readonly\s+)?ChocolateyPackageUpdateViewModel\b"), "no cached review");
        Assert.IsFalse(Regex.IsMatch(window, @"private\s+(readonly\s+)?ChocolateyPackageUpdateReviewSession\b"), "no cached session");
        StringAssert.Contains(window, "this.createChocolateyPackageUpdateSession = createChocolateyPackageUpdateSession ?? throw new ArgumentNullException(nameof(createChocolateyPackageUpdateSession));");
        StringAssert.Contains(handler, "OpenChecking, createChocolateyPackageUpdateSession,");

        // The driver prepares only when the view model says so, and confirms only through the session.
        StringAssert.Contains(dialog, "if (!current.ShouldPrepare) return;");
        StringAssert.Contains(dialog, "else if (current.CanConfirm && session is { } confirmed) _ = RunAsync(confirmed);");
        StringAssert.Contains(dialog, "ChocolateyClientUpdateRunResult run = await confirmed.ConfirmAsync(progress, CancellationToken.None);");
        Assert.AreEqual(1, Regex.Count(dialog, @"\.ConfirmAsync\("));
        Assert.AreEqual(1, Regex.Count(dialog, @"\.PrepareAsync\("));
        Assert.AreEqual(1, Regex.Count(dialog, @"createSession\(\)"), "one session per check");
        Assert.IsFalse(Regex.IsMatch(dialog, @"IsPrimaryButtonEnabled\s*=|PrimaryButtonText\s*="), "the driver never enables the primary button itself");

        // Close guards: the dialog's Closing and the main window's close are refused while a run is in flight.
        StringAssert.Contains(dialog, "if (running) args.Cancel = true;");
        StringAssert.Contains(window, "activeChocolateyPackageUpdate?.IsRunning == true) args.Cancel = true;");
        string progress = Between(surfaces, "internal static void RenderProgress(", "\n    }\n");
        StringAssert.Contains(progress, "dialog.CloseButtonText = string.Empty;");
        StringAssert.Contains(progress, "dialog.PrimaryButtonText = string.Empty;");
        StringAssert.Contains(Between(surfaces, "internal static void RenderResult(", "\n    }\n"), "dialog.CloseButtonText = ChocolateyPackageUpdateViewModel.CloseText;");

        // Thin rendering: every line, then the primary button from the view model only; Close is the default button.
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in model.Fields)");
        StringAssert.Contains(surfaces, "foreach (ReviewedDisplayField field in model.UnavailableFields)");
        StringAssert.Contains(surfaces, "dialog.PrimaryButtonText = model.PrimaryText;");
        StringAssert.Contains(surfaces, "dialog.IsPrimaryButtonEnabled = model.IsPrimaryActionEnabled;");
        StringAssert.Contains(surfaces, "DefaultButton = ContentDialogButton.Close,");
        Assert.IsFalse(Regex.IsMatch(surfaces, @"\b(TryConfirm|ConfirmAsync|PrepareAsync|Click|DataOperationLease|ReviewedUpdateReview)\b"), "the surfaces hold no action");
    }

    private static string Desktop(string file) =>
        DesktopWindowSource.ReadDesktopFile(CompiledIlScanner.FindRepositoryRoot(), file).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ApplicationSource(string file) =>
        File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Application", file)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        int to = source.IndexOf(end, from + 1, StringComparison.Ordinal);
        Assert.IsTrue(from >= 0 && to > from, $"{start} .. {end} not found");
        return source[from..to];
    }
}
