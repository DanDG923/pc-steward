using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// UI review 2026-11-13, items 11 and 12 (FR-UI-01/04/05, FR-POL-03/05, FR-UPD-01): app details lead with one sentence and at most
/// one main action, and a saved decision is one line with Undo instead of a second dialog. Presentation only: "Update…" is offered
/// exactly when the Updates page lists the app as ready, and nothing here records or reverses anything by itself.
/// </summary>
[TestClass]
public sealed class UiReviewAppDetailsPresentationTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly SourceSnapshot[] Live = [new("winget", "1.9", Now.AddMinutes(-8), SourceSnapshotState.Live, [], [])];
    private static readonly string[] OnThisPcLabels = ["Version", "Publisher", "Installed for"];
    private static readonly string[] FromSourceLabels = ["Package", "Latest", "How sure", "Checked"];
    private static readonly string[] InternalWords = ["inventory", "package source", "trusted source", "source evidence", "package identity", "connector", "scope", "architecture", "mapping"];

    [TestMethod]
    [Description("Item 11: a ready update is one sentence and one Update button; the administrator note appears only when it is known.")]
    public void AReadyUpdateLeadsWithUpdate()
    {
        AppDetailSummary user = Build(Row("Discord", "VerifiedUpdate", "1.0.9267"), Detail("1.0.9267", "1.0.9268", "Discord.Discord"));
        Assert.AreEqual("Update ready", user.Headline);
        Assert.AreEqual("Version 1.0.9268 is ready. You see a review before anything is installed.", user.Sentence);
        Assert.AreEqual(AppDetailAction.Update, user.Action);
        Assert.AreEqual("Update…", user.ActionLabel);
        Assert.IsEmpty(user.Notes, "an app installed just for you says nothing about administrator permission: the review does");
        CollectionAssert.AreEqual(OnThisPcLabels, user.OnThisPc.Select(fact => fact.Label).ToArray());
        Assert.AreEqual("Just you", user.OnThisPc[2].Value);
        Assert.AreEqual("From WinGet", user.SourceTitle);
        CollectionAssert.AreEqual(FromSourceLabels, user.FromSource.Select(fact => fact.Label).ToArray());
        Assert.AreEqual("8 minutes ago", user.FromSource[3].Value);
        Assert.AreEqual("Version 1.0.9267 · Contoso", user.Subtitle);
        Assert.IsTrue(user.MoreActions.Any(item => item.Action == UpdateRowAction.SkipVersion), "skip, pin and ignore are in the More menu");
        Assert.IsFalse(user.MoreActions.Any(item => item.Action is UpdateRowAction.Details or UpdateRowAction.Reverse));

        AppDetailSummary machine = Build(Row("Runtime", "VerifiedUpdate", "8.0.33", scope: "Machine"), Detail("8.0.33", "9.0.0", "Microsoft.Runtime"));
        CollectionAssert.Contains(machine.Notes.ToArray(), AppDetailSummaryPresenter.AdministratorNote);
        CollectionAssert.Contains(machine.Notes.ToArray(), "This is a major version change, so bigger changes are possible.");
        Assert.AreEqual("Everyone on this PC", machine.OnThisPc[2].Value);

        // Update checks off: the Updates page wouldn't list it as ready, so details offers no Update button either (FR-UPD-01).
        AppDetailSummary off = Build(Row("Discord", "VerifiedUpdate", "1.0.9267"), Detail("1.0.9267", "1.0.9268", "Discord.Discord"), sourcesEnabled: false);
        Assert.AreEqual("Can't update yet", off.Headline);
        Assert.AreEqual(AppDetailAction.TurnOnUpdateChecks, off.Action);
    }

    [TestMethod]
    [Description("Item 11: a match the owner can settle is asked as a question; anything else gets one reason and, at most, Check again.")]
    public void AMatchIsAQuestionAndABlockedUpdateIsAReason()
    {
        AppDetailSummary match = Build(Row("Elgato Wave Link", "ReviewRequired", "3.3.0"), Detail("3.3.0", "3.4.0", "Elgato.WaveLink", confidence: "Low", canConfirm: true));
        Assert.AreEqual("Check the match", match.Headline);
        Assert.AreEqual("WinGet has version 3.4.0, listed as Elgato.WaveLink. If that is this app, confirm it and PC Steward will offer the update.", match.Sentence);
        Assert.AreEqual(AppDetailAction.ConfirmMatch, match.Action);
        Assert.AreEqual("Yes, same app", match.ActionLabel);

        AppDetailSummary blocked = Build(Row("Windows Terminal", "ReviewRequired", "1.25.2733.0"), Detail("1.25.2733.0", "1.25.3131.0", "Microsoft.WindowsTerminal", reasons: ["MissingScopeEvidence"]));
        Assert.AreEqual("Can't update yet", blocked.Headline);
        Assert.AreEqual($"Version 1.25.3131.0 is on offer, but PC Steward can't update this app yet. {CantUpdateReasons.HowInstalledUnknown}", blocked.Sentence);
        Assert.AreEqual(AppDetailAction.None, blocked.Action, "checking again can't tell PC Steward how an app was installed");
        Assert.AreEqual(StatusTone.Neutral, blocked.Tone, "nothing here needs the owner, so it isn't amber");

        AppDetailSummary stale = Build(Row("Notepad++", "ReviewRequired", "8.6"), Detail("8.6", "8.7", "Notepad++.Notepad++", needsFreshCheck: true));
        Assert.AreEqual(AppDetailAction.CheckAgain, stale.Action);
        Assert.AreEqual("Check again", stale.ActionLabel);

        AppDetailSummary same = Build(Row("7-Zip", "ReviewRequired", "24.08"), Detail("24.08", "24.08", "7zip.7zip", reasons: ["CandidateIsCurrent"], canConfirm: true));
        Assert.AreEqual(AppDetailAction.ConfirmMatch, same.Action, "confirming the match lets later updates be offered");
        StringAssert.Contains(same.Sentence, "nothing newer than the version you have");

        UpdateDetail tied = new("key", "1.0", null, null, null, "None", "Fixture.", "winget: live result from today.", "X64", null, "Fixture.", ["AmbiguousCandidate"], [],
            [new("winget", "Contoso.Widget", "1.1", "Contoso", "User", "x64", "Stable", "Fixture.", []), new("winget", "Contoso.Widget.Legacy", "1.0", "Contoso", "User", "x64", "Stable", "Fixture.", [])]);
        AppDetailSummary choose = Build(Row("Widget", "ReviewRequired", "1.0"), tied);
        Assert.AreEqual(AppDetailAction.ChooseMatch, choose.Action);
        Assert.AreEqual("PC Steward found 2 possible matches for this app, and a newer version is on offer. Use one only if you recognise it as this app.", choose.Sentence);
        Assert.IsEmpty(choose.FromSource, "no single package is shown as the app's source until one is chosen");
    }

    [TestMethod]
    [Description("Item 11: the other states say one thing each; an app with no update source offers Match it yourself; a decision offers its undo.")]
    public void EveryOtherStateIsOneSentence()
    {
        AppDetailSummary current = Build(Row("Firefox", "Current", "132.0"), Detail("132.0", "132.0", "Mozilla.Firefox"));
        Assert.AreEqual("Up to date", current.Headline);
        Assert.AreEqual("This app is up to date. WinGet has nothing newer.", current.Sentence);
        Assert.IsFalse(current.HasAction);

        AppDetailSummary none = Build(Row("Old Tool", "Unmanaged", "1.0", publisher: "Unknown"), null);
        Assert.AreEqual("No update source", none.Headline);
        Assert.AreEqual("PC Steward found no update source for this app, so it can't check it for updates.", none.Sentence);
        Assert.IsTrue(none.OffersMatchItYourself);
        Assert.IsEmpty(none.FromSource);
        Assert.AreEqual("Version 1.0", none.Subtitle, "an unknown publisher is not shown as a fact");
        Assert.AreEqual("Not known", none.OnThisPc[1].Value);

        AppDetailSummary failed = Build(Row("Paint.NET", "FailedUnavailable", "5.0"), Detail("5.0", null, "dotPDN.PaintDotNet"));
        Assert.AreEqual("Couldn't check", failed.Headline);
        Assert.AreEqual(AppDetailAction.CheckAgain, failed.Action);
        Assert.IsEmpty(failed.MoreActions, "while it's unknown whether an update exists there is nothing to pin or ignore");

        AppDetailSummary notChecked = Build(Row("New App", StatusVocabulary.InstalledKey, "1.0"), null, sourcesEnabled: false);
        Assert.AreEqual(AppDetailAction.TurnOnUpdateChecks, notChecked.Action);
        Assert.AreEqual(HomePresenter.TurnOnUpdateChecksLabel, notChecked.ActionLabel);

        InventoryRow pinnedRow = Row("VLC", "Pinned", "3.0.20");
        StableDecision pin = new(Guid.NewGuid(), pinnedRow.StableProductKey, "PinCurrentVersion", "3.0.20", Now.AddDays(-1));
        AppDetailSummary pinned = AppDetailSummaryPresenter.Build(pinnedRow, Detail("3.0.20", "3.0.21", "VideoLAN.VLC"), true, [pin], Live, Now, Culture, Utc);
        Assert.AreEqual("Pinned", pinned.Headline);
        Assert.AreEqual("You pinned this app at version 3.0.20. No other version is offered.", pinned.Sentence);
        Assert.AreEqual(AppDetailAction.UndoDecision, pinned.Action);
        Assert.AreEqual("Remove pin", pinned.ActionLabel);
        Assert.AreEqual(pin.Id, pinned.DecisionId);

        foreach (AppDetailSummary summary in new[] { current, none, failed, notChecked, pinned })
            foreach (string word in InternalWords)
                Assert.IsFalse((summary.Sentence + " " + string.Join(" ", summary.Notes)).Contains(word, StringComparison.OrdinalIgnoreCase), $"\"{summary.Sentence}\" uses \"{word}\"");
    }

    [TestMethod]
    [Description("Item 12: a saved decision is one line, and Undo names exactly the newest record of that kind for each app it was saved for.")]
    public void ASavedDecisionIsOneLineWithUndo()
    {
        Guid older = Guid.NewGuid(), newest = Guid.NewGuid(), other = Guid.NewGuid(), twin = Guid.NewGuid();
        StableDecision[] active =
        [
            new(older, "a", "PinCurrentVersion", "1.0", Now.AddDays(-2)),
            new(newest, "a", "PinCurrentVersion", "1.1", Now),
            new(other, "a", "Ignore", "", Now.AddMinutes(1)),
            new(twin, "b", "PinCurrentVersion", "1.1", Now)
        ];
        DecisionNotice pinned = DecisionNoticePresenter.Pinned("Discord", "1.1", ["a", "b", "a"], active);
        Assert.AreEqual("Discord is pinned at version 1.1.", pinned.Message);
        CollectionAssert.AreEqual(new List<Guid> { newest, twin }, pinned.DecisionIds.ToList(), "never an older pin, and never a decision of another kind");
        Assert.IsTrue(pinned.CanUndo);
        Assert.AreEqual("Undo: Discord is pinned at version 1.1.", pinned.UndoAccessibleName);

        Assert.AreEqual("Updates for Discord are ignored.", DecisionNoticePresenter.Ignored("Discord", null, ["a"], active).Message);
        Assert.AreEqual("Updates for Discord are ignored until 13 Dec 2026.", DecisionNoticePresenter.Ignored("Discord", new DateTimeOffset(2026, 12, 13, 0, 0, 0, TimeSpan.Zero), ["a"], active, Culture, Utc).Message);
        DecisionNotice skipped = DecisionNoticePresenter.Skipped("Discord", "1.2", ["a"], active);
        Assert.AreEqual("Version 1.2 of Discord is skipped. A newer version will be offered.", skipped.Message);
        Assert.IsFalse(skipped.CanUndo, "the line is still shown when the saved record can't be found again, without Undo");

        ManualMapping mapping = new(Guid.NewGuid(), "a", "winget", "Elgato.WaveLink", Now);
        ManualMapping[] saved = [mapping, new(Guid.NewGuid(), "b", "winget", "Elgato.WaveLink", Now)];
        DecisionNotice matched = DecisionNoticePresenter.MatchConfirmed("Elgato Wave Link", "a", "WINGET", "Elgato.WaveLink", saved, []);
        Assert.AreEqual("Elgato.WaveLink is now matched to Elgato Wave Link.", matched.Message);
        CollectionAssert.AreEqual(new List<Guid> { mapping.Id }, matched.MappingIds.ToList());
        Assert.IsEmpty(matched.DecisionIds);

        // Confirming a match that was already saved adds nothing, so Undo must not be offered: it would remove the earlier decision.
        DecisionNotice again = DecisionNoticePresenter.MatchConfirmed("Elgato Wave Link", "a", "winget", "Elgato.WaveLink", saved, [mapping.Id]);
        Assert.AreEqual("Elgato.WaveLink was already matched to Elgato Wave Link.", again.Message);
        Assert.IsFalse(again.CanUndo);
    }

    private static AppDetailSummary Build(InventoryRow row, UpdateDetail? detail, bool sourcesEnabled = true) =>
        AppDetailSummaryPresenter.Build(row, detail, sourcesEnabled, [], Live, Now, Culture, Utc);

    private static InventoryRow Row(string name, string status, string version, string scope = "User", string publisher = "Contoso") =>
        new(Guid.NewGuid(), name, publisher, "winget", scope, version, status, "Application", [], "key");

    private static UpdateDetail Detail(string installed, string? candidate, string packageId, string confidence = "High", string[]? reasons = null, bool canConfirm = false, bool needsFreshCheck = false) =>
        new("key", installed, candidate, "winget", packageId, confidence, "Fixture.", "winget: live result from today.", "X64", null,
            "Fixture.", reasons ?? [], [], [], CanConfirmPackageIdentity: canConfirm, RequiresFreshSourceEvidence: needsFreshCheck);
}
