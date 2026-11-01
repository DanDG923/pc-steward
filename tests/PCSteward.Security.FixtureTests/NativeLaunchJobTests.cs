using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/07/13; Task 50 design rev 5 §4.1 and §8 N0: the job-object launcher against real fixture process
/// trees (<c>cmd.exe</c> and <c>PING.EXE</c> from the system directory). Every test is Windows-only and is reported as
/// failed on other platforms. Grandchildren write a late marker file only if they survive, which proves the job killed
/// them independently of the launcher's own settlement report.
/// </summary>
[TestClass]
public sealed class NativeLaunchJobTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(60);
    // Scheduling slack for CI hosts: process start-up, poll slices and the final handle close.
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(4);

    [TestMethod]
    public async Task ACompletedChildSettlesWithItsExitCodeAndBothStreams()
    {
        RequireWindows("job-object launch of a real child");
        using LaunchFixture fixture = new();
        Stopwatch clock = Stopwatch.StartNew();

        NativeProcessObservation observation = await fixture.RunCmdAsync(Long, CancellationToken.None,
            "echo", "hello&", "echo", "oops", "1>&2&", "exit", "7");

        Assert.AreEqual(NativeInvocation.Started, observation.Invocation);
        Assert.AreEqual(7, observation.ExitCode);
        Assert.IsTrue(observation.DirectExitObserved && observation.OwnedIoCompleted && observation.PipeEofObserved);
        Assert.IsTrue(observation.AggregateSettled);
        Assert.IsFalse(observation.TerminationFailed || observation.Cancelled || observation.TimedOut ||
            observation.OutputLimitExceeded || observation.LingeringTerminated);
        Assert.AreEqual("hello", observation.StandardOutput.Trim());
        Assert.AreEqual("oops", observation.StandardError.Trim());
        Assert.IsTrue(clock.Elapsed < LaunchJob.SettleWindow, "a settled tree must not wait for the settle window");
    }

    [TestMethod]
    [DataRow("cancel")]
    [DataRow("deadline")]
    public async Task CancellationAndTheDeadlineKillTheChildAndItsGrandchild(string trigger)
    {
        RequireWindows("job-object teardown of a real process tree");
        using LaunchFixture fixture = new();
        using CancellationTokenSource cancellation = new();
        TimeSpan deadline = trigger == "deadline" ? TimeSpan.FromSeconds(3) : Long;
        if (trigger == "cancel") cancellation.CancelAfter(TimeSpan.FromSeconds(3));
        Stopwatch clock = Stopwatch.StartNew();

        NativeProcessObservation observation = await fixture.RunCmdAsync(deadline, cancellation.Token,
            [.. LaunchFixture.LateGrandchild(pingCount: 11), "&", LaunchFixture.Ping, "-n", "60", "127.0.0.1", ">nul"]);

        Assert.AreEqual(NativeInvocation.Started, observation.Invocation);
        Assert.AreEqual(trigger == "cancel", observation.Cancelled);
        Assert.AreEqual(trigger == "deadline", observation.TimedOut);
        Assert.IsTrue(observation.AggregateSettled, "zero active processes after TerminateJobObject");
        Assert.IsFalse(observation.TerminationFailed || observation.LingeringTerminated || observation.OutputLimitExceeded);
        Assert.IsTrue(observation.DirectExitObserved && observation.OwnedIoCompleted && observation.PipeEofObserved);
        Assert.AreEqual((int)LaunchJob.TerminationExitCode, observation.ExitCode);
        StringAssert.Contains(observation.StandardOutput, "grandchild-started");
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(3) + LaunchJob.TerminationBudget + Slack, clock.Elapsed.ToString());

        await WaitUntilAsync(clock, TimeSpan.FromSeconds(14));
        Assert.IsFalse(File.Exists(fixture.PathOf("late.txt")), "the grandchild survived the job teardown");
    }

    [TestMethod]
    public async Task ALingeringGrandchildDelaysSettlementUntilZeroAndIsKilledAfterTheSettleWindow()
    {
        RequireWindows("settle window and lingering-descendant teardown");
        using LaunchFixture fixture = new();
        Stopwatch clock = Stopwatch.StartNew();

        NativeProcessObservation observation = await fixture.RunCmdAsync(Long, CancellationToken.None,
            [.. LaunchFixture.LateGrandchild(pingCount: 16), "&", "echo", "primary-done"]);

        Assert.AreEqual(NativeInvocation.Started, observation.Invocation);
        Assert.AreEqual(0, observation.ExitCode, "the primary exited normally before the window");
        Assert.IsTrue(observation.DirectExitObserved);
        // The primary exit and its pipes are not enough: settlement waited for zero active processes.
        Assert.IsTrue(clock.Elapsed >= LaunchJob.SettleWindow - TimeSpan.FromMilliseconds(250), clock.Elapsed.ToString());
        Assert.IsTrue(observation.LingeringTerminated);
        Assert.IsTrue(observation.AggregateSettled);
        Assert.IsFalse(observation.TerminationFailed || observation.Cancelled || observation.TimedOut);
        StringAssert.Contains(observation.StandardOutput, "grandchild-started");
        StringAssert.Contains(observation.StandardOutput, "primary-done");
        Assert.IsTrue(clock.Elapsed < LaunchJob.SettleWindow + LaunchJob.TerminationBudget + Slack, clock.Elapsed.ToString());

        await WaitUntilAsync(clock, TimeSpan.FromSeconds(19));
        Assert.IsFalse(File.Exists(fixture.PathOf("late.txt")), "the lingering grandchild survived");
    }

    [TestMethod]
    public async Task TheSettleWindowIsClippedToTheLaunchDeadline()
    {
        RequireWindows("settle window clipped by the deadline (rev-4 L4)");
        using LaunchFixture fixture = new();
        Stopwatch clock = Stopwatch.StartNew();

        NativeProcessObservation observation = await fixture.RunCmdAsync(TimeSpan.FromSeconds(4), CancellationToken.None,
            [.. LaunchFixture.LateGrandchild(pingCount: 16), "&", "echo", "primary-done"]);

        Assert.AreEqual(0, observation.ExitCode);
        Assert.IsTrue(observation.TimedOut, "the clipped window ended at the launch deadline");
        Assert.IsTrue(observation.LingeringTerminated && observation.AggregateSettled);
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(4) + LaunchJob.TerminationBudget + Slack, clock.Elapsed.ToString());
    }

    [TestMethod]
    public async Task EachLaunchHasItsOwnJobAndCompletionPort()
    {
        RequireWindows("independent per-launch jobs");
        using LaunchFixture fixture = new();
        using CancellationTokenSource cancelFirst = new();
        Task<NativeProcessObservation> first = fixture.RunCmdAsync(Long, cancelFirst.Token, LaunchFixture.Ping, "-n", "60", "127.0.0.1", ">nul");
        Stopwatch secondClock = Stopwatch.StartNew();
        Task<NativeProcessObservation> second = fixture.RunCmdAsync(Long, CancellationToken.None, LaunchFixture.Ping, "-n", "4", "127.0.0.1");
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await cancelFirst.CancelAsync();

        NativeProcessObservation cancelled = await first;
        NativeProcessObservation completed = await second;
        TimeSpan secondElapsed = secondClock.Elapsed;

        Assert.IsTrue(cancelled.Cancelled && cancelled.AggregateSettled);
        // The first job's teardown and its active-process-zero message never reach the second launch.
        Assert.AreEqual(0, completed.ExitCode);
        Assert.IsTrue(completed.AggregateSettled);
        Assert.IsFalse(completed.Cancelled || completed.TimedOut || completed.LingeringTerminated || completed.TerminationFailed);
        StringAssert.Contains(completed.StandardOutput, "127.0.0.1");
        Assert.IsTrue(secondElapsed >= TimeSpan.FromSeconds(2.5), secondElapsed.ToString());

        NativeProcessObservation third = await fixture.RunCmdAsync(Long, CancellationToken.None, "exit", "3");
        Assert.AreEqual(3, third.ExitCode);
        Assert.IsTrue(third.AggregateSettled && !third.LingeringTerminated);
    }

    [TestMethod]
    public async Task AMissingImageIsNotStarted()
    {
        RequireWindows("CreateProcessW failure");
        using LaunchFixture fixture = new();
        NativeLaunchSpec spec = NativeLaunchSpec.ForTests(fixture.PathOf($"missing-{Guid.NewGuid():N}.exe"), [],
            fixture.Environment, fixture.Directory);

        NativeProcessObservation observation = await LaunchJob.RunCoreAsync(spec, Long, CancellationToken.None);

        AssertNotStarted(observation);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AnInheritableHandleOutsideTheHandleListIsNotInherited()
    {
        RequireWindows("PROC_THREAD_ATTRIBUTE_HANDLE_LIST");
        using LaunchFixture fixture = new();
        // The client (write) end of this pipe is inheritable. If the child inherited it, the pipe could not reach end
        // of file while the child runs.
        using AnonymousPipeServerStream probe = new(PipeDirection.In, HandleInheritability.Inheritable);
        Task<NativeProcessObservation> run = fixture.RunCmdAsync(Long, CancellationToken.None,
            "echo", "x>started.txt&", LaunchFixture.Ping, "-n", "8", "127.0.0.1", ">nul");
        Assert.IsTrue(await WaitForFileAsync(fixture.PathOf("started.txt"), TimeSpan.FromSeconds(15)), "the child did not start");

        probe.DisposeLocalCopyOfClientHandle();
        Task<int> read = Task.Run(() => probe.Read(new byte[1], 0, 1));
        Task first = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.AreSame(read, first, "a child held an inherited copy of the test handle");
        Assert.AreEqual(0, await read);
        Assert.IsFalse(run.IsCompleted, "the child was still running when the probe reached end of file");
        NativeProcessObservation observation = await run;
        Assert.AreEqual(0, observation.ExitCode);
        Assert.IsTrue(observation.AggregateSettled);
    }

    [TestMethod]
    public async Task OutputBeyondTheLimitIsReportedAndTheTreeIsTornDown()
    {
        RequireWindows("bounded output capture");
        using LaunchFixture fixture = new();

        NativeProcessObservation observation = await fixture.RunCmdAsync(Long, CancellationToken.None,
            "for", "/l", "%i", "in", "(1,1,30000)", "do", "@echo", "0123456789012345678901234567890123456789");

        Assert.AreEqual(NativeInvocation.Started, observation.Invocation);
        Assert.IsTrue(observation.OutputLimitExceeded);
        Assert.IsTrue(observation.StandardOutput.Length + observation.StandardError.Length <= LaunchJob.OutputLimitCharacters);
        Assert.IsFalse(observation.TerminationFailed);
        Assert.IsTrue(observation.AggregateSettled, "the readers keep draining after the overflow, so EOF follows teardown");
        Assert.IsFalse(observation.LingeringTerminated);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheChildEnvironmentIsExactlyTheSpecEnvironment()
    {
        RequireWindows("CREATE_UNICODE_ENVIRONMENT block");
        using LaunchFixture fixture = new();
        const string Sentinel = "PCSTEWARD_N0_PARENT_SENTINEL";
        Environment.SetEnvironmentVariable(Sentinel, Guid.NewGuid().ToString("N"));
        try
        {
            Dictionary<string, string> environment = new(fixture.Environment, StringComparer.OrdinalIgnoreCase)
            {
                ["PCSTEWARD_N0_VALUE"] = "a b=c;d"
            };
            NativeLaunchSpec spec = NativeLaunchSpec.ForTests(LaunchFixture.Cmd, ["/d", "/c", "set"], environment, fixture.Directory);

            NativeProcessObservation observation = await LaunchJob.RunCoreAsync(spec, Long, CancellationToken.None);

            Assert.AreEqual(0, observation.ExitCode);
            string[] printed = observation.StandardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
            string[] expected = environment.Select(pair => $"{pair.Key}={pair.Value}").ToArray();
            CollectionAssert.AreEquivalent(expected, printed, observation.StandardOutput);
            Assert.IsFalse(observation.StandardOutput.Contains(Sentinel, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Sentinel, null);
        }
    }

    [TestMethod]
    [DataRow("assign")]
    [DataRow("resume")]
    public async Task AnAssignOrResumeFailureIsNotStartedAfterConfirmedTermination(string step)
    {
        LaunchJobFault fault = step == "assign" ? LaunchJobFault.AssignFails : LaunchJobFault.ResumeFails;
        RequireWindows("suspended process termination (L1)");
        using LaunchFixture fixture = new();
        NativeLaunchSpec spec = NativeLaunchSpec.ForTests(LaunchFixture.Cmd, ["/d", "/c", "echo", "ran>ran.txt"],
            fixture.Environment, fixture.Directory);

        NativeProcessObservation observation = await LaunchJob.RunCoreWithFaultForTestsAsync(spec, Long, fault, CancellationToken.None);

        AssertNotStarted(observation);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.IsFalse(File.Exists(fixture.PathOf("ran.txt")), "the never-resumed process ran user code");
    }

    [TestMethod]
    public async Task ATeardownFailureIsReportedAsTerminationFailedAndUnsettled()
    {
        RequireWindows("teardown failure and kill-on-close backstop");
        using LaunchFixture fixture = new();
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(1));
        NativeLaunchSpec spec = NativeLaunchSpec.ForTests(LaunchFixture.Cmd,
            ["/d", "/c", LaunchFixture.Ping, "-n", "13", "127.0.0.1", ">nul&", "echo", "late>late.txt"], fixture.Environment, fixture.Directory);
        Stopwatch clock = Stopwatch.StartNew();

        NativeProcessObservation observation = await LaunchJob.RunCoreWithFaultForTestsAsync(spec, Long,
            LaunchJobFault.TeardownFails, cancellation.Token);

        Assert.AreEqual(NativeInvocation.Started, observation.Invocation);
        Assert.IsTrue(observation.Cancelled);
        Assert.IsTrue(observation.TerminationFailed);
        Assert.IsFalse(observation.AggregateSettled, "active processes never reached zero");
        Assert.IsFalse(observation.DirectExitObserved);
        Assert.IsNull(observation.ExitCode);
        Assert.IsFalse(observation.LingeringTerminated);
        Assert.IsTrue(clock.Elapsed >= LaunchJob.TerminationBudget, clock.Elapsed.ToString());

        // Closing the job handle is the kill-on-close backstop, never the settlement step.
        await WaitUntilAsync(clock, TimeSpan.FromSeconds(16));
        Assert.IsFalse(File.Exists(fixture.PathOf("late.txt")), "kill-on-close did not end the tree");
    }

    private static void AssertNotStarted(NativeProcessObservation observation)
    {
        Assert.AreEqual(NativeInvocation.NotStarted, observation.Invocation);
        Assert.IsNull(observation.ExitCode);
        Assert.IsFalse(observation.DirectExitObserved || observation.OwnedIoCompleted || observation.PipeEofObserved);
        Assert.IsTrue(observation.AggregateSettled, "nothing ran, so nothing is left to settle");
        Assert.IsFalse(observation.TerminationFailed || observation.Cancelled || observation.TimedOut || observation.LingeringTerminated);
        Assert.AreEqual(string.Empty, observation.StandardOutput);
        Assert.AreEqual(string.Empty, observation.StandardError);
    }

    private static async Task WaitUntilAsync(Stopwatch clock, TimeSpan target)
    {
        TimeSpan left = target - clock.Elapsed;
        if (left > TimeSpan.Zero) await Task.Delay(left);
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(50);
        }
        return File.Exists(path);
    }

    private static void RequireWindows(string evidence)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException($"Windows-native test ({evidence}): it runs on Windows CI and is reported as failed on other platforms.");
    }

    private sealed class LaunchFixture : IDisposable
    {
        internal LaunchFixture()
        {
            Directory = Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), $"pcsteward-n0-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
            string windows = Path.GetDirectoryName(System32) ?? throw new InvalidOperationException("No Windows directory.");
            // Includes the names cmd.exe would otherwise add itself, so the child environment can equal the spec.
            Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SystemRoot"] = windows, ["windir"] = windows, ["ComSpec"] = Cmd, ["PATH"] = $"{System32};{windows}",
                ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD", ["PROMPT"] = "$P$G", ["TEMP"] = Directory, ["TMP"] = Directory
            };
        }

        internal static string System32 => System.Environment.SystemDirectory;
        internal static string Cmd => Path.Combine(System32, "cmd.exe");
        internal static string Ping => Path.Combine(System32, "PING.EXE");
        internal string Directory { get; }
        internal IReadOnlyDictionary<string, string> Environment { get; }

        internal string PathOf(string name) => Path.Combine(Directory, name);

        /// <summary>A detached grandchild that prints a marker, sleeps, then writes late.txt unless it is killed.</summary>
        internal static string[] LateGrandchild(int pingCount) =>
            ["start", "/b", Cmd, "/d", "/c", $"echo grandchild-started& {Ping} -n {pingCount} 127.0.0.1 >nul& echo late>late.txt"];

        internal Task<NativeProcessObservation> RunCmdAsync(TimeSpan deadline, CancellationToken cancellationToken,
            params string[] arguments) =>
            LaunchJob.RunCoreAsync(NativeLaunchSpec.ForTests(Cmd, ["/d", "/c", .. arguments], Environment, Directory),
                deadline, cancellationToken);

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
