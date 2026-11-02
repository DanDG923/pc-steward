using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13; ADR-031 design §8.6 admission gates (checked on compiled IL of every built
/// <c>src</c> assembly, which must include Application, Provider.Chocolatey, Execution and Storage, and on LF-normalised source): the three test-only seams that let the gate tests run the client
/// self-update route before admission have no <c>src</c> caller, and their production defaults are the reviewed behaviour.
/// <list type="bullet">
/// <item>Gate 1: <c>ReviewedRouteTestAdmission.EnterClientSelfUpdateForTests</c> (the only way to set its flag) is never
/// called from <c>src</c>; <c>Admits</c> is read only as the trailing term of <c>HasSupportedMode</c>, whose compiled rule
/// is otherwise unchanged; the flag is written only by the scope entry and its disposal.</item>
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
    private const string Review = "PCSteward.Application.ReviewedUpdateReview";
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
        foreach ((string type, string member) in new[] { (Admission, "EnterClientSelfUpdateForTests"), (Admission, "Admits") })
            Assert.IsTrue(applicationMethods.Contains((type, member)), $"{type}.{member} is not defined; update the ban list");
        foreach ((string type, string member) in new[]
                 {
                     (Item, "CreateClientOverFixtureForTests"), (Targets, "UseEnvironmentWatchForTests"), (Targets, "TestEnvironmentWatch"),
                 })
            Assert.IsTrue(chocolateyMethods.Contains((type, member)), $"{type}.{member} is not defined; update the ban list");

        List<string> violations = [];
        int admitsReads = 0, environmentReads = 0, flagWrites = 0;
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                string where = $"{Path.GetFileName(path)}: {reference.LogicalCaller}";
                if (reference.TargetType == Admission && reference.TargetMember == "EnterClientSelfUpdateForTests")
                    violations.Add($"{where} enters the test-only client route admission");
                if (reference.TargetType == Admission && reference.TargetMember == "Admits")
                {
                    admitsReads++;
                    if (reference.LogicalCaller != Review + ".HasSupportedMode") violations.Add($"{where} reads the test admission");
                }
                if (reference.TargetType.StartsWith(Admission + "+", StringComparison.Ordinal) && reference.TargetMember == ".ctor" &&
                    reference.LogicalCaller != Admission + ".EnterClientSelfUpdateForTests")
                    violations.Add($"{where} constructs the admission scope");
                // The AsyncLocal flag's setter is reached only by the scope entry and the scope's disposal.
                if (reference.Kind == CompiledIlScanner.ReferenceKind.Method && reference.TargetMember == "set_Value" &&
                    reference.TargetType.StartsWith("System.Threading.AsyncLocal", StringComparison.Ordinal) &&
                    (reference.CallerType ?? reference.Caller).StartsWith(Admission, StringComparison.Ordinal))
                {
                    flagWrites++;
                    if (reference.LogicalCaller is not (Admission + ".EnterClientSelfUpdateForTests" or Admission + "+Scope.Dispose"))
                        violations.Add($"{where} writes the admission flag");
                }
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
        Assert.AreEqual(1, admitsReads, "HasSupportedMode reads the test admission exactly once");
        Assert.AreEqual(1, environmentReads, "FromBinding reads the binding's test key watch exactly once");
        Assert.AreEqual(2, flagWrites, "the scope entry and its disposal write the flag");
    }

    [TestMethod]
    public void TheProductionDefaultsAreTheReviewedRuleTheCompiledListAndTheRealKeys()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        string review = Source(root, "PCSteward.Application", "ReviewedUpdateReview.cs");
        Match gate = Regex.Match(review, @"internal static bool HasSupportedMode\(ReviewedUpdateOperation operation\) =>(?<body>[^;]*);");
        Assert.IsTrue(gate.Success, "HasSupportedMode not found");
        string body = Regex.Replace(gate.Groups["body"].Value, @"\s+", " ").Trim();
        // The reviewed WinGet-only rule, byte-for-byte, then the single test-scope term.
        Assert.AreEqual("operation.HasCurrentSchema && operation.RouteContract == UpdateRouteContract.WinGetUserExactV3 && " +
            "operation.Selection.Architecture.Binding == ReviewedArchitectureBinding.Observed && " +
            "operation.Selection.Architecture.Value is UpdateArchitecture.X86 or UpdateArchitecture.X64 or UpdateArchitecture.Arm64 && " +
            "UpdateInstallerModeCapabilities.Resolve(operation.Selection.Provider, operation.Selection.Source, operation.Selection.Scope) == operation.Mode || " +
            "ReviewedRouteTestAdmission.Admits(operation)", body);
        string admission = Source(root, "PCSteward.Application", "ReviewedRouteTestAdmission.cs");
        StringAssert.Contains(admission, "ClientSelfUpdateAdmitted.Value && operation.HasCurrentSchema &&");
        Assert.AreEqual(1, Regex.Count(admission, @"ClientSelfUpdateAdmitted\.Value = true;"), "only the scope entry admits");
        // Source-text fallback for all three seams (LF-normalised): each is named only in its own defining file.
        (string Seam, string DefiningFile)[] seams =
        [
            ("EnterClientSelfUpdateForTests", "ReviewedRouteTestAdmission.cs"),
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
