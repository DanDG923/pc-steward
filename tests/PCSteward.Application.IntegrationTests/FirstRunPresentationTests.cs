using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// UI review 2026-11-13, item 21 (FR-UI-01/05): a two-step start. Step 1 scans this PC; step 2 asks once, straight after that first
/// scan, whether to look for updates online. Presentation only: the question opens the existing consent and decides nothing itself.
/// </summary>
[TestClass]
public sealed class FirstRunPresentationTests
{
    private static readonly string[] InternalWords = ["inventory", "package", "trusted source", "source evidence", "connector", "mapping", "repository", "local state", "identifier"];

    [TestMethod]
    [Description("FR-UI-01/05 (item 21): step 1 is numbered while update checks are off, and says the scan stays on this PC and changes nothing.")]
    public void StepOneScansThisPc()
    {
        FirstRunStep step = FirstRunPresenter.ScanStep(updateChecksOn: false);
        Assert.AreEqual("Step 1 of 2: See what is installed", step.Heading);
        Assert.AreEqual("Scan this PC", step.PrimaryLabel);
        Assert.IsNull(step.SecondaryLabel);
        StringAssert.Contains(step.Message, "doesn't go online");
        StringAssert.Contains(step.Message, "ask for administrator permission");
        // An owner who already turned update checks on has no second step, so the scan is not numbered.
        Assert.AreEqual("See what is installed", FirstRunPresenter.ScanStep(updateChecksOn: true).Heading);
    }

    [TestMethod]
    [Description("FR-UI-01/05 (item 21): step 2 is one question that says what is shared, that the owner is asked first, and that nothing is installed.")]
    public void StepTwoAsksOneQuestionAndSaysWhatIsShared()
    {
        FirstRunStep step = FirstRunPresenter.LookOnlineStep(310);
        Assert.AreEqual("Step 2 of 2: Look for updates online?", step.Heading);
        StringAssert.StartsWith(step.Message, "PC Steward found 310 apps on this PC.");
        StringAssert.Contains(step.Message, "WinGet and Chocolatey Community");
        StringAssert.Contains(step.Message, "may see which apps are installed here");
        StringAssert.Contains(step.Message, "You are asked to approve before anything is sent.");
        StringAssert.Contains(step.Message, "Nothing is installed.");
        Assert.AreEqual("Look for updates online…", step.PrimaryLabel);
        Assert.AreEqual("Not now", step.SecondaryLabel);
        Assert.AreEqual("Update checks stay off. You can turn them on at any time in Settings.", step.SecondaryHelp);
        StringAssert.StartsWith(FirstRunPresenter.LookOnlineStep(1).Message, "PC Steward found 1 app on this PC.");
    }

    [TestMethod]
    [Description("FR-UI-01 (item 21): the question is asked only after the first scan of this session, only while update checks are off, and only until it is answered.")]
    public void TheQuestionIsAskedOnce()
    {
        Assert.IsTrue(FirstRunPresenter.AsksAboutUpdates(firstScanThisSession: true, hasApps: true, updateChecksOn: false, answered: false));
        Assert.IsFalse(FirstRunPresenter.AsksAboutUpdates(firstScanThisSession: false, hasApps: true, updateChecksOn: false, answered: false), "not for an owner who already has an app list");
        Assert.IsFalse(FirstRunPresenter.AsksAboutUpdates(firstScanThisSession: true, hasApps: false, updateChecksOn: false, answered: false), "not when the scan found nothing");
        Assert.IsFalse(FirstRunPresenter.AsksAboutUpdates(firstScanThisSession: true, hasApps: true, updateChecksOn: true, answered: false), "not when update checks are already on");
        Assert.IsFalse(FirstRunPresenter.AsksAboutUpdates(firstScanThisSession: true, hasApps: true, updateChecksOn: false, answered: true), "not again after Not now");
        Assert.AreEqual(ShellPage.Home, FirstRunPresenter.AfterScan(asksAboutUpdates: true));
        Assert.AreEqual(ShellPage.InstalledApps, FirstRunPresenter.AfterScan(asksAboutUpdates: false));
    }

    [TestMethod]
    [Description("FR-UI-01/05 (item 21): the first-run and after-reset screens use the owner's words, with no internal terms.")]
    public void FirstRunAndAfterResetUsePlainWords()
    {
        FirstRunStep scan = FirstRunPresenter.ScanStep(false);
        FirstRunStep look = FirstRunPresenter.LookOnlineStep(12);
        string[] texts =
        [
            scan.Heading, scan.Message, scan.PrimaryLabel, scan.PrimaryHelp, look.Heading, look.Message, look.PrimaryLabel, look.PrimaryHelp, look.SecondaryLabel!, look.SecondaryHelp!,
            FirstRunPresenter.AfterResetTitle, FirstRunPresenter.AfterResetDescription, FirstRunPresenter.AfterResetEyebrow, FirstRunPresenter.AfterResetRemovedTitle,
            FirstRunPresenter.AfterResetRemovedMessage, FirstRunPresenter.AfterResetNextTitle, FirstRunPresenter.AfterResetNextMessage, FirstRunPresenter.AfterResetContinueLabel, FirstRunPresenter.AfterResetContinueHelp
        ];
        foreach (string text in texts)
            foreach (string word in InternalWords)
                Assert.IsFalse(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"'{word}' in: {text}");
        Assert.AreEqual("The factory reset is complete. Nothing was scanned and nothing went online.", FirstRunPresenter.AfterResetDescription);
        StringAssert.Contains(FirstRunPresenter.AfterResetNextMessage, "It doesn't start the scan for you.");
        Assert.AreEqual("Continue", FirstRunPresenter.AfterResetContinueLabel);
    }
}
