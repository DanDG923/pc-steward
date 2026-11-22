using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// FR-AI-02/05 (ADR-028 consent item 3; Task 67 stage 1): every identifier class ADR-028 names is redacted before a preview, after
/// an edit and in an answer, and useful facts such as versions and error codes survive. Synthetic values only.
/// </summary>
[TestClass]
public sealed class AdvisoryAiRedactionTests
{
    private static readonly DiagnosticRedactionContext Identity = new(["alexpc", "alex", "CONTOSO"]);

    [TestMethod]
    [TestCategory("FR-AI-02")]
    [DataRow("Authorization: Bearer abcDEF123456.xyz", "abcDEF123456", "Bearer [secret]")]
    [DataRow("password=hunter2 and more", "hunter2", "password=[secret]")]
    [DataRow("api_key: 1234abcd", "1234abcd", "api_key: [secret]")]
    [DataRow("client secret=s3cr3tvalue", "s3cr3tvalue", "=[secret]")]
    [DataRow("Serial Number: PF3X9K2Q", "PF3X9K2Q", "Serial Number: [secret]")]
    [DataRow("S/N: 5CD1234XYZ", "5CD1234XYZ", "S/N: [secret]")]
    [DataRow("key sk-proj-ABCDEFGHIJKLMNOPQRSTUVWX was used", "sk-proj-ABCDEFGHIJKLMNOPQRSTUVWX", "[secret]")]
    [DataRow("ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123 leaked", "ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123", "[secret]")]
    [DataRow("aws AKIAABCDEFGHIJKLMNOP", "AKIAABCDEFGHIJKLMNOP", "[secret]")]
    [DataRow("jwt eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.c2lnbmF0dXJl", "eyJhbGciOiJIUzI1NiJ9", "[secret]")]
    [DataRow("product key ABCDE-FGHIJ-KLMNO-PQRST-UVWXY", "ABCDE-FGHIJ", "[secret]")]
    [DataRow("device USB\\VID_046D&PID_C52B\\5&2A3B9C&0&2 failed", "VID_046D", "[id]")]
    [DataRow("id {0F3B2C1D-4E5F-6789-ABCD-0123456789EF} seen", "0F3B2C1D", "[id]")]
    [DataRow("mac 00:1A:2B:3C:4D:5E online", "00:1A:2B", "[id]")]
    [DataRow("hash 0123456789abcdef0123456789abcdef01234567", "0123456789abcdef0123", "[secret]")]
    [DataRow("blob QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVphYmNkZWZnaGlqa2xtbm9w==", "QUJDREVGR0hJSktM", "[secret]")]
    [DataRow("log C:\\Users\\alex\\AppData\\Local\\Temp\\setup.log", "alex", "[path]")]
    [DataRow("share \\\\fileserver\\backups\\pc.zip", "fileserver", "[path]")]
    [DataRow("mail alex@example.com about it", "alex@example.com", "[email]")]
    [DataRow("see https://example.com/x?token=1", "example.com", "[link]")]
    [DataRow("sid S-1-5-21-1004336348-1177238915-682003330-1001", "1004336348", "[account]")]
    [DataRow("computer ALEXPC user alex domain contoso", "ALEXPC", "[redacted]")]
    public void EachIdentifierClassIsRedacted(string text, string mustNotRemain, string placeholder)
    {
        string? clean = AdvisoryAiRedaction.SanitizeEvidence(text, 2048, Identity).Value;
        Assert.IsNotNull(clean);
        Assert.IsFalse(clean.Contains(mustNotRemain, StringComparison.OrdinalIgnoreCase), clean);
        StringAssert.Contains(clean, placeholder);
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    [DataRow("{\"password\":\"hunter2\",\"user\":\"x\"}", "hunter2", "{\"password\":\"[secret]\",\"user\":\"x\"}")]
    [DataRow("password=\"correct horse battery staple\" next", "horse", "password=\"[secret]\" next")]
    [DataRow("api_key = 'two words' next", "words", "api_key = '[secret]' next")]
    [DataRow("token: \"never closed and more", "closed", "token: \"[secret]")]
    [DataRow("{\"client_secret\": \"a, b; c\"}", "a, b; c", "{\"client_secret\": \"[secret]\"}")]
    [DataRow("{\"password\":\"prefix\\\"hunter2\"}", "hunter2", "{\"password\":\"[secret]\"}")]
    [DataRow("secret='it\\'s hunter2' next", "hunter2", "secret='[secret]' next")]
    [DataRow("password=\"C:\\temp\\prefix\\\"hunter2\" next", "hunter2", "password=\"[secret]\" next", DisplayName = "path-like value with an escaped quote")]
    [DataRow("token='C:\\temp\\it\\'s hunter2' next", "hunter2", "token='[secret]' next", DisplayName = "single-quoted path-like value")]
    [DataRow("pass\u200Bword=\"C:\\temp\\x\\\"hunter2\"", "hunter2", "password=\"[secret]\"", DisplayName = "invisible character in the label")]
    public void QuotedNamesAndValuesAreRedactedInFull(string text, string mustNotRemain, string expected)
    {
        string? clean = AdvisoryAiRedaction.SanitizeEvidence(text, 2048, Identity).Value;
        Assert.AreEqual(expected, clean);
        Assert.IsFalse(clean!.Contains(mustNotRemain, StringComparison.Ordinal));
        Assert.AreEqual(clean, AdvisoryAiRedaction.SanitizeEvidence(clean, 2048, Identity).Value, "applying it again changes nothing");
        Assert.AreEqual(expected, AdvisoryAiRedaction.SanitizeAnswer(text, 2048, Identity).Value);
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    [DataRow("token")]
    [DataRow("serial")]
    [DataRow("password")]
    public void AComputerOrAccountNameEqualToASecretLabelCannotShieldTheSecret(string name)
    {
        DiagnosticRedactionContext context = new([name]);
        string text = name + "=hunter2 seen";
        string? evidence = AdvisoryAiRedaction.SanitizeEvidence(text, 2048, context).Value;
        string? answer = AdvisoryAiRedaction.SanitizeAnswer(text, 2048, context).Value;
        Assert.IsFalse(evidence!.Contains("hunter2", StringComparison.Ordinal), evidence);
        Assert.IsFalse(answer!.Contains("hunter2", StringComparison.Ordinal), answer);
        Assert.IsFalse(evidence.Contains(name + "=", StringComparison.Ordinal), "the name itself is still replaced");
        Assert.AreEqual(evidence, AdvisoryAiRedaction.SanitizeEvidence(evidence, 2048, context).Value, "applying it again changes nothing");
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    public void UsefulFactsSurvive()
    {
        const string text = "Installer exited with 0x80070005 (exit code 1603) while updating Contoso Tool 10.0.26100.1234 to 2.15.0; Windows 11 24H2.";
        string? clean = AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value;
        Assert.AreEqual(text, clean);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ControlFormatAndBidirectionalCharactersAreRemoved()
    {
        string? clean = AdvisoryAiRedaction.SanitizeEvidence("pay\u202Eload\u0000 with\u200B hidden\u0007\r\nlines", 2048).Value;
        Assert.AreEqual("payload with hidden lines", clean);
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    public void KeysSplitByInvisibleCharactersAreStillFound()
    {
        string? clean = AdvisoryAiRedaction.SanitizeEvidence("sk-\u200Bproj-ABCDEFGHIJKLMNOP\u2060QRSTUVWX", 2048).Value;
        Assert.AreEqual(AdvisoryAiRedaction.SecretPlaceholder, clean);
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    public void RedactionIsStableWhenAppliedAgain()
    {
        string[] samples =
        [
            "password=x token=y Bearer abcdefghij C:\\Users\\alex\\x.txt alex@example.com {0F3B2C1D-4E5F-6789-ABCD-0123456789EF}",
            new string('a', 5000), "ABCDE-FGHIJ-KLMNO-PQRST-UVWXY and 00-1A-2B-3C-4D-5E",
            "{\"password\": \"a b\", 'secret': 'c d', token=e}"
        ];
        foreach (string sample in samples)
        {
            DiagnosticSanitizedText once = AdvisoryAiRedaction.SanitizeEvidence(sample, 300, Identity);
            DiagnosticSanitizedText twice = AdvisoryAiRedaction.SanitizeEvidence(once.Value, 300, Identity);
            Assert.AreEqual(once.Value, twice.Value);
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    public void LongTextIsCutByScalarsAndMarked()
    {
        DiagnosticSanitizedText cut = AdvisoryAiRedaction.SanitizeEvidence(string.Concat(Enumerable.Repeat("😀 word ", 400)), 50);
        Assert.IsTrue(cut.Truncated);
        Assert.IsLessThanOrEqualTo(50, cut.Value!.EnumerateRunes().Count());
        Assert.IsTrue(cut.Value.EndsWith(DiagnosticTextSanitizer.Ellipsis, StringComparison.Ordinal));
        Assert.IsTrue(AdvisoryAiRedaction.SanitizeEvidence(new string('x', AdvisoryAiRedaction.MaximumInputScalars + 10), 2048).Truncated);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnAnswerIsCutBeforeItIsCleanedAtALineBreakWhereThereIsOne()
    {
        // Task 71C review 71C-R2: the raw text is cut to the bound before cleaning, so cleaning time follows the bound, not the reply.
        Assert.AreEqual(("short", false), AdvisoryAiRedaction.BoundAnswer("short", 10));
        Assert.AreEqual(("line one", true), AdvisoryAiRedaction.BoundAnswer("line one\nline two", 12), "cut at the line break");
        Assert.AreEqual(("abcdefghij", true), AdvisoryAiRedaction.BoundAnswer("abcdefghijklmnop", 10), "no line break: cut at the bound");
        Assert.AreEqual(("a\nbcdefghi", true), AdvisoryAiRedaction.BoundAnswer("a\nbcdefghijklmnop", 10), "a break in the first half is not used");
        (string surrogate, bool cut) = AdvisoryAiRedaction.BoundAnswer("abcdefghi\U0001F600z", 10);
        Assert.AreEqual("abcdefghi", surrogate, "a surrogate pair is never split");
        Assert.IsTrue(cut);
        string huge = new(' ', 3_000_000);
        (string bounded, bool hugeCut) = AdvisoryAiRedaction.BoundAnswer(huge, 393_216);
        Assert.AreEqual(393_216, bounded.Length);
        Assert.IsTrue(hugeCut);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnAnswerKeepsEachLinesIndentationSoCodeKeepsItsMeaning()
    {
        // Task 71C review 71C-04: leading spaces survive redaction (spaces inside a line still collapse), capped, and the result is
        // unchanged when it is sanitized again, as a saved answer is on every read.
        const string answer = "Try this:\n```python\nif ready:\n    run(  1 )\n        log(\"C:\\Users\\alex\\x.log\")\n```";
        string? clean = AdvisoryAiRedaction.SanitizeAnswer(answer, 4000, Identity).Value;
        Assert.AreEqual("Try this:\n```python\nif ready:\n    run( 1 )\n        log(\"[path]\")\n```", clean);
        Assert.AreEqual(clean, AdvisoryAiRedaction.SanitizeAnswer(clean, 4000, Identity).Value);
        string deep = AdvisoryAiRedaction.SanitizeAnswer(new string(' ', 500) + "x", 4000).Value!;
        Assert.AreEqual(new string(' ', AdvisoryAiRedaction.MaximumAnswerIndentation) + "x", deep);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnAnswerKeepsItsParagraphsAndIsRedactedLineByLine()
    {
        const string answer = "What the evidence shows:\r\n\r\n\r\nThe log at C:\\Users\\alex\\setup.log failed.\nRun this: password=abc\u202E\n\n";
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeAnswer(answer, 4000, Identity);
        Assert.AreEqual("What the evidence shows:\n\nThe log at [path] failed.\nRun this: password=[secret]", clean.Value);
        Assert.IsNull(AdvisoryAiRedaction.SanitizeAnswer(" \n\t\n", 100).Value);
        Assert.IsTrue(AdvisoryAiRedaction.SanitizeAnswer(string.Concat(Enumerable.Repeat("word ", 100)), 100).Truncated);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ALongAnswerLineKeepsEverythingTheLimitAdmitsAndReportsAnyCut()
    {
        string line = string.Concat(Enumerable.Repeat("word ", 4000)).TrimEnd();
        DiagnosticSanitizedText whole = AdvisoryAiRedaction.SanitizeAnswer(line, 30_000);
        Assert.AreEqual(line, whole.Value);
        Assert.IsFalse(whole.Truncated);

        DiagnosticSanitizedText cut = AdvisoryAiRedaction.SanitizeAnswer("intro\n" + line + "\nend", 10_000);
        Assert.IsTrue(cut.Truncated);
        Assert.AreEqual(10_000, cut.Value!.EnumerateRunes().Count());

        string tooLong = new('a', (int)AdvisoryAiResourcePolicy.MaximumResponseBytes + 1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AdvisoryAiRedaction.SanitizeAnswer(tooLong, 100));
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Use this:\npassword:\nhunter2\nthen restart.", "Use this:\npassword:\n[secret]\nthen restart.")]
    [DataRow("token =\r\n  hunter2", "token =\n  [secret]")]
    [DataRow("{\"password\": \"line one\nhunter2 line two\"}\nNext.", "{\"password\": \"[secret]\"}\nNext.")]
    [DataRow("Authorization: Bearer\nabcDEF123456xyz", "Authorization: Bearer [secret]")]
    [DataRow("pass\u200Bword:\nhunter2", "password:\n[secret]")]
    [DataRow("to\u2060ken =\u2028hunter2\rnext", "token =\n[secret]\nnext")]
    [DataRow("api\u00ADkey:\u0085\u200Bhunter2", "apikey:\n[secret]")]
    public void ASecretSplitAcrossAnswerLinesIsStillRemoved(string answer, string expected)
    {
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeAnswer(answer, 4000);
        Assert.AreEqual(expected, clean.Value);
        Assert.IsFalse(clean.Value!.Contains("hunter2", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void LinksInAnAnswerCannotBeOpened() =>
        Assert.AreEqual("Download the fix from [link] now.", AdvisoryAiRedaction.SanitizeAnswer("Download the fix from https://evil.example/fix.exe now.", 1000).Value);

    [TestMethod]
    [TestCategory("FR-AI-02")]
    [DataRow("Authorization: Basic dXNlcjpodW50ZXIy", "dXNlcjpodW50ZXIy", "Authorization: Basic [secret]")]
    [DataRow("header basic YWJjOmRlZg== sent", "YWJjOmRlZg", "basic [secret]")]
    [DataRow("password is: hunter2", "hunter2", "password is: [secret]")]
    [DataRow("PIN: 4821", "4821", "PIN: [secret]")]
    [DataRow("passcode=99887766", "99887766", "passcode=[secret]")]
    [DataRow("wifi psk: correcthorse", "correcthorse", "psk: [secret]")]
    [DataRow("pre-shared key = s3cr3t", "s3cr3t", "pre-shared key = [secret]")]
    [DataRow("link-local fe80::1c2b:3a4d:5e6f:7a8b%12 down", "1c2b:3a4d", "[address]")]
    [DataRow("addr 2001:0db8:85a3:0000:0000:8a2e:0370:7334 seen", "85a3", "[address]")]
    [DataRow("IP address: 192.168.1.23 unreachable", "192.168.1.23", "IP address: [address]")]
    [DataRow("gateway: 10.0.0.1 timed out", "10.0.0.1", "gateway: [address]")]
    [DataRow("IP 10.1.2.3 refused", "10.1.2.3", "IP [address]")]
    [DataRow("IP address: ::ffff:c0a8:117", "c0a8", "IP address: [address]", DisplayName = "leading-compression IPv6")]
    [DataRow("mapped ::ffff:192.168.1.23 seen", "192.168", "mapped [address]", DisplayName = "IPv4-mapped IPv6")]
    [DataRow("peer ::abcd:ef12 reset", "abcd:ef12", "peer [address]")]
    [DataRow("dns server=8.8.4.4:53", "8.8.4.4", "dns server=[address]")]
    public void IndependentReviewRedactionGapsAreClosed(string text, string mustNotRemain, string placeholder)
    {
        // Independent review F4: Basic credentials, "is:" and PIN, passcode and pre-shared-key labels, IPv6 and labelled IPv4 addresses.
        string? clean = AdvisoryAiRedaction.SanitizeEvidence(text, 2048, Identity).Value;
        Assert.IsNotNull(clean);
        Assert.IsFalse(clean.Contains(mustNotRemain, StringComparison.OrdinalIgnoreCase), clean);
        StringAssert.Contains(clean, placeholder);
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    [DataRow("Updated from 8.0.33.0 to 8.0.34.0 on build 10.0.19045.5737")]
    [DataRow("Follow the basic steps in the guide")]
    [DataRow("The pinned version stays; spin up the service")]
    [DataRow("At 12:30:45 the std::vector call failed")]
    [DataRow("SQL Server 8.0.33.0 stopped responding")]
    [DataRow("SQL Server 8.0.300.0 stopped responding")]
    [DataRow("host 4.2.1.0 failed")]
    [DataRow("Product: TeamViewer Host 15.71.5.0 -- Installation failed.")]
    [DataRow("Zoom Client 6.2.11.5 needs a restart")]
    [DataRow("address: 8.0.300.0 is not an address")]
    public void VersionsTimesAndOrdinaryWordsAreNotTakenForAddressesOrSecrets(string text) =>
        Assert.AreEqual(text, AdvisoryAiRedaction.SanitizeEvidence(text, 2048, Identity).Value);
}
