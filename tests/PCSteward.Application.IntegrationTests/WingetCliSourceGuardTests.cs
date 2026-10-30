using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-01/15; TM-03/04/13: a fixed source name alone is not an effective-source observation.</summary>
[TestClass]
public sealed class WingetCliSourceGuardTests
{
    private static readonly string[] ExpectedArguments = ["source", "export", "--name", "winget", "--disable-interactivity"];
    private const string Export = """
        {"Arg":"https://cdn.winget.microsoft.com/cache","Data":"Microsoft.Winget.Source_8wekyb3d8bbwe","Explicit":false,"Identifier":"Microsoft.Winget.Source_8wekyb3d8bbwe","Name":"winget","TrustLevel":["Trusted","StoreOrigin"],"Type":"Microsoft.PreIndexed.Package"}
        """;

    [TestMethod]
    public async Task ExpectedSourceUsesOneFixedReadOnlyCommandAndTheRemainingBudget()
    {
        WingetProcessResult native = Result();
        FakeProcess process = new(native);
        WingetCliSourceObservation observation = await new WingetCliSourceGuard(process).ObserveAsync(TimeSpan.FromSeconds(7), CancellationToken.None);
        Assert.IsTrue(observation.IsExpected);
        Assert.IsTrue(observation.QueryRequested);
        Assert.AreSame(native.OwnedCompletion, observation.OwnedCompletion);
        CollectionAssert.AreEqual(ExpectedArguments, process.Arguments!.ToArray());
        Assert.AreEqual(TimeSpan.FromSeconds(7), process.Timeout);
        Assert.AreEqual(1, process.Calls);
        await new WingetCliSourceGuard(process).ObserveAsync(TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.AreEqual(TimeSpan.FromSeconds(20), process.Timeout);
    }

    [TestMethod]
    [DataRow("name")]
    [DataRow("url")]
    [DataRow("type")]
    [DataRow("data")]
    [DataRow("identifier")]
    [DataRow("explicit")]
    [DataRow("explicit-type")]
    [DataRow("unknown")]
    [DataRow("duplicate")]
    [DataRow("missing")]
    [DataRow("trust-missing")]
    [DataRow("trust-duplicate")]
    [DataRow("trust-unknown")]
    [DataRow("trust-type")]
    [DataRow("extra-record")]
    [DataRow("array")]
    [DataRow("malformed")]
    [DataRow("surrogate-name")]
    [DataRow("oversize")]
    [DataRow("utf8-oversize")]
    public async Task SubstitutedAmbiguousOrMalformedSourceExportsRefuse(string change)
    {
        string output = change switch
        {
            "name" => Export.Replace("\"winget\"", "\"other\"", StringComparison.Ordinal),
            "url" => Export.Replace("https://cdn.winget.microsoft.com/cache", "https://other.invalid/cache", StringComparison.Ordinal),
            "type" => Export.Replace("Microsoft.PreIndexed.Package", "Microsoft.Rest", StringComparison.Ordinal),
            "data" => Export.Replace("\"Data\":\"Microsoft.Winget.Source_8wekyb3d8bbwe\"", "\"Data\":\"other\"", StringComparison.Ordinal),
            "identifier" => Export.Replace("\"Identifier\":\"Microsoft.Winget.Source_8wekyb3d8bbwe\"", "\"Identifier\":\"other\"", StringComparison.Ordinal),
            "explicit" => Export.Replace("false", "true", StringComparison.Ordinal),
            "explicit-type" => Export.Replace("false", "\"false\"", StringComparison.Ordinal),
            "unknown" => Export.Insert(1, "\"Unknown\":true,"),
            "duplicate" => Export.Insert(1, "\"Name\":\"winget\","),
            "missing" => Export.Replace("\"Name\":\"winget\",", string.Empty, StringComparison.Ordinal),
            "trust-missing" => Export.Replace("\"Trusted\",", string.Empty, StringComparison.Ordinal),
            "trust-duplicate" => Export.Replace("\"StoreOrigin\"", "\"Trusted\"", StringComparison.Ordinal),
            "trust-unknown" => Export.Replace("\"StoreOrigin\"", "\"Other\"", StringComparison.Ordinal),
            "trust-type" => Export.Replace("[\"Trusted\",\"StoreOrigin\"]", "\"Trusted\"", StringComparison.Ordinal),
            "extra-record" => Export + "\n" + Export,
            "array" => "[" + Export + "]",
            "malformed" => "{",
            "surrogate-name" => "{\"\\uD800\":1}",
            "oversize" => Export.PadRight(4097),
            "utf8-oversize" => Export.Replace("\"winget\"", "\"" + new string('\u6f22', 1500) + "\"", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        WingetProcessResult native = Result(output);
        WingetCliSourceObservation observation = await new WingetCliSourceGuard(new FakeProcess(native)).ObserveAsync(TimeSpan.FromSeconds(20), CancellationToken.None);
        Assert.IsFalse(observation.IsExpected);
        Assert.IsTrue(observation.QueryRequested);
        Assert.AreSame(native.OwnedCompletion, observation.OwnedCompletion);
    }

    [TestMethod]
    [DataRow("exit")]
    [DataRow("timeout")]
    [DataRow("cancelled")]
    [DataRow("overflow")]
    [DataRow("incomplete")]
    [DataRow("uncertain")]
    [DataRow("pending")]
    [DataRow("faulted")]
    [DataRow("cancelled-root")]
    [DataRow("missing-root")]
    [DataRow("stderr")]
    public async Task NativeFailureOrUnsettledOwnershipCannotQualifyAndTheRootIsRetained(string change)
    {
        TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WingetProcessResult result = change switch
        {
            "exit" => Result() with { ExitCode = 1 },
            "timeout" => Result() with { TimedOut = true },
            "cancelled" => Result() with { Cancelled = true },
            "overflow" => Result() with { OutputLimitExceeded = true },
            "incomplete" => Result() with { OutputIncomplete = true },
            "uncertain" => Result() with { ExecutionUncertain = true },
            "pending" => Result() with { OwnedCompletion = pending.Task },
            "faulted" => Result() with { OwnedCompletion = Task.FromException(new IOException("synthetic exit observation failed")) },
            "cancelled-root" => Result() with { OwnedCompletion = Task.FromCanceled(new CancellationToken(canceled: true)) },
            "missing-root" => Result() with { OwnedCompletion = null },
            "stderr" => Result() with { StandardError = "unreviewed warning" },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        WingetCliSourceObservation observation = await new WingetCliSourceGuard(new FakeProcess(result))
            .ObserveAsync(TimeSpan.FromSeconds(20), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(observation.IsExpected);
        Assert.AreSame(result.OwnedCompletion, observation.OwnedCompletion);
        if (change == "pending") Assert.IsFalse(pending.Task.IsCompleted);
        if (change == "faulted") Assert.IsNotNull(result.OwnedCompletion!.Exception);
    }

    [TestMethod]
    public async Task ExpiredBudgetAndPrecancelledRequestsNeverCallTheProcess()
    {
        FakeProcess process = new(Result());
        WingetCliSourceGuard guard = new(process);
        WingetCliSourceObservation observation = await guard.ObserveAsync(TimeSpan.Zero, CancellationToken.None);
        Assert.IsFalse(observation.IsExpected || observation.QueryRequested);
        Assert.IsNull(observation.OwnedCompletion);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => guard.ObserveAsync(TimeSpan.FromSeconds(1), cancellation.Token));
        Assert.AreEqual(0, process.Calls);
    }

    [TestMethod]
    public async Task CancellationAfterTheQueryCannotQualifyEvenWithACompleteNativeResult()
    {
        using CancellationTokenSource cancellation = new();
        WingetProcessResult native = Result();
        FakeProcess process = new(native, cancellation.Cancel);
        WingetCliSourceObservation observation = await new WingetCliSourceGuard(process).ObserveAsync(TimeSpan.FromSeconds(20), cancellation.Token);
        Assert.IsFalse(observation.IsExpected);
        Assert.IsTrue(observation.QueryRequested);
        Assert.AreSame(native.OwnedCompletion, observation.OwnedCompletion);
        Assert.AreEqual(1, process.Calls);
    }

    private static WingetProcessResult Result(string? output = null) => new(0, output ?? Export, string.Empty, false, OwnedCompletion: Task.CompletedTask);
    private sealed class FakeProcess(WingetProcessResult result, Action? onRun = null) : IWingetProcess
    {
        internal IReadOnlyList<string>? Arguments;
        internal TimeSpan Timeout;
        internal int Calls;
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
        {
            Arguments = arguments.ToArray(); Timeout = timeout; Calls++;
            onRun?.Invoke();
            return Task.FromResult(result);
        }
    }
}
