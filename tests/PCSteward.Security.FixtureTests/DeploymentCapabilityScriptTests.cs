using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using PCSteward.Domain;
using PCSteward.Execution;
using Status = PCSteward.Execution.HelperProtocolCapabilityStatus;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-022 alignment note (protocol-2 capability statement); Task 50 adapter gates A.3, A.7
/// "Regression"/"Rollback", §R A2-L1/A2-L2; follow-up items 1–7 of the Task 50 ADR-022 note record. The deployment scripts
/// and the pure <see cref="DeploymentCapabilityPolicy"/> must agree byte for byte: ledgers come from the REAL script writer
/// text (executed from the parsed AST with only Windows-only primitives stubbed), and the script capability parser is
/// compared with <see cref="DeploymentCapabilityPolicy.TryParseCapability"/> over one vector set.
/// </summary>
[TestClass]
public sealed class DeploymentCapabilityScriptTests
{
    private const string Sid = DeploymentCapabilityPolicyTests.Sid;
    private const string InstallRoot = DeploymentCapabilityPolicyTests.InstallRoot;
    private const string HelperTargetFramework = "net10.0-windows10.0.26100.0";
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    [TestMethod]
    public void CheckedInCapabilityStatementEqualsTheCompiledCanonicalBytesAndIsPublishedAtTheRoot()
    {
        string root = FindRepositoryRoot();
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "src", "PCSteward.ElevatedHelper", DeploymentCapabilityPolicy.CapabilityFileName));
        CollectionAssert.AreEqual(DeploymentCapabilityPolicy.CanonicalCapabilityBytes(), bytes);
        Assert.IsTrue(DeploymentCapabilityPolicy.TryParseCapability(bytes, out string protocol, out IReadOnlyList<UpdateRouteContract> operations));
        Assert.AreEqual(ReviewedUpdateWireCodec.ProtocolVersion, protocol);
        // The statement lists the allow-list in ordinal token order (two routes since the Task 50 activation).
        CollectionAssert.AreEqual(DeploymentCapabilityPolicy.CapabilityOperationAllowlist.OrderBy(DeploymentCapabilityPolicy.OperationToken, StringComparer.Ordinal).ToArray(),
            operations.ToArray());
        // A Windows checkout with core.autocrlf must not turn LF into CRLF.
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, ".gitattributes")), "src/PCSteward.ElevatedHelper/PCSteward.DeploymentCapability.txt -text");
        XElement item = XDocument.Load(Path.Combine(root, "src", "PCSteward.ElevatedHelper", "PCSteward.ElevatedHelper.csproj"))
            .Descendants("None").Single(element => (string?)element.Attribute("Include") == DeploymentCapabilityPolicy.CapabilityFileName);
        Assert.AreEqual("PreserveNewest", (string?)item.Attribute("CopyToPublishDirectory"));
        Assert.AreEqual("PreserveNewest", (string?)item.Attribute("CopyToOutputDirectory"));
        Assert.IsNull(item.Attribute("Link"), "the statement must sit at the payload root");
    }

    [TestMethod]
    public void BindingSetEqualsTheBuiltHelperDepsJsonProjectClosure()
    {
        // Closest buildable check of the published closure: the helper's own build output (this project references the
        // helper with ReferenceOutputAssembly=false only to order the build). Publish adds the same project libraries.
        string output = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ElevatedHelper", "bin", Configuration, HelperTargetFramework);
        string deps = Path.Combine(output, "PCSteward.ElevatedHelper.deps.json");
        Assert.IsTrue(File.Exists(deps), $"Built helper deps.json is missing: {deps}");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(deps));
        JsonElement libraries = document.RootElement.GetProperty("libraries");
        JsonElement target = document.RootElement.GetProperty("targets").EnumerateObject().Single().Value;
        HashSet<string> closure = new(StringComparer.Ordinal);
        foreach (JsonProperty library in target.EnumerateObject())
        {
            if (library.Name.StartsWith("PCSteward.ElevatedHelper/", StringComparison.Ordinal)) continue;
            if (libraries.GetProperty(library.Name).GetProperty("type").GetString() != "project") continue;
            Assert.IsTrue(library.Value.TryGetProperty("runtime", out JsonElement runtime), library.Name);
            foreach (JsonProperty assembly in runtime.EnumerateObject()) Assert.IsTrue(closure.Add(Path.GetFileName(assembly.Name)), assembly.Name);
        }
        // ADR-033: the published helper is one Native AOT image; this managed build output still lists the project closure the
        // image is compiled from, which is also the closure the desktop loads.
        string[] fixedLeaves =
        [
            "PCSteward.Desktop.exe", "PCSteward.Desktop.dll", "PCSteward.ElevatedHelper.exe", DeploymentCapabilityPolicy.CapabilityFileName,
        ];
        CollectionAssert.AreEquivalent(DeploymentCapabilityPolicy.BindingSet.Except(fixedLeaves).ToArray(), closure.ToArray());
        foreach (string leaf in closure)
            Assert.IsTrue(File.Exists(Path.Combine(output, leaf)), leaf);
        CollectionAssert.AreEqual(DeploymentCapabilityPolicy.CanonicalCapabilityBytes(), File.ReadAllBytes(Path.Combine(output, DeploymentCapabilityPolicy.CapabilityFileName)));
    }

    [TestMethod]
    [DataRow("pwsh")]
    [DataRow("windows-powershell")]
    public async Task RealScriptWritersAndCapabilityParserAgreeWithThePolicy(string shell)
    {
        string? executable = ResolveShell(shell);
        if (executable is null)
        {
            // Windows PowerShell 5.1 is the deployment runtime and exists only on Windows; that row runs on Windows CI.
            if (shell == "windows-powershell") Assert.Inconclusive("Windows PowerShell 5.1 runs only on Windows; this row is Windows-CI evidence.");
            Assert.Inconclusive("PowerShell 7 (pwsh) is not on PATH; the real-writer fixture needs it on this platform.");
        }
        string root = FindRepositoryRoot();
        DirectoryInfo work = Directory.CreateTempSubdirectory("pc-steward-capability-fixture-");
        try
        {
            Dictionary<string, byte[]> vectors = CapabilityVectors();
            Directory.CreateDirectory(Path.Combine(work.FullName, "capability"));
            foreach ((string name, byte[] bytes) in vectors) File.WriteAllBytes(Path.Combine(work.FullName, "capability", name + ".bin"), bytes);
            File.WriteAllText(Path.Combine(work.FullName, "bindingset.txt"), string.Join("\n", DeploymentCapabilityPolicy.BindingSet) + "\n");

            (int exitCode, string output, string error) = await RunAsync(executable!, root,
                "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "tests", "PCSteward.Security.FixtureTests", "Fixtures", "DeploymentCapabilityScripts.ps1"),
                "-RepositoryRoot", root, "-WorkRoot", work.FullName, "-FixtureInstallRoot", InstallRoot, "-FixtureInstallerSid", Sid);
            Assert.AreEqual(0, exitCode, $"Fixture failed:\n{output}\n{error}");
            StringAssert.Contains(output, "PASS: deployment capability script fixtures");
            using JsonDocument results = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(work.FullName, "results.json")));
            JsonElement result = results.RootElement;

            // Parse check: every deployment script and fixture parses with zero errors in this shell.
            foreach (JsonProperty parsed in result.GetProperty("parseErrors").EnumerateObject()) Assert.AreEqual(0, parsed.Value.GetInt32(), parsed.Name);

            // Item 1: script capability parser == TryParseCapability; acceptance additionally requires the compiled protocol.
            JsonElement[] capability = [.. result.GetProperty("capability").EnumerateArray()];
            Assert.AreEqual(vectors.Count, capability.Length);
            foreach (JsonElement row in capability)
            {
                string name = row.GetProperty("name").GetString()!;
                bool expected = DeploymentCapabilityPolicy.TryParseCapability(vectors[name], out string protocol, out IReadOnlyList<UpdateRouteContract> operations);
                Assert.AreEqual(expected, row.GetProperty("parsed").GetBoolean(), name);
                Assert.AreEqual(expected && protocol == ReviewedUpdateWireCodec.ProtocolVersion, row.GetProperty("accepted").GetBoolean(), name);
                if (!expected) continue;
                Assert.AreEqual(protocol, row.GetProperty("protocol").GetString(), name);
                Assert.AreEqual(string.Join(',', operations.Select(DeploymentCapabilityPolicy.OperationToken)), row.GetProperty("operations").GetString(), name);
            }
            Assert.IsTrue(capability.Any(row => row.GetProperty("accepted").GetBoolean()) && capability.Any(row => row.GetProperty("parsed").GetBoolean() && !row.GetProperty("accepted").GetBoolean()));

            // Items 1/3: the manifest implies the origin; malformed capability entries are refused.
            JsonElement origins = result.GetProperty("origins");
            Assert.AreEqual(DeploymentCapabilityPolicy.PayloadOriginV1, origins.GetProperty("v1").GetString());
            Assert.AreEqual(DeploymentCapabilityPolicy.PayloadOriginV2, origins.GetProperty("v2").GetString());
            foreach (string refused in new[] { "nested", "wrongCase", "empty", "oversize" })
                Assert.AreEqual("Malformed pinned deployment capability entry.", origins.GetProperty(refused).GetString(), refused);

            // Item 7 (A2-L1): ledgers from the real writers parse with their closed key sets and evaluate as specified.
            string ledgers = Path.Combine(work.FullName, "ledgers");
            (string Name, DeploymentLedgerVariant Variant, JsonValueKind Stores)[] incomplete =
            [
                ("pending-null-thumbprint", DeploymentLedgerVariant.Pending, JsonValueKind.Array),
                ("pending-with-certificate", DeploymentLedgerVariant.Pending, JsonValueKind.Array),
                // Windows PowerShell 5.1 writes the empty unrolled store list as {}; pwsh 7 writes null (both are zero stores).
                ("rollback-zero-stores", DeploymentLedgerVariant.Rollback, shell == "windows-powershell" ? JsonValueKind.Object : JsonValueKind.Null),
                ("rollback-one-store", DeploymentLedgerVariant.Rollback, JsonValueKind.String),
                ("rollback-several-stores", DeploymentLedgerVariant.Rollback, JsonValueKind.Array),
            ];
            foreach ((string name, DeploymentLedgerVariant variant, JsonValueKind stores) in incomplete)
            {
                byte[] bytes = ReadLedger(ledgers, name, variant, out DeploymentLedger _);
                using JsonDocument json = JsonDocument.Parse(bytes);
                Assert.AreEqual(stores, json.RootElement.GetProperty("ownedCertificateStores").ValueKind, name);
                Assert.AreEqual(Status.Incomplete, Evaluate(bytes, null, null), name);
            }
            byte[] capabilityBytes = File.ReadAllBytes(Path.Combine(root, "src", "PCSteward.ElevatedHelper", DeploymentCapabilityPolicy.CapabilityFileName));
            JsonElement manifestHashes = result.GetProperty("manifestSha256");
            foreach (string kind in new[] { "v1", "v2" })
            {
                byte[] bytes = ReadLedger(ledgers, "completed-" + kind, DeploymentLedgerVariant.CompletedPinned, out DeploymentLedger ledger);
                byte[] manifest = File.ReadAllBytes(Path.Combine(work.FullName, $"manifest-{kind}.json"));
                Assert.AreEqual(kind == "v1" ? DeploymentCapabilityPolicy.PayloadOriginV1 : DeploymentCapabilityPolicy.PayloadOriginV2, ledger.PayloadOrigin, kind);
                Assert.AreEqual(manifestHashes.GetProperty(kind).GetString(), ledger.CandidateManifestSha256, kind);
                Assert.AreEqual(Convert.ToHexString(SHA256.HashData(manifest)), ledger.CandidateManifestSha256, kind);
                Assert.AreEqual(kind == "v1" ? Status.Absent : Status.Supported, Evaluate(bytes, manifest, capabilityBytes), kind);
            }
            byte[] v2Ledger = File.ReadAllBytes(Path.Combine(ledgers, "completed-v2.json"));
            Assert.AreEqual(Status.Mismatch, Evaluate(v2Ledger, File.ReadAllBytes(Path.Combine(work.FullName, "manifest-v1.json")), capabilityBytes));
            Assert.AreEqual(Status.Mismatch, Evaluate(v2Ledger, File.ReadAllBytes(Path.Combine(work.FullName, "manifest-v2.json")), null));

            // Items 2/4: profile accepts v1 and v2; current provenance refuses other origins; the frozen pre-change
            // v1-only validator (what existing hosts carry) throws on a v2 ledger.
            JsonElement profile = result.GetProperty("profile");
            Assert.AreEqual(JsonValueKind.Null, profile.GetProperty("v1").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, profile.GetProperty("v2").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, profile.GetProperty("legacyAllowed").ValueKind);
            Assert.AreEqual(JsonValueKind.String, profile.GetProperty("legacyDefault").ValueKind);
            Assert.AreEqual(JsonValueKind.String, profile.GetProperty("unknown").ValueKind);
            Assert.AreEqual("Unrecognised pinned-payload provenance.", profile.GetProperty("provenanceUnknownOrigin").GetString());
            Assert.AreEqual("Unrecognised pinned-payload provenance.", profile.GetProperty("provenanceNonStringOrigin").GetString());
            JsonElement preV2 = result.GetProperty("preV2");
            Assert.AreEqual("Unrecognised pinned-payload provenance.", preV2.GetProperty("provenanceOnV2Ledger").GetString());
            Assert.AreEqual(JsonValueKind.String, preV2.GetProperty("profileOnV2").ValueKind);
        }
        finally { work.Delete(recursive: true); }
    }

    private static byte[] ReadLedger(string directory, string name, DeploymentLedgerVariant variant, out DeploymentLedger ledger)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, name + ".json"));
        Assert.IsFalse(bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]), name + " has a BOM");
        Assert.IsTrue(DeploymentCapabilityPolicy.TryReadLedger(bytes, out DeploymentLedger? parsed), name + ":\n" + Encoding.UTF8.GetString(bytes));
        Assert.AreEqual(variant, parsed!.Variant, name);
        Assert.AreEqual(DeploymentCapabilityPolicy.LedgerSchema, parsed.Schema, name);
        ledger = parsed;
        return bytes;
    }

    private static Status Evaluate(byte[] ledger, byte[]? manifest, byte[]? capability)
    {
        // Observations follow the fixture's synthetic payload: "content <leaf>" for every leaf except the real capability bytes.
        Dictionary<string, DeploymentObservedFile> observed = new(StringComparer.Ordinal);
        foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
        {
            byte[] bytes = leaf == DeploymentCapabilityPolicy.CapabilityFileName && capability is not null ? capability : Encoding.UTF8.GetBytes("content " + leaf);
            observed[leaf] = new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        return DeploymentCapabilityPolicy.Evaluate(new(ledger, manifest, capability, Sid, InstallRoot, observed), UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3).Status;
    }

    private static Dictionary<string, byte[]> CapabilityVectors()
    {
        string valid = Encoding.ASCII.GetString(DeploymentCapabilityPolicy.CanonicalCapabilityBytes());
        string operation = "operation:Update/ChocolateyMachineRuntimeLimitsV3\n";
        Dictionary<string, byte[]> vectors = new(StringComparer.Ordinal)
        {
            ["canonical"] = Encoding.ASCII.GetBytes(valid),
            ["protocol-non-numeric"] = Encoding.ASCII.GetBytes(valid.Replace("helper-protocol:2", "helper-protocol:abc", StringComparison.Ordinal)),
            ["nine-identical-operations"] = Encoding.ASCII.GetBytes("pc-steward-deployment-capability:1\nhelper-protocol:2\n" + string.Concat(Enumerable.Repeat(operation, 9))),
            ["empty"] = [],
            ["lf-only"] = "\n"u8.ToArray(),
            ["del-character"] = Encoding.ASCII.GetBytes(valid.Replace("V3\n", "V3\u007f\n", StringComparison.Ordinal)),
            ["exactly-1024-bytes"] = Encoding.ASCII.GetBytes(valid[..^1] + new string('x', 1024 - valid.Length) + "\n"),
        };
        foreach (object[] row in DeploymentCapabilityPolicyTests.MalformedCapabilities()) vectors.Add((string)row[0], (byte[])row[1]);
        return vectors;
    }

    private static string? ResolveShell(string shell)
    {
        if (shell == "windows-powershell")
        {
            if (!OperatingSystem.IsWindows()) return null;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            Assert.IsTrue(File.Exists(path), "Windows PowerShell 5.1 is required on Windows.");
            return path;
        }
        string leaf = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, leaf);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string executable, string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the deployment-script fixture.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(180));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
