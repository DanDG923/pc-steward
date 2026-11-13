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
    /// FR-UI-01/05/06 (ADR-030 concept A; UI polish phase 2, approved targets T-Home and T-Home-Ready): info bars for updates that need
    /// the owner, then one status card (an icon, one sentence, facts with icons, and the action that fits the state), an "At a glance"
    /// list of plain-English rows that open filtered pages, and a footer line. "Turn on update checks…" opens the existing source
    /// consent; "Review updates" only opens Updates; "Check for updates" and "Scan again" never elevate. Nothing here selects,
    /// reviews, approves or runs an update.
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
            Button start = StewardVisuals.PrimaryButton("Start local inventory", "(read-only scan of this PC)");
            start.IsEnabled = !HasActiveOperation;
            start.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
            panel.Children.Add(StewardVisuals.EmptyState("\uE721", "See what is installed", "The first scan reads Windows application records on this PC. It does not request administrator access, connect to package sources, or change software.", start));
            panel.Children.Add(StewardVisuals.InformationBanner("Private by default", "Inventory and decisions stay on this PC. Package sources remain off until you approve their disclosure."));
            return panel;
        }

        // FR-UI-06: failed or unverified updates and pending restarts, as info bars above the page (approved target T-Home-Ready).
        // ADR-032 revision 2 §8: PC Steward's own recorded update comes first.
        if (SelfUpdateHomeBanner() is { } selfUpdateBanner) panel.Children.Add(selfUpdateBanner);
        foreach (HomeNotice notice in home.Notices) panel.Children.Add(HomeNoticeBar(notice));
        panel.Children.Add(StewardVisuals.PageTitle("Home"));
        panel.Children.Add(HomeStatusCard(home));
        if (activityHistoryUnavailable)
            panel.Children.Add(StewardVisuals.InformationBanner("Update history unavailable", "PC Steward couldn't read its recorded update history just now, so restart and attention counts may be incomplete. Nothing was changed.", "neutral"));

        // "At a glance": Settings-style rows, each one button that opens a filtered page, in one to three columns (three on a wide window).
        StackPanel glance = new() { Spacing = StewardSpacing.Small };
        glance.Children.Add(SectionHeading("At a glance"));
        glance.Children.Add(StewardVisuals.ResponsiveColumns([.. home.Rows.Select(HomeRowButton)], 380, StewardSpacing.XSmall, maxColumns: 3));
        panel.Children.Add(glance);
        panel.Children.Add(StewardVisuals.IconLine("\uEA18", HomePresenter.Reassurance, styleKey: "StewardCaptionStyle"));
        return panel;
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
        // The last scan's results page stays reachable from Home (audit P-H2), as a quiet link beside the scan time.
        HyperlinkButton results = new() { Content = "View scan results", Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        StewardVisuals.Named(results, "View scan results");
        results.Click += (_, _) => NavigateTo(ShellPage.Results);
        facts.Children.Add(results);
        copy.Children.Add(facts);
        Grid.SetColumn(copy, 1);
        card.Children.Add(copy);

        StewardWrapPanel actions = StewardVisuals.ActionRow();
        actions.VerticalAlignment = VerticalAlignment.Center;
        actions.Children.Add(HomeActionButton(home.Primary, isPrimary: true));
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
    /// A Home action. "Review updates" only opens Updates. "Turn on update checks…" opens the existing source consent and, if the
    /// owner approves, turns the sources on and starts nothing else. "Check for updates" and "Scan again" run the existing read-only
    /// scan (and, with sources on, the source refresh) and never ask for administrator permission.
    /// </summary>
    private Button HomeActionButton(HomeAction action, bool isPrimary)
    {
        bool scanAction = action.Kind is HomeActionKind.CheckForUpdates or HomeActionKind.ScanAgain;
        // Independent review L1: while a scan or check runs, its button says so.
        string label = scanAction && viewModel.IsScanning ? "Scanning…" : scanAction && viewModel.IsCheckingUpdates ? "Checking…" : action.Label;
        object content = label;
        if (scanAction)
        {
            StackPanel withIcon = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
            withIcon.Children.Add(new FontIcon { Glyph = "\uE72C", FontSize = 16 });
            withIcon.Children.Add(new TextBlock { Text = label });
            content = withIcon;
        }
        Button button = new() { Content = content, AccessKey = AccessKeyFor(action.Label) };
        if (isPrimary) button.Style = AppStyle("StewardPrimaryButtonStyle");
        // The presenter's name already starts with the visible label (HomeActivityDecisionsPresentationTests: label-in-name).
        if (label == action.Label) AutomationProperties.SetName(button, action.AccessibleName); else StewardVisuals.Named(button, label);
        AutomationProperties.SetHelpText(button, action.HelpText);
        ToolTipService.SetToolTip(button, action.HelpText);
        switch (action.Kind)
        {
            case HomeActionKind.ReviewUpdates:
                button.Click += (_, _) => NavigateTo(ShellPage.Updates);
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

    /// <summary>Colour option 2: each "At a glance" row keeps one decorative hue while it has no status to show.</summary>
    private static string RowHue(HomeRowKind kind) => kind switch
    {
        HomeRowKind.InstalledApps => "blue",
        HomeRowKind.Updates => "teal",
        HomeRowKind.NeedsReview => "amber",
        HomeRowKind.RestartsAndChecks => "purple",
        HomeRowKind.Decisions => "pink",
        _ => "green"
    };

    /// <summary>An "At a glance" row: one button (keyboard, Narrator and focus visuals for free) that opens its filtered page.</summary>
    private Button HomeRowButton(HomeRow row)
    {
        string tone = StewardStatus.ToneName(row.Tone);
        Button button = StewardVisuals.NavigationRow(row.Glyph, row.Title, row.Description, tone == "neutral" ? RowHue(row.Kind) : tone);
        button.VerticalAlignment = VerticalAlignment.Stretch;
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
