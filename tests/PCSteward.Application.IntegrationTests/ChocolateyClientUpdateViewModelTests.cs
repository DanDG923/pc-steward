using System.Reflection;
using System.Security.Cryptography;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-HIS-01, FR-UI-03/04, TM-03/04/07/13; ADR-031 (owner decisions 1–5; "offered only from a
/// listed, currently trusted client"), ADR-030; design mapping §3.3, §3.7, §8 and §8.6 gate 3: the portable Chocolatey client
/// update view model the Desktop binds. Every property is checked; the offer follows the ADR-031 rule over the compiled list
/// (never a downgrade, never from an unlisted release) and is computed only from a verified installed-client check, never from
/// the scan's reported version; the not-admitted reason is always present, so Confirm is never enabled and the view model has no
/// confirm path. History shows every client outcome message, including a run fault's recovery text on an Uncertain record.
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdateViewModelTests
{
    private const string Signer = "Chocolatey Software, Inc";
    private static readonly ChocolateyClientReleaseCatalogFacts Compiled = new(["2.7.5"], Signer);
    private static readonly string[] FactLabels =
    [
        "Installed", "Reported by the last scan", "Offer", "Reviewed releases", "Signer", "What happens", "Verification", "Administrator permission",
        "Safety checks while it runs", "If the folder may have been changed",
    ];
    private static readonly ChocolateyClientReleaseCatalogFacts Fixture = new(["2.7.5", "2.8.0", "2.9.1"], Signer);

    private static ChocolateyInstalledClientVerification Verified(string release) => ChocolateyInstalledClientVerification.Verified(release);

    private static ChocolateyClientUpdateViewModel Open(ChocolateyClientReleaseCatalogFacts catalog, ChocolateyInstalledClientVerification verification,
        string? reported = "2.7.5") =>
        ChocolateyClientUpdateViewModel.Open(catalog, verification, reported, () => ReviewedRouteSplitTokenState.SplitTokenLimited);

    [TestMethod]
    public void TodaysListShowsTheVerifiedReleaseNoNewerReleaseEveryFactAndADisabledConfirm()
    {
        ChocolateyClientUpdateViewModel model = Open(Compiled, Verified("2.7.5"));

        Assert.AreEqual("Update Chocolatey", model.Title);
        Assert.AreEqual(ChocolateyInstalledClientCheck.Verified, model.Verification.Check);
        Assert.AreEqual("2.7.5", model.VerifiedInstalledVersion);
        Assert.AreEqual("2.7.5", model.ReportedVersion);
        StringAssert.StartsWith(model.InstalledText, "Chocolatey 2.7.5, verified by PC Steward: every Chocolatey file matches this reviewed release.");
        Assert.AreEqual("Chocolatey 2.7.5 (reported by the last scan). " + ChocolateyClientUpdateViewModel.ReportedDisplayOnlyText, model.ReportedText);
        Assert.IsNull(model.OfferVersion);
        Assert.AreEqual("No newer reviewed Chocolatey release is available.", model.OfferText);
        Assert.AreEqual("Chocolatey 2.7.5 (verified) · No newer reviewed release", model.Summary);
        Assert.IsTrue(model.CatalogAvailable);
        Assert.IsTrue(model.InstalledReported);
        CollectionAssert.AreEqual(Compiled.ListedVersions.ToArray(), model.ListedVersions.ToArray());
        Assert.AreEqual("2.7.5", model.ReviewedReleasesText);
        Assert.AreEqual(Signer, model.Signer);
        StringAssert.Contains(model.SignerText, "signed by Chocolatey Software, Inc");
        StringAssert.Contains(model.SignerText, "every file exactly matches a reviewed release");
        Assert.AreEqual(ReviewedRouteSplitTokenState.SplitTokenLimited, model.SplitTokenState);

        // The review-sheet facts, in display order, each with an accessible name.
        CollectionAssert.AreEqual(FactLabels, model.Fields.Select(field => field.Label).ToArray());
        CollectionAssert.AreEqual(new[]
        {
            model.InstalledText, model.ReportedText, model.OfferText, model.ReviewedReleasesText, model.SignerText, ChocolateyClientUpdatePresentation.WhatHappensText,
            ChocolateyClientUpdatePresentation.VerificationText, ChocolateyClientUpdateViewModel.PermissionText,
            ChocolateyClientUpdateViewModel.WatchdogText, ChocolateyClientUpdateViewModel.UntrustedFolderCautionText,
        }, model.Fields.Select(field => field.Text).ToArray());
        StringAssert.Contains(ChocolateyClientUpdatePresentation.VerificationText, "checks every Chocolatey client file");
        StringAssert.StartsWith(ChocolateyClientUpdateViewModel.PermissionText, "Windows will ask for permission once");
        StringAssert.Contains(ChocolateyClientUpdateViewModel.WatchdogText, "stops the update immediately");
        StringAssert.EndsWith(ChocolateyClientUpdateViewModel.UntrustedFolderCautionText, ChocolateyClientUpdatePresentation.UntrustedFolderRecoveryStep);
        CollectionAssert.AreEqual(ChocolateyClientUpdatePresentation.Disclosures.ToArray(), model.DisclosureFields.Select(field => field.Text).ToArray());
        Assert.IsTrue(model.DisclosureFields.All(field => field.Label == "Chocolatey's setup"));
        AssertAccessibleAndNoElevatedAdvice(model);

        // Unavailable: not admitted (always), then no newer reviewed release.
        CollectionAssert.AreEqual(new[]
        {
            ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview, ChocolateyClientUpdateUnavailableReason.NoNewerReviewedRelease,
        }, model.UnavailableReasons.ToArray());
        CollectionAssert.AreEqual(new[] { "Not available yet: waiting for review and owner approval.", ChocolateyClientUpdateViewModel.NoNewerReviewedReleaseText },
            model.UnavailableFields.Select(field => field.Text).ToArray());
        Assert.IsTrue(model.UnavailableFields.All(field => field.Label == "Unavailable"));
        AssertConfirmDisabled(model);
        StringAssert.StartsWith(model.EntryAutomationName, ChocolateyClientUpdateViewModel.EntryText.TrimEnd('…'));
        StringAssert.Contains(model.EntryAutomationName, model.Summary);
    }

    [TestMethod]
    public void AVerifiedOlderListedReleaseIsOfferedOnlyTheNewestAndItIsNotYetEnabled()
    {
        foreach (string installed in new[] { "2.7.5", "2.8.0" })
        {
            ChocolateyClientUpdateViewModel model = Open(Fixture, Verified(installed));
            Assert.AreEqual("2.9.1", model.OfferVersion, "the newest listed release, never an intermediate one");
            Assert.AreEqual("A newer reviewed Chocolatey release is available: 2.9.1 (not yet enabled)", model.Title);
            Assert.IsFalse(model.Title.StartsWith("Update Chocolatey to", StringComparison.Ordinal));
            Assert.IsFalse(model.Title.Contains("may be available", StringComparison.Ordinal));
            Assert.AreEqual($"A newer reviewed Chocolatey release is available: 2.9.1 (not yet enabled). PC Steward verified the installed Chocolatey {installed}; " +
                "2.9.1 is the newest release on its reviewed list. Updating Chocolatey itself is not available yet.", model.OfferText);
            // Independent review L5: never the "Update available — verified" status, which is reserved for "Review and update".
            Assert.AreEqual($"Chocolatey {installed} (verified) · 2.9.1 is a newer reviewed release; updating is not available yet", model.Summary);
            Assert.IsFalse(model.Summary.Contains("Update available", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("2.7.5, 2.8.0, 2.9.1", model.ReviewedReleasesText);
            // Even with a verified offer and a split token, the route is not admitted.
            CollectionAssert.AreEqual(new[] { ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview }, model.UnavailableReasons.ToArray());
            AssertConfirmDisabled(model);
        }

        ChocolateyClientUpdateViewModel newest = Open(Fixture, Verified("2.9.1"));
        Assert.IsNull(newest.OfferVersion, "never an equal release");
        Assert.AreEqual(ChocolateyClientUpdateViewModel.NoNewerReleaseText, newest.OfferText, "listed, nothing newer listed");
        Assert.AreEqual("Chocolatey 2.9.1 (verified) · No newer reviewed release", newest.Summary);
        CollectionAssert.Contains(newest.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.NoNewerReviewedRelease);
        AssertConfirmDisabled(newest);

        Assert.AreEqual("2.9.1", ChocolateyClientUpdateViewModel.OfferTarget("2.7.5", Fixture.ListedVersions));
        Assert.IsNull(ChocolateyClientUpdateViewModel.OfferTarget("2.9.1", Fixture.ListedVersions));
        Assert.IsNull(ChocolateyClientUpdateViewModel.OfferTarget(null, Fixture.ListedVersions));
        Assert.IsNull(ChocolateyClientUpdateViewModel.OfferTarget("2.7.5", []));
    }

    [TestMethod]
    public void TheOfferComesOnlyFromTheVerifiedReleaseNeverFromTheScan()
    {
        // ADR-031 / design §8.6 gate 3: the scan reports an old listed release, but the verified client is the newest: no offer.
        ChocolateyClientUpdateViewModel newestVerified = Open(Fixture, Verified("2.9.1"), reported: "2.7.5");
        Assert.IsNull(newestVerified.OfferVersion);
        Assert.AreEqual("2.7.5", newestVerified.ReportedVersion, "the scan's report stays visible as secondary information");
        StringAssert.StartsWith(newestVerified.ReportedText, "Chocolatey 2.7.5 (reported by the last scan).");
        // The scan reports the newest release, but the verified client is older: the offer follows the verified release.
        Assert.AreEqual("2.9.1", Open(Fixture, Verified("2.7.5"), reported: "2.9.1").OfferVersion);
        Assert.AreEqual("2.9.1", Open(Fixture, Verified("2.7.5"), reported: null).OfferVersion, "no scan needed");

        // Without a verified result nothing is offered, whatever the scan reported.
        foreach (ChocolateyInstalledClientVerification unverified in NotVerified().Append(ChocolateyInstalledClientVerification.NotChecked)
                     .Append(ChocolateyInstalledClientVerification.Checking))
            foreach (string? reported in new[] { "2.7.5", "2.8.0", null })
            {
                ChocolateyClientUpdateViewModel model = Open(Fixture, unverified, reported);
                Assert.IsNull(model.OfferVersion, $"{unverified.Check} {reported}");
                Assert.IsNull(model.VerifiedInstalledVersion);
                Assert.AreEqual("Update Chocolatey", model.Title);
                AssertConfirmDisabled(model);
            }
    }

    [TestMethod]
    public void SummariesShowNotCheckedYetUntilTheReviewChecks()
    {
        ChocolateyClientUpdateViewModel model = Open(Fixture, ChocolateyInstalledClientVerification.NotChecked);
        Assert.AreEqual("Chocolatey 2.7.5 (reported by the last scan) · Not checked yet", model.Summary);
        Assert.AreEqual(ChocolateyClientUpdateViewModel.NotCheckedInstalledText, model.InstalledText);
        Assert.AreEqual(ChocolateyClientUpdateViewModel.NotCheckedOfferText, model.OfferText);
        CollectionAssert.AreEqual(new[]
        {
            ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview, ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotChecked,
        }, model.UnavailableReasons.ToArray());
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledReleaseNotCheckedText, model.UnavailableFields[1].Text);
        // The Settings card's second line is the not-admitted reason.
        Assert.AreEqual(ChocolateyClientUpdateViewModel.NotAdmittedText, model.UnavailableFields[0].Text);
        Assert.AreEqual("Chocolatey (release unknown) · Not checked yet", Open(Fixture, ChocolateyInstalledClientVerification.NotChecked, null).Summary);
        AssertConfirmDisabled(model);

        ChocolateyClientUpdateViewModel checking = Open(Fixture, ChocolateyInstalledClientVerification.Checking);
        Assert.AreEqual("Chocolatey 2.7.5 (reported by the last scan) · Checking…", checking.Summary);
        Assert.AreEqual(ChocolateyClientUpdateViewModel.CheckingText, checking.InstalledText);
        Assert.AreEqual(ChocolateyClientUpdateViewModel.CheckingOfferText, checking.OfferText);
        // Independent review L1: while the check runs, the reason says it is checking, not "not checked yet".
        CollectionAssert.AreEqual(new[]
        {
            ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview, ChocolateyClientUpdateUnavailableReason.InstalledReleaseChecking,
        }, checking.UnavailableReasons.ToArray());
        Assert.AreEqual("PC Steward is checking the installed Chocolatey; nothing is offered until it is verified.", checking.UnavailableFields[1].Text);
        Assert.IsFalse(checking.UnavailableFields.Any(field => field.Text.StartsWith(ChocolateyClientUpdateViewModel.NotCheckedYetText, StringComparison.Ordinal)));
        AssertConfirmDisabled(checking);
    }

    [TestMethod]
    [DataRow(ChocolateyInstalledClientCheck.Refused, "its files, folder or settings do not exactly match a reviewed release.")]
    [DataRow(ChocolateyInstalledClientCheck.Unavailable, "the check could not be completed.")]
    [DataRow(ChocolateyInstalledClientCheck.TimedOut, "the check did not finish in time.")]
    [DataRow(ChocolateyInstalledClientCheck.Cancelled, "the check was stopped.")]
    public void ARefusedUnavailableTimedOutOrCancelledCheckOffersNothingAndSaysItCouldNotVerify(ChocolateyInstalledClientCheck check, string detail)
    {
        ChocolateyInstalledClientVerification verification = NotVerified().Single(candidate => candidate.Check == check);
        Assert.IsTrue(verification.CouldNotVerify);
        Assert.IsNull(verification.InstalledRelease);
        foreach (ChocolateyClientReleaseCatalogFacts catalog in new[] { Compiled, Fixture })
        {
            ChocolateyClientUpdateViewModel model = Open(catalog, verification);
            Assert.IsNull(model.OfferVersion);
            Assert.AreEqual("Update Chocolatey", model.Title);
            Assert.AreEqual("PC Steward could not verify the installed Chocolatey: " + detail, model.InstalledText);
            Assert.AreEqual("No update offered: PC Steward could not verify the installed Chocolatey.", model.OfferText);
            Assert.AreEqual("Chocolatey 2.7.5 (reported by the last scan) · Could not verify the installed Chocolatey", model.Summary);
            StringAssert.StartsWith(model.ReportedText, "Chocolatey 2.7.5 (reported by the last scan).");
            CollectionAssert.AreEqual(new[]
            {
                ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview, ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotVerified,
            }, model.UnavailableReasons.ToArray());
            StringAssert.StartsWith(model.UnavailableFields[1].Text, "PC Steward could not verify the installed Chocolatey, so no update is offered.");
            AssertAccessibleAndNoElevatedAdvice(model);
            AssertConfirmDisabled(model);
        }
    }

    [TestMethod]
    public void WithVerificationKeepsTheOpenedViewAndProbesNothing()
    {
        int probes = 0;
        ChocolateyClientUpdateViewModel opened = ChocolateyClientUpdateViewModel.Open(Fixture, ChocolateyInstalledClientVerification.Checking, "2.4.0",
            () => { probes++; return ReviewedRouteSplitTokenState.NoSplitToken; });
        ChocolateyClientUpdateViewModel verified = opened.WithVerification(Verified("2.8.0"));
        ChocolateyClientUpdateViewModel timedOut = opened.WithVerification(ChocolateyInstalledClientVerification.TimedOut);
        Assert.AreEqual(1, probes, "the split token is probed when the review opens, not again for its check result");
        Assert.AreEqual("2.9.1", verified.OfferVersion);
        Assert.AreEqual("2.4.0", verified.ReportedVersion);
        Assert.AreEqual(ReviewedRouteSplitTokenState.NoSplitToken, verified.SplitTokenState);
        CollectionAssert.AreEqual(new[]
        {
            ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview, ChocolateyClientUpdateUnavailableReason.SplitTokenRequired,
        }, verified.UnavailableReasons.ToArray());
        CollectionAssert.AreEqual(opened.ListedVersions.ToArray(), verified.ListedVersions.ToArray());
        Assert.IsNull(timedOut.OfferVersion);
        CollectionAssert.Contains(timedOut.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotVerified);
        AssertConfirmDisabled(verified);
        AssertConfirmDisabled(timedOut);
        Assert.ThrowsExactly<ArgumentNullException>(() => opened.WithVerification(null!));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("2.4.0")]
    [DataRow("3.0.0")]
    [DataRow("2.7.5-beta")]
    [DataRow("02.7.5")]
    [DataRow("2.7")]
    [DataRow("2.7.5 ")]
    public void TheReportedReleaseIsSecondaryDisplayOnly(string? reported)
    {
        ChocolateyClientUpdateViewModel model = Open(Fixture, ChocolateyInstalledClientVerification.NotChecked, reported);
        Assert.IsNull(model.OfferVersion);
        bool wellFormed = reported is "2.4.0" or "3.0.0";
        Assert.AreEqual(wellFormed ? reported : null, model.ReportedVersion);
        Assert.AreEqual(reported is not null, model.InstalledReported);
        Assert.AreEqual(model.ReportedText, model.Fields.Single(field => field.Label == "Reported by the last scan").Text);
        if (wellFormed)
        {
            Assert.AreEqual($"Chocolatey {reported} (reported by the last scan). " + ChocolateyClientUpdateViewModel.ReportedDisplayOnlyText, model.ReportedText);
            Assert.AreEqual($"Chocolatey {reported} (reported by the last scan) · Not checked yet", model.Summary);
        }
        else
        {
            Assert.AreEqual("Chocolatey (release unknown) · Not checked yet", model.Summary);
            // Independent review L4: no report at all is distinguished from a reported version that could not be read.
            Assert.AreEqual(reported is null
                ? "Unknown: no scan has reported a Chocolatey version."
                : "Unknown: the last scan reported a Chocolatey version that could not be read.", model.ReportedText);
        }
        AssertConfirmDisabled(model);
    }

    [TestMethod]
    [DataRow("2.4.0")]
    [DataRow("3.0.0")]
    public void AVerifiedReleaseNotOnTheShownListIsOfferedNothingAndSaysWhy(string installed)
    {
        // Independent review M1: a release older or newer than every listed one is not offered anything, and the text says why: it is
        // not on the reviewed list (never "no newer release is available").
        ChocolateyClientUpdateViewModel model = Open(Fixture, Verified(installed));
        Assert.IsNull(model.OfferVersion);
        Assert.IsNull(ChocolateyClientUpdateViewModel.OfferTarget(installed, Fixture.ListedVersions));
        Assert.AreEqual(installed, model.VerifiedInstalledVersion);
        Assert.AreEqual("Update Chocolatey", model.Title);
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledNotListedOfferText, model.OfferText);
        Assert.AreEqual($"Chocolatey {installed} (verified) · Not on the reviewed list", model.Summary);
        Assert.AreEqual(model.OfferText, model.Fields.Single(field => field.Label == "Offer").Text);
        CollectionAssert.AreEqual(new[]
        {
            ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview, ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotReviewed,
        }, model.UnavailableReasons.ToArray());
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledReleaseNotReviewedText, model.UnavailableFields[1].Text);
        // Independent review L2: the reason matches the verified state; it never calls the verified release "unknown".
        StringAssert.StartsWith(model.UnavailableFields[1].Text, "The verified Chocolatey release is not on PC Steward's reviewed list, so no update is offered.");
        Assert.IsFalse(model.UnavailableFields[1].Text.Contains("unknown", StringComparison.OrdinalIgnoreCase));
        AssertConfirmDisabled(model);
    }

    [TestMethod]
    public void AMalformedVerifiedReleaseIsUnavailableNeverVerified()
    {
        foreach (string? malformed in new[] { null, "", "2.7.5-beta", "02.7.5", "2.7", "2.7.5 ", "2.7.5.0.1" })
        {
            ChocolateyInstalledClientVerification verification = ChocolateyInstalledClientVerification.Verified(malformed!);
            Assert.AreSame(ChocolateyInstalledClientVerification.Unavailable, verification, malformed);
            Assert.IsNull(Open(Fixture, verification).OfferVersion);
        }
        Assert.AreEqual("2.7.5", Verified("2.7.5").InstalledRelease);
        Assert.IsFalse(Verified("2.7.5").CouldNotVerify);
        Assert.IsFalse(ChocolateyInstalledClientVerification.NotChecked.CouldNotVerify);
        Assert.IsFalse(ChocolateyInstalledClientVerification.Checking.CouldNotVerify);
    }

    [TestMethod]
    public void AMalformedCatalogFailsClosedToNoReviewedReleases()
    {
        ChocolateyClientReleaseCatalogFacts[] malformed =
        [
            new(["2.9.1", "2.7.5"], Signer), new(["2.7.5", "2.7.5"], Signer), new(["2.7.5", "2.8.0-rc1"], Signer), new([], Signer),
            new(null!, Signer), new(Enumerable.Range(1, 65).Select(index => $"1.0.{index}").ToArray(), Signer),
        ];
        foreach (ChocolateyClientReleaseCatalogFacts catalog in malformed)
        {
            ChocolateyClientUpdateViewModel model = Open(catalog, Verified("2.7.5"));
            Assert.AreEqual(0, model.ListedVersions.Count);
            // Independent review L4: a list that fails validation is unavailable, never "None".
            Assert.IsFalse(model.CatalogAvailable);
            Assert.AreEqual("Unavailable", model.ReviewedReleasesText);
            Assert.AreEqual("Reviewed releases: Unavailable", model.Fields.Single(field => field.Label == "Reviewed releases").AutomationName);
            Assert.AreEqual(ChocolateyClientUpdateViewModel.CatalogUnavailableOfferText, model.OfferText);
            Assert.AreEqual("Chocolatey 2.7.5 (verified) · Reviewed releases unavailable", model.Summary);
            Assert.IsNull(model.OfferVersion);
            // Independent review L2: an unavailable list has its own reason, never "not on the reviewed list".
            CollectionAssert.AreEqual(new[]
            {
                ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview, ChocolateyClientUpdateUnavailableReason.ReviewedReleasesUnavailable,
            }, model.UnavailableReasons.ToArray());
            Assert.AreEqual("PC Steward's list of reviewed releases is unavailable, so no update is offered.", model.UnavailableFields[1].Text);
            AssertConfirmDisabled(model);
        }
        foreach (string? signer in new[] { null, "", " ", new string('x', 129) })
            Assert.AreEqual("Unknown", Open(Compiled with { Signer = signer! }, Verified("2.7.5")).Signer);
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyClientUpdateViewModel.Open(null!, Verified("2.7.5"), "2.7.5", () => ReviewedRouteSplitTokenState.SplitTokenLimited));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyClientUpdateViewModel.Open(Compiled, null!, "2.7.5", () => ReviewedRouteSplitTokenState.SplitTokenLimited));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyClientUpdateViewModel.Open(Compiled, Verified("2.7.5"), "2.7.5", null!));
    }

    [TestMethod]
    public void AFaultBuildingTheCompiledListFallsBackToAnUnavailableCatalog()
    {
        // Independent review L5: the composition reads the compiled list through ReadOrUnavailable, so no fault can fail startup.
        Assert.AreSame(Compiled, ChocolateyClientReleaseCatalogFacts.ReadOrUnavailable(() => Compiled));
        Func<ChocolateyClientReleaseCatalogFacts>[] faulted =
        [
            () => throw new TypeInitializationException("PCSteward.Provider.Chocolatey.ChocolateyClientReleaseCatalog", new InvalidOperationException("list")),
            () => throw new InvalidOperationException("list"), () => null!,
        ];
        foreach (Func<ChocolateyClientReleaseCatalogFacts> read in faulted)
        {
            ChocolateyClientReleaseCatalogFacts facts = ChocolateyClientReleaseCatalogFacts.ReadOrUnavailable(read);
            Assert.AreSame(ChocolateyClientReleaseCatalogFacts.Unavailable, facts);
            ChocolateyClientUpdateViewModel model = Open(facts, Verified("2.7.5"));
            Assert.IsFalse(model.CatalogAvailable);
            Assert.AreEqual(ChocolateyClientUpdateViewModel.ReviewedReleasesUnavailableText, model.ReviewedReleasesText);
            Assert.AreEqual(ChocolateyClientUpdateViewModel.CatalogUnavailableOfferText, model.OfferText);
            Assert.AreEqual("Unknown", model.Signer);
            Assert.IsNull(model.OfferVersion);
            AssertConfirmDisabled(model);
        }
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyClientReleaseCatalogFacts.ReadOrUnavailable(null!));

        // A list that faults while it is read is unavailable too (the view never throws on display data).
        ChocolateyClientUpdateViewModel throwing = Open(new ChocolateyClientReleaseCatalogFacts(new ThrowingList(), Signer), Verified("2.7.5"));
        Assert.IsFalse(throwing.CatalogAvailable);
        Assert.AreEqual(ChocolateyClientUpdateViewModel.ReviewedReleasesUnavailableText, throwing.ReviewedReleasesText);
        AssertConfirmDisabled(throwing);
    }

    private sealed class ThrowingList : IReadOnlyList<string>
    {
        public string this[int index] => throw new InvalidOperationException("unreadable");
        public int Count => 1;
        public IEnumerator<string> GetEnumerator() => throw new InvalidOperationException("unreadable");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [TestMethod]
    [DataRow(ReviewedRouteSplitTokenState.NoSplitToken, ChocolateyClientUpdateUnavailableReason.SplitTokenRequired)]
    [DataRow(ReviewedRouteSplitTokenState.DesktopTokenUnsupported, ChocolateyClientUpdateUnavailableReason.DesktopTokenUnsupported)]
    [DataRow(ReviewedRouteSplitTokenState.Unknown, ChocolateyClientUpdateUnavailableReason.DesktopTokenUnknown)]
    [DataRow((ReviewedRouteSplitTokenState)99, ChocolateyClientUpdateUnavailableReason.DesktopTokenUnknown)]
    public void TheProbedSplitTokenStateAddsItsReason(ReviewedRouteSplitTokenState state, ChocolateyClientUpdateUnavailableReason reason)
    {
        foreach (ChocolateyClientReleaseCatalogFacts catalog in new[] { Compiled, Fixture })
        {
            ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.Open(catalog, Verified("2.7.5"), "2.7.5", () => state);
            Assert.AreEqual(reason, model.UnavailableReasons[^1]);
            Assert.AreEqual(ChocolateyClientUpdateViewModel.Text(reason), model.UnavailableFields[^1].Text);
            Assert.AreEqual(Enum.IsDefined(state) ? state : ReviewedRouteSplitTokenState.Unknown, model.SplitTokenState);
            AssertConfirmDisabled(model);
        }
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.SplitTokenRequiredText, ChocolateyClientUpdateViewModel.Text(ChocolateyClientUpdateUnavailableReason.SplitTokenRequired));
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.DesktopTokenUnsupportedText, ChocolateyClientUpdateViewModel.Text(ChocolateyClientUpdateUnavailableReason.DesktopTokenUnsupported));
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.DesktopTokenUnknownText, ChocolateyClientUpdateViewModel.Text(ChocolateyClientUpdateUnavailableReason.DesktopTokenUnknown));
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledReleaseNotCheckedText, ChocolateyClientUpdateViewModel.Text(ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotChecked));
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledReleaseNotVerifiedText, ChocolateyClientUpdateViewModel.Text(ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotVerified));
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledReleaseCheckingText, ChocolateyClientUpdateViewModel.Text(ChocolateyClientUpdateUnavailableReason.InstalledReleaseChecking));
        Assert.AreEqual(ChocolateyClientUpdateViewModel.ReviewedReleasesUnavailableReasonText, ChocolateyClientUpdateViewModel.Text(ChocolateyClientUpdateUnavailableReason.ReviewedReleasesUnavailable));
        foreach (ChocolateyClientUpdateUnavailableReason defined in Enum.GetValues<ChocolateyClientUpdateUnavailableReason>())
            Assert.IsFalse(string.IsNullOrWhiteSpace(ChocolateyClientUpdateViewModel.Text(defined)), defined.ToString());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ChocolateyClientUpdateViewModel.Text((ChocolateyClientUpdateUnavailableReason)99));
    }

    [TestMethod]
    public void TheSplitTokenIsProbedAtEveryOpenAndAProbeFaultIsUnknown()
    {
        int probes = 0;
        ReviewedRouteSplitTokenState Probe() { probes++; return probes == 1 ? ReviewedRouteSplitTokenState.SplitTokenLimited : ReviewedRouteSplitTokenState.NoSplitToken; }
        ChocolateyClientUpdateViewModel first = ChocolateyClientUpdateViewModel.Open(Compiled, Verified("2.7.5"), "2.7.5", Probe);
        ChocolateyClientUpdateViewModel second = ChocolateyClientUpdateViewModel.Open(Compiled, Verified("2.7.5"), "2.7.5", Probe);
        Assert.AreEqual(2, probes, "one probe per open, never cached");
        CollectionAssert.DoesNotContain(first.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.SplitTokenRequired);
        CollectionAssert.Contains(second.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.SplitTokenRequired);
        ChocolateyClientUpdateViewModel faulted = ChocolateyClientUpdateViewModel.Open(Compiled, Verified("2.7.5"), "2.7.5",
            () => throw new InvalidOperationException("token unreadable"));
        Assert.AreEqual(ReviewedRouteSplitTokenState.Unknown, faulted.SplitTokenState);
        CollectionAssert.Contains(faulted.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.DesktopTokenUnknown);
        AssertConfirmDisabled(faulted);
    }

    // ---- The bounded, cancellable check (design §8.6 gate 3) -------------------------------------------------------------

    [TestMethod]
    public async Task RunAsyncPassesAVerifiedOrRefusedResultThroughUnderTheBoundedToken()
    {
        CancellationToken seen = default;
        ChocolateyInstalledClientVerification verified = await ChocolateyInstalledClientVerification.RunAsync(token =>
        {
            seen = token;
            return Task.FromResult(Verified("2.7.5"));
        }, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.AreEqual("2.7.5", verified.InstalledRelease);
        Assert.IsTrue(seen.CanBeCanceled, "the check runs under the bounded token");
        foreach (ChocolateyInstalledClientVerification result in NotVerified().Where(candidate => candidate.Check != ChocolateyInstalledClientCheck.Cancelled))
            Assert.AreSame(result, await ChocolateyInstalledClientVerification.RunAsync(_ => Task.FromResult(result), TimeSpan.FromSeconds(30), CancellationToken.None));
    }

    [TestMethod]
    public async Task RunAsyncMapsAFaultANullOrANonFinalResultToUnavailable()
    {
        Func<CancellationToken, Task<ChocolateyInstalledClientVerification>>[] checks =
        [
            _ => throw new InvalidOperationException("observer fault"),
            _ => Task.FromException<ChocolateyInstalledClientVerification>(new UnauthorizedAccessException("denied")),
            _ => Task.FromResult<ChocolateyInstalledClientVerification>(null!),
            _ => Task.FromResult(ChocolateyInstalledClientVerification.NotChecked),
            _ => Task.FromResult(ChocolateyInstalledClientVerification.Checking),
            // A check that reports cancellation although nobody cancelled and the bound did not pass.
            _ => Task.FromResult(ChocolateyInstalledClientVerification.Cancelled),
        ];
        foreach (Func<CancellationToken, Task<ChocolateyInstalledClientVerification>> check in checks)
            Assert.AreSame(ChocolateyInstalledClientVerification.Unavailable,
                await ChocolateyInstalledClientVerification.RunAsync(check, TimeSpan.FromSeconds(30), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ChocolateyInstalledClientVerification.RunAsync(null!, TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [TestMethod]
    public async Task RunAsyncTimesOutAHangingCheckAndCancelsIt()
    {
        TaskCompletionSource<bool> cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ChocolateyInstalledClientVerification result = await ChocolateyInstalledClientVerification.RunAsync(async token =>
        {
            token.Register(() => cancelled.TrySetResult(true));
            await Task.Delay(Timeout.Infinite, CancellationToken.None);
            return Verified("2.7.5");
        }, TimeSpan.FromMilliseconds(100), CancellationToken.None);
        Assert.AreSame(ChocolateyInstalledClientVerification.TimedOut, result);
        Assert.IsTrue(await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10)), "the bound cancels the check's token");

        // A check that honours the bounded token and reports Cancelled is a timeout, not a user cancellation.
        Assert.AreSame(ChocolateyInstalledClientVerification.TimedOut, await ChocolateyInstalledClientVerification.RunAsync(async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { }
            return ChocolateyInstalledClientVerification.Cancelled;
        }, TimeSpan.FromMilliseconds(50), CancellationToken.None));
        Assert.AreSame(ChocolateyInstalledClientVerification.TimedOut,
            await ChocolateyInstalledClientVerification.RunAsync(_ => Task.FromResult(Verified("2.7.5")), TimeSpan.Zero, CancellationToken.None));
        Assert.IsTrue(ChocolateyInstalledClientVerification.DisplayCheckTimeout > TimeSpan.FromSeconds(65), "outside the native budget plus its cap");
        Assert.IsTrue(ChocolateyInstalledClientVerification.DisplayCheckTimeout <= TimeSpan.FromSeconds(90));
    }

    [TestMethod]
    public async Task RunAsyncIsCancelledWhenTheDialogCloses()
    {
        using CancellationTokenSource dialog = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> checkSawCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ChocolateyInstalledClientVerification> running = ChocolateyInstalledClientVerification.RunAsync(async token =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { checkSawCancellation.TrySetResult(true); }
            return Verified("2.7.5");
        }, TimeSpan.FromSeconds(30), dialog.Token);
        await started.Task;
        dialog.Cancel();
        Assert.AreSame(ChocolateyInstalledClientVerification.Cancelled, await running.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsTrue(await checkSawCancellation.Task.WaitAsync(TimeSpan.FromSeconds(10)), "closing the dialog cancels the running check");

        int calls = 0;
        Assert.AreSame(ChocolateyInstalledClientVerification.Cancelled, await ChocolateyInstalledClientVerification.RunAsync(_ =>
        {
            calls++;
            return Task.FromResult(Verified("2.7.5"));
        }, TimeSpan.FromSeconds(30), dialog.Token));
        Assert.AreEqual(0, calls, "a closed dialog starts no check");
    }

    [TestMethod]
    public void TheInstalledReleaseComesOnlyFromTheChocolateyInventoryClientPackage()
    {
        InventoryRow client = Row("chocolatey", "chocolatey", "2.7.5");
        InventoryRow git = Row("chocolatey", "git", "2.45.0");
        InventoryRow wingetNamedChocolatey = Row("winget", "chocolatey", "9.9.9");
        Assert.AreEqual("2.7.5", ChocolateyClientUpdateViewModel.ReportedInstalledVersion([git, client, wingetNamedChocolatey]));
        Assert.AreEqual("2.7.5", ChocolateyClientUpdateViewModel.ReportedInstalledVersion([Row("Chocolatey", "Chocolatey", "2.7.5")]));
        Assert.IsNull(ChocolateyClientUpdateViewModel.ReportedInstalledVersion([git, wingetNamedChocolatey]), "no client row: not reported");
        Assert.IsNull(ChocolateyClientUpdateViewModel.ReportedInstalledVersion([]));
        // Independent review L4: a reported but unreadable version is the empty string (reported, unreadable), never null.
        Assert.AreEqual(string.Empty, ChocolateyClientUpdateViewModel.ReportedInstalledVersion([client, Row("chocolatey", "chocolatey", "2.8.0")]), "conflicting rows");
        Assert.AreEqual(string.Empty, ChocolateyClientUpdateViewModel.ReportedInstalledVersion([Row("chocolatey", "chocolatey", null)]));
        Assert.AreEqual(string.Empty, ChocolateyClientUpdateViewModel.ReportedInstalledVersion([Row("chocolatey", "chocolatey", "2.7.5-beta")]));
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledUnreadableText, Open(Compiled, ChocolateyInstalledClientVerification.NotChecked,
            ChocolateyClientUpdateViewModel.ReportedInstalledVersion([Row("chocolatey", "chocolatey", "2.7.5-beta")])).ReportedText);
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledUnknownText, Open(Compiled, ChocolateyInstalledClientVerification.NotChecked,
            ChocolateyClientUpdateViewModel.ReportedInstalledVersion([git])).ReportedText);
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyClientUpdateViewModel.ReportedInstalledVersion(null!));

        Assert.IsTrue(ChocolateyClientUpdateViewModel.IsClientRow(client));
        Assert.IsFalse(ChocolateyClientUpdateViewModel.IsClientRow(git));
        Assert.IsFalse(ChocolateyClientUpdateViewModel.IsClientRow(wingetNamedChocolatey));
        Assert.IsFalse(ChocolateyClientUpdateViewModel.IsClientRow(Row("chocolatey", "chocolatey.extension", "2.7.5")));
        Assert.IsFalse(ChocolateyClientUpdateViewModel.IsClientRow(null));
    }

    private static IEnumerable<ChocolateyInstalledClientVerification> NotVerified() =>
    [
        ChocolateyInstalledClientVerification.Refused, ChocolateyInstalledClientVerification.Unavailable,
        ChocolateyInstalledClientVerification.TimedOut, ChocolateyInstalledClientVerification.Cancelled,
    ];

    private static void AssertAccessibleAndNoElevatedAdvice(ChocolateyClientUpdateViewModel model)
    {
        foreach (ReviewedDisplayField field in model.Fields.Concat(model.DisclosureFields).Concat(model.UnavailableFields))
        {
            Assert.AreEqual(field.Label + ": " + field.Text, field.AutomationName);
            // The pre-review copy never advises an elevated choco command.
            Assert.IsFalse(field.Text.Contains("choco upgrade", StringComparison.Ordinal) || field.Text.Contains("--force", StringComparison.Ordinal), field.Text);
        }
    }

    [TestMethod]
    public void TheAdmittedRouteStillGivesTheViewModelNoConfirmPath()
    {
        // ADR-031 design §8.6 gate 3, re-verified on the Task 50 activation (TASK_50_ACTIVATION_CHANGE.md §6, review L10): the
        // review gate now admits the exact client shape, so the sheet shows no unavailable reason; the Desktop offer still cannot
        // be confirmed, because the view model's not-admitted reason does not depend on HasSupportedMode and it holds no
        // operation, review, draft or permit.
        Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(ClientOperation("2.7.5", "2.8.0")), "the activated review gate admits the exact client shape");
        ChocolateyClientUpdateReviewView sheet = ChocolateyClientUpdatePresentation.Review(ClientOperation("2.7.5", "2.8.0"))!;
        Assert.IsNull(sheet.UnavailableReason, "the route is admitted");
        // Strongest case first: a verified older listed release with a newer reviewed release on the list (an offer is shown).
        foreach (ChocolateyInstalledClientVerification verification in new[] { Verified("2.7.5") }.Concat(NotVerified()))
            foreach (ReviewedRouteSplitTokenState token in Enum.GetValues<ReviewedRouteSplitTokenState>())
            {
                ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.Open(Fixture, verification, "2.7.5", () => token);
                Assert.AreEqual(ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview, model.UnavailableReasons[0], $"{verification.Check} {token}");
                Assert.IsFalse(model.CanConfirm, $"{verification.Check} {token}");
                Assert.IsFalse(model.IsConfirmEnabled, $"{verification.Check} {token}");
                // Gate 3 itself: the offer comes only from the verified release, never from the scan-reported one.
                Assert.AreEqual(verification.Check == ChocolateyInstalledClientCheck.Verified ? "2.9.1" : null, model.OfferVersion, $"{verification.Check} {token}");
            }

        Type type = typeof(ChocolateyClientUpdateViewModel);
        BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        Type[] forbidden = [typeof(ReviewedUpdateReview), typeof(ReviewedUpdateDraft), typeof(LiveApprovedUpdate), typeof(ReviewedUpdateOperation)];
        foreach (FieldInfo field in type.GetFields(all))
            CollectionAssert.DoesNotContain(forbidden, field.FieldType, field.Name);
        foreach (MethodInfo method in type.GetMethods(all).Where(method => method.DeclaringType == type))
        {
            CollectionAssert.DoesNotContain(forbidden, method.ReturnType, method.Name);
            Assert.IsFalse(method.GetParameters().Any(parameter => forbidden.Contains(parameter.ParameterType)), method.Name);
            Assert.IsFalse(method.Name.Contains("Confirm", StringComparison.Ordinal) && !method.Name.StartsWith("get_", StringComparison.Ordinal),
                $"{method.Name}: no confirm method");
        }
    }

    // ---- History: every client outcome message renders through the existing history presenters ------------------------

    [TestMethod]
    public void AnUncertainClientRecordShowsTheRunFaultRecoveryTextAndKeepsEveryWarning()
    {
        // ADR-031 design §8.3: on the unsettled path the workflow appends the fault's fixed recovery message; history shows it as
        // the record's fixed detail beside the Uncertain status (the untrusted-folder advice and the release note are kept).
        ReviewedUpdateOperation client = ClientOperation("2.7.5", "2.8.0");
        int shown = 0;
        foreach (ReviewedClientWatchdogStop stop in Enum.GetValues<ReviewedClientWatchdogStop>())
            foreach (bool release in new[] { false, true })
            {
                ReviewedClientRunFault fault = ReviewedClientRunFault.FromAcceptedHelperReply(stop, release);
                string? expected = ChocolateyClientUpdatePresentation.RunFaultMessage(fault);
                ReviewedUpdateDispatchObservation observation = new(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Failed, null,
                    "settlement-unconfirmed", Task.CompletedTask, false, false, false, false, true, ClientRunFault: fault);
                ReviewedUpdateHistoryItemViewModel item = Item(client, UpdateDispatchState.Uncertain, UpdateDispatchOutcome.Uncertain,
                    ReviewedUpdateDispatchWorkflow.UnsettledMessage(client, observation));
                StringAssert.StartsWith(item.Status, "Uncertain:");
                if (expected is null)
                {
                    // None on a started launch: the lost-track caution (independent review M1).
                    Assert.AreEqual(ChocolateyClientUpdatePresentation.LostTrackCautionMessage, item.VerificationDetail);
                    continue;
                }
                shown++;
                Assert.AreEqual(expected, item.VerificationDetail, $"{stop} {release}");
                // Independent review L1: recorded as a fixed leading marker from the fault value; the visible diagnostic omits it.
                string marker = ChocolateyClientUpdatePresentation.RunFaultMarker(fault)!;
                StringAssert.StartsWith(ReviewedUpdateDispatchWorkflow.UnsettledMessage(client, observation), marker + " Owned work");
                Assert.IsFalse(item.Diagnostic.Contains("client-run-fault", StringComparison.Ordinal), item.Diagnostic);
                StringAssert.StartsWith(item.Diagnostic, "Owned work or invocation is unresolved;");
                Assert.IsTrue(item.Fields.Any(field => field.Label == "Verification" && field.Text == expected));
                if (stop is ReviewedClientWatchdogStop.SetupOverrideDuringRun or ReviewedClientWatchdogStop.RootWritableDuringRun)
                    StringAssert.Contains(item.VerificationDetail, ChocolateyClientUpdatePresentation.UntrustedFolderRecoveryStep);
                if (release && stop != ReviewedClientWatchdogStop.None) StringAssert.EndsWith(item.VerificationDetail, ChocolateyClientUpdatePresentation.ReleaseIncompleteAlsoNote);
                if (release && stop == ReviewedClientWatchdogStop.None) Assert.AreEqual(ChocolateyClientUpdatePresentation.NotVerifiedReleaseIncompleteMessage, item.VerificationDetail);
            }
        Assert.AreEqual(7, shown);

        // Only the workflow's leading marker is recognised: message text (at the end or anywhere), another route or outcome, a
        // marker not at the start, or a longer token, shows nothing.
        string root = ChocolateyClientUpdatePresentation.StoppedRootWritableMessage;
        string rootMarker = ChocolateyClientUpdatePresentation.RunFaultMarker(
            ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.RootWritableDuringRun, false))!;
        ReviewedUpdateOperation package = PackageOperation();
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, Terminal(UpdateDispatchOutcome.Uncertain, "x " + root)), "never the ending");
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, Terminal(UpdateDispatchOutcome.Uncertain, root)));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, Terminal(UpdateDispatchOutcome.Uncertain, "x " + rootMarker + " y")));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, Terminal(UpdateDispatchOutcome.Uncertain, rootMarker + "-x y")));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(package, Terminal(UpdateDispatchOutcome.Uncertain, rootMarker + " x")));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, Terminal(UpdateDispatchOutcome.Failed, rootMarker + " x")));
        Assert.AreEqual(root, ReviewedUpdateHistoryPresentation.UncertainDetail(client, Terminal(UpdateDispatchOutcome.Uncertain, rootMarker + " x")));
        // After the workflow's other leading markers (helper-check prefixed last, then lingering), the client marker still leads.
        string led = ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic + " " + ReviewedUpdateLingeringProcesses.Token + " " + rootMarker + " x " + root;
        Assert.AreEqual(root, ReviewedUpdateHistoryPresentation.UncertainDetail(client, Terminal(UpdateDispatchOutcome.Uncertain, led)));
        ReviewedUpdateHistoryItemViewModel ledItem = Item(client, UpdateDispatchState.Uncertain, UpdateDispatchOutcome.Uncertain, led);
        Assert.AreEqual(root, ledItem.VerificationDetail);
        Assert.AreEqual(ReviewedUpdateLingeringProcesses.HistoryNote, ledItem.LingeringProcessesNote);
        Assert.AreEqual(ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic + " x " + root, ledItem.Diagnostic, "both recognised markers are stripped");
    }

    [TestMethod]
    public void AHelperDiagnosticEndingWithAFixedMessageIsNeverShownAsAFault()
    {
        // Independent review L1 (FR-HIS-01/03): a NotStarted launch with run fault None leaves an unsettled client record without a
        // marker; its helper diagnostic ending with a watchdog message must not show the watchdog detail.
        ReviewedUpdateOperation client = ClientOperation("2.7.5", "2.8.0");
        string root = ChocolateyClientUpdatePresentation.StoppedRootWritableMessage;
        ReviewedClientRunFault none = ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.None, false);
        ReviewedUpdateDispatchObservation notStarted = new(ReviewedUpdateInvocation.NotStarted, ReviewedUpdateProviderStatus.Failed, null,
            "helper said: " + root, Task.CompletedTask, false, false, false, false, true, ClientRunFault: none);
        string message = ReviewedUpdateDispatchWorkflow.UnsettledMessage(client, notStarted);
        StringAssert.EndsWith(message, " " + root, "the spoofed text is at the end");
        StringAssert.StartsWith(message, "Owned work", "no marker for a not-started launch with no fault");
        ReviewedUpdateHistoryItemViewModel item = Item(client, UpdateDispatchState.Uncertain, UpdateDispatchOutcome.Uncertain, message);
        Assert.IsNull(item.VerificationDetail);
        Assert.IsFalse(item.Fields.Any(field => field.Label == "Verification"));

        // A launch that may have started with no fault shows the lost-track caution (marker-based), never the spoofed watchdog text.
        foreach (ReviewedUpdateInvocation invocation in new[] { ReviewedUpdateInvocation.Started, ReviewedUpdateInvocation.Unknown })
        {
            string lost = ReviewedUpdateDispatchWorkflow.UnsettledMessage(client, notStarted with { Invocation = invocation });
            StringAssert.StartsWith(lost, ChocolateyClientUpdatePresentation.LostTrackMarker + " Owned work");
            ReviewedUpdateHistoryItemViewModel lostItem = Item(client, UpdateDispatchState.Uncertain, UpdateDispatchOutcome.Uncertain, lost);
            Assert.AreEqual(ChocolateyClientUpdatePresentation.LostTrackCautionMessage, lostItem.VerificationDetail);
            Assert.IsFalse(lostItem.Diagnostic.Contains(ChocolateyClientUpdatePresentation.LostTrackMarker, StringComparison.Ordinal));
        }
        // The caution's text alone, at the end of a helper diagnostic, is not recognised either.
        ReviewedUpdateDispatchObservation spoofedCaution = notStarted with { Diagnostic = "x " + ChocolateyClientUpdatePresentation.LostTrackCautionMessage };
        Assert.IsNull(Item(client, UpdateDispatchState.Uncertain, UpdateDispatchOutcome.Uncertain,
            ReviewedUpdateDispatchWorkflow.UnsettledMessage(client, spoofedCaution)).VerificationDetail);
    }

    [TestMethod]
    public void EverySettledClientOutcomeMessageRendersInTheHistoryItem()
    {
        ReviewedUpdateOperation client = ClientOperation("2.7.5", "2.8.0");
        string[] unverified =
        [
            ChocolateyClientUpdatePresentation.NotVerifiedLeftoverMessage, ChocolateyClientUpdatePresentation.NotVerifiedLeftoverNotSourceMessage,
            ChocolateyClientUpdatePresentation.NotVerifiedReleaseIncompleteMessage, ChocolateyClientUpdatePresentation.NotVerifiedReleaseMessage,
            ChocolateyClientUpdatePresentation.NotVerifiedUnreadableMessage, ChocolateyClientUpdatePresentation.StoppedSetupOverrideMessage,
            ChocolateyClientUpdatePresentation.StoppedRootWritableMessage, ChocolateyClientUpdatePresentation.StoppedWatchFailedMessage,
            ChocolateyClientUpdatePresentation.StoppedSetupOverrideReleaseIncompleteMessage, ChocolateyClientUpdatePresentation.StoppedRootWritableReleaseIncompleteMessage,
            ChocolateyClientUpdatePresentation.StoppedWatchFailedReleaseIncompleteMessage, ChocolateyClientUpdatePresentation.RunFaultNotReportedMessage,
        ];
        foreach (string message in unverified)
        {
            ReviewedUpdateHistoryItemViewModel item = Item(client, UpdateDispatchState.Final, UpdateDispatchOutcome.Unverified, message);
            Assert.AreEqual(message, item.VerificationDetail);
            Assert.AreEqual(ChocolateyClientUpdatePresentation.HistoryRouteText, item.Route);
            Assert.IsTrue(item.Fields.Any(field => field.Label == "Verification" && field.Text == message), message);
            Assert.IsTrue(item.Fields.Any(field => field.Label == "Route" && field.Text == ChocolateyClientUpdatePresentation.HistoryRouteText));
        }
        ReviewedUpdateHistoryItemViewModel verified = Item(client, UpdateDispatchState.Final, UpdateDispatchOutcome.Success,
            ReviewedUpdateDispatchWorkflow.TargetVerifiedWithLeftoverMessage);
        StringAssert.EndsWith(verified.VerificationDetail, ChocolateyClientUpdatePresentation.VerifiedLeftoverNote);
    }

    private static void AssertConfirmDisabled(ChocolateyClientUpdateViewModel model)
    {
        Assert.IsTrue(model.IsUnavailable);
        Assert.AreEqual(ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview, model.UnavailableReasons[0], "not admitted is always first");
        Assert.IsFalse(model.CanConfirm);
        Assert.IsFalse(model.IsConfirmEnabled);
        Assert.AreEqual(model.CanConfirm, model.IsConfirmEnabled);
        Assert.AreEqual("Update Chocolatey is unavailable: " + ChocolateyClientUpdateViewModel.NotAdmittedText, model.ConfirmAutomationName);
    }

    private static ReviewedUpdateHistoryItemViewModel Item(ReviewedUpdateOperation operation, UpdateDispatchState state, UpdateDispatchOutcome outcome, string diagnostic)
    {
        // As FinalizeAsync records it: sanitized and bounded.
        string recorded = SanitizedDiagnosticBundle.Truncate(SanitizedDiagnosticBundle.Sanitize(diagnostic), 4000);
        return ReviewedUpdateHistoryItemViewModel.From(ReviewedUpdateHistoryPresentation.Present(new ReviewedUpdateHistoryRecord(operation.JobId, operation, state,
            operation.ReviewedAt, Terminal(outcome, recorded), true)));
    }

    private static ReviewedUpdateTerminalRecord Terminal(UpdateDispatchOutcome outcome, string diagnostic) =>
        new(outcome, new DateTimeOffset(2026, 11, 2, 12, 5, 0, TimeSpan.Zero), null, UpdateDispatchSettlementState.OwnedWorkStopped, diagnostic);

    private static InventoryRow Row(string provider, string identity, string? version) =>
        new(Guid.NewGuid(), identity, "Unknown", provider, "Machine", version ?? "Unknown", "Installed", "Application",
            [new InstallationInstance(Guid.NewGuid(), Guid.NewGuid(), provider, identity, version, "Machine", "Neutral")]);

    private static ReviewedUpdateOperation ClientOperation(string installed, string candidate) =>
        Build("chocolatey", installed, candidate, UpdateRouteContract.ChocolateyClientSelfUpdateV1);

    private static ReviewedUpdateOperation PackageOperation() => Build("git", "1.0.0", "2.0.0", UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);

    private static ReviewedUpdateOperation Build(string packageId, string installed, string candidate, UpdateRouteContract route)
    {
        DateTimeOffset at = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("73000000-0000-0000-0000-000000000011"),
            Guid.Parse("73000000-0000-0000-0000-000000000012"), new UpdateJobSelection("choco:" + packageId,
                UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, packageId, installed, candidate, UpdateScope.Machine,
                UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "choco:" + packageId, "machine:choco",
            ReviewedUpdateOperation.ProfileEvidencePrefix + Convert.ToHexString(SHA256.HashData("client profile"u8)), at, route,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }
}
