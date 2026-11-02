using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2, §4, §4.1, §5.4 and §8 N3 (rev-3 M-A/M-B, rev-4 L2, rev-5 F3/F4),
/// checked on compiled IL of every built <c>src</c> assembly:
/// <list type="bullet">
/// <item>no <c>src</c> code outside <c>Native/</c> names an N3 type, except the Execution stage machine naming the
/// allow-listed stage-C values (N4a), and Execution never names the launcher, the spec, the binding or a compiled command;</item>
/// <item>the binding, the command hierarchy (base and every nested record), the query outcome and the spec have only
/// private constructors; the command and candidate-value hierarchies are closed;</item>
/// <item>every N3 factory and hold-release hook has exactly its pinned caller; since N4b the command factories, the three
/// client launches and the stage-C mapping are called only by the native item's work;</item>
/// <item>the N3 files never build a <c>ProcessStartInfo</c>, start a process directly, or read ambient state.</item>
/// </list>
/// The test-only seams (spec factory, fault entry, observer seam, derived token) stay banned by their own tests.
/// </summary>
[TestClass]
public sealed class ClientQueriesBoundaryTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string Binding = Native + "ChocolateyHeldClientBinding";
    private const string Held = Native + "ChocolateyHeldProfileFiles";
    private const string Command = Native + "ChocolateyCompiledCommand";
    private const string Outcome = Native + "ChocolateyQueryOutcome`1";
    private const string Values = Native + "ChocolateyCandidateValues";
    private const string Spec = Native + "NativeLaunchSpec";
    private const string LaunchJob = Native + "LaunchJob";
    private const string ItemWork = Native + "NativeChocolateyHelperItem+NativeWork";

    private static readonly string[] N3Types =
    [
        "ChocolateyHeldClientBinding", "ChocolateyCompiledCommand", "ChocolateyExactListArguments", "ChocolateyClientQueries",
        "ChocolateyQueryParser", "ChocolateyQueryOutcome`1", "ChocolateyQueryStatus", "ChocolateyNativeOutdatedRecord",
        "ChocolateyCandidateValues", "ChocolateyCandidateRefusal", "ChocolateyStageCBaseline", "ChocolateyStageCMapping",
        "LaunchJob", "NativeLaunchSpec",
    ];

    private static readonly string[] ExecutionBanned = ["LaunchJob", "NativeLaunchSpec", "ChocolateyCompiledCommand", "ChocolateyHeldClientBinding"];

    /// <summary>
    /// N4a (design rev 6 §2, §5.4): the stage-C value record and its reason are on the port allow-list, so the Execution stage
    /// machine (and only it) may name them; <c>HelperEndpointBoundaryTests</c> pins the whole allow-list.
    /// </summary>
    private static readonly string[] PortValueTypes = ["ChocolateyCandidateValues", "ChocolateyCandidateRefusal"];
    private const string Endpoint = "PCSteward.Execution.ReviewedHelperEndpoint";

    private static readonly string[] N3Files =
    [
        "ChocolateyHeldClientBinding.cs", "ChocolateyCompiledCommand.cs", "ChocolateyExactListArguments.cs", "ChocolateyClientQueries.cs",
        "ChocolateyCandidateValues.cs",
    ];

    private static readonly string[] PrivatelyConstructed =
    [
        Binding, Command, Command + "+ListExact", Command + "+Outdated", Command + "+Upgrade", Outcome, Spec,
        // ADR-031: the client self-update commands.
        Command + "+UpgradeClient", Command + "+ListClient",
    ];

    /// <summary>(target type, member) → the exact set of source-level callers allowed in <c>src</c> (empty: none yet).</summary>
    private static readonly (string Type, string Member, string[] Callers)[] Pinned =
    [
        (Binding, "Create", [Native + "ChocolateyProfileObserver+Observation.Evaluate"]),
        (Held, "ReleaseCode", [Binding + ".ReleaseCodeAfterResume"]),
        (Binding, "ReleaseCodeAfterResume", [Spec + ".OnResumed"]),
        // ADR-031 design §1: only the client self-update binding releases every hold, only from the resumed launch.
        (Binding, "CreateForClientUpdate", [Native + "ChocolateyProfileObserver+Observation.Evaluate"]),
        (Held, "ReleaseAll", [Binding + ".ReleaseAllAfterResume"]),
        (Binding, "ReleaseAllAfterResume", [Spec + ".OnResumed"]),
        (Spec, "OnResumed", [LaunchJob + ".Run"]),
        (Spec, "get_MayCreateProcess", [LaunchJob + ".Run"]),
        // ADR-031 design §2 R5 and R3: the pre-creation setup-override check and the release-fault record, launcher only.
        (Spec, "SetupEnvironmentPermitsLaunch", [LaunchJob + ".Run"]),
        (Spec, "OnReleaseFaulted", [LaunchJob + ".Run"]),
        (Binding, "RecordReleaseIncomplete", [Spec + ".OnReleaseFaulted"]),
        (Binding, "RecordSetupAccessOverrideRefused", [Spec + ".SetupEnvironmentPermitsLaunch"]),
        (Native + "ChocolateySetupEnvironmentReader", "Read", [Spec + ".SetupEnvironmentPermitsLaunch", Native + "ChocolateyProfileObserver+Observation.Evaluate"]),
        (Native + "ChocolateySetupEnvironmentGuard", "Permits", [Spec + ".SetupEnvironmentPermitsLaunch", Native + "ChocolateyProfileObserver+Observation.Evaluate"]),
        (Command + "+ListExact", "TryCreate", [Command + ".TryListExact"]),
        (Command + "+Outdated", "Create", [Command + ".CreateOutdated"]),
        (Command + "+Upgrade", "TryCreate", [Command + ".TryUpgrade"]),
        (Native + "ChocolateyExactListArguments", "Build", [Command + "+ListExact.TryCreate"]),
        // ADR-031 design §3.5: the client commands come only from their list-bound builders. Design §7.1: the native item's
        // stage B is their only caller; nothing admits the route (A3's allow-list and HasSupportedMode still refuse it).
        (Command + "+UpgradeClient", "TryCreate", [Command + ".TryUpgradeClient"]),
        (Command + "+ListClient", "TryCreate", [Command + ".TryListClient"]),
        (Native + "ChocolateyClientUpdateArguments", "BuildUpgrade", [Command + "+UpgradeClient.TryCreate"]),
        (Native + "ChocolateyClientUpdateArguments", "BuildList", [Command + "+ListClient.TryCreate"]),
        (Command, "TryUpgradeClient", [ItemWork + ".ObserveProfile"]),
        (Command, "TryListClient", [ItemWork + ".ObserveProfile"]),
        (Outcome, "Create", [Native + "ChocolateyQueryParser.ParseListExact", Native + "ChocolateyQueryParser.ParseOutdated"]),
        // N4b (design rev 6 §4, §5.4): the native item owns the commands, the launches and the stage-C decision.
        (Command, "TryListExact", [ItemWork + ".ObserveProfile"]),
        (Command, "CreateOutdated", [ItemWork + ".QueryCandidateAsync"]),
        (Command, "TryUpgrade", [ItemWork + ".ObserveProfile"]),
        (Native + "ChocolateyClientQueries", "ListExactAsync", [ItemWork + ".QueryCandidateAsync"]),
        (Native + "ChocolateyClientQueries", "OutdatedAsync", [ItemWork + ".QueryCandidateAsync"]),
        (Native + "ChocolateyClientQueries", "UpgradeAsync", [ItemWork + ".UpgradeAsync"]),
        (Native + "ChocolateyClientQueries", "ListClientAsync", [ItemWork + ".QueryClientAsync"]),
        (Native + "ChocolateyClientQueries", "UpgradeClientAsync", [ItemWork + ".UpgradeAsync"]),
        (Native + "ChocolateyClientStageCMapping", "Map", [ItemWork + ".QueryClientAsync"]),
        (Native + "ChocolateyStageCMapping", "Map", [ItemWork + ".QueryCandidateAsync"]),
        (Native + "ChocolateyStageCMapping", "AfterList", [Native + "ChocolateyStageCMapping.Map", ItemWork + ".QueryCandidateAsync"]),
    ];

    [TestMethod]
    public void NoCompiledSrcCodeOutsideNativeNamesAnN3TypeAndExecutionNeverNamesTheLauncherSurface()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        Assert.IsTrue(assemblies.Count >= 5, "src assemblies were not built");
        Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution"), "Execution was not built");
        List<string> violations = [];
        foreach (string path in assemblies)
        {
            bool chocolatey = Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey";
            bool executionAssembly = Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution";
            if (!chocolatey)
                foreach (string type in CompiledIlScanner.TypeReferences(path).Where(type => IsType(type, N3Types) && !(executionAssembly && IsType(type, PortValueTypes))))
                    violations.Add($"{Path.GetFileName(path)} references type {type}");
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path).Where(reference => IsType(reference.TargetType, N3Types)))
                if ((!chocolatey || !reference.Caller.StartsWith(Native, StringComparison.Ordinal)) &&
                    !(executionAssembly && reference.Caller == Endpoint && IsType(reference.TargetType, PortValueTypes)))
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} references {reference.TargetType}.{reference.TargetMember}");
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));

        string execution = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution");
        foreach (string file in Directory.EnumerateFiles(execution, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
            Assert.IsFalse(Regex.IsMatch(File.ReadAllText(file), @"\b(" + string.Join('|', ExecutionBanned) + @")\b"), file);
    }

    [TestMethod]
    public void TheBindingCommandsOutcomeAndSpecHaveOnlyPrivateConstructorsAndTheHierarchiesAreClosed()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        string chocolatey = Chocolatey(assemblies);
        Dictionary<string, List<(MethodAttributes Attributes, int Parameters)>> constructors = InstanceConstructors(chocolatey);
        foreach (string type in PrivatelyConstructed)
        {
            Assert.IsTrue(constructors.TryGetValue(type, out List<(MethodAttributes Attributes, int Parameters)>? access) && access.Count > 0,
                $"{type} has no constructor");
            // The abstract record base also has the compiler's protected copy constructor, which needs an existing instance.
            Assert.IsTrue(access.All(ctor => (ctor.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private ||
                type == Command && ctor.Parameters == 1 && (ctor.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family),
                $"{type} has a non-private constructor");
        }
        CollectionAssert.AreEquivalent(new[] { Command + "+ListExact", Command + "+Outdated", Command + "+Upgrade", Command + "+UpgradeClient",
                Command + "+ListClient" },
            assemblies.SelectMany(path => DerivedFrom(path, Command)).ToArray(), "the compiled command hierarchy is closed");
        CollectionAssert.AreEquivalent(new[] { Values + "+Matched", Values + "+Refused", Values + "+Undetermined", Values + "+OutOfOrder" },
            assemblies.SelectMany(path => DerivedFrom(path, Values)).ToArray(), "the candidate values are a closed kind");

        List<string> violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetMember == ".ctor" && PrivatelyConstructed.Contains(reference.TargetType) &&
                    reference.Caller != reference.TargetType.Split('+')[0])
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} constructs {reference.TargetType}");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void EveryN3FactoryAndHoldReleaseHookHasExactlyItsPinnedCompiledCallers()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        IReadOnlyList<(string Type, string Method)> defined = CompiledIlScanner.MethodDefinitions(Chocolatey(assemblies));
        foreach ((string type, string member, _) in Pinned)
            Assert.IsTrue(defined.Contains((type, member)), $"{type}.{member} is not defined; update the pin list");

        Dictionary<(string, string), HashSet<string>> seen = Pinned.ToDictionary(pin => (pin.Type, pin.Member), _ => new HashSet<string>());
        List<string> violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                foreach ((string type, string member, string[] callers) in Pinned)
                {
                    if (reference.TargetType != type || reference.TargetMember != member) continue;
                    if (callers.Contains(reference.LogicalCaller)) seen[(type, member)].Add(reference.LogicalCaller);
                    else violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls {type}.{member}");
                }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        foreach ((string type, string member, string[] callers) in Pinned)
            CollectionAssert.AreEquivalent(callers, seen[(type, member)].ToArray(), $"{type}.{member}");
    }

    [TestMethod]
    public void TheN3FilesNeverBuildAProcessStartInfoStartAProcessOrReadAmbientState()
    {
        string native = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey", "Native");
        string[] forbidden =
        [
            "ProcessStartInfo", "Process.Start", "UseShellExecute", "CreateProcess", "ShellExecute", "GetEnvironmentVariable",
            "ExpandEnvironmentVariables", "Environment.CurrentDirectory", "Registry", "File.Open", "FileStream", "FileAccess.Write",
            "TryResolveMachine", "MachineTrustedChocolatey", "ForTests",
        ];
        foreach (string name in N3Files)
        {
            // Code only: documentation may name the native calls the launcher makes (for example the L2 release point).
            string source = string.Join('\n', File.ReadAllText(Path.Combine(native, name)).Split('\n')
                .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));
            StringAssert.Contains(source, "namespace PCSteward.Provider.Chocolatey.Native;", name);
            foreach (string token in forbidden)
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{name} uses {token}");
            Assert.IsFalse(source.Contains("sqlite", StringComparison.OrdinalIgnoreCase), $"{name} uses SQLite");
            Assert.IsFalse(Regex.IsMatch(source, @"\bpublic\s+((sealed|static|abstract|partial|readonly|record)\s+)*(class|struct|record|enum|interface)\b"),
                $"{name} declares a public type");
        }
    }

    private static bool IsType(string fullName, string[] names) =>
        fullName.StartsWith(Native, StringComparison.Ordinal) && names.Contains(fullName[Native.Length..].Split('+')[0]);

    private static string Chocolatey(List<string> assemblies) =>
        assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");

    private static Dictionary<string, List<(MethodAttributes Attributes, int Parameters)>> InstanceConstructors(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        Dictionary<string, List<(MethodAttributes, int)>> result = new(StringComparer.Ordinal);
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            string name = CompiledIlScanner.FullName(md, handle);
            if (!name.StartsWith(Native, StringComparison.Ordinal)) continue;
            List<(MethodAttributes, int)> access = [];
            foreach (MethodDefinitionHandle methodHandle in md.GetTypeDefinition(handle).GetMethods())
            {
                MethodDefinition method = md.GetMethodDefinition(methodHandle);
                if (md.GetString(method.Name) == ".ctor") access.Add((method.Attributes, method.GetParameters().Count));
            }
            result[name] = access;
        }
        return result;
    }

    private static List<string> DerivedFrom(string path, string baseFullName)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        string simple = baseFullName[(baseFullName.LastIndexOf('.') + 1)..];
        List<string> derived = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            EntityHandle baseType = md.GetTypeDefinition(handle).BaseType;
            if (baseType.IsNil) continue;
            string? name = baseType.Kind switch
            {
                HandleKind.TypeDefinition => CompiledIlScanner.FullName(md, (TypeDefinitionHandle)baseType),
                HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)baseType).Name),
                _ => null,
            };
            if (name == baseFullName || name == simple) derived.Add(CompiledIlScanner.FullName(md, handle));
        }
        return derived;
    }
}
