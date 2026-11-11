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
    /// <summary>FR-POL-01–05 (ADR-030 foundation 7): pins, ignores and confirmed package identities in one reversible view.</summary>
    private StackPanel BuildDecisionsPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Decisions", "Pins, ignores and package identities you confirmed. Each one is reversible, and none of them starts an update."));
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
        panel.Children.Add(PageCopy(rows.Count == 0
            ? "No active pins, ignores, skipped versions or confirmed package identities. Decisions are optional, reversible, and never enable or start updates."
            : "Remove a decision to reapply the normal review rules on the next source check. Nothing is updated when you do."));
        foreach (DecisionKind kind in new[] { DecisionKind.Pin, DecisionKind.Ignore, DecisionKind.Skip, DecisionKind.Mapping, DecisionKind.Other })
        {
            DecisionRow[] group = rows.Where(row => row.Kind == kind).ToArray();
            if (group.Length == 0) continue;
            StackPanel section = new() { Spacing = 0 };
            section.Children.Add(SectionHeading(DecisionsPresenter.GroupHeading(kind, group.Length)));
            foreach (DecisionRow decision in group) section.Children.Add(DecisionRowView(decision));
            panel.Children.Add(Card(section));
        }
    }

    private Border DecisionRowView(DecisionRow decision)
    {
        Grid line = new() { ColumnSpacing = StewardSpacing.Medium };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(new FontIcon { Glyph = decision.Glyph, FontSize = 16, Foreground = StewardVisuals.Brush("StewardBrandBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        StackPanel copy = new() { Spacing = StewardSpacing.XSmall };
        copy.Children.Add(new TextBlock { Text = decision.ProductName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        copy.Children.Add(new TextBlock { Text = decision.Title, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        copy.Children.Add(new TextBlock { Text = decision.Detail, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        if (decision.Reason is { } reason) copy.Children.Add(new TextBlock { Text = $"Reason: {reason}", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        Grid.SetColumn(copy, 1); line.Children.Add(copy);
        Button remove = new() { Content = decision.RemoveLabel, VerticalAlignment = VerticalAlignment.Top, IsEnabled = !HasActiveOperation };
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
        Grid.SetColumn(remove, 2); line.Children.Add(remove);
        Border row = new() { BorderBrush = StewardVisuals.Brush("StewardBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, StewardSpacing.Medium, 0, StewardSpacing.Medium), Child = line };
        AutomationProperties.SetName(row, $"{decision.ProductName}: {decision.Title}");
        return row;
    }
}
