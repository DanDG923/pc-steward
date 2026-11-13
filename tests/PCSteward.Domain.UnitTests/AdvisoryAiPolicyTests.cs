using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// FR-AI-04, FR-BUD-01 (ADR-028 "AI limits" and F1, ADR-029 Q7; Task 67 stage 1): checked micro-dollar parsing, the four editable
/// limits, and the coupled token, request, response and saved-item byte bounds. Synthetic profiles only: no real model is claimed.
/// </summary>
[TestClass]
public sealed class AdvisoryAiPolicyTests
{
    internal static readonly AdvisoryAiModelProfile Profile = new("synthetic-model-2026-01-01", 1, 128_000, 16_384, 64, true, 64, 65_536, "default");

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    [DataRow("0.25", 250_000L)]
    [DataRow("200", 200_000_000L)]
    [DataRow("0.000001", 1L)]
    [DataRow("1.5", 1_500_000L)]
    [DataRow("0", 0L)]
    [DataRow("9223372036854", 9_223_372_036_854_000_000L)]
    public void DollarAmountsParseExactly(string text, long micro)
    {
        Assert.IsTrue(MicroUsd.TryParseDollars(text, out MicroUsd amount));
        Assert.AreEqual(micro, amount.Value);
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    [DataRow("")]
    [DataRow(".5")]
    [DataRow("5.")]
    [DataRow("-1")]
    [DataRow("+1")]
    [DataRow("1e3")]
    [DataRow("0.0000001")]
    [DataRow("1,000")]
    [DataRow(" 1")]
    [DataRow("$1")]
    [DataRow("1.2.3")]
    [DataRow("9223372036855")]
    [DataRow("NaN")]
    [DataRow("١")]
    public void UnsupportedPrecisionSignsAndOverflowAreRefusedNotRounded(string text)
    {
        Assert.IsFalse(MicroUsd.TryParseDollars(text, out MicroUsd amount));
        Assert.AreEqual(0, amount.Value);
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void AmountsFormatAsDollarsWithoutLosingPrecision()
    {
        Assert.AreEqual("$0.25", new MicroUsd(250_000).ToDollarString());
        Assert.AreEqual("$200.00", new MicroUsd(200_000_000).ToDollarString());
        Assert.AreEqual("$0.000125", new MicroUsd(125).ToDollarString());
        Assert.AreEqual("$1.10", new MicroUsd(1_100_000).ToDollarString());
        Assert.ThrowsExactly<OverflowException>(() => MicroUsd.Add(new MicroUsd(long.MaxValue), new MicroUsd(1)));
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    [TestCategory("FR-BUD-01")]
    public void TheAcceptedDefaultsAreUsableWithAReviewedProfile()
    {
        AdvisoryAiLimits defaults = AdvisoryAiLimits.Defaults;
        Assert.AreEqual(8192, defaults.MaximumInputTokens);
        Assert.AreEqual(1024, defaults.MaximumOutputTokens);
        Assert.AreEqual("$0.25", defaults.RequestCap.ToDollarString());
        Assert.AreEqual("$200.00", defaults.MonthlyBudget.ToDollarString());
        Assert.IsFalse(defaults.PaidRequestsDisabled);
        Assert.AreEqual(AdvisoryAiLimitProblem.None, AdvisoryAiLimitValidation.Validate(defaults, Profile, 900));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void ZeroInEitherSpendingLimitDisablesPaidRequestsAndKeepsTheOther()
    {
        AdvisoryAiLimits noMonth = AdvisoryAiLimits.Defaults with { MonthlyBudget = MicroUsd.Zero };
        AdvisoryAiLimits noRequest = AdvisoryAiLimits.Defaults with { RequestCap = MicroUsd.Zero, MonthlyBudget = new(1) };
        Assert.IsTrue(noMonth.PaidRequestsDisabled);
        Assert.IsTrue(noRequest.PaidRequestsDisabled);
        Assert.AreEqual(250_000, noMonth.RequestCap.Value, "the other value is kept");
        Assert.AreEqual(AdvisoryAiLimitProblem.None, AdvisoryAiLimitValidation.Validate(noMonth));
        Assert.AreEqual(AdvisoryAiLimitProblem.None, AdvisoryAiLimitValidation.Validate(noRequest));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void PerRequestCapCannotExceedTheMonthlyBudgetWhenBothArePositive()
    {
        Assert.AreEqual(AdvisoryAiLimitProblem.None,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { RequestCap = new(5_000_000), MonthlyBudget = new(5_000_000) }));
        Assert.AreEqual(AdvisoryAiLimitProblem.RequestCapAboveMonthlyBudget,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { RequestCap = new(5_000_001), MonthlyBudget = new(5_000_000) }));
        Assert.AreEqual(AdvisoryAiLimitProblem.None,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { RequestCap = new(1_000_000_000_000), MonthlyBudget = new(1_000_000_000_000) }),
            "no extra product-level dollar maximum");
        Assert.AreEqual(AdvisoryAiLimitProblem.NegativeAmount, AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { RequestCap = new(-1) }));
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public void TokenLimitsMustBePositiveAndFitTheModel()
    {
        Assert.AreEqual(AdvisoryAiLimitProblem.InputTokensNotPositive, AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { MaximumInputTokens = 0 }));
        Assert.AreEqual(AdvisoryAiLimitProblem.OutputTokensNotPositive, AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { MaximumOutputTokens = -1 }));
        Assert.AreEqual(AdvisoryAiLimitProblem.InputAboveModelContext,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { MaximumInputTokens = 128_001 }, Profile));
        Assert.AreEqual(AdvisoryAiLimitProblem.OutputAboveModelMaximum,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { MaximumOutputTokens = 16_385 }, Profile));
        Assert.AreEqual(AdvisoryAiLimitProblem.InputPlusOutputAboveModelContext,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { MaximumInputTokens = 120_000, MaximumOutputTokens = 8_001 }, Profile));
        Assert.AreEqual(AdvisoryAiLimitProblem.InputTooSmallForInstructions,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { MaximumInputTokens = 964 }, Profile, 900));
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public void RaisingOutputRaisesTheCoupledByteAllowancesUntilAFixedMaximumBinds()
    {
        // Saved item: 512 + 10 * output * 64 must be at most 4 MiB, so 6,552 output tokens fit and 6,553 do not.
        Assert.AreEqual(4_193_792L, Profile.SavedItemBytes(6552));
        Assert.AreEqual(4_194_432L, Profile.SavedItemBytes(6553));
        Assert.AreEqual(AdvisoryAiLimitProblem.None,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { MaximumOutputTokens = 6552 }, Profile));
        Assert.AreEqual(AdvisoryAiLimitProblem.SavedItemBytesAboveMaximum,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { MaximumOutputTokens = 6553 }, Profile));
        Assert.IsTrue(Profile.ResponseBytes(8192, 6553) <= AdvisoryAiResourcePolicy.MaximumResponseBytes,
            "the response allowance was not the binding limit, so no cosmetic increase hides behind a smaller cap");

        AdvisoryAiModelProfile longTokens = Profile with { MaximumTokenUtf8Bytes = 1024, ProfileVersion = 2 };
        Assert.AreEqual(AdvisoryAiLimitProblem.ResponseBytesAboveMaximum,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults, longTokens));
        Assert.AreEqual(AdvisoryAiLimitProblem.RequestBytesAboveMaximum,
            AdvisoryAiLimitValidation.Validate(AdvisoryAiLimits.Defaults with { MaximumInputTokens = 700_000, MaximumOutputTokens = 16 },
                Profile with { ContextWindowTokens = 1_000_000 }));
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public void ByteBoundsUseCheckedArithmetic() =>
        Assert.ThrowsExactly<OverflowException>(() => (Profile with { MaximumTokenUtf8Bytes = int.MaxValue }).ResponseBytes(int.MaxValue, int.MaxValue));

    [TestMethod]
    [TestCategory("FR-AI-01")]
    public void NoModelIsReviewedYetAndPaidRequestsAreDisabledInThisBuild()
    {
        Assert.AreEqual(0, AdvisoryAiModelProfiles.Reviewed.Count, "a model is added only with Task 69 evidence");
        Assert.IsNull(AdvisoryAiModelProfiles.Find(Profile.ModelId));
        Assert.AreEqual(AdvisoryAiPaidRequestMode.Disabled, AdvisoryAiBuildPolicy.PaidRequests);
    }

    [TestMethod]
    [TestCategory("FR-AI-01")]
    public void AProfileWithoutAProvenTokenBoundIsNotCoherent()
    {
        Assert.IsTrue(Profile.IsCoherent);
        Assert.IsFalse((Profile with { TokenizerIsByteLevel = false }).IsCoherent);
        Assert.IsFalse((Profile with { ModelId = "model with spaces" }).IsCoherent);
        Assert.IsFalse((Profile with { ServiceTier = "Default" }).IsCoherent);
        Assert.IsFalse((Profile with { MaximumOutputTokens = 128_000 }).IsCoherent);
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public void EveryProblemHasAPlainExplanation()
    {
        foreach (AdvisoryAiLimitProblem problem in Enum.GetValues<AdvisoryAiLimitProblem>())
            Assert.IsFalse(string.IsNullOrWhiteSpace(AdvisoryAiLimitValidation.Explain(problem)), problem.ToString());
    }
}
