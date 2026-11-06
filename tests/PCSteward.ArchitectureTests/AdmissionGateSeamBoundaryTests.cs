using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13; ADR-031 design §8.6 admission gates (checked on compiled IL of every built
/// <c>src</c> assembly, which must include Application, Provider.Chocolatey, Execution and Storage, and on LF-normalised source): the test-only seams that let the gate tests run the client
/// self-update route have no <c>src</c> caller, and their production defaults are the reviewed behaviour.
/// <list type="bullet">
/// <item>Gate 1: the former review-gate seam <c>ReviewedRouteTestAdmission</c> is removed by the Task 50 activation (ADR-031
/// owner decision 7, step C-A): the production review gate now admits the exact client shape itself
/// (<c>ChocolateyRouteActivationBoundaryTests</c>), so no type, member or source text of the seam may remain.</item>
/// <item>Gate 2: <c>NativeChocolateyHelperItem.CreateClientOverFixtureForTests</c> (fixture release list) and
/// <c>ChocolateyClientWatchTargets.UseEnvironmentWatchForTests</c> (test environment keys for one binding) are never
/// called from <c>src</c>; the production item passes the compiled list; the registration is read only by
/// <c>ChocolateyClientWatchTargets.FromBinding</c>.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class AdmissionGateSeamBoundaryTests
{
    private const string Admission = "PCSteward.Application.ReviewedRouteTestAdmission";
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string Item = Native + "NativeChocolateyHelperItem";
    private const string Targets = Native + "ChocolateyClientWatchTargets";

    [TestMethod]
    public void TheTestOnlyAdmissionSeamsHaveNoCompiledSrcCaller()
    {
        List<string> assemblies = RequireBuilt();
        string application = Assembly(assemblies, "PCSteward.Application");
        string chocolatey = Assembly(assemblies, "PCSteward.Provider.Chocolatey");
        IReadOnlyList<(string Type, string Method)> applicationMethods = CompiledIlScanner.MethodDefinitions(application);
        IReadOnlyList<(string Type, string Method)> chocolateyMethods = CompiledIlScanner.MethodDefinitions(chocolatey);
        // Activation removed the gate-1 seam: no method of the former admission seam type is compiled into Application.
        Assert.IsFalse(applicationMethods.Any(method => method.Type == Admission || method.Type.StartsWith(Admission + "+", StringComparison.Ordinal)),
            "the removed test-only review-gate admission seam is compiled into Application");
        foreach ((string type, string member) in new[]
                 {
                     (Item, "CreateClientOverFixtureForTests"), (Targets, "UseEnvironmentWatchForTests"), (Targets, "TestEnvironmentWatch"),
                 })
            Assert.IsTrue(chocolateyMethods.Contains((type, member)), $"{type}.{member} is not defined; update the ban list");

        List<string> violations = [];
        int environmentReads = 0;
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                string where = $"{Path.GetFileName(path)}: {reference.LogicalCaller}";
                if (reference.TargetType == Admission || reference.TargetType.StartsWith(Admission + "+", StringComparison.Ordinal))
                    violations.Add($"{where} references the removed test-only review-gate admission seam");
                // No AsyncLocal admission flag remains in the review gate's assembly.
                if (reference.Kind == CompiledIlScanner.ReferenceKind.Method && reference.TargetMember == "set_Value" &&
                    reference.TargetType.StartsWith("System.Threading.AsyncLocal", StringComparison.Ordinal) &&
                    (reference.CallerType ?? reference.Caller).StartsWith("PCSteward.Application.Reviewed", StringComparison.Ordinal))
                    violations.Add($"{where} writes an ambient admission flag");
                if (reference.TargetType == Item && reference.TargetMember == "CreateClientOverFixtureForTests")
                    violations.Add($"{where} calls the test-only fixture release-list item factory");
                if (reference.TargetType == Targets && reference.TargetMember == "UseEnvironmentWatchForTests")
                    violations.Add($"{where} registers a test environment-key watch");
                if (reference.TargetType == Targets && reference.TargetMember == "TestEnvironmentWatch")
                {
                    environmentReads++;
                    if (reference.LogicalCaller != Targets + ".FromBinding") violations.Add($"{where} reads the test environment-key watch");
                }
                // The registration table is written only by the test-only setter and read only by its lookup.
                if (reference.TargetType == Targets && reference.TargetMember == "TestEnvironmentWatches" &&
                    reference.LogicalCaller is not (Targets + ".UseEnvironmentWatchForTests" or Targets + ".TestEnvironmentWatch" or Targets + "..cctor"))
                    violations.Add($"{where} touches the test environment-key table");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        Assert.AreEqual(1, environmentReads, "FromBinding reads the binding's test key watch exactly once");
    }

    [TestMethod]
    public void TheProductionDefaultsAreTheReviewedRuleTheCompiledListAndTheRealKeys()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        // Gate 1 (Task 50 activation): the former review-gate seam is gone from every src file; the production rule is pinned
        // by ChocolateyRouteActivationBoundaryTests.
        foreach (string path in SourcePaths(root))
        {
            Assert.AreNotEqual("ReviewedRouteTestAdmission.cs", Path.GetFileName(path), "the removed seam file is back");
            string text = File.ReadAllText(path);
            foreach (string removed in new[] { "ReviewedRouteTestAdmission", "EnterClientSelfUpdateForTests" })
                Assert.IsFalse(text.Contains(removed, StringComparison.Ordinal), $"{path} names the removed seam {removed}");
        }
        // Source-text fallback for the gate-2 seams (LF-normalised): each is named only in its own defining file.
        (string Seam, string DefiningFile)[] seams =
        [
            ("CreateClientOverFixtureForTests", "NativeChocolateyHelperItem.cs"),
            ("UseEnvironmentWatchForTests", "ChocolateyClientUpdateWatchdog.cs"),
        ];
        foreach (string path in SourcePaths(root))
        {
            string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            foreach ((string seam, string definingFile) in seams)
                if (Path.GetFileName(path) != definingFile)
                    Assert.IsFalse(text.Contains(seam, StringComparison.Ordinal), $"{path} names the test-only seam {seam}");
        }
        foreach ((string seam, string definingFile) in seams)
        {
            string[] defining = [.. SourcePaths(root).Where(path => Path.GetFileName(path) == definingFile)];
            Assert.HasCount(1, defining, $"{definingFile} is unique under src");
            Assert.IsTrue(Regex.IsMatch(File.ReadAllText(defining[0]).Replace("\r\n", "\n", StringComparison.Ordinal),
                @"internal static \w+ " + seam + @"\("), $"{seam} is defined in {definingFile}; update the ban list");
        }

        string item = Source(root, "PCSteward.Provider.Chocolatey", @"Native/NativeChocolateyHelperItem.cs");
        // The production item and the existing fixture factory read the compiled list; only the gate-2 factory passes another.
        StringAssert.Contains(item, "new NativeWork(token, ProductionObserve, ancestryBase: null, stageCForTests: null, ChocolateyReleaseList.Listed)");
        StringAssert.Contains(item, "new NativeWork(token, observe, ancestryBase, stageCForTests, ChocolateyReleaseList.Listed)");
        StringAssert.Contains(item, "new NativeWork(token, observe, ancestryBase: null, stageCForTests: null, releases)");
        Assert.AreEqual(3, Regex.Count(item, @"new NativeWork\("), "three constructions of the native work");
        StringAssert.Contains(item, "ChocolateyReviewedRelease? release = releases.FindByVersion(installed);");

        string watchdog = Source(root, "PCSteward.Provider.Chocolatey", @"Native/ChocolateyClientUpdateWatchdog.cs");
        StringAssert.Contains(watchdog, "TestEnvironmentWatch(client) ?? ChocolateySetupEnvironmentReader.TryWatch, watchThreadForTests: null);");
        Assert.AreEqual(1, Regex.Count(watchdog, @"TestEnvironmentWatches\.Add\("), "a single registration site");
    }

    /// <summary>Review L6 (as <c>HelperEndpointBoundaryTests.RequireBuilt</c>): a missing assembly fails instead of passing vacuously.</summary>
    private static List<string> RequireBuilt()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        foreach (string required in new[] { "PCSteward.Application", "PCSteward.Provider.Chocolatey", "PCSteward.Execution", "PCSteward.Storage" })
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required} was not built");
        return assemblies;
    }

    private static string Assembly(List<string> assemblies, string name) =>
        assemblies.SingleOrDefault(path => Path.GetFileNameWithoutExtension(path) == name) ?? throw new AssertFailedException($"{name}.dll was not built");

    /// <summary>Source text without comment lines, with CRLF normalised and runs of whitespace collapsed to one space.</summary>
    private static string Source(string root, string project, string relative)
    {
        string text = File.ReadAllText(Path.Combine(root, "src", project, relative.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        string code = string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        return Regex.Replace(code, @"[ \t]*\n[ \t]*", " ");
    }

    private static IEnumerable<string> SourcePaths(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));
}
