using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-AI-01/02/03 (ADR-028 Q8 and R4; Task 67 stage 1): the API key holder refuses what ADR-028 refuses and never reveals itself in
/// text, and a failed update's facts come only from that job's own recorded history.
/// </summary>
[TestClass]
public sealed class AdvisoryAiApplicationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [TestCategory("FR-AI-03")]
    [DataRow("", AdvisoryAiKeyProblem.Empty)]
    [DataRow("sk-abc def", AdvisoryAiKeyProblem.ContainsWhitespace)]
    [DataRow("sk-abc\tdef", AdvisoryAiKeyProblem.ContainsWhitespace)]
    [DataRow("sk-abc def", AdvisoryAiKeyProblem.ContainsWhitespace)]
    [DataRow("sk-abcédef", AdvisoryAiKeyProblem.NotPrintableAscii)]
    [DataRow("sk-abc\u0000def", AdvisoryAiKeyProblem.NotPrintableAscii)]
    [DataRow("sk-abc​def", AdvisoryAiKeyProblem.NotPrintableAscii)]
    public void KeysThatCannotTravelInAHeaderAreRefused(string text, AdvisoryAiKeyProblem problem)
    {
        Assert.AreEqual(problem, AdvisoryAiApiKey.TryCreate(text, out AdvisoryAiApiKey? key));
        Assert.IsNull(key);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public void TheSizeLimitIs2048BytesAndALargerKeyIsRefusedNotCut()
    {
        Assert.AreEqual(AdvisoryAiKeyProblem.None, AdvisoryAiApiKey.TryCreate(new string('k', 2048), out AdvisoryAiApiKey? largest));
        using (largest) Assert.AreEqual(2048, largest!.Length);
        Assert.AreEqual(AdvisoryAiKeyProblem.TooLong, AdvisoryAiApiKey.TryCreate(new string('k', 2049), out AdvisoryAiApiKey? none));
        Assert.IsNull(none);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public void AKeyNeverAppearsInTextAndIsClearedWhenDisposed()
    {
        const string secret = "sk-synthetic-ABCDEFGHIJKLMNOP";
        Assert.AreEqual(AdvisoryAiKeyProblem.None, AdvisoryAiApiKey.TryCreate(secret, out AdvisoryAiApiKey? key));
        Assert.AreEqual("[API key hidden]", key!.ToString());
        Assert.AreEqual("[API key hidden]", $"{key}");
        Assert.AreEqual(secret, key.Reveal().ToString());
        key.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => key.Reveal().ToString());
        Assert.ThrowsExactly<ObjectDisposedException>(() => key.Length);
        key.Dispose();
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public void TheCredentialTargetIsTheOneExactAcceptedName()
    {
        Assert.AreEqual("PCSteward/OpenAI/ApiKey.v1", AdvisoryAiProviders.OpenAI.CredentialTarget);
        // ADR-028 addendum A1: one key per provider, each under its own exact target.
        foreach (AdvisoryAiProvider provider in AdvisoryAiProviders.All)
            Assert.AreEqual("PCSteward/" + provider.Id + "/ApiKey.v1", provider.CredentialTarget);
        Assert.HasCount(AdvisoryAiProviders.All.Count, AdvisoryAiProviders.All.Select(provider => provider.CredentialTarget).Distinct());
    }

    [TestMethod]
    [TestCategory("FR-AI-01")]
    public void OnlyFailedTimedOutOrInconclusiveUpdatesCanBeExplained()
    {
        UpdateHistoryStatus[] explainable = [.. Enum.GetValues<UpdateHistoryStatus>().Where(AdvisoryAiFailedUpdateEvidence.IsExplainable)];
        CollectionAssert.AreEquivalent(new[]
        {
            UpdateHistoryStatus.SourceFailed, UpdateHistoryStatus.DownloadFailed, UpdateHistoryStatus.InstallerFailed, UpdateHistoryStatus.TimedOut,
            UpdateHistoryStatus.VerificationFailed, UpdateHistoryStatus.ProductFailed, UpdateHistoryStatus.Inconclusive
        }, explainable);
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    public void FactsComeOnlyFromTheSelectedJobsOwnEvents()
    {
        Guid job = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        UpdateHistoryEntry[] history =
        [
            Entry(job, Start, UpdateHistoryStatus.Running, null, string.Empty),
            Entry(job, Start.AddMinutes(3), UpdateHistoryStatus.InstallerFailed, 1603, "Installer failed with 1603."),
            Entry(other, Start.AddMinutes(1), UpdateHistoryStatus.InstallerFailed, 5, "Another job's diagnostic.")
        ];
        UpdateJobRecord record = UpdateJobHistory.Group(history).Single(item => item.JobId == job);

        AdvisoryAiFailedUpdateFacts facts = AdvisoryAiFailedUpdateEvidence.Facts(record, history, "Contoso Editor");
        Assert.AreEqual("Contoso Editor", facts.AppName);
        Assert.AreEqual("WinGet", facts.Provider);
        Assert.AreEqual("1.2.0", facts.InstalledVersion);
        Assert.AreEqual("1.3.0", facts.TargetVersion);
        Assert.AreEqual("InstallerFailed", facts.Outcome);
        Assert.AreEqual(1603, facts.ExitCode);
        Assert.AreEqual(2, facts.Events.Count);
        Assert.IsNull(facts.Events[0].Diagnostic, "an empty diagnostic is left out");
        Assert.AreEqual("Installer failed with 1603.", facts.Events[1].Diagnostic);
        Assert.IsFalse(facts.Events.Any(item => item.Diagnostic?.Contains("Another job", StringComparison.Ordinal) == true));
        Assert.AreEqual("Contoso.Editor", AdvisoryAiFailedUpdateEvidence.Facts(record, history, null).AppName, "falls back to the package ID");
    }

    private static UpdateHistoryEntry Entry(Guid job, DateTimeOffset at, UpdateHistoryStatus status, int? exitCode, string diagnostic) =>
        new(Guid.NewGuid(), job, Guid.NewGuid(), "winget:Contoso.Editor", UpdateProvider.WinGet, "Contoso.Editor", "1.2.0", "1.3.0", at, status, exitCode, diagnostic);
}
