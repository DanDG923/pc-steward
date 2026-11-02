using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// Test-only (an architecture test bans every <c>src</c> reference to <c>CreateForTests</c> (both overloads) and to
/// <see cref="ChocolateyProfileObserver.ObserveForTests"/>): synthetic machine inputs for the observer. A test supplies
/// fixture roots (production uses only <see cref="ChocolateyNativePathResolver.TryResolveMachine"/>); an ancestry base, below
/// which ancestry is evaluated in Ancestor mode from that base instead of from the drive root (a temp fixture always has a
/// requester-owned profile ancestor); a release list for fixture contents (production uses only
/// <see cref="ChocolateyReleaseList.Listed"/>); and an engine-trust check for an unsigned fixture engine (production uses
/// only <c>ChocolateyEngineTrust.IsTrustedEngine</c> with the matched release's file version). The HTTP cache need not be the fixed concatenation, because a
/// standard-user runner cannot create a tree that it does not own (N3 launch-path resolution, for which the HTTP cache is not
/// an input, is then given the fixed concatenation); every other observer rule is unchanged.
/// </summary>
internal sealed class ChocolateyObserverTestSeam
{
    private ChocolateyObserverTestSeam(ChocolateyNativeRoots roots, string ancestryBase, ChocolateyReleaseList releases,
        Func<SafeFileHandle, string, bool> engineTrust)
    {
        Roots = roots;
        AncestryBase = ancestryBase;
        Releases = releases;
        EngineTrust = engineTrust;
    }

    internal ChocolateyNativeRoots Roots { get; }
    internal string AncestryBase { get; }

    /// <summary>ADR-031: the fixture release list (production uses only <see cref="ChocolateyReleaseList.Listed"/>).</summary>
    internal ChocolateyReleaseList Releases { get; }

    internal Func<SafeFileHandle, string, bool> EngineTrust { get; }

    /// <summary>One fixture release table, as a single-entry 2.7.5-shaped fixture list with the reviewed 2.7.5 asset digest.</summary>
    internal static ChocolateyObserverTestSeam CreateForTests(ChocolateyNativeRoots roots, string ancestryBase,
        IReadOnlyList<ChocolateyReleaseFile> releaseFiles, Func<SafeFileHandle, string, bool> engineTrust)
    {
        ArgumentNullException.ThrowIfNull(releaseFiles);
        return Create(roots, ancestryBase, ChocolateyReleaseList.CreateForTests(
        [
            new ChocolateyReleaseEntry(ChocolateyReleaseManifest2_7_5.Release, ChocolateyClientReleasePolicy.Version,
                ChocolateyReleaseManifest2_7_5.Asset, ChocolateyReleaseManifest2_7_5.AssetLength, ChocolateyReleaseManifest2_7_5.AssetSha256,
                [.. releaseFiles]),
        ]), engineTrust);
    }

    /// <summary>ADR-031: a fixture release list (a multi-entry list exercises release selection and the §3a leftover rule).</summary>
    internal static ChocolateyObserverTestSeam CreateForTests(ChocolateyNativeRoots roots, string ancestryBase,
        ChocolateyReleaseList releases, Func<SafeFileHandle, string, bool> engineTrust) => Create(roots, ancestryBase, releases, engineTrust);

    private static ChocolateyObserverTestSeam Create(ChocolateyNativeRoots roots, string ancestryBase,
        ChocolateyReleaseList releases, Func<SafeFileHandle, string, bool> engineTrust)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(engineTrust);
        if (!ProtectedNodeReader.IsCanonicalLocalPath(ancestryBase) || ancestryBase.Length <= 3)
            throw new ArgumentException("A canonical ancestry base below a drive root is required.", nameof(ancestryBase));
        return new ChocolateyObserverTestSeam(roots, ancestryBase, releases, engineTrust);
    }
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §3 (with §2.3, §3.1, rev-3 L3 and rev-2 L5): the static Chocolatey
/// profile observer. Every read goes through the N1 primitives: ancestry first (<see cref="RequesterAccess.EvaluateAncestry"/>),
/// then <see cref="ProtectedTreeWalker"/> coverage, then read-share-only reopen of each walked node with its identity
/// required unchanged. Steps run in the design order 1–7, 8, 8a, 9; the first failure returns, with the rev 3 §3.1
/// precedence MachineInputsUnsupported, SourceNotCommunity, InstalledMismatch, ProfileMismatch. An interrupt (the caller's
/// token, or <c>remaining</c> exceeded) returns <see cref="ChocolateyProfileResult.Cancelled"/> or
/// <see cref="ChocolateyProfileResult.TimedOut"/> and never a settled refusal: every refusal is re-checked against the
/// interrupt first, so a read cut short by an interrupt cannot surface as MachineInputsUnsupported. Every uncertainty refuses.
/// </summary>
internal static class ChocolateyProfileObserver
{
    internal const int ConfigurationMaximumBytes = 64 * 1024;
    internal const long CodeFileMaximumBytes = 64L * 1024 * 1024;
    internal const long CodeMaximumBytes = 512L * 1024 * 1024;
    internal const int ArchiveMaximumBytes = 64 * 1024 * 1024;
    internal const long ArchiveMaximumAggregateBytes = 512L * 1024 * 1024;
    internal const int MaximumArchives = 2048;
    internal const int MaximumLibraryDirectories = 1024;
    internal const int MaximumStoreDirectories = 4096;
    private const string ClientId = "chocolatey";
    private const string LicensedExtensionId = "chocolatey.extension";

    private static readonly string[] RequiredTrees = ["bin", "lib", ".chocolatey", "config", "helpers", "tools"];
    private static readonly string[] OptionalTrees = ["hooks", "extensions", "license", "lib-bkp", "lib-bad", "logs"];
    private static readonly string[] Frameworks = ["Framework", "Framework64"];
    private static readonly string[] GacRoots =
    [
        @"Microsoft.NET\assembly\GAC_MSIL", @"Microsoft.NET\assembly\GAC_32", @"Microsoft.NET\assembly\GAC_64",
        @"assembly\GAC", @"assembly\GAC_32", @"assembly\GAC_64", @"assembly\GAC_MSIL",
    ];

    internal static ChocolateyProfileResult Observe(RequesterToken token, ChocolateyObserverRole role, string targetId,
        ChocolateyNativeVersion frozenInstalled, string? expectedFingerprint, TimeSpan remaining, CancellationToken cancellationToken) =>
        ObserveCore(token, role, targetId, frozenInstalled, expectedFingerprint, remaining, seam: null, cancellationToken);

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): <see cref="Observe"/> over synthetic machine inputs.</summary>
    internal static ChocolateyProfileResult ObserveForTests(RequesterToken token, ChocolateyObserverRole role, string targetId,
        ChocolateyNativeVersion frozenInstalled, string? expectedFingerprint, TimeSpan remaining, ChocolateyObserverTestSeam seam,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seam);
        return ObserveCore(token, role, targetId, frozenInstalled, expectedFingerprint, remaining, seam, cancellationToken);
    }

    /// <summary>
    /// ADR-031 design §3.6: the same observation for the client self-update, whose target is the client registration
    /// <c>chocolatey</c> itself. Step 7 then requires exactly one <c>chocolatey</c> registration at the version of the listed
    /// release step 2 matched, and no <c>chocolatey.extension</c> registration; the Helper role returns a client-update
    /// binding. Every other step is unchanged. Post-update verification calls it in the Desktop role with the target version.
    /// </summary>
    internal static ChocolateyProfileResult ObserveClient(RequesterToken token, ChocolateyObserverRole role, ChocolateyNativeVersion frozenInstalled,
        string? expectedFingerprint, TimeSpan remaining, CancellationToken cancellationToken) =>
        ObserveCore(token, role, PCSteward.Domain.ReviewedUpdateOperation.ChocolateyClientPackageId, frozenInstalled, expectedFingerprint,
            remaining, seam: null, cancellationToken, clientMode: true);

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): <see cref="ObserveClient"/> over synthetic machine inputs.</summary>
    internal static ChocolateyProfileResult ObserveClientForTests(RequesterToken token, ChocolateyObserverRole role,
        ChocolateyNativeVersion frozenInstalled, string? expectedFingerprint, TimeSpan remaining, ChocolateyObserverTestSeam seam,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seam);
        return ObserveCore(token, role, PCSteward.Domain.ReviewedUpdateOperation.ChocolateyClientPackageId, frozenInstalled, expectedFingerprint,
            remaining, seam, cancellationToken, clientMode: true);
    }

    private static ChocolateyProfileResult ObserveCore(RequesterToken token, ChocolateyObserverRole role, string targetId,
        ChocolateyNativeVersion frozenInstalled, string? expectedFingerprint, TimeSpan remaining, ChocolateyObserverTestSeam? seam,
        CancellationToken cancellationToken, bool clientMode = false)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(frozenInstalled);
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        using Run run = new(remaining, cancellationToken);
        if (run.Interrupted) return run.Interrupt();
        using Observation observation = new(token, role, targetId, seam, run, clientMode);
        ChocolateyProfileResult result = observation.Evaluate(frozenInstalled, expectedFingerprint);
        if (result is ChocolateyProfileResult.Observed) observation.Release();
        return result;
    }

    /// <summary>The interrupt state: the caller's token, plus a linked source cancelled when <c>remaining</c> elapses.</summary>
    private sealed class Run : IDisposable
    {
        private readonly CancellationTokenSource linked;
        private readonly CancellationToken caller;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly TimeSpan remaining;

        internal Run(TimeSpan remaining, CancellationToken caller)
        {
            this.caller = caller;
            this.remaining = remaining;
            linked = CancellationTokenSource.CreateLinkedTokenSource(caller);
            if (remaining <= TimeSpan.Zero) linked.Cancel();
            else if (remaining.TotalMilliseconds < int.MaxValue) linked.CancelAfter(remaining);
        }

        /// <summary>Passed to the walker so an interrupt also stops a walk in progress.</summary>
        internal CancellationToken Token => linked.Token;

        internal bool Interrupted => caller.IsCancellationRequested || linked.IsCancellationRequested || clock.Elapsed >= remaining;

        /// <summary>Cancelled when the caller asked; otherwise the budget ran out.</summary>
        internal ChocolateyProfileResult Interrupt() =>
            caller.IsCancellationRequested ? ChocolateyProfileResult.Cancelled.Create() : ChocolateyProfileResult.TimedOut.Create();

        public void Dispose() => linked.Dispose();
    }

    private enum DirectoryState
    {
        Present,
        Absent,
        Refused
    }

    /// <summary>One observation's state. Steps 1–7 return false on the first failure (MachineInputsUnsupported).</summary>
    private sealed class Observation(RequesterToken token, ChocolateyObserverRole role, string targetId, ChocolateyObserverTestSeam? seam, Run run,
        bool clientMode)
        : IDisposable
    {
        private readonly ChocolateyHeldProfileFiles? held = role == ChocolateyObserverRole.Helper ? ChocolateyHeldProfileFiles.Begin() : null;
        private readonly Dictionary<string, NativeNode> byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ChocolateyCodeFileData> codeFiles = [];
        private readonly List<ChocolateyRegistrationRecord> clientRecords = [];
        private readonly List<ChocolateyRegistrationRecord> extensionRecords = [];
        private bool released;
        private ChocolateyNativeRoots roots = null!;
        private IReadOnlyList<NativeNode> nodes = [];
        private string rootIdentity = string.Empty;
        private string engineSha256 = string.Empty;
        private string codeDigest = string.Empty;
        private ChocolateyConfigurationObservation configuration = null!;
        private string cacheIdentity = string.Empty;
        private ChocolateyLaunchPaths? launchPaths;
        private ChocolateyRegistrationRecord target = null!;
        private string? storeIdentity;
        private bool libBadResidue;
        private ChocolateyReviewedRelease release = null!;
        private ChocolateyReleaseResidue? residue;
        private bool unlistedLeftover;

        internal ChocolateyProfileResult Evaluate(ChocolateyNativeVersion frozenInstalled, string? expectedFingerprint)
        {
            // Steps 1–7: MachineInputsUnsupported (step 7 covers every library/store structure failure, L5).
            if (!Roots() || !ReleaseTree() || !CodeIdentities() ||
                (role == ChocolateyObserverRole.Helper && !HttpCache()) ||
                !LoaderAndLicensing() || !Configuration() || !LibraryAndStore())
                return Refuse(ChocolateyProfileRefusal.MachineInputsUnsupported, null, null);
            // ADR-031 §3a fallback: steps 1–7 otherwise passed, so the unlisted leftover was their only failure (disclosed).
            if (unlistedLeftover)
                return Refuse(ChocolateyProfileRefusal.MachineInputsUnsupported, null, null, ChocolateyProfileRefusalDetail.UnlistedReleaseLeftover);

            // Step 8: the compiled Community URL must stay byte-equal (S8: installed and fingerprint null).
            if (!ChocolateySourceResolution.TryResolve(configuration, out _))
                return Refuse(ChocolateyProfileRefusal.SourceNotCommunity, null, null);

            // Step 8a: the library's installed version must equal the frozen value.
            ChocolateyNativeVersion installed = target.Version;
            if (!string.Equals(installed.Normalized, frozenInstalled.Normalized, StringComparison.Ordinal))
                return Refuse(ChocolateyProfileRefusal.InstalledMismatch, installed.Normalized, null);

            // Step 9: the common fingerprint, compared in fixed time when the helper supplies the expected value.
            ChocolateyCommonProfileData common = new(token.Sid, rootIdentity, codeFiles.AsReadOnly(), cacheIdentity, configuration,
                targetId, installed, target.ArchiveIdentity, target.Sha256, storeIdentity, release.AssetSha256);
            string fingerprint;
            try { fingerprint = ChocolateyCommonProfileFingerprint.Compute(common); }
            catch (ArgumentException) { return Refuse(ChocolateyProfileRefusal.MachineInputsUnsupported, null, null); }
            if (expectedFingerprint is not null && !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(fingerprint),
                    Encoding.UTF8.GetBytes(expectedFingerprint)))
                return Refuse(ChocolateyProfileRefusal.ProfileMismatch, installed.Normalized, fingerprint);

            ChocolateyClientStateData clientState = new(engineSha256, codeDigest,
                ChocolateyProfileRules.ComputeRegistrationDigest(clientRecords), ChocolateyProfileRules.ComputeRegistrationDigest(extensionRecords));
            if (run.Interrupted) return run.Interrupt();
            ChocolateyHeldClientBinding? client = null;
            if (held is not null)
            {
                // N3: the binding takes the sealed holder over; its launch paths were resolved and validated in step 6.
                held.Seal();
                client = clientMode
                    ? ChocolateyHeldClientBinding.CreateForClientUpdate(held, launchPaths!, release)
                    : ChocolateyHeldClientBinding.Create(held, launchPaths!, targetId);
            }
            return ChocolateyProfileResult.Observed.Create(
                ChocolateyProfileObservation.Create(common, fingerprint, clientState, installed, libBadResidue, client, release, residue));
        }

        /// <summary>The observation took ownership of the held files.</summary>
        internal void Release() => released = true;

        public void Dispose()
        {
            if (!released) held?.Dispose();
        }

        /// <summary>Never a settled refusal on an interrupt (§3, rev-4 L3).</summary>
        private ChocolateyProfileResult Refuse(ChocolateyProfileRefusal reason, string? installed, string? fingerprint,
            ChocolateyProfileRefusalDetail detail = ChocolateyProfileRefusalDetail.None) =>
            run.Interrupted ? run.Interrupt() : ChocolateyProfileResult.Refused.Create(reason, installed, fingerprint, detail);

        // Step 1. Machine roots, then the Chocolatey root's ancestry and its complete bounded walk (M3).
        private bool Roots()
        {
            ChocolateyNativeRoots? resolved = seam is null ? ChocolateyNativePathResolver.TryResolveMachine() : seam.Roots;
            if (resolved is null) return false;
            roots = resolved;
            if (!new[] { roots.ProgramData, roots.Root, roots.HttpCache, roots.WindowsRoot }.All(path =>
                    ProtectedNodeReader.IsCanonicalLocalPath(path) && path.Length > 3))
                return false;
            if (!string.Equals(roots.Root, roots.ProgramData + @"\chocolatey", StringComparison.Ordinal)) return false;
            if (seam is null && !string.Equals(roots.HttpCache, roots.ProgramData + @"\ChocolateyHttpCache", StringComparison.Ordinal)) return false;
            if (Ancestry(roots.Root) != RequesterModification.Denied) return false;

            // The whole root is walked: every node except regular files under logs must be requester-unmodifiable, and the
            // root itself evaluates Denied for FILE_ADD_SUBDIRECTORY, which is the M3 condition for an optional tree's absence.
            if (ProtectedTreeWalker.Walk(token, roots.Root, TreeWalkBounds.Profile, ChocolateyTreeCoveragePolicy.Classify, run.Token)
                is not TreeWalkResult.Complete complete)
                return false;
            nodes = complete.Nodes;
            foreach (NativeNode node in nodes)
                if (!byPath.TryAdd(node.RelativePath, node)) return false;
            if (nodes.Count == 0 || nodes[0].RelativePath.Length != 0 || !nodes[0].Directory) return false;
            rootIdentity = ChocolateyProfileRules.FormatIdentity(nodes[0].Identity);
            foreach (string tree in RequiredTrees)
                if (!byPath.TryGetValue(tree, out NativeNode node) || !node.Directory) return false;
            foreach (string tree in OptionalTrees)
                if (byPath.TryGetValue(tree, out NativeNode node) && !node.Directory) return false;
            return !run.Interrupted;
        }

        // Step 2 (ADR-031 §3.2, §3a). The listed release is selected by the held root engine's SHA-256 (exactly one listed
        // match); then that release's complete table: every entry present with its length and SHA-256, no other helpers/tools
        // file or directory except the one reviewed leftover whose bytes equal a listed release's file (a disclosed finding),
        // and the root engine trusted by WinVerifyTrust, signer and that release's file version on its held handle (L3).
        private bool ReleaseTree()
        {
            ChocolateyReleaseList list = seam is null ? ChocolateyReleaseList.Listed : seam.Releases;
            if (!byPath.TryGetValue(ChocolateyReviewedRelease.EnginePath, out NativeNode engineNode) || engineNode.Directory) return false;
            long engineMaximum = list.Releases.Max(candidate => candidate.EngineLength);
            if (engineNode.Length < 0 || engineNode.Length > engineMaximum) return false;
            SafeFileHandle? engineHandle = Reopen(engineNode);
            if (engineHandle is null) return false;
            bool engineKept = false;
            try
            {
                if (!ProtectedNodeReader.TryHashBounded(engineHandle, engineMaximum, out NativeObservedFile engineObserved) ||
                    engineObserved.Length != engineNode.Length)
                    return false;
                ChocolateyReviewedRelease? matched = list.FindByEngine(engineObserved.Sha256);
                if (matched is null || matched.EngineLength != engineObserved.Length) return false;
                release = matched;
                if (!ReleaseTable(matched.Files, engineNode, engineHandle, engineObserved, ref engineKept)) return false;
            }
            finally
            {
                if (!engineKept) engineHandle.Dispose();
            }
            return engineSha256.Length != 0 && !run.Interrupted;
        }

        /// <summary>The selected release's table; the already-hashed engine handle is reused (and kept when held).</summary>
        private bool ReleaseTable(IReadOnlyList<ChocolateyReleaseFile> table, NativeNode engineNode, SafeFileHandle engineHandle,
            NativeObservedFile engineObserved, ref bool engineKept)
        {
            HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase) { "helpers", "tools" };
            foreach (ChocolateyReleaseFile entry in table)
            {
                string relative = entry.Path.Replace('/', '\\');
                if (!files.Add(relative)) return false;
                for (int slash = relative.IndexOf('\\', StringComparison.Ordinal); slash > 0; slash = relative.IndexOf('\\', slash + 1))
                    directories.Add(relative[..slash]);
            }

            foreach (NativeNode node in nodes)
            {
                string top = node.RelativePath.Split('\\')[0];
                if (!top.Equals("helpers", StringComparison.OrdinalIgnoreCase) && !top.Equals("tools", StringComparison.OrdinalIgnoreCase)) continue;
                if (node.Directory ? directories.Contains(node.RelativePath) : files.Contains(node.RelativePath)) continue;
                if (node.Directory || !node.RelativePath.Equals(ChocolateyReleaseList.PowerShellModuleLeftover, StringComparison.OrdinalIgnoreCase) ||
                    !LeftoverObserved(node))
                    return false;
            }

            foreach (ChocolateyReleaseFile entry in table)
            {
                if (run.Interrupted) return false;
                if (entry.Path == ChocolateyReviewedRelease.EnginePath)
                {
                    if (entry.Length != engineObserved.Length || !string.Equals(entry.Sha256, engineObserved.Sha256, StringComparison.Ordinal))
                        return false;
                    bool trusted = seam is null
                        ? ChocolateyEngineTrust.IsTrustedEngine(engineHandle, engineNode.FullPath, release.EngineFileVersion)
                        : seam.EngineTrust(engineHandle, engineNode.FullPath);
                    if (!trusted) return false;
                    engineSha256 = engineObserved.Sha256;
                    if (held is not null)
                    {
                        held.AddRelease(new ChocolateyHeldFile(entry.Path, engineNode, engineHandle), engine: true);
                        engineKept = true;
                    }
                    continue;
                }
                if (!byPath.TryGetValue(entry.Path.Replace('/', '\\'), out NativeNode node) || node.Directory || node.Length != entry.Length) return false;
                SafeFileHandle? handle = Reopen(node);
                if (handle is null) return false;
                bool keep = false;
                try
                {
                    if (!ProtectedNodeReader.TryHashBounded(handle, entry.Length, out NativeObservedFile observed) ||
                        observed.Length != entry.Length || !string.Equals(observed.Sha256, entry.Sha256, StringComparison.Ordinal))
                        return false;
                    if (held is not null)
                    {
                        held.AddRelease(new ChocolateyHeldFile(entry.Path, node, handle), engine: false);
                        keep = true;
                    }
                }
                finally
                {
                    if (!keep) handle.Dispose();
                }
            }
            return true;
        }

        /// <summary>
        /// §3a: the one reviewed leftover, already covered by the walk (requester-unmodifiable, reparse-free). Its bytes are read
        /// through a read-share-only handle that is closed again (never held). A match with a listed release's file becomes the
        /// disclosed finding; a readable mismatch is recorded for the disclosed fallback refusal; an unreadable leftover refuses.
        /// </summary>
        private bool LeftoverObserved(NativeNode node)
        {
            if (node.Length < 0 || node.Length > CodeFileMaximumBytes) return false;
            using SafeFileHandle? leftover = Reopen(node);
            if (leftover is null || !ProtectedNodeReader.TryHashBounded(leftover, node.Length, out NativeObservedFile observed) ||
                observed.Length != node.Length)
                return false;
            ChocolateyReviewedRelease? source = release.List.MatchLeftover(node.RelativePath, observed.Length, observed.Sha256);
            if (source is null) unlistedLeftover = true;
            else residue = new ChocolateyReleaseResidue(ChocolateyReleaseList.PowerShellModuleLeftover, source);
            return true;
        }

        // Step 3. Ordered code identities of every regular file under hooks and extensions (L5), within the code bounds.
        private bool CodeIdentities()
        {
            long total = 0;
            foreach (NativeNode node in nodes)
            {
                if (node.Directory) continue;
                string top = node.RelativePath.Split('\\')[0];
                if (!top.Equals("hooks", StringComparison.OrdinalIgnoreCase) && !top.Equals("extensions", StringComparison.OrdinalIgnoreCase)) continue;
                if (run.Interrupted || node.Length is < 0 or > CodeFileMaximumBytes || (total += node.Length) > CodeMaximumBytes) return false;
                SafeFileHandle? handle = Reopen(node);
                if (handle is null) return false;
                bool keep = false;
                try
                {
                    if (!ProtectedNodeReader.TryHashBounded(handle, CodeFileMaximumBytes, out NativeObservedFile observed) || observed.Length != node.Length)
                        return false;
                    codeFiles.Add(new ChocolateyCodeFileData(node.RelativePath, ChocolateyProfileRules.FormatIdentity(node.Identity), observed.Length, observed.Sha256));
                    if (held is not null)
                    {
                        held.AddCode(new ChocolateyHeldFile(node.RelativePath, node, handle));
                        keep = true;
                    }
                }
                finally
                {
                    if (!keep) handle.Dispose();
                }
            }
            try { codeDigest = ChocolateyCommonProfileFingerprint.ComputeCodeList(codeFiles.AsReadOnly()); }
            catch (ArgumentException) { return false; }
            return !run.Interrupted;
        }

        // Step 4 (Helper only, L1, L4). The existing helper HTTP cache: ancestry, a complete walk, and every node neither
        // requester-modifiable nor requester-owned. Absent refuses; PC Steward never creates or repairs it.
        private bool HttpCache()
        {
            if (Ancestry(roots.HttpCache) != RequesterModification.Denied) return false;
            if (ProtectedTreeWalker.Walk(token, roots.HttpCache, TreeWalkBounds.Profile, static (_, _) => ChocolateyTreeContentPolicy.Protected, run.Token)
                is not TreeWalkResult.Complete complete)
                return false;
            foreach (NativeNode node in complete.Nodes)
            {
                if (run.Interrupted) return false;
                using SafeFileHandle? handle = Reopen(node);
                if (handle is null || RequesterAccess.IsRequesterOwned(token, handle)) return false;
            }
            return !run.Interrupted;
        }

        // Step 5. CLR4 loader and licensing inputs (M3): both machine.config files and their Config directories, every GAC
        // root, the licensed assembly anywhere in the root walk, and choco.exe.config.
        private bool LoaderAndLicensing()
        {
            foreach (string framework in Frameworks)
            {
                string configDirectory = roots.WindowsRoot + @"\Microsoft.NET\" + framework + @"\v4.0.30319\Config";
                string machineConfig = configDirectory + @"\machine.config";
                if (Ancestry(machineConfig) != RequesterModification.Denied) return false;
                if (OpenProtectedDirectory(configDirectory, mayBeAbsent: false, out SafeFileHandle? opened, out NativeNode directoryNode) != DirectoryState.Present)
                    return false;
                using SafeFileHandle directory = opened!;
                if (ProtectedTreeWalker.LocateChild(directory, directoryNode, "machine.config", directory: false, run.Token,
                        out SafeFileHandle? file, out _, out _) != ChildPresence.Present)
                    return false;
                using (file)
                {
                    if (RequesterAccess.Evaluate(token, file!, directory: false, directory, AccessMode.Node) != RequesterModification.Denied ||
                        !ProtectedNodeReader.TryReadBounded(file!, ChocolateyProfileRules.MachineConfigMaximumBytes, out byte[] bytes) ||
                        !ChocolateyProfileRules.IsSupportedMachineConfig(bytes))
                        return false;
                }
            }

            foreach (string gac in GacRoots)
            {
                if (run.Interrupted) return false;
                DirectoryState state = OpenProtectedDirectory(roots.WindowsRoot + '\\' + gac, mayBeAbsent: true, out SafeFileHandle? opened, out _);
                if (state == DirectoryState.Absent) continue;
                if (state != DirectoryState.Present) return false;
                using SafeFileHandle directory = opened!;
                if (ProtectedTreeWalker.TryListChildren(directory, TreeWalkBounds.Profile.MaximumEntries, run.Token,
                        out IReadOnlyList<NativeDirectoryEntry> entries) is not null)
                    return false;
                if (entries.Any(entry => ChocolateyProfileRules.IsLicensedGacEntry(entry.Name))) return false;
            }

            foreach (NativeNode node in nodes)
                if (ChocolateyProfileRules.IsLicensedAssemblyNode(Leaf(node.RelativePath), node.Directory)) return false;
            return !byPath.ContainsKey("choco.exe.config") && !run.Interrupted;
        }

        // Step 6. The main configuration through its held handle, the required effective features, and a protected,
        // existing cacheLocation whose directory identity is captured.
        private bool Configuration()
        {
            if (!byPath.TryGetValue(@"config\chocolatey.config", out NativeNode node) || node.Directory) return false;
            using (SafeFileHandle? handle = Reopen(node))
            {
                if (handle is null || !ProtectedNodeReader.TryReadBounded(handle, ConfigurationMaximumBytes, out byte[] bytes) ||
                    !ChocolateyConfigurationParser.TryRead(bytes, out ChocolateyConfigurationObservation? parsed) || parsed is null)
                    return false;
                configuration = parsed;
            }
            if (!ChocolateyProfileRules.HasRequiredFeatures(configuration) || !ChocolateyProfileRules.HasSupportedSources(configuration)) return false;
            string cache = configuration.CacheLocation;
            if (!ChocolateyProfileRules.IsSupportedCacheLocation(cache) || Ancestry(cache) != RequesterModification.Denied) return false;
            using SafeFileHandle? cacheHandle = ProtectedNodeReader.TryOpen(cache, directory: true, out NativeNode cacheNode);
            if (cacheHandle is null ||
                RequesterAccess.Evaluate(token, cacheHandle, directory: true, parentDirectory: null, AccessMode.Node) != RequesterModification.Denied)
                return false;
            cacheIdentity = ChocolateyProfileRules.FormatIdentity(cacheNode.Identity);

            // Helper only (N3, §4): the compiled launch environment's paths, with TEMP/TMP = this protected cacheLocation.
            // An unresolvable or unformattable path set refuses here, inside the MachineInputsUnsupported steps. The HTTP
            // cache is not a launch path; under the test seam (which may substitute it) the fixed concatenation is supplied.
            if (held is not null)
            {
                ChocolateyNativeRoots launchRoots = seam is null ? roots : roots with { HttpCache = roots.ProgramData + @"\ChocolateyHttpCache" };
                launchPaths = ChocolateyNativePathResolver.TryResolveLaunchPaths(token, launchRoots, cache);
                if (launchPaths is null || !ChocolateyHeldClientBinding.IsLaunchable(launchPaths, roots.Root)) return false;
            }
            return !run.Interrupted;
        }

        // Step 7. Library discovery (lib-root .nupkg files and each immediate directory's top-level .nupkg), exactly one target
        // identity, the store alternatives, markers, pending state, lib-bkp prefixes and lib-bad residue (L5).
        private bool LibraryAndStore()
        {
            // Package jobs refuse the client identities; the client self-update (ADR-031) targets exactly chocolatey.
            if (!ChocolateyNativePackageId.IsSupported(targetId) ||
                (clientMode
                    ? !string.Equals(targetId, PCSteward.Domain.ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal)
                    : ChocolateyClientReleasePolicy.IsClientTarget(targetId)))
                return false;
            NativeNode[] libChildren = Children("lib");
            if (libChildren.Count(node => node.Directory) > MaximumLibraryDirectories) return false;
            HashSet<string> libDirectories = new(libChildren.Where(node => node.Directory).Select(node => node.RelativePath), StringComparer.OrdinalIgnoreCase);
            NativeNode[] archives = [.. nodes.Where(node => !node.Directory && node.RelativePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
                (Parent(node.RelativePath).Equals("lib", StringComparison.OrdinalIgnoreCase) || libDirectories.Contains(Parent(node.RelativePath))))];
            if (archives.Length > MaximumArchives) return false;
            long aggregate = 0;
            List<ChocolateyRegistrationRecord> targets = [];
            foreach (NativeNode archive in archives)
            {
                if (run.Interrupted || archive.Length is < 22 or > ArchiveMaximumBytes || (aggregate += archive.Length) > ArchiveMaximumAggregateBytes)
                    return false;
                byte[] bytes;
                using (SafeFileHandle? handle = Reopen(archive))
                {
                    if (handle is null || !ProtectedNodeReader.TryReadBounded(handle, ArchiveMaximumBytes, out bytes) || bytes.LongLength != archive.Length)
                        return false;
                }
                if (!ChocolateyPackageArchiveIdentityReader.TryRead(bytes, out ChocolateyNuspecIdentity? identity) || identity is null) return false;
                ChocolateyRegistrationRecord record = new(archive.RelativePath, identity.Id, identity.Version,
                    ChocolateyProfileRules.FormatIdentity(archive.Identity), bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)));
                if (identity.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase)) targets.Add(record);
                if (identity.Id.Equals(ClientId, StringComparison.OrdinalIgnoreCase)) clientRecords.Add(record);
                if (identity.Id.Equals(LicensedExtensionId, StringComparison.OrdinalIgnoreCase)) extensionRecords.Add(record);
            }
            if (targets.Count != 1) return false;
            target = targets[0];
            // ADR-031 design §3.6: the client registration is the matched listed release, with no licensed extension registered.
            if (clientMode && (!string.Equals(target.Version.Normalized, release.Version, StringComparison.Ordinal) || extensionRecords.Count != 0))
                return false;

            // Store: at most one of the four pinned alternatives exists, it is a directory, and it holds no override marker.
            IReadOnlyList<string> alternatives;
            try { alternatives = target.Version.GetStoreDirectoryNames(target.Id); }
            catch (ArgumentException) { return false; }
            NativeNode[] store = Children(".chocolatey");
            if (store.Count(node => node.Directory) > MaximumStoreDirectories) return false;
            if (!ChocolateyProfileRules.TrySelectStore(alternatives, store.Select(node => Leaf(node.RelativePath)), out string? selected)) return false;
            storeIdentity = null;
            if (selected is not null)
            {
                NativeNode chosen = byPath[@".chocolatey\" + selected];
                if (!chosen.Directory) return false;
                if (Children(chosen.RelativePath).Any(node => ChocolateyProfileRules.IsStoreOverrideMarker(Leaf(node.RelativePath)))) return false;
                storeIdentity = ChocolateyProfileRules.FormatIdentity(chosen.Identity);
            }

            // Pending markers anywhere under lib, including the top-level lib\.chocolateyPending.
            if (nodes.Any(node => Parent(node.RelativePath).Length != 0 && node.RelativePath.StartsWith(@"lib\", StringComparison.OrdinalIgnoreCase) &&
                    ChocolateyProfileRules.IsPendingMarker(Leaf(node.RelativePath))))
                return false;

            // lib-bkp: the native cleanup glob also matches 8.3 short names, so the held directory is enumerated for both names.
            if (byPath.TryGetValue("lib-bkp", out NativeNode backup))
            {
                using SafeFileHandle? handle = Reopen(backup);
                if (handle is null || ProtectedTreeWalker.TryListChildren(handle, TreeWalkBounds.Profile.MaximumEntries, run.Token,
                        out IReadOnlyList<NativeDirectoryEntry> entries) is not null)
                    return false;
                if (entries.Any(entry => ChocolateyProfileRules.IsLibBackupConflict(entry.Name, entry.ShortName, targetId))) return false;
            }

            libBadResidue = Children("lib-bad").Any(node => Leaf(node.RelativePath).StartsWith(targetId, StringComparison.OrdinalIgnoreCase));
            return !run.Interrupted;
        }

        /// <summary>
        /// Ancestry (from the drive root in production), then the directory through its held parent's enumeration in Node
        /// mode with the parent's FILE_DELETE_CHILD. <see cref="DirectoryState.Absent"/> only when the held parent does not
        /// list it, absence is allowed, and the parent itself denies the requester creating it (Node mode).
        /// </summary>
        private DirectoryState OpenProtectedDirectory(string path, bool mayBeAbsent, out SafeFileHandle? handle, out NativeNode node)
        {
            handle = null;
            node = default;
            int slash = path.LastIndexOf('\\');
            if (slash < 2 || Ancestry(path) != RequesterModification.Denied) return DirectoryState.Refused;
            string parentPath = slash == 2 ? path[..3] : path[..slash];
            using SafeFileHandle? parent = ProtectedNodeReader.TryOpen(parentPath, directory: true, out NativeNode parentNode);
            if (parent is null) return DirectoryState.Refused;
            switch (ProtectedTreeWalker.LocateChild(parent, parentNode, path[(slash + 1)..], directory: true, run.Token,
                        out SafeFileHandle? child, out node, out _))
            {
                case ChildPresence.Present:
                    if (RequesterAccess.Evaluate(token, child!, directory: true, parent, AccessMode.Node) == RequesterModification.Denied)
                    {
                        handle = child;
                        return DirectoryState.Present;
                    }
                    child!.Dispose();
                    return DirectoryState.Refused;
                case ChildPresence.Absent:
                    return mayBeAbsent &&
                        RequesterAccess.Evaluate(token, parent, directory: true, parentDirectory: null, AccessMode.Node) == RequesterModification.Denied
                        ? DirectoryState.Absent : DirectoryState.Refused;
                default:
                    return DirectoryState.Refused;
            }
        }

        /// <summary>
        /// Production: <see cref="RequesterAccess.EvaluateAncestry"/> from the lexical drive root. Test seam: below its base,
        /// Ancestor mode on every directory from the base down to the parent, each opened through the protected reader.
        /// </summary>
        private RequesterModification Ancestry(string path)
        {
            if (seam is null || !path.StartsWith(seam.AncestryBase + '\\', StringComparison.OrdinalIgnoreCase))
                return RequesterAccess.EvaluateAncestry(token, path);
            if (!ProtectedNodeReader.IsCanonicalLocalPath(path)) return RequesterModification.Unreadable;
            string[] segments = path[(seam.AncestryBase.Length + 1)..].Split('\\');
            string directory = seam.AncestryBase;
            ulong? volume = null;
            for (int index = 0; ; index++)
            {
                using (SafeFileHandle? handle = ProtectedNodeReader.TryOpen(directory, directory: true, out NativeNode node))
                {
                    if (handle is null || (volume is not null && volume != node.Identity.VolumeSerial)) return RequesterModification.Unreadable;
                    volume = node.Identity.VolumeSerial;
                    RequesterModification verdict = RequesterAccess.Evaluate(token, handle, directory: true, parentDirectory: null, AccessMode.Ancestor);
                    if (verdict != RequesterModification.Denied) return verdict;
                }
                if (index == segments.Length - 1) return RequesterModification.Denied;
                directory += '\\' + segments[index];
            }
        }

        /// <summary>Reopens a walked node read-share-only and requires the same file identity the walk proved.</summary>
        private static SafeFileHandle? Reopen(NativeNode node)
        {
            SafeFileHandle? handle = ProtectedNodeReader.TryOpen(node.FullPath, node.Directory, out NativeNode reopened);
            if (handle is null) return null;
            if (reopened.Identity == node.Identity && reopened.Directory == node.Directory) return handle;
            handle.Dispose();
            return null;
        }

        private NativeNode[] Children(string relativeDirectory) =>
            [.. nodes.Where(node => Parent(node.RelativePath).Equals(relativeDirectory, StringComparison.OrdinalIgnoreCase) && node.RelativePath.Length != 0)];

        private static string Parent(string relative)
        {
            int slash = relative.LastIndexOf('\\');
            return slash < 0 ? string.Empty : relative[..slash];
        }

        private static string Leaf(string relative) => relative[(relative.LastIndexOf('\\') + 1)..];

    }
}
