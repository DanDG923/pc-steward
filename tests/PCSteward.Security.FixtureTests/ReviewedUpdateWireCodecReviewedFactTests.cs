using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Security.FixtureTests;

internal static class ReviewedUpdateWireCodecFixture
{
    internal const string Installed = "1.2.3";
    internal const string Candidate = "1.2.4";
    internal static readonly string Fingerprint = Convert.ToHexString(SHA256.HashData("choco-profile-v2 fixture"u8));
}

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13: protocol-2 reviewed helper fact extension (Task 50 rev 3) at the passive codec layer:
/// the exact 20-property response (§2), structural predicates S1–S13 (§3.2), bounds (§5), O1 in-place redefinition and the
/// O9 version-grammar differential (§8). Operation-bound predicates P1–P6 belong to the later desktop adapter slice.
/// </summary>
[TestClass]
public sealed class ReviewedUpdateWireCodecReviewedFactTests
{
    private const string Installed = ReviewedUpdateWireCodecFixture.Installed;
    private const string Candidate = ReviewedUpdateWireCodecFixture.Candidate;
    private static readonly string Fingerprint = ReviewedUpdateWireCodecFixture.Fingerprint;
    private static readonly Guid Job = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly string Nonce = new('C', 64);
    private static readonly string Digest = Convert.ToHexString(SHA256.HashData("operation"u8));
    private static readonly string Version128 = "1.0.0-" + new string('a', 122);
    private static readonly string OtherVersion128 = "2.0.0-" + new string('b', 122);
    private static readonly string Version129 = "1.0.0-" + new string('a', 123);
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly string[] Fields =
    [
        "protocolVersion", "jobId", "operationDigest", "nonce", "invocation", "exitCode", "directProcessExitObserved",
        "ownedIoCompleted", "pipeEofObserved", "aggregateTerminationFailed", "aggregateSettlementConfirmed", "cancelled",
        "timedOut", "outputLimitExceeded", "qualification", "refusalReason", "observedInstalledVersion",
        "observedCandidateVersion", "observedProfileFingerprint", "diagnostic"
    ];
    private static readonly string[] NoViolation = [];
    private static readonly string[] S12Only = ["S12"];

    [TestMethod]
    public void ResponseHasTheExactTwentyPropertyOrderAndEncoding()
    {
        byte[] matched = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, StartedSettled(), Matched(), "ok");
        string prefix = "{\"protocolVersion\":\"2\",\"jobId\":\"" + Job + "\",\"operationDigest\":\"" + Digest + "\",\"nonce\":\"" + Nonce + "\",";
        Assert.AreEqual(prefix + "\"invocation\":\"Started\",\"exitCode\":0,\"directProcessExitObserved\":true,\"ownedIoCompleted\":true," +
            "\"pipeEofObserved\":true,\"aggregateTerminationFailed\":false,\"aggregateSettlementConfirmed\":true,\"cancelled\":false," +
            "\"timedOut\":false,\"outputLimitExceeded\":false,\"qualification\":\"Matched\",\"refusalReason\":\"None\"," +
            "\"observedInstalledVersion\":\"1.2.3\",\"observedCandidateVersion\":\"1.2.4\",\"observedProfileFingerprint\":\"" + Fingerprint +
            "\",\"diagnostic\":\"ok\"}", Encoding.UTF8.GetString(matched));
        CollectionAssert.AreEqual(Fields, Split(matched).Select(part => part.Key).ToArray());

        byte[] refused = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, NotStartedSettled(), NotAdmitted(), "");
        Assert.AreEqual(prefix + "\"invocation\":\"NotStarted\",\"exitCode\":null,\"directProcessExitObserved\":false,\"ownedIoCompleted\":false," +
            "\"pipeEofObserved\":false,\"aggregateTerminationFailed\":false,\"aggregateSettlementConfirmed\":true,\"cancelled\":false," +
            "\"timedOut\":false,\"outputLimitExceeded\":false,\"qualification\":\"Refused\",\"refusalReason\":\"RequestNotAdmitted\"," +
            "\"observedInstalledVersion\":null,\"observedCandidateVersion\":null,\"observedProfileFingerprint\":null,\"diagnostic\":\"\"}",
            Encoding.UTF8.GetString(refused));
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(refused, Job, Digest, Nonce, out FramedUpdateResponse? read));
        Assert.AreEqual(NotAdmitted(), read!.Reviewed);
        Assert.IsNull(read.Reviewed.ObservedInstalledVersion);
    }

    [TestMethod]
    public void ExplicitFifteenFieldLegacyProtocol2ResponseIsRejected()
    {
        // O1: protocol "2" is redefined in place; the passive 15-field shape is no longer readable.
        string legacy = "{\"protocolVersion\":\"2\",\"jobId\":\"" + Job + "\",\"operationDigest\":\"" + Digest + "\",\"nonce\":\"" + Nonce +
            "\",\"invocation\":\"NotStarted\",\"exitCode\":null,\"directProcessExitObserved\":false,\"ownedIoCompleted\":false," +
            "\"pipeEofObserved\":false,\"aggregateTerminationFailed\":false,\"aggregateSettlementConfirmed\":true,\"cancelled\":false," +
            "\"timedOut\":false,\"outputLimitExceeded\":false,\"diagnostic\":\"ok\"}";
        byte[] current = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, NotStartedSettled(), NotAdmitted(), "ok");
        Assert.AreEqual(legacy, Encoding.UTF8.GetString(Join(Split(current).Where(part => Array.IndexOf(Fields, part.Key) is < 14 or 19))));
        Assert.IsFalse(Read(legacy));
        Assert.IsTrue(Read(current));
    }

    [TestMethod]
    public void EachMissingSwappedOrNulledPropertyRejectsTheWholeFrame()
    {
        byte[] frame = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, StartedSettled(), Matched(), "ok");
        List<KeyValuePair<string, string>> parts = Split(frame);
        CollectionAssert.AreEqual(frame, Join(parts));
        for (int i = 0; i < parts.Count; i++)
        {
            Assert.IsFalse(Read(Join(parts.Where((_, index) => index != i))), "missing " + parts[i].Key);
            // Fields 0–15 and 19 are non-nullable. exitCode (5) is nullable only for work that did not start, so it rejects here too;
            // fields 16–18 are nullable but S5 requires all three for Matched.
            List<KeyValuePair<string, string>> nulled = [.. parts];
            nulled[i] = new(parts[i].Key, "null");
            Assert.IsFalse(Read(Join(nulled)), "null " + parts[i].Key);
            if (i + 1 == parts.Count) continue;
            List<KeyValuePair<string, string>> swapped = [.. parts];
            (swapped[i], swapped[i + 1]) = (swapped[i + 1], swapped[i]);
            Assert.IsFalse(Read(Join(swapped)), "swapped " + parts[i].Key);
        }
        byte[] notStarted = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, NotStartedSettled(), NotAdmitted(), "ok");
        foreach (string key in new[] { "qualification", "refusalReason", "diagnostic", "invocation" })
        {
            List<KeyValuePair<string, string>> nulled = Split(notStarted);
            int index = nulled.FindIndex(part => part.Key == key);
            nulled[index] = new(key, "null");
            Assert.IsFalse(Read(Join(nulled)), "null " + key);
        }
    }

    [TestMethod]
    [DataRow("extra")]
    [DataRow("prohibitedHelperQualified")]
    [DataRow("duplicateFirst")]
    [DataRow("duplicateLast")]
    [DataRow("leadingSpace")]
    [DataRow("innerSpace")]
    [DataRow("trailingNewline")]
    [DataRow("trailingComma")]
    [DataRow("comment")]
    [DataRow("byteOrderMark")]
    [DataRow("escapedName")]
    [DataRow("escapedQualification")]
    [DataRow("escapedVersion")]
    [DataRow("escapedFingerprint")]
    [DataRow("loneSurrogateVersion")]
    [DataRow("loneSurrogateFingerprint")]
    [DataRow("loneSurrogateDiagnostic")]
    [DataRow("controlCharacterDiagnostic")]
    [DataRow("booleanAsString")]
    [DataRow("booleanAsZero")]
    [DataRow("booleanAsOne")]
    [DataRow("qualificationAsNumber")]
    [DataRow("reasonAsNumber")]
    [DataRow("versionAsNumber")]
    [DataRow("fingerprintAsBoolean")]
    [DataRow("exitCode2147483648")]
    [DataRow("exitCodeBelowInt32")]
    [DataRow("exitCode0.0")]
    [DataRow("exitCodeNegativeZero")]
    [DataRow("exitCode1e0")]
    [DataRow("exitCodeAsString")]
    [DataRow("exitCodeAsBoolean")]
    [DataRow("emptyVersion")]
    [DataRow("nullAsString")]
    [DataRow("secondFrame")]
    [DataRow("trailingByte")]
    [DataRow("trailingNul")]
    [DataRow("protocol1")]
    [DataRow("protocol3")]
    public void NonCanonicalOrMistypedResponseFramesReject(string mutation)
    {
        string frame = Encoding.UTF8.GetString(ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, StartedSettled(), Matched(), "safe"));
        string mutated = mutation switch
        {
            "extra" => frame.Replace(",\"diagnostic\"", ",\"restartReported\":false,\"diagnostic\"", StringComparison.Ordinal),
            "prohibitedHelperQualified" => frame.Insert(1, "\"helperQualified\":true,"),
            "duplicateFirst" => frame.Insert(1, "\"qualification\":\"Matched\","),
            "duplicateLast" => frame.Replace(",\"diagnostic\"", ",\"refusalReason\":\"None\",\"diagnostic\"", StringComparison.Ordinal),
            "leadingSpace" => " " + frame,
            "innerSpace" => frame.Replace("\"qualification\":", "\"qualification\": ", StringComparison.Ordinal),
            "trailingNewline" => frame + "\n",
            "trailingComma" => frame[..^1] + ",}",
            "comment" => frame.Insert(1, "/*c*/"),
            "byteOrderMark" => "\uFEFF" + frame,
            "escapedName" => frame.Replace("\"qualification\"", "\"\\u0071ualification\"", StringComparison.Ordinal),
            "escapedQualification" => frame.Replace("\"Matched\"", "\"\\u004Datched\"", StringComparison.Ordinal),
            "escapedVersion" => frame.Replace("\"1.2.3\"", "\"1.2.\\u0033\"", StringComparison.Ordinal),
            "escapedFingerprint" => frame.Replace("\"" + Fingerprint, "\"\\u00" + ((int)Fingerprint[0]).ToString("X2", CultureInfo.InvariantCulture) + Fingerprint[1..], StringComparison.Ordinal),
            "loneSurrogateVersion" => frame.Replace("\"1.2.3\"", "\"\\uD800\"", StringComparison.Ordinal),
            "loneSurrogateFingerprint" => frame.Replace("\"" + Fingerprint + "\"", "\"\\uDC00\"", StringComparison.Ordinal),
            "loneSurrogateDiagnostic" => frame.Replace("\"safe\"", "\"\\uD800safe\"", StringComparison.Ordinal),
            "controlCharacterDiagnostic" => frame.Replace("\"safe\"", "\"\\u0007safe\"", StringComparison.Ordinal),
            "booleanAsString" => frame.Replace("\"cancelled\":false", "\"cancelled\":\"false\"", StringComparison.Ordinal),
            "booleanAsZero" => frame.Replace("\"cancelled\":false", "\"cancelled\":0", StringComparison.Ordinal),
            "booleanAsOne" => frame.Replace("\"aggregateSettlementConfirmed\":true", "\"aggregateSettlementConfirmed\":1", StringComparison.Ordinal),
            "qualificationAsNumber" => frame.Replace("\"qualification\":\"Matched\"", "\"qualification\":1", StringComparison.Ordinal),
            "reasonAsNumber" => frame.Replace("\"refusalReason\":\"None\"", "\"refusalReason\":0", StringComparison.Ordinal),
            "versionAsNumber" => frame.Replace("\"observedInstalledVersion\":\"1.2.3\"", "\"observedInstalledVersion\":1.2", StringComparison.Ordinal),
            "fingerprintAsBoolean" => frame.Replace("\"" + Fingerprint + "\"", "true", StringComparison.Ordinal),
            "exitCode2147483648" => frame.Replace("\"exitCode\":0", "\"exitCode\":2147483648", StringComparison.Ordinal),
            "exitCodeBelowInt32" => frame.Replace("\"exitCode\":0", "\"exitCode\":-2147483649", StringComparison.Ordinal),
            "exitCode0.0" => frame.Replace("\"exitCode\":0", "\"exitCode\":0.0", StringComparison.Ordinal),
            "exitCodeNegativeZero" => frame.Replace("\"exitCode\":0", "\"exitCode\":-0", StringComparison.Ordinal),
            "exitCode1e0" => frame.Replace("\"exitCode\":0", "\"exitCode\":1e0", StringComparison.Ordinal),
            "exitCodeAsString" => frame.Replace("\"exitCode\":0", "\"exitCode\":\"0\"", StringComparison.Ordinal),
            "exitCodeAsBoolean" => frame.Replace("\"exitCode\":0", "\"exitCode\":false", StringComparison.Ordinal),
            "emptyVersion" => frame.Replace("\"1.2.3\"", "\"\"", StringComparison.Ordinal),
            "nullAsString" => frame.Replace("\"1.2.3\"", "\"null\"", StringComparison.Ordinal),
            "secondFrame" => frame + frame,
            "trailingByte" => frame + "x",
            "trailingNul" => frame + "\0",
            "protocol1" => frame.Replace("\"protocolVersion\":\"2\"", "\"protocolVersion\":\"1\"", StringComparison.Ordinal),
            "protocol3" => frame.Replace("\"protocolVersion\":\"2\"", "\"protocolVersion\":\"3\"", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        Assert.AreNotEqual(frame, mutated, "mutation did not apply");
        Assert.IsTrue(Read(frame));
        Assert.IsFalse(Read(mutated));
    }

    [TestMethod]
    public void EveryQualificationAndRefusalReasonRoundTripsAndOnlyItsExactSpellingIsAccepted()
    {
        List<(UpdateWireFacts Facts, ReviewedHelperFacts Reviewed)> cases =
        [
            (StartedSettled(), Matched()),
            (NotStartedSettled() with { Cancelled = true }, Undetermined()),
            .. Enum.GetValues<ReviewedHelperRefusalReason>().Where(reason => reason != ReviewedHelperRefusalReason.None)
                .Select(reason => (NotStartedSettled(), RefusedWith(reason)))
        ];
        CollectionAssert.AreEquivalent(Enum.GetValues<ReviewedHelperQualification>(), cases.Select(c => c.Reviewed.Qualification).Distinct().ToArray());
        CollectionAssert.AreEquivalent(Enum.GetValues<ReviewedHelperRefusalReason>(), cases.Select(c => c.Reviewed.RefusalReason).Distinct().ToArray());
        foreach ((UpdateWireFacts facts, ReviewedHelperFacts reviewed) in cases)
        {
            byte[] bytes = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, facts, reviewed, "enum");
            Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(bytes, Job, Digest, Nonce, out FramedUpdateResponse? read), reviewed.ToString());
            Assert.AreEqual(reviewed, read!.Reviewed);
            string frame = Encoding.UTF8.GetString(bytes);
            foreach ((string property, string value, int number) in new[]
            {
                ("qualification", reviewed.Qualification.ToString(), (int)reviewed.Qualification),
                ("refusalReason", reviewed.RefusalReason.ToString(), (int)reviewed.RefusalReason)
            })
            {
                string original = "\"" + property + "\":\"" + value + "\"";
                StringAssert.Contains(frame, original);
                foreach (string variant in new[]
                {
                    value.ToLowerInvariant(), value.ToUpperInvariant(), number.ToString(CultureInfo.InvariantCulture), " " + value, value + " ",
                    value + ", " + value, "Undetermined, " + value, value + "X", "Success", "HelperQualified", "Final"
                })
                {
                    if (variant == value) continue;
                    Assert.IsFalse(Read(frame.Replace(original, "\"" + property + "\":\"" + variant + "\"", StringComparison.Ordinal)), property + "=" + variant);
                }
            }
        }
        Assert.IsFalse(Enum.IsDefined((ReviewedHelperQualification)3));
        Assert.IsFalse(Enum.IsDefined((ReviewedHelperRefusalReason)9));
    }

    [TestMethod]
    public void VersionAndFingerprintLengthBoundariesAcceptTheMaximumAndRejectOneMore()
    {
        Assert.AreEqual(128, Version128.Length);
        Assert.AreEqual(129, Version129.Length);
        ReviewedHelperFacts maximum = Matched() with { ObservedInstalledVersion = Version128, ObservedCandidateVersion = OtherVersion128 };
        RoundTrips(StartedSettled(), maximum);
        foreach (ReviewedHelperFacts tooLong in new[]
        {
            maximum with { ObservedInstalledVersion = Version129 },
            maximum with { ObservedCandidateVersion = Version129 },
            Matched() with { ObservedProfileFingerprint = Fingerprint + "A" },
            Matched() with { ObservedProfileFingerprint = Fingerprint[..63] }
        })
            RejectsOnly(StartedSettled(), tooLong, S12Only);
    }

    [TestMethod]
    [DataRow("1.0+meta")]
    [DataRow("01.0")]
    [DataRow("1.0")]
    [DataRow("1.0.0.0")]
    [DataRow("1.0.0-")]
    [DataRow("")]
    [DataRow(" 1.0.0")]
    [DataRow("1.0.0 ")]
    [DataRow("1.0.0-01")]
    [DataRow("v1.0.0")]
    [DataRow("1..0")]
    [DataRow("1.0.0.0.0")]
    [DataRow("\uFF11.0.0")]
    [DataRow("1.0.0-beta..1")]
    [DataRow("1.0.0-be_ta")]
    [DataRow("2147483648.0.0")]
    [DataRow("1.0.0.0-beta")]
    [DataRow("1.0.0\u00e9")]
    public void NonNormalizedVersionsAreRefusedInEitherVersionField(string version)
    {
        RejectsOnly(StartedSettled(), Matched() with { ObservedInstalledVersion = version }, S12Only);
        RejectsOnly(StartedSettled(), Matched() with { ObservedCandidateVersion = version }, S12Only);
    }

    [TestMethod]
    [DataRow("0.0.0")]
    [DataRow("1.0.0")]
    [DataRow("1.0.0.1")]
    [DataRow("1.0.0-beta")]
    [DataRow("1.0.0-Beta.1")]
    [DataRow("1.0.0-a-b")]
    [DataRow("1.2.3.4-rc.10")]
    [DataRow("2147483647.2147483647.2147483647.2147483647")]
    public void NormalizedVersionsRoundTripInEitherVersionField(string version)
    {
        RoundTrips(StartedSettled(), Matched() with { ObservedInstalledVersion = version });
        RoundTrips(StartedSettled(), Matched() with { ObservedCandidateVersion = version });
    }

    [TestMethod]
    [DataRow("lowercase")]
    [DataRow("63")]
    [DataRow("65")]
    [DataRow("nonHex")]
    [DataRow("empty")]
    [DataRow("prefixed")]
    public void MalformedFingerprintsAreRefused(string form)
    {
        string fingerprint = form switch
        {
            "lowercase" => Fingerprint.ToLowerInvariant(),
            "63" => Fingerprint[..63],
            "65" => Fingerprint + "0",
            "nonHex" => "G" + Fingerprint[1..],
            "empty" => "",
            "prefixed" => "choco-profile-v2:" + Fingerprint,
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
        Assert.AreNotEqual(Fingerprint, fingerprint);
        RejectsOnly(StartedSettled(), Matched() with { ObservedProfileFingerprint = fingerprint }, S12Only);
    }

    [TestMethod]
    [DataRow("Facts-Contradictory")]
    [DataRow("S1-UndefinedQualification")]
    [DataRow("S1-UndefinedReason")]
    [DataRow("S2-RefusedWithNone")]
    [DataRow("S2-MatchedWithReason")]
    [DataRow("S2-UndeterminedWithReason")]
    [DataRow("S3S4-RefusedStarted")]
    [DataRow("S3S4-UndeterminedStarted")]
    [DataRow("S3S4-UndeterminedUnknown")]
    [DataRow("S3S4-RefusedUnknown")]
    [DataRow("S5-MatchedNullCandidate")]
    [DataRow("S5-MatchedNullFingerprint")]
    [DataRow("S5S7-MatchedNullInstalled")]
    [DataRow("S6-NotAdmittedWithInstalled")]
    [DataRow("S6-NotAdmittedWithFingerprint")]
    [DataRow("S6S7-NotAdmittedWithCandidate")]
    [DataRow("S7-CandidateWithPreflightQueryFailed")]
    [DataRow("S7-CandidateWithoutInstalled")]
    [DataRow("S7-StageDVanishedRegistrationKeepsCandidate")]
    [DataRow("S8-SourceNotCommunityWithFingerprint")]
    [DataRow("S9-ProfileMismatchWithoutInstalled")]
    [DataRow("S9-ProfileMismatchWithoutFingerprint")]
    [DataRow("S10-RefusedCancelled")]
    [DataRow("S10-RefusedTimedOut")]
    [DataRow("S10-OutputLimitWithNonQueryRefusal")]
    [DataRow("S11-SettledUndeterminedWithoutFlag")]
    [DataRow("S12-LowercaseFingerprint")]
    [DataRow("S13-MatchedNotStartedUnsettled")]
    public void StructuralPredicateViolationIsRefusedByWriterAndReader(string name)
    {
        (UpdateWireFacts facts, ReviewedHelperFacts reviewed, string[] expected) = NegativeCases[name];
        RejectsOnly(facts, reviewed, expected);
    }

    [TestMethod]
    [DataRow("S2S3-MatchedStartedExit0")]
    [DataRow("S3-MatchedStartedUnsettledNonZeroExit")]
    [DataRow("S3-MatchedUnknown")]
    [DataRow("S4-RefusedNotStarted")]
    [DataRow("S5-MatchedMaximumValues")]
    [DataRow("S6-NotAdmittedAllNull")]
    [DataRow("S7-CandidateMismatchWithDifferingCandidate")]
    [DataRow("S7-StageDMachineInputsWithCandidate")]
    [DataRow("S7-InstalledMismatchAllNull")]
    [DataRow("S8-SourceNotCommunityWithoutFingerprint")]
    [DataRow("S9-ProfileMismatchWithInstalledAndFingerprint")]
    [DataRow("S10-PreflightQueryFailedWithOutputLimit")]
    [DataRow("S10-ApprovalExpired")]
    [DataRow("S11-UndeterminedCancelled")]
    [DataRow("S11-UndeterminedTimedOut")]
    [DataRow("S11-UndeterminedUnsettled")]
    [DataRow("S13-MatchedNotStartedSettledLaunchFailure")]
    [DataRow("S13-MatchedNotStartedSettledCancelled")]
    public void StructurallyValidCaseRoundTripsByteExactly(string name) => RoundTrips(PositiveCases[name].Facts, PositiveCases[name].Reviewed);

    [TestMethod]
    public void StructuralCaseTablesAreFullyExercised()
    {
        // Every S-predicate has at least one negative case that violates it.
        foreach (string id in Enumerable.Range(1, 13).Select(n => "S" + n.ToString(CultureInfo.InvariantCulture)))
            Assert.IsTrue(NegativeCases.Values.Any(c => c.Expected.Contains(id)), id);
        foreach ((UpdateWireFacts facts, ReviewedHelperFacts reviewed) in PositiveCases.Values)
            CollectionAssert.AreEqual(NoViolation, ReviewedUpdateWireCodec.StructuralViolations(facts, reviewed).ToArray());
    }

    [TestMethod]
    public void LargestStructurallyValidFrameIs5083BytesAndRoundTripsByteExactly()
    {
        // §5: NotStarted (exitCode null), all booleans false (an unsettled refusal is structurally valid; the desktop records it
        // Uncertain), Refused/MachineInputsUnsupported from stage D with the candidate retained, 128+128+64 character values,
        // and a 2,048-character diagnostic in which every character is escaped.
        UpdateWireFacts allFalse = NotStartedSettled() with { AggregateSettlementConfirmed = false };
        ReviewedHelperFacts largest = new(ReviewedHelperQualification.Refused, ReviewedHelperRefusalReason.MachineInputsUnsupported,
            Version128, OtherVersion128, Fingerprint);
        byte[] frame = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, allFalse, largest, new string('"', 2048));
        Assert.AreEqual(5_083, frame.Length);
        // The settled variant is exactly one byte shorter ("true" versus "false").
        Assert.HasCount(5_082, ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, NotStartedSettled(), largest, new string('"', 2048)));
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, out FramedUpdateResponse? read));
        Assert.AreEqual(largest, read!.Reviewed);
        Assert.AreEqual(2048, read.Diagnostic.Length);
        CollectionAssert.AreEqual(frame, ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, read.Facts, read.Reviewed, read.Diagnostic));
        // A longer diagnostic is truncated to the same frame, never grown.
        CollectionAssert.AreEqual(frame, ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, allFalse, largest, new string('"', 9000)));
    }

    [TestMethod]
    public void OverBoundAndOversizeDiagnosticFramesReject()
    {
        byte[] frame = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, StartedSettled(), Matched(), "ok");
        byte[] padded = [.. frame, .. Enumerable.Repeat((byte)' ', 16_385 - frame.Length)];
        Assert.AreEqual(16_385, padded.Length);
        Assert.IsFalse(Read(padded));
        Assert.IsFalse(Read(new byte[16_385]));
        Assert.IsFalse(Read(frame[..^1]));
        // A well-formed frame whose diagnostic exceeds 2,048 characters, or a 16,385-byte frame carrying one, is rejected.
        Assert.IsFalse(Read(Forge(StartedSettled(), Matched(), new string('a', 2049))));
        byte[] huge = Forge(StartedSettled(), Matched(), new string('a', 2048 + 16_385 - frame.Length));
        Assert.IsGreaterThan(16_384, huge.Length);
        Assert.IsFalse(Read(huge));
        Assert.IsTrue(Read(Forge(StartedSettled(), Matched(), new string('a', 2048))));
    }

    [TestMethod]
    public void CodecVersionGrammarMatchesChocolateyNativeVersionOverAGeneratedCorpus()
    {
        // O9 differential (choice (a): the codec delegates to ChocolateyNativeVersion; this test pins that acceptance never diverges).
        HashSet<string> corpus = new(StringComparer.Ordinal)
        {
            "", "0", "1", "1.0", "1.0.0", "1.0.0.0", "1.0.0.1", "01.0.0", "1.00.0", "1.0.0-", "-1.0.0", "1.0.0-a", "1.0.0-a.", "1.0.0-.a",
            "1.0.0-0", "1.0.0-00", "1.0.0-01", "1.0.0-0a", "1.0.0-a-", "1.0.0--", "1.0+x", "1.0.0+x", " 1.0.0", "1.0.0 ", "1.0.0\t",
            "1.0.0\u00e9", "\uFF11.0.0", "2147483647.0.0", "2147483648.0.0", "4294967296.0.0", "1.0.0.0.0", "1.0.0.0-a", "1.2.3.4-rc.10",
            "1.0.0-A.B.C", "1.0.0-a_b", "1,0,0", "1.0.0\n", "1.0.0\0", "..", ".1.0", "1.0.", "1.0.0-a..b"
        };
        for (int length = 1; length <= 140; length++)
        {
            corpus.Add("1.0.0-" + new string('a', Math.Max(0, length - 6)));
            corpus.Add("1.0.0." + new string('1', Math.Max(0, length - 6)));
            corpus.Add(new string('9', length));
        }
        ulong state = 0x5083_0128_0064_2048;
        const string Alphabet = "0123456789.-+aZz_ \u00e9";
        for (int i = 0; i < 20_000; i++)
        {
            StringBuilder builder = new();
            if (i % 2 == 0)
            {
                int parts = 1 + (int)(Next(ref state) % 5);
                for (int part = 0; part < parts; part++)
                {
                    if (part > 0) builder.Append('.');
                    int digits = (int)(Next(ref state) % 4);
                    for (int d = 0; d < digits; d++) builder.Append((char)('0' + (int)(Next(ref state) % 10)));
                }
                if (Next(ref state) % 2 == 0)
                {
                    builder.Append('-');
                    int labels = (int)(Next(ref state) % 4);
                    for (int label = 0; label < labels; label++)
                    {
                        if (label > 0) builder.Append('.');
                        int count = (int)(Next(ref state) % 4);
                        for (int c = 0; c < count; c++) builder.Append("0a1Bz-9"[(int)(Next(ref state) % 7)]);
                    }
                }
            }
            else
            {
                int count = (int)(Next(ref state) % 16);
                for (int c = 0; c < count; c++) builder.Append(Alphabet[(int)(Next(ref state) % (ulong)Alphabet.Length)]);
            }
            corpus.Add(builder.ToString());
        }
        foreach (string text in corpus.ToArray())
            if (ChocolateyNativeVersion.TryParse(text, out ChocolateyNativeVersion? parsed)) corpus.Add(parsed!.Normalized);

        int accepted = 0, rejected = 0;
        foreach (string text in corpus)
        {
            bool native = ChocolateyNativeVersion.TryParse(text, out ChocolateyNativeVersion? version) &&
                string.Equals(version!.Normalized, text, StringComparison.Ordinal);
            Assert.AreEqual(native, ReviewedUpdateWireCodec.IsNormalizedVersion(text), text);
            if (native) accepted++;
            else rejected++;
        }
        Assert.IsFalse(ReviewedUpdateWireCodec.IsNormalizedVersion(null));
        Assert.IsGreaterThan(1_000, accepted);
        Assert.IsGreaterThan(1_000, rejected);
    }

    private static readonly Dictionary<string, (UpdateWireFacts Facts, ReviewedHelperFacts Reviewed, string[] Expected)> NegativeCases = new(StringComparer.Ordinal)
    {
        ["Facts-Contradictory"] = (StartedSettled() with { OwnedIoCompleted = false }, Matched(), ["Facts"]),
        ["S1-UndefinedQualification"] = (NotStartedSettled(), Matched() with { Qualification = (ReviewedHelperQualification)7 }, ["S1"]),
        ["S1-UndefinedReason"] = (NotStartedSettled(), RefusedWith(ReviewedHelperRefusalReason.ApprovalExpired) with { RefusalReason = (ReviewedHelperRefusalReason)42 }, ["S1"]),
        ["S2-RefusedWithNone"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.None, null, null, null), ["S2"]),
        ["S2-MatchedWithReason"] = (StartedSettled(), Matched() with { RefusalReason = ReviewedHelperRefusalReason.InstalledMismatch }, ["S2"]),
        ["S2-UndeterminedWithReason"] = (NotStartedSettled() with { Cancelled = true }, Undetermined() with { RefusalReason = ReviewedHelperRefusalReason.ApprovalExpired }, ["S2"]),
        ["S3S4-RefusedStarted"] = (StartedSettled(), Refused(ReviewedHelperRefusalReason.MachineInputsUnsupported, null, null, null), ["S3", "S4"]),
        ["S3S4-UndeterminedStarted"] = (StartedSettled() with { Cancelled = true }, Undetermined(), ["S3", "S4"]),
        ["S3S4-UndeterminedUnknown"] = (Unknown(), Undetermined(), ["S3", "S4"]),
        ["S3S4-RefusedUnknown"] = (Unknown(), Refused(ReviewedHelperRefusalReason.ApprovalExpired, null, null, null), ["S3", "S4"]),
        ["S5-MatchedNullCandidate"] = (StartedSettled(), Matched() with { ObservedCandidateVersion = null }, ["S5"]),
        ["S5-MatchedNullFingerprint"] = (StartedSettled(), Matched() with { ObservedProfileFingerprint = null }, ["S5"]),
        ["S5S7-MatchedNullInstalled"] = (StartedSettled(), Matched() with { ObservedInstalledVersion = null }, ["S5", "S7"]),
        ["S6-NotAdmittedWithInstalled"] = (NotStartedSettled(), NotAdmitted() with { ObservedInstalledVersion = Installed }, ["S6"]),
        ["S6-NotAdmittedWithFingerprint"] = (NotStartedSettled(), NotAdmitted() with { ObservedProfileFingerprint = Fingerprint }, ["S6"]),
        ["S6S7-NotAdmittedWithCandidate"] = (NotStartedSettled(), NotAdmitted() with { ObservedInstalledVersion = Installed, ObservedCandidateVersion = Candidate }, ["S6", "S7"]),
        ["S7-CandidateWithPreflightQueryFailed"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.PreflightQueryFailed, Installed, Candidate, Fingerprint), ["S7"]),
        ["S7-CandidateWithoutInstalled"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.MachineInputsUnsupported, null, Candidate, Fingerprint), ["S7"]),
        // M-B helper fixture: stage D found no installed registration but kept the stage-C candidate; the helper must null it first.
        ["S7-StageDVanishedRegistrationKeepsCandidate"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.InstalledMismatch, null, Candidate, null), ["S7"]),
        ["S8-SourceNotCommunityWithFingerprint"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.SourceNotCommunity, Installed, null, Fingerprint), ["S8"]),
        ["S9-ProfileMismatchWithoutInstalled"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.ProfileMismatch, null, null, Fingerprint), ["S9"]),
        ["S9-ProfileMismatchWithoutFingerprint"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.ProfileMismatch, Installed, null, null), ["S9"]),
        ["S10-RefusedCancelled"] = (NotStartedSettled() with { Cancelled = true }, RefusedWith(ReviewedHelperRefusalReason.ApprovalExpired), ["S10"]),
        ["S10-RefusedTimedOut"] = (NotStartedSettled() with { TimedOut = true }, RefusedWith(ReviewedHelperRefusalReason.MachineInputsUnsupported), ["S10"]),
        ["S10-OutputLimitWithNonQueryRefusal"] = (NotStartedSettled() with { OutputLimitExceeded = true }, RefusedWith(ReviewedHelperRefusalReason.CandidateMismatch), ["S10"]),
        ["S11-SettledUndeterminedWithoutFlag"] = (NotStartedSettled(), Undetermined(), ["S11"]),
        ["S12-LowercaseFingerprint"] = (StartedSettled(), Matched() with { ObservedProfileFingerprint = Fingerprint.ToLowerInvariant() }, ["S12"]),
        ["S13-MatchedNotStartedUnsettled"] = (NotStartedSettled() with { AggregateSettlementConfirmed = false }, Matched(), ["S13"]),
    };

    private static readonly Dictionary<string, (UpdateWireFacts Facts, ReviewedHelperFacts Reviewed)> PositiveCases = new(StringComparer.Ordinal)
    {
        ["S2S3-MatchedStartedExit0"] = (StartedSettled(), Matched()),
        ["S3-MatchedStartedUnsettledNonZeroExit"] = (StartedSettled() with { ExitCode = -2147483648, AggregateSettlementConfirmed = false }, Matched()),
        ["S3-MatchedUnknown"] = (Unknown(), Matched()),
        ["S4-RefusedNotStarted"] = (NotStartedSettled(), RefusedWith(ReviewedHelperRefusalReason.InstalledMismatch)),
        ["S5-MatchedMaximumValues"] = (StartedSettled(), new(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None, Version128, OtherVersion128, Fingerprint)),
        ["S6-NotAdmittedAllNull"] = (NotStartedSettled(), NotAdmitted()),
        ["S7-CandidateMismatchWithDifferingCandidate"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.CandidateMismatch, Installed, "9.9.9", Fingerprint)),
        ["S7-StageDMachineInputsWithCandidate"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.MachineInputsUnsupported, Installed, Candidate, Fingerprint)),
        ["S7-InstalledMismatchAllNull"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.InstalledMismatch, null, null, null)),
        ["S8-SourceNotCommunityWithoutFingerprint"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.SourceNotCommunity, Installed, null, null)),
        ["S9-ProfileMismatchWithInstalledAndFingerprint"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.ProfileMismatch, Installed, null, Fingerprint)),
        ["S10-PreflightQueryFailedWithOutputLimit"] = (NotStartedSettled() with { OutputLimitExceeded = true }, Refused(ReviewedHelperRefusalReason.PreflightQueryFailed, Installed, null, Fingerprint)),
        ["S10-ApprovalExpired"] = (NotStartedSettled(), Refused(ReviewedHelperRefusalReason.ApprovalExpired, Installed, Candidate, Fingerprint)),
        ["S11-UndeterminedCancelled"] = (NotStartedSettled() with { Cancelled = true }, Undetermined()),
        ["S11-UndeterminedTimedOut"] = (NotStartedSettled() with { TimedOut = true }, Undetermined() with { ObservedInstalledVersion = Installed, ObservedProfileFingerprint = Fingerprint }),
        ["S11-UndeterminedUnsettled"] = (NotStartedSettled() with { AggregateSettlementConfirmed = false }, Undetermined()),
        ["S13-MatchedNotStartedSettledLaunchFailure"] = (NotStartedSettled(), Matched()),
        ["S13-MatchedNotStartedSettledCancelled"] = (NotStartedSettled() with { Cancelled = true }, Matched()),
    };

    private static void RoundTrips(UpdateWireFacts facts, ReviewedHelperFacts reviewed)
    {
        CollectionAssert.AreEqual(NoViolation, ReviewedUpdateWireCodec.StructuralViolations(facts, reviewed).ToArray());
        byte[] frame = ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, facts, reviewed, "case");
        CollectionAssert.AreEqual(Forge(facts, reviewed, "case"), frame);
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, out FramedUpdateResponse? read));
        Assert.AreEqual(facts, read!.Facts);
        Assert.AreEqual(reviewed, read.Reviewed);
        CollectionAssert.AreEqual(frame, ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, read.Facts, read.Reviewed, read.Diagnostic));
    }

    private static void RejectsOnly(UpdateWireFacts facts, ReviewedHelperFacts reviewed, string[] expected)
    {
        CollectionAssert.AreEqual(expected, ReviewedUpdateWireCodec.StructuralViolations(facts, reviewed).ToArray(),
            string.Join(",", ReviewedUpdateWireCodec.StructuralViolations(facts, reviewed)));
        Assert.ThrowsExactly<ArgumentException>(() => ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, facts, reviewed, "case"));
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(Forge(facts, reviewed, "case"), Job, Digest, Nonce, out FramedUpdateResponse? read));
        Assert.IsNull(read);
    }

    /// <summary>Writes a frame exactly as the codec would, but without its predicates, so the reader can be fed invalid facts.</summary>
    private static byte[] Forge(UpdateWireFacts facts, ReviewedHelperFacts reviewed, string diagnostic)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("protocolVersion", "2");
            writer.WriteString("jobId", Job.ToString("D", CultureInfo.InvariantCulture));
            writer.WriteString("operationDigest", Digest);
            writer.WriteString("nonce", Nonce);
            writer.WriteString("invocation", facts.Invocation.ToString());
            if (facts.ExitCode is int exit) writer.WriteNumber("exitCode", exit);
            else writer.WriteNull("exitCode");
            writer.WriteBoolean("directProcessExitObserved", facts.DirectProcessExitObserved);
            writer.WriteBoolean("ownedIoCompleted", facts.OwnedIoCompleted);
            writer.WriteBoolean("pipeEofObserved", facts.PipeEofObserved);
            writer.WriteBoolean("aggregateTerminationFailed", facts.AggregateTerminationFailed);
            writer.WriteBoolean("aggregateSettlementConfirmed", facts.AggregateSettlementConfirmed);
            writer.WriteBoolean("cancelled", facts.Cancelled);
            writer.WriteBoolean("timedOut", facts.TimedOut);
            writer.WriteBoolean("outputLimitExceeded", facts.OutputLimitExceeded);
            writer.WriteString("qualification", reviewed.Qualification.ToString());
            writer.WriteString("refusalReason", reviewed.RefusalReason.ToString());
            foreach ((string name, string? value) in new[]
            {
                ("observedInstalledVersion", reviewed.ObservedInstalledVersion),
                ("observedCandidateVersion", reviewed.ObservedCandidateVersion),
                ("observedProfileFingerprint", reviewed.ObservedProfileFingerprint)
            })
            {
                if (value is null) writer.WriteNull(name);
                else writer.WriteString(name, value);
            }
            writer.WriteString("diagnostic", diagnostic);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static List<KeyValuePair<string, string>> Split(byte[] frame)
    {
        using JsonDocument document = JsonDocument.Parse(frame);
        return document.RootElement.EnumerateObject().Select(property => new KeyValuePair<string, string>(property.Name, property.Value.GetRawText())).ToList();
    }

    private static byte[] Join(IEnumerable<KeyValuePair<string, string>> parts) =>
        Encoding.UTF8.GetBytes("{" + string.Join(",", parts.Select(part => "\"" + part.Key + "\":" + part.Value)) + "}");

    private static bool Read(string frame) => Read(Encoding.UTF8.GetBytes(frame));

    private static bool Read(byte[] frame)
    {
        bool result = ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, out FramedUpdateResponse? response);
        Assert.AreEqual(result, response is not null);
        return result;
    }

    private static ulong Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    private static UpdateWireFacts StartedSettled() => new(UpdateWireInvocation.Started, 0, true, true, true, false, true, false, false, false);

    private static UpdateWireFacts NotStartedSettled() =>
        new(UpdateWireInvocation.NotStarted, null, false, false, false, false, true, false, false, false);

    private static UpdateWireFacts Unknown() => new(UpdateWireInvocation.Unknown, null, false, false, false, false, false, false, false, false);

    private static ReviewedHelperFacts Matched() =>
        new(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None, Installed, Candidate, Fingerprint);

    private static ReviewedHelperFacts Undetermined() =>
        new(ReviewedHelperQualification.Undetermined, ReviewedHelperRefusalReason.None, null, null, null);

    private static ReviewedHelperFacts NotAdmitted() => Refused(ReviewedHelperRefusalReason.RequestNotAdmitted, null, null, null);

    private static ReviewedHelperFacts Refused(ReviewedHelperRefusalReason reason, string? installed, string? candidate, string? fingerprint) =>
        new(ReviewedHelperQualification.Refused, reason, installed, candidate, fingerprint);

    /// <summary>A structurally valid refusal for each reason: values present where its predicates allow them.</summary>
    private static ReviewedHelperFacts RefusedWith(ReviewedHelperRefusalReason reason) => reason switch
    {
        ReviewedHelperRefusalReason.RequestNotAdmitted => NotAdmitted(),
        ReviewedHelperRefusalReason.SourceNotCommunity => Refused(reason, Installed, null, null),
        _ => Refused(reason, Installed, null, Fingerprint)
    };
}
