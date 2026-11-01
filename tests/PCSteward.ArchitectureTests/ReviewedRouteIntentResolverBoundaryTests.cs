using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 admission obligation 6 (N5 obligation (a)): the production intent resolver,
/// on compiled IL of every built <c>src</c> assembly and on source.
/// <list type="bullet">
/// <item>its only provider call is the existing <c>ChocolateyUpdateSourceConnector.ResolveAsync</c>; it never names a
/// process, the client process runner, the launcher, the helper or elevation;</item>
/// <item>no <c>src</c> code constructs it or calls its test-only factory, so (like the composition) it is not composed yet
/// and the runtime-limit route stays non-admitted;</item>
/// <item>the composition calls it only through its in-call supply path, immediately before the observation.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ReviewedRouteIntentResolverBoundaryTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string Resolver = Native + "ChocolateyReviewIntentResolver";
    private const string Connector = "PCSteward.Provider.Chocolatey.ChocolateyUpdateSourceConnector";
    private static readonly string[] ConnectorMembers = ["ResolveAsync", "get_Timeout"];

    private static readonly string[] BannedTargets =
    [
        "System.Diagnostics.Process", "System.Diagnostics.ProcessStartInfo", "PCSteward.Provider.Chocolatey.WindowsChocolateyProcess",
        "PCSteward.Provider.Chocolatey.IChocolateyProcess", "PCSteward.Provider.Chocolatey.MachineTrustedChocolatey", "System.IO.File",
        "System.IO.Directory", "Microsoft.Win32.Registry", "System.Environment", Native + "LaunchJob", Native + "NativeLaunchSpec",
        Native + "ChocolateyClientQueries", Native + "ChocolateyCompiledCommand", Native + "NativeChocolateyHelperItem", "PCSteward.Execution.",
        "PCSteward.ElevatedHelper.",
    ];

    [TestMethod]
    public void TheResolverReachesTheClientOnlyThroughTheExistingConnectorQuery()
    {
        string chocolatey = Chocolatey(out _);
        List<string> violations = [];
        HashSet<string> connectorMembers = [];
        int scanned = 0;
        foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(chocolatey))
        {
            if (reference.Caller != Resolver) continue;
            scanned++;
            if (reference.TargetType == Connector && reference.Kind == CompiledIlScanner.ReferenceKind.Method) connectorMembers.Add(reference.TargetMember);
            if (BannedTargets.Any(banned => reference.TargetType == banned || banned.EndsWith('.') && reference.TargetType.StartsWith(banned, StringComparison.Ordinal) ||
                    reference.TargetType.StartsWith(banned + "+", StringComparison.Ordinal)))
                violations.Add($"{reference.LogicalCaller} references {reference.TargetType}.{reference.TargetMember}");
        }
        Assert.IsTrue(scanned > 10, $"only {scanned} resolver references resolved");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        // ResolveAsync is the only query; get_Timeout sizes the real-time cap.
        CollectionAssert.AreEquivalent(ConnectorMembers, connectorMembers.ToArray(), string.Join(", ", connectorMembers));

        string source = Read("ChocolateyReviewIntentResolver.cs");
        StringAssert.Contains(source, "connector.ResolveAsync(ChocolateyUpdateSourceConnector.CommunitySourceId, packageId, bounded.Token)");
        StringAssert.Contains(source, "DateTimeOffset resolvedAt = time.GetUtcNow();");
        Assert.IsTrue(source.IndexOf("DateTimeOffset resolvedAt = time.GetUtcNow();", StringComparison.Ordinal) <
            source.IndexOf("connector.ResolveAsync(", StringComparison.Ordinal), "the resolution time is stamped before the client query starts");
        Assert.IsFalse(Regex.IsMatch(source, @"\b(runas|Verb\s*=|UseShellExecute|ProcessStartInfo|ElevatedHelper|HelperTransport|ReviewedHelper|HasSupportedMode)\b"),
            "the resolver names no process, elevation, helper or admission");
    }

    [TestMethod]
    public void NoSrcCodeConstructsTheResolverOrCallsItsTestFactory()
    {
        string chocolatey = Chocolatey(out List<string> assemblies);
        IReadOnlyList<(string Type, string Method)> defined = CompiledIlScanner.MethodDefinitions(chocolatey);
        Assert.IsTrue(defined.Contains((Resolver, ".ctor")) && defined.Contains((Resolver, "CreateForTests")),
            "the scan must see the resolver's constructor and test factory, or their non-use proves nothing");
        List<string> violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (reference.TargetType != Resolver) continue;
                // Only its own constructor chaining and its own test factory may reach its constructor.
                if (reference.TargetMember == ".ctor" && reference.Caller != Resolver)
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the intent resolver (not composed yet)");
                if (reference.TargetMember == "CreateForTests")
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls the resolver's test-only factory");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void TheCompositionUsesTheResolverOnlyThroughItsInCallSupplyPath()
    {
        string source = Read("ChocolateyReviewedRouteComposition.cs");
        StringAssert.Contains(source, "return ResolveReviewIntentAsync(resolver.For(packageId), cancellationToken);");
        Assert.AreEqual(1, Regex.Count(source, @"\bresolver\.For\("), "one use of the production resolver");
        // The in-call time rule and the observation follow the resolver call inside the same method.
        int resolve = source.IndexOf("resolved = await resolve(cancellationToken)", StringComparison.Ordinal);
        int window = source.IndexOf("if (resolved.ResolvedAt < requested || resolved.ResolvedAt > returned) return null;", StringComparison.Ordinal);
        int observe = source.IndexOf("evidenceSource.ObserveReviewIntentAsync(resolved.Candidate, resolved.ResolvedAt, cancellationToken)", StringComparison.Ordinal);
        Assert.IsTrue(resolve >= 0 && resolve < window && window < observe, "resolve, check the in-call time, then observe");
        Assert.IsFalse(Regex.IsMatch(source, @"new\s+ChocolateyReviewIntentResolver\b|new\s+ChocolateyUpdateSourceConnector\b"),
            "the composition constructs neither the resolver nor the connector");
    }

    private static string Chocolatey(out List<string> assemblies)
    {
        assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        Assert.IsTrue(assemblies.Count >= 5, "src assemblies were not built");
        return assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
    }

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey", "Native", file));
}
