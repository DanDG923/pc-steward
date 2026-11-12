using PCSteward.Storage;
using PCSteward.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class FactoryResetTests
{
    [TestInitialize]
    [TestCleanup]
    public void ResetNativeControlCutpoints()
    {
        NativeResetControl.BeforeMarkerDispositionForTesting = null;
        NativeResetControl.BeforeDirectoryDispositionForTesting = null;
        NativeResetControl.BeforeProgressWriteForTesting = null;
        NativeResetControl.BeforeFinalOnboardingRootVerificationForTesting = null;
        NativeResetControl.BeforeOnboardingControlVerificationForTesting = null;
    }

    [TestMethod]
    public void CleanProfileWithoutStateRootStartsWithoutResetRecovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-first-launch-" + Guid.NewGuid().ToString("N"));
        Assert.IsFalse(Directory.Exists(root));

        Assert.AreEqual(FactoryResetMarkerState.None, FactoryReset.GetMarkerState(root));
        Assert.IsFalse(FactoryReset.IsPending(root));
        Assert.IsFalse(Directory.Exists(root), "Reading reset state must not create first-launch storage.");
    }

    [TestMethod]
    public void TestResetSeamRejectsAnArbitraryFilesystemRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "unrelated-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => FactoryReset.ArmForTesting(root));
        }
        finally { Directory.Delete(root); }
    }

    [TestMethod]
    public async Task ResumedOnboardingAcceptsOnlyAVerifiedFreshSQLiteStore()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-resume-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            FactoryReset.ArmForTesting(root);
            Assert.IsTrue((await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None)).Succeeded);
            SqliteDatabase database = new(Path.Combine(root, "pcsteward.db"));
            await database.MigrateAsync(CancellationToken.None);

            FactoryResetResult resumed = FactoryReset.VerifyOnboardingBootstrapForTesting(root);

            Assert.IsTrue(resumed.Succeeded, string.Join(",", resumed.Residuals.Select(item => item.Reason)));
            Assert.IsTrue(await database.IsFreshResetStoreAsync(CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ContinueRechecksTheFreshStoreAndKeepsTheMarkerWhenItChanged()
    {
        // Codex security scan finding 3: a store replaced or filled after startup's check is refused at Continue.
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-continue-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            FactoryReset.ArmForTesting(root);
            Assert.IsTrue((await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None)).Succeeded);
            SqliteDatabase database = new(Path.Combine(root, "pcsteward.db"));
            await database.MigrateAsync(CancellationToken.None);
            Assert.IsTrue(await database.IsFreshResetStoreAsync(CancellationToken.None));
            await database.SaveScheduledCheckStatusAsync(new ScheduledCheckStatus(DateTimeOffset.UtcNow, "Completed", 0), CancellationToken.None);
            SqliteConnection.ClearAllPools();

            FactoryResetResult refused = FactoryReset.AcknowledgeOnboardingForTesting(root, () => database.IsFreshResetStoreAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.IsFalse(refused.Succeeded);
            Assert.IsTrue(refused.Residuals.Any(item => item.Category == "Fresh store" && item.Reason == "VerificationFailed"));
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root), "the reset authority stays");

            FactoryResetResult thrown = FactoryReset.AcknowledgeOnboardingForTesting(root, () => throw new IOException("locked"));
            Assert.IsFalse(thrown.Succeeded, "a failing check refuses");
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));

            FactoryResetResult fresh = FactoryReset.AcknowledgeOnboardingForTesting(root, () => true);
            Assert.IsTrue(fresh.Succeeded, string.Join("; ", fresh.Residuals.Select(item => item.Reason + ":" + item.Detail)));
            Assert.IsFalse(FactoryReset.IsPending(root));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task MarkerLedResetDeletesOnlyTheFixedManifestAndIsIdempotent()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "downloads", "extract"));
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            Directory.CreateDirectory(outside);
            await File.WriteAllTextAsync(Path.Combine(root, "downloads", "extract", "payload.bin"), "owned");
            await File.WriteAllTextAsync(Path.Combine(root, "logs", "events.log"), "owned");
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "database");
            await File.WriteAllTextAsync(Path.Combine(outside, "provider-cache.txt"), "preserved");
            int removedSchedules = 0;

            FactoryReset.ArmForTesting(root);
            Assert.IsTrue(FactoryReset.IsPending(root));
            FactoryResetResult first = await FactoryReset.CompletePendingForTestingAsync(root, _ => { removedSchedules++; return Task.CompletedTask; }, CancellationToken.None);
            FactoryResetResult second = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsTrue(first.Succeeded, string.Join("; ", first.Residuals.Select(item => item.Reason + ":" + item.Detail)));
            Assert.IsTrue(second.Succeeded);
            Assert.AreEqual(1, removedSchedules);
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
            Assert.IsFalse(File.Exists(Path.Combine(root, "pcsteward.db")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "downloads")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "logs")));
            Assert.IsTrue(File.Exists(Path.Combine(outside, "provider-cache.txt")));
            FactoryResetResult acknowledgement = FactoryReset.AcknowledgeOnboardingForTesting(root);
            Assert.IsTrue(acknowledgement.Succeeded, string.Join("; ", acknowledgement.Residuals.Select(item => item.Reason + ":" + item.Detail)));
            Assert.IsFalse(FactoryReset.IsPending(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [TestMethod]
    public async Task AJunctionedCategoryRootIsRefusedBeforeItsTargetIsListed()
    {
        // Codex security scan finding 9: a category root that is itself a junction is reported once and never entered.
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-junction-root-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-junction-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(outside, "nested"));
            await File.WriteAllTextAsync(Path.Combine(outside, "nested", "outside-sentinel.txt"), "outside");
            await CreateJunctionAsync(Path.Combine(root, "downloads"), outside);
            FactoryReset.ArmForTesting(root);

            FactoryResetResult partial = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(partial.Succeeded);
            Assert.IsTrue(partial.Residuals.Any(item => item.Category == "downloads" && item.Reason == "UnsafeReparsePoint" && item.Detail.Length == 0),
                string.Join("; ", partial.Residuals.Select(item => item.Category + ":" + item.Reason + ":" + item.Detail)));
            Assert.IsFalse(partial.Residuals.Any(item => item.Detail.Contains("nested", StringComparison.OrdinalIgnoreCase) || item.Detail.Contains("outside-sentinel", StringComparison.OrdinalIgnoreCase)),
                "nothing below the junction target was listed");
            Assert.AreEqual("outside", await File.ReadAllTextAsync(Path.Combine(outside, "nested", "outside-sentinel.txt")));
        }
        finally
        {
            CleanupFixtureRoot(root, Path.Combine(root, "downloads"));
            CleanupFixtureRoot(outside);
        }
    }

    [TestMethod]
    public async Task UnclassifiedDirectRootObjectsRemainBlockingWithoutWideningDeletionAuthority()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-unclassified-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-unclassified-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outside);
            await File.WriteAllTextAsync(Path.Combine(root, "undeclared.txt"), "must-remain");
            await File.WriteAllTextAsync(Path.Combine(outside, "sentinel.txt"), "outside");
            await CreateJunctionAsync(Path.Combine(root, "undeclared-directory"), outside);
            FactoryReset.ArmForTesting(root);

            FactoryResetResult partial = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(partial.Succeeded);
            Assert.IsTrue(partial.Residuals.Any(item => item.Reason == "UnexpectedOwnedObject"));
            Assert.AreEqual(FactoryResetMarkerState.Partial, FactoryReset.GetMarkerState(root));
            Assert.IsTrue(File.Exists(FactoryReset.GetMarkerPath(root)));
            Assert.IsTrue(File.Exists(Path.Combine(root, "undeclared.txt")));
            Assert.IsTrue(Directory.Exists(Path.Combine(root, "undeclared-directory")));
            Assert.AreEqual("outside", await File.ReadAllTextAsync(Path.Combine(outside, "sentinel.txt")));

            File.Delete(Path.Combine(root, "undeclared.txt"));
            new DirectoryInfo(Path.Combine(root, "undeclared-directory")).Delete();
            FactoryResetResult retry = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsTrue(retry.Succeeded, string.Join("; ", retry.Residuals.Select(item => item.Reason)));
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
        }
        finally
        {
            CleanupFixtureRoot(root, Path.Combine(root, "undeclared-directory"));
            CleanupFixtureRoot(outside);
        }
    }

    [TestMethod]
    public async Task OnboardingAcknowledgementAllowsOnlyTheFreshSQLiteLeafGroup()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-onboarding-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            FactoryReset.ArmForTesting(root);
            FactoryResetResult completed = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsTrue(completed.Succeeded, string.Join("; ", completed.Residuals.Select(item => item.Reason)));

            SqliteDatabase freshDatabase = new(Path.Combine(root, "pcsteward.db"));
            await freshDatabase.MigrateAsync(CancellationToken.None);
            Assert.IsTrue(await freshDatabase.IsFreshResetStoreAsync(CancellationToken.None));

            FactoryResetResult acknowledgement = FactoryReset.AcknowledgeOnboardingForTesting(root);
            Assert.IsTrue(acknowledgement.Succeeded, string.Join("; ", acknowledgement.Residuals.Select(item => item.Reason)));
            Assert.IsFalse(FactoryReset.IsPending(root));
            Assert.IsTrue(File.Exists(Path.Combine(root, "pcsteward.db")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task OnboardingAcknowledgementRejectsUnexpectedRootChildAndRetainsMarker()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-onboarding-unexpected-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            FactoryReset.ArmForTesting(root);
            Assert.IsTrue((await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None)).Succeeded);
            SqliteDatabase freshDatabase = new(Path.Combine(root, "pcsteward.db"));
            await freshDatabase.MigrateAsync(CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(root, "unexpected-after-bootstrap.txt"), "must-block");

            FactoryResetResult blocked = FactoryReset.AcknowledgeOnboardingForTesting(root);

            Assert.IsFalse(blocked.Succeeded);
            Assert.IsTrue(blocked.Residuals.Any(item => item.Reason == "UnexpectedOwnedObject"));
            Assert.IsTrue(File.Exists(FactoryReset.GetMarkerPath(root)));
            File.Delete(Path.Combine(root, "unexpected-after-bootstrap.txt"));
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task FinalAcknowledgementVerificationRestoresOnboardingAuthorityAfterLateRootInjection()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-final-root-race-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            FactoryReset.ArmForTesting(root);
            Assert.IsTrue((await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None)).Succeeded);
            SqliteDatabase freshDatabase = new(Path.Combine(root, "pcsteward.db"));
            await freshDatabase.MigrateAsync(CancellationToken.None);
            NativeResetControl.BeforeFinalOnboardingRootVerificationForTesting = () => File.WriteAllText(Path.Combine(root, "late-unexpected.txt"), "must-block");

            FactoryResetResult blocked = FactoryReset.AcknowledgeOnboardingForTesting(root);

            Assert.IsFalse(blocked.Succeeded);
            Assert.IsTrue(blocked.Residuals.Any(item => item.Reason == "FinalRootVerificationFailed"));
            Assert.IsTrue(File.Exists(FactoryReset.GetMarkerPath(root)), "A late verification failure must recreate durable onboarding authority.");
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
            NativeResetControl.BeforeFinalOnboardingRootVerificationForTesting = null;
            File.Delete(Path.Combine(root, "late-unexpected.txt"));
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);
        }
        finally
        {
            NativeResetControl.BeforeFinalOnboardingRootVerificationForTesting = null;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task OnboardingAllowlistedLeafNamesRejectDirectoryReparseAndHardLinkCollisions()
    {
        string directoryRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-leaf-directory-" + Guid.NewGuid().ToString("N"));
        string reparseRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-leaf-reparse-" + Guid.NewGuid().ToString("N"));
        string hardLinkRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-leaf-hardlink-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-leaf-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outside);
            await CompleteToOnboardingAsync(directoryRoot);
            Directory.CreateDirectory(Path.Combine(directoryRoot, "pcsteward.db"));
            AssertOnboardingLeafCollision(directoryRoot);
            Directory.Delete(Path.Combine(directoryRoot, "pcsteward.db"));
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(directoryRoot).Succeeded);

            await CompleteToOnboardingAsync(reparseRoot);
            await CreateJunctionAsync(Path.Combine(reparseRoot, "pcsteward.db"), outside);
            AssertOnboardingLeafCollision(reparseRoot);
            new DirectoryInfo(Path.Combine(reparseRoot, "pcsteward.db")).Delete();
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(reparseRoot).Succeeded);

            await CompleteToOnboardingAsync(hardLinkRoot);
            string outsideFile = Path.Combine(outside, "source.db");
            await File.WriteAllTextAsync(outsideFile, "outside");
            await CreateHardLinkAsync(Path.Combine(hardLinkRoot, "pcsteward.db"), outsideFile);
            AssertOnboardingLeafCollision(hardLinkRoot);
            File.Delete(Path.Combine(hardLinkRoot, "pcsteward.db"));
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(hardLinkRoot).Succeeded);
            Assert.AreEqual("outside", await File.ReadAllTextAsync(outsideFile));
        }
        finally
        {
            if (Directory.Exists(directoryRoot)) Directory.Delete(directoryRoot, true);
            CleanupFixtureRoot(reparseRoot, Path.Combine(reparseRoot, "pcsteward.db"));
            if (Directory.Exists(hardLinkRoot)) Directory.Delete(hardLinkRoot, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [TestMethod]
    public async Task MarkerProgressIsBoundedAndRecordsPartialRetryAttempts()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-progress-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            FactoryReset.ArmForTesting(root);

            FactoryResetResult partial = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.FromException(new InvalidOperationException("exact task remains")), CancellationToken.None);
            Assert.IsFalse(partial.Succeeded);
            using (JsonDocument partialMarker = JsonDocument.Parse(await File.ReadAllTextAsync(FactoryReset.GetMarkerPath(root))))
            {
                JsonElement payload = partialMarker.RootElement;
                Assert.AreEqual(6, payload.EnumerateObject().Count());
                Assert.AreEqual("Partial", payload.GetProperty("state").GetString());
                Assert.AreEqual(1, payload.GetProperty("attempt").GetInt32());
                Assert.AreEqual("Residual", payload.GetProperty("categories").GetProperty("ScheduledCheck").GetString());
                Assert.AreEqual("VerifiedAbsent", payload.GetProperty("categories").GetProperty("OwnedRoot").GetString());
            }

            FactoryResetResult retry = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsTrue(retry.Succeeded, string.Join("; ", retry.Residuals.Select(item => item.Reason)));
            using JsonDocument completedMarker = JsonDocument.Parse(await File.ReadAllTextAsync(FactoryReset.GetMarkerPath(root)));
            Assert.AreEqual("OnboardingPending", completedMarker.RootElement.GetProperty("state").GetString());
            Assert.AreEqual(2, completedMarker.RootElement.GetProperty("attempt").GetInt32());
            Assert.AreEqual("VerifiedAbsent", completedMarker.RootElement.GetProperty("categories").GetProperty("ScheduledCheck").GetString());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ProgressWriteFailureKeepsTheMarkerAndBlocksOnboardingUntilRetry()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-progress-write-failure-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "old");
            FactoryReset.ArmForTesting(root);
            NativeResetControl.BeforeProgressWriteForTesting = () => throw new IOException("simulated progress write failure");

            FactoryResetResult failure = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(failure.Succeeded);
            Assert.IsTrue(failure.Residuals.Any(item => item.Reason == "ControlStateWriteFailed"));
            Assert.IsTrue(File.Exists(FactoryReset.GetMarkerPath(root)));
            Assert.IsFalse(File.Exists(Path.Combine(root, "pcsteward.db")), "Safe deletion may complete, but the durable marker remains the recovery authority.");
            Assert.AreNotEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));

            NativeResetControl.BeforeProgressWriteForTesting = null;
            FactoryResetResult retry = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsTrue(retry.Succeeded, string.Join("; ", retry.Residuals.Select(item => item.Reason)));
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
        }
        finally
        {
            NativeResetControl.BeforeProgressWriteForTesting = null;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task RecoveredOnboardingTransitionRechecksTheExactOwnedRootManifest()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-transition-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reset"));
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), CreateMarkerPayload("ResetPending", attempt: 1));
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.TemporaryMarkerFileName), CreateMarkerPayload("OnboardingPending", attempt: 1));
            await File.WriteAllTextAsync(Path.Combine(root, "unclassified.txt"), "must-remain");

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.FromException(new InvalidOperationException("cleanup must not replay")), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(item => item.Reason == "UnexpectedOwnedObject"));
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
            Assert.IsTrue(File.Exists(Path.Combine(root, "unclassified.txt")));
            Assert.IsTrue(File.Exists(FactoryReset.GetMarkerPath(root)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task MalformedMarkerFailsClosedWithoutDeletingExistingState()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reset"));
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), "{ not-json");
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "preserved");

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(item => item.Reason == "InvalidMarker"));
            Assert.IsTrue(File.Exists(Path.Combine(root, "pcsteward.db")));
            Assert.IsTrue(FactoryReset.IsPending(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task UnknownMarkerSchemaFailsClosedWithoutDeletingExistingState()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-unknown-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reset"));
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), "{\"schemaVersion\":2,\"state\":\"ResetPending\"}");
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "preserved");

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(item => item.Reason == "InvalidMarker"));
            Assert.IsTrue(File.Exists(Path.Combine(root, "pcsteward.db")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task OutOfRangeMarkerAttemptFailsClosedWithoutDeletingExistingState()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-attempt-bound-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reset"));
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), CreateMarkerPayload("ResetPending", attempt: 100001));
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "preserved");

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(item => item.Reason == "InvalidMarker"));
            Assert.IsTrue(File.Exists(Path.Combine(root, "pcsteward.db")));
            Assert.IsTrue(FactoryReset.IsPending(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task AttemptBoundaryPersistsFinalAttemptAndCapsSubsequentRecoveryAsPartial()
    {
        string boundaryRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-attempt-final-" + Guid.NewGuid().ToString("N"));
        string cappedRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-attempt-capped-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(boundaryRoot, "reset"));
            await File.WriteAllTextAsync(Path.Combine(boundaryRoot, "reset", FactoryReset.MarkerFileName), CreateMarkerPayload("ResetPending", attempt: 99999));
            FactoryResetResult finalAttempt = await FactoryReset.CompletePendingForTestingAsync(boundaryRoot, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsTrue(finalAttempt.Succeeded, string.Join("; ", finalAttempt.Residuals.Select(item => item.Reason)));
            using (JsonDocument marker = JsonDocument.Parse(await File.ReadAllTextAsync(FactoryReset.GetMarkerPath(boundaryRoot))))
                Assert.AreEqual(100000, marker.RootElement.GetProperty("attempt").GetInt32());

            Directory.CreateDirectory(Path.Combine(cappedRoot, "reset"));
            await File.WriteAllTextAsync(Path.Combine(cappedRoot, "reset", FactoryReset.MarkerFileName), CreateMarkerPayload("Partial", attempt: 100000));
            await File.WriteAllTextAsync(Path.Combine(cappedRoot, "pcsteward.db"), "must-survive");
            FactoryResetResult capped = await FactoryReset.CompletePendingForTestingAsync(cappedRoot, _ => Task.CompletedTask, CancellationToken.None);
            FactoryResetResult retry = await FactoryReset.CompletePendingForTestingAsync(cappedRoot, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(capped.Succeeded);
            Assert.IsFalse(retry.Succeeded);
            Assert.IsTrue(capped.Residuals.Any(item => item.Reason == "AttemptLimitReached"));
            Assert.IsTrue(retry.Residuals.Any(item => item.Reason == "AttemptLimitReached"));
            Assert.AreEqual(FactoryResetMarkerState.Partial, FactoryReset.GetMarkerState(cappedRoot));
            Assert.IsTrue(File.Exists(FactoryReset.GetMarkerPath(cappedRoot)));
            Assert.IsTrue(File.Exists(Path.Combine(cappedRoot, "pcsteward.db")));
        }
        finally
        {
            if (Directory.Exists(boundaryRoot)) Directory.Delete(boundaryRoot, true);
            if (Directory.Exists(cappedRoot)) Directory.Delete(cappedRoot, true);
        }
    }

    [TestMethod]
    public async Task OnboardingBootstrapAllowsDatabaseLeavesButRejectsControlFileOrReparse()
    {
        string databaseRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-bootstrap-database-" + Guid.NewGuid().ToString("N"));
        string fileRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-bootstrap-file-" + Guid.NewGuid().ToString("N"));
        string reparseRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-bootstrap-reparse-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-bootstrap-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outside);
            await File.WriteAllTextAsync(Path.Combine(outside, "sentinel.txt"), "outside");

            await CompleteToOnboardingAsync(databaseRoot);
            await File.WriteAllTextAsync(Path.Combine(databaseRoot, "pcsteward.db"), "old-state");
            Assert.IsTrue(FactoryReset.VerifyOnboardingBootstrapForTesting(databaseRoot).Succeeded);
            Assert.IsTrue(FactoryReset.IsPending(databaseRoot));
            SqliteDatabase invalidDatabase = new(Path.Combine(databaseRoot, "pcsteward.db"));
            await Assert.ThrowsExactlyAsync<SqliteException>(() => invalidDatabase.MigrateAsync(CancellationToken.None));
            File.Delete(Path.Combine(databaseRoot, "pcsteward.db"));
            Assert.IsTrue(FactoryReset.VerifyOnboardingBootstrapForTesting(databaseRoot).Succeeded);

            await CompleteToOnboardingAsync(fileRoot);
            Directory.Move(Path.Combine(fileRoot, "reset"), Path.Combine(outside, "moved-reset"));
            await File.WriteAllTextAsync(Path.Combine(fileRoot, "reset"), "file-substitution");
            Assert.IsFalse(FactoryReset.VerifyOnboardingBootstrapForTesting(fileRoot).Succeeded);
            Assert.IsTrue(FactoryReset.IsPending(fileRoot));

            await CompleteToOnboardingAsync(reparseRoot);
            Directory.Move(Path.Combine(reparseRoot, "reset"), Path.Combine(outside, "moved-reset-reparse"));
            await CreateJunctionAsync(Path.Combine(reparseRoot, "reset"), outside);
            Assert.IsFalse(FactoryReset.VerifyOnboardingBootstrapForTesting(reparseRoot).Succeeded);
            Assert.IsTrue(FactoryReset.IsPending(reparseRoot));
            Assert.AreEqual("outside", await File.ReadAllTextAsync(Path.Combine(outside, "sentinel.txt")));
            new DirectoryInfo(Path.Combine(reparseRoot, "reset")).Delete();
        }
        finally
        {
            if (Directory.Exists(databaseRoot)) Directory.Delete(databaseRoot, true);
            if (Directory.Exists(fileRoot)) Directory.Delete(fileRoot, true);
            CleanupFixtureRoot(reparseRoot, Path.Combine(reparseRoot, "reset"));
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [TestMethod]
    public async Task PromotionControlSwapCutpointRetainsTheBoundOnboardingAuthority()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-promotion-swap-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-promotion-swap-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reset"));
            Directory.CreateDirectory(outside);
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), CreateMarkerPayload("ResetPending"));
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.TemporaryMarkerFileName), CreateMarkerPayload("OnboardingPending"));
            await File.WriteAllTextAsync(Path.Combine(outside, "sentinel.txt"), "outside");
            bool swapBlocked = false;
            NativeResetControl.BeforeOnboardingControlVerificationForTesting = () =>
            {
                try { Directory.Move(Path.Combine(root, "reset"), Path.Combine(outside, "moved-reset")); }
                catch (IOException) { swapBlocked = true; }
            };

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.FromException(new InvalidOperationException("cleanup must not replay")), CancellationToken.None);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Residuals.Select(item => item.Reason)));
            Assert.IsTrue(swapBlocked, "The bound reset-control handle must prevent reset-to-file/reparse substitution before onboarding is accepted.");
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
            Assert.AreEqual("outside", await File.ReadAllTextAsync(Path.Combine(outside, "sentinel.txt")));
        }
        finally
        {
            NativeResetControl.BeforeOnboardingControlVerificationForTesting = null;
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [TestMethod]
    public async Task ScheduleRemovalFailureKeepsMarkerAndOldStateUnavailable()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-schedule-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "database");
            FactoryReset.ArmForTesting(root);

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.FromException(new InvalidOperationException("exact task still present")), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(item => item.Reason == "ScheduleRemovalFailed"));
            Assert.IsTrue(FactoryReset.IsPending(root));
            Assert.IsFalse(File.Exists(Path.Combine(root, "pcsteward.db")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task FreshStoreVerificationRejectsPriorStateAndAcceptsNewMigratedDatabase()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-fresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SqliteDatabase database = new(Path.Combine(root, "pcsteward.db"));
            await database.MigrateAsync(CancellationToken.None);
            Assert.IsTrue(await database.IsFreshResetStoreAsync(CancellationToken.None));

            await database.SaveConnectorEnabledAsync("WinGet installed inventory", true, CancellationToken.None);

            Assert.IsFalse(await database.IsFreshResetStoreAsync(CancellationToken.None));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task AsyncDataOperationLeaseBlocksResetUntilTheOperationCompletes()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-lease-" + Guid.NewGuid().ToString("N"));
        Guid gate = Guid.NewGuid();
        CurrentUserDataOperationSession? Acquire(TimeSpan wait, CancellationToken ct) => CurrentUserDataOperationSession.TryAcquireForTesting(gate, wait, ct);
        static Task<bool> Clear(OperationOwnership _, CancellationToken __) => Task.FromResult(true);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? operation = null;
        try
        {
            Directory.CreateDirectory(root);
            // Hold the common gate on a dedicated owner thread, as the desktop facade does for asynchronous work.
            operation = Task.Factory.StartNew(() => DataOperationLease.RunOnCurrentThread(_ => { started.SetResult(); release.Task.GetAwaiter().GetResult(); return true; },
                DataOperationKind.Mutating, Acquire, () => true, Clear, CancellationToken.None), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            // Fail fast (never hang) if the holder could not acquire the gate.
            await Task.WhenAny(started.Task, operation).WaitAsync(TimeSpan.FromSeconds(30));
            if (!started.Task.IsCompleted) await operation;

            Assert.ThrowsExactly<InvalidOperationException>(() => DataOperationLease.RunOnCurrentThread(_ => { FactoryReset.ArmForTesting(root); return true; },
                DataOperationKind.Mutating, Acquire, () => true, Clear, CancellationToken.None), "Reset must fail while another data operation owns the gate.");
            Assert.AreEqual(FactoryResetMarkerState.None, FactoryReset.GetMarkerState(root));

            release.SetResult();
            await operation;
            DataOperationLease.RunOnCurrentThread(_ => { FactoryReset.ArmForTesting(root); return true; }, DataOperationKind.Mutating, Acquire, () => true, Clear, CancellationToken.None);
            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, async _ => await Task.Yield(), CancellationToken.None);
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
        }
        finally
        {
            release.TrySetResult();
            if (operation is not null) await operation;
            CleanupFixtureRoot(root);
        }
    }

    [TestMethod]
    public async Task ValidTemporaryMarkerRecoversButUnexpectedOrOversizedControlStateFailsClosed()
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-temp-" + Guid.NewGuid().ToString("N"));
        string hostileRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-hostile-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "reset"));
            await File.WriteAllTextAsync(Path.Combine(temporaryRoot, "reset", FactoryReset.TemporaryMarkerFileName), CreateMarkerPayload("ResetPending"));
            await File.WriteAllTextAsync(Path.Combine(temporaryRoot, "pcsteward.db"), "old");
            FactoryResetResult recovered = await FactoryReset.CompletePendingForTestingAsync(temporaryRoot, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsTrue(recovered.Succeeded);
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(temporaryRoot));
            Assert.IsFalse(File.Exists(Path.Combine(temporaryRoot, "pcsteward.db")));

            Directory.CreateDirectory(Path.Combine(hostileRoot, "reset"));
            await File.WriteAllTextAsync(Path.Combine(hostileRoot, "reset", FactoryReset.MarkerFileName), new string('x', 129));
            await File.WriteAllTextAsync(Path.Combine(hostileRoot, "reset", "unknown-control"), "hostile");
            await File.WriteAllTextAsync(Path.Combine(hostileRoot, "pcsteward.db"), "must-survive");
            FactoryResetResult hostile = await FactoryReset.CompletePendingForTestingAsync(hostileRoot, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsFalse(hostile.Succeeded);
            Assert.IsTrue(hostile.Residuals.Any(item => item.Reason == "InvalidMarker"));
            Assert.IsTrue(File.Exists(Path.Combine(hostileRoot, "pcsteward.db")));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
            if (Directory.Exists(hostileRoot)) Directory.Delete(hostileRoot, true);
        }
    }

    [TestMethod]
    public async Task ReparseCollisionAndLockedControlStateNeverDropsResetAuthority()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-control-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-control-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
            await CreateJunctionAsync(Path.Combine(root, "reset"), outside);
            Assert.ThrowsExactly<System.Security.SecurityException>(() => FactoryReset.ArmForTesting(root));
            Assert.IsFalse(File.Exists(Path.Combine(outside, FactoryReset.MarkerFileName)));
            new DirectoryInfo(Path.Combine(root, "reset")).Delete();

            Directory.CreateDirectory(Path.Combine(root, "reset"));
            await CreateJunctionAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), outside);
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "must-survive");
            FactoryResetResult reparse = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsFalse(reparse.Succeeded);
            Assert.IsTrue(File.Exists(Path.Combine(root, "pcsteward.db")));
            new DirectoryInfo(Path.Combine(root, "reset", FactoryReset.MarkerFileName)).Delete();
            Directory.Delete(Path.Combine(root, "reset"));

            Directory.CreateDirectory(Path.Combine(root, "reset"));
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), CreateMarkerPayload("ResetPending"));
            Assert.ThrowsExactly<InvalidOperationException>(() => FactoryReset.ArmForTesting(root));
            Assert.IsTrue(File.Exists(Path.Combine(root, "reset", FactoryReset.MarkerFileName)));

            FactoryResetResult completed = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsTrue(completed.Succeeded);
            await using (FileStream lockHandle = new(Path.Combine(root, "reset", FactoryReset.MarkerFileName), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                FactoryResetResult blocked = FactoryReset.AcknowledgeOnboardingForTesting(root);
                Assert.IsFalse(blocked.Succeeded);
                Assert.IsTrue(FactoryReset.IsPending(root));
            }
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);
        }
        finally
        {
            CleanupFixtureRoot(root, Path.Combine(root, "reset", FactoryReset.MarkerFileName), Path.Combine(root, "reset"));
            CleanupFixtureRoot(outside);
        }
    }

    [TestMethod]
    public async Task HardLinkedMarkerFailsClosedAndDoesNotDeletePriorState()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-hardlink-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-hardlink-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reset")); Directory.CreateDirectory(outside);
            string source = Path.Combine(outside, "control.json");
            await File.WriteAllTextAsync(source, CreateMarkerPayload("ResetPending"));
            await CreateHardLinkAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), source);
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "must-survive");

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(item => item.Reason == "InvalidMarker"));
            Assert.IsTrue(File.Exists(Path.Combine(root, "pcsteward.db")));
            Assert.IsTrue(File.Exists(source));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [TestMethod]
    public async Task EmptyControlDirectoryBlocksStartupAndTheOnlyValidTwoLeafTransitionIsPromoted()
    {
        string emptyRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-empty-" + Guid.NewGuid().ToString("N"));
        string transitionRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-transition-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(emptyRoot, "reset"));
            Assert.AreEqual(FactoryResetMarkerState.Invalid, FactoryReset.GetMarkerState(emptyRoot));
            Assert.IsTrue(FactoryReset.IsPending(emptyRoot));
            Assert.ThrowsExactly<InvalidOperationException>(() => FactoryReset.ArmForTesting(emptyRoot));
            Assert.IsTrue(FactoryReset.IsPending(emptyRoot));

            Directory.CreateDirectory(Path.Combine(transitionRoot, "reset"));
            await File.WriteAllTextAsync(Path.Combine(transitionRoot, "reset", FactoryReset.MarkerFileName), CreateMarkerPayload("ResetPending"));
            await File.WriteAllTextAsync(Path.Combine(transitionRoot, "reset", FactoryReset.TemporaryMarkerFileName), CreateMarkerPayload("OnboardingPending"));
            FactoryResetResult transition = await FactoryReset.CompletePendingForTestingAsync(transitionRoot, _ => Task.FromException(new InvalidOperationException("must not replay cleanup")), CancellationToken.None);
            Assert.IsTrue(transition.Succeeded);
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(transitionRoot));
            Assert.IsFalse(File.Exists(Path.Combine(transitionRoot, "reset", FactoryReset.TemporaryMarkerFileName)));

            await File.WriteAllTextAsync(Path.Combine(emptyRoot, "reset", FactoryReset.MarkerFileName), CreateMarkerPayload("OnboardingPending"));
            await File.WriteAllTextAsync(Path.Combine(emptyRoot, "reset", FactoryReset.TemporaryMarkerFileName), CreateMarkerPayload("ResetPending"));
            Assert.AreEqual(FactoryResetMarkerState.Invalid, FactoryReset.GetMarkerState(emptyRoot));
        }
        finally
        {
            if (Directory.Exists(emptyRoot)) Directory.Delete(emptyRoot, true);
            if (Directory.Exists(transitionRoot)) Directory.Delete(transitionRoot, true);
        }
    }

    [TestMethod]
    public async Task BoundLeafAndDirectorySwapCutpointsCannotRedirectAcknowledgement()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-swap-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-swap-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
            string sentinel = Path.Combine(outside, "must-survive.txt");
            await File.WriteAllTextAsync(sentinel, "outside");

            await CreateOnboardingMarkerAsync(root);
            bool leafSwapBlocked = false;
            NativeResetControl.BeforeMarkerDispositionForTesting = () =>
            {
                try { File.Move(Path.Combine(root, "reset", FactoryReset.MarkerFileName), Path.Combine(outside, "moved-marker.json")); }
                catch (IOException) { leafSwapBlocked = true; }
            };
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);
            Assert.IsTrue(leafSwapBlocked);
            Assert.AreEqual("outside", await File.ReadAllTextAsync(sentinel));

            await CreateOnboardingMarkerAsync(root);
            bool directorySwapBlocked = false;
            NativeResetControl.BeforeDirectoryDispositionForTesting = () =>
            {
                try { Directory.Move(Path.Combine(root, "reset"), Path.Combine(outside, "moved-reset")); }
                catch (IOException) { directorySwapBlocked = true; }
            };
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);
            Assert.IsTrue(directorySwapBlocked);
            Assert.AreEqual("outside", await File.ReadAllTextAsync(sentinel));
        }
        finally
        {
            NativeResetControl.BeforeMarkerDispositionForTesting = null;
            NativeResetControl.BeforeDirectoryDispositionForTesting = null;
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [TestMethod]
    public async Task LockedControlDirectoryRetainsDurableOnboardingAuthority()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-directory-lock-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await CreateOnboardingMarkerAsync(root);
            using (SafeFileHandle directoryLock = OpenDirectoryExclusive(Path.Combine(root, "reset")))
            {
                FactoryResetResult result = FactoryReset.AcknowledgeOnboardingForTesting(root);
                Assert.IsFalse(result.Succeeded);
                Assert.IsTrue(FactoryReset.IsPending(root));
                Assert.IsTrue(File.Exists(Path.Combine(root, "reset", FactoryReset.MarkerFileName)));
            }
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);

            await CreateOnboardingMarkerAsync(root);
            NativeResetControl.BeforeDirectoryDispositionForTesting = () => throw new IOException("simulated directory lock after leaf disposition");
            FactoryResetResult midCleanupFailure = FactoryReset.AcknowledgeOnboardingForTesting(root);
            Assert.IsFalse(midCleanupFailure.Succeeded);
            Assert.IsTrue(FactoryReset.IsPending(root));
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
            Assert.IsTrue(File.Exists(Path.Combine(root, "reset", FactoryReset.TemporaryMarkerFileName)));
            NativeResetControl.BeforeDirectoryDispositionForTesting = null;
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);
        }
        finally
        {
            NativeResetControl.BeforeDirectoryDispositionForTesting = null;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task CreateOnboardingMarkerAsync(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "reset"));
        await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), CreateMarkerPayload("OnboardingPending"));
    }

    private static string CreateMarkerPayload(string state, int attempt = 0, string resetId = "11111111-1111-1111-1111-111111111111")
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            resetId,
            requestedUtc = "2026-08-01T00:00:00.0000000+00:00",
            attempt,
            state,
            categories = new
            {
                ScheduledCheck = "Pending",
                Downloads = "Pending",
                Logs = "Pending",
                Database = "Pending",
                OwnedRoot = "Pending"
            }
        });
    }

    private static async Task CompleteToOnboardingAsync(string root)
    {
        Directory.CreateDirectory(root);
        FactoryReset.ArmForTesting(root);
        FactoryResetResult completed = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);
        Assert.IsTrue(completed.Succeeded, string.Join("; ", completed.Residuals.Select(item => item.Reason)));
    }

    private static void AssertOnboardingLeafCollision(string root)
    {
        FactoryResetResult blocked = FactoryReset.AcknowledgeOnboardingForTesting(root);
        Assert.IsFalse(blocked.Succeeded);
        Assert.IsTrue(blocked.Residuals.Any(item => item.Reason == "UnexpectedOwnedObject"));
        Assert.IsTrue(File.Exists(FactoryReset.GetMarkerPath(root)));
    }

    private static void CleanupFixtureRoot(string root, params string[] reparsePaths)
    {
        foreach (string reparsePath in reparsePaths.OrderBy(path => path.Count(character =>
                     character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar)))
        {
            DeleteReparsePointIfPresent(reparsePath);
        }

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void DeleteReparsePointIfPresent(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            return;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            Directory.Delete(path);
        }
        else
        {
            File.Delete(path);
        }
    }

    private static SafeFileHandle OpenDirectoryExclusive(string path)
    {
        SafeFileHandle handle = CreateFileW(path, 0x80000000, 0, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        Assert.IsFalse(handle.IsInvalid, "Opening the control directory exclusively failed. Win32=" + Marshal.GetLastWin32Error());
        return handle;
    }

    private static async Task CreateJunctionAsync(string path, string target)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{path}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        process.Start();
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, await process.StandardError.ReadToEndAsync());
    }

    private static async Task CreateHardLinkAsync(string path, string target)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /H \"{path}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        process.Start();
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, await process.StandardError.ReadToEndAsync());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
