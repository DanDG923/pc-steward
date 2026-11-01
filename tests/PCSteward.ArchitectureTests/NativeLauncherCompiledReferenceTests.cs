using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/07/13; Task 50 design rev 6 §4.1 (rev-5 F4, N0 review M1): the bans on the launcher core, the
/// test-only spec factory and the test-only fault entry are checked on <b>compiled</b> IL of every built <c>src</c>
/// assembly, so aliases, Unicode-escaped identifiers or generated sources cannot hide a call. Calls from inside the
/// declaring type (including its nested and compiler-generated types) are the only permitted call sites.
/// </summary>
[TestClass]
public sealed class NativeLauncherCompiledReferenceTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private static readonly (string Type, string Member)[] Forbidden =
    [
        (Native + "LaunchJob", "RunCoreAsync"),
        (Native + "LaunchJob", "RunCoreWithFaultForTestsAsync"),
        (Native + "NativeLaunchSpec", "ForTests"),
    ];
    private static readonly string[] RequiredAssemblies = ["PCSteward.Provider.Chocolatey", "PCSteward.Execution", "PCSteward.ElevatedHelper"];

    [TestMethod]
    public void NoCompiledSrcCodeCallsTheLauncherCoreTheSpecTestFactoryOrTheFaultEntryFromOutsideItsType()
    {
        IReadOnlyList<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        foreach (string required in RequiredAssemblies)
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required}.dll was not built");

        List<string> violations = [];
        int resolvedCalls = 0, launchJobInternalCalls = 0;
        foreach (string path in assemblies)
        {
            foreach ((string caller, string targetType, string targetMember, _) in CompiledIlScanner.References(path)
                         .Where(reference => reference.Kind == CompiledIlScanner.ReferenceKind.Method))
            {
                resolvedCalls++;
                if (caller == Native + "LaunchJob" && targetType == Native + "LaunchJob" && targetMember == "StartAsync")
                    launchJobInternalCalls++;
                foreach ((string type, string member) in Forbidden)
                    if (targetType == type && targetMember == member && caller != type)
                        violations.Add($"{Path.GetFileName(path)}: {caller} calls {type}.{member}");
            }
        }

        // The scanner works: it sees the core's own internal calls, and a meaningful volume of calls overall.
        Assert.IsTrue(resolvedCalls > 1000, $"only {resolvedCalls} calls resolved");
        Assert.AreEqual(2, launchJobInternalCalls, "RunCoreAsync and the fault entry each call StartAsync once");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }
}
