using System.Text;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// Task 90 for Task 70 T70-02 (ADR-028 F1/F7, ADR-029): the arithmetic behind the Task 71 test packet in
/// docs/review/TASK_69_AI_EVIDENCE_GPT_6_1_SOL.md §6 and §10, with the owner's wide safety margins (2026-11-14). The profile here is a
/// copy for checking only. It is not reviewed, is never offered, and nothing here enables a model or a paid request.
/// </summary>
[TestClass]
public sealed class AdvisoryAiTask71CandidateTests
{
    /// <summary>The Task 69 §6 candidate with the owner's wide margins: 8,192 framing tokens, 192 bytes per output token, 1 MiB envelope.</summary>
    private static readonly AdvisoryAiModelProfile Candidate = new("gpt-6.1-sol", 1, 1_050_000, 128_000, 8_192, true, 192, 1_048_576, "default")
    {
        ReasoningEffort = "low",
        StandardPriceInputTokens = 272_000,
        RequestNoPromptCache = true
    };

    /// <summary>The Task 71 limits: 12,288 input tokens, 2,048 output tokens, $0.05 per request, $1.00 per month.</summary>
    private static readonly AdvisoryAiLimits TestLimits = new(12_288, 2_048, new(50_000), new(1_000_000));

    private static readonly DateTimeOffset Confirmed = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Task 69 §3 Standard prices, per 1M tokens: input $2.00, cached $0.10, output $10.00.</summary>
    private static readonly AdvisoryAiTariff Listed = new(1, "gpt-6.1-sol", "default", new(2_000_000), new(100_000), new(10_000_000), Confirmed);

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public void TheCandidateStaysUnreviewedAndPaidRequestsStayOff()
    {
        Assert.AreEqual(0, AdvisoryAiModelProfiles.Reviewed.Count, "Task 90 adds no reviewed model");
        Assert.AreEqual(AdvisoryAiPaidRequestMode.Disabled, AdvisoryAiBuildPolicy.PaidRequests);
        Assert.IsTrue(Candidate.IsCoherent);
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void TheTestLimitsFitTheWideMarginsAndEveryByteCeiling()
    {
        int instructionBytes = Encoding.UTF8.GetByteCount(AdvisoryAiInstructions.Text);
        Assert.AreEqual(AdvisoryAiLimitProblem.None, AdvisoryAiLimitValidation.Validate(TestLimits, Candidate, instructionBytes));
        Assert.IsTrue(Candidate.ResponseBytes(TestLimits.MaximumInputTokens, TestLimits.MaximumOutputTokens) <= AdvisoryAiResourcePolicy.MaximumResponseBytes);
        Assert.IsTrue(Candidate.SavedItemBytes(TestLimits.MaximumOutputTokens) <= AdvisoryAiResourcePolicy.MaximumSavedItemBytes);

        // Room left for the synthetic evidence after the instructions and 8,192 framing tokens: a few KB, plenty for one made-up record.
        int evidenceBytes = AdvisoryAiInputAllowance.For(TestLimits, Candidate, AdvisoryAiInstructions.Text).InputTextBytes;
        Assert.IsTrue(evidenceBytes >= 2_048, $"only {evidenceBytes} bytes left for evidence");
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public void TheWorstCaseReservationStaysUnderFiveCentsEvenWithATenPercentUplift()
    {
        MicroUsd listed = AdvisoryAiCost.Reservation(TestLimits.MaximumInputTokens, TestLimits.MaximumOutputTokens, Listed);
        Assert.AreEqual(45_056, listed.Value, "12,288 x $2.00/M + 2,048 x $10.00/M");
        Assert.IsTrue(listed <= TestLimits.RequestCap);

        // T70-03: a project whose own prices are 10% higher (regional processing) still fits the $0.05 cap.
        AdvisoryAiTariff uplifted = Listed with { InputPerMillion = new(2_200_000), CachedInputPerMillion = new(110_000), OutputPerMillion = new(11_000_000) };
        MicroUsd high = AdvisoryAiCost.Reservation(TestLimits.MaximumInputTokens, TestLimits.MaximumOutputTokens, uplifted);
        Assert.AreEqual(49_562, high.Value);
        Assert.IsTrue(high <= TestLimits.RequestCap);
    }
}
