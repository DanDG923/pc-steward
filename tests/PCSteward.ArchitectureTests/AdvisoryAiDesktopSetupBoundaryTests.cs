using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-AI-01/03/04, FR-BUD-01 (ADR-028 D1/Q8 "Disconnect" and "Factory reset", ADR-029; Task 67 stage 4b): the desktop app composes the
/// AI adapters only when the build has a reviewed model, and only in two places: the set-up service and the factory reset's key
/// removal. Ordinary start-up reads no credential. The Settings dialogs keep the key in a box that never reveals it, clear it at
/// once, default every confirmation to Cancel and send nothing.
/// </summary>
[TestClass]
public sealed class AdvisoryAiDesktopSetupBoundaryTests
{
    [TestMethod]
    public void TheAdaptersAreComposedOnlyForAReviewedModelAndForTheResetKeyRemoval()
    {
        string app = DesktopFile("App.xaml.cs");
        Assert.AreEqual(2, Regex.Count(app, @"new WindowsCredentialManagerStore\("), "the store is made for set-up and for reset only");
        Assert.AreEqual(1, Regex.Count(app, @"new OpenAiResponsesTransport\("), "the transport is made for set-up only");
        string setup = Between(app, "private static (AdvisoryAiSetupService? Setup, AdvisoryAiExplainService? Explain) CreateAdvisoryAi(SqliteDatabase database)", "private static async Task<bool> RemoveAdvisoryAiKeysAsync(");
        StringAssert.Contains(setup, "if (!availability.CanSetUp || availability.Provider is not PCSteward.Domain.AdvisoryAiProvider provider) return (null, null);");
        Assert.IsTrue(setup.IndexOf("return (null, null);", StringComparison.Ordinal) < setup.IndexOf("new WindowsCredentialManagerStore(", StringComparison.Ordinal),
            "nothing is composed before the reviewed-model check");
        // Stage 4c: the one request workflow uses the compiled model list and paid-request policy, and the same adapters as set-up.
        StringAssert.Contains(setup, "AdvisoryAiRequestWorkflow workflow = new(database, database, adapters, database, PCSteward.Domain.AdvisoryAiModelProfiles.Reviewed,");
        StringAssert.Contains(setup, "PCSteward.Domain.AdvisoryAiBuildPolicy.PaidRequests, () => DateTimeOffset.UtcNow);");
        StringAssert.Contains(setup, "IdentityRedaction());");
        Assert.AreEqual(1, Regex.Count(app, @"new AdvisoryAiRequestWorkflow\(|AdvisoryAiRequestWorkflow workflow = new\("), "one request workflow");
        Assert.AreEqual(1, Regex.Count(app, @"CreateAdvisoryAi\(database\)"), "composed once at start-up");
        foreach (string forbidden in new[] { "CheckPresenceAsync", "ReadAsync", "SaveAsync", "SendAsync", "LoadAsync" })
            Assert.IsFalse(setup.Contains(forbidden, StringComparison.Ordinal), $"composition calls {forbidden}");

        string reset = Between(app, "private static async Task<bool> RemoveAdvisoryAiKeysAsync(", "private static string? CurrentUserSid()");
        StringAssert.Contains(reset, "RemoveAndVerifyAsync(cancellationToken)");
        StringAssert.Contains(reset, "if (removal is not (AdvisoryAiCredentialRemoval.Removed or AdvisoryAiCredentialRemoval.AlreadyAbsent)) return false;");
        // The reset step is passed to the reset, to the start-up check of first-run setup and to its Continue, and nowhere else.
        Assert.AreEqual(3, Regex.Count(app, @"RemoveAdvisoryAiKeysAsync\b") - 1, "the reset step has three callers");
        StringAssert.Contains(app, "FactoryReset.CompletePendingAsync(scheduleRegistrar.EnsureAbsentAsync, RemoveAdvisoryAiKeysAsync, CancellationToken.None, stateDirectory)");
        StringAssert.Contains(app, "FactoryReset.VerifyOnboardingBootstrap(RemoveAdvisoryAiKeysAsync, stateDirectory)");
        StringAssert.Contains(app, "GetResult(), RemoveAdvisoryAiKeysAsync)");
    }

    [TestMethod]
    public void TheKeyBoxNeverRevealsTheKeyAndIsClearedAtOnce()
    {
        string ai = DesktopFile("MainWindow.AdvisoryAi.cs");
        StringAssert.Contains(ai, "PasswordRevealMode = PasswordRevealMode.Hidden");
        Assert.AreEqual(1, Regex.Count(ai, @"PasswordBox box = new\(\)"), "one masked key box, made in one place");
        Assert.AreEqual(0, Regex.Count(ai, @"new PasswordBox\b"));
        Assert.AreEqual(1, Regex.Count(ai, @"AdvisoryAiApiKey\.TryCreate\("), "the key is taken from the box in one place");
        string take = Between(ai, "private static AdvisoryAiApiKey? TakeKey(", "private static TextBlock ProblemText()");
        Assert.IsTrue(take.IndexOf("TryCreate(box.Password", StringComparison.Ordinal) < take.IndexOf("box.Password = string.Empty;", StringComparison.Ordinal), "the box is cleared after the copy");
        Assert.AreEqual(2, Regex.Count(ai, @"dialog\.Closed \+= \(_, _\) => keyBox\.Password = string\.Empty;"), "both key dialogs clear the box when they close");
        Assert.AreEqual(2, Regex.Count(ai, @"using \(key\)"), "both keys are disposed after use");
        // The key's text goes nowhere else: no other read of the box than the length check and the one copy.
        Assert.AreEqual(2, Regex.Count(ai, @"keyBox\.Password\.Length"), "only the length is read to enable the buttons");
        Assert.AreEqual(1, Regex.Count(ai, @"\bbox\.Password\b(?! = string\.Empty)"), "the box's text is read once, for the copy");
        foreach (string forbidden in new[] { "Reveal()", "PasswordRevealMode.Peek", "PasswordRevealMode.Visible", "Clipboard", "Debug.", "Trace.", "Console." })
            Assert.IsFalse(ai.Contains(forbidden, StringComparison.Ordinal), $"AI set-up uses {forbidden}");
    }

    [TestMethod]
    public void EveryConfirmationDefaultsToCancelAndNothingIsSent()
    {
        string ai = DesktopFile("MainWindow.AdvisoryAi.cs");
        int confirmations = Regex.Count(ai, @"PrimaryButtonText = ");
        Assert.AreEqual(5, confirmations, "connect, disconnect, replace, limits and prices");
        Assert.AreEqual(confirmations, Regex.Count(ai, @"DefaultButton = ContentDialogButton\.Close"), "Cancel is the default everywhere");
        Assert.IsFalse(ai.Contains("DefaultButton = ContentDialogButton.Primary", StringComparison.Ordinal));
        foreach (string forbidden in new[] { "AdvisoryAiRequestWorkflow", "IAdvisoryAiTransport", "OpenAiResponsesTransport", "WindowsCredentialManagerStore", "SendAsync", "HttpClient" })
            Assert.IsFalse(ai.Contains(forbidden, StringComparison.Ordinal), $"Settings reaches {forbidden}");
        // Every change runs inside the data-operation lease, and the read uses the read lease.
        Assert.AreEqual(1, Regex.Count(ai, @"DataOperationLease\.RunAsync\("));
        Assert.AreEqual(1, Regex.Count(ai, @"DataOperationLease\.RunReadAsync\("));
        StringAssert.Contains(ai, "AdvisoryAiSetupService? advisoryAiSetup;");
        StringAssert.Contains(ai, "if (advisoryAiSetup is null)");
        StringAssert.Contains(ai, "AdvisoryAiSettingsPresenter.PresentAvailability(AdvisoryAiAvailability.Current)");
    }

    private static string DesktopFile(string name) => File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Desktop", name));

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.IsTrue(from >= 0, $"missing: {start}");
        int to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.IsTrue(to > from, $"missing: {end}");
        return source[from..to];
    }
}
