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
    private StackPanel BuildStoragePage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Storage", "Space used by PC Steward's own records and downloaded update files.", "PC Steward data"));
        panel.Children.Add(StewardVisuals.InformationBanner("Only PC Steward's own files", "This page never covers Windows temporary files, other apps' caches, your documents, drivers, firmware or installed apps."));
        StackPanel summary = new() { Spacing = StewardSpacing.Small };
        foreach (DetailFact fact in DataResetPresenter.StorageSummary(DataResetInputsNow())) summary.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        summary.Children.Add(PageCopy("Source metadata, logs and update history have their own cards in Data & reset."));
        Button privacy = StewardVisuals.ActionButton("Open Data & reset");
        privacy.Click += (_, _) => NavigateTo(ShellPage.Privacy);
        summary.Children.Add(privacy);
        panel.Children.Add(StewardVisuals.SettingsCard("\uEDA2", "PC Steward storage", null, summary));
        return panel;
    }
}
