using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/07/13; Task 50 design rev 6 §4.1 (rev-5 F3/F4, N0 review M1) and §8 N3: the launcher surface is
/// checked on <b>compiled</b> IL of every built <c>src</c> assembly, so aliases, Unicode-escaped identifiers or generated
/// sources cannot hide a call. Callers are resolved to their source method (async state machines and lambdas map back):
/// <list type="bullet">
/// <item><c>LaunchJob.RunCoreAsync</c>: only <c>LaunchJob.RunAsync</c>;</item>
/// <item><c>NativeLaunchSpec.FromBinding</c>: only <c>LaunchJob.RunAsync</c>;</item>
/// <item><c>LaunchJob.RunAsync</c>: only the three <c>ChocolateyClientQueries</c> methods, each of which calls it;</item>
/// <item>the test-only spec factory, fault entry and watchdog entry (ADR-031 R5): no <c>src</c> caller outside their own type.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class NativeLauncherCompiledReferenceTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string LaunchJob = Native + "LaunchJob";
    private const string Spec = Native + "NativeLaunchSpec";
    private const string Queries = Native + "ChocolateyClientQueries";
    private static readonly (string Type, string Member)[] Forbidden =
    [
        (LaunchJob, "RunCoreWithFaultForTestsAsync"),
        // ADR-031 design §2.1 R5: the test-only watchdog launch entry.
        (LaunchJob, "RunCoreWithWatchdogForTestsAsync"),
        (Spec, "ForTests"),
    ];

    /// <summary>(target type, member) → the exact set of source-level callers allowed in <c>src</c>.</summary>
    private static readonly (string Type, string Member, string[] Callers)[] Pinned =
    [
        (LaunchJob, "RunCoreAsync", [LaunchJob + ".RunAsync"]),
        (Spec, "FromBinding", [LaunchJob + ".RunAsync"]),
        (LaunchJob, "RunAsync", [Queries + ".ListExactAsync", Queries + ".OutdatedAsync", Queries + ".UpgradeAsync"]),
    ];

    private static readonly string[] RequiredAssemblies = ["PCSteward.Provider.Chocolatey", "PCSteward.Execution", "PCSteward.ElevatedHelper"];

    [TestMethod]
    public void TheLauncherCoreSpecFactoryAndTypedWrapperHaveExactlyTheirPinnedCompiledCallers()
    {
        IReadOnlyList<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        foreach (string required in RequiredAssemblies)
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required}.dll was not built");

        List<string> violations = [];
        Dictionary<(string, string), HashSet<string>> seen = Pinned.ToDictionary(pin => (pin.Type, pin.Member), _ => new HashSet<string>());
        int resolvedCalls = 0, launchJobInternalCalls = 0;
        foreach (string path in assemblies)
        {
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (reference.Kind != CompiledIlScanner.ReferenceKind.Method) continue;
                resolvedCalls++;
                if (reference.Caller == LaunchJob && reference.TargetType == LaunchJob && reference.TargetMember == "StartAsync")
                    launchJobInternalCalls++;
                foreach ((string type, string member) in Forbidden)
                    if (reference.TargetType == type && reference.TargetMember == member && reference.Caller != type)
                        violations.Add($"{Path.GetFileName(path)}: {reference.Caller} calls {type}.{member}");
                foreach ((string type, string member, string[] callers) in Pinned)
                {
                    if (reference.TargetType != type || reference.TargetMember != member) continue;
                    if (callers.Contains(reference.LogicalCaller)) seen[(type, member)].Add(reference.LogicalCaller);
                    else violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls {type}.{member}");
                }
            }
        }

        // The scanner works: it sees the core's own internal calls, and a meaningful volume of calls overall.
        Assert.IsTrue(resolvedCalls > 1000, $"only {resolvedCalls} calls resolved");
        Assert.AreEqual(3, launchJobInternalCalls, "RunCoreAsync, the fault entry and the watchdog test entry each call StartAsync once");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        // Every pinned caller really calls its target (the pin is the exact set, not merely an allow-list).
        foreach ((string type, string member, string[] callers) in Pinned)
            CollectionAssert.AreEquivalent(callers, seen[(type, member)].ToArray(), $"{type}.{member}");
    }

    [TestMethod]
    public void TheLogicalCallerMapsAsyncStateMachinesAndLambdasBackToTheSourceMethod()
    {
        CompiledIlScanner.IlReference stateMachine = new("A.B", "T", "M", CompiledIlScanner.ReferenceKind.Method)
        { CallerType = "A.B+<ListExactAsync>d__3", CallerMethod = "MoveNext" };
        CompiledIlScanner.IlReference lambda = new("A.B", "T", "M", CompiledIlScanner.ReferenceKind.Method)
        { CallerType = "A.B+<>c", CallerMethod = "<RunAsync>b__4_0" };
        CompiledIlScanner.IlReference nested = new("A.B", "T", "M", CompiledIlScanner.ReferenceKind.Method)
        { CallerType = "A.B+ListExact", CallerMethod = "TryCreate" };
        Assert.AreEqual("A.B.ListExactAsync", stateMachine.LogicalCaller);
        Assert.AreEqual("A.B.RunAsync", lambda.LogicalCaller);
        Assert.AreEqual("A.B+ListExact.TryCreate", nested.LogicalCaller);
    }
}
