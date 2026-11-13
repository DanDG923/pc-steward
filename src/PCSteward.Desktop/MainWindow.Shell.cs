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
    /// FR-UI-01/02 (ADR-030 foundation 11; audit P-L1): the window chrome, built once. Content extends into a 32 px custom title
    /// bar with a Back button, the app mark and name; the drag region excludes Back so it stays clickable. Mica is the base
    /// layer where supported (Desktop Acrylic, then the opaque canvas brush, as fallbacks); high contrast always uses the
    /// opaque system-mapped canvas brush.
    /// </summary>
    private void BuildWindowChrome()
    {
        ExtendsContentIntoTitleBar = true;
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Standard;
        }

        windowRoot = new Grid();
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(StewardSpacing.TitleBarHeight) });
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid titleBar = new() { Height = StewardSpacing.TitleBarHeight };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBarBack = new Button
        {
            Content = new FontIcon { Glyph = "\uE72B", FontSize = 12 },
            Width = 40,
            Height = StewardSpacing.TitleBarHeight,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(StewardSpacing.XSmall),
            VerticalAlignment = VerticalAlignment.Top,
            IsEnabled = false,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(titleBarBack, "Back");
        // Independent review L9: WinUI appends the keyboard accelerator to the tooltip itself, so the text stays plain.
        ToolTipService.SetToolTip(titleBarBack, "Back");
        titleBarBack.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = Windows.System.VirtualKey.Left, Modifiers = Windows.System.VirtualKeyModifiers.Menu });
        titleBarBack.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = Windows.System.VirtualKey.GoBack });
        titleBarBack.Click += (_, _) => GoBack();
        titleBarBack.AccessKey = "B";
        titleBar.Children.Add(titleBarBack);

        Grid dragRegion = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        StackPanel brand = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(StewardSpacing.Medium, 0, 0, 0) };
        Image mark = new() { Width = 16, Height = 16, Source = new BitmapImage(new Uri("ms-appx:///Assets/logo-mark-64.png")) };
        AutomationProperties.SetAccessibilityView(mark, AccessibilityView.Raw);
        brand.Children.Add(mark);
        // Owner report 2026-11-13 (release 0.1.15): the name sat higher than the mark beside it and the window's own buttons
        // (1.5 units above their shared middle, measured from the drawn pixels). Its capital letters are now what is centred.
        brand.Children.Add(StewardVisuals.CapHeightBox(new TextBlock { Text = AppIdentity.DisplayName, Style = AppStyle("StewardCaptionStyle") }));
        dragRegion.Children.Add(brand);
        Grid.SetColumn(dragRegion, 1);
        titleBar.Children.Add(dragRegion);
        windowRoot.Children.Add(titleBar);
        Content = windowRoot;
        // Final review Info-1: PreferredMinimumWidth/Height are physical pixels, so scale the 500x500 epx minimum by the display
        // scale, and again whenever the window moves to a display with a different scale (XamlRoot.Changed).
        windowRoot.Loaded += (_, _) =>
        {
            if (windowRoot?.XamlRoot is not { } root) return;
            ApplyMinimumWindowSize(root.RasterizationScale);
            root.Changed -= OnXamlRootChanged;
            root.Changed += OnXamlRootChanged;
        };
        SetTitleBar(dragRegion);
        // Independent review L9: the mouse Back button (XButton1) goes back anywhere in the shell, even over handled controls.
        windowRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnShellPointerPressed), true);
        AddWindowShortcuts(windowRoot);

        try
        {
            accessibilitySettings = new Windows.UI.ViewManagement.AccessibilitySettings();
            accessibilitySettings.HighContrastChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!windowClosed) RefreshForThemeChange(); });
        }
        catch (Exception) { accessibilitySettings = null; }
        windowRoot.ActualThemeChanged += (_, _) => { if (!windowClosed) RefreshForThemeChange(); };
        ApplyBackdrop();
        ApplyCaptionButtonColors();
    }

    /// <summary>
    /// AC-UX-01/09, FR-UPD-01 (Task 73 Phase 5; research §2 items 2–3): window shortcuts. F6 and Shift+F6 move focus between the
    /// navigation pane, the page and the operation band; Ctrl+F focuses the page's search or filters; F5 starts the same read-only
    /// scan (and, when sources are on, source check) as the visible "Scan again"/"Check for updates" button, and only when
    /// <see cref="ShortcutGuard.CanStartScan"/> allows it — never during active work, an open review or a dialog. No shortcut can
    /// confirm a review, approve or start an update, or skip a confirmation.
    /// </summary>
    private void AddWindowShortcuts(UIElement root)
    {
        root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        KeyboardAccelerator nextRegion = new() { Key = Windows.System.VirtualKey.F6 };
        nextRegion.Invoked += (_, args) => args.Handled = CycleFocusRegion(backwards: false);
        KeyboardAccelerator previousRegion = new() { Key = Windows.System.VirtualKey.F6, Modifiers = Windows.System.VirtualKeyModifiers.Shift };
        previousRegion.Invoked += (_, args) => args.Handled = CycleFocusRegion(backwards: true);
        KeyboardAccelerator search = new() { Key = Windows.System.VirtualKey.F, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        search.Invoked += (_, args) => args.Handled = FocusPageSearch();
        KeyboardAccelerator scan = new() { Key = Windows.System.VirtualKey.F5 };
        scan.Invoked += async (_, args) => { args.Handled = true; await ScanShortcutAsync(); };
        root.KeyboardAccelerators.Add(nextRegion);
        root.KeyboardAccelerators.Add(previousRegion);
        root.KeyboardAccelerators.Add(search);
        root.KeyboardAccelerators.Add(scan);
    }

    /// <summary>F5: the guarded read-only scan. It calls only <see cref="CheckForUpdatesAsync"/>, which re-checks the guard itself.</summary>
    private async Task ScanShortcutAsync()
    {
        if (!ShortcutGuard.CanStartScan(shellShown: navigationView is not null, HasActiveOperation, reviewOpen: reviewDecision is not null, dialogOpen: IsPopupOpen())) return;
        await CheckForUpdatesAsync();
    }

    private bool IsPopupOpen() => Content?.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0;

    private bool FocusPageSearch()
    {
        if (IsPopupOpen() || pageSearchTarget is not { } target || !target.Focus(FocusState.Keyboard)) return false;
        if (target is TextBox box) box.SelectAll();
        return true;
    }

    /// <summary>F6 / Shift+F6: the next available region (<see cref="FocusRegionCycle"/>); a region that cannot take focus is skipped.</summary>
    private bool CycleFocusRegion(bool backwards)
    {
        if (navigationView is null || pageHost is null || Content?.XamlRoot is null || IsPopupOpen()) return false;
        List<FocusRegion> available = [FocusRegion.Navigation, FocusRegion.Page];
        if (operationBand is { Visibility: Visibility.Visible }) available.Add(FocusRegion.OperationBand);
        FocusRegion? current = CurrentFocusRegion();
        for (int attempt = 0; attempt < available.Count; attempt++)
        {
            if (FocusRegionCycle.Next(current, available, backwards) is not { } next) return false;
            if (FocusRegionElement(next)) return true;
            current = next;
        }
        return false;
    }

    private FocusRegion? CurrentFocusRegion()
    {
        if (Content?.XamlRoot is null || FocusManager.GetFocusedElement(Content.XamlRoot) is not DependencyObject focused) return null;
        for (DependencyObject? node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, operationBand)) return FocusRegion.OperationBand;
            if (ReferenceEquals(node, pageHost) || ReferenceEquals(node, pageFooterHost) || ReferenceEquals(node, detailPaneHost)) return FocusRegion.Page;
            if (ReferenceEquals(node, navigationView)) return FocusRegion.Navigation;
        }
        return null;
    }

    private bool FocusRegionElement(FocusRegion region)
    {
        if (navigationView is null || pageHost is null) return false;
        Control? target = region switch
        {
            FocusRegion.Navigation => navigationView.SelectedItem as Control ?? navigationView.MenuItems.OfType<Control>().FirstOrDefault(),
            FocusRegion.Page => pageHost.Content is DependencyObject page ? FocusManager.FindFirstFocusableElement(page) as Control : null,
            _ => operationBand?.Child is DependencyObject band ? FocusManager.FindFirstFocusableElement(band) as Control : null
        };
        return target?.Focus(FocusState.Keyboard) == true;
    }

    private void OnShellPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (windowRoot is null || navigationView is null || !navigation.CanGoBack) return;
        if (!args.GetCurrentPoint(windowRoot).Properties.IsXButton1Pressed) return;
        args.Handled = true;
        GoBack();
    }

    /// <summary>
    /// Independent review L7: theme brushes are read when controls are built, so a high-contrast or light/dark change refreshes
    /// the backdrop, the caption buttons, the persistent operation band and the current page (which rebuilds its list items).
    /// </summary>
    private void RefreshForThemeChange()
    {
        ApplyBackdrop();
        ApplyCaptionButtonColors();
        if (operationBand is not null)
        {
            operationBand.Background = StewardVisuals.Brush("StewardBrandSubtleBrush");
            operationBand.BorderBrush = StewardVisuals.Brush("StewardBorderBrush");
        }
        if (pageFooterHost is not null)
        {
            pageFooterHost.Background = StewardVisuals.Brush("StewardSurfaceBrush");
            pageFooterHost.BorderBrush = StewardVisuals.Brush("StewardBorderBrush");
        }
        if (detailPaneHost is not null) detailPaneHost.BorderBrush = StewardVisuals.Brush("StewardBorderBrush");
        if (operationStatusText is not null) operationStatusText.Foreground = StewardVisuals.Brush("StewardTextBrush");
        Render();
    }

    /// <summary>
    /// Independent review L9: with content extended into the title bar, the caption buttons take the current theme's text colour,
    /// with a subtle hover and pressed fill; high contrast uses the system-mapped text and canvas colours.
    /// </summary>
    private void ApplyCaptionButtonColors()
    {
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
        Microsoft.UI.Windowing.AppWindowTitleBar bar = AppWindow.TitleBar;
        bool highContrast = accessibilitySettings?.HighContrast == true;
        Windows.UI.Color text = ((SolidColorBrush)StewardVisuals.Brush("StewardTextBrush")).Color;
        Windows.UI.Color canvas = ((SolidColorBrush)StewardVisuals.Brush("StewardCanvasBrush")).Color;
        Windows.UI.Color inactive = ((SolidColorBrush)StewardVisuals.Brush("StewardTertiaryTextBrush")).Color;
        Windows.UI.Color hover = highContrast ? text : Microsoft.UI.ColorHelper.FromArgb(0x19, text.R, text.G, text.B);
        Windows.UI.Color pressed = highContrast ? text : Microsoft.UI.ColorHelper.FromArgb(0x33, text.R, text.G, text.B);
        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonForegroundColor = text;
        bar.ButtonInactiveForegroundColor = inactive;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = highContrast ? canvas : text;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = highContrast ? canvas : text;
    }

    private void ApplyBackdrop()
    {
        if (windowRoot is null) return;
        // MicaBackdrop also falls back to a solid colour in high contrast; the explicit check keeps the system-mapped canvas brush.
        bool highContrast = accessibilitySettings?.HighContrast == true;
        SystemBackdrop = highContrast ? null
            // Colour option 2 (owner choice 2026-11-11): Mica Alt, the stronger wallpaper tint Windows uses for tabbed and
            // long-lived app windows, so the app reads less grey on dark wallpapers. Steady (2026-11-13): it keeps the active look
            // when the window loses focus, so the background no longer changes colour on every focus change.
            : MicaController.IsSupported() ? new SteadyMicaAltBackdrop()
            : DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop()
            : null;
        windowRoot.Background = SystemBackdrop is null ? StewardVisuals.Brush("StewardCanvasBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    /// <summary>Shows a page without the navigation shell (startup failure, post-reset onboarding).</summary>
    private void ShowBarePage(StackPanel panel)
    {
        if (windowRoot is null) return;
        if (navigationView is not null) { windowRoot.Children.Remove(navigationView); navigationView = null; pageHost = null; renderedPage = null; }
        // The details column goes with the shell it was part of.
        shellContent = null; detailPaneHost = null; detailPaneScroll = null; detailPaneKey = null; detailPaneOpen = false;
        foreach (UIElement bare in windowRoot.Children.Where(child => Grid.GetRow((FrameworkElement)child) == 1).ToArray()) windowRoot.Children.Remove(bare);
        // Both bare pages are a few lines of text, so they read as one column on a wide window (UI review 2026-11-13, item 19).
        panel.MaxWidth = PageLayout.MaximumWidth(ShellPage.Onboarding, StewardSpacing.PagePadding.Left + StewardSpacing.PagePadding.Right);
        ScrollViewer host = new() { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        StewardVisuals.KeepPageColumnFitted(host);
        Grid.SetRow(host, 1);
        windowRoot.Children.Add(host);
        SyncNavigationChrome();
    }

    /// <summary>
    /// FR-UI-01/02, AC-UX-01/02 (ADR-030; audit P-H1/P-H7): the persistent shell, created once. The NavigationView, the operation
    /// band and the page host survive page changes and refreshes; only the hosted page is rebuilt. Navigation reacts to
    /// <c>ItemInvoked</c>, so invoking the highlighted parent of a contextual page navigates to it.
    /// </summary>
    private void EnsureShell()
    {
        if (navigationView is not null || windowRoot is null) return;
        foreach (UIElement bare in windowRoot.Children.Where(child => Grid.GetRow((FrameworkElement)child) == 1).ToArray()) windowRoot.Children.Remove(bare);

        Grid shell = new();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // UI review 2026-11-13 (item 19): the page, and beside it a column for the selected app's details on a wide window.
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        shellContent = shell;

        Grid operation = new() { ColumnSpacing = StewardSpacing.Large };
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        operationStatusText = new TextBlock { Text = CurrentOperationLabel, Foreground = StewardVisuals.Brush("StewardTextBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLiveSetting(operationStatusText, AutomationLiveSetting.Polite);
        TextBlock progressLabel = operationStatusText;
        progressLabel.Loaded += (_, _) => { if (ReferenceEquals(operationStatusText, progressLabel) && isExecuting) StartUpdateProgressPolling(); };
        progressLabel.Unloaded += (_, _) => { if (ReferenceEquals(updateProgressTarget, progressLabel)) StopUpdateProgressPolling(); };
        operation.Children.Add(operationStatusText);
        ProgressBar progress = new() { IsIndeterminate = true, VerticalAlignment = VerticalAlignment.Center, Height = 4 };
        AutomationProperties.SetName(progress, "Current PC Steward operation progress");
        Grid.SetColumn(progress, 1);
        operation.Children.Add(progress);
        operationProgress = progress;
        Button cancel = StewardVisuals.ActionButton("Cancel scan", "in progress");
        cancel.Visibility = viewModel.IsScanning || scanCancellation is not null ? Visibility.Visible : Visibility.Collapsed;
        cancel.Click += (_, _) => scanCancellation?.Cancel();
        Grid.SetColumn(cancel, 2);
        operation.Children.Add(cancel);
        operationCancel = cancel;
        // Task 73 Phase 2: the update job is non-modal; from any page the band leads back to its progress. It never cancels it.
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Button viewJob = StewardVisuals.ActionButton("View update progress");
        viewJob.Visibility = Visibility.Collapsed;
        viewJob.Click += (_, _) => NavigateTo(ShellPage.UpdateJob);
        Grid.SetColumn(viewJob, 3);
        operation.Children.Add(viewJob);
        operationViewJob = viewJob;
        // Task 73 Phase 6 review M2: preparing a review can be abandoned; nothing has been approved or started at that point.
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Button cancelReview = StewardVisuals.ActionButton("Cancel review preparation");
        cancelReview.Visibility = preparingReview && reviewPreparationCancellation is not null ? Visibility.Visible : Visibility.Collapsed;
        cancelReview.Click += (_, _) => reviewPreparationCancellation?.Cancel();
        Grid.SetColumn(cancelReview, 4);
        operation.Children.Add(cancelReview);
        operationCancelReview = cancelReview;
        operationBand = new Border
        {
            Background = StewardVisuals.Brush("StewardBrandSubtleBrush"),
            BorderBrush = StewardVisuals.Brush("StewardBorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            // UI polish Phase 1: the band's text starts on the page column's left edge.
            Padding = new Thickness(StewardSpacing.PagePadding.Left, StewardSpacing.Small, StewardSpacing.PagePadding.Right, StewardSpacing.Small),
            Visibility = HasActiveOperation ? Visibility.Visible : Visibility.Collapsed,
            Child = operation
        };
        Grid.SetColumnSpan(operationBand, 2);
        shell.Children.Add(operationBand);

        pageHost = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        StewardVisuals.KeepPageColumnFitted(pageHost);
        Grid.SetRow(pageHost, 1);
        shell.Children.Add(pageHost);

        // The details column: its own scroll host, fitted like the page's, with a line between it and the list. It is hidden
        // until an app is opened from a list on a wide window (ShowDetailPane), and it only ever shows what the details page shows.
        detailPaneScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetAutomationId(detailPaneScroll, "AppDetailsPane");
        StewardVisuals.KeepPageColumnFitted(detailPaneScroll);
        detailPaneHost = new Border
        {
            BorderBrush = StewardVisuals.Brush("StewardBorderBrush"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = detailPaneScroll
        };
        Grid.SetRow(detailPaneHost, 1);
        Grid.SetColumn(detailPaneHost, 1);
        shell.Children.Add(detailPaneHost);
        // A window made narrower or wider moves the details between the column and the page's own width.
        shell.SizeChanged += (_, _) => ShowDetailPane(rebuild: false);

        // UI review 2026-11-13 (item 13): the current page's action bar, when it has one. It sits under the scrolling page, on the
        // page column's edges, so it stays in view; it comes after the page in tab order and belongs to the page's F6 region.
        pageFooterHost = new Border
        {
            Background = StewardVisuals.Brush("StewardSurfaceBrush"),
            BorderBrush = StewardVisuals.Brush("StewardBorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(StewardSpacing.PagePadding.Left, StewardSpacing.Medium, StewardSpacing.PagePadding.Right, StewardSpacing.Medium),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(pageFooterHost, 2);
        Grid.SetColumnSpan(pageFooterHost, 2);
        shell.Children.Add(pageFooterHost);

        navigationView = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsTitleBarAutoPaddingEnabled = false,
            OpenPaneLength = 232,
            CompactPaneLength = 56,
            CompactModeThresholdWidth = 900,
            ExpandedModeThresholdWidth = 1100,
            AlwaysShowHeader = false,
            SelectionFollowsFocus = NavigationViewSelectionFollowsFocus.Disabled,
            Content = shell
        };
        foreach (ShellNavigationItem item in ShellNavigation.Items) AddNavigationItem(navigationView, item);
        navigationView.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItemContainer?.Tag is string tag && Enum.TryParse(tag, out ShellPage page)) NavigateTo(page);
        };
        Grid.SetRow(navigationView, 1);
        windowRoot.Children.Add(navigationView);
        SyncNavigationChrome();
    }

    private static void AddNavigationItem(NavigationView navigation, ShellNavigationItem item)
    {
        NavigationViewItem entry = new() { Content = item.Label, Tag = item.Page.ToString(), Icon = new FontIcon { Glyph = item.Glyph } };
        AutomationProperties.SetName(entry, item.Label);
        if (item.IsFooter) navigation.FooterMenuItems.Add(entry); else navigation.MenuItems.Add(entry);
    }

    /// <summary>Highlights the navigation item that owns the current page and enables Back only when there is history.</summary>
    private void SyncNavigationChrome()
    {
        if (titleBarBack is not null)
        {
            titleBarBack.Visibility = navigationView is null ? Visibility.Collapsed : Visibility.Visible;
            titleBarBack.IsEnabled = navigationView is not null && navigation.CanGoBack;
        }
        if (navigationView is null) return;
        string selected = navigation.SelectedItem.ToString();
        navigationView.SelectedItem = navigationView.MenuItems.Concat(navigationView.FooterMenuItems).OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, selected, StringComparison.Ordinal));
    }

    /// <summary>
    /// Rebuilds only the hosted page. On a refresh of the same page the scroll offset and, when it still exists, the focused
    /// control are restored, so keyboard and screen-reader users are not thrown back to the top (AC-UX-02; audit P-H7).
    /// </summary>
    private void ShowCurrentPage(bool preserveViewState)
    {
        if (pageHost is null) return;
        bool sameView = preserveViewState && renderedPage == navigation.Current;
        double offset = sameView ? pageHost.VerticalOffset : 0;
        bool focusWasInPage = IsFocusInside(pageHost, out string? focusKey) || (pageFooterHost is not null && IsFocusInside(pageFooterHost, out focusKey));
        bool focusWasInDetailPane = detailPaneHost is not null && IsFocusInside(detailPaneHost, out _);
        // The details column belongs to the list it was opened from: another page closes it.
        if (renderedPage != navigation.Current) detailPaneOpen = false;
        pageInitialFocus = null;
        pageSearchTarget = null;
        pageFooter = null;
        StackPanel page = BuildPage(navigation.Current);
        // UI review 2026-11-13 (item 19): a page of text and switches reads as one column; a list page still fills the window.
        page.MaxWidth = PageLayout.MaximumWidth(navigation.Current, StewardSpacing.PagePadding.Left + StewardSpacing.PagePadding.Right);
        // UI review 2026-11-13 (item 12): the line that says what was just saved, under the header of the page it was made on.
        if (decisionNotice is { } saved)
        {
            if (decisionNoticePage != navigation.Current) decisionNotice = null;
            // A choice saved for the app in the details column is confirmed in that column instead (BuildDetailPane).
            else if (!decisionNoticeInDetailPane) page.Children.Insert(Math.Min(1, page.Children.Count), DecisionNoticeBar(saved));
        }
        // A page may name the control that takes focus when it is opened, for example Cancel on the update review (Cancel is the default).
        Control? initialFocus = sameView ? null : pageInitialFocus;
        renderedPage = navigation.Current;
        pageHost.Content = page;
        StewardVisuals.FitPageColumn(pageHost);
        ShowDetailPane(rebuild: true);
        // The page's action bar, if it set one while it was built; a page without one leaves the bar hidden.
        FrameworkElement? footer = pageFooter;
        if (pageFooterHost is not null)
        {
            pageFooterHost.Child = footer;
            pageFooterHost.Visibility = footer is null ? Visibility.Collapsed : Visibility.Visible;
        }
        // Owner feedback on 0.1.1: a page whose main content is an empty state fills the window's height instead of leaving
        // most of a maximised window blank under it.
        if (page.Children.FirstOrDefault(StewardVisuals.IsEmptyState) is FrameworkElement empty) StewardVisuals.FillViewportHeight(page, empty);
        if (!sameView) pageHost.ChangeView(null, 0, null, true);
        void Restore(object sender, RoutedEventArgs args)
        {
            page.Loaded -= Restore;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (windowClosed || pageHost is not { } host || !ReferenceEquals(host.Content, page)) return;
                if (offset > 0) RestoreScrollOffset(host, page, offset);
                // A refresh started from the details column keeps the keyboard there instead of dropping focus.
                if (focusWasInDetailPane && detailPaneHost is { Visibility: Visibility.Visible } && detailPaneScroll?.Content is DependencyObject details)
                {
                    (FocusManager.FindFirstFocusableElement(details) as Control)?.Focus(FocusState.Programmatic);
                    return;
                }
                if (!focusWasInPage && initialFocus is null) return;
                // Independent review M3: restore only an unambiguous match; otherwise start at the top of the page.
                Control? target = (sameView && focusKey is not null ? PageFocusRestore.UniqueMatch(FindFocusTargets(page, focusKey)) ?? (footer is null ? null : PageFocusRestore.UniqueMatch(FindFocusTargets(footer, focusKey))) : null)
                    ?? initialFocus
                    ?? FocusManager.FindFirstFocusableElement(page) as Control;
                target?.Focus(FocusState.Programmatic);
            });
        }
        page.Loaded += Restore;
        RefreshOperationVisuals();
        SyncNavigationChrome();
    }

    /// <summary>
    /// Independent review L8: the rebuilt page must be measured before its saved offset is reachable, so layout is completed
    /// first; if the content is still shorter than the offset, the offset is applied once more after the next layout pass.
    /// </summary>
    private void RestoreScrollOffset(ScrollViewer host, StackPanel page, double offset)
    {
        host.UpdateLayout();
        host.ChangeView(null, offset, null, true);
        if (host.ScrollableHeight >= offset) return;
        void Retry(object? sender, object args)
        {
            host.LayoutUpdated -= Retry;
            if (!windowClosed && ReferenceEquals(host.Content, page)) host.ChangeView(null, offset, null, true);
        }
        host.LayoutUpdated += Retry;
    }

    private bool IsFocusInside(DependencyObject container, out string? key)
    {
        key = null;
        if (Content?.XamlRoot is null || FocusManager.GetFocusedElement(Content.XamlRoot) is not DependencyObject focused) return false;
        key = FocusKey(focused);
        for (DependencyObject? current = focused; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, container)) return true;
        return false;
    }

    private static string? FocusKey(DependencyObject element)
    {
        string name = AutomationProperties.GetName(element);
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return element switch
        {
            ContentControl { Content: string content } => content,
            TextBox { Header: string header } => header,
            ComboBox { Header: string header } => header,
            _ => null
        };
    }

    /// <summary>Every enabled control on the page whose accessible name (or content) equals <paramref name="key"/>.</summary>
    private static IEnumerable<Control> FindFocusTargets(DependencyObject root, string key)
    {
        if (root is Control { IsEnabled: true } control && string.Equals(FocusKey(control), key, StringComparison.Ordinal)) yield return control;
        IEnumerable<DependencyObject> children = root switch
        {
            Panel panel => panel.Children,
            Border { Child: { } child } => [child],
            Expander { Content: DependencyObject content } => [content],
            ContentControl { Content: DependencyObject content } => [content],
            _ => []
        };
        foreach (DependencyObject child in children)
            foreach (Control match in FindFocusTargets(child, key))
                yield return match;
    }
}
