using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Task 50 gaps G1, G2 and G4 (FR-UPD-15, TM-03/04/06/07/13, ADR-022): redeploy plan schema v3 with user-data preservation,
/// the committed non-elevated caller (NewPlan, CheckOnly, ExecuteApprovedPlan) and the read-only database copy. The fixture
/// executes the REAL template statements and script functions from their parsed AST on temp directories only; no elevation,
/// certificate store, Program Files, installed root or real user data is touched.
/// </summary>
[TestClass]
public sealed class RedeployToolingScriptTests
{
    private const string Absent = "The preserved user data root is absent.";
    private const string Differs = "The preserved user database differs from its pin.";
    private const string Sidecar = "A SQLite sidecar exists beside the preserved user database.";
    private const string Reparse = "Reparse path refused for user data or a pinned file.";
    private const string Malformed = "Malformed pinned user database entry.";
    private const string SystemPowerShell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    private static readonly string[] WindowsOnlyRows = ["realProgramNonElevated", "resultAcl", "reparseKind"];

    [TestMethod]
    [DataRow("pwsh")]
    [DataRow("windows-powershell")]
    public async Task RedeployToolingFunctionsHoldTheirContracts(string shell)
    {
        string? executable = ResolveShell(shell);
        if (executable is null)
        {
            if (shell == "windows-powershell") Assert.Inconclusive("Windows PowerShell 5.1 runs only on Windows; this row (including the one-element pins round trip under 5.1) is Windows-CI evidence.");
            Assert.Inconclusive("PowerShell 7 (pwsh) is not on PATH; the redeploy tooling fixture needs it on this platform.");
        }
        string root = FindRepositoryRoot();
        DirectoryInfo work = Directory.CreateTempSubdirectory("pc-steward-redeploy-fixture-");
        try
        {
            (int exitCode, string output, string error) = await RunAsync(executable!, root,
                "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "tests", "PCSteward.Security.FixtureTests", "Fixtures", "RedeployTooling.ps1"),
                "-RepositoryRoot", root, "-WorkRoot", work.FullName);
            Assert.AreEqual(0, exitCode, $"Fixture failed:\n{output}\n{error}");
            StringAssert.Contains(output, "PASS: redeploy tooling fixtures");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(work.FullName, "results.json")));
            JsonElement r = document.RootElement;
            bool windows = r.GetProperty("windows").GetBoolean();
            Assert.AreEqual(OperatingSystem.IsWindows(), windows);

            foreach (JsonProperty parsed in r.GetProperty("parseErrors").EnumerateObject()) Assert.AreEqual(0, parsed.Value.GetInt32(), parsed.Name);

            // G1: plan schema v3 refusals.
            JsonElement plan = r.GetProperty("planV3");
            Assert.AreEqual(JsonValueKind.Null, plan.GetProperty("valid").ValueKind, plan.GetProperty("valid").ToString());
            foreach (string name in new[] { "missingPreserveUserData", "preserveUserDataFalse", "preserveUserDataString" })
                Assert.AreEqual("Plan schema v3 requires preserveUserData true.", Text(plan, name), name);
            Assert.AreEqual("Plan schema v3 requires exactly the reviewed key set.", Text(plan, "extraKey"));
            Assert.AreEqual("Plan schema v3 must pin pcsteward.db.", Text(plan, "missingDatabasePin"));
            foreach (string name in new[] { "sidecarPinned", "traversingPin", "lowercaseDatabaseHash", "zeroBytes" }) Assert.AreEqual(Malformed, Text(plan, name), name);
            Assert.AreEqual("Plan schema v3 requires one to eight pinned user database files.", Text(plan, "filesNotArray"));
            Assert.AreEqual("Plan schema v3 requires exactly six reviewed script pins.", Text(plan, "fiveScripts"));
            Assert.AreEqual("Plan schema v3 must name an allowlisted expected old and new payload origin.", Text(plan, "badOrigin"));
            Assert.AreEqual("Plan schema v3 requires a user installer SID.", Text(plan, "computerSid"));

            // G1: the real template plan section; v1/v2 behaviour and error texts are unchanged.
            JsonElement top = r.GetProperty("templatePlan");
            AssertPlan(top.GetProperty("v1"), "legacy-v6", "pinned-unsigned-v1", false);
            Assert.AreEqual("Plan schema v1 cannot name payload origins.", Text(top.GetProperty("v1NamedOrigin"), "failure"));
            AssertPlan(top.GetProperty("v2"), "pinned-unsigned-v1", "pinned-unsigned-v2", false);
            Assert.AreEqual("Plan schema v2 must name an allowlisted expected old and new payload origin.", Text(top.GetProperty("v2BadOrigin"), "failure"));
            AssertPlan(top.GetProperty("v2PreserveIgnored"), "pinned-unsigned-v1", "pinned-unsigned-v2", false);
            Assert.AreEqual("An exact embedded owner-approved redeployment plan is required.", Text(top.GetProperty("unknownSchema"), "failure"));
            AssertPlan(top.GetProperty("v3"), "pinned-unsigned-v1", "pinned-unsigned-v2", true);
            Assert.AreEqual(1, top.GetProperty("v3").GetProperty("pins").GetInt32());
            Assert.AreEqual("Plan schema v3 requires preserveUserData true.", Text(top.GetProperty("v3MissingPreserve"), "failure"));
            Assert.AreEqual(Malformed, Text(top.GetProperty("v3MalformedDatabasePin"), "failure"));

            // G1: preflight, removal and final postconditions from the real template statements.
            JsonElement guards = r.GetProperty("guards");
            string preserved = guards.GetProperty("expectedPreservedSha256").GetString()!;
            (string Case, string? Failure)[] expected =
            [
                ("preflightV2DataAbsent", null), ("preflightV2DataPresent", "Approved old deployment/data/process baseline changed."),
                ("preflightV3Pinned", null), ("preflightV3HashMismatch", Differs), ("preflightV3Wal", Sidecar), ("preflightV3Shm", Sidecar),
                ("preflightV3Journal", Sidecar), ("preflightV3Process", "Approved old deployment/data/process baseline changed."),
                ("preflightV3DataAbsent", Absent), ("preflightV3OtherRoot", "The approved user data root differs."), ("preflightV3ReparseRoot", Reparse),
                ("removalV2DataPresent", "Removal postconditions failed."), ("removalV2DataAbsent", null), ("removalV3Unchanged", null),
                ("removalV3Changed", Differs), ("removalV3Sidecar", Sidecar), ("removalV3DataAbsent", Absent), ("removalV3InstallPresent", "Removal postconditions failed."),
                ("finalV2DataPresent", "New exact identity/preservation postconditions failed."), ("finalV3Unchanged", null), ("finalV3Shm", Sidecar),
                ("finalV3Process", "New exact identity/preservation postconditions failed."), ("finalV3Changed", Differs),
            ];
            foreach ((string name, string? failure) in expected)
            {
                JsonElement outcome = guards.GetProperty(name);
                if (failure is null) Assert.AreEqual(JsonValueKind.Null, outcome.GetProperty("failure").ValueKind, name + ": " + outcome);
                else Assert.AreEqual(failure, Text(outcome, "failure"), name);
            }
            foreach (string name in new[] { "preflightV3Pinned", "removalV3Unchanged", "finalV3Unchanged" }) Assert.AreEqual(preserved, Text(guards.GetProperty(name), "preservedSha256"), name);

            // G1: result record; v1/v2 keys and order unchanged.
            JsonElement record = r.GetProperty("resultRecord");
            Assert.AreEqual("exitCode,phase,payloadOrigin,candidateManifestSha256,certificateThumbprint,launched,applicationUpdates,userDataRootAbsent,executionRoot", Text(record, "v2Keys"));
            Assert.AreEqual("exitCode,phase,payloadOrigin,candidateManifestSha256,certificateThumbprint,launched,applicationUpdates,userDataPreserved,userDatabaseBytes,userDatabaseSha256,executionRoot", Text(record, "v3Keys"));
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData("db"u8)), Text(record, "v3Sha256"));

            // G1: the guarded remover deletes only the install and state roots and reports preserved user data.
            JsonElement remover = r.GetProperty("remover");
            Assert.AreEqual("$installRoot,$stateRoot", Text(remover, "removeTargets"));
            Assert.IsTrue(remover.GetProperty("reportsUserDataPreserved").GetBoolean());
            Assert.AreEqual(0, remover.GetProperty("mentionsUserData").GetInt32());

            // G2: caller structure. CheckOnly and NewPlan never reach a process start; execution starts exactly one RunAs.
            JsonElement caller = r.GetProperty("caller");
            Assert.AreEqual(1, caller.GetProperty("startProcessCount").GetInt32());
            Assert.AreEqual("Start-OwnerOnlyElevatedRedeploy", Text(caller, "startProcessFunction"));
            Assert.IsFalse(caller.GetProperty("startProcessInLoop").GetBoolean());
            Assert.IsTrue(caller.GetProperty("filePathIsLiteral").GetBoolean());
            Assert.AreEqual(SystemPowerShell, Text(caller, "filePath"));
            Assert.AreEqual(SystemPowerShell, Text(caller, "systemLiteral"));
            Assert.AreEqual("RunAs", Text(caller, "verb"));
            Assert.IsFalse(caller.GetProperty("hasWait").GetBoolean(), "the Task 48 review requires the native waiter, not -Wait");
            foreach (string argument in new[] { "'-NoProfile'", "'-NonInteractive'", "'-ExecutionPolicy','Bypass'", "'-EncodedCommand',$EncodedCommand" }) StringAssert.Contains(Text(caller, "argumentList"), argument);
            Assert.AreEqual(1, caller.GetProperty("startCallCount").GetInt32());
            Assert.AreEqual("Invoke-OwnerOnlyRedeployExecution", Text(caller, "startCallFunction"));
            Assert.IsFalse(caller.GetProperty("startCallInLoop").GetBoolean(), "no retry");
            Assert.IsFalse(caller.GetProperty("checkPathReachesStart").GetBoolean(), "CheckOnly/NewPlan must never start or elevate a process");
            Assert.IsTrue(caller.GetProperty("executePathReachesStart").GetBoolean());
            Assert.IsTrue(caller.GetProperty("executionGuardedByMode").GetBoolean());
            Assert.IsFalse(caller.GetProperty("mainCallsStartDirectly").GetBoolean());
            Assert.AreEqual(0, caller.GetProperty("forbiddenCommands").GetInt32());
            Assert.AreEqual("Get-OwnerOnlyPinnedScriptBlock", Text(caller, "scriptBlockCreateFunctions"));
            Assert.AreEqual(0, caller.GetProperty("helperFileStartProcess").GetInt32());

            // G2: canonical plan JSON, including a ONE-element database pin array (the Task 49 5.1 serialization defect).
            JsonElement canonical = r.GetProperty("canonical");
            foreach (string name in new[] { "roundTrip", "scriptsIsArray", "oneDatabasePinIsArray", "readSha256Matches", "unicodeEscaped" }) Assert.IsTrue(canonical.GetProperty(name).GetBoolean(), name);
            Assert.IsFalse(canonical.GetProperty("wrappedArray").GetBoolean());
            Assert.AreEqual(6, canonical.GetProperty("reparsedScriptCount").GetInt32());
            Assert.AreEqual(1, canonical.GetProperty("reparsedDatabaseCount").GetInt32());
            Assert.AreEqual(1, canonical.GetProperty("readPins").GetInt32());
            Assert.AreEqual("The plan must be one line of printable ASCII JSON.", Text(canonical, "multiLine"));
            Assert.AreEqual("The plan must be one line of printable ASCII JSON.", Text(canonical, "nonAscii"));
            Assert.AreEqual("The plan is not in canonical form; regenerate it and approve the regenerated plan.", Text(canonical, "nonCanonical"));

            // G2: generated program and encoded command.
            JsonElement program = r.GetProperty("program");
            int length = program.GetProperty("encodedLength").GetInt32();
            Assert.IsTrue(length > 0 && length <= 30000, $"encoded command length {length}");
            foreach (string name in new[] { "lossless", "stubPinsProgram", "encodedSha256", "embeddedPlanExact", "deterministic" }) Assert.IsTrue(program.GetProperty(name).GetBoolean(), name);
            Assert.AreEqual(0, program.GetProperty("programParseErrors").GetInt32());
            Assert.AreEqual(0, program.GetProperty("stubParseErrors").GetInt32());
            Assert.AreEqual("Unexpected bootstrap template or helper text.", Text(program, "helperInsidePlanRefused"));
            Assert.AreEqual("The embedded plan must be printable ASCII.", Text(program, "nonAsciiPlanRefused"));
            Assert.AreEqual(7, program.GetProperty("stubRunsVerifiedProgram").GetProperty("exitCode").GetInt32());
            Assert.AreEqual(20, program.GetProperty("stubRefusesChangedExpansion").GetProperty("exitCode").GetInt32());
            JsonElement notRun = r.GetProperty("notRun");
            if (windows)
            {
                JsonElement real = program.GetProperty("realProgramNonElevated");
                Assert.AreEqual(1, real.GetProperty("exitCode").GetInt32(), Text(real, "output"));
                StringAssert.Contains(Text(real, "output"), "Exact installing SID and elevation required.");
            }
            else Assert.IsFalse(string.IsNullOrWhiteSpace(Text(notRun, "realProgramNonElevated")));

            // G2: one-use attempt record and classification; CLR override names; pinned helpers are function-only.
            JsonElement attempt = r.GetProperty("attempt");
            StringAssert.Matches(Text(attempt, "pathLeaf"), new System.Text.RegularExpressions.Regex("^redeploy-attempt-[A-F0-9]{16}\\.json$"));
            Assert.IsTrue(attempt.GetProperty("pathBesidePlan").GetBoolean());
            Assert.AreEqual("An attempt record for this plan already exists; the approval is used. Never delete or reuse it to run again.", Text(attempt, "second"));
            Assert.AreEqual("Validated", Text(attempt, "finalStatus"));
            Assert.AreEqual("Validated", Text(attempt, "classifyValidated"));
            Assert.AreEqual("PendingOrUnknown", Text(attempt, "classifyTimeout"));
            foreach (string name in new[] { "classifyNativeFailure", "classifyMissingResult", "classifyOtherDatabase" }) Assert.AreEqual("StoppedOrUnknown", Text(attempt, name), name);
            Assert.AreEqual("ValidatedPostcheckFailed", Text(attempt, "classifyPostcheckFailed"));
            Assert.AreEqual("COMPlus_EnableDiagnostics,COR_ENABLE_PROFILING,COR_PROFILER_PATH_64,APPDOMAIN_MANAGER_ASM", Text(attempt, "clrOverrides"));
            Assert.IsTrue(attempt.GetProperty("helpersFunctionOnly").GetBoolean());
            Assert.AreEqual("A pinned helper must contain only function definitions.", Text(attempt, "statementHelperRefused"));
            Assert.IsTrue(attempt.GetProperty("sqliteNotLoaded").GetBoolean());

            // G2: candidate tree and old ledger pins.
            JsonElement candidate = r.GetProperty("candidate");
            Assert.AreEqual(3, candidate.GetProperty("valid").GetInt32());
            Assert.AreEqual("The pinned candidate payload origin differs from the plan.", Text(candidate, "originMismatch"));
            Assert.AreEqual("The candidate payload has an unpinned file.", Text(candidate, "unpinnedFile"));
            Assert.AreEqual("Unsafe candidate manifest path.", Text(candidate, "traversal"));
            Assert.AreEqual("The candidate payload differs from its pinned manifest.", Text(candidate, "changed"));
            Assert.AreEqual("The candidate payload is missing a pinned file.", Text(candidate, "missing"));
            Assert.AreEqual("Reparse path refused in the candidate payload.", Text(candidate, "reparse"));
            JsonElement ledger = r.GetProperty("ledger");
            Assert.AreEqual(JsonValueKind.Null, ledger.GetProperty("valid").ValueKind, ledger.GetProperty("valid").ToString());
            Assert.AreEqual("Approved old ledger changed.", Text(ledger, "hashChanged"));
            Assert.AreEqual("Old ledger identity changed.", Text(ledger, "otherSigner"));
            Assert.AreEqual("The old deployment origin differs from the plan.", Text(ledger, "otherOrigin"));

            // G4: database preservation copy.
            JsonElement copy = r.GetProperty("copy");
            foreach (string name in new[] { "hashesEqual", "sourceUnchanged", "recordWritten", "headerValid", "recordHasNoProfilePath", "noFolderOnRefusal" }) Assert.IsTrue(copy.GetProperty(name).GetBoolean(), name);
            Assert.AreEqual("not-run-by-design", Text(copy, "integrityCheck"));
            Assert.AreEqual("The copy destination must be outside Program Files, Windows and the PC Steward data root.", Text(copy, "destinationInsideData"));
            Assert.AreEqual("The copy destination must be outside Program Files, Windows and the PC Steward data root.", Text(copy, "destinationInsideForbidden"));
            StringAssert.StartsWith(Text(copy, "sidecar"), "A SQLite sidecar exists;");
            Assert.AreEqual("A PC Steward process is running; user data cannot be preserved.", Text(copy, "process"));
            Assert.AreEqual(Reparse, Text(copy, "reparse"));
            Assert.AreEqual(0, copy.GetProperty("forbiddenCommands").GetInt32());
            Assert.IsFalse(copy.GetProperty("mentionsEngine").GetBoolean());

            // G2: Windows-only result ACL rows (the Task 49 three-versus-four entry defect).
            if (windows)
            {
                JsonElement acl = r.GetProperty("resultAcl");
                Assert.IsTrue(acl.GetProperty("writerIsLeafExecutionSecurity").GetBoolean());
                Assert.AreEqual(JsonValueKind.Null, acl.GetProperty("bootstrapWriter").ValueKind, acl.GetProperty("bootstrapWriter").ToString());
                Assert.AreEqual("Execution infrastructure ACL is incomplete.", Text(acl, "threeEntryWriter"));
                Assert.AreEqual("The bootstrap result must be a file.", Text(acl, "directory"));
                Assert.AreEqual("Execution infrastructure ACL has unexpected authority.", Text(acl, "extraEntry"));
                Assert.AreEqual("Execution infrastructure ACL has unexpected authority.", Text(acl, "usersWrite"));
                Assert.AreEqual("Execution infrastructure must be protected and Administrators-owned.", Text(acl, "unprotected"));
                Assert.AreEqual("Execution infrastructure must be protected and Administrators-owned.", Text(acl, "wrongOwner"));
            }
            else
            {
                string reasons = string.Join("; ", WindowsOnlyRows.Select(name => name + ": " + Text(notRun, name)));
                Assert.Inconclusive("Portable redeploy-tooling rows passed under " + Text(r, "shell") + ". Windows-only rows not run here (Windows-CI evidence): " + reasons);
            }
        }
        finally { DeleteFixtureTree(work); }
    }

    /// <summary>
    /// Best-effort removal of the per-test temp tree. Windows CI refused the recursive delete at the fixture's junction
    /// ('linked') after every assertion had passed, so links are removed as links first (never followed) and a residual
    /// temp folder is not a test failure, as in <see cref="N1NativeFixture"/>.
    /// </summary>
    private static void DeleteFixtureTree(DirectoryInfo root)
    {
        try
        {
            foreach (FileSystemInfo entry in root.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) continue;
                try
                {
                    if (entry is DirectoryInfo link) Directory.Delete(link.FullName, recursive: false);
                    else entry.Delete();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            root.Refresh();
            if (root.Exists) root.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    [TestMethod]
    public void TemplateConfinesV3ToPreserveUserDataAndKeepsV1V2DataAbsence()
    {
        string template = ReadScript("Invoke-OwnerOnlyApprovedRedeployment.ps1");
        // Every data-root test in the template is the v1/v2 absence check, guarded by !$preserveUserData.
        Assert.AreEqual(3, Count(template, "Test-Path -LiteralPath $data"));
        Assert.AreEqual(3, Count(template, "(!$preserveUserData -and (Test-Path -LiteralPath $data))"));
        Assert.AreEqual(3, Count(template, "if($preserveUserData){$userData=@(Assert-OwnerOnlyUserDataPreserved -DataRoot $data -Files $userDataPins)}")
            + Count(template, ";$userData=@(Assert-OwnerOnlyUserDataPreserved -DataRoot $data -Files $userDataPins)}"));
        Assert.AreEqual(1, Count(template, "userDataRootAbsent=$true"));
        Assert.AreEqual(1, Count(template, "userDataPreserved=$true"));
        StringAssert.Contains(template, "$preserveUserData=$OwnerOnlyPlan.schema -ceq 'pc-steward-exact-redeploy-v3'");
        StringAssert.Contains(template, "@('pc-steward-exact-redeploy-v1','pc-steward-exact-redeploy-v2','pc-steward-exact-redeploy-v3')");
        // The remover the template calls never names the per-user data root (it reports userDataPreserved = $true).
        string remover = ReadScript("Remove-OwnerOnlyProtectedTestDeployment.ps1");
        StringAssert.Contains(remover, "userDataPreserved = $true");
        Assert.IsFalse(remover.Contains("LocalApplicationData", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ToolingScriptsArePrintableAsciiAndNeverLoadSqlite()
    {
        foreach (string name in new[] { "Invoke-OwnerOnlyApprovedRedeployment.ps1", "OwnerOnlyExecution.ps1", "Invoke-OwnerOnlyRedeployCaller.ps1", "Copy-OwnerOnlyUserDatabase.ps1" })
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "scripts", name));
            Assert.IsFalse(bytes.Any(value => value > 0x7E || (value < 0x20 && value is not (byte)'\n' and not (byte)'\r' and not (byte)'\t')), name + " must be ASCII so Windows PowerShell 5.1 reads it unchanged");
            string text = Encoding.ASCII.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
            foreach (string forbidden in new[] { "DllImport", "Microsoft.Data.Sqlite", "SQLitePCL", "Invoke-Expression", "LoadLibrary" })
                Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal), $"{name} contains {forbidden}");
        }
        string caller = ReadScript("Invoke-OwnerOnlyRedeployCaller.ps1");
        Assert.AreEqual(1, Count(caller, "Start-Process "));
        Assert.IsFalse(caller.Contains("Add-Type", StringComparison.Ordinal));
        StringAssert.Contains(caller, "[CmdletBinding(DefaultParameterSetName='CheckOnly')]");
    }

    [TestMethod]
    public void WindowsCiRunsTheseFixtures()
    {
        string root = FindRepositoryRoot();
        string ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        StringAssert.Contains(ci, "runs-on: windows-latest");
        StringAssert.Contains(ci, "./scripts/Test-CI.ps1");
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, "scripts", "Test.ps1")), "dotnet test PCSteward.slnx");
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, "PCSteward.slnx")), "tests/PCSteward.Security.FixtureTests/PCSteward.Security.FixtureTests.csproj");
    }

    private static void AssertPlan(JsonElement outcome, string oldOrigin, string newOrigin, bool preserve)
    {
        Assert.AreEqual(JsonValueKind.Null, outcome.GetProperty("failure").ValueKind, outcome.ToString());
        Assert.AreEqual(oldOrigin, Text(outcome, "old"));
        Assert.AreEqual(newOrigin, Text(outcome, "new"));
        Assert.AreEqual(preserve, outcome.GetProperty("preserve").GetBoolean());
    }

    private static string Text(JsonElement element, string name)
    {
        Assert.IsTrue(element.TryGetProperty(name, out JsonElement value), $"missing {name} in {element}");
        Assert.AreEqual(JsonValueKind.String, value.ValueKind, $"{name}: {value}");
        return value.GetString()!;
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string ReadScript(string name) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", name)).Replace("\r\n", "\n", StringComparison.Ordinal);

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
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the redeploy tooling fixture.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(240));
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
