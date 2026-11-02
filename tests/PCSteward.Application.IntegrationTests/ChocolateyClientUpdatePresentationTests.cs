using System.Security.Cryptography;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-HIS-01, TM-07/13; ADR-031 design mapping §2 (R4), §3.6, §3.7 and §3a: the client self-update
/// review sheet, Verify and history texts, and the workflow rules that apply to the route. The route stays unavailable
/// (not admitted); restart-pending and every non-None detail are Unverified; the profile evidence equality applies.
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdatePresentationTests
{
    [TestMethod]
    public void TheReviewSheetSaysUpdateChocolateyToXAndDisclosesTheSideEffects()
    {
        ChocolateyClientUpdateReviewView view = ChocolateyClientUpdatePresentation.Review(Operation("2.7.5", "2.8.0"))
            ?? throw new AssertFailedException("no review sheet");
        Assert.AreEqual("Update Chocolatey to 2.8.0", view.Title);
        Assert.AreEqual("Chocolatey 2.7.5 → 2.8.0", view.Versions);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.WhatHappensText, view.WhatHappens);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.VerificationText, view.Verification);
        CollectionAssert.Contains(view.Disclosures.ToArray(), ReviewedUpdateLingeringProcesses.ReviewDisclosure);
        Assert.IsTrue(view.Disclosures.Any(text => text.Contains("PATH", StringComparison.Ordinal)));
        Assert.IsTrue(view.Disclosures.Any(text => text.Contains(".old", StringComparison.Ordinal)));
        Assert.AreEqual(ChocolateyClientUpdatePresentation.UnavailableText, view.UnavailableReason, "the route is not admitted");
        Assert.IsFalse(ReviewedUpdateReview.HasSupportedMode(Operation("2.7.5", "2.8.0")));
    }

    [TestMethod]
    public void NoSheetForAnotherRouteADowngradeOrAMalformedVersion()
    {
        Assert.IsNull(ChocolateyClientUpdatePresentation.Review(Operation("2.8.0", "2.7.5")));
        Assert.IsNull(ChocolateyClientUpdatePresentation.Review(Operation("2.7.5", "2.7.5")));
        Assert.IsNull(ChocolateyClientUpdatePresentation.Review(Operation("2.7.5", "2.8.0-beta")));
        Assert.IsNull(ChocolateyClientUpdatePresentation.Review(Operation("2.7.5", "02.8.0")));
        Assert.IsNull(ChocolateyClientUpdatePresentation.Review(PackageOperation()));
        Assert.AreEqual("Update Chocolatey", ChocolateyClientUpdatePresentation.Title("2.8.0<script>"));
    }

    [TestMethod]
    public void VerificationRejectsRestartPendingAndNamesTheUnlistedLeftover()
    {
        ReviewedUpdateOperation operation = Operation("2.7.5", "2.8.0");
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.RestartContractViolationMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation, new PostUpdateInventoryResult(true, true)));
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ChocolateyClientUpdatePresentation.NotVerifiedLeftoverMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation,
                new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientLeftoverNotListed)));
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ChocolateyClientUpdatePresentation.NotVerifiedReleaseMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation,
                new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientChanged)));
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ChocolateyClientUpdatePresentation.NotVerifiedUnreadableMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation,
                new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientUnverifiable)));
        Assert.IsNull(ReviewedUpdateDispatchWorkflow.RejectVerification(operation, new PostUpdateInventoryResult(true, false)));
        StringAssert.Contains(ChocolateyClientUpdatePresentation.NotVerifiedLeftoverMessage, @"helpers\Chocolatey.PowerShell.dll.old");
        StringAssert.Contains(ChocolateyClientUpdatePresentation.NotVerifiedLeftoverMessage, "never deletes");
        // The package route keeps its own message.
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(PackageOperation(),
                new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientLeftoverNotListed)));
    }

    [TestMethod]
    public void TheReviewedLeftoverNoteIsCarriedToVerifyAndHistoryAndOnlyForTheClientRoute()
    {
        // ADR-031 design §3a (MEDIUM-2b): VerifiedWithLeftover is a distinct note on a verified result, never a detail.
        ReviewedUpdateOperation operation = Operation("2.7.5", "2.8.0");
        PostUpdateInventoryResult noted = new(true, false, PostUpdateVerificationDetail.None, PostUpdateVerificationNote.ClientReviewedLeftover);
        Assert.IsNull(ReviewedUpdateDispatchWorkflow.RejectVerification(operation, noted));
        StringAssert.EndsWith(ReviewedUpdateDispatchWorkflow.TargetVerifiedWithLeftoverMessage, ChocolateyClientUpdatePresentation.VerifiedLeftoverNote);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.VerifiedMessage + " " + ChocolateyClientUpdatePresentation.VerifiedLeftoverNote,
            ChocolateyClientUpdatePresentation.VerifiedText(PostUpdateVerificationNote.ClientReviewedLeftover));
        Assert.AreEqual(ChocolateyClientUpdatePresentation.VerifiedMessage, ChocolateyClientUpdatePresentation.VerifiedText(PostUpdateVerificationNote.None));
        StringAssert.Contains(ChocolateyClientUpdatePresentation.VerifiedLeftoverNote, @"helpers\Chocolatey.PowerShell.dll.old");

        // History shows the note beside the verified text, and only for a Success recorded with that exact message.
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.TargetVerifiedText + " " + ChocolateyClientUpdatePresentation.VerifiedLeftoverNote,
            ReviewedUpdateHistoryPresentation.VerificationDetail(Terminal(UpdateDispatchOutcome.Success, ReviewedUpdateDispatchWorkflow.TargetVerifiedWithLeftoverMessage)));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.VerificationDetail(Terminal(UpdateDispatchOutcome.Unverified,
            ReviewedUpdateDispatchWorkflow.TargetVerifiedWithLeftoverMessage)));

        // A note on another route, an undefined note, or a note without a detail of None is never Success.
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(PackageOperation(), noted));
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation, noted with { Note = (PostUpdateVerificationNote)99 }));
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ChocolateyClientUpdatePresentation.NotVerifiedReleaseMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation, noted with { Detail = PostUpdateVerificationDetail.ClientChanged }));
    }

    [TestMethod]
    public void TheLeftoverOfAnotherReleaseAndAnIncompleteReleaseAreNamedAndShownInHistory()
    {
        ReviewedUpdateOperation operation = Operation("2.7.5", "2.8.0");
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ChocolateyClientUpdatePresentation.NotVerifiedLeftoverNotSourceMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation,
                new PostUpdateInventoryResult(true, false, PostUpdateVerificationDetail.ClientLeftoverNotSource)));
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ChocolateyClientUpdatePresentation.NotVerifiedReleaseIncompleteMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation,
                new PostUpdateInventoryResult(true, false, PostUpdateVerificationDetail.ClientReleaseIncomplete)));
        StringAssert.Contains(ChocolateyClientUpdatePresentation.NotVerifiedLeftoverNotSourceMessage, @"helpers\Chocolatey.PowerShell.dll.old");
        foreach (string message in new[]
                 {
                     ChocolateyClientUpdatePresentation.NotVerifiedLeftoverMessage, ChocolateyClientUpdatePresentation.NotVerifiedLeftoverNotSourceMessage,
                     ChocolateyClientUpdatePresentation.NotVerifiedReleaseIncompleteMessage, ChocolateyClientUpdatePresentation.NotVerifiedReleaseMessage,
                     ChocolateyClientUpdatePresentation.NotVerifiedUnreadableMessage,
                 })
        {
            Assert.AreEqual(message, ReviewedUpdateHistoryPresentation.VerificationDetail(Terminal(UpdateDispatchOutcome.Unverified, message)));
            Assert.IsNull(ReviewedUpdateHistoryPresentation.VerificationDetail(Terminal(UpdateDispatchOutcome.Success, message)));
        }
    }

    [TestMethod]
    public void AWatchdogStopIsNeverVerifiedSaysChocolateyMayBePartlyUpdatedAndGivesTheManualStep()
    {
        // ADR-031 design §2.1 R5 runtime watchdog (FR-UPD-15, TM-07/13): each fault has its own fixed message, recognised by history.
        ReviewedUpdateOperation operation = Operation("2.7.5", "2.8.0");
        (PostUpdateVerificationDetail Detail, string Message)[] stops =
        [
            (PostUpdateVerificationDetail.ClientSetupOverrideDuringRun, ChocolateyClientUpdatePresentation.StoppedSetupOverrideMessage),
            (PostUpdateVerificationDetail.ClientRootWritableDuringRun, ChocolateyClientUpdatePresentation.StoppedRootWritableMessage),
            (PostUpdateVerificationDetail.ClientWatchFailedDuringRun, ChocolateyClientUpdatePresentation.StoppedWatchFailedMessage),
        ];
        foreach ((PostUpdateVerificationDetail detail, string message) in stops)
        {
            // Even an otherwise verified result is rejected once the watchdog stopped the run.
            Assert.AreEqual((UpdateDispatchOutcome.Unverified, message),
                ReviewedUpdateDispatchWorkflow.RejectVerification(operation, new PostUpdateInventoryResult(true, false, detail)));
            Assert.AreEqual(message, ChocolateyClientUpdatePresentation.NotVerifiedMessage(detail));
            StringAssert.StartsWith(message, "Stopped, not verified:");
            StringAssert.Contains(message, "may be partly updated");
            StringAssert.Contains(message, "administrator prompt");
            Assert.AreEqual(message, ReviewedUpdateHistoryPresentation.VerificationDetail(Terminal(UpdateDispatchOutcome.Unverified, message)));
        }
        StringAssert.Contains(ChocolateyClientUpdatePresentation.StoppedSetupOverrideMessage, "ChocolateyInstallAllowCurrentUser");
        StringAssert.Contains(ChocolateyClientUpdatePresentation.StoppedRootWritableMessage, "changeable by your account");
        // The forced reinstall advice stays only where the folder was never shown changeable by the user.
        StringAssert.Contains(ChocolateyClientUpdatePresentation.StoppedWatchFailedMessage, "PC Steward does not repair Chocolatey automatically.");
        StringAssert.Contains(ChocolateyClientUpdatePresentation.StoppedWatchFailedMessage,
            "from an administrator prompt, run choco upgrade chocolatey --version <the version you approved> --force");
    }

    [TestMethod]
    [DataRow((int)PostUpdateVerificationDetail.ClientSetupOverrideDuringRun)]
    [DataRow((int)PostUpdateVerificationDetail.ClientRootWritableDuringRun)]
    public void AFolderShownChangeableDuringTheRunIsTreatedAsUntrustedAndNeverRepairedElevated(int injected)
    {
        // Independent review H1 (ADR-031 design §2.2 R5; FR-UPD-15, TM-07/13): the folder may stay changeable by the user, so
        // the message never advises running choco elevated from it; it says to treat it as untrusted and reinstall.
        string message = ChocolateyClientUpdatePresentation.NotVerifiedMessage((PostUpdateVerificationDetail)injected);
        Assert.IsFalse(message.Contains("choco upgrade", StringComparison.OrdinalIgnoreCase), message);
        Assert.IsFalse(message.Contains("--force", StringComparison.Ordinal), message);
        Assert.IsFalse(message.Contains("repair Chocolatey)", StringComparison.Ordinal), message);
        Assert.IsFalse(message.Contains("http", StringComparison.OrdinalIgnoreCase), "no invented URL");
        StringAssert.Contains(message, "Treat the Chocolatey folder as untrusted");
        StringAssert.Contains(message, "do not run choco from an administrator prompt");
        StringAssert.Contains(message, "Remove or quarantine the Chocolatey folder");
        StringAssert.Contains(message, "reinstall Chocolatey using Chocolatey's official installation instructions");
        StringAssert.Contains(message, "PC Steward will not repair it.");
        StringAssert.EndsWith(message, ChocolateyClientUpdatePresentation.UntrustedFolderRecoveryStep);
    }

    private static ReviewedUpdateTerminalRecord Terminal(UpdateDispatchOutcome outcome, string diagnostic) =>
        new(outcome, new DateTimeOffset(2026, 11, 2, 12, 5, 0, TimeSpan.Zero), 0, UpdateDispatchSettlementState.OwnedWorkStopped, diagnostic);

    [TestMethod]
    public void HistoryNamesTheClientRouteAndFreshEvidenceMustEqualTheFrozenProfile()
    {
        Assert.AreEqual(ChocolateyClientUpdatePresentation.HistoryRouteText,
            ReviewedUpdateHistoryPresentation.Route(UpdateRouteContract.ChocolateyClientSelfUpdateV1));
        ReviewedUpdateOperation operation = Operation("2.7.5", "2.8.0");
        Assert.IsTrue(Evidence(operation, operation.EvidenceId).ProfileEvidenceMatches(operation));
        Assert.IsFalse(Evidence(operation, ReviewedUpdateOperation.ProfileEvidencePrefix + new string('0', 64)).ProfileEvidenceMatches(operation));
        Assert.IsFalse(Evidence(operation, "fresh:1").ProfileEvidenceMatches(operation));
    }

    private static FreshUpdateDispatchEvidence Evidence(ReviewedUpdateOperation operation, string evidenceId) =>
        new(operation.Selection, operation.RequestingUserSid, operation.NativeInstanceId, operation.InstallationContextId, evidenceId,
            operation.ReviewedAt, operation.RouteContract, operation.Mode, true, true);

    private static ReviewedUpdateOperation Operation(string installed, string candidate) => Build("chocolatey", installed, candidate,
        UpdateRouteContract.ChocolateyClientSelfUpdateV1);

    private static ReviewedUpdateOperation PackageOperation() => Build("git", "1.0.0", "2.0.0", UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);

    private static ReviewedUpdateOperation Build(string packageId, string installed, string candidate, UpdateRouteContract route)
    {
        DateTimeOffset at = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("73000000-0000-0000-0000-000000000001"),
            Guid.Parse("73000000-0000-0000-0000-000000000002"), new UpdateJobSelection("choco:" + packageId,
                UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, packageId, installed, candidate, UpdateScope.Machine,
                UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "choco:" + packageId, "machine:choco",
            ReviewedUpdateOperation.ProfileEvidencePrefix + Convert.ToHexString(SHA256.HashData("client profile"u8)), at, route,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }
}
