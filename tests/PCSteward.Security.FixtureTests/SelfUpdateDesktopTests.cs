using System.Text;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-04/15, TM-12/26; ADR-032 revision 2 §5, §6 (desktop side) and §8: the desktop's pure rules for PC Steward's own
/// update. The session line it emits is the one the helper accepts; it accepts only the helper's answer for this launch; its
/// job file is canonical and strict; and the reopened desktop calls a job Updated only when it is the new version, its install
/// is intact and its manifest is the expected one, and the install was verified (by the helper copy, or after a restart by
/// its own check). Inconclusive only when the work folder or its files are missing or unreadable.
/// </summary>
[TestClass]
public sealed class SelfUpdateDesktopTests
{
    private const string Nonce = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string Pipe = "pcsteward-reviewed-0123456789abcdef0123456789abcdef";
    private static readonly Guid JobId = new("6f1c2d3e-4b5a-4978-8c6d-5e4f3a2b1c0d");
    private static readonly string Manifest = new('A', 64);
    private static readonly DateTimeOffset Approved = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    private static readonly SelfUpdateDesktopJob Job = new(JobId, "0.1.1", "0.1.2", 4242, new string('a', 40), new string('B', 64), new string('C', 64),
        Approved, SelfUpdateInstallPolicy.WorkFolderName(JobId), Manifest);
    private static readonly SelfUpdateRequest Request = new(Nonce, JobId, "0.1.2", 4242, new string('a', 40), new string('B', 64), new string('C', 64));
    private static readonly SelfUpdateExpectedInstall Expected = new(JobId, "0.1.2", Manifest,
        [new SelfUpdateTreeFile("PCSteward.ReleaseManifest.json", 10, new string('D', 64))]);
    private const long Boot = 1_800_000_100;

    [TestMethod]
    public void TheSessionLineIsTheOneTheHelperAccepts()
    {
        string[] line = [.. SelfUpdateDesktop.SessionArguments(4321, Pipe, Nonce)];
        Assert.AreEqual(10, line.Length);
        Assert.AreEqual("--kind", line[0]);
        Assert.AreEqual(SelfUpdateHelperProtocol.Kind, line[1]);
        Assert.IsTrue(SelfUpdateHelperProtocol.SelectsSession(line));
        Assert.IsTrue(SelfUpdateHelperProtocol.TryParseSession(line, out int pid, out string pipe, out string nonce));
        Assert.AreEqual(4321, pid);
        Assert.AreEqual(Pipe, pipe);
        Assert.AreEqual(Nonce, nonce);
        Assert.IsFalse(SelfUpdateHelperProtocol.SelectsFinish(line));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateDesktop.SessionArguments(0, Pipe, Nonce));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateDesktop.SessionArguments(4321, "pcsteward-elevated-0123456789abcdef0123456789abcdef", Nonce));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateDesktop.SessionArguments(4321, Pipe, Nonce.ToLowerInvariant()));
    }

    [TestMethod]
    public void OnlyThisLaunchsAnswerIsAccepted()
    {
        byte[] ready = SelfUpdateHelperProtocol.WriteResponse(new(Nonce, true, Job.WorkFolder, "0.1.2", Manifest, null));
        Assert.IsTrue(SelfUpdateDesktop.TryAcceptResponse(ready, Request, out SelfUpdateResponse? accepted));
        Assert.IsTrue(accepted!.Ready);
        byte[] refused = SelfUpdateHelperProtocol.WriteResponse(new(Nonce, false, null, null, null, SelfUpdateSessionRefusal.MsiNotPinned));
        Assert.IsTrue(SelfUpdateDesktop.TryAcceptResponse(refused, Request, out SelfUpdateResponse? no));
        Assert.AreEqual(SelfUpdateSessionRefusal.MsiNotPinned, no!.Reason);

        string otherNonce = new('F', 64);
        Assert.IsFalse(SelfUpdateDesktop.TryAcceptResponse(SelfUpdateHelperProtocol.WriteResponse(new(otherNonce, true, Job.WorkFolder, "0.1.2", Manifest, null)), Request, out _));
        Assert.IsFalse(SelfUpdateDesktop.TryAcceptResponse(SelfUpdateHelperProtocol.WriteResponse(
            new(Nonce, true, SelfUpdateInstallPolicy.WorkFolderName(Guid.NewGuid()), "0.1.2", Manifest, null)), Request, out _), "another job's folder");
        Assert.IsFalse(SelfUpdateDesktop.TryAcceptResponse(SelfUpdateHelperProtocol.WriteResponse(new(Nonce, true, Job.WorkFolder, "0.1.3", Manifest, null)), Request, out _));
        Assert.IsFalse(SelfUpdateDesktop.TryAcceptResponse(Encoding.ASCII.GetBytes("{\"nonce\":\"" + Nonce + "\",\"outcome\":\"ready\"}"), Request, out _));
        Assert.IsFalse(SelfUpdateDesktop.TryAcceptResponse([], Request, out _));
    }

    [TestMethod]
    public void TheJobFileIsCanonicalAndReadStrictly()
    {
        byte[] bytes = SelfUpdateDesktop.WriteJob(Job);
        Assert.IsTrue(SelfUpdateDesktop.TryReadJob(bytes, out SelfUpdateDesktopJob? read));
        Assert.AreEqual(Job, read);
        string text = Encoding.ASCII.GetString(bytes);
        Assert.IsFalse(SelfUpdateDesktop.TryReadJob(Encoding.ASCII.GetBytes(text.Replace("{", "{ ", StringComparison.Ordinal)), out _), "non-canonical");
        Assert.IsFalse(SelfUpdateDesktop.TryReadJob(Encoding.ASCII.GetBytes(text.Replace("}", ",\"extra\":1}", StringComparison.Ordinal)), out _), "extra key");
        Assert.IsFalse(SelfUpdateDesktop.TryReadJob(Encoding.ASCII.GetBytes(text.Replace(Job.WorkFolder, SelfUpdateInstallPolicy.WorkFolderName(Guid.NewGuid()), StringComparison.Ordinal)), out _),
            "a work folder that is not the job's");
        Assert.IsFalse(SelfUpdateDesktop.TryReadJob(Encoding.ASCII.GetBytes(text.Replace("\"0.1.1\"", "\"..\\\\x\"", StringComparison.Ordinal)), out _), "a path as a version");
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateDesktop.WriteJob(Job with { WorkFolder = @"..\PC Steward" }));
        Assert.ThrowsExactly<ArgumentException>(() => SelfUpdateDesktop.WriteJob(Job with { ManifestSha256 = Manifest.ToLowerInvariant() }));
    }

    [TestMethod]
    public void AVerifiedInstallOfTheNewVersionIsUpdated()
    {
        SelfUpdateJobVerdict verdict = SelfUpdateDesktop.Decide(Job, Inputs(Result(SelfUpdateFinishOutcome.Installed)));
        Assert.AreEqual(SelfUpdateJobState.Updated, verdict.State);
        Assert.IsTrue(verdict.IsFinal);
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Failed, "running-version-differs"),
            SelfUpdateDesktop.Decide(Job, Inputs(Result(SelfUpdateFinishOutcome.Installed)) with { RunningVersion = "0.1.1" }));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Failed, "install-not-intact"),
            SelfUpdateDesktop.Decide(Job, Inputs(Result(SelfUpdateFinishOutcome.Installed)) with { InstallIntact = false }));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Failed, "installed-manifest-differs"),
            SelfUpdateDesktop.Decide(Job, Inputs(Result(SelfUpdateFinishOutcome.Installed)) with { InstalledManifestSha256 = new string('E', 64) }));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Failed, "installed-manifest-differs"),
            SelfUpdateDesktop.Decide(Job, Inputs(Result(SelfUpdateFinishOutcome.Installed)) with { InstalledManifestSha256 = null }));
    }

    [TestMethod]
    public void ARestartIsNeededUntilTheBootTimeMovesAndThenTheOwnCheckDecides()
    {
        byte[] restart = Result(SelfUpdateFinishOutcome.RestartNeeded, Boot);
        int checks = 0;
        Assert.AreEqual(SelfUpdateJobState.RestartToFinish, SelfUpdateDesktop.Decide(Job, Inputs(restart, () => { checks++; return true; }) with { BootTimeUnixSeconds = Boot + 5 }).State);
        Assert.AreEqual(0, checks, "the tree is not read before a restart");
        Assert.IsFalse(new SelfUpdateJobVerdict(SelfUpdateJobState.RestartToFinish, "restart-needed").IsFinal);
        SelfUpdateJobVerdict after = SelfUpdateDesktop.Decide(Job, Inputs(restart, () => { checks++; return true; }) with { BootTimeUnixSeconds = Boot + 3600 });
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Updated, "installed-after-restart"), after);
        Assert.AreEqual(1, checks);
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Failed, "installed-tree-differs"),
            SelfUpdateDesktop.Decide(Job, Inputs(restart, () => false) with { BootTimeUnixSeconds = Boot + 3600 }));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Failed, "installed-tree-differs"),
            SelfUpdateDesktop.Decide(Job, Inputs(restart, () => throw new IOException()) with { BootTimeUnixSeconds = Boot + 3600 }));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Failed, "running-version-differs"),
            SelfUpdateDesktop.Decide(Job, Inputs(restart) with { BootTimeUnixSeconds = Boot + 3600, RunningVersion = "0.1.1" }));
    }

    [TestMethod]
    public void EveryOtherOutcomeIsFailedAndNeverUpdated()
    {
        (SelfUpdateFinishOutcome Outcome, string Code)[] cases =
        [
            (SelfUpdateFinishOutcome.InstallFailed, "install-failed"),
            (SelfUpdateFinishOutcome.MismatchUninstalled, "mismatch-uninstalled"),
            (SelfUpdateFinishOutcome.MismatchUninstallFailed, "mismatch-uninstall-failed"),
            (SelfUpdateFinishOutcome.PcStewardDidNotClose, "pc-steward-did-not-close"),
            (SelfUpdateFinishOutcome.RefusedBeforeInstall, "refused-before-install"),
        ];
        foreach ((SelfUpdateFinishOutcome outcome, string code) in cases)
            Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Failed, code), SelfUpdateDesktop.Decide(Job, Inputs(Result(outcome))), code);
    }

    [TestMethod]
    public void MissingOrForeignFilesAreInconclusiveAndAMissingResultIsInProgressOnlyWithinTheWindow()
    {
        byte[] installed = Result(SelfUpdateFinishOutcome.Installed);
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Inconclusive, "work-folder-unavailable"),
            SelfUpdateDesktop.Decide(Job, Inputs(installed) with { WorkFolderProtected = false }));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Inconclusive, "expected-install-unreadable"),
            SelfUpdateDesktop.Decide(Job, Inputs(installed) with { ExpectedBytes = null }));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Inconclusive, "expected-install-unreadable"),
            SelfUpdateDesktop.Decide(Job, Inputs(installed) with { ExpectedBytes = SelfUpdateInstallPolicy.WriteExpected(Expected with { JobId = Guid.NewGuid() }) }));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Inconclusive, "expected-install-unreadable"),
            SelfUpdateDesktop.Decide(Job, Inputs(installed) with { ExpectedBytes = SelfUpdateInstallPolicy.WriteExpected(Expected with { ManifestSha256 = new string('E', 64) }) }));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Inconclusive, "result-unreadable"),
            SelfUpdateDesktop.Decide(Job, Inputs([0x7B])));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Inconclusive, "result-unreadable"),
            SelfUpdateDesktop.Decide(Job, Inputs(SelfUpdateHelperProtocol.WriteResult(new(Guid.NewGuid(), SelfUpdateFinishOutcome.Installed, 0, "0.1.2", Manifest, null, "installed")))));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Inconclusive, "result-unreadable"),
            SelfUpdateDesktop.Decide(Job, Inputs(SelfUpdateHelperProtocol.WriteResult(new(JobId, SelfUpdateFinishOutcome.Installed, 0, "0.1.3", Manifest, null, "installed")))));

        SelfUpdateOutcomeInputs pending = Inputs(null);
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.InProgress, "installing"), SelfUpdateDesktop.Decide(Job, pending with { Now = Approved.AddMinutes(5) }));
        Assert.IsFalse(SelfUpdateDesktop.Decide(Job, pending with { Now = Approved.AddMinutes(5) }).IsFinal);
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Inconclusive, "no-result"), SelfUpdateDesktop.Decide(Job, pending with { Now = Approved.AddHours(2) }));
        Assert.AreEqual(new SelfUpdateJobVerdict(SelfUpdateJobState.Inconclusive, "no-result"), SelfUpdateDesktop.Decide(Job, pending with { Now = Approved.AddMinutes(-5) }),
            "a clock before the approval is not a running install");
    }

    [TestMethod]
    public void ThePublicStatesMirrorTheInternalOnes()
    {
        CollectionAssert.AreEqual(Enum.GetNames<SelfUpdateJobState>(), Enum.GetNames<PcStewardUpdateState>());
        foreach (SelfUpdateJobState state in Enum.GetValues<SelfUpdateJobState>())
            Assert.AreEqual(state.ToString(), ((PcStewardUpdateState)state).ToString());
    }

    private static byte[] Result(SelfUpdateFinishOutcome outcome, long? boot = null) =>
        SelfUpdateHelperProtocol.WriteResult(new(JobId, outcome, outcome is SelfUpdateFinishOutcome.PcStewardDidNotClose or SelfUpdateFinishOutcome.RefusedBeforeInstall ? null : 0,
            "0.1.2", Manifest, outcome == SelfUpdateFinishOutcome.RestartNeeded ? boot ?? Boot : null, "x"));

    private static SelfUpdateOutcomeInputs Inputs(byte[]? result, Func<bool>? treeMatches = null) =>
        new("0.1.2", true, Manifest, true, result, SelfUpdateInstallPolicy.WriteExpected(Expected), Boot + 10,
            _ => treeMatches?.Invoke() ?? true, Approved.AddMinutes(1));
}
