using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-AI-02/04/05/06 (ADR-028 consent items 4–5; Task 67 stage 4c): "Explain with AI…" is offered only when the explain service was
/// composed (a reviewed model), and only for a Diagnostics record that can be explained or a failed update. A request is sent only
/// from the review dialog's "Approve and send", which is never the default, inside the exclusive data-operation lease, once. The
/// key, the transport and the credential store are never reached from the window.
/// </summary>
[TestClass]
public sealed class AdvisoryAiDesktopExplainBoundaryTests
{
    [TestMethod]
    public void TheActionIsOfferedOnlyWhenComposedAndOnlyForExplainableRecords()
    {
        string explain = DesktopFile("MainWindow.AdvisoryAiExplain.cs");
        StringAssert.Contains(explain, "if (advisoryAiExplain is not AdvisoryAiExplainService explain || !entry.CanExplain) return null;");
        StringAssert.Contains(explain, "if (advisoryAiExplain is not AdvisoryAiExplainService explain || entry.Job is not UpdateJobRecord job || !AdvisoryAiFailedUpdateEvidence.IsExplainable(job.Final.Status)) return null;");
        StringAssert.Contains(DesktopFile("MainWindow.Diagnostics.cs"), "DiagnosticEntryRow(entry, ExplainIncidentButton(snapshot, entry))");
        StringAssert.Contains(DesktopFile("MainWindow.Activity.cs"), "ActivityEntryRow(entry, ExplainUpdateButton(entry))");
        StringAssert.Contains(DesktopFile("MainWindow.cs"), "this.advisoryAiExplain = advisoryAiExplain;");
    }

    [TestMethod]
    public void OneApprovedSendInsideTheLeaseWithCancelAsTheDefault()
    {
        string explain = DesktopFile("MainWindow.AdvisoryAiExplain.cs");
        Assert.AreEqual(1, Regex.Count(explain, @"\.SendAsync\("), "one send");
        StringAssert.Contains(explain, "DataOperationLease.RunAsync(token => explain.SendAsync(review, token), cancellation.Token)");
        // The send happens only after the review dialog returned Approve, which only its primary button gives.
        string flow = Between(explain, "ReviewChoice approval = await ShowExplainReviewAsync(subject, review);", "AnnounceCompletion(");
        Assert.IsTrue(flow.IndexOf("if (approval != ReviewChoice.Approve || windowClosed) return;", StringComparison.Ordinal) < flow.IndexOf("SendExplanationAsync(", StringComparison.Ordinal));
        StringAssert.Contains(explain, "PrimaryButtonText = AdvisoryAiExplainText.ApproveLabel,");
        StringAssert.Contains(explain, "ContentDialogResult.Primary => ReviewChoice.Approve,");
        int confirmations = Regex.Count(explain, @"PrimaryButtonText = ");
        Assert.AreEqual(2, confirmations, "the draft and the review");
        Assert.AreEqual(4, Regex.Count(explain, @"SecondaryButtonText = "), "earlier answers from the draft or when no draft is possible, back from the review, back from a block");
        Assert.AreEqual(4, Regex.Count(explain, @"DefaultButton = ContentDialogButton\.Close"), "Cancel is the default wherever there is a choice");
        Assert.IsFalse(explain.Contains("DefaultButton = ContentDialogButton.Primary", StringComparison.Ordinal));
        Assert.IsFalse(explain.Contains("DefaultButton = ContentDialogButton.Secondary", StringComparison.Ordinal));
        // Nothing retries: there is no loop around the send.
        string send = Between(explain, "private async Task<AdvisoryAiSendOutcome?> SendExplanationAsync(", "private async Task ShowExplainOutcomeAsync(");
        foreach (string forbidden in new[] { "while", "for (", "goto", "Retry" })
            Assert.IsFalse(send.Contains(forbidden, StringComparison.Ordinal), $"the send uses {forbidden}");
    }

    [TestMethod]
    public void TheWindowNeverReachesTheKeyOrTheTransport()
    {
        foreach (string name in new[] { "MainWindow.AdvisoryAiExplain.cs", "MainWindow.Diagnostics.cs", "MainWindow.Activity.cs" })
        {
            string source = DesktopFile(name);
            foreach (string forbidden in new[] { "AdvisoryAiRequestWorkflow", "IAdvisoryAiTransport", "OpenAiResponsesTransport", "WindowsCredentialManagerStore", "IAdvisoryAiCredentialStore", "AdvisoryAiApiKey", "HttpClient", "Clipboard", "Debug.", "Trace.", "Console." })
                Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), $"{name} uses {forbidden}");
        }
        string explain = DesktopFile("MainWindow.AdvisoryAiExplain.cs");
        // Every read goes through the read lease, and the only write besides the send is deleting a saved answer.
        Assert.AreEqual(2, Regex.Count(explain, @"DataOperationLease\.RunAsync\("), "the send and deleting a saved answer");
        StringAssert.Contains(explain, "DataOperationLease.RunAsync(token => explain.DeleteSavedAsync(id, token), CancellationToken.None)");
        Assert.AreEqual(3, Regex.Count(explain, @"DataOperationLease\.RunReadAsync\("), "the draft, the review and the saved answers");
        // Edits are applied by the service, which redacts them again.
        StringAssert.Contains(explain, "explain.ApplyEdits(draft, texts, out AdvisoryAiEvidenceDraft changed)");
    }

    [TestMethod]
    public void TheDraftAndTheExactRequestBothWarnThatSecretsCanRemain()
    {
        // Task 70 T70-06: redaction is best effort; the warning sits in the draft (rebuilt after every edit) and in the final review.
        string explain = DesktopFile("MainWindow.AdvisoryAiExplain.cs");
        string draft = Between(explain, "private async Task<(DraftChoice Choice, AdvisoryAiEvidenceDraft? Edited)> ShowExplainDraftAsync(", "private async Task<ReviewChoice> ShowExplainReviewAsync(");
        string review = Between(explain, "private async Task<ReviewChoice> ShowExplainReviewAsync(", "private async Task<AdvisoryAiSendOutcome?> SendExplanationAsync(");
        foreach (string dialog in new[] { draft, review })
            StringAssert.Contains(dialog, "StewardVisuals.InformationBanner(\"Check for secrets\", AdvisoryAiExplainText.ResidualSecrets, \"attention\")");
        StringAssert.Contains(explain, "await ShowExplainDraftAsync(explain, subject, draft, texts, saved.Count);", "the draft dialog is shown again, with the warning, after each return");
    }

    [TestMethod]
    public void SavedAnswersStayReachableWhenNoNewDraftIsPossible()
    {
        // ADR-028: disconnect and lower limits keep saved answers; reading or deleting them needs no key and no draft.
        string explain = DesktopFile("MainWindow.AdvisoryAiExplain.cs");
        string noDraft = Between(explain, "if (result.Draft is not AdvisoryAiEvidenceDraft draft)", "List<string?> texts =");
        StringAssert.Contains(noDraft, "ReadSavedExplanationsAsync(explain, source, originId)");
        StringAssert.Contains(noDraft, "await ShowSavedExplanationsAsync(explain, subject, kept);");
        StringAssert.Contains(explain, "AdvisoryAiDiagnosticDraftBuilder.OriginId(snapshot, key)");
        StringAssert.Contains(explain, "AdvisoryAiFailedUpdateDraftBuilder.OriginId(job.JobId)");
    }

    private static string DesktopFile(string name) => File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Desktop", name));

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.IsTrue(from >= 0, $"missing: {start}");
        int to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.IsTrue(to > from, $"missing: {end}");
        return source[from..to];
    }
}
