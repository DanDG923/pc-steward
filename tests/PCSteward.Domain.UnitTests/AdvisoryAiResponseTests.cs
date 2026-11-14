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
}
