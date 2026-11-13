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
using System.Reflection;
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
    /// UI polish phase 4 (approved target T-About): an identity card (logo, name, preview build and version, Copy diagnostic), the
    /// preview-build warning, and "How PC Steward works" as expandable rows with the same words as before (one elevation story,
    /// audit P-H5). Copy diagnostic (FR-HIS-05) moved here from Settings; it copies the same sanitized bundle.
    /// </summary>
    private StackPanel BuildAboutPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("About", string.Empty));

        Grid identity = new() { ColumnSpacing = StewardSpacing.Section, RowSpacing = StewardSpacing.Medium };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Image logo = new() { Width = 48, Height = 48, Source = new BitmapImage(new Uri("ms-appx:///Assets/logo-mark-64.png")), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(logo, AccessibilityView.Raw);
        identity.Children.Add(logo);
        StackPanel name = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        TextBlock product = new() { Text = AppIdentity.DisplayName, Style = AppStyle("StewardSubtitleStyle") };
        AutomationProperties.SetHeadingLevel(product, AutomationHeadingLevel.Level2);
        name.Children.Add(product);
        name.Children.Add(new TextBlock { Text = $"Preview build {ProductVersion()} · Lists the apps on this PC and helps you review and apply their updates. Everything stays on this PC.", Style = AppStyle("StewardDescriptionStyle"), Foreground = StewardVisuals.Brush("StewardSecondaryTextBrush"), IsTextSelectionEnabled = true });
        Grid.SetColumn(name, 1);
        identity.Children.Add(name);
        // FR-HIS-05: Copy diagnostic copies the existing sanitized bundle; the confirmation (a polite live region) says what was copied.
        DiagnosticCopyPresentation diagnostic = SettingsPresenter.DiagnosticCopy(activityHistoryUnavailable);
        Button copyDiagnostic = new() { IsEnabled = diagnostic.IsEnabled, VerticalAlignment = VerticalAlignment.Center };
        copyDiagnostic.Content = StewardVisuals.IconLabel("\uE8C8", diagnostic.Label);
        AutomationProperties.SetName(copyDiagnostic, diagnostic.AccessibleName);
        ToolTipService.SetToolTip(copyDiagnostic, diagnostic.Description);
        Grid.SetColumn(copyDiagnostic, 2);
        identity.Children.Add(copyDiagnostic);
        TextBlock copied = new() { Text = diagnostic.UnavailableReason ?? string.Empty, Style = AppStyle("StewardDescriptionStyle") };
        AutomationProperties.SetLiveSetting(copied, AutomationLiveSetting.Polite);
        copyDiagnostic.Click += (_, _) => CopyDiagnostic(copied);
        StackPanel identityCard = new() { Spacing = StewardSpacing.Small };
        identityCard.Children.Add(identity);
        identityCard.Children.Add(new TextBlock { Text = diagnostic.Description, Style = AppStyle("StewardDescriptionStyle") });
        identityCard.Children.Add(copied);
        panel.Children.Add(Card(identityCard));

        panel.Children.Add(StewardVisuals.InformationBanner("Preview build", "PC Steward is still being tested. Get it only from its GitHub releases page.", "attention"));
        panel.Children.Add(BuildSelfUpdateCard());

        StackPanel works = new() { Spacing = StewardSpacing.XSmall };
        works.Children.Add(SectionHeading("How PC Steward works"));
        StackPanel privacy = new() { Spacing = StewardSpacing.Small };
        privacy.Children.Add(PageCopy("PC Steward has no account and sends no usage data. It never runs in the background as administrator and never installs anything you haven't reviewed and approved."));
        // ADR-035: the lists read each app's own icon; said here because it is a read PC Steward did not make before.
        privacy.Children.Add(PageCopy(AppIconRules.PrivacyNote));
        works.Children.Add(StewardVisuals.ExpanderRow("\uE72E", "Private, and you stay in control", "No account, cloud sync, usage tracking, always-running background service or automatic updates.", privacy, "green"));
        StackPanel elevation = new() { Spacing = StewardSpacing.Small };
        elevation.Children.Add(PageCopy(ElevationStory.Summary));
        elevation.Children.Add(PageCopy(ElevationStory.HelperRequirement));
        works.Children.Add(StewardVisuals.ExpanderRow("\uEA18", "Updates and administrator permission", "Checking never asks for permission. Windows asks once for each machine-wide app you approve.", elevation, "amber"));
        works.Children.Add(StewardVisuals.ExpanderRow("\uE74E", "Data and removal", "Everything is kept locally. Factory reset removes PC Steward's data and its scheduled check.",
            PageCopy("Your app list, settings, decisions, history, downloads and logs are stored on this PC only. Uninstalling PC Steward leaves that data in place. To remove it as well, use Factory reset (Settings, Data & reset) before you uninstall; it also removes the scheduled check."), "purple"));
        works.Children.Add(StewardVisuals.ExpanderRow("\uE777", "If updating PC Steward fails", "The version you have stays installed. Copy the diagnostic, then try again.",
            PageCopy("If an update to PC Steward can't finish, Windows puts the version you had back. Select Copy diagnostic above to keep a record of what happened, then check for a new version again later, or get the installer from the GitHub releases page. Don't download PC Steward from mirrors or other download sites."), "blue"));
        panel.Children.Add(works);
        panel.Children.Add(StewardVisuals.FootNote("\uE8A5", "Open source under the MIT licence."));
        return panel;
    }

    /// <summary>This build's version without its source revision suffix (for example "0.1.0-private-beta").</summary>
    private static string ProductVersion()
    {
        string? version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version)) return typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        int metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}
