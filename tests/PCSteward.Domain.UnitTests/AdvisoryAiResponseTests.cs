using System.Text;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// FR-AI-04/05/06, FR-BUD-03 (ADR-028 Q1, ADR-029 F4; Task 67 stage 1): strict, bounded reading of synthetic response bodies.
/// Answer completeness and usage reliability are independent; nothing that the request did not allow is trusted.
/// </summary>
[TestClass]
public sealed class AdvisoryAiResponseTests
{
    private const string Model = "synthetic-model-2026-01-01";
    private const string Usage = "\"usage\":{\"input_tokens\":900,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":120},\"total_tokens\":1200}";

    private static AdvisoryAiResponse Read(string json, long maximumBytes = 1_000_000, long input = 8192, long output = 1024) =>
        AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(json), Model, maximumBytes, input, output);

    private static string Body(string status = "completed", string output = "[{\"type\":\"reasoning\",\"summary\":[]},{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"Line one.\\nLine two.\",\"annotations\":[]}]}]",
        string usage = Usage, string extra = "") =>
        "{\"id\":\"resp_abc123\",\"object\":\"response\",\"created_at\":1763000000,\"status\":\"" + status + "\",\"model\":\"" + Model + "\",\"store\":false,\"tools\":[],\"output\":" + output + "," + usage + extra + "}";

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ACompletedAnswerWithCompleteUsage()
    {
        AdvisoryAiResponse response = Read(Body());
        Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, response.Status);
        Assert.AreEqual("resp_abc123", response.ResponseId);
        Assert.AreEqual("Line one.\nLine two.", response.Text);
        Assert.IsTrue(response.UsageReliable);
        Assert.AreEqual(new AdvisoryAiUsage(900, 0, 300, 120, 1200), response.Usage);
        Assert.IsFalse(response.UsageAboveApprovedBounds);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    [DataRow(300 * 4, false, DisplayName = "text 300 tokens can decode to")]
    [DataRow((300 * 4) + 1, true, DisplayName = "one byte more than 300 tokens can decode to")]
    public void AnswerTextLongerThanTheReportedOutputCouldDecodeToIsNotTrusted(int bytes, bool contradicted)
    {
        // Codex review of ce0eaf3, C1: with at most 4 bytes per token, 300 reported output tokens decode to at most 1,200 bytes.
        string text = new('x', bytes);
        string output = "[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"" + text + "\",\"annotations\":[]}]}]";
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(output: output)), Model, 1_000_000, 8192, 1024, maximumTokenUtf8Bytes: 4);
        Assert.AreEqual(text, response.Text);
        Assert.AreEqual(!contradicted, response.UsageReliable);
        Assert.AreEqual(contradicted, response.UsageAboveApprovedBounds);
        // Without a per-token bound the text is not compared, as before.
        Assert.IsTrue(Read(Body(output: output)).UsageReliable);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    [DataRow("[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"TEXT\"}]}]", DisplayName = "a refusal")]
    [DataRow("[{\"type\":\"reasoning\",\"summary\":[{\"type\":\"summary_text\",\"text\":\"TEXT\"}]},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"Short.\"}]}]", DisplayName = "a reasoning summary")]
    [DataRow("NOID[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"TEXT\"}]}]", DisplayName = "an answer in a body refused for another reason")]
    public void AnyOutputTextLongerThanTheReportedOutputCouldDecodeToIsNotTrusted(string output)
    {
        // Independent review of a4db871, AG1: the body refused for another reason has no id, since a function call item, the reason
        // it was refused before, now pauses on its own.
        bool noId = output.StartsWith("NOID", StringComparison.Ordinal);
        output = noId ? output.Substring(4) : output;
        string Shaped(string o) => noId ? Body(output: o).Replace("\"resp_abc123\"", "\"\"", StringComparison.Ordinal) : Body(output: o);
        // Independent review of 4056e3f, G2: the C1 check counted answer text only, so a huge refusal or reasoning summary, or an
        // answer in a body that was refused before the check, settled at the reported usage with nothing stopped for review.
        string large = output.Replace("TEXT", new string('x', (300 * 4) + 1), StringComparison.Ordinal);
        AdvisoryAiResponse over = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Shaped(large)), Model, 1_000_000, 8192, 1024, maximumTokenUtf8Bytes: 4);
        Assert.IsFalse(over.UsageReliable);
        Assert.IsTrue(over.UsageAboveApprovedBounds);
        // The same reply with text the reported tokens can decode to is trusted, so the check charges only the text, not the fixed names.
        string small = output.Replace("TEXT", "Short reason.", StringComparison.Ordinal);
        AdvisoryAiResponse within = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Shaped(small)), Model, 1_000_000, 8192, 1024, maximumTokenUtf8Bytes: 4);
        Assert.AreEqual(over.Status, within.Status);
        Assert.AreEqual(within.MalformedReason == AdvisoryAiMalformedReason.None, within.UsageReliable);
        Assert.IsFalse(within.UsageAboveApprovedBounds);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public void TextUnderANameTheCounterSkipsIsCountedWhenItIsNotAPlainString()
    {
        // Codex review of cd04620, 71C-02: the byte counter skipped every value named status, role, type or id, so text inside an
        // object under one of those names wasn't counted, and the usage settled at one reported output token.
        const string oneToken = "\"usage\":{\"input_tokens\":900,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":1,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":901}";
        string hidden = "[{\"type\":\"reasoning\",\"status\":{\"note\":\"" + new string('x', 5) + "\"},\"summary\":[]},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"TEXT\"}]}]";
        AdvisoryAiResponse within = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(output: hidden.Replace("TEXT", "abc", StringComparison.Ordinal), usage: oneToken)), Model, 1_000_000, 8192, 1024, maximumTokenUtf8Bytes: 8);
        Assert.IsTrue(within.UsageReliable, "5 + 3 bytes fit the 8 one token can decode to");
        AdvisoryAiResponse over = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(output: hidden.Replace("TEXT", "abcd", StringComparison.Ordinal), usage: oneToken)), Model, 1_000_000, 8192, 1024, maximumTokenUtf8Bytes: 8);
        Assert.IsFalse(over.UsageReliable, "the text in the status object is counted");
        Assert.IsTrue(over.UsageAboveApprovedBounds);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    [DataRow(",\"store\":false,\"id\":\"resp_again\"", "\"output_tokens\":1,", "901", DisplayName = "a body refused before the output is read")]
    [DataRow("", "\"output_tokens\":1,", "5", DisplayName = "a total that doesn't add up, with a plain output count")]
    public void OutputTextThatContradictsAPlainOutputCountAlwaysPauses(string extra, string outputCount, string total)
    {
        // Codex review of cd04620, 71C-03: a body refused early (here for echoing store) returned before the check, and an inconsistent
        // total made the check use the whole approved output instead of the reported count. Both kept the reservation with no pause.
        // Independent review of a4db871, AG1: the early refusal is now a repeated id, since an echoed store pauses on its own.
        string usage = "\"usage\":{\"input_tokens\":900,\"input_tokens_details\":{\"cached_tokens\":0}," + outputCount + "\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":" + total + "}";
        string output = "[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"" + new string('x', 193) + "\"}]}]";
        string body = Body(output: output, usage: usage).Replace(",\"store\":false", extra, StringComparison.Ordinal);
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 8192, 2048, maximumTokenUtf8Bytes: 192);
        Assert.IsFalse(response.UsageReliable);
        Assert.IsTrue(response.UsageAboveApprovedBounds, "193 bytes can't come from one token of at most 192");
        string fits = body.Replace(new string('x', 193), new string('x', 192), StringComparison.Ordinal);
        AdvisoryAiResponse within = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(fits), Model, 1_000_000, 8192, 2048, maximumTokenUtf8Bytes: 192);
        Assert.IsFalse(within.UsageAboveApprovedBounds, "the same body with text one token can decode to isn't stopped for this");
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void AnIncompleteAnswerWithNoTextCanStillCarryCompleteBillableUsage()
    {
        AdvisoryAiResponse response = Read(Body("incomplete", "[{\"type\":\"reasoning\",\"summary\":[]}]", extra: ",\"incomplete_details\":{\"reason\":\"max_output_tokens\"}"));
        Assert.AreEqual(AdvisoryAiAnswerStatus.Incomplete, response.Status);
        Assert.AreEqual(AdvisoryAiIncompleteReason.OutputLimit, response.IncompleteReason);
        Assert.IsNull(response.Text);
        Assert.IsTrue(response.UsageReliable);
    }

    [TestMethod]
    [TestCategory("FR-AI-06")]
    public void ARefusalAndAProviderFailureAreTheirOwnResults()
    {
        Assert.AreEqual(AdvisoryAiAnswerStatus.Refused,
            Read(Body(output: "[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"I can't help with that.\"}]}]")).Status);
        AdvisoryAiResponse failed = Read(Body("failed", "[]", extra: ",\"error\":{\"code\":\"server_error\",\"message\":\"x\"}"));
        Assert.AreEqual(AdvisoryAiAnswerStatus.ProviderFailed, failed.Status);
        Assert.IsTrue(failed.UsageReliable);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("function_call")]
    [DataRow("web_search_call")]
    [DataRow("file_search_call")]
    [DataRow("computer_call")]
    [DataRow("mcp_call")]
    [DataRow("code_interpreter_call")]
    public void AnyToolOutputMakesTheBodyUntrusted(string type)
    {
        AdvisoryAiResponse response = Read(Body(output: "[{\"type\":\"" + type + "\",\"name\":\"run\",\"arguments\":\"{}\"},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"done\"}]}]"));
        Assert.AreEqual(AdvisoryAiAnswerStatus.Malformed, response.Status);
        Assert.AreEqual(AdvisoryAiMalformedReason.UnexpectedOutputItem, response.MalformedReason);
        Assert.IsNull(response.Text);
        Assert.IsFalse(response.UsageReliable, "never settlement evidence");
        Assert.IsNotNull(response.Usage, "kept so a charge above the reservation can be recorded in full");
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public void ABodyForAnotherModelOrStoredOrWithToolsIsRefused()
    {
        Assert.AreEqual(AdvisoryAiMalformedReason.ModelMismatch, Read(Body().Replace(Model, "other-model", StringComparison.Ordinal)).MalformedReason);
        Assert.AreEqual(AdvisoryAiMalformedReason.StoredResponse, Read(Body().Replace("\"store\":false", "\"store\":true", StringComparison.Ordinal)).MalformedReason);
        Assert.AreEqual(AdvisoryAiMalformedReason.ToolsPresent, Read(Body().Replace("\"tools\":[]", "\"tools\":[{\"type\":\"web_search\"}]", StringComparison.Ordinal)).MalformedReason);
        Assert.AreEqual(AdvisoryAiMalformedReason.UnknownStatus, Read(Body("in_progress")).MalformedReason);
        Assert.AreEqual(AdvisoryAiMalformedReason.MissingOrInvalidId, Read(Body().Replace("resp_abc123", "resp abc", StringComparison.Ordinal)).MalformedReason);
        Assert.AreEqual(AdvisoryAiMalformedReason.NotAResponseObject, Read(Body().Replace("\"object\":\"response\"", "\"object\":\"list\"", StringComparison.Ordinal)).MalformedReason);
        Assert.AreEqual(AdvisoryAiMalformedReason.DuplicateProperty, Read(Body(extra: ",\"id\":\"resp_other\"")).MalformedReason);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("[{\"type\":\"function_call\",\"type\":\"reasoning\"}]", DisplayName = "item type")]
    [DataRow("[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"a\"}],\"content\":[]}]", DisplayName = "item content")]
    [DataRow("[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"type\":\"output_text\",\"text\":\"a\"}]}]", DisplayName = "part type")]
    [DataRow("[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"a\",\"text\":\"b\"}]}]", DisplayName = "part text")]
    public void ADuplicatePropertyInsideAnOutputItemOrContentPartMakesTheBodyUntrusted(string output)
    {
        AdvisoryAiResponse response = Read(Body(output: output));
        Assert.AreEqual(AdvisoryAiAnswerStatus.Malformed, response.Status);
        Assert.AreEqual(AdvisoryAiMalformedReason.DuplicateProperty, response.MalformedReason);
        Assert.IsNull(response.Text);
        Assert.IsFalse(response.UsageReliable, "never settlement evidence");
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public void OversizeDeepOrTrailingBodiesAreMalformed()
    {
        string body = Body();
        Assert.AreEqual(AdvisoryAiMalformedReason.TooLarge, Read(body, Encoding.UTF8.GetByteCount(body) - 1).MalformedReason);
        Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, Read(body, Encoding.UTF8.GetByteCount(body)).Status, "exactly at the bound");
        Assert.AreEqual(AdvisoryAiMalformedReason.NotJson, Read(body + "{}").MalformedReason);
        Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, Read(body + "\n").Status, "trailing whitespace is fine");
        Assert.AreEqual(AdvisoryAiMalformedReason.NotJson, Read(Body(extra: ",\"x\":" + new string('[', 40) + new string(']', 40))).MalformedReason);
        Assert.AreEqual(AdvisoryAiMalformedReason.NotJson, Read(body[..^10]).MalformedReason, "a cut-off body");
        Assert.AreEqual(AdvisoryAiMalformedReason.NotJson, Read("not json").MalformedReason);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("")]
    [DataRow(",\"usage\":null")]
    [DataRow(",\"usage\":{\"input_tokens\":900,\"output_tokens\":300,\"total_tokens\":1200}")]
    [DataRow(",\"usage\":{\"input_tokens\":900,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":120},\"total_tokens\":1199}")]
    [DataRow(",\"usage\":{\"input_tokens\":900,\"input_tokens_details\":{\"cached_tokens\":901},\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":120},\"total_tokens\":1200}")]
    [DataRow(",\"usage\":{\"input_tokens\":900,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":301},\"total_tokens\":1200}")]
    [DataRow(",\"usage\":{\"input_tokens\":-1,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":299}")]
    [DataRow(",\"usage\":{\"input_tokens\":\"900\",\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":1200}")]
    [DataRow(",\"usage\":{\"input_tokens\":900.5,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":1200}")]
    [DataRow(",\"usage\":{\"input_tokens\":900,\"input_tokens\":1,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":1200}")]
    public void MissingInconsistentOrMalformedUsageIsNeverReliable(string usage)
    {
        AdvisoryAiResponse response = Read(Body(usage: "\"x\":1" + usage));
        Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, response.Status, "the answer can still be shown");
        Assert.IsFalse(response.UsageReliable);
        // Codex review of 2cdfcc4, 71C-01: a count that isn't a plain whole number can't be compared exactly with the approval, so it
        // also stops paid requests; independent review of 2de2ec6, AA1: so does a count written as a string. Every other row here is
        // untrusted without a pause.
        Assert.AreEqual(usage.Contains("900.5", StringComparison.Ordinal) || usage.Contains("\"input_tokens\":\"900\"", StringComparison.Ordinal),
            response.UsageAboveApprovedBounds);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void AReportedCacheWriteIsNeverTrustedAndStopsForReview()
    {
        // Task 69: the request asks for no prompt cache; a write is billed above the input rate, so it can't settle the request.
        string none = Usage.Replace("{\"cached_tokens\":0}", "{\"cached_tokens\":0,\"cache_write_tokens\":0}", StringComparison.Ordinal);
        AdvisoryAiResponse clean = Read(Body(usage: none));
        Assert.IsTrue(clean.UsageReliable, "a reported zero is fine");
        Assert.IsFalse(clean.UsageAboveApprovedBounds);
        // T70-07: the exact figure is kept for the owner to see, apart from settlement.
        Assert.AreEqual(0L, clean.CacheWriteTokens);
        Assert.IsFalse(clean.CacheWriteUnreadable);
        Assert.IsNull(Read(Body()).CacheWriteTokens, "not reported");
        Assert.AreEqual(512L, Read(Body(usage: Usage.Replace("{\"cached_tokens\":0}", "{\"cached_tokens\":0,\"cache_write_tokens\":512}", StringComparison.Ordinal))).CacheWriteTokens);
        AdvisoryAiResponse text = Read(Body(usage: Usage.Replace("{\"cached_tokens\":0}", "{\"cached_tokens\":0,\"cache_write_tokens\":\"x\"}", StringComparison.Ordinal)));
        Assert.IsNull(text.CacheWriteTokens);
        Assert.IsTrue(text.CacheWriteUnreadable);
        foreach (string written in new[] { "{\"cached_tokens\":0,\"cache_write_tokens\":512}", "{\"cached_tokens\":0,\"cache_write_tokens\":\"x\"}" })
        {
            AdvisoryAiResponse response = Read(Body(usage: Usage.Replace("{\"cached_tokens\":0}", written, StringComparison.Ordinal)));
            Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, response.Status, "the answer can still be shown");
            Assert.IsFalse(response.UsageReliable);
            Assert.IsTrue(response.UsageAboveApprovedBounds, "paid requests stop for review");
        }
        // A write is found even when the other details are missing or repeated.
        string write = Usage.Replace("{\"cached_tokens\":0}", "{\"cached_tokens\":0,\"cache_write_tokens\":512}", StringComparison.Ordinal);
        foreach (string incomplete in new[]
        {
            write.Replace("{\"reasoning_tokens\":120}", "{}", StringComparison.Ordinal),
            write.Replace("\"cached_tokens\":0,", string.Empty, StringComparison.Ordinal),
            write.Replace("\"cache_write_tokens\":512", "\"cache_write_tokens\":0,\"cache_write_tokens\":512", StringComparison.Ordinal)
        })
        {
            AdvisoryAiResponse response = Read(Body(usage: incomplete));
            Assert.IsFalse(response.UsageReliable);
            Assert.IsTrue(response.UsageAboveApprovedBounds, incomplete);
        }
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void UsageAboveTheApprovedBoundsIsFlagged()
    {
        Assert.IsTrue(Read(Body(), output: 299).UsageAboveApprovedBounds);
        Assert.IsTrue(Read(Body(), input: 899).UsageAboveApprovedBounds);
        Assert.IsFalse(Read(Body(), input: 900, output: 300).UsageAboveApprovedBounds, "exactly at the bound");
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnUnknownContentPartIsNotTrusted() =>
        Assert.AreEqual(AdvisoryAiMalformedReason.InvalidOutputText,
            Read(Body(output: "[{\"type\":\"message\",\"content\":[{\"type\":\"output_image\",\"url\":\"x\"}]}]")).MalformedReason);
    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("model", AdvisoryAiMalformedReason.ModelMismatch)]
    [DataRow("id", AdvisoryAiMalformedReason.MissingOrInvalidId)]
    [DataRow("store", AdvisoryAiMalformedReason.StoredResponse)]
    [DataRow("tools", AdvisoryAiMalformedReason.ToolsPresent)]
    [DataRow("status", AdvisoryAiMalformedReason.UnknownStatus)]
    public void ACacheWriteInARefusedBodyStillStopsPaidRequestsForReview(string change, AdvisoryAiMalformedReason expected)
    {
        // Task 70B T70B-02: the body is refused before its usage is read, but the reported write is still an unexpected billing
        // category, so settlement keeps the whole reservation and stops for review. Without a write, a refused body only keeps it.
        AdvisoryAiTariff tariff = new(1, Model, "default", new(2_000_000), new(100_000), new(10_000_000), DateTimeOffset.UnixEpoch);
        foreach ((string written, bool review) in new[] { ("\"cache_write_tokens\":12", true), ("\"cache_write_tokens\":\"x\"", true), ("\"cache_write_tokens\":0", false) })
        {
            string body = Body(usage: Usage.Replace("{\"cached_tokens\":0}", "{\"cached_tokens\":0," + written + "}", StringComparison.Ordinal));
            body = change switch
            {
                "model" => body.Replace(Model, "other-model", StringComparison.Ordinal),
                "id" => body.Replace("\"resp_abc123\"", "\"\"", StringComparison.Ordinal),
                "store" => body.Replace("\"store\":false", "\"store\":true", StringComparison.Ordinal),
                "tools" => body.Replace("\"tools\":[]", "\"tools\":[{}]", StringComparison.Ordinal),
                _ => body.Replace("\"status\":\"completed\"", "\"status\":\"queued\"", StringComparison.Ordinal)
            };
            AdvisoryAiResponse response = Read(body);
            Assert.AreEqual(AdvisoryAiAnswerStatus.Malformed, response.Status);
            Assert.AreEqual(expected, response.MalformedReason);
            // Independent review of a4db871, AG1, and the owner's review, 71C-01: another model, a stored response, tools or an
            // unsupported status pause on their own, with or without a write.
            bool pauses = review || change is "model" or "store" or "tools" or "status";
            Assert.AreEqual(pauses, response.UsageAboveApprovedBounds, written);
            AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), tariff);
            Assert.AreEqual(AdvisoryAiRequestState.Uncertain, settlement.State);
            Assert.AreEqual(45_056, settlement.Committed.Value, "the whole reservation is kept");
            Assert.AreEqual(pauses, settlement.NeedsReview, written);
        }
    }
    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("usage", "12", "0", DisplayName = "two usage objects, write then zero")]
    [DataRow("usage", "0", "12", DisplayName = "two usage objects, zero then write")]
    [DataRow("usage", "12", "missing", DisplayName = "two usage objects, write then none")]
    [DataRow("usage", "missing", "12", DisplayName = "two usage objects, none then write")]
    [DataRow("usage", "12", "null", DisplayName = "usage with a write, then usage null")]
    [DataRow("usage", "\"x\"", "0", DisplayName = "two usage objects, unreadable then zero")]
    [DataRow("details", "12", "0", DisplayName = "two input details, write then zero")]
    [DataRow("details", "0", "12", DisplayName = "two input details, zero then write")]
    [DataRow("details", "12", "missing", DisplayName = "two input details, write then none")]
    [DataRow("details", "missing", "12", DisplayName = "two input details, none then write")]
    public void ARepeatedBillingContainerCanNotHideACacheWrite(string container, string first, string last)
    {
        // Task 70C T70C-01: a repeated container is ambiguous billing evidence. The write it might hide stops paid requests for review,
        // usage is never trusted, and the whole reservation is kept, whichever occurrence comes last.
        static string Details(string write) => write == "missing" ? "{\"cached_tokens\":0}" : "{\"cached_tokens\":0,\"cache_write_tokens\":" + write + "}";
        static string UsageWith(string details) => "{\"input_tokens\":900,\"input_tokens_details\":" + details + ",\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":120},\"total_tokens\":1200}";
        string usage = container == "usage"
            ? "\"usage\":" + UsageWith(Details(first)) + ",\"usage\":" + (last == "null" ? "null" : UsageWith(Details(last)))
            : "\"usage\":{\"input_tokens\":900,\"input_tokens_details\":" + Details(first) + ",\"input_tokens_details\":" + Details(last) +
              ",\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":120},\"total_tokens\":1200}";
        AdvisoryAiResponse response = Read(Body(usage: usage));

        Assert.IsFalse(response.UsageReliable);
        Assert.IsTrue(response.CacheWriteUnreadable, "an ambiguous figure is never shown as exact");
        Assert.IsNull(response.CacheWriteTokens);
        Assert.IsTrue(response.UsageAboveApprovedBounds, "paid requests stop for review");
        AdvisoryAiTariff tariff = new(1, Model, "default", new(2_000_000), new(100_000), new(10_000_000), DateTimeOffset.UnixEpoch);
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(12_000), tariff);
        Assert.AreEqual(AdvisoryAiRequestState.Uncertain, settlement.State);
        Assert.AreEqual(12_000, settlement.Committed.Value);
        Assert.IsTrue(settlement.NeedsReview);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void ARepeatedContainerWithoutAnyCacheWriteFieldKeepsTheReservationWithoutAPause()
    {
        // No cache-write field anywhere: the duplicate makes the body malformed (reservation kept) but carries no billing signal.
        string usage = Usage + "," + Usage;
        AdvisoryAiResponse response = Read(Body(usage: usage));
        Assert.AreEqual(AdvisoryAiMalformedReason.DuplicateProperty, response.MalformedReason);
        Assert.IsFalse(response.CacheWriteUnreadable);
        Assert.IsFalse(response.UsageAboveApprovedBounds);
    }

    private static readonly AdvisoryAiTariff ReviewTariff = new(1, Model, "default", new(2_000_000), new(100_000), new(10_000_000), DateTimeOffset.UnixEpoch);

    /// <summary>As the production overload reads it: the requested tier is "default" and the request asked for no prompt cache.</summary>
    private static AdvisoryAiResponse ReadAsRequested(string json) =>
        AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(json), Model, 1_000_000, 8192, 1024, "default", noPromptCache: true);

    private static void AssertPausedWithWholeReservation(AdvisoryAiResponse response, string because)
    {
        Assert.IsFalse(response.UsageReliable, because);
        Assert.IsTrue(response.UsageAboveApprovedBounds, because);
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.AreEqual(AdvisoryAiRequestState.Uncertain, settlement.State, because);
        Assert.AreEqual(45_056, settlement.Committed.Value, because);
        Assert.IsTrue(settlement.NeedsReview, because);
        Assert.AreEqual(AdvisoryAiReviewCause.UntrustedReply, settlement.ReviewCauses, because);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow(",\"service_tier\":\"default\"", "default", DisplayName = "the requested tier")]
    [DataRow("", null, DisplayName = "no tier reported")]
    [DataRow(",\"service_tier\":null", null, DisplayName = "a null tier")]
    public void TheRequestedTierOrNoTierSettlesNormally(string tier, string? shown)
    {
        AdvisoryAiResponse response = ReadAsRequested(Body(extra: tier));
        Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, response.Status);
        Assert.AreEqual(shown, response.ServiceTier);
        Assert.AreEqual(AdvisoryAiResponseDeparture.None, response.Departures);
        Assert.IsTrue(response.UsageReliable);
        Assert.IsFalse(response.UsageAboveApprovedBounds);
        Assert.AreEqual(AdvisoryAiRequestState.Settled, AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff).State);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow(",\"service_tier\":\"priority\"", "priority")]
    [DataRow(",\"service_tier\":\"fast\"", "fast")]
    [DataRow(",\"service_tier\":\"ultrafast\"", "ultrafast")]
    [DataRow(",\"service_tier\":\"flex\"", "flex")]
    [DataRow(",\"service_tier\":\"auto\"", "auto")]
    [DataRow(",\"service_tier\":\"Default\"", null, DisplayName = "not a plain lower-case name")]
    [DataRow(",\"service_tier\":7", null, DisplayName = "not a name")]
    [DataRow(",\"service_tier\":[\"default\"]", null, DisplayName = "a list")]
    [DataRow(",\"service_tier\":\"reviewaccount\"", null, DisplayName = "an unknown name, here the PC's account name")]
    [DataRow(",\"service_tier\":\"sk_live_short\"", null, DisplayName = "an unknown, secret-shaped name")]
    public void AReplyFromAnotherServiceTierStopsPaidRequestsForReview(string tier, string? shown)
    {
        // Independent review F1: the request asks for, and the owner priced, the default tier. A reply naming another tier may have
        // been charged at that tier's price, so its usage can't settle the charge at the confirmed prices.
        AdvisoryAiResponse response = ReadAsRequested(Body(extra: tier));
        Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, response.Status, "the answer itself is still shown");
        Assert.AreEqual(shown, response.ServiceTier);
        // Task 70E T70E-01: an unknown name is never kept, only reported as unrecognised; a known one is the app's own constant.
        Assert.AreEqual(shown is null ? AdvisoryAiServiceTierReport.Unrecognised : AdvisoryAiServiceTierReport.Named, response.ServiceTierReport);
        if (shown is not null) Assert.IsTrue(AdvisoryAiResponseReader.KnownServiceTiers.Contains(shown));
        Assert.AreEqual(AdvisoryAiResponseDeparture.ServiceTier, response.Departures);
        AssertPausedWithWholeReservation(response, tier);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void AnotherTierStillStopsPaidRequestsWhenTheBodyIsRefusedOrTheTierIsRepeated()
    {
        AdvisoryAiResponse otherModel = ReadAsRequested(Body(extra: ",\"service_tier\":\"priority\"").Replace(Model, "other-model", StringComparison.Ordinal));
        Assert.AreEqual(AdvisoryAiMalformedReason.ModelMismatch, otherModel.MalformedReason);
        AssertPausedWithWholeReservation(otherModel, "refused for another reason");

        AdvisoryAiResponse repeated = ReadAsRequested(Body(extra: ",\"service_tier\":\"priority\",\"service_tier\":\"default\""));
        Assert.AreEqual(AdvisoryAiMalformedReason.DuplicateProperty, repeated.MalformedReason);
        Assert.IsNull(repeated.ServiceTier);
        Assert.AreEqual(AdvisoryAiServiceTierReport.Repeated, repeated.ServiceTierReport);
        AssertPausedWithWholeReservation(repeated, "a repeated tier can't hide another one");

        AdvisoryAiResponse unknownRefused = ReadAsRequested(Body(extra: ",\"service_tier\":\"reviewaccount\"").Replace("\"store\":false", "\"store\":true", StringComparison.Ordinal));
        Assert.AreEqual(AdvisoryAiMalformedReason.StoredResponse, unknownRefused.MalformedReason);
        Assert.IsNull(unknownRefused.ServiceTier, "an early-refused body keeps no provider tier text either");
        Assert.AreEqual(AdvisoryAiServiceTierReport.Unrecognised, unknownRefused.ServiceTierReport);
        AssertPausedWithWholeReservation(unknownRefused, "an unknown tier in a refused body");
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow(",\"service_tier\":\"default\",\"service_tier\":null", DisplayName = "default then null")]
    [DataRow(",\"service_tier\":null,\"service_tier\":\"default\"", DisplayName = "null then default")]
    [DataRow(",\"service_tier\":null,\"service_tier\":null", DisplayName = "two nulls")]
    [DataRow(",\"service_tier\":\"default\",\"service_tier\":\"default\"", DisplayName = "the requested tier twice")]
    public void ARepeatedTierPausesEvenWhenACopyIsNull(string tiers)
    {
        // Task 70E T70E-02: every occurrence counts, null ones included, so a repeated tier always stops paid requests for review.
        AdvisoryAiResponse response = ReadAsRequested(Body(extra: tiers));
        Assert.AreEqual(AdvisoryAiMalformedReason.DuplicateProperty, response.MalformedReason);
        Assert.AreEqual(AdvisoryAiServiceTierReport.Repeated, response.ServiceTierReport);
        Assert.IsNull(response.ServiceTier);
        Assert.IsTrue(response.Departures.HasFlag(AdvisoryAiResponseDeparture.ServiceTier));
        AssertPausedWithWholeReservation(response, tiers);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("\"input_tokens\":900,", "\"input_tokens\":900,\"audio_tokens\":5,", DisplayName = "an extra usage counter")]
    [DataRow("\"total_tokens\":1200", "\"total_tokens\":1200,\"cache_write_tokens\":40", DisplayName = "a cache write one level up")]
    [DataRow("{\"cached_tokens\":0}", "{\"cached_tokens\":0,\"cache_creation_tokens\":40}", DisplayName = "a cache write under another name")]
    [DataRow("{\"reasoning_tokens\":120}", "{\"reasoning_tokens\":120,\"accepted_prediction_tokens\":3}", DisplayName = "an extra output counter")]
    public void AnUnknownUsageCounterStopsPaidRequestsForReview(string from, string to)
    {
        // Independent review F2: a counter the reader doesn't know may be a billing category the owner never priced.
        AdvisoryAiResponse response = ReadAsRequested(Body(usage: Usage.Replace(from, to, StringComparison.Ordinal)));
        Assert.IsTrue(response.Departures.HasFlag(AdvisoryAiResponseDeparture.UnknownUsageField));
        AssertPausedWithWholeReservation(response, to);
    }

    /// <summary>The input details as the first live test's reply carried them (Task 71): every cache count zero, with the retention breakdown.</summary>
    private const string LiveInputDetails = "{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_30m_tokens\":0}}";

    private static string WithInputDetails(string details) => Body(usage: Usage.Replace("{\"cached_tokens\":0}", details, StringComparison.Ordinal));

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow(LiveInputDetails, 0L, DisplayName = "the first live test's shape")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":null}", 0L, DisplayName = "a null breakdown")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_30m_tokens\":0}}", 0L, DisplayName = "one retention count")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_30m_tokens\":0}}", null, DisplayName = "a zero breakdown with no total")]
    public void AZeroCacheWriteBreakdownSettlesNormally(string details, long? shownWrites)
    {
        // First live test (Task 71): OpenAI added cache_write_tokens_details after SDK 2.15.0. All-zero counts are no write and no
        // departure, so the request settles at its actual cost instead of keeping the whole reservation.
        AdvisoryAiResponse response = ReadAsRequested(WithInputDetails(details));
        Assert.AreEqual(AdvisoryAiResponseDeparture.None, response.Departures);
        Assert.AreEqual(AdvisoryAiUsageFieldLocation.None, response.UnknownUsageFieldLocations);
        Assert.IsFalse(response.CacheWriteUnreadable);
        Assert.AreEqual(shownWrites, response.CacheWriteTokens);
        Assert.IsTrue(response.UsageReliable);
        Assert.IsFalse(response.UsageAboveApprovedBounds);
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.AreEqual(AdvisoryAiRequestState.Settled, settlement.State);
        Assert.AreEqual(4_800, settlement.Committed.Value, "900 input at $2 and 300 output at $10 per million");
        Assert.IsFalse(settlement.NeedsReview);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":40,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":40,\"ttl_30m_tokens\":0}}", 40L, false, DisplayName = "a write that adds up")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_30m_tokens\":7}}", null, true, DisplayName = "a breakdown write behind a zero total")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":7,\"ttl_30m_tokens\":0}}", null, true, DisplayName = "a breakdown write with no total")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":40,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_30m_tokens\":0}}", null, true, DisplayName = "a total the breakdown doesn't add up to")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":\"0\",\"ttl_30m_tokens\":0}}", null, true, DisplayName = "a count as text")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":-1,\"ttl_30m_tokens\":1}}", null, true, DisplayName = "a negative count")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0.5,\"ttl_30m_tokens\":0}}", null, true, DisplayName = "a fraction")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":{\"x\":0},\"ttl_30m_tokens\":0}}", null, true, DisplayName = "a nested object")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":9223372036854775807,\"ttl_30m_tokens\":1}}", null, true, DisplayName = "counts too large to add")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_12h_tokens\":5}}", null, true, DisplayName = "a repeated count")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":[0,0]}", null, true, DisplayName = "an array")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":0}", null, true, DisplayName = "a number")]
    public void ACacheWriteOrUnreadableBreakdownStopsPaidRequestsForReview(string details, long? shownWrites, bool unreadable)
    {
        // A write in the breakdown is a write, whatever the total says, and anything that isn't plain counts that add up is unreadable.
        AdvisoryAiResponse response = ReadAsRequested(WithInputDetails(details));
        Assert.AreEqual(shownWrites, response.CacheWriteTokens, details);
        Assert.AreEqual(unreadable, response.CacheWriteUnreadable, details);
        AssertPausedWithWholeReservation(response, details);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void TheFirstLiveRequestsReplySettlesAtExactlyItsCost()
    {
        // Task 71C review 71C-05: the counts the first live request reported (491 input, none cached, 321 output, no reasoning, a zero
        // cache-write total with its zero retention breakdown, the default tier) settle at exactly 491 x $2 + 321 x $10 per million,
        // which is $0.004192, and release the rest of the reservation without a pause.
        string usage = "\"usage\":{\"input_tokens\":491,\"input_tokens_details\":" + LiveInputDetails +
            ",\"output_tokens\":321,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":812}";
        AdvisoryAiResponse response = ReadAsRequested(Body(usage: usage, extra: ",\"service_tier\":\"default\""));
        Assert.AreEqual(AdvisoryAiResponseDeparture.None, response.Departures);
        Assert.IsTrue(response.UsageReliable);
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.AreEqual(AdvisoryAiRequestState.Settled, settlement.State);
        Assert.AreEqual(4_192, settlement.Committed.Value);
        Assert.IsFalse(settlement.NeedsReview);
        Assert.AreEqual(AdvisoryAiReviewCause.None, settlement.ReviewCauses);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow(",\"metadata\":{\"note\":\"\\uD800\"}", "[{\"type\":\"reasoning\",\"summary\":[]}", DisplayName = "a metadata value nothing reads")]
    [DataRow(",\"metadata\":{\"a\":{\"b\":[{\"c\\uDC00\":1}]}}", "[{\"type\":\"reasoning\",\"summary\":[]}", DisplayName = "a nested name nothing reads")]
    [DataRow("", "[{\"type\":\"reasoning\",\"summary\":[{\"type\":\"summary_text\",\"text\":\"x\\uD800y\"}]}", DisplayName = "text in a skipped reasoning item")]
    [DataRow("", "[{\"type\":\"reasoning\",\"encrypted_content\":\"\\uDFFF\",\"summary\":[]}", DisplayName = "an opaque value in a skipped item")]
    [DataRow(",\"usage_note\\uD800\":null", "[{\"type\":\"reasoning\",\"summary\":[]}", DisplayName = "a top-level name after usage")]
    public void UndecodableTextAnywhereInTheReplyStopsPaidRequestsForReview(string extra, string firstItems)
    {
        // Task 71C review 71C-R1: the reader never visits metadata, the contents of reasoning items or names it doesn't look up, so
        // undecodable text there threw nothing and the live usage below settled at its actual cost. Every name and string is now
        // decoded first, so the reply is untrusted, keeps the whole reservation and pauses paid requests wherever the text sits.
        string usage = "\"usage\":{\"input_tokens\":491,\"input_tokens_details\":" + LiveInputDetails +
            ",\"output_tokens\":321,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":812}";
        string output = firstItems + ",{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"Line one.\",\"annotations\":[]}]}]";
        string json = Body(output: output, usage: usage, extra: ",\"service_tier\":\"default\"" + extra);
        string clean = json.Replace("\\uD800", "", StringComparison.Ordinal).Replace("\\uDC00", "", StringComparison.Ordinal).Replace("\\uDFFF", "", StringComparison.Ordinal);
        Assert.IsTrue(ReadAsRequested(clean).UsageReliable, "without the undecodable text the same reply settles: " + clean);

        AdvisoryAiResponse response = ReadAsRequested(json);
        Assert.AreEqual(AdvisoryAiAnswerStatus.Malformed, response.Status, json);
        Assert.AreEqual(AdvisoryAiMalformedReason.UndecodableText, response.MalformedReason, json);
        Assert.IsNull(response.Text, json);
        AssertPausedWithWholeReservation(response, json);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void InvalidUtf8InAValueNothingReadsStopsPaidRequestsForReview()
    {
        // Task 71C review 71C-R1: raw bytes that aren't UTF-8 also parse and fail only when decoded.
        string json = Body(extra: ",\"metadata\":{\"note\":\"@@\"}");
        byte[] body = Encoding.UTF8.GetBytes(json);
        int at = Array.IndexOf(body, (byte)'@');
        body[at] = 0xC3;
        body[at + 1] = 0x28;
        Assert.IsFalse(AdvisoryAiResponseReader.DecodesInFull(body));
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(body, Model, 1_000_000, 8192, 1024, "default", noPromptCache: true);
        Assert.AreEqual(AdvisoryAiMalformedReason.UndecodableText, response.MalformedReason);
        AssertPausedWithWholeReservation(response, json);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_\\uD800\":0}}", "", "Line one.", DisplayName = "an undecodable key in the cache-write details")]
    [DataRow(LiveInputDetails, ",\"x\\uDC00\":1", "Line one.", DisplayName = "an undecodable key at the top")]
    [DataRow(LiveInputDetails, "", "bad \\uD800 text", DisplayName = "undecodable answer text")]
    public void AReplyThatCanNotBeDecodedStopsPaidRequestsForReview(string details, string extra, string text)
    {
        // Task 71C review 71C-01: System.Text.Json accepts an unpaired escaped surrogate while parsing and throws only when the name or
        // string is read. That reply is malformed, never trusted, keeps the whole reservation and pauses paid requests.
        string output = "[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"" + text + "\",\"annotations\":[]}]}]";
        string json = Body(output: output, usage: Usage.Replace("{\"cached_tokens\":0}", details, StringComparison.Ordinal), extra: extra);
        AdvisoryAiResponse response = ReadAsRequested(json);
        Assert.AreEqual(AdvisoryAiAnswerStatus.Malformed, response.Status, json);
        Assert.AreEqual(AdvisoryAiMalformedReason.UndecodableText, response.MalformedReason, json);
        Assert.IsNull(response.Text, json);
        AssertPausedWithWholeReservation(response, json);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void ARepeatedBreakdownCanNotHideACacheWrite()
    {
        string repeated = "{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_30m_tokens\":0}," +
            "\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_30m_tokens\":0}}";
        AdvisoryAiResponse response = ReadAsRequested(WithInputDetails(repeated));
        Assert.IsTrue(response.CacheWriteUnreadable);
        AssertPausedWithWholeReservation(response, "a repeated breakdown");
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("\"input_tokens\":900,", "\"input_tokens\":900,\"audio_tokens\":5,", AdvisoryAiUsageFieldLocation.UsageTotals, DisplayName = "in the usage totals")]
    [DataRow("{\"cached_tokens\":0}", "{\"cached_tokens\":0,\"cache_creation_tokens\":40}", AdvisoryAiUsageFieldLocation.InputDetails, DisplayName = "in the input details")]
    [DataRow("{\"reasoning_tokens\":120}", "{\"reasoning_tokens\":120,\"accepted_prediction_tokens\":3}", AdvisoryAiUsageFieldLocation.OutputDetails, DisplayName = "in the output details")]
    [DataRow("{\"cached_tokens\":0}", "{\"cached_tokens\":0,\"cache_write_tokens\":0,\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_30m_tokens\":0,\"ttl_24h_tokens\":0}}",
        AdvisoryAiUsageFieldLocation.CacheWriteDetails, DisplayName = "a new retention class in the breakdown")]
    public void AnUnknownUsageCounterSaysWhereItWasWithoutItsName(string from, string to, AdvisoryAiUsageFieldLocation where)
    {
        // First live test: the location is PC Steward's own closed flag, so an unknown counter can be traced without keeping its name.
        AdvisoryAiResponse response = ReadAsRequested(Body(usage: Usage.Replace(from, to, StringComparison.Ordinal)));
        Assert.IsTrue(response.Departures.HasFlag(AdvisoryAiResponseDeparture.UnknownUsageField));
        Assert.AreEqual(where, response.UnknownUsageFieldLocations);
        AssertPausedWithWholeReservation(response, to);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void CachedInputOnANoCacheRequestStopsPaidRequestsForReview()
    {
        // Independent review F2: the request asked for no prompt cache, so cached input means the request wasn't honoured as approved.
        string cached = Body(usage: Usage.Replace("\"cached_tokens\":0", "\"cached_tokens\":512", StringComparison.Ordinal));
        AdvisoryAiResponse response = ReadAsRequested(cached);
        Assert.AreEqual(AdvisoryAiResponseDeparture.PromptCacheUsed, response.Departures);
        AssertPausedWithWholeReservation(response, "cached input");
        Assert.IsTrue(Read(cached).UsageReliable, "a profile that allows the cache settles it at the cached rate");
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    [DataRow(",\"background\":true")]
    [DataRow(",\"previous_response_id\":\"resp_earlier\"")]
    [DataRow(",\"conversation\":{\"id\":\"conv_1\"}")]
    public void ConversationStateTheRequestNeverAskedForIsNotAnAnswer(string state)
    {
        // Independent review F2: a reply that ran in the background or continued other state isn't an answer to this request.
        AdvisoryAiResponse response = ReadAsRequested(Body(extra: state));
        Assert.AreEqual(AdvisoryAiAnswerStatus.Malformed, response.Status);
        Assert.AreEqual(AdvisoryAiMalformedReason.UnexpectedState, response.MalformedReason);
        Assert.IsNull(response.Text);
        AssertPausedWithWholeReservation(response, state);
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public void ExplicitlyEmptyConversationStateIsAccepted()
    {
        AdvisoryAiResponse response = ReadAsRequested(Body(extra: ",\"background\":false,\"previous_response_id\":null,\"conversation\":null"));
        Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, response.Status);
        Assert.IsTrue(response.UsageReliable);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("store", 60_982, DisplayName = "stored: the reported charge is kept in full")]
    [DataRow("tools", 60_982, DisplayName = "tools: the reported charge is kept in full")]
    [DataRow("status", 60_982, DisplayName = "unknown status: the reported charge is kept in full")]
    [DataRow("model", 45_056, DisplayName = "another model: the reservation is kept")]
    [DataRow("id", 45_056, DisplayName = "no id: the reservation is kept")]
    public void ARefusedBodyReportingUsageAboveTheBoundsStillStopsPaidRequests(string change, long committed)
    {
        // Codex review of bf637b5, 71C-01: a body refused early (stored, with tools, another model) used to drop its usage, so a
        // reported 6,000 output tokens on a 2,048-token approval was kept only as the reservation, with no pause.
        string over = "\"usage\":{\"input_tokens\":491,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":6000,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":6491}";
        string body = Body(output: "[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"OK\",\"annotations\":[]}]}]", usage: over);
        body = change switch
        {
            "model" => body.Replace(Model, "other-model", StringComparison.Ordinal),
            "id" => body.Replace("\"resp_abc123\"", "\"\"", StringComparison.Ordinal),
            "store" => body.Replace("\"store\":false", "\"store\":true", StringComparison.Ordinal),
            "tools" => body.Replace("\"tools\":[]", "\"tools\":[{}]", StringComparison.Ordinal),
            _ => body.Replace("\"status\":\"completed\"", "\"status\":\"queued\"", StringComparison.Ordinal)
        };
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.AreEqual(AdvisoryAiAnswerStatus.Malformed, response.Status);
        Assert.IsNull(response.Text);
        Assert.IsFalse(response.UsageReliable);
        Assert.IsTrue(response.UsageAboveApprovedBounds);
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.AreEqual(AdvisoryAiRequestState.Uncertain, settlement.State);
        Assert.AreEqual(committed, settlement.Committed.Value);
        Assert.IsTrue(settlement.NeedsReview);

        // Within the bounds, a body refused for no id still only keeps the reservation, without a pause. Independent review of
        // a4db871, AG1, and the owner's review, 71C-01: one that shows another model, storage, tools or an unsupported status pauses,
        // since those are charged outside the reservation or say the provider may still be working.
        bool neverAllowed = change is "model" or "store" or "tools" or "status";
        string within = body.Replace("6000", "300", StringComparison.Ordinal).Replace("6491", "791", StringComparison.Ordinal);
        AdvisoryAiResponse refused = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(within), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.AreEqual(neverAllowed, refused.UsageAboveApprovedBounds);
        AdvisoryAiSettlement kept = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, refused, new(45_056), ReviewTariff);
        Assert.AreEqual(45_056, kept.Committed.Value);
        Assert.AreEqual(neverAllowed, kept.NeedsReview);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("\"input_tokens\":12289", DisplayName = "input above the approval")]
    [DataRow("\"total_tokens\":99999", DisplayName = "total above the approval")]
    [DataRow("\"output_tokens\":1e400", DisplayName = "a count too large for a double")]
    [DataRow("\"output_tokens\":2048.5", DisplayName = "a fraction above the approval")]
    public void AnyCountAboveTheApprovalStopsPaidRequestsEvenWhenTheUsageIsNotTrusted(string count)
    {
        // Codex review of bf637b5, 71C-01: each count is read on its own, so one the usage reader can't trust still pauses.
        string usage = "\"usage\":{\"input_tokens\":491,\"output_tokens\":300,\"total_tokens\":791," + count + "}";
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(usage: usage)), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.IsFalse(response.UsageReliable);
        Assert.IsTrue(response.UsageAboveApprovedBounds);
        Assert.IsTrue(AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff).NeedsReview);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("input_tokens", "12288.0000000000000000000001", DisplayName = "input a whisker above the approval")]
    [DataRow("output_tokens", "2048.0000000000000000000001", DisplayName = "output a whisker above the approval")]
    [DataRow("total_tokens", "14336.0000000000000000000001", DisplayName = "total a whisker above the approval")]
    [DataRow("output_tokens", "2048.0", DisplayName = "output at the approval written with a fraction")]
    [DataRow("output_tokens", "2.048e3", DisplayName = "output at the approval written with an exponent")]
    [DataRow("input_tokens", "491.5", DisplayName = "input below the approval with a fraction")]
    [DataRow("output_tokens", "1E-400", DisplayName = "output too small for a double")]
    [DataRow("total_tokens", "99999999999999999999", DisplayName = "total too large for a long")]
    public void ACountThatIsNotAPlainWholeNumberStopsPaidRequestsInEveryBodyShape(string name, string written)
    {
        // Codex review of 2cdfcc4, 71C-01: read as a double, 2048.0000000000000000000001 became exactly 2,048, so a count above the
        // approval passed the strict bound check and a refused body kept only the reservation with no pause. A count that isn't a
        // plain whole number can't be compared exactly, so it now stops paid requests whatever its size, in a normal body, one refused
        // early and one wrapped in another shape.
        string usage = ("\"usage\":{\"input_tokens\":491,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300," +
            "\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":791}").Replace("\"" + name + "\":" + (name switch
            {
                "input_tokens" => "491",
                "output_tokens" => "300",
                _ => "791"
            }), "\"" + name + "\":" + written, StringComparison.Ordinal);
        StringAssert.Contains(usage, written);
        string normal = Body(usage: usage);
        foreach ((string shape, string body) in new[]
        {
            ("normal", normal),
            ("refused as stored", normal.Replace("\"store\":false", "\"store\":true", StringComparison.Ordinal)),
            ("refused as another model", normal.Replace(Model, "other-model", StringComparison.Ordinal)),
            ("wrapped in an array", "[" + normal + "]"),
            ("wrapped in an event", "{\"type\":\"response.completed\",\"response\":" + normal + "}")
        })
        {
            AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
            Assert.IsFalse(response.UsageReliable, shape);
            Assert.IsTrue(response.UsageAboveApprovedBounds, shape);
            AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
            Assert.AreEqual(AdvisoryAiRequestState.Uncertain, settlement.State, shape);
            Assert.AreEqual(45_056, settlement.Committed.Value, shape);
            Assert.IsTrue(settlement.NeedsReview, shape);
            Assert.IsTrue(settlement.ReviewCause.HasFlag(AdvisoryAiReviewCause.UntrustedReply), shape);
        }
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("\"reasoning_tokens\":0", "\"reasoning_tokens\":2049", DisplayName = "reasoning tokens above the output approval")]
    [DataRow("\"reasoning_tokens\":0", "\"reasoning_tokens\":99999", DisplayName = "reasoning tokens far above the output approval")]
    [DataRow("\"cached_tokens\":0", "\"cached_tokens\":12289", DisplayName = "cached tokens above the input approval")]
    [DataRow("\"output_tokens\":300", "\"output_tokens\":\"2049\"", DisplayName = "output written as a string")]
    [DataRow("\"output_tokens\":300", "\"output_tokens\":[99999]", DisplayName = "output written as an array")]
    [DataRow("\"output_tokens\":300", "\"output_tokens\":true", DisplayName = "output written as true")]
    [DataRow("\"total_tokens\":791", "\"total_tokens\":\"791\"", DisplayName = "total within the approval written as a string")]
    [DataRow("\"total_tokens\":791", "\"completion_tokens\":99999", DisplayName = "another count name above the total, with no total")]
    [DataRow("\"cached_tokens\":0", "\"cached_tokens\":99999", DisplayName = "cached tokens far above the input approval")]
    public void EveryCountUnderUsageIsBoundedByItsPlaceInEveryBodyShape(string from, string to)
    {
        // Independent review of 2de2ec6, AA1: only input_tokens, output_tokens and total_tokens written as numbers were read, so
        // reasoning tokens above the output approval, a count written as a string, or (in a wrapped body, where the usage reader
        // doesn't run) a cached or other count above the approval kept the reservation with no pause. Every count under a "usage"
        // object is now bounded by its place: input details by the input approval, output details by the output approval, and any
        // other name by the total.
        string usage = "\"usage\":{\"input_tokens\":491,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300," +
            "\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":791}";
        Assert.AreEqual(1, usage.Split(from).Length - 1, from);
        string normal = Body(usage: usage.Replace(from, to, StringComparison.Ordinal));
        foreach ((string shape, string body) in new[]
        {
            ("normal", normal),
            ("refused as stored", normal.Replace("\"store\":false", "\"store\":true", StringComparison.Ordinal)),
            ("refused as another model", normal.Replace(Model, "other-model", StringComparison.Ordinal)),
            ("wrapped in an array", "[" + normal + "]"),
            ("wrapped in an event", "{\"type\":\"response.completed\",\"response\":" + normal + "}"),
            ("wrapped in a list", "{\"object\":\"list\",\"data\":[" + normal + "]}")
        })
        {
            AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
            Assert.IsFalse(response.UsageReliable, shape);
            Assert.IsTrue(response.UsageAboveApprovedBounds, shape);
            AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
            Assert.AreEqual(AdvisoryAiRequestState.Uncertain, settlement.State, shape);
            Assert.AreEqual(45_056, settlement.Committed.Value, shape);
            Assert.IsTrue(settlement.NeedsReview, shape);
        }
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void TheFirstLiveReplyShapeStillSettlesWithEveryCountReadByItsPlace()
    {
        // Independent review of 2de2ec6, AA1: the first live reply (491 input, 321 output, zero cached, cache-write and reasoning
        // details) is within every place's bound, so it still settles at its real cost, and a null detail is no count.
        string usage = "\"usage\":{\"input_tokens\":491,\"input_tokens_details\":{\"cached_tokens\":0,\"cache_write_tokens\":0," +
            "\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_30m_tokens\":0}},\"output_tokens\":321," +
            "\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":812}";
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(usage: usage)), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.IsTrue(response.UsageReliable);
        Assert.IsFalse(response.UsageAboveApprovedBounds);
        AdvisoryAiSettlement settled = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.AreEqual(AdvisoryAiRequestState.Settled, settled.State);
        Assert.AreEqual(4_192, settled.Committed.Value);
        Assert.IsFalse(settled.NeedsReview);

        string withNull = usage.Replace("\"cache_write_tokens_details\":{\"ttl_12h_tokens\":0,\"ttl_30m_tokens\":0}", "\"cache_write_tokens_details\":null", StringComparison.Ordinal);
        Assert.IsFalse(AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(usage: withNull)), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true).UsageAboveApprovedBounds);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void PlainWholeCountsAtTheApprovalStillKeepTheReservationWithoutAPause()
    {
        // Codex review of 2cdfcc4, 71C-01: the stricter rule only reaches counts that aren't plain whole numbers; whole counts exactly
        // at the approval in a body refused early keep the reservation without a pause, as before.
        string usage = "\"usage\":{\"input_tokens\":12288,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":2048," +
            "\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":14336}";
        // Independent review of a4db871, AG1: the early refusal is no id, since an echoed store now pauses on its own.
        string body = Body(usage: usage).Replace("\"resp_abc123\"", "\"\"", StringComparison.Ordinal);
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.IsFalse(response.UsageAboveApprovedBounds);
        AdvisoryAiSettlement kept = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.AreEqual(45_056, kept.Committed.Value);
        Assert.IsFalse(kept.NeedsReview);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("[{0}]")]
    [DataRow("{{\"type\":\"response.completed\",\"response\":{0}}}")]
    [DataRow("{{\"object\":\"list\",\"data\":[{0}]}}")]
    public void AResponseWrappedInAnotherShapeStillStopsPaidRequestsWhenItsUsageIsAboveTheBounds(string wrapper)
    {
        // Independent review of be08c02, W1: a response one level down was refused as not a response object before any usage was
        // read, so a count above the approval kept only the reservation, with no pause.
        string above = "\"usage\":{\"input_tokens\":491,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":99999,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":100490}";
        string body = string.Format(System.Globalization.CultureInfo.InvariantCulture, wrapper, Body(usage: above));
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.AreEqual(AdvisoryAiMalformedReason.NotAResponseObject, response.MalformedReason);
        Assert.IsTrue(response.UsageAboveApprovedBounds);
        AdvisoryAiSettlement kept = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(40_100), ReviewTariff);
        Assert.IsTrue(kept.NeedsReview);
        Assert.AreEqual(40_100, kept.Committed.Value);

        // Independent review of a99b79a, AC1: a wrapped reply within the bounds now pauses too, since its tier, cache write, state and
        // output length can't be checked in its place.
        string within = above.Replace("99999", "300", StringComparison.Ordinal).Replace("100490", "791", StringComparison.Ordinal);
        string calm = string.Format(System.Globalization.CultureInfo.InvariantCulture, wrapper, Body(usage: within));
        AdvisoryAiResponse wrapped = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(calm), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.IsTrue(wrapped.UsageAboveApprovedBounds, "a wrapped reply within the bounds stops paid requests for review");
        AdvisoryAiSettlement held = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, wrapped, new(40_100), ReviewTariff);
        Assert.IsTrue(held.NeedsReview);
        Assert.AreEqual(40_100, held.Committed.Value);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("long", DisplayName = "answer text far longer than the approved output")]
    [DataRow("priority", DisplayName = "the priority service tier")]
    [DataRow("flex", DisplayName = "the flex service tier")]
    [DataRow("cachewrite", DisplayName = "a cache write")]
    [DataRow("unknown", DisplayName = "an unknown usage counter")]
    [DataRow("background", DisplayName = "background state")]
    [DataRow("conversation", DisplayName = "conversation state")]
    public void AWrappedReplyStopsPaidRequestsWhateverItHolds(string change)
    {
        // Independent review of a99b79a, AC1: in a wrapped body only token counts were read, so a reply the normal path pauses on
        // (long text, another tier, a cache write, an unknown counter, background or conversation state) kept the reservation with no
        // pause when wrapped. A body that isn't a response object but carries usage or output now pauses, whatever it holds.
        string usage = Usage;
        string output = "[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"OK\",\"annotations\":[]}]}]";
        string extra = "";
        switch (change)
        {
            case "long":
                output = output.Replace("\"OK\"", "\"" + new string('x', 1_000_000) + "\"", StringComparison.Ordinal);
                break;
            case "priority":
            case "flex":
                extra = ",\"service_tier\":\"" + change + "\"";
                break;
            case "cachewrite":
                usage = usage.Replace("{\"cached_tokens\":0}", "{\"cached_tokens\":0,\"cache_write_tokens\":5}", StringComparison.Ordinal);
                break;
            case "unknown":
                usage = usage.Replace("{\"reasoning_tokens\":", "{\"other_tokens\":0,\"reasoning_tokens\":", StringComparison.Ordinal);
                break;
            case "background":
                extra = ",\"background\":true";
                break;
            default:
                extra = ",\"previous_response_id\":\"resp_before\"";
                break;
        }
        string body = Body(output: output, usage: usage, extra: extra);
        Assert.IsTrue(AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 2_000_000, 900, 300, "default", noPromptCache: true, maximumTokenUtf8Bytes: 4).UsageAboveApprovedBounds,
            "the same reply unwrapped already pauses");
        foreach (string wrapper in new[] { "[{0}]", "{{\"type\":\"response.completed\",\"response\":{0}}}", "{{\"object\":\"list\",\"data\":[{0}]}}", "{{\"a\":{{\"b\":[{0}]}}}}" })
        {
            string wrapped = string.Format(System.Globalization.CultureInfo.InvariantCulture, wrapper, body);
            AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(wrapped), Model, 2_000_000, 900, 300, "default", noPromptCache: true, maximumTokenUtf8Bytes: 4);
            Assert.AreEqual(AdvisoryAiMalformedReason.NotAResponseObject, response.MalformedReason, wrapper);
            Assert.IsTrue(response.UsageAboveApprovedBounds, wrapper);
            AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
            Assert.IsTrue(settlement.NeedsReview, wrapper);
            Assert.AreEqual(45_056, settlement.Committed.Value, wrapper);
        }
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("{\"type\":\"response.completed\",\"type\":\"response.completed\",\"response\":@@}", DisplayName = "an event wrapper with its type twice")]
    [DataRow("{\"type\":\"response.completed\",\"response\":@@,\"response\":@@}", DisplayName = "an event wrapper with its response twice")]
    [DataRow("{\"type\":\"response.completed\",\"sequence_number\":1,\"sequence_number\":2,\"response\":@@}", DisplayName = "an event wrapper with a sequence number twice")]
    [DataRow("{\"object\":\"list\",\"object\":\"list\",\"data\":[@@]}", DisplayName = "a list with its object twice")]
    [DataRow("{\"object\":\"list\",\"object\":\"response\",\"data\":[@@]}", DisplayName = "a list whose object is list, then response")]
    [DataRow("{\"object\":\"response\",\"object\":\"list\",\"data\":[@@]}", DisplayName = "a list whose object is response, then list")]
    [DataRow("{\"object\":\"list\",\"data\":[],\"data\":[@@]}", DisplayName = "a list with its data twice")]
    [DataRow("[{\"type\":\"response.completed\",\"type\":\"x\",\"response\":@@}]", DisplayName = "an array of a wrapper with a name twice")]
    [DataRow("{\"object\":\"response\",\"response\":@@}", DisplayName = "a wrapper whose object is response, the reply under response")]
    [DataRow("{\"id\":\"resp_outer\",\"object\":\"response\",\"model\":\"MODEL\",\"response\":@@}", DisplayName = "a wrapper with an id and model, the reply under response")]
    [DataRow("{\"id\":\"resp_outer\",\"object\":\"response\",\"model\":\"other-model\",\"response\":@@}", DisplayName = "a response with another model, the reply under response")]
    [DataRow("{\"object\":\"response\",\"data\":[@@]}", DisplayName = "a response with no id, the reply under data")]
    [DataRow("{\"id\":\"resp_outer\",\"object\":\"response\",\"status\":\"queued\",\"model\":\"MODEL\",\"response\":@@}", DisplayName = "a queued response, the reply under response")]
    [DataRow("{\"id\":\"resp_outer\",\"object\":\"response\",\"status\":\"failed\",\"model\":\"MODEL\",\"output\":[],\"response\":@@}", DisplayName = "a failed response with no output, the reply under response")]
    [DataRow("GOOD,\"metadata\":{\"r\":@@}}", DisplayName = "a good reply that also holds this reply in metadata")]
    [DataRow("GOOD,\"response\":@@}", DisplayName = "a good reply that also holds this reply under response")]
    public void AReplyOutOfItsPlaceStopsPaidRequestsWhateverSitsAroundIt(string wrapper)
    {
        // Independent review of dcef10c, AE1: #156 only paused a reply out of place when the body was refused as not a response
        // object, so a wrapper that repeated a name (refused for another reason) or a wrapper that called itself a response let a
        // reply through unread: its cache write, tier or state kept the reservation with no pause. Now any usage or output anywhere
        // but directly on a root response object pauses, whatever the body is refused for, and so does a good reply that also holds
        // one deeper down.
        string good = Body();
        string goodOpen = good.Substring(0, good.Length - 1);
        string cacheWrite = Body(usage: Usage.Replace("{\"cached_tokens\":0}", "{\"cached_tokens\":0,\"cache_write_tokens\":5}", StringComparison.Ordinal));
        foreach ((string reply, string what) in new[] { (good, "a calm reply"), (cacheWrite, "a reply with a cache write"), (Body(extra: ",\"service_tier\":\"priority\""), "a priority reply") })
        {
            string body = wrapper.Replace("GOOD", goodOpen, StringComparison.Ordinal).Replace("MODEL", Model, StringComparison.Ordinal).Replace("@@", reply, StringComparison.Ordinal);
            AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
            Assert.IsTrue(response.UsageAboveApprovedBounds, what);
            Assert.IsFalse(response.UsageReliable, what);
            AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
            Assert.IsTrue(settlement.NeedsReview, what);
            Assert.AreEqual(45_056, settlement.Committed.Value, what);
        }
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("[USAGE]", DisplayName = "usage is an array of the good usage")]
    [DataRow("[CACHEWRITE]", DisplayName = "usage is an array holding a cache write")]
    [DataRow("[UNKNOWN]", DisplayName = "usage is an array holding an unknown counter")]
    [DataRow("[CACHED]", DisplayName = "usage is an array holding cached input")]
    [DataRow("[]", DisplayName = "usage is an empty array")]
    [DataRow("{\"input_tokens\":{\"cache_write_tokens\":5},\"output_tokens\":300,\"total_tokens\":1200}", DisplayName = "input_tokens is an object holding a cache write")]
    [DataRow("{\"input_tokens\":900,\"input_tokens_details\":{\"cached_tokens\":{\"cache_write_tokens\":5}},\"output_tokens\":300,\"total_tokens\":1200}", DisplayName = "cached_tokens is an object holding a cache write")]
    [DataRow("{\"input_tokens\":900,\"input_tokens_details\":[{\"cached_tokens\":0,\"cache_write_tokens\":5}],\"output_tokens\":300,\"total_tokens\":1200}", DisplayName = "input details is an array holding a cache write")]
    [DataRow("{\"input_tokens\":900,\"output_tokens\":{\"a\":300},\"total_tokens\":1200}", DisplayName = "output_tokens is an object of counts within the approval")]
    [DataRow("{\"input_tokens\":900,\"output_tokens\":300,\"total_tokens\":{\"a\":1200}}", DisplayName = "total_tokens is an object of counts within the approval")]
    [DataRow("{\"input_tokens\":900,\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":{\"a\":1}},\"total_tokens\":1200}", DisplayName = "reasoning_tokens is an object of counts within the approval")]
    public void UsageInAnOddContainerStopsPaidRequestsEvenWithCountsWithinTheApproval(string usage)
    {
        // Independent review of dcef10c, AE1: an unwrapped usage array was read only for counts above the approval, so a cache
        // write, an unknown counter or cached input inside it (and a cache write under input_tokens or cached_tokens) was never seen.
        // A usage array now counts as above, and only a name ending in "_details" may hold an object.
        string inner = Usage.Substring("\"usage\":".Length);
        string value = usage.Replace("USAGE", inner, StringComparison.Ordinal)
            .Replace("CACHEWRITE", inner.Replace("{\"cached_tokens\":0}", "{\"cached_tokens\":0,\"cache_write_tokens\":5}", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("UNKNOWN", inner.Replace("{\"reasoning_tokens\":", "{\"other_tokens\":0,\"reasoning_tokens\":", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("CACHED", inner.Replace("{\"cached_tokens\":0}", "{\"cached_tokens\":5}", StringComparison.Ordinal), StringComparison.Ordinal);
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(usage: "\"usage\":" + value)), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.IsFalse(response.UsageReliable);
        Assert.IsTrue(response.UsageAboveApprovedBounds);
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.IsTrue(settlement.NeedsReview);
        Assert.AreEqual(45_056, settlement.Committed.Value);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("{\"type\":\"response.completed\",\"response\":{\"output\":\"LONG\",\"usage\":null}}", true, DisplayName = "an event wrapper whose output is a long string and usage null")]
    [DataRow("{\"type\":\"response.completed\",\"response\":{\"output\":\"OK\"}}", true, DisplayName = "an event wrapper whose output is a short string, with no usage")]
    [DataRow("{\"type\":\"response.completed\",\"response\":{\"usage\":5}}", true, DisplayName = "an event wrapper whose usage is the number 5")]
    [DataRow("{\"output\":null,\"usage\":null}", false, DisplayName = "an object with only output null and usage null")]
    [DataRow("{\"error\":{\"message\":\"Rate limit reached.\",\"type\":\"requests\",\"code\":\"rate_limit_exceeded\"},\"usage\":null}", false, DisplayName = "an error envelope with usage null beside it")]
    public void APlainValueWhereAReplyBelongsOutOfPlacePausesButNullDoesNot(string body, bool pauses)
    {
        // Independent review of dcef10c, AE1: output or usage written as a plain value out of place is still a reply that couldn't be
        // read in its place, so it pauses. null is nothing, as a missing name is.
        body = body.Replace("LONG", new string('x', 1_000_000), StringComparison.Ordinal);
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 2_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.AreEqual(pauses, response.UsageAboveApprovedBounds);
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.AreEqual(pauses, settlement.NeedsReview);
        Assert.AreEqual(45_056, settlement.Committed.Value);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("{\"error\":{\"message\":\"The server had an error.\",\"type\":\"server_error\",\"code\":null}}", DisplayName = "an error envelope")]
    [DataRow("{}", DisplayName = "an empty object")]
    [DataRow("[]", DisplayName = "an empty array")]
    [DataRow("\"a string\"", DisplayName = "a JSON string")]
    [DataRow("5", DisplayName = "a JSON number")]
    [DataRow("null", DisplayName = "null")]
    [DataRow("true", DisplayName = "true")]
    [DataRow("{\"object\":\"list\",\"data\":[]}", DisplayName = "an empty list")]
    [DataRow("{\"id\":\"resp_abc123\",\"object\":\"response\",\"status\":\"failed\",\"model\":\"MODEL\",\"output\":[],\"error\":{\"code\":\"server_error\",\"message\":\"x\"}}", DisplayName = "a failed response with an error and empty output, in place")]
    public void ABodyThatCarriesNoReplyKeepsTheReservationWithoutAPause(string body)
    {
        // Independent review of a99b79a, AC1: only a body carrying usage or output counts as a wrapped reply; others are kept as before.
        // Independent review of dcef10c, AE1: so is a bare value, an empty list and a failed response read in its place.
        body = body.Replace("MODEL", Model, StringComparison.Ordinal);
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.IsFalse(response.UsageAboveApprovedBounds);
        AdvisoryAiSettlement kept = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.IsFalse(kept.NeedsReview);
        Assert.AreEqual(45_056, kept.Committed.Value);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void AnAnswerReportingFewerInputTokensThanItsTextCanBeIsNotSettled()
    {
        // Codex review of bf637b5, 71C-02: one input token for hundreds of bytes of approved text settled at almost nothing.
        string one = "\"usage\":{\"input_tokens\":1,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":1,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":2}";
        AdvisoryAiResponse answer = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(usage: one)), Model, 1_000_000, 8192, 1024, "default", noPromptCache: true, minimumInputTokens: 6);
        Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, answer.Status);
        Assert.IsFalse(answer.UsageReliable);
        Assert.IsFalse(answer.UsageAboveApprovedBounds);
        AdvisoryAiSettlement kept = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, answer, new(45_056), ReviewTariff);
        Assert.AreEqual(AdvisoryAiRequestState.Uncertain, kept.State);
        Assert.AreEqual(45_056, kept.Committed.Value);

        string six = one.Replace("\"input_tokens\":1,", "\"input_tokens\":6,", StringComparison.Ordinal).Replace("\"total_tokens\":2", "\"total_tokens\":7", StringComparison.Ordinal);
        Assert.IsTrue(AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(usage: six)), Model, 1_000_000, 8192, 1024, "default", noPromptCache: true, minimumInputTokens: 6).UsageReliable,
            "exactly at the floor");
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void AnAnswerReportingNoInputCanNotSettleForNothing()
    {
        // Independent review F2: an answer always has input, so zero usage keeps the reservation (without a pause).
        string zero = "\"usage\":{\"input_tokens\":0,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":0,\"output_tokens_details\":{\"reasoning_tokens\":0},\"total_tokens\":0}";
        AdvisoryAiResponse answer = ReadAsRequested(Body(usage: zero));
        Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, answer.Status);
        Assert.IsFalse(answer.UsageReliable);
        Assert.IsFalse(answer.UsageAboveApprovedBounds);
        AdvisoryAiSettlement kept = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, answer, new(45_056), ReviewTariff);
        Assert.AreEqual(AdvisoryAiRequestState.Uncertain, kept.State);
        Assert.AreEqual(45_056, kept.Committed.Value);

        AdvisoryAiResponse failed = ReadAsRequested(Body("failed", "[]", usage: zero, extra: ",\"error\":{\"code\":\"server_error\",\"message\":\"x\"}"));
        Assert.IsTrue(failed.UsageReliable, "a provider failure can honestly have used nothing");
    }

    private const string AnswerItem = "{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"OK\",\"annotations\":[]}]}";

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("websearch", DisplayName = "one web search call beside the answer")]
    [DataRow("sixsearches", DisplayName = "six web search calls beside the answer")]
    [DataRow("image", DisplayName = "an image generation call")]
    [DataRow("code", DisplayName = "a code interpreter call")]
    [DataRow("filesearch", DisplayName = "a file search call")]
    [DataRow("computer", DisplayName = "a computer call")]
    [DataRow("mcp", DisplayName = "a remote tool call")]
    [DataRow("function", DisplayName = "a function call")]
    [DataRow("twotypes", DisplayName = "an item with two types, message then a search call")]
    [DataRow("searchpart", DisplayName = "a search call inside the message parts")]
    [DataRow("audio", DisplayName = "an audio part in the message")]
    [DataRow("toolsecho", DisplayName = "a tools echo naming web search")]
    [DataRow("toolsimage", DisplayName = "a tools echo naming image generation, with the call item")]
    [DataRow("model", DisplayName = "another model")]
    [DataRow("suffix", DisplayName = "the model with a date suffix")]
    [DataRow("store", DisplayName = "store true")]
    [DataRow("modeltwice", DisplayName = "model twice, the approved one last")]
    [DataRow("storetwice", DisplayName = "store twice, false last")]
    [DataRow("toolstwice", DisplayName = "tools twice, empty last")]
    [DataRow("outputtwice", DisplayName = "output twice, a search call first and the answer last")]
    public void AReplyShowingWhatTheRequestNeverAllowedStopsPaidRequestsWhateverElseRefusesIt(string change)
    {
        // Independent review of a4db871, AG1: a reply in its place with counts within the approval that showed a hosted tool call, a
        // part that isn't text, a tools echo, another model or storage was refused, but a refusal only kept the reservation, with no
        // pause, so a second paid request was sent. Hosted tools are charged per call and another model has another price list, both
        // outside the reservation. Each now pauses, alone or behind a repeated root name, no id or an unknown status.
        string output = "[" + AnswerItem + "]";
        string extra = "";
        Func<string, string> edit = body => body;
        switch (change)
        {
            case "websearch": output = "[{\"type\":\"web_search_call\",\"id\":\"ws_1\",\"status\":\"completed\",\"action\":{\"type\":\"search\",\"query\":\"x\"}}," + AnswerItem + "]"; break;
            case "sixsearches": output = "[" + string.Concat(Enumerable.Repeat("{\"type\":\"web_search_call\",\"id\":\"ws_1\",\"status\":\"completed\"},", 6)) + AnswerItem + "]"; break;
            case "image": output = "[{\"type\":\"image_generation_call\",\"id\":\"ig_1\",\"status\":\"completed\",\"result\":\"aGk=\"}," + AnswerItem + "]"; break;
            case "code": output = "[{\"type\":\"code_interpreter_call\",\"id\":\"ci_1\",\"status\":\"completed\",\"code\":\"x\"}," + AnswerItem + "]"; break;
            case "filesearch": output = "[{\"type\":\"file_search_call\",\"id\":\"fs_1\",\"status\":\"completed\",\"queries\":[\"x\"]}," + AnswerItem + "]"; break;
            case "computer": output = "[{\"type\":\"computer_call\",\"id\":\"cc_1\",\"status\":\"completed\"}," + AnswerItem + "]"; break;
            case "mcp": output = "[{\"type\":\"mcp_call\",\"id\":\"mc_1\",\"name\":\"x\"}," + AnswerItem + "]"; break;
            case "function": output = "[{\"type\":\"function_call\",\"id\":\"fc_1\",\"name\":\"x\",\"arguments\":\"{}\"}," + AnswerItem + "]"; break;
            case "twotypes": output = "[" + AnswerItem.Replace("{\"type\":\"message\",", "{\"type\":\"message\",\"type\":\"web_search_call\",", StringComparison.Ordinal) + "]"; break;
            case "searchpart": output = "[" + AnswerItem.Replace("\"annotations\":[]}", "\"annotations\":[]},{\"type\":\"web_search_call\",\"id\":\"ws_1\"}", StringComparison.Ordinal) + "]"; break;
            case "audio": output = "[" + AnswerItem.Replace("\"annotations\":[]}", "\"annotations\":[]},{\"type\":\"output_audio\",\"data\":\"aGk=\"}", StringComparison.Ordinal) + "]"; break;
            case "toolsecho": edit = body => body.Replace("\"tools\":[]", "\"tools\":[{\"type\":\"web_search\"}]", StringComparison.Ordinal); break;
            case "toolsimage":
                output = "[{\"type\":\"image_generation_call\",\"id\":\"ig_1\",\"status\":\"completed\",\"result\":\"aGk=\"}," + AnswerItem + "]";
                edit = body => body.Replace("\"tools\":[]", "\"tools\":[{\"type\":\"image_generation\"}]", StringComparison.Ordinal);
                break;
            case "model": edit = body => body.Replace(Model, "other-model", StringComparison.Ordinal); break;
            case "suffix": edit = body => body.Replace(Model, Model + "-2026-11-14", StringComparison.Ordinal); break;
            case "store": edit = body => body.Replace("\"store\":false", "\"store\":true", StringComparison.Ordinal); break;
            case "modeltwice": edit = body => body.Replace("\"model\":\"" + Model + "\"", "\"model\":\"other-model\",\"model\":\"" + Model + "\"", StringComparison.Ordinal); break;
            case "storetwice": edit = body => body.Replace("\"store\":false", "\"store\":true,\"store\":false", StringComparison.Ordinal); break;
            case "toolstwice": edit = body => body.Replace("\"tools\":[]", "\"tools\":[{\"type\":\"web_search\"}],\"tools\":[]", StringComparison.Ordinal); break;
            default:
                edit = body => body.Replace("\"output\":", "\"output\":[{\"type\":\"web_search_call\",\"id\":\"ws_1\"}],\"output\":", StringComparison.Ordinal);
                break;
        }
        string reply = edit(Body(output: output, extra: extra));
        foreach ((string refusal, string body) in new[]
        {
            ("alone", reply),
            ("a root name twice", reply.Replace("\"created_at\":1763000000", "\"created_at\":1763000000,\"created_at\":1763000001", StringComparison.Ordinal)),
            ("no id", reply.Replace("\"resp_abc123\"", "\"\"", StringComparison.Ordinal)),
            ("an unknown status", reply.Replace("\"status\":\"completed\",\"model\"", "\"status\":\"paused\",\"model\"", StringComparison.Ordinal))
        })
        {
            Assert.AreNotEqual(reply == body, refusal != "alone", refusal);
            AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
            Assert.AreEqual(AdvisoryAiAnswerStatus.Malformed, response.Status, refusal);
            Assert.IsNull(response.Text, refusal);
            AssertPausedWithWholeReservation(response, refusal);
        }
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("\"queued\"", true, DisplayName = "queued")]
    [DataRow("\"in_progress\"", true, DisplayName = "in progress")]
    [DataRow("\"paused\"", true, DisplayName = "an unknown word")]
    [DataRow("5", true, DisplayName = "a number")]
    [DataRow("\"completed\"", false, DisplayName = "completed")]
    [DataRow("\"incomplete\"", false, DisplayName = "incomplete")]
    [DataRow("\"failed\"", false, DisplayName = "failed")]
    [DataRow("\"cancelled\"", false, DisplayName = "cancelled")]
    public void AnUnsupportedStatusStopsPaidRequestsForReview(string status, bool pauses)
    {
        // Owner's review of a4db871, 71C-01: a status the reader doesn't support was refused but only kept the reservation, so a
        // reply saying the provider was still working on the request could be followed by another paid request. It now pauses.
        string body = Body().Replace("\"status\":\"completed\"", "\"status\":" + status, StringComparison.Ordinal);
        AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.AreEqual(pauses, response.UsageAboveApprovedBounds);
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.AreEqual(pauses, settlement.NeedsReview);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void BodiesThatShowNothingTheRequestNeverAllowedStillOnlyKeepTheReservation()
    {
        // Independent review of a4db871, AG1: the new check reads only a response object's model, store, tools, status and output, so
        // an error envelope (even one naming a model and tools), no id, no status, no model and a failed response still only keep the
        // reservation, without a pause. A message with no content now pauses: a recorded cost of the check. The owner's review,
        // 71C-01: so does an unsupported status.
        foreach ((string what, string body, bool pauses) in new[]
        {
            ("an error envelope naming a model and tools", "{\"error\":{\"message\":\"x\",\"type\":\"invalid_request_error\",\"model\":\"other\",\"tools\":[{}]}}", false),
            ("a response with no id", Body().Replace("\"resp_abc123\"", "\"\"", StringComparison.Ordinal), false),
            ("a response with an unknown status", Body().Replace("\"status\":\"completed\",\"model\"", "\"status\":\"paused\",\"model\"", StringComparison.Ordinal), true),
            ("a response with no status", Body().Replace("\"status\":\"completed\",", "", StringComparison.Ordinal), false),
            ("a response with no model named", Body().Replace("\"model\":\"" + Model + "\",", "", StringComparison.Ordinal), false),
            ("a failed response with no output field", "{\"id\":\"resp_abc123\",\"object\":\"response\",\"created_at\":1763000000,\"status\":\"failed\",\"model\":\"" + Model + "\",\"store\":false,\"tools\":[],\"error\":{\"code\":\"server_error\",\"message\":\"x\"}}", false),
            ("a message with no content", Body(output: "[{\"type\":\"message\",\"role\":\"assistant\"}]"), true)
        })
        {
            AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
            Assert.AreEqual(pauses, response.UsageAboveApprovedBounds, what);
            AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
            Assert.AreEqual(pauses, settlement.NeedsReview, what);
            Assert.AreEqual(45_056, settlement.Committed.Value, what);
        }
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void RepliesMadeOnlyOfReasoningAndAnswerStillSettle()
    {
        // Independent review of a4db871, AG1: a reasoning summary, a refusal, a link citation, two messages, empty output and echoed
        // tool settings with no tools are all within what the request allowed, so they still settle at the reported usage.
        foreach ((string what, string body) in new[]
        {
            ("the plain reply", Body()),
            ("a reasoning summary, then the answer", Body(output: "[{\"type\":\"reasoning\",\"summary\":[{\"type\":\"summary_text\",\"text\":\"Why.\"}]}," + AnswerItem + "]")),
            ("a refusal", Body(output: "[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"No.\"}]}]")),
            ("a link citation", Body(output: "[" + AnswerItem.Replace("\"annotations\":[]", "\"annotations\":[{\"type\":\"url_citation\",\"url\":\"https://example.com/\",\"start_index\":0,\"end_index\":2}]", StringComparison.Ordinal) + "]")),
            ("two messages", Body(output: "[" + AnswerItem + "," + AnswerItem + "]")),
            ("empty output", Body(output: "[]")),
            ("tool settings echoed with no tools", Body(extra: ",\"tool_choice\":\"auto\",\"parallel_tool_calls\":true"))
        })
        {
            AdvisoryAiResponse response = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(body), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
            Assert.IsFalse(response.UsageAboveApprovedBounds, what);
            Assert.IsTrue(response.UsageReliable, what);
        }
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void AModelNameWithASuffixKeepsTheReservationAndStopsPaidRequestsForReview()
    {
        // Task 70F T70F-01: a model mismatch alone is uncertain (whole reservation kept, nothing shown). It used not to pause paid
        // requests by itself. Independent review of a4db871, AG1: another model has another price list, charged outside the
        // reservation, so any model name but the approved one now pauses too, a date suffix included. A departure still pauses.
        string suffixed = Body().Replace(Model, Model + "-2026-11-14", StringComparison.Ordinal);
        AdvisoryAiResponse response = ReadAsRequested(suffixed);
        Assert.AreEqual(AdvisoryAiMalformedReason.ModelMismatch, response.MalformedReason);
        Assert.AreEqual(AdvisoryAiResponseDeparture.None, response.Departures);
        Assert.IsNull(response.Text);
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.AreEqual(AdvisoryAiRequestState.Uncertain, settlement.State);
        Assert.AreEqual(45_056, settlement.Committed.Value);
        Assert.IsTrue(settlement.NeedsReview);

        AdvisoryAiResponse withDeparture = ReadAsRequested(suffixed.Replace("\"store\":false", "\"store\":false,\"background\":null", StringComparison.Ordinal));
        AssertPausedWithWholeReservation(withDeparture, "background null beside the mismatch");
    }
}
