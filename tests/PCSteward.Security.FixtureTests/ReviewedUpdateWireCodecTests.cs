using System.Security.Cryptography;
using System.Text;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>FR-UPD-15; TM-03/04/07/13: passive framing never grants dispatch or settlement authority.</summary>
[TestClass]
public sealed class ReviewedUpdateWireCodecTests
{
    private static readonly Guid Job = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly string Nonce = new('A', 64);
    private static readonly byte[] Payload = [251, 239, 255]; // Base64 contains '+' and '/'.
    private static readonly string Digest = Convert.ToHexString(SHA256.HashData(Payload));
    private static readonly DateTimeOffset Issued = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RequestHasByteExactBase64AndUtcZTimestamp()
    {
        byte[] frame = ReviewedUpdateWireCodec.CreateRequest(Payload, Digest, Nonce, Issued);
        string expected = "{\"protocolVersion\":\"2\",\"operation\":\"UpdatePackage\",\"operationPayload\":\"++//\",\"operationDigest\":\"" + Digest + "\",\"nonce\":\"" + Nonce + "\",\"issuedAtUtc\":\"2026-10-30T12:00:00.0000000Z\"}";
        Assert.AreEqual(expected, Encoding.UTF8.GetString(frame));
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadRequest(frame, out FramedUpdateRequest? read));
        CollectionAssert.AreEqual(Payload, read!.GetPayload());
        byte[] copy = read.GetPayload();
        copy[0] = 0;
        CollectionAssert.AreEqual(Payload, read.GetPayload());
    }

    [TestMethod]
    [DataRow("escape")]
    [DataRow("space")]
    [DataRow("time")]
    [DataRow("duplicate")]
    [DataRow("extra")]
    [DataRow("digest")]
    [DataRow("version")]
    [DataRow("base64")]
    [DataRow("order")]
    public void SemanticallyEquivalentOrMalformedRequestFramesReject(string mutation)
    {
        string frame = Encoding.UTF8.GetString(ReviewedUpdateWireCodec.CreateRequest(Payload, Digest, Nonce, Issued));
        frame = mutation switch
        {
            "escape" => frame.Replace("+", "\\u002B", StringComparison.Ordinal),
            "space" => frame + " ",
            "time" => frame.Replace("0000000Z", "0000000+00:00", StringComparison.Ordinal),
            "duplicate" => frame.Insert(1, "\"nonce\":\"" + Nonce + "\","),
            "extra" => frame.Insert(1, "\"Final\":true,"),
            "digest" => frame.Replace(Digest, new string('0', 64), StringComparison.Ordinal),
            "version" => frame.Replace("\"protocolVersion\":\"2\"", "\"protocolVersion\":\"1\"", StringComparison.Ordinal),
            "base64" => frame.Replace("++//", "+ +//", StringComparison.Ordinal),
            "order" => frame.Replace("\"protocolVersion\":\"2\",\"operation\":\"UpdatePackage\"", "\"operation\":\"UpdatePackage\",\"protocolVersion\":\"2\"", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadRequest(Encoding.UTF8.GetBytes(frame), out _));
    }

    [TestMethod]
    public void MaximumRequestAndEscapedUnicodeResponseStayWithinFrameBound()
    {
        byte[] payload = Enumerable.Repeat((byte)255, ReviewedUpdateWireCodec.MaximumPayloadBytes).ToArray();
        byte[] request = ReviewedUpdateWireCodec.CreateRequest(payload, Convert.ToHexString(SHA256.HashData(payload)), Nonce, Issued);
        Assert.IsLessThan(16_384, request.Length);
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadRequest(request, out _));
        foreach (string diagnostic in new[] { new string('"', 4096), new string('\\', 4096), string.Concat(Enumerable.Repeat("😀漢", 4096)), "bad\uD800tail" })
        {
            byte[] response = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, Settled(), Matched(), diagnostic);
            Assert.IsLessThan(16_384, response.Length);
            Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(response, Job, Digest, Nonce, out FramedUpdateResponse? read));
            Assert.IsTrue(read!.Diagnostic.Length <= 2048 && read.Diagnostic.All(c => c is >= ' ' and <= '~'));
        }
        Assert.ThrowsExactly<ArgumentException>(() => ReviewedUpdateWireCodec.CreateRequest(new byte[4097], Digest, Nonce, Issued));
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadRequest(new byte[16_385], out _));
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadRequest([0xFF], out _));
    }

    [TestMethod]
    public void EveryBooleanFactCombinationHasAnExplicitConsistencyRule()
    {
        foreach (UpdateWireInvocation invocation in Enum.GetValues<UpdateWireInvocation>())
        for (int flags = 0; flags < 32; flags++)
        foreach (int? exit in new int?[] { null, 0, 7 })
        {
            bool direct = (flags & 1) != 0, io = (flags & 2) != 0, eof = (flags & 4) != 0;
            bool termination = (flags & 8) != 0, aggregate = (flags & 16) != 0;
            UpdateWireFacts facts = new(invocation, exit, direct, io, eof, termination, aggregate, false, false, false);
            bool expected = (exit.HasValue == direct) &&
                (invocation == UpdateWireInvocation.Started || (exit is null && !direct && !io && !eof)) &&
                (!aggregate || (!termination && invocation != UpdateWireInvocation.Unknown &&
                    (invocation == UpdateWireInvocation.NotStarted || (direct && io && eof))));
            Assert.AreEqual(expected, facts.IsConsistent, $"{invocation}/{flags}/{exit}");
            if (!expected) continue;
            // S3/S4/S11: started or unknown work is Matched; settled not-started work is a refusal; unsettled is Undetermined.
            ReviewedHelperFacts reviewed = invocation != UpdateWireInvocation.NotStarted ? Matched()
                : aggregate ? NotAdmitted() : Reviewed(ReviewedHelperQualification.Undetermined, ReviewedHelperRefusalReason.None, null, null, null);
            byte[] response = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, facts, reviewed, "facts only");
            Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(response, Job, Digest, Nonce, out FramedUpdateResponse? read));
            Assert.AreEqual(facts, read!.Facts);
            Assert.AreEqual(reviewed, read.Reviewed);
            Assert.AreEqual(!aggregate, read.Facts.RequiresUncertainty);
        }
        Assert.IsFalse(new UpdateWireFacts((UpdateWireInvocation)99, null, false, false, false, false, false, false, false, false).IsConsistent);
    }

    [TestMethod]
    public void DiagnosticCleanupIsStableAtTheTruncationBoundary()
    {
        byte[] frame = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, Settled(), Matched(), new string('a', 2047) + " b");
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, out FramedUpdateResponse? read));
        Assert.AreEqual(new string('a', 2047), read!.Diagnostic);
        CollectionAssert.AreEqual(frame, ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, read.Facts, read.Reviewed, read.Diagnostic));
    }

    [TestMethod]
    [DataRow("\u00e9password=hunter2")]
    [DataRow("\uD800password=hunter2")]
    [DataRow("\U0001F600token=hunter2")]
    public void AsciiConversionCannotExposeASensitiveValueThatHadNoUnicodeWordBoundary(string diagnostic)
    {
        byte[] frame = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, Settled(), Matched(), diagnostic);
        Assert.DoesNotContain("hunter2", Encoding.UTF8.GetString(frame));
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, out FramedUpdateResponse? read));
        StringAssert.Contains(read!.Diagnostic, "[redacted]");
        CollectionAssert.AreEqual(frame, ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, read.Facts, read.Reviewed, read.Diagnostic));
    }

    [TestMethod]
    [DataRow("to\u212Aen=hunter2", "hunter2")]
    [DataRow("\u212A:\\private\\document.txt", "private")]
    public void UnicodeCaseEquivalentKeywordsAndPathsStayRedacted(string diagnostic, string privateValue)
    {
        byte[] frame = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, Settled(), Matched(), diagnostic);
        Assert.DoesNotContain(privateValue, Encoding.UTF8.GetString(frame));
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, out _));
    }

    [TestMethod]
    public void ReaderRejectsAWellFramedDiagnosticContainingAnUnredactedSensitiveValue()
    {
        string frame = Encoding.UTF8.GetString(ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, Settled(), Matched(), "safe-placeholder"))
            .Replace("safe-placeholder", "password=hunter2", StringComparison.Ordinal);
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(Encoding.UTF8.GetBytes(frame), Job, Digest, Nonce, out _));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LoneSurrogatePropertyNamesAreRejectedByBothTryParsersWithoutThrowing(bool response)
    {
        byte[] malformed = Encoding.ASCII.GetBytes("{\"\\uD800\":1}");
        if (response) Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(malformed, Job, Digest, Nonce, out _));
        else Assert.IsFalse(ReviewedUpdateWireCodec.TryReadRequest(malformed, out _));
    }

    [TestMethod]
    public void CancellationTimeoutAndOutputLimitObservationsSurviveTheWire()
    {
        UpdateWireFacts facts = Settled() with { Cancelled = true, TimedOut = true, OutputLimitExceeded = true };
        byte[] frame = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, facts, Matched(), "native observations");
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, out FramedUpdateResponse? read));
        Assert.AreEqual(facts, read!.Facts);
        Assert.IsTrue(read.Facts.Cancelled && read.Facts.TimedOut && read.Facts.OutputLimitExceeded);
    }

    [TestMethod]
    public void ResponseCorrelationAndContradictorySettlementCannotBeSubstituted()
    {
        byte[] frame = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, Settled(), Matched(), "provider exited; verification still required");
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(frame, Guid.NewGuid(), Digest, Nonce, out _));
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, new string('0', 64), Nonce, out _));
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, new string('B', 64), out _));
        string json = Encoding.UTF8.GetString(frame);
        foreach (string invalid in new[] {
            json.Replace("\"ownedIoCompleted\":true", "\"ownedIoCompleted\":false", StringComparison.Ordinal),
            json.Replace("\"exitCode\":0", "\"exitCode\":\"0\"", StringComparison.Ordinal),
            json.Replace("\"exitCode\":0", "\"exitCode\":-0", StringComparison.Ordinal),
            json.Replace("\"protocolVersion\":\"2\"", "\"protocolVersion\":\"1\"", StringComparison.Ordinal),
            json.Replace("\"protocolVersion\":\"2\"", "\"protocolVersion\":\"3\"", StringComparison.Ordinal),
            json.Insert(1, "\"Final\":true,"), json + " ",
            json.Replace("provider", "\\u0070rovider", StringComparison.Ordinal) })
            Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(Encoding.UTF8.GetBytes(invalid), Job, Digest, Nonce, out _));
        Assert.ThrowsExactly<ArgumentException>(() => ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce,
            Settled() with { OwnedIoCompleted = false }, Matched(), "contradictory"));
        // A protocol-1 response is never a protocol-2 response.
        byte[] protocol1 = Encoding.UTF8.GetBytes("{\"protocolVersion\":\"1\",\"jobId\":\"" + Job + "\",\"nonce\":\"" + Nonce +
            "\",\"outcome\":\"Completed\",\"exitCode\":0,\"sanitizedOutput\":\"completed\"}");
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(protocol1, Job, Digest, Nonce, out _));
    }

    private static UpdateWireFacts Settled() => new(UpdateWireInvocation.Started, 0, true, true, true, false, true, false, false, false);

    private static ReviewedHelperFacts Matched() => Reviewed(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None,
        ReviewedUpdateWireCodecFixture.Installed, ReviewedUpdateWireCodecFixture.Candidate, ReviewedUpdateWireCodecFixture.Fingerprint);

    private static ReviewedHelperFacts NotAdmitted() =>
        Reviewed(ReviewedHelperQualification.Refused, ReviewedHelperRefusalReason.RequestNotAdmitted, null, null, null);

    private static ReviewedHelperFacts Reviewed(ReviewedHelperQualification qualification, ReviewedHelperRefusalReason reason,
        string? installed, string? candidate, string? fingerprint) => new(qualification, reason, installed, candidate, fingerprint);
}
