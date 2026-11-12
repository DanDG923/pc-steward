using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03, AC-UPD-03, TM-03/04/13. Stage C4 (Task 54 R1): the desktop composes per-user WinGet updates only through
/// the reviewed executor, which builds its arguments from the reviewed operation alone, and the legacy mode-less user builder
/// (<c>WingetUpdateExecutor.BuildUpdateArguments(ImmutableUpdateJob)</c>) is not reachable from <c>App.xaml.cs</c>: it no longer
/// exists in source or in any built <c>src</c> assembly. The only remaining job-based WinGet builder is the machine-scope one used
/// by protocol 1 under the ADR-025 transition amendment, and it refuses user scope.
/// </summary>
[TestClass]
public sealed class WingetUserReviewedRouteCompositionTests
{
    private const string LegacyUserExecutor = "PCSteward.Provider.WinGet.WingetUpdateExecutor";
    private const string ReviewedUserExecutor = "PCSteward.Provider.WinGet.WingetReviewedUserUpdateExecutor";

    [TestMethod]
    [TestCategory("FR-UPD-15")]
    public void TheDesktopComposesOnlyTheReviewedPerUserExecutor()
    {
        string app = Code(Read("src", "PCSteward.Desktop", "App.xaml.cs"));
        Assert.AreEqual(1, Regex.Count(app, @"new\s+NonElevatedUpdateRunner\s*\(\s*new\s+WingetReviewedUserUpdateExecutor\s*\(\s*\)\s*\)"),
            "the desktop's one non-elevated runner is composed over the reviewed per-user executor");
        Assert.AreEqual(1, Regex.Count(app, @"new\s+NonElevatedUpdateRunner\s*\("), "exactly one non-elevated runner");
        Assert.IsFalse(Regex.IsMatch(app, @"\bWingetUpdateExecutor\b"), "the legacy user executor is never composed");
        Assert.IsFalse(Regex.IsMatch(app, @"\bBuildUpdateArguments\b"), "the desktop never builds provider arguments itself");
        StringAssert.Contains(app, "requestingUserSid: CurrentUserSid", "the reviewed workflow can freeze the per-user operation");
    }

    [TestMethod]
    [TestCategory("FR-UPD-15")]
    public void TheLegacyModelessUserBuilderNoLongerExistsInSource()
    {
        foreach ((string path, string source) in SrcSources())
            Assert.IsFalse(Regex.IsMatch(source, @"\bWingetUpdateExecutor\b"), path);

        string provider = Code(Read("src", "PCSteward.Provider.WinGet", "WingetInstalledInventoryProvider.cs"));
        // D4b stage 3: the machine executor (protocol 1) is gone too, so no job-based argument builder is left; every WinGet update
        // line is built from a reviewed operation.
        Assert.AreEqual(0, Regex.Count(provider, @"static\s+IReadOnlyList<string>\s+BuildUpdateArguments\s*\(\s*ImmutableUpdateJob\b"));
        Assert.IsFalse(Regex.IsMatch(provider, @"\bWingetMachineUpdateExecutor\b"), "the protocol-1 machine executor is retired");

        // The reviewed executor builds its arguments only from the reviewed operation, after the route admission check.
        Match reviewed = Regex.Match(provider, @"public sealed class WingetReviewedUserUpdateExecutor\b(?<body>.*?)\n}", RegexOptions.Singleline);
        Assert.IsTrue(reviewed.Success, "WingetReviewedUserUpdateExecutor not found");
        string body = reviewed.Groups["body"].Value;
        StringAssert.Contains(body, "if (!WingetUserReviewedRoute.Admits(operation))");
        StringAssert.Contains(body, "process.RunUpgradeAsync(WingetReviewedUpdateArguments.Build(operation), UpdateTimeout, progress, cancellationToken)");
        Assert.IsFalse(body.Contains("ImmutableUpdateJob", StringComparison.Ordinal), "the reviewed executor never reads a mode-less job");
    }

    [TestMethod]
    [TestCategory("FR-UPD-15")]
    public void TheNonElevatedRunnerRequiresTheReviewedOperationBoundToItsJob()
    {
        string runner = Code(Read("src", "PCSteward.Execution", "NonElevatedUpdateRunner.cs"));
        Assert.IsFalse(Regex.IsMatch(runner, @"\bIWingetUpdateExecutor\b"), "the runner cannot take a job-based WinGet executor");
        StringAssert.Contains(runner, "private readonly IWingetReviewedUserUpdateExecutor winget;");
        StringAssert.Contains(runner, "if (reviewed is null || !WingetUserReviewedRoute.Admits(reviewed) || !reviewed.BindsJob(job))");
        Assert.AreEqual(1, Regex.Count(runner, @"winget\.ExecuteAsync\("));
        StringAssert.Contains(runner, "winget.ExecuteAsync(reviewed, progress, cancellationToken)");
        Assert.IsTrue(runner.IndexOf("WingetUserReviewedRoute.Admits(reviewed)", StringComparison.Ordinal) <
            runner.IndexOf("winget.ExecuteAsync(", StringComparison.Ordinal), "the reviewed mode is checked before the provider starts");
    }

    [TestMethod]
    [TestCategory("FR-UPD-15")]
    public void NoBuiltSrcAssemblyDefinesOrReferencesTheLegacyUserExecutor()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        string? provider = assemblies.SingleOrDefault(path => Path.GetFileName(path) == "PCSteward.Provider.WinGet.dll");
        if (provider is null) Assert.Inconclusive("Build the solution first: the WinGet provider assembly was not found.");
        foreach (string assembly in assemblies)
        {
            Assert.IsFalse(CompiledIlScanner.Types(assembly).Any(type => type.FullName == LegacyUserExecutor), assembly);
            Assert.IsFalse(CompiledIlScanner.TypeReferences(assembly).Contains(LegacyUserExecutor), assembly);
        }
        Assert.IsTrue(CompiledIlScanner.Types(provider!).Any(type => type.FullName == ReviewedUserExecutor));
        string? desktop = assemblies.SingleOrDefault(path => Path.GetFileName(path) == "PCSteward.Desktop.dll");
        if (desktop is not null) Assert.IsTrue(CompiledIlScanner.TypeReferences(desktop).Contains(ReviewedUserExecutor), "the desktop composes the reviewed executor");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([CompiledIlScanner.FindRepositoryRoot(), .. parts]));

    private static IEnumerable<(string Path, string Source)> SrcSources()
    {
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string separator = Path.DirectorySeparatorChar.ToString();
        foreach (string path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains(separator + "obj" + separator, StringComparison.Ordinal) || path.Contains(separator + "bin" + separator, StringComparison.Ordinal)) continue;
            yield return (path, Code(File.ReadAllText(path)));
        }
    }

    /// <summary>Source with comments removed, so documentation that names the removed type is not counted as a reference.</summary>
    private static string Code(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"//[^\n]*", string.Empty);
}
