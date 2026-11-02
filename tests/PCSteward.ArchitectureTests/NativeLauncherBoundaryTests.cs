using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/07/13; Task 50 design rev 6 §4.1, §8 N0 and N3 (rev-4 M1, rev-5 F3): the N0 launcher core's only
/// <c>src</c> caller is the typed N3 wrapper <c>LaunchJob.RunAsync</c> (the compiled-IL rule is in
/// <see cref="NativeLauncherCompiledReferenceTests"/>); its test-only spec factory and test-only fault hook have no
/// <c>src</c> caller; the native launcher code reads no ambient environment or registry state, declares only internal
/// types and permits no job breakaway.
/// </summary>
[TestClass]
public sealed class NativeLauncherBoundaryTests
{
    private const string LaunchJobFile = "LaunchJob.cs";
    private const string SpecFile = "NativeLaunchSpec.cs";

    [TestMethod]
    public void NoSourceFileCallsTheLauncherCoreTheSpecTestFactoryOrTheFaultHook()
    {
        Dictionary<string, string> sources = SourceFiles(Path.Combine(FindRepositoryRoot(), "src"));
        string launchJob = sources.Keys.Single(path => IsNativeFile(path, LaunchJobFile));
        string spec = sources.Keys.Single(path => IsNativeFile(path, SpecFile));

        // N3: the core is declared once and called once (by RunAsync); documentation lines are not code.
        Assert.AreEqual(2, Count(Code(sources[launchJob]), @"\bRunCoreAsync\b"), "LaunchJob.cs may only declare the core and call it from RunAsync");
        Assert.AreEqual(1, Count(Code(sources[launchJob]), @"return\s+RunCoreAsync\(spec,\s*deadline,\s*cancellationToken\);"),
            "the one core call is RunAsync's");
        Assert.AreEqual(1, Count(sources[launchJob], @"\bRunCoreWithFaultForTestsAsync\b"), "LaunchJob.cs may only declare the fault entry");
        Assert.AreEqual(1, Count(sources[spec], @"\bForTests\b"), "NativeLaunchSpec.cs may only declare the test factory");

        string[] banned =
        [
            @"\bRunCoreWithFaultForTestsAsync\b",
            @"\bLaunchJobFault\b",
            @"\bLaunchJob\s*\.\s*RunCoreAsync\b",
            @"\bNativeLaunchSpec\s*\.\s*ForTests\b",
            @"using\s+static\s+PCSteward\.Provider\.Chocolatey\.Native\.(LaunchJob|NativeLaunchSpec)\s*;"
        ];
        foreach ((string path, string source) in sources.Where(pair => pair.Key != launchJob))
            foreach (string pattern in banned)
                Assert.IsFalse(Regex.IsMatch(source, pattern), $"{path} uses a test-only launcher surface ({pattern})");

        string chocolatey = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey") + Path.DirectorySeparatorChar;
        foreach ((string path, string source) in sources.Where(pair => pair.Key.StartsWith(chocolatey, StringComparison.Ordinal)))
        {
            if (path != launchJob)
                Assert.IsFalse(Regex.IsMatch(source, @"\bRunCoreAsync\b"), $"{path} reaches the N0 launcher core");
            if (path != spec)
                Assert.IsFalse(Regex.IsMatch(source, @"\bForTests\b"), $"{path} reaches the test-only spec factory");
        }
    }

    [TestMethod]
    public void ExecutionNeverNamesTheLauncherOrItsSpec()
    {
        string execution = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Execution");
        foreach ((string path, string source) in SourceFiles(execution))
            Assert.IsFalse(Regex.IsMatch(source, @"\b(LaunchJob|NativeLaunchSpec|LaunchJobFault)\b"), path);
    }

    [TestMethod]
    public void NativeLauncherCodeReadsNoAmbientEnvironmentOrRegistry()
    {
        Dictionary<string, string> native = NativeSources();
        Assert.IsTrue(native.Keys.Any(path => IsNativeFile(path, LaunchJobFile)));
        string[] forbidden =
        [
            "GetEnvironmentVariable", "SetEnvironmentVariable", "ExpandEnvironmentVariables", "EnvironmentVariableTarget",
            "Registry", "RegOpenKey", "RegQueryValue", "RegGetValue", "GetEnvironmentStrings"
        ];
        // The generated release table is data only (pinned by ProfileObserverBoundaryTests); its paths name a helper script.
        // ADR-031 design §2 R5: the single read-only setup-environment reader is the one pinned exception.
        foreach ((string path, string source) in native.Where(pair => !IsNativeFile(pair.Key, "ChocolateyReleaseManifest2_7_5.cs") &&
                     !IsNativeFile(pair.Key, SetupEnvironmentReaderBoundaryTests.ReaderFile)))
            foreach (string token in forbidden)
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{path} uses {token}");
    }

    [TestMethod]
    public void NativeTypesAreInternalAndTheSpecHasOnlyAPrivateConstructor()
    {
        Dictionary<string, string> native = NativeSources();
        foreach ((string path, string source) in native)
            Assert.IsFalse(Regex.IsMatch(source, @"\bpublic\s+((sealed|static|abstract|partial|readonly|ref|unsafe)\s+)*(class|struct|record|enum|interface|delegate)\b"),
                $"{path} declares a public type");
        string spec = native.Single(pair => IsNativeFile(pair.Key, SpecFile)).Value;
        StringAssert.Contains(spec, "private NativeLaunchSpec(");
        Assert.IsFalse(Regex.IsMatch(spec, @"\b(public|internal|protected)\s+NativeLaunchSpec\s*\("), "the spec must only be constructed privately");
        string launchJob = native.Single(pair => IsNativeFile(pair.Key, LaunchJobFile)).Value;
        StringAssert.Contains(launchJob, "private LaunchJob(");
        Assert.IsFalse(Regex.IsMatch(launchJob, @"\b(public|internal|protected)\s+LaunchJob\s*\("));
    }

    [TestMethod]
    public void TheLauncherCreatesSuspendedWithAHandleListAndPermitsNoBreakaway()
    {
        Dictionary<string, string> native = NativeSources();
        string all = string.Join('\n', native.Values);
        // JOB_OBJECT_LIMIT_BREAKAWAY_OK, JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK and CREATE_BREAKAWAY_FROM_JOB.
        foreach (string value in new[] { "0x00000800", "0x00001000", "0x01000000" })
            Assert.IsFalse(all.Contains(value, StringComparison.OrdinalIgnoreCase), value);
        Assert.IsFalse(Regex.IsMatch(all, @"const\s+\w+\s+\w*Breakaway\w*"), "no breakaway constant may be declared");

        string launchJob = native.Single(pair => IsNativeFile(pair.Key, LaunchJobFile)).Value;
        StringAssert.Contains(launchJob, "Win32.CreateSuspended | Win32.CreateNoWindow | Win32.ExtendedStartupInfoPresent |");
        StringAssert.Contains(launchJob, "Win32.CreateUnicodeEnvironment");
        StringAssert.Contains(launchJob, "Win32.ProcThreadAttributeHandleList");
        StringAssert.Contains(launchJob, "applied.BasicLimitInformation.LimitFlags == Win32.JobObjectLimitKillOnJobClose");
        StringAssert.Contains(launchJob, "Win32.JobObjectBasicAccountingInformationClass");
        // The environment block comes from the spec alone.
        StringAssert.Contains(launchJob, "NativeCommandLine.BuildEnvironmentBlock(spec.Environment)");
        Assert.IsFalse(Regex.IsMatch(all, @"\bWin32_Product\b"));
    }

    private static Dictionary<string, string> NativeSources() =>
        SourceFiles(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey", "Native"));

    private static bool IsNativeFile(string path, string name) =>
        Path.GetFileName(path) == name && Path.GetFileName(Path.GetDirectoryName(path)) == "Native";

    private static Dictionary<string, string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToDictionary(path => path, File.ReadAllText);

    private static int Count(string source, string pattern) => Regex.Count(source, pattern);

    /// <summary>The source without <c>///</c> documentation lines.</summary>
    private static string Code(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
