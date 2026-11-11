using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-04/15, TM-03/04/12/13/20/23/24/26/27; ADR-032 revision 2 §6–§7: the elevated self-update session and the finish copy
/// over fake machines, and their pure formats. The session answers only a request carrying the launch nonce, runs every check
/// in order, stops at the first refusal with one compiled code, and starts the finish copy only after every check passed. The
/// finish copy acts only on its own protected work folder, installs only after PC Steward closed, records a needed restart
/// with the boot time, and uninstalls an install that does not match. Real named pipes; no Windows API is called.
/// </summary>
[TestClass]
public sealed class SelfUpdateHelperSessionTests
{
    private const string Nonce = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static readonly Guid Job = new("6f1c2d3e-4b5a-4978-8c6d-5e4f3a2b1c0d");
    private static readonly string Leaf = SelfUpdateInstallPolicy.WorkFolderName(Job);
    private static readonly string ProgramFiles = Path.Combine(Path.GetTempPath(), "pcsteward-fixture-program-files");
    private static readonly string Private = Path.Combine(ProgramFiles, Leaf, "private");
    private static readonly string[] SessionOrder =
    [
        "ProfileFolder", "CreateProtectedFolder", "CreatePrivateFolder", "CopyInput:zip", "CopyInput:msi", "ConfirmAsync", "RemoveOlderWorkFolders", "CarriesCompiledPin", "ReadMsiTables",
        "HasPerUserRegistration", "RunMsiexecAsync:/a", "HasPerUserRegistration", "ReadImage", "BothCarrySameReleasePin", "CreateProtectedFolder", "CopyOwnImage", "WriteNewFile:job.json",
        "WriteNewFile:expected-install.json", "StartFinish",
    ];
    private const string ProductCode = "{11111111-1111-1111-1111-111111111111}";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);
    private static readonly string[] InstallOnly = ["/i"];
    private static readonly string[] InstallThenRemove = ["/i", "/x"];
    private static readonly SelfUpdateSessionBudgets Budgets = new(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    // ---- Command lines and dispatch ----

    [TestMethod]
    public void TheCommandLinesAreExactAndDoNotOverlapOtherModes()
    {
        string pipe = "pcsteward-reviewed-" + new string('a', 32);
        string[] session = ["--kind", "pcsteward-self-update", "--protocol", "2", "--desktop-pid", "4321", "--pipe", pipe, "--nonce", Nonce];
        Assert.IsTrue(SelfUpdateHelperProtocol.TryParseSession(session, out int pid, out string name, out string nonce));
        Assert.AreEqual((4321, pipe, Nonce), (pid, name, nonce));
        Assert.IsTrue(ElevatedHelperEntryPoint.SelectsSelfUpdate(session));
        Assert.IsFalse(ElevatedHelperEntryPoint.SelectsSelfUpdateFinish(session));

        string[] probe = ["--kind", "qualify-chocolatey-helper", .. session[2..]];
        Assert.IsFalse(SelfUpdateHelperProtocol.SelectsSession(probe), "the probe line is not a self-update line");
        Assert.IsTrue(ElevatedHelperEntryPoint.SelectsProbe(probe));
        Assert.IsFalse(SelfUpdateHelperProtocol.TryParseSession(probe, out _, out _, out _));

        foreach (string[] bad in new[]
        {
            session[..8],
            [.. session, "--extra"],
            Replace(session, 5, "04321"), Replace(session, 5, "+4321"), Replace(session, 5, "0"),
            Replace(session, 7, "pcsteward-reviewed-" + new string('A', 32)), Replace(session, 9, Nonce.ToLowerInvariant()),
            Replace(session, 2, "--desktop-pid"), Replace(session, 3, "1"), Replace(session, 1, "PCSteward-Self-Update"),
        })
            Assert.IsFalse(SelfUpdateHelperProtocol.TryParseSession(bad, out _, out _, out _), string.Join(' ', bad));

        Assert.IsTrue(SelfUpdateHelperProtocol.TryParseFinish(["--finish-self-update", Leaf], out string leaf));
        Assert.AreEqual(Leaf, leaf);
        Assert.IsTrue(ElevatedHelperEntryPoint.SelectsSelfUpdateFinish(["--finish-self-update", Leaf]));
        foreach (string[] bad in new[]
        {
            new[] { "--finish-self-update", "PC Steward" }, ["--finish-self-update", Leaf + "\\.."], ["--finish-self-update", Leaf.ToUpperInvariant()],
            ["--finish-self-update", Leaf, "x"], ["--Finish-Self-Update", Leaf],
        })
            Assert.IsFalse(SelfUpdateHelperProtocol.TryParseFinish(bad, out _), string.Join(' ', bad));

        string helper = Path.Combine(ProgramFiles, Leaf, "bin", "PCSteward.ElevatedHelper.exe");
        Assert.AreEqual("\"" + helper + "\" --finish-self-update \"" + Leaf + "\"", SelfUpdateHelperProtocol.FinishCommandLine(helper, Leaf));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.FinishCommandLine(helper, "PC Steward"));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.FinishCommandLine("bin\\PCSteward.ElevatedHelper.exe", Leaf));
    }

    [TestMethod]
    public void TheResponseJobAndResultFilesAreCanonical()
    {
        SelfUpdateResponse ready = new(Nonce, true, Leaf, "0.1.2", new string('A', 64), null);
        byte[] bytes = SelfUpdateHelperProtocol.WriteResponse(ready);
        Assert.IsTrue(SelfUpdateHelperProtocol.TryReadResponse(bytes, out SelfUpdateResponse? read));
        Assert.AreEqual(ready, read);
        SelfUpdateResponse refused = new(Nonce, false, null, null, null, SelfUpdateSessionRefusal.ArtifactMismatch);
        bytes = SelfUpdateHelperProtocol.WriteResponse(refused);
        StringAssert.Contains(Encoding.ASCII.GetString(bytes), "\"reason\":\"artifact-mismatch\"");
        Assert.IsTrue(SelfUpdateHelperProtocol.TryReadResponse(bytes, out read));
        Assert.AreEqual(refused, read);
        Assert.IsFalse(SelfUpdateHelperProtocol.TryReadResponse([.. bytes, (byte)' '], out _));
        Assert.IsFalse(SelfUpdateHelperProtocol.TryReadResponse(Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(bytes).Replace("artifact-mismatch", "C:\\\\x", StringComparison.Ordinal)), out _));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.WriteResponse(ready with { WorkFolder = "..\\PC Steward" }));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.WriteResponse(refused with { WorkFolder = Leaf }));
        foreach (SelfUpdateSessionRefusal reason in Enum.GetValues<SelfUpdateSessionRefusal>())
            Assert.IsTrue(SelfUpdateHelperProtocol.TryReadResponse(SelfUpdateHelperProtocol.WriteResponse(refused with { Reason = reason }), out SelfUpdateResponse? each) &&
                each!.Reason == reason, reason.ToString());

        SelfUpdateJobFile job = new(Job, "0.1.2", 4242, ReleaseCheckFixture.Commit, new string('E', 64), new string('F', 64), 4321, ProductCode);
        Assert.IsTrue(SelfUpdateHelperProtocol.TryReadJob(SelfUpdateHelperProtocol.WriteJob(job), out SelfUpdateJobFile? jobRead));
        Assert.AreEqual(job, jobRead);
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.WriteJob(job with { MsiSha256 = "..\\x" }));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.WriteJob(job with { DesktopProcessId = 0 }));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.WriteJob(job with { ProductCode = "{11111111-1111-1111-1111-11111111111a}" }));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.WriteJob(job with { ProductCode = "..\\x" }));

        SelfUpdateResultFile result = new(Job, SelfUpdateFinishOutcome.RestartNeeded, 3010, "0.1.2", new string('A', 64), 1_700_000_000, "restart-needed");
        Assert.IsTrue(SelfUpdateHelperProtocol.TryReadResult(SelfUpdateHelperProtocol.WriteResult(result), out SelfUpdateResultFile? resultRead));
        Assert.AreEqual(result, resultRead);
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.WriteResult(result with { BootTimeUnixSeconds = null }));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.WriteResult(result with { Outcome = SelfUpdateFinishOutcome.Installed }));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateHelperProtocol.WriteResult(result with { Detail = "C:\\Users" }));
        SelfUpdateResultFile failed = result with { Outcome = SelfUpdateFinishOutcome.InstallFailed, ExitCode = null, BootTimeUnixSeconds = null, Detail = "msiexec-did-not-finish" };
        Assert.IsTrue(SelfUpdateHelperProtocol.TryReadResult(SelfUpdateHelperProtocol.WriteResult(failed), out resultRead));
        Assert.AreEqual(failed, resultRead);
    }

    [TestMethod]
    public void TheAclPolicyAcceptsOnlyTheExactDescriptors()
    {
        Assert.IsTrue(SelfUpdateAclPolicy.IsProtectedWorkFolder("O:BAD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;AU)"));
        Assert.IsTrue(SelfUpdateAclPolicy.IsProtectedWorkFolder("O:SYD:PAI(A;OICI;0x1200a9;;;AU)(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)"));
        foreach (string bad in new[]
        {
            "D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;AU)",
            "O:S-1-5-21-1-2-3-1001D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;AU)",
            "O:BAD:AI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;AU)",
            "O:BAD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;AU)",
            "O:BAD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;AU)(A;OICI;FA;;;S-1-5-21-1-2-3-1001)",
            "O:BAD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)",
            "", "garbage",
        })
            Assert.IsFalse(SelfUpdateAclPolicy.IsProtectedWorkFolder(bad), bad);
        // Mythos review S2: the private folder is SYSTEM and Administrators only, and the work folder descriptor is not it.
        Assert.IsTrue(SelfUpdateAclPolicy.IsPrivateFolder(SelfUpdateAclPolicy.PrivateFolderDescriptor));
        Assert.IsTrue(SelfUpdateAclPolicy.IsPrivateFolder("O:SYD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)"));
        foreach (string bad in new[]
        {
            SelfUpdateAclPolicy.WorkFolderDescriptor,
            "D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)",
            "O:BAG:SYD:AI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)",
            "O:BAG:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)",
            "O:S-1-5-21-1-2-3-1001D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)",
            "O:BAG:SYD:PAI(A;OICI;FA;;;SY)",
        })
            Assert.IsFalse(SelfUpdateAclPolicy.IsPrivateFolder(bad), bad);
        Assert.IsFalse(SelfUpdateAclPolicy.IsProtectedWorkFolder(SelfUpdateAclPolicy.PrivateFolderDescriptor));
        Assert.IsTrue(SelfUpdateAclPolicy.IsProtectedInstallRoot("D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)"));
        Assert.IsFalse(SelfUpdateAclPolicy.IsProtectedInstallRoot("D:AI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)"));
        Assert.IsTrue(SelfUpdateAclPolicy.IsInheritedInstallEntry("D:AI(A;ID;FA;;;SY)(A;ID;FA;;;BA)(A;ID;0x1200a9;;;BU)", directory: false));
        Assert.IsTrue(SelfUpdateAclPolicy.IsInheritedInstallEntry("D:AI(A;OICIID;FA;;;SY)(A;OICIID;FA;;;BA)(A;OICIID;0x1200a9;;;BU)", directory: true));
        Assert.IsFalse(SelfUpdateAclPolicy.IsInheritedInstallEntry("D:AI(A;ID;FA;;;SY)(A;ID;FA;;;BA)(A;ID;0x1200a9;;;BU)(A;;FA;;;BU)", directory: false));
        Assert.IsFalse(SelfUpdateAclPolicy.IsInheritedInstallEntry("D:PAI(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;BU)", directory: false));
    }

    [TestMethod]
    public void MsiexecArgumentsAreQuotedTheWayMsiexecReadsThem()
    {
        Assert.AreEqual("/a \"C:\\Program Files\\x\\a.msi\" /qn TARGETDIR=\"C:\\Program Files\\x\\image\" /l*v \"C:\\Program Files\\x\\extract.log\"",
            WindowsSelfUpdateHost.MsiexecCommandLine(["/a", "C:\\Program Files\\x\\a.msi", "/qn", "TARGETDIR=C:\\Program Files\\x\\image", "/l*v", "C:\\Program Files\\x\\extract.log"]));
        Assert.AreEqual("/x \"{2AD17330-BAD5-46BE-A409-20814B26673C}\" /qn /norestart",
            WindowsSelfUpdateHost.MsiexecCommandLine(["/x", "{2AD17330-BAD5-46BE-A409-20814B26673C}", "/qn", "/norestart"]));
        Assert.ThrowsExactly<ArgumentException>(() => WindowsSelfUpdateHost.MsiexecCommandLine(["/i", "a\" /qn CUSTOM=\"1"]));
        Assert.ThrowsExactly<ArgumentException>(() => WindowsSelfUpdateHost.MsiexecCommandLine(["/i", ""]));
    }

    // ---- Session ----

    [TestMethod]
    public async Task EveryCheckPassesAndTheFinishCopyStartsLast()
    {
        SessionHost host = new();
        (SelfUpdateSessionResult result, SelfUpdateResponse? response) = await RunSessionAsync(host);
        Assert.AreEqual(SelfUpdateSessionExit.ResponseWritten, result.Exit, result.Diagnostic);
        Assert.IsTrue(response!.Ready);
        Assert.AreEqual(Leaf, response.WorkFolder);
        Assert.AreEqual("0.1.2", response.ToVersion);
        Assert.AreEqual(Sha(host.Build.Manifest), response.ManifestSha256);
        Assert.AreEqual("StartFinish", host.Calls[^1]);
        CollectionAssert.AreEqual(SessionOrder, host.Calls.Where(call => !call.StartsWith("ReadFile", StringComparison.Ordinal)).ToArray());
        Assert.AreEqual(Path.Combine(ProgramFiles, Leaf, "bin", "PCSteward.ElevatedHelper.exe"), host.FinishPath);
        Assert.IsTrue(SelfUpdateHelperProtocol.TryReadJob(host.Written["job.json"], out SelfUpdateJobFile? job));
        Assert.AreEqual(4321, job!.DesktopProcessId);
        Assert.AreEqual(host.Request.MsiSha256, job.MsiSha256);
        Assert.AreEqual(ProductCode, job.ProductCode, "the finish copy removes only the product code read from the protected MSI copy");
        Assert.IsTrue(SelfUpdateInstallPolicy.TryReadExpected(host.Written["expected-install.json"], out SelfUpdateExpectedInstall? expected));
        Assert.AreEqual(5, expected!.Files.Count);
        Assert.AreEqual(Path.Combine(host.Profile, "AppData", "Local", "PCSteward", "Downloads", "SelfUpdate", host.Request.MsiSha256 + ".msi"), host.MsiSource);
    }

    [TestMethod]
    public async Task EachFailedCheckRefusesWithItsCodeAndStartsNothing()
    {
        (Action<SessionHost> Arrange, SelfUpdateSessionRefusal Expected)[] cases =
        [
            (host => host.InstallSupported = false, SelfUpdateSessionRefusal.InstallNotIntact),
            (host => host.RequestToVersion("0.1.1"), SelfUpdateSessionRefusal.VersionNotNewer),
            (host => host.RequestToVersion("0.1.0"), SelfUpdateSessionRefusal.VersionNotNewer),
            (host => host.ProfileOverride = "", SelfUpdateSessionRefusal.ProfileUnavailable),
            (host => host.CreateFolder = false, SelfUpdateSessionRefusal.WorkFolderUnavailable),
            (host => host.CreatePrivate = false, SelfUpdateSessionRefusal.WorkFolderUnavailable),
            (host => host.ZipCopy = SelfUpdateCopyVerdict.Unreadable, SelfUpdateSessionRefusal.InputUnreadable),
            (host => host.MsiCopy = SelfUpdateCopyVerdict.Changed, SelfUpdateSessionRefusal.InputChanged),
            (host => host.ZipBytesOverride = Encoding.ASCII.GetBytes("not a zip"), SelfUpdateSessionRefusal.InputChanged),
            (host => host.ZipContent = Encoding.ASCII.GetBytes("not a zip"), SelfUpdateSessionRefusal.CheckFileRefused),
            (host => host.ZipContent = ReleaseCheckFixture.Zip(ReleaseCheckFixture.Record(msiVersion: "0.1.2")), SelfUpdateSessionRefusal.RecordNotApproved),
            (host => host.UseFixturePins = true, SelfUpdateSessionRefusal.RecordNotApproved),
            (host => host.Verdict = HelperGitHubVerdict.RunNotGenuine, SelfUpdateSessionRefusal.RunNotGenuine),
            (host => host.Verdict = HelperGitHubVerdict.ArtifactGone, SelfUpdateSessionRefusal.ArtifactGone),
            (host => host.Verdict = HelperGitHubVerdict.ArtifactMismatch, SelfUpdateSessionRefusal.ArtifactMismatch),
            (host => host.Verdict = HelperGitHubVerdict.CouldNotReach, SelfUpdateSessionRefusal.CouldNotConfirm),
            (host => host.GitHubHangs = true, SelfUpdateSessionRefusal.CouldNotConfirm),
            (host => host.MsiPinned = false, SelfUpdateSessionRefusal.MsiNotPinned),
            (host => host.TablesReadable = false, SelfUpdateSessionRefusal.MsiUnreadable),
            (host => host.TablesSha = new string('0', 64), SelfUpdateSessionRefusal.MsiTablesDiffer),
            (host => host.ForbiddenTable = true, SelfUpdateSessionRefusal.MsiStructureRefused),
            (host => host.NoProductCode = true, SelfUpdateSessionRefusal.MsiStructureRefused),
            (host => host.PerUserBefore = true, SelfUpdateSessionRefusal.PerUserRegistration),
            (host => host.PerUserAfterExtract = true, SelfUpdateSessionRefusal.PerUserRegistration),
            (host => host.ExtractExit = 1603, SelfUpdateSessionRefusal.ExtractFailed),
            (host => host.ExtractExit = null, SelfUpdateSessionRefusal.ExtractFailed),
            (host => host.ImageReadable = false, SelfUpdateSessionRefusal.ImageLayoutRefused),
            (host => host.TamperImage = true, SelfUpdateSessionRefusal.ContentDiffers),
            (host => host.ExtractedPinned = false, SelfUpdateSessionRefusal.ExecutablesNotPinned),
            (host => host.OwnCopy = false, SelfUpdateSessionRefusal.HelperCopyFailed),
            (host => host.WriteFiles = false, SelfUpdateSessionRefusal.JobFilesFailed),
            (host => host.FinishStarts = false, SelfUpdateSessionRefusal.FinishNotStarted),
            (host => host.ThrowOnPin = true, SelfUpdateSessionRefusal.InternalFault),
        ];
        foreach ((Action<SessionHost> arrange, SelfUpdateSessionRefusal expected) in cases)
        {
            SessionHost host = new();
            arrange(host);
            (SelfUpdateSessionResult result, SelfUpdateResponse? response) = await RunSessionAsync(host);
            Assert.AreEqual(SelfUpdateSessionExit.ResponseWritten, result.Exit, expected.ToString());
            Assert.IsFalse(response!.Ready, expected.ToString());
            Assert.AreEqual(expected, response.Reason, expected.ToString());
            Assert.AreEqual(Nonce, response.Nonce);
            if (expected != SelfUpdateSessionRefusal.FinishNotStarted) Assert.IsNull(host.FinishPath, expected.ToString());
            // Mythos review S3: once a job file may exist, a stop before the finish copy started closes the job with a result.
            if (expected is SelfUpdateSessionRefusal.JobFilesFailed or SelfUpdateSessionRefusal.FinishNotStarted) host.AssertJobClosed(expected.ToString());
            else Assert.IsFalse(host.Written.ContainsKey(SelfUpdateHelperProtocol.ResultFileName), expected.ToString());
            // Mythos review S4: earlier jobs' folders are removed only after GitHub confirmed this request.
            Assert.AreEqual(host.Calls.Contains("CarriesCompiledPin"), host.Calls.Contains("RemoveOlderWorkFolders"), expected.ToString());
        }
    }

    [TestMethod]
    public async Task AnUncorrelatedRequestGetsNoResponse()
    {
        SessionHost host = new();
        byte[] other = SelfUpdateInstallPolicy.WriteRequest(host.Request with { Nonce = new string('1', 64) });
        (SelfUpdateSessionResult result, _) = await RunSessionAsync(host, other);
        Assert.AreEqual(SelfUpdateSessionExit.NoResponse, result.Exit);
        Assert.IsEmpty(host.Calls);

        (result, _) = await RunSessionAsync(new SessionHost(), Encoding.ASCII.GetBytes("{\"nonce\":\"" + Nonce + "\",\"path\":\"C:\\\\Windows\"}"));
        Assert.AreEqual(SelfUpdateSessionExit.NoResponse, result.Exit);
        (result, _) = await RunSessionAsync(new SessionHost(), request: []);
        Assert.AreEqual(SelfUpdateSessionExit.NoResponse, result.Exit, "no frame within the read budget");
    }

    [TestMethod]
    public async Task ADesktopDisconnectStopsTheSessionBeforeTheFinishCopy()
    {
        SessionHost host = new() { ExtractExit = 0 };
        string name = "pcsteward-test-" + Guid.NewGuid().ToString("N");
        await using NamedPipeServerStream server = Server(name);
        await using NamedPipeClientStream client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task waiting = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waiting;
        await PipeMessageFraming.WriteAsync(server, SelfUpdateInstallPolicy.WriteRequest(host.Request), CancellationToken.None).WaitAsync(Wait);
        host.OnExtract = () => server.Disconnect();
        SelfUpdateSessionResult result = await SelfUpdateSession.RunAsync(client, Nonce, Peer(), host, host, TimeProvider.System,
            TimeProvider.System.GetTimestamp(), Budgets, CancellationToken.None).WaitAsync(Wait);
        Assert.AreNotEqual(SelfUpdateSessionExit.ResponseWritten, result.Exit);
        Assert.IsNull(host.FinishPath, "the finish copy never starts without the desktop");
        Assert.AreEqual(SelfUpdateSessionRefusal.TimedOut, result.Response?.Reason);
    }

    [TestMethod]
    public async Task ADesktopDisconnectAfterTheJobFilesClosesTheJob()
    {
        SessionHost host = new();
        string name = "pcsteward-test-" + Guid.NewGuid().ToString("N");
        await using NamedPipeServerStream server = Server(name);
        await using NamedPipeClientStream client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task waiting = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waiting;
        await PipeMessageFraming.WriteAsync(server, SelfUpdateInstallPolicy.WriteRequest(host.Request), CancellationToken.None).WaitAsync(Wait);
        host.OnExpectedWritten = () =>
        {
            server.Disconnect();
            Thread.Sleep(TimeSpan.FromSeconds(1));
        };
        SelfUpdateSessionResult result = await SelfUpdateSession.RunAsync(client, Nonce, Peer(), host, host, TimeProvider.System,
            TimeProvider.System.GetTimestamp(), Budgets, CancellationToken.None).WaitAsync(Wait);
        Assert.AreEqual(SelfUpdateSessionRefusal.TimedOut, result.Response?.Reason);
        Assert.IsNull(host.FinishPath, "the finish copy never starts without the desktop");
        host.AssertJobClosed("disconnect after the job files");
    }

    // ---- Finish copy ----

    [TestMethod]
    public async Task TheFinishCopyInstallsAndVerifies()
    {
        FinishHost host = new();
        Assert.AreEqual(SelfUpdateFinish.ResultWritten, await SelfUpdateFinish.RunAsync(Leaf, elevated: true, host, CancellationToken.None));
        SelfUpdateResultFile result = host.Result();
        Assert.AreEqual(SelfUpdateFinishOutcome.Installed, result.Outcome);
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsNull(result.BootTimeUnixSeconds);
        CollectionAssert.AreEqual(InstallOnly, host.Msiexec.Select(arguments => arguments[0]).ToArray());
        CollectionAssert.Contains(host.Msiexec[0].ToArray(), "/norestart");
    }

    [TestMethod]
    public async Task TheFinishCopyRecordsEveryOtherOutcome()
    {
        FinishHost restart = new() { InstallExit = 3010 };
        await SelfUpdateFinish.RunAsync(Leaf, elevated: true, restart, CancellationToken.None);
        Assert.AreEqual(SelfUpdateFinishOutcome.RestartNeeded, restart.Result().Outcome);
        Assert.AreEqual(1_700_000_000, restart.Result().BootTimeUnixSeconds);
        Assert.AreEqual(0, restart.TreeReads, "after 3010 the desktop checks the tree once the PC has restarted");

        FinishHost failed = new() { InstallExit = 1603 };
        await SelfUpdateFinish.RunAsync(Leaf, elevated: true, failed, CancellationToken.None);
        Assert.AreEqual((SelfUpdateFinishOutcome.InstallFailed, (int?)1603), (failed.Result().Outcome, failed.Result().ExitCode));

        FinishHost open = new() { Closes = false };
        await SelfUpdateFinish.RunAsync(Leaf, elevated: true, open, CancellationToken.None);
        Assert.AreEqual(SelfUpdateFinishOutcome.PcStewardDidNotClose, open.Result().Outcome);
        Assert.IsEmpty(open.Msiexec);

        FinishHost changed = new() { MsiHash = new string('0', 64) };
        await SelfUpdateFinish.RunAsync(Leaf, elevated: true, changed, CancellationToken.None);
        Assert.AreEqual(SelfUpdateFinishOutcome.RefusedBeforeInstall, changed.Result().Outcome);
        Assert.IsEmpty(changed.Msiexec);

        foreach ((Action<FinishHost> arrange, string detail) in new (Action<FinishHost>, string)[]
        {
            (host => host.TreeDiffers = true, "installed-tree-differs"),
            (host => host.Protected = false, "installed-dacl-differs"),
            (host => host.Pinned = false, "installed-executables-not-pinned"),
            (host => host.Products = [new(ProductCode, "0.1.2"), new("{22222222-2222-2222-2222-222222222222}", "0.1.1")], "product-registration-differs"),
        })
        {
            FinishHost host = new();
            arrange(host);
            await SelfUpdateFinish.RunAsync(Leaf, elevated: true, host, CancellationToken.None);
            Assert.AreEqual(SelfUpdateFinishOutcome.MismatchUninstalled, host.Result().Outcome, detail);
            Assert.AreEqual(detail, host.Result().Detail);
            CollectionAssert.AreEqual(InstallThenRemove, host.Msiexec.Select(arguments => arguments[0]).ToArray(), detail);
            Assert.AreEqual(ProductCode, host.Msiexec[1][1], detail);
        }

        // Security review R1: no msiexec at all while the owner's registry registers the product, and no uninstall after one appears.
        FinishHost perUser = new() { PerUserBefore = true };
        await SelfUpdateFinish.RunAsync(Leaf, elevated: true, perUser, CancellationToken.None);
        Assert.AreEqual((SelfUpdateFinishOutcome.RefusedBeforeInstall, "per-user-registration"), (perUser.Result().Outcome, perUser.Result().Detail));
        Assert.IsEmpty(perUser.Msiexec);
        FinishHost appeared = new() { PerUserAfter = true };
        await SelfUpdateFinish.RunAsync(Leaf, elevated: true, appeared, CancellationToken.None);
        Assert.AreEqual((SelfUpdateFinishOutcome.MismatchUninstallFailed, "per-user-registration; uninstall-refused"), (appeared.Result().Outcome, appeared.Result().Detail));
        CollectionAssert.AreEqual(InstallOnly, appeared.Msiexec.Select(arguments => arguments[0]).ToArray());

        // Security review R2: a machine product that is not this MSI's own is never removed.
        FinishHost other = new() { Products = [new("{22222222-2222-2222-2222-222222222222}", "0.1.2")] };
        await SelfUpdateFinish.RunAsync(Leaf, elevated: true, other, CancellationToken.None);
        Assert.AreEqual((SelfUpdateFinishOutcome.MismatchUninstallFailed, "product-registration-differs; no-product-to-remove"), (other.Result().Outcome, other.Result().Detail));
        CollectionAssert.AreEqual(InstallOnly, other.Msiexec.Select(arguments => arguments[0]).ToArray());

        FinishHost stuck = new() { TreeDiffers = true, UninstallExit = 1603 };
        await SelfUpdateFinish.RunAsync(Leaf, elevated: true, stuck, CancellationToken.None);
        Assert.AreEqual(SelfUpdateFinishOutcome.MismatchUninstallFailed, stuck.Result().Outcome);
    }

    [TestMethod]
    public async Task TheFinishCopyRefusesAnythingButItsOwnProtectedJob()
    {
        foreach ((Action<FinishHost> arrange, bool elevated, string label) in new (Action<FinishHost>, bool, string)[]
        {
            (_ => { }, false, "not elevated"),
            (host => host.Path = Path.Combine(ProgramFiles, "PC Steward", "PCSteward.ElevatedHelper.exe"), true, "runs from the install root"),
            (host => host.Path = Path.Combine(ProgramFiles, SelfUpdateInstallPolicy.WorkFolderName(Guid.NewGuid()), "bin", "PCSteward.ElevatedHelper.exe"), true, "another job's copy"),
            (host => host.UnprotectedFolder = Leaf, true, "work folder not protected"),
            (host => host.UnprotectedFolder = "bin", true, "bin not protected"),
            (host => host.UnprotectedFolder = "private", true, "private folder not administrators-only"),
            (host => host.JobBytes = Encoding.ASCII.GetBytes("{}"), true, "job unreadable"),
            (host => host.ExpectedJob = Guid.NewGuid(), true, "files name different jobs"),
            (host => host.EarlierResult = SelfUpdateHelperProtocol.WriteResult(new(Job, SelfUpdateFinishOutcome.Installed, 0, "0.1.2", new string('A', 64), null, "installed")),
                true, "the job already has a result"),
        })
        {
            FinishHost host = new();
            arrange(host);
            Assert.AreEqual(SelfUpdateFinish.Refused, await SelfUpdateFinish.RunAsync(Leaf, elevated, host, CancellationToken.None), label);
            Assert.IsEmpty(host.Msiexec, label);
            Assert.IsFalse(host.Written.ContainsKey(SelfUpdateHelperProtocol.ResultFileName), label);
        }
        Assert.AreEqual(SelfUpdateFinish.Refused, await SelfUpdateFinish.RunAsync("PC Steward", elevated: true, new FinishHost(), CancellationToken.None));
    }

    // ---- Harness ----

    private static SelfUpdatePeer Peer() => new(4321, "S-1-5-21-1-2-3-1001", "PCSteward.Desktop.exe", static (_, _) => true);

    private static async Task<(SelfUpdateSessionResult Result, SelfUpdateResponse? Response)> RunSessionAsync(SessionHost host, byte[]? request = null)
    {
        string name = "pcsteward-test-" + Guid.NewGuid().ToString("N");
        await using NamedPipeServerStream server = Server(name);
        await using NamedPipeClientStream client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task waiting = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waiting;
        request ??= SelfUpdateInstallPolicy.WriteRequest(host.Request);
        // The session reads while the desktop end writes, as in production; a hang fails the test instead of the whole run.
        Task<SelfUpdateSessionResult> session = Task.Run(() => SelfUpdateSession.RunAsync(client, Nonce, Peer(), host, host, TimeProvider.System,
            TimeProvider.System.GetTimestamp(), Budgets, CancellationToken.None));
        if (request.Length > 0) await PipeMessageFraming.WriteAsync(server, request, CancellationToken.None).WaitAsync(Wait);
        SelfUpdateSessionResult result = await session.WaitAsync(Wait);
        if (result.Exit != SelfUpdateSessionExit.ResponseWritten) return (result, null);
        byte[] frame = await PipeMessageFraming.ReadAsync(server, CancellationToken.None).WaitAsync(Wait);
        Assert.IsTrue(SelfUpdateHelperProtocol.TryReadResponse(frame, out SelfUpdateResponse? response));
        Assert.AreEqual(result.Response, response);
        return (result, response);
    }

    /// <summary>
    /// The desktop end. Windows pipe writes wait for buffer quota, so both directions get room for a whole frame, as the
    /// protocol-2 fixtures give theirs.
    /// </summary>
    private static NamedPipeServerStream Server(string name) =>
        new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64 * 1024, 64 * 1024);

    private static string[] Replace(string[] source, int index, string value)
    {
        string[] copy = [.. source];
        copy[index] = value;
        return copy;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class SessionHost : ISelfUpdateSessionHost, ISelfUpdateInstallCheck
    {
        private byte[] zip;
        private readonly byte[] installedManifest;
        private readonly byte[] helperImage = Encoding.ASCII.GetBytes("installed helper image");

        internal SessionHost()
        {
            Build = SelfUpdateInstallPolicyTests.Build.Create(pins: ReleaseSigningPins.Compiled);
            zip = ReleaseCheckFixture.Zip(Build.RecordBytes);
            Request = new(Nonce, Job, "0.1.2", 4242, ReleaseCheckFixture.Commit, Sha(zip), new string('F', 64));
            installedManifest = Encoding.ASCII.GetBytes("{\"format\":\"pc-steward-release-manifest:1\",\"productVersion\":\"0.1.1-private-beta\",\"files\":[" +
                "{\"path\":\"PCSteward.ElevatedHelper.exe\",\"bytes\":" + helperImage.Length + ",\"sha256\":\"" + Sha(helperImage) + "\"}]}");
        }

        internal SelfUpdateInstallPolicyTests.Build Build { get; private set; }
        internal SelfUpdateRequest Request { get; private set; }

        internal void RequestToVersion(string version) => Request = Request with { ToVersion = version };

        /// <summary>A check zip the request names by its hash (so the copy and re-hash pass) but whose content is as given.</summary>
        internal byte[] ZipContent
        {
            set
            {
                zip = value;
                Request = Request with { CheckZipSha256 = Sha(value) };
            }
        }

        /// <summary>A record naming the fixture pins, not the compiled ones: not the record the owner approved.</summary>
        internal bool UseFixturePins
        {
            set
            {
                Build = SelfUpdateInstallPolicyTests.Build.Create(pins: value ? ReleaseCheckFixture.Pins : ReleaseSigningPins.Compiled);
                ZipContent = ReleaseCheckFixture.Zip(Build.RecordBytes);
            }
        }
        internal List<string> Calls { get; } = [];
        internal Dictionary<string, byte[]> Written { get; } = new(StringComparer.Ordinal);
        internal string Profile { get; } = Path.Combine(Path.GetTempPath(), "pcsteward-fixture-profile");
        internal string? FinishPath { get; private set; }
        internal string? MsiSource { get; private set; }
        internal bool InstallSupported { get; set; } = true;
        internal string? ProfileOverride { get; set; }
        internal bool CreateFolder { get; set; } = true;
        internal bool CreatePrivate { get; set; } = true;
        internal Action? OnExpectedWritten { get; set; }
        internal SelfUpdateCopyVerdict ZipCopy { get; set; } = SelfUpdateCopyVerdict.Copied;
        internal SelfUpdateCopyVerdict MsiCopy { get; set; } = SelfUpdateCopyVerdict.Copied;
        internal byte[]? ZipBytesOverride { get; set; }
        internal HelperGitHubVerdict Verdict { get; set; } = HelperGitHubVerdict.Confirmed;
        internal bool GitHubHangs { get; set; }
        internal bool MsiPinned { get; set; } = true;
        internal bool ThrowOnPin { get; set; }
        internal bool TablesReadable { get; set; } = true;
        internal string TablesSha { get; set; } = new('C', 64);
        internal bool ForbiddenTable { get; set; }
        internal bool NoProductCode { get; set; }
        internal bool PerUserBefore { get; set; }
        internal bool PerUserAfterExtract { get; set; }
        internal int? ExtractExit { get; set; } = 0;
        internal Action? OnExtract { get; set; }
        internal bool ImageReadable { get; set; } = true;
        internal bool TamperImage { get; set; }
        internal bool ExtractedPinned { get; set; } = true;
        internal bool OwnCopy { get; set; } = true;
        internal bool WriteFiles { get; set; } = true;
        internal bool FinishStarts { get; set; } = true;

        public string InstallRoot => Path.Combine(ProgramFiles, "PC Steward");
        string ISelfUpdateSessionHost.ProgramFiles => SelfUpdateHelperSessionTests.ProgramFiles;
        private static string InstallFolder => Path.Combine(Private, "image", "PFiles64", "PC Steward");

        /// <summary>Mythos review S3: the session wrote a result that makes the finish copy refuse this job.</summary>
        internal void AssertJobClosed(string label)
        {
            Assert.IsTrue(Written.TryGetValue(SelfUpdateHelperProtocol.ResultFileName, out byte[]? bytes), label);
            Assert.IsTrue(SelfUpdateHelperProtocol.TryReadResult(bytes!, out SelfUpdateResultFile? result), label);
            Assert.AreEqual((Job, SelfUpdateFinishOutcome.RefusedBeforeInstall, SelfUpdateSession.SessionStoppedDetail), (result!.JobId, result.Outcome, result.Detail), label);
        }

        public HelperProtocolCapabilityResult EvaluateInstalledRelease(DeploymentLayoutProtectionCheck requesterCannotModify, string peerImagePath) =>
            HelperProtocolCapabilityResult.For(InstallSupported ? HelperProtocolCapabilityStatus.Supported : HelperProtocolCapabilityStatus.Mismatch);

        public string? ProfileFolder(string userSid)
        {
            Calls.Add("ProfileFolder");
            return ProfileOverride is { Length: 0 } ? null : ProfileOverride ?? Profile;
        }

        public bool CreateProtectedFolder(string path)
        {
            Calls.Add("CreateProtectedFolder");
            return CreateFolder;
        }

        public bool CreatePrivateFolder(string path)
        {
            Calls.Add("CreatePrivateFolder");
            Assert.AreEqual(Private, path);
            return CreatePrivate;
        }

        public void RemoveOlderWorkFolders(string keepLeaf) => Calls.Add("RemoveOlderWorkFolders");

        public SelfUpdateCopyVerdict CopyInput(string source, string destination, long maximumBytes, string expectedSha256)
        {
            bool isZip = source.EndsWith(".zip", StringComparison.Ordinal);
            Calls.Add("CopyInput:" + (isZip ? "zip" : "msi"));
            Assert.AreEqual(Private, Path.GetDirectoryName(destination), "input copies go in the private folder");
            if (!isZip) MsiSource = source;
            return isZip ? ZipCopy : MsiCopy;
        }

        public byte[]? ReadFile(string path, int maximumBytes)
        {
            Calls.Add("ReadFile");
            if (path.EndsWith(".zip", StringComparison.Ordinal)) return ZipBytesOverride ?? zip;
            if (path == Path.Combine(InstallRoot, ReleaseManifestPolicy.FileName)) return installedManifest;
            if (path == Path.Combine(InstallFolder, ReleaseManifestPolicy.FileName)) return Build.Manifest;
            string relative = Path.GetRelativePath(InstallFolder, path).Replace('/', '\\');
            return Build.Files.TryGetValue(relative, out byte[]? bytes) ? bytes : null;
        }

        public async Task<HelperGitHubVerdict> ConfirmAsync(long runId, string sourceCommit, string checkZipSha256, CancellationToken cancellationToken)
        {
            Calls.Add("ConfirmAsync");
            Assert.AreEqual((4242L, ReleaseCheckFixture.Commit, Request.CheckZipSha256), (runId, sourceCommit, checkZipSha256));
            if (GitHubHangs) await Task.Delay(Timeout.Infinite, cancellationToken);
            return Verdict;
        }

        public bool CarriesCompiledPin(string path)
        {
            Calls.Add("CarriesCompiledPin");
            return ThrowOnPin ? throw new InvalidOperationException("fault") : MsiPinned;
        }

        public MsiDatabaseSnapshot? ReadMsiTables(string msiPath)
        {
            Calls.Add("ReadMsiTables");
            Dictionary<string, MsiTable> tables = new(StringComparer.Ordinal);
            if (ForbiddenTable) tables["ServiceInstall"] = new("ServiceInstall", ["ServiceInstall"], [["svc"]]);
            // As MsiTableDigest.Read returns them: the tables normalised (ProductCode "*"), the real code beside them.
            tables["Property"] = new("Property", ["Property", "Value"], [["ProductName", "PC Steward"], ["ProductCode", "*"]]);
            return TablesReadable ? new([], TablesSha, tables, NoProductCode ? null : ProductCode) : null;
        }

        public async Task<int?> RunMsiexecAsync(IReadOnlyList<string> arguments, string privateFolder, TimeSpan limit, CancellationToken cancellationToken)
        {
            Calls.Add("RunMsiexecAsync:" + arguments[0]);
            Assert.AreEqual(Private, privateFolder);
            Assert.AreEqual(Path.Combine(Private, Request.MsiSha256 + ".msi"), arguments[1]);
            Assert.AreEqual("TARGETDIR=" + Path.Combine(Private, "image"), arguments[3]);
            Assert.AreEqual(Path.Combine(Private, "extract.log"), arguments[5]);
            if (OnExtract is not null)
            {
                OnExtract();
                try { await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken); }
                catch (OperationCanceledException) { }
            }
            return ExtractExit;
        }

        public bool HasPerUserRegistration(string productCode)
        {
            Calls.Add("HasPerUserRegistration");
            Assert.AreEqual(ProductCode, productCode);
            return Calls.Contains("RunMsiexecAsync:/a") ? PerUserAfterExtract : PerUserBefore;
        }

        public SelfUpdateImage? ReadImage(string imageRoot, string packageLeaf)
        {
            Calls.Add("ReadImage");
            Assert.AreEqual(Request.MsiSha256 + ".msi", packageLeaf);
            if (!ImageReadable) return null;
            List<SelfUpdateTreeFile> tree = Build.Tree();
            if (TamperImage) tree[0] = tree[0] with { Sha256 = new string('0', 64) };
            return new(InstallFolder, tree);
        }

        public bool BothCarrySameReleasePin(string first, string second)
        {
            Calls.Add("BothCarrySameReleasePin");
            return ExtractedPinned;
        }

        public bool CopyOwnImage(string destination, string expectedSha256)
        {
            Calls.Add("CopyOwnImage");
            Assert.AreEqual(expectedSha256, Sha(helperImage));
            return OwnCopy;
        }

        public bool WriteNewFile(string path, byte[] bytes)
        {
            Calls.Add("WriteNewFile:" + Path.GetFileName(path));
            Assert.AreEqual(Path.Combine(ProgramFiles, Leaf), Path.GetDirectoryName(path), "the job files and result stay where users can read them");
            Written[Path.GetFileName(path)] = bytes;
            if (Path.GetFileName(path) == SelfUpdateInstallPolicy.ExpectedInstallFileName) OnExpectedWritten?.Invoke();
            return WriteFiles || Path.GetFileName(path) == SelfUpdateHelperProtocol.ResultFileName;
        }

        public bool StartFinish(string helperCopyPath, string workFolderLeaf)
        {
            Calls.Add("StartFinish");
            if (!FinishStarts) return false;
            FinishPath = helperCopyPath;
            return true;
        }
    }

    private sealed class FinishHost : ISelfUpdateFinishHost
    {
        private readonly SelfUpdateExpectedInstall expected;

        internal FinishHost()
        {
            Path = System.IO.Path.Combine(ProgramFiles, Leaf, "bin", "PCSteward.ElevatedHelper.exe");
            expected = new(Job, "0.1.2", new string('A', 64),
            [
                new(ReleaseManifestPolicy.FileName, 10, new string('A', 64)),
                new("PCSteward.Desktop.exe", 20, new string('B', 64)),
            ]);
            JobBytes = SelfUpdateHelperProtocol.WriteJob(new(Job, "0.1.2", 4242, ReleaseCheckFixture.Commit, new string('E', 64), new string('F', 64), 4321, ProductCode));
        }

        internal string Path { get; set; }
        internal string? UnprotectedFolder { get; set; }
        internal byte[] JobBytes { get; set; }
        internal Guid ExpectedJob { get; set; } = Job;
        internal byte[]? EarlierResult { get; set; }
        internal bool Closes { get; set; } = true;
        internal string MsiHash { get; set; } = new('F', 64);
        internal int? InstallExit { get; set; } = 0;
        internal int? UninstallExit { get; set; } = 0;
        internal bool TreeDiffers { get; set; }
        internal bool Protected { get; set; } = true;
        internal bool Pinned { get; set; } = true;
        internal IReadOnlyList<SelfUpdateRelatedProduct> Products { get; set; } = [new(ProductCode, "0.1.2")];
        internal bool PerUserBefore { get; set; }
        internal bool PerUserAfter { get; set; }
        internal List<IReadOnlyList<string>> Msiexec { get; } = [];
        internal Dictionary<string, byte[]> Written { get; } = new(StringComparer.Ordinal);
        internal int TreeReads { get; private set; }

        public string InstallRoot => System.IO.Path.Combine(ProgramFiles, "PC Steward");
        string ISelfUpdateFinishHost.ProgramFiles => SelfUpdateHelperSessionTests.ProgramFiles;
        public string? ProcessPath => Path;

        internal SelfUpdateResultFile Result()
        {
            Assert.IsTrue(SelfUpdateHelperProtocol.TryReadResult(Written[SelfUpdateHelperProtocol.ResultFileName], out SelfUpdateResultFile? result));
            return result!;
        }

        public bool IsProtectedWorkFolder(string path) =>
            UnprotectedFolder is null || !string.Equals(System.IO.Path.GetFileName(path), UnprotectedFolder, StringComparison.Ordinal);

        public bool IsPrivateFolder(string path)
        {
            Assert.AreEqual(Private, path);
            return UnprotectedFolder != "private";
        }

        public byte[]? ReadFile(string path, int maximumBytes) => System.IO.Path.GetFileName(path) switch
        {
            SelfUpdateHelperProtocol.JobFileName => JobBytes,
            SelfUpdateInstallPolicy.ExpectedInstallFileName => SelfUpdateInstallPolicy.WriteExpected(expected with { JobId = ExpectedJob }),
            SelfUpdateHelperProtocol.ResultFileName => EarlierResult,
            _ => null,
        };

        public string? HashFile(string path, long maximumBytes) => MsiHash;

        public Task<bool> WaitForPcStewardToCloseAsync(int desktopProcessId, TimeSpan limit, CancellationToken cancellationToken)
        {
            Assert.AreEqual(4321, desktopProcessId);
            return Task.FromResult(Closes);
        }

        public Task<int?> RunMsiexecAsync(IReadOnlyList<string> arguments, string privateFolder, TimeSpan limit, CancellationToken cancellationToken)
        {
            Assert.AreEqual(Private, privateFolder);
            Assert.AreEqual(System.IO.Path.Combine(Private, arguments[0] == "/i" ? "install.log" : "uninstall.log"), arguments[^1]);
            if (arguments[0] == "/i") Assert.AreEqual(System.IO.Path.Combine(Private, new string('F', 64) + ".msi"), arguments[1]);
            Msiexec.Add(arguments);
            return Task.FromResult(arguments[0] == "/i" ? InstallExit : UninstallExit);
        }

        public IReadOnlyList<SelfUpdateTreeFile>? ReadInstalledTree()
        {
            TreeReads++;
            return TreeDiffers ? [.. expected.Files.Take(1)] : expected.Files;
        }

        public bool InstalledTreeIsProtected() => Protected;

        public bool BothCarrySameReleasePin(string first, string second) => Pinned;

        public IReadOnlyList<SelfUpdateRelatedProduct>? RelatedProducts() => Products;

        public bool HasPerUserRegistration(string productCode)
        {
            Assert.AreEqual(ProductCode, productCode);
            return Msiexec.Count == 0 ? PerUserBefore : PerUserAfter;
        }

        public long BootTimeUnixSeconds() => 1_700_000_000;

        public bool WriteNewFile(string path, byte[] bytes)
        {
            Written[System.IO.Path.GetFileName(path)] = bytes;
            return true;
        }
    }
}
