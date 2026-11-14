using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Storage;

namespace PCSteward.Desktop;

/// <summary>
/// Task 67 stage 4b (FR-AI-01/03/04, FR-BUD-01; ADR-028, ADR-029): the "AI explanations" section of Settings and its set-up dialogs.
/// <see cref="advisoryAiSetup"/> is composed only when the build has a reviewed model (<see cref="AdvisoryAiAvailability.CanSetUp"/>);
/// otherwise the section only says what this version offers and nothing reads Credential Manager or the AI records. Every action is
/// one deliberate owner choice, runs inside the data-operation lease and sends nothing: connecting saves the key and records consent,
/// nothing more. The key is typed into a masked box that never reveals it, copied into <see cref="AdvisoryAiApiKey"/> and the box
/// cleared; the key is never shown again, logged or put in any text.
/// </summary>
public sealed partial class MainWindow
{
    private readonly AdvisoryAiSetupService? advisoryAiSetup;
    private AdvisoryAiSetupSnapshot? advisoryAiSnapshot;
    private bool advisoryAiLoading;
    private bool advisoryAiLoadFailed;
    private bool isChangingAdvisoryAi;

    /// <summary>
    /// ADR-028: true after a disconnect, a retry or a failed connection in this session that could not verify the key is gone. It
    /// lasts for this session only, because ordinary start-up never probes Credential Manager.
    /// </summary>
    private bool advisoryAiKeyRemovalIncomplete;

    private StackPanel BuildAdvisoryAiSection()
    {
        StackPanel section = SettingsSection(AdvisoryAiSetupText.SectionHeading, AdvisoryAiSetupText.SectionDescription);
        if (advisoryAiSetup is null)
        {
            // This build: no reviewed model, so no key store, transport or AI record is composed or read.
            AdvisoryAiSettingsPresentation offered = AdvisoryAiSettingsPresenter.PresentAvailability(AdvisoryAiAvailability.Current);
            section.Children.Add(StewardVisuals.RowCard("", offered.AvailabilityTitle, offered.AvailabilityText, new TextBlock { Text = "Off", Style = AppStyle("StewardBodyStyle") }));
            return section;
        }
        // Codex review on #115: the section is read again each time Settings is opened, and whenever what was read no longer describes
        // the present (a new UTC month, or prices that have expired since), so its notices and spending are never stale.
        if (advisoryAiSnapshot is AdvisoryAiSetupSnapshot read && !advisoryAiLoading && !isChangingAdvisoryAi &&
            (renderedPage != ShellPage.Settings || AdvisoryAiSettingsPresenter.NeedsRereading(read, DateTimeOffset.UtcNow)))
            advisoryAiSnapshot = null;
        if (advisoryAiSnapshot is not AdvisoryAiSetupSnapshot snapshot)
        {
            if (advisoryAiLoadFailed)
            {
                Button retry = StewardVisuals.ActionButton("Try again", "(read the AI settings again)");
                retry.Click += async (_, _) => { advisoryAiLoadFailed = false; await LoadAdvisoryAiAsync(); };
                section.Children.Add(StewardVisuals.RowCard("", "AI settings unavailable", AdvisoryAiSetupText.LoadFailed, retry, "attention"));
            }
            else
            {
                // Deferred, so the read never re-renders the page while it is being built.
                if (!advisoryAiLoading) DispatcherQueue.TryEnqueue(() => _ = LoadAdvisoryAiAsync());
                section.Children.Add(StewardVisuals.RowCard("", AdvisoryAiSetupText.SectionHeading, "Loading…"));
            }
            return section;
        }

        AdvisoryAiSettingsPresentation ai = AdvisoryAiSettingsPresenter.Present(snapshot, advisoryAiKeyRemovalIncomplete);
        section.Children.Add(StewardVisuals.RowCard("", ai.AvailabilityTitle, ai.AvailabilityText));
        if (!ai.ShowsSetup) return section;
        bool enabled = !isChangingAdvisoryAi && !HasActiveOperation;

        // Connection: Connect… or Disconnect…, and Replace key… while connected.
        StewardWrapPanel connectionActions = StewardVisuals.ActionRow();
        Button connection = ai.IsConnected ? StewardVisuals.ActionButton(ai.ConnectionActionLabel) : StewardVisuals.PrimaryButton(ai.ConnectionActionLabel);
        connection.IsEnabled = enabled;
        connection.Click += async (_, _) => { if (ai.IsConnected) await DisconnectAdvisoryAiAsync(); else await ConnectAdvisoryAiAsync(); };
        connectionActions.Children.Add(connection);
        if (ai.IsConnected)
        {
            Button replace = StewardVisuals.ActionButton("Replace key…", "(save a different API key under the same name)");
            replace.IsEnabled = enabled;
            replace.Click += async (_, _) => await ReplaceAdvisoryAiKeyAsync();
            connectionActions.Children.Add(replace);
        }
        section.Children.Add(StewardVisuals.RowCard(ai.IsConnected ? "" : "", ai.ConnectionTitle, ai.ConnectionText + " " + ai.ModelText + ".", connectionActions,
            advisoryAiKeyRemovalIncomplete && !ai.IsConnected ? "attention" : "neutral"));

        // Notices, each with the one action it offers.
        foreach (AdvisoryAiNotice notice in ai.Notices)
        {
            Button? act = null;
            if (notice.Action != AdvisoryAiNoticeAction.None && notice.ActionLabel is string label)
            {
                act = StewardVisuals.ActionButton(label);
                act.IsEnabled = enabled;
                AdvisoryAiNoticeAction action = notice.Action;
                act.Click += async (_, _) => await RunAdvisoryAiNoticeAsync(action);
            }
            section.Children.Add(StewardVisuals.InformationBanner(act is null ? "Note" : "Needs your choice", notice.Text, act is null ? "information" : "attention", act));
        }

        // Limits, with the R5 deadline note.
        Button limits = StewardVisuals.ActionButton("Change limits…");
        limits.IsEnabled = enabled;
        limits.Click += async (_, _) => await ChangeAdvisoryAiLimitsAsync();
        section.Children.Add(FactsCard("Limits", ai.Limits, ai.LimitsNotes, limits));

        // Prices: typed fresh, valid for 24 hours.
        Button prices = ai.PricesCurrent ? StewardVisuals.ActionButton("Confirm prices again…") : StewardVisuals.ActionButton("Confirm prices…");
        prices.IsEnabled = enabled;
        prices.Click += async (_, _) => await ConfirmAdvisoryAiPricesAsync();
        section.Children.Add(StewardVisuals.RowCard("", ai.PricesCurrent ? "Prices confirmed" : "Prices needed", ai.PricesText, prices, ai.PricesCurrent ? "neutral" : "attention"));

        // Spending, as PC Steward counts it.
        if (ai.Spending.Count > 0) section.Children.Add(FactsCard("Spending", ai.Spending, [ai.SpendingNote], null));
        return section;
    }

    /// <summary>A row card of label and value rows, with notes under them and an optional action on the right.</summary>
    private static Border FactsCard(string title, IReadOnlyList<DetailFact> facts, IReadOnlyList<string> notes, Button? action)
    {
        StackPanel copy = new() { Spacing = StewardSpacing.XSmall, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = title, Style = AppStyle("StewardRowTitleStyle") });
        foreach (DetailFact fact in facts) copy.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        foreach (string note in notes) copy.Children.Add(new TextBlock { Text = note, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
        Grid row = new() { ColumnSpacing = StewardSpacing.Large };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(copy);
        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(action, 1);
            row.Children.Add(action);
        }
        return new Border { Style = AppStyle("StewardRowCardStyle"), Child = row };
    }

    /// <summary>Reads the AI settings and spending record (no Credential Manager access) and shows them.</summary>
    private async Task LoadAdvisoryAiAsync()
    {
        if (advisoryAiSetup is not AdvisoryAiSetupService setup || advisoryAiLoading) return;
        advisoryAiLoading = true;
        try
        {
            advisoryAiSnapshot = await DataOperationLease.RunReadAsync(token => setup.LoadAsync(token), CancellationToken.None);
            advisoryAiLoadFailed = false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { advisoryAiSnapshot = null; advisoryAiLoadFailed = true; }
        finally { advisoryAiLoading = false; }
        if (!windowClosed) Render();
    }

    /// <summary>
    /// Runs one set-up change inside the data-operation lease, reads the section again and shows what happened. The change returns
    /// the sentence to show, or null to show nothing.
    /// </summary>
    private async Task RunAdvisoryAiChangeAsync(string title, Func<CancellationToken, Task<string?>> change)
    {
        if (isChangingAdvisoryAi) return;
        if (HasActiveOperation) { await ShowDataOperationUnavailableAsync(); return; }
        isChangingAdvisoryAi = true;
        Render();
        string? message;
        try { message = await DataOperationLease.RunAsync(change, CancellationToken.None); }
        catch (InvalidOperationException)
        {
            isChangingAdvisoryAi = false;
            await LoadAdvisoryAiAsync();
            await ShowDataOperationUnavailableAsync();
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { message = AdvisoryAiSetupText.ActionUnfinished; }
        isChangingAdvisoryAi = false;
        await LoadAdvisoryAiAsync();
        if (message is null || windowClosed) return;
        AnnounceCompletion(OperationAnnouncements.Completed(title), "advisory-ai-changed");
        await new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = Wrapped(message), CloseButtonText = "Close" }.ShowAsync();
    }

    private static TextBlock Wrapped(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    /// <summary>A masked key box that never reveals what was typed or pasted.</summary>
    private static PasswordBox KeyBox(AdvisoryAiProvider provider)
    {
        PasswordBox box = new() { Header = $"{provider.Name} API key", PasswordRevealMode = PasswordRevealMode.Hidden, MaxLength = AdvisoryAiApiKey.MaximumUtf8Bytes + 1 };
        AutomationProperties.SetHelpText(box, AdvisoryAiSetupText.KeyBoxNote);
        return box;
    }

    /// <summary>Copies the box into a key and clears the box at once. A problem is shown in the dialog; nothing is saved.</summary>
    private static AdvisoryAiApiKey? TakeKey(PasswordBox box, TextBlock problemText)
    {
        AdvisoryAiKeyProblem problem = AdvisoryAiApiKey.TryCreate(box.Password, out AdvisoryAiApiKey? key);
        box.Password = string.Empty;
        problemText.Text = AdvisoryAiSetupText.KeyProblem(problem);
        problemText.Visibility = Shown(problem != AdvisoryAiKeyProblem.None);
        return problem == AdvisoryAiKeyProblem.None ? key : null;
    }

    private static TextBlock ProblemText() => new() { TextWrapping = TextWrapping.Wrap, Foreground = StewardVisuals.Brush("StewardDangerBrush"), Visibility = Visibility.Collapsed };

    /// <summary>ADR-028 Q8 and ADR-029: the disclosure, the first-connection question, then the masked key box.</summary>
    private async Task ConnectAdvisoryAiAsync()
    {
        if (advisoryAiSetup is not AdvisoryAiSetupService setup || setup.Availability.Provider is not AdvisoryAiProvider provider || advisoryAiSnapshot is not AdvisoryAiSetupSnapshot snapshot) return;
        bool askPriorUse = snapshot.Accounting.Continuity == AdvisoryAiContinuity.NotEstablished;
        StackPanel body = new() { Spacing = StewardSpacing.Small };
        foreach (string line in AdvisoryAiSetupText.ConnectDisclosure(provider)) body.Children.Add(Wrapped(line));
        RadioButtons priorUse = new() { Header = AdvisoryAiSetupText.PriorUseQuestion, SelectedIndex = -1 };
        priorUse.Items.Add(AdvisoryAiSetupText.PriorUseNo);
        priorUse.Items.Add(AdvisoryAiSetupText.PriorUseYesOrUnsure);
        if (askPriorUse)
        {
            body.Children.Add(priorUse);
            body.Children.Add(new TextBlock { Text = AdvisoryAiSetupText.PriorUseNote, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
        }
        PasswordBox keyBox = KeyBox(provider);
        body.Children.Add(keyBox);
        body.Children.Add(new TextBlock { Text = AdvisoryAiSetupText.KeyBoxNote, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
        TextBlock problem = ProblemText();
        body.Children.Add(problem);
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot, Title = $"Connect {provider.Name}", Content = new ScrollViewer { Content = body, MaxHeight = 520 },
            PrimaryButtonText = "Save key and connect", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false
        };
        void Refresh() => dialog.IsPrimaryButtonEnabled = keyBox.Password.Length > 0 && (!askPriorUse || priorUse.SelectedIndex >= 0);
        keyBox.PasswordChanged += (_, _) => Refresh();
        priorUse.SelectionChanged += (_, _) => Refresh();
        AdvisoryAiApiKey? key = null;
        dialog.PrimaryButtonClick += (_, args) => { key = TakeKey(keyBox, problem); if (key is null) args.Cancel = true; };
        dialog.Closed += (_, _) => keyBox.Password = string.Empty;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || key is null) { key?.Dispose(); return; }
        AdvisoryAiPriorUseAnswer? answer = askPriorUse ? (priorUse.SelectedIndex == 0 ? AdvisoryAiPriorUseAnswer.NoPriorUse : AdvisoryAiPriorUseAnswer.YesOrUnsure) : null;
        using (key)
        {
            AdvisoryAiApiKey saved = key;
            await RunAdvisoryAiChangeAsync($"Connect {provider.Name}", async token =>
            {
                AdvisoryAiConnectResult result = await setup.ConnectAsync(saved, answer, token);
                if (result.Outcome == AdvisoryAiConnectOutcome.Connected) advisoryAiKeyRemovalIncomplete = false;
                else if (result.KeyMayRemain) advisoryAiKeyRemovalIncomplete = true;
                return AdvisoryAiSetupText.ConnectResult(result, provider);
            });
        }
    }

    /// <summary>ADR-028 "Disconnect": disclosed first, Cancel is the default, and an unverified removal is never called removed.</summary>
    private async Task DisconnectAdvisoryAiAsync()
    {
        if (advisoryAiSetup is not AdvisoryAiSetupService setup || setup.Availability.Provider is not AdvisoryAiProvider provider) return;
        ContentDialog confirm = new()
        {
            XamlRoot = Content.XamlRoot, Title = $"Disconnect {provider.Name}?", Content = Wrapped(AdvisoryAiSetupText.DisconnectDisclosure(provider)),
            PrimaryButtonText = "Disconnect", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        await RunAdvisoryAiChangeAsync($"Disconnect {provider.Name}", async token =>
        {
            // Stage 4c passes the work to stop (a running AI request); this stage runs none.
            AdvisoryAiDisconnectResult result = await setup.DisconnectAsync(null, token);
            advisoryAiKeyRemovalIncomplete = !result.KeyGone;
            return AdvisoryAiSetupText.DisconnectResult(result, provider);
        });
    }

    private async Task ReplaceAdvisoryAiKeyAsync()
    {
        if (advisoryAiSetup is not AdvisoryAiSetupService setup || setup.Availability.Provider is not AdvisoryAiProvider provider) return;
        StackPanel body = new() { Spacing = StewardSpacing.Small };
        body.Children.Add(Wrapped($"The new key is saved in Credential Manager as {provider.CredentialTarget}, in place of the old one. Requests already approved with the old key need a fresh review. Spending already recorded is kept."));
        PasswordBox keyBox = KeyBox(provider);
        body.Children.Add(keyBox);
        body.Children.Add(new TextBlock { Text = AdvisoryAiSetupText.KeyBoxNote, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
        TextBlock problem = ProblemText();
        body.Children.Add(problem);
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot, Title = "Replace key", Content = body,
            PrimaryButtonText = "Save new key", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false
        };
        keyBox.PasswordChanged += (_, _) => dialog.IsPrimaryButtonEnabled = keyBox.Password.Length > 0;
        AdvisoryAiApiKey? key = null;
        dialog.PrimaryButtonClick += (_, args) => { key = TakeKey(keyBox, problem); if (key is null) args.Cancel = true; };
        dialog.Closed += (_, _) => keyBox.Password = string.Empty;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || key is null) { key?.Dispose(); return; }
        using (key)
        {
            AdvisoryAiApiKey saved = key;
            await RunAdvisoryAiChangeAsync("Replace key", async token => AdvisoryAiSetupText.ReplaceResult(await setup.ReplaceKeyAsync(saved, token)));
        }
    }

    /// <summary>FR-BUD-01: the four limits, parsed exactly; a problem keeps the dialog open on that field.</summary>
    private async Task ChangeAdvisoryAiLimitsAsync()
    {
        if (advisoryAiSetup is not AdvisoryAiSetupService setup || setup.Availability.Model is not AdvisoryAiModelProfile model || advisoryAiSnapshot is not AdvisoryAiSetupSnapshot snapshot) return;
        AdvisoryAiLimitsDraft shown = AdvisoryAiLimitsDraft.From(snapshot.Settings.Limits);
        long shownVersion = snapshot.Settings.SettingsVersion;
        TextBox input = new() { Header = "Input limit (tokens)", Text = shown.InputTokens, InputScope = Numbers() };
        TextBox output = new() { Header = "Output limit (tokens)", Text = shown.OutputTokens, InputScope = Numbers() };
        TextBox perRequest = new() { Header = "Limit per request (US dollars)", Text = shown.RequestCap };
        TextBox monthly = new() { Header = "Monthly budget (US dollars, UTC month)", Text = shown.MonthlyBudget };
        TextBlock problem = ProblemText();
        // FR-BUD-01 (Codex review on #115): Save first shows each limit now beside the new value and what is already counted this
        // month; only a second Save, with those same values, saves them.
        StackPanel change = new() { Spacing = StewardSpacing.XSmall, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(change, AutomationLiveSetting.Polite);
        StackPanel body = new() { Spacing = StewardSpacing.Small };
        foreach (TextBox box in new[] { input, output, perRequest, monthly }) body.Children.Add(box);
        body.Children.Add(new TextBlock { Text = AdvisoryAiSetupText.OutputDeadlineNote, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "A spending limit of 0 turns paid requests off. Changing a limit changes no recorded spending.", Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(problem);
        body.Children.Add(change);
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot, Title = "AI limits", Content = new ScrollViewer { Content = body, MaxHeight = 520 },
            PrimaryButtonText = "Save limits", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
        };
        AdvisoryAiLimitsDraft? draft = null;
        AdvisoryAiLimitsDraft? compared = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            AdvisoryAiLimitsDraft typed = new(input.Text, output.Text, perRequest.Text, monthly.Text);
            AdvisoryAiLimitsCheck check = typed.Check(model);
            if (check.Limits is null)
            {
                args.Cancel = true;
                problem.Text = check.Message;
                problem.Visibility = Visibility.Visible;
                (check.Field switch { AdvisoryAiLimitField.OutputTokens => output, AdvisoryAiLimitField.RequestCap => perRequest, AdvisoryAiLimitField.MonthlyBudget => monthly, _ => input }).Focus(FocusState.Programmatic);
                return;
            }
            problem.Visibility = Visibility.Collapsed;
            if (compared != typed)
            {
                // First Save, or the values changed since the comparison was shown: show it and wait for Save again.
                args.Cancel = true;
                compared = typed;
                change.Children.Clear();
                change.Children.Add(new TextBlock { Text = "Check the change, then choose Save limits again:", TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                foreach (string line in AdvisoryAiSettingsPresenter.LimitsChange(snapshot.Settings.Limits, check.Limits, snapshot.Spending)) change.Children.Add(Wrapped(line));
                change.Visibility = Visibility.Visible;
                return;
            }
            draft = typed;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || draft is null) return;
        AdvisoryAiLimitsDraft confirmed = draft;
        await RunAdvisoryAiChangeAsync("AI limits", async token => (await setup.SaveLimitsAsync(confirmed, shownVersion, token)).Message);
    }

    /// <summary>ADR-029 F6: today's prices, typed fresh from the provider's pricing page every time; nothing is filled in.</summary>
    private async Task ConfirmAdvisoryAiPricesAsync()
    {
        if (advisoryAiSetup is not AdvisoryAiSetupService setup || setup.Availability.Model is not AdvisoryAiModelProfile model || setup.Availability.Provider is not AdvisoryAiProvider provider) return;
        StackPanel body = new() { Spacing = StewardSpacing.Small };
        body.Children.Add(Wrapped($"Type today's prices for {model.ModelId} ({model.ServiceTier} tier) from {provider.Name}'s pricing page, in US dollars per million tokens. They last 24 hours."));
        body.Children.Add(new HyperlinkButton { Content = $"Open {provider.Name} pricing", NavigateUri = provider.PricingPage });
        TextBox input = new() { Header = "Input, per million tokens" };
        TextBox cached = new() { Header = "Cached input, per million tokens" };
        TextBox output = new() { Header = "Output, per million tokens" };
        foreach (TextBox box in new[] { input, cached, output }) body.Children.Add(box);
        TextBlock problem = ProblemText();
        body.Children.Add(problem);
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot, Title = "Confirm prices", Content = new ScrollViewer { Content = body, MaxHeight = 520 },
            PrimaryButtonText = "Confirm prices", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
        };
        AdvisoryAiPricesDraft? draft = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            AdvisoryAiPricesDraft typed = new(input.Text, cached.Text, output.Text);
            AdvisoryAiPricesCheck check = typed.Check(model, DateTimeOffset.UtcNow);
            if (check.Tariff is null)
            {
                args.Cancel = true;
                problem.Text = check.Message;
                problem.Visibility = Visibility.Visible;
                return;
            }
            draft = typed;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || draft is null) return;
        AdvisoryAiPricesDraft confirmed = draft;
        await RunAdvisoryAiChangeAsync("Confirm prices", async token => (await setup.ConfirmPricesAsync(confirmed, token)).Message);
    }

    /// <summary>The one action a notice offers. None of them sends anything.</summary>
    private async Task RunAdvisoryAiNoticeAsync(AdvisoryAiNoticeAction action)
    {
        if (advisoryAiSetup is not AdvisoryAiSetupService setup || setup.Availability.Provider is not AdvisoryAiProvider provider) return;
        if (action == AdvisoryAiNoticeAction.RetryKeyRemoval)
        {
            await RunAdvisoryAiChangeAsync("Remove key", async token =>
            {
                AdvisoryAiCredentialRemoval removal = await setup.RetryKeyRemovalAsync(token);
                advisoryAiKeyRemovalIncomplete = removal is not (AdvisoryAiCredentialRemoval.Removed or AdvisoryAiCredentialRemoval.AlreadyAbsent);
                return AdvisoryAiSetupText.RetryRemovalResult(removal, provider);
            });
            return;
        }
        Func<CancellationToken, Task<AdvisoryAiAccountingWrite>>? write = action switch
        {
            AdvisoryAiNoticeAction.ConfirmNewMonth => setup.ConfirmNewMonthAsync,
            AdvisoryAiNoticeAction.AcceptClock => setup.AcceptClockAsync,
            AdvisoryAiNoticeAction.AcknowledgeReview => setup.AcknowledgeReviewAsync,
            _ => null
        };
        if (write is null) return;
        await RunAdvisoryAiChangeAsync(AdvisoryAiSetupText.SectionHeading, async token => AdvisoryAiSetupText.NoticeActionResult(action, await write(token)));
    }

    private static Microsoft.UI.Xaml.Input.InputScope Numbers()
    {
        Microsoft.UI.Xaml.Input.InputScope scope = new();
        scope.Names.Add(new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number));
        return scope;
    }
}
