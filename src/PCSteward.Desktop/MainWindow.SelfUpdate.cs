using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using PCSteward.Application;
using PCSteward.Execution;
using PCSteward.SelfUpdate;

namespace PCSteward.Desktop;

/// <summary>
/// ADR-032 revision 2 §1, §5, §6 and §8: the "PC Steward updates" card on About. Everything starts from the owner's own
/// click: Check for updates (four anonymous GitHub reads), Download and check (the installer and the desktop's first check),
/// Install (one Windows prompt; PC Steward then closes so the protected helper copy can install, and that copy, started
/// unelevated, opens PC Steward again once the install has finished). When PC Steward opens, a
/// recorded update is decided once and shown here and on Home. Nothing runs in the background or on a schedule.
/// </summary>
public sealed partial class MainWindow
{
    private enum SelfUpdateStage { Idle, Checking, Checked, Downloading, Downloaded, Installing, Closing }

    private SelfUpdateStage selfUpdateStage;
    private SelfUpdateCheck? selfUpdateCheck;
    private VerifiedRelease? selfUpdateRelease;
    private string? selfUpdateMsiSha256;
    private string? selfUpdateMessage;
    private PcStewardUpdateStatus? selfUpdateStatus;
    private Task? selfUpdateStatusLoad;
    private PcStewardSelfUpdate? selfUpdater;
    private bool selfUpdaterResolved;

    /// <summary>This build's MSI version (major.minor.patch), the form the installer and the helper use.</summary>
    private static string RunningMsiVersion() => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? string.Empty;

    private PcStewardSelfUpdate? SelfUpdater()
    {
        if (!selfUpdaterResolved)
        {
            selfUpdater = PcStewardSelfUpdate.TryCreate();
            selfUpdaterResolved = true;
        }
        return selfUpdater;
    }

    /// <summary>§8: decides a recorded update once per launch, off the UI thread, then shows it.</summary>
    private async Task LoadSelfUpdateStatusAsync()
    {
        PcStewardSelfUpdate? updater = SelfUpdater();
        if (updater is null) return;
        string running = RunningMsiVersion();
        PcStewardUpdateStatus? status;
        try { status = await Task.Run(() => updater.ReadJob(running, DateTimeOffset.UtcNow)).ConfigureAwait(true); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { status = null; }
        if (status is null || windowClosed) return;
        selfUpdateStatus = status;
        // A failure that blocks further updates (ADR-032 §8) is kept and decided again at the next launch.
        if (status.IsFinal) updater.ForgetJob();
        if (navigation.Current is ShellPage.Home or ShellPage.About) Render();
    }

    /// <summary>The Home info bar for a recorded update that needs the owner's attention or just finished.</summary>
    private InfoBar? SelfUpdateHomeBanner()
    {
        if (selfUpdateStatus is not { } status) return null;
        string tone = status.State switch
        {
            PcStewardUpdateState.Updated => "success",
            PcStewardUpdateState.InProgress or PcStewardUpdateState.RestartToFinish => "attention",
            _ => "blocked",
        };
        HyperlinkButton openAbout = new() { Content = "Open About" };
        AutomationProperties.SetName(openAbout, "Open About to see the PC Steward update");
        openAbout.Click += (_, _) => NavigateTo(ShellPage.About);
        return StewardVisuals.InformationBanner("PC Steward update", SelfUpdateText.Describe(status), tone, openAbout);
    }

    private Border BuildSelfUpdateCard()
    {
        StackPanel card = new() { Spacing = StewardSpacing.Small };
        card.Children.Add(SectionHeading("PC Steward updates"));
        card.Children.Add(PageCopy("PC Steward checks for a newer version only when you ask. It downloads, checks and installs one only after you choose to, and Windows asks for permission once."));
        if (selfUpdateStatus is { } status) card.Children.Add(PageCopy(SelfUpdateText.Describe(status)));

        TextBlock state = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite);
        state.Text = selfUpdateMessage ?? (selfUpdateCheck is { } check ? SelfUpdateText.Describe(check) : string.Empty);
        card.Children.Add(state);

        if (selfUpdateRelease is { } release && selfUpdateStage is SelfUpdateStage.Checked or SelfUpdateStage.Downloaded)
        {
            StewardWrapPanel links = StewardVisuals.ActionRow();
            links.Children.Add(new HyperlinkButton { Content = "Release notes", NavigateUri = release.ReleasePage });
            links.Children.Add(new HyperlinkButton { Content = "The build on GitHub", NavigateUri = release.RunPage });
            links.Children.Add(new HyperlinkButton { Content = "The source commit", NavigateUri = release.CommitPage });
            card.Children.Add(links);
        }

        StewardWrapPanel actions = StewardVisuals.ActionRow();
        bool busy = selfUpdateStage is SelfUpdateStage.Checking or SelfUpdateStage.Downloading or SelfUpdateStage.Installing or SelfUpdateStage.Closing;
        bool blocked = selfUpdateStatus is { State: PcStewardUpdateState.InProgress or PcStewardUpdateState.RestartToFinish } or { BlocksUpdates: true };
        if (busy)
        {
            actions.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
        }
        else if (selfUpdateStage == SelfUpdateStage.Downloaded)
        {
            Button install = StewardVisuals.PrimaryButton("Install", "the checked update (Windows asks for permission once; PC Steward then closes)");
            install.IsEnabled = !blocked && !HasActiveOperation;
            install.Click += async (_, _) => await InstallSelfUpdateAsync();
            actions.Children.Add(install);
        }
        else if (selfUpdateStage == SelfUpdateStage.Checked && selfUpdateCheck?.Status == SelfUpdateCheckStatus.NewerRelease)
        {
            Button download = StewardVisuals.PrimaryButton("Download and check", "the installer for " + selfUpdateRelease?.Version.Text);
            download.IsEnabled = !blocked;
            download.Click += async (_, _) => await DownloadSelfUpdateAsync();
            actions.Children.Add(download);
        }
        if (!busy)
        {
            Button checkNow = StewardVisuals.ActionButton("Check for a new version", "of PC Steward on GitHub");
            checkNow.IsEnabled = !blocked;
            checkNow.Click += async (_, _) => await CheckSelfUpdateAsync();
            actions.Children.Add(checkNow);
        }
        card.Children.Add(actions);
        return Card(card);
    }

    private async Task CheckSelfUpdateAsync()
    {
        if (!ReleaseVersion.TryParse(ProductVersion(), out ReleaseVersion? running) || running is null)
        {
            ShowSelfUpdate(SelfUpdateStage.Idle, "This build's version could not be read, so PC Steward cannot check for updates.");
            return;
        }
        DiscardSelfUpdateDownload();
        selfUpdateCheck = null;
        selfUpdateRelease = null;
        ShowSelfUpdate(SelfUpdateStage.Checking, "Checking GitHub for a newer PC Steward…");
        SelfUpdateCheck check;
        using (GitHubReleaseClient client = new())
        {
            try { check = await client.CheckAsync(running, CancellationToken.None).ConfigureAwait(true); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                check = new(SelfUpdateCheckStatus.CouldNotCheck, SelfUpdateRefusal.UnexpectedError, null, DateTimeOffset.UtcNow);
            }
        }
        selfUpdateCheck = check;
        selfUpdateRelease = check.Status == SelfUpdateCheckStatus.NewerRelease ? check.Release : null;
        ShowSelfUpdate(SelfUpdateStage.Checked, null);
    }

    private async Task DownloadSelfUpdateAsync()
    {
        if (selfUpdateRelease is not { } release || SelfUpdater() is not { } updater)
        {
            ShowSelfUpdate(SelfUpdateStage.Idle, "PC Steward can update itself only on Windows from its installed copy.");
            return;
        }
        ShowSelfUpdate(SelfUpdateStage.Downloading, "Downloading PC Steward " + release.Version.Text + "…");
        string? sha = null;
        PcStewardUpdatePrecheck precheck = PcStewardUpdatePrecheck.Unreadable;
        bool downloaded = false;
        try
        {
            using PcStewardDownload? download = updater.BeginMsiDownload();
            if (download is not null)
            {
                using GitHubReleaseClient client = new();
                SelfUpdateDownload? result = await client.DownloadMsiAsync(release, download.Stream, CancellationToken.None).ConfigureAwait(true);
                if (result is not null && download.Commit(result.Sha256) && updater.SaveCheckZip(release.CheckZip, release.CheckZipSha256))
                {
                    downloaded = true;
                    sha = result.Sha256;
                    string tables = release.MsiTablesSha256;
                    precheck = await Task.Run(() => updater.Precheck(result.Sha256, tables)).ConfigureAwait(true);
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { downloaded = false; }

        if (!downloaded || sha is null)
        {
            if (sha is not null) updater.DiscardDownloads(sha, release.CheckZipSha256);
            ShowSelfUpdate(SelfUpdateStage.Checked, SelfUpdateText.Describe(SelfUpdateRefusal.MsiDownloadFailed) + " Nothing was installed.");
            return;
        }
        if (precheck != PcStewardUpdatePrecheck.Passed)
        {
            updater.DiscardDownloads(sha, release.CheckZipSha256);
            ShowSelfUpdate(SelfUpdateStage.Idle, SelfUpdateText.Describe(precheck));
            return;
        }
        selfUpdateMsiSha256 = sha;
        ShowSelfUpdate(SelfUpdateStage.Downloaded, SelfUpdateText.Describe(precheck));
    }

    private async Task InstallSelfUpdateAsync()
    {
        if (selfUpdateRelease is not { } release || selfUpdateMsiSha256 is not { } sha || SelfUpdater() is not { } updater) return;
        ContentDialog confirm = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Install PC Steward " + release.Version.Text + "?",
            Content = PageCopy("Windows will ask for permission once. PC Steward's administrator step checks the installer again, confirms the release with GitHub, and then PC Steward closes while Windows installs the update. It opens again by itself when the install has finished."),
            PrimaryButtonText = "Install",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Close,
        };
        ContentDialogResult answer;
        // Windows allows one open dialog per window; a second one throws, which counts as "Not now".
        try { answer = await confirm.ShowAsync(); }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException) { return; }
        if (answer != ContentDialogResult.Primary) return;
        // PC Steward closes once the session is ready, so no other work may be running or start meanwhile: Installing and
        // Closing count as an active operation (HasActiveOperation) until the window exits.
        if (HasActiveOperation)
        {
            ShowSelfUpdate(SelfUpdateStage.Downloaded, "Another task is running. Install the update once it has finished.");
            return;
        }
        ShowSelfUpdate(SelfUpdateStage.Installing, "Waiting for Windows permission and PC Steward's administrator checks…");
        PcStewardApprovedUpdate approved = new(RunningMsiVersion(), release.Version.Major + "." + release.Version.Minor + "." + release.Version.Patch,
            release.RunId, release.SourceCommit, release.CheckZipSha256, sha);
        PcStewardInstallResult result = await updater.InstallAsync(approved, DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(true);
        if (result.Outcome is PcStewardInstallOutcome.Ready or PcStewardInstallOutcome.ReadyJobNotSaved)
        {
            ShowSelfUpdate(SelfUpdateStage.Closing, SelfUpdateText.Describe(result));
            // The finish copy waits for PC Steward to close before it installs anything.
            await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(true);
            Microsoft.UI.Xaml.Application.Current.Exit();
            return;
        }
        if (result.Outcome is PcStewardInstallOutcome.Refused) DiscardSelfUpdateDownload();
        ShowSelfUpdate(result.Outcome is PcStewardInstallOutcome.Refused ? SelfUpdateStage.Idle : SelfUpdateStage.Downloaded, SelfUpdateText.Describe(result));
    }

    private void DiscardSelfUpdateDownload()
    {
        if (selfUpdateMsiSha256 is { } sha && selfUpdateRelease is { } release) SelfUpdater()?.DiscardDownloads(sha, release.CheckZipSha256);
        selfUpdateMsiSha256 = null;
    }

    private void ShowSelfUpdate(SelfUpdateStage stage, string? message)
    {
        selfUpdateStage = stage;
        selfUpdateMessage = message;
        if (windowClosed) return;
        if (navigation.Current == ShellPage.About) Render();
        if (message is not null) StewardVisuals.Announce(Content, message, "pc-steward-self-update");
    }
}
