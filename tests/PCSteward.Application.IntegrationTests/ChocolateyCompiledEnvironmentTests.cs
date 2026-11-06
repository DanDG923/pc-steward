using System.Diagnostics;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/01; TM-03/04/13: native bootstrap environment cannot inherit executable authority.</summary>
[TestClass]
public sealed class ChocolateyCompiledEnvironmentTests
{
    private static readonly string[] ExpectedKeys = ["SystemRoot", "WINDIR", "SystemDrive", "ComSpec", "PATH", "PATHEXT",
        "ProgramFiles", "ProgramW6432", "ProgramFiles(x86)", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
        "ChocolateyInstall", "TEMP", "TMP", "PSModulePath", "PROCESSOR_ARCHITECTURE"];
    private static ChocolateyLaunchPaths Paths => new(@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)",
        @"C:\Users\fixture", @"C:\Users\fixture\AppData\Roaming", @"C:\Users\fixture\AppData\Local",
        @"C:\ProgramData\chocolatey", @"D:\Protected Cache");

    [TestMethod]
    public void FixedEnvironmentHasOnlyTheReviewedKeysAndImmutableValues()
    {
        IReadOnlyDictionary<string, string> result = ChocolateyCompiledEnvironment.Create(Paths);
        CollectionAssert.AreEquivalent(ExpectedKeys, result.Keys.ToArray());
        Assert.AreEqual(@"C:\Windows", result["systemroot"]);
        Assert.AreEqual(@"C:\Windows\System32\cmd.exe", result["ComSpec"]);
        Assert.AreEqual(@"C:\Windows\System32;C:\Windows;C:\Windows\System32\Wbem;C:\Windows\System32\WindowsPowerShell\v1.0", result["PATH"]);
        Assert.AreEqual(@"C:\Program Files\WindowsPowerShell\Modules;C:\Windows\System32\WindowsPowerShell\v1.0\Modules", result["PSModulePath"]);
        Assert.AreEqual(@"D:\Protected Cache", result["TEMP"]);
        Assert.AreEqual(result["TEMP"], result["TMP"]);
        Assert.AreEqual(@"C:\ProgramData\chocolatey", result["ChocolateyInstall"]);
        Assert.AreEqual("AMD64", result["PROCESSOR_ARCHITECTURE"]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, string>)result)["PATH"] = @"C:\Mutable");
    }

    [TestMethod]
    public void ApplyReplacesTheWholeParentBlockWithoutStartingAProcess()
    {
        ProcessStartInfo start = new() { FileName = "never-launched", UseShellExecute = false };
        start.Environment.Clear();
        foreach (string name in new[] { "COR_ENABLE_PROFILING", "COR_PROFILER", "COR_PROFILER_PATH", "COMPlus_Version",
            "DOTNET_STARTUP_HOOKS", "CORECLR_PROFILER_PATH", "APPDOMAIN_MANAGER_ASM", "APPDOMAIN_MANAGER_TYPE",
            "DEVPATH", "__COMPAT_LAYER", "HTTPS_PROXY", "UNREVIEWED_FUTURE_VARIABLE" }) start.Environment.Add(name, "untrusted");
        start.Environment["chocolateyinstall"] = @"C:\Mutable";
        start.Environment["PATH"] = @"C:\Mutable";
        ChocolateyCompiledEnvironment.ApplyTo(start, Paths);
        IReadOnlyDictionary<string, string> expected = ChocolateyCompiledEnvironment.Create(Paths);
        Assert.AreEqual(expected.Count, start.Environment.Count);
        foreach ((string key, string value) in expected) Assert.AreEqual(value, start.Environment[key]);
        Assert.AreEqual(1, start.Environment.Keys.Count(key => key.Equals("ChocolateyInstall", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual("never-launched", start.FileName);
        Assert.IsEmpty(start.ArgumentList);
    }

    [TestMethod]
    [DataRow("relative")]
    [DataRow("C:relative")]
    [DataRow(@"\\server\share")]
    [DataRow(@"\\?\C:\device")]
    [DataRow(@"C:\base\..\other")]
    [DataRow(@"C:\base\folder.")]
    [DataRow(@"C:\base\folder ")]
    [DataRow(@"C:\base;%USERPROFILE%")]
    [DataRow(@"C:\base;D:\other")]
    [DataRow("C:\\bad\"quote")]
    [DataRow("C:\\bad\ncontrol")]
    public void InvalidPathsRefuseBeforeChangingTheParentBlock(string path)
    {
        ProcessStartInfo start = new() { UseShellExecute = false };
        start.Environment.Clear(); start.Environment["sentinel"] = "unchanged";
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCompiledEnvironment.ApplyTo(start, Paths with { ProtectedCache = path }));
        Assert.AreEqual(1, start.Environment.Count);
        Assert.AreEqual("unchanged", start.Environment["sentinel"]);
    }

    [TestMethod]
    public void ShellExecutionCannotConsumeTheCompiledBlock()
    {
        ProcessStartInfo start = new() { UseShellExecute = true };
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCompiledEnvironment.ApplyTo(start, Paths));
    }
}
