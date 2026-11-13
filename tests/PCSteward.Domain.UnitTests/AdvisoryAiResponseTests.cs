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
}
