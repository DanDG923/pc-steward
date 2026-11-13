using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// UI review 2026-11-13, items 5 to 8 (FR-UI-01/05, AC-UX-03): one name per state used everywhere, "Review" kept for the step
/// before an update is installed, reassurance said where a decision is made rather than on every page, and plain words in
/// place of internal ones. Portable and deterministic; nothing here grants authority.
/// </summary>
[TestClass]
public sealed class UiReviewWordingPresentationTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly string[] StateNames = ["Update ready", "Check the match", "Couldn't check", "Up to date", "No update source", "Pinned", "Ignored", "Skipped", "Not checked yet"];
    private static readonly string[] SharedNamesExpected = ["Elgato Wave Link"];
    private static readonly string[] InternalWords = ["inventory", "package source", "trusted source", "source evidence", "package identity", "local-only", "connector"];

    [TestMethod]
    [Description("FR-UI-01 (item 5): each state has one short name, and the Installed apps tabs and the Home row use that same name.")]
    public void EachStateHasOneNameUsedByChipsTabsAndHome()
    {
        CollectionAssert.AreEqual(StateNames, StatusVocabulary.FilterLabels.Skip(1).ToArray());
        Assert.AreEqual(StateNames.Length, StateNames.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "no two states share a name");

        InventoryRow[] rows = [Row("a", "Alpha", "VerifiedUpdate"), Row("b", "Beta", "ReviewRequired"), Row("c", "Gamma", "FailedUnavailable"), Row("d", "Delta", "Current")];
        IReadOnlyList<InstalledAppsFilterOption> tabs = InstalledAppsFilters.Options(rows);
        Assert.AreEqual("Update ready", tabs[1].Label);
        Assert.AreEqual("Check the match", tabs[2].Label);
        Assert.AreEqual(1, tabs[2].Count, "the tab named for one state lists only that state; an app that couldn't be checked is not a match to check");
        Assert.IsTrue(InstalledAppsFilters.Matches(InstalledAppsFilter.NeedsReview, "ReviewRequired"));
        Assert.IsFalse(InstalledAppsFilters.Matches(InstalledAppsFilter.NeedsReview, "FailedUnavailable"));
        Assert.IsTrue(InstalledAppsFilters.Matches(InstalledAppsFilter.All, "FailedUnavailable"), "it is still listed under All");

        HomePresentation home = HomePresenter.Build(new HomeInputs(rows, new Dictionary<string, UpdateDetail>(StringComparer.Ordinal), true, [], [], [], [], Now.AddHours(-1), Now), Culture, Utc);
        // UI review 2026-11-13 (item 15): Home's line carries the same name, and appears only for a match the owner can confirm with
        // something newer on offer; a possible match with nothing newer is not a question on Home.
        Assert.IsFalse(home.Rows.Any(row => row.Kind == HomeRowKind.NeedsReview));
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal)
        {
            ["b"] = new("b", "1.0", "2.0", "winget", "Contoso.Beta", "Low", "Name only.", "winget: live result from today.", "X64", null, "Confirm the match.", [], [], [], CanConfirmPackageIdentity: true)
        };
        HomePresentation asks = HomePresenter.Build(new HomeInputs(rows, details, true, [], [], [], [], Now.AddHours(-1), Now), Culture, Utc);
        HomeRow match = asks.Rows.Single(row => row.Kind == HomeRowKind.NeedsReview);
        Assert.AreEqual("Check the match", match.Title);
        Assert.AreEqual("Beta has a possible match. Confirm it is the same app and its update can be offered.", match.Description);
        Assert.AreEqual(new HomeRowTarget(ShellPage.Updates), match.Target, "it opens the page that asks the question");
    }

    [TestMethod]
    [Description("FR-UI-01 (items 6 and 7): Home's button that opens Updates doesn't say Review, and Home's help says what happens without repeating the promise.")]
    public void ReviewNamesOnlyTheStepBeforeAnUpdate()
    {
        Assert.AreEqual("Opens one review of every ready update. Nothing is installed until you confirm there.", HomePresenter.UpdateAllHelp);
        foreach (string help in new[] { HomePresenter.CheckForUpdatesHelp, HomePresenter.ScanAgainHelp, HomePresenter.TurnOnUpdateChecksHelp, HomePresenter.UpdateAllHelp })
        {
            Assert.IsFalse(help.Contains("administrator", StringComparison.OrdinalIgnoreCase), help);
            Assert.IsFalse(help.Contains("Review", StringComparison.Ordinal), help);
        }
        // The promise itself is still made where the choice is: the consent text that goes with turning update checks on.
        StringAssert.Contains(ElevationStory.SourceConsent, "never asks for administrator permission");
        Assert.AreEqual("Scan", ShellNavigation.TitleOf(ShellPage.Scan));
    }

    [TestMethod]
    [Description("FR-UI-05 (item 8): when this PC was scanned is said as a day and a time, never as the scan mode's internal name.")]
    public void ScanTimeIsADayAndATime()
    {
        Assert.AreEqual("Scanned today at 14:50", InstalledAppsFilters.ScannedText(Now.AddMinutes(-10), Now, Culture, Utc));
        Assert.AreEqual("Scanned yesterday at 09:12", InstalledAppsFilters.ScannedText(new DateTimeOffset(2026, 11, 12, 9, 12, 0, TimeSpan.Zero), Now, Culture, Utc));
        Assert.AreEqual("Scanned on 3 Nov 2026 at 14:50", InstalledAppsFilters.ScannedText(new DateTimeOffset(2026, 11, 3, 14, 50, 0, TimeSpan.Zero), Now, Culture, Utc));
        Assert.AreEqual("Not scanned yet", InstalledAppsFilters.ScannedText(null, Now, Culture, Utc));
        // The day is the owner's local day, not 24 hours: 23:30 the evening before is "yesterday" half an hour into today.
        DateTimeOffset justAfterMidnight = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
        Assert.AreEqual("yesterday at 23:30", DisplayFormat.DayAndTime(justAfterMidnight.AddMinutes(-30), justAfterMidnight, Culture, Utc));
    }

    [TestMethod]
    [Description("FR-UI-01 (item 8): a row says where its record came from only when another row has the same name.")]
    public void SourceIsShownOnlyToTellSameNamedRowsApart()
    {
        InventoryRow[] rows = [Row("a", "Elgato Wave Link", "Current"), Row("b", "elgato wave link", "Current"), Row("c", "Discord", "Current")];
        IReadOnlySet<string> shared = InstalledAppsFilters.SharedNames(rows);
        CollectionAssert.AreEqual(SharedNamesExpected, shared.ToArray());
        Assert.IsTrue(shared.Contains("ELGATO WAVE LINK"), "names are compared without regard to case");
        Assert.IsFalse(shared.Contains("Discord"));

        Assert.AreEqual("Discord Inc.", InstalledAppsFilters.PublisherAndSource("Discord Inc.", "Windows", showSource: false));
        Assert.AreEqual("Unknown publisher", InstalledAppsFilters.PublisherAndSource(null, "Windows", showSource: false));
        Assert.AreEqual("Elgato · Microsoft Store", InstalledAppsFilters.PublisherAndSource("Elgato", "Microsoft Store", showSource: true));
    }

    [TestMethod]
    [Description("FR-UI-01 (item 8): the words people read on the everyday pages are plain; internal terms stay out of them.")]
    public void EverydayTextUsesPlainWords()
    {
        InventoryExperienceViewModel viewModel = new();
        string[] everyday =
        [
            .. StatusVocabulary.All.Select(chip => chip.Label), .. StatusVocabulary.All.Select(chip => chip.Meaning),
            InstalledAppsFilters.PageDescription, InstalledAppsFilters.NoUpdateSourceNote,
            HomePresenter.CheckForUpdatesHelp, HomePresenter.ScanAgainHelp, HomePresenter.UpdateAllHelp,
            SettingsPresenter.SourceSettingLabel, SettingsPresenter.SourceConsentNote, SettingsPresenter.SaveModelText, SettingsPresenter.SourceRowDescription(false, string.Empty),
            viewModel.ScanPhase, viewModel.ScanModeLabel, viewModel.FreshnessLabel, viewModel.UpdateFreshnessLabel,
            FreshnessPresenter.Describe(false, [], Now, Culture, Utc).Sentence, FreshnessPresenter.Describe(true, [], Now, Culture, Utc).Sentence
        ];
        foreach (string text in everyday)
            foreach (string word in InternalWords)
                Assert.IsFalse(text.Contains(word, StringComparison.OrdinalIgnoreCase) && !text.StartsWith("Local-only:", StringComparison.Ordinal), $"\"{text}\" uses \"{word}\"");
        Assert.AreEqual("Look for updates online", SettingsPresenter.SourceSettingLabel);
        Assert.AreEqual("Update checks are off.", viewModel.UpdateFreshnessLabel);
        Assert.AreEqual("Local-only: update checks are off.", FreshnessPresenter.Describe(false, [], Now, Culture, Utc).Sentence, "FR-UI-05 keeps the freshness word");
    }

    private static InventoryRow Row(string key, string name, string status) =>
        new(Guid.NewGuid(), name, "Contoso", "winget", "User", "1.0", status, "Application", [], key);
}
