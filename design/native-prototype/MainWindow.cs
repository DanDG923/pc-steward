using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace PCSteward.NativePrototype;

public sealed class MainWindow : Window
{
    private readonly Grid root = new();
    private readonly NavigationView navigation = new();
    private readonly Frame contentFrame = new();
    private readonly ComboBox scenarioPicker = new();
    private readonly ToggleButton themeButton = new();
    private readonly DispatcherTimer scanTimer = new();
    private readonly Dictionary<string, Windows.UI.Color> lightPalette;
    private readonly Dictionary<string, Windows.UI.Color> darkPalette;
    private NavigationViewItem overviewItem = null!;
    private string destination = "overview";
    private string scenario = "ready";
    private double scanProgress = 42;
    private bool scanPaused;
    private bool scanCancelled;
    private bool synchronizingScenario;
    private bool windowSized;

    private static readonly ApplicationFixture[] Applications =
    [
        new("Contoso Editor", "4.1.3", "Verified update", "Contoso Software", "User", "WinGet · live"),
        new("Fabrikam Player", "12.8.1", "Current", "Fabrikam Media", "Machine", "WinGet · live"),
        new("Archive Studio", "7.4", "Review required", "Adventure Works", "Machine", "2 sources · live"),
        new("Northwind Notes", "2.3.7", "Unmanaged", "Northwind Labs", "User", "Registry · local"),
        new("Visual C++ 2015–2022 Runtime", "14.40.33810", "Current", "Microsoft Corporation", "Machine", "Registry · local"),
        new("Wingtip Screenshot Tool", "5.0.2", "Pinned", "Wingtip", "User", "WinGet · cached"),
        new("Tailspin PDF", "3.8.0", "Current", "Tailspin Toys", "Machine", "MSIX · local")
    ];

    private static readonly UpdateFixture[] Updates =
    [
        new("Contoso Editor", "Contoso Software", "4.1.3", "4.2.0", "Contoso.Editor", "User", "x64"),
        new("Woodgrove Terminal", "Woodgrove", "1.18.2", "1.19.0", "Woodgrove.Terminal", "User", "x64"),
        new("Tailspin PDF", "Tailspin Toys", "3.8.0", "3.8.4", "9P9TailspinPDF", "User", "Neutral")
    ];

    public MainWindow()
    {
        lightPalette = CapturePalette("Light");
        darkPalette = CapturePalette("Dark");
        Title = "PC Steward — Native design prototype";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "pc-steward.ico"));
        Content = root;
        BuildShell();

        scanTimer.Interval = TimeSpan.FromMilliseconds(650);
        scanTimer.Tick += ScanTimerTick;
        Activated += WindowActivated;
        Closed += (_, _) => scanTimer.Stop();
    }

    private void WindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (windowSized)
        {
            return;
        }

        windowSized = true;
        AppWindow.Resize(new SizeInt32(1280, 820));
    }

    private void BuildShell()
    {
        root.Background = ResourceBrush("StewardPageBrush");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        FrameworkElement topBar = BuildTopBar();
        root.Children.Add(topBar);

        BuildNavigation();
        Grid.SetRow(navigation, 1);
        root.Children.Add(navigation);

        root.Loaded += (_, _) =>
        {
            bool isDark = root.ActualTheme == ElementTheme.Dark;
            themeButton.IsChecked = isDark;
            themeButton.Content = isDark ? "Light theme" : "Dark theme";
            navigation.SelectedItem = overviewItem;
            RenderCurrentDestination();
        };
    }

    private FrameworkElement BuildTopBar()
    {
        Border shell = new()
        {
            Background = ResourceBrush("StewardCardBrush"),
            BorderBrush = ResourceBrush("StewardBorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 10, 18, 10)
        };

        Grid bar = new() { ColumnSpacing = 16 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel brand = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        Image logo = new()
        {
            Width = 36,
            Height = 36,
            Source = new BitmapImage(new Uri("ms-appx:///Assets/logo-mark-64.png"))
        };
        AutomationProperties.SetName(logo, "PC Steward");
        StackPanel brandCopy = new() { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        brandCopy.Children.Add(new TextBlock
        {
            Text = "PC Steward",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 16,
            Foreground = ResourceBrush("StewardTextBrush")
        });
        brandCopy.Children.Add(new TextBlock
        {
            Text = "Native Windows prototype",
            FontSize = 11,
            Foreground = ResourceBrush("StewardSecondaryTextBrush")
        });
        brand.Children.Add(logo);
        brand.Children.Add(brandCopy);
        bar.Children.Add(brand);

        StackPanel controls = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        controls.Children.Add(new TextBlock
        {
            Text = "Prototype state",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ResourceBrush("StewardSecondaryTextBrush")
        });

        (string Label, string Tag)[] scenarios =
        [
            ("First launch", "first-launch"),
            ("Ready / cached", "ready"),
            ("Scanning", "scanning"),
            ("Scan complete", "complete"),
            ("No findings", "no-findings"),
            ("Partial failure", "partial"),
            ("Offline", "offline")
        ];
        foreach ((string label, string tag) in scenarios)
        {
            scenarioPicker.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        }

        scenarioPicker.Width = 180;
        scenarioPicker.SelectedIndex = 1;
        AutomationProperties.SetName(scenarioPicker, "Prototype state");
        scenarioPicker.SelectionChanged += ScenarioSelectionChanged;
        controls.Children.Add(scenarioPicker);

        themeButton.Content = "Dark theme";
        themeButton.MinHeight = 36;
        themeButton.Click += (_, _) =>
        {
            bool useDark = themeButton.IsChecked == true;
            ApplyPalette(useDark ? darkPalette : lightPalette);
            root.RequestedTheme = useDark ? ElementTheme.Dark : ElementTheme.Light;
            themeButton.Content = useDark ? "Light theme" : "Dark theme";
        };
        AutomationProperties.SetName(themeButton, "Toggle light and dark theme");
        controls.Children.Add(themeButton);

        controls.Children.Add(StatusBadge("Stored locally", "success"));
        Grid.SetColumn(controls, 2);
        bar.Children.Add(controls);

        shell.Child = bar;
        return shell;
    }

    private void BuildNavigation()
    {
        navigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        navigation.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        navigation.IsPaneToggleButtonVisible = true;
        navigation.IsSettingsVisible = true;
        navigation.IsPaneOpen = true;
        navigation.OpenPaneLength = 230;
        navigation.CompactPaneLength = 52;
        navigation.AlwaysShowHeader = false;
        navigation.SelectionFollowsFocus = NavigationViewSelectionFollowsFocus.Disabled;
        navigation.Background = ResourceBrush("StewardPageBrush");
        navigation.Content = contentFrame;
        contentFrame.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        contentFrame.VerticalContentAlignment = VerticalAlignment.Stretch;

        navigation.MenuItems.Add(new NavigationViewItemHeader { Content = "Home" });
        overviewItem = NavigationItem("Overview", "overview", "\uE80F");
        navigation.MenuItems.Add(overviewItem);
        navigation.MenuItems.Add(NavigationItem("Scan", "scan", "\uE721"));

        navigation.MenuItems.Add(new NavigationViewItemHeader { Content = "Review" });
        navigation.MenuItems.Add(NavigationItem("Scan results", "results", "\uE9D2"));
        navigation.MenuItems.Add(NavigationItem("Review actions", "review", "\uE8C8"));
        NavigationViewItem updatesItem = NavigationItem("Updates", "updates", "\uE896");
        AutomationProperties.SetName(updatesItem, "Updates, 3 ready to review");
        navigation.MenuItems.Add(NavigationItem("Applications", "applications", "\uE71D"));
        navigation.MenuItems.Add(updatesItem);
        navigation.MenuItems.Add(NavigationItem("Startup · future", "startup", "\uE768"));
        navigation.MenuItems.Add(NavigationItem("Storage", "storage", "\uE7F1"));

        navigation.MenuItems.Add(new NavigationViewItemHeader { Content = "Records" });
        navigation.MenuItems.Add(NavigationItem("Activity", "activity", "\uE823"));
        navigation.MenuItems.Add(NavigationItem("Privacy & data", "privacy", "\uEA18"));

        NavigationViewItem about = NavigationItem("About & support", "about", "\uE897");
        navigation.FooterMenuItems.Add(about);
        navigation.SelectionChanged += NavigationSelectionChanged;
    }

    private static NavigationViewItem NavigationItem(string label, string tag, string glyph)
    {
        NavigationViewItem item = new()
        {
            Content = label,
            Tag = tag,
            Icon = new FontIcon { Glyph = glyph }
        };
        AutomationProperties.SetName(item, label);
        return item;
    }

    private void NavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            destination = "settings";
        }
        else if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            destination = tag;
        }
        else
        {
            return;
        }

        RenderCurrentDestination();
    }

    private void Navigate(string tag)
    {
        foreach (object item in navigation.MenuItems.Concat(navigation.FooterMenuItems))
        {
            if (item is NavigationViewItem navigationItem && string.Equals(navigationItem.Tag as string, tag, StringComparison.Ordinal))
            {
                if (ReferenceEquals(navigation.SelectedItem, navigationItem))
                {
                    destination = tag;
                    RenderCurrentDestination();
                }
                else
                {
                    navigation.SelectedItem = navigationItem;
                }

                return;
            }
        }

        if (string.Equals(tag, "settings", StringComparison.Ordinal))
        {
            destination = tag;
            RenderCurrentDestination();
        }
    }

    private void RenderCurrentDestination()
    {
        contentFrame.Content = destination switch
        {
            "overview" => BuildOverview(),
            "scan" => BuildScan(),
            "results" => BuildResults(),
            "review" => BuildReview(),
            "applications" => BuildApplications(),
            "updates" => BuildUpdates(),
            "startup" => BuildStartup(),
            "storage" => BuildStorage(),
            "activity" => BuildActivity(),
            "privacy" => BuildPrivacy(),
            "settings" => BuildSettings(),
            "about" => BuildAbout(),
            _ => BuildOverview()
        };

        contentFrame.Focus(FocusState.Programmatic);
    }

    private ScrollViewer CreatePage(
        string eyebrow,
        string title,
        string description,
        out StackPanel body,
        FrameworkElement? action = null)
    {
        body = new StackPanel
        {
            Spacing = 18,
            Padding = new Thickness(30, 24, 30, 48)
        };
        body.Children.Add(PageHeader(eyebrow, title, description, action));

        ScrollViewer viewer = new()
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            Background = ResourceBrush("StewardPageBrush")
        };
        AutomationProperties.SetName(viewer, title);
        return viewer;
    }

    private FrameworkElement PageHeader(string eyebrow, string title, string description, FrameworkElement? action)
    {
        Grid header = new() { ColumnSpacing = 24 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel copy = new() { Spacing = 4 };
        copy.Children.Add(Eyebrow(eyebrow));
        TextBlock titleBlock = new() { Text = title, Style = ResourceStyle("StewardPageTitleStyle") };
        AutomationProperties.SetHeadingLevel(titleBlock, AutomationHeadingLevel.Level1);
        copy.Children.Add(titleBlock);
        copy.Children.Add(BodyText(description));
        header.Children.Add(copy);

        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 1);
            header.Children.Add(action);
        }

        return header;
    }

    private FrameworkElement BuildOverview()
    {
        if (string.Equals(scenario, "first-launch", StringComparison.Ordinal))
        {
            ScrollViewer firstPage = CreatePage(
                "Overview",
                "Know what is installed. Decide what changes.",
                "PC Steward has not scanned this Windows profile yet.",
                out StackPanel firstBody,
                StatusBadge("No scan data", "neutral"));
            firstBody.Children.Add(EmptyState(
                "\uE721",
                "Start with a local-only inventory",
                "Scanning reads bounded Windows inventory sources. It does not install, remove, elevate, or contact a network source unless you approve that separately.",
                "Start first scan",
                StartScan));
            return firstPage;
        }

        if (string.Equals(scenario, "no-findings", StringComparison.Ordinal))
        {
            ScrollViewer emptyPage = CreatePage(
                "Overview",
                "Nothing needs your attention",
                "Every supported application is current. Unmanaged items remain visible rather than being labelled unsafe.",
                out StackPanel emptyBody,
                StatusBadge("Checked 4 min ago", "success"));
            emptyBody.Children.Add(EmptyState(
                "\uE930",
                "Supported software is current",
                "PC Steward checked 68 applications against enabled trusted sources and made no changes.",
                "Scan again",
                StartScan));
            return emptyPage;
        }

        string overviewTitle = string.Equals(scenario, "offline", StringComparison.Ordinal)
            ? "Local information is still available"
            : string.Equals(scenario, "partial", StringComparison.Ordinal)
                ? "Most checks completed"
                : "Three updates are ready to review";
        string overviewDescription = string.Equals(scenario, "offline", StringComparison.Ordinal)
            ? "You are offline. PC Steward is showing the last local inventory and clearly dated source results."
            : "PC Steward found clear next steps without changing anything on this PC.";

        Button reviewButton = ActionButton("Review 3 updates", true);
        reviewButton.Click += (_, _) => Navigate("updates");
        ScrollViewer page = CreatePage(
            "Overview",
            overviewTitle,
            overviewDescription,
            out StackPanel body,
            reviewButton);

        if (string.Equals(scenario, "offline", StringComparison.Ordinal))
        {
            body.Children.Add(Information(
                InfoBarSeverity.Informational,
                "Working from local records",
                "The last WinGet source result is cached from 2 days ago. No update action is available until evidence is refreshed."));
        }
        else if (string.Equals(scenario, "partial", StringComparison.Ordinal))
        {
            body.Children.Add(Information(
                InfoBarSeverity.Warning,
                "Microsoft Store source unavailable",
                "WinGet and local inventory completed. Store-backed candidates are shown as unavailable and no source is selected automatically."));
        }

        body.Children.Add(BuildOverviewHero());
        body.Children.Add(BuildMetrics());
        body.Children.Add(SectionHeading("What is worth reviewing", "4 clearly explained items"));

        body.Children.Add(FindingButton(
            "Contoso Editor 4.2 is ready to review",
            "Exact WinGet identity · live 4 min ago",
            "Verified match",
            "Update Contoso Editor from 4.1.3 to 4.2.0 after a separate confirmation."));
        body.Children.Add(FindingButton(
            "Archive Studio needs your decision",
            "Two trusted sources disagree about the newest stable version.",
            "Review required",
            "No automatic action is available until a preferred source is reviewed."));
        body.Children.Add(FindingButton(
            "Northwind Notes has no approved update source",
            "Inventoried locally; unmanaged is not the same as unsafe.",
            "No automatic action",
            "Keep the application visible or store a reviewed package mapping."));
        body.Children.Add(FindingButton(
            "640 MB of PC Steward downloads can be removed",
            "Only inactive files owned by PC Steward are included.",
            "Exact app-owned scope",
            "Delete inactive payloads while preserving installed apps and provider caches."));
        return page;
    }

    private FrameworkElement BuildOverviewHero()
    {
        Grid grid = new() { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });

        StackPanel copy = new() { Spacing = 10 };
        copy.Children.Add(Eyebrow("Evidence before action"));
        copy.Children.Add(SectionTitle("Your PC is accounted for"));
        copy.Children.Add(BodyText(
            "68 applications were found. Three verified updates can be reviewed, two items need a decision, and six applications remain unmanaged rather than hidden."));
        StackPanel actions = HorizontalActions();
        Button review = ActionButton("Review updates", true);
        review.Click += (_, _) => Navigate("updates");
        Button scan = ActionButton("Scan again", false);
        scan.Click += (_, _) => StartScan();
        actions.Children.Add(review);
        actions.Children.Add(scan);
        copy.Children.Add(actions);
        grid.Children.Add(copy);

        StackPanel evidence = new() { Spacing = 8 };
        evidence.Children.Add(EvidenceRow("Scan mode", "Inventory + trusted sources"));
        evidence.Children.Add(EvidenceRow("Completed", "Today, 10:42 · 8.6 sec"));
        evidence.Children.Add(EvidenceRow("Network", "WinGet and Store only"));
        evidence.Children.Add(EvidenceRow("Changes made", "None"));
        Grid.SetColumn(evidence, 1);
        grid.Children.Add(evidence);
        return Card(grid, "subtle");
    }

    private FrameworkElement BuildMetrics()
    {
        Grid grid = new() { ColumnSpacing = 12, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        FrameworkElement[] metrics =
        [
            Metric("3", "Ready to review", "Verified package identity", "brand"),
            Metric("2", "Need a decision", "No automatic action", "warning"),
            Metric("61", "Current", "No action expected", "success"),
            Metric("6", "Unmanaged", "Visible, not labelled unsafe", "neutral")
        ];
        for (int index = 0; index < metrics.Length; index++)
        {
            Grid.SetColumn(metrics[index], index % 2);
            Grid.SetRow(metrics[index], index / 2);
            grid.Children.Add(metrics[index]);
        }

        return grid;
    }

    private FrameworkElement BuildScan()
    {
        if (scanCancelled)
        {
            ScrollViewer cancelledPage = CreatePage(
                "Scan",
                "Scan stopped safely",
                "Active providers acknowledged cancellation. Your previous completed inventory remains available.",
                out StackPanel cancelledBody,
                StatusBadge("Cancelled", "warning"));
            cancelledBody.Children.Add(Information(
                InfoBarSeverity.Success,
                "Previous result preserved",
                "No partial inventory replaced the last completed result."));
            Button startAgain = ActionButton("Start a new scan", true);
            startAgain.Click += (_, _) => StartScan();
            cancelledBody.Children.Add(startAgain);
            return cancelledPage;
        }

        if (!string.Equals(scenario, "scanning", StringComparison.Ordinal))
        {
            ScrollViewer configPage = CreatePage(
                "Scan",
                "Choose what PC Steward may check",
                "Local inventory is always read-only. Trusted online sources require separate disclosure approval.",
                out StackPanel configBody,
                StatusBadge("No administrator access", "success"));

            StackPanel scope = new() { Spacing = 14 };
            CheckBox localInventory = new()
            {
                Content = "Windows application inventory",
                IsChecked = true,
                IsEnabled = false
            };
            AutomationProperties.SetName(localInventory, "Windows application inventory, required and read-only");
            scope.Children.Add(localInventory);
            scope.Children.Add(new ToggleSwitch
            {
                Header = "Trusted source comparison",
                OffContent = "Local only",
                OnContent = "WinGet sources",
                IsOn = false
            });
            scope.Children.Add(Information(
                InfoBarSeverity.Informational,
                "Source disclosure",
                "Enabling WinGet may disclose installed package identifiers to configured package sources. No package is installed during scanning."));
            configBody.Children.Add(Card(scope));

            Button start = ActionButton("Start scan", true);
            start.Click += (_, _) => StartScan();
            configBody.Children.Add(start);
            return configPage;
        }

        string phase = scanProgress < 30
            ? "Reading Windows inventory"
            : scanProgress < 55
                ? "Normalising installed applications"
                : scanProgress < 82
                    ? "Comparing approved sources"
                    : "Preparing results";
        string title = scanPaused ? "Scan paused" : phase;
        ScrollViewer progressPage = CreatePage(
            "Scan in progress",
            title,
            scanPaused
                ? "No scan work is advancing. Resume when you are ready or cancel safely."
                : "PC Steward is reading bounded inventory and trusted source information. Nothing is being changed.",
            out StackPanel progressBody,
            StatusBadge(scanPaused ? "Paused" : "Reading only", scanPaused ? "warning" : "brand"));

        StackPanel progressCard = new() { Spacing = 14 };
        Grid progressHeader = new();
        progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progressHeader.Children.Add(SectionTitle("Overall progress"));
        TextBlock percentage = new()
        {
            Text = $"{Math.Round(scanProgress)}%",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        Grid.SetColumn(percentage, 1);
        progressHeader.Children.Add(percentage);
        progressCard.Children.Add(progressHeader);
        ProgressBar progress = new()
        {
            Minimum = 0,
            Maximum = 100,
            Value = scanProgress,
            Height = 8
        };
        AutomationProperties.SetName(progress, $"Scan progress, {phase}, {Math.Round(scanProgress)} percent");
        progressCard.Children.Add(progress);
        progressCard.Children.Add(EvidenceRow("Windows inventory", scanProgress >= 30 ? "Completed" : "In progress"));
        progressCard.Children.Add(EvidenceRow("Application identity", scanProgress >= 55 ? "Completed" : scanProgress >= 30 ? "In progress" : "Waiting"));
        progressCard.Children.Add(EvidenceRow("Approved source comparison", scanProgress >= 82 ? "Completed" : scanProgress >= 55 ? scanPaused ? "Paused" : "In progress" : "Waiting"));
        progressCard.Children.Add(EvidenceRow("Result summary", scanProgress >= 96 ? "In progress" : "Waiting"));

        StackPanel progressActions = HorizontalActions();
        Button pause = ActionButton(scanPaused ? "Resume scan" : "Pause", false);
        pause.Click += (_, _) =>
        {
            scanPaused = !scanPaused;
            RenderCurrentDestination();
        };
        Button cancel = ActionButton("Cancel scan", false);
        cancel.Click += (_, _) => CancelScan();
        progressActions.Children.Add(pause);
        progressActions.Children.Add(cancel);
        progressCard.Children.Add(progressActions);
        progressBody.Children.Add(Card(progressCard));
        progressBody.Children.Add(BodyText(
            "Pause is a design concept. Production exposes it only after the scan orchestrator supports a safe pause state."));
        return progressPage;
    }

    private void StartScan()
    {
        scenario = "scanning";
        scanProgress = 18;
        scanPaused = false;
        scanCancelled = false;
        SynchronizeScenarioPicker("scanning");
        scanTimer.Start();
        Navigate("scan");
    }

    private void CancelScan()
    {
        scanTimer.Stop();
        scenario = "ready";
        scanPaused = false;
        scanCancelled = true;
        SynchronizeScenarioPicker("ready");
        RenderCurrentDestination();
    }

    private void ScanTimerTick(object? sender, object e)
    {
        if (scanPaused)
        {
            return;
        }

        scanProgress = Math.Min(100, scanProgress + 4);
        if (scanProgress >= 100)
        {
            scanTimer.Stop();
            scenario = "complete";
            SynchronizeScenarioPicker("complete");
            if (string.Equals(destination, "scan", StringComparison.Ordinal))
            {
                Navigate("results");
            }

            return;
        }

        if (string.Equals(destination, "scan", StringComparison.Ordinal))
        {
            RenderCurrentDestination();
        }
    }

    private void ScenarioSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (synchronizingScenario || scenarioPicker.SelectedItem is not ComboBoxItem item || item.Tag is not string selected)
        {
            return;
        }

        scanTimer.Stop();
        scanCancelled = false;
        scenario = selected;
        switch (selected)
        {
            case "first-launch":
                Navigate("overview");
                _ = ShowOnboardingAsync();
                break;
            case "scanning":
                StartScan();
                break;
            case "complete":
                Navigate("results");
                break;
            default:
                Navigate("overview");
                break;
        }
    }

    private void SynchronizeScenarioPicker(string tag)
    {
        synchronizingScenario = true;
        foreach (object item in scenarioPicker.Items)
        {
            if (item is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Tag as string, tag, StringComparison.Ordinal))
            {
                scenarioPicker.SelectedItem = comboBoxItem;
                break;
            }
        }

        synchronizingScenario = false;
    }

    private FrameworkElement BuildResults()
    {
        ScrollViewer page = CreatePage(
            "Scan results",
            string.Equals(scenario, "no-findings", StringComparison.Ordinal) ? "No supported changes found" : "Review evidence before choosing an action",
            "Results distinguish verified opportunities, decisions, unmanaged software and app-owned cleanup.",
            out StackPanel body,
            StatusBadge(string.Equals(scenario, "partial", StringComparison.Ordinal) ? "Partial · 1 source unavailable" : "Checked 4 min ago", string.Equals(scenario, "partial", StringComparison.Ordinal) ? "warning" : "success"));

        StackPanel filters = HorizontalActions();
        filters.Children.Add(ActionButton("All 4", true));
        filters.Children.Add(ActionButton("Actionable 2", false));
        filters.Children.Add(ActionButton("Needs decision 1", false));
        filters.Children.Add(ActionButton("Unmanaged 1", false));
        body.Children.Add(filters);
        body.Children.Add(FindingButton(
            "Contoso Editor 4.2 is ready to review",
            "WinGet · exact package identity · compatible user scope",
            "Verified match",
            "Update only after a second confirmation and post-update verification."));
        body.Children.Add(FindingButton(
            "Archive Studio source disagreement",
            "WinGet reports 7.5; Store reports 7.4.1.",
            "Review required",
            "No source is selected automatically."));
        body.Children.Add(FindingButton(
            "Northwind Notes is unmanaged",
            "Registry inventory · no approved package mapping",
            "Information",
            "No automatic action is proposed."));
        body.Children.Add(FindingButton(
            "640 MB of downloaded files can be cleared",
            "PC Steward download ledger · exact app-owned scope",
            "Safe after confirmation",
            "Provider caches and active files are preserved."));
        Button review = ActionButton("Review selected actions", true);
        review.Click += (_, _) => Navigate("review");
        body.Children.Add(review);
        return page;
    }

    private FrameworkElement BuildReview()
    {
        ScrollViewer page = CreatePage(
            "Review actions",
            "Two approved actions are ready to confirm",
            "The queue is frozen for review. If evidence changes, execution stops and asks again.",
            out StackPanel body,
            StatusBadge("Nothing has changed yet", "neutral"));

        StackPanel queue = new() { Spacing = 12 };
        queue.Children.Add(ReviewRow(
            "Contoso Editor 4.1.3 → 4.2.0",
            "WinGet · exact identity · user scope · no administrator prompt expected"));
        queue.Children.Add(ReviewRow(
            "Delete 640 MB of inactive PC Steward downloads",
            "Preserves active files, scan data, installed apps and every provider cache"));
        body.Children.Add(Card(queue));
        body.Children.Add(Information(
            InfoBarSeverity.Informational,
            "Sequential and verified",
            "Updates run one at a time. PC Steward checks the installed version again before recording success."));
        Button confirm = ActionButton("Confirm 2 actions", true);
        confirm.Click += async (_, _) => await ShowQueueConfirmationAsync();
        body.Children.Add(confirm);
        return page;
    }

    private FrameworkElement BuildApplications()
    {
        ScrollViewer page = CreatePage(
            "Applications",
            "Installed application inventory",
            "Canonical products appear once while user, machine and architecture instances remain inspectable.",
            out StackPanel body,
            StatusBadge("Local inventory · 9 min ago", "neutral"));

        Grid filters = new() { ColumnSpacing = 10 };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        TextBox search = new()
        {
            PlaceholderText = "Search applications or publishers",
            Header = "Search applications"
        };
        ComboBox status = new()
        {
            Header = "Status",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (string option in new[] { "All statuses", "Verified update", "Review required", "Current", "Unmanaged", "Pinned" })
        {
            status.Items.Add(option);
        }

        status.SelectedIndex = 0;
        filters.Children.Add(search);
        Grid.SetColumn(status, 1);
        filters.Children.Add(status);
        body.Children.Add(filters);

        StackPanel rows = new() { Spacing = 8 };
        body.Children.Add(rows);

        void RefreshRows()
        {
            rows.Children.Clear();
            string query = search.Text.Trim();
            string selectedStatus = status.SelectedItem as string ?? "All statuses";
            ApplicationFixture[] visible = Applications
                .Where(application =>
                    (query.Length == 0 ||
                     application.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     application.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                    (string.Equals(selectedStatus, "All statuses", StringComparison.Ordinal) ||
                     string.Equals(application.Status, selectedStatus, StringComparison.Ordinal)))
                .ToArray();
            rows.Children.Add(BodyText($"{visible.Length} applications"));
            foreach (ApplicationFixture application in visible)
            {
                rows.Children.Add(ApplicationRow(application));
            }

            if (visible.Length == 0)
            {
                rows.Children.Add(EmptyState(
                    "\uE721",
                    "No applications match these filters",
                    "Clear search or choose another status. The inventory has not changed.",
                    "Clear filters",
                    () =>
                    {
                        search.Text = string.Empty;
                        status.SelectedIndex = 0;
                    }));
            }
        }

        search.TextChanged += (_, _) => RefreshRows();
        status.SelectionChanged += (_, _) => RefreshRows();
        RefreshRows();
        return page;
    }

    private FrameworkElement ApplicationRow(ApplicationFixture application)
    {
        Grid row = new() { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });

        Button name = new()
        {
            Content = application.Name,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Style = ResourceStyle("StewardButtonStyle")
        };
        AutomationProperties.SetName(name, $"Open {application.Name} application details");
        name.Click += async (_, _) => await ShowApplicationDialogAsync(application);
        row.Children.Add(name);
        AddGridText(row, application.Version, 1);
        FrameworkElement status = StatusBadge(application.Status, StatusTone(application.Status));
        Grid.SetColumn(status, 2);
        row.Children.Add(status);
        AddGridText(row, $"{application.Scope} · {application.Source}", 3);
        return Card(row);
    }

    private FrameworkElement BuildUpdates()
    {
        ScrollViewer page = CreatePage(
            "Updates",
            "Review trusted update evidence",
            "Only fresh, compatible and explicitly reviewed package identities can move into an update queue.",
            out StackPanel body,
            StatusBadge("WinGet · live 4 min ago", "success"));

        foreach (UpdateFixture update in Updates)
        {
            Grid row = new() { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel copy = new() { Spacing = 5 };
            copy.Children.Add(SectionTitle(update.Name));
            copy.Children.Add(BodyText(update.Publisher));
            copy.Children.Add(new TextBlock
            {
                Text = $"{update.CurrentVersion}  →  {update.TargetVersion}",
                FontFamily = new FontFamily("Cascadia Mono"),
                Foreground = ResourceBrush("StewardTextBrush")
            });
            copy.Children.Add(BodyText($"{update.Scope} scope · {update.Architecture} · WinGet live"));
            copy.Children.Add(BodyText(update.PackageId));
            row.Children.Add(copy);

            Button review = ActionButton("Review evidence", false);
            review.Click += async (_, _) => await ShowUpdateDialogAsync(update);
            Grid.SetColumn(review, 1);
            row.Children.Add(review);
            body.Children.Add(Card(row));
        }

        body.Children.Add(Information(
            InfoBarSeverity.Warning,
            "Archive Studio has source disagreement",
            "WinGet reports 7.5 while Store reports 7.4.1. Both timestamps remain visible and neither source is selected automatically."));
        return page;
    }

    private FrameworkElement BuildStartup()
    {
        ScrollViewer page = CreatePage(
            "Startup · future concept",
            "Review what begins with Windows",
            "This read-only concept is outside the approved PC Steward MVP and has no production action.",
            out StackPanel body,
            StatusBadge("Future scope", "warning"));
        body.Children.Add(Information(
            InfoBarSeverity.Warning,
            "Not approved for implementation",
            "Startup control requires amended product requirements, threat modelling and acceptance criteria."));
        body.Children.Add(SimpleDataRow("Cloud Sync", "Enabled", "Registry · current user", "Evidence not yet defined"));
        body.Children.Add(SimpleDataRow("Contoso Chat", "Enabled", "Startup task", "Evidence not yet defined"));
        return page;
    }

    private FrameworkElement BuildStorage()
    {
        ScrollViewer page = CreatePage(
            "Storage",
            "Storage used by PC Steward",
            "The approved scope covers only PC Steward-owned data and inactive update files.",
            out StackPanel body,
            StatusBadge("1.34 GB total", "neutral"));

        StackPanel storage = new() { Spacing = 12 };
        storage.Children.Add(SectionTitle("App-owned storage"));
        storage.Children.Add(StorageBar("Scan data", 342, "brand"));
        storage.Children.Add(StorageBar("Downloaded update files", 640, "warning"));
        storage.Children.Add(StorageBar("Source metadata", 198, "success"));
        storage.Children.Add(StorageBar("Logs & update history", 160, "neutral"));
        body.Children.Add(Card(storage));
        body.Children.Add(Information(
            InfoBarSeverity.Informational,
            "What PC Steward will not touch",
            "Windows temporary files, browser data, Windows Update, Store and WinGet caches, documents, arbitrary folders, drivers and firmware are outside this scope."));

        StackPanel future = new() { Spacing = 8 };
        future.Children.Add(Eyebrow("Future concept"));
        future.Children.Add(SectionTitle("Device-wide storage analysis"));
        future.Children.Add(BodyText(
            "A future read-only design only. It must not enter production without approved collection boundaries and deletion authorities."));
        body.Children.Add(Card(future, "subtle"));
        return page;
    }

    private FrameworkElement BuildActivity()
    {
        ScrollViewer page = CreatePage(
            "Activity",
            "A local record of what happened",
            "Scan, decision and update outcomes remain bounded, sanitized and independently inspectable.",
            out StackPanel body,
            StatusBadge("Stored locally", "success"));
        body.Children.Add(ActivityRow("Today, 10:42", "Software inventory and update check", "Completed with note", "68 applications · 1 provider warning"));
        body.Children.Add(ActivityRow("Yesterday, 18:16", "Fabrikam Player updated", "Verified success", "12.7.6 → 12.8.1 · exact post-update verification"));
        body.Children.Add(ActivityRow("25 Jul, 14:03", "Current version pinned", "Decision", "Wingtip Screenshot Tool · compatibility reason"));
        body.Children.Add(ActivityRow("21 Jul, 09:28", "Downloaded files cleared", "Completed", "412 MB recovered · provider caches preserved"));
        return page;
    }

    private FrameworkElement BuildPrivacy()
    {
        ScrollViewer page = CreatePage(
            "Privacy & data",
            "Your PC Steward data stays on this PC",
            "No account is required. Each local data category has a separate lifecycle and confirmation.",
            out StackPanel body,
            StatusBadge("Local-first", "success"));

        body.Children.Add(Information(
            InfoBarSeverity.Success,
            "No inventory upload or background telemetry",
            "The last approved network contact was WinGet at 10:42. Local data clearing works offline."));
        body.Children.Add(DataCategoryCard(
            "Scan data",
            "342 MB · 68 applications",
            "Inventory, raw provider records, candidates and scan runs.",
            "Clear scan data",
            () => _ = ShowDeleteScopeAsync(
                "Clear scan data?",
                ["Current inventory", "Raw provider records", "Update candidates", "Scan-run records"],
                ["Settings", "Enabled connectors", "Schedules", "Update history", "Downloaded files", "Mappings, pins and ignores"])));
        body.Children.Add(DataCategoryCard(
            "Downloaded update files",
            "640 MB recoverable",
            "Inactive payloads and temporary extraction files owned by PC Steward.",
            "Clear downloaded files",
            () => _ = ShowDeleteScopeAsync(
                "Delete 640 MB of downloaded files?",
                ["Inactive PC Steward update payloads", "Inactive temporary extraction files"],
                ["Installed applications", "Active update files", "Scan data and update history", "Windows, WinGet, Store and other provider caches"])));
        body.Children.Add(DataCategoryCard(
            "Source metadata",
            "198 MB · checked 4 min ago",
            "Replaceable cached package information from enabled trusted sources.",
            "Manage retention",
            () => Navigate("settings")));
        body.Children.Add(DataCategoryCard(
            "Logs & update history",
            "160 MB · 24 events",
            "Bounded, sanitized records of scans, decisions and update outcomes.",
            "View activity",
            () => Navigate("activity")));

        StackPanel reset = new() { Spacing = 10 };
        reset.Children.Add(Eyebrow("Separate destructive area"));
        reset.Children.Add(SectionTitle("Factory reset PC Steward"));
        reset.Children.Add(BodyText(
            "Removes all local PC Steward state and the current-user schedule. PC Steward remains installed and every third-party application is preserved."));
        Button factoryReset = ActionButton("Factory reset PC Steward", false);
        factoryReset.Click += async (_, _) => await ShowFactoryResetAsync();
        reset.Children.Add(factoryReset);
        body.Children.Add(Card(reset));
        return page;
    }

    private FrameworkElement BuildSettings()
    {
        ScrollViewer page = CreatePage(
            "Settings",
            "Preferences and trusted sources",
            "Settings describe their privacy and resource impact before they change.",
            out StackPanel body,
            StatusBadge("Saved locally", "success"));

        StackPanel appearance = new() { Spacing = 12 };
        appearance.Children.Add(SectionTitle("Appearance"));
        ComboBox theme = new() { Header = "App theme", Width = 260 };
        theme.Items.Add("Use Windows setting");
        theme.Items.Add("Light");
        theme.Items.Add("Dark");
        theme.SelectedIndex = 0;
        theme.SelectionChanged += (_, _) =>
        {
            root.RequestedTheme = theme.SelectedIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        };
        appearance.Children.Add(theme);
        appearance.Children.Add(new ToggleSwitch
        {
            Header = "Reduced motion",
            OffContent = "Use Windows animation setting",
            OnContent = "Minimise authored motion"
        });
        body.Children.Add(Card(appearance));

        StackPanel sources = new() { Spacing = 12 };
        sources.Children.Add(SectionTitle("Inventory and update sources"));
        sources.Children.Add(new ToggleSwitch
        {
            Header = "Windows local inventory",
            IsOn = true,
            IsEnabled = false,
            OnContent = "Required · local only"
        });
        ToggleSwitch winget = new()
        {
            Header = "WinGet trusted sources",
            OffContent = "Disabled",
            OnContent = "Enabled after disclosure"
        };
        winget.Toggled += async (_, _) =>
        {
            if (!winget.IsOn)
            {
                return;
            }

            ContentDialog consent = new()
            {
                XamlRoot = root.XamlRoot,
                Title = "Enable WinGet trusted sources?",
                Content = "WinGet may disclose installed package identifiers to configured package sources. Scanning remains read-only.",
                PrimaryButtonText = "I approve",
                CloseButtonText = "Keep local-only",
                DefaultButton = ContentDialogButton.Close
            };
            if (await consent.ShowAsync() != ContentDialogResult.Primary)
            {
                winget.IsOn = false;
            }
        };
        sources.Children.Add(winget);
        body.Children.Add(Card(sources));

        StackPanel schedule = new() { Spacing = 12 };
        schedule.Children.Add(SectionTitle("Scheduled checks"));
        schedule.Children.Add(new ToggleSwitch
        {
            Header = "Weekly scan-only task",
            OffContent = "Not scheduled",
            OnContent = "Current-user task · no resident service"
        });
        schedule.Children.Add(BodyText(
            "A scheduled task starts PC Steward only for the bounded scan and exits afterward. It never performs updates automatically."));
        body.Children.Add(Card(schedule));
        return page;
    }

    private FrameworkElement BuildAbout()
    {
        ScrollViewer page = CreatePage(
            "About & support",
            "PC Steward",
            "A local-first software inventory and trusted update steward for Windows 11.",
            out StackPanel body,
            StatusBadge("Native WinUI 3 prototype", "brand"));

        StackPanel about = new() { Spacing = 10 };
        Image logo = new()
        {
            Width = 64,
            Height = 64,
            HorizontalAlignment = HorizontalAlignment.Left,
            Source = new BitmapImage(new Uri("ms-appx:///Assets/logo-mark-64.png"))
        };
        about.Children.Add(logo);
        about.Children.Add(SectionTitle("PC Steward 0.1 private-beta design"));
        about.Children.Add(BodyText("Windows 11 x64 · Windows App SDK · WinUI 3 · account-free"));
        about.Children.Add(EvidenceRow("Prototype safety", "No scanning or system changes"));
        about.Children.Add(EvidenceRow("Production scope", "Inventory, trusted updates, decisions and app-owned data"));
        about.Children.Add(EvidenceRow("Not included", "Drivers, firmware, Windows Update, registry cleaning or arbitrary web search"));
        body.Children.Add(Card(about));
        return page;
    }

    private FrameworkElement FindingButton(string title, string summary, string status, string effect)
    {
        Button button = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(12),
            Background = ResourceBrush("StewardCardBrush"),
            BorderBrush = ResourceBrush("StewardBorderBrush"),
            BorderThickness = new Thickness(1)
        };
        AutomationProperties.SetName(button, $"{title}. {status}. Open evidence.");

        Grid content = new() { ColumnSpacing = 16 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel copy = new() { Spacing = 4 };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("StewardTextBrush")
        });
        copy.Children.Add(BodyText(summary));
        content.Children.Add(copy);
        FrameworkElement badge = StatusBadge(status, StatusTone(status));
        Grid.SetColumn(badge, 1);
        content.Children.Add(badge);
        button.Content = content;
        button.Click += async (_, _) => await ShowFindingDialogAsync(title, summary, status, effect);
        return button;
    }

    private async Task ShowFindingDialogAsync(string title, string evidence, string status, string effect)
    {
        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(StatusBadge(status, StatusTone(status)));
        content.Children.Add(SectionTitle("Why this appears"));
        content.Children.Add(EvidenceRow("Evidence", evidence));
        content.Children.Add(EvidenceRow("Actionability", status.Contains("Verified", StringComparison.OrdinalIgnoreCase) ? "Supported after review" : "No automatic action"));
        content.Children.Add(EvidenceRow("Changes made", "None"));
        content.Children.Add(SectionTitle("What PC Steward can do"));
        content.Children.Add(BodyText(effect));
        content.Children.Add(SectionTitle("What is preserved"));
        content.Children.Add(BodyText("Documents, settings, unrelated applications and every provider-owned cache."));

        ContentDialog dialog = new()
        {
            XamlRoot = root.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = status.Contains("Verified", StringComparison.OrdinalIgnoreCase) ? "Add to review" : string.Empty,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            Navigate("review");
        }
    }

    private async Task ShowApplicationDialogAsync(ApplicationFixture application)
    {
        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(StatusBadge(application.Status, StatusTone(application.Status)));
        content.Children.Add(EvidenceRow("Installed version", application.Version));
        content.Children.Add(EvidenceRow("Publisher", application.Publisher));
        content.Children.Add(EvidenceRow("Scope", application.Scope));
        content.Children.Add(EvidenceRow("Evidence", application.Source));
        content.Children.Add(EvidenceRow("Actionability", string.Equals(application.Status, "Verified update", StringComparison.Ordinal) ? "Review update evidence" : "No automatic action"));
        content.Children.Add(BodyText("No production system state is read or changed by this prototype."));
        ContentDialog dialog = new()
        {
            XamlRoot = root.XamlRoot,
            Title = application.Name,
            Content = content,
            CloseButtonText = "Close"
        };
        await dialog.ShowAsync();
    }

    private async Task ShowUpdateDialogAsync(UpdateFixture update)
    {
        Grid comparison = new() { ColumnSpacing = 16 };
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StackPanel installed = new() { Spacing = 6 };
        installed.Children.Add(Eyebrow("Installed"));
        installed.Children.Add(SectionTitle(update.Name));
        installed.Children.Add(EvidenceRow("Version", update.CurrentVersion));
        installed.Children.Add(EvidenceRow("Publisher", update.Publisher));
        installed.Children.Add(EvidenceRow("Scope", update.Scope));
        installed.Children.Add(EvidenceRow("Architecture", update.Architecture));
        comparison.Children.Add(Card(installed, "subtle"));

        StackPanel candidate = new() { Spacing = 6 };
        candidate.Children.Add(Eyebrow("Candidate"));
        candidate.Children.Add(SectionTitle(update.PackageId));
        candidate.Children.Add(EvidenceRow("Version", update.TargetVersion));
        candidate.Children.Add(EvidenceRow("Source", "WinGet"));
        candidate.Children.Add(EvidenceRow("Scope", update.Scope));
        candidate.Children.Add(EvidenceRow("Architecture", update.Architecture));
        FrameworkElement candidateCard = Card(candidate, "subtle");
        Grid.SetColumn(candidateCard, 1);
        comparison.Children.Add(candidateCard);

        StackPanel content = new() { Spacing = 14 };
        content.Children.Add(comparison);
        content.Children.Add(Information(
            InfoBarSeverity.Success,
            "Verified match",
            "Exact package identity and compatible target. Live evidence checked 4 minutes ago."));
        content.Children.Add(Information(
            InfoBarSeverity.Informational,
            "No administrator prompt expected",
            "This is a user-scope update. PC Steward will verify the installed version afterward."));

        ContentDialog review = new()
        {
            XamlRoot = root.XamlRoot,
            Title = "Review approved update",
            Content = content,
            PrimaryButtonText = "Install approved update",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await review.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ContentDialog outcome = new()
        {
            XamlRoot = root.XamlRoot,
            Title = "Prototype action completed",
            Content = $"{update.Name} {update.TargetVersion} was simulated and verified. No system action was performed.",
            CloseButtonText = "Done"
        };
        await outcome.ShowAsync();
    }

    private async Task ShowQueueConfirmationAsync()
    {
        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(ReviewRow(
            "1. Update Contoso Editor",
            "4.1.3 → 4.2.0 · exact WinGet identity · verify afterward"));
        content.Children.Add(ReviewRow(
            "2. Delete inactive PC Steward downloads",
            "640 MB · provider caches and active files preserved"));
        content.Children.Add(Information(
            InfoBarSeverity.Informational,
            "Prototype only",
            "This dialog demonstrates review and confirmation. It cannot call any production service."));
        ContentDialog dialog = new()
        {
            XamlRoot = root.XamlRoot,
            Title = "Confirm 2 actions?",
            Content = content,
            PrimaryButtonText = "Confirm 2 actions",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ContentDialog outcome = new()
        {
            XamlRoot = root.XamlRoot,
            Title = "Actions simulated safely",
            Content = "One verified update and 640 MB of app-owned cleanup were recorded as prototype outcomes. No Windows state changed.",
            PrimaryButtonText = "View activity",
            CloseButtonText = "Done"
        };
        if (await outcome.ShowAsync() == ContentDialogResult.Primary)
        {
            Navigate("activity");
        }
    }

    private async Task ShowOnboardingAsync()
    {
        RadioButton local = new()
        {
            Content = "Local-only inventory — recommended",
            GroupName = "FirstScanMode",
            IsChecked = true
        };
        RadioButton online = new()
        {
            Content = "Inventory + trusted-source update check",
            GroupName = "FirstScanMode"
        };
        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(SectionTitle("Stored on this PC"));
        content.Children.Add(BodyText("No account or inventory upload."));
        content.Children.Add(SectionTitle("Scanning reads only"));
        content.Children.Add(BodyText("No install, removal or elevation."));
        content.Children.Add(SectionTitle("You approve every change"));
        content.Children.Add(BodyText("Evidence appears before action."));
        content.Children.Add(local);
        content.Children.Add(online);
        content.Children.Add(Information(
            InfoBarSeverity.Informational,
            "Network disclosure",
            "The online option may disclose installed package identifiers to configured WinGet sources."));

        ContentDialog dialog = new()
        {
            XamlRoot = root.XamlRoot,
            Title = "Know what is installed. Decide what changes.",
            Content = content,
            PrimaryButtonText = "Start first scan",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            StartScan();
        }
    }

    private async Task ShowDeleteScopeAsync(string title, string[] deletes, string[] preserves)
    {
        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(SectionTitle("Deletes"));
        content.Children.Add(BulletList(deletes));
        content.Children.Add(SectionTitle("Preserves"));
        content.Children.Add(BulletList(preserves));
        content.Children.Add(Information(
            InfoBarSeverity.Informational,
            "No administrator access",
            "Cleanup works offline and reports locked or protected residuals accurately."));

        ContentDialog dialog = new()
        {
            XamlRoot = root.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = title.StartsWith("Delete", StringComparison.Ordinal) ? "Delete downloaded files" : "Clear scan data",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ContentDialog outcome = new()
        {
            XamlRoot = root.XamlRoot,
            Title = "Prototype cleanup complete",
            Content = "The selected category was simulated as cleared. No file or database was changed.",
            CloseButtonText = "Done"
        };
        await outcome.ShowAsync();
    }

    private async Task ShowFactoryResetAsync()
    {
        StackPanel scope = new() { Spacing = 12 };
        scope.Children.Add(SectionTitle("Deletes"));
        scope.Children.Add(BulletList(
        [
            "Database, inventory and scan history",
            "Source metadata, logs and update history",
            "Downloaded files",
            "Mappings, pins, ignores and settings",
            "Consent records and current-user schedule"
        ]));
        scope.Children.Add(SectionTitle("Preserves"));
        scope.Children.Add(BulletList(
        [
            "PC Steward application binaries",
            "Every installed third-party application",
            "All provider-owned caches"
        ]));
        scope.Children.Add(Information(
            InfoBarSeverity.Error,
            "This cannot be undone",
            "A second, separate confirmation is required. No reset has been scheduled yet."));

        ContentDialog first = new()
        {
            XamlRoot = root.XamlRoot,
            Title = "Review factory reset scope",
            Content = scope,
            PrimaryButtonText = "Continue to final confirmation",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await first.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        TextBox phrase = new()
        {
            Header = "Confirmation phrase",
            PlaceholderText = "Type RESET"
        };
        StackPanel finalContent = new() { Spacing = 12 };
        finalContent.Children.Add(BodyText(
            "Type RESET to confirm. Pressing Enter in the field does not activate the reset action."));
        finalContent.Children.Add(phrase);
        finalContent.Children.Add(BodyText(
            "PC Steward remains installed. Installed applications and provider-owned caches remain unchanged."));
        ContentDialog second = new()
        {
            XamlRoot = root.XamlRoot,
            Title = "This cannot be undone",
            Content = finalContent,
            PrimaryButtonText = "Factory reset PC Steward",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };
        phrase.TextChanged += (_, _) =>
        {
            second.IsPrimaryButtonEnabled = string.Equals(phrase.Text.Trim(), "RESET", StringComparison.Ordinal);
        };
        if (await second.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        scenario = "first-launch";
        SynchronizeScenarioPicker("first-launch");
        Navigate("overview");
        ContentDialog outcome = new()
        {
            XamlRoot = root.XamlRoot,
            Title = "Reset will complete on the next launch",
            Content = "This prototype did not close the app or delete data. Production must verify the reset manifest before showing onboarding.",
            CloseButtonText = "Done"
        };
        await outcome.ShowAsync();
    }

    private FrameworkElement DataCategoryCard(string title, string amount, string description, string actionLabel, Action action)
    {
        Grid grid = new() { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel copy = new() { Spacing = 4 };
        copy.Children.Add(SectionTitle(title));
        copy.Children.Add(new TextBlock
        {
            Text = amount,
            Foreground = ResourceBrush("StewardBrandTextBrush"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        copy.Children.Add(BodyText(description));
        grid.Children.Add(copy);
        Button button = ActionButton(actionLabel, false);
        button.Click += (_, _) => action();
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return Card(grid);
    }

    private FrameworkElement ActivityRow(string time, string title, string status, string detail)
    {
        Grid grid = new() { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(BodyText(time));
        StackPanel copy = new() { Spacing = 4 };
        copy.Children.Add(SectionTitle(title));
        copy.Children.Add(BodyText(detail));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        FrameworkElement badge = StatusBadge(status, StatusTone(status));
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);
        return Card(grid);
    }

    private FrameworkElement SimpleDataRow(string name, string status, string source, string evidence)
    {
        Grid grid = new() { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.6, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddGridText(grid, name, 0, true);
        AddGridText(grid, status, 1);
        AddGridText(grid, source, 2);
        AddGridText(grid, evidence, 3);
        return Card(grid);
    }

    private FrameworkElement ReviewRow(string title, string detail)
    {
        Grid row = new() { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        CheckBox selected = new() { IsChecked = true, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(selected, $"Select {title}");
        row.Children.Add(selected);
        StackPanel copy = new() { Spacing = 4 };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        copy.Children.Add(BodyText(detail));
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        return row;
    }

    private FrameworkElement StorageBar(string label, double megabytes, string tone)
    {
        StackPanel panel = new() { Spacing = 5 };
        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = label, Foreground = ResourceBrush("StewardTextBrush") });
        TextBlock amount = new() { Text = $"{megabytes:0} MB", Foreground = ResourceBrush("StewardSecondaryTextBrush") };
        Grid.SetColumn(amount, 1);
        header.Children.Add(amount);
        panel.Children.Add(header);
        ProgressBar bar = new()
        {
            Minimum = 0,
            Maximum = 700,
            Value = megabytes,
            Height = 8,
            Foreground = ResourceBrush(ToneBrush(tone, false))
        };
        AutomationProperties.SetName(bar, $"{label}, {megabytes:0} megabytes");
        panel.Children.Add(bar);
        return panel;
    }

    private FrameworkElement EmptyState(string glyph, string title, string description, string actionLabel, Action action)
    {
        StackPanel content = new()
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 620
        };
        FontIcon icon = new()
        {
            Glyph = glyph,
            FontSize = 54,
            Foreground = ResourceBrush("StewardBrandBrush")
        };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        content.Children.Add(icon);
        TextBlock heading = SectionTitle(title);
        heading.TextAlignment = TextAlignment.Center;
        content.Children.Add(heading);
        TextBlock body = BodyText(description);
        body.TextAlignment = TextAlignment.Center;
        content.Children.Add(body);
        Button button = ActionButton(actionLabel, true);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Click += (_, _) => action();
        content.Children.Add(button);
        Border card = Card(content);
        card.Padding = new Thickness(38);
        return card;
    }

    private FrameworkElement Metric(string value, string label, string detail, string tone)
    {
        StackPanel content = new() { Spacing = 5 };
        content.Children.Add(new TextBlock
        {
            Text = value,
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 26,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ResourceBrush(ToneBrush(tone, false))
        });
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ResourceBrush("StewardTextBrush")
        });
        content.Children.Add(BodyText(detail));
        Border card = Card(content);
        AutomationProperties.SetName(card, $"{value}, {label}. {detail}");
        return card;
    }

    private FrameworkElement EvidenceRow(string label, string value)
    {
        Grid row = new() { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.72, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.28, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ResourceBrush("StewardSecondaryTextBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        TextBlock valueText = new()
        {
            Text = value,
            Foreground = ResourceBrush("StewardTextBrush"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return row;
    }

    private FrameworkElement SectionHeading(string title, string detail)
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(SectionTitle(title));
        TextBlock detailText = BodyText(detail);
        detailText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(detailText, 1);
        grid.Children.Add(detailText);
        return grid;
    }

    private static StackPanel HorizontalActions() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8
    };

    private Button ActionButton(string label, bool primary)
    {
        Button button = new()
        {
            Content = label,
            Style = primary
                ? (Style)Application.Current.Resources["AccentButtonStyle"]
                : ResourceStyle("StewardButtonStyle")
        };
        AutomationProperties.SetName(button, label);
        return button;
    }

    private Border Card(UIElement child, string tone = "default")
    {
        Border border = new()
        {
            Style = ResourceStyle("StewardCardStyle"),
            Child = child
        };
        if (string.Equals(tone, "subtle", StringComparison.Ordinal))
        {
            border.Background = ResourceBrush("StewardSubtleBrush");
        }

        return border;
    }

    private FrameworkElement Information(InfoBarSeverity severity, string title, string message)
    {
        InfoBar info = new()
        {
            IsOpen = true,
            IsClosable = false,
            Severity = severity,
            Title = title,
            Message = message
        };
        AutomationProperties.SetName(info, $"{title}. {message}");
        return info;
    }

    private FrameworkElement StatusBadge(string label, string tone)
    {
        Border badge = new()
        {
            Background = ResourceBrush(ToneBrush(tone, true)),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(9, 4, 9, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        badge.Child = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ResourceBrush(ToneBrush(tone, false))
        };
        AutomationProperties.SetName(badge, label);
        return badge;
    }

    private static string ToneBrush(string tone, bool surface)
    {
        return tone switch
        {
            "success" => surface ? "StewardSuccessSurfaceBrush" : "StewardSuccessBrush",
            "warning" => surface ? "StewardWarningSurfaceBrush" : "StewardWarningBrush",
            "danger" => surface ? "StewardDangerSurfaceBrush" : "StewardDangerBrush",
            "brand" => surface ? "StewardSubtleBrush" : "StewardBrandTextBrush",
            _ => surface ? "StewardSubtleBrush" : "StewardSecondaryTextBrush"
        };
    }

    private static string StatusTone(string status)
    {
        if (status.Contains("Verified", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Current", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Completed", StringComparison.OrdinalIgnoreCase))
        {
            return "success";
        }

        if (status.Contains("Review", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("decision", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Partial", StringComparison.OrdinalIgnoreCase))
        {
            return "warning";
        }

        return status.Contains("ready", StringComparison.OrdinalIgnoreCase) ? "brand" : "neutral";
    }

    private static Dictionary<string, Windows.UI.Color> CapturePalette(string themeKey)
    {
        if (Application.Current.Resources.ThemeDictionaries[themeKey] is not ResourceDictionary dictionary)
        {
            throw new InvalidOperationException($"Theme dictionary '{themeKey}' is unavailable.");
        }

        Dictionary<string, Windows.UI.Color> palette = new(StringComparer.Ordinal);
        foreach (string brushKey in PaletteBrushKeys)
        {
            palette[brushKey] = ((SolidColorBrush)dictionary[brushKey]).Color;
        }

        return palette;
    }

    private static void ApplyPalette(IReadOnlyDictionary<string, Windows.UI.Color> palette)
    {
        foreach (string themeKey in new[] { "Default", "Light", "Dark" })
        {
            if (Application.Current.Resources.ThemeDictionaries[themeKey] is not ResourceDictionary dictionary)
            {
                continue;
            }

            foreach ((string brushKey, Windows.UI.Color color) in palette)
            {
                ((SolidColorBrush)dictionary[brushKey]).Color = color;
            }
        }
    }

    private static readonly string[] PaletteBrushKeys =
    [
        "StewardPageBrush",
        "StewardCardBrush",
        "StewardSubtleBrush",
        "StewardTextBrush",
        "StewardSecondaryTextBrush",
        "StewardBorderBrush",
        "StewardBrandBrush",
        "StewardBrandTextBrush",
        "StewardSuccessBrush",
        "StewardSuccessSurfaceBrush",
        "StewardWarningBrush",
        "StewardWarningSurfaceBrush",
        "StewardDangerBrush",
        "StewardDangerSurfaceBrush"
    ];

    private static TextBlock Eyebrow(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        CharacterSpacing = 55,
        Foreground = ResourceBrush("StewardSecondaryTextBrush")
    };

    private static TextBlock SectionTitle(string text)
    {
        TextBlock block = new() { Text = text, Style = ResourceStyle("StewardSectionTitleStyle") };
        AutomationProperties.SetHeadingLevel(block, AutomationHeadingLevel.Level2);
        return block;
    }

    private static TextBlock BodyText(string text) => new()
    {
        Text = text,
        Style = ResourceStyle("StewardBodyStyle")
    };

    private static void AddGridText(Grid grid, string text, int column, bool emphasized = false)
    {
        TextBlock block = new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ResourceBrush(emphasized ? "StewardTextBrush" : "StewardSecondaryTextBrush"),
            FontWeight = emphasized ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static FrameworkElement BulletList(IEnumerable<string> items)
    {
        StackPanel list = new() { Spacing = 5 };
        foreach (string item in items)
        {
            Grid row = new() { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = "•",
                Foreground = ResourceBrush("StewardBrandTextBrush")
            });
            TextBlock text = BodyText(item);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            list.Children.Add(row);
        }

        return list;
    }

    private static Brush ResourceBrush(string key) => (Brush)Application.Current.Resources[key];

    private static Style ResourceStyle(string key) => (Style)Application.Current.Resources[key];

    private sealed record ApplicationFixture(
        string Name,
        string Version,
        string Status,
        string Publisher,
        string Scope,
        string Source);

    private sealed record UpdateFixture(
        string Name,
        string Publisher,
        string CurrentVersion,
        string TargetVersion,
        string PackageId,
        string Scope,
        string Architecture);
}
