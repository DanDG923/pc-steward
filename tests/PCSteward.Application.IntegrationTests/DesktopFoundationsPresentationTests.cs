using PropertyChangedNotifier = System.ComponentModel.INotifyPropertyChanged;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Task 73 Phase 1 (ADR-030): portable navigation, status vocabulary and elevation-story presentation.</summary>
[TestClass]
public sealed class DesktopFoundationsPresentationTests
{
    private static readonly string[] NavigationLabels = ["Home", "Updates", "Installed apps", "Activity", "Decisions", "Settings", "About"];
    private static readonly string[] FooterLabels = ["Settings", "About"];
    private static readonly string[] SingleFocusCandidate = ["only"];
    private static readonly string[] AmbiguousFocusCandidates = ["Remove", "Remove"];

    [TestMethod]
    [Description("FR-UI-01/02; AC-UX-01/02: ADR-030 navigation set — five main items then Settings and About in the footer.")]
    public void NavigationItemsFollowAdr030()
    {
        CollectionAssert.AreEqual(
            NavigationLabels,
            ShellNavigation.Items.Select(item => item.Label).ToArray());
        CollectionAssert.AreEqual(FooterLabels, ShellNavigation.Items.Where(item => item.IsFooter).Select(item => item.Label).ToArray());
        Assert.HasCount(7, ShellNavigation.Items.Select(item => item.Page).Distinct().ToArray());
        Assert.IsTrue(ShellNavigation.Items.All(item => !string.IsNullOrEmpty(item.Glyph)));
    }

    [TestMethod]
    [Description("FR-UI-01/02; AC-UX-01: every contextual page is reachable, highlights a navigation item and can be left with Back.")]
    public void EveryPageHasATitleAndAHighlightedNavigationItem()
    {
        foreach (ShellPage page in Enum.GetValues<ShellPage>())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ShellNavigation.TitleOf(page)), page.ToString());
            Assert.IsTrue(ShellNavigation.IsNavigationItem(ShellNavigation.ParentOf(page)), page.ToString());
            ShellNavigation navigation = new();
            if (page != ShellPage.Home)
            {
                Assert.IsTrue(navigation.Navigate(page));
                Assert.IsTrue(navigation.CanGoBack, $"{page} can be left with Back");
            }
        }
        Assert.AreEqual(ShellPage.Home, ShellNavigation.ParentOf(ShellPage.Scan));
        Assert.AreEqual(ShellPage.Home, ShellNavigation.ParentOf(ShellPage.Results));
        Assert.AreEqual(ShellPage.Updates, ShellNavigation.ParentOf(ShellPage.Review));
        Assert.AreEqual(ShellPage.InstalledApps, ShellNavigation.ParentOf(ShellPage.RepositoryApps));
        Assert.AreEqual(ShellPage.Settings, ShellNavigation.ParentOf(ShellPage.Storage));
        Assert.AreEqual(ShellPage.Settings, ShellNavigation.ParentOf(ShellPage.Privacy));
    }

    [TestMethod]
    [Description("FR-UI-02; AC-UX-01 (audit P-H1): invoking the highlighted parent from a contextual page navigates; re-invoking the current page adds no Back entry.")]
    public void HighlightedParentNavigatesAndSamePageIsANoOp()
    {
        ShellNavigation navigation = new();
        Assert.IsTrue(navigation.Navigate(ShellPage.Scan));
        Assert.AreEqual(ShellPage.Home, navigation.SelectedItem);
        Assert.IsTrue(navigation.Navigate(ShellPage.Home), "clicking the highlighted Home item from Scan must navigate");
        Assert.AreEqual(ShellPage.Home, navigation.Current);
        int depth = navigation.BackStackDepth;
        Assert.IsFalse(navigation.Navigate(ShellPage.Home));
        Assert.AreEqual(depth, navigation.BackStackDepth);
    }

    [TestMethod]
    [Description("FR-UI-02; AC-UX-01: Back walks the real history; details and onboarding are never returned to.")]
    public void BackStackWalksHistoryAndSkipsTransientPages()
    {
        ShellNavigation navigation = new();
        List<string> changes = [];
        ((PropertyChangedNotifier)navigation).PropertyChanged += (_, args) => changes.Add(args.PropertyName!);
        Assert.IsFalse(navigation.CanGoBack);
        Assert.IsFalse(navigation.GoBack());

        navigation.Navigate(ShellPage.Updates);
        navigation.Navigate(ShellPage.Review);
        navigation.Navigate(ShellPage.Detail);
        Assert.AreEqual(ShellPage.Updates, navigation.SelectedItem, "details highlight the page they were opened from");
        navigation.Navigate(ShellPage.InstalledApps);
        Assert.IsTrue(navigation.GoBack());
        Assert.AreEqual(ShellPage.Review, navigation.Current, "details are not returned to with Back");
        Assert.IsTrue(navigation.GoBack());
        Assert.AreEqual(ShellPage.Updates, navigation.Current);
        Assert.IsTrue(navigation.GoBack());
        Assert.AreEqual(ShellPage.Home, navigation.Current);
        Assert.IsFalse(navigation.CanGoBack);
        CollectionAssert.Contains(changes, nameof(ShellNavigation.CanGoBack));
        CollectionAssert.Contains(changes, nameof(ShellNavigation.Current));

        ShellNavigation onboarding = new(ShellPage.Onboarding);
        onboarding.Navigate(ShellPage.Scan);
        Assert.IsFalse(onboarding.CanGoBack, "post-reset onboarding is never returned to");
    }

    [TestMethod]
    [Description("FR-UI-02; AC-UX-01 (independent review L5): leaving details for the page beneath them does not stack that page twice.")]
    public void LeavingDetailsForTheOpeningPageDoesNotDuplicateIt()
    {
        ShellNavigation navigation = new();
        Assert.IsTrue(navigation.Navigate(ShellPage.InstalledApps));
        Assert.IsTrue(navigation.Navigate(ShellPage.Detail));
        Assert.AreEqual(2, navigation.BackStackDepth);
        Assert.IsTrue(navigation.Navigate(ShellPage.InstalledApps));
        Assert.AreEqual(ShellPage.InstalledApps, navigation.Current);
        Assert.AreEqual(1, navigation.BackStackDepth, "Installed apps is not stacked beneath itself");
        Assert.IsTrue(navigation.GoBack());
        Assert.AreEqual(ShellPage.Home, navigation.Current, "one Back returns past Installed apps, not to it again");
        Assert.IsFalse(navigation.CanGoBack);

        Assert.IsTrue(navigation.Navigate(ShellPage.Updates));
        Assert.IsTrue(navigation.Navigate(ShellPage.Detail));
        Assert.IsTrue(navigation.Navigate(ShellPage.Activity));
        Assert.AreEqual(2, navigation.BackStackDepth, "leaving details for a different page still keeps the opening page for Back");
        Assert.IsTrue(navigation.GoBack());
        Assert.AreEqual(ShellPage.Updates, navigation.Current);
    }

    [TestMethod]
    [Description("AC-UX-02; FR-UI-02 (independent review M3): focus is restored only to a unique match, and per-row actions name their product.")]
    public void FocusRestoreNeedsAUniqueMatchAndRowActionNamesAreDistinct()
    {
        Assert.IsNull(PageFocusRestore.UniqueMatch(Array.Empty<string>()));
        Assert.AreEqual("only", PageFocusRestore.UniqueMatch(SingleFocusCandidate));
        Assert.IsNull(PageFocusRestore.UniqueMatch(AmbiguousFocusCandidates),"an ambiguous name is never restored to the first match");

        InventoryExperienceViewModel viewModel = new();
        Guid widget = Guid.NewGuid(), gadget = Guid.NewGuid();
        viewModel.LoadInventory(
            [new CanonicalProduct(widget, "product:widget", "Widget"), new CanonicalProduct(gadget, "product:gadget", "Gadget")],
            [new InstallationInstance(Guid.NewGuid(), widget, "registry", "widget", "1.0", "Machine", "x64", "Contoso"), new InstallationInstance(Guid.NewGuid(), gadget, "registry", "gadget", "2.0", "User", "x64", "Fabrikam")]);
        Assert.AreEqual("Widget", PageFocusRestore.ProductName(viewModel.AllRows, "product:widget"));
        Assert.AreEqual("product:missing", PageFocusRestore.ProductName(viewModel.AllRows, "product:missing"), "an unloaded product falls back to its key");
        Assert.AreEqual("an unknown app", PageFocusRestore.ProductName(viewModel.AllRows, ""));

        string[] names =
        [
            PageFocusRestore.RemoveDecisionName(PageFocusRestore.ProductName(viewModel.AllRows, "product:widget"), "Pin"),
            PageFocusRestore.RemoveDecisionName(PageFocusRestore.ProductName(viewModel.AllRows, "product:gadget"), "Pin"),
            PageFocusRestore.RemoveManualMappingName("Widget", "winget", "Contoso.Widget"),
            PageFocusRestore.RemoveManualMappingName("Gadget", "winget", "Contoso.Widget")
        ];
        Assert.AreEqual("Remove Pin decision for Widget", names[0]);
        Assert.HasCount(names.Length, names.Distinct(StringComparer.Ordinal).ToArray(), "the same decision kind on two products gets two names");
    }

    [TestMethod]
    [Description("FR-UI-02: the back stack is bounded and Reset clears it.")]
    public void BackStackIsBoundedAndResettable()
    {
        ShellNavigation navigation = new();
        for (int index = 0; index < ShellNavigation.MaximumBackStackDepth * 2; index++)
            navigation.Navigate(index % 2 == 0 ? ShellPage.Updates : ShellPage.Activity);
        Assert.AreEqual(ShellNavigation.MaximumBackStackDepth, navigation.BackStackDepth);
        navigation.Reset(ShellPage.Scan);
        Assert.AreEqual(ShellPage.Scan, navigation.Current);
        Assert.IsFalse(navigation.CanGoBack);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => navigation.Navigate((ShellPage)999));
    }

    [TestMethod]
    [Description("FR-UI-01/05; AC-UX-03; SPEC §7.2: one label set, with glyph and text for every status, mapped from the inventory statuses.")]
    public void StatusVocabularyUsesSpecLabelsWithGlyphAndText()
    {
        Assert.AreEqual("Up to date", StatusVocabulary.For(UpdateIntelligenceStatus.Current).Label);
        Assert.AreEqual("Update ready", StatusVocabulary.For(UpdateIntelligenceStatus.VerifiedUpdate).Label);
        Assert.AreEqual("Check the match", StatusVocabulary.For(UpdateIntelligenceStatus.ReviewRequired).Label);
        Assert.AreEqual("Couldn't check", StatusVocabulary.For(UpdateIntelligenceStatus.FailedUnavailable).Label);
        Assert.AreEqual("No update source", StatusVocabulary.For(UpdateIntelligenceStatus.Unmanaged).Label, "UI polish phase 2: owner-approved wording");
        Assert.AreEqual("Pinned", StatusVocabulary.For(UpdateIntelligenceStatus.Pinned).Label);
        Assert.AreEqual("Ignored", StatusVocabulary.For(UpdateIntelligenceStatus.Ignored).Label);
        Assert.AreEqual("Not checked yet", StatusVocabulary.For(StatusVocabulary.InstalledKey).Label);
        // Final review L3: the unreferenced outcome chips are gone; outcomes are presented by UpdateOutcomePresenter only.
        foreach (string removed in new[] { "SelfManaged", "UnknownVersion", "UpdateFailed", "RestartRequired" })
            Assert.AreEqual("Status unavailable", StatusVocabulary.For(removed).Label, removed);
        Assert.IsFalse(StatusVocabulary.All.Any(chip => chip.Label is "Update failed" or "Restart required"), "no second outcome vocabulary");
        Assert.AreEqual("Status unavailable", StatusVocabulary.For("NotAStatus").Label);
        Assert.AreEqual("Status unavailable", StatusVocabulary.For((string?)null).Label);

        foreach (UpdateIntelligenceStatus status in Enum.GetValues<UpdateIntelligenceStatus>())
            Assert.AreEqual(status.ToString(), StatusVocabulary.For(status).Key, "every inventory status has its own chip");
        foreach (StatusChipPresentation chip in StatusVocabulary.All)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(chip.Label));
            Assert.IsFalse(string.IsNullOrWhiteSpace(chip.Glyph), "colour is never the only signal");
            Assert.IsFalse(string.IsNullOrWhiteSpace(chip.Meaning));
            StringAssert.Contains(chip.AccessibleName("Widget"), chip.Label, "accessible name contains the visible label");
            StringAssert.Contains(chip.AccessibleName(), chip.Label);
            foreach (string forbidden in new[] { "unsafe", "danger", "health", "vulnerab" })
                Assert.IsFalse((chip.Label + chip.Meaning).Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"{chip.Key}: {forbidden}");
        }
        Assert.HasCount(StatusVocabulary.All.Count, StatusVocabulary.All.Select(chip => chip.Label).Distinct().ToArray(), "labels are unique");
    }

    [TestMethod]
    [Description("SPEC §17.5; protected product trust: unmanaged is not unsafe, harmless states are never red, and only a failed update is critical.")]
    public void StatusTonesNeverAlarmForHarmlessStates()
    {
        StatusChipPresentation unmanaged = StatusVocabulary.For(UpdateIntelligenceStatus.Unmanaged);
        Assert.AreEqual(StatusTone.Neutral, unmanaged.Tone);
        StringAssert.Contains(unmanaged.Meaning, "not a safety warning");
        Assert.IsFalse(StatusVocabulary.All.Any(chip => chip.Tone == StatusTone.Critical), "no inventory status is critical; only a failed update outcome is");
        // Final review L3: Restart required has one tone everywhere (Verify, Home, Activity): Information, never alarming.
        Assert.AreEqual(StatusTone.Information, UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.RestartRequired, null, null, "2.0").Tone);
        Assert.AreEqual(StatusTone.Critical, UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.InstallerFailed, null, 1, "2.0").Tone);
        Assert.AreEqual(StatusTone.Success, StatusVocabulary.For(UpdateIntelligenceStatus.Current).Tone);
        StringAssert.Contains(StatusVocabulary.For(UpdateIntelligenceStatus.VerifiedUpdate).Meaning, "until you review and confirm it");
        StringAssert.Contains(StatusVocabulary.For(UpdateIntelligenceStatus.ReviewRequired).Meaning, "can't be updated until you confirm the match");
        StringAssert.Contains(StatusVocabulary.For(UpdateIntelligenceStatus.FailedUnavailable).Meaning, "can't be updated until then");
    }

    [TestMethod]
    [Description("FR-UI-01; audit P-M2: status filters show the same labels as rows, never raw enum names, and map back to the stored key.")]
    public void StatusFiltersShowRowLabelsAndMapBackToKeys()
    {
        Assert.AreEqual(StatusVocabulary.AllStatusesLabel, StatusVocabulary.FilterLabels[0]);
        foreach (string name in Enum.GetNames<UpdateIntelligenceStatus>().Where(name => StatusVocabulary.For(name).Label != name))
            CollectionAssert.DoesNotContain(StatusVocabulary.FilterLabels.ToArray(), name, $"raw enum name {name} must not be a filter label");
        foreach (StatusChipPresentation chip in StatusVocabulary.InventoryStatuses)
        {
            CollectionAssert.Contains(StatusVocabulary.FilterLabels.ToArray(), chip.Label);
            Assert.AreEqual(chip.Key, StatusVocabulary.FilterKeyForLabel(chip.Label));
            Assert.AreEqual(chip.Label, StatusVocabulary.FilterLabelForKey(chip.Key));
        }
        Assert.AreEqual(StatusVocabulary.AllStatusesLabel, StatusVocabulary.FilterKeyForLabel(StatusVocabulary.AllStatusesLabel));
        Assert.AreEqual(StatusVocabulary.AllStatusesLabel, StatusVocabulary.FilterKeyForLabel(null));
        Assert.AreEqual(StatusVocabulary.AllStatusesLabel, StatusVocabulary.FilterLabelForKey(StatusVocabulary.AllStatusesLabel));

        InventoryExperienceViewModel viewModel = new(); Guid id = Guid.NewGuid();
        viewModel.LoadInventory([new CanonicalProduct(id, "product:widget", "Widget")], [new InstallationInstance(Guid.NewGuid(), id, "registry", "widget", "1.0", "Machine", "x64", "Contoso")]);
        string installedLabel = StatusVocabulary.For(viewModel.AllRows.Single().Status).Label;
        viewModel.SetFilters(null, StatusVocabulary.FilterKeyForLabel(installedLabel), null, null, null, null);
        Assert.HasCount(1, viewModel.VisibleRows, "the filter label of a row's chip selects that row");
    }

    [TestMethod]
    [Description("FR-UPD-04; FR-UI-03; audit P-H5: one elevation story — the desktop is designed to run unelevated and never requests elevation for current-user updates; Windows asks once per approved job only for a machine-wide update; checking never asks.")]
    public void ElevationStoryIsConsistentEverywhere()
    {
        StringAssert.Contains(ElevationStory.Summary, "is designed to run without administrator rights");
        StringAssert.Contains(ElevationStory.Summary, "doesn't request administrator permission for current-user updates");
        StringAssert.Contains(ElevationStory.Summary, "An app's own installer can still ask Windows for permission.");
        StringAssert.Contains(ElevationStory.Summary, "Checking for updates never asks for administrator permission");
        StringAssert.Contains(ElevationStory.Summary, "once for that job");
        StringAssert.Contains(ElevationStory.Summary, "machine-wide");
        StringAssert.Contains(ElevationStory.SourceConsent, "never asks for administrator permission");
        StringAssert.Contains(ElevationStory.SourceConsent, "once per approved job");
        StringAssert.Contains(ElevationStory.SourceConsent, "starts no scan, check, download or update");
        StringAssert.StartsWith(ElevationStory.HelperMissingOutcome, "The update was blocked before it could run.");
        StringAssert.Contains(ElevationStory.HelperMissingOutcome, "No application was changed.");
        StringAssert.Contains(ElevationStory.HelperRequirement, "blocked before it starts");
        foreach (string copy in new[] { ElevationStory.Summary, ElevationStory.SourceConsent, ElevationStory.HelperRequirement, ElevationStory.HelperMissingOutcome })
        {
            Assert.IsFalse(copy.Contains("does not use elevation", StringComparison.OrdinalIgnoreCase), "contradicts the composed elevated helper");
            Assert.IsFalse(copy.Contains("machine-scope only", StringComparison.OrdinalIgnoreCase), "contradicts current-user updates");
            Assert.IsFalse(copy.Contains("always runs without administrator", StringComparison.OrdinalIgnoreCase), "no startup guard enforces it");
            Assert.IsFalse(copy.Contains("updates do not ask", StringComparison.OrdinalIgnoreCase), "an app's own installer can still prompt");
        }

        UpdateJobSelection machine = new("product:machine", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Machine", "1.0", "1.1", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1");
        UpdateJobSelection user = new("product:user", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.User", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1");
        AdministratorRequirementPresentation required = AdministratorRequirementPresenter.ForSelection(machine);
        AdministratorRequirementPresentation notExpected = AdministratorRequirementPresenter.ForSelection(user);
        AdministratorRequirementPresentation unknown = AdministratorRequirementPresenter.BeforeReview();
        StringAssert.Contains(required.Explanation, "once for this update job");
        StringAssert.Contains(required.Explanation, "After you confirm it");
        StringAssert.Contains(required.Explanation, "keeps running without administrator rights");
        StringAssert.Contains(notExpected.Explanation, "PC Steward will not request administrator permission for this current-user update.");
        StringAssert.Contains(notExpected.Explanation, "installer can still ask Windows for permission");
        Assert.IsFalse(notExpected.Explanation.Contains("Windows will not ask", StringComparison.OrdinalIgnoreCase), "PC Steward cannot promise Windows will not prompt");
        StringAssert.Contains(unknown.Explanation, "never ask for administrator permission");
    }
}
