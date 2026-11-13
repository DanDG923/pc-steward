using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Storage;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PCSteward.Desktop;

public sealed partial class MainWindow
{
    // Task 64 stage 4: the Diagnostics page's state. Nothing is read until the page is opened, and nothing is collected until the
    // owner agrees on the consent dialog; the consent is spent by that one collection.
    private IReadOnlyList<DiagnosticSnapshotSummary>? diagnosticSnapshots;
    private bool diagnosticSnapshotsLoading;
    private bool diagnosticSnapshotsUnavailable;
    private DiagnosticSnapshot? diagnosticSelected;
    private bool diagnosticSelectedLoading;
    private string? diagnosticsNotice;
    private CancellationTokenSource? diagnosticsCollection;
    // Codex review on #105: bumped when scan data is cleared. A collection saves only if no clear began since it started, and the
    // check runs inside the same exclusive data lease as the clear, so a clear can never be followed by older diagnostic data.
    private int diagnosticsGeneration;

    /// <summary>
    /// FR-DIA-01–05 (Task 64 stage 4, ADR-027): the local diagnostic timeline. "Collect a timeline…" asks for consent each time,
    /// then reads the chosen sources once, without administrator permission, and saves one immutable snapshot. The newest saved
    /// timeline is shown day by day with every source's coverage; a record lists the PC Steward changes within 15 minutes of it,
    /// with the fixed note that nearby is not a cause. "Export…" previews the file, warns, and saves only where the owner chooses.
    /// </summary>
    private StackPanel BuildDiagnosticsPage()
    {
        StackPanel panel = CreatePagePanel();
        bool collecting = diagnosticsCollection is not null;
        Button collect = StewardVisuals.PrimaryButton(DiagnosticsPresenter.CollectLabel, DiagnosticsPresenter.CollectHelp);
        collect.IsEnabled = !collecting;
        collect.Click += async (_, _) => await CollectDiagnosticTimelineAsync();
        panel.Children.Add(StewardVisuals.PageHeader(DiagnosticsPresenter.Title, DiagnosticsPresenter.PageDescription, action: collect));

        if (collecting)
        {
            Button stop = StewardVisuals.ActionButton(DiagnosticsPresenter.CancelCollectLabel);
            stop.Click += (_, _) => diagnosticsCollection?.Cancel();
            StackPanel progress = new() { Spacing = StewardSpacing.Small };
            progress.Children.Add(new ProgressBar { IsIndeterminate = true });
            TextBlock text = new() { Text = DiagnosticsPresenter.CollectingText, Style = AppStyle("StewardBodyStyle"), TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
            progress.Children.Add(text);
            progress.Children.Add(stop);
            panel.Children.Add(Card(progress));
        }
        // FR-DIA-03 (Codex review on #105): a timeline that expired while it was open is never shown again from memory.
        if (diagnosticSelected is { } open && open.IsExpired(DateTimeOffset.UtcNow))
        {
            diagnosticSelected = null;
            diagnosticSnapshots = null;
            diagnosticsNotice = DiagnosticsPresenter.UnavailableText;
        }
        if (diagnosticsNotice is not null)
            panel.Children.Add(StewardVisuals.InformationBanner(DiagnosticsPresenter.Title, diagnosticsNotice, "neutral"));

        if (diagnosticSnapshots is null)
        {
            if (!diagnosticSnapshotsLoading) _ = LoadDiagnosticSnapshotsAsync(openNewest: true);
            panel.Children.Add(PageCopy("Reading saved timelines…"));
            return panel;
        }
        if (diagnosticSnapshotsUnavailable)
            panel.Children.Add(StewardVisuals.InformationBanner("Saved timelines unavailable", "PC Steward couldn't read its saved timelines just now. Nothing was changed; try again later.", "neutral"));
        if (diagnosticSnapshots.Count == 0)
        {
            if (!diagnosticSnapshotsUnavailable && !collecting)
            {
                Button start = StewardVisuals.PrimaryButton(DiagnosticsPresenter.CollectLabel, DiagnosticsPresenter.CollectHelp);
                start.Click += async (_, _) => await CollectDiagnosticTimelineAsync();
                panel.Children.Add(StewardVisuals.EmptyState("", DiagnosticsPresenter.EmptyTitle, DiagnosticsPresenter.EmptyText, start));
            }
            return panel;
        }

        if (diagnosticSelectedLoading) panel.Children.Add(PageCopy("Opening the timeline…"));
        else if (diagnosticSelected is { } snapshot) AddDiagnosticTimeline(panel, snapshot);
        if (diagnosticSnapshots.Count > 1 || diagnosticSelected is null) AddSavedDiagnosticTimelines(panel);
        return panel;
    }

    private void AddSavedDiagnosticTimelines(StackPanel panel)
    {
        StackPanel section = new() { Spacing = StewardSpacing.Small };
        section.Children.Add(SectionHeading(DiagnosticsPresenter.SavedHeading));
        List<FrameworkElement> rows = [];
        foreach (DiagnosticSnapshotSummary summary in diagnosticSnapshots ?? [])
        {
            DiagnosticsSnapshotRow row = DiagnosticsPresenter.SnapshotRow(summary, DateTimeOffset.UtcNow);
            bool open = diagnosticSelected?.Id == row.Id;
            Button item = StewardVisuals.NavigationRow("", row.Title, row.Summary, row.HasCoverage ? row.IsPartial ? "attention" : "neutral" : "attention");
            AutomationProperties.SetName(item, row.AccessibleName);
            if (open) AutomationProperties.SetItemStatus(item, "Shown above");
            item.IsEnabled = !open && !diagnosticSelectedLoading;
            Guid id = row.Id;
            item.Click += async (_, _) => await OpenDiagnosticSnapshotAsync(id);
            rows.Add(item);
        }
        section.Children.Add(StewardVisuals.ListCard(rows));
        panel.Children.Add(section);
    }

    private void AddDiagnosticTimeline(StackPanel panel, DiagnosticSnapshot snapshot)
    {
        DiagnosticsTimelinePresentation timeline = DiagnosticsPresenter.Timeline(snapshot, DateTimeOffset.UtcNow);
        Button export = StewardVisuals.ActionButton(DiagnosticsPresenter.ExportLabel, DiagnosticsPresenter.ExportHelp);
        export.Click += async (_, _) => await ExportDiagnosticSnapshotAsync(snapshot);
        StackPanel heading = new() { Spacing = StewardSpacing.XSmall };
        heading.Children.Add(SectionHeading(timeline.Heading));
        heading.Children.Add(new TextBlock { Text = timeline.Summary, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
        StewardWrapPanel actions = StewardVisuals.ActionRow();
        actions.Children.Add(export);
        heading.Children.Add(actions);
        panel.Children.Add(heading);
        if (timeline.CoverageWarning is not null)
            panel.Children.Add(StewardVisuals.InformationBanner(snapshot.HasCoverage ? "Partly read" : "Nothing could be read", timeline.CoverageWarning, "attention"));

        // FR-DIA-01: every source is listed with its result, including the ones the owner left out.
        StackPanel coverage = new() { Spacing = 0 };
        foreach (DiagnosticsCoverageLine line in timeline.Coverage)
            coverage.Children.Add(StewardVisuals.EvidenceRow(line.Label, line.Result));
        Expander coverageRow = StewardVisuals.ExpanderRow("", DiagnosticsPresenter.CoverageHeading, string.Join(" · ", timeline.Coverage.Select(line => $"{line.Label}: {line.Result}")), coverage, snapshot.IsPartial ? "attention" : "neutral");
        coverageRow.IsExpanded = snapshot.IsPartial;
        panel.Children.Add(coverageRow);

        foreach (DiagnosticsDay day in timeline.Days)
        {
            StackPanel daySection = new() { Spacing = StewardSpacing.Small };
            daySection.Children.Add(SectionHeading(day.Heading));
            StackPanel entries = new() { Spacing = StewardSpacing.XSmall };
            foreach (DiagnosticsEntry entry in day.Entries) entries.Children.Add(DiagnosticEntryRow(entry));
            daySection.Children.Add(entries);
            panel.Children.Add(daySection);
        }
        if (timeline.Untimed.Count != 0)
        {
            StackPanel untimed = new() { Spacing = StewardSpacing.Small };
            untimed.Children.Add(SectionHeading(DiagnosticsPresenter.UntimedHeading));
            untimed.Children.Add(new TextBlock { Text = DiagnosticsPresenter.UntimedNote, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
            StackPanel entries = new() { Spacing = StewardSpacing.XSmall };
            foreach (DiagnosticsEntry entry in timeline.Untimed) entries.Children.Add(DiagnosticEntryRow(entry));
            untimed.Children.Add(entries);
            panel.Children.Add(untimed);
        }
        if (timeline.RecordCount == 0 && snapshot.HasCoverage)
            panel.Children.Add(PageCopy("Nothing matching was recorded in these seven days by the sources that were read."));
        if (timeline.DuplicatesNote is not null) panel.Children.Add(StewardVisuals.FootNote("", timeline.DuplicatesNote));
    }

    /// <summary>One timeline record: icon, heading, sentence and time; its facts, fixed notes and nearby PC Steward changes are inside.</summary>
    private static Expander DiagnosticEntryRow(DiagnosticsEntry entry)
    {
        TextBlock time = new() { Text = entry.TimeText, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.NoWrap };
        Grid header = StewardVisuals.ListRow(StewardVisuals.IconWell(entry.Glyph, StewardStatus.ToneName(entry.Tone)), entry.Heading, entry.Summary, time);
        header.Padding = new Thickness(0, StewardSpacing.Small, 0, StewardSpacing.Small);
        StackPanel details = new() { Spacing = StewardSpacing.XSmall };
        foreach (DetailFact fact in entry.Facts)
        {
            Grid line = StewardVisuals.EvidenceRow(fact.Label, fact.Value);
            foreach (TextBlock text in line.Children.OfType<TextBlock>()) text.IsTextSelectionEnabled = true;
            details.Children.Add(line);
        }
        if (entry.Nearby.Count != 0)
        {
            details.Children.Add(SectionHeading(DiagnosticsPresenter.NearbyHeading, AutomationHeadingLevel.Level3));
            foreach (DiagnosticsNearbyLine nearby in entry.Nearby) details.Children.Add(StewardVisuals.IconLine("", nearby.Text));
        }
        foreach (string note in entry.Notes) details.Children.Add(StewardVisuals.FootNote("", note));
        Expander expander = new() { Header = header, Content = details, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(expander, entry.AccessibleName);
        AutomationProperties.SetHelpText(expander, entry.Summary);
        return expander;
    }

    /// <summary>
    /// FR-DIA-04 (Codex review on #105): called when the owner confirms Clear scan data, before the clear starts. A collection that
    /// is running is stopped and can no longer save, and the page reads the list again when it is next shown.
    /// </summary>
    private void ForgetDiagnosticTimelines()
    {
        Interlocked.Increment(ref diagnosticsGeneration);
        diagnosticsCollection?.Cancel();
        diagnosticSnapshots = null;
        diagnosticSelected = null;
        diagnosticsNotice = null;
    }

    private async Task LoadDiagnosticSnapshotsAsync(bool openNewest)
    {
        diagnosticSnapshotsLoading = true;
        IReadOnlyList<DiagnosticSnapshotSummary> list;
        bool unavailable = false;
        try { list = await DataOperationLease.RunReadAsync(token => diagnostics.Store.ListDiagnosticSnapshotsAsync(DateTimeOffset.UtcNow, token), CancellationToken.None); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { list = []; unavailable = true; }
        finally { diagnosticSnapshotsLoading = false; }
        if (windowClosed) return;
        diagnosticSnapshots = list;
        diagnosticSnapshotsUnavailable = unavailable;
        if (diagnosticSelected is not null && !list.Any(item => item.Id == diagnosticSelected.Id)) diagnosticSelected = null;
        if (openNewest && diagnosticSelected is null && list.Count != 0) { await OpenDiagnosticSnapshotAsync(list[0].Id); return; }
        if (navigation.Current == ShellPage.Diagnostics) Render();
    }

    private async Task OpenDiagnosticSnapshotAsync(Guid id)
    {
        diagnosticSelectedLoading = true;
        if (navigation.Current == ShellPage.Diagnostics) Render();
        DiagnosticSnapshotLoad load;
        try { load = await DataOperationLease.RunReadAsync(token => diagnostics.Store.LoadDiagnosticSnapshotAsync(id, DateTimeOffset.UtcNow, token), CancellationToken.None); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { load = new(DiagnosticSnapshotLoadStatus.Unreadable, null); }
        finally { diagnosticSelectedLoading = false; }
        if (windowClosed) return;
        // FR-DIA-03: a snapshot is shown whole or not at all.
        if (load.Status == DiagnosticSnapshotLoadStatus.Loaded && load.Snapshot is not null) diagnosticSelected = load.Snapshot;
        else diagnosticsNotice = load.Status == DiagnosticSnapshotLoadStatus.Unavailable ? DiagnosticsPresenter.UnavailableText : DiagnosticsPresenter.UnreadableText;
        if (navigation.Current == ShellPage.Diagnostics) Render();
    }

    /// <summary>
    /// FR-DIA-01 (ADR-027 per-collection consent): asks which sources to read, then collects once. Cancelling the dialog or the
    /// collection saves nothing and leaves earlier timelines as they were. The timeline being viewed is protected from capacity pruning.
    /// </summary>
    private async Task CollectDiagnosticTimelineAsync()
    {
        if (diagnosticsCollection is not null) return;
        StackPanel body = new() { Spacing = StewardSpacing.Small };
        body.Children.Add(new TextBlock { Text = DiagnosticsPresenter.ConsentIntro, TextWrapping = TextWrapping.Wrap });
        List<(CheckBox Box, DiagnosticSourceKind Source)> choices = [];
        foreach (DiagnosticsSourceChoice choice in DiagnosticsPresenter.SourceChoices)
        {
            StackPanel label = new() { Spacing = 0 };
            label.Children.Add(new TextBlock { Text = choice.Label, TextWrapping = TextWrapping.Wrap });
            label.Children.Add(new TextBlock { Text = choice.Reads, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
            CheckBox box = new() { Content = label, IsChecked = true };
            AutomationProperties.SetName(box, choice.Label);
            AutomationProperties.SetHelpText(box, choice.Reads);
            choices.Add((box, choice.Source));
            body.Children.Add(box);
        }
        body.Children.Add(new TextBlock { Text = DiagnosticsPresenter.ConsentNotRead, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = DiagnosticsPresenter.ConsentKeep, TextWrapping = TextWrapping.Wrap });
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = DiagnosticsPresenter.ConsentTitle,
            Content = new ScrollViewer { Content = body },
            PrimaryButtonText = DiagnosticsPresenter.ConsentConfirm,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        foreach ((CheckBox box, _) in choices)
        {
            box.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
            box.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = choices.Any(item => item.Box.IsChecked == true);
        }
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        DiagnosticSourceKind[] selected = [.. choices.Where(item => item.Box.IsChecked == true).Select(item => item.Source)];
        if (selected.Length == 0 || windowClosed) return;

        DiagnosticCollectionConsent consent = new(selected, DiagnosticCollectionPolicy.Lookback, DiagnosticCollectionPolicy.Version);
        int generation = Volatile.Read(ref diagnosticsGeneration);
        using CancellationTokenSource cancellation = new();
        diagnosticsCollection = cancellation;
        diagnosticsNotice = null;
        Render();
        DiagnosticSnapshot? saved = null;
        try
        {
            DiagnosticCollectionOrchestrator collection = diagnostics.CreateCollection();
            DiagnosticCollectionResult result = await Task.Run(() => collection.CollectAsync(consent, cancellation.Token), CancellationToken.None);
            if (result.Status != DiagnosticCollectionStatus.Completed || result.Snapshot is null) diagnosticsNotice = DiagnosticsPresenter.CancelledText;
            else
            {
                Guid[] protectedIds = diagnosticSelected is { } viewing ? [viewing.Id] : [];
                DiagnosticSnapshot snapshot = result.Snapshot;
                DiagnosticSnapshotSaveResult? save = await DataOperationLease.RunAsync<DiagnosticSnapshotSaveResult?>(async token =>
                    windowClosed || Volatile.Read(ref diagnosticsGeneration) != generation ? null
                        : await diagnostics.Store.SaveDiagnosticSnapshotAsync(snapshot, DateTimeOffset.UtcNow, protectedIds, token), CancellationToken.None);
                diagnosticsNotice = save is null ? DiagnosticsPresenter.CancelledText : DiagnosticsPresenter.SaveNote(save);
                if (save?.Saved == true) saved = snapshot;
            }
        }
        catch (OperationCanceledException) { diagnosticsNotice = DiagnosticsPresenter.CancelledText; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { diagnosticsNotice = DiagnosticsPresenter.SaveFailedText; }
        finally { diagnosticsCollection = null; }
        if (windowClosed) return;
        if (saved is not null) diagnosticSelected = saved;
        AnnounceCompletion(OperationAnnouncements.Completed(saved is not null ? "Diagnostic timeline collected" : "Diagnostic timeline not saved"), "diagnostics-collected");
        await LoadDiagnosticSnapshotsAsync(openNewest: false);
    }

    /// <summary>
    /// FR-DIA-05 (ADR-027 Export): preview exactly what the file holds, warn about sensitivity, then save only to the place the owner
    /// picks. The file is the same projection the page shows; nothing is attached or sent.
    /// </summary>
    private async Task ExportDiagnosticSnapshotAsync(DiagnosticSnapshot snapshot)
    {
        if (snapshot.IsExpired(DateTimeOffset.UtcNow)) { Render(); return; }
        DiagnosticsExportPreview preview = DiagnosticsPresenter.ExportPreview(snapshot);
        StackPanel body = new() { Spacing = StewardSpacing.Small };
        foreach (string line in preview.Lines) body.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        body.Children.Add(StewardVisuals.InformationBanner("Before you share it", preview.Warning, "attention"));
        if (preview.RefusalReason is not null) body.Children.Add(new TextBlock { Text = preview.RefusalReason, TextWrapping = TextWrapping.Wrap });
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = preview.Title,
            Content = new ScrollViewer { Content = body },
            PrimaryButtonText = "Choose where to save…",
            IsPrimaryButtonEnabled = preview.CanExport,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || !preview.CanExport) return;
        FileSavePicker picker = new() { SuggestedFileName = Path.GetFileNameWithoutExtension(preview.SuggestedFileName) };
        picker.FileTypeChoices.Add("JSON diagnostic timeline", [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;
        // FR-DIA-03/05 (Codex review on #105): the file is written from the store as it is now, never from memory, so a timeline that
        // expired or was cleared while the dialogs were open is not exported.
        DiagnosticSnapshotLoad current;
        try { current = await DataOperationLease.RunReadAsync(token => diagnostics.Store.LoadDiagnosticSnapshotAsync(snapshot.Id, DateTimeOffset.UtcNow, token), CancellationToken.None); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { current = new(DiagnosticSnapshotLoadStatus.Unreadable, null); }
        if (current.Status != DiagnosticSnapshotLoadStatus.Loaded || current.Snapshot is null)
        {
            diagnosticsNotice = current.Status == DiagnosticSnapshotLoadStatus.Unavailable ? DiagnosticsPresenter.UnavailableText : DiagnosticsPresenter.UnreadableText;
            diagnosticSelected = null;
            diagnosticSnapshots = null;
            if (!windowClosed && navigation.Current == ShellPage.Diagnostics) Render();
            return;
        }
        try
        {
            await FileIO.WriteBytesAsync(file, DiagnosticSnapshotExport.Serialize(current.Snapshot));
            diagnosticsNotice = $"Exported to {file.Name}.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { diagnosticsNotice = "The timeline couldn't be saved there. Nothing else was changed; try another place."; }
        if (!windowClosed && navigation.Current == ShellPage.Diagnostics) Render();
    }
}
