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
    private StackPanel BuildOnboardingPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Start fresh with PC Steward", "Factory reset is complete. No scan or source connection was started automatically.", "Fresh local state"));
        panel.Children.Add(StewardVisuals.InformationBanner("PC Steward data was removed", "Inventory, source evidence, settings, decisions, history, app-owned downloads, logs, and the scheduled check were included in the reset.", "success"));
        panel.Children.Add(StewardVisuals.EmptyState("\uE721", "Build a new local inventory", "When you continue, PC Steward first verifies the fresh state and then opens the read-only inventory screen. It does not start the scan for you."));
        Button continueButton = StewardVisuals.PrimaryButton("Continue to local inventory", "after factory reset (local only)");
        continueButton.Click += async (_, _) =>
        {
            if (onboardingAcknowledging) return;
            onboardingAcknowledging = true;
            continueButton.IsEnabled = false;
            PostResetOnboardingOutcome outcome = PostResetOnboardingFlow.Evaluate(
                () => acknowledgeResetOnboarding?.Invoke() ?? new FactoryResetResult(true, []),
                () => FactoryReset.GetMarkerState(),
                LoadSavedInventory);
            if (!outcome.AcknowledgementSucceeded)
            {
                string details = string.Join(", ", outcome.AcknowledgementResult.Residuals.Select(item => item.Reason));
                if (!string.IsNullOrWhiteSpace(details))
                {
                    details = $"{details} | marker state: {outcome.MarkerState}";
                }
                else
                {
                    details = $"Reset marker state: {outcome.MarkerState}";
                }
                // Blame-free message; the residual reasons and marker state stay available under "Technical details".
                await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Factory reset still needs attention", Content = StewardVisuals.MessageWithDetails("PC Steward couldn't confirm that the reset finished. Nothing was scanned. Close apps that might be using PC Steward's files, then try again.", StartupNoticeText.SanitizedDetails(details)), CloseButtonText = "Close" }.ShowAsync();
                onboardingAcknowledging = false;
                continueButton.IsEnabled = true;
                return;
            }
            if (!outcome.ScanNavigationRequested)
            {
                await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Fresh state could not be loaded", Content = "PC Steward could not confirm and reload the fresh local state. No scan was started.", CloseButtonText = "Close" }.ShowAsync();
                onboardingAcknowledging = false;
                continueButton.IsEnabled = true;
                return;
            }
            navigation.Reset(ShellPage.Scan); Render();
        };
        panel.Children.Add(continueButton);
        return panel;
    }
}
