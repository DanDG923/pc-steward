using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// UI review 2026-11-13, items 9 and 10 (FR-UPD-01, FR-UI-01/04, SPEC §7.3): the Updates page lists an app only when an update is
/// on offer, asks the owner only what they can answer ("Check the match"), and gives everything else one reason in words ("Can't
/// update yet"). Presentation only: Ready is still exactly the domain's verified status, and nothing here makes a row updatable.
/// </summary>
[TestClass]
public sealed class UiReviewUpdatesPresentationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly string[] ReadyKeys = ["ready"];
    private static readonly string[] MatchKeys = ["match"];
    private static readonly string[] TiedKeys = ["tied", "odd"];
    private static readonly string[] CantKeys = ["terminal", "wave", "unreachable", "stale"];
    private static readonly string[] GroupReasons = [CantUpdateReasons.HowInstalledUnknown, CantUpdateReasons.CouldNotCheck, CantUpdateReasons.NeedsFreshCheck];

    [TestMethod]
    [Description("Item 10: a confirmable match with an update on offer is a question; other rows get one reason; an app with nothing on offer is not listed.")]
    public void NeedsReviewSplitsIntoAQuestionAndReasons()
    {
        InventoryRow[] rows =
        [
            Row("ready", "Discord", "VerifiedUpdate", "1.0.9267"),
            Row("match", "Elgato Wave Link", "ReviewRequired", "3.3.0"),
            Row("terminal", "Windows Terminal", "ReviewRequired", "1.25.2733.0"),
            Row("wave", "Wave Link Helper", "ReviewRequired", "1.0"),
            Row("unreachable", "Paint.NET", "FailedUnavailable", "5.0"),
            Row("stale", "Notepad++", "ReviewRequired", "8.6"),
            Row("copilot", "Copilot", "ReviewRequired", "157.0"),
            Row("same", "7-Zip", "ReviewRequired", "24.08")
        ];
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal)
        {
            ["ready"] = Detail("ready", "1.0.9267", "1.0.9268", "Discord.Discord"),
            ["match"] = Detail("match", "3.3.0", "3.4.0", "Elgato.WaveLink", confidence: "Low", canConfirm: true),
            ["terminal"] = Detail("terminal", "1.25.2733.0", "1.25.3131.0", "Microsoft.WindowsTerminal", reasons: ["MissingArchitectureEvidence", "MissingScopeEvidence"]),
            ["wave"] = Detail("wave", "1.0", "2.0", "Elgato.Helper", reasons: ["MissingScopeEvidence"]),
            ["unreachable"] = Detail("unreachable", "5.0", null, "dotPDN.PaintDotNet"),
            ["stale"] = Detail("stale", "8.6", "8.7", "Notepad++.Notepad++", needsFreshCheck: true),
            ["copilot"] = Detail("copilot", "157.0", null, null, confidence: "None"),
            ["same"] = Detail("same", "24.08", "24.08", "7zip.7zip", reasons: ["CandidateIsCurrent"], canConfirm: true)
        };

        UpdatesPagePresentation page = UpdatesPagePresenter.Build(rows, details, true, [], ["ready", "match", "terminal"], Now);

        CollectionAssert.AreEqual(ReadyKeys, page.Ready.Select(row => row.StableProductKey).ToArray(), "Ready is exactly the verified status");
        CollectionAssert.AreEqual(ReadyKeys, page.SelectedKeys.ToArray(), "only a ready row can be selected, whatever was asked for");
        CollectionAssert.AreEqual(MatchKeys, page.CheckTheMatch.Select(row => row.StableProductKey).ToArray());
        CollectionAssert.AreEqual(CantKeys, page.CantUpdateYet.Select(row => row.StableProductKey).ToArray());
        Assert.AreEqual(2, page.NothingOnOfferCount, "no update on offer (Copilot) and nothing newer on offer (7-Zip) stay on Installed apps");
        Assert.HasCount(7, page.NeedsReview, "nothing is dropped from the evidence: the page only chooses what to list");

        UpdateRowPresentation match = page.CheckTheMatch.Single();
        Assert.AreEqual("WinGet has 3.4.0, listed as Elgato.WaveLink. Is that this app?", match.Question);
        Assert.AreEqual(match.Question, match.Summary, "the row says the question, not the reasons");
        Assert.IsFalse(match.HasUpdateControl, "answering the question is a decision; it is never an update control");
        Assert.IsFalse(match.CanSelect);
        foreach (UpdateRowPresentation row in page.CantUpdateYet)
        {
            Assert.IsFalse(row.HasUpdateControl, row.StableProductKey);
            Assert.AreEqual(string.Empty, row.Question, row.StableProductKey);
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.CantUpdateReason), row.StableProductKey);
        }

        IReadOnlyList<CantUpdateGroup> groups = page.CantUpdateGroups;
        CollectionAssert.AreEqual(GroupReasons, groups.Select(group => group.Reason).ToArray(), "apps that share a cause share a line");
        Assert.AreEqual("Windows Terminal, Wave Link Helper", groups[0].Names);
        Assert.IsFalse(groups[0].CanCheckAgain, "checking again can't tell PC Steward how an app was installed");
        Assert.IsTrue(groups[1].CanCheckAgain, "an unreachable source is worth another check");
        Assert.IsTrue(groups[2].CanCheckAgain);

        SourceSnapshot live = new("winget", "1.9", Now.AddMinutes(-8), SourceSnapshotState.Live, [], []);
        UpdatesPageHeader header = UpdatesPagePresenter.Header(page, true, [live], Now, CultureInfo.InvariantCulture, TimeZoneInfo.Utc);
        Assert.AreEqual("1 ready · 1 match to check · 4 can't update yet · checked 8 minutes ago", header.Description);
    }

    [TestMethod]
    [Description("Item 10: with nothing on offer the page is empty even when apps have possible matches, and it says how many there are.")]
    public void AnAppWithNothingOnOfferIsNotAnUpdatesRow()
    {
        InventoryRow[] rows = [Row("copilot", "Copilot", "ReviewRequired", "157.0")];
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal) { ["copilot"] = Detail("copilot", "157.0", null, null, confidence: "None") };
        UpdatesPagePresentation page = UpdatesPagePresenter.Build(rows, details, true, [], null, Now);
        Assert.IsFalse(page.HasAnyRow);
        Assert.AreEqual(1, page.NothingOnOfferCount);
        Assert.IsEmpty(page.CheckTheMatch);
        Assert.IsEmpty(page.CantUpdateYet);

        // With update checks off nothing is a question: the answer couldn't be used.
        UpdatesPagePresentation off = UpdatesPagePresenter.Build([Row("match", "Elgato Wave Link", "ReviewRequired", "3.3.0")],
            new Dictionary<string, UpdateDetail>(StringComparer.Ordinal) { ["match"] = Detail("match", "3.3.0", "3.4.0", "Elgato.WaveLink", canConfirm: true) }, false, [], null, Now);
        Assert.IsEmpty(off.CheckTheMatch);
        Assert.AreEqual(1, off.CantUpdateYet.Count);
    }

    [TestMethod]
    [Description("Item 10 (owner decision 2026-11-13: ask only when two packages tie): tied matches with a newer version stay on Updates as a question; without one they are not listed.")]
    public void TiedMatchesWithANewerVersionAreAQuestionNotHidden()
    {
        // The matcher records no single candidate for a tie, so the row has no new version of its own; the choices carry theirs.
        InventoryRow[] rows = [Row("tied", "Widget", "ReviewRequired", "1.0"), Row("same", "Gadget", "ReviewRequired", "2.0"), Row("odd", "Gizmo", "ReviewRequired", "2026-R1")];
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal)
        {
            ["tied"] = Tied("tied", "1.0", "1.1", "1.0"),
            ["same"] = Tied("same", "2.0", "2.0", "1.9"),
            ["odd"] = Tied("odd", "2026-R1", "spring", null)
        };
        UpdatesPagePresentation page = UpdatesPagePresenter.Build(rows, details, true, [], ["tied"], Now);
        CollectionAssert.AreEqual(TiedKeys, page.CheckTheMatch.Select(row => row.StableProductKey).ToArray(), "a version that can't be compared is not assumed to be nothing");
        Assert.AreEqual(1, page.NothingOnOfferCount, "choices that are the installed version or older are not an update on offer");
        UpdateRowPresentation tied = page.CheckTheMatch[0];
        Assert.IsTrue(tied.AsksToChoose);
        Assert.IsFalse(tied.AsksAboutMatch, "there is no single match to say yes to");
        Assert.AreEqual("PC Steward found 2 possible matches, and a newer version is on offer. Which one is this app?", tied.Question);
        Assert.AreEqual(tied.Question, tied.Summary);
        Assert.IsFalse(tied.HasUpdateControl, "choosing a match is a decision; it is never an update control");
        Assert.IsEmpty(page.SelectedKeys, "a tied row can never be selected");
        Assert.IsEmpty(page.Ready);

        // With update checks off the choice couldn't be used, so it is a reason, not a question; the app is still listed.
        UpdatesPagePresentation off = UpdatesPagePresenter.Build([rows[0]], details, false, [], null, Now);
        Assert.IsEmpty(off.CheckTheMatch);
        Assert.AreEqual(CantUpdateReasons.MoreThanOne, off.CantUpdateYet.Single().CantUpdateReason);
    }

    [TestMethod]
    [Description("Item 9: a ready row doesn't repeat that its match is sure, and the size of the version change is a fact without a warning colour.")]
    public void ReadyRowsSayOnlyWhatDiffers()
    {
        InventoryRow[] rows = [Row("firefox", "Firefox", "VerifiedUpdate", "131.0"), Row("tool", "Tool", "VerifiedUpdate", "1.0.0") with { Publisher = "Unknown" }];
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal)
        {
            ["firefox"] = Detail("firefox", "131.0", "132.0", "Mozilla.Firefox"),
            ["tool"] = Detail("tool", "1.0.0", "1.0.1", "Contoso.Tool")
        };
        UpdatesPagePresentation page = UpdatesPagePresenter.Build(rows, details, true, [], null, Now);
        Assert.AreEqual("Contoso · bigger changes are possible", page.Ready[0].Summary);
        Assert.AreEqual(string.Empty, page.Ready[1].Summary, "an unknown publisher is not shown as a fact");
        Assert.IsFalse(page.Ready.Any(row => row.Summary.Contains("confidence", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual("High confidence match", page.Ready[0].MatchText, "how sure the match is stays available to the details and the review");
        foreach (VersionJump jump in new[] { VersionJump.Patch, VersionJump.Minor, VersionJump.Major })
            Assert.AreEqual(StatusTone.Neutral, VersionJumpPresenter.Badge(jump)!.Tone, $"{jump}: a version change is not a warning");
    }

    private static InventoryRow Row(string key, string name, string status, string version) =>
        new(Guid.NewGuid(), name, "Contoso", "winget", "User", version, status, "Application", [], key);

    private static UpdateDetail Tied(string key, string installed, params string?[] versions) =>
        new(key, installed, null, null, null, "None", "More than one package has the same strongest evidence.", "winget: live result from today.", "X64", null,
            "Fixture.", ["AmbiguousCandidate"], [],
            [.. versions.Select((version, index) => new ReviewCandidateChoice("winget", $"Contoso.Choice{index}", version, "Contoso", "User", "x64", "Stable", "Fixture.", []))]);

    private static UpdateDetail Detail(string key, string installed, string? candidate, string? packageId, string confidence = "High",
        string[]? reasons = null, bool canConfirm = false, bool needsFreshCheck = false) =>
        new(key, installed, candidate, packageId is null ? null : "winget", packageId, confidence, "Fixture.", "winget: live result from today.", "X64", null,
            "Fixture.", reasons ?? [], [], [], CanConfirmPackageIdentity: canConfirm, RequiresFreshSourceEvidence: needsFreshCheck);
}
