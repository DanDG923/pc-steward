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
    /// Task 73 Phase 4 (ADR-030 concept A; audit P-M7, §6 items 9–10; FR-DAT-01/03, FR-HIS-05): Settings as SettingsCard groups in one
    /// column (Update sources, Scan scope, Schedule, Decisions, Privacy &amp; exports, Data &amp; reset, About) with one save model —
    /// every control saves as soon as it changes, except the Schedule card, which changes Windows Task Scheduler and so applies only
    /// through its one "Save schedule" button (<see cref="SettingsPresenter.SaveModel"/>). The source consent toggle keeps its own
    /// disclosure and turning it on starts nothing; data controls live on the Data &amp; reset subpage.
    /// </summary>
    private StackPanel BuildSettingsPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Settings", "Choose what PC Steward looks at, when it checks, and what it keeps on this PC.", "Preferences"));
        panel.Children.Add(PageCopy(SettingsPresenter.SaveModelText));

        // Update sources: the consent toggle stays separate, with its existing disclosure dialog before first use.
        StackPanel sources = new() { Spacing = StewardSpacing.Small };
        ToggleSwitch winget = new() { IsOn = viewModel.IsWinGetTrustedSourcesEnabled, OffContent = "Off", OnContent = "On" };
        AutomationProperties.SetName(winget, "WinGet and Chocolatey trusted sources");
        winget.Toggled += async (_, _) => await ChangeWinGetTrustedSourcesAsync(winget.IsOn);
        sources.Children.Add(StewardVisuals.SettingsRow("WinGet and Chocolatey trusted sources", SettingsPresenter.SourceConsentNote, winget));
        sources.Children.Add(StewardVisuals.EvidenceRow("Current evidence", viewModel.UpdateFreshnessLabel));
        panel.Children.Add(StewardVisuals.SettingsCard("", "Update sources", "Optional package sources that tell PC Steward which updates exist. They are off until you approve them.", sources));

        // ADR-031 (FR-UPD-15, FR-UI-03/04): the Chocolatey client update, shown as unavailable until a reviewed Desktop composition and owner admission exist.
        // The summary is display only ("Not checked yet": only the review checks the installed client); opening it starts nothing.
        StackPanel chocolateyClient = new() { Spacing = StewardSpacing.Small };
        ChocolateyClientUpdateViewModel chocolateyClientSummary = ChocolateyClientUpdateView();
        chocolateyClient.Children.Add(StewardVisuals.SettingsRow(chocolateyClientSummary.Summary, chocolateyClientSummary.UnavailableFields[0].Text, ChocolateyClientUpdateEntry(chocolateyClientSummary)));
        panel.Children.Add(StewardVisuals.SettingsCard("\uE7B8", ChocolateyClientUpdateViewModel.SettingsCardTitle, ChocolateyClientUpdateViewModel.SettingsCardDescription, chocolateyClient));

        // Scan scope / AppX.
        StackPanel scope = new() { Spacing = StewardSpacing.Small };
        CheckBox local = new() { Content = "Always on", IsChecked = true, IsEnabled = false };
        StewardVisuals.Named(local, "Always on", "(Windows local inventory, required, local only)");
        scope.Children.Add(StewardVisuals.SettingsRow("Windows local inventory", "Installed apps that Windows reports for this PC. Always on, read-only and never shared.", local));
        ConnectorOption appx = viewModel.Connectors.Single(connector => connector.Name == "Current-user AppX/MSIX catalog");
        ToggleSwitch includeAppx = new() { IsOn = appx.IsEnabled, OffContent = "Off", OnContent = "On" };
        AutomationProperties.SetName(includeAppx, "Include current-user AppX/MSIX catalog");
        includeAppx.Toggled += async (_, _) => await ChangeConnectorAsync(appx.Name, includeAppx.IsOn);
        scope.Children.Add(StewardVisuals.SettingsRow("Include current-user AppX/MSIX catalog", "Store and packaged apps installed for your account. Read locally; nothing is shared.", includeAppx));
        panel.Children.Add(StewardVisuals.SettingsCard("", "Scan scope", "What a scan reads. Scanning never asks for administrator permission and never changes anything.", scope));

        // Schedule: nothing applies until "Save schedule", which saves the whole record through the register-then-save operation.
        ScheduleCardPresentation plan = SettingsPresenter.Schedule(scheduledSettings, scheduledStatus, isSavingSchedule, otherWorkActive: HasActiveOperation);
        StackPanel schedule = new() { Spacing = StewardSpacing.Small };
        RadioButtons cadence = new() { SelectedIndex = plan.CadenceIndex, IsEnabled = plan.IsCadenceEnabled };
        foreach (string option in SettingsPresenter.CadenceOptions) cadence.Items.Add(option);
        AutomationProperties.SetName(cadence, "Scheduled check cadence");
        TimePicker startTime = new() { Time = plan.LocalStartTime.ToTimeSpan(), IsEnabled = plan.IsTimeEnabled };
        AutomationProperties.SetName(startTime, "Local start time");
        ComboBox weeklyDay = new() { IsEnabled = plan.IsWeekdayEnabled, MinWidth = 160 };
        // Audit P-M6: localized day names; the selection stays index-based (DayOfWeek order), so what is saved is unchanged.
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>()) weeklyDay.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day));
        weeklyDay.SelectedIndex = (int)plan.WeeklyDay;
        AutomationProperties.SetName(weeklyDay, "Weekly day");
        ToggleSwitch scheduleEnabled = new() { IsOn = plan.IsEnabled, IsEnabled = plan.IsEnableToggleEnabled, OffContent = "Not scheduled", OnContent = "On · runs for your account only, no background service" };
        AutomationProperties.SetName(scheduleEnabled, "Enable scheduled checks");
        ToggleSwitch notifications = new() { IsOn = plan.NotificationsEnabled, IsEnabled = plan.IsNotificationsToggleEnabled, OffContent = "Off", OnContent = "Attention and repeated scan failures only" };
        AutomationProperties.SetName(notifications, "Windows notifications");
        TextBlock scheduleSummary = new() { Text = isSavingSchedule ? "Saving…" : plan.Summary, TextWrapping = TextWrapping.Wrap };
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
        schedule.Children.Add(scheduleSummary);
        schedule.Children.Add(StewardVisuals.SettingsRow("Check cadence", "Manual only means PC Steward checks only when you start a scan.", cadence));
        schedule.Children.Add(StewardVisuals.SettingsRow("Local start time", "Used for daily and weekly checks.", startTime));
        schedule.Children.Add(StewardVisuals.SettingsRow("Weekly day", "Used for weekly checks.", weeklyDay));
        schedule.Children.Add(StewardVisuals.SettingsRow("Enable scheduled checks", "Creates a current-user scheduled check. Unavailable with Manual only.", scheduleEnabled));
        schedule.Children.Add(StewardVisuals.SettingsRow("Windows notifications", "Only when something needs your attention or scans keep failing.", notifications));
        schedule.Children.Add(saveSchedule);
        foreach (DetailFact fact in plan.Status) schedule.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        panel.Children.Add(StewardVisuals.SettingsCard("", "Schedule", SettingsPresenter.ScheduleDescription, schedule));

        StackPanel decisions = new() { Spacing = StewardSpacing.Small };
        Button openDecisions = StewardVisuals.ActionButton("Open Decisions");
        openDecisions.Click += (_, _) => NavigateTo(ShellPage.Decisions);
        decisions.Children.Add(StewardVisuals.SettingsRow(activeDecisions.Count == 0 && manualMappings.Count == 0 ? "No active pins, ignores or confirmed package identities." : $"{activeDecisions.Count + manualMappings.Count:N0} active decision{(activeDecisions.Count + manualMappings.Count == 1 ? string.Empty : "s")}", "Review or reverse them on the Decisions page.", openDecisions));
        panel.Children.Add(StewardVisuals.SettingsCard("", "Decisions", "Pins, ignores and package identities you confirmed. Each one is reversible.", decisions));

        // Privacy & exports (FR-DAT-01/03) and Copy diagnostic (FR-HIS-05).
        StackPanel privacy = new() { Spacing = StewardSpacing.Small };
        privacy.Children.Add(StewardVisuals.InformationBanner("Exports can identify this PC", SettingsPresenter.ExportWarning, "attention"));
        StewardWrapPanel exportActions = StewardVisuals.ActionRow();
        Button exportCsv = StewardVisuals.ActionButton("Export inventory CSV"); exportCsv.Click += async (_, _) => await SaveExportAsync(false); exportActions.Children.Add(exportCsv);
        Button exportJson = StewardVisuals.ActionButton("Export inventory JSON"); exportJson.Click += async (_, _) => await SaveExportAsync(true); exportActions.Children.Add(exportJson);
        privacy.Children.Add(StewardVisuals.SettingsRow("Export inventory", "Saves a file only where you choose. Nothing is uploaded.", exportActions));
        DiagnosticCopyPresentation diagnostic = SettingsPresenter.DiagnosticCopy(activityHistoryUnavailable);
        Button copyDiagnostic = new() { Content = diagnostic.Label, IsEnabled = diagnostic.IsEnabled };
        AutomationProperties.SetName(copyDiagnostic, diagnostic.AccessibleName);
        TextBlock copied = new() { Text = diagnostic.UnavailableReason ?? string.Empty, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(copied, AutomationLiveSetting.Polite);
        copyDiagnostic.Click += (_, _) => CopyDiagnostic(copied);
        privacy.Children.Add(StewardVisuals.SettingsRow(diagnostic.Label, diagnostic.Description, copyDiagnostic));
        privacy.Children.Add(copied);
        panel.Children.Add(StewardVisuals.SettingsCard("", "Privacy & exports", "PC Steward keeps everything on this PC. You decide what leaves it.", privacy));

        // Data & reset subpage (SPEC §17.3).
        StackPanel data = new() { Spacing = StewardSpacing.Small };
        foreach (DetailFact fact in DataResetPresenter.StorageSummary(DataResetInputsNow())) data.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        Button openData = StewardVisuals.ActionButton("Open Data & reset");
        openData.Click += (_, _) => NavigateTo(ShellPage.Privacy);
        data.Children.Add(StewardVisuals.SettingsRow("Clear data or reset PC Steward", "Separate controls for scan data, downloaded files, source metadata, logs and history, and factory reset.", openData));
        panel.Children.Add(StewardVisuals.SettingsCard("", "Data & reset", "What PC Steward stores, how much space it uses, and how to remove it.", data));

        StackPanel about = new() { Spacing = StewardSpacing.Small };
        Button openAbout = StewardVisuals.ActionButton("About PC Steward");
        openAbout.Click += (_, _) => NavigateTo(ShellPage.About);
        about.Children.Add(StewardVisuals.SettingsRow(AppIdentity.DisplayName, "Version, privacy and how updates and administrator permission work.", openAbout));
        panel.Children.Add(StewardVisuals.SettingsCard("", "About", null, about));
        return panel;
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
