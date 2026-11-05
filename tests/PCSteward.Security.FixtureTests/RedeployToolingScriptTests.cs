using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Task 50 gaps G1, G2 and G4 (FR-UPD-15, TM-03/04/06/07/13, ADR-022): redeploy plan schema v3 with user-data preservation,
/// the committed non-elevated caller (NewPlan, CheckOnly, ExecuteApprovedPlan) and the read-only database copy. The fixture
/// executes the REAL template statements and script functions from their parsed AST on temp directories only; no elevation,
/// certificate store change, Program Files write, installed root or real user data is touched.
/// Task 50 R2 (owner decision D-R2; FR-UPD-15, TM-03/04/07/13, ADR-022) adds the fresh-install plan schema (no old deployment,
/// no removal), the packaged-process refusal (GetCurrentPackageFullName must return 15700) and the installer's signing
/// diagnostics; the fresh-install baseline rows open the CurrentUser My, Root and TrustedPublisher stores read-only.
/// </summary>
[TestClass]
public sealed class RedeployToolingScriptTests
{
    private const string Absent = "The preserved user data root is absent.";
    private const string Differs = "The preserved user database differs from its pin.";
    private const string Sidecar = "A SQLite sidecar exists beside the preserved user database.";
    private const string Reparse = "Reparse path refused for user data or a pinned file.";
    private const string Malformed = "Malformed pinned user database entry.";
    private const string TaskEnabled = "The PC Steward scheduled check task is enabled or unreadable; disable it before the redeploy.";
    private const string NotReviewedHelper = "OwnerOnlyExecution.ps1 is not the reviewed version embedded in this caller; regenerate the plan from the reviewed scripts.";
    private static readonly string[] PinnedFiles = ["OwnerOnlyExecution.ps1", "Wait-OwnerOnlyDeploymentProcess.ps1", "Invoke-OwnerOnlyApprovedRedeployment.ps1"];
    private const string SystemPowerShell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    private static readonly string[] WindowsOnlyRows = ["realProgramNonElevated", "resultAcl", "reparseKind", "packagedRealQuery", "freshEndToEnd"];
    private const string KeySet = "Plan schema v3 requires exactly the reviewed key set.";
    private const string V3Origin = "Plan schema v3 must name an allowlisted expected old and new payload origin.";
    private const string FreshRoots = "A fresh install requires absent PC Steward install and state roots.";
    private const string ProcessRunning = "A PC Steward process is running; user data cannot be preserved.";
    private const string FreshSchema = "pc-steward-exact-fresh-install-v1";
    private const string Untrusted = "A certificate chain processed, but terminated in a root certificate which is not trusted by the trust provider.";
    private static string PackagedRefusal(int code) => $"This process has a package identity (GetCurrentPackageFullName returned {code}, not 15700); run from a Windows PowerShell console started directly, not from a packaged app.";

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

            // Task 50 R2 (D-R2): fresh-install plan schema refusals. v3 itself still refuses old origin "none" and missing old pins.
            JsonElement freshPlan = r.GetProperty("planFresh");
            Assert.AreEqual(JsonValueKind.Null, freshPlan.GetProperty("valid").ValueKind, freshPlan.GetProperty("valid").ToString());
            foreach (string name in new[] { "withOldLedgerPin", "withOldThumbprint", "redeployWithoutOldPins" }) Assert.AreEqual(KeySet, Text(freshPlan, name), name);
            foreach (string name in new[] { "oldOriginNotNone", "redeployOldOriginNone" }) Assert.AreEqual(V3Origin, Text(freshPlan, name), name);
            Assert.AreEqual("Plan schema v3 requires preserveUserData true.", Text(freshPlan, "preserveUserDataFalse"));
            Assert.AreEqual("Plan schema v3 must pin pcsteward.db.", Text(freshPlan, "missingDatabasePin"));
            Assert.AreEqual("Plan schema v3 is required.", Text(freshPlan, "otherSchema"));

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
            AssertPlan(top.GetProperty("fresh"), "none", "pinned-unsigned-v2", true);
            Assert.AreEqual(1, top.GetProperty("fresh").GetProperty("pins").GetInt32());
            Assert.AreEqual("Plan schema v2 must name an allowlisted expected old and new payload origin.", Text(top.GetProperty("v2OldOriginNone"), "failure"));
            Assert.AreEqual(V3Origin, Text(top.GetProperty("v3OldOriginNone"), "failure"));
            Assert.AreEqual(KeySet, Text(top.GetProperty("freshWithOldPins"), "failure"));
            Assert.AreEqual("Plan schema v3 requires preserveUserData true.", Text(top.GetProperty("v1Fresh"), "failure"));

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

            // Review M1: an enabled PCSteward.ScheduledCheck task stops a v3 run at preflight and again immediately before
            // removal, together with a process re-check; v1/v2 behaviour is unchanged.
            (string Case, string? Failure)[] scheduled =
            [
                ("preflightV3TaskEnabled", TaskEnabled), ("preflightV2TaskEnabled", null), ("preRemovalV3Clean", null),
                ("preRemovalV3Process", "A PC Steward process is running; user data cannot be preserved."), ("preRemovalV3TaskEnabled", TaskEnabled), ("preRemovalV2Process", null),
            ];
            foreach ((string name, string? failure) in scheduled)
            {
                JsonElement outcome = guards.GetProperty(name);
                if (failure is null) Assert.AreEqual(JsonValueKind.Null, outcome.GetProperty("failure").ValueKind, name + ": " + outcome);
                else Assert.AreEqual(failure, Text(outcome, "failure"), name);
            }
            Assert.AreEqual("if($preserveUserData){Assert-OwnerOnlyScheduledCheckInactive;Assert-OwnerOnlyNoPCStewardProcess}", Text(guards, "preRemovalText"));
            Assert.IsTrue(guards.GetProperty("preRemovalFollowsPhase").GetBoolean());

            // Task 50 R2: fresh-install preflight, pre-install re-check and final postconditions from the real template statements.
            // The old ledger path does not exist in these rows, so a passing row proves the template never hashes or reads it.
            (string Case, string? Failure)[] fresh =
            [
                ("preflightFreshClean", null), ("preflightFreshInstallPresent", FreshRoots), ("preflightFreshStatePresent", FreshRoots),
                ("preflightFreshProcess", ProcessRunning), ("preflightFreshHashMismatch", Differs), ("preflightFreshSidecar", Sidecar),
                ("preflightFreshDataAbsent", Absent), ("preflightFreshTaskEnabled", TaskEnabled), ("preflightFreshReparseRoot", Reparse),
                ("recheckFreshClean", null), ("recheckFreshInstallPresent", FreshRoots), ("recheckFreshStatePresent", FreshRoots),
                ("recheckFreshTaskEnabled", TaskEnabled), ("recheckFreshProcess", ProcessRunning),
                ("finalFreshUnchanged", null), ("finalFreshChanged", Differs), ("preflightV3IgnoresFreshBaseline", null),
            ];
            foreach ((string name, string? failure) in fresh)
            {
                JsonElement outcome = guards.GetProperty(name);
                if (failure is null) Assert.AreEqual(JsonValueKind.Null, outcome.GetProperty("failure").ValueKind, name + ": " + outcome);
                else Assert.AreEqual(failure, Text(outcome, "failure"), name);
            }
            foreach (string name in new[] { "preflightFreshClean", "finalFreshUnchanged" }) Assert.AreEqual(preserved, Text(guards.GetProperty(name), "preservedSha256"), name);
            JsonElement freshShape = guards.GetProperty("fresh");
            Assert.AreEqual("Assert-OwnerOnlyScheduledCheckInactive,Assert-OwnerOnlyFreshInstallBaseline", Text(freshShape, "branchCommands"), "the fresh branch re-checks and never removes");
            Assert.IsFalse(freshShape.GetProperty("branchMentionsRemover").GetBoolean());
            Assert.AreEqual(1, freshShape.GetProperty("removerInvocations").GetInt32(), "the remover is invoked once, in the redeploy branch only");
            Assert.AreEqual(2, freshShape.GetProperty("ledgerReferences").GetInt32());
            Assert.AreEqual(1, freshShape.GetProperty("oldOnlyBlocks").GetInt32());
            foreach (string name in new[] { "removerOnlyInElse", "installAfterSwitch", "validateAfterInstall", "oldOnlyReadsLedger", "ledgerHashGuardedByFresh", "packagedAfterPrincipal", "packagedBeforeTry", "createDirectoryOnlyInTry" })
                Assert.IsTrue(freshShape.GetProperty(name).GetBoolean(), name);
            JsonElement task = r.GetProperty("scheduledCheck");
            foreach (string name in new[] { "absent", "disabled" }) Assert.AreEqual(JsonValueKind.Null, task.GetProperty(name).ValueKind, name + ": " + task.GetProperty(name));
            foreach (string name in new[] { "ready", "running", "oneOfTwoEnabled", "queryDenied" }) Assert.AreEqual(TaskEnabled, Text(task, name), name);
            Assert.IsTrue(task.GetProperty("importsFromPSHome").GetBoolean(), "ScheduledTasks must load from the $PSHOME module path");

            // G1: result record; v1/v2 keys and order unchanged.
            JsonElement record = r.GetProperty("resultRecord");
            Assert.AreEqual("exitCode,phase,payloadOrigin,candidateManifestSha256,certificateThumbprint,launched,applicationUpdates,userDataRootAbsent,executionRoot", Text(record, "v2Keys"));
            Assert.AreEqual("exitCode,phase,payloadOrigin,candidateManifestSha256,certificateThumbprint,launched,applicationUpdates,userDataPreserved,userDatabaseBytes,userDatabaseSha256,executionRoot", Text(record, "v3Keys"));
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData("db"u8)), Text(record, "v3Sha256"));

            // G1: the guarded remover deletes only the install and state roots and reports preserved user data.
            JsonElement remover = r.GetProperty("remover");
            Assert.AreEqual("$installRoot,$stateRoot", Text(remover, "removeTargets"));
            Assert.AreEqual("'PCSteward*'", Text(remover, "processNames"), "review M1: the remover refuses any PCSteward* process, including ScheduledHost");
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
            JsonElement canonicalFresh = r.GetProperty("canonicalFresh");
            foreach (string name in new[] { "roundTrip", "oneDatabasePinIsArray" }) Assert.IsTrue(canonicalFresh.GetProperty(name).GetBoolean(), name);
            Assert.IsFalse(canonicalFresh.GetProperty("mentionsOldPins").GetBoolean());
            Assert.AreEqual("schema,expectedOldOrigin,expectedNewOrigin,preserveUserData,userDataRoot,userDataFiles,installerSid,executionRoot,scriptSource,scripts,candidateRoot,candidateManifestPath,candidateManifestSha256", Text(canonicalFresh, "keys"));
            Assert.AreEqual("schema,expectedOldOrigin,expectedNewOrigin,preserveUserData,userDataRoot,userDataFiles,installerSid,executionRoot,scriptSource,scripts,oldLedgerSha256,oldThumbprint,candidateRoot,candidateManifestPath,candidateManifestSha256", Text(canonicalFresh, "v3Keys"), "the v3 canonical key order is unchanged");
            Assert.AreEqual(FreshSchema, Text(canonicalFresh, "readSchema"));
            Assert.AreEqual(1, canonicalFresh.GetProperty("readPins").GetInt32());

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
            // Task 50 R2: the fresh-install program stays within the same fixed bound.
            int freshLength = program.GetProperty("freshEncodedLength").GetInt32();
            Assert.IsTrue(freshLength > 0 && freshLength <= 30000, $"fresh-install encoded command length {freshLength}");
            Assert.AreEqual(0, program.GetProperty("freshProgramParseErrors").GetInt32());
            Assert.IsTrue(program.GetProperty("freshDeterministic").GetBoolean());
            JsonElement notRun = r.GetProperty("notRun");
            if (windows)
            {
                foreach (string row in new[] { "realProgramNonElevated", "realFreshProgramNonElevated" })
                {
                    JsonElement real = program.GetProperty(row);
                    Assert.AreEqual(1, real.GetProperty("exitCode").GetInt32(), row + ": " + Text(real, "output"));
                    StringAssert.Contains(Text(real, "output"), "Exact installing SID and elevation required.", row);
                }
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

            // Review L1/L4: the real execution function with the start, waiter, result reader and post-check stubbed.
            JsonElement execute = r.GetProperty("execute");
            JsonElement cancel = execute.GetProperty("cancel");
            AssertExecution(cancel, "NotStarted", startCalls: 1, waitCalls: 0);
            Assert.AreEqual("Claimed", Text(cancel, "statusAtStart"), "the one-use claim is on disk before the single UAC request");
            Assert.AreEqual("The operation was canceled by the user.", Text(cancel, "attemptFailure"));
            Assert.AreEqual(JsonValueKind.Null, cancel.GetProperty("processId").ValueKind);
            JsonElement timeout = execute.GetProperty("timeout");
            AssertExecution(timeout, "PendingOrUnknown", startCalls: 1, waitCalls: 1);
            Assert.AreEqual("Started", Text(timeout, "statusAtWait"));
            Assert.AreEqual(900000, timeout.GetProperty("waitTimeout").GetInt32());
            Assert.AreEqual(4242, timeout.GetProperty("processId").GetInt32());
            Assert.AreEqual(JsonValueKind.Null, timeout.GetProperty("nativeExitCode").ValueKind);
            JsonElement unwritten = execute.GetProperty("startedWriteFails");
            AssertExecution(unwritten, "StoppedOrUnknown", startCalls: 1, waitCalls: 1);
            Assert.AreEqual("FIXTURE: the Started record write failed.", Text(unwritten, "recordFailure"), "a failed Started write is recorded and the child is still observed");
            Assert.AreEqual(0, unwritten.GetProperty("nativeExitCode").GetInt32());
            AssertExecution(execute.GetProperty("validated"), "Validated", startCalls: 1, waitCalls: 1);
            JsonElement mismatch = execute.GetProperty("approvalMismatch");
            Assert.AreEqual("The plan or encoded command differs from the approved pins.", Text(mismatch, "failure"));
            Assert.IsFalse(mismatch.GetProperty("recordExists").GetBoolean());
            Assert.AreEqual(0, mismatch.GetProperty("startCalls").GetInt32());
            // Task 50 R2: a packaged caller is refused before the one-use claim and before any elevation request.
            JsonElement packagedExecute = execute.GetProperty("packaged");
            Assert.AreEqual(PackagedRefusal(122), Text(packagedExecute, "failure"));
            Assert.IsFalse(packagedExecute.GetProperty("recordExists").GetBoolean(), "the approval is not consumed");
            Assert.AreEqual(0, packagedExecute.GetProperty("startCalls").GetInt32());
            JsonElement packaged = r.GetProperty("packaged");
            Assert.AreEqual(JsonValueKind.Null, packaged.GetProperty("unpackaged").ValueKind, packaged.ToString());
            Assert.AreEqual(PackagedRefusal(122), Text(packaged, "packaged"));
            Assert.AreEqual(PackagedRefusal(0), Text(packaged, "zero"), "only exactly 15700 is accepted");
            if (windows)
            {
                Assert.AreEqual(JsonValueKind.Number, packaged.GetProperty("realQuery").ValueKind, packaged.GetProperty("realQuery").ToString());
                Assert.AreEqual(15700, packaged.GetProperty("realQuery").GetInt32(), "a CI runner process has no package identity");
                Assert.AreEqual(JsonValueKind.Null, packaged.GetProperty("realAssert").ValueKind, packaged.GetProperty("realAssert").ToString());

                // Task 50 R2: fresh-install NewPlan and CheckOnly end to end (non-elevated, temp roots, stubbed baseline).
                JsonElement e2e = r.GetProperty("freshEndToEnd");
                Assert.AreEqual(JsonValueKind.Null, e2e.GetProperty("newPlanFailure").ValueKind, e2e.ToString());
                Assert.AreEqual("Written", Text(e2e, "newPlanResult"));
                Assert.AreEqual(FreshSchema, Text(e2e, "newPlanSchema"));
                Assert.AreEqual("none", Text(e2e, "planOldOrigin"));
                Assert.IsFalse(e2e.GetProperty("planMentionsOldPins").GetBoolean());
                Assert.AreEqual(0, e2e.GetProperty("oldDeploymentCalls").GetInt32(), "NewPlan/CheckOnly never check an old ledger or signer");
                Assert.IsFalse(e2e.GetProperty("readsLedger").GetBoolean(), "no old ledger is read");
                Assert.AreEqual(1, e2e.GetProperty("newPlanBaselineCalls").GetInt32());
                Assert.AreEqual(1, e2e.GetProperty("checkBaselineCalls").GetInt32());
                Assert.AreEqual(JsonValueKind.Null, e2e.GetProperty("checkFailure").ValueKind, e2e.ToString());
                Assert.AreEqual("Pass", Text(e2e, "checkResult"));
                Assert.AreEqual(FreshSchema, Text(e2e, "checkSchema"));
                int e2eLength = e2e.GetProperty("checkEncodedLength").GetInt32();
                Assert.IsTrue(e2eLength > 0 && e2eLength <= 30000, $"fresh-install CheckOnly encoded command length {e2eLength}");
                Assert.IsTrue(e2e.GetProperty("checkInstallRootAbsent").GetBoolean());
                Assert.IsFalse(e2e.GetProperty("checkHasOldKeys").GetBoolean());
                Assert.IsTrue(e2e.GetProperty("checkUnpackaged").GetBoolean());
                Assert.AreEqual(15700, e2e.GetProperty("checkPackageQuery").GetInt32());
            }

            // Task 50 R2: installer signing diagnostics (FR-UPD-15, TM-07); acceptance is unchanged.
            JsonElement diagnostics = r.GetProperty("installerDiagnostics");
            string thumbC = new('C', 40), thumbD = new('D', 40);
            Assert.AreEqual($"Could not sign PCSteward.Desktop.exe with the owner-only certificate. Set-AuthenticodeSignature: Status=UnknownError; StatusMessage=[{Untrusted}]; SignerThumbprint={thumbC}; ExpectedThumbprint={thumbD}. Get-AuthenticodeSignature: Status=UnknownError; StatusMessage=[{Untrusted}]", Text(diagnostics, "withSigner"));
            Assert.AreEqual($"Could not sign PCSteward.Desktop.exe with the owner-only certificate. Set-AuthenticodeSignature: Status=UnknownError; StatusMessage=[untrusted]; SignerThumbprint=none; ExpectedThumbprint={thumbD}. Get-AuthenticodeSignature: Status=UnknownError; StatusMessage=[{Untrusted}]", Text(diagnostics, "withoutSigner"));
            Assert.AreEqual($"Could not sign PCSteward.ElevatedHelper.exe with the owner-only certificate. Set-AuthenticodeSignature: no result object; SignerThumbprint=none; ExpectedThumbprint={thumbD}. Get-AuthenticodeSignature: Status=UnknownError; StatusMessage=[{Untrusted}]", Text(diagnostics, "nullSignature"));
            StringAssert.EndsWith(Text(diagnostics, "queryFails"), "Get-AuthenticodeSignature: query failed: [FIXTURE query refused]");
            Assert.IsTrue(diagnostics.GetProperty("acceptanceUnchanged").GetBoolean(), "still Valid plus the owned signer's thumbprint");
            Assert.IsTrue(diagnostics.GetProperty("ledgerKeepsOriginalFailure").GetBoolean(), "the rollback ledger's originalFailure carries the diagnostic text");

            // Review M2: helpers and the template load only at the caller's reviewed constants; helpers only add functions.
            JsonElement pins = r.GetProperty("pins");
            Assert.AreEqual("Assert-OwnerOnlyScheduledCheckInactive,Wait-OwnerOnlyDeploymentProcess", Text(pins, "committedHelpersLoadInFreshRunspace"), Text(pins, "freshRunspaceErrors"));
            Assert.AreEqual(NotReviewedHelper, Text(pins, "unreviewedHelperRefused"));
            StringAssert.StartsWith(Text(pins, "reviewedHelperShadowingRefused"), "A pinned helper may not define or replace the command ");
            Assert.AreEqual(JsonValueKind.Null, pins.GetProperty("templateAccepted").ValueKind, pins.GetProperty("templateAccepted").ToString());
            Assert.AreEqual("Invoke-OwnerOnlyApprovedRedeployment.ps1 is not the reviewed version embedded in this caller; regenerate the plan from the reviewed scripts.", Text(pins, "templateChangedRefused"));
            Assert.AreEqual("A pinned helper may not define or replace the command Get-OwnerOnlySha256Hex.", Text(pins, "shadowCallerFunction"));
            Assert.AreEqual("A pinned helper may not define or replace the command Get-ChildItem.", Text(pins, "shadowCmdlet"));
            Assert.AreEqual("A pinned helper may not define or replace the command Get-Item.", Text(pins, "shadowNested"));
            Assert.AreEqual(JsonValueKind.Null, pins.GetProperty("newFunctionAllowed").ValueKind, pins.GetProperty("newFunctionAllowed").ToString());

            // Review L1/L2/L5: environment, absolute paths, working directory, old signer (no certificate store is opened).
            bool desktop = Text(r, "shell").StartsWith("Desktop ", StringComparison.Ordinal);
            JsonElement environment = r.GetProperty("environment");
            if (desktop)
            {
                Assert.AreEqual("The current Windows user is not the approved installing user.", Text(environment, "wrongSid"));
                Assert.AreEqual(JsonValueKind.Null, environment.GetProperty("currentSid").ValueKind, environment.GetProperty("currentSid").ToString());
                Assert.IsTrue(environment.GetProperty("elevationIsBool").GetBoolean());
            }
            else Assert.AreEqual("Run this caller in Windows PowerShell 5.1.", Text(environment, "notDesktop"));
            JsonElement absolute = r.GetProperty("absolutePath");
            Assert.AreEqual(JsonValueKind.Null, absolute.GetProperty("local").ValueKind, absolute.GetProperty("local").ToString());
            foreach (string name in new[] { "relative", "driveRelative", "unc" }) Assert.AreEqual(@"ScriptSource must be an absolute local path such as C:\...; relative, UNC and device paths are refused.", Text(absolute, name), name);
            Assert.AreEqual(@"NewPlanPath must be an absolute local path such as C:\...; relative, UNC and device paths are refused.", Text(absolute, "stream"));
            JsonElement directory = r.GetProperty("workingDirectory");
            Assert.AreEqual(JsonValueKind.Null, directory.GetProperty("outside").ValueKind, directory.GetProperty("outside").ToString());
            foreach (string name in new[] { "locationInsideInstall", "processInsideState", "exactInstallRoot" }) Assert.AreEqual("Run the caller from a working directory outside the PC Steward install and state roots.", Text(directory, name), name);
            JsonElement signer = r.GetProperty("oldSigner");
            foreach (string name in new[] { "notSigned", "otherSigner" }) Assert.AreEqual("The old signed deployment is not valid for the approved signer.", Text(signer, name), name);

            // Review L1: the real caller main dispatch (Windows PowerShell 5.1 only).
            if (desktop)
            {
                JsonElement main = r.GetProperty("callerMain");
                JsonElement checkOnly = main.GetProperty("checkOnlyReachesLedger");
                Assert.AreEqual(@"FIXTURE-LEDGER-REACHED PC Steward OwnerOnlyTest State\deployment.json", Text(checkOnly, "failure"), "CheckOnly must pass every earlier check and stop at the ledger");
                Assert.AreEqual("CheckOnly", Text(checkOnly, "mode"));
                Assert.AreEqual("Fail", Text(checkOnly, "result"));
                Assert.AreNotEqual(0, checkOnly.GetProperty("exitCode").GetInt32());
                Assert.AreEqual(NotReviewedHelper, Text(main.GetProperty("checkOnlyUnreviewedHelper"), "failure"));
                JsonElement freshMain = main.GetProperty("checkOnlyFreshPassesBaseline");
                Assert.AreEqual("CheckOnly", Text(freshMain, "mode"));
                Assert.AreEqual("Fail", Text(freshMain, "result"));
                Assert.IsFalse(Text(freshMain, "failure").Contains("FIXTURE-LEDGER-REACHED", StringComparison.Ordinal), "a fresh-install plan never reaches the old ledger");
                StringAssert.Contains(Text(freshMain, "failure"), "OwnerOnlyDeploymentCandidates", "it passes the real fresh-install baseline and stops at the synthetic candidate root");
                Assert.AreEqual(@"ScriptSource must be an absolute local path such as C:\...; relative, UNC and device paths are refused.", Text(main.GetProperty("newPlanRelativeSource"), "failure"), "the path is refused before any read");
            }
            else Assert.IsFalse(string.IsNullOrWhiteSpace(Text(r.GetProperty("notRun"), "callerMain")));

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
            Assert.AreEqual(Reparse, Text(copy, "reparseAncestor"));
            Assert.IsTrue(copy.GetProperty("reparseAncestorCreatedNothing").GetBoolean(), "review L3: no folder is created on a reparse refusal");
            Assert.AreEqual(JsonValueKind.Null, copy.GetProperty("helperLoadsCommitted").ValueKind, copy.GetProperty("helperLoadsCommitted").ToString());
            Assert.AreEqual("OwnerOnlyExecution.ps1 is not the reviewed version embedded in this copy script; nothing was copied.", Text(copy, "helperRefusesUnreviewed"));
            Assert.AreEqual("Run this copy with powershell.exe -File; the pinned helper is found only beside the script.", Text(copy, "helperNeedsScriptRoot"));
            Assert.AreEqual(JsonValueKind.Null, copy.GetProperty("destinationLocal").ValueKind, copy.GetProperty("destinationLocal").ToString());
            foreach (string name in new[] { "destinationRelative", "destinationUnc" }) Assert.AreEqual(@"DestinationParent must be an absolute local path such as C:\...; relative, UNC and device paths are refused.", Text(copy, name), name);
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
                string reasons = string.Join("; ", WindowsOnlyRows.Append("callerMain").Select(name => name + ": " + Text(notRun, name)));
                Assert.Inconclusive("Portable redeploy-tooling rows passed under " + Text(r, "shell") + $" (encoded command {length} characters; fresh-install {freshLength}). Windows-only rows not run here (Windows-CI evidence): " + reasons);
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
        StringAssert.Contains(template, "$preserveUserData=$OwnerOnlyPlan.schema -ceq 'pc-steward-exact-redeploy-v3' -or $fresh;");
        StringAssert.Contains(template, "@('pc-steward-exact-redeploy-v1','pc-steward-exact-redeploy-v2','pc-steward-exact-redeploy-v3','pc-steward-exact-fresh-install-v1')");
        // Task 50 R2: "none" is an allowed old origin only for the fresh-install schema (whose own validator then requires it).
        StringAssert.Contains(template, "$fresh=$OwnerOnlyPlan.schema -ceq 'pc-steward-exact-fresh-install-v1'");
        StringAssert.Contains(template, "-cnotin @(@('legacy-v6','pinned-unsigned-v1','pinned-unsigned-v2')+@(if($fresh){'none'}))");
        Assert.AreEqual(2, Count(template, "Assert-OwnerOnlyFreshInstallBaseline -InstallRoot $install -StateRoot $state"), "preflight and pre-install re-check");
        StringAssert.Contains(template, "$installed=& (Join-Path $scripts 'Install-OwnerOnlyProtectedTestDeployment.ps1') -Install -CreateOwnerOnlyCertificate -CandidateRoot $OwnerOnlyPlan.candidateRoot -CandidateManifestPath $OwnerOnlyPlan.candidateManifestPath -CandidateManifestSha256 $OwnerOnlyPlan.candidateManifestSha256");
        // The remover the template calls never names the per-user data root (it reports userDataPreserved = $true).
        string remover = ReadScript("Remove-OwnerOnlyProtectedTestDeployment.ps1");
        StringAssert.Contains(remover, "userDataPreserved = $true");
        Assert.IsFalse(remover.Contains("LocalApplicationData", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ToolingScriptsArePrintableAsciiAndNeverLoadSqlite()
    {
        foreach (string name in new[] { "Invoke-OwnerOnlyApprovedRedeployment.ps1", "OwnerOnlyExecution.ps1", "Invoke-OwnerOnlyRedeployCaller.ps1", "Copy-OwnerOnlyUserDatabase.ps1", "Install-OwnerOnlyProtectedTestDeployment.ps1" })
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

    /// <summary>
    /// Review M2 ordering guard: the caller and the copy script embed reviewed SHA-256 constants for the helper, the waiter and
    /// the bootstrap template. They are recomputed here from the committed bytes (LF form, and CRLF by converting), so any edit
    /// to those files fails this test until the constants are updated and re-reviewed.
    /// </summary>
    [TestMethod]
    public void CallerAndCopyConstantsPinTheCommittedHelperWaiterAndTemplate()
    {
        string caller = ReadScript("Invoke-OwnerOnlyRedeployCaller.ps1");
        string copy = ReadScript("Copy-OwnerOnlyUserDatabase.ps1");
        foreach (string name in PinnedFiles)
        {
            (string lf, string crlf) = CommittedHashes(name);
            string pair = $"'{name}'=@('{lf}','{crlf}')";
            Assert.AreEqual(1, Count(caller, pair), $"Invoke-OwnerOnlyRedeployCaller.ps1 must pin {name} as {pair}");
        }
        Assert.AreEqual(PinnedFiles.Length, System.Text.RegularExpressions.Regex.Count(caller, "'[A-Za-z-]+\\.ps1'=@\\('[A-F0-9]{64}','[A-F0-9]{64}'\\)"), "exactly the three reviewed pins");
        (string helperLf, string helperCrlf) = CommittedHashes("OwnerOnlyExecution.ps1");
        Assert.AreEqual(1, Count(copy, $"$hash -cnotin @('{helperLf}','{helperCrlf}')"), "the copy script loads the same reviewed helper");
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Count(copy, "[A-F0-9]{64}") / 2, "the copy script pins only the helper");
    }

    /// <summary>
    /// Task 50 R2 (D-R2; FR-UPD-15, TM-03/04/07/13, ADR-022): the packaged-process refusal and the fresh-install baseline. The
    /// only native binding in the embedded helper is kernel32 GetCurrentPackageFullName, bound in memory (no Add-Type, so no
    /// compiler and no TEMP file before the elevated template's private compiler TEMP exists); exactly 15700 is accepted; the
    /// caller enforces it before the one-use claim and the template before its first mutation; the baseline looks for the
    /// owner-only test subject in exactly CurrentUser My, Root and TrustedPublisher, read-only.
    /// </summary>
    [TestMethod]
    public void PackagedProcessRefusalAndFreshBaselineAreBoundAsReviewed()
    {
        string helpers = ReadScript("OwnerOnlyExecution.ps1");
        Assert.AreEqual(1, Count(helpers, ".DefinePInvokeMethod("));
        StringAssert.Contains(helpers, ".DefinePInvokeMethod('GetCurrentPackageFullName','kernel32.dll','Public,Static,PinvokeImpl','Standard',[int],[Type[]]@([uint32].MakeByRefType(),[IntPtr]),'Winapi','Unicode').SetImplementationFlags('PreserveSig')");
        StringAssert.Contains(helpers, ".Invoke($null,[object[]]@([uint32]0,[IntPtr]::Zero))");
        StringAssert.Contains(helpers, "if($code -ne 15700){throw");
        Assert.IsFalse(helpers.Contains("Add-Type", StringComparison.Ordinal));
        StringAssert.Contains(helpers, "foreach($name in @('My','Root','TrustedPublisher')){");
        StringAssert.Contains(helpers, "$store=[Security.Cryptography.X509Certificates.X509Store]::new($name,'CurrentUser')");
        StringAssert.Contains(helpers, "$store.Open('ReadOnly')");
        StringAssert.Contains(helpers, "Where-Object{$_.Subject -like 'CN=PC Steward Owner-Only Test *'}");
        Assert.IsFalse(helpers.Contains(".Remove(", StringComparison.Ordinal) || helpers.Contains("ReadWrite", StringComparison.Ordinal), "the helper never changes a certificate store");

        string caller = ReadScript("Invoke-OwnerOnlyRedeployCaller.ps1");
        int compare = caller.IndexOf("{throw 'The plan or encoded command differs from the approved pins.'}", StringComparison.Ordinal);
        int refuse = caller.IndexOf("    Assert-OwnerOnlyUnpackagedProcess\n", StringComparison.Ordinal);
        int claim = caller.IndexOf("$stream=New-OwnerOnlyAttemptRecord", StringComparison.Ordinal);
        Assert.IsTrue(compare > 0 && refuse > compare && claim > refuse, "packaged refusal after the approval check and before the one-use claim");
        Assert.AreEqual(1, Count(caller, "Assert-OwnerOnlyUnpackagedProcess"));
        StringAssert.Contains(caller, "$report['unpackagedProcess']=($package -is [int] -and $package -eq 15700)");
        StringAssert.Contains(caller, "$fresh=$ExpectedOldOrigin -ceq 'none'");

        string template = ReadScript("Invoke-OwnerOnlyApprovedRedeployment.ps1");
        int principal = template.IndexOf("{throw 'Exact installing SID and elevation required.'}", StringComparison.Ordinal);
        int packaged = template.IndexOf("\nAssert-OwnerOnlyUnpackagedProcess\n", StringComparison.Ordinal);
        int firstMutation = template.IndexOf("CreateDirectory(", StringComparison.Ordinal);
        Assert.IsTrue(principal > 0 && packaged > principal && firstMutation > packaged, "template refuses a packaged process before creating anything");
    }

    [TestMethod]
    public void RemoverRefusesAnyPCStewardProcess()
    {
        // Review M1: the scheduled host (PCSteward.ScheduledHost) holds e_sqlite3.dll and the database like the desktop does.
        string remover = ReadScript("Remove-OwnerOnlyProtectedTestDeployment.ps1");
        StringAssert.Contains(remover, "Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue | ForEach-Object { throw \"Refusing removal while PC Steward is running");
        Assert.IsFalse(remover.Contains("'PCSteward.Desktop','PCSteward.ElevatedHelper'", StringComparison.Ordinal));
        string helpers = ReadScript("OwnerOnlyExecution.ps1");
        StringAssert.Contains(helpers, "Import-Module ([IO.Path]::Combine($PSHOME,'Modules','ScheduledTasks','ScheduledTasks.psd1')) -ErrorAction Stop");
        StringAssert.Contains(helpers, "Get-ScheduledTask -TaskName 'PCSteward.ScheduledCheck' -ErrorAction SilentlyContinue -ErrorVariable failures");
    }

    private static (string Lf, string Crlf) CommittedHashes(string name)
    {
        string lf = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", name), Encoding.ASCII).Replace("\r\n", "\n", StringComparison.Ordinal);
        return (Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(lf))), Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(lf.Replace("\n", "\r\n", StringComparison.Ordinal)))));
    }

    private static void AssertExecution(JsonElement outcome, string status, int startCalls, int waitCalls)
    {
        Assert.AreEqual(JsonValueKind.Null, outcome.GetProperty("failure").ValueKind, outcome.ToString());
        Assert.AreEqual(status, Text(outcome, "returned"), outcome.ToString());
        Assert.AreEqual(status, Text(outcome, "status"), "attempt record: " + outcome);
        Assert.IsFalse(outcome.GetProperty("retry").GetBoolean());
        Assert.AreEqual(startCalls, outcome.GetProperty("startCalls").GetInt32(), "exactly one start request");
        Assert.AreEqual(waitCalls, outcome.GetProperty("waitCalls").GetInt32());
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
