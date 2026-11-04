using System.Text.Json;
using PCSteward.Storage;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §5.2 step 2 and §7 D7 (slice S4): the Storage facts behind
/// the desktop probe gate (<c>PCSteward.Desktop.QualificationProbeLaunchGate</c>, which acquires with
/// <c>TryAcquire(TimeSpan.Zero, …)</c>, reads <c>Context.RecoveryRequired</c> once and maps only
/// <see cref="FactoryResetMarkerState.None"/> to "settled"), on test-identity gates and GUID-named temp state roots: a held gate
/// gives a null session (GateBusy); an abandonment is reported to one acquirer only (review M-2 b); every non-<c>None</c>
/// marker state, including <c>Invalid</c>, is unsettled (GateResetPending) and reading it writes nothing (review M-2 c); the
/// session is released only on the acquiring thread.
/// </summary>
[TestClass]
public sealed class QualificationProbeGateTests
{
    [TestMethod]
    public void AHeldGateGivesASecondAcquirerNoSessionWhichTheProbeRecordsAsGateBusy()
    {
        Guid id = Guid.NewGuid();
        using CurrentUserDataOperationSession? holder = CurrentUserDataOperationSession.TryAcquireForTesting(id, TimeSpan.Zero, CancellationToken.None);
        Assert.IsNotNull(holder);
        CurrentUserDataOperationSession? second = null;
        Exception? failure = null;
        Thread other = new(() =>
        {
            try { second = CurrentUserDataOperationSession.TryAcquireForTesting(id, TimeSpan.Zero, CancellationToken.None); }
            catch (Exception exception) { failure = exception; }
        });
        other.Start();
        Assert.IsTrue(other.Join(TimeSpan.FromSeconds(10)));
        Assert.IsNull(failure);
        Assert.IsNull(second, "a held gate is busy, never a wait");
    }

    [TestMethod]
    public void AnAbandonedGateIsReportedToTheFirstAcquirerOnlyAndASecondAcquisitionReportsNoRecovery()
    {
        Guid id = Guid.NewGuid();
        using Mutex retained = new(false, CurrentUserDataOperationSession.LegacyNameForTesting(id));
        Exception? failure = null;
        Thread abandon = new(() =>
        {
            try
            {
                using Mutex held = new(false, CurrentUserDataOperationSession.LegacyNameForTesting(id));
                if (!held.WaitOne(0)) throw new InvalidOperationException("Fixture mutex is busy.");
            }
            catch (Exception exception) { failure = exception; }
        });
        abandon.Start();
        Assert.IsTrue(abandon.Join(TimeSpan.FromSeconds(10)));
        Assert.IsNull(failure);

        using (CurrentUserDataOperationSession? first = CurrentUserDataOperationSession.TryAcquireForTesting(id, TimeSpan.Zero, CancellationToken.None))
        {
            Assert.IsNotNull(first);
            Assert.IsTrue(first.Context.RecoveryRequired, "the probe would record GateRecoveryRequired");
        }
        using CurrentUserDataOperationSession? second = CurrentUserDataOperationSession.TryAcquireForTesting(id, TimeSpan.Zero, CancellationToken.None);
        Assert.IsNotNull(second);
        Assert.IsFalse(second.Context.RecoveryRequired, "Windows reports an abandonment to one acquirer only: a narrow check, not a compensating control");
    }

    [TestMethod]
    public void TheSessionIsReleasedOnlyOnTheAcquiringThread()
    {
        using CurrentUserDataOperationSession? session = CurrentUserDataOperationSession.TryAcquireForTesting(Guid.NewGuid(), TimeSpan.Zero, CancellationToken.None);
        Assert.IsNotNull(session);
        Exception? failure = null;
        Thread worker = new(() => { try { session.Dispose(); } catch (Exception exception) { failure = exception; } });
        worker.Start();
        Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(10)));
        Assert.IsInstanceOfType<InvalidOperationException>(failure, "so the probe runs its worker steps off this thread and disposes here");
        Assert.IsTrue(session.Context.IsActive);
    }

    [TestMethod]
    [DataRow("None")]
    [DataRow("ResetPending")]
    [DataRow("Partial")]
    [DataRow("OnboardingPending")]
    [DataRow("TransitionPending")]
    [DataRow("Invalid")]
    public async Task EveryNonNoneMarkerStateIsUnsettledAndReadingItWritesNothing(string stateName)
    {
        FactoryResetMarkerState expected = Enum.Parse<FactoryResetMarkerState>(stateName);
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-probe-marker-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string control = Path.Combine(root, "reset");
            switch (expected)
            {
                case FactoryResetMarkerState.ResetPending or FactoryResetMarkerState.Partial or FactoryResetMarkerState.OnboardingPending:
                    Directory.CreateDirectory(control);
                    await File.WriteAllTextAsync(Path.Combine(control, FactoryReset.MarkerFileName), Marker(stateName));
                    break;
                case FactoryResetMarkerState.TransitionPending:
                    Directory.CreateDirectory(control);
                    await File.WriteAllTextAsync(Path.Combine(control, FactoryReset.MarkerFileName), Marker("ResetPending"));
                    await File.WriteAllTextAsync(Path.Combine(control, FactoryReset.TemporaryMarkerFileName), Marker("OnboardingPending"));
                    break;
                case FactoryResetMarkerState.Invalid:
                    Directory.CreateDirectory(control);
                    break;
            }
            string before = Snapshot(root);

            FactoryResetMarkerState state = FactoryReset.GetMarkerState(root);

            Assert.AreEqual(expected, state);
            Assert.AreEqual(expected == FactoryResetMarkerState.None, state == FactoryResetMarkerState.None,
                "the probe gate is settled only for None; every other state is GateResetPending");
            Assert.AreEqual(before, Snapshot(root), "reading the marker deletes, promotes and writes nothing");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string Snapshot(string root) => string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => File.Exists(path)
            ? $"{Path.GetRelativePath(root, path)}|{Convert.ToHexString(File.ReadAllBytes(path))}|{File.GetLastWriteTimeUtc(path):O}"
            : $"{Path.GetRelativePath(root, path)}/|{Directory.GetLastWriteTimeUtc(path):O}"));

    private static string Marker(string state) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        resetId = "11111111-1111-1111-1111-111111111111",
        requestedUtc = "2026-08-01T00:00:00.0000000+00:00",
        attempt = 0,
        state,
        categories = new { ScheduledCheck = "Pending", Downloads = "Pending", Logs = "Pending", Database = "Pending", OwnedRoot = "Pending" },
    });
}
