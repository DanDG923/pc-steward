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
    /// Task 73 Phase 4 (ADR-030 concept A; audit P-M7, §6 items 9–10; FR-DAT-01/03, FR-HIS-05): Settings in one column with one save
    /// model — every control saves as soon as it changes, except the Schedule section, which changes Windows Task Scheduler and so
    /// applies only through its one "Save schedule" button (<see cref="SettingsPresenter.SaveModel"/>). The source consent toggle keeps
    /// its own disclosure and turning it on starts nothing; data controls live on the Data &amp; reset subpage.
    /// UI polish phase 4 (approved target T-Settings): sections of Windows Settings-style row cards (Update checks, Scan scope,
    /// Schedule, Privacy &amp; data); Decisions and About are in the navigation pane, and Copy diagnostic moved to About.
    /// </summary>
    private StackPanel BuildSettingsPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Settings", SettingsPresenter.PageDescription));

        // Update checks: the consent toggle stays separate, with its existing disclosure dialog before first use.
        StackPanel sources = SettingsSection("Update checks", null);
        ToggleSwitch winget = new() { IsOn = viewModel.IsWinGetTrustedSourcesEnabled, OffContent = "Off", OnContent = "On" };
        AutomationProperties.SetName(winget, SettingsPresenter.SourceSettingLabel);
        AutomationProperties.SetHelpText(winget, SettingsPresenter.SourceConsentNote);
        winget.Toggled += async (_, _) => await ChangeWinGetTrustedSourcesAsync(winget.IsOn);
        sources.Children.Add(StewardVisuals.RowCard("\uE753", SettingsPresenter.SourceSettingLabel, SettingsPresenter.SourceRowDescription(viewModel.IsWinGetTrustedSourcesEnabled, FreshnessPresenter.Describe(viewModel.IsUpdateSourceEnabled, viewModel.Sources, DateTimeOffset.UtcNow).Sentence), winget, "blue"));

        // ADR-031 (FR-UPD-15, FR-UI-03/04): the Chocolatey client update, shown as unavailable until a reviewed Desktop composition and owner admission exist.
        // The summary is display only ("Not checked yet": only the review checks the installed client); opening it starts nothing.
        ChocolateyClientUpdateViewModel chocolateyClientSummary = ChocolateyClientUpdateView();
        // Why it is not offered yet stays one hover or screen-reader query away (help text and tooltip on the button).
        Button chocolateyEntry = WithHelpText(chocolateyClientSummary.UnavailableFields[0].Text, ChocolateyClientUpdateEntry(chocolateyClientSummary));
        sources.Children.Add(StewardVisuals.RowCard("\uE7B8", $"{ChocolateyClientUpdateViewModel.SettingsCardTitle} client", chocolateyClientSummary.Summary, chocolateyEntry, "blue"));
        panel.Children.Add(sources);

        // Scan scope / AppX.
        StackPanel scope = SettingsSection("Scan scope", null);
        TextBlock alwaysOn = new() { Text = "Always on", Style = AppStyle("StewardBodyStyle") };
        scope.Children.Add(StewardVisuals.RowCard("\uE7F8", "Windows installed apps", "Always included.", alwaysOn, "teal"));
        ConnectorOption appx = viewModel.Connectors.Single(connector => connector.Name == "Current-user AppX/MSIX catalog");
        ToggleSwitch includeAppx = new() { IsOn = appx.IsEnabled, OffContent = "Off", OnContent = "On" };
        AutomationProperties.SetName(includeAppx, "Store and packaged apps for your account");
        includeAppx.Toggled += async (_, _) => await ChangeConnectorAsync(appx.Name, includeAppx.IsOn);
        scope.Children.Add(StewardVisuals.RowCard("\uE719", "Store and packaged apps for your account", "Apps from the Microsoft Store and other packaged apps.", includeAppx, "teal"));
        panel.Children.Add(scope);

        // Schedule: nothing applies until "Save schedule", which saves the whole record through the register-then-save operation.
        ScheduleCardPresentation plan = SettingsPresenter.Schedule(scheduledSettings, scheduledStatus, isSavingSchedule, otherWorkActive: HasActiveOperation);
        StackPanel schedule = SettingsSection("Schedule", SettingsPresenter.ScheduleDescription);
        ComboBox cadence = new() { IsEnabled = plan.IsCadenceEnabled, MinWidth = 160 };
        foreach (string option in SettingsPresenter.CadenceOptions) cadence.Items.Add(option);
        cadence.SelectedIndex = plan.CadenceIndex;
        AutomationProperties.SetName(cadence, "Check automatically");
        TimePicker startTime = new() { Time = plan.LocalStartTime.ToTimeSpan(), IsEnabled = plan.IsTimeEnabled };
        AutomationProperties.SetName(startTime, "Start time");
        ComboBox weeklyDay = new() { IsEnabled = plan.IsWeekdayEnabled, MinWidth = 160 };
        // Audit P-M6: localized day names; the selection stays index-based (DayOfWeek order), so what is saved is unchanged.
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>()) weeklyDay.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day));
        weeklyDay.SelectedIndex = (int)plan.WeeklyDay;
        AutomationProperties.SetName(weeklyDay, "Day of the week");
        ToggleSwitch scheduleEnabled = new() { IsOn = plan.IsEnabled, IsEnabled = plan.IsEnableToggleEnabled, OffContent = "Off", OnContent = "On" };
        AutomationProperties.SetName(scheduleEnabled, "Turn on scheduled checks");
        ToggleSwitch notifications = new() { IsOn = plan.NotificationsEnabled, IsEnabled = plan.IsNotificationsToggleEnabled, OffContent = "Off", OnContent = "On" };
        AutomationProperties.SetName(notifications, "Windows notifications");
        TextBlock scheduleSummary = new() { Text = isSavingSchedule ? "Saving…" : plan.Summary, Style = AppStyle("StewardRowTitleStyle") };
        AutomationProperties.SetLiveSetting(scheduleSummary, AutomationLiveSetting.Polite);
        Button saveSchedule = StewardVisuals.PrimaryButton(SettingsPresenter.SaveScheduleLabel);
        saveSchedule.AccessKey = "S";
        saveSchedule.IsEnabled = plan.IsSaveEnabled;
        Control[] scheduleControls = [cadence, startTime, weeklyDay, scheduleEnabled, notifications, saveSchedule];
        async Task SaveScheduleAsync()
        {
            if (isSavingSchedule) return;
            if (HasActiveOperation) { scheduleSummary.Text = DataResetPresenter.ActiveWorkReason; return; }
            // Fail closed: no cadence or weekday selection means nothing is saved.
            ScheduledCheckSettings? requested = SettingsPresenter.RequestedSchedule(cadence.SelectedIndex, scheduleEnabled.IsOn, notifications.IsOn, weeklyDay.SelectedIndex, startTime.Time);
            if (requested is null) { scheduleSummary.Text = SettingsPresenter.ScheduleNotChosen; return; }
            foreach (Control control in scheduleControls) control.IsEnabled = false;
            scheduleSummary.Text = "Saving…";
            await ApplyScheduleAsync(requested);
        }
        // Handlers are attached after the saved values are set. Only "Save schedule" saves; the cadence handler changes which
        // controls are available and never saves, registers or re-renders anything.
        cadence.SelectionChanged += (_, _) =>
        {
            if (isSavingSchedule) return;
            (bool time, bool weekday, bool enable) = SettingsPresenter.ScheduleControlAvailability(cadence.SelectedIndex, busy: false);
            startTime.IsEnabled = time;
            weeklyDay.IsEnabled = weekday;
            scheduleEnabled.IsEnabled = enable;
        };
        saveSchedule.Click += async (_, _) => await SaveScheduleAsync();
        schedule.Children.Add(StewardVisuals.RowCard("\uE787", "Check automatically", "Manual only means PC Steward checks only when you start a scan.", cadence, "amber"));
        schedule.Children.Add(StewardVisuals.RowCard("\uE823", "Start time", "Used for daily and weekly checks.", startTime, "amber"));
        schedule.Children.Add(StewardVisuals.RowCard("\uE8BF", "Day of the week", "Used for weekly checks.", weeklyDay, "amber"));
        schedule.Children.Add(StewardVisuals.RowCard("\uE768", "Turn on scheduled checks", "Runs for your account only, with no background service. Unavailable with Manual only.", scheduleEnabled, "amber"));
        schedule.Children.Add(StewardVisuals.RowCard("\uEA8F", "Windows notifications", "Only when something needs your attention or checks keep failing.", notifications, "amber"));
        // The save row: what is saved now (a live region), the scheduled task's status, and the one Save button.
        StackPanel saved = new() { Spacing = StewardSpacing.XSmall, VerticalAlignment = VerticalAlignment.Center };
        saved.Children.Add(scheduleSummary);
        foreach (DetailFact fact in plan.Status) saved.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        Grid saveRow = new() { ColumnSpacing = StewardSpacing.Large };
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        saveRow.Children.Add(saved);
        saveSchedule.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(saveSchedule, 1);
        saveRow.Children.Add(saveSchedule);
        schedule.Children.Add(new Border { Style = AppStyle("StewardRowCardStyle"), Child = saveRow });
        panel.Children.Add(schedule);

        // Privacy & data (FR-DAT-01/03): exports keep the identifying-data warning; data controls are on the Data & reset subpage.
        StackPanel privacy = SettingsSection("Privacy & data", null);
        MenuFlyoutItem exportCsv = new() { Text = "CSV file" }; exportCsv.Click += async (_, _) => await SaveExportAsync(false);
        MenuFlyoutItem exportJson = new() { Text = "JSON file" }; exportJson.Click += async (_, _) => await SaveExportAsync(true);
        MenuFlyout exportFormats = new();
        exportFormats.Items.Add(exportCsv);
        exportFormats.Items.Add(exportJson);
        DropDownButton export = new() { Content = "Export…", Flyout = exportFormats };
        StewardVisuals.Named(export, "Export…", "(the app list as a CSV or JSON file)");
        privacy.Children.Add(StewardVisuals.RowCard("\uEDE1", "Export the app list", $"Save a CSV or JSON file where you choose. {SettingsPresenter.ExportWarning}", export, "purple"));
        // UI review 2026-11-13 (item 14): Storage was reached only from the retired Scan results page, so Settings opens it now.
        Button openStorage = StewardVisuals.NavigationRow("\uEDA2", "Storage", "Space used by PC Steward's own records and downloaded update files.", "purple");
        openStorage.Click += (_, _) => NavigateTo(ShellPage.Storage);
        privacy.Children.Add(openStorage);
        Button openData = StewardVisuals.NavigationRow("\uE74E", "Data & reset", "Clear scan data, downloads, source data, logs or history, or reset PC Steward.", "purple");
        openData.Click += (_, _) => NavigateTo(ShellPage.Privacy);
        privacy.Children.Add(openData);
        panel.Children.Add(privacy);
        panel.Children.Add(StewardVisuals.FootNote("\uE946", SettingsPresenter.SaveModelText));
        return panel;
    }

    /// <summary>Gives <paramref name="button"/> a tooltip and help text with <paramref name="help"/>.</summary>
    private static Button WithHelpText(string help, Button button)
    {
        ToolTipService.SetToolTip(button, help);
        AutomationProperties.SetHelpText(button, help);
        return button;
    }

    /// <summary>A Settings section: a level-2 heading, an optional one-line description, then its row cards 4 epx apart.</summary>
    private static StackPanel SettingsSection(string heading, string? description)
    {
        StackPanel section = new() { Spacing = StewardSpacing.XSmall };
        TextBlock title = SectionHeading(heading);
        title.Margin = new Thickness(0, 0, 0, StewardSpacing.XSmall);
        section.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(description)) section.Children.Add(new TextBlock { Text = description, Style = AppStyle("StewardCaptionStyle"), Margin = new Thickness(0, 0, 0, StewardSpacing.XSmall) });
        return section;
    }

    /// <summary>
    /// Audit P-M7 and independent review fixes 5–6: "Save schedule" registers (or removes) the current-user scan-only task, then
    /// saves the settings, inside the data-operation lease (<see cref="ScheduleSaveOperation"/>). Registration and save failures are
    /// handled inside the lease, so only a busy gate reaches the "Action unavailable" dialog; a Task Scheduler refusal, a restored
    /// task or a task that could not be put back each say what happened. While saving, <see cref="HasActiveOperation"/> is true, so
    /// clear, reset, scan and other actions are unavailable.
    /// </summary>
    private async Task ApplyScheduleAsync(ScheduledCheckSettings requested)
    {
        ScheduledCheckSettings previous = scheduledSettings.Normalize();
        isSavingSchedule = true;
        RefreshOperationVisuals();
        ScheduleSaveOutcome outcome;
        try
        {
            (outcome, ScheduledCheckStatus? status) = await DataOperationLease.RunAsync(async token =>
            {
                ScheduleSaveOutcome saved = await ScheduleSaveOperation.SaveAsync(previous, requested, scheduleRegistration.ApplyAsync, scheduleSettingsStore.SaveScheduledCheckSettingsAsync, token);
                ScheduledCheckStatus? current = null;
                try { current = await scheduleSettingsStore.GetScheduledCheckStatusAsync(token); }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException)) { }
                return (saved, current);
            }, CancellationToken.None);
            if (outcome == ScheduleSaveOutcome.Saved) scheduledSettings = requested;
            if (status is not null) scheduledStatus = status;
        }
        catch (InvalidOperationException) { isSavingSchedule = false; Render(); await ShowDataOperationUnavailableAsync(); return; }
        catch (Exception) { isSavingSchedule = false; Render(); await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Schedule was not changed", Content = "PC Steward couldn't start saving the schedule. Your saved settings were left unchanged.", CloseButtonText = "Close" }.ShowAsync(); return; }
        isSavingSchedule = false;
        Render();
        AnnounceCompletion(SettingsPresenter.ScheduleSaveResult(outcome) is { } result ? OperationAnnouncements.Completed(result.Title) : OperationAnnouncements.Completed("Schedule saved"), "schedule-saved");
        if (SettingsPresenter.ScheduleSaveResult(outcome) is { } message)
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = message.Title, Content = message.Body, CloseButtonText = "Close" }.ShowAsync();
    }

    /// <summary>FR-HIS-05: copies the existing sanitized diagnostic bundle of recent update outcomes and confirms in text.</summary>
    private void CopyDiagnostic(TextBlock confirmation)
    {
        try
        {
            Windows.ApplicationModel.DataTransfer.DataPackage package = new();
            package.SetText(SanitizedDiagnosticBundle.Create(updateHistory));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            // Keeps the copied diagnostic on the clipboard after PC Steward closes.
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            StewardVisuals.SetLiveText(confirmation, SettingsPresenter.DiagnosticCopied(updateHistory.Count), announce: true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { StewardVisuals.SetLiveText(confirmation, SettingsPresenter.DiagnosticCopyFailed, announce: true); }
    }

    /// <summary>The Data &amp; reset inputs from the state loaded for this window; controls are unavailable during any active work.</summary>
    private DataResetInputs DataResetInputsNow() => new(
        scanData, downloadCache.TotalBytes, downloadCache.RecoverableBytes, downloadCache.ArtifactCount, downloadCache.ActiveCount, downloadCache.LastCleanedAt,
        viewModel.UpdateFreshnessLabel, updateHistory.Count, ActivityHistoryLimit, activityHistoryUnavailable,
        activeDecisions.Count + manualMappings.Count, HasActiveWork: HasActiveOperation);
}
