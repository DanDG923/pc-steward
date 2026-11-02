using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13; ADR-031 design mapping §2 R5: the client self-update launches only while none of the names pinned
/// setup.psm1 uses to weaken or skip the root ACL (ChocolateyInstallAllowCurrentUser, ChocolateyAllowInsecureRootDirectory,
/// and a user-scope ChocolateyInstall) can reach the elevated setup: not in the compiled child block, not in the user or
/// machine environment key, and never on an incomplete reading. The guard tests are portable; the compiled-environment and
/// live-reader tests are Windows-only (reported as failed elsewhere).
/// </summary>
[TestClass]
public sealed class ChocolateySetupEnvironmentGuardTests
{
    private const string Root = @"C:\ProgramData\chocolatey";
    private static readonly string[] UserNames = ["ChocolateyInstallAllowCurrentUser", "ChocolateyAllowInsecureRootDirectory", "ChocolateyInstall"];
    private static readonly string[] MachineNames = ["ChocolateyInstallAllowCurrentUser", "ChocolateyAllowInsecureRootDirectory"];

    [TestMethod]
    public void ACleanBlockAndACleanCompleteReadingPermit()
    {
        Assert.IsTrue(ChocolateySetupEnvironmentGuard.Permits(Block(), Root, Clean()));
        // Names compare ignoring case, as Windows environment names do.
        Assert.IsTrue(ChocolateySetupEnvironmentGuard.Permits(new Dictionary<string, string> { ["CHOCOLATEYINSTALL"] = Root }, Root, Clean()));
    }

    [TestMethod]
    public void EveryPinnedNameInEitherKeyOrAnIncompleteReadingRefuses()
    {
        foreach (string name in ChocolateySetupEnvironmentReader.UserScopeNames)
            Assert.IsFalse(ChocolateySetupEnvironmentGuard.Permits(Block(), Root, ChocolateySetupEnvironmentReading.Of([name], [])), name);
        foreach (string name in ChocolateySetupEnvironmentReader.MachineScopeNames)
            Assert.IsFalse(ChocolateySetupEnvironmentGuard.Permits(Block(), Root, ChocolateySetupEnvironmentReading.Of([], [name])), name);
        Assert.IsFalse(ChocolateySetupEnvironmentGuard.Permits(Block(), Root, ChocolateySetupEnvironmentReading.Unreadable));
        CollectionAssert.AreEqual(UserNames, ChocolateySetupEnvironmentReader.UserScopeNames.ToArray());
        CollectionAssert.AreEqual(MachineNames, ChocolateySetupEnvironmentReader.MachineScopeNames.ToArray());
    }

    [TestMethod]
    public void TheCompiledBlockMustNotCarryAnOverrideAndMustNameExactlyTheRoot()
    {
        foreach (string name in new[] { "ChocolateyInstallAllowCurrentUser", "chocolateyinstallallowcurrentuser", "ChocolateyAllowInsecureRootDirectory" })
        {
            Dictionary<string, string> block = Block();
            block[name] = "true";
            Assert.IsFalse(ChocolateySetupEnvironmentGuard.Permits(block, Root, Clean()), name);
            block[name] = "false";
            Assert.IsFalse(ChocolateySetupEnvironmentGuard.Permits(block, Root, Clean()), name + " (any value)");
        }
        Assert.IsFalse(ChocolateySetupEnvironmentGuard.Permits(new Dictionary<string, string> { ["PATH"] = @"C:\Windows" }, Root, Clean()), "no root");
        Assert.IsFalse(ChocolateySetupEnvironmentGuard.Permits(new Dictionary<string, string> { ["ChocolateyInstall"] = @"C:\Chocolatey" }, Root, Clean()));
        Assert.IsFalse(ChocolateySetupEnvironmentGuard.Permits(new Dictionary<string, string> { ["ChocolateyInstall"] = Root.ToUpperInvariant() }, Root, Clean()),
            "the root is compared exactly");
        Assert.IsFalse(ChocolateySetupEnvironmentGuard.Permits(new Dictionary<string, string>(StringComparer.Ordinal)
            { ["ChocolateyInstall"] = Root, ["CHOCOLATEYINSTALL"] = @"C:\Chocolatey" }, Root, Clean()), "two spellings");
    }

    [TestMethod]
    public void TheCompiledEnvironmentNeverCarriesAnOverrideName()
    {
        N1NativeFixture.RequireWindows("compiled environment formatting of Windows paths");
        IReadOnlyDictionary<string, string> compiled = ChocolateyCompiledEnvironment.Create(new ChocolateyLaunchPaths(@"C:\Windows",
            @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\u", @"C:\Users\u\AppData\Roaming", @"C:\Users\u\AppData\Local", Root, @"C:\cache"));
        Assert.IsTrue(ChocolateySetupEnvironmentGuard.CompiledEnvironmentIsClean(compiled, Root));
        foreach (string name in ChocolateySetupEnvironmentReader.MachineScopeNames)
            Assert.IsFalse(compiled.Keys.Any(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)), name);
        Assert.AreEqual(Root, compiled["ChocolateyInstall"]);
    }

    [TestMethod]
    public void TheLiveReaderCompletesOnWindowsAndIsIncompleteElsewhere()
    {
        ChocolateySetupEnvironmentReading reading = ChocolateySetupEnvironmentReader.Read();
        if (!OperatingSystem.IsWindows())
        {
            Assert.IsFalse(reading.Complete, "off Windows the reading is incomplete, so the guard refuses");
            return;
        }
        // The reader only reports presence; a CI machine does not set these names, but the assertion is only completeness.
        Assert.IsTrue(reading.Complete);
        Assert.IsTrue(reading.UserNames.All(ChocolateySetupEnvironmentReader.UserScopeNames.Contains));
        Assert.IsTrue(reading.MachineNames.All(ChocolateySetupEnvironmentReader.MachineScopeNames.Contains));
    }

    private static Dictionary<string, string> Block() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemRoot"] = @"C:\Windows", ["PATH"] = @"C:\Windows\System32", ["ChocolateyInstall"] = Root, ["TEMP"] = @"C:\cache",
    };

    private static ChocolateySetupEnvironmentReading Clean() => ChocolateySetupEnvironmentReading.Of([], []);
}
