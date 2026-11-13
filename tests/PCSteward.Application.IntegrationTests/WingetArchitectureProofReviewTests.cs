using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13. D4b 32-bit-registered apps (design note PHASE_D4B_32BIT_REGISTERED_APPS_DESIGN §3.3–3.4; owner accepted
/// P1 and P2, 2026-11-13): a 64-bit app whose uninstall entry is in the 32-bit view is reviewed as x64 only when the review proves it,
/// from the installed version's own manifest (P1) or from WinGet's installer selection for both versions (P2). The digest binds the
/// 32-bit view the entry was read from, and the entry the target manifest declares goes with the approved job to the post-update check.
/// </summary>
[TestClass]
[TestCategory("FR-UPD-15")]
public sealed class WingetArchitectureProofReviewTests
{
    private const string Sid = "S-1-5-21-11-22-33-1001";
    private const string PackageId = "Contoso.DesktopRuntime.8";
    private const string InstalledCode = "{4d0027d4-962f-4f65-a01b-f8764a5f4220}";
    private const string TargetCode = "{de6ce2b1-81f5-406c-a0ca-534ccdf2ad7e}";
    private const string SourceExport = """
        {"Arg":"https://cdn.winget.microsoft.com/cache","Data":"Microsoft.Winget.Source_8wekyb3d8bbwe","Explicit":false,"Identifier":"Microsoft.Winget.Source_8wekyb3d8bbwe","Name":"winget","TrustLevel":["Trusted","StoreOrigin"],"Type":"Microsoft.PreIndexed.Package"}
        """;

    private static readonly string[] SourceArguments = ["source", "export", "--name", "winget", "--disable-interactivity"];
    private static readonly string[] ProvenVersions = ["8.0.33", "8.0.34"];
    private static readonly string ManifestDigest = new('B', 64);
    private static readonly string InstallerSha = new('C', 64);
    private static readonly DateTimeOffset ObservedAt = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProductId = Guid.Parse("d4b32000-0000-0000-0000-000000000001");
    private static readonly byte[] InstallerBytes = "fixture runtime installer"u8.ToArray();
    private static readonly WingetArchitectureProofRequest Request = new("8.0.33", InstalledCode, "8.0.33.36622");

    [TestMethod]
    public async Task P1IsProvenFromTheInstalledVersionsOwnManifest()
    {
        using TempRoot root = new();
        ScriptedProcess process = new(new() { ["8.0.33"] = RuntimeManifest("8.0.33", InstalledCode, "8.0.33.36622") });
        RecordingQuery query = new(null);

        WingetArchitectureProofResult result = await Source(process, root, query).ProveAsync(Selection(), Request, CancellationToken.None);

        Assert.IsTrue(result.Proven, result.Message);
        Assert.HasCount(2, process.Calls);
        CollectionAssert.AreEqual(SourceArguments, process.Calls[0].ToArray());
        string folder = process.Calls[1][13];
        CollectionAssert.AreEqual(new[]
        {
            "download", "--id", PackageId, "--version", "8.0.33", "--source", "winget", "--exact", "--scope", "machine",
            "--architecture", "x64", "--download-directory", folder, "--disable-interactivity"
        }, process.Calls[1].ToArray());
        Assert.IsFalse(Directory.Exists(folder), "the proof's folder is deleted");
        Assert.AreEqual(0, query.Calls, "P2 is not needed");
    }

    [TestMethod]
    public async Task WithoutP1AnEntryVersionThatIsNotWinGetsVersionIsNotProven()
    {
        using TempRoot root = new();
        ScriptedProcess otherKey = new(new() { ["8.0.33"] = RuntimeManifest("8.0.33", "{0aa1b8a3-0000-4000-8000-000000000000}", "8.0.33.36622") });
        RecordingQuery query = new(Proving("8.0.33", "8.0.34"));

        WingetArchitectureProofResult result = await Source(otherKey, root, query).ProveAsync(Selection(), Request, CancellationToken.None);

        Assert.IsFalse(result.Proven);
        Assert.AreEqual(WingetMachineInstallerEvidenceSource.ArchitectureNotConfirmedMessage, result.Message);
        Assert.AreEqual(0, query.Calls, "P2 can't tie 8.0.33 to 8.0.33.36622");

        ScriptedProcess failed = new(new() { ["8.0.33"] = RuntimeManifest("8.0.33", InstalledCode, "8.0.33.36622") }) { DownloadExitCode = 1 };
        Assert.IsFalse((await Source(failed, root, query).ProveAsync(Selection(), Request, CancellationToken.None)).Proven, "the download failed");
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(root.Path), "every proof folder was deleted");
    }

    [TestMethod]
    public async Task P2IsProvenWhenNeitherVersionHasAnX86MachineInstaller()
    {
        using TempRoot root = new();
        WingetArchitectureProofRequest buildTools = new("8.0.33", "a1b2c3d4", "8.0.33");
        ScriptedProcess process = new(new() { ["8.0.33"] = RuntimeManifest("8.0.33", code: null, displayVersion: null) });
        RecordingQuery proving = new(Proving("8.0.33", "8.0.34"));

        WingetArchitectureProofResult result = await Source(process, root, proving).ProveAsync(Selection(), buildTools, CancellationToken.None);

        Assert.IsTrue(result.Proven, result.Message);
        Assert.AreEqual(1, proving.Calls);
        Assert.AreEqual(PackageId, proving.PackageId);
        CollectionAssert.AreEqual(ProvenVersions, proving.Versions!.ToArray());

        WinGetInstallerArchitectureObservation withX86 = Proving("8.0.33", "8.0.34") with
        {
            Versions = [new("8.0.33", 1, true, true), new("8.0.34", 1, true, false)],
        };
        Assert.IsFalse((await Source(process, root, new(withX86)).ProveAsync(Selection(), buildTools, CancellationToken.None)).Proven,
            "the installed version also had an x86 installer");
        Assert.IsFalse((await Source(process, root, new(null)).ProveAsync(Selection(), buildTools, CancellationToken.None)).Proven, "no answer");
        Assert.IsFalse((await Source(process, root, new(null) { Throws = true }).ProveAsync(Selection(), buildTools, CancellationToken.None)).Proven,
            "the query failed");
    }

    [TestMethod]
    public async Task NothingIsProvenOnAPcThatIsNotX64OrForASelectionThatIsNotX64()
    {
        using TempRoot root = new();
        ScriptedProcess process = new(new() { ["8.0.33"] = RuntimeManifest("8.0.33", InstalledCode, "8.0.33.36622") });

        WingetMachineInstallerEvidenceSource arm64 = new(process, root, new RecordingQuery(null), isX64Windows: () => false);
        Assert.IsFalse((await arm64.ProveAsync(Selection(), Request, CancellationToken.None)).Proven);
        Assert.IsFalse((await Source(process, root, new(null)).ProveAsync(Selection(UpdateArchitecture.X86), Request, CancellationToken.None)).Proven);
        Assert.IsFalse((await Source(process, root, new(null)).ProveAsync(Selection(), Request with { ProviderInstalledVersion = "--version" }, CancellationToken.None)).Proven);
        Assert.HasCount(0, process.Calls);
    }

    [TestMethod]
    public async Task TheTargetChecksDeclaredEntryIsRecorded()
    {
        using TempRoot root = new();
        ScriptedProcess process = new(new() { ["8.0.34"] = RuntimeManifest("8.0.34", TargetCode, "8.0.34.36622") });

        WingetMachineInstallerCheck check = await Source(process, root, new(null)).CheckAsync(Selection(), CancellationToken.None);

        Assert.IsNotNull(check.Evidence, check.Message);
        Assert.AreEqual(WinGetMachineInstallerType.Burn, check.Evidence.InstallerType);
        Assert.AreEqual(new ExpectedUninstallEntry(TargetCode, "8.0.34.36622"), check.Evidence.DeclaredEntry);
    }

    [TestMethod]
    public async Task AProvenReviewBindsThe32BitViewAndConfirmCarriesTheDeclaredEntry()
    {
        ExpectedUninstallEntry declared = new(TargetCode, "8.0.34.36622");
        FakeEvidence evidence = new(new(new(WinGetMachineInstallerType.Burn, ManifestDigest, InstallerSha) { DeclaredEntry = declared }, "checked"));
        FakeProof proof = new(new(true, "proven"));
        Harness harness = new(evidence, proof);

        UpdateReviewPreparation preparation = await harness.Workflow.BeginReviewAsync(Selection().StableProductKey, CancellationToken.None);

        UpdateJobReview review = preparation.Review!;
        Assert.IsNotNull(review, preparation.Message);
        Assert.AreEqual(UpdateArchitecture.X64, review.Selection.Architecture);
        Assert.AreEqual(EvidenceId(), review.EvidenceId, "the digest binds the 32-bit view the entry was read from");
        Assert.IsTrue(review.ArchitectureProven);
        ExpectedUninstallEntry? bound = review.ExpectedEntry;
        Assert.AreEqual(declared, bound);
        Assert.AreEqual(Request, proof.Requests.Single());
        Assert.AreEqual(1, evidence.Calls);

        ReviewedUpdateExecutionResult result = await harness.Workflow.ConfirmAndRunAsync(review, false, CancellationToken.None);

        Assert.IsTrue(result.Started, result.Message);
        Assert.AreEqual(declared, harness.Inventory.Expected.Single(), "the post-update check gets the declared entry");
        Assert.IsNotNull(harness.Executor.Reviewed.Single());
    }

    [TestMethod]
    public async Task WithoutADeclaredEntryThePostUpdateCheckExpectsTheReviewedKeyAtTheTargetVersion()
    {
        FakeEvidence evidence = new(new(new(WinGetMachineInstallerType.Exe, ManifestDigest, InstallerSha), "checked"));
        Harness harness = new(evidence, new FakeProof(new(true, "proven")));

        UpdateJobReview review = (await harness.Workflow.BeginReviewAsync(Selection().StableProductKey, CancellationToken.None)).Review!;

        ExpectedUninstallEntry? bound = review.ExpectedEntry;
        Assert.AreEqual(new ExpectedUninstallEntry(InstalledCode, "8.0.34"), bound);
    }

    [TestMethod]
    public async Task WithoutTheProofNothingIsReviewed()
    {
        FakeEvidence evidence = new(new(new(WinGetMachineInstallerType.Burn, ManifestDigest, InstallerSha), "checked"));

        UpdateReviewPreparation uncomposed = await new Harness(evidence, proof: null).Workflow.BeginReviewAsync(Selection().StableProductKey, CancellationToken.None);
        Assert.IsNull(uncomposed.Review);
        Assert.AreEqual(InventoryWorkflow.ArchitectureNotConfirmedMessage, uncomposed.Message);
        Assert.AreEqual(0, evidence.Calls, "nothing is downloaded without a proof source");

        FakeProof refusing = new(new(false, WingetMachineInstallerEvidenceSource.ArchitectureNotConfirmedMessage));
        UpdateReviewPreparation refused = await new Harness(evidence, refusing).Workflow.BeginReviewAsync(Selection().StableProductKey, CancellationToken.None);
        Assert.IsNull(refused.Review);
        Assert.AreEqual(WingetMachineInstallerEvidenceSource.ArchitectureNotConfirmedMessage, refused.Message);
        Assert.AreEqual(1, evidence.Calls, "the target was checked first");

        FakeProof unused = new(new(true, "proven"));
        UpdateReviewPreparation unproven = await new Harness(evidence, unused, withProofRequest: false).Workflow
            .BeginReviewAsync(Selection().StableProductKey, CancellationToken.None);
        Assert.IsNull(unproven.Review, "an x64 selection never matches a 32-bit-view entry without the proof");
        Assert.AreEqual(WingetMachineReviewedRoute.RegistrationNotFoundMessage, unproven.Message);
        Assert.HasCount(0, unused.Requests);
    }

    [TestMethod]
    public async Task ConfirmRefusesWhenTheFreshPreparationNoLongerAsksForTheProof()
    {
        FakeEvidence evidence = new(new(new(WinGetMachineInstallerType.Burn, ManifestDigest, InstallerSha), "checked"));
        Harness harness = new(evidence, new FakeProof(new(true, "proven")), confirmWithProofRequest: false);
        UpdateJobReview review = (await harness.Workflow.BeginReviewAsync(Selection().StableProductKey, CancellationToken.None)).Review!;

        ReviewedUpdateExecutionResult result = await harness.Workflow.ConfirmAndRunAsync(review, false, CancellationToken.None);

        Assert.IsFalse(result.Started);
        Assert.AreEqual(WingetMachineReviewedRoute.RegistrationChangedMessage, result.Message);
        Assert.HasCount(0, harness.Store.Jobs, "nothing was approved");
    }

    [TestMethod]
    public void TheVersionedReviewDownloadAcceptsOnlyAPlainVersionAndAKnownArchitecture()
    {
        string folder = Path.Combine(Path.GetTempPath(), "pcs-review");
        Assert.AreEqual("8.0.33", WingetMachineReviewedArguments.BuildReviewDownload(PackageId, "8.0.33", "x64", folder)[4]);
        Assert.ThrowsExactly<ArgumentException>(() => WingetMachineReviewedArguments.BuildReviewDownload(PackageId, "-8.0.33", "x64", folder));
        Assert.ThrowsExactly<ArgumentException>(() => WingetMachineReviewedArguments.BuildReviewDownload(PackageId, "8.0.33 --force", "x64", folder));
        Assert.ThrowsExactly<ArgumentException>(() => WingetMachineReviewedArguments.BuildReviewDownload(PackageId, "8.0.33", "neutral", folder));
        Assert.ThrowsExactly<ArgumentException>(() => WingetMachineReviewedArguments.BuildReviewDownload(PackageId, "8.0.33", "x64", "relative"));
    }

    private static WingetMachineInstallerEvidenceSource Source(ScriptedProcess process, TempRoot root, RecordingQuery query) =>
        new(process, root, query, isX64Windows: () => true);

    private static WinGetInstallerArchitectureObservation Proving(string installed, string target) =>
        new(PackageId, true, true, true, 1, false, PackageId, [new(installed, 1, true, false), new(target, 1, true, false)]);

    private static string EvidenceId() =>
        $"wgm1:burn:{WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine32, InstalledCode, "8.0.33.36622")}:{ManifestDigest}:{InstallerSha}";

    private static byte[] RuntimeManifest(string version, string? code, string? displayVersion)
    {
        StringBuilder text = new();
        text.Append("PackageIdentifier: ").Append(PackageId).Append("\r\nPackageVersion: ").Append(version).Append("\r\n");
        text.Append("PackageLocale: en-US\r\nPackageName: Contoso Desktop Runtime 8\r\nPublisher: Contoso\r\nInstallers:\r\n");
        text.Append("- Architecture: x64\r\n  InstallerType: burn\r\n  InstallerSha256: ")
            .Append(Convert.ToHexString(SHA256.HashData(InstallerBytes)).ToLowerInvariant()).Append("\r\n  Scope: machine\r\n");
        if (code is not null)
        {
            text.Append("  ProductCode: '").Append(code).Append("'\r\n  AppsAndFeaturesEntries:\r\n");
            text.Append("  - DisplayName: Contoso Desktop Runtime - ").Append(version).Append(" (x64)\r\n");
            text.Append("    DisplayVersion: ").Append(displayVersion).Append("\r\n    ProductCode: '").Append(code).Append("'\r\n    Publisher: Contoso\r\n");
        }
        text.Append("ManifestVersion: 1.9.0\r\nManifestType: merged\r\n");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static UpdateJobSelection Selection(UpdateArchitecture architecture = UpdateArchitecture.X64) =>
        new("product:runtime8", UpdateProvider.WinGet, UpdateSource.WinGet, PackageId, "8.0.33.36622", "8.0.34", UpdateScope.Machine, architecture,
            UpdateChannel.Unknown, "winget-com-v1");

    /// <summary>The fresh inventory: one entry in the 32-bit machine view, which inventory records as Machine + x86.</summary>
    private static LiveInventoryObservation Observation() =>
        new(Guid.NewGuid(), ObservedAt, [new CanonicalProduct(ProductId, "product:runtime8", "Contoso Desktop Runtime - 8.0.33 (x64)")],
            [new InstallationInstance(Guid.NewGuid(), ProductId, "registry", InstalledCode, "8.0.33.36622", "Machine", "x86", "Contoso", null, null, null, "Application")],
            []);

    private sealed class Harness
    {
        internal Harness(FakeEvidence evidence, FakeProof? proof, bool withProofRequest = true, bool confirmWithProofRequest = true)
        {
            Queue<bool> asks = new([withProofRequest, confirmWithProofRequest]);
            RunningDetector running = new();
            SequentialUpdateBatchCoordinator coordinator = new(Executor, Inventory, new InMemoryHistory(), running);
            Workflow = new ReviewedUpdateExecutionWorkflow(
                (_, _) => Task.FromResult(new UpdateSelectionPreparation(Selection(), "fresh", Observation(),
                    (asks.Count > 0 ? asks.Dequeue() : confirmWithProofRequest) ? Request : null)),
                new UpdateJobApprovalService(Store), coordinator, running, requestingUserSid: () => Sid, machineInstallerEvidence: evidence,
                architectureProof: proof);
        }

        internal RecordingExecutor Executor { get; } = new();
        internal RecordingStore Store { get; } = new();
        internal RecordingInventory Inventory { get; } = new();
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

    private sealed class FakeProof(WingetArchitectureProofResult result) : IWingetArchitectureProofSource
    {
        internal List<WingetArchitectureProofRequest> Requests { get; } = [];

        public Task<WingetArchitectureProofResult> ProveAsync(UpdateJobSelection selection, WingetArchitectureProofRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingQuery(WinGetInstallerArchitectureObservation? observation) : IWinGetInstallerArchitectureQuery
    {
        internal int Calls { get; private set; }
        internal string? PackageId { get; private set; }
        internal IReadOnlyList<string>? Versions { get; private set; }
        internal bool Throws { get; init; }

        public Task<WinGetInstallerArchitectureObservation?> QueryAsync(string packageId, IReadOnlyList<string> versions, CancellationToken cancellationToken)
        {
            Calls++;
            PackageId = packageId;
            Versions = versions;
            if (Throws) throw new InvalidOperationException("COM failed");
            return Task.FromResult(observation);
        }
    }

    /// <summary>Answers the source export, then plays <c>winget download</c> of the requested version into the named folder.</summary>
    private sealed class ScriptedProcess(Dictionary<string, byte[]> manifests) : IWingetProcess
    {
        internal List<IReadOnlyList<string>> Calls { get; } = [];
        internal int DownloadExitCode { get; init; }

        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add([.. arguments]);
            if (arguments[0] == "source") return Task.FromResult(new WingetProcessResult(0, SourceExport, string.Empty, false, OwnedCompletion: Task.CompletedTask));
            string folder = arguments[13];
            if (manifests.TryGetValue(arguments[4], out byte[]? manifest))
            {
                File.WriteAllBytes(Path.Combine(folder, "Runtime_Machine_X64_burn_en-US.exe"), InstallerBytes);
                File.WriteAllBytes(Path.Combine(folder, "Runtime_Machine_X64_burn_en-US.yaml"), manifest);
            }
            return Task.FromResult(new WingetProcessResult(manifest is null ? 1 : DownloadExitCode, "Successfully verified installer hash", string.Empty, false,
                OwnedCompletion: Task.CompletedTask));
        }
    }

    private sealed class TempRoot : IReviewDownloadFolders, IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pcs-d4b-proof-" + Guid.NewGuid().ToString("N"));

        public string? CreateFolder() =>
            Directory.CreateDirectory(System.IO.Path.Combine(Path, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))).FullName;

        public bool DeleteFolder(string folder)
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class RecordingExecutor : IApprovedUpdateExecutor
    {
        public List<ReviewedUpdateOperation?> Reviewed { get; } = [];

        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken)
        {
            Reviewed.Add(null);
            return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "provider-completed"));
        }

        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, ReviewedUpdateOperation? reviewed, UpdateJobExecutionEvidence currentEvidence,
            UpdateItemProgress progress, CancellationToken cancellationToken)
        {
            Reviewed.Add(reviewed);
            return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "provider-completed"));
        }
    }

    private sealed class RecordingStore : IUpdateJobStore
    {
        public List<ImmutableUpdateJob> Jobs { get; } = [];
        public Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) { Jobs.Add(job); return Task.CompletedTask; }
        public Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult<ImmutableUpdateJob?>(Jobs.SingleOrDefault(job => job.Id == jobId));
    }

    private sealed class RecordingInventory : IPostUpdateInventory
    {
        internal List<ExpectedUninstallEntry?> Expected { get; } = [];

        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
            ReInventoryAsync(job, null, cancellationToken);

        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, ExpectedUninstallEntry? expected, CancellationToken cancellationToken)
        {
            Expected.Add(expected);
            return Task.FromResult(new PostUpdateInventoryResult(true, false));
        }
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
