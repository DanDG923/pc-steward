using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §4 result-mapping table, §5.4 and §8 N3 (portable), with rev 3 §3.1
/// stage C, §10 N1/N3 and the rev-4 M2 correction: the strict query parser over fake launcher observations, and every
/// stage-C mapping row. Nothing is launched.
/// </summary>
[TestClass]
public sealed class ChocolateyStageCQueryTests
{
    private const string Id = "git";
    private const string Fingerprint = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static readonly ChocolateyStageCBaseline Baseline = new(Id, "1.2.0", Fingerprint, Fingerprint, "1.2.0", "1.3.0");

    // ---- Parser: launch facts first ------------------------------------------------------------------------------------

    [TestMethod]
    public void InterruptsAndUnsettledLaunchesAreNeverParsed()
    {
        Assert.AreEqual(ChocolateyQueryStatus.Interrupted, List(Observation("git|1.2.0", cancelled: true)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.Interrupted, List(Observation("git|1.2.0", timedOut: true)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.Interrupted, List(Observation("git|1.2.0", timedOut: true, settled: false)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.Unsettled, List(Observation("git|1.2.0", settled: false)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.Unsettled, List(Observation("", invocation: NativeInvocation.Unknown)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.Unsettled, List(Observation("", invocation: NativeInvocation.Unknown, settled: true)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.NotStarted, List(Observation("", invocation: NativeInvocation.NotStarted)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.Interrupted, Outdated(Observation("", cancelled: true)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.Unsettled, Outdated(Observation("", settled: false)).Status);
        Assert.AreEqual(0, List(Observation("git|1.2.0", cancelled: true)).Records.Count, "an unparsed query carries no records");
    }

    [TestMethod]
    public void TheCaptureCeilingAnIncompleteCaptureAndANonZeroExitRefuseBeforeAnyRecord()
    {
        Assert.AreEqual(ChocolateyQueryStatus.OutputLimitExceeded, List(Observation("git|1.2.0", overflow: true)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.OutputLimitExceeded,
            List(Observation("git|1.2.0\n" + new string('\n', ChocolateyQueryParser.MaximumCharacters))).Status, "more than 1,000,000 characters");
        Assert.AreEqual(ChocolateyQueryStatus.OutputLimitExceeded,
            List(Observation("git|1.2.0\n", standardError: new string('x', ChocolateyQueryParser.MaximumCharacters))).Status, "the ceiling covers both streams");
        Assert.AreEqual(ChocolateyQueryStatus.Incomplete, List(Observation("git|1.2.0", exitCode: null)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.Incomplete, List(Observation("git|1.2.0", eof: false)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.ExitFailure, List(Observation("git|1.2.0", exitCode: 1)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.ExitFailure, Outdated(Observation("git|1.2.0|1.3.0|false", exitCode: 2)).Status);
        Assert.AreEqual(ChocolateyQueryStatus.ExitFailure, List(Observation("git|1.2.0", exitCode: -1)).Status);
    }

    // ---- Parser: strict records -----------------------------------------------------------------------------------------

    [TestMethod]
    public void AStrictListRecordParsesWithLfOrCrLfAndBlankLines()
    {
        foreach (string output in new[] { "git|1.2.0", "git|1.2.0\n", "git|1.2.0\r\n", "\r\ngit|1.2.0\r\n\r\n", "GIT|1.2.0" })
        {
            ChocolateyQueryOutcome<ChocolateyInstalledRecord> outcome = List(Observation(output));
            Assert.AreEqual(ChocolateyQueryStatus.Parsed, outcome.Status, output);
            Assert.AreEqual(1, outcome.Records.Count);
            Assert.AreEqual("1.2.0", outcome.Records[0].Version);
        }
        ChocolateyQueryOutcome<ChocolateyInstalledRecord> empty = List(Observation(""));
        Assert.AreEqual(ChocolateyQueryStatus.Parsed, empty.Status);
        Assert.AreEqual(0, empty.Records.Count, "zero records parse; the mapping refuses them as incomplete");
    }

    [TestMethod]
    [DataRow("git|1.2", DisplayName = "non-normalized version")]
    [DataRow("git|1.2.0.0", DisplayName = "non-normalized zero revision")]
    [DataRow("git|01.2.0", DisplayName = "leading zero")]
    [DataRow("git|1.2.0+meta", DisplayName = "metadata version")]
    [DataRow("git|1.2.0|extra", DisplayName = "extra column")]
    [DataRow("git", DisplayName = "missing column")]
    [DataRow("git 1.2.0", DisplayName = "space separated")]
    [DataRow("git|1.2.0 ", DisplayName = "trailing space")]
    [DataRow(" git|1.2.0", DisplayName = "leading space")]
    [DataRow("git|1.2.0\r", DisplayName = "lone carriage return")]
    [DataRow("git\t|1.2.0", DisplayName = "control character")]
    [DataRow("git.install|1.2.0", DisplayName = "another package")]
    [DataRow("a..b|1.2.0", DisplayName = "unsupported ID")]
    [DataRow("Chocolatey v2.7.5", DisplayName = "banner line")]
    [DataRow("git|1.2.0\nChocolatey v2.7.5", DisplayName = "trailing banner")]
    [DataRow("git|", DisplayName = "empty version")]
    public void AMalformedListRecordRefusesTheWholeQuery(string output) =>
        Assert.AreEqual(ChocolateyQueryStatus.Malformed, List(Observation(output)).Status);

    [TestMethod]
    public void ADuplicateListRecordRefusesEvenWithTheSameValue()
    {
        Assert.AreEqual(ChocolateyQueryStatus.Duplicate, List(Observation("git|1.2.0\ngit|1.2.0")).Status);
        Assert.AreEqual(ChocolateyQueryStatus.Duplicate, List(Observation("git|1.2.0\nGIT|1.3.0")).Status);
    }

    [TestMethod]
    public void AStrictOutdatedRecordSetParsesWithPinnedFlags()
    {
        ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord> outcome = Outdated(Observation(
            "git|1.2.0|1.3.0|false\r\n7zip|23.1.0|24.8.0|true\npython3|3.12.1|3.13.0-rc1|False\n"));
        Assert.AreEqual(ChocolateyQueryStatus.Parsed, outcome.Status);
        Assert.AreEqual(3, outcome.Records.Count);
        Assert.AreEqual(new ChocolateyNativeOutdatedRecord("git", "1.2.0", "1.3.0", false), outcome.Records[0]);
        Assert.IsTrue(outcome.Records[1].Pinned);
        Assert.AreEqual(ChocolateyQueryStatus.Parsed, Outdated(Observation("")).Status);
    }

    [TestMethod]
    [DataRow("git|1.2.0|1.3.0", DisplayName = "no pinned column")]
    [DataRow("git|1.2.0|1.3.0|yes", DisplayName = "unknown pinned value")]
    [DataRow("git|1.2.0|1.3.0|false|x", DisplayName = "extra column")]
    [DataRow("git|1.2|1.3.0|false", DisplayName = "non-normalized installed")]
    [DataRow("git|1.2.0|1.3|false", DisplayName = "non-normalized available")]
    [DataRow("git|1.2.0|1.3.0.0|false", DisplayName = "non-normalized zero revision")]
    [DataRow("git|1.2.0|1.3.0+meta|false", DisplayName = "metadata available")]
    [DataRow("a..b|1.2.0|1.3.0|false", DisplayName = "unsupported ID")]
    [DataRow("git|1.2.0|1.3.0|false\nother|1|2|false", DisplayName = "malformed other record")]
    [DataRow("Outdated Packages", DisplayName = "header line")]
    public void AMalformedOutdatedRecordRefusesTheWholeQuery(string output) =>
        Assert.AreEqual(ChocolateyQueryStatus.Malformed, Outdated(Observation(output)).Status);

    [TestMethod]
    public void ADuplicateOutdatedIdRefusesTheWholeQuery()
    {
        Assert.AreEqual(ChocolateyQueryStatus.Duplicate, Outdated(Observation("git|1.2.0|1.3.0|false\nGit|1.2.0|1.3.0|false")).Status);
        Assert.AreEqual(ChocolateyQueryStatus.Duplicate, Outdated(Observation("other|1.0.0|2.0.0|false\nother|1.0.0|3.0.0|true")).Status);
    }

    [TestMethod]
    public void TheRecordBoundIsExactly1024LogicalRecords()
    {
        string within = string.Join('\n', Enumerable.Range(0, ChocolateyQueryParser.MaximumRecords).Select(index => $"p{index}|1.0.0|2.0.0|false"));
        string over = within + "\nlast|1.0.0|2.0.0|false";
        Assert.AreEqual(ChocolateyQueryStatus.Parsed, Outdated(Observation(within + "\n\n")).Status);
        Assert.AreEqual(ChocolateyQueryParser.MaximumRecords, Outdated(Observation(within)).Records.Count);
        Assert.AreEqual(ChocolateyQueryStatus.RecordLimitExceeded, Outdated(Observation(over)).Status);
        // The record bound is applied before record syntax: 1,025 lines of anything refuse as an output bound.
        Assert.AreEqual(ChocolateyQueryStatus.RecordLimitExceeded, List(Observation(string.Join('\n', Enumerable.Repeat("x", 1025)))).Status);
    }

    [TestMethod]
    public void ParsedOutcomesAreTheOnlyOnesWithRecordsAndTheParserNeedsASupportedId()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyQueryParser.ParseListExact(Observation("git|1.2.0"), "a..b"));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyQueryOutcome<ChocolateyInstalledRecord>.Create(Observation(""),
            ChocolateyQueryStatus.Malformed, [new ChocolateyInstalledRecord("git", "1.2.0")]));
        Assert.IsTrue(ChocolateyQueryParser.IsNormalizedVersion("1.2.3.4"));
        Assert.IsTrue(ChocolateyQueryParser.IsNormalizedVersion("1.2.3-beta.1"));
        foreach (string? text in new[] { null, "", "1", "1.2", "1.2.3.0", "v1.2.3", "1.2.3 ", new string('1', 129) })
            Assert.IsFalse(ChocolateyQueryParser.IsNormalizedVersion(text), text);
    }

    // ---- Mapping rows -------------------------------------------------------------------------------------------------

    [TestMethod]
    public void Row1AnInterruptedOrUnsettledListIsUndeterminedWithTheMatchingFlags()
    {
        Undetermined(Map(Observation("git|1.2.0", cancelled: true), null), cancelled: true, timedOut: false, settled: true);
        Undetermined(Map(Observation("git|1.2.0", timedOut: true), null), cancelled: false, timedOut: true, settled: true);
        Undetermined(Map(Observation("git|1.2.0", timedOut: true, settled: false, overflow: true), null), cancelled: false, timedOut: true,
            settled: false, overflow: true);
        Undetermined(Map(Observation("git|1.2.0", settled: false), null), cancelled: false, timedOut: false, settled: false);
        Undetermined(Map(Observation("", invocation: NativeInvocation.Unknown, settled: true), null), cancelled: false, timedOut: false, settled: false);
        Undetermined(Map(Observation("", invocation: NativeInvocation.NotStarted, cancelled: true), null), cancelled: true, timedOut: false, settled: true);
    }

    [TestMethod]
    public void Row1AnInterruptedOrUnsettledOutdatedQueryIsUndetermined()
    {
        Undetermined(Map(Observation("git|1.2.0"), Observation("git|1.2.0|1.3.0|false", cancelled: true)), cancelled: true, timedOut: false, settled: true);
        Undetermined(Map(Observation("git|1.2.0"), Observation("git|1.2.0|1.3.0|false", timedOut: true)), cancelled: false, timedOut: true, settled: true);
        Undetermined(Map(Observation("git|1.2.0"), Observation("git|1.2.0|1.3.0|false", settled: false)), cancelled: false, timedOut: false, settled: false);
    }

    [TestMethod]
    [DataRow("exit", DisplayName = "settled non-zero exit")]
    [DataRow("notstarted", DisplayName = "no process")]
    [DataRow("incomplete", DisplayName = "no exit code")]
    [DataRow("malformed", DisplayName = "malformed record")]
    [DataRow("duplicate", DisplayName = "duplicate record")]
    public void Row2ASettledListFailureIsPreflightQueryFailedWithStageBValuesAndNoCandidate(string failure)
    {
        NativeProcessObservation list = failure switch
        {
            "exit" => Observation("git|1.2.0", exitCode: 1),
            "notstarted" => Observation("", invocation: NativeInvocation.NotStarted),
            "incomplete" => Observation("git|1.2.0", exitCode: null),
            "malformed" => Observation("git|1.2"),
            "duplicate" => Observation("git|1.2.0\ngit|1.2.0"),
            _ => throw new AssertFailedException(failure),
        };
        Assert.IsNull(ChocolateyStageCMapping.AfterList(Baseline, ChocolateyQueryParser.ParseListExact(Observation("git|1.2.0"), Id)),
            "a matching list continues to outdated");
        Refused(Map(list, null), ChocolateyCandidateRefusal.PreflightQueryFailed, "1.2.0", null, overflow: false);
    }

    [TestMethod]
    public void Row2AnOutputOrRecordOverflowIsPreflightQueryFailedWithOutputLimitExceeded()
    {
        Refused(Map(Observation("git|1.2.0", overflow: true), null), ChocolateyCandidateRefusal.PreflightQueryFailed, "1.2.0", null, overflow: true);
        string records = string.Join('\n', Enumerable.Range(0, 1025).Select(index => $"p{index}|1.0.0|2.0.0|false"));
        Refused(Map(Observation("git|1.2.0"), Observation(records)), ChocolateyCandidateRefusal.PreflightQueryFailed, "1.2.0", null, overflow: true);
        Refused(Map(Observation("git|1.2.0"), Observation("git|1.2.0|1.3.0|false", overflow: true)), ChocolateyCandidateRefusal.PreflightQueryFailed,
            "1.2.0", null, overflow: true);
    }

    [TestMethod]
    [DataRow("exit")]
    [DataRow("malformed")]
    [DataRow("duplicate")]
    public void Row2ASettledOutdatedFailureIsPreflightQueryFailedWithTheListValue(string failure)
    {
        NativeProcessObservation outdated = failure switch
        {
            "exit" => Observation("git|1.2.0|1.3.0|false", exitCode: 1),
            "malformed" => Observation("git|1.2.0|1.3.0"),
            _ => Observation("git|1.2.0|1.3.0|false\ngit|1.2.0|1.3.0|false"),
        };
        Refused(Map(Observation("git|1.2.0"), outdated), ChocolateyCandidateRefusal.PreflightQueryFailed, "1.2.0", null, overflow: false);
    }

    [TestMethod]
    public void Row3AWellFormedListValueThatDiffersFromStageBIsInstalledMismatchWithTheStageCValue()
    {
        // N3: the stage-C value is reported (it differs from the frozen value, so P2 holds); outdated is not run.
        ChocolateyCandidateValues result = ChocolateyStageCMapping.AfterList(Baseline, ChocolateyQueryParser.ParseListExact(Observation("git|1.1.0"), Id))
            ?? throw new AssertFailedException("a differing list value is terminal");
        Refused(result, ChocolateyCandidateRefusal.InstalledMismatch, "1.1.0", null, overflow: false);
        Refused(Map(Observation("git|1.3.0"), Observation("git|1.3.0|1.4.0|false")), ChocolateyCandidateRefusal.InstalledMismatch, "1.3.0", null, overflow: false);
    }

    [TestMethod]
    public void Row3AWellFormedListWithNoRecordIsInstalledMismatchWithInstalledNull()
    {
        // N3 review L1: stage C observed the package gone; report that (null), not the stage-B value.
        Refused(Map(Observation(""), null), ChocolateyCandidateRefusal.InstalledMismatch, null, null, overflow: false);
    }

    [TestMethod]
    public void ABaselineWhoseStageBFingerprintDiffersFromTheExpectedOneIsRefused()
    {
        // N3 review L3: P4 needs field 18 (the stage-B fingerprint) equal to the frozen operation's F.
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyStageCBaseline(Id, "1.2.0", Fingerprint, new string('A', 64), "1.2.0", "1.3.0"));
    }

    [TestMethod]
    public void EverySettledStageCRefusalPassesTheDesktopStructuralAndOperationBoundPredicates()
    {
        // N3 review L4: the refusals the mapping produces must settle at the desktop (S1–S13, P1–P6), never become Uncertain.
        (NativeProcessObservation List, NativeProcessObservation? Outdated)[] cases =
        [
            (Observation("git|1.1.0"), null),                                         // InstalledMismatch, stage-C value
            (Observation(""), null),                                                  // InstalledMismatch, null
            (Observation("git|1.2.0", exitCode: 1), null),                           // PreflightQueryFailed
            (Observation("git|1.2.0", overflow: true), null),                        // PreflightQueryFailed + outputLimitExceeded
            (Observation("git|1.2.0"), Observation("git|1.1.0|1.3.0|false")),       // rev-4 M2
            (Observation("git|1.2.0"), Observation("")),                             // CandidateMismatch, none
            (Observation("git|1.2.0"), Observation("git|1.2.0|1.4.0|false")),       // CandidateMismatch, moved on
            (Observation("git|1.2.0"), Observation("git|1.2.0|1.3.0|true")),        // CandidateMismatch, pinned
        ];
        foreach ((NativeProcessObservation list, NativeProcessObservation? outdated) in cases)
        {
            ChocolateyCandidateValues.Refused refused = Map(list, outdated) as ChocolateyCandidateValues.Refused
                ?? throw new AssertFailedException("expected a settled refusal");
            PCSteward.Execution.ReviewedHelperFacts reviewed = new(PCSteward.Execution.ReviewedHelperQualification.Refused, refused.Reason switch
            {
                ChocolateyCandidateRefusal.InstalledMismatch => PCSteward.Execution.ReviewedHelperRefusalReason.InstalledMismatch,
                ChocolateyCandidateRefusal.PreflightQueryFailed => PCSteward.Execution.ReviewedHelperRefusalReason.PreflightQueryFailed,
                ChocolateyCandidateRefusal.CandidateMismatch => PCSteward.Execution.ReviewedHelperRefusalReason.CandidateMismatch,
                _ => throw new AssertFailedException(refused.Reason.ToString()),
            }, refused.Installed, refused.Candidate, refused.Fingerprint);
            PCSteward.Execution.UpdateWireFacts facts = Protocol2InterpreterFixture.NotStarted(outputLimit: refused.OutputLimitExceeded);
            string description = $"{refused.Reason} installed={refused.Installed} candidate={refused.Candidate}";
            Assert.AreEqual(0, PCSteward.Execution.ReviewedUpdateWireCodec.StructuralViolations(facts, reviewed).Count, description);
            Assert.AreEqual(0, PCSteward.Execution.ReviewedUpdateProtocol2Interpreter.OperationBoundViolations("1.2.0", "1.3.0", Fingerprint, reviewed).Count,
                description);
        }
    }

    [TestMethod]
    public void Row4AnOutdatedInstalledFieldThatDiffersFromTheListValueIsPreflightQueryFailedWithNoCandidate()
    {
        // rev-4 M2: never InstalledMismatch with the list value (it equals the frozen value, which would violate P2).
        Refused(Map(Observation("git|1.2.0"), Observation("git|1.1.0|1.3.0|false")), ChocolateyCandidateRefusal.PreflightQueryFailed, "1.2.0", null, overflow: false);
        // It precedes the candidate rows: a pinned or moved-on candidate does not mask the disagreement.
        Refused(Map(Observation("git|1.2.0"), Observation("git|1.1.0|1.4.0|true")), ChocolateyCandidateRefusal.PreflightQueryFailed, "1.2.0", null, overflow: false);
    }

    [TestMethod]
    [DataRow("other|1.0.0|2.0.0|false", DisplayName = "target absent")]
    [DataRow("", DisplayName = "no outdated packages")]
    [DataRow("git|1.2.0|1.3.0|true", DisplayName = "pinned")]
    [DataRow("git|1.2.0|1.3.0-beta|false", DisplayName = "prerelease")]
    [DataRow("git|1.2.0|1.2.0|false", DisplayName = "not newer (equal)")]
    [DataRow("git|1.2.0|1.1.0|false", DisplayName = "not newer (older)")]
    public void Row5NoUniqueUnpinnedStableStrictlyNewerCandidateIsCandidateMismatchWithNoCandidate(string outdated)
    {
        // N1: field 16 is the stage-C list value and field 18 the stage-B fingerprint, so P4 holds and the refusal settles.
        Refused(Map(Observation("git|1.2.0"), Observation(outdated)), ChocolateyCandidateRefusal.CandidateMismatch, "1.2.0", null, overflow: false);
    }

    [TestMethod]
    public void Row5AMovedOnCandidateIsCandidateMismatchWithThatCandidateNeverARetarget()
    {
        Refused(Map(Observation("git|1.2.0"), Observation("git|1.2.0|1.4.0|false")), ChocolateyCandidateRefusal.CandidateMismatch, "1.2.0", "1.4.0", overflow: false);
        Refused(Map(Observation("git|1.2.0"), Observation("git|1.2.0|1.2.1|false")), ChocolateyCandidateRefusal.CandidateMismatch, "1.2.0", "1.2.1", overflow: false);
    }

    [TestMethod]
    public void Row6TheFrozenCandidateMatchesAmongOtherRecordsCaseInsensitively()
    {
        ChocolateyCandidateValues result = Map(Observation("Git|1.2.0"), Observation("7zip|23.1.0|24.8.0|true\nGIT|1.2.0|1.3.0|false\npython3|3.12.1|3.13.0|false"));
        Assert.AreEqual(new ChocolateyCandidateValues.Matched("1.3.0"), result);
    }

    [TestMethod]
    public void TheMapNeedsTheOutdatedQueryOnlyWhenTheListContinues()
    {
        ChocolateyQueryOutcome<ChocolateyInstalledRecord> list = ChocolateyQueryParser.ParseListExact(Observation("git|1.2.0"), Id);
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyStageCMapping.Map(Baseline, list, null));
        Assert.IsInstanceOfType<ChocolateyCandidateValues.Refused>(Map(Observation("git|1.2.0", exitCode: 1), null));
    }

    // ---- Value-record and baseline rules ------------------------------------------------------------------------------

    [TestMethod]
    public void TheValueRecordsEnforceTheStageCPredicates()
    {
        // S7: no candidate with PreflightQueryFailed or InstalledMismatch, or without an installed value.
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.PreflightQueryFailed, "1.2.0", "1.3.0", Fingerprint, false));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.InstalledMismatch, "1.1.0", "1.3.0", Fingerprint, false));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.CandidateMismatch, null, "1.3.0", Fingerprint, false));
        // S10: outputLimitExceeded only with PreflightQueryFailed.
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.CandidateMismatch, "1.2.0", null, Fingerprint, true));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.InstalledMismatch, "1.1.0", null, Fingerprint, true));
        // N1/P4: CandidateMismatch carries the installed value and fingerprint.
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.CandidateMismatch, "1.2.0", null, null, false));
        // S12 formats.
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.PreflightQueryFailed, "1.2", null, Fingerprint, false));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.PreflightQueryFailed, "1.2.0", null, Fingerprint.ToLowerInvariant(), false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ChocolateyCandidateValues.Refused((ChocolateyCandidateRefusal)9, null, null, null, false));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Matched("1.3"));
        // S11: Undetermined needs an interrupt or an unconfirmed settlement.
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Undetermined(false, false, true, false));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Undetermined(false, false, true, true));
        _ = new ChocolateyCandidateValues.Undetermined(false, false, false, false);
        _ = new ChocolateyCandidateValues.OutOfOrder();
    }

    [TestMethod]
    public void TheBaselineRequiresAPassedStageBAndAStableStrictlyNewerFrozenCandidate()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyStageCBaseline(Id, "1.1.0", Fingerprint, Fingerprint, "1.2.0", "1.3.0"));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyStageCBaseline(Id, "1.2.0", Fingerprint, Fingerprint, "1.2.0", "1.2.0"));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyStageCBaseline(Id, "1.2.0", Fingerprint, Fingerprint, "1.2.0", "1.3.0-beta"));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyStageCBaseline(Id, "1.2.0", Fingerprint, Fingerprint, "1.2.0", "1.3"));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyStageCBaseline(Id, "1.2.0", "ABC", Fingerprint, "1.2.0", "1.3.0"));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyStageCBaseline("chocolatey", "1.2.0", Fingerprint, Fingerprint, "1.2.0", "1.3.0"));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyStageCBaseline("a..b", "1.2.0", Fingerprint, Fingerprint, "1.2.0", "1.3.0"));
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------------

    private static ChocolateyQueryOutcome<ChocolateyInstalledRecord> List(NativeProcessObservation observation) =>
        ChocolateyQueryParser.ParseListExact(observation, Id);

    private static ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord> Outdated(NativeProcessObservation observation) =>
        ChocolateyQueryParser.ParseOutdated(observation);

    private static ChocolateyCandidateValues Map(NativeProcessObservation list, NativeProcessObservation? outdated) =>
        ChocolateyStageCMapping.Map(Baseline, List(list), outdated is null ? null : Outdated(outdated));

    private static void Undetermined(ChocolateyCandidateValues result, bool cancelled, bool timedOut, bool settled, bool overflow = false)
    {
        ChocolateyCandidateValues.Undetermined undetermined = result as ChocolateyCandidateValues.Undetermined
            ?? throw new AssertFailedException($"expected Undetermined, got {result}");
        Assert.AreEqual(cancelled, undetermined.Cancelled);
        Assert.AreEqual(timedOut, undetermined.TimedOut);
        Assert.AreEqual(settled, undetermined.AggregateSettlementConfirmed);
        Assert.AreEqual(overflow, undetermined.OutputLimitExceeded);
    }

    private static void Refused(ChocolateyCandidateValues result, ChocolateyCandidateRefusal reason, string? installed, string? candidate, bool overflow)
    {
        ChocolateyCandidateValues.Refused refused = result as ChocolateyCandidateValues.Refused
            ?? throw new AssertFailedException($"expected Refused({reason}), got {result}");
        Assert.AreEqual(reason, refused.Reason);
        Assert.AreEqual(installed, refused.Installed);
        Assert.AreEqual(candidate, refused.Candidate);
        Assert.AreEqual(Fingerprint, refused.Fingerprint, "field 18 is the stage-B fingerprint for every stage-C refusal (N1)");
        Assert.AreEqual(overflow, refused.OutputLimitExceeded);
    }

    /// <summary>A fake launcher observation: by default a settled, started launch that exited 0 with both streams at EOF.</summary>
    private static NativeProcessObservation Observation(string standardOutput, int? exitCode = 0, bool cancelled = false, bool timedOut = false,
        bool settled = true, bool overflow = false, bool eof = true, NativeInvocation invocation = NativeInvocation.Started, string standardError = "")
    {
        bool started = invocation == NativeInvocation.Started;
        return new NativeProcessObservation(invocation, started ? exitCode : null, DirectExitObserved: started && exitCode.HasValue,
            OwnedIoCompleted: started && eof, PipeEofObserved: started && eof, TerminationFailed: !settled, AggregateSettled: settled,
            cancelled, timedOut, overflow, LingeringTerminated: false, standardOutput, standardError);
    }
}
