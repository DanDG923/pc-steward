using System.Security.AccessControl;
using System.Security.Principal;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using PCSteward.Storage;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Task 50 / ADR-025: synthetic gate objects only; no application data or provider work.</summary>
[TestClass]
public sealed class CurrentUserOperationGateTests
{
    private static readonly SecurityIdentifier FixtureUser = new("S-1-5-21-11-22-33-1001");

    [TestMethod]
    public void NamespaceSeparatesUsersButNotLogonSessions()
    {
        Assert.AreEqual("Global\\PCSteward.DataOperation.v2.S-1-5-21-11-22-33-1001", OperationGateSecurity.NameFor(FixtureUser));
        Assert.AreNotEqual(OperationGateSecurity.NameFor(FixtureUser), OperationGateSecurity.NameFor(new SecurityIdentifier("S-1-5-21-11-22-33-1002")));
    }

    [TestMethod]
    public void ExactProtectedUserDescriptorIsTrusted()
    {
        Assert.IsTrue(OperationGateSecurity.IsTrusted(OperationGateSecurity.DescriptorFor(FixtureUser), FixtureUser));
    }

    [TestMethod]
    [DataRow("O:SYD:P(A;;0x001F0001;;;S-1-5-21-11-22-33-1001)")]
    [DataRow("O:S-1-5-21-11-22-33-1001D:(A;;0x001F0001;;;S-1-5-21-11-22-33-1001)")]
    [DataRow("O:S-1-5-21-11-22-33-1001D:P(A;;0x001F0001;;;WD)")]
    [DataRow("O:S-1-5-21-11-22-33-1001D:P(A;;0x001F0001;;;S-1-5-21-11-22-33-1001)(A;;0x001F0001;;;BA)")]
    [DataRow("O:S-1-5-21-11-22-33-1001D:P(A;;0x00100001;;;S-1-5-21-11-22-33-1001)")]
    [DataRow("O:S-1-5-21-11-22-33-1001")]
    public void ForeignOwnerOrNoncanonicalAccessCannotAdmitWork(string sddl)
    {
        Assert.IsFalse(OperationGateSecurity.IsTrusted(new RawSecurityDescriptor(sddl), FixtureUser));
    }

    [TestMethod]
    public void NativeGateCanBeReopenedAndReleasedWithoutReentrancy()
    {
        Guid id = Guid.NewGuid();
        using CurrentUserOperationGate gate = CurrentUserOperationGate.OpenForTesting(id);
        using CurrentUserOperationGate other = CurrentUserOperationGate.OpenForTesting(id);
        using (OperationGateOwnership first = gate.TryAcquire(TimeSpan.Zero, CancellationToken.None)!)
        {
            Assert.IsNotNull(first);
            Assert.IsFalse(first.RecoveryRequired);
            Assert.ThrowsExactly<InvalidOperationException>(() => gate.TryAcquire(TimeSpan.Zero, CancellationToken.None));
            // A mutex is recursively acquirable on its owner thread, so contend on another thread.
            bool busy = false;
            RunOnOtherThread(() => busy = other.TryAcquire(TimeSpan.Zero, CancellationToken.None) is null);
            Assert.IsTrue(busy);
        }
        using OperationGateOwnership second = other.TryAcquire(TimeSpan.Zero, CancellationToken.None)!;
        Assert.IsNotNull(second);
    }

    [TestMethod]
    public void PrecancelledWaitNeverTakesOwnership()
    {
        using CurrentUserOperationGate gate = CurrentUserOperationGate.OpenForTesting(Guid.NewGuid());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => gate.TryAcquire(TimeSpan.FromSeconds(1), cancellation.Token));
        using OperationGateOwnership acquired = gate.TryAcquire(TimeSpan.Zero, CancellationToken.None)!;
        Assert.IsNotNull(acquired);
    }

    [TestMethod]
    public void AbandonmentGrantsRecoveryOwnershipInsteadOfOrdinaryAdmission()
    {
        Guid id = Guid.NewGuid();
        using CurrentUserOperationGate gate = CurrentUserOperationGate.OpenForTesting(id);
        Exception? workerFailure = null;
        Thread worker = new(() =>
        {
            try
            {
                // Intentionally omit ReleaseMutex; the fixture thread exits while owning it.
                using Mutex abandoned = Mutex.OpenExisting(gate.Name);
                if (!abandoned.WaitOne(TimeSpan.FromSeconds(2))) throw new TimeoutException("Fixture could not acquire mutex.");
            }
            catch (Exception exception) { workerFailure = exception; }
        });
        worker.Start();
        Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.IsNull(workerFailure);
        using OperationGateOwnership recovery = gate.TryAcquire(TimeSpan.FromSeconds(2), CancellationToken.None)!;
        Assert.IsNotNull(recovery);
        Assert.IsTrue(recovery.RecoveryRequired);
    }

    [TestMethod]
    public void ExistingUnprotectedObjectIsRefusedWithoutChangingItsSecurity()
    {
        Guid id = Guid.NewGuid();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string name = OperationGateSecurity.NameFor(identity.User!) + ".test." + id.ToString("N");
        using Mutex squatted = new(false, name);
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => CurrentUserOperationGate.OpenForTesting(id));
        Assert.IsTrue(squatted.WaitOne(0));
        squatted.ReleaseMutex();
    }

    [TestMethod]
    public void SecondProcessWithSameSidObservesContentionThenReleasedOwnership()
    {
        Guid id = Guid.NewGuid();
        using CurrentUserOperationGate gate = CurrentUserOperationGate.OpenForTesting(id);
        using (OperationGateOwnership owner = gate.TryAcquire(TimeSpan.Zero, CancellationToken.None)!)
        {
            Assert.IsNotNull(owner);
            Assert.AreEqual("busy", ObserveFromSecondProcess(id));
        }
        Assert.AreEqual("acquired", ObserveFromSecondProcess(id));
    }

    [TestMethod]
    public void WrongThreadCannotReleaseAnOwnersExclusion()
    {
        Guid id = Guid.NewGuid();
        using CurrentUserOperationGate gate = CurrentUserOperationGate.OpenForTesting(id);
        using OperationGateOwnership owner = gate.TryAcquire(TimeSpan.Zero, CancellationToken.None)!;
        Assert.IsNotNull(owner);
        RunOnOtherThread(() => Assert.ThrowsExactly<InvalidOperationException>(owner.Dispose));
        Assert.AreEqual("busy", ObserveFromSecondProcess(id));
        Assert.ThrowsExactly<InvalidOperationException>(gate.Dispose);
    }

    [TestMethod]
    public void ContendedCancellationAndTimeoutLeaveTheGateReusable()
    {
        Guid id = Guid.NewGuid();
        using CurrentUserOperationGate gate = CurrentUserOperationGate.OpenForTesting(id);
        using CurrentUserOperationGate waiting = CurrentUserOperationGate.OpenForTesting(id);
        using (OperationGateOwnership owner = gate.TryAcquire(TimeSpan.Zero, CancellationToken.None)!)
        {
            Assert.IsNotNull(owner);
            RunOnOtherThread(() =>
            {
                Assert.IsNull(waiting.TryAcquire(TimeSpan.FromMilliseconds(10), CancellationToken.None));
                using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(20));
                Assert.ThrowsExactly<OperationCanceledException>(() => waiting.TryAcquire(TimeSpan.FromSeconds(5), cancellation.Token));
            });
        }
        using OperationGateOwnership after = waiting.TryAcquire(TimeSpan.Zero, CancellationToken.None)!;
        Assert.IsNotNull(after);
    }

    [TestMethod]
    public void UnboundedWaitIsNotRepresentable()
    {
        using CurrentUserOperationGate gate = CurrentUserOperationGate.OpenForTesting(Guid.NewGuid());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => gate.TryAcquire(Timeout.InfiniteTimeSpan, CancellationToken.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => gate.TryAcquire(TimeSpan.FromMinutes(30) + TimeSpan.FromTicks(1), CancellationToken.None));
    }

    private static void RunOnOtherThread(Action action)
    {
        int callerThread = Environment.CurrentManagedThreadId;
        Exception? failure = null;
        Thread worker = new(() =>
        {
            try
            {
                Assert.AreNotEqual(callerThread, Environment.CurrentManagedThreadId);
                action();
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        worker.Start();
        Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(10)), "Fixture thread did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string ObserveFromSecondProcess(Guid id)
    {
        ProcessStartInfo start = new()
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "PCSteward.ProcessFixture.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("gate-contender");
        start.ArgumentList.Add(id.ToString("N"));
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Fixture did not start.");
        if (!process.WaitForExit(5000))
        {
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5000)) throw new TimeoutException("Fixture did not stop.");
            Assert.Fail("Fixture did not finish within five seconds.");
        }
        Assert.AreEqual(0, process.ExitCode);
        return process.StandardOutput.ReadToEnd().Trim();
    }
}
