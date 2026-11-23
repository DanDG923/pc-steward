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
        Assert.IsTrue(AdvisoryAiRedaction.SanitizeEvidence(string.Concat(Enumerable.Repeat("log ", AdvisoryAiRedaction.MaximumInputScalars)), 2048).Truncated);
        // Independent review of ce0eaf3, F1: a cut never falls inside a word, so one word longer than the bound keeps nothing.
        Assert.IsNull(AdvisoryAiRedaction.SanitizeEvidence(new string('x', AdvisoryAiRedaction.MaximumInputScalars + 10), 2048).Value);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnAnswerIsCutBeforeItIsCleanedAtALineBreakWhereThereIsOne()
    {
        // Task 71C review 71C-R2: the raw text is cut to the bound before cleaning, so cleaning time follows the bound, not the reply.
        Assert.AreEqual(("short", false), AdvisoryAiRedaction.BoundAnswer("short", 10));
        Assert.AreEqual(("line one", true), AdvisoryAiRedaction.BoundAnswer("line one\nline two", 12), "cut at the line break");
        Assert.AreEqual(("abc", true), AdvisoryAiRedaction.BoundAnswer("abc defghijklmnop", 10), "no line break: cut at the last space");
        Assert.AreEqual(("a\nbc", true), AdvisoryAiRedaction.BoundAnswer("a\nbc defghijklmnop", 10), "a break in the first half is not used");
        Assert.AreEqual(("abc defghi", true), AdvisoryAiRedaction.BoundAnswer("abc defghi jkl", 10), "the word inside the bound is whole");
        Assert.AreEqual((string.Empty, true), AdvisoryAiRedaction.BoundAnswer("abcdefghi\U0001F600z", 10), "one word longer than the bound: nothing kept");
        string huge = new(' ', 3_000_000);
        (string bounded, bool hugeCut) = AdvisoryAiRedaction.BoundAnswer(huge, 393_216);
        Assert.AreEqual(393_216, bounded.Length);
        Assert.IsTrue(hugeCut);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("See AAAAA-BBBBB-CCCCC-DDDDD-EEEEE", DisplayName = "a product key")]
    [DataRow("See 0123456789abcdef0123456789abcdef0123", DisplayName = "a long hex value")]
    [DataRow("See QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVphYmNkZWZn", DisplayName = "a long base64 value")]
    public void ACutNeverLeavesPartOfASecretThatIsKnownByItsShape(string answer)
    {
        // Codex review of #141: cutting one character off an unlabelled secret would leave a prefix its pattern no longer matches.
        Assert.AreEqual("See [secret]", AdvisoryAiRedaction.SanitizeAnswer(answer, 4000).Value?.Replace("[product key]", "[secret]", StringComparison.Ordinal), "whole, it is redacted");
        for (int bound = 5; bound < answer.Length; bound++)
        {
            (string cut, _) = AdvisoryAiRedaction.BoundAnswer(answer, bound);
            string? clean = AdvisoryAiRedaction.SanitizeAnswer(cut, 4000).Value;
            Assert.IsFalse(clean?.Contains("AAAAA", StringComparison.Ordinal) == true || clean?.Contains("0123", StringComparison.Ordinal) == true ||
                clean?.Contains("QUJD", StringComparison.Ordinal) == true, $"bound {bound}: {clean}");
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("AAAAA-BBBBB-CCCCC-DDDDD-EEEEE", DisplayName = "a product key")]
    [DataRow("0123456789abcdef0123456789abcdef0123", DisplayName = "a long hex value")]
    [DataRow("QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVphYmNkZWZn", DisplayName = "a long base64 value")]
    [DataRow("6f9619ff-8b86-d011-b42d-00c04fc964ff", DisplayName = "a GUID")]
    [DataRow("someone.person@example.com", DisplayName = "an e-mail address")]
    [DataRow("alexpc", DisplayName = "the computer name")]
    public void AnOverLongEvidenceEditNeverKeepsPartOfASecret(string value)
    {
        // Independent review of ce0eaf3, F1: an owner edit over 16,384 characters was cut there, inside a word, before it was
        // cleaned. A log of long paths shrinks to a fraction when cleaned, so the start of a value cut part-way survived into the
        // text to send. The cut now drops the unfinished word.
        const string path = @"C:\Program Files\Contoso\Editor\plugins\component\library.dll ";
        for (int offset = 1; offset < value.Length; offset++)
        {
            string head = string.Concat(Enumerable.Repeat(path, ((16 * 1024) - offset - 1) / path.Length));
            string text = head + new string('x', (16 * 1024) - offset - head.Length - 1) + " " + value + " more log text";
            Assert.AreEqual((16 * 1024) - offset, text.IndexOf(value, StringComparison.Ordinal));
            DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(text, 2048, Identity);
            Assert.IsNotNull(clean.Value);
            Assert.IsTrue(clean.Truncated);
            Assert.IsLessThan(2048, clean.Value.Length, "the paths shrank, so the end of the cut text is kept");
            if (offset >= 3) Assert.DoesNotContain(value[..offset], clean.Value, $"cut {offset} characters into the value");
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ACutBetweenTheWordsOfANameDropsTheFirstWordsToo()
    {
        // Independent review of ce0eaf3, F3: a name of several words cut between them would keep its first word.
        DiagnosticRedactionContext twoWords = new(["Jo Bloggs"]);
        string answer = new string('a', 20) + " signed in as Jo Bloggs today";
        int bound = answer.IndexOf("Bloggs", StringComparison.Ordinal) + 2;
        (string cut, bool wasCut) = AdvisoryAiRedaction.BoundAnswer(answer, bound, twoWords);
        Assert.IsTrue(wasCut);
        Assert.EndsWith("signed in as", cut);
        string evidence = string.Concat(Enumerable.Repeat("log ", 4095)) + "Jo Bloggs";
        Assert.AreEqual(16 * 1024, evidence.IndexOf("Bloggs", StringComparison.Ordinal) + 1, "the bound falls inside the second word");
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(evidence, 16 * 1024, twoWords);
        Assert.IsTrue(clean.Truncated);
        Assert.EndsWith("log", clean.Value!.TrimEnd('…'));
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("\"C:\\Data\\Project Amber Notes.txt\"", DisplayName = "quoted file name with spaces")]
    [DataRow("'D:\\Shared\\Project Amber Notes.txt'", DisplayName = "single-quoted file name with spaces")]
    [DataRow("C:\\Project Amber\\Notes\\plan.txt", DisplayName = "unquoted path with a space in a folder")]
    [DataRow("\\\\server\\Project Amber\\Notes\\plan.txt", DisplayName = "network path with a space in a folder")]
    [DataRow("%USERPROFILE%\\Project Amber\\Notes\\plan.txt", DisplayName = "variable path with a space in a folder")]
    public void ACutNeverSplitsAPathThatHoldsSpaces(string path)
    {
        // Codex review of ce0eaf3, C3: a cut at a space inside a path left its first words as a shorter path and "Amber" as plain
        // text. Every cut (an answer, a saved answer, an evidence item and the timeline's working bound) moves before the path.
        string answer = "See " + path + " for details.";
        Assert.DoesNotContain("Amber", AdvisoryAiRedaction.SanitizeAnswer(answer, 4000).Value!, "the whole path is removed");
        int split = answer.IndexOf(" Notes", StringComparison.Ordinal) > 0 ? answer.IndexOf(" Notes", StringComparison.Ordinal) : answer.IndexOf("\\Notes", StringComparison.Ordinal);
        for (int bound = answer.IndexOf("Amber", StringComparison.Ordinal) + 1; bound < answer.Length - 1; bound++)
        {
            (string cut, bool wasCut) = AdvisoryAiRedaction.BoundAnswer(answer, bound);
            Assert.IsTrue(wasCut);
            string? shown = AdvisoryAiRedaction.SanitizeAnswer(cut, 4000).Value;
            Assert.DoesNotContain("Amber", shown ?? string.Empty, $"answer cut at {bound}");
        }
        Assert.IsGreaterThan(0, split);

        string evidence = new string(' ', (16 * 1024) - path.IndexOf(" Notes", StringComparison.Ordinal) - 4) + "See " + path + " tail";
        if (!path.Contains(" Notes", StringComparison.Ordinal))
            evidence = new string(' ', (16 * 1024) - path.IndexOf("\\Notes", StringComparison.Ordinal) - 4) + "See " + path + " tail";
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(evidence, 2048);
        Assert.IsTrue(clean.Truncated);
        Assert.DoesNotContain("Amber", clean.Value ?? string.Empty, "evidence");

        string timeline = new string('a', 10) + new string(' ', (16 * 1024) - 14 - answer.IndexOf("Amber", StringComparison.Ordinal)) + answer + " tail";
        DiagnosticSanitizedText record = DiagnosticTextSanitizer.Sanitize(timeline, 4096);
        Assert.IsTrue(record.Truncated);
        Assert.DoesNotContain("Amber", record.Value ?? string.Empty, "timeline");
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("\n", DisplayName = "a line break")]
    [DataRow("\r\n", DisplayName = "a Windows line break")]
    [DataRow("\u2028", DisplayName = "a line separator")]
    [DataRow("\t", DisplayName = "a tab")]
    public void AnEvidenceCutNeverSplitsAPathWrappedOverALineBreak(string wrap)
    {
        // Independent review of 4056e3f, G1: the evidence cut was chosen on the raw text, where a path wrapped over a line break is
        // two pieces, but cleaning makes the break a space, so the cut kept the first words as a shorter path and the next word as
        // plain text. The cut is now chosen on the text as cleaning sees it.
        string path = "C:\\Clients\\Hartwell" + wrap + "Family Trust\\Accounts\\ledger.xlsx";
        int first = path.IndexOf("Hartwell", StringComparison.Ordinal);
        for (int bound = first + 1; bound < path.Length; bound++)
        {
            string evidence = new string(' ', (16 * 1024) - 4 - bound) + "See " + path + " tail";
            Assert.AreEqual(16 * 1024, evidence.IndexOf(path, StringComparison.Ordinal) + bound);
            DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(evidence, 2048);
            Assert.IsTrue(clean.Truncated);
            foreach (string word in (string[])["Hartwell", "Family", "Trust", "Accounts"])
                Assert.DoesNotContain(word, clean.Value ?? string.Empty, $"cut {bound} characters into the path");
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("\t", DisplayName = "a tab")]
    [DataRow("  ", DisplayName = "two spaces")]
    [DataRow("\u00A0", DisplayName = "a no-break space")]
    [DataRow("\r\n", DisplayName = "a line break")]
    public void ACutBetweenTheWordsOfANameDropsTheFirstWordsWhateverSpaceIsBetweenThem(string gap)
    {
        // Independent review of 4056e3f, L2: the first words were dropped only when one plain space was between them.
        DiagnosticRedactionContext twoWords = new(["Jo Bloggs"]);
        string answer = new string('a', 20) + " signed in as Jo" + gap + "Bloggs today";
        (string cut, bool wasCut) = AdvisoryAiRedaction.BoundAnswer(answer, answer.IndexOf("Bloggs", StringComparison.Ordinal) + 2, twoWords);
        Assert.IsTrue(wasCut);
        Assert.EndsWith("signed in as", cut);
        string evidence = new string(' ', (16 * 1024) - 3 - gap.Length) + "Jo" + gap + "Bloggs tail";
        Assert.AreEqual((16 * 1024) - 1, evidence.IndexOf("Bloggs", StringComparison.Ordinal), "the bound falls inside the second word");
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(evidence, 2048, twoWords);
        Assert.IsTrue(clean.Truncated);
        Assert.DoesNotContain("Jo", clean.Value ?? string.Empty);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("\"C:\u200B\\Data\\Project Amber Notes.txt\"", DisplayName = "a zero-width space after the colon")]
    [DataRow("\"\u202AC:\\Data\\Project Amber Notes.txt\"", DisplayName = "a direction mark before the drive")]
    [DataRow("'C:\\\uFEFFData\\Project Amber Notes.txt'", DisplayName = "a byte-order mark after the separator")]
    [DataRow("\"C\U0001D173:\\Data\\Project Amber Notes.txt\"", DisplayName = "an astral format character in the drive")]
    public void ACutNeverSplitsAPathThatAnInvisibleCharacterInterrupts(string path)
    {
        // Codex review of cd04620, 71C-01, and the independent review's P1 and P4: cleaning removes an invisible character, so the
        // whole path is removed, but the cut was chosen on text where the character still stood between the drive and the rest, so a
        // cut at a space inside the path left "Amber" as plain text. Every cut is now chosen on the text as cleaning sees it.
        string answer = "See " + path + " for details.";
        Assert.DoesNotContain("Amber", AdvisoryAiRedaction.SanitizeAnswer(answer, 4000).Value!, "the whole path is removed");
        Assert.DoesNotContain("Amber", AdvisoryAiRedaction.SanitizeEvidence(answer, 2048).Value!, "the whole path is removed");
        for (int bound = answer.IndexOf("Amber", StringComparison.Ordinal) + 1; bound < answer.Length - 1; bound++)
        {
            (string cut, bool wasCut) = AdvisoryAiRedaction.BoundAnswer(answer, bound);
            Assert.IsTrue(wasCut);
            Assert.DoesNotContain("Amber", AdvisoryAiRedaction.SanitizeAnswer(cut, 4000).Value ?? string.Empty, $"answer cut at {bound}");
        }
        for (int offset = 1; offset < path.Length; offset++)
        {
            string evidence = new string(' ', (16 * 1024) - 4 - offset) + "See " + path + " tail";
            DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(evidence, 2048);
            Assert.IsTrue(clean.Truncated);
            Assert.DoesNotContain("Amber", clean.Value ?? string.Empty, $"evidence cut {offset} characters into the path");
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ARunOfInvisibleCharactersPastTheCutCannotMakeASplitSecretLookWhole()
    {
        // Codex review of #145: the view of the text past the cut was one longest path of raw characters, so a run of characters
        // cleaning removes there left the view ending early, and the start of a secret before the cut looked like a whole word.
        const string secret = "Ab3dEf7hIj1lMn5pQr9tUv2xYz4BcDe6GhIk8MoPqRsTuVwXyZ";
        string evidence = new string(' ', (16 * 1024) - 20) + secret[..20] + new string('\u200B', 32 * 1024) + secret[20..] + " tail";
        Assert.AreEqual("[secret]", AdvisoryAiRedaction.SanitizeEvidence(secret, 2048).Value, "the whole secret is removed");
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(evidence, 2048);
        Assert.IsTrue(clean.Truncated);
        Assert.DoesNotContain(secret[..20], clean.Value ?? string.Empty);
        string answer = "Key: " + secret[..20] + new string('\u200B', 32 * 1024) + secret[20..] + " end";
        (string cut, _) = AdvisoryAiRedaction.BoundAnswer(answer, 5 + 20);
        Assert.DoesNotContain(secret[..20], AdvisoryAiRedaction.SanitizeAnswer(cut, 4000).Value ?? string.Empty);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("J\u200Bo", DisplayName = "a zero-width space inside the first word")]
    [DataRow("\u2060Jo", DisplayName = "a word joiner before the first word")]
    [DataRow("Jo", DisplayName = "no invisible character")]
    public void ACutBetweenTheWordsOfANameDropsTheFirstWordsWhateverInvisibleCharacterIsInThem(string first)
    {
        // Codex review of cd04620, 71C-04, and the independent review's P2 and P3: the first words were matched on the raw text, so an
        // invisible character inside them hid them; and the timeline's own cut had no name step at all.
        DiagnosticRedactionContext twoWords = new(["Jo Bloggs"]);
        string answer = new string('a', 20) + " signed in as " + first + " Bloggs today";
        (string cut, bool wasCut) = AdvisoryAiRedaction.BoundAnswer(answer, answer.IndexOf("Bloggs", StringComparison.Ordinal) + 2, twoWords);
        Assert.IsTrue(wasCut);
        Assert.EndsWith("signed in as", cut);
        string evidence = new string(' ', (16 * 1024) - 2 - first.Length) + first + " Bloggs tail";
        Assert.AreEqual((16 * 1024) - 1, evidence.IndexOf("Bloggs", StringComparison.Ordinal), "the bound falls inside the second word");
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(evidence, 2048, twoWords);
        Assert.IsTrue(clean.Truncated);
        Assert.DoesNotContain("Jo", clean.Value ?? string.Empty, "evidence");
        string timeline = string.Concat(Enumerable.Repeat("log ", 4095)) + first + " Bloggs tail";
        DiagnosticSanitizedText record = DiagnosticTextSanitizer.Sanitize(timeline, 20_000, twoWords);
        Assert.IsTrue(record.Truncated);
        Assert.DoesNotContain("Jo", record.Value!, "timeline");
        Assert.EndsWith("log" + DiagnosticTextSanitizer.Ellipsis, record.Value!);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ASingleQuotedPathWithAnApostropheInAFolderNameIsRemovedWhole()
    {
        // Independent review of cd04620, P7: the first apostrophe (O'Brien) ended the quoted path, so the rest of it stayed visible.
        const string text = "Saved to 'C:\\Users\\O'Brien\\Documents\\Hartwell Accounts.pdf' yesterday.";
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual("Saved to '[path]' yesterday.", clean);
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ANameThatEndsASecretIsNotDroppedBeforeTheSecretIsRemoved()
    {
        // Codex review of #142: dropping the first word of a name that is also the last group of a product key would leave the
        // key's first four groups, which no longer look like a key, so they would be kept. The whole key must still be removed.
        DiagnosticRedactionContext name = new(["Alice Smith"]);
        const string key = "AAAAA-BBBBB-CCCCC-DDDDD-ALICE";
        string evidence = new string(' ', (16 * 1024) - key.Length) + key + " tail";
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(evidence, 2048, name);
        Assert.IsTrue(clean.Truncated);
        Assert.DoesNotContain("AAAAA", clean.Value ?? string.Empty);
        Assert.DoesNotContain("DDDDD", clean.Value ?? string.Empty);
        string answer = "The key is " + key + " and more";
        (string cut, bool wasCut) = AdvisoryAiRedaction.BoundAnswer(answer, answer.IndexOf(" and", StringComparison.Ordinal), name);
        Assert.IsTrue(wasCut);
        string? shown = AdvisoryAiRedaction.SanitizeAnswer(cut, 4000, name).Value;
        Assert.DoesNotContain("AAAAA", shown!);
        Assert.DoesNotContain("DDDDD", shown!);
        // A first name that is still plain text after cleaning is dropped as before.
        (string plain, _) = AdvisoryAiRedaction.BoundAnswer("Signed in as Alice Smith today", "Signed in as Alice Sm".Length, name);
        Assert.AreEqual("Signed in as", plain);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnAnswerKeepsEachLinesIndentationSoCodeKeepsItsMeaning()
    {
        // Task 71C review 71C-04: leading spaces survive redaction, capped, and the result is unchanged when it is sanitized again, as a
        // saved answer is on every read. Codex review of cd04620, 71C-06: inside a fence, spaces inside a line are kept too when nothing
        // in the line was removed; a line that had something removed has its spaces collapsed, and so does prose outside a fence.
        const string answer = "Try  this:\n```python\nif ready:\n    run(  1 )\n    label = 'A  B'\n        log(  \"C:\\Users\\alex\\x.log\")\n```";
        string? clean = AdvisoryAiRedaction.SanitizeAnswer(answer, 4000, Identity).Value;
        Assert.AreEqual("Try this:\n```python\nif ready:\n    run(  1 )\n    label = 'A  B'\n        log( \"[path]\")\n```", clean);
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

    /// <summary>Every cut position in <paramref name="from"/>..<paramref name="to"/> of <paramref name="line"/>, at all four cuts.</summary>
    private static void AssertNoCutShows(string line, int from, int to, string[] hidden, DiagnosticRedactionContext? context = null)
    {
        for (int at = from; at < to; at++)
        {
            string evidence = new string(' ', (16 * 1024) - at) + line + " tail";
            string timeline = evidence;
            string? evidenceClean = AdvisoryAiRedaction.SanitizeEvidence(evidence, 2048, context).Value;
            string? timelineClean = DiagnosticTextSanitizer.Sanitize(timeline, 20_000, context).Value;
            (string answerCut, _) = AdvisoryAiRedaction.BoundAnswer(line + " tail", Math.Max(2, at), context);
            string? answerClean = AdvisoryAiRedaction.SanitizeAnswer(answerCut, 4000, context).Value;
            foreach (string word in hidden)
            {
                Assert.DoesNotContain(word, evidenceClean ?? string.Empty, $"evidence cut at {at}");
                Assert.DoesNotContain(word, timelineClean ?? string.Empty, $"timeline cut at {at}");
                Assert.DoesNotContain(word, answerClean ?? string.Empty, $"answer cut at {at}");
            }
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Parents' House", "House", DisplayName = "a folder name ending in an apostrophe")]
    [DataRow("Guns N' Roses", "Roses", DisplayName = "an apostrophe followed by a space")]
    [DataRow("O'Brien", "Brien", DisplayName = "an apostrophe followed by a letter")]
    public void ACutInsideADoubleQuotedPathInASingleQuotedCommandLineShowsNothingOfIt(string folder, string folderWord)
    {
        // Independent review of 4d2e238, Q1: the cut check followed one quoted path at a time, so a double-quoted path inside a
        // single-quoted command line was missed, while cleaning removes double-quoted paths first. A cut inside the file name then
        // left its words visible. Each kind of quote is now followed on its own, and every cut is checked by cleaning it.
        string line = "Ran 'C:\\Windows\\System32\\msiexec.exe /i \"C:\\Users\\Pat\\" + folder + "\\Hartwell Family Trust Dispute.msi\" /qn' (exit 1603)";
        string[] hidden = [folderWord, "Hartwell", "Family", "Trust", "Dispute"];
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(line, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(line, 4000).Value, DiagnosticTextSanitizer.Sanitize(line, 4096).Value])
            Assert.AreEqual("Ran '[path]' (exit 1603)", clean, "pasted short, the whole command line is removed");
        AssertNoCutShows(line, line.IndexOf("Pat", StringComparison.Ordinal), line.IndexOf(".msi", StringComparison.Ordinal), hidden);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnApostropheBeforeANumberThatIsNotADigitCarriesTheQuotedPathOnAtACut()
    {
        // Independent review of 4d2e238, Q2: the cut check took only a letter or digit after an apostrophe as carrying a single-quoted
        // path on, while the remover takes any letter or number (², ½, Ⅷ).
        const string line = "Saved 'C:\\Users\\Pat\\O'\u00B2 Zebra Quokka.txt' today";
        AssertNoCutShows(line, line.IndexOf("Zebra", StringComparison.Ordinal), line.IndexOf(".txt", StringComparison.Ordinal), ["Zebra", "Quokka"]);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnAnswerCutInsideAQuotedPathLongerThanTheLongestPathShowsNothingOfIt()
    {
        // Independent review of 4d2e238, Q3: the check started at most one longest path before the cut, so the opening quote of a
        // longer quoted "path" was not seen at the answer cuts. The line is now read from its start, and the cut is checked.
        string answer = "See \"C:\\" + new string('x', 40_000) + " Zebra Quokka Ocelot.txt\" now";
        int bound = answer.IndexOf("Quokka", StringComparison.Ordinal) + 2;
        (string cut, bool wasCut) = AdvisoryAiRedaction.BoundAnswer(answer, bound);
        Assert.IsTrue(wasCut);
        string shown = AdvisoryAiRedaction.SanitizeAnswer(cut, 100_000).Value ?? string.Empty;
        Assert.DoesNotContain("Zebra", shown);
        Assert.DoesNotContain("Quokka", shown);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnAnswerCutBetweenTheWordsOfANameIsCheckedAsAnAnswerIsCleaned()
    {
        // Independent review of 4d2e238, Q4: the name check cleaned the kept answer as one evidence item, all lines joined, so a path
        // at the end of an earlier line swallowed the first words of the name, and they were kept. The cut is now checked by cleaning
        // the kept answer line by line, as an answer is cleaned.
        DiagnosticRedactionContext name = new(["Pat Ocelot Zebra"]);
        string answer = "Open C:\\Users\\\nsigned in as DOMAIN\\Pat Ocelot Zebra today";
        (string cut, bool wasCut) = AdvisoryAiRedaction.BoundAnswer(answer, answer.IndexOf("Zebra", StringComparison.Ordinal) + 2, name);
        Assert.IsTrue(wasCut);
        string shown = AdvisoryAiRedaction.SanitizeAnswer(cut, 4000, name).Value ?? string.Empty;
        Assert.DoesNotContain("Pat", shown);
        Assert.DoesNotContain("Ocelot", shown);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Saved 'C:\\Music\\Guns N' Roses\\Tax Return 2026.mp3' today", "Saved '[path]'", DisplayName = "an apostrophe followed by a space")]
    [DataRow("Saved 'C:\\Users\\Parents' House\\Tax Return 2026.mp3' today", "Saved '[path]'", DisplayName = "a folder name ending in an apostrophe")]
    [DataRow("Saved 'C:/Music/Guns N' Roses/Tax Return 2026.mp3' today", "Saved '[path]'", DisplayName = "forward slashes (Codex review of bf637b5, 71C-04)")]
    [DataRow("Saved 'C:\\Music\\Guns N' Roses Tax Return 2026.mp3' today", "Saved '[path]'", DisplayName = "an apostrophe in the file name")]
    [DataRow("Saved 'C:\\Temp\\Quarterly O'\U00010400Tax Return 2026.mp3' today", "Saved '[path]' today", DisplayName = "a letter outside the Basic Multilingual Plane after the apostrophe (71C-04)")]
    [DataRow("Saved 'C:\\Users\\O''Brien\\Tax Return 2026.mp3' today", "Saved '[path]' today", DisplayName = "a doubled apostrophe (PowerShell)")]
    public void ASingleQuotedPathWithAnyApostropheInAFolderNameIsRemovedWhole(string text, string expected)
    {
        // Independent review of 4d2e238, Q6: the path ended at an apostrophe followed by a space or another apostrophe, and the rest
        // of it was left visible with no cut involved. Codex review of bf637b5, 71C-04, and the independent review's U1: each rule
        // that guessed which later apostrophe closed the path left part of a name in some shape, so the rest of the line now goes
        // whenever a backslash, a slash or another apostrophe follows; a doubled apostrophe carries the path on.
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual(expected, clean);
        }
        AssertNoCutShows(text, text.IndexOf("Tax", StringComparison.Ordinal), text.IndexOf(".mp3", StringComparison.Ordinal), ["Tax", "Return", "Roses", "House", "Brien", "Quarterly"]);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Copied 'C:\\a\\b.txt' to 'D:\\c\\d.txt' fine", "Copied '[path]'", DisplayName = "another quoted path follows")]
    [DataRow("Copied 'C:\\a\\b.txt' to bin/x64 fine", "Copied '[path]'", DisplayName = "a slash follows")]
    [DataRow("Copied 'C:\\a\\b.txt' but couldn't finish", "Copied '[path]'", DisplayName = "an apostrophe follows")]
    [DataRow("Copied 'C:\\a\\b.txt' to the share fine. Next step done.", "Copied '[path]' to the share fine. Next step done.", DisplayName = "nothing like a path or quote follows")]
    public void ASingleQuotedPathTakesTheRestOfItsLineWhenASeparatorOrQuoteFollows(string text, string expected)
    {
        // Codex review of bf637b5, 71C-04, and the independent review's U1: the owner's plain rule in place of guessing the close.
        Assert.AreEqual(expected, DiagnosticTextSanitizer.Sanitize(text, 4096).Value);
        Assert.AreEqual(expected, AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("file:///C:/Users/Pat/Tax Returns 2026/Final Copy/return.pdf", DisplayName = "a drive in a file link")]
    [DataRow("FILE://server/share/Tax Returns 2026/Final Copy/return.pdf", DisplayName = "a network share in a file link")]
    [DataRow("file:///C:/Tax Returns 2026/Final Copy/return.pdf?customer=Hartwell-Family#page=2", DisplayName = "a query string and fragment (Codex review of #146)")]
    public void AFileLinkWithSpacesIsRemovedWhole(string link)
    {
        // Independent review of 4d2e238, Q7: a file link ended at its first space, leaving the private folder names after it.
        string text = "Open " + link + " now";
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual("Open [link] now", clean);
        }
        AssertNoCutShows(text, text.IndexOf("Returns", StringComparison.Ordinal), text.IndexOf(".pdf", StringComparison.Ordinal), ["Returns", "Final", "Copy", "return", "Hartwell"]);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Ran \"C:\\Users\\Pat\\Tax Return.exe --input=file:///C:/My Documents/foo.txt\" now", "Ran \"[path]\" now", DisplayName = "a double-quoted command line (Codex review of #146)")]
    [DataRow("Ran 'C:\\Users\\Pat\\Tax Return.exe --input=file:///C:/My Documents/foo.txt' now", "Ran '[path]' now", DisplayName = "a single-quoted command line")]
    [DataRow("Open file:///C:/Users/Tax Returns/O'Brien.txt now", "Open [link] now", DisplayName = "an apostrophe in the last part")]
    [DataRow("Open file:///C:/Clients/Tax Returns/O'-Brien.pdf now", "Open [link] now", DisplayName = "an apostrophe before punctuation in the last part (Codex review of #146)")]
    [DataRow("Ran \"C:\\Tools\\x.exe --url=https://example.test/a\" now", "Ran \"[path]\" now", DisplayName = "a web link in a quoted command line")]
    public void AFileLinkInsideAQuotedCommandLineLeavesTheClosingQuote(string text, string expected)
    {
        // Codex review of #146: the file link's last part ran to whitespace and took the command line's closing quote, so the quoted
        // path around it was no longer recognised and the file name's words stayed.
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual(expected, clean);
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Pat  Ocelot  Zebra", DisplayName = "two spaces")]
    [DataRow("Pat\r\nOcelot Zebra", DisplayName = "a line break")]
    [DataRow("Pat \tOcelot Zebra", DisplayName = "a space and a tab")]
    public void TheTimelineRemovesANameWhateverWhitespaceSeparatesItsWords(string written)
    {
        // Independent review of 4d2e238, Q8: names were replaced before runs of whitespace were joined.
        DiagnosticRedactionContext name = new(["Pat Ocelot Zebra"]);
        Assert.AreEqual("Signed in as [redacted] today", DiagnosticTextSanitizer.Sanitize("Signed in as " + written + " today", 4096, name).Value);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ACutIsCheckedByCleaningSoAShapeThePredictionMissesStillShowsNothing()
    {
        // Independent review of 4d2e238 (the fix that ends the class): after the predicted cut, the kept text is cleaned and compared
        // with the whole text cleaned the same way, and the cut moves back until it shows only what the whole shows. A quoted secret
        // whose value holds spaces must stay hidden at every cut, whichever step catches it.
        string line = "token = \"Zebra Quokka Ocelot Lynx\" end";
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(line, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(line, 4000).Value])
            Assert.DoesNotContain("Quokka", clean ?? string.Empty, "pasted short, the value is removed");
        for (int at = line.IndexOf("Quokka", StringComparison.Ordinal); at < line.IndexOf("Lynx", StringComparison.Ordinal); at++)
        {
            string evidence = new string(' ', (16 * 1024) - at) + line + " tail";
            string? clean = AdvisoryAiRedaction.SanitizeEvidence(evidence, 2048).Value;
            Assert.DoesNotContain("Zebra", clean ?? string.Empty, $"evidence cut at {at}");
            (string cut, _) = AdvisoryAiRedaction.BoundAnswer(line, at);
            Assert.DoesNotContain("Zebra", AdvisoryAiRedaction.SanitizeAnswer(cut, 4000).Value ?? string.Empty, $"answer cut at {at}");
        }
    }
    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Copy-Item : Cannot find path 'C:\\Temp\\setup.log' because it does not exist. At C:\\Users\\O'Brien\\Documents\\Scripts\\backup.ps1:12 char:1", "Copy-Item : Cannot find path '[path]'", DisplayName = "a later path under a folder with an apostrophe")]
    [DataRow("Copied 'C:\\Temp\\setup.log' to C:\\Users\\Pat\\Documents\\Mum's Papers\\Hartwell-Trust-Dispute.pdf", "Copied '[path]'", DisplayName = "a later path with a possessive folder")]
    [DataRow("Compared 'C:\\Temp\\a.txt' (from \\\\nas\\backup) with 'C:\\Users\\Pat\\Documents\\Mum's will.pdf'", "Compared '[path]'", DisplayName = "a network path between two quoted paths")]
    [DataRow("Installer 'C:\\Temp\\setup.exe' wrote C:\\Temp\\setup.log, then fetched https://files.example.org/o'brien/Hartwell-Trust/setup.msi?code=4/0AbCdEf", "Installer '[path]'", DisplayName = "a later link with an apostrophe")]
    [DataRow("Opened 'C:\\Temp\\a.txt' and C:\\Logs\\b.log then file:///C:/Users/Pat/Mum's Papers/Hartwell-Trust.pdf", "Opened '[path]'", DisplayName = "a later file link with a possessive folder")]
    [DataRow("Faulting application path: 'C:\\Program Files\\App\\app.exe' Faulting module path: C:\\Users\\O'Brien\\AppData\\Local\\Temp\\mod.dll", "Faulting application path: '[path]'", DisplayName = "an event's module path under a folder with an apostrophe")]
    [DataRow("Log 'C:\\Temp\\x.log shows https://example.org/a?q='x'&code=4/0AbCdEf", "Log '[path]'", DisplayName = "an unclosed quote, then a link holding a quoted value")]
    [DataRow("Log \"C:\\Temp\\x.log shows https://example.org/pa\"th/Hartwell-Trust", "Log \"[path]\"", DisplayName = "an unclosed double quote, then a link holding a double quote")]
    [DataRow("Opened 'C:\\Users\\O'Brien\\Documents\\x.pdf (access denied)", "Opened '[path]'", DisplayName = "an unclosed quote around a path with an apostrophe")]
    [DataRow("Copied 'C:\\Temp\\a.txt' to C:\\Music\\Guns N' Roses\\Hartwell.mp3", "Copied '[path]'", DisplayName = "a later unquoted path with an apostrophe before a space")]
    public void AQuotedPathsRemovalLeavesNoPartOfALaterPathOrLink(string text, string expected)
    {
        // Independent review of 1e57444, R1: the removal of a single-quoted path ran on to the next apostrophe whenever a backslash
        // lay between, so it swallowed the head of a later path, link or account name and left the tail visible. It now runs on only
        // to an apostrophe that closes a quote, over text with no start of another path or link.
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual(expected, clean);
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Cannot find path 'C:\\Temp\\setup.log' because it does not exist. HKLM\\SOFTWARE\\Contoso was read. Copy-Item 'C:\\Users\\Pat Smith\\My Notes\\Budget Plan.txt' failed", "Cannot find path '[path]'", DisplayName = "a registry key between two quoted paths")]
    [DataRow("'C:\\Users\\Pat\\a b.txt' was read by CORP\\jbloggs; see 'C:\\Logs\\Xsecret Ysecret.log'", "'[path]'", DisplayName = "an account name between two quoted paths")]
    [DataRow("Copy 'C:\\Temp\\a.txt' to bin\\x64 then open 'D:\\Private Folder\\Tax Return 2025.pdf' please", "Copy '[path]'", DisplayName = "a relative path between two quoted paths")]
    [DataRow("Copy '%USERPROFILE%\\a.txt' to bin\\x64 then open '%USERPROFILE%\\Private Folder\\Tax Return 2025.pdf' please", "Copy '[path]'", DisplayName = "two quoted variable paths")]
    [DataRow("Copy 'C:\\Temp\\a.txt' to bin\\x64 then Open('D:\\Private Folder\\Tax Return 2025.pdf') now", "Copy '[path]'", DisplayName = "a quoted path after a bracket")]
    [DataRow("Ran 'C:\\Temp\\a.exe' as CORP\\jbloggs &'C:\\Users\\Pat Smith\\out file.txt' done", "Ran '[path]'", DisplayName = "PowerShell's call operator before the second path")]
    [DataRow("Ran 'C:\\Temp\\a.exe' as CORP\\jbloggs `'C:\\Users\\Pat Smith\\out file.txt' done", "Ran '[path]'", DisplayName = "a backtick before the second path")]
    [DataRow("Ran 'C:\\Temp\\a.exe' as CORP\\jbloggs \"x\"'C:\\Users\\Pat Smith\\out file.txt' done", "Ran '[path]'", DisplayName = "a closing double quote before the second path")]
    [DataRow("Ran 'C:\\Temp\\a.exe' as CORP\\jbloggs \u2014'C:\\Users\\Pat Smith\\out file.txt' done", "Ran '[path]'", DisplayName = "a long dash before the second path")]
    [DataRow("See 'C:\\Temp\\a.txt' near bin\\x64 and 'file:///C:/Private Folder/TaxReturn.pdf' now", "See '[path]'", DisplayName = "a quoted file link after a backslash")]
    public void AQuotedPathsRemovalEndsBeforeTheNextQuotedPath(string text, string expected)
    {
        // Independent review of 5526f3d, S1: when the text between two single-quoted paths held a backslash, the first path's
        // run-on ended at the second path's opening quote, so the second path's removal started inside it and left its tail. A
        // quote that a path or link start follows, with no letter or number right before it, now opens that path or link and never
        // ends a run-on.
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual(expected, clean);
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("'C:\\Tools\\my cc.exe' -c src\\main.c -I'C:\\Program Files\\Acme SDK\\Include Files' -o out.exe", "'[path]'", DisplayName = "a compiler flag glued to a quoted path")]
    [DataRow("paths = [r'C:\\Data\\in file.csv', rel\\path, r'D:\\Clients\\Smith Estate\\Will Draft.docx']", "paths = [r'[path]'", DisplayName = "a Python raw string")]
    [DataRow("RESTORE DATABASE Pay FROM DISK = N'C:\\Bak\\pay db.bak' ON SRV01\\SQLEXPRESS WITH MOVE TO N'D:\\Data\\Payroll Ledger.mdf'", "RESTORE DATABASE Pay FROM DISK = N'[path]'", DisplayName = "a SQL Unicode string")]
    [DataRow("set a='C:\\Temp\\a b.txt' x\\y 2'D:\\Private\\Medical History.pdf'", "set a='[path]'", DisplayName = "a digit before the quote")]
    [DataRow("tool 'C:\\Temp\\a b.txt' bin\\x -f'%USERPROFILE%\\Documents\\Loan Application.pdf'", "tool '[path]'", DisplayName = "a variable path after a flag")]
    public void AQuotedPathAfterALetterIsRemovedWholeAfterAnEarlierQuotedPath(string text, string expected)
    {
        // Independent review of 44678e4, T1: a quote with a letter or number right before it (a flag such as -I, a Python r, a
        // SQL N) was still taken as the close of an earlier single-quoted path's run-on, so the later path lost its head and kept the
        // words after its first space. The run-on now ends before such a quote, which is left to open the later path.
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual(expected, clean);
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Opened file://pat:pw@nas01/share/Pat Smith/Tax Returns/return.pdf today", "Opened [link] today", DisplayName = "a user name and password before the host")]
    [DataRow("Opened file://pat@nas01/share/Pat Smith/Tax Returns/return.pdf today", "Opened [link] today", DisplayName = "a user name before the host")]
    public void AFileLinkWithAUserNameBeforeTheHostIsRemovedWhole(string text, string expected)
    {
        // Independent review of 5526f3d, S2: a user name and password before a file link's host sent the link to the general link
        // rule, which ends at the first space, so later folder words were left.
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual(expected, clean);
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ACutInsideTheSecondOfTwoQuotedPathsLeavesNoPartOfIt()
    {
        // Independent review of 5526f3d, S1 (and of 44678e4, T1, for the flag line), for the evidence and timeline cuts: wherever the
        // cut falls, no part of the second quoted path's private folder or file name is kept. (A text that itself ends inside an unclosed quoted path with a space
        // keeps the words after that space, the same limit an unquoted drive path has.)
        string[] lines =
        [
            "Cannot find path 'C:\\Temp\\setup.log' because it does not exist. HKLM\\SOFTWARE\\Contoso was read. Copy-Item 'C:\\Users\\Pat Smith\\My Notes\\Budget Plan.txt' failed",
            "Copy 'C:\\Temp\\a.txt' to bin\\x64 then open 'D:\\Private Folder\\Tax Return 2025.pdf' please",
            "'C:\\Tools\\my cc.exe' -c src\\main.c -I'C:\\Users\\Pat Smith\\Budget Plan' -o out.exe",
        ];
        string[] secrets = ["Pat", "Smith", "Notes", "Budget", "Plan", "Private", "Folder", "Tax", "Return", "2025"];
        string padding = string.Concat(Enumerable.Repeat("The update was checked. ", 625));
        foreach (string line in lines)
        {
            for (int length = 2; length <= line.Length; length++)
            {
                string? evidence = AdvisoryAiRedaction.SanitizeEvidence(padding + line + " and the update stopped.", padding.Length + length).Value;
                string? timeline = DiagnosticTextSanitizer.Sanitize(line + " and the update stopped.", length).Value;
                foreach (string? clean in (string?[])[evidence, timeline])
                {
                    if (clean is null) continue;
                    foreach (string secret in secrets)
                    {
                        for (int size = 3; size <= secret.Length; size++)
                        {
                            StringAssert.DoesNotMatch(clean, new System.Text.RegularExpressions.Regex(@"(?<![\p{L}\p{N}])" + System.Text.RegularExpressions.Regex.Escape(secret[..size]) + @"(?![\p{L}\p{N}])"), $"length {length}");
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Open \"file:///C:/Shared/Quarterly Violet Notes.txt\" failed", "Open \"[link]\" failed", DisplayName = "a double-quoted file link with spaces in its last part (71C-03)")]
    [DataRow("Open \"file:///C:/Shared/Quarterly Violet Notes.txt?q=Violet\" failed", "Open \"[link]\" failed", DisplayName = "and a query after it")]
    [DataRow("Open 'file:///C:/Shared/Quarterly Violet Notes.txt' failed", "Open '[link]' failed", DisplayName = "a single-quoted file link with spaces in its last part")]
    [DataRow("Links file:///C:/a/x.txt,file:///C:/Private Violet Folder/note.txt done", "Links [link][link] done", DisplayName = "two file links joined by a comma (71C-03)")]
    [DataRow("Links https://example.invalid/x,C:\\Users\\Pat\\Private Violet Folder\\note.txt done", "Links [link][path] done", DisplayName = "a web link joined to a drive path")]
    [DataRow("\"C:\\Tools\\reader.exe https://example.invalid/x\",file:///C:/Private Violet Folder/note.txt", "\"[path]\",[link]", DisplayName = "a quoted command line ending in a link, then a file link (71C-08)")]
    public void AFileLinkIsRemovedWholeWhenQuotedOrJoinedToAnotherPathOrLink(string text, string expected)
    {
        // Codex review of bf637b5, 71C-03 and 71C-08: a quoted file link's last part ended at its first space, a file link's last
        // part took the head of a link joined to it, and a quoted command's link tail took the head of the next field.
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual(expected, clean);
        }
        AssertNoCutShows(text, text.IndexOf("Violet", StringComparison.Ordinal) - 8, text.LastIndexOf(".txt", StringComparison.Ordinal), ["Violet", "Quarterly", "Folder", "note"]);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Open \"https://example.invalid/My Violet Folder/Quarterly Amber Notes.txt\" failed", "Open \"[link]\" failed")]
    [DataRow("Open 'https://example.invalid/My Violet Folder/Quarterly Amber Notes.txt' failed", "Open '[link]' failed")]
    [DataRow("url=\"https://example.invalid/search?q=violet clinic amber results&lang=en\" next", "url=\"[link]\" next")]
    [DataRow("Opening \"ms-word:ofe|u|https://example.invalid/Violet Folder/Amber Notes.docx\" now", "Opening \"ms-word:ofe|u|[link]\" now")]
    [DataRow("{\\\"url\\\":\\\"https://example.invalid/My Violet Folder/Quarterly Amber Notes.txt\\\"}", "{\\\"url\\\":\\\"[link]\"}", DisplayName = "after an escaped quote (P3)")]
    [DataRow("Open \"\"https://example.invalid/My Violet Folder/Quarterly Amber Notes.txt\"\" failed", "Open \"\"[link]\"\" failed", DisplayName = "after a doubled quote (P3)")]
    [DataRow("<td>\"https://example.invalid/My Violet Folder/Quarterly Amber Notes.txt\"</td>", "<td>\"[link]\"</td>", DisplayName = "after a tag (P3)")]
    [DataRow("var u = base+\"https://example.invalid/My Violet Folder/Quarterly Amber Notes.txt\";", "var u = base+\"[link]\";", DisplayName = "after a plus")]
    [DataRow("<\"https://example.invalid/My Violet Folder/Quarterly Amber Notes.txt\">", "<\"[link]\">", DisplayName = "after an angle bracket")]
    [DataRow("x=\"C:\\Temp\\a.txt\"\"https://example.invalid/My Violet Folder/Quarterly Amber Notes.txt\"", "x=\"[path]\"\"[link]\"", DisplayName = "after another value's close")]
    public void AQuotedWebLinkWithASpaceIsRemovedToItsClose(string text, string expected)
    {
        // Independent review of be08c02, V1: a quoted file link ran to its closing quote, but a web link still ended at its first
        // space and kept its later folder and file words.
        AssertCleanedEverywhere(text, expected);
        AssertNoCutShows(text, text.IndexOf("://", StringComparison.Ordinal), text.Length, ["Violet", "violet", "Amber", "amber"]);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Open `C:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt` failed", "Open `[path]` failed")]
    [DataRow("Open \u201CC:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt\u201D failed", "Open \u201C[path]\u201D failed")]
    [DataRow("Datei \u201EC:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt\u201C nicht gefunden", "Datei \u201E[path]\u201C nicht gefunden")]
    [DataRow("Fichier \u00AB C:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt \u00BB introuvable", "Fichier \u00AB [path]\u00BB introuvable")]
    [DataRow("Open \u2018C:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt\u2019 failed", "Open \u2018[path]\u2019 failed")]
    [DataRow("Open (C:\\Program Files (x86)\\My Violet Folder\\Quarterly Amber Notes.txt) failed", "Open ([path]) failed")]
    [DataRow("See [the log](C:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt) for details", "See [the log]([path]) for details")]
    [DataRow("Open {C:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt} failed", "Open {[path]} failed")]
    [DataRow("Open ( C:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt ) failed", "Open ( [path]) failed")]
    [DataRow("Open [ C:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt ] failed", "Open [ [path]] failed")]
    [DataRow("Open <https://example.invalid/My Violet Folder/Quarterly Amber Notes.txt> failed", "Open <[link]> failed")]
    [DataRow("Open &quot;C:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt&quot; failed", "Open &quot;[path]&quot; failed")]
    [DataRow("| C:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt | failed |", "| [path]| failed |")]
    [DataRow("| C:\\Users\\Pat\\My Violet Folder\\Quarterly Amber Notes.txt | C:\\Users\\Pat\\Amber Violet Files\\b.txt |", "| [path]| [path]|")]
    public void APathOrLinkInsideAnotherDelimiterIsRemovedToItsClose(string text, string expected)
    {
        // Independent review of be08c02, V2: inside a backtick, typographic quotes, guillemets, brackets, an escaped quote or a
        // table bar, a path or link with a space in its last part kept the words after the space, as an unquoted one does.
        AssertCleanedEverywhere(text, expected);
        AssertNoCutShows(text, text.IndexOf("Violet", StringComparison.Ordinal) - 8, text.IndexOf(".txt", StringComparison.Ordinal), ["Violet", "Amber"]);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void GluedQuotesBeforeFileLinksAreCleanedInLinearTime()
    {
        // Independent review of be08c02, X1: each glued opener ("file:///C:/a"file:///C:/a…) was matched again to the end of the
        // run, so one answer line inside the reply bound took minutes. The cuts are bounded as for joined links.
        string line = string.Concat(Enumerable.Repeat("\"file:///C:/a", 393_216 / 13));
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        string clean = AdvisoryAiRedaction.SanitizeAnswer(line, 400_000).Value ?? string.Empty;
        Assert.IsLessThan(TimeSpan.FromSeconds(10), watch.Elapsed);
        Assert.DoesNotContain("file:", clean);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Open `https://example.invalid/My Violet Folder/` failed", "Open `[link]` failed")]
    [DataRow("Open \u201Chttps://contoso.example/sites/Team Violet Site/Shared Amber Documents/\u201D failed", "Open \u201C[link]\u201D failed")]
    [DataRow("Ouvrir \u00ABhttps://example.invalid/My Violet Folder/\u00BB maintenant", "Ouvrir \u00AB[link]\u00BB maintenant")]
    [DataRow("Datei \u201Eftp://example.invalid/My Violet Folder/\u201C fehlt", "Datei \u201E[link]\u201C fehlt")]
    [DataRow("Open &quot;https://example.invalid/My Violet Folder/&quot; failed", "Open &quot;[link]&quot; failed")]
    [DataRow("|https://example.invalid/My Violet Folder/|failed|", "|[link]|failed|")]
    [DataRow("| https://example.invalid/My Violet Folder/ | C:\\Users\\Pat\\Amber Files\\ |", "| [link]| [path]|")]
    [DataRow("Open \u201Dhttps://example.invalid/My Violet Folder/\u201D failed", "Open \u201D[link]\u201D failed")]
    [DataRow("Ouvrir \u00BBhttps://example.invalid/My Violet Folder/\u00AB maintenant", "Ouvrir \u00BB[link]\u00AB maintenant")]
    [DataRow("Ouvrir \u2039https://example.invalid/My Violet Folder/\u203A maintenant", "Ouvrir \u2039[link]\u203A maintenant")]
    [DataRow("Ouvrir \u203Ahttps://example.invalid/My Violet Folder/\u2039 maintenant", "Ouvrir \u203A[link]\u2039 maintenant")]
    [DataRow("Open &#34;https://example.invalid/My Violet Folder/&#34; failed", "Open &#34;[link]&#34; failed")]
    [DataRow("Open \u201CC:\\Users\\Pat\\My Violet Folder\\\u201D failed", "Open \u201C[path]\u201D failed")]
    public void AFolderLinkEndingInASlashInsideADelimiterIsRemovedToItsClose(string text, string expected)
    {
        // Independent review of d16ab4c, Y1: a close right after a slash was read as part of a path that goes on, so a web link to
        // a folder kept the words after its first space. Only a close with a letter or number glued after it is read that way now.
        AssertCleanedEverywhere(text, expected);
        AssertNoCutShows(text, text.IndexOf("://", StringComparison.Ordinal), text.LastIndexOf('/') + 1, ["Violet", "Amber"]);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("\u201EC:\\a\u201C ")]
    [DataRow("\u201EC:\\a\u201D ")]
    [DataRow("\u201CC:\\a\u201D ")]
    [DataRow("\u201CC:\\a\u201C ")]
    [DataRow("\"https://a/x y\" ")]
    public void ManyQuotedValuesWithAnAbsentOtherCloseAreCleanedInLinearTime(string value)
    {
        // Codex review of #152: each well-formed value searched the whole rest of the line for the close it didn't use, so a long
        // answer line cost the square of its length, as did the line's end for each quoted web link. Each close is now looked for
        // once until it is passed, and a link's close and its line's end are looked for together.
        string line = string.Concat(Enumerable.Repeat(value, 393_216 / value.Length));
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        string clean = AdvisoryAiRedaction.SanitizeAnswer(line, 400_000).Value ?? string.Empty;
        Assert.IsLessThan(TimeSpan.FromSeconds(10), watch.Elapsed);
        Assert.DoesNotContain("C:\\", clean);
        Assert.DoesNotContain("://", clean);
    }

    private static void AssertCleanedEverywhere(string text, string expected)
    {
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual(expected, clean);
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("https://example.invalid/Violet/x")]
    [DataRow("file:///C:/Shared/Violet/Notes.txt")]
    [DataRow("file:///C:/a.txt+C:\\Violet\\b.txt")]
    public void ALongRunOfJoinedLinksIsRemovedWholeWithABoundedNumberOfCuts(string link)
    {
        // Codex review of #151: each value cut from a joined run was matched again to the run's end, so a long run cost the square
        // of its length. After 16 cuts the rest of the run goes as one link, and nothing of any link in it is left.
        string text = "Links " + string.Join(",", Enumerable.Repeat(link, 3000)) + " done";
        string clean = AdvisoryAiRedaction.SanitizeAnswer(text, 200_000).Value ?? string.Empty;
        Assert.DoesNotContain("Violet", clean);
        Assert.DoesNotContain("Notes", clean);
        StringAssert.StartsWith(clean, "Links [link]");
        StringAssert.EndsWith(clean, " done");
        Assert.IsLessThan(200, clean.Length);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AQuotedPathsRemovalLeavesALaterAccountNameToBeRemoved()
    {
        // Independent review of 1e57444, R1: with O'Brien as the PC's account name, the quoted path's removal took "CORP\O'". With
        // the rest of the line taken after a single-quoted path (71C-04), the name goes with it; on a later line it is still replaced.
        DiagnosticRedactionContext names = new(["CORP", "O'Brien"]);
        const string text = "Signed in 'C:\\Temp\\a.txt' as CORP\\O'Brien today\nSigned in as CORP\\O'Brien again";
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048, names).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000, names).Value, DiagnosticTextSanitizer.Sanitize(text, 4096, names).Value])
        {
            Assert.DoesNotContain("Brien", clean ?? string.Empty);
            StringAssert.StartsWith(clean, "Signed in '[path]'");
        }
        // Evidence and the timeline read a line break as a space, so there the second line goes too; the answer keeps it.
        StringAssert.EndsWith(AdvisoryAiRedaction.SanitizeAnswer(text, 4000, names).Value, "Signed in as [redacted]\\[redacted] again");
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Could not open 'C:\\Temp\\a.txt'. The installer stopped because the disk was full", "Could not open '[path]'. The installer stopped because the disk was full")]
    [DataRow("Cannot find path 'C:\\Program Files\\Contoso Editor\\settings.ini' because it does not exist.", "Cannot find path '[path]' because it does not exist.")]
    [DataRow("Could not open 'C:\\Temp\\a.txt'. Setup log at C:\\Logs\\x.log says the installer couldn't continue", "Could not open '[path]'", DisplayName = "a later path and apostrophe on the line take its words too")]
    public void AQuotedPathsRemovalKeepsTheOrdinaryWordsAfterIt(string text, string expected)
    {
        // Independent review of 1e57444, R2: ordinary words after a quoted path are kept, unless a backslash, slash or apostrophe
        // follows on the same line (Codex review of bf637b5, 71C-04), when the rest of the line goes with the path.
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual(expected, clean);
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ACutAfterAQuotedPathKeepsTheOrdinaryTextBeforeIt()
    {
        // Independent review of 1e57444, R2: with the next apostrophe past the cut, the cut check kept 6 characters of 20,000. The
        // rest of a line after a single-quoted path goes when an apostrophe follows on it (71C-04), so on one long line nothing of
        // it is kept, and with nothing like that after the path the cut keeps the words.
        string words = string.Concat(Enumerable.Repeat("plain words here ", 1_200)) + "it didn't";
        string sameLine = AdvisoryAiRedaction.SanitizeEvidence("Opened 'C:\\Temp\\a.txt' and C:\\Logs\\b.log. " + words, 16_384).Value ?? string.Empty;
        Assert.DoesNotContain("plain", sameLine);
        Assert.DoesNotContain("Temp", sameLine);
        string plain = AdvisoryAiRedaction.SanitizeEvidence("Opened 'C:\\Temp\\a.txt' today. " + words.Replace("it didn't", "it did not", StringComparison.Ordinal), 16_384).Value ?? string.Empty;
        Assert.IsGreaterThan(15_000, plain.Length, "nothing like a path or quote follows, so the cut keeps the words");
        StringAssert.StartsWith(plain, "Opened '[path]' today. plain words");
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Opened file://localhost/C:/Users/Pat/Tax Returns 2026/Final Copy/return.pdf today", "Opened [link] today", DisplayName = "a host before the drive (R5)")]
    [DataRow("Opened file://[2001:db8::1]/share/Tax Returns/Private Copy/return.pdf today", "Opened [link] today", DisplayName = "an IPv6 host (Codex review of #147)")]
    [DataRow("Opened file://nas:445/share/Tax Returns/Private Copy/return.pdf today", "Opened [link] today", DisplayName = "a host with a port")]
    [DataRow("Paths C:\\a\\b.txt;D:\\Users\\Pat\\c.txt and \\\\srv\\share\\x;C:\\y\\z.txt done", "Paths [path][path] and [path][path] done", DisplayName = "a drive or share path, then a drive path with no space (R6)")]
    public void AFileLinkAfterAHostAndTwoPathsJoinedWithNoSpaceAreRemovedWhole(string text, string expected)
    {
        foreach (string? clean in (string?[])[AdvisoryAiRedaction.SanitizeEvidence(text, 2048).Value, AdvisoryAiRedaction.SanitizeAnswer(text, 4000).Value, DiagnosticTextSanitizer.Sanitize(text, 4096).Value])
        {
            Assert.AreEqual(expected, clean);
        }
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("'C:\\x ", DisplayName = "every quote opens a path and closes the one before")]
    [DataRow("'C:\\Users\\O'Brien\\x ", DisplayName = "every quote is inside a word that goes on as a path")]
    [DataRow("'C:\\a.txt' b\\c'd ", DisplayName = "every run-on reads to a quote that doesn't close")]
    [DataRow("'C:\\a.txt' x\\y 'C:\\b' ", DisplayName = "every run-on meets a path start")]
    [DataRow("a'b ", DisplayName = "no path at all")]
    public void QuotedPathRemovalStaysQuickOnTextBuiltToRepeatIt(string unit)
    {
        // Codex review of #147: a recovery that matched every shorter prefix again made text built to need one at every quote take
        // work that grows with the cube of its length. The recoveries are bounded and the run-on's scans are remembered.
        string line = string.Concat(Enumerable.Repeat(unit, (200_000 / unit.Length) + 1));
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        _ = AdvisoryAiRedaction.SanitizeEvidence(line[..16_000], 16_384).Value;
        _ = DiagnosticTextSanitizer.Sanitize(line[..16_000], 20_000).Value;
        _ = AdvisoryAiRedaction.SanitizeAnswer(line, 400_000).Value;
        Assert.IsLessThan(TimeSpan.FromSeconds(10), watch.Elapsed);
    }
}
