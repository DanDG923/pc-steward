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
    /// FR-POL-01–05 (ADR-030 foundation 7): pins, ignores and confirmed package identities in one reversible view. UI polish phase 4
    /// (approved target T-Decisions): a calm empty state with "What each decision does", or the decisions grouped by kind in list cards.
    /// </summary>
    private StackPanel BuildDecisionsPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Decisions", DecisionsPresenter.PageDescription));
        AddDecisionsSection(panel);
        return panel;
    }

    /// <summary>
    /// FR-POL-01–05 (Task 73 Phase 3, ADR-030 foundation 7): pins, ignores (indefinitely or until a date) and confirmed package
    /// identities in one view, grouped by kind, each with its reason and its existing reversal. Each Remove button's accessible name
    /// starts with its label and names the product (Phase 1 focus rule). Removing a decision only reapplies the normal review rules;
    /// it never starts an update.
    /// </summary>
    private void AddDecisionsSection(StackPanel panel)
    {
        IReadOnlyList<DecisionRow> rows = DecisionsPresenter.Build(activeDecisions, manualMappings, viewModel.AllRows);
        if (rows.Count == 0)
        {
            panel.Children.Add(StewardVisuals.EmptyState("\uE8C8", DecisionsPresenter.EmptyTitle, DecisionsPresenter.EmptyMessage));
            StackPanel explainers = new() { Spacing = StewardSpacing.Small };
            explainers.Children.Add(SectionHeading("What each decision does"));
            // Colour option 2: one decorative hue per kind of decision (pin, skip, ignore, confirm).
            string[] hues = ["blue", "amber", "pink", "teal"];
            FrameworkElement[] cards = [.. DecisionsPresenter.Explainers.Select((explainer, index) => (FrameworkElement)StewardVisuals.RowCard(explainer.Glyph, explainer.Title, explainer.Description, tone: hues[index % hues.Length]))];
            explainers.Children.Add(StewardVisuals.ResponsiveColumns(cards, 380, StewardSpacing.XSmall));
            panel.Children.Add(explainers);
            return;
        }
        foreach (DecisionKind kind in new[] { DecisionKind.Pin, DecisionKind.Ignore, DecisionKind.Skip, DecisionKind.Mapping, DecisionKind.Other })
        {
            DecisionRow[] group = rows.Where(row => row.Kind == kind).ToArray();
            if (group.Length == 0) continue;
            StackPanel section = new() { Spacing = StewardSpacing.Small };
            section.Children.Add(SectionHeading(DecisionsPresenter.GroupHeading(kind, group.Length)));
            section.Children.Add(StewardVisuals.ListCard([.. group.Select(DecisionRowView)]));
            panel.Children.Add(section);
        }
        panel.Children.Add(StewardVisuals.FootNote("\uE946", DecisionsPresenter.RemoveNote));
    }

    /// <summary>One decision: its icon, the app, what was decided (with its date and any reason) and its Remove button.</summary>
    private FrameworkElement DecisionRowView(DecisionRow decision)
    {
        Button remove = new() { Content = decision.RemoveLabel, IsEnabled = !HasActiveOperation };
        AutomationProperties.SetName(remove, decision.RemoveAccessibleName);
        remove.Click += async (_, _) =>
        {
            remove.IsEnabled = false;
            try
            {
                // The existing reversal operations: a pin or ignore is reversed, a confirmed mapping is deleted.
                if (decision.Kind == DecisionKind.Mapping) await DataOperationLease.RunAsync(token => workflow.DeleteManualMappingAsync(decision.Id, token), CancellationToken.None);
                else await DataOperationLease.RunAsync(token => workflow.ReverseDecisionAsync(decision.Id, token), CancellationToken.None);
                LoadSavedInventory(); Render();
            }
            catch (InvalidOperationException) { remove.IsEnabled = true; await ShowDataOperationUnavailableAsync(); }
        };
        Grid line = StewardVisuals.ListRow(StewardVisuals.IconWell(decision.Glyph), decision.ProductName, $"{decision.Title} · {decision.Detail}", remove);
        if (decision.Reason is { } reason)
        {
            StackPanel copy = line.Children.OfType<StackPanel>().First();
            copy.Children.Add(new TextBlock { Text = $"Reason: {reason}", Style = AppStyle("StewardCaptionStyle"), IsTextSelectionEnabled = true });
        }
        return line;
    }
}
