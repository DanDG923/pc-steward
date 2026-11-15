using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// Task 71 (ADR-029; docs/review/TASK_69_AI_EVIDENCE_GPT_6_1_SOL.md §10 item 2): the one-request AI test build stays off the
/// release path. Only its own build script sets the property, only the Domain's policy file reads the symbol, the release
/// workflows never name either, and the build is made on demand from phase/4-beta with a read-only token. In the test build the
/// window refuses app updates, the scheduled check, data clears, factory reset and self-update, leaves a pending reset alone at
/// start-up, composes no elevated runner, and says what it is.
/// </summary>
[TestClass]
public sealed class AdvisoryAiFirstLiveTestBuildBoundaryTests
{
    private const string Property = "PCStewardAiFirstLiveTest";
    private const string Symbol = "PCSTEWARD_AI_FIRST_LIVE_TEST";
    private const string Guard = "PCSteward.Domain.AdvisoryAiFirstLiveTestBuild.IsTestBuild";

    /// <summary>The files that may name the build property: the props that read it, the script that sets it, the candidate builder's
    /// refusal and the policy's own documentation.</summary>
    private static readonly string[] PropertyFiles =
        ["Directory.Build.props", "scripts/New-AdvisoryAiFirstLiveTestBuild.ps1", "scripts/New-OwnerOnlyCandidate.ps1", "src/PCSteward.Domain/AdvisoryAiPolicy.cs"];

    private static readonly string[] SymbolFiles = ["src/PCSteward.Domain/AdvisoryAiPolicy.cs"];

    private static readonly string[] ProductFolders = ["src", "scripts", ".github"];

    [TestMethod]
    public void OnlyTheTestBuildScriptSetsThePropertyAndOnlyThePolicyFileReadsTheSymbol()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        string[] mentions = [.. ProductAndBuildFiles(root).Where(path => File.ReadAllText(path).Contains(Property, StringComparison.Ordinal)).Select(path => Relative(root, path))];
        CollectionAssert.AreEquivalent(PropertyFiles, mentions, string.Join("\n", mentions));
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, "scripts", "New-AdvisoryAiFirstLiveTestBuild.ps1")), "$property = '-p:PCStewardAiFirstLiveTest=true'");
        Assert.AreEqual(1, Regex.Count(File.ReadAllText(Path.Combine(root, "scripts", "New-OwnerOnlyCandidate.ps1")), Property), "the candidate builder only names it in its refusal");

        string props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        StringAssert.Contains(props, "<PropertyGroup Condition=\"'$(PCStewardAiFirstLiveTest)' == 'true'\">");
        StringAssert.Contains(props, "<DefineConstants>$(DefineConstants);PCSTEWARD_AI_FIRST_LIVE_TEST</DefineConstants>");

        string[] symbolReaders = [.. Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path) && File.ReadAllText(path).Contains(Symbol, StringComparison.Ordinal)).Select(path => Relative(root, path))];
        CollectionAssert.AreEqual(SymbolFiles, symbolReaders, string.Join("\n", symbolReaders));
    }

    [TestMethod]
    public void TheReleaseWorkflowsNeverBuildTheTestBuildAndItsOwnWorkflowIsOnDemandFromPhase4BetaOnly()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        string workflows = Path.Combine(root, ".github", "workflows");
        foreach (string release in new[] { "release-payload.yml", "publish-release.yml" })
        {
            string text = File.ReadAllText(Path.Combine(workflows, release));
            Assert.IsFalse(text.Contains("New-AdvisoryAiFirstLiveTestBuild", StringComparison.Ordinal), release);
            Assert.IsFalse(text.Contains("ai-first-live-test", StringComparison.OrdinalIgnoreCase), release);
        }

        string build = File.ReadAllText(Path.Combine(workflows, "ai-first-live-test-build.yml"));
        string nl = NewLine(build);
        StringAssert.Contains(build, "on:" + nl + "  workflow_dispatch:" + nl);
        Assert.IsFalse(Regex.IsMatch(build, @"^\s*(push|pull_request|pull_request_target|schedule|workflow_run|release):", RegexOptions.Multiline), "on demand only");
        StringAssert.Contains(build, "if ($env:REF_TYPE -cne 'branch' -or $env:REF -cne 'refs/heads/phase/4-beta') { throw ");
        StringAssert.Contains(build, "permissions:" + nl + "  contents: read");
        Assert.AreEqual(1, Regex.Count(build, @"permissions:"), "no job asks for more than a read-only token");
        Assert.IsFalse(build.Contains("secrets.", StringComparison.Ordinal), "no secret reaches the test build");
        StringAssert.Contains(build, "run: pwsh -NoProfile -File ./scripts/New-AdvisoryAiFirstLiveTestBuild.ps1");
        StringAssert.Contains(build, "retention-days: 7");
        StringAssert.Contains(build, "include-hidden-files: true", "the upload carries exactly the folder the manifest lists");

        // The PR check builds it and runs its AI suites, and uploads nothing.
        string ci = File.ReadAllText(Path.Combine(workflows, "ci.yml"));
        int checkStart = ci.IndexOf("  ai-first-live-test-build-check:", StringComparison.Ordinal);
        string check = ci[checkStart..ci.IndexOf("  package-licence-map:", checkStart, StringComparison.Ordinal)];
        StringAssert.Contains(check, "run: pwsh -NoProfile -File ./scripts/New-AdvisoryAiFirstLiveTestBuild.ps1");
        Assert.IsFalse(check.Contains("upload-artifact", StringComparison.Ordinal), "the PR check uploads no test build");

        string script = File.ReadAllText(Path.Combine(root, "scripts", "New-AdvisoryAiFirstLiveTestBuild.ps1"));
        StringAssert.Contains(script, "if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw ");
        StringAssert.Contains(script, "if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) { throw ");
        StringAssert.Contains(script, "if ($domainText.IndexOf($mark + [char]0, [StringComparison]::Ordinal) -lt 0) { throw ");
        StringAssert.Contains(script, "foreach ($leaf in @('PCSteward.ElevatedHelper.exe', 'PCSteward.ElevatedHelper.dll'))");
        StringAssert.Contains(script, "foreach ($file in Get-ChildItem -LiteralPath $app -Recurse -File -Force) {");
        StringAssert.Contains(script, "if ($uploaded.Count -ne $files.Count + 1) { throw ");
        foreach (string forbidden in new[] { "signtool", "New-PCStewardMsi", "Publish-PCStewardRelease", "New-PCStewardReleaseCheck", "Invoke-WebRequest", "Invoke-RestMethod", "OPENAI_API_KEY" })
            Assert.IsFalse(script.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
    }

    [TestMethod]
    public void TheTestBuildRefusesEveryActionThatChangesThePcItsScheduleItsDataOrItself()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        string Desktop(string name) => File.ReadAllText(Path.Combine(root, "src", "PCSteward.Desktop", name));

        string update = Desktop("MainWindow.UpdateJob.cs");
        string review = update[update.IndexOf("private async Task ReviewAndExecuteAsync(IReadOnlyList<InventoryRow> rows)", StringComparison.Ordinal)..];
        Assert.IsTrue(review.IndexOf(Guard, StringComparison.Ordinal) is > 0 and var reviewGuard && reviewGuard < review.IndexOf("updates.BeginBatchReviewAsync", StringComparison.Ordinal),
            "the update review is refused before anything is prepared");

        string chocolatey = Desktop("MainWindow.Chocolatey.cs");
        foreach (string method in new[] { "private async Task ShowChocolateyPackageUpdateAsync(string packageId)", "private async Task ShowChocolateyClientUpdateAsync()" })
        {
            string body = chocolatey[chocolatey.IndexOf(method, StringComparison.Ordinal)..];
            Assert.IsTrue(body.IndexOf(Guard, StringComparison.Ordinal) is > 0 and var dialogGuard && dialogGuard < body.IndexOf("ReviewDialog(Content.XamlRoot, model)", StringComparison.Ordinal), method);
        }

        string settings = Desktop("MainWindow.Settings.cs");
        Assert.IsTrue(settings.IndexOf(Guard, StringComparison.Ordinal) is > 0 and var scheduleGuard && scheduleGuard < settings.IndexOf("await ApplyScheduleAsync(requested);", StringComparison.Ordinal),
            "the schedule is never saved or registered");

        string reset = Desktop("MainWindow.DataAndReset.cs");
        string arm = reset[reset.IndexOf("private async Task ConfirmAndArmFactoryResetAsync(Button invoker)", StringComparison.Ordinal)..];
        Assert.IsTrue(arm.IndexOf(Guard, StringComparison.Ordinal) is > 0 and var resetGuard && resetGuard < arm.IndexOf("FactoryReset.Arm()", StringComparison.Ordinal), "factory reset is never armed");
        string clear = reset[reset.IndexOf("private Button DataActionButton(DataActionPresentation action)", StringComparison.Ordinal)..];
        Assert.IsTrue(clear.IndexOf(Guard, StringComparison.Ordinal) is > 0 and var clearGuard && clearGuard < clear.IndexOf("switch (action.Action)", StringComparison.Ordinal),
            "no data-clear button is wired to its operation (T71-02)");

        // T71-01: start-up leaves a reset the installed copy started alone. The reset step is the only start-up caller of reset
        // recovery, and it is told it runs in the test build; its behaviour is proven by AdvisoryAiFirstLiveTestStartupResetTests.
        string app = Desktop("App.xaml.cs");
        StringAssert.Contains(app, "FactoryReset.RecoverAtStartup(scheduleRegistrar.EnsureAbsentAsync, RemoveAdvisoryAiKeysAsync, " + Guard + ", stateDirectory);");
        Assert.IsTrue(app.IndexOf("FactoryReset.RecoverAtStartup(", StringComparison.Ordinal) < app.IndexOf("database.MigrateAsync(", StringComparison.Ordinal), "before any migration");
        foreach (string recovery in new[] { "FactoryReset.CompletePendingAsync(", "FactoryReset.VerifyOnboardingBootstrap(" })
            Assert.IsFalse(Directory.EnumerateFiles(Path.Combine(root, "src", "PCSteward.Desktop"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path)).Any(path => File.ReadAllText(path).Contains(recovery, StringComparison.Ordinal)), recovery);

        string self = Desktop("MainWindow.SelfUpdate.cs");
        StringAssert.Contains(self, "selfUpdater = " + Guard + " ? null : PcStewardSelfUpdate.TryCreate();");
        string check = self[self.IndexOf("private async Task CheckSelfUpdateAsync()", StringComparison.Ordinal)..];
        Assert.IsTrue(check.IndexOf(Guard, StringComparison.Ordinal) is > 0 and var checkGuard && checkGuard < check.IndexOf("GitHubReleaseClient client = new()", StringComparison.Ordinal), "no release check");

        StringAssert.Contains(Desktop("App.xaml.cs"), Guard + " ? null : WingetMachineUpdateComposition.CreateElevatedRunner());");
        StringAssert.Contains(Desktop("AppIdentity.cs"), "public static string WindowName => " + Guard + " ? DisplayName + \" — AI test build, not a release\" : DisplayName;");
        StringAssert.Contains(Desktop("MainWindow.cs"), "Title = AppIdentity.WindowName;");
    }

    private static IEnumerable<string> ProductAndBuildFiles(string root) =>
        ProductFolders.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(root, "*.props")).Concat(Directory.EnumerateFiles(root, "*.targets")).Concat(Directory.EnumerateFiles(root, "*.rsp"))
            .Where(path => !IsBuildOutput(path));

    private static bool IsBuildOutput(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string NewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
