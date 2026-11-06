using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using PCSteward.Domain;
using PCSteward.Execution;
using Status = PCSteward.Execution.HelperProtocolCapabilityStatus;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 adapter gates Part A §A.3, §A.4 steps 3–6 (pure part), §A.7 and §R A2-L1/A2-L4:
/// the pure deployment-capability policy over ledger, candidate-manifest, capability and binding-set bytes.
/// <para>
/// Ledger fixtures are transcribed key-for-key from the real writers (<c>Install-OwnerOnlyProtectedTestDeployment.ps1</c>
/// <c>Write-PendingLedger</c>, the failure-path rollback ledger and <c>$completedState</c>, and the pre-Task-48 legacy writer
/// at <c>e99bc8a^</c>) and rendered in the PowerShell 7 <c>ConvertTo-Json</c> shape (two-space indent, CRLF). Ledgers generated
/// by the real current writers (pwsh 7, and Windows PowerShell 5.1 on Windows CI) are covered by <see cref="DeploymentCapabilityScriptTests"/>.
/// </para>
/// </summary>
[TestClass]
public sealed class DeploymentCapabilityPolicyTests
{
    internal const string Sid = "S-1-5-21-11-22-33-1001";
    internal const string InstallRoot = @"C:\Program Files\PC Steward";
    private const string Thumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";
    private const string Subject = "CN=PC Steward Owner-Only Test 3f2504e0-4f89-11d3-9a0c-0305e82c3301";
    private const UpdateRouteContract Route = UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3;

    // ---- Writer-variant fixtures (A2-L1) ----

    [TestMethod]
    public void PendingLedgerLiteralFromWritePendingLedgerParsesAndIsIncomplete()
    {
        // Write-PendingLedger, first call in New-OwnerOnlyCertificate (no certificate yet): null thumbprint, OwnedStores = @('My').
        string literal = """
            {
              "schema": "pc-steward-owner-only-protected-test-v6",
              "installRoot": "C:\\Program Files\\PC Steward",
              "installerSid": "S-1-5-21-11-22-33-1001",
              "certificateThumbprint": null,
              "certificateSubject": "CN=PC Steward Owner-Only Test 3f2504e0-4f89-11d3-9a0c-0305e82c3301",
              "ownedCertificateStores": [
                "My"
              ],
              "completed": false,
              "rollbackRequired": false,
              "pendingCertificate": true
            }
            """.ReplaceLineEndings("\r\n");
        Assert.AreEqual(literal, Ps7Json.Render(Ledgers.Pending(thumbprint: null, stores: ["My"])), "fixture emitter must match the ConvertTo-Json shape");
        byte[] bytes = Encoding.UTF8.GetBytes(literal);
        Assert.IsTrue(DeploymentCapabilityPolicy.TryReadLedger(bytes, out DeploymentLedger? ledger));
        Assert.AreEqual(DeploymentLedgerVariant.Pending, ledger!.Variant);
        Assert.AreEqual(Status.Incomplete, Evaluate(new Scenario { Ledger = bytes }));
    }

    [TestMethod]
    [DataRow("Rollback", "{}", true)]
    [DataRow("Rollback", "{\r\n\r\n                               }", true)]
    [DataRow("Rollback", "{ \"x\": 1 }", false)]
    [DataRow("Rollback", "{ \"My\": null }", false)]
    [DataRow("Pending", "{}", false)]
    public void WindowsPowerShellEmptyObjectIsZeroStoresOnlyForRollbackLedgers(string variantName, string storesJson, bool readable)
    {
        // Windows PowerShell 5.1 (the deployment runtime) renders the rollback writer's empty unrolled store list as {}.
        IReadOnlyList<(string, object?)> ledger = variantName == "Rollback" ? Ledgers.Rollback(null, null, null) : Ledgers.Pending(null, ["My"]);
        string rendered = Ps7Json.Render(ledger);
        string mutated = System.Text.RegularExpressions.Regex.Replace(rendered, "\"ownedCertificateStores\":\\s*(null|\\[[^\\]]*\\])",
            "\"ownedCertificateStores\":  " + storesJson.Replace("$", "$$"));
        Assert.AreNotEqual(rendered, mutated, "fixture mutation must apply");
        byte[] bytes = Encoding.UTF8.GetBytes(mutated);
        Assert.AreEqual(readable, DeploymentCapabilityPolicy.TryReadLedger(bytes, out _), storesJson);
        Assert.AreEqual(readable ? Status.Incomplete : Status.Unreadable, Evaluate(new Scenario { Ledger = bytes }), storesJson);
    }

    public static IEnumerable<object[]> WriterVariants() =>
    [
        ["pending-null-thumbprint", Ledgers.Pending(null, ["My"]), "Pending", "Incomplete"],
        ["pending-with-certificate", Ledgers.Pending(Thumbprint, ["My", "Root", "TrustedPublisher"]), "Pending", "Incomplete"],
        ["rollback-no-certificate-state", Ledgers.Rollback(null, null, null), "Rollback", "Incomplete"],
        ["rollback-pending-certificate-one-store", Ledgers.Rollback(null, Subject, "My"), "Rollback", "Incomplete"],
        ["rollback-certificate-several-stores", Ledgers.Rollback(Thumbprint, Subject, new object?[] { "My", "Root" }), "Rollback", "Incomplete"],
        ["completed-legacy-v6", Ledgers.Completed(origin: null), "CompletedLegacy", "Absent"],
        ["completed-pinned-unsigned-v1", Ledgers.Completed(origin: DeploymentCapabilityPolicy.PayloadOriginV1), "CompletedPinned", "Absent"],
        ["completed-pinned-unsigned-v2", Ledgers.Completed(origin: DeploymentCapabilityPolicy.PayloadOriginV2), "CompletedPinned", "Supported"],
    ];

    [TestMethod]
    [DynamicData(nameof(WriterVariants))]
    public void EveryWriterVariantParsesWithItsClosedKeySet(string name, IReadOnlyList<(string, object?)> ledger, string variantName, string expectedName)
    {
        DeploymentLedgerVariant variant = Enum.Parse<DeploymentLedgerVariant>(variantName);
        Status expected = Enum.Parse<Status>(expectedName);
        byte[] bytes = Encoding.UTF8.GetBytes(Ps7Json.Render(ledger));
        Assert.IsTrue(DeploymentCapabilityPolicy.TryReadLedger(bytes, out DeploymentLedger? parsed), name);
        Assert.AreEqual(variant, parsed!.Variant, name);
        Assert.AreEqual(expected, Evaluate(new Scenario { Ledger = bytes }), name);
        // Every writer's key set is closed: one extra key makes the ledger unreadable.
        byte[] extra = Encoding.UTF8.GetBytes(Ps7Json.Render([.. ledger, ("unexpected", "x")]));
        Assert.IsFalse(DeploymentCapabilityPolicy.TryReadLedger(extra, out _), name);
        Assert.AreEqual(Status.Unreadable, Evaluate(new Scenario { Ledger = extra }), name);
    }

    [TestMethod]
    public void LegacyLedgerWrittenBySetContentWithTrailingNewlineIsStillAbsent()
    {
        // Pre-Task-48 writer: ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 (pwsh: no BOM, trailing newline).
        byte[] bytes = Encoding.UTF8.GetBytes(Ps7Json.Render(Ledgers.Completed(origin: null)) + "\r\n");
        Assert.AreEqual(Status.Absent, Evaluate(new Scenario { Ledger = bytes }));
        // The same text with a UTF-8 BOM (as Windows PowerShell 5.1 would write it) fails closed as unreadable.
        Assert.AreEqual(Status.Unreadable, Evaluate(new Scenario { Ledger = [0xEF, 0xBB, 0xBF, .. bytes] }));
    }

    [TestMethod]
    public void MissingLedgerIsAbsent() => Assert.AreEqual(Status.Absent, Evaluate(new Scenario { LedgerAbsent = true }));

    // ---- Ledger negatives (A.7 "Ledger") ----

    [TestMethod]
    [DataRow("unknown-origin", "Unreadable")]
    [DataRow("origin-case-variant", "Unreadable")]
    [DataRow("duplicate-key", "Unreadable")]
    [DataRow("duplicate-key-case-variant", "Unreadable")]
    [DataRow("unknown-key", "Unreadable")]
    [DataRow("missing-key", "Unreadable")]
    [DataRow("completed-as-string", "Unreadable")]
    [DataRow("files-as-object", "Unreadable")]
    [DataRow("file-hash-63", "Unreadable")]
    [DataRow("duplicate-file-path", "Unreadable")]
    [DataRow("duplicate-file-path-case-variant", "Unreadable")]
    [DataRow("completed-thumbprint-null", "Unreadable")]
    [DataRow("unknown-certificate-store", "Unreadable")]
    [DataRow("schema-v5", "Unreadable")]
    [DataRow("completed-with-rollback-required", "Unreadable")]
    [DataRow("completed-false-rollback-missing", "Unreadable")]
    [DataRow("other-installer-sid", "Mismatch")]
    [DataRow("other-install-root", "Mismatch")]
    [DataRow("install-root-case-variant", "Mismatch")]
    [DataRow("one-executable-image", "Unreadable")]
    [DataRow("created-not-round-trip", "Unreadable")]
    [DataRow("trailing-content", "Unreadable")]
    [DataRow("comment", "Unreadable")]
    [DataRow("array-root", "Unreadable")]
    public void LedgerNegatives(string change, string expectedName)
    {
        Status expected = Enum.Parse<Status>(expectedName);
        List<(string Key, object? Value)> ledger = [.. Ledgers.Completed(DeploymentCapabilityPolicy.PayloadOriginV2)];
        string? text = null;
        switch (change)
        {
            case "unknown-origin": Set(ledger, "payloadOrigin", "pinned-unsigned-v3"); break;
            case "origin-case-variant": Set(ledger, "payloadOrigin", "Pinned-Unsigned-V2"); break;
            case "duplicate-key": ledger.Add(("schema", DeploymentCapabilityPolicy.LedgerSchema)); break;
            case "duplicate-key-case-variant": ledger.Add(("Schema", DeploymentCapabilityPolicy.LedgerSchema)); break;
            case "unknown-key": ledger.Add(("helperProtocol", "2")); break;
            case "missing-key": ledger.RemoveAll(item => item.Key == "createdAtUtc"); break;
            case "completed-as-string": Set(ledger, "completed", "true"); break;
            case "files-as-object": Set(ledger, "files", Obj(("path", "x"), ("sha256", Hash("x")))); break;
            case "file-hash-63": Set(ledger, "files", new object?[] { Obj(("path", "App.xbf"), ("sha256", Hash("x")[..63])) }); break;
            case "duplicate-file-path":
                Set(ledger, "files", new object?[] { Obj(("path", "App.xbf"), ("sha256", Hash("a"))), Obj(("path", "App.xbf"), ("sha256", Hash("b"))) }); break;
            case "duplicate-file-path-case-variant":
                Set(ledger, "files", new object?[] { Obj(("path", "App.xbf"), ("sha256", Hash("a"))), Obj(("path", "app.XBF"), ("sha256", Hash("b"))) }); break;
            case "completed-thumbprint-null": Set(ledger, "certificateThumbprint", null); break;
            case "unknown-certificate-store": Set(ledger, "ownedCertificateStores", new object?[] { "My", "CA" }); break;
            case "schema-v5": Set(ledger, "schema", "pc-steward-owner-only-protected-test-v5"); break;
            case "completed-with-rollback-required": ledger.Add(("rollbackRequired", true)); break;
            case "completed-false-rollback-missing": Set(ledger, "completed", false); break;
            case "other-installer-sid": Set(ledger, "installerSid", "S-1-5-21-11-22-33-1002"); break;
            case "other-install-root": Set(ledger, "installRoot", @"D:\PC Steward"); break;
            case "install-root-case-variant": Set(ledger, "installRoot", @"C:\Program Files\PC STEWARD"); break;
            case "one-executable-image": Set(ledger, "executableImages", new object?[] { Ledgers.Image("PCSteward.Desktop.exe") }); break;
            case "created-not-round-trip": Set(ledger, "createdAtUtc", "2026-10-29 12:00:00"); break;
            case "trailing-content": text = Ps7Json.Render(ledger) + "{}"; break;
            case "comment": text = "// note\r\n" + Ps7Json.Render(ledger); break;
            case "array-root": text = "[" + Ps7Json.Render(ledger) + "]"; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        byte[] bytes = Encoding.UTF8.GetBytes(text ?? Ps7Json.Render(ledger));
        Assert.AreEqual(expected, Evaluate(new Scenario { Ledger = bytes }), change);
    }

    [TestMethod]
    [DataRow("bom")]
    [DataRow("invalid-utf8")]
    [DataRow("over-one-mebibyte")]
    [DataRow("empty")]
    public void LedgerEncodingNegativesAreUnreadable(string change)
    {
        byte[] valid = Encoding.UTF8.GetBytes(Ps7Json.Render(Ledgers.Completed(DeploymentCapabilityPolicy.PayloadOriginV2)));
        byte[] bytes = change switch
        {
            "bom" => [0xEF, 0xBB, 0xBF, .. valid],
            "invalid-utf8" => [.. valid[..10], 0xFF, .. valid[10..]],
            "over-one-mebibyte" => [.. valid, .. Enumerable.Repeat((byte)' ', DeploymentCapabilityPolicy.MaximumLedgerBytes)],
            _ => [],
        };
        Assert.AreEqual(Status.Unreadable, Evaluate(new Scenario { Ledger = bytes }), change);
    }

    // ---- Candidate manifest and capability statement (A.7 "Manifest / capability") ----

    [TestMethod]
    public void ConsistentV2DeploymentIsSupportedForTheAllowlistedRouteOnly()
    {
        Scenario scenario = new();
        Assert.AreEqual(Status.Supported, Evaluate(scenario));
        Assert.AreEqual(Status.Mismatch, Evaluate(scenario, UpdateRouteContract.WinGetUserExactV3));
        Assert.AreEqual(Status.Mismatch, Evaluate(scenario, UpdateRouteContract.ChocolateyMachineProvisionalV3));
    }

    [TestMethod]
    public void ManifestSourcedLowercaseLedgerHashesStillCompare()
    {
        Scenario scenario = new() { LowercaseLedgerHashes = true };
        Assert.AreEqual(Status.Supported, Evaluate(scenario));
    }

    [TestMethod]
    [DataRow("manifest-missing")]
    [DataRow("manifest-hash-mismatch")]
    [DataRow("manifest-not-json")]
    [DataRow("manifest-extra-key")]
    [DataRow("capability-absent-from-manifest")]
    [DataRow("capability-duplicated-in-manifest")]
    [DataRow("capability-case-variant-in-manifest")]
    [DataRow("capability-absent-from-ledger")]
    [DataRow("capability-manifest-hash-differs-from-ledger")]
    [DataRow("capability-absent-at-root")]
    [DataRow("capability-bytes-differ-from-ledger")]
    public void ManifestAndCapabilityPinningNegativesAreMismatch(string change)
    {
        Scenario scenario = new();
        switch (change)
        {
            case "manifest-missing": scenario.ManifestAbsent = true; break;
            case "manifest-hash-mismatch": scenario.ManifestHashOverride = Hash("other manifest"); break;
            case "manifest-not-json": scenario.ManifestOverride = "not json"u8.ToArray(); break;
            case "manifest-extra-key": scenario.ManifestExtraKey = true; break;
            case "capability-absent-from-manifest": scenario.OmitCapabilityFromManifest = true; break;
            case "capability-duplicated-in-manifest": scenario.DuplicateCapabilityInManifest = true; break;
            case "capability-case-variant-in-manifest": scenario.CapabilityManifestPath = "pcsteward.deploymentcapability.txt"; break;
            case "capability-absent-from-ledger": scenario.OmitFromLedger = DeploymentCapabilityPolicy.CapabilityFileName; break;
            case "capability-manifest-hash-differs-from-ledger": scenario.CapabilityManifestHashOverride = Hash("different"); break;
            case "capability-absent-at-root": scenario.CapabilityAbsent = true; break;
            case "capability-bytes-differ-from-ledger": scenario.CapabilityReadOverride = Encoding.ASCII.GetBytes("pc-steward-deployment-capability:1\nhelper-protocol:2\n"); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        Assert.AreEqual(Status.Mismatch, Evaluate(scenario), change);
    }

    public static IEnumerable<object[]> MalformedCapabilities()
    {
        string valid = Encoding.ASCII.GetString(DeploymentCapabilityPolicy.CanonicalCapabilityBytes());
        string operation = "operation:Update/ChocolateyMachineRuntimeLimitsV3\n";
        return
        [
            ["crlf", Encoding.ASCII.GetBytes(valid.Replace("\n", "\r\n", StringComparison.Ordinal))],
            ["trailing-space", Encoding.ASCII.GetBytes(valid.Replace("V3\n", "V3 \n", StringComparison.Ordinal))],
            ["bom", (byte[])[0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes(valid)]],
            ["no-final-lf", Encoding.ASCII.GetBytes(valid[..^1])],
            ["uppercase-header", Encoding.ASCII.GetBytes(valid.Replace("pc-steward-deployment-capability", "PC-STEWARD-DEPLOYMENT-CAPABILITY", StringComparison.Ordinal))],
            ["protocol-1", Encoding.ASCII.GetBytes(valid.Replace("helper-protocol:2", "helper-protocol:1", StringComparison.Ordinal))],
            ["protocol-3", Encoding.ASCII.GetBytes(valid.Replace("helper-protocol:2", "helper-protocol:3", StringComparison.Ordinal))],
            ["protocol-2-space", Encoding.ASCII.GetBytes(valid.Replace("helper-protocol:2", "helper-protocol:2 ", StringComparison.Ordinal))],
            ["protocol-empty", Encoding.ASCII.GetBytes(valid.Replace("helper-protocol:2", "helper-protocol:", StringComparison.Ordinal))],
            ["two-protocol-lines", Encoding.ASCII.GetBytes(valid.Replace("helper-protocol:2\n", "helper-protocol:2\nhelper-protocol:2\n", StringComparison.Ordinal))],
            ["protocol-after-operation", Encoding.ASCII.GetBytes("pc-steward-deployment-capability:1\n" + operation + "helper-protocol:2\n")],
            ["route-missing", Encoding.ASCII.GetBytes("pc-steward-deployment-capability:1\nhelper-protocol:2\n")],
            ["duplicate-operation", Encoding.ASCII.GetBytes(valid + operation)],
            ["unknown-operation", Encoding.ASCII.GetBytes(valid + "operation:Update/WinGetUserExactV3\n")],
            ["operation-case-variant", Encoding.ASCII.GetBytes(valid.Replace("Update/Chocolatey", "update/Chocolatey", StringComparison.Ordinal))],
            ["unknown-line", Encoding.ASCII.GetBytes(valid + "comment:x\n")],
            ["empty-line", Encoding.ASCII.GetBytes(valid + "\n")],
            ["header-version-2", Encoding.ASCII.GetBytes(valid.Replace("capability:1", "capability:2", StringComparison.Ordinal))],
            ["tab", Encoding.ASCII.GetBytes(valid.Replace("helper-protocol:2", "helper-protocol:\t2", StringComparison.Ordinal))],
            ["non-ascii", Encoding.UTF8.GetBytes(valid.Replace("helper", "hélper", StringComparison.Ordinal))],
            ["over-1024-bytes", Encoding.ASCII.GetBytes(valid + new string('x', 1024) + "\n")],
            ["nine-operations", Encoding.ASCII.GetBytes(valid + string.Concat(Enumerable.Repeat(operation, 8)))],
        ];
    }

    [TestMethod]
    [DynamicData(nameof(MalformedCapabilities))]
    public void MalformedCapabilityStatementsAreMismatchEvenWhenConsistentlyPinned(string name, byte[] capability)
    {
        Assert.IsFalse(DeploymentCapabilityPolicy.TryParseCapability(capability, out string protocol, out IReadOnlyList<UpdateRouteContract> operations) &&
            protocol == "2" && operations.Contains(Route), name);
        Assert.AreEqual(Status.Mismatch, Evaluate(new Scenario { Capability = capability }), name);
    }

    [TestMethod]
    public void CapabilityBytesAreGeneratedFromTheCompiledProtocolAndAllowlist()
    {
        // Task 50 activation (ADR-031 owner decision 7, step C-A): the client self-update route joins the allow-list; the
        // statement lists the tokens in ordinal order (client first).
        byte[] expected = Encoding.ASCII.GetBytes("pc-steward-deployment-capability:1\nhelper-protocol:2\n" +
            "operation:Update/ChocolateyClientSelfUpdateV1\noperation:Update/ChocolateyMachineRuntimeLimitsV3\n");
        Assert.AreEqual(149, expected.Length);
        CollectionAssert.AreEqual(expected, DeploymentCapabilityPolicy.CanonicalCapabilityBytes());
        Assert.AreEqual("2", ReviewedUpdateWireCodec.ProtocolVersion);
        CollectionAssert.AreEqual(new[] { Route, UpdateRouteContract.ChocolateyClientSelfUpdateV1 }, DeploymentCapabilityPolicy.CapabilityOperationAllowlist.ToArray());
        Assert.IsTrue(DeploymentCapabilityPolicy.TryParseCapability(expected, out string protocol, out IReadOnlyList<UpdateRouteContract> operations));
        Assert.AreEqual(ReviewedUpdateWireCodec.ProtocolVersion, protocol);
        CollectionAssert.AreEqual(new[] { UpdateRouteContract.ChocolateyClientSelfUpdateV1, Route }, operations.ToArray());
        // The pre-activation one-token statement still parses (an old deployment validates), with only its one route.
        byte[] oneToken = Encoding.ASCII.GetBytes("pc-steward-deployment-capability:1\nhelper-protocol:2\noperation:Update/ChocolateyMachineRuntimeLimitsV3\n");
        Assert.IsTrue(DeploymentCapabilityPolicy.TryParseCapability(oneToken, out _, out IReadOnlyList<UpdateRouteContract> old));
        CollectionAssert.AreEqual(new[] { Route }, old.ToArray());
    }

    // ---- Binding set (A.7 "Binding", A2-L4) ----

    [TestMethod]
    [DataRow("PCSteward.Desktop.exe")]
    [DataRow("PCSteward.Desktop.dll")]
    [DataRow("PCSteward.ElevatedHelper.exe")]
    [DataRow("PCSteward.ElevatedHelper.dll")]
    [DataRow("PCSteward.ElevatedHelper.deps.json")]
    [DataRow("PCSteward.ElevatedHelper.runtimeconfig.json")]
    [DataRow("PCSteward.Execution.dll")]
    [DataRow("PCSteward.Application.dll")]
    [DataRow("PCSteward.Domain.dll")]
    [DataRow("PCSteward.Provider.Chocolatey.dll")]
    [DataRow("PCSteward.Provider.WinGet.dll")]
    [DataRow("PCSteward.DeploymentCapability.txt")]
    public void AnyBindingLeafThatDiffersFromTheLedgerIsMismatch(string leaf)
    {
        Assert.AreEqual(Status.Mismatch, Evaluate(new Scenario { ObservedHashOverride = (leaf, Hash("replaced " + leaf)) }), leaf);
        if (leaf != DeploymentCapabilityPolicy.CapabilityFileName)
            Assert.AreEqual(Status.Mismatch, Evaluate(new Scenario { OmitFromLedger = leaf }), leaf + " absent from ledger");
        Assert.AreEqual(Status.Unreadable, Evaluate(new Scenario { OmitObservation = leaf }), leaf + " not observed");
    }

    [TestMethod]
    [DataRow("extra-observation")]
    [DataRow("file-over-32-mib")]
    [DataRow("set-over-64-mib")]
    [DataRow("negative-length")]
    [DataRow("observed-hash-malformed")]
    [DataRow("observations-null")]
    public void BindingObservationBoundsAreUnreadable(string change)
    {
        Scenario scenario = new();
        switch (change)
        {
            case "extra-observation": scenario.ExtraObservation = true; break;
            case "file-over-32-mib": scenario.ObservedLengthOverride = ("PCSteward.Execution.dll", DeploymentCapabilityPolicy.MaximumBindingFileBytes + 1); break;
            case "set-over-64-mib": scenario.UniformObservedLength = DeploymentCapabilityPolicy.MaximumBindingFileBytes / 4; break;
            case "negative-length": scenario.ObservedLengthOverride = ("PCSteward.Domain.dll", -1); break;
            case "observed-hash-malformed": scenario.ObservedHashOverride = ("PCSteward.Domain.dll", "not-a-hash"); break;
            case "observations-null": scenario.NullObservations = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        Assert.AreEqual(Status.Unreadable, Evaluate(scenario), change);
    }

    [TestMethod]
    public void BindingSetEqualsTheHelperProjectClosurePlusFixedLeaves()
    {
        string root = FindRepositoryRoot();
        HashSet<string> closure = new(StringComparer.Ordinal);
        Queue<string> pending = new([Path.Combine(root, "src", "PCSteward.ElevatedHelper", "PCSteward.ElevatedHelper.csproj")]);
        while (pending.TryDequeue(out string? project))
        {
            foreach (XElement reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                if (string.Equals((string?)reference.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase)) continue;
                string referenced = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, ((string)reference.Attribute("Include")!).Replace('\\', '/')));
                if (closure.Add(Path.GetFileNameWithoutExtension(referenced) + ".dll")) pending.Enqueue(referenced);
            }
        }
        string[] expected =
        [
            "PCSteward.Desktop.exe", "PCSteward.Desktop.dll", "PCSteward.ElevatedHelper.exe", "PCSteward.ElevatedHelper.dll",
            "PCSteward.ElevatedHelper.deps.json", "PCSteward.ElevatedHelper.runtimeconfig.json", DeploymentCapabilityPolicy.CapabilityFileName,
            .. closure,
        ];
        CollectionAssert.AreEquivalent(expected, DeploymentCapabilityPolicy.BindingSet.ToArray());
        Assert.AreEqual(DeploymentCapabilityPolicy.BindingSet.Count, DeploymentCapabilityPolicy.BindingSet.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ---- Results, diagnostics and exceptions ----

    [TestMethod]
    public void EveryStatusHasOneFixedDiagnosticWithoutDeploymentDetail()
    {
        foreach (Status status in Enum.GetValues<Status>())
        {
            HelperProtocolCapabilityResult result = HelperProtocolCapabilityResult.For(status);
            Assert.AreEqual(status, result.Status);
            Assert.AreEqual(status == Status.Supported, result.IsSupported);
            Assert.AreEqual("helper-capability-" + status.ToString().ToLowerInvariant(), result.Diagnostic);
            Assert.AreSame(result, HelperProtocolCapabilityResult.For(status));
        }
        Assert.AreEqual(Status.Unreadable, HelperProtocolCapabilityResult.For((Status)99).Status);
    }

    [TestMethod]
    public void TwoEvaluationsReadTheirInputsAgainAndNothingIsCached()
    {
        Scenario scenario = new();
        Assert.AreEqual(Status.Supported, Evaluate(scenario));
        scenario.CapabilityAbsent = true;
        Assert.AreEqual(Status.Mismatch, Evaluate(scenario));
        scenario.CapabilityAbsent = false;
        Assert.AreEqual(Status.Supported, Evaluate(scenario));
    }

    // ---- Rollback and the existing plan (A.7 "Rollback", A-L2, A2-L2), expressed over the real script text ----

    [TestMethod]
    public void ExistingV1PlanStaysV1OnlyAndThePreChangeValidatorFailsClosedOnAV2Ledger()
    {
        string root = FindRepositoryRoot();
        string scripts = Path.Combine(root, "scripts");
        // The frozen pre-change validator (what existing hosts carry) throws for any origin other than pinned-unsigned-v1;
        // DeploymentCapabilityScriptTests executes it against a real-writer v2 ledger.
        string frozen = File.ReadAllText(Path.Combine(root, "tests", "PCSteward.Security.FixtureTests", "Fixtures", "PreV2PayloadValidator.ps1.txt"));
        StringAssert.Contains(frozen, "if ($State.payloadOrigin -cne 'pinned-unsigned-v1' -or !$hasOtherFields) { throw 'Unrecognised pinned-payload provenance.' }");
        Assert.IsFalse(frozen.Contains(DeploymentCapabilityPolicy.PayloadOriginV2, StringComparison.Ordinal));
        // Plan schema v1 (the existing approved plan) fixes the legacy-v6 preflight and the v1 postcondition and cannot name origins.
        string redeploy = File.ReadAllText(Path.Combine(scripts, "Invoke-OwnerOnlyApprovedRedeployment.ps1"));
        StringAssert.Contains(redeploy, "if($OwnerOnlyPlan.schema -ceq 'pc-steward-exact-redeploy-v1'){");
        StringAssert.Contains(redeploy, "{throw 'Plan schema v1 cannot name payload origins.'}");
        StringAssert.Contains(redeploy, "$expectedOldOrigin='legacy-v6';$expectedNewOrigin='pinned-unsigned-v1'");
        StringAssert.Contains(redeploy, "$legacy.payloadOrigin -cne $expectedOldOrigin");
        StringAssert.Contains(redeploy, "if($candidate.PayloadOrigin -cne $expectedNewOrigin){throw");
        StringAssert.Contains(redeploy, "$proof.payloadOrigin -cne $expectedNewOrigin");
        StringAssert.Contains(redeploy, "Executing ANY new plan (v1 or v2) needs its own separate exact owner approval.");
        // The installer derives the origin from the pinned manifest and validates the capability bytes before any certificate mutation.
        string install = File.ReadAllText(Path.Combine(scripts, "Install-OwnerOnlyProtectedTestDeployment.ps1"));
        StringAssert.Contains(install, "payloadOrigin = $candidate.PayloadOrigin;");
        StringAssert.Contains(install, "if ($origin -cne $candidate.PayloadOrigin) {");
        Assert.IsFalse(install.Contains("'pinned-unsigned-v1'", StringComparison.Ordinal));
        int validated = install.IndexOf("-ExpectedSha256 $CandidateManifestSha256 -PayloadRoot $CandidateRoot", StringComparison.Ordinal);
        int certificate = install.IndexOf("New-OwnerOnlyCertificate ([ref]$certificateState)", StringComparison.Ordinal);
        Assert.IsTrue(validated > 0 && certificate > validated, "capability validated before any certificate mutation or signing");
        // The v2 ledger this policy supports is exactly the shape the pre-change validator rejects by origin.
        byte[] v2 = Encoding.UTF8.GetBytes(Ps7Json.Render(Ledgers.Completed(DeploymentCapabilityPolicy.PayloadOriginV2)));
        Assert.IsTrue(DeploymentCapabilityPolicy.TryReadLedger(v2, out DeploymentLedger? ledger));
        Assert.AreNotEqual(DeploymentCapabilityPolicy.PayloadOriginV1, ledger!.PayloadOrigin);
    }

    // ---- Fixture machinery ----

    // ---- Staged evaluation used by the Windows reader ("the first failure ends evaluation") ----

    public static IEnumerable<object[]> StagedScenarios() =>
    [
        ["consistent", new Scenario(), true, true, "Supported"],
        ["ledger-absent", new Scenario { LedgerAbsent = true }, false, false, "Absent"],
        ["ledger-v1", new Scenario { Ledger = Encoding.UTF8.GetBytes(Ps7Json.Render(Ledgers.Completed(DeploymentCapabilityPolicy.PayloadOriginV1))) }, false, false, "Absent"],
        ["ledger-pending", new Scenario { Ledger = Encoding.UTF8.GetBytes(Ps7Json.Render(Ledgers.Pending(null, ["My"]))) }, false, false, "Incomplete"],
        ["ledger-bom", new Scenario { Ledger = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Ps7Json.Render(Ledgers.Completed(DeploymentCapabilityPolicy.PayloadOriginV2)))] }, false, false, "Unreadable"],
        ["manifest-absent", new Scenario { ManifestAbsent = true }, true, false, "Mismatch"],
        ["manifest-hash", new Scenario { ManifestHashOverride = Hash("other manifest") }, true, false, "Mismatch"],
        ["capability-absent", new Scenario { CapabilityAbsent = true }, true, false, "Mismatch"],
        ["capability-protocol-1", new Scenario { Capability = Encoding.ASCII.GetBytes("pc-steward-deployment-capability:1\nhelper-protocol:1\noperation:Update/ChocolateyMachineRuntimeLimitsV3\n") }, true, false, "Mismatch"],
        ["binding-hash", new Scenario { ObservedHashOverride = ("PCSteward.Provider.Chocolatey.dll", Hash("replaced")) }, true, true, "Mismatch"],
        ["binding-unobserved", new Scenario { OmitObservation = "PCSteward.Desktop.dll" }, true, true, "Unreadable"],
    ];

    [TestMethod]
    [DynamicData(nameof(StagedScenarios))]
    public void StagedEvaluationAgreesWithTheFullPolicyAndStopsAtTheFirstFailingStage(
        string name, object scenarioObject, bool passesLedger, bool passesStatement, string fullName)
    {
        Status full = Enum.Parse<Status>(fullName);
        DeploymentCapabilityInputs inputs = ((Scenario)scenarioObject).Inputs();
        Assert.AreEqual(full, DeploymentCapabilityPolicy.Evaluate(inputs, Route).Status, name);
        // Later-stage inputs are ignored by an earlier stage, as the reader has not read them yet.
        DeploymentCapabilityInputs ledgerOnly = inputs with { CandidateManifestBytes = null, CapabilityBytes = null, ObservedHashes = new Dictionary<string, DeploymentObservedFile>() };
        Assert.AreEqual(passesLedger, DeploymentCapabilityPolicy.PassesThrough(ledgerOnly, Route, DeploymentCapabilityStage.Ledger, out Status ledgerFailure), name);
        if (!passesLedger) Assert.AreEqual(full, ledgerFailure, name);
        if (!passesLedger) return;
        DeploymentCapabilityInputs statement = inputs with { ObservedHashes = new Dictionary<string, DeploymentObservedFile>() };
        Assert.AreEqual(passesStatement, DeploymentCapabilityPolicy.PassesThrough(statement, Route, DeploymentCapabilityStage.Statement, out Status statementFailure), name);
        if (!passesStatement) Assert.AreEqual(full, statementFailure, name);
    }

    [TestMethod]
    public void StagedEvaluationNeverReportsSupportedAndRefusesTheBindingStageOrBadInput()
    {
        DeploymentCapabilityInputs inputs = new Scenario().Inputs();
        Assert.IsFalse(DeploymentCapabilityPolicy.PassesThrough(inputs, Route, DeploymentCapabilityStage.BindingSet, out Status failure));
        Assert.AreEqual(Status.Unreadable, failure, "only Evaluate can report a full result");
        Assert.IsFalse(DeploymentCapabilityPolicy.PassesThrough(inputs, Route, (DeploymentCapabilityStage)99, out failure));
        Assert.AreEqual(Status.Unreadable, failure);
        Assert.IsFalse(DeploymentCapabilityPolicy.PassesThrough(null!, Route, DeploymentCapabilityStage.Ledger, out failure));
        Assert.AreEqual(Status.Unreadable, failure);
        // A route outside the statement fails at the statement stage, never at the ledger stage.
        Assert.IsTrue(DeploymentCapabilityPolicy.PassesThrough(inputs, UpdateRouteContract.WinGetUserExactV3, DeploymentCapabilityStage.Ledger, out _));
        Assert.IsFalse(DeploymentCapabilityPolicy.PassesThrough(inputs, UpdateRouteContract.WinGetUserExactV3, DeploymentCapabilityStage.Statement, out failure));
        Assert.AreEqual(Status.Mismatch, failure);
    }

    private static Status Evaluate(Scenario scenario, UpdateRouteContract route = Route) =>
        DeploymentCapabilityPolicy.Evaluate(scenario.Inputs(), route).Status;

    private static void Set(List<(string Key, object? Value)> ledger, string key, object? value) =>
        ledger[ledger.FindIndex(item => item.Key == key)] = (key, value);

    internal static IReadOnlyList<(string, object?)> Obj(params (string, object?)[] fields) => fields;

    internal static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }

    /// <summary>Key lists transcribed from the ADR-022 installer's ledger writers, in writer order.</summary>
    internal static class Ledgers
    {
        private static readonly string[] Extras = ["App.xbf", "PCSteward.Desktop.pri", @"Resources\MainWindow.xbf", "PCSteward.ScheduledHost.dll"];

        // Write-PendingLedger: schema, installRoot, installerSid, certificateThumbprint, certificateSubject, ownedCertificateStores = @(...),
        // completed = $false, rollbackRequired = $false, pendingCertificate = ($null -eq Certificate).
        internal static IReadOnlyList<(string, object?)> Pending(string? thumbprint, string[] stores) =>
        [
            ("schema", DeploymentCapabilityPolicy.LedgerSchema), ("installRoot", InstallRoot), ("installerSid", Sid),
            ("certificateThumbprint", thumbprint), ("certificateSubject", Subject), ("ownedCertificateStores", stores.Cast<object?>().ToArray()),
            ("completed", false), ("rollbackRequired", false), ("pendingCertificate", thumbprint is null),
        ];

        // Failure path: certificateThumbprint/certificateSubject/ownedCertificateStores are `if` expressions, so PowerShell unrolls
        // the store list (null, one string, or an array); completed = $false; rollbackRequired = $true; rollbackFailures = @(...);
        // originalFailure = exception message.
        internal static IReadOnlyList<(string, object?)> Rollback(string? thumbprint, string? subject, object? stores) =>
        [
            ("schema", DeploymentCapabilityPolicy.LedgerSchema), ("installRoot", InstallRoot), ("installerSid", Sid),
            ("certificateThumbprint", thumbprint), ("certificateSubject", subject), ("ownedCertificateStores", stores),
            ("completed", false), ("rollbackRequired", true),
            ("rollbackFailures", new object?[] { @"installation remains at C:\Program Files\PC Steward" }),
            ("originalFailure", "Could not sign PCSteward.Desktop.exe with the owner-only certificate."),
        ];

        // $completedState (Task 48) and the pre-Task-48 completed writer (no payloadOrigin/candidateManifestSha256/executableImages).
        internal static IReadOnlyList<(string, object?)> Completed(string? origin, bool lowercaseHashes = false,
            IReadOnlyDictionary<string, string>? hashes = null, string? manifestSha256 = null, string? omit = null)
        {
            hashes ??= Scenario.BindingHashes(DeploymentCapabilityPolicy.CanonicalCapabilityBytes());
            List<(string, object?)> files = [];
            foreach (string leaf in DeploymentCapabilityPolicy.BindingSet.Concat(Extras).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (leaf == omit) continue;
                if (origin != DeploymentCapabilityPolicy.PayloadOriginV2 && leaf == DeploymentCapabilityPolicy.CapabilityFileName) continue;
                string hash = hashes is not null && hashes.TryGetValue(leaf, out string? known) ? known : Hash("content " + leaf);
                files.Add(("", Obj(("path", leaf), ("sha256", lowercaseHashes && !leaf.EndsWith(".exe", StringComparison.Ordinal) ? hash.ToLowerInvariant() : hash))));
            }
            List<(string, object?)> ledger =
            [
                ("schema", DeploymentCapabilityPolicy.LedgerSchema), ("installRoot", InstallRoot), ("installerSid", Sid),
                ("certificateThumbprint", Thumbprint), ("certificateSubject", Subject),
                ("ownedCertificateStores", new object?[] { "My", "Root", "TrustedPublisher" }),
                ("createdAtUtc", new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567).ToString("O", CultureInfo.InvariantCulture)),
                ("completed", true), ("files", files.Select(file => file.Item2).ToArray()),
            ];
            if (origin is not null)
            {
                ledger.Add(("payloadOrigin", origin));
                ledger.Add(("candidateManifestSha256", manifestSha256 ?? Scenario.DefaultManifestSha256()));
                ledger.Add(("executableImages", new object?[] { Image("PCSteward.Desktop.exe"), Image("PCSteward.ElevatedHelper.exe") }));
            }
            return ledger;
        }

        internal static IReadOnlyList<(string, object?)> Image(string leaf) => Obj(("path", leaf), ("bytes", 150016L), ("originalChecksum", 0L));
    }

    /// <summary>A consistent v2 deployment whose pieces can each be broken independently.</summary>
    private sealed class Scenario
    {
        internal byte[]? Ledger { get; set; }
        internal bool LedgerAbsent { get; set; }
        internal byte[] Capability { get; set; } = DeploymentCapabilityPolicy.CanonicalCapabilityBytes();
        internal bool LowercaseLedgerHashes { get; set; }
        internal bool ManifestAbsent { get; set; }
        internal byte[]? ManifestOverride { get; set; }
        internal string? ManifestHashOverride { get; set; }
        internal bool ManifestExtraKey { get; set; }
        internal bool OmitCapabilityFromManifest { get; set; }
        internal bool DuplicateCapabilityInManifest { get; set; }
        internal string CapabilityManifestPath { get; set; } = DeploymentCapabilityPolicy.CapabilityFileName;
        internal string? CapabilityManifestHashOverride { get; set; }
        internal string? OmitFromLedger { get; set; }
        internal bool CapabilityAbsent { get; set; }
        internal byte[]? CapabilityReadOverride { get; set; }
        internal (string Leaf, string Hash)? ObservedHashOverride { get; set; }
        internal (string Leaf, long Length)? ObservedLengthOverride { get; set; }
        internal long? UniformObservedLength { get; set; }
        internal string? OmitObservation { get; set; }
        internal bool ExtraObservation { get; set; }
        internal bool NullObservations { get; set; }

        internal static Dictionary<string, string> BindingHashes(byte[] capability)
        {
            Dictionary<string, string> hashes = DeploymentCapabilityPolicy.BindingSet.ToDictionary(leaf => leaf, leaf => Hash("content " + leaf), StringComparer.Ordinal);
            hashes[DeploymentCapabilityPolicy.CapabilityFileName] = Convert.ToHexString(SHA256.HashData(capability));
            return hashes;
        }

        /// <summary>SHA-256 of the default scenario's candidate manifest, so a default v2 writer fixture is fully consistent.</summary>
        internal static string DefaultManifestSha256() => Convert.ToHexString(SHA256.HashData(new Scenario().Manifest(BindingHashes(DeploymentCapabilityPolicy.CanonicalCapabilityBytes()))));

        internal DeploymentCapabilityInputs Inputs()
        {
            Dictionary<string, string> hashes = BindingHashes(Capability);
            byte[] manifest = ManifestOverride ?? Manifest(hashes);
            string manifestHashValue = ManifestHashOverride ?? Convert.ToHexString(SHA256.HashData(manifest));
            byte[]? ledger = LedgerAbsent ? null : Ledger ?? Encoding.UTF8.GetBytes(Ps7Json.Render(
                Ledgers.Completed(DeploymentCapabilityPolicy.PayloadOriginV2, LowercaseLedgerHashes, hashes, manifestHashValue, OmitFromLedger)));

            Dictionary<string, DeploymentObservedFile> observed = new(StringComparer.Ordinal);
            foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
            {
                if (leaf == OmitObservation) continue;
                long length = UniformObservedLength ?? (leaf == DeploymentCapabilityPolicy.CapabilityFileName ? Capability.Length : 4096);
                if (ObservedLengthOverride is { } lengthOverride && lengthOverride.Leaf == leaf) length = lengthOverride.Length;
                string hash = ObservedHashOverride is { } hashOverride && hashOverride.Leaf == leaf ? hashOverride.Hash : hashes[leaf];
                observed[leaf] = new(length, hash);
            }
            if (ExtraObservation) observed["PCSteward.ScheduledHost.dll"] = new(4096, Hash("content PCSteward.ScheduledHost.dll"));

            return new(ledger, ManifestAbsent ? null : manifest, CapabilityAbsent ? null : CapabilityReadOverride ?? Capability,
                Sid, InstallRoot, NullObservations ? null! : observed);
        }

        private byte[] Manifest(Dictionary<string, string> hashes)
        {
            string capabilityHash = hashes[DeploymentCapabilityPolicy.CapabilityFileName];
            List<object?> manifestEntries = [];
            foreach (string leaf in hashes.Keys.Order(StringComparer.Ordinal))
            {
                if (leaf == DeploymentCapabilityPolicy.CapabilityFileName) continue;
                // Unsigned manifest hashes for the two apphosts differ from the signed ledger hashes; the policy never compares them.
                string manifestHash = leaf.EndsWith(".exe", StringComparison.Ordinal) ? Hash("unsigned " + leaf) : hashes[leaf].ToLowerInvariant();
                manifestEntries.Add(Obj(("bytes", 4096L), ("path", leaf), ("sha256", manifestHash)));
            }
            if (!OmitCapabilityFromManifest)
            {
                IReadOnlyList<(string, object?)> capabilityEntry = ManifestExtraKey
                    ? Obj(("bytes", (long)Capability.Length), ("path", CapabilityManifestPath), ("sha256", CapabilityManifestHashOverride ?? capabilityHash), ("extra", 1L))
                    : Obj(("bytes", (long)Capability.Length), ("path", CapabilityManifestPath), ("sha256", CapabilityManifestHashOverride ?? capabilityHash));
                manifestEntries.Add(capabilityEntry);
                if (DuplicateCapabilityInManifest) manifestEntries.Add(capabilityEntry);
            }
            return Encoding.UTF8.GetBytes(Ps7Json.RenderValue(manifestEntries.ToArray()));
        }
    }

    /// <summary>
    /// Minimal emitter for the PowerShell 7 <c>ConvertTo-Json</c> (Newtonsoft, indented) shape: two-space indent, <c>"key": value</c>,
    /// CRLF line endings, <c>[]</c> for empty arrays, default string escaping.
    /// </summary>
    internal static class Ps7Json
    {
        internal static string Render(IReadOnlyList<(string, object?)> fields) => RenderValue(fields);

        internal static string RenderValue(object? value)
        {
            StringBuilder builder = new();
            Write(builder, value, 0);
            return builder.ToString();
        }

        private static void Write(StringBuilder builder, object? value, int depth)
        {
            switch (value)
            {
                case null: builder.Append("null"); break;
                case bool flag: builder.Append(flag ? "true" : "false"); break;
                case long number: builder.Append(number.ToString(CultureInfo.InvariantCulture)); break;
                case string text: WriteString(builder, text); break;
                case IReadOnlyList<(string, object?)> fields:
                    if (fields.Count == 0) { builder.Append("{}"); break; }
                    builder.Append('{');
                    for (int index = 0; index < fields.Count; index++)
                    {
                        builder.Append(index == 0 ? "\r\n" : ",\r\n").Append(' ', (depth + 1) * 2);
                        WriteString(builder, fields[index].Item1);
                        builder.Append(": ");
                        Write(builder, fields[index].Item2, depth + 1);
                    }
                    builder.Append("\r\n").Append(' ', depth * 2).Append('}');
                    break;
                case object?[] items:
                    if (items.Length == 0) { builder.Append("[]"); break; }
                    builder.Append('[');
                    for (int index = 0; index < items.Length; index++)
                    {
                        builder.Append(index == 0 ? "\r\n" : ",\r\n").Append(' ', (depth + 1) * 2);
                        Write(builder, items[index], depth + 1);
                    }
                    builder.Append("\r\n").Append(' ', depth * 2).Append(']');
                    break;
                default: throw new ArgumentException("Unsupported fixture value.", nameof(value));
            }
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
