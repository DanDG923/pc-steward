using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// TM-03/04/13, FR-UPD-15; Chocolatey update security review H1: no elevated helper launch while a user-writable environment
/// source holds a .NET runtime control name, and no helper run while its own environment holds one. The decision tests are
/// portable; the live read is Windows-only and reads names only.
/// </summary>
[TestClass]
public sealed class ElevatedRuntimeEnvironmentGuardTests
{
    [TestMethod]
    [DataRow("DOTNET_ROOT")]
    [DataRow("DOTNET_ROOT_X64")]
    [DataRow("DOTNET_STARTUP_HOOKS")]
    [DataRow("DOTNET_ADDITIONAL_DEPS")]
    [DataRow("DOTNET_DiagnosticPorts")]
    [DataRow("DOTNET_EnableEventPipe")]
    [DataRow("dotnet_startup_hooks")]
    [DataRow("COMPlus_EnableDiagnostics")]
    [DataRow("complus_gcName")]
    [DataRow("CORECLR_ENABLE_PROFILING")]
    [DataRow("CORECLR_PROFILER_PATH_64")]
    [DataRow("COR_ENABLE_PROFILING")]
    [DataRow("COR_PROFILER_PATH")]
    [DataRow("APPDOMAIN_MANAGER_ASM")]
    [DataRow(" DOTNET_ROOT")]
    public void RuntimeControlNamesAreRefused(string name)
    {
        Assert.IsTrue(ElevatedRuntimeEnvironmentGuard.IsRuntimeControlName(name), name);
        Assert.IsFalse(ElevatedRuntimeEnvironmentGuard.Permits([["PATH", name]]), name);
    }

    [TestMethod]
    [DataRow("PATH")]
    [DataRow("TEMP")]
    [DataRow("ChocolateyInstall")]
    [DataRow("DOTNET")]
    [DataRow("CORE")]
    [DataRow("DOTNET_CLI_TELEMETRY_OPTOUT")]
    [DataRow("dotnet_nologo")]
    [DataRow("")]
    public void OtherNamesAndTheTwoSdkOnlyNamesPermit(string name)
    {
        Assert.IsFalse(ElevatedRuntimeEnvironmentGuard.IsRuntimeControlName(name), name);
        Assert.IsTrue(ElevatedRuntimeEnvironmentGuard.Permits([[name], []]), name);
    }

    [TestMethod]
    public void OnlyExactSdkNamesAreExempt()
    {
        Assert.IsTrue(ElevatedRuntimeEnvironmentGuard.IsRuntimeControlName("DOTNET_CLI_HOME"));
        Assert.IsTrue(ElevatedRuntimeEnvironmentGuard.IsRuntimeControlName("DOTNET_NOLOGO_X"));
        Assert.IsTrue(ElevatedRuntimeEnvironmentGuard.IsRuntimeControlName("DOTNET_CLI_TELEMETRY_OPTOUTX"));
    }

    [TestMethod]
    public void AnUnreadableSourceRefusesAndNameInAnySourceRefuses()
    {
        Assert.IsFalse(ElevatedRuntimeEnvironmentGuard.Permits([["PATH"], null]));
        Assert.IsFalse(ElevatedRuntimeEnvironmentGuard.Permits([["PATH"], [], ["CORECLR_PROFILER"]]));
        Assert.IsTrue(ElevatedRuntimeEnvironmentGuard.Permits([["PATH"], [], ["TEMP"]]));
        Assert.IsTrue(ElevatedRuntimeEnvironmentGuard.Permits([]));
    }

    [TestMethod]
    public void TheLiveChecksNeverThrowAndRefuseOffWindows()
    {
        bool launch = ElevatedRuntimeEnvironmentGuard.PermitsElevatedLaunch();
        bool clean = ElevatedRuntimeEnvironmentGuard.ProcessEnvironmentIsClean();
        if (!OperatingSystem.IsWindows()) Assert.IsFalse(launch, "the launch check refuses off Windows");
        // The test host's own environment decides the process check; it must agree with the pure rule over the same names.
        List<string> names = [.. Environment.GetEnvironmentVariables().Keys.Cast<string>()];
        Assert.AreEqual(ElevatedRuntimeEnvironmentGuard.Permits([names]), clean);
    }
}
