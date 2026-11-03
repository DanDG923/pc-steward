using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §4 (slice S2): the closed probe response codec. Goldens
/// (§4.4: one minimal frame per Q13 row, the passing frame on the one- and two-token allow-list, the largest valid frame, a
/// Refused frame per <c>failedCheck</c> and per <c>failedAccess</c> value), round-trips, one negative test per Q rule, property
/// mutations, the wire-name pins (§4.4 L6), the allow-list bound (§4.3 L8) and the unchanged update frames.
/// </summary>
[TestClass]
public sealed class QualificationProbeWireCodecTests
{
    private const string Nonce = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string Token = "Update/ChocolateyMachineRuntimeLimitsV3";
    private const string ClientToken = "Update/ChocolateyClientSelfUpdateV1";
    private const string Prefix = "{\"protocolVersion\":\"2\",\"kind\":\"qualify-chocolatey-helper\",\"nonce\":\"" + Nonce + "\",";
    private const string G1Supported = "\"g1Status\":{\"" + Token + "\":\"Supported\"},";
    private static readonly JsonWriterOptions RawWriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>§4.1 field 3, ordered (review L6): the enum name is the wire value.</summary>
    private static readonly string[] TokenVerdictWireNames =
    [
        "Accepted", "FactsMalformed", "PeerNotLimited", "PeerSidMismatch", "EvaluationSidMismatch", "AuthenticationIdMismatch",
        "EvaluationNotLimited", "EvaluationNotMediumIntegrity", "EvaluationRestricted", "EvaluationAppContainer",
        "EvaluationAdministratorsEnabled", "EvaluationPrivilegeNotAllowed", "PeerProcessIdMismatch", "PeerExited", "PeerTokenUnreadable",
        "NoLinkedToken", "EvaluationTokenUnreadable", "EvaluationNotIdentificationLevel",
    ];

    private static readonly string[] FailedCheckNames = ["None", "StepRule", "InstalledVersion", "SetupAccessGuard", "ReleaseLeftover"];
    private static readonly string[] FailedAccessNames = ["None", "Granted", "Unreadable", "Other"];
    private static readonly string[] ObservationNames = ["NotRun", "Observed", "Refused", "TimedOut"];
    private static readonly string[] RefusalReasonNames = ["None", "MachineInputsUnsupported", "SourceNotCommunity", "InstalledMismatch"];
    private static readonly string[] RefusalDetailNames = ["None", "UnlistedReleaseLeftover", "SetupAccessOverride"];
    private static readonly string[] CapabilityStatusNames = ["Supported", "Absent", "Incomplete", "Unreadable", "Mismatch", "NotEvaluated"];
    private static readonly string[] DiagnosticTokens =
        ["probe-token-refused", "probe-capability-refused", "probe-observed", "probe-observation-refused", "probe-observation-timed-out"];
    private static readonly string[] Q10Only = ["Q10"];

    // ---- Goldens -------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void PassingFrameGoldenOnTheOneTokenAllowListIs520Bytes()
    {
        byte[] frame = QualificationProbeWireCodec.CreateProbeResponse(Nonce, Passing());
        string expected = Prefix + "\"tokenVerdict\":\"Accepted\"," + G1Supported + "\"observation\":\"Observed\",\"failedStep\":null," +
            "\"failedCheck\":\"None\",\"failedAccess\":\"None\",\"refusalReason\":\"None\",\"refusalDetail\":\"None\",\"nodesEvaluated\":123456," +
            "\"ancestorsEvaluated\":12345,\"observedClientVersion\":\"2.7.5\",\"fingerprintPresent\":true,\"fingerprintLength\":64," +
            "\"diagnostic\":\"probe-observed\"}";
        Assert.AreEqual(expected, Encoding.UTF8.GetString(frame));
        Assert.AreEqual(520, frame.Length, "spec §4.3 passing frame, one-token allow-list");
        AssertRoundTrip(frame, Passing());
    }

    [TestMethod]
    public void PassingFrameGoldenOnTheTwoTokenAllowListIs570Bytes()
    {
        string client = DeploymentCapabilityPolicy.OperationToken(UpdateRouteContract.ChocolateyClientSelfUpdateV1);
        Assert.AreEqual(ClientToken, client);
        QualificationProbeContract two = QualificationProbeContract.CreateForTests([ClientToken, Token], ["2.7.5"]);
        QualificationProbeFacts facts = Passing() with { G1Status = G1(two, QualificationProbeCapabilityStatus.Supported) };
        byte[] frame = QualificationProbeWireCodec.CreateProbeResponse(Nonce, facts, two);
        string expected = Prefix + "\"tokenVerdict\":\"Accepted\",\"g1Status\":{\"" + ClientToken + "\":\"Supported\",\"" + Token +
            "\":\"Supported\"},\"observation\":\"Observed\",\"failedStep\":null,\"failedCheck\":\"None\",\"failedAccess\":\"None\"," +
            "\"refusalReason\":\"None\",\"refusalDetail\":\"None\",\"nodesEvaluated\":123456,\"ancestorsEvaluated\":12345," +
            "\"observedClientVersion\":\"2.7.5\",\"fingerprintPresent\":true,\"fingerprintLength\":64,\"diagnostic\":\"probe-observed\"}";
        Assert.AreEqual(expected, Encoding.UTF8.GetString(frame));
        Assert.AreEqual(570, frame.Length, "spec §4.3 passing frame, two-token allow-list");
        Assert.IsTrue(QualificationProbeWireCodec.TryReadProbeResponse(frame, Nonce, two, out QualificationProbeFacts? read));
        Assert.AreEqual(facts, read);
        // Q4: a two-token frame is not readable over the compiled one-token contract, and vice versa.
        Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(frame, Nonce, out _));
        Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(QualificationProbeWireCodec.CreateProbeResponse(Nonce, Passing()), Nonce, two, out _));
    }

    [TestMethod]
    [DataRow("token", "\"tokenVerdict\":\"NoLinkedToken\",\"g1Status\":{\"" + Token + "\":\"NotEvaluated\"},\"observation\":\"NotRun\",\"failedStep\":null,\"failedCheck\":\"None\",\"failedAccess\":\"None\",\"refusalReason\":\"None\",\"refusalDetail\":\"None\",\"nodesEvaluated\":0,\"ancestorsEvaluated\":0,\"observedClientVersion\":null,\"fingerprintPresent\":false,\"fingerprintLength\":0,\"diagnostic\":\"probe-token-refused\"}", 519)]
    [DataRow("capability", "\"tokenVerdict\":\"Accepted\",\"g1Status\":{\"" + Token + "\":\"Absent\"},\"observation\":\"NotRun\",\"failedStep\":null,\"failedCheck\":\"None\",\"failedAccess\":\"None\",\"refusalReason\":\"None\",\"refusalDetail\":\"None\",\"nodesEvaluated\":0,\"ancestorsEvaluated\":0,\"observedClientVersion\":null,\"fingerprintPresent\":false,\"fingerprintLength\":0,\"diagnostic\":\"probe-capability-refused\"}", 513)]
    [DataRow("observed", "\"tokenVerdict\":\"Accepted\"," + G1Supported + "\"observation\":\"Observed\",\"failedStep\":null,\"failedCheck\":\"None\",\"failedAccess\":\"None\",\"refusalReason\":\"None\",\"refusalDetail\":\"None\",\"nodesEvaluated\":1,\"ancestorsEvaluated\":1,\"observedClientVersion\":\"2.7.5\",\"fingerprintPresent\":true,\"fingerprintLength\":64,\"diagnostic\":\"probe-observed\"}", 511)]
    [DataRow("refused", "\"tokenVerdict\":\"Accepted\"," + G1Supported + "\"observation\":\"Refused\",\"failedStep\":1,\"failedCheck\":\"StepRule\",\"failedAccess\":\"Other\",\"refusalReason\":\"MachineInputsUnsupported\",\"refusalDetail\":\"None\",\"nodesEvaluated\":0,\"ancestorsEvaluated\":0,\"observedClientVersion\":null,\"fingerprintPresent\":false,\"fingerprintLength\":0,\"diagnostic\":\"probe-observation-refused\"}", 540)]
    [DataRow("timedOut", "\"tokenVerdict\":\"Accepted\"," + G1Supported + "\"observation\":\"TimedOut\",\"failedStep\":null,\"failedCheck\":\"None\",\"failedAccess\":\"None\",\"refusalReason\":\"None\",\"refusalDetail\":\"None\",\"nodesEvaluated\":0,\"ancestorsEvaluated\":0,\"observedClientVersion\":null,\"fingerprintPresent\":false,\"fingerprintLength\":0,\"diagnostic\":\"probe-observation-timed-out\"}", 521)]
    public void MinimalFrameGoldenPerDiagnosticRow(string row, string tail, int length)
    {
        QualificationProbeFacts facts = row switch
        {
            "token" => TokenRefused(),
            "capability" => CapabilityRefused(QualificationProbeCapabilityStatus.Absent),
            "observed" => Passing(1, 1),
            "refused" => Refused(1, QualificationProbeFailedCheck.StepRule, QualificationProbeFailedAccess.Other,
                QualificationProbeRefusalReason.MachineInputsUnsupported, QualificationProbeRefusalDetail.None),
            "timedOut" => TimedOut(0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };
        byte[] frame = QualificationProbeWireCodec.CreateProbeResponse(Nonce, facts);
        Assert.AreEqual(Prefix + tail, Encoding.UTF8.GetString(frame), row);
        Assert.AreEqual(length, frame.Length, row);
        AssertRoundTrip(frame, facts);
    }

    [TestMethod]
    [DataRow("StepRule/Granted", 4, "StepRule", "Granted", "MachineInputsUnsupported", "None", null)]
    [DataRow("StepRule/Unreadable", 5, "StepRule", "Unreadable", "MachineInputsUnsupported", "None", null)]
    [DataRow("StepRule/Other", 9, "StepRule", "Other", "MachineInputsUnsupported", "None", null)]
    [DataRow("StepRule/Source", 8, "StepRule", "Other", "SourceNotCommunity", "None", null)]
    [DataRow("SetupAccessGuard", 7, "SetupAccessGuard", "Other", "MachineInputsUnsupported", "SetupAccessOverride", null)]
    [DataRow("ReleaseLeftover", 7, "ReleaseLeftover", "Other", "MachineInputsUnsupported", "UnlistedReleaseLeftover", null)]
    [DataRow("InstalledVersion", 8, "InstalledVersion", "Other", "InstalledMismatch", "None", "2.7.4")]
    public void RefusedFrameGoldenPerFailedCheckAndFailedAccessValue(string row, int step, string check, string access, string reason,
        string detail, string? version)
    {
        // InstalledMismatch needs an older listed release (Q10); with today's one-entry list it cannot occur (checked below).
        QualificationProbeContract contract = version is null ? QualificationProbeContract.Compiled
            : QualificationProbeContract.CreateForTests([Token], ["2.7.4", "2.7.5"]);
        QualificationProbeFacts facts = Refused(step, Enum.Parse<QualificationProbeFailedCheck>(check), Enum.Parse<QualificationProbeFailedAccess>(access),
            Enum.Parse<QualificationProbeRefusalReason>(reason), Enum.Parse<QualificationProbeRefusalDetail>(detail), 17, 3) with { ObservedClientVersion = version };
        byte[] frame = QualificationProbeWireCodec.CreateProbeResponse(Nonce, facts, contract);
        string expected = Prefix + "\"tokenVerdict\":\"Accepted\"," + G1Supported + "\"observation\":\"Refused\",\"failedStep\":" + step +
            ",\"failedCheck\":\"" + check + "\",\"failedAccess\":\"" + access + "\",\"refusalReason\":\"" + reason + "\",\"refusalDetail\":\"" + detail +
            "\",\"nodesEvaluated\":17,\"ancestorsEvaluated\":3,\"observedClientVersion\":" + (version is null ? "null" : "\"" + version + "\"") +
            ",\"fingerprintPresent\":false,\"fingerprintLength\":0,\"diagnostic\":\"probe-observation-refused\"}";
        Assert.AreEqual(expected, Encoding.UTF8.GetString(frame), row);
        Assert.IsTrue(QualificationProbeWireCodec.TryReadProbeResponse(frame, Nonce, contract, out QualificationProbeFacts? read), row);
        Assert.AreEqual(facts, read, row);
        if (version is not null)
        {
            Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(frame, Nonce, out _), "InstalledMismatch cannot pass the compiled one-entry list");
            Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeWireCodec.CreateProbeResponse(Nonce, facts));
        }
    }

    [TestMethod]
    public void LargestValidFrameOnTheCompiledContractIsFoundExhaustivelyAndPinned()
    {
        QualificationProbeContract contract = QualificationProbeContract.Compiled;
        int valid = 0;
        byte[] largest = [];
        foreach (QualificationProbeFacts facts in EveryValueCombination(contract))
        {
            if (QualificationProbeWireCodec.RuleViolations(Nonce, facts, contract).Count != 0) continue;
            valid++;
            byte[] frame = QualificationProbeWireCodec.CreateProbeResponse(Nonce, facts);
            AssertRoundTrip(frame, facts);
            if (frame.Length > largest.Length) largest = frame;
        }
        Assert.AreEqual(44, valid, "17 token refusals, 4 capability refusals, 1 Observed, 1 TimedOut and 21 refusals");
        string expected = Prefix + "\"tokenVerdict\":\"Accepted\"," + G1Supported + "\"observation\":\"Refused\",\"failedStep\":7," +
            "\"failedCheck\":\"ReleaseLeftover\",\"failedAccess\":\"Other\",\"refusalReason\":\"MachineInputsUnsupported\"," +
            "\"refusalDetail\":\"UnlistedReleaseLeftover\",\"nodesEvaluated\":1048576,\"ancestorsEvaluated\":65536," +
            "\"observedClientVersion\":null,\"fingerprintPresent\":false,\"fingerprintLength\":0,\"diagnostic\":\"probe-observation-refused\"}";
        Assert.AreEqual(expected, Encoding.UTF8.GetString(largest));
        Assert.AreEqual(576, largest.Length);
        Assert.IsLessThanOrEqualTo(QualificationProbeWireCodec.MaximumFrameBytes, largest.Length);
    }

    [TestMethod]
    public void TheSpecUpperBoundIs1338BytesAndEveryValidEightKeyFrameStaysBelowIt()
    {
        // §4.3: maxima that no valid frame holds together (8 keys × 64 B NotEvaluated, the 32-B verdict, failedStep 9, the longest
        // check, access, reason and detail, 7- and 5-digit counts, a 128-B version, "false" with length 64, the longest diagnostic).
        string[] keys = [.. Enumerable.Range(0, 8).Select(index => "Update/" + new string('X', 56) + index)];
        Assert.IsTrue(keys.All(key => Encoding.UTF8.GetByteCount(key) == 64));
        string version128 = "1.0.0-" + new string('a', 122);
        string bound = Raw(Nonce, new QualificationProbeFacts(QualificationProbeTokenVerdict.EvaluationNotIdentificationLevel,
            [.. keys.Select(key => new QualificationProbeCapabilityEntry(key, QualificationProbeCapabilityStatus.NotEvaluated))],
            QualificationProbeObservation.Observed, 9, QualificationProbeFailedCheck.SetupAccessGuard, QualificationProbeFailedAccess.Unreadable,
            QualificationProbeRefusalReason.MachineInputsUnsupported, QualificationProbeRefusalDetail.UnlistedReleaseLeftover,
            QualificationProbeWireCodec.MaximumNodesEvaluated, QualificationProbeWireCodec.MaximumAncestorsEvaluated, version128, false, 64,
            QualificationProbeWireCodec.ObservationTimedOutDiagnostic));
        Assert.AreEqual(1338, Encoding.UTF8.GetByteCount(bound));
        Assert.IsLessThanOrEqualTo(QualificationProbeWireCodec.MaximumFrameBytes, 1338);
        Assert.AreEqual(2048, QualificationProbeWireCodec.MaximumFrameBytes);

        // The largest valid eight-key frames (an InstalledMismatch with a 128-B older release; a token refusal) are below the bound.
        QualificationProbeContract eight = QualificationProbeContract.CreateForTests(keys, [version128, "9.0.0"]);
        QualificationProbeFacts mismatch = Refused(8, QualificationProbeFailedCheck.InstalledVersion, QualificationProbeFailedAccess.Other,
            QualificationProbeRefusalReason.InstalledMismatch, QualificationProbeRefusalDetail.None, QualificationProbeWireCodec.MaximumNodesEvaluated,
            QualificationProbeWireCodec.MaximumAncestorsEvaluated) with { G1Status = G1(eight, QualificationProbeCapabilityStatus.Supported), ObservedClientVersion = version128 };
        QualificationProbeFacts token = TokenRefused(QualificationProbeTokenVerdict.EvaluationNotIdentificationLevel) with
        {
            G1Status = G1(eight, QualificationProbeCapabilityStatus.NotEvaluated)
        };
        foreach (QualificationProbeFacts facts in new[] { mismatch, token })
        {
            byte[] frame = QualificationProbeWireCodec.CreateProbeResponse(Nonce, facts, eight);
            Assert.IsLessThan(1338, frame.Length);
            Assert.IsTrue(QualificationProbeWireCodec.TryReadProbeResponse(frame, Nonce, eight, out QualificationProbeFacts? read));
            Assert.AreEqual(facts, read);
        }
    }

    // ---- Q rules --------------------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("Q3", "verdictUndefined")]
    [DataRow("Q3", "statusUndefined")]
    [DataRow("Q3", "observationUndefined")]
    [DataRow("Q3", "checkUndefined")]
    [DataRow("Q3", "accessUndefined")]
    [DataRow("Q3", "reasonUndefined")]
    [DataRow("Q3", "detailUndefined")]
    [DataRow("Q4", "noKeys")]
    [DataRow("Q4", "otherKey")]
    [DataRow("Q4", "extraKey")]
    [DataRow("Q4", "nullList")]
    [DataRow("Q5", "refusedVerdictEvaluated")]
    [DataRow("Q5", "refusedVerdictObserved")]
    [DataRow("Q5", "acceptedNotEvaluated")]
    [DataRow("Q6", "observedWithAbsent")]
    [DataRow("Q6", "notRunAllSupported")]
    [DataRow("Q7", "refusedWithoutStep")]
    [DataRow("Q7", "observedWithReason")]
    [DataRow("Q7", "refusedWithoutAccess")]
    [DataRow("Q7", "timedOutWithStep")]
    [DataRow("Q8", "sourceAtStep7")]
    [DataRow("Q8", "sourceWithGuard")]
    [DataRow("Q8", "mismatchStepRule")]
    [DataRow("Q8", "machineInputsAtStep8")]
    [DataRow("Q8", "machineInputsInstalledVersion")]
    [DataRow("Q8", "machineInputsAtStep10")]
    [DataRow("Q8", "machineInputsAtStep0")]
    [DataRow("Q9", "guardWithoutDetail")]
    [DataRow("Q9", "leftoverDetailWithGuard")]
    [DataRow("Q9", "guardAtStep6")]
    [DataRow("Q9", "detailWithStepRule")]
    [DataRow("Q10", "observedOtherVersion")]
    [DataRow("Q10", "observedNoVersion")]
    [DataRow("Q10", "refusedWithVersion")]
    [DataRow("Q10", "mismatchNewestVersion")]
    [DataRow("Q10", "timedOutWithVersion")]
    [DataRow("Q11", "observedNoFingerprint")]
    [DataRow("Q11", "observedLength32")]
    [DataRow("Q11", "refusedFingerprint")]
    [DataRow("Q11", "absentLength64")]
    [DataRow("Q12", "notRunCounts")]
    [DataRow("Q12", "observedZeroNodes")]
    [DataRow("Q12", "observedZeroAncestors")]
    [DataRow("Q12", "nodesOverMaximum")]
    [DataRow("Q12", "ancestorsOverMaximum")]
    [DataRow("Q12", "negativeNodes")]
    [DataRow("Q13", "observedWrongToken")]
    [DataRow("Q13", "tokenRowCapabilityToken")]
    [DataRow("Q13", "capabilityRowTokenToken")]
    [DataRow("Q13", "freeText")]
    [DataRow("Q15", "grantedAtStep2")]
    [DataRow("Q15", "unreadableAtStep9")]
    [DataRow("Q15", "grantedWithSource")]
    [DataRow("Q15", "unreadableWithGuard")]
    [DataRow("Q16", "timedOutWithCheck")]
    [DataRow("Q16", "timedOutWithDetail")]
    public void EachQRuleViolationIsReportedThrowsOnCreateAndIsRejectedOnRead(string rule, string mutation)
    {
        QualificationProbeContract two = QualificationProbeContract.CreateForTests([Token], ["2.7.4", "2.7.5"]);
        QualificationProbeContract contract = mutation == "mismatchNewestVersion" ? two : QualificationProbeContract.Compiled;
        QualificationProbeFacts refused = Refused(1, QualificationProbeFailedCheck.StepRule, QualificationProbeFailedAccess.Granted,
            QualificationProbeRefusalReason.MachineInputsUnsupported, QualificationProbeRefusalDetail.None);
        QualificationProbeFacts guard = Refused(7, QualificationProbeFailedCheck.SetupAccessGuard, QualificationProbeFailedAccess.Other,
            QualificationProbeRefusalReason.MachineInputsUnsupported, QualificationProbeRefusalDetail.SetupAccessOverride);
        QualificationProbeFacts source = Refused(8, QualificationProbeFailedCheck.StepRule, QualificationProbeFailedAccess.Other,
            QualificationProbeRefusalReason.SourceNotCommunity, QualificationProbeRefusalDetail.None);
        QualificationProbeFacts mismatch = Refused(8, QualificationProbeFailedCheck.InstalledVersion, QualificationProbeFailedAccess.Other,
            QualificationProbeRefusalReason.InstalledMismatch, QualificationProbeRefusalDetail.None) with { ObservedClientVersion = "2.7.4" };
        QualificationProbeFacts facts = mutation switch
        {
            "verdictUndefined" => TokenRefused() with { Verdict = (QualificationProbeTokenVerdict)99 },
            "statusUndefined" => CapabilityRefused((QualificationProbeCapabilityStatus)99),
            "observationUndefined" => Passing() with { Observation = (QualificationProbeObservation)99 },
            "checkUndefined" => refused with { FailedCheck = (QualificationProbeFailedCheck)99 },
            "accessUndefined" => refused with { FailedAccess = (QualificationProbeFailedAccess)99 },
            "reasonUndefined" => refused with { RefusalReason = (QualificationProbeRefusalReason)99 },
            "detailUndefined" => refused with { RefusalDetail = (QualificationProbeRefusalDetail)99 },
            "noKeys" => Passing() with { G1Status = [] },
            "otherKey" => Passing() with { G1Status = [new(ClientToken, QualificationProbeCapabilityStatus.Supported)] },
            "extraKey" => Passing() with { G1Status = [new(ClientToken, QualificationProbeCapabilityStatus.Supported), new(Token, QualificationProbeCapabilityStatus.Supported)] },
            "nullList" => Passing() with { G1Status = null! },
            "refusedVerdictEvaluated" => TokenRefused() with { G1Status = G1(QualificationProbeContract.Compiled, QualificationProbeCapabilityStatus.Absent) },
            "refusedVerdictObserved" => Passing() with { Verdict = QualificationProbeTokenVerdict.PeerExited },
            "acceptedNotEvaluated" => TokenRefused() with { Verdict = QualificationProbeTokenVerdict.Accepted, Diagnostic = QualificationProbeWireCodec.CapabilityRefusedDiagnostic },
            "observedWithAbsent" => Passing() with { G1Status = G1(QualificationProbeContract.Compiled, QualificationProbeCapabilityStatus.Absent) },
            "notRunAllSupported" => CapabilityRefused(QualificationProbeCapabilityStatus.Supported),
            "refusedWithoutStep" => refused with { FailedStep = null },
            "observedWithReason" => Passing() with { RefusalReason = QualificationProbeRefusalReason.MachineInputsUnsupported },
            "refusedWithoutAccess" => refused with { FailedAccess = QualificationProbeFailedAccess.None },
            "timedOutWithStep" => TimedOut() with { FailedStep = 3 },
            "sourceAtStep7" => source with { FailedStep = 7 },
            "sourceWithGuard" => source with { FailedCheck = QualificationProbeFailedCheck.SetupAccessGuard, RefusalDetail = QualificationProbeRefusalDetail.SetupAccessOverride },
            "mismatchStepRule" => mismatch with { FailedCheck = QualificationProbeFailedCheck.StepRule },
            "machineInputsAtStep8" => refused with { FailedStep = 8, FailedAccess = QualificationProbeFailedAccess.Other },
            "machineInputsInstalledVersion" => refused with { FailedStep = 8, FailedCheck = QualificationProbeFailedCheck.InstalledVersion, FailedAccess = QualificationProbeFailedAccess.Other },
            "machineInputsAtStep10" => refused with { FailedStep = 10, FailedAccess = QualificationProbeFailedAccess.Other },
            "machineInputsAtStep0" => refused with { FailedStep = 0, FailedAccess = QualificationProbeFailedAccess.Other },
            "guardWithoutDetail" => guard with { RefusalDetail = QualificationProbeRefusalDetail.None },
            "leftoverDetailWithGuard" => guard with { RefusalDetail = QualificationProbeRefusalDetail.UnlistedReleaseLeftover },
            "guardAtStep6" => guard with { FailedStep = 6 },
            "detailWithStepRule" => refused with { RefusalDetail = QualificationProbeRefusalDetail.SetupAccessOverride, FailedStep = 7 },
            "observedOtherVersion" => Passing() with { ObservedClientVersion = "2.7.4" },
            "observedNoVersion" => Passing() with { ObservedClientVersion = null },
            "refusedWithVersion" => refused with { ObservedClientVersion = "2.7.5" },
            "mismatchNewestVersion" => mismatch with { ObservedClientVersion = "2.7.5" },
            "timedOutWithVersion" => TimedOut() with { ObservedClientVersion = "2.7.5" },
            "observedNoFingerprint" => Passing() with { FingerprintPresent = false, FingerprintLength = 0 },
            "observedLength32" => Passing() with { FingerprintLength = 32 },
            "refusedFingerprint" => refused with { FingerprintPresent = true, FingerprintLength = 64 },
            "absentLength64" => refused with { FingerprintLength = 64 },
            "notRunCounts" => TokenRefused() with { NodesEvaluated = 1 },
            "observedZeroNodes" => Passing() with { NodesEvaluated = 0 },
            "observedZeroAncestors" => Passing() with { AncestorsEvaluated = 0 },
            "nodesOverMaximum" => Passing() with { NodesEvaluated = QualificationProbeWireCodec.MaximumNodesEvaluated + 1 },
            "ancestorsOverMaximum" => TimedOut() with { AncestorsEvaluated = QualificationProbeWireCodec.MaximumAncestorsEvaluated + 1 },
            "negativeNodes" => refused with { NodesEvaluated = -1 },
            "observedWrongToken" => Passing() with { Diagnostic = QualificationProbeWireCodec.ObservationRefusedDiagnostic },
            "tokenRowCapabilityToken" => TokenRefused() with { Diagnostic = QualificationProbeWireCodec.CapabilityRefusedDiagnostic },
            "capabilityRowTokenToken" => CapabilityRefused(QualificationProbeCapabilityStatus.Mismatch) with { Diagnostic = QualificationProbeWireCodec.TokenRefusedDiagnostic },
            "freeText" => Passing() with { Diagnostic = "observed" },
            "grantedAtStep2" => refused with { FailedStep = 2 },
            "unreadableAtStep9" => refused with { FailedStep = 9, FailedAccess = QualificationProbeFailedAccess.Unreadable },
            "grantedWithSource" => source with { FailedAccess = QualificationProbeFailedAccess.Granted },
            "unreadableWithGuard" => guard with { FailedAccess = QualificationProbeFailedAccess.Unreadable },
            "timedOutWithCheck" => TimedOut() with { FailedCheck = QualificationProbeFailedCheck.StepRule },
            "timedOutWithDetail" => TimedOut() with { RefusalDetail = QualificationProbeRefusalDetail.UnlistedReleaseLeftover },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        IReadOnlyList<string> violations = QualificationProbeWireCodec.RuleViolations(Nonce, facts, contract);
        CollectionAssert.Contains(violations.ToArray(), rule, $"{mutation}: {string.Join(",", violations)}");
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeWireCodec.CreateProbeResponse(Nonce, facts, contract), mutation);
        if (facts.G1Status is not null && !mutation.EndsWith("Undefined", StringComparison.Ordinal))
            Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(Encoding.UTF8.GetBytes(Raw(Nonce, facts)), Nonce, contract, out _), mutation);
    }

    [TestMethod]
    public void EveryValidBaseFactHasNoViolationAndTheRawEncoderMatchesTheCodec()
    {
        QualificationProbeContract two = QualificationProbeContract.CreateForTests([Token], ["2.7.4", "2.7.5"]);
        foreach (QualificationProbeFacts facts in new[] { Passing(), TokenRefused(), CapabilityRefused(QualificationProbeCapabilityStatus.Unreadable), TimedOut() })
        {
            Assert.AreEqual(0, QualificationProbeWireCodec.RuleViolations(Nonce, facts, QualificationProbeContract.Compiled).Count);
            Assert.AreEqual(Raw(Nonce, facts), Encoding.UTF8.GetString(QualificationProbeWireCodec.CreateProbeResponse(Nonce, facts)));
        }
        QualificationProbeFacts mismatch = Refused(8, QualificationProbeFailedCheck.InstalledVersion, QualificationProbeFailedAccess.Other,
            QualificationProbeRefusalReason.InstalledMismatch, QualificationProbeRefusalDetail.None) with { ObservedClientVersion = "2.7.4" };
        Assert.AreEqual(0, QualificationProbeWireCodec.RuleViolations(Nonce, mismatch, two).Count);
        CollectionAssert.AreEqual(Q10Only, QualificationProbeWireCodec.RuleViolations(Nonce, mismatch, QualificationProbeContract.Compiled).ToArray());
        // Q12: saturated counts are valid ("at least this many").
        Assert.AreEqual(0, QualificationProbeWireCodec.RuleViolations(Nonce, TimedOut(QualificationProbeWireCodec.MaximumNodesEvaluated,
            QualificationProbeWireCodec.MaximumAncestorsEvaluated), QualificationProbeContract.Compiled).Count);
    }

    [TestMethod]
    public void Q1FrameFormViolationsAreRejected()
    {
        string frame = Encoding.UTF8.GetString(QualificationProbeWireCodec.CreateProbeResponse(Nonce, Passing()));
        Assert.IsTrue(Read(frame));
        string[] invalid =
        [
            frame + " ", " " + frame, frame + "\n", frame.Replace(",\"kind\"", ", \"kind\"", StringComparison.Ordinal),
            "\uFEFF" + frame,
            frame.Replace("probe-observed", "probe\\u002Dobserved", StringComparison.Ordinal),
            frame.Replace("\"kind\"", "\"\\u006Bind\"", StringComparison.Ordinal),
            frame.Replace(Token, "Update\\/ChocolateyMachineRuntimeLimitsV3", StringComparison.Ordinal),
            frame.Replace("\"nodesEvaluated\":123456", "\"nodesEvaluated\":\"123456\"", StringComparison.Ordinal),
            frame.Replace("\"nodesEvaluated\":123456", "\"nodesEvaluated\":123456.0", StringComparison.Ordinal),
            frame.Replace("\"nodesEvaluated\":123456", "\"nodesEvaluated\":1.23456e5", StringComparison.Ordinal),
            frame.Replace("\"fingerprintLength\":64", "\"fingerprintLength\":064", StringComparison.Ordinal),
            frame.Replace("\"fingerprintLength\":64", "\"fingerprintLength\":+64", StringComparison.Ordinal),
            frame.Replace("\"ancestorsEvaluated\":12345", "\"ancestorsEvaluated\":12345.00", StringComparison.Ordinal),
            frame.Replace("\"fingerprintPresent\":true", "\"fingerprintPresent\":\"true\"", StringComparison.Ordinal),
            frame.Replace("\"fingerprintPresent\":true", "\"fingerprintPresent\":1", StringComparison.Ordinal),
            frame.Replace("\"observedClientVersion\":\"2.7.5\"", "\"observedClientVersion\":2.7", StringComparison.Ordinal),
            frame.Replace("\"failedStep\":null", "\"failedStep\":\"null\"", StringComparison.Ordinal),
            frame.Insert(frame.Length - 1, ","), frame.Insert(1, "/*c*/"), frame.Insert(1, "\"extra\":0,"),
            frame.Replace("\"kind\":\"qualify-chocolatey-helper\",\"nonce\"", "\"nonce\"", StringComparison.Ordinal)
                .Replace("\"tokenVerdict\"", "\"kind\":\"qualify-chocolatey-helper\",\"tokenVerdict\"", StringComparison.Ordinal),
            "[" + frame + "]", "null", "{}", frame[..^1],
        ];
        foreach (string mutated in invalid)
            Assert.IsFalse(Read(mutated), mutated);
        Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse([], Nonce, out _));
        Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse([0xFF, 0xFE], Nonce, out _));
        // A lone surrogate in a property name or value is rejected without throwing.
        Assert.IsFalse(Read("{\"\\uD800\":1}"));
        Assert.IsFalse(Read(frame.Replace("probe-observed", "probe-\\uD800", StringComparison.Ordinal)));
        // Q1 MaxDepth = 2: a nested object inside g1Status is rejected.
        Assert.IsFalse(Read(frame.Replace("\"Supported\"}", "{\"a\":1}}", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Q2FixedLiteralsAndNonceAreEnforced()
    {
        string frame = Encoding.UTF8.GetString(QualificationProbeWireCodec.CreateProbeResponse(Nonce, Passing()));
        foreach (string mutated in new[]
        {
            frame.Replace("\"protocolVersion\":\"2\"", "\"protocolVersion\":\"1\"", StringComparison.Ordinal),
            frame.Replace("\"protocolVersion\":\"2\"", "\"protocolVersion\":\"3\"", StringComparison.Ordinal),
            frame.Replace("\"protocolVersion\":\"2\"", "\"protocolVersion\":2", StringComparison.Ordinal),
            frame.Replace("qualify-chocolatey-helper", "Qualify-chocolatey-helper", StringComparison.Ordinal),
            frame.Replace("qualify-chocolatey-helper", "qualify-chocolatey-helper ", StringComparison.Ordinal),
            frame.Replace("qualify-chocolatey-helper", "update", StringComparison.Ordinal),
            frame.Replace(Nonce, Nonce.ToLowerInvariant(), StringComparison.Ordinal),
            frame.Replace(Nonce, Nonce[..63], StringComparison.Ordinal),
            frame.Replace(Nonce, new string('F', 64), StringComparison.Ordinal),
        })
            Assert.IsFalse(Read(mutated), mutated);
        byte[] bytes = Encoding.UTF8.GetBytes(frame);
        Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(bytes, new string('F', 64), out _));
        Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(bytes, Nonce.ToLowerInvariant(), out _));
        Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(bytes, null!, out _));
        foreach (string? nonce in new[] { null, "", Nonce.ToLowerInvariant(), Nonce[..63], Nonce + "0", Nonce[..63] + "G" })
        {
            CollectionAssert.Contains(QualificationProbeWireCodec.RuleViolations(nonce, Passing(), QualificationProbeContract.Compiled).ToArray(), "Q2");
            Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeWireCodec.CreateProbeResponse(nonce!, Passing()));
        }
    }

    [TestMethod]
    public void Q3EnumsMustBeSpelledExactlyAndCancelledIsNotAnObservation()
    {
        string frame = Encoding.UTF8.GetString(QualificationProbeWireCodec.CreateProbeResponse(Nonce, Refused(4,
            QualificationProbeFailedCheck.StepRule, QualificationProbeFailedAccess.Granted, QualificationProbeRefusalReason.MachineInputsUnsupported,
            QualificationProbeRefusalDetail.None)));
        Assert.IsTrue(Read(frame));
        foreach ((string from, string to) in new[]
        {
            ("\"tokenVerdict\":\"Accepted\"", "\"tokenVerdict\":\"accepted\""), ("\"tokenVerdict\":\"Accepted\"", "\"tokenVerdict\":\"0\""),
            ("\"tokenVerdict\":\"Accepted\"", "\"tokenVerdict\":0"), ("\"tokenVerdict\":\"Accepted\"", "\"tokenVerdict\":\" Accepted\""),
            ("\"observation\":\"Refused\"", "\"observation\":\"Cancelled\""), ("\"observation\":\"Refused\"", "\"observation\":\"refused\""),
            ("\"observation\":\"Refused\"", "\"observation\":\"2\""), ("\"failedCheck\":\"StepRule\"", "\"failedCheck\":\"steprule\""),
            ("\"failedCheck\":\"StepRule\"", "\"failedCheck\":\"1\""), ("\"failedAccess\":\"Granted\"", "\"failedAccess\":\"GRANTED\""),
            ("\"failedAccess\":\"Granted\"", "\"failedAccess\":\"Granted,Other\""), ("\"failedAccess\":\"Granted\"", "\"failedAccess\":\"Denied\""),
            ("\"refusalReason\":\"MachineInputsUnsupported\"", "\"refusalReason\":\"machineInputsUnsupported\""),
            ("\"refusalReason\":\"MachineInputsUnsupported\"", "\"refusalReason\":\"ProfileMismatch\""),
            ("\"refusalReason\":\"MachineInputsUnsupported\"", "\"refusalReason\":\"RequestNotAdmitted\""),
            ("\"refusalDetail\":\"None\"", "\"refusalDetail\":\"none\""), ("\"refusalDetail\":\"None\"", "\"refusalDetail\":\"\""),
            ("\":\"Supported\"}", "\":\"supported\"}"), ("\":\"Supported\"}", "\":\"0\"}"), ("\":\"Supported\"}", "\":0}"),
            ("\":\"Supported\"}", "\":\"Cancelled\"}"),
        })
        {
            Assert.IsTrue(frame.Contains(from, StringComparison.Ordinal), from);
            Assert.IsFalse(Read(frame.Replace(from, to, StringComparison.Ordinal)), to);
        }
        Assert.IsFalse(Enum.GetNames<QualificationProbeObservation>().Contains("Cancelled"));
        Assert.IsFalse(Enum.GetNames<QualificationProbeRefusalReason>().Contains("ProfileMismatch"));
    }

    [TestMethod]
    public void Q4G1KeysAreExactlyTheCompiledTokensOnceEach()
    {
        string frame = Encoding.UTF8.GetString(QualificationProbeWireCodec.CreateProbeResponse(Nonce, Passing()));
        string g1 = "{\"" + Token + "\":\"Supported\"}";
        foreach (string replacement in new[]
        {
            "{}", "{\"" + Token + "\":\"Supported\",\"" + Token + "\":\"Supported\"}", "{\"" + Token.ToLowerInvariant() + "\":\"Supported\"}",
            "{\"" + ClientToken + "\":\"Supported\",\"" + Token + "\":\"Supported\"}", "{\"" + Token + "\":\"Supported\",\"" + ClientToken + "\":\"Supported\"}",
            "{\"Update/ChocolateyMachineRegistrationV3\":\"Supported\"}", "{\"" + Token + " \":\"Supported\"}", "[\"Supported\"]", "\"Supported\"", "null",
        })
            Assert.IsFalse(Read(frame.Replace(g1, replacement, StringComparison.Ordinal)), replacement);

        // Over a two-token contract the keys must be in ordinal order.
        QualificationProbeContract two = QualificationProbeContract.CreateForTests([ClientToken, Token], ["2.7.5"]);
        QualificationProbeFacts reversed = Passing() with
        {
            G1Status = [new(Token, QualificationProbeCapabilityStatus.Supported), new(ClientToken, QualificationProbeCapabilityStatus.Supported)]
        };
        CollectionAssert.Contains(QualificationProbeWireCodec.RuleViolations(Nonce, reversed, two).ToArray(), "Q4");
        Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(Encoding.UTF8.GetBytes(Raw(Nonce, reversed)), Nonce, two, out _));
    }

    [TestMethod]
    public void Q14FramesOverTheCodecBoundAreRejectedBeforeParsing()
    {
        Assert.AreEqual(2048, QualificationProbeWireCodec.MaximumFrameBytes);
        Assert.IsLessThan(16 * 1024, QualificationProbeWireCodec.MaximumFrameBytes, "inside the unchanged pipe framing limit");
        string frame = Encoding.UTF8.GetString(QualificationProbeWireCodec.CreateProbeResponse(Nonce, Passing()));
        // A frame padded inside a string to just over the bound; the bound refuses before the grammar does.
        string padded = frame.Replace("probe-observed", "probe-observed" + new string('x', 2049 - frame.Length), StringComparison.Ordinal);
        Assert.AreEqual(2049, Encoding.UTF8.GetByteCount(padded));
        Assert.IsFalse(Read(padded));
        Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(new byte[2049], Nonce, out _));
        Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(new byte[16 * 1024], Nonce, out _));
    }

    // ---- Mutations ------------------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("passing")]
    [DataRow("refused")]
    [DataRow("token")]
    public void EachPropertyMissingDuplicatedSwappedNulledOrRetypedRejectsTheWholeFrame(string name)
    {
        QualificationProbeFacts facts = name switch
        {
            "passing" => Passing(),
            "refused" => Refused(6, QualificationProbeFailedCheck.StepRule, QualificationProbeFailedAccess.Unreadable,
                QualificationProbeRefusalReason.MachineInputsUnsupported, QualificationProbeRefusalDetail.None, 2, 1),
            _ => TokenRefused(),
        };
        byte[] frame = QualificationProbeWireCodec.CreateProbeResponse(Nonce, facts);
        List<KeyValuePair<string, string>> parts = Split(frame);
        CollectionAssert.AreEqual(QualificationProbeWireCodec.Fields.ToArray(), parts.Select(part => part.Key).ToArray());
        CollectionAssert.AreEqual(frame, Join(parts));
        for (int i = 0; i < parts.Count; i++)
        {
            string key = parts[i].Key;
            Assert.IsFalse(Read(Join(parts.Where((_, index) => index != i))), "missing " + key);

            List<KeyValuePair<string, string>> duplicated = [.. parts];
            duplicated.Insert(i + 1, parts[i]);
            Assert.IsFalse(Read(Join(duplicated)), "duplicated " + key);

            List<KeyValuePair<string, string>> renamed = [.. parts];
            renamed[i] = new(key.ToUpperInvariant(), parts[i].Value);
            Assert.IsFalse(Read(Join(renamed)), "renamed " + key);

            if (parts[i].Value != "null")
            {
                List<KeyValuePair<string, string>> nulled = [.. parts];
                nulled[i] = new(key, "null");
                Assert.IsFalse(Read(Join(nulled)), "null " + key);
            }

            List<KeyValuePair<string, string>> retyped = [.. parts];
            retyped[i] = new(key, Retype(parts[i].Value));
            Assert.IsFalse(Read(Join(retyped)), "retyped " + key);

            if (i + 1 < parts.Count)
            {
                List<KeyValuePair<string, string>> swapped = [.. parts];
                (swapped[i], swapped[i + 1]) = (swapped[i + 1], swapped[i]);
                Assert.IsFalse(Read(Join(swapped)), "swapped " + key);
            }
        }
    }

    [TestMethod]
    public void UpdateFramesAreRejectedByTheProbeReaderAndTheProbeFrameByEveryUpdateReader()
    {
        Guid job = Guid.Parse("44444444-5555-6666-7777-888888888888");
        string digest = Convert.ToHexString(SHA256.HashData("probe-cross"u8));
        UpdateWireFacts settled = new(UpdateWireInvocation.Started, 0, true, true, true, false, true, false, false, false);
        ReviewedHelperFacts matched = new(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None, "2.7.5", "2.8.0",
            ReviewedUpdateWireCodecFixture.Fingerprint);
        byte[] package = ReviewedUpdateWireCodec.CreateResponse(job, digest, Nonce, settled, matched, "ok");
        byte[] client = ReviewedUpdateWireCodec.CreateClientResponse(job, digest, Nonce, settled, matched, ReviewedClientRunFaultWire.None, "ok");
        byte[] request = ReviewedUpdateWireCodec.CreateRequest("x"u8, Convert.ToHexString(SHA256.HashData("x"u8)), Nonce,
            new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
        foreach (byte[] update in new[] { package, client, request })
            Assert.IsFalse(QualificationProbeWireCodec.TryReadProbeResponse(update, Nonce, out _));

        byte[] probe = QualificationProbeWireCodec.CreateProbeResponse(Nonce, Passing());
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(probe, job, digest, Nonce, out _));
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(probe, job, digest, Nonce, ReviewedHelperReplyShape.ClientSelfUpdate, out _));
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadRequest(probe, out _));
    }

    [TestMethod]
    public void ThePackageClientAndRequestFramesAreByteUnchanged()
    {
        // Pins of the protocol-2 update frames as they were before slice S2 (the update codec file is not modified by S2).
        Guid job = Guid.Parse("44444444-5555-6666-7777-888888888888");
        string digest = Convert.ToHexString(SHA256.HashData("probe-cross"u8));
        UpdateWireFacts settled = new(UpdateWireInvocation.Started, 0, true, true, true, false, true, false, false, false);
        ReviewedHelperFacts matched = new(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None, "2.7.5", "2.8.0",
            ReviewedUpdateWireCodecFixture.Fingerprint);
        byte[] package = ReviewedUpdateWireCodec.CreateResponse(job, digest, Nonce, settled, matched, "ok");
        byte[] client = ReviewedUpdateWireCodec.CreateClientResponse(job, digest, Nonce, settled, matched,
            ReviewedClientRunFaultWire.WatchFailedDuringRunAndReleaseIncomplete, "ok");
        byte[] request = ReviewedUpdateWireCodec.CreateRequest("x"u8, Convert.ToHexString(SHA256.HashData("x"u8)), Nonce,
            new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
        Assert.AreEqual(PackageFrameSha256, Convert.ToHexString(SHA256.HashData(package)), "package reply");
        Assert.AreEqual(ClientFrameSha256, Convert.ToHexString(SHA256.HashData(client)), "client reply");
        Assert.AreEqual(RequestFrameSha256, Convert.ToHexString(SHA256.HashData(request)), "request");
        Assert.AreEqual(PackageFrameLength, package.Length);
        Assert.AreEqual(ClientFrameLength, client.Length);
        Assert.AreEqual(RequestFrameLength, request.Length);
        Assert.AreEqual("2", ReviewedUpdateWireCodec.ProtocolVersion);
    }

    private const string PackageFrameSha256 = "6B5EE5531DBAF604E1CC7105A7DC6AC390A7E19482C608A3AAE47706C752E16B";
    private const string ClientFrameSha256 = "295E4F577E9FD942E8076D05F8DA6B067726276840020105F791B721892066C0";
    private const string RequestFrameSha256 = "0D1417DA82B59E9B0649E50974461AFD2200C47CFCB02AFE8DEA9812FB29B7D4";
    private const int PackageFrameLength = 713;
    private const int ClientFrameLength = 771;
    private const int RequestFrameLength = 282;

    // ---- Wire-name and bound pins ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void TheEighteenTokenVerdictWireNamesArePinnedAgainstBothEnums()
    {
        Assert.AreEqual(18, TokenVerdictWireNames.Length);
        CollectionAssert.AreEqual(TokenVerdictWireNames, Enum.GetNames<QualificationProbeTokenVerdict>(), "the probe wire enum");
        CollectionAssert.AreEqual(TokenVerdictWireNames, Enum.GetNames<TokenVerdict>(), "the native verdict enum (a rename needs a spec revision)");
        foreach (QualificationProbeTokenVerdict verdict in Enum.GetValues<QualificationProbeTokenVerdict>())
            Assert.AreEqual((int)Enum.Parse<TokenVerdict>(verdict.ToString()), (int)verdict, verdict.ToString());
    }

    [TestMethod]
    public void TheOtherClosedValueTablesArePinned()
    {
        CollectionAssert.AreEqual(FailedCheckNames,
            Enum.GetNames<QualificationProbeFailedCheck>());
        CollectionAssert.AreEqual(FailedAccessNames, Enum.GetNames<QualificationProbeFailedAccess>());
        CollectionAssert.AreEqual(ObservationNames, Enum.GetNames<QualificationProbeObservation>());
        CollectionAssert.AreEqual(RefusalReasonNames,
            Enum.GetNames<QualificationProbeRefusalReason>());
        CollectionAssert.AreEqual(RefusalDetailNames, Enum.GetNames<QualificationProbeRefusalDetail>());
        CollectionAssert.AreEqual(CapabilityStatusNames,
            Enum.GetNames<QualificationProbeCapabilityStatus>());
        CollectionAssert.AreEqual(Enum.GetNames<HelperProtocolCapabilityStatus>(), Enum.GetNames<QualificationProbeCapabilityStatus>()[..^1],
            "the G1 statuses plus NotEvaluated");
        // The refusal names the probe shares with the update reply keep their spelling.
        foreach (string reason in Enum.GetNames<QualificationProbeRefusalReason>())
            Assert.IsTrue(Enum.IsDefined(Enum.Parse<ReviewedHelperRefusalReason>(reason)), reason);
        CollectionAssert.AreEqual(DiagnosticTokens, new[] { QualificationProbeWireCodec.TokenRefusedDiagnostic, QualificationProbeWireCodec.CapabilityRefusedDiagnostic,
            QualificationProbeWireCodec.ObservedDiagnostic, QualificationProbeWireCodec.ObservationRefusedDiagnostic,
            QualificationProbeWireCodec.ObservationTimedOutDiagnostic });
        Assert.AreEqual("qualify-chocolatey-helper", QualificationProbeWireCodec.Kind);
        Assert.AreEqual(1_048_576, QualificationProbeWireCodec.MaximumNodesEvaluated);
        Assert.AreEqual(65_536, QualificationProbeWireCodec.MaximumAncestorsEvaluated);
        Assert.AreEqual(17, QualificationProbeWireCodec.Fields.Count);
    }

    [TestMethod]
    public void TheCompiledAllowListHasAtMostEightTokensOfAtMost64Bytes()
    {
        // §4.3 (review L8): a larger allow-list fails here and needs a spec revision of the 1,338-B bound.
        IReadOnlyList<UpdateRouteContract> allowList = DeploymentCapabilityPolicy.CapabilityOperationAllowlist;
        Assert.IsLessThanOrEqualTo(8, allowList.Count);
        foreach (UpdateRouteContract route in allowList)
            Assert.IsLessThanOrEqualTo(64, Encoding.UTF8.GetByteCount(DeploymentCapabilityPolicy.OperationToken(route)), route.ToString());
        Assert.AreEqual(8, QualificationProbeContract.MaximumG1Keys);
        Assert.AreEqual(64, QualificationProbeContract.MaximumG1KeyBytes);
        CollectionAssert.AreEqual(allowList.Select(DeploymentCapabilityPolicy.OperationToken).Order(StringComparer.Ordinal).ToArray(),
            QualificationProbeContract.Compiled.G1Keys.ToArray(), "Q4 keys are the compiled tokens in ordinal order");
        // Today: one token of 39 bytes.
        Assert.AreEqual(39, Encoding.UTF8.GetByteCount(Token));

        string[] nine = [.. Enumerable.Range(1, 9).Select(index => "Update/Route" + index)];
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeContract.CreateForTests(nine, ["2.7.5"]));
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeContract.CreateForTests(["Update/" + new string('X', 58)], ["2.7.5"]));
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeContract.CreateForTests([Token, ClientToken], ["2.7.5"]));
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeContract.CreateForTests([Token, Token], ["2.7.5"]));
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeContract.CreateForTests([], ["2.7.5"]));
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeContract.CreateForTests(["Update/a\"b"], ["2.7.5"]));
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeContract.CreateForTests([Token], []));
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeContract.CreateForTests([Token], ["2.7.5.0"]));
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeContract.CreateForTests([Token], ["2.7.5", "2.7.5"]));
        Assert.AreEqual(8, QualificationProbeContract.CreateForTests([.. nine[..8]], ["2.7.5"]).G1Keys.Count);
    }

    [TestMethod]
    public void Q10UsesTheNewestCompiledListedRelease()
    {
        CollectionAssert.AreEqual(ChocolateyReleaseList.Listed.Releases.Select(release => release.Version).ToArray(),
            QualificationProbeContract.Compiled.ListedClientVersions.ToArray());
        Assert.AreEqual(ChocolateyReleaseList.Listed.Releases[^1].NativeVersion.Normalized, QualificationProbeContract.Compiled.ListedClientVersions[^1]);
        Assert.AreEqual("2.7.5", QualificationProbeContract.Compiled.ListedClientVersions[^1]);
    }

    // ---- Fixtures -------------------------------------------------------------------------------------------------------------

    private static IReadOnlyList<QualificationProbeCapabilityEntry> G1(QualificationProbeContract contract, QualificationProbeCapabilityStatus status) =>
        [.. contract.G1Keys.Select(key => new QualificationProbeCapabilityEntry(key, status))];

    private static QualificationProbeFacts Passing(int nodes = 123456, int ancestors = 12345) => new(QualificationProbeTokenVerdict.Accepted,
        G1(QualificationProbeContract.Compiled, QualificationProbeCapabilityStatus.Supported), QualificationProbeObservation.Observed, null,
        QualificationProbeFailedCheck.None, QualificationProbeFailedAccess.None, QualificationProbeRefusalReason.None, QualificationProbeRefusalDetail.None,
        nodes, ancestors, "2.7.5", true, 64, QualificationProbeWireCodec.ObservedDiagnostic);

    private static QualificationProbeFacts TokenRefused(QualificationProbeTokenVerdict verdict = QualificationProbeTokenVerdict.NoLinkedToken) => new(verdict,
        G1(QualificationProbeContract.Compiled, QualificationProbeCapabilityStatus.NotEvaluated), QualificationProbeObservation.NotRun, null,
        QualificationProbeFailedCheck.None, QualificationProbeFailedAccess.None, QualificationProbeRefusalReason.None, QualificationProbeRefusalDetail.None,
        0, 0, null, false, 0, QualificationProbeWireCodec.TokenRefusedDiagnostic);

    private static QualificationProbeFacts CapabilityRefused(QualificationProbeCapabilityStatus status) => new(QualificationProbeTokenVerdict.Accepted,
        G1(QualificationProbeContract.Compiled, status), QualificationProbeObservation.NotRun, null,
        QualificationProbeFailedCheck.None, QualificationProbeFailedAccess.None, QualificationProbeRefusalReason.None, QualificationProbeRefusalDetail.None,
        0, 0, null, false, 0, QualificationProbeWireCodec.CapabilityRefusedDiagnostic);

    private static QualificationProbeFacts Refused(int step, QualificationProbeFailedCheck check, QualificationProbeFailedAccess access,
        QualificationProbeRefusalReason reason, QualificationProbeRefusalDetail detail, int nodes = 0, int ancestors = 0) =>
        new(QualificationProbeTokenVerdict.Accepted, G1(QualificationProbeContract.Compiled, QualificationProbeCapabilityStatus.Supported),
            QualificationProbeObservation.Refused, step, check, access, reason, detail, nodes, ancestors, null, false, 0,
            QualificationProbeWireCodec.ObservationRefusedDiagnostic);

    private static QualificationProbeFacts TimedOut(int nodes = 5, int ancestors = 1) => new(QualificationProbeTokenVerdict.Accepted,
        G1(QualificationProbeContract.Compiled, QualificationProbeCapabilityStatus.Supported), QualificationProbeObservation.TimedOut, null,
        QualificationProbeFailedCheck.None, QualificationProbeFailedAccess.None, QualificationProbeRefusalReason.None, QualificationProbeRefusalDetail.None,
        nodes, ancestors, null, false, 0, QualificationProbeWireCodec.ObservationTimedOutDiagnostic);

    /// <summary>
    /// Every closed-value combination over a one-key contract, with the derived fields at their largest valid values (counts at
    /// the maxima unless NotRun, fingerprint by Q11, diagnostic by Q13) and every possible version (null or a listed release).
    /// </summary>
    private static IEnumerable<QualificationProbeFacts> EveryValueCombination(QualificationProbeContract contract)
    {
        string?[] versions = [null, .. contract.ListedClientVersions];
        int?[] steps = [null, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        foreach (QualificationProbeTokenVerdict verdict in Enum.GetValues<QualificationProbeTokenVerdict>())
        foreach (QualificationProbeCapabilityStatus status in Enum.GetValues<QualificationProbeCapabilityStatus>())
        foreach (QualificationProbeObservation observation in Enum.GetValues<QualificationProbeObservation>())
        {
            bool notRun = observation == QualificationProbeObservation.NotRun, observed = observation == QualificationProbeObservation.Observed;
            int nodes = notRun ? 0 : QualificationProbeWireCodec.MaximumNodesEvaluated;
            int ancestors = notRun ? 0 : QualificationProbeWireCodec.MaximumAncestorsEvaluated;
            string diagnostic = QualificationProbeWireCodec.ExpectedDiagnostic(verdict, observation)!;
            foreach (int? step in steps)
            foreach (QualificationProbeFailedCheck check in Enum.GetValues<QualificationProbeFailedCheck>())
            foreach (QualificationProbeFailedAccess access in Enum.GetValues<QualificationProbeFailedAccess>())
            foreach (QualificationProbeRefusalReason reason in Enum.GetValues<QualificationProbeRefusalReason>())
            foreach (QualificationProbeRefusalDetail detail in Enum.GetValues<QualificationProbeRefusalDetail>())
            foreach (string? version in versions)
                yield return new(verdict, G1(contract, status), observation, step, check, access, reason, detail, nodes, ancestors, version,
                    observed, observed ? 64 : 0, diagnostic);
        }
    }

    private static void AssertRoundTrip(byte[] frame, QualificationProbeFacts expected)
    {
        Assert.IsTrue(QualificationProbeWireCodec.TryReadProbeResponse(frame, Nonce, out QualificationProbeFacts? read), Encoding.UTF8.GetString(frame));
        Assert.AreEqual(expected, read);
        CollectionAssert.AreEqual(frame, QualificationProbeWireCodec.CreateProbeResponse(Nonce, read!));
    }

    /// <summary>The probe frame layout without any rule check, to feed rule-violating frames to the reader.</summary>
    private static string Raw(string nonce, QualificationProbeFacts facts)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, RawWriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("protocolVersion", "2");
            writer.WriteString("kind", "qualify-chocolatey-helper");
            writer.WriteString("nonce", nonce);
            writer.WriteString("tokenVerdict", facts.Verdict.ToString());
            writer.WriteStartObject("g1Status");
            foreach (QualificationProbeCapabilityEntry entry in facts.G1Status) writer.WriteString(entry.Token, entry.Status.ToString());
            writer.WriteEndObject();
            writer.WriteString("observation", facts.Observation.ToString());
            if (facts.FailedStep is int step) writer.WriteNumber("failedStep", step);
            else writer.WriteNull("failedStep");
            writer.WriteString("failedCheck", facts.FailedCheck.ToString());
            writer.WriteString("failedAccess", facts.FailedAccess.ToString());
            writer.WriteString("refusalReason", facts.RefusalReason.ToString());
            writer.WriteString("refusalDetail", facts.RefusalDetail.ToString());
            writer.WriteNumber("nodesEvaluated", facts.NodesEvaluated);
            writer.WriteNumber("ancestorsEvaluated", facts.AncestorsEvaluated);
            if (facts.ObservedClientVersion is null) writer.WriteNull("observedClientVersion");
            else writer.WriteString("observedClientVersion", facts.ObservedClientVersion);
            writer.WriteBoolean("fingerprintPresent", facts.FingerprintPresent);
            writer.WriteNumber("fingerprintLength", facts.FingerprintLength);
            writer.WriteString("diagnostic", facts.Diagnostic);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Retype(string value) => value switch
    {
        "null" => "\"null\"",
        "true" or "false" => "\"" + value + "\"",
        _ when value.StartsWith('{') => "\"" + "object" + "\"",
        _ when value.StartsWith('"') => "1",
        _ => "\"" + value + "\"",
    };

    private static List<KeyValuePair<string, string>> Split(byte[] frame)
    {
        using JsonDocument document = JsonDocument.Parse(frame);
        return [.. document.RootElement.EnumerateObject().Select(property => new KeyValuePair<string, string>(property.Name, property.Value.GetRawText()))];
    }

    private static byte[] Join(IEnumerable<KeyValuePair<string, string>> parts) =>
        Encoding.UTF8.GetBytes("{" + string.Join(",", parts.Select(part => "\"" + part.Key + "\":" + part.Value)) + "}");

    private static bool Read(string frame) => Read(Encoding.UTF8.GetBytes(frame));

    private static bool Read(byte[] frame)
    {
        bool result = QualificationProbeWireCodec.TryReadProbeResponse(frame, Nonce, out QualificationProbeFacts? facts);
        Assert.AreEqual(result, facts is not null);
        return result;
    }
}
