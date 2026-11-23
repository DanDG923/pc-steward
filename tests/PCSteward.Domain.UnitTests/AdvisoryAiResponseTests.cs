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
    [DataRow("[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"TEXT\"}]},{\"type\":\"function_call\",\"name\":\"x\"}]", DisplayName = "an answer in a body refused for another reason")]
    public void AnyOutputTextLongerThanTheReportedOutputCouldDecodeToIsNotTrusted(string output)
    {
        // Independent review of 4056e3f, G2: the C1 check counted answer text only, so a huge refusal or reasoning summary, or an
        // answer in a body that was refused before the check, settled at the reported usage with nothing stopped for review.
        string large = output.Replace("TEXT", new string('x', (300 * 4) + 1), StringComparison.Ordinal);
        AdvisoryAiResponse over = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(output: large)), Model, 1_000_000, 8192, 1024, maximumTokenUtf8Bytes: 4);
        Assert.IsFalse(over.UsageReliable);
        Assert.IsTrue(over.UsageAboveApprovedBounds);
        // The same reply with text the reported tokens can decode to is trusted, so the check charges only the text, not the fixed names.
        string small = output.Replace("TEXT", "Short reason.", StringComparison.Ordinal);
        AdvisoryAiResponse within = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(Body(output: small)), Model, 1_000_000, 8192, 1024, maximumTokenUtf8Bytes: 4);
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
    [DataRow(",\"store\":true", "\"output_tokens\":1,", "901", DisplayName = "a body refused before the output is read")]
    [DataRow("", "\"output_tokens\":1,", "5", DisplayName = "a total that doesn't add up, with a plain output count")]
    public void OutputTextThatContradictsAPlainOutputCountAlwaysPauses(string extra, string outputCount, string total)
    {
        // Codex review of cd04620, 71C-03: a body refused early (here for echoing store) returned before the check, and an inconsistent
        // total made the check use the whole approved output instead of the reported count. Both kept the reservation with no pause.
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
        Assert.IsFalse(response.UsageAboveApprovedBounds);
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
            Assert.AreEqual(review, response.UsageAboveApprovedBounds, written);
            AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), tariff);
            Assert.AreEqual(AdvisoryAiRequestState.Uncertain, settlement.State);
            Assert.AreEqual(45_056, settlement.Committed.Value, "the whole reservation is kept");
            Assert.AreEqual(review, settlement.NeedsReview, written);
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

        // Within the bounds, a refused body still only keeps the reservation, without a pause.
        string within = body.Replace("6000", "300", StringComparison.Ordinal).Replace("6491", "791", StringComparison.Ordinal);
        AdvisoryAiResponse refused = AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(within), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true);
        Assert.IsFalse(refused.UsageAboveApprovedBounds);
        AdvisoryAiSettlement kept = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, refused, new(45_056), ReviewTariff);
        Assert.AreEqual(45_056, kept.Committed.Value);
        Assert.IsFalse(kept.NeedsReview);
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

        string within = above.Replace("99999", "300", StringComparison.Ordinal).Replace("100490", "791", StringComparison.Ordinal);
        string calm = string.Format(System.Globalization.CultureInfo.InvariantCulture, wrapper, Body(usage: within));
        Assert.IsFalse(AdvisoryAiResponseReader.Read(Encoding.UTF8.GetBytes(calm), Model, 1_000_000, 12_288, 2_048, "default", noPromptCache: true).UsageAboveApprovedBounds,
            "a wrapped reply within the bounds keeps the reservation without a pause, as before");
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

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void AModelNameWithASuffixKeepsTheReservationWithoutAnAutomaticPause()
    {
        // Task 70F T70F-01: a model mismatch alone is uncertain (whole reservation kept, nothing shown) but isn't a departure from the
        // priced request, so it doesn't pause paid requests by itself; the Task 71 packet's manual stop covers it. A departure in the
        // same body still pauses.
        string suffixed = Body().Replace(Model, Model + "-2026-11-14", StringComparison.Ordinal);
        AdvisoryAiResponse response = ReadAsRequested(suffixed);
        Assert.AreEqual(AdvisoryAiMalformedReason.ModelMismatch, response.MalformedReason);
        Assert.AreEqual(AdvisoryAiResponseDeparture.None, response.Departures);
        Assert.IsNull(response.Text);
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, response, new(45_056), ReviewTariff);
        Assert.AreEqual(AdvisoryAiRequestState.Uncertain, settlement.State);
        Assert.AreEqual(45_056, settlement.Committed.Value);
        Assert.IsFalse(settlement.NeedsReview);

        AdvisoryAiResponse withDeparture = ReadAsRequested(suffixed.Replace("\"store\":false", "\"store\":false,\"background\":null", StringComparison.Ordinal));
        AssertPausedWithWholeReservation(withDeparture, "background null beside the mismatch");
    }
}
