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
    private static StackPanel BuildAboutPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("About", "PC Steward lists the apps on this Windows 11 PC and helps you review and apply their updates. Everything stays on this PC."));
        panel.Children.Add(StewardVisuals.InformationBanner("Private, and you stay in control", "No account, cloud sync, usage tracking, always-running background service or automatic updates."));
        StackPanel scope = new() { Spacing = 8 };
        scope.Children.Add(SectionHeading("Updates and administrator permission"));
        scope.Children.Add(PageCopy(ElevationStory.Summary));
        scope.Children.Add(PageCopy(ElevationStory.HelperRequirement));
        panel.Children.Add(Card(scope));
        StackPanel data = new() { Spacing = 8 };
        data.Children.Add(SectionHeading("Data and removal"));
        data.Children.Add(PageCopy("Inventory, settings, decisions, history, source evidence, downloads and logs are stored locally under your PC Steward data root. Removing the extracted package removes its binaries but retains that data by default. Use Factory reset PC Steward to remove app-owned local data and the current-user scheduled check before removal."));
        panel.Children.Add(Card(data));
        panel.Children.Add(StewardVisuals.InformationBanner("Preview build", "This build is for private testing and is not a public release. Install PC Steward only from the private release channel you were given, and check its published checksum.", "attention"));
        StackPanel support = new() { Spacing = 8 };
        support.Children.Add(SectionHeading("Support and rollback"));
        support.Children.Add(PageCopy("If replacing PC Steward with a newer build fails, stop, copy the diagnostic from Settings › Privacy & exports, and use the rollback package from the same private release channel. Don't download PC Steward from mirrors or other download sites."));
        panel.Children.Add(Card(support));
        return panel;
    }
}
