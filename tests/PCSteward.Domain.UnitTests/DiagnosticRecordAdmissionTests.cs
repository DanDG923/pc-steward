using System.Text;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// FR-DIA-03 (ADR-010/016/027, Task 64): hostile source text is data. Sanitization runs before anything is kept, records are
/// limited to the closed source/kind sets and to 4 KiB of canonical UTF-8 JSON, and no path, link, SID, email address or
/// supplied device/account name survives.
/// </summary>
[TestClass]
public sealed class DiagnosticRecordAdmissionTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly DiagnosticRedactionContext Identity = new(["DESKTOP-SYN123", "alice"]);

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    [DataRow(@"Faulting path C:\Users\alice\AppData\Local\Contoso\widget.exe stopped", "Faulting path [path] stopped")]
    [DataRow(@"Module C:\Program Files\Contoso App\bin\core.dll crashed", "Module [path] crashed")]
    [DataRow(@"Share \\fileserver\team\setup.msi failed", "Share [path] failed")]
    [DataRow(@"Profile %LOCALAPPDATA%\Contoso\cache missing", "Profile [path] missing")]
    [DataRow("See https://example.invalid/report?id=1&user=alice now", "See [link] now")]
    [DataRow("Account S-1-5-21-1004336348-1177238915-682003330-1001 denied", "Account [account] denied")]
    [DataRow("Contact someone@example.invalid today", "Contact [email] today")]
    [DataRow("Host DESKTOP-SYN123 user ALICE", "Host [redacted] user [redacted]")]
    [DataRow("alicent and malice stay", "alicent and malice stay")]
    public void RedactsPathsLinksAccountsAndIdentity(string input, string expected) =>
        Assert.AreEqual(expected, DiagnosticTextSanitizer.Sanitize(input, 512, Identity).Value);

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void RedactsSingleCharacterIdentityTokensAsWholeWords()
    {
        DiagnosticRedactionContext shortName = new(["x"]);
        Assert.AreEqual("User [redacted] signed in", DiagnosticTextSanitizer.Sanitize("User x signed in", 512, shortName).Value);
        Assert.AreEqual("Box xylophone stays", DiagnosticTextSanitizer.Sanitize("Box xylophone stays", 512, shortName).Value);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void IdentityTokensCannotBreakAPathBeforeItIsRedacted()
    {
        DiagnosticRedactionContext driveLetter = new(["c", "someone"]);
        Assert.AreEqual("[path] failed for [redacted]", DiagnosticTextSanitizer.Sanitize(@"C:\Users\someone\private\widget.exe failed for someone", 512, driveLetter).Value);
        Assert.AreEqual("Mail [email] link [link]", DiagnosticTextSanitizer.Sanitize("Mail someone@example.invalid link https://c.example.invalid/someone", 512, driveLetter).Value);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void RemovesControlFormatAndBidirectionalCharactersAndCollapsesWhitespace()
    {
        string hostile = "Line1\r\n\tLine2\u0000\u0007 \u202Eevil\u202C \u200Bzero\u2028next   end";
        Assert.AreEqual("Line1 Line2 evil zero next end", DiagnosticTextSanitizer.Sanitize(hostile, 512).Value);
        Assert.IsNull(DiagnosticTextSanitizer.Sanitize(" \r\n\t ", 512).Value);
        Assert.IsNull(DiagnosticTextSanitizer.Sanitize(null, 512).Value);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void TruncatesByUnicodeScalarWithoutSplittingACharacter()
    {
        string emoji = string.Concat(Enumerable.Repeat("\U0001F600", 600));
        DiagnosticSanitizedText result = DiagnosticTextSanitizer.Sanitize(emoji, 512);

        Assert.IsTrue(result.Truncated);
        Assert.AreEqual(512, result.Value!.EnumerateRunes().Count());
        Assert.IsTrue(result.Value.EndsWith(DiagnosticTextSanitizer.Ellipsis, StringComparison.Ordinal));
        Assert.IsFalse(result.Value.EnumerateRunes().Any(rune => rune == Rune.ReplacementChar), "no surrogate pair is split");
        Assert.IsFalse(DiagnosticTextSanitizer.Sanitize(new string('a', 512), 512).Truncated);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void LoneSurrogatesBecomeAPlainMarker()
    {
        string broken = "ok\uD800done";
        Assert.AreEqual("ok?done", DiagnosticTextSanitizer.Sanitize(broken, 512).Value);
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    [DataRow(DiagnosticSourceKind.SystemEventLog, DiagnosticEventKind.ApplicationCrash)]
    [DataRow(DiagnosticSourceKind.ApplicationEventLog, DiagnosticEventKind.UnexpectedShutdown)]
    [DataRow(DiagnosticSourceKind.Reliability, DiagnosticEventKind.PcStewardUpdate)]
    [DataRow(DiagnosticSourceKind.WindowsUpdateHistory, DiagnosticEventKind.SoftwareInstalled)]
    [DataRow(DiagnosticSourceKind.PcStewardHistory, DiagnosticEventKind.WindowsUpdate)]
    public void EachSourceMayOnlyProduceItsOwnEventKinds(DiagnosticSourceKind source, DiagnosticEventKind kind) =>
        Assert.AreEqual(DiagnosticRejection.KindNotAllowedForSource, DiagnosticRecordAdmission.Admit(Candidate(source, kind)).Rejection);

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void RejectsInvalidIdentityEventIdTimeAndUndefinedValues()
    {
        DiagnosticRecordCandidate valid = Candidate(DiagnosticSourceKind.SystemEventLog, DiagnosticEventKind.UnexpectedShutdown);

        Assert.IsTrue(DiagnosticRecordAdmission.Admit(valid).Admitted);
        Assert.AreEqual(DiagnosticRejection.InvalidRecordId, DiagnosticRecordAdmission.Admit(valid with { SourceRecordId = @"C:\Windows\x" }).Rejection);
        Assert.AreEqual(DiagnosticRejection.InvalidRecordId, DiagnosticRecordAdmission.Admit(valid with { SourceRecordId = new string('1', 129) }).Rejection);
        Assert.AreEqual(DiagnosticRejection.InvalidEventId, DiagnosticRecordAdmission.Admit(valid with { EventId = 70000 }).Rejection);
        Assert.AreEqual(DiagnosticRejection.InvalidEventId, DiagnosticRecordAdmission.Admit(valid with { EventId = -1 }).Rejection);
        Assert.AreEqual(DiagnosticRejection.InconsistentTime, DiagnosticRecordAdmission.Admit(valid with { Occurrence = new(null, null, DiagnosticTimeQuality.Known) }).Rejection);
        Assert.AreEqual(DiagnosticRejection.InconsistentTime, DiagnosticRecordAdmission.Admit(valid with { Occurrence = new(At, null, DiagnosticTimeQuality.Unknown) }).Rejection);
        Assert.AreEqual(DiagnosticRejection.InconsistentTime, DiagnosticRecordAdmission.Admit(valid with { Occurrence = new(At.ToOffset(TimeSpan.FromHours(2)), null, DiagnosticTimeQuality.Known) }).Rejection);
        Assert.AreEqual(DiagnosticRejection.UndefinedValue, DiagnosticRecordAdmission.Admit(valid with { Kind = (DiagnosticEventKind)99 }).Rejection);
        Assert.AreEqual(DiagnosticRejection.UndefinedValue, DiagnosticRecordAdmission.Admit(valid with { Outcome = (DiagnosticOutcomeKind)99 }).Rejection);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void SanitizesEveryTextFieldBeforeTheRecordExists()
    {
        DiagnosticRecordCandidate candidate = Candidate(DiagnosticSourceKind.ApplicationEventLog, DiagnosticEventKind.ApplicationCrash) with
        {
            Subject = @"widget.exe at C:\Users\alice\widget.exe",
            Detail = "on DESKTOP-SYN123",
            Summary = "\u202Ereport https://example.invalid/x"
        };

        DiagnosticRecord record = DiagnosticRecordAdmission.Admit(candidate, Identity).Record!;

        Assert.AreEqual("widget.exe at [path]", record.Subject);
        Assert.AreEqual("on [redacted]", record.Detail);
        Assert.AreEqual("report [link]", record.Summary);
        string json = DiagnosticRecordJson.Serialize(record);
        Assert.IsFalse(json.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("DESKTOP-SYN123", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void OversizedRecordsAreCutToFourKibibytesOfCanonicalJson()
    {
        string wide = string.Concat(Enumerable.Repeat("\U0001F600", 600));
        DiagnosticRecordCandidate candidate = Candidate(DiagnosticSourceKind.Reliability, DiagnosticEventKind.OtherReliabilityProblem) with { Subject = wide, Detail = wide, Summary = wide };

        DiagnosticAdmission admission = DiagnosticRecordAdmission.Admit(candidate);

        Assert.IsTrue(admission.Admitted);
        Assert.IsLessThanOrEqualTo(DiagnosticCollectionPolicy.MaximumRecordBytes, admission.SerializedBytes);
        Assert.AreEqual(Encoding.UTF8.GetByteCount(DiagnosticRecordJson.Serialize(admission.Record!)), admission.SerializedBytes, "counted bytes are the bytes written");
        Assert.IsTrue(admission.Record!.TextTruncated);
        Assert.AreEqual(DiagnosticCollectionPolicy.MaximumSubjectScalars, admission.Record.Subject!.EnumerateRunes().Count());
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    public void RecordsWithoutASourceIdentityGetADeterministicWeakIdentity()
    {
        DiagnosticRecordCandidate candidate = Candidate(DiagnosticSourceKind.Reliability, DiagnosticEventKind.ApplicationHang) with { SourceRecordId = null, Subject = "Contoso" };

        DiagnosticRecord first = DiagnosticRecordAdmission.Admit(candidate).Record!;
        DiagnosticRecord second = DiagnosticRecordAdmission.Admit(candidate).Record!;
        DiagnosticRecord other = DiagnosticRecordAdmission.Admit(candidate with { Subject = "Fabrikam" }).Record!;

        Assert.IsTrue(first.WeakIdentity);
        Assert.AreEqual(first.SourceRecordId, second.SourceRecordId);
        Assert.AreNotEqual(first.SourceRecordId, other.SourceRecordId);
        Assert.IsTrue(DiagnosticRecordAdmission.IsValidRecordId(first.SourceRecordId));
    }

    [TestMethod]
    [TestCategory("FR-DIA-05")]
    public void CanonicalJsonHoldsOnlyTheMinimalFieldsInAFixedOrder()
    {
        DiagnosticRecord record = DiagnosticRecordAdmission.Admit(Candidate(DiagnosticSourceKind.SystemEventLog, DiagnosticEventKind.UnexpectedShutdown) with { ResultCode = 0x80070005 }).Record!;

        Assert.AreEqual(
            "{\"source\":\"SystemEventLog\",\"id\":\"System:1234\",\"utc\":\"2026-11-12T09:00:00.0000000Z\",\"offsetMinutes\":0,\"time\":\"Known\",\"kind\":\"UnexpectedShutdown\",\"outcome\":\"NotApplicable\",\"eventId\":41,\"code\":2147942405}",
            DiagnosticRecordJson.Serialize(record));
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void StoredRecordsRoundTripOnlyInExactCanonicalForm()
    {
        DiagnosticRecord record = DiagnosticRecordAdmission.Admit(Candidate(DiagnosticSourceKind.Reliability, DiagnosticEventKind.ApplicationHang) with
        {
            SourceRecordId = null,
            Subject = "Contoso \"Widget\" \u00e9\U0001F600",
            Summary = new string('x', 600),
            ResultCode = -2147024891
        }).Record!;
        string json = DiagnosticRecordJson.Serialize(record);

        Assert.IsTrue(DiagnosticRecordJson.TryParse(json, out DiagnosticRecord? parsed));
        Assert.AreEqual(record, parsed);

        string[] refused =
        [
            json.Replace("{\"source\"", "{ \"source\"", StringComparison.Ordinal),
            json.Replace("\"kind\":\"ApplicationHang\"", "\"kind\":\"1\"", StringComparison.Ordinal),
            json.Replace("\"kind\":\"ApplicationHang\"", "\"kind\":\"applicationhang\"", StringComparison.Ordinal),
            json.Replace("{\"source\"", "{\"extra\":true,\"source\"", StringComparison.Ordinal),
            json.Replace("{\"source\":\"Reliability\"", "{\"source\":\"Reliability\",\"source\":\"Reliability\"", StringComparison.Ordinal),
            json.Replace("\"kind\":\"ApplicationHang\"", "\"kind\":\"PcStewardUpdate\"", StringComparison.Ordinal),
            json.Replace("Z\"", "+00:00\"", StringComparison.Ordinal),
            json.Replace("\"subject\":\"Contoso", "\"subject\":\"C:\\\\Users\\\\x Contoso", StringComparison.Ordinal),
            "[]",
            "",
            "{" + new string(' ', 5000) + "}"
        ];
        foreach (string value in refused)
            Assert.IsFalse(DiagnosticRecordJson.TryParse(value, out _), value.Length > 200 ? value[..200] : value);
    }

    private static DiagnosticRecordCandidate Candidate(DiagnosticSourceKind source, DiagnosticEventKind kind) =>
        new(source, "System:1234", new(At, TimeSpan.Zero, DiagnosticTimeQuality.Known), kind, DiagnosticOutcomeKind.NotApplicable, EventId: 41);
}
