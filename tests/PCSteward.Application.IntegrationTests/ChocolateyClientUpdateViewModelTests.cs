using System.Reflection;
using System.Security.Cryptography;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-HIS-01, FR-UI-03/04, TM-03/04/07/13; ADR-031 (owner decisions 1–5), ADR-030; design mapping
/// §3.3, §3.7 and §8: the portable "Update Chocolatey to X" view model the Desktop binds. Every property is checked; the offer
/// follows the ADR-031 rule over the compiled list (never a downgrade, never from an unlisted release); the not-admitted reason
/// is always present, so Confirm is never enabled and the view model has no confirm path. History shows every client outcome
/// message, including a run fault's recovery text on an Uncertain record.
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdateViewModelTests
{
    private const string Signer = "Chocolatey Software, Inc";
    private static readonly ChocolateyClientReleaseCatalogFacts Compiled = new(["2.7.5"], Signer);
    private static readonly string[] FactLabels =
    [
        "Installed", "Offer", "Reviewed releases", "Signer", "What happens", "Verification", "Administrator permission",
        "Safety checks while it runs", "If the folder may have been changed",
    ];
    private static readonly ChocolateyClientReleaseCatalogFacts Fixture = new(["2.7.5", "2.8.0", "2.9.1"], Signer);

    [TestMethod]
    public void TodaysListShowsTheInstalledReleaseNoNewerReleaseEveryFactAndADisabledConfirm()
    {
        ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.Open(Compiled, "2.7.5", () => ReviewedRouteSplitTokenState.SplitTokenLimited);

        Assert.AreEqual("Update Chocolatey", model.Title);
        Assert.AreEqual("2.7.5", model.InstalledVersion);
        StringAssert.StartsWith(model.InstalledText, "Chocolatey 2.7.5, as Chocolatey's package list reported at the last scan.");
        Assert.IsNull(model.OfferVersion);
        Assert.AreEqual("No newer reviewed Chocolatey release is available.", model.OfferText);
        // Independent review L2: the version is qualified as the scan's report (Settings card and Installed apps "Status" row).
        Assert.AreEqual("Chocolatey 2.7.5 (reported by the last scan) · No newer reviewed release", model.Summary);
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
            model.InstalledText, model.OfferText, model.ReviewedReleasesText, model.SignerText, ChocolateyClientUpdatePresentation.WhatHappensText,
            ChocolateyClientUpdatePresentation.VerificationText, ChocolateyClientUpdateViewModel.PermissionText,
            ChocolateyClientUpdateViewModel.WatchdogText, ChocolateyClientUpdateViewModel.UntrustedFolderCautionText,
        }, model.Fields.Select(field => field.Text).ToArray());
        StringAssert.Contains(ChocolateyClientUpdatePresentation.VerificationText, "checks every Chocolatey client file");
        StringAssert.StartsWith(ChocolateyClientUpdateViewModel.PermissionText, "Windows will ask for permission once");
        StringAssert.Contains(ChocolateyClientUpdateViewModel.WatchdogText, "stops the update immediately");
        StringAssert.EndsWith(ChocolateyClientUpdateViewModel.UntrustedFolderCautionText, ChocolateyClientUpdatePresentation.UntrustedFolderRecoveryStep);
        CollectionAssert.AreEqual(ChocolateyClientUpdatePresentation.Disclosures.ToArray(), model.DisclosureFields.Select(field => field.Text).ToArray());
        Assert.IsTrue(model.DisclosureFields.All(field => field.Label == "Chocolatey's setup"));
        foreach (ReviewedDisplayField field in model.Fields.Concat(model.DisclosureFields).Concat(model.UnavailableFields))
        {
            Assert.AreEqual(field.Label + ": " + field.Text, field.AutomationName);
            // The pre-review copy never advises an elevated choco command.
            Assert.IsFalse(field.Text.Contains("choco upgrade", StringComparison.Ordinal) || field.Text.Contains("--force", StringComparison.Ordinal), field.Text);
        }

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
    public void AFixtureListWithNewerReleasesOffersOnlyTheNewestAndStaysUnconfirmable()
    {
        foreach (string installed in new[] { "2.7.5", "2.8.0" })
        {
            ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.Open(Fixture, installed, () => ReviewedRouteSplitTokenState.SplitTokenLimited);
            Assert.AreEqual("2.9.1", model.OfferVersion, "the newest listed release, never an intermediate one");
            // Independent review L3: until admission the offer comes from unverified scan data, so it is never asserted as an update.
            Assert.AreEqual("A newer reviewed Chocolatey release may be available: 2.9.1", model.Title);
            Assert.IsFalse(model.Title.StartsWith("Update Chocolatey to", StringComparison.Ordinal));
            Assert.AreEqual($"A newer reviewed Chocolatey release may be available: 2.9.1. The last scan reported Chocolatey {installed}; 2.9.1 is the " +
                "newest release on PC Steward's reviewed list, and PC Steward checks the installed Chocolatey's files before any update.", model.OfferText);
            Assert.AreEqual($"Chocolatey {installed} (reported by the last scan) · 2.9.1 may be available; updating is not available yet", model.Summary);
            Assert.AreEqual("2.7.5, 2.8.0, 2.9.1", model.ReviewedReleasesText);
            // Even with an offer and a split token, the route is not admitted.
            CollectionAssert.AreEqual(new[] { ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview }, model.UnavailableReasons.ToArray());
            AssertConfirmDisabled(model);
        }

        ChocolateyClientUpdateViewModel newest = ChocolateyClientUpdateViewModel.Open(Fixture, "2.9.1", () => ReviewedRouteSplitTokenState.SplitTokenLimited);
        Assert.IsNull(newest.OfferVersion, "never an equal release");
        Assert.AreEqual(ChocolateyClientUpdateViewModel.NoNewerReleaseText, newest.OfferText, "listed, nothing newer listed");
        Assert.AreEqual("Chocolatey 2.9.1 (reported by the last scan) · No newer reviewed release", newest.Summary);
        CollectionAssert.Contains(newest.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.NoNewerReviewedRelease);
        AssertConfirmDisabled(newest);

        Assert.AreEqual("2.9.1", ChocolateyClientUpdateViewModel.OfferTarget("2.7.5", Fixture.ListedVersions));
        Assert.IsNull(ChocolateyClientUpdateViewModel.OfferTarget("2.9.1", Fixture.ListedVersions));
        Assert.IsNull(ChocolateyClientUpdateViewModel.OfferTarget(null, Fixture.ListedVersions));
        Assert.IsNull(ChocolateyClientUpdateViewModel.OfferTarget("2.7.5", []));
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
    public void AnUnknownMalformedOrUnlistedInstalledReleaseIsOfferedNothing(string? installed)
    {
        ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.Open(Fixture, installed, () => ReviewedRouteSplitTokenState.SplitTokenLimited);
        Assert.IsNull(model.OfferVersion, "an unlisted installed client is never offered an update, and a newer one is never downgraded");
        Assert.AreEqual("Update Chocolatey", model.Title);
        CollectionAssert.AreEqual(new[]
        {
            ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview, ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotReviewed,
        }, model.UnavailableReasons.ToArray());
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledReleaseNotReviewedText, model.UnavailableFields[1].Text);
        bool wellFormed = installed is "2.4.0" or "3.0.0";
        Assert.AreEqual(wellFormed ? installed : null, model.InstalledVersion);
        Assert.AreEqual(installed is not null, model.InstalledReported);
        // Independent review M1: the offer text names the actual reason, never "no newer release" for an unlisted or unknown one.
        Assert.AreNotEqual(ChocolateyClientUpdateViewModel.NoNewerReleaseText, model.OfferText);
        if (wellFormed)
        {
            Assert.AreEqual("No update offered: the installed release is not on the reviewed list.", model.OfferText);
            Assert.AreEqual($"Chocolatey {installed} (reported by the last scan) · Not on the reviewed list", model.Summary);
        }
        else
        {
            Assert.AreEqual("No update offered: the installed release is unknown.", model.OfferText);
            Assert.AreEqual("Chocolatey (release unknown) · No update offered", model.Summary);
            // Independent review L4: no report at all is distinguished from a reported version that could not be read.
            Assert.AreEqual(installed is null
                ? "Unknown: no scan has reported a Chocolatey version."
                : "Unknown: the last scan reported a Chocolatey version that could not be read.", model.InstalledText);
        }
        AssertConfirmDisabled(model);
    }

    [TestMethod]
    public void AnOlderUnlistedInstalledReleaseSaysItIsNotOnTheReviewedList()
    {
        // Independent review M1: 2.4.0 is older than every listed release (fixture list through the test-only catalog seam), yet it is
        // not offered anything, and the text says why: it is not on the reviewed list (never "no newer release is available").
        ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.Open(Fixture, "2.4.0", () => ReviewedRouteSplitTokenState.SplitTokenLimited);
        Assert.IsNull(model.OfferVersion);
        Assert.IsNull(ChocolateyClientUpdateViewModel.OfferTarget("2.4.0", Fixture.ListedVersions));
        Assert.AreEqual("2.4.0", model.InstalledVersion);
        Assert.AreEqual("Update Chocolatey", model.Title);
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledNotListedOfferText, model.OfferText);
        Assert.AreEqual("Chocolatey 2.4.0 (reported by the last scan) · Not on the reviewed list", model.Summary);
        Assert.AreEqual("2.7.5, 2.8.0, 2.9.1", model.ReviewedReleasesText);
        Assert.AreEqual(model.OfferText, model.Fields.Single(field => field.Label == "Offer").Text);
        CollectionAssert.Contains(model.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotReviewed);
        CollectionAssert.DoesNotContain(model.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.NoNewerReviewedRelease);
        AssertConfirmDisabled(model);
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
            ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.Open(catalog, "2.7.5", () => ReviewedRouteSplitTokenState.SplitTokenLimited);
            Assert.AreEqual(0, model.ListedVersions.Count);
            // Independent review L4: a list that fails validation is unavailable, never "None".
            Assert.IsFalse(model.CatalogAvailable);
            Assert.AreEqual("Unavailable", model.ReviewedReleasesText);
            Assert.AreEqual("Reviewed releases: Unavailable", model.Fields.Single(field => field.Label == "Reviewed releases").AutomationName);
            Assert.AreEqual(ChocolateyClientUpdateViewModel.CatalogUnavailableOfferText, model.OfferText);
            Assert.AreEqual("Chocolatey 2.7.5 (reported by the last scan) · Reviewed releases unavailable", model.Summary);
            Assert.IsNull(model.OfferVersion);
            CollectionAssert.Contains(model.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotReviewed);
            AssertConfirmDisabled(model);
        }
        foreach (string? signer in new[] { null, "", " ", new string('x', 129) })
            Assert.AreEqual("Unknown", ChocolateyClientUpdateViewModel.Open(Compiled with { Signer = signer! }, "2.7.5", () => ReviewedRouteSplitTokenState.SplitTokenLimited).Signer);
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyClientUpdateViewModel.Open(null!, "2.7.5", () => ReviewedRouteSplitTokenState.SplitTokenLimited));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyClientUpdateViewModel.Open(Compiled, "2.7.5", null!));
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
            ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.Open(facts, "2.7.5", () => ReviewedRouteSplitTokenState.SplitTokenLimited);
            Assert.IsFalse(model.CatalogAvailable);
            Assert.AreEqual(ChocolateyClientUpdateViewModel.ReviewedReleasesUnavailableText, model.ReviewedReleasesText);
            Assert.AreEqual(ChocolateyClientUpdateViewModel.CatalogUnavailableOfferText, model.OfferText);
            Assert.AreEqual("Unknown", model.Signer);
            Assert.IsNull(model.OfferVersion);
            AssertConfirmDisabled(model);
        }
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyClientReleaseCatalogFacts.ReadOrUnavailable(null!));

        // A list that faults while it is read is unavailable too (the view never throws on display data).
        ChocolateyClientUpdateViewModel throwing = ChocolateyClientUpdateViewModel.Open(new ChocolateyClientReleaseCatalogFacts(new ThrowingList(), Signer), "2.7.5",
            () => ReviewedRouteSplitTokenState.SplitTokenLimited);
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
            ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.Open(catalog, "2.7.5", () => state);
            Assert.AreEqual(reason, model.UnavailableReasons[^1]);
            Assert.AreEqual(ChocolateyClientUpdateViewModel.Text(reason), model.UnavailableFields[^1].Text);
            Assert.AreEqual(Enum.IsDefined(state) ? state : ReviewedRouteSplitTokenState.Unknown, model.SplitTokenState);
            AssertConfirmDisabled(model);
        }
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.SplitTokenRequiredText, ChocolateyClientUpdateViewModel.Text(ChocolateyClientUpdateUnavailableReason.SplitTokenRequired));
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.DesktopTokenUnsupportedText, ChocolateyClientUpdateViewModel.Text(ChocolateyClientUpdateUnavailableReason.DesktopTokenUnsupported));
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.DesktopTokenUnknownText, ChocolateyClientUpdateViewModel.Text(ChocolateyClientUpdateUnavailableReason.DesktopTokenUnknown));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ChocolateyClientUpdateViewModel.Text((ChocolateyClientUpdateUnavailableReason)99));
    }

    [TestMethod]
    public void TheSplitTokenIsProbedAtEveryOpenAndAProbeFaultIsUnknown()
    {
        int probes = 0;
        ReviewedRouteSplitTokenState Probe() { probes++; return probes == 1 ? ReviewedRouteSplitTokenState.SplitTokenLimited : ReviewedRouteSplitTokenState.NoSplitToken; }
        ChocolateyClientUpdateViewModel first = ChocolateyClientUpdateViewModel.Open(Compiled, "2.7.5", Probe);
        ChocolateyClientUpdateViewModel second = ChocolateyClientUpdateViewModel.Open(Compiled, "2.7.5", Probe);
        Assert.AreEqual(2, probes, "one probe per open, never cached");
        CollectionAssert.DoesNotContain(first.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.SplitTokenRequired);
        CollectionAssert.Contains(second.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.SplitTokenRequired);
        ChocolateyClientUpdateViewModel faulted = ChocolateyClientUpdateViewModel.Open(Compiled, "2.7.5", () => throw new InvalidOperationException("token unreadable"));
        Assert.AreEqual(ReviewedRouteSplitTokenState.Unknown, faulted.SplitTokenState);
        CollectionAssert.Contains(faulted.UnavailableReasons.ToArray(), ChocolateyClientUpdateUnavailableReason.DesktopTokenUnknown);
        AssertConfirmDisabled(faulted);
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
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledUnreadableText, ChocolateyClientUpdateViewModel.Open(Compiled,
            ChocolateyClientUpdateViewModel.ReportedInstalledVersion([Row("chocolatey", "chocolatey", "2.7.5-beta")]), () => ReviewedRouteSplitTokenState.SplitTokenLimited).InstalledText);
        Assert.AreEqual(ChocolateyClientUpdateViewModel.InstalledUnknownText, ChocolateyClientUpdateViewModel.Open(Compiled,
            ChocolateyClientUpdateViewModel.ReportedInstalledVersion([git]), () => ReviewedRouteSplitTokenState.SplitTokenLimited).InstalledText);
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyClientUpdateViewModel.ReportedInstalledVersion(null!));

        Assert.IsTrue(ChocolateyClientUpdateViewModel.IsClientRow(client));
        Assert.IsFalse(ChocolateyClientUpdateViewModel.IsClientRow(git));
        Assert.IsFalse(ChocolateyClientUpdateViewModel.IsClientRow(wingetNamedChocolatey));
        Assert.IsFalse(ChocolateyClientUpdateViewModel.IsClientRow(Row("chocolatey", "chocolatey.extension", "2.7.5")));
        Assert.IsFalse(ChocolateyClientUpdateViewModel.IsClientRow(null));
    }

    [TestMethod]
    public void TheRouteStaysNonAdmittedAndTheViewModelHasNoConfirmPath()
    {
        Assert.IsFalse(ReviewedUpdateReview.HasSupportedMode(ClientOperation("2.7.5", "2.8.0")), "the review gate refuses the client route");
        ChocolateyClientUpdateReviewView sheet = ChocolateyClientUpdatePresentation.Review(ClientOperation("2.7.5", "2.8.0"))!;
        Assert.AreEqual(ChocolateyClientUpdatePresentation.UnavailableText, sheet.UnavailableReason);

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
                Assert.AreEqual(expected, ChocolateyClientUpdatePresentation.TrailingRunFaultMessage(" " + expected));
                Assert.IsTrue(item.Fields.Any(field => field.Label == "Verification" && field.Text == expected));
                if (stop is ReviewedClientWatchdogStop.SetupOverrideDuringRun or ReviewedClientWatchdogStop.RootWritableDuringRun)
                    StringAssert.Contains(item.VerificationDetail, ChocolateyClientUpdatePresentation.UntrustedFolderRecoveryStep);
                if (release && stop != ReviewedClientWatchdogStop.None) StringAssert.EndsWith(item.VerificationDetail, ChocolateyClientUpdatePresentation.ReleaseIncompleteAlsoNote);
                if (release && stop == ReviewedClientWatchdogStop.None) Assert.AreEqual(ChocolateyClientUpdatePresentation.NotVerifiedReleaseIncompleteMessage, item.VerificationDetail);
            }
        Assert.AreEqual(7, shown);

        // Another route or outcome, or the text not at the end, shows nothing.
        string root = ChocolateyClientUpdatePresentation.StoppedRootWritableMessage;
        ReviewedUpdateOperation package = PackageOperation();
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(package, Terminal(UpdateDispatchOutcome.Uncertain, "x " + root)));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, Terminal(UpdateDispatchOutcome.Uncertain, root + " x")));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, Terminal(UpdateDispatchOutcome.Uncertain, root)), "only an appended message");
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, Terminal(UpdateDispatchOutcome.Failed, "x " + root)));
        Assert.IsNull(ChocolateyClientUpdatePresentation.TrailingRunFaultMessage(null));
        Assert.IsNull(ChocolateyClientUpdatePresentation.TrailingRunFaultMessage(" " + ChocolateyClientUpdatePresentation.LostTrackCautionMessage));
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
