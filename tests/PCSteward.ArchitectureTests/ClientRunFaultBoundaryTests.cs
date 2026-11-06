using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13, ADR-031 design §8 (owner-approved W1): the client run fault is constructed only
/// by the protocol-2 interpreter from an accepted reply; the desktop postcheck takes only that value (no free-enum overload is
/// reachable from <c>src</c>); the helper writes the field only through the codec's client reply; and nothing in <c>src</c>
/// admits <c>ChocolateyClientSelfUpdateV1</c> (owner: the route stays disabled pending review and R5 acceptance).
/// </summary>
[TestClass]
public sealed class ClientRunFaultBoundaryTests
{
    private const string InterpreterFile = "ReviewedUpdateProtocol2Interpreter.cs";
    private const string FactFile = "ReviewedClientRunFault.cs";
    private const string CodecFile = "ReviewedUpdateWireCodec.cs";
    private const string EndpointFile = "ReviewedHelperEndpoint.cs";
    private const string PostcheckFile = "ChocolateyClientUpdatePostcheck.cs";

    [TestMethod]
    public void TheRunFaultIsConstructedOnlyByTheInterpreter()
    {
        Dictionary<string, string> sources = Sources();
        string fact = Single(sources, FactFile);
        StringAssert.Contains(fact, "private ReviewedClientRunFault(");
        Assert.AreEqual(1, Regex.Count(fact, @"new ReviewedClientRunFault\("), "the factory is the only construction");
        Assert.IsFalse(Regex.IsMatch(fact, @"public\s+ReviewedClientRunFault\("), "no public constructor");
        Assert.AreEqual(1, Count(fact, "FromAcceptedHelperReply("), "the definition only");
        string interpreter = Single(sources, InterpreterFile);
        Assert.AreEqual(8, Count(interpreter, "ReviewedClientRunFault.FromAcceptedHelperReply("), "one call per table row, in ToRunFault only");
        Assert.AreEqual(2, Count(interpreter, "ToRunFault("), "the definition and the single accepted-reply use");
        foreach ((string path, string source) in sources)
            if (Path.GetFileName(path) is not (FactFile or InterpreterFile))
            {
                Assert.AreEqual(0, Count(source, "FromAcceptedHelperReply"), path);
                // Independent review L3: assignment/initializer ("ClientRunFault = x") and named-argument ("ClientRunFault: x")
                // forms; "==" comparisons and the null default are not settings.
                Assert.IsFalse(Regex.IsMatch(source, @"\bClientRunFault\s*[:=](?!=)(?!\s*null\b)"),
                    $"{path}: only the interpreter sets the observation's run fault");
            }
        // The named-argument form is the interpreter's own (single) setting, so the pattern really matches it.
        Assert.AreEqual(1, Regex.Count(interpreter, @"\bClientRunFault\s*[:=](?!=)(?!\s*null\b)"), "the interpreter's single setting");
    }

    /// <summary>
    /// Independent review L3, on compiled IL of every built <c>src</c> assembly (aliases, named arguments, Unicode escapes or
    /// generated sources cannot hide a call): the private constructor is called only by the factory, and the factory only by
    /// the interpreter's <c>ToRunFault</c>. The factory stays public because its one caller is in PCSteward.Execution, and an
    /// Application → Execution <c>InternalsVisibleTo</c> would also expose Application's internal dispatch-ticket constructor.
    /// </summary>
    [TestMethod]
    public void TheCompiledRunFaultConstructionHasExactlyItsPinnedCallers()
    {
        const string Fact = "PCSteward.Application.ReviewedClientRunFault";
        const string Interpreter = "PCSteward.Execution.ReviewedUpdateProtocol2Interpreter";
        IReadOnlyList<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        foreach (string required in new[] { "PCSteward.Application", "PCSteward.Execution", "PCSteward.Provider.Chocolatey" })
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required}.dll was not built");
        List<string> violations = [];
        int factoryCalls = 0, constructorCalls = 0;
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (reference.Kind != CompiledIlScanner.ReferenceKind.Method || reference.TargetType != Fact) continue;
                if (reference.TargetMember == "FromAcceptedHelperReply")
                {
                    factoryCalls++;
                    if (reference.LogicalCaller != Interpreter + ".ToRunFault") violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls the factory");
                }
                else if (reference.TargetMember == ".ctor")
                {
                    constructorCalls++;
                    if (reference.LogicalCaller != Fact + ".FromAcceptedHelperReply") violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs it");
                }
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        Assert.AreEqual(8, factoryCalls, "one compiled call per table row of ToRunFault");
        Assert.AreEqual(1, constructorCalls, "the factory's single construction");
    }

    [TestMethod]
    public void OnlyTheEndpointWritesAClientReplyAndOnlyTheInterpreterAndEndpointNameTheClientShape()
    {
        Dictionary<string, string> sources = Sources();
        foreach ((string path, string source) in sources)
        {
            string name = Path.GetFileName(path);
            if (name is not (CodecFile or EndpointFile)) Assert.AreEqual(0, Count(source, "CreateClientResponse("), path);
            if (name is not (CodecFile or InterpreterFile or EndpointFile)) Assert.AreEqual(0, Count(source, "ReviewedHelperReplyShape.ClientSelfUpdate"), path);
        }
        // The codec's package entries keep their reviewed signatures and the package shape.
        string codec = Single(sources, CodecFile);
        StringAssert.Contains(codec, "string diagnostic) => CreateResponseCore(jobId, digest, nonce, facts, reviewed, null, diagnostic);");
        StringAssert.Contains(codec, "TryReadResponse(frame, expectedJob, expectedDigest, expectedNonce, ReviewedHelperReplyShape.Package, out response);");
    }

    [TestMethod]
    public void ThePostcheckTakesOnlyTheBindingOrTheInterpreterFact()
    {
        Dictionary<string, string> sources = Sources();
        string postcheck = Single(sources, PostcheckFile);
        MatchCollection entries = Regex.Matches(postcheck, @"internal static ChocolateyClientUpdateVerdict (?<name>\w+)\((?<args>[^)]*)\)");
        string[] signatures = entries.Select(match => match.Groups["name"].Value + "(" + Regex.Replace(match.Groups["args"].Value, @"\s+", " ") + ")").ToArray();
        Assert.AreEqual(3, signatures.Length, string.Join(" | ", signatures));
        Assert.IsTrue(signatures.Any(signature => signature.StartsWith("Evaluate(ChocolateyProfileResult result, ChocolateyHeldClientBinding client,", StringComparison.Ordinal)));
        Assert.IsTrue(signatures.Any(signature => signature.StartsWith("Evaluate(ChocolateyProfileResult? result, ReviewedClientRunFault runFault,", StringComparison.Ordinal)));
        Assert.IsTrue(signatures.Any(signature => signature.StartsWith("EvaluateForTests(", StringComparison.Ordinal)));
        // The free-enum core stays private (so unreachable); no src file outside the postcheck calls the test overload.
        StringAssert.Contains(postcheck, "private static ChocolateyClientUpdateVerdict EvaluateCore(");
        foreach ((string path, string source) in sources)
            if (Path.GetFileName(path) != PostcheckFile)
                Assert.AreEqual(0, Count(source, "ChocolateyClientUpdatePostcheck.EvaluateForTests("), path);
    }

    [TestMethod]
    public void TheDesktopClientEntriesHaveFixedCallSitesAndTheirTestFactoriesNone()
    {
        Dictionary<string, string> sources = Sources();
        foreach ((string path, string source) in sources)
        {
            string name = Path.GetFileName(path);
            // The fact overload is called only by the desktop verifier.
            if (name != "ChocolateyReviewedUpdateVerifier.cs") Assert.AreEqual(0, Regex.Count(source, @"ChocolateyClientUpdatePostcheck\.Evaluate\([^;]*runFault"), path);
            // The client-mode observer has one Desktop and one Helper production caller.
            if (name is not ("ChocolateyDesktopObservation.cs" or "NativeChocolateyHelperItem.cs"))
                Assert.AreEqual(0, Count(source, "ChocolateyProfileObserver.ObserveClient("), path);
            // The client test factories are never called from src.
            if (name is not ("ChocolateyReviewedUpdateVerifier.cs" or "ChocolateyProfileEvidenceSource.cs"))
                Assert.AreEqual(0, Count(source, "CreateClientForTests("), path);
        }
        StringAssert.Contains(Single(sources, "ChocolateyDesktopObservation.cs"), "ChocolateyProfileObserver.ObserveClient(token, ChocolateyObserverRole.Desktop,");
        foreach (string file in new[] { "ChocolateyReviewedUpdateVerifier.cs", "ChocolateyProfileEvidenceSource.cs" })
            Assert.AreEqual(1, Count(Single(sources, file), "CreateClientForTests("), $"{file}: the definition only");
    }

    /// <summary>
    /// Task 50 activation (ADR-031 owner decision 7, step C-A; FR-UPD-15): the deliberate change of the former non-admission
    /// pin. The client self-update route is admitted only through the three admission points: the review gate's second term
    /// (<c>ReviewedChocolateyRouteAdmission.Admits</c>), the single capability allow-list definition, and the dispatcher's
    /// route-discriminated shape check before the capability seam. <c>ChocolateyRouteActivationBoundaryTests</c> pins the
    /// exact texts.
    /// </summary>
    [TestMethod]
    public void TheClientSelfUpdateRouteIsAdmittedOnlyThroughTheThreeActivationPoints()
    {
        Dictionary<string, string> sources = Sources();
        string review = Single(sources, "ReviewedUpdateReview.cs");
        Match gate = Regex.Match(review, @"internal static bool HasSupportedMode\(ReviewedUpdateOperation operation\) =>(?<body>[^;]*);");
        Assert.IsTrue(gate.Success, "HasSupportedMode not found");
        StringAssert.Contains(gate.Groups["body"].Value, "UpdateRouteContract.WinGetUserExactV3");
        StringAssert.Contains(gate.Groups["body"].Value, "ReviewedChocolateyRouteAdmission.Admits(operation)");
        Assert.IsFalse(gate.Groups["body"].Value.Contains("ChocolateyClientSelfUpdateV1", StringComparison.Ordinal), "the shape lives in the admission type");
        string policy = Single(sources, "DeploymentCapabilityPolicy.cs");
        StringAssert.Contains(Regex.Replace(policy, @"\s+", " "),
            "internal static readonly IReadOnlyList<UpdateRouteContract> CapabilityOperationAllowlist = " +
            "[UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, UpdateRouteContract.ChocolateyClientSelfUpdateV1];");
        Assert.AreEqual(1, Count(policy, "CapabilityOperationAllowlist ="), "a single allow-list definition");
        Assert.AreEqual(1, Count(policy, "ChocolateyClientSelfUpdateV1"), "the capability policy names the client route only in the allow-list");
        string dispatcher = Single(sources, "ChocolateyRuntimeLimitsRouteDispatcher.cs");
        Match supports = Regex.Match(dispatcher, @"public bool Supports\(ReviewedUpdateOperation operation\)(?<body>[\s\S]*?)\n    }");
        Assert.IsTrue(supports.Success);
        StringAssert.Contains(supports.Groups["body"].Value, "ReviewedUpdateProtocol2Interpreter.TryGetRouteProfileDigest(operation, out _, out _) &&");
        Assert.IsFalse(supports.Groups["body"].Value.Contains("TryGetClientProfileDigest", StringComparison.Ordinal), "only the route-discriminated form");
    }

    private static string Single(Dictionary<string, string> sources, string name) =>
        sources.Single(pair => Path.GetFileName(pair.Key) == name).Value;

    private static Dictionary<string, string> Sources()
    {
        string src = Path.Combine(FindRepositoryRoot(), "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToDictionary(path => path, File.ReadAllText);
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        for (int index = source.IndexOf(value, StringComparison.Ordinal); index >= 0; index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
