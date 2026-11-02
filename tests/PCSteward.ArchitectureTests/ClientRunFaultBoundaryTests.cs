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
                Assert.IsFalse(Regex.IsMatch(source, @"ClientRunFault\s*=(?!\s*null\b)"), $"{path}: only the interpreter sets the observation's run fault");
            }
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
        Assert.IsTrue(signatures.Any(signature => signature.StartsWith("Evaluate(ChocolateyProfileResult result, ReviewedClientRunFault runFault,", StringComparison.Ordinal)));
        Assert.IsTrue(signatures.Any(signature => signature.StartsWith("EvaluateForTests(", StringComparison.Ordinal)));
        // The free-enum core stays private (so unreachable); no src file outside the postcheck calls the test overload.
        StringAssert.Contains(postcheck, "private static ChocolateyClientUpdateVerdict EvaluateCore(");
        foreach ((string path, string source) in sources)
            if (Path.GetFileName(path) != PostcheckFile)
                Assert.AreEqual(0, Count(source, "ChocolateyClientUpdatePostcheck.EvaluateForTests("), path);
    }

    [TestMethod]
    public void NothingInSrcAdmitsTheClientSelfUpdateRoute()
    {
        Dictionary<string, string> sources = Sources();
        string review = Single(sources, "ReviewedUpdateReview.cs");
        Match gate = Regex.Match(review, @"internal static bool HasSupportedMode\(ReviewedUpdateOperation operation\) =>(?<body>[^;]*);");
        Assert.IsTrue(gate.Success, "HasSupportedMode not found");
        Assert.IsFalse(gate.Groups["body"].Value.Contains("Chocolatey", StringComparison.Ordinal));
        StringAssert.Contains(gate.Groups["body"].Value, "UpdateRouteContract.WinGetUserExactV3");
        string policy = Single(sources, "DeploymentCapabilityPolicy.cs");
        StringAssert.Contains(policy,
            "internal static readonly IReadOnlyList<UpdateRouteContract> CapabilityOperationAllowlist = [UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3];");
        Assert.AreEqual(1, Count(policy, "CapabilityOperationAllowlist ="), "a single allow-list definition");
        Assert.IsFalse(policy.Contains("ChocolateyClientSelfUpdateV1", StringComparison.Ordinal), "the capability policy never names the client route");
        // The runtime-limits dispatcher's Supports stays on the package arm only.
        string dispatcher = Single(sources, "ChocolateyRuntimeLimitsRouteDispatcher.cs");
        Match supports = Regex.Match(dispatcher, @"public bool Supports\(ReviewedUpdateOperation operation\)(?<body>[\s\S]*?)\n    }");
        Assert.IsTrue(supports.Success);
        Assert.IsFalse(supports.Groups["body"].Value.Contains("Client", StringComparison.Ordinal), "Supports never admits the client route");
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
