using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03, TM-03/04/13. Phase D stage D4b, stage 2b (design §5 H6 and §7; owner answers Q1 and Q3, 2026-11-12):
/// a whole-PC WinGet review binds Silent, the one HKLM registration's digest from fresh inventory and the installer evidence of the
/// review's own <c>winget download</c>; Confirm checks the registration again and freezes the whole-PC operation, which the review
/// gate still refuses, so nothing reaches an executor. The installer evidence source checks the source, downloads into a fresh folder
/// under PC Steward's download cache, reads it strictly and deletes it.
/// </summary>
[TestClass]
[TestCategory("FR-UPD-15")]
public sealed class WingetMachineReviewEvidenceTests
{
    private const string Sid = "S-1-5-21-11-22-33-1001";
    private const string KeyName = "{23170F69-40C1-2702-2501-000001000000}";
    private const string SourceExport = """
        {"Arg":"https://cdn.winget.microsoft.com/cache","Data":"Microsoft.Winget.Source_8wekyb3d8bbwe","Explicit":false,"Identifier":"Microsoft.Winget.Source_8wekyb3d8bbwe","Name":"winget","TrustLevel":["Trusted","StoreOrigin"],"Type":"Microsoft.PreIndexed.Package"}
        """;

    private static readonly string[] SourceArguments = ["source", "export", "--name", "winget", "--disable-interactivity"];
    private static readonly ReviewedInstallerMode Silent = ReviewedInstallerMode.Selected(InstallerMode.Silent);
    private static readonly string ManifestDigest = new('B', 64);
    private static readonly string InstallerSha = new('C', 64);
    private static readonly DateTimeOffset ObservedAt = new(2026, 11, 12, 22, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProductId = Guid.Parse("d4b20000-0000-0000-0000-000000000001");

    [TestMethod]
    public void TheRegistrationDigestIsTheOneMachineRegistryInstanceOfTheProduct()
    {
        UpdateJobSelection selection = Selection();
        Assert.AreEqual(WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine64, KeyName, "24.09"),
            WingetMachineReviewedRoute.RegistrationDigest(selection, Observation()));
        Assert.AreEqual(WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine32, KeyName, "24.09"),
            WingetMachineReviewedRoute.RegistrationDigest(Selection(architecture: UpdateArchitecture.X86), Observation(architecture: "x86")));

        Assert.IsNull(WingetMachineReviewedRoute.RegistrationDigest(selection, null), "no fresh inventory");
        Assert.IsNull(WingetMachineReviewedRoute.RegistrationDigest(selection, Observation(extra: Instance(key: "other"))), "two registrations");
        Assert.IsNull(WingetMachineReviewedRoute.RegistrationDigest(selection, Observation(architecture: "x86")), "other view");
        Assert.IsNull(WingetMachineReviewedRoute.RegistrationDigest(selection, Observation(scope: "User")), "per-user registration");
        Assert.IsNull(WingetMachineReviewedRoute.RegistrationDigest(selection, Observation(provider: "winget")), "not the registry");
        Assert.IsNull(WingetMachineReviewedRoute.RegistrationDigest(selection, Observation(productKey: "other")), "other product");
        Assert.IsNull(WingetMachineReviewedRoute.RegistrationDigest(Selection(architecture: UpdateArchitecture.Arm64), Observation()), "arm64");
        Assert.IsNull(WingetMachineReviewedRoute.RegistrationDigest(Selection(source: UpdateSource.MicrosoftStore), Observation()), "msstore");
        Assert.AreEqual(WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine64, KeyName, "24.09"),
            WingetMachineReviewedRoute.RegistrationDigest(selection, Observation(extra: Instance(scope: "User"))), "a per-user copy is not a match");
    }

    [TestMethod]
    public void OnlyTheRouteShapeBindsSilent()
    {
        Assert.AreEqual(Silent, WingetMachineReviewedRoute.BindMode(Selection()));
        Assert.AreEqual(Silent, WingetMachineReviewedRoute.BindMode(Selection(channel: UpdateChannel.Stable)));
        Assert.IsNull(WingetMachineReviewedRoute.BindMode(Selection(source: UpdateSource.MicrosoftStore)));
        Assert.IsNull(WingetMachineReviewedRoute.BindMode(Selection(scope: UpdateScope.User)));
        Assert.IsNull(WingetMachineReviewedRoute.BindMode(Selection(channel: UpdateChannel.Preview)));
        Assert.IsNull(WingetMachineReviewedRoute.BindMode(Selection(architecture: UpdateArchitecture.Neutral)));
    }

    [TestMethod]
    public void FreezeBuildsTheRouteShapeButTheReviewGateRefusesItUntilStageThree()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection(), Silent).Confirm(ObservedAt);
        string evidence = EvidenceId();
        ReviewedUpdateOperation shaped = new(job, Sid, job.Selection.StableProductKey, WingetMachineReviewedRoute.InstallationContextId,
            evidence, ObservedAt, UpdateRouteContract.WinGetMachineSilentV1, Silent);
        Assert.IsTrue(WingetMachineReviewedRoute.TryGetShape(shaped, out _, out _, out _));
        Assert.IsFalse(WingetMachineReviewedRoute.Admits(shaped));
        Assert.IsNull(WingetMachineReviewedRoute.Freeze(job, Silent, Sid, evidence, ObservedAt), "not admitted before stage 3");
        Assert.IsNull(WingetMachineReviewedRoute.Freeze(job, null, Sid, evidence, ObservedAt));
        Assert.IsNull(WingetMachineReviewedRoute.Freeze(job, Silent, null, evidence, ObservedAt));
        Assert.IsNull(WingetMachineReviewedRoute.Freeze(job, Silent, Sid, null, ObservedAt));
        Assert.IsNull(WingetMachineReviewedRoute.Freeze(job, Silent, Sid, evidence, null));
        Assert.IsNull(WingetMachineReviewedRoute.Freeze(job, Silent, Sid, "wgm1:msi:short", ObservedAt));
    }

    [TestMethod]
    public async Task AWholePcReviewBindsTheEvidenceAndConfirmStartsNothingWhileTheRouteIsOff()
    {
        FakeEvidence evidence = new(new(new(WinGetMachineInstallerType.Msi, ManifestDigest, InstallerSha), "checked"));
        Harness harness = new(evidence);

        UpdateReviewPreparation preparation = await harness.Workflow.BeginReviewAsync(Selection().StableProductKey, CancellationToken.None);

        UpdateJobReview review = preparation.Review!;
        Assert.AreEqual(Silent, review.InstallerMode);
        Assert.AreEqual(EvidenceId(), review.EvidenceId);
        Assert.AreEqual(ObservedAt, review.EvidenceObservedAt);
        Assert.AreEqual(1, evidence.Calls);

        ReviewedUpdateExecutionResult result = await harness.Workflow.ConfirmAndRunAsync(review, false, CancellationToken.None);
        Assert.IsFalse(result.Started);
        Assert.AreEqual(WingetMachineReviewedRoute.NotOnThisRouteMessage, result.Message);
        Assert.HasCount(0, harness.Executor.Calls, "nothing reaches an executor, and never protocol 1");
    }

    [TestMethod]
    public async Task TheReviewRefusesWithTheReasonAndChecksTheRegistrationFirst()
    {
        FakeEvidence refusing = new(new(null, WingetMachineInstallerEvidenceSource.DependenciesMessage));
        UpdateReviewPreparation refused = await new Harness(refusing).Workflow.BeginReviewAsync(Selection().StableProductKey, CancellationToken.None);
        Assert.IsNull(refused.Review);
        Assert.AreEqual(WingetMachineInstallerEvidenceSource.DependenciesMessage, refused.Message);

        FakeEvidence unused = new(new(new(WinGetMachineInstallerType.Msi, ManifestDigest, InstallerSha), "checked"));
        UpdateReviewPreparation noRegistration = await new Harness(unused, Observation(provider: "winget")).Workflow
            .BeginReviewAsync(Selection().StableProductKey, CancellationToken.None);
        Assert.IsNull(noRegistration.Review);
        Assert.AreEqual(WingetMachineReviewedRoute.RegistrationNotFoundMessage, noRegistration.Message);
        Assert.AreEqual(0, unused.Calls, "no download without the registration");

        UpdateReviewPreparation store = await new Harness(unused, selection: Selection(source: UpdateSource.MicrosoftStore)).Workflow
            .BeginReviewAsync(Selection().StableProductKey, CancellationToken.None);
        Assert.IsNull(store.Review);
        Assert.AreEqual(WingetMachineReviewedRoute.NotOnThisRouteMessage, store.Message);
        Assert.AreEqual(0, unused.Calls);
    }

    [TestMethod]
    public async Task ConfirmRefusesAChangedRegistrationOrAnUnknownRequesterBeforeApproval()
    {
        FakeEvidence evidence = new(new(new(WinGetMachineInstallerType.Msi, ManifestDigest, InstallerSha), "checked"));
        Harness moved = new(evidence, Observation(), Observation(version: "24.08"));
        UpdateJobReview review = (await moved.Workflow.BeginReviewAsync(Selection().StableProductKey, CancellationToken.None)).Review!;
        ReviewedUpdateExecutionResult result = await moved.Workflow.ConfirmAndRunAsync(review, false, CancellationToken.None);
        Assert.IsFalse(result.Started);
        Assert.AreEqual(WingetMachineReviewedRoute.RegistrationChangedMessage, result.Message);
        Assert.HasCount(0, moved.Store.Jobs, "nothing was approved");

        Harness anonymous = new(evidence, sid: () => null);
        review = (await anonymous.Workflow.BeginReviewAsync(Selection().StableProductKey, CancellationToken.None)).Review!;
        result = await anonymous.Workflow.ConfirmAndRunAsync(review, false, CancellationToken.None);
        Assert.AreEqual(WingetUserReviewedRoute.RequesterUnavailableMessage, result.Message);
        Assert.HasCount(0, anonymous.Store.Jobs);
        Assert.HasCount(0, anonymous.Executor.Calls);
    }

    [TestMethod]
    public async Task TheEvidenceSourceDownloadsTheExactPackageIntoAFreshFolderAndDeletesIt()
    {
        using TempRoot root = new();
        byte[] installer = "fixture installer"u8.ToArray();
        ScriptedProcess process = new(installer, Manifest(Sha(installer)));
        WingetMachineInstallerCheck check = await new WingetMachineInstallerEvidenceSource(process, () => root.Path)
            .CheckAsync(Selection(), CancellationToken.None);

        Assert.IsNotNull(check.Evidence, check.Message);
        Assert.AreEqual(WinGetMachineInstallerType.Msi, check.Evidence.InstallerType);
        Assert.AreEqual(Sha(installer), check.Evidence.InstallerSha256);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Manifest(Sha(installer)))), check.Evidence.ManifestDigest);
        Assert.HasCount(2, process.Calls);
        CollectionAssert.AreEqual(SourceArguments, process.Calls[0].ToArray());
        string folder = process.Calls[1][13];
        CollectionAssert.AreEqual(new[]
        {
            "download", "--id", "7zip.7zip", "--version", "25.01", "--source", "winget", "--exact", "--scope", "machine",
            "--architecture", "x64", "--download-directory", folder, "--disable-interactivity"
        }, process.Calls[1].ToArray());
        Assert.AreEqual(root.Path, Path.GetDirectoryName(folder));
        Assert.AreEqual(32, Path.GetFileName(folder).Length);
        Assert.IsFalse(Directory.Exists(folder), "the review folder is deleted");
    }

    [TestMethod]
    public async Task TheEvidenceSourceRefusesWithTheReason()
    {
        using TempRoot root = new();
        byte[] installer = "fixture installer"u8.ToArray();
        string sha = Sha(installer);

        async Task<WingetMachineInstallerCheck> Check(ScriptedProcess process) =>
            await new WingetMachineInstallerEvidenceSource(process, () => root.Path).CheckAsync(Selection(), CancellationToken.None);

        Assert.AreEqual(WingetMachineInstallerEvidenceSource.DependenciesMessage,
            (await Check(new(installer, Manifest(sha, "  Dependencies:\n    PackageDependencies:\n    - PackageIdentifier: Example.Runtime\n")))).Message);
        Assert.AreEqual(WingetMachineInstallerEvidenceSource.NoMachineInstallerMessage,
            (await Check(new(installer, Manifest(sha, scope: null)))).Message);
        Assert.AreEqual(WingetMachineInstallerEvidenceSource.NoMachineInstallerMessage,
            (await Check(new(installer, Manifest(sha, scope: "user")))).Message);
        Assert.AreEqual(WingetMachineInstallerEvidenceSource.InstallerTypeMessage,
            (await Check(new(installer, Manifest(sha, type: "portable")))).Message);
        Assert.AreEqual(WingetMachineInstallerEvidenceSource.NotCheckedMessage,
            (await Check(new(installer, Manifest(new string('0', 64))))).Message, "installer hash differs from its manifest");
        Assert.AreEqual(WingetMachineInstallerEvidenceSource.NotCheckedMessage,
            (await Check(new(installer, Manifest(sha)) { DownloadExitCode = unchecked((int)0x8A150049) })).Message, "download failed");

        ScriptedProcess untrusted = new(installer, Manifest(sha)) { Export = SourceExport.Replace("cdn.winget", "example", StringComparison.Ordinal) };
        Assert.AreEqual(WingetMachineInstallerEvidenceSource.NotCheckedMessage, (await Check(untrusted)).Message);
        Assert.HasCount(1, untrusted.Calls, "no download after an untrusted source");

        ScriptedProcess unused = new(installer, Manifest(sha));
        WingetMachineInstallerCheck store = await new WingetMachineInstallerEvidenceSource(unused, () => root.Path)
            .CheckAsync(Selection(source: UpdateSource.MicrosoftStore), CancellationToken.None);
        Assert.AreEqual(WingetMachineReviewedRoute.NotOnThisRouteMessage, store.Message);
        Assert.HasCount(0, unused.Calls);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(root.Path), "every review folder was deleted");
    }

    private static string EvidenceId() =>
        $"wgm1:msi:{WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine64, KeyName, "24.09")}:{ManifestDigest}:{InstallerSha}";

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static byte[] Manifest(string sha, string extra = "", string? scope = "machine", string type = "msi")
    {
        StringBuilder text = new();
        text.Append("PackageIdentifier: 7zip.7zip\r\nPackageVersion: 25.01\r\nPackageLocale: en-US\r\nPackageName: 7-Zip\r\nInstallers:\r\n");
        text.Append("- Architecture: x64\r\n").Append("  InstallerType: ").Append(type).Append("\r\n");
        text.Append("  InstallerSha256: ").Append(sha.ToLowerInvariant()).Append("\r\n");
        if (scope is not null) text.Append("  Scope: ").Append(scope).Append("\r\n");
        text.Append(extra.Replace("\n", "\r\n", StringComparison.Ordinal));
        text.Append("ManifestVersion: 1.12.0\r\nManifestType: merged\r\n");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static UpdateJobSelection Selection(UpdateScope scope = UpdateScope.Machine, UpdateSource source = UpdateSource.WinGet,
        UpdateArchitecture architecture = UpdateArchitecture.X64, UpdateChannel channel = UpdateChannel.Unknown) =>
        new("product:7zip", UpdateProvider.WinGet, source, "7zip.7zip", "24.09", "25.01", scope, architecture, channel, "winget-com-v1");

    private static InstallationInstance Instance(string key = KeyName, string version = "24.09", string scope = "Machine",
        string architecture = "x64", string provider = "registry") =>
        new(Guid.NewGuid(), ProductId, provider, key, version, scope, architecture, "Igor Pavlov", null, null, null, "Application");

    private static LiveInventoryObservation Observation(string version = "24.09", string scope = "Machine", string architecture = "x64",
        string provider = "registry", string productKey = "product:7zip", InstallationInstance? extra = null) =>
        new(Guid.NewGuid(), ObservedAt, [new CanonicalProduct(ProductId, productKey, "7-Zip")],
            extra is null
                ? [Instance(version: version, scope: scope, architecture: architecture, provider: provider)]
                : [Instance(version: version, scope: scope, architecture: architecture, provider: provider), extra],
            []);

    private sealed class Harness
    {
        internal Harness(FakeEvidence evidence, LiveInventoryObservation? review = null, LiveInventoryObservation? confirm = null,
            Func<string?>? sid = null, UpdateJobSelection? selection = null)
        {
            Queue<LiveInventoryObservation> observations = new([review ?? Observation(), confirm ?? review ?? Observation()]);
            UpdateJobSelection chosen = selection ?? Selection();
            RunningDetector running = new();
            SequentialUpdateBatchCoordinator coordinator = new(Executor, new VerifiedInventory(), new InMemoryHistory(), running);
            Workflow = new ReviewedUpdateExecutionWorkflow(
                (_, _) => Task.FromResult(new UpdateSelectionPreparation(chosen, "fresh", observations.Count > 1 ? observations.Dequeue() : observations.Peek())),
                new UpdateJobApprovalService(Store), coordinator, running, requestingUserSid: sid ?? (() => Sid), machineInstallerEvidence: evidence);
        }

        internal RecordingExecutor Executor { get; } = new();
        internal RecordingStore Store { get; } = new();
        internal ReviewedUpdateExecutionWorkflow Workflow { get; }
    }

    private sealed class FakeEvidence(WingetMachineInstallerCheck result) : IWingetMachineInstallerEvidenceSource
    {
        internal int Calls { get; private set; }

        public Task<WingetMachineInstallerCheck> CheckAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    /// <summary>Answers the source export, then plays <c>winget download</c> by writing the manifest and installer to the named folder.</summary>
    private sealed class ScriptedProcess(byte[] installer, byte[] manifest) : IWingetProcess
    {
        internal List<IReadOnlyList<string>> Calls { get; } = [];
        internal string Export { get; init; } = SourceExport;
        internal int DownloadExitCode { get; init; }

        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add([.. arguments]);
            if (arguments[0] == "source") return Task.FromResult(new WingetProcessResult(0, Export, string.Empty, false, OwnedCompletion: Task.CompletedTask));
            string folder = arguments[13];
            File.WriteAllBytes(Path.Combine(folder, "7-Zip_25.01_Machine_X64_msi_en-US.msi"), installer);
            File.WriteAllBytes(Path.Combine(folder, "7-Zip_25.01_Machine_X64_msi_en-US.yaml"), manifest);
            return Task.FromResult(new WingetProcessResult(DownloadExitCode, "Successfully verified installer hash", string.Empty, false,
                OwnedCompletion: Task.CompletedTask));
        }
    }

    private sealed class TempRoot : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pcs-d4b-review-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class RecordingExecutor : IApprovedUpdateExecutor
    {
        public List<ImmutableUpdateJob> Calls { get; } = [];

        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken)
        {
            Calls.Add(job);
            return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "provider-completed"));
        }

        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, ReviewedUpdateOperation? reviewed, UpdateJobExecutionEvidence currentEvidence,
            UpdateItemProgress progress, CancellationToken cancellationToken)
        {
            Calls.Add(job);
            return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "provider-completed"));
        }
    }

    private sealed class RecordingStore : IUpdateJobStore
    {
        public List<ImmutableUpdateJob> Jobs { get; } = [];
        public Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) { Jobs.Add(job); return Task.CompletedTask; }
        public Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult<ImmutableUpdateJob?>(Jobs.SingleOrDefault(job => job.Id == jobId));
    }

    private sealed class VerifiedInventory : IPostUpdateInventory
    {
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(new PostUpdateInventoryResult(true, false));
    }

    private sealed class InMemoryHistory : IUpdateHistoryStore
    {
        public List<UpdateHistoryEntry> Entries { get; } = [];
        public Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>(Entries.Where(entry => entry.StableProductKey == stableProductKey).ToArray());
        public Task RecoverInterruptedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RunningDetector : IRunningApplicationDetector
    {
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken) =>
            Task.FromResult(RunningApplicationDetection.NotDetected);
    }
}
