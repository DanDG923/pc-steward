using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Application;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 N5 obligation (a), admission obligation 6 (portable; scripted client
/// process, no real client): the production intent resolver resolves the exact selected package only through the existing
/// connector's fixed <c>outdated</c> query, stamps its own time from the injected clock when resolution starts, and gives no
/// intent on every error, timeout, cancellation, missing, ambiguous, non-newer or non-exact candidate.
/// </summary>
[TestClass]
public sealed class ChocolateyReviewIntentResolverTests
{
    private const string Target = "git.install";
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 11, 59, 50, TimeSpan.Zero);

    [TestMethod]
    public async Task TheExactSelectedCandidateIsResolvedThroughTheConnectorWithTheResolversOwnTime()
    {
        ScriptedProcess process = new(_ => Result("git.install|1.0.0|2.0.0|false\nother.package|1.0|1.1|false\n"));
        ChocolateyResolvedIntent? resolved = await Resolver(process).ResolveAsync(Target, CancellationToken.None);

        Assert.IsNotNull(resolved);
        Assert.AreEqual(Now, resolved.ResolvedAt);
        Assert.AreEqual(new StructuredUpdateCandidateEvidence(ChocolateyUpdateSourceConnector.CommunitySourceId, Target, "1.0.0", "2.0.0"), resolved.Candidate);
        Assert.AreEqual(1, process.Calls.Count, "one client query per resolution");
        CollectionAssert.AreEqual(ChocolateyUpdateSourceConnector.BuildOutdatedArguments().ToArray(), process.Calls[0].ToArray(),
            "only the connector's fixed outdated arguments; the package ID is never passed to the client");
    }

    [TestMethod]
    public async Task TheDelegateResolvesTheSamePackageOnEveryCall()
    {
        ScriptedProcess process = new(_ => Result("git.install|1.0.0|2.0.0\n"));
        ChocolateyIntentResolver resolve = Resolver(process).For(Target);
        Assert.IsNotNull(await resolve(CancellationToken.None));
        Assert.IsNotNull(await resolve(CancellationToken.None));
        Assert.AreEqual(2, process.Calls.Count, "resolved afresh on every review, never cached");
    }

    [TestMethod]
    [DataRow("exit-code")]
    [DataRow("timed-out")]
    [DataRow("output-too-large")]
    [DataRow("malformed-row")]
    [DataRow("missing")]
    [DataRow("ambiguous")]
    [DataRow("not-newer")]
    [DataRow("older")]
    [DataRow("case-differs")]
    [DataRow("throws-io")]
    [DataRow("throws-unauthorized")]
    [DataRow("throws-cancelled")]
    public async Task EveryClientRefusalOrFaultGivesNoIntent(string kind)
    {
        ScriptedProcess process = new(_ => kind switch
        {
            "exit-code" => Result("git.install|1.0.0|2.0.0\n", exitCode: 1),
            "timed-out" => new ChocolateyProcessResult(-1, "git.install|1.0.0|2.0.0\n", string.Empty, TimedOut: true),
            "output-too-large" => new ChocolateyProcessResult(-1, "git.install|1.0.0|2.0.0\n", string.Empty, false, OutputLimitExceeded: true),
            "malformed-row" => Result("git.install|1.0.0|2.0.0\nbroken row\n"),
            "missing" => Result("other.package|1.0|1.1\n"),
            "ambiguous" => Result("git.install|1.0.0|2.0.0\nGIT.INSTALL|1.0.0|2.1.0\n"),
            "not-newer" => Result("git.install|2.0.0|2.0.0\n"),
            "older" => Result("git.install|2.0.0|1.0.0\n"),
            "case-differs" => Result("Git.Install|1.0.0|2.0.0\n"),
            "throws-io" => throw new IOException("client unavailable"),
            "throws-unauthorized" => throw new UnauthorizedAccessException("client access denied"),
            "throws-cancelled" => throw new OperationCanceledException(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        });
        Assert.IsNull(await Resolver(process).ResolveAsync(Target, CancellationToken.None), kind);
        Assert.AreEqual(1, process.Calls.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("-leading-dash")]
    [DataRow("space id")]
    [DataRow("semi;colon")]
    [DataRow("quote\"id")]
    public async Task AnUnsupportedPackageIdIsRefusedWithoutRunningTheClient(string packageId)
    {
        ScriptedProcess process = new(_ => Result("git.install|1.0.0|2.0.0\n"));
        Assert.IsNull(await Resolver(process).ResolveAsync(packageId, CancellationToken.None));
        Assert.IsNull(await Resolver(process).ResolveAsync(null!, CancellationToken.None));
        Assert.AreEqual(0, process.Calls.Count);
    }

    [TestMethod]
    public async Task CancellationBeforeOrDuringResolutionGivesNoIntent()
    {
        ScriptedProcess process = new(_ => Result("git.install|1.0.0|2.0.0\n"));
        using CancellationTokenSource before = new();
        before.Cancel();
        Assert.IsNull(await Resolver(process).ResolveAsync(Target, before.Token));
        Assert.AreEqual(0, process.Calls.Count, "a cancelled request never starts the client");

        using CancellationTokenSource during = new();
        ScriptedProcess cancelling = new(_ =>
        {
            during.Cancel();
            return Result("git.install|1.0.0|2.0.0\n");
        });
        Assert.IsNull(await Resolver(cancelling).ResolveAsync(Target, during.Token), "a result returned after cancellation is discarded");
    }

    [TestMethod]
    public async Task TheRealTimeCapStopsAHungClientAndGivesNoIntent()
    {
        ScriptedProcess hung = new(token =>
        {
            Task.Delay(Timeout.InfiniteTimeSpan, token).GetAwaiter().GetResult();
            return Result("git.install|1.0.0|2.0.0\n");
        });
        ChocolateyReviewIntentResolver resolver = ChocolateyReviewIntentResolver.CreateForTests(new ChocolateyUpdateSourceConnector(hung), new Clock(Now),
            TimeSpan.FromMilliseconds(100));
        Task<ChocolateyResolvedIntent?> resolution = resolver.ResolveAsync(Target, CancellationToken.None);
        Assert.AreSame(resolution, await Task.WhenAny(resolution, Task.Delay(TimeSpan.FromSeconds(30))), "the cap must end the call");
        Assert.IsNull(await resolution);
    }

    [TestMethod]
    public void TheProductionCapIsTheConnectorTimeoutPlusTheMargin()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(5), ChocolateyReviewIntentResolver.CapMargin);
        Assert.ThrowsExactly<ArgumentNullException>(() => new ChocolateyReviewIntentResolver(null!, TimeProvider.System));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ChocolateyReviewIntentResolver(new ChocolateyUpdateSourceConnector(new ScriptedProcess(_ => Result(""))), null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ChocolateyReviewIntentResolver.CreateForTests(new ChocolateyUpdateSourceConnector(new ScriptedProcess(_ => Result(""))), TimeProvider.System, TimeSpan.Zero));
    }

    private static ChocolateyReviewIntentResolver Resolver(ScriptedProcess process) =>
        new(new ChocolateyUpdateSourceConnector(process), new Clock(Now));

    private static ChocolateyProcessResult Result(string output, int exitCode = 0) => new(exitCode, output, string.Empty, false);

    /// <summary>Scripted client process; records each call's arguments and runs the script on the thread pool.</summary>
    private sealed class ScriptedProcess(Func<CancellationToken, ChocolateyProcessResult> script) : IChocolateyProcess
    {
        internal List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<ChocolateyProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add([.. arguments]);
            return Task.Run(() => script(cancellationToken), CancellationToken.None);
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
