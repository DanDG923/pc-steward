using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13; Task 50 design rev 6 §4 and §4.1 (rev-3 M-B), §8 N3 (portable): golden argument lists for the
/// three compiled commands, the exact-list golden against the protocol-1 literal, the existing budgets and the deadline cap,
/// and refusals of every mutated operation (including a recomputed digest). Nothing is launched.
/// </summary>
[TestClass]
public sealed class ChocolateyCompiledCommandTests
{
    private static readonly string[] ListGolden = ["list", "git.install", "--exact", "--limit-output", "--no-color"];
    private static readonly string[] OutdatedGolden = ["outdated", "--source", "https://community.chocolatey.org/api/v2/", "--ignore-unfound",
        "--limit-output", "--no-color", "--execution-timeout", "60"];
    private static readonly string[] UpgradeGolden = ["upgrade", "git.install", "--version", "2.0.0", "--source",
        "https://community.chocolatey.org/api/v2/", "--yes", "--limit-output", "--no-color", "--execution-timeout", "300",
        "--ignore-remembered-arguments", "--fail-on-not-installed", "--require-checksums"];

    [TestMethod]
    public void EachFactoryProducesItsGoldenImmutableArgumentListAndExistingBudget()
    {
        ReviewedUpdateOperation operation = N3Operations.Create();
        ChocolateyCompiledCommand.ListExact list = ChocolateyCompiledCommand.TryListExact(operation) ?? throw new AssertFailedException("list refused");
        ChocolateyCompiledCommand.Outdated outdated = ChocolateyCompiledCommand.CreateOutdated();
        ChocolateyCompiledCommand.Upgrade upgrade = ChocolateyCompiledCommand.TryUpgrade(operation) ?? throw new AssertFailedException("upgrade refused");

        CollectionAssert.AreEqual(ListGolden, list.Arguments.ToArray());
        CollectionAssert.AreEqual(OutdatedGolden, outdated.Arguments.ToArray());
        CollectionAssert.AreEqual(UpgradeGolden, upgrade.Arguments.ToArray());
        Assert.AreEqual("git.install", list.PackageId);
        Assert.AreEqual("git.install", upgrade.PackageId);
        Assert.AreEqual("2.0.0", upgrade.CandidateVersion);
        foreach (ChocolateyCompiledCommand command in new ChocolateyCompiledCommand[] { list, outdated, upgrade })
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)command.Arguments)[0] = "install");
        // The builders are the existing pinned ones.
        CollectionAssert.AreEqual(ChocolateyUpdateSourceConnector.BuildOutdatedArguments().ToArray(), outdated.Arguments.ToArray());
        CollectionAssert.AreEqual(ChocolateyReviewedUpdateArguments.Build(operation).ToArray(), upgrade.Arguments.ToArray());
        CollectionAssert.AreEqual(ChocolateyExactListArguments.Build(operation).ToArray(), list.Arguments.ToArray());

        // Existing item budgets (Profile V2: no new deadline).
        Assert.AreEqual(TimeSpan.FromSeconds(30), list.Budget);
        Assert.AreEqual(new ChocolateyUpdateSourceConnector().Timeout, outdated.Budget);
        TimeSpan executor = (TimeSpan)typeof(ChocolateyMachineUpdateExecutor).GetField("UpdateTimeout", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.AreEqual(executor, upgrade.Budget);
    }

    [TestMethod]
    public async Task TheExactListGoldenEqualsTheProtocolOnePostUpdateLiteralAndItsBudget()
    {
        ReviewedUpdateOperation operation = N3Operations.Create();
        CapturingProcess process = new();
        ChocolateyPostUpdatePackageInventory protocolOne = new(process);
        ImmutableUpdateJob job = N3Operations.Job("git.install", "1.0.0", "2.0.0");

        Assert.IsTrue(await protocolOne.HasExactExpectedVersionAsync(job, CancellationToken.None));

        CollectionAssert.AreEqual(process.Arguments!.ToArray(), ChocolateyExactListArguments.Build(operation).ToArray());
        CollectionAssert.AreEqual(ListGolden, process.Arguments!.ToArray());
        Assert.AreEqual(process.Timeout, ChocolateyCompiledCommand.TryListExact(operation)!.Budget);
        // The literal in source is the one pinned here.
        string provider = File.ReadAllText(Path.Combine(CompiledSourceRoot(), "src", "PCSteward.Provider.Chocolatey", "ChocolateyProvider.cs"));
        StringAssert.Contains(provider, "[\"list\", job.Selection.PackageId, \"--exact\", \"--limit-output\", \"--no-color\"]");
    }

    [TestMethod]
    public void TheLaunchDeadlineIsTheExistingBudgetCappedByRemaining()
    {
        ReviewedUpdateOperation operation = N3Operations.Create();
        ChocolateyCompiledCommand list = ChocolateyCompiledCommand.TryListExact(operation)!;
        ChocolateyCompiledCommand outdated = ChocolateyCompiledCommand.CreateOutdated();
        ChocolateyCompiledCommand upgrade = ChocolateyCompiledCommand.TryUpgrade(operation)!;

        Assert.AreEqual(TimeSpan.FromSeconds(30), LaunchJob.CapDeadline(list, TimeSpan.FromMinutes(11)));
        Assert.AreEqual(TimeSpan.FromSeconds(45), LaunchJob.CapDeadline(outdated, TimeSpan.FromMinutes(11)));
        Assert.AreEqual(TimeSpan.FromMinutes(10), LaunchJob.CapDeadline(upgrade, TimeSpan.FromMinutes(11)));
        Assert.AreEqual(TimeSpan.FromSeconds(7), LaunchJob.CapDeadline(list, TimeSpan.FromSeconds(7)));
        Assert.AreEqual(TimeSpan.FromSeconds(44), LaunchJob.CapDeadline(outdated, TimeSpan.FromSeconds(44)));
        Assert.AreEqual(TimeSpan.FromMinutes(9), LaunchJob.CapDeadline(upgrade, TimeSpan.FromMinutes(9)));
        Assert.AreEqual(TimeSpan.Zero, LaunchJob.CapDeadline(list, TimeSpan.Zero));
        Assert.AreEqual(TimeSpan.FromSeconds(-1), LaunchJob.CapDeadline(upgrade, TimeSpan.FromSeconds(-1)));
        Assert.AreEqual(TimeSpan.FromMinutes(10), LaunchJob.CapDeadline(upgrade, TimeSpan.MaxValue));
    }

    [TestMethod]
    [DataRow(9, "chocolatey", DisplayName = "client target")]
    [DataRow(9, "Chocolatey.Extension", DisplayName = "licensed extension target")]
    [DataRow(9, "a..b", DisplayName = "unsupported ID")]
    [DataRow(10, "1.0", DisplayName = "non-normalized installed")]
    [DataRow(11, "02.0.0", DisplayName = "non-normalized candidate")]
    [DataRow(11, "2.0.0-beta", DisplayName = "prerelease candidate")]
    [DataRow(11, "2.0.0+metadata", DisplayName = "metadata candidate")]
    [DataRow(11, "1.0.0", DisplayName = "equal candidate")]
    [DataRow(11, "0.9.0", DisplayName = "older candidate")]
    [DataRow(15, "Preview", DisplayName = "wrong channel")]
    [DataRow(15, "Unknown", DisplayName = "unknown channel")]
    [DataRow(21, "ChocolateyMachineRegistrationV3", DisplayName = "older route")]
    [DataRow(21, "ChocolateyMachineProvisionalV3", DisplayName = "provisional route")]
    public void ARecomputedDigestCannotMakeAMutatedOperationFormattableForAnyCommand(int field, string value)
    {
        ReviewedUpdateOperation changed = N3Operations.Mutate(lines => lines[field] = value);
        AssertRefusedEverywhere(changed);
    }

    [TestMethod]
    [DataRow(8, "WinGet", DisplayName = "wrong source")]
    [DataRow(12, "User", DisplayName = "wrong scope")]
    [DataRow(9, "pkg id", DisplayName = "ID with a space")]
    public void ASourceScopeOrIdSyntaxChangeIsAlreadyRefusedByTheDomainRestore(int field, string value)
    {
        // These never reach a command factory: the runtime-limit route is bound to Chocolatey Community machine scope.
        string[] lines = Encoding.UTF8.GetString(N3Operations.Create().GetCanonicalBytes()).Split('\n');
        lines[field] = value;
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out _));
    }

    [TestMethod]
    [DataRow("observed-architecture")]
    [DataRow("selected-mode")]
    [DataRow("winget-provider")]
    [DataRow("legacy-schema")]
    public void ARecomputedUnionModeOrProviderChangeIsRefusedByEveryCommand(string change)
    {
        ReviewedUpdateOperation changed = N3Operations.Mutate(lines =>
        {
            if (change == "observed-architecture") { lines[13] = "Observed"; lines[14] = "Neutral"; }
            if (change == "selected-mode") { lines[22] = "Selected"; lines[23] = "Silent"; lines[24] = "None"; }
            if (change == "winget-provider") { lines[7] = "WinGet"; lines[8] = "WinGet"; lines[12] = "User"; lines[21] = "WinGetUserExactV3"; }
        }, legacy: change == "legacy-schema");
        AssertRefusedEverywhere(changed);
    }

    [TestMethod]
    public void TheExactListBuilderSharesTheUpgradePredicatesAndRefusesAnOlderRouteOperation()
    {
        foreach (UpdateRouteContract route in new[] { UpdateRouteContract.ChocolateyMachineRegistrationV3, UpdateRouteContract.ChocolateyMachineProvisionalV3 })
        {
            ReviewedUpdateOperation operation = N3Operations.Create(route: route);
            Assert.ThrowsExactly<ArgumentException>(() => ChocolateyExactListArguments.Build(operation));
            Assert.IsNull(ChocolateyCompiledCommand.TryListExact(operation));
            Assert.IsNull(ChocolateyCompiledCommand.TryUpgrade(operation));
        }
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyExactListArguments.Build(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyCompiledCommand.TryListExact(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyCompiledCommand.TryUpgrade(null!));
    }

    [TestMethod]
    public void AWithExpressionCopiesTheFixedArgumentsAndCannotChangeThem()
    {
        ChocolateyCompiledCommand.ListExact list = ChocolateyCompiledCommand.TryListExact(N3Operations.Create())!;
        ChocolateyCompiledCommand.ListExact copy = list with { };
        CollectionAssert.AreEqual(list.Arguments.ToArray(), copy.Arguments.ToArray());
        Assert.AreEqual(list.PackageId, copy.PackageId);
        Assert.AreEqual(list.Budget, copy.Budget);
    }

    private static void AssertRefusedEverywhere(ReviewedUpdateOperation changed)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyExactListArguments.Build(changed));
        Assert.IsNull(ChocolateyCompiledCommand.TryListExact(changed));
        Assert.IsNull(ChocolateyCompiledCommand.TryUpgrade(changed));
    }

    private static string CompiledSourceRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class CapturingProcess : IChocolateyProcess
    {
        internal IReadOnlyList<string>? Arguments { get; private set; }
        internal TimeSpan Timeout { get; private set; }

        public Task<ChocolateyProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Arguments = [.. arguments];
            Timeout = timeout;
            return Task.FromResult(new ChocolateyProcessResult(0, "git.install|2.0.0\n", string.Empty, false));
        }
    }
}

/// <summary>
/// FR-UPD-15, TM-03/07/13; Task 50 design rev 6 §4 (M4, L2), portable: the holder under the binding releases only the code
/// handles, only once sealed, and keeps the engine and release-tree handles until disposed; the launch-path rule refuses a
/// root other than the observed one.
/// </summary>
[TestClass]
public sealed class ChocolateyHeldFilesTests
{
    [TestMethod]
    public void ReleaseCodeClosesOnlyTheCodeHandlesAndDisposeClosesTheRest()
    {
        string directory = Directory.CreateTempSubdirectory("pcsteward-n3-").FullName;
        try
        {
            using ChocolateyHeldProfileFiles files = ChocolateyHeldProfileFiles.Begin();
            ChocolateyHeldFile engine = Held(directory, "choco.exe");
            ChocolateyHeldFile helper = Held(directory, "helpers/a.psm1");
            ChocolateyHeldFile hook = Held(directory, @"hooks\a.ps1");
            files.AddRelease(engine, engine: true);
            files.AddRelease(helper, engine: false);
            files.AddCode(hook);
            Assert.ThrowsExactly<InvalidOperationException>(files.ReleaseCode, "an unsealed holder cannot release");
            files.Seal();
            Assert.IsFalse(files.CodeReleased);

            files.ReleaseCode();
            files.ReleaseCode();

            Assert.IsTrue(files.CodeReleased);
            Assert.IsTrue(hook.Handle.IsClosed);
            Assert.IsFalse(engine.Handle.IsClosed || helper.Handle.IsClosed, "the engine and release tree stay held");
            // The refused file is never taken by the holder, so the test closes its own handle (else the temp tree stays in use).
            ChocolateyHeldFile late = Held(directory, @"extensions\b.psm1");
            Assert.ThrowsExactly<InvalidOperationException>(() => files.AddCode(late));
            late.Handle.Dispose();
            files.Dispose();
            Assert.IsTrue(engine.Handle.IsClosed && helper.Handle.IsClosed && files.IsDisposed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void TheLaunchPathRuleRequiresTheObservedCanonicalRoot()
    {
        ChocolateyLaunchPaths paths = new(@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\a",
            @"C:\Users\a\AppData\Roaming", @"C:\Users\a\AppData\Local", @"C:\ProgramData\chocolatey", @"C:\ProgramData\cache");
        Assert.IsFalse(ChocolateyHeldClientBinding.IsLaunchable(paths, @"C:\ProgramData\chocolatey2"));
        Assert.IsFalse(ChocolateyHeldClientBinding.IsLaunchable(paths, @"C:\PROGRAMDATA\chocolatey"), "the observed root is compared exactly");
        Assert.IsFalse(ChocolateyHeldClientBinding.IsLaunchable(paths with { ChocolateyRoot = @"C:\ProgramData\x\..\chocolatey" },
            @"C:\ProgramData\x\..\chocolatey"));
        Assert.IsFalse(ChocolateyHeldClientBinding.IsLaunchable(paths with { ProtectedCache = @"C:\a;b" }, @"C:\ProgramData\chocolatey"));
    }

    private static ChocolateyHeldFile Held(string directory, string relative)
    {
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, relative);
        return new ChocolateyHeldFile(relative, new NativeNode(path, relative, false, new NativeFileIdentity(1, 1), relative.Length),
            File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }
}

/// <summary>Reviewed runtime-limit operations for the N3 tests (the accepted route shape, or a mutation with a recomputed digest).</summary>
internal static class N3Operations
{
    internal static readonly DateTimeOffset ReviewedAt = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    internal static ImmutableUpdateJob Job(string id, string installed, string candidate) =>
        new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:" + id, UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, id, installed, candidate, UpdateScope.Machine, UpdateArchitecture.Neutral,
            UpdateChannel.Stable, "mapping-v2")).Confirm(ReviewedAt);

    internal static ReviewedUpdateOperation Create(string id = "git.install", string installed = "1.0.0", string candidate = "2.0.0",
        UpdateRouteContract route = UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3) =>
        new(Job(id, installed, candidate), "S-1-5-21-11-22-33-1001", "choco:" + id, "machine:choco",
            ReviewedUpdateOperation.ProfileEvidencePrefix + new string('A', 64), ReviewedAt, route,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());

    /// <summary>Edits the canonical lines and restores them with a freshly computed digest (the restore must succeed).</summary>
    internal static ReviewedUpdateOperation Mutate(Action<string[]> change, bool legacy = false)
    {
        string[] lines = Encoding.UTF8.GetString(Create(route: legacy ? UpdateRouteContract.ChocolateyMachineRegistrationV3
            : UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3).GetCanonicalBytes()).Split('\n');
        change(lines);
        if (legacy)
        {
            lines[0] = "pc-steward-reviewed-update:2";
            lines[13] = "Neutral";
            lines[21] = nameof(UpdateRouteContract.ChocolateyMachineRegistrationV2);
            lines = [.. lines.Where((_, index) => index != 14)];
        }
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out ReviewedUpdateOperation? changed),
            "the mutated operation must restore so that only the command factories can refuse it");
        return changed!;
    }
}
