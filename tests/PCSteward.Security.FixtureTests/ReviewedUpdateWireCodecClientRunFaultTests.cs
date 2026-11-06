using System.Security.Cryptography;
using System.Text;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13, ADR-031 design §8 (owner-approved W1): the <c>clientRunFault</c> reply field and
/// codec rule S14 — route-conditional presence (S14a), the closed allowed-combination table (S14b) and consistency with the
/// facts (S14c) — plus the compatibility pin that the package-route reply bytes are unchanged.
/// </summary>
[TestClass]
public sealed class ReviewedUpdateWireCodecClientRunFaultTests
{
    private static readonly Guid Job = Guid.Parse("33333333-4444-5555-6666-777777777777");
    private static readonly string Nonce = new('D', 64);
    private static readonly string Digest = Convert.ToHexString(SHA256.HashData("client-operation"u8));
    private static readonly string Fingerprint = ReviewedUpdateWireCodecFixture.Fingerprint;
    private static readonly string[] S14bOnly = ["S14b"];

    private static readonly (ReviewedClientRunFaultWire Value, string Text)[] Table =
    [
        (ReviewedClientRunFaultWire.None, "None"),
        (ReviewedClientRunFaultWire.SetupOverrideDuringRun, "SetupOverrideDuringRun"),
        (ReviewedClientRunFaultWire.RootWritableDuringRun, "RootWritableDuringRun"),
        (ReviewedClientRunFaultWire.WatchFailedDuringRun, "WatchFailedDuringRun"),
        (ReviewedClientRunFaultWire.ReleaseIncomplete, "ReleaseIncomplete"),
        (ReviewedClientRunFaultWire.SetupOverrideDuringRunAndReleaseIncomplete, "SetupOverrideDuringRun+ReleaseIncomplete"),
        (ReviewedClientRunFaultWire.RootWritableDuringRunAndReleaseIncomplete, "RootWritableDuringRun+ReleaseIncomplete"),
        (ReviewedClientRunFaultWire.WatchFailedDuringRunAndReleaseIncomplete, "WatchFailedDuringRun+ReleaseIncomplete"),
    ];

    internal static UpdateWireFacts StartedSettled(int exitCode = 0) => new(UpdateWireInvocation.Started, exitCode, true, true, true, false, true, false, false, false);
    internal static UpdateWireFacts UnknownUnsettled() => new(UpdateWireInvocation.Unknown, null, false, false, false, false, false, false, false, false);
    internal static UpdateWireFacts NotStartedSettled() => new(UpdateWireInvocation.NotStarted, null, false, false, false, false, true, false, false, false);
    internal static UpdateWireFacts NotStartedUnsettled() => new(UpdateWireInvocation.NotStarted, null, false, false, false, false, false, false, false, false);
    internal static ReviewedHelperFacts Matched() => new(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None, "2.7.5", "2.8.0", Fingerprint);
    internal static ReviewedHelperFacts NotAdmitted() => new(ReviewedHelperQualification.Refused, ReviewedHelperRefusalReason.RequestNotAdmitted, null, null, null);
    internal static ReviewedHelperFacts Undetermined() => new(ReviewedHelperQualification.Undetermined, ReviewedHelperRefusalReason.None, null, null, null);

    [TestMethod]
    public void ClientReplyGoldenPlacesTheFieldBeforeDiagnosticForEveryTableRow()
    {
        string prefix = "{\"protocolVersion\":\"2\",\"jobId\":\"" + Job + "\",\"operationDigest\":\"" + Digest + "\",\"nonce\":\"" + Nonce + "\",";
        foreach ((ReviewedClientRunFaultWire value, string text) in Table)
        {
            byte[] frame = ReviewedUpdateWireCodec.CreateClientResponse(Job, Digest, Nonce, StartedSettled(), Matched(), value, "launch-started");
            Assert.AreEqual(prefix + "\"invocation\":\"Started\",\"exitCode\":0,\"directProcessExitObserved\":true,\"ownedIoCompleted\":true," +
                "\"pipeEofObserved\":true,\"aggregateTerminationFailed\":false,\"aggregateSettlementConfirmed\":true,\"cancelled\":false," +
                "\"timedOut\":false,\"outputLimitExceeded\":false,\"qualification\":\"Matched\",\"refusalReason\":\"None\"," +
                "\"observedInstalledVersion\":\"2.7.5\",\"observedCandidateVersion\":\"2.8.0\",\"observedProfileFingerprint\":\"" + Fingerprint +
                "\",\"clientRunFault\":\"" + text + "\",\"diagnostic\":\"launch-started\"}", Encoding.UTF8.GetString(frame), text);
            Assert.AreEqual(text, ReviewedUpdateWireCodec.ClientRunFaultText(value));
            Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, ReviewedHelperReplyShape.ClientSelfUpdate,
                out FramedUpdateResponse? read), text);
            Assert.AreEqual(value, read!.ClientRunFault);
            Assert.AreEqual(Matched(), read.Reviewed);
        }
        Assert.AreEqual(8, Enum.GetValues<ReviewedClientRunFaultWire>().Length, "the table is the whole closed set");
    }

    [TestMethod]
    public void PackageReplyBytesAreUnchangedAndCarryNoField()
    {
        // Compatibility (design §8.4 a): the package shape is the reviewed 20 properties; removing the client field from a client
        // reply yields exactly the package bytes, so every existing package golden stays valid.
        byte[] package = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, StartedSettled(), Matched(), "launch-started");
        byte[] client = ReviewedUpdateWireCodec.CreateClientResponse(Job, Digest, Nonce, StartedSettled(), Matched(), ReviewedClientRunFaultWire.None,
            "launch-started");
        Assert.IsFalse(Encoding.UTF8.GetString(package).Contains("clientRunFault", StringComparison.Ordinal));
        Assert.AreEqual(Encoding.UTF8.GetString(package), Encoding.UTF8.GetString(client).Replace(",\"clientRunFault\":\"None\"", "", StringComparison.Ordinal));
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(package, Job, Digest, Nonce, out FramedUpdateResponse? read));
        Assert.IsNull(read!.ClientRunFault);
    }

    [TestMethod]
    public void S14aPresenceIsRouteConditionalBothWays()
    {
        byte[] package = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, StartedSettled(), Matched(), "ok");
        byte[] client = ReviewedUpdateWireCodec.CreateClientResponse(Job, Digest, Nonce, StartedSettled(), Matched(), ReviewedClientRunFaultWire.None, "ok");
        // Absent on the client route: rejected (no success can follow).
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(package, Job, Digest, Nonce, ReviewedHelperReplyShape.ClientSelfUpdate, out _));
        // Present on the package route: rejected, through both reader overloads.
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(client, Job, Digest, Nonce, out _));
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(client, Job, Digest, Nonce, ReviewedHelperReplyShape.Package, out _));
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(client, Job, Digest, Nonce, ReviewedHelperReplyShape.ClientSelfUpdate, out _));
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(client, Job, Digest, Nonce, (ReviewedHelperReplyShape)7, out _));

        string text = Encoding.UTF8.GetString(client);
        // Moved after diagnostic, moved to the front, duplicated: all rejected.
        string moved = text.Replace(",\"clientRunFault\":\"None\"", "", StringComparison.Ordinal).TrimEnd('}') + ",\"clientRunFault\":\"None\"}";
        string front = "{\"clientRunFault\":\"None\"," + text[1..].Replace(",\"clientRunFault\":\"None\"", "", StringComparison.Ordinal);
        string duplicate = text.Replace(",\"clientRunFault\":\"None\"", ",\"clientRunFault\":\"None\",\"clientRunFault\":\"None\"", StringComparison.Ordinal);
        foreach (string variant in new[] { moved, front, duplicate })
            Assert.IsFalse(ReadClient(variant), variant);
    }

    [TestMethod]
    public void S14bRejectsEveryValueOutsideTheTable()
    {
        byte[] client = ReviewedUpdateWireCodec.CreateClientResponse(Job, Digest, Nonce, StartedSettled(), Matched(), ReviewedClientRunFaultWire.None, "ok");
        string text = Encoding.UTF8.GetString(client);
        string[] invalid =
        [
            "\"none\"", "\"NONE\"", "\" None\"", "\"None \"", "\"\"", "\"Unknown\"", "\"0\"", "\"1\"", "\"SetupOverride\"",
            "\"ReleaseIncomplete+SetupOverrideDuringRun\"", "\"SetupOverrideDuringRun+RootWritableDuringRun\"",
            "\"SetupOverrideDuringRun+RootWritableDuringRun+ReleaseIncomplete\"", "\"None+ReleaseIncomplete\"",
            "\"SetupOverrideDuringRun, ReleaseIncomplete\"", "\"SetupOverrideDuringRun|ReleaseIncomplete\"",
            "\"SetupOverrideDuringRunAndReleaseIncomplete\"", "\"WatchFailedDuringRun+ReleaseIncomplete+ReleaseIncomplete\"",
            "\"\\u004Eone\"", "0", "null", "true", "[]",
        ];
        foreach (string value in invalid)
            Assert.IsFalse(ReadClient(text.Replace("\"clientRunFault\":\"None\"", "\"clientRunFault\":" + value, StringComparison.Ordinal)), value);
        foreach (string value in invalid)
            Assert.IsFalse(ReviewedUpdateWireCodec.TryParseClientRunFault(value.Trim('"'), out _), value);
        Assert.ThrowsExactly<ArgumentException>(() => ReviewedUpdateWireCodec.CreateClientResponse(Job, Digest, Nonce, StartedSettled(), Matched(),
            (ReviewedClientRunFaultWire)99, "ok"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReviewedUpdateWireCodec.ClientRunFaultText((ReviewedClientRunFaultWire)99));
        CollectionAssert.AreEqual(S14bOnly,
            ReviewedUpdateWireCodec.ClientRunFaultViolations(StartedSettled(), Matched(), (ReviewedClientRunFaultWire)(-1)).ToArray());
    }

    [TestMethod]
    public void S14cAllowsAFaultOnlyWithMatchedStartedOrUnknown()
    {
        (UpdateWireFacts Facts, ReviewedHelperFacts Reviewed, bool FaultAllowed)[] rows =
        [
            (StartedSettled(), Matched(), true),
            (StartedSettled(exitCode: 1), Matched(), true),
            (UnknownUnsettled(), Matched(), true),
            (NotStartedSettled(), Matched(), false),
            (NotStartedSettled(), NotAdmitted(), false),
            (NotStartedUnsettled(), Undetermined(), false),
        ];
        foreach ((UpdateWireFacts facts, ReviewedHelperFacts reviewed, bool allowed) in rows)
            foreach ((ReviewedClientRunFaultWire value, string text) in Table)
            {
                bool expected = value == ReviewedClientRunFaultWire.None || allowed;
                Assert.AreEqual(expected, ReviewedUpdateWireCodec.ClientRunFaultViolations(facts, reviewed, value).Count == 0, $"{reviewed.Qualification} {facts.Invocation} {text}");
                if (expected)
                {
                    byte[] frame = ReviewedUpdateWireCodec.CreateClientResponse(Job, Digest, Nonce, facts, reviewed, value, "x");
                    Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, ReviewedHelperReplyShape.ClientSelfUpdate, out _), text);
                }
                else
                {
                    Assert.ThrowsExactly<ArgumentException>(() => ReviewedUpdateWireCodec.CreateClientResponse(Job, Digest, Nonce, facts, reviewed, value, "x"));
                    // A hand-built frame with the same content is rejected by the reader as well.
                    byte[] none = ReviewedUpdateWireCodec.CreateClientResponse(Job, Digest, Nonce, facts, reviewed, ReviewedClientRunFaultWire.None, "x");
                    string forged = Encoding.UTF8.GetString(none).Replace("\"clientRunFault\":\"None\"", "\"clientRunFault\":\"" + text + "\"", StringComparison.Ordinal);
                    Assert.IsFalse(ReadClient(forged), text);
                }
            }
    }

    [TestMethod]
    public void LargestClientReplyStaysInsideTheFrameCeiling()
    {
        string version = "1.0.0-" + new string('a', 122);
        ReviewedHelperFacts reviewed = new(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None, version, version, Fingerprint);
        UpdateWireFacts facts = new(UpdateWireInvocation.Started, int.MinValue, true, true, true, true, false, true, true, true);
        byte[] frame = ReviewedUpdateWireCodec.CreateClientResponse(Job, Digest, Nonce, facts, reviewed,
            ReviewedClientRunFaultWire.SetupOverrideDuringRunAndReleaseIncomplete, new string('\u0001', 4096));
        Assert.IsLessThan(ReviewedUpdateWireCodec.MaximumFrameBytes, frame.Length);
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, ReviewedHelperReplyShape.ClientSelfUpdate, out FramedUpdateResponse? read));
        Assert.AreEqual(ReviewedClientRunFaultWire.SetupOverrideDuringRunAndReleaseIncomplete, read!.ClientRunFault);
    }

    private static bool ReadClient(string frame) =>
        ReviewedUpdateWireCodec.TryReadResponse(Encoding.UTF8.GetBytes(frame), Job, Digest, Nonce, ReviewedHelperReplyShape.ClientSelfUpdate, out _);
}
