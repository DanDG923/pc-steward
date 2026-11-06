using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §4.4 and §7 (slice S2), checked on compiled IL of every
/// built <c>src</c> assembly and on source text: the probe response codec is wired on the helper side (S3, the only producer,
/// <c>ReviewedHelperProbeEndpoint</c>) and on the desktop side (S4: the probe transport, the only reader, and the result
/// document) only. No other <c>src</c> code references them, so <c>PCSteward.Desktop</c> and <c>ScheduledHost</c> reach them only
/// through the S4 entry; the test-only contract factory has no <c>src</c> caller; there is no probe request
/// codec; and the codec names no native type. The admission pins (<c>HasSupportedMode</c>, <c>CapabilityOperationAllowlist</c>,
/// the dispatcher's <c>Supports</c>) were changed deliberately by the Task 50 activation and are pinned by
/// <c>ChocolateyRouteActivationBoundaryTests</c>.
/// </summary>
[TestClass]
public sealed class QualificationProbeWireBoundaryTests
{
    private const string Execution = "PCSteward.Execution.";
    private const string Codec = Execution + "QualificationProbeWireCodec";
    private const string Contract = Execution + "QualificationProbeContract";
    private const string CodecFile = "QualificationProbeWireCodec.cs";
    private static readonly string[] CodecFileOnly =
        ["QualificationProbeResultFile.cs", CodecFile, "ReviewedHelperProbeBootstrap.cs", "ReviewedHelperProbeEndpoint.cs", "ReviewedHelperProbeTransport.cs"];

    /// <summary>
    /// S4: the desktop probe types (Execution only) that consume the codec: the transport reads the one frame with
    /// <c>TryReadProbeResponse</c> and the result document copies fields 3–16 of an accepted response.
    /// </summary>
    private static readonly string[] DesktopConsumers =
    [
        Execution + "ReviewedHelperProbeTransport", Execution + "ReviewedHelperProbeSession", Execution + "ReviewedHelperProbeSessionResult",
        Execution + "QualificationProbeRun", Execution + "QualificationProbeResultDocument",
    ];

    /// <summary>S3: the probe entry is the one non-codec type that may reference the probe wire types.</summary>
    private const string ProbeEndpoint = Execution + "ReviewedHelperProbeEndpoint";

    /// <summary>Every S2 type (all declared in <see cref="CodecFile"/>).</summary>
    private static readonly string[] ProbeTypes =
    [
        "QualificationProbeWireCodec", "QualificationProbeFacts", "QualificationProbeContract", "QualificationProbeCapabilityEntry",
        "QualificationProbeTokenVerdict", "QualificationProbeCapabilityStatus", "QualificationProbeObservation", "QualificationProbeFailedCheck",
        "QualificationProbeFailedAccess", "QualificationProbeRefusalReason", "QualificationProbeRefusalDetail",
    ];

    private static bool IsProbeType(string fullName)
    {
        if (!fullName.StartsWith(Execution, StringComparison.Ordinal)) return false;
        string outer = fullName[Execution.Length..].Split('+')[0];
        return ProbeTypes.Contains(outer, StringComparer.Ordinal);
    }

    [TestMethod]
    public void NoCompiledSrcCodeOutsideTheProbeTypesReferencesThem()
    {
        List<string> assemblies = RequireBuilt();
        List<string> violations = [];
        int internalReferences = 0;
        foreach (string path in assemblies)
        {
            bool execution = Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution";
            foreach (string type in CompiledIlScanner.TypeReferences(path).Where(IsProbeType))
                violations.Add($"{Path.GetFileName(path)} references type {type}");
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path).Where(reference => IsProbeType(reference.TargetType)))
            {
                if (execution && IsProbeType(reference.Caller)) internalReferences++;
                else if (execution && reference.Caller == ProbeEndpoint) { }
                else if (execution && DesktopConsumers.Contains(reference.Caller, StringComparer.Ordinal)) { }
                else violations.Add($"{Path.GetFileName(path)}: {reference.Caller} references {reference.TargetType}.{reference.TargetMember}");
            }
        }
        // The scanner sees the codec's own references (the test is not vacuous).
        Assert.IsGreaterThan(0, internalReferences, "no in-codec probe reference resolved");
        string executionPath = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution");
        CollectionAssert.Contains(CompiledIlScanner.References(executionPath).Where(reference => reference.Caller == Codec)
            .Select(reference => reference.TargetType + "." + reference.TargetMember).ToArray(), Contract + ".get_Compiled");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void NoSourceFileOutsideTheCodecFileNamesAProbeType()
    {
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        Regex names = new(@"\b(" + string.Join('|', ProbeTypes) + @")\b", RegexOptions.CultureInvariant);
        string[] declaring = [.. Sources(src).Where(path => names.IsMatch(File.ReadAllText(path))).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        CollectionAssert.AreEqual(CodecFileOnly, declaring, "only the S2 codec file and the S3 helper probe files name the probe wire types");
        // The entry points S4 will wire stay untouched in S3; the helper's Program names no probe literal (Execution owns the parser).
        foreach (string project in new[] { "PCSteward.Desktop", "PCSteward.ElevatedHelper", "PCSteward.ScheduledHost" })
            foreach (string path in Sources(Path.Combine(src, project)))
                Assert.IsFalse(File.ReadAllText(path).Contains("qualify-chocolatey-helper", StringComparison.Ordinal), $"{path} names the probe kind");
    }

    [TestMethod]
    public void TheTestOnlyContractFactoryHasNoSrcCaller()
    {
        List<string> assemblies = RequireBuilt();
        string execution = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution");
        Assert.IsTrue(CompiledIlScanner.MethodDefinitions(execution).Contains((Contract, "CreateForTests")), "update the ban list");
        List<string> violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == Contract && reference.TargetMember == "CreateForTests")
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls {Contract}.CreateForTests");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void TheListedClientReleasesHaveExactlyOneSrcCallerTheContractStaticConstructor()
    {
        // Review L3: ListedVersions() feeds only the compiled Q10 contract; any new src caller needs review.
        const string Policy = "PCSteward.Provider.Chocolatey.ChocolateyClientReleasePolicy";
        List<string> assemblies = RequireBuilt();
        string provider = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        Assert.IsTrue(CompiledIlScanner.MethodDefinitions(provider).Contains((Policy, "ListedVersions")), "update the caller pin");
        List<string> callers = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == Policy && reference.TargetMember == "ListedVersions")
                    callers.Add($"{Path.GetFileNameWithoutExtension(path)}: {reference.CallerType}::{reference.CallerMethod}");
        CollectionAssert.AreEqual(new[] { "PCSteward.Execution: " + Contract + "::.cctor" }, callers.ToArray(), string.Join(Environment.NewLine, callers));
    }

    [TestMethod]
    public void ThereIsNoProbeRequestCodecAndTheCodecNamesNoNativeOrTransportType()
    {
        List<string> assemblies = RequireBuilt();
        string execution = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution");
        string[] methods = [.. CompiledIlScanner.MethodDefinitions(execution).Where(method => IsProbeType(method.Type)).Select(method => method.Method)];
        CollectionAssert.Contains(methods, "CreateProbeResponse");
        CollectionAssert.Contains(methods, "TryReadProbeResponse");
        Assert.IsFalse(methods.Any(method => method.Contains("Request", StringComparison.Ordinal)), "S2 adds no probe request codec");

        string[] referenced = [.. CompiledIlScanner.References(execution).Where(reference => IsProbeType(reference.Caller))
            .Select(reference => reference.TargetType + "." + reference.TargetMember)];
        string[] forbidden =
        [
            "PCSteward.Provider.Chocolatey.Native.", "PCSteward.Execution.PipeMessageFraming.", "PCSteward.Execution.ReviewedUpdateWireCodec.TryReadRequest",
            "PCSteward.Execution.ReviewedUpdateWireCodec.CreateRequest", "PCSteward.Execution.ReviewedUpdateWireCodec.CreateResponse",
            "PCSteward.Execution.ReviewedUpdateWireCodec.CreateClientResponse", "PCSteward.Execution.ReviewedUpdateWireCodec.TryReadResponse",
            "PCSteward.Execution.ReviewedHelperEndpoint", "PCSteward.Execution.ReviewedHelperBootstrap", "PCSteward.Execution.WindowsReviewedHelperTransport",
            "System.IO.Pipes.", "System.Diagnostics.Process",
        ];
        foreach (string target in referenced)
            Assert.IsFalse(forbidden.Any(prefix => target.StartsWith(prefix, StringComparison.Ordinal)), $"the probe codec references {target}");
        string source = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution", CodecFile));
        Assert.IsFalse(source.Contains("PCSteward.Provider.Chocolatey.Native", StringComparison.Ordinal), "the codec names the Native namespace");
    }

    private static List<string> RequireBuilt()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        foreach (string required in new[] { "PCSteward.Provider.Chocolatey", "PCSteward.Execution", "PCSteward.ElevatedHelper" })
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required} was not built");
        return assemblies;
    }

    private static IEnumerable<string> Sources(string root) => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));
}
