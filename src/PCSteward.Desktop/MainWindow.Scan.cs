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
    private StackPanel BuildScanPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Inventory scan", viewModel.IsScanning || scanCancellation is not null ? "PC Steward is reading the apps installed on this PC. Your last completed scan stays available until this one finishes." : viewModel.LastScanAt is null ? "Start a private, read-only scan. Nothing is installed, removed or changed, and no administrator permission is requested." : "Check what the scan covers before replacing your last completed scan.", "Read only"));
        // Status text, not a heading: the operation band's live region announces state changes.
        scanPageStatusText = new TextBlock { Text = $"{viewModel.ScanPhase} · elapsed {viewModel.Elapsed:mm\\:ss}", Style = AppStyle("StewardSubtitleStyle") };
        StackPanel state = new() { Spacing = 8 };
        state.Children.Add(scanPageStatusText);
        state.Children.Add(StewardVisuals.EvidenceRow("Saved inventory", viewModel.LastScanAt is null ? "No completed scan" : viewModel.LastScanCompletedLabel));
        state.Children.Add(StewardVisuals.EvidenceRow("Selected scope", viewModel.ScanModeLabel));
        state.Children.Add(StewardVisuals.EvidenceRow("Administrator access", "Not requested"));
        panel.Children.Add(Card(state));
        if (viewModel.LastScanWarnings.Count != 0) panel.Children.Add(StewardVisuals.InformationBanner("Some app sources couldn't be read fully", string.Join(" ", viewModel.LastScanWarnings) + " Data from your last completed scan is kept where available.", "attention"));
        if (!viewModel.IsScanning && scanCancellation is null)
        {
            StackPanel scope = new() { Spacing = 8 };
            scope.Children.Add(SectionHeading("What the scan covers"));
            scope.Children.Add(new CheckBox { Content = "Windows application inventory (required · read-only)", IsChecked = true, IsEnabled = false });
            ToggleSwitch winget = new() { Header = "WinGet and Chocolatey trusted sources", IsOn = viewModel.IsWinGetTrustedSourcesEnabled, OffContent = "Local only", OnContent = "Enabled after disclosure" };
            AutomationProperties.SetName(winget, "WinGet and Chocolatey trusted sources"); winget.Toggled += async (_, _) => await ChangeWinGetTrustedSourcesAsync(winget.IsOn); scope.Children.Add(winget);
            scope.Children.Add(PageCopy("Enabling these sources may disclose installed package identifiers to WinGet and Chocolatey Community. Scanning remains read-only and never asks for administrator permission; an update still needs its own review and confirmation."));
            panel.Children.Add(Card(scope));
        }
        StewardWrapPanel actions = StewardVisuals.ActionRow();
        // Label in name (audit §5): the accessible name starts with the visible "Start inventory" / "Replace saved inventory".
        Button scan = StewardVisuals.PrimaryButton(viewModel.LastScanAt is null ? "Start inventory" : "Replace saved inventory", "(read-only scan of this PC)");
        scan.IsEnabled = !HasActiveOperation;
        scan.Click += async (_, _) => await StartScanAsync(); actions.Children.Add(scan);
        if (viewModel.IsScanning || scanCancellation is not null) { Button cancel = StewardVisuals.ActionButton("Cancel scan"); cancel.AccessKey = "C"; cancel.Click += (_, _) => scanCancellation?.Cancel(); actions.Children.Add(cancel); }
        panel.Children.Add(actions);
        panel.Children.Add(StewardVisuals.InformationBanner("A scan changes nothing", "A scan never approves, downloads or installs an update. Package sources stay off until you turn them on."));
        return panel;
    }
}
