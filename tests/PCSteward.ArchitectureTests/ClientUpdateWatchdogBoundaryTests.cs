using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-031 design §2.1 R5 runtime watchdog (owner option 1), checked on source and compiled IL:
/// <list type="bullet">
/// <item>the watchdog is used only by the client self-update route: its arming and start are called only by the launcher,
/// its production targets come only from the client upgrade spec, and the fault reaches only the client binding;</item>
/// <item>its test-only seams (test targets and their watch-thread factory, test keys, the watchdog launch entry) and the
/// postcheck's free-parameter overload (review L3: <c>src</c> must pass the client binding) have no <c>src</c> caller;</item>
/// <item>review L2: an incomplete read whose event is signalled is a failed re-arm, never a busy-spinning continue;</item>
/// <item>its file is read-only: exactly two directory opens (<c>FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES</c> for the
/// change watch, <c>READ_CONTROL | FILE_READ_ATTRIBUTES</c> for the access re-check), both with full sharing and
/// <c>OPEN_EXISTING</c>, one <c>ReadDirectoryChangesW</c> filter (<c>FILE_NOTIFY_CHANGE_SECURITY</c>), no write right,
/// no registry token (registry access stays in the single pinned reader, <c>KEY_NOTIFY</c>/<c>KEY_QUERY_VALUE</c> only);</item>
/// <item>no <c>src</c> Native file writes a registry value or a security descriptor.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ClientUpdateWatchdogBoundaryTests
{
    private const string WatchdogFile = "ChocolateyClientUpdateWatchdog.cs";
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string Watchdog = Native + "ChocolateyClientUpdateWatchdog";
    private const string Targets = Native + "ChocolateyClientWatchTargets";
    private const string Reader = Native + "ChocolateySetupEnvironmentReader";
    private const string Spec = Native + "NativeLaunchSpec";
    private const string Binding = Native + "ChocolateyHeldClientBinding";
    private const string LaunchJob = Native + "LaunchJob";
    private const string Postcheck = Native + "ChocolateyClientUpdatePostcheck";

    private static readonly string[] WatchdogTypes =
    [
        "ChocolateyClientUpdateWatchdog", "ChocolateyClientWatchTargets", "ChocolateyClientWatchMonitor", "ChocolateyClientWatchdogRule",
        "ChocolateyClientWatchWake", "ChocolateyClientWatchdogFault",
    ];

    private static readonly string[] DeclaredNames = ["CreateFile", "ReadDirectoryChanges", "GetOverlappedResult", "CancelIoEx"];
    private static readonly string[] EntryPoints = ["CreateFileW", "ReadDirectoryChangesW"];

    private static readonly (string Type, string Member)[] TestOnly =
    [
        (Targets, "CreateForTests"),
        (Reader, "TryWatchForTests"),
        (LaunchJob, "RunCoreWithWatchdogForTestsAsync"),
        (Postcheck, "EvaluateForTests"),
        // ADR-031 design §8.6 gate 2: the binding's test environment-key watch.
        (Targets, "UseEnvironmentWatchForTests"),
    ];

    /// <summary>(target type, member) → the exact set of source-level callers in <c>src</c>.</summary>
    private static readonly (string Type, string Member, string[] Callers)[] Pinned =
    [
        (Watchdog, "TryArm", [LaunchJob + ".Run"]),
        (Watchdog, "PermitsCreation", [LaunchJob + ".Run"]),
        (Watchdog, "Start", [LaunchJob + ".Run"]),
        (Spec, "WatchTargets", [LaunchJob + ".Run"]),
        (Spec, "OnWatchdogStopped", [LaunchJob + ".Run"]),
        (Targets, "FromBinding", [Spec + ".WatchTargets"]),
        (Binding, "RecordWatchdogFault", [Spec + ".OnWatchdogStopped"]),
        (Reader, "TryWatch", [Targets + ".FromBinding"]),
        (Targets, "TestEnvironmentWatch", [Targets + ".FromBinding"]),
        (Targets, "get_WatchThreadForTests", [Watchdog + ".Start"]),
    ];

    [TestMethod]
    public void TheWatchdogIsUsedOnlyByTheClientRouteAndItsTestSeamsHaveNoSrcCaller()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        string chocolatey = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        IReadOnlyList<(string Type, string Method)> defined = CompiledIlScanner.MethodDefinitions(chocolatey);
        foreach ((string type, string member) in TestOnly)
            Assert.IsTrue(defined.Contains((type, member)), $"{type}.{member} is not defined; update the ban list");
        foreach ((string type, string member, _) in Pinned)
            Assert.IsTrue(defined.Contains((type, member)), $"{type}.{member} is not defined; update the pin list");

        Dictionary<(string, string), HashSet<string>> seen = Pinned.ToDictionary(pin => (pin.Type, pin.Member), _ => new HashSet<string>());
        List<string> violations = [];
        foreach (string path in assemblies)
        {
            bool isChocolatey = path == chocolatey;
            if (!isChocolatey)
                foreach (string type in CompiledIlScanner.TypeReferences(path).Where(IsWatchdogType))
                    violations.Add($"{Path.GetFileName(path)} references type {type}");
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (IsWatchdogType(reference.TargetType) && (!isChocolatey || !reference.Caller.StartsWith(Native, StringComparison.Ordinal)))
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} references {reference.TargetType}.{reference.TargetMember}");
                foreach ((string type, string member) in TestOnly)
                    if (reference.TargetType == type && reference.TargetMember == member && reference.Caller != type)
                        violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls test-only {type}.{member}");
                foreach ((string type, string member, string[] callers) in Pinned)
                {
                    if (reference.TargetType != type || reference.TargetMember != member) continue;
                    if (callers.Contains(reference.LogicalCaller)) seen[(type, member)].Add(reference.LogicalCaller);
                    else violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls {type}.{member}");
                }
            }
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        foreach ((string type, string member, string[] callers) in Pinned)
            CollectionAssert.AreEquivalent(callers, seen[(type, member)].ToArray(), $"{type}.{member}");

        // Only the client upgrade spec yields targets (the package route is never watched).
        string spec = Code(File.ReadAllText(Path.Combine(NativeDirectory(), "NativeLaunchSpec.cs")));
        StringAssert.Contains(spec, "releaseAllAfterResume ? ChocolateyClientWatchTargets.FromBinding(client!) : null;");
        string targets = Code(File.ReadAllText(Path.Combine(NativeDirectory(), WatchdogFile)));
        StringAssert.Contains(targets, "client.Purpose != ChocolateyBindingPurpose.ClientSelfUpdate");
        // Production targets never carry a watch-thread factory (M2d seam): only CreateForTests can pass one.
        Assert.IsTrue(Regex.IsMatch(targets, @"ChocolateySetupEnvironmentReader\.TryWatch,\s+watchThreadForTests: null\);"),
            "FromBinding passes no watch-thread factory");
        Assert.AreEqual(1, Regex.Count(targets, @"watchThreadForTests: null"), "only FromBinding names the factory as null");
        // L3: the binding overload reads the fault and release state from the client self-update binding itself.
        string postcheck = Code(File.ReadAllText(Path.Combine(NativeDirectory(), "ChocolateyClientUpdatePostcheck.cs")));
        StringAssert.Contains(postcheck, "client.Purpose != ChocolateyBindingPurpose.ClientSelfUpdate || client.InstalledRelease is not { } source");
        StringAssert.Contains(postcheck, "return EvaluateCore(result, source, target, requestingUserSid, client.WatchdogFault, client.ReleaseIncomplete);");
    }

    [TestMethod]
    public void TheWatchdogOpensDirectoriesReadOnlyWithFullSharingAndWritesNothing()
    {
        string code = Code(File.ReadAllText(Path.Combine(NativeDirectory(), WatchdogFile)));

        foreach (string constant in new[]
                 {
                     "internal const uint FileListDirectory = 0x00000001;", "internal const uint FileReadAttributes = 0x00000080;",
                     "internal const uint ReadControl = 0x00020000;", "internal const uint WatchOpenAccess = FileListDirectory | FileReadAttributes;",
                     "internal const uint SecurityReadAccess = ReadControl | FileReadAttributes;", "internal const uint FileShareAll = 0x00000007;",
                     "internal const uint OpenExisting = 3;", "internal const uint FileNotifyChangeSecurity = 0x00000100;",
                 })
            StringAssert.Contains(code, constant);
        string[] declared = [.. Regex.Matches(code, @"static\s+extern\s+\w+\s+(\w+)\s*\(").Select(match => match.Groups[1].Value)];
        CollectionAssert.AreEquivalent(DeclaredNames, declared);
        CollectionAssert.AreEquivalent(EntryPoints,
            Regex.Matches(code, @"EntryPoint\s*=\s*""(\w+)""").Select(match => match.Groups[1].Value).ToArray());
        Assert.AreEqual(4, Regex.Count(code, @"\[DllImport\(""kernel32\.dll"""), "kernel32 only");

        // Exactly two opens: the change watch and the access re-check, both full-share OPEN_EXISTING without reparse traversal.
        MatchCollection opens = Regex.Matches(code, @"WatchdogNativeMethods\.CreateFile\(([^;]*);");
        Assert.AreEqual(2, opens.Count);
        Assert.AreEqual(1, opens.Count(open => open.Groups[1].Value.Contains("WatchdogNativeMethods.WatchOpenAccess,", StringComparison.Ordinal)));
        Assert.AreEqual(1, opens.Count(open => open.Groups[1].Value.Contains("WatchdogNativeMethods.SecurityReadAccess,", StringComparison.Ordinal)));
        foreach (Match open in opens)
        {
            StringAssert.Contains(open.Groups[1].Value, "WatchdogNativeMethods.FileShareAll, IntPtr.Zero, WatchdogNativeMethods.OpenExisting,");
            StringAssert.Contains(open.Groups[1].Value, "WatchdogNativeMethods.FileFlagBackupSemantics | WatchdogNativeMethods.FileFlagOpenReparsePoint");
        }
        Assert.AreEqual(1, Regex.Count(code, @"WatchdogNativeMethods\.ReadDirectoryChanges\(handle, buffer, BufferBytes, subtree, WatchdogNativeMethods\.FileNotifyChangeSecurity,"),
            "one security-only change read");
        Assert.AreEqual(2, Regex.Count(code, @"DirectoryWatch\.TryArm\("), "root and parent watches only");
        StringAssert.Contains(code, "DirectoryWatch.TryArm(targets.Root, subtree: true)");
        StringAssert.Contains(code, "DirectoryWatch.TryArm(targets.Parent, subtree: false)");
        // Review L2: TryRearm runs only after the manual-reset event fired; an incomplete read there fails closed (no spin).
        StringAssert.Contains(code, "if (error == ErrorIoIncomplete) return false;");
        Assert.IsFalse(code.Contains("ErrorIoIncomplete) return true", StringComparison.Ordinal), "an incomplete read must not continue");

        foreach (string token in new[]
                 {
                     "Registry", "RegOpenKey", "RegSetValue", "RegCreateKey", "RegDelete", "RegNotify", "GenericWrite", "GenericAll", "FileWrite",
                     "WriteDac", "WriteOwner", "Delete", "0x10000000", "SetSecurityInfo", "SetNamedSecurityInfo",
                     "SetFileSecurity", "SetAccessControl", "AddAccessRule", "WriteFile", "MoveFile", "RemoveDirectory", "CreateDirectory",
                     "SetFileInformationByHandle", "File.Open", "FileStream", "FileAccess.Write", "ProcessStartInfo", "Process.Start",
                     "CreateProcess", "Impersonat", "GetEnvironmentVariable", "SetEnvironmentVariable",
                 })
            Assert.IsFalse(code.Contains(token, StringComparison.Ordinal), $"the watchdog uses {token}");
        // The job is ended only through the launcher's terminator; the watchdog never names a process or the job itself.
        Assert.IsFalse(Regex.IsMatch(code, @"\bTerminate(JobObject|Process)\b"), "the watchdog calls no termination API directly");
    }

    [TestMethod]
    public void NoNativeSrcFileWritesARegistryValueOrASecurityDescriptor()
    {
        string[] writes =
        [
            "RegSetValue", "RegSetKeyValue", "RegCreateKey", "RegDeleteValue", "RegDeleteKey", "RegDeleteTree", "RegSetKeySecurity",
            "RegRestoreKey", "RegLoadKey", "SetSecurityInfo", "SetNamedSecurityInfo", "SetFileSecurity", "SetKernelObjectSecurity",
            "SetSecurityDescriptorDacl", "SetAccessControl", "AddAccessRule", "SetAccessRule", "RemoveAccessRule", "SetOwner(",
            "TreeSetNamedSecurityInfo", "Microsoft.Win32.Registry",
        ];
        int scanned = 0;
        foreach (string path in Directory.EnumerateFiles(NativeDirectory(), "*.cs"))
        {
            scanned++;
            string code = Code(File.ReadAllText(path));
            foreach (string token in writes)
                Assert.IsFalse(code.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(path)} uses {token}");
        }
        Assert.IsTrue(scanned > 30, "the Native directory was not scanned");
        // Registry change notification lives only in the pinned reader (KEY_NOTIFY / KEY_QUERY_VALUE only, pinned there).
        foreach (string path in Directory.EnumerateFiles(NativeDirectory(), "*.cs").Where(path => Path.GetFileName(path) != SetupEnvironmentReaderBoundaryTests.ReaderFile))
            Assert.IsFalse(Code(File.ReadAllText(path)).Contains("RegNotifyChangeKeyValue", StringComparison.Ordinal), path);
        foreach (string path in Directory.EnumerateFiles(NativeDirectory(), "*.cs").Where(path => Path.GetFileName(path) != WatchdogFile))
        {
            string code = Code(File.ReadAllText(path));
            Assert.IsFalse(code.Contains("ReadDirectoryChanges", StringComparison.Ordinal) || code.Contains("FindFirstChangeNotification", StringComparison.Ordinal),
                $"{path} watches a directory outside the watchdog");
        }
    }

    private static bool IsWatchdogType(string fullName) =>
        fullName.StartsWith(Native, StringComparison.Ordinal) && WatchdogTypes.Contains(fullName[Native.Length..].Split('+')[0]);

    private static string Code(string source) => string.Join('\n', source.Split('\n')
        .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string NativeDirectory() => Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey", "Native");
}
