using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §2 (rev-4 L1, rev-5 F1), §5.1–§5.4 and §8 N4a, checked on
/// compiled IL of every built <c>src</c> assembly and on source text:
/// <list type="bullet">
/// <item>the only <c>ReviewedUpdateWireCodec.CreateResponse</c> call site in <c>src</c> is the endpoint (besides the codec's
/// own canonical re-encode), and the endpoint never references the protocol-1 <c>ElevatedHelperProtocol</c>;</item>
/// <item>Execution references only the §5.4 allow-list of native types, and only from the endpoint;</item>
/// <item>the stage and upgrade value records are closed kinds; the port has no <c>src</c> implementation until N4b;</item>
/// <item>nothing calls the endpoint yet and the protocol-1 bootstrap still refuses <c>--protocol 2</c> (exit code 20).</item>
/// </list>
/// </summary>
[TestClass]
public sealed class HelperEndpointBoundaryTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string Endpoint = "PCSteward.Execution.ReviewedHelperEndpoint";
    private const string Codec = "PCSteward.Execution.ReviewedUpdateWireCodec";
    private const string StageValues = Native + "ChocolateyStageValues";
    private const string UpgradeResult = Native + "ChocolateyUpgradeResult";

    /// <summary>
    /// §2 allow-list: the port interfaces of §5.4 plus the value records and their reason enums (rev-5 F1). The two reason
    /// enums are carried by the value records; they are named here explicitly so nothing else can slip in.
    /// </summary>
    private static readonly string[] AllowList =
    [
        "IChocolateyHelperQualificationPort", "IChocolateyPeer", "IChocolateyHelperItem", "ChocolateyStageValues", "ChocolateyCandidateValues",
        "ChocolateyUpgradeResult", "NativeProcessObservation", "NativeInvocation", "ChocolateyItemRefusal", "ChocolateyStageRefusal",
        "ChocolateyCandidateRefusal",
    ];

    private static readonly string[] PortCalls =
    [
        "IChocolateyHelperQualificationPort.BeginItem", "IChocolateyHelperItem.ObserveProfile", "IChocolateyHelperItem.QueryCandidateAsync",
        "IChocolateyHelperItem.RevalidateBeforeLaunch", "IChocolateyHelperItem.UpgradeAsync", "IChocolateyHelperItem.get_AggregateSettled",
        "IChocolateyHelperItem.RequesterCannotModify", "IChocolateyPeer.TryReadIdentity", "IChocolateyPeer.get_HasExited",
    ];

    private static readonly string[] PortInterfaces = [Native + "IChocolateyHelperQualificationPort", Native + "IChocolateyPeer", Native + "IChocolateyHelperItem"];

    [TestMethod]
    public void TheOnlyCreateResponseCallSiteInSrcIsTheEndpoint()
    {
        List<string> assemblies = RequireBuilt();
        HashSet<string> callers = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == Codec && reference.TargetMember == "CreateResponse") callers.Add(reference.LogicalCaller);
        // TryReadResponse re-encodes the frame it read to prove canonical form; the endpoint is the only producer.
        CollectionAssert.AreEquivalent(new[] { Codec + ".TryReadResponse", Endpoint + ".RunAsync" }, callers.ToArray());

        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        foreach (string path in Sources(src))
        {
            string name = Path.GetFileName(path);
            if (name is "ReviewedUpdateWireCodec.cs" or "ReviewedHelperEndpoint.cs") continue;
            Assert.IsFalse(Regex.IsMatch(File.ReadAllText(path), @"ReviewedUpdateWireCodec\s*\.\s*CreateResponse\b"), $"{path} creates a protocol-2 response");
        }
    }

    [TestMethod]
    public void TheEndpointNeverReferencesProtocolOne()
    {
        List<string> assemblies = RequireBuilt();
        string execution = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution");
        string[] protocolOne =
        [
            "PCSteward.Execution.ElevatedHelperProtocol", "PCSteward.Execution.OneShotHelperRequestValidator", "PCSteward.Execution.HelperBootstrap",
            "PCSteward.Execution.ElevatedHelperResult",
        ];
        List<string> violations = [.. CompiledIlScanner.References(execution)
            .Where(reference => reference.Caller == Endpoint && protocolOne.Contains(reference.TargetType))
            .Select(reference => $"{reference.LogicalCaller} references {reference.TargetType}.{reference.TargetMember}")];
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));

        string source = EndpointSource();
        foreach (string token in new[] { "ElevatedHelperProtocol", "OneShotHelperRequestValidator", "HelperBootstrap", "ImmutableUpdateJob" })
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"the endpoint names {token}");
    }

    [TestMethod]
    public void ExecutionReferencesOnlyTheAllowListedNativeTypesAndOnlyFromTheEndpoint()
    {
        List<string> assemblies = RequireBuilt();
        List<string> violations = [];
        foreach (string path in assemblies)
        {
            string assembly = Path.GetFileNameWithoutExtension(path);
            if (assembly == "PCSteward.Provider.Chocolatey") continue;
            foreach (string type in CompiledIlScanner.TypeReferences(path).Where(type => type.StartsWith(Native, StringComparison.Ordinal)))
                if (assembly != "PCSteward.Execution" || !IsAllowListed(type)) violations.Add($"{assembly} references type {type}");
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path).Where(reference => reference.TargetType.StartsWith(Native, StringComparison.Ordinal)))
                if (assembly != "PCSteward.Execution" || reference.Caller != Endpoint || !IsAllowListed(reference.TargetType))
                    violations.Add($"{assembly}: {reference.Caller} references {reference.TargetType}.{reference.TargetMember}");
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));

        // The scanner sees the endpoint's port calls (a self-check that the allow-list test is not vacuous).
        string execution = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution");
        string[] called = [.. CompiledIlScanner.References(execution).Where(reference => reference.Caller == Endpoint)
            .Select(reference => reference.TargetType + "." + reference.TargetMember)];
        foreach (string member in PortCalls)
            CollectionAssert.Contains(called, Native + member);
        CollectionAssert.DoesNotContain(called, Native + "IChocolateyHelperQualificationPort.OpenPeer", "OpenPeer belongs to the N4b bootstrap");

        // Source: exactly one Execution file names the Native namespace.
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution");
        string[] naming = [.. Sources(src).Where(path => File.ReadAllText(path).Contains("PCSteward.Provider.Chocolatey.Native", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))];
        Assert.AreEqual("ReviewedHelperEndpoint.cs", string.Join(", ", naming));
    }

    [TestMethod]
    public void TheStageAndUpgradeValuesAreClosedKindsAndThePortHasNoSrcImplementationYet()
    {
        List<string> assemblies = RequireBuilt();
        string chocolatey = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        CollectionAssert.AreEquivalent(new[] { StageValues + "+Observed", StageValues + "+Refused", StageValues + "+TimedOut", StageValues + "+Cancelled", StageValues + "+OutOfOrder" },
            assemblies.SelectMany(path => DerivedFrom(path, StageValues)).ToArray(), "the stage values are a closed kind");
        CollectionAssert.AreEquivalent(new[] { UpgradeResult + "+Observed", UpgradeResult + "+OutOfOrder" },
            assemblies.SelectMany(path => DerivedFrom(path, UpgradeResult)).ToArray(), "the upgrade result is a closed record");
        foreach (string type in new[] { StageValues, UpgradeResult })
            foreach ((MethodAttributes attributes, int parameters) in Constructors(chocolatey, type))
                Assert.IsTrue((attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private ||
                    parameters == 1 && (attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family, $"{type} has a non-private constructor");

        // §5.4 single-implementation rule: N4b adds exactly one native implementation of each; N4a adds none.
        List<string> implementations = [];
        foreach (string path in assemblies)
            implementations.AddRange(Implementations(path, PortInterfaces).Select(type => $"{Path.GetFileName(path)}: {type}"));
        Assert.AreEqual(0, implementations.Count, string.Join(Environment.NewLine, implementations));
        // The helper-side G1 seam has no src implementation either until the N4b Helper-role reader.
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        foreach (string path in Sources(src))
            Assert.IsFalse(Regex.IsMatch(File.ReadAllText(path), @"\b(class|record|struct)\s+\w+[^{;]*:[^{;]*\bIReviewedHelperCapabilityCheck\b"), path);
    }

    [TestMethod]
    public void TheEndpointIsNotWiredAndProtocolTwoStillExitsWithCode20()
    {
        List<string> assemblies = RequireBuilt();
        List<string> callers = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == Endpoint && reference.TargetMember == "RunAsync" && reference.Caller != Endpoint)
                    callers.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller}");
        Assert.AreEqual(0, callers.Count, string.Join(Environment.NewLine, callers));

        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string transport = File.ReadAllText(Path.Combine(src, "PCSteward.Execution", "WindowsElevatedHelperTransport.cs"));
        string protocol = File.ReadAllText(Path.Combine(src, "PCSteward.Execution", "ElevatedHelperProtocol.cs"));
        Assert.IsFalse(transport.Contains("ReviewedHelperEndpoint", StringComparison.Ordinal), "the entry point dispatches to protocol 2 (N4b)");
        StringAssert.Contains(transport, "fields[\"--protocol\"] != ElevatedHelperProtocol.Version");
        StringAssert.Contains(protocol, "internal const string Version = \"1\";");
        Assert.IsTrue(Regex.IsMatch(transport, @"bootstrap is null \|\|\s*!IsElevated\(\)\)\s*\{\s*return 20;"), "a refused bootstrap exits with code 20");
        foreach (string path in Sources(Path.Combine(src, "PCSteward.ElevatedHelper")))
            Assert.IsFalse(File.ReadAllText(path).Contains("ReviewedHelperEndpoint", StringComparison.Ordinal), path);
    }

    [TestMethod]
    public void TheEndpointReadsNoAmbientStateAndUpgradesAtOneCallSite()
    {
        string source = EndpointSource();
        foreach (string token in new[]
        {
            "Sqlite", "Registry", "GetEnvironmentVariable", "ExpandEnvironmentVariables", "Environment.", "File.", "Directory.",
            "System.Diagnostics", "ProcessStartInfo", "Process.", "HttpClient", "DateTimeOffset.UtcNow", "DateTime.Now", "DateTime.UtcNow", "Stopwatch", "Success",
        })
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"the endpoint uses {token}");
        Assert.AreEqual(1, Regex.Count(source, @"\.UpgradeAsync\("), "exactly one upgrade call site");
        Assert.AreEqual(1, Regex.Count(source, @"PipeMessageFraming\.WriteAsync\("), "exactly one response write");
        Assert.AreEqual(1, Regex.Count(source, @"ReviewedUpdateWireCodec\.CreateResponse\("), "the response is built only through the codec");
    }

    private static bool IsAllowListed(string fullName) =>
        fullName.StartsWith(Native, StringComparison.Ordinal) && AllowList.Contains(fullName[Native.Length..].Split('+')[0]);

    private static List<string> RequireBuilt()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        foreach (string required in new[] { "PCSteward.Provider.Chocolatey", "PCSteward.Execution", "PCSteward.ElevatedHelper" })
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required} was not built");
        return assemblies;
    }

    private static string EndpointSource()
    {
        string path = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution", "ReviewedHelperEndpoint.cs");
        // Code only: documentation may name what the endpoint never does.
        return string.Join('\n', File.ReadAllText(path).Split('\n').Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal) &&
            !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    private static IEnumerable<string> Sources(string root) => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private static List<(MethodAttributes Attributes, int Parameters)> Constructors(string path, string fullName)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<(MethodAttributes, int)> result = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            if (CompiledIlScanner.FullName(md, handle) != fullName) continue;
            foreach (MethodDefinitionHandle methodHandle in md.GetTypeDefinition(handle).GetMethods())
            {
                MethodDefinition method = md.GetMethodDefinition(methodHandle);
                if (md.GetString(method.Name) == ".ctor") result.Add((method.Attributes, method.GetParameters().Count));
            }
        }
        Assert.IsTrue(result.Count > 0, $"{fullName} has no constructor");
        return result;
    }

    private static List<string> DerivedFrom(string path, string baseFullName)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<string> derived = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
            if (TypeName(md, md.GetTypeDefinition(handle).BaseType) == baseFullName) derived.Add(CompiledIlScanner.FullName(md, handle));
        return derived;
    }

    private static List<string> Implementations(string path, string[] interfaces)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<string> found = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
            foreach (InterfaceImplementationHandle implementation in md.GetTypeDefinition(handle).GetInterfaceImplementations())
                if (interfaces.Contains(TypeName(md, md.GetInterfaceImplementation(implementation).Interface)))
                    found.Add(CompiledIlScanner.FullName(md, handle));
        return found;
    }

    private static string? TypeName(MetadataReader md, EntityHandle handle)
    {
        if (handle.IsNil) return null;
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => CompiledIlScanner.FullName(md, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => ReferenceName(md, (TypeReferenceHandle)handle),
            _ => null,
        };
    }

    private static string ReferenceName(MetadataReader md, TypeReferenceHandle handle)
    {
        TypeReference reference = md.GetTypeReference(handle);
        string name = md.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference) return ReferenceName(md, (TypeReferenceHandle)reference.ResolutionScope) + "+" + name;
        string ns = md.GetString(reference.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
