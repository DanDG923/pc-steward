using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 S7 (qualification probe spec §5.3, §8 and §9 obligations 1–3; exact-artifact test design
/// §8 C1–C7): <c>scripts/evidence/Task50-QualificationProbe.ps1</c>, the owner's host tooling for the Part C probe run on the
/// activated deployment. Structural tests pin its bytes and its single launch; the fixture rows run the REAL text of its pure
/// functions (taken from the parsed script) under PowerShell 7 and Windows PowerShell 5.1 on result documents written by
/// the real <see cref="QualificationProbeResultDocument"/> writer, and on synthetic build records, ledgers, trees and folders.
/// The host steps (C1 reads of the installed deployment, the C3 launch and the C7 deletion) are owner-run only.
/// </summary>
[TestClass]
public sealed class QualificationProbeHostToolingTests
{
    private const string ScriptName = "Task50-QualificationProbe.ps1";
    private static readonly string[] VerdictKeys = ["E1", "A3", "E2", "E3", "E5", "OVERALL"];
    private static readonly string[] Deletes = ["[IO.File]::Delete($resultFile)", "[IO.Directory]::Delete($resultFolder, $false)"];
    private static readonly string[] CompiledTokens = ["Update/ChocolateyClientSelfUpdateV1", "Update/ChocolateyMachineRuntimeLimitsV3"];
    private static readonly string[] Launch = ["Start-Process -FilePath $desktop -ArgumentList '--qualify-chocolatey-helper' -Wait -PassThru"];
    private static readonly string[] Inconclusive =
        ["ElevationDeclined", "GateBusy", "GateUnavailable", "GateRecoveryRequired", "GateResetPending", "ConflictingProcess", "DesktopNotLimited", "HelperNotStarted"];
    private static readonly string[] PassLines =
    [
        "C6 VERDICT E1: PASS (tokenVerdict=Accepted)",
        "C6 VERDICT A3: PASS (g1Status=Supported for every token)",
        "C6 VERDICT E2: PASS (observed 2.7.5)",
        "C6 VERDICT E3: PASS (machine facts; owner prompt judgment required)",
        "C6 VERDICT E5: PASS (C4=C2)",
        "C6 OVERALL: PASS (valid only with the owner E3 prompt confirmation; proves UAC and pre-launch enforcement only)",
        "C6 OWNER-CHECK E3: PASS needs exactly one UAC prompt, for PCSteward.ElevatedHelper.exe signed by the owner-only test signer, whose details show --kind qualify-chocolatey-helper right after the program path.",
    ];

    [TestMethod]
    public void ScriptIsAsciiCrlfCheckedInWithoutConversionAndLaunchesOnlyTheInstalledDesktopProbe()
    {
        string root = FindRepositoryRoot();
        byte[] bytes = File.ReadAllBytes(ScriptPath(root));
        for (int index = 0; index < bytes.Length; index++)
        {
            Assert.IsTrue(bytes[index] < 0x80, $"non-ASCII byte at offset {index}");
            if (bytes[index] == (byte)'\n') Assert.IsTrue(index > 0 && bytes[index - 1] == (byte)'\r', $"bare LF at offset {index}");
            if (bytes[index] == (byte)'\r') Assert.IsTrue(index + 1 < bytes.Length && bytes[index + 1] == (byte)'\n', $"bare CR at offset {index}");
        }
        // The approval binds the CRLF bytes; a checkout must not rewrite them.
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, ".gitattributes")), "scripts/evidence/Task50-QualificationProbe.ps1 -text");
        string text = Encoding.ASCII.GetString(bytes);
        StringAssert.Contains(text, "[ValidateSet('Check', 'Run')][string]$Mode");
        StringAssert.Contains(text, "[ValidateSet('NoBlockingRecord')][string]$ActivityCheck");
        Assert.AreEqual(1, CountOf(text, "Start-Process "), "exactly one process start");
        StringAssert.Contains(text, "$process = Start-Process -FilePath $desktop -ArgumentList '--qualify-chocolatey-helper' -Wait -PassThru");
        StringAssert.Contains(text, "$desktop = Join-Path $installRoot 'PCSteward.Desktop.exe'");
        StringAssert.Contains(text, "$installRoot = Join-Path $programFiles 'PC Steward'");
        StringAssert.Contains(text, "$resultFolder = Join-Path $temp 'pcsteward-exact-artifact'");
        StringAssert.Contains(text, "if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Stop-ProbeRun");
        StringAssert.Contains(text, "if (Test-Path -LiteralPath $resultFolder) { Stop-ProbeRun 'precondition (the result folder already exists; a run never reuses it)' }");
        StringAssert.Contains(text, "if (!$created) { 'C7 CLEANUP-NOT-NEEDED'; return }");
        // C1 stops on anything other than a probe build (S7 obligation 1) before C2 or C3.
        StringAssert.Contains(text, "catch { Stop-ProbeRun ('not a probe build (' +");
        Assert.IsTrue(text.IndexOf("not a probe build (", StringComparison.Ordinal) < text.IndexOf("# ---- C2 baseline ----", StringComparison.Ordinal));
        Assert.IsTrue(text.IndexOf("# ---- C2 baseline ----", StringComparison.Ordinal) < text.IndexOf("Start-Process -FilePath", StringComparison.Ordinal));
        Assert.IsTrue(text.IndexOf("if ($Mode -ceq 'Check')", StringComparison.Ordinal) < text.IndexOf("Start-Process -FilePath", StringComparison.Ordinal),
            "Check mode returns before the launch");
        foreach (string banned in new[] { "Invoke-Expression", "-Verb", "RunAs", "Start-Transcript", "Out-File", "Set-Content", "Remove-Item", "Add-Type", "EncodedCommand" })
            Assert.IsFalse(text.Contains(banned, StringComparison.OrdinalIgnoreCase), banned);
    }

    [TestMethod]
    public void TheScriptsContractListsAreTheCompiledDomainsOfTheActivationBuild() =>
        // The fixture row below dumps the script's lists; this row checks the C# side of the same contract directly.
        CollectionAssert.AreEqual(CompiledTokens, QualificationProbeContract.Compiled.G1Keys.ToArray());

    [TestMethod]
    [DataRow("pwsh")]
    [DataRow("windows-powershell")]
    public async Task PureFunctionsReadTheRealResultDocumentsAndInterpretThemAsSpecified(string shell)
    {
        string? executable = ResolveShell(shell);
        if (executable is null)
        {
            if (shell == "windows-powershell") Assert.Inconclusive("Windows PowerShell 5.1 runs only on Windows; this row is Windows-CI evidence.");
            Assert.Inconclusive("PowerShell 7 (pwsh) is not on PATH; the fixture needs it on this platform.");
        }
        string root = FindRepositoryRoot();
        DirectoryInfo work = Directory.CreateTempSubdirectory("pc-steward-probe-tooling-");
        try
        {
            Dictionary<string, Case> cases = WriteCases(work.FullName);
            (int exitCode, string output, string error) = await RunAsync(executable!, root, TimeSpan.FromSeconds(120),
                "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "tests", "PCSteward.Security.FixtureTests", "Fixtures", "QualificationProbeHostTooling.ps1"),
                "-RepositoryRoot", root, "-WorkRoot", work.FullName);
            Assert.AreEqual(0, exitCode, $"Fixture failed:\n{output}\n{error}");
            StringAssert.Contains(output, "PASS: qualification probe host tooling fixtures");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(work.FullName, "results.json")));
            JsonElement result = document.RootElement;

            Assert.AreEqual(0, result.GetProperty("parseErrors").GetInt32(), "parse errors");
            string[] violations = Strings(result.GetProperty("violations"));
            Assert.AreEqual(0, violations.Length, string.Join("\n", violations));
            CollectionAssert.AreEqual(Launch, Strings(result.GetProperty("startProcess")));
            CollectionAssert.AreEqual(Deletes, Strings(result.GetProperty("deletes")), "C7 deletes only the checked result file and folder");

            AssertContract(result.GetProperty("contract"), root);
            JsonElement documents = result.GetProperty("documents");
            CollectionAssert.AreEquivalent(cases.Keys.ToArray(), documents.EnumerateObject().Select(item => item.Name).ToArray());
            foreach ((string name, Case expected) in cases)
            {
                JsonElement actual = documents.GetProperty(name);
                Assert.AreEqual(expected.State, actual.GetProperty("state").GetString(), $"{name}: {actual}");
                if (expected.Reason is not null) Assert.AreEqual(expected.Reason, actual.GetProperty("reason").GetString(), name);
                CollectionAssert.AreEqual(expected.Verdicts, VerdictKeys.Select(key => actual.GetProperty("verdicts").GetProperty(key).GetString()).ToArray(), $"{name}: {actual}");
                if (expected.E3Detail is not null) Assert.AreEqual(expected.E3Detail, actual.GetProperty("details").GetProperty("E3").GetString(), name);
                string[] lines = Strings(actual.GetProperty("lines"));
                Assert.HasCount(7, lines, name);
                Assert.IsTrue(lines.All(line => line.StartsWith("C6 ", StringComparison.Ordinal)), name);
                foreach (string line in lines)
                    foreach (string secret in new[] { "\\", ":\\", "S-1-5", "Users", "Program Files", "Temp" })
                        Assert.IsFalse(line.Contains(secret, StringComparison.Ordinal), $"{name}: {line}");
            }
            CollectionAssert.AreEqual(PassLines, Strings(documents.GetProperty("accepted-pass").GetProperty("lines")));

            AssertBuildRecords(result.GetProperty("buildRecords"));
            AssertLedgers(result.GetProperty("ledgers"));
            AssertTree(result.GetProperty("tree"));
            StringAssert.Contains(result.GetProperty("treeRootRefused").GetString(), "tree-root");
            JsonElement folders = result.GetProperty("folders");
            Assert.AreEqual("absent", folders.GetProperty("absent").GetString());
            Assert.AreEqual("removable", folders.GetProperty("only-result").GetString());
            Assert.AreEqual("removable", folders.GetProperty("empty").GetString());
            Assert.AreEqual("unexpected-content", folders.GetProperty("extra-file").GetString());
            Assert.AreEqual("unexpected-content", folders.GetProperty("subdirectory").GetString());
            JsonElement safe = result.GetProperty("safeMessages");
            Assert.AreEqual("STOP: pin differs (desktop)", safe.GetProperty("stop").GetString());
            Assert.AreEqual("fallback", safe.GetProperty("path").GetString(), "an exception text with a path is never printed");
            Assert.AreEqual("result-capabilityStatus-keys", safe.GetProperty("resultToken").GetString());
            Assert.AreEqual("fallback", safe.GetProperty("resultOther").GetString());
        }
        finally { work.Delete(recursive: true); }
    }

    private sealed record Case(string State, string? Reason, string[] Verdicts, string? E3Detail = null);

    /// <summary>The result documents, written with the real writer (and raw mutations of them), and their expected readings.</summary>
    private static Dictionary<string, Case> WriteCases(string work)
    {
        DateTimeOffset started = DesktopProbeFixture.Started, finished = started.AddSeconds(42);
        QualificationProbeTransportFacts trust = QualificationProbeTransportFacts.None with { InitialLaunchTrust = true, FinalLaunchTrust = true };
        Dictionary<string, QualificationProbeRun> runs = new()
        {
            ["accepted-pass"] = DesktopProbeFixture.Accepted(),
            ["accepted-token-refused"] = DesktopProbeFixture.Accepted(DesktopProbeFixture.TokenRefused()),
            ["accepted-capability-refused"] = DesktopProbeFixture.Accepted(DesktopProbeFixture.Passing() with
            {
                G1Status = DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.Absent), Observation = QualificationProbeObservation.NotRun,
                NodesEvaluated = 0, AncestorsEvaluated = 0, ObservedClientVersion = null, FingerprintPresent = false, FingerprintLength = 0,
                Diagnostic = QualificationProbeWireCodec.CapabilityRefusedDiagnostic,
            }),
            ["accepted-refused-granted"] = DesktopProbeFixture.Accepted(DesktopProbeFixture.Passing() with
            {
                Observation = QualificationProbeObservation.Refused, FailedStep = 4, FailedCheck = QualificationProbeFailedCheck.StepRule,
                FailedAccess = QualificationProbeFailedAccess.Granted, RefusalReason = QualificationProbeRefusalReason.MachineInputsUnsupported,
                ObservedClientVersion = null, FingerprintPresent = false, FingerprintLength = 0, Diagnostic = QualificationProbeWireCodec.ObservationRefusedDiagnostic,
            }),
            ["accepted-timed-out"] = DesktopProbeFixture.Accepted(DesktopProbeFixture.Passing() with
            {
                Observation = QualificationProbeObservation.TimedOut, ObservedClientVersion = null, FingerprintPresent = false, FingerprintLength = 0,
                Diagnostic = QualificationProbeWireCodec.ObservationTimedOutDiagnostic,
            }),
            ["gate-busy"] = QualificationProbeRun.Refused(QualificationProbeDesktopOutcome.GateBusy),
            ["elevation-declined"] = new(QualificationProbeDesktopOutcome.ElevationDeclined, DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.Supported),
                trust, DesktopProbeFixture.Pins(), null),
            ["capability-not-held"] = new(QualificationProbeDesktopOutcome.CapabilityNotHeld,
                [new("Update/ChocolateyClientSelfUpdateV1", QualificationProbeCapabilityStatus.Absent), new("Update/ChocolateyMachineRuntimeLimitsV3", QualificationProbeCapabilityStatus.Supported)],
                trust, null, null),
            ["pins-differ"] = DesktopProbeFixture.Accepted() with { Pins = DesktopProbeFixture.Pins('F') },
            ["exit-code-1"] = DesktopProbeFixture.Accepted() with { Transport = DesktopProbeFixture.AllFacts(1) },
            ["eof-not-observed"] = DesktopProbeFixture.Accepted() with { Transport = DesktopProbeFixture.AllFacts() with { PipeEofObserved = false } },
        };
        Dictionary<string, string> files = [];
        foreach ((string name, QualificationProbeRun run) in runs)
        {
            File.WriteAllBytes(Path.Combine(work, name + ".json"), QualificationProbeResultDocument.Create(run, started, finished));
            files[name] = name + ".json";
        }
        string accepted = Encoding.ASCII.GetString(QualificationProbeResultDocument.Create(runs["accepted-pass"], started, finished));
        string busy = Encoding.ASCII.GetString(QualificationProbeResultDocument.Create(runs["gate-busy"], started, finished));
        string probeObject = accepted[accepted.IndexOf("\"probe\":{", StringComparison.Ordinal)..accepted.IndexOf(",\"startedAtUtc\"", StringComparison.Ordinal)];
        Dictionary<string, (byte[] Bytes, string Reason)> mutations = new()
        {
            ["bom"] = ([0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes(accepted)], "result-bom"),
            ["trailing-space"] = (Encoding.ASCII.GetBytes(accepted + " "), "result-json-trailing"),
            ["trailing-newline"] = (Encoding.ASCII.GetBytes(accepted + "\n"), "result-not-printable-ascii"),
            ["one-token-status"] = (Encoding.ASCII.GetBytes(accepted.Replace("\"Update/ChocolateyClientSelfUpdateV1\":\"Supported\",", "", StringComparison.Ordinal)), "result-capabilityStatus-keys"),
            ["extra-key"] = (Encoding.ASCII.GetBytes(accepted[..^1] + ",\"note\":\"x\"}"), "result-top-keys"),
            ["reordered"] = (Encoding.ASCII.GetBytes(accepted.Replace("\"startedAtUtc\":\"2026-11-04T10:00:00.000Z\",\"finishedAtUtc\":\"2026-11-04T10:00:42.000Z\"",
                "\"finishedAtUtc\":\"2026-11-04T10:00:42.000Z\",\"startedAtUtc\":\"2026-11-04T10:00:00.000Z\"", StringComparison.Ordinal)), "result-top-keys"),
            ["lowercase-pin"] = (Encoding.ASCII.GetBytes(accepted.Replace(new string('A', 64), new string('a', 64), StringComparison.Ordinal)), "result-ledgerSha256"),
            ["escape"] = (Encoding.ASCII.GetBytes(accepted.Replace("\"probe-observed\"", "\"probe-\\u006fbserved\"", StringComparison.Ordinal)), "result-json-string"),
            ["duplicate-key"] = (Encoding.ASCII.GetBytes(accepted.Replace("{\"schema\":", "{\"schema\":\"x\",\"schema\":", StringComparison.Ordinal)), "result-json-duplicate"),
            ["unknown-outcome"] = (Encoding.ASCII.GetBytes(accepted.Replace("\"ResponseAccepted\"", "\"Success\"", StringComparison.Ordinal)), "result-desktopOutcome"),
            ["probe-on-gate-busy"] = (Encoding.ASCII.GetBytes(busy.Replace("\"probe\":null", probeObject, StringComparison.Ordinal)), "result-probe-presence"),
            ["oversize"] = (Encoding.ASCII.GetBytes(accepted + new string(' ', 4097 - accepted.Length)), "result-size"),
            ["path-value"] = (Encoding.ASCII.GetBytes(accepted.Replace("\"probe-observed\"", "\"C:\\\\Users\\\\owner\"", StringComparison.Ordinal)), "result-json-string"),
        };
        foreach ((string name, (byte[] bytes, _)) in mutations)
        {
            File.WriteAllBytes(Path.Combine(work, name + ".json"), bytes);
            files[name] = name + ".json";
        }

        string[] fail3 = ["INCONCLUSIVE", "INCONCLUSIVE", "INCONCLUSIVE", "FAIL", "PASS", "FAIL"];
        Dictionary<string, Case> expected = new()
        {
            ["accepted-pass"] = new("valid", null, ["PASS", "PASS", "PASS", "PASS", "PASS", "PASS"]),
            ["accepted-token-refused"] = new("valid", null, ["FAIL", "INCONCLUSIVE", "INCONCLUSIVE", "PASS", "PASS", "FAIL"]),
            ["accepted-capability-refused"] = new("valid", null, ["PASS", "FAIL", "INCONCLUSIVE", "PASS", "PASS", "FAIL"]),
            ["accepted-refused-granted"] = new("valid", null, ["PASS", "PASS", "FAIL", "PASS", "PASS", "FAIL"]),
            ["accepted-timed-out"] = new("valid", null, ["PASS", "PASS", "INCONCLUSIVE", "PASS", "PASS", "INCONCLUSIVE"]),
            ["gate-busy"] = new("valid", null, ["INCONCLUSIVE", "INCONCLUSIVE", "INCONCLUSIVE", "INCONCLUSIVE", "PASS", "INCONCLUSIVE"], "desktopOutcome=GateBusy"),
            ["elevation-declined"] = new("valid", null, ["INCONCLUSIVE", "INCONCLUSIVE", "INCONCLUSIVE", "INCONCLUSIVE", "PASS", "INCONCLUSIVE"], "desktopOutcome=ElevationDeclined"),
            ["capability-not-held"] = new("valid", null, fail3, "desktopOutcome=CapabilityNotHeld"),
            ["pins-differ"] = new("valid", null, ["PASS", "PASS", "PASS", "FAIL", "PASS", "FAIL"], "pins-differ-from-C1"),
            ["exit-code-1"] = new("valid", null, ["PASS", "PASS", "PASS", "FAIL", "PASS", "FAIL"], "helperExitCode"),
            ["eof-not-observed"] = new("valid", null, ["PASS", "PASS", "PASS", "FAIL", "PASS", "FAIL"], "false-facts=pipeEofObserved"),
            ["no-result-file"] = new("absent", null, ["INCONCLUSIVE", "INCONCLUSIVE", "INCONCLUSIVE", "INCONCLUSIVE", "PASS", "INCONCLUSIVE"], "no-result-file"),
            ["database-changed"] = new("valid", null, ["PASS", "PASS", "PASS", "PASS", "FAIL", "FAIL"]),
            ["choco-running-after"] = new("valid", null, ["PASS", "PASS", "PASS", "PASS", "FAIL", "FAIL"]),
            ["baseline-missing"] = new("valid", null, ["PASS", "PASS", "PASS", "PASS", "INCONCLUSIVE", "INCONCLUSIVE"]),
        };
        foreach ((string name, (_, string reason)) in mutations)
            expected[name] = new("malformed", reason, fail3, "result-malformed");

        List<object> rows = [];
        foreach (string name in expected.Keys)
        {
            string? file = name switch
            {
                "no-result-file" => null,
                "database-changed" or "choco-running-after" or "baseline-missing" => files["accepted-pass"],
                _ => files[name],
            };
            string e5 = name switch { "database-changed" => "db-changed", "choco-running-after" => "choco-running", "baseline-missing" => "missing", _ => "same" };
            rows.Add(new { name, file, e5 });
        }
        QualificationProbePins pins = DesktopProbeFixture.Pins();
        File.WriteAllText(Path.Combine(work, "cases.json"), JsonSerializer.Serialize(new
        {
            pins = new
            {
                ledgerSha256 = pins.LedgerSha256, capabilityFileSha256 = pins.CapabilityFileSha256, desktopImageSha256 = pins.DesktopImageSha256,
                helperImageSha256 = pins.HelperImageSha256, signerThumbprint = pins.SignerThumbprint,
                // Prefixed so that PowerShell 7's ConvertFrom-Json keeps it a string (it turns ISO dates into DateTime).
                signerNotAfterUtc = "ts:" + QualificationProbeResultDocument.Timestamp(pins.SignerNotAfterUtc),
            },
            cases = rows,
        }));
        return expected;
    }

    /// <summary>The script's closed lists equal the compiled domains, the result-document field orders and the builder's mark.</summary>
    private static void AssertContract(JsonElement contract, string root)
    {
        Assert.AreEqual(QualificationProbeResultDocument.Schema, contract.GetProperty("Schema").GetString());
        CollectionAssert.AreEqual(QualificationProbeResultDocument.Fields.ToArray(), Strings(contract.GetProperty("Fields")));
        CollectionAssert.AreEqual(QualificationProbeResultDocument.PinFields.ToArray(), Strings(contract.GetProperty("PinFields")));
        CollectionAssert.AreEqual(QualificationProbeResultDocument.ProbeFields.ToArray(), Strings(contract.GetProperty("ProbeFields")));
        CollectionAssert.AreEqual(QualificationProbeResultDocument.Fields.Skip(3).Take(11).ToArray(), Strings(contract.GetProperty("Facts")));
        CollectionAssert.AreEqual(Enum.GetNames<QualificationProbeDesktopOutcome>(), Strings(contract.GetProperty("Outcomes")));
        CollectionAssert.AreEqual(QualificationProbeContract.Compiled.G1Keys.ToArray(), Strings(contract.GetProperty("Tokens")), "the activation build's compiled allow-list");
        CollectionAssert.AreEqual(Enum.GetNames<QualificationProbeCapabilityStatus>(), Strings(contract.GetProperty("Statuses")));
        CollectionAssert.AreEqual(Enum.GetNames<QualificationProbeTokenVerdict>(), Strings(contract.GetProperty("Verdicts")));
        CollectionAssert.AreEqual(Enum.GetNames<QualificationProbeObservation>(), Strings(contract.GetProperty("Observations")));
        CollectionAssert.AreEqual(Enum.GetNames<QualificationProbeFailedCheck>(), Strings(contract.GetProperty("Checks")));
        CollectionAssert.AreEqual(Enum.GetNames<QualificationProbeFailedAccess>(), Strings(contract.GetProperty("Accesses")));
        CollectionAssert.AreEqual(Enum.GetNames<QualificationProbeRefusalReason>(), Strings(contract.GetProperty("Reasons")));
        CollectionAssert.AreEqual(Enum.GetNames<QualificationProbeRefusalDetail>(), Strings(contract.GetProperty("Details")));
        CollectionAssert.AreEqual(new[] { QualificationProbeWireCodec.TokenRefusedDiagnostic, QualificationProbeWireCodec.CapabilityRefusedDiagnostic,
            QualificationProbeWireCodec.ObservedDiagnostic, QualificationProbeWireCodec.ObservationRefusedDiagnostic, QualificationProbeWireCodec.ObservationTimedOutDiagnostic },
            Strings(contract.GetProperty("Diagnostics")));
        CollectionAssert.AreEqual(QualificationProbeContract.Compiled.ListedClientVersions.ToArray(), Strings(contract.GetProperty("ListedReleases")));
        Assert.AreEqual(ChocolateyReleaseList.Listed.Releases[^1].Version, Strings(contract.GetProperty("ListedReleases"))[^1]);
        Assert.AreEqual(QualificationProbeWireCodec.MaximumNodesEvaluated, contract.GetProperty("MaximumNodes").GetInt32());
        Assert.AreEqual(QualificationProbeWireCodec.MaximumAncestorsEvaluated, contract.GetProperty("MaximumAncestors").GetInt32());
        Assert.AreEqual(QualificationProbeResultDocument.MaximumBytes, contract.GetProperty("MaximumBytes").GetInt32());
        // spec §8 E3: the inconclusive outcomes; every other non-accepted outcome is a FAIL.
        CollectionAssert.AreEquivalent(Inconclusive, Strings(contract.GetProperty("InconclusiveOutcomes")));
        Assert.AreEqual(DeploymentCapabilityPolicy.LedgerSchema, contract.GetProperty("LedgerSchema").GetString());
        string builder = File.ReadAllText(Path.Combine(root, "scripts", "New-OwnerOnlyCandidate.ps1"));
        StringAssert.Contains(builder, "$script:CandidateSchema = '" + contract.GetProperty("BuildRecordSchema").GetString() + "'");
        StringAssert.Contains(builder, "$script:ProbeMarkRule = '" + contract.GetProperty("ProbeMarkRule").GetString() + "'");
        StringAssert.Contains(builder, "$script:ProbeS6Commit = '" + contract.GetProperty("ProbeCommit").GetString() + "'");
    }

    private static void AssertBuildRecords(JsonElement records)
    {
        Dictionary<string, string> expected = new()
        {
            ["valid"] = "commit:" + string.Concat(Enumerable.Repeat("ab", 20)),
            ["v1-schema"] = "not-a-probe-build:record-schema",
            ["no-mark"] = "not-a-probe-build:record-lacks-probeIncluded",
            ["not-included"] = "not-a-probe-build:probe-not-included",
            ["string-true"] = "not-a-probe-build:probe-not-included",
            ["other-manifest"] = "not-a-probe-build:record-is-not-the-deployed-build",
            ["lowercase-manifest"] = "not-a-probe-build:record-is-not-the-deployed-build",
            ["dirty"] = "not-a-probe-build:record-dirty",
            ["other-rule"] = "not-a-probe-build:mark-rule",
            ["other-commit"] = "not-a-probe-build:mark-commit",
            ["not-validated"] = "not-a-probe-build:record-not-validated",
            ["detail-mismatch"] = "not-a-probe-build:probe-not-included",
            ["unreadable"] = "not-a-probe-build:record-unreadable",
        };
        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), records.EnumerateObject().Select(item => item.Name).ToArray());
        foreach ((string name, string value) in expected) Assert.AreEqual(value, records.GetProperty(name).GetString(), name);
    }

    private static void AssertLedgers(JsonElement ledgers)
    {
        Dictionary<string, string> expected = new()
        {
            ["valid"] = "pins:" + new string('A', 64) + "," + new string('B', 64) + "," + new string('C', 64),
            ["other-manifest"] = "pin-differs:manifest",
            ["other-signer"] = "pin-differs:signer",
            ["v1-origin"] = "pin-differs:payload-origin",
            ["incomplete"] = "pin-differs:ledger-state",
            ["other-schema"] = "pin-differs:ledger-state",
            ["no-helper"] = "pin-differs:ledger-PCSteward.ElevatedHelper.exe",
        };
        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), ledgers.EnumerateObject().Select(item => item.Name).ToArray());
        foreach ((string name, string value) in expected) Assert.AreEqual(value, ledgers.GetProperty(name).GetString(), name);
    }

    /// <summary>The combined digest is recomputed independently here from the specified line format.</summary>
    private static void AssertTree(JsonElement tree)
    {
        string abc = Convert.ToHexString(SHA256.HashData("abc"u8)), empty = Convert.ToHexString(SHA256.HashData([]));
        string[] lines = ["D|a", "D|c", "F|a\\x.txt|3|" + abc, "F|b.txt|0|" + empty];
        Assert.AreEqual(Digest(lines), tree.GetProperty("first").GetString());
        Assert.AreEqual(2, tree.GetProperty("files").GetInt32());
        Assert.IsTrue(tree.GetProperty("stable").GetBoolean());
        Assert.AreNotEqual(tree.GetProperty("first").GetString(), tree.GetProperty("changed").GetString(), "a changed file changes the digest");
        Assert.AreEqual(Digest([.. lines, "R|link"]), tree.GetProperty("linked").GetString(), "a reparse point is recorded and never followed");
        Assert.AreEqual(2, tree.GetProperty("linkedFiles").GetInt32());
    }

    private static string Digest(string[] lines)
    {
        string[] sorted = [.. lines];
        Array.Sort(sorted, StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', sorted))));
    }

    private static string[] Strings(JsonElement array) => array.ValueKind == JsonValueKind.Array
        ? [.. array.EnumerateArray().Select(item => item.GetString()!)]
        : [array.GetString()!];

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string ScriptPath(string root) => Path.Combine(root, "scripts", "evidence", ScriptName);

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

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string executable, string workingDirectory, TimeSpan timeout, params string[] arguments)
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
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the probe tooling fixture.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(timeout);
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
