using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// Task 73 Phase 5 (ADR-030 decision 4; concept study §2 foundation 10): portable accessibility rules — label in name (WCAG 2.5.3),
/// the F6 region order, the F5 guard and the completion announcements. AC-UX-01–09 are best effort under ADR-021.
/// </summary>
[TestClass]
public sealed class AccessibilityTextTests
{
    private static readonly FocusRegion[] AllRegions = [FocusRegion.Navigation, FocusRegion.Page, FocusRegion.OperationBand];
    private static readonly FocusRegion[] WithoutBand = [FocusRegion.Navigation, FocusRegion.Page];

    [TestMethod]
    [Description("AC-UX-01; WCAG 2.5.3: the accessible name is the visible label, or the label followed by detail; a trailing ellipsis is not spoken.")]
    public void LabelInNameAlwaysStartsWithTheVisibleLabel()
    {
        Assert.AreEqual("Scan again", AccessibilityText.LabelInName("Scan again"));
        Assert.AreEqual("Scan again", AccessibilityText.LabelInName("Scan again", "  "));
        Assert.AreEqual("View details for Firefox", AccessibilityText.LabelInName("View details", "for Firefox"));
        Assert.AreEqual("Ignore Firefox: choose how long", AccessibilityText.LabelInName("Ignore…", "Firefox: choose how long"));
        Assert.AreEqual("Pin current version of Firefox (128.0)", AccessibilityText.LabelInName("Pin current version", "of Firefox (128.0)"));
        foreach ((string label, string? detail) in new (string, string?)[] { ("Start inventory", "(read-only scan of this PC)"), ("Match package…", "for Widget"), ("Done", null), ("Cancel", "the review of Widget. Nothing is changed.") })
            Assert.IsTrue(AccessibilityText.StartsWithLabel(AccessibilityText.LabelInName(label, detail), label), label);
        Assert.IsFalse(AccessibilityText.StartsWithLabel("Scan this PC", "Replace saved inventory"), "the audit's mismatch is detected");
        Assert.IsFalse(AccessibilityText.StartsWithLabel(string.Empty, "Back"));
        Assert.ThrowsExactly<ArgumentException>(() => AccessibilityText.LabelInName(" "));
    }

    [TestMethod]
    [Description("AC-UX-01; research §2 item 2: F6 moves navigation → page → operation band and wraps; Shift+F6 reverses; a hidden band is skipped.")]
    public void FocusRegionsCycleInOrderAndSkipUnavailableRegions()
    {
        Assert.AreEqual(FocusRegion.Page, FocusRegionCycle.Next(FocusRegion.Navigation, AllRegions, backwards: false));
        Assert.AreEqual(FocusRegion.OperationBand, FocusRegionCycle.Next(FocusRegion.Page, AllRegions, backwards: false));
        Assert.AreEqual(FocusRegion.Navigation, FocusRegionCycle.Next(FocusRegion.OperationBand, AllRegions, backwards: false));
        Assert.AreEqual(FocusRegion.OperationBand, FocusRegionCycle.Next(FocusRegion.Navigation, AllRegions, backwards: true));
        Assert.AreEqual(FocusRegion.Navigation, FocusRegionCycle.Next(FocusRegion.Page, WithoutBand, backwards: false), "the hidden band is skipped");
        Assert.AreEqual(FocusRegion.Navigation, FocusRegionCycle.Next(null, WithoutBand, backwards: false), "from outside any region (title bar) F6 starts at navigation");
        Assert.AreEqual(FocusRegion.Page, FocusRegionCycle.Next(null, WithoutBand, backwards: true));
        Assert.AreEqual(FocusRegion.Navigation, FocusRegionCycle.Next(FocusRegion.OperationBand, WithoutBand, backwards: false), "focus in a region that just hid starts over");
        Assert.IsNull(FocusRegionCycle.Next(FocusRegion.Page, [], backwards: false));
    }

    [TestMethod]
    [Description("AC-UX-09; FR-UPD-01: F5 starts the read-only scan only when the shell is shown and nothing is running, no review and no dialog is open.")]
    public void ScanShortcutIsBlockedDuringActiveWorkReviewsAndDialogs()
    {
        Assert.IsTrue(ShortcutGuard.CanStartScan(shellShown: true, hasActiveOperation: false, reviewOpen: false, dialogOpen: false));
        Assert.IsFalse(ShortcutGuard.CanStartScan(shellShown: true, hasActiveOperation: true, reviewOpen: false, dialogOpen: false), "never during active work");
        Assert.IsFalse(ShortcutGuard.CanStartScan(shellShown: true, hasActiveOperation: false, reviewOpen: true, dialogOpen: false), "never while an update review is open");
        Assert.IsFalse(ShortcutGuard.CanStartScan(shellShown: true, hasActiveOperation: false, reviewOpen: false, dialogOpen: true), "never over a dialog");
        Assert.IsFalse(ShortcutGuard.CanStartScan(shellShown: false, hasActiveOperation: false, reviewOpen: false, dialogOpen: false), "not on startup or onboarding pages");
    }

    [TestMethod]
    [Description("AC-UX-09; research §2 item 10: completion sentences are plain and truthful; the live-region key ignores elapsed-time ticks.")]
    public void AnnouncementsArePlainAndTheLiveRegionKeyIgnoresElapsedTime()
    {
        StringAssert.StartsWith(OperationAnnouncements.ScanFinished(succeeded: true, cancelled: false, appCount: 42), "Scan finished. 42 apps found");
        Assert.AreEqual("Scan cancelled. Your last completed scan is kept.", OperationAnnouncements.ScanFinished(succeeded: false, cancelled: true, appCount: 0));
        StringAssert.StartsWith(OperationAnnouncements.ScanFinished(succeeded: false, cancelled: false, appCount: 0), "The scan didn't finish");
        Assert.AreEqual("Update check finished. 1 update ready to review, 2 need your review.", OperationAnnouncements.UpdateCheckFinished(1, 2));
        StringAssert.Contains(OperationAnnouncements.ReviewReady("Widget"), "nothing changes until you choose the update button");
        Assert.AreEqual("Widget: Verified", OperationAnnouncements.JobFinished("Widget", "Verified"));
        Assert.AreEqual("Scan data cleared.", OperationAnnouncements.Completed("Scan data cleared"));

        Assert.AreEqual("Scanning this PC · Windows registry", OperationAnnouncements.StateKey("Scanning this PC · Windows registry · 00:07"));
        Assert.AreEqual(OperationAnnouncements.StateKey("Scanning this PC · Windows registry · 00:07"), OperationAnnouncements.StateKey("Scanning this PC · Windows registry · 01:12"), "ticks don't change the key");
        Assert.AreNotEqual(OperationAnnouncements.StateKey("Scanning this PC · Windows registry · 00:07"), OperationAnnouncements.StateKey("Scanning this PC · AppX · 00:09"), "a new phase does");
        Assert.AreEqual("Looking for updates", OperationAnnouncements.StateKey("Looking for updates"));
        Assert.AreEqual("Widget: Installed · not verified", OperationAnnouncements.StateKey("Widget: Installed · not verified"), "only a trailing mm:ss is removed");
    }

    [TestMethod]
    [Description("AC-UX-04 (audit P-M9; final review Info-1): the 500x500 epx minimum window size is converted to physical pixels at the display scale.")]
    public void MinimumWindowSizeScalesEffectivePixelsToPhysicalPixels()
    {
        Assert.AreEqual(500, WindowMinimumSize.EffectiveWidth);
        Assert.AreEqual(500, WindowMinimumSize.EffectiveHeight);
        Assert.AreEqual(500, WindowMinimumSize.PhysicalPixels(500, 1.0));
        Assert.AreEqual(625, WindowMinimumSize.PhysicalPixels(500, 1.25));
        Assert.AreEqual(750, WindowMinimumSize.PhysicalPixels(500, 1.5));
        Assert.AreEqual(1000, WindowMinimumSize.PhysicalPixels(500, 2.0));
        Assert.AreEqual(875, WindowMinimumSize.PhysicalPixels(500, 1.75));
        Assert.AreEqual(584, WindowMinimumSize.PhysicalPixels(500, 1.1666), "rounded up, never clipped");
        foreach (double invalid in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            Assert.AreEqual(500, WindowMinimumSize.PhysicalPixels(500, invalid), $"scale {invalid} falls back to 100%");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WindowMinimumSize.PhysicalPixels(-1, 1.0));
    }
}
