using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// TM-03/04/13; Chocolatey update security review H1: every <c>runas</c> launch of the elevated helper is preceded by the
/// runtime-environment guard in the same launcher method, the helper's entry point checks its own environment before it
/// dispatches anything, the helper project keeps the two build switches, and the guard reads registry value names only,
/// read-only, below <c>HKEY_CURRENT_USER</c>.
/// </summary>
[TestClass]
public sealed class ElevatedRuntimeEnvironmentBoundaryTests
{
    private const string GuardFile = "ElevatedRuntimeEnvironmentGuard.cs";
    private static readonly string[] Users = ["ReviewedHelperProbeTransport.cs", "ReviewedHelperProtocol2Transport.cs", "WindowsElevatedHelperTransport.cs"];

    [TestMethod]
    public void EveryRunasLaunchIsGuardedBeforeProcessStart()
    {
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        int launches = 0;
        foreach (string path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            string code = CodeOnly(File.ReadAllText(path));
            foreach (Match runas in Regex.Matches(code, @"Verb\s*=\s*""runas"""))
            {
                launches++;
                int start = Regex.Match(code[runas.Index..], @"Process\.Start\(").Index + runas.Index;
                Assert.IsTrue(start > runas.Index, $"{path}: runas without Process.Start");
                string between = code[runas.Index..start];
                StringAssert.Contains(between, "ElevatedRuntimeEnvironmentGuard.PermitsElevatedLaunch()", $"{path}: unguarded runas launch");
            }
        }
        Assert.AreEqual(3, launches, "the protocol-1, protocol-2 and probe launchers");
    }

    [TestMethod]
    public void TheHelperEntryChecksItsOwnEnvironmentFirst()
    {
        string code = CodeOnly(File.ReadAllText(Path.Combine(ExecutionDirectory(), "WindowsElevatedHelperTransport.cs")));
        Match entry = Regex.Match(code, @"public static Task<int> RunAsync\(string\[\] arguments\) =>\s*(?<first>[^?]+)\?");
        Assert.IsTrue(entry.Success, "the entry point dispatch");
        StringAssert.Contains(entry.Groups["first"].Value, "!ElevatedRuntimeEnvironmentGuard.ProcessEnvironmentIsClean()");
    }

    [TestMethod]
    public void TheHelperProjectKeepsTheRuntimeSwitches()
    {
        string project = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.ElevatedHelper",
            "PCSteward.ElevatedHelper.csproj"));
        StringAssert.Contains(project, "<StartupHookSupport>false</StartupHookSupport>");
        StringAssert.Contains(project, "<AppHostDotNetSearch>Global</AppHostDotNetSearch>");
        // ADR-033: the published helper is one Native AOT image with EventSource compiled out, set unconditionally here.
        StringAssert.Contains(project, "<PublishAot>true</PublishAot>");
        StringAssert.Contains(project, "<EventSourceSupport>false</EventSourceSupport>");
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Count(project, "<PublishAot>"), "one PublishAot setting");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(project, @"<PropertyGroup[^>]*Condition"), "no conditional property group can switch it off");
    }

    [TestMethod]
    public void TheGuardReadsValueNamesOnlyAndNeverWrites()
    {
        string code = CodeOnly(File.ReadAllText(Path.Combine(ExecutionDirectory(), GuardFile)));
        foreach (string token in new[]
                 {
                     "GetValue(", "SetValue", "CreateSubKey", "DeleteValue", "DeleteSubKey", "writable: true", "RegistryHive.LocalMachine",
                     "ExpandEnvironmentVariables", "GetEnvironmentVariable(", "SetEnvironmentVariable", "DllImport", "LibraryImport",
                 })
            Assert.IsFalse(code.Contains(token, StringComparison.Ordinal), $"the guard uses {token}");
        Assert.AreEqual(2, Regex.Count(code, @"OpenSubKey\([^)]*writable: false\)"), "two read-only opens");
        Assert.AreEqual(Regex.Count(code, @"OpenSubKey\("), Regex.Count(code, @"OpenSubKey\([^)]*writable: false\)"), "every open is read-only");
        // The guard is used only by the three launchers and the helper entry point.
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string[] users = [.. Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj") && Path.GetFileName(path) != GuardFile)
            .Where(path => CodeOnly(File.ReadAllText(path)).Contains("ElevatedRuntimeEnvironmentGuard", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)!];
        CollectionAssert.AreEqual(Users, users);
    }

    private static string ExecutionDirectory() => Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution");

    private static string CodeOnly(string source) => string.Join('\n', source.Split('\n')
        .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
