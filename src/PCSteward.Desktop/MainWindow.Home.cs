using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Dispatching;
using System.Globalization;
using PCSteward.Application;
using PCSteward.Storage;
using PCSteward.Scheduling.Windows;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PCSteward.Desktop;

public sealed partial class MainWindow
{
    /// <summary>
    /// Task 51: when Windows last started, from the system tick count (it starts again at zero on every restart). A restart-required
    /// update stays on Home until Windows has restarted after it; this read is presentation only and never execution authority.
    /// </summary>
    private static DateTimeOffset LastBootAt() => DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// FR-UI-01/05/06 (ADR-030 concept A; UI review 2026-11-13, item 15): Home answers "is anything out of date?". Info bars for
    /// updates that need the owner, then one status card (an icon, one sentence, facts with icons, and the action that fits the
    /// state), the ready updates themselves, lines that appear only when they have something to say, and the last few things PC
    /// Steward did. "Turn on update checks…" opens the existing source consent; "Update all N…" and a row's "Update…" open the
    /// same review as the Updates page, where nothing is installed until the owner confirms (FR-UPD-01, AC-UPD-01); "Check for
    /// updates" and "Scan again" never elevate. Nothing here approves or runs an update.
    /// </summary>
    private StackPanel BuildHomePage()
    {
        IReadOnlyList<UpdateJobRecord> jobs = UpdateJobHistory.Group(updateHistory);
        IReadOnlyList<ActivityEntry> activity = ActivityPresenter.Entries(jobs, scanRuns, activeDecisions, manualMappings, viewModel.AllRows, packageNames: viewModel.PackageNames);
        HomePresentation home = HomePresenter.Build(new HomeInputs(viewModel.AllRows, viewModel.UpdateDetails, viewModel.IsUpdateSourceEnabled, viewModel.Sources,
            activeDecisions, [.. jobs], activity, viewModel.LastScanAt, DateTimeOffset.UtcNow, LastBootAt()));
        StackPanel panel = CreatePagePanel();

        if (!home.HasInventory)
        {
            panel.Children.Add(StewardVisuals.PageTitle("Home"));
            Button start = StewardVisuals.PrimaryButton("Scan this PC", "(reads the installed apps; nothing is changed)");
            start.IsEnabled = !HasActiveOperation;
            start.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
            // UI review 2026-11-13 (item 21): step 1 of the guided first run. It is numbered only while update checks are off.
            FirstRunStep scanStep = FirstRunPresenter.ScanStep(viewModel.IsUpdateSourceEnabled);
            panel.Children.Add(StewardVisuals.EmptyState("\uE721", scanStep.Heading, scanStep.Message, start));
            panel.Children.Add(StewardVisuals.InformationBanner("Private by default", "What PC Steward finds stays on this PC. It looks for updates online only after you turn that on."));
            return panel;
        }

        // FR-UI-06: failed or unverified updates and pending restarts, as info bars above the page (approved target T-Home-Ready).
        // ADR-032 revision 2 §8: PC Steward's own recorded update comes first.
        if (SelfUpdateHomeBanner() is { } selfUpdateBanner) panel.Children.Add(selfUpdateBanner);
        foreach (HomeNotice notice in home.Notices) panel.Children.Add(HomeNoticeBar(notice));
        panel.Children.Add(StewardVisuals.PageTitle("Home"));
        // UI review 2026-11-13 (item 21): straight after the first scan, the one question of the guided first run stands where
        // the status card would say the same thing ("update checks are off"). Once answered, the status card is back.
        panel.Children.Add(AsksAboutUpdates ? FirstRunQuestionCard() : HomeStatusCard(home));
        if (activityHistoryUnavailable)
            panel.Children.Add(StewardVisuals.InformationBanner("Update history unavailable", "PC Steward couldn't read its recorded update history just now, so restart and attention counts may be incomplete. Nothing was changed.", "neutral"));

        // The ready updates themselves (UI review 2026-11-13, item 15), so the common job is one click from launch. Each row's
        // "Update…" opens the same review as its row on Updates, for that app alone.
        if (home.ReadyUpdates.Count > 0)
        {
            List<FrameworkElement> readyRows = [];
            foreach (HomeReadyUpdate ready in home.ReadyUpdates)
            {
                Grid line = UpdateRowGrid();
                Border avatar = AppTile(ready.Name, ready.StableProductKey);
                Grid.SetColumnSpan(avatar, 2); line.Children.Add(avatar);
                StewardWrapPanel title = new() { HorizontalSpacing = StewardSpacing.Medium, VerticalSpacing = StewardSpacing.XSmall };
                title.Children.Add(UpdateRowName(ready.Name, ready.StableProductKey));
                title.Children.Add(new TextBlock { Text = ready.VersionText, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true });
                UpdateRowCopy(line, title, string.Empty);
                string key = ready.StableProductKey;
                Button update = StewardVisuals.ActionButton("Update…", $"{ready.Name} to {ready.NewVersion}");
                update.IsEnabled = !HasActiveOperation && reviewDecision is null && viewModel.IsUpdateSourceEnabled;
                update.VerticalAlignment = VerticalAlignment.Center;
                AutomationProperties.SetHelpText(update, "Opens the review for this app. Nothing is installed until you confirm on the review page.");
                update.Click += async (_, _) => await ReviewAndExecuteAsync(RowsFor([key]));
                Grid.SetColumn(update, 3); line.Children.Add(update);
                readyRows.Add(line);
            }
            panel.Children.Add(StewardVisuals.ListCard(readyRows));
            if (home.MoreReadyText is { } more)
            {
                HyperlinkButton moreReady = new() { Content = more, Padding = new Thickness(0) };
                StewardVisuals.Named(moreReady, more, "(opens Updates)");
                moreReady.Click += (_, _) => NavigateTo(ShellPage.Updates);
                panel.Children.Add(moreReady);
            }
        }

        // Lines that have something to say: a match to check, an app that can't update yet, decisions in force. A calm PC has none.
        if (home.Rows.Count > 0)
        {
            StackPanel lines = new() { Spacing = StewardSpacing.XSmall };
            foreach (HomeRow row in home.Rows) lines.Children.Add(HomeRowButton(row));
            panel.Children.Add(lines);
        }

        // Recent: the last few things PC Steward did, with a link to the full history.
        if (home.Recent.Count > 0)
        {
            StackPanel recent = new() { Spacing = StewardSpacing.Small };
            recent.Children.Add(SectionHeading("Recent"));
            foreach (HomeRecentLine line in home.Recent) recent.Children.Add(StewardVisuals.IconLine(line.Glyph, line.Line, StewardStatus.ToneName(line.Tone)));
            HyperlinkButton allActivity = new() { Content = "See all activity", Padding = new Thickness(0) };
            StewardVisuals.Named(allActivity, "See all activity", "(opens Activity)");
            ActivityFilter everything = HomePresenter.RecentActivityFilter;
            allActivity.Click += (_, _) => { activityFilter = everything; NavigateTo(ShellPage.Activity); };
            recent.Children.Add(allActivity);
            panel.Children.Add(recent);
        }
        return panel;
    }

    // UI review 2026-11-13 (item 21): the guided first run asks its question once, in the session where this PC was first scanned.
    private bool firstScanThisSession;
    private bool firstRunQuestionAnswered;

    private bool AsksAboutUpdates => FirstRunPresenter.AsksAboutUpdates(firstScanThisSession, viewModel.AllRows.Count > 0, viewModel.IsUpdateSourceEnabled, firstRunQuestionAnswered);

    /// <summary>
    /// Step 2 of the guided first run: "Look for updates online?". Yes opens the one existing consent dialog, which shows what is
    /// shared and keeps "Keep off" as its default; only if the owner approves there does PC Steward look for updates, which is
    /// what they asked for. "Not now" changes nothing. Neither button approves, selects or installs an update (FR-UPD-01).
    /// </summary>
    private Border FirstRunQuestionCard()
    {
        FirstRunStep step = FirstRunPresenter.LookOnlineStep(viewModel.AllRows.Count);
        Button look = StewardVisuals.PrimaryButton(step.PrimaryLabel, step.PrimaryHelp);
        look.IsEnabled = !HasActiveOperation;
        look.Click += async (_, _) =>
        {
            await ChangeWinGetTrustedSourcesAsync(true);
            if (windowClosed || !viewModel.IsUpdateSourceEnabled) return;
            firstRunQuestionAnswered = true;
            if (HasActiveOperation) { Render(); return; }
            await CheckUpdateSourcesAsync();
        };
        Button notNow = StewardVisuals.ActionButton(step.SecondaryLabel ?? FirstRunPresenter.NotNowLabel);
        string notNowHelp = step.SecondaryHelp ?? FirstRunPresenter.NotNowHelp;
        AutomationProperties.SetHelpText(notNow, notNowHelp);
        ToolTipService.SetToolTip(notNow, notNowHelp);
        notNow.IsEnabled = !HasActiveOperation;
        notNow.Click += (_, _) => { firstRunQuestionAnswered = true; Render(); };
        return StewardVisuals.EmptyState("\uE774", step.Heading, step.Message, look, notNow);
    }

    /// <summary>
    /// The status card: a round icon well, the status sentence (a level-2 heading), facts with icons (freshness in words, never
    /// colour alone, FR-UI-05) and the actions on the right, which move under the sentence on narrow windows or at large text sizes.
    /// </summary>
    private Border HomeStatusCard(HomePresentation home)
    {
        Grid card = new() { ColumnSpacing = StewardSpacing.XLarge };
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.Children.Add(StewardVisuals.StatusWell(home.HeroGlyph, StewardStatus.ToneName(home.HeroTone)));

        StackPanel copy = new() { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        TextBlock sentence = new() { Text = home.Hero, Style = AppStyle("StewardSubtitleStyle"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(sentence, AutomationHeadingLevel.Level2);
        copy.Children.Add(sentence);
        StewardWrapPanel facts = new() { HorizontalSpacing = StewardSpacing.Large, VerticalSpacing = StewardSpacing.XSmall };
        foreach (HomeFact fact in home.Facts) facts.Children.Add(StewardVisuals.IconLine(fact.Glyph, fact.Text, StewardStatus.ToneName(fact.Tone)));
        // UI review 2026-11-13 (item 14): what the last scan found is the Installed apps list, which Home already links to below.
        copy.Children.Add(facts);
        // Owner request 2026-11-13: while a scan or an update check runs, how far along it is and about how long is left.
        copy.Children.Add(WorkProgressPanel());
        Grid.SetColumn(copy, 1);
        card.Children.Add(copy);

        StewardWrapPanel actions = StewardVisuals.ActionRow();
        actions.VerticalAlignment = VerticalAlignment.Center;
        actions.Children.Add(HomeActionButton(home.Primary, isPrimary: true, home.ReadyKeys));
        if (home.Secondary is { } secondary) actions.Children.Add(HomeActionButton(secondary, isPrimary: false));
        card.Children.Add(actions);
        void Place(double width)
        {
            bool narrow = width > 0 && width < 640;
            Grid.SetColumn(actions, narrow ? 1 : 2);
            Grid.SetRow(actions, narrow ? 1 : 0);
            card.RowSpacing = narrow ? StewardSpacing.Medium : 0;
        }
        Place(0);
        card.SizeChanged += (_, args) => Place(args.NewSize.Width);
        // Colour option 2: the status card is tinted with its state's tone (blue local-only or checking, green up to date, amber
        // needs attention); the sentence and facts still carry the meaning, never colour alone.
        return new Border { Style = AppStyle("StewardCardStyle"), Background = StewardVisuals.ToneTint(StewardStatus.ToneName(home.HeroTone)), Padding = new Thickness(StewardSpacing.XLarge, StewardSpacing.Section, StewardSpacing.XLarge, StewardSpacing.Section), Child = card };
    }

    /// <summary>
    /// A Home action. "Update all N…" opens one review of every ready update, exactly as the Updates page's button does; nothing
    /// is installed until the owner confirms there. "Turn on update checks…" opens the existing source consent and, if the
    /// owner approves, turns the sources on and starts nothing else. "Check for updates" and "Scan again" run the existing read-only
    /// scan (and, with sources on, the source refresh) and never ask for administrator permission.
    /// </summary>
    private Button HomeActionButton(HomeAction action, bool isPrimary, IReadOnlyList<string>? readyKeys = null)
    {
        bool scanAction = action.Kind is HomeActionKind.CheckForUpdates or HomeActionKind.ScanAgain;
        // Independent review L1: while a scan or check runs, its button says so.
        string label = scanAction && viewModel.IsScanning ? "Scanning…" : scanAction && viewModel.IsCheckingUpdates ? "Checking…"
            : action.Kind == HomeActionKind.UpdateAll && preparingReview ? PreparingReviewLabel : action.Label;
        object content = label;
        if (scanAction) content = StewardVisuals.IconLabel("\uE72C", label, 16);
        Button button = new() { Content = content, AccessKey = AccessKeyFor(action.Label) };
        if (isPrimary) button.Style = AppStyle("StewardPrimaryButtonStyle");
        // The presenter's name already starts with the visible label (HomeActivityDecisionsPresentationTests: label-in-name).
        if (label == action.Label) AutomationProperties.SetName(button, action.AccessibleName); else StewardVisuals.Named(button, label);
        AutomationProperties.SetHelpText(button, action.HelpText);
        ToolTipService.SetToolTip(button, action.HelpText);
        switch (action.Kind)
        {
            case HomeActionKind.UpdateAll:
                // The same frozen set and the same review as "Update all N…" on Updates; the review needs its own confirmation.
                string[] keys = [.. readyKeys ?? []];
                button.IsEnabled = keys.Length > 0 && !HasActiveOperation && reviewDecision is null && viewModel.IsUpdateSourceEnabled;
                button.Click += async (_, _) => await ReviewAndExecuteAsync(RowsFor(keys));
                break;
            case HomeActionKind.TurnOnUpdateChecks:
                button.IsEnabled = !HasActiveOperation;
                button.Click += async (_, _) => await ChangeWinGetTrustedSourcesAsync(true);
                break;
            default:
                button.IsEnabled = !HasActiveOperation;
                button.Click += async (_, _) => await CheckForUpdatesAsync();
                break;
        }
        return button;
    }

    /// <summary>A Home info bar whose action opens Activity with a filter that contains every app the bar names.</summary>
    private InfoBar HomeNoticeBar(HomeNotice notice)
    {
        HyperlinkButton open = new() { Content = notice.ActionLabel };
        AutomationProperties.SetName(open, notice.ActionAccessibleName);
        ActivityFilter filter = notice.ActivityFilter;
        open.Click += (_, _) => { activityFilter = filter; NavigateTo(ShellPage.Activity); };
        return StewardVisuals.InformationBanner(notice.Title, notice.Message, StewardStatus.ToneName(notice.Tone), open);
    }

    /// <summary>
    /// A Home line: one button (keyboard, Narrator and focus visuals for free) that opens its page. UI review 2026-11-13 (item
    /// 15): colour is used only for status, so a line with no status to show has a neutral icon.
    /// </summary>
    private Button HomeRowButton(HomeRow row)
    {
        string tone = StewardStatus.ToneName(row.Tone);
        Button button = StewardVisuals.NavigationRow(row.Glyph, row.Title, row.Description, tone);
        AutomationProperties.SetName(button, row.AccessibleName);
        HomeRowTarget target = row.Target;
        button.Click += (_, _) =>
        {
            // Opening a row only navigates with a filter; it never scans, checks, approves or updates anything.
            if (target.AppsFilter is { } appsFilter) viewModel.SetSavedFilter(appsFilter);
            if (target.ActivityFilter is { } filter) activityFilter = filter;
            NavigateTo(target.Page);
        };
        return button;
    }
}
