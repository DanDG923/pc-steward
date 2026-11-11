using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>
/// The fixed locations the reader inspects. Production derives every member from <c>SpecialFolder.ProgramFiles</c>,
/// <c>Environment.ProcessPath</c> and the current Windows identity only. <see cref="InstallRoot"/> is also the exact
/// <c>installRoot</c> string the ADR-022 installer writes (<c>Join-Path $programFiles 'PC Steward'</c>).
/// </summary>
internal sealed record DeploymentCapabilityLayout(string InstallRoot, string StateRoot, string ProcessPath, string CurrentUserSid);

/// <summary>
/// True only when <paramref name="root"/> and every one of <paramref name="files"/> has no reparse component and cannot be
/// modified by the caller. Production uses the <see cref="ProtectedLaunchLayout"/> primitives.
/// </summary>
internal delegate bool DeploymentLayoutProtectionCheck(string root, IReadOnlyList<string> files);

/// <summary>
/// Task 50 design rev 6 §6 (H1, L6): which process reads the deployment capability. The role sets the expected process leaf
/// (<c>PCSteward.Desktop.exe</c> or <c>PCSteward.ElevatedHelper.exe</c>) and the protection principal: the Desktop role keeps
/// the ADR-018 caller-token check; the Helper role is constructed only with the requester check
/// (<c>IChocolateyHelperItem.RequesterCannotModify</c>), because the elevated helper's own token can write Program Files.
/// </summary>
internal enum DeploymentCapabilityRole
{
    Desktop,
    Helper
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; adapter gates §A.2, §A.4 steps 2–6 and step 3.1, §A.5: the Windows producer of
/// <see cref="IHelperProtocolCapability"/>. It reads the ADR-022 protected ledger, the pinned candidate manifest, the pinned
/// capability statement and the binding-set hashes through read-only, read-share-only handles, and hands the bytes to the
/// pure <see cref="DeploymentCapabilityPolicy"/>. Reads are bounded (1 MiB ledger and manifest, 1,024 B statement, 32 MiB
/// per binding file, 64 MiB per set); evaluation stops at the first failing stage; every error fails closed; nothing is
/// cached or persisted; no database, environment-variable, registry or remote input is read. The result is a consistency
/// gate, never eligibility evidence, and ADR-018 launch trust remains the security boundary.
/// <para>
/// ADR-034 §4: on an MSI release install the protected <c>PCSteward.ReleaseManifest.json</c> in the install root replaces the
/// ledger and candidate manifest (<see cref="ReleaseManifestPolicy"/>), and both held executables must carry the same compiled
/// release pin. A release manifest beside the ADR-022 state root is a mismatch. The class keeps its name until ADR-034 build
/// order step 7 retires the owner-only path.
/// </para>
/// </summary>
internal sealed class OwnerOnlyDeploymentCapabilityReader : IHelperProtocolCapability
{
    internal const string StateDirectoryName = "PC Steward OwnerOnlyTest State";
    internal const string LedgerFileName = "deployment.json";
    internal const string CandidateManifestFileName = "candidate-manifest.json";
    private static readonly IReadOnlyDictionary<string, DeploymentObservedFile> NoObservations = ReadOnlyDictionary<string, DeploymentObservedFile>.Empty;

    private readonly DeploymentCapabilityRole role;
    private readonly Func<DeploymentCapabilityLayout?> resolveLayout;
    private readonly DeploymentLayoutProtectionCheck protection;
    private readonly Func<string, string, bool> executablesCarrySamePin;

    internal OwnerOnlyDeploymentCapabilityReader() : this(ResolveFixedLayout, IsProtectedLayout)
    {
    }

    /// <summary>Test seam: fixture layouts and a substitute protection check. Production uses the parameterless constructor.</summary>
    internal OwnerOnlyDeploymentCapabilityReader(Func<DeploymentCapabilityLayout?> resolveLayout, DeploymentLayoutProtectionCheck protection)
        : this(DeploymentCapabilityRole.Desktop, resolveLayout, protection)
    {
    }

    /// <summary>
    /// The role-parameterized reader (§6). Helper role in production: <see cref="ResolveHelperLayout"/> and the item's
    /// requester check (<see cref="HelperDeploymentCapabilityCheck"/>); fixtures may substitute both.
    /// </summary>
    internal OwnerOnlyDeploymentCapabilityReader(DeploymentCapabilityRole role, Func<DeploymentCapabilityLayout?> resolveLayout,
        DeploymentLayoutProtectionCheck protection)
        : this(role, resolveLayout, protection, ExecutablesCarrySameCompiledPin)
    {
    }

    /// <summary>
    /// Test seam (ADR-034 §4): a substitute for the pinned-signature check of the two held executables, so unsigned fixture
    /// trees can reach every release-manifest stage. Production uses <see cref="ExecutablesCarrySameCompiledPin"/>.
    /// </summary>
    internal OwnerOnlyDeploymentCapabilityReader(DeploymentCapabilityRole role, Func<DeploymentCapabilityLayout?> resolveLayout,
        DeploymentLayoutProtectionCheck protection, Func<string, string, bool> executablesCarrySamePin)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        this.role = role;
        this.resolveLayout = resolveLayout ?? throw new ArgumentNullException(nameof(resolveLayout));
        this.protection = protection ?? throw new ArgumentNullException(nameof(protection));
        this.executablesCarrySamePin = executablesCarrySamePin ?? throw new ArgumentNullException(nameof(executablesCarrySamePin));
    }

    /// <summary>The process leaf this role must run as, inside the install root.</summary>
    internal static string ExpectedProcessLeaf(DeploymentCapabilityRole role) => role switch
    {
        DeploymentCapabilityRole.Desktop => InstalledHelperLaunchTrust.DesktopExecutableName,
        DeploymentCapabilityRole.Helper => InstalledHelperLaunchTrust.HelperExecutableName,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route)
    {
        HelperProtocolCapabilityResult result = TryHold(route, out HeldHelperBinding? binding);
        binding?.Dispose();
        return result;
    }

    public HelperProtocolCapabilityResult TryHold(UpdateRouteContract route, out HeldHelperBinding? binding)
    {
        binding = null;
        // A route the compiled allowlist can never contain needs no file I/O.
        if (!DeploymentCapabilityPolicy.CapabilityOperationAllowlist.Contains(route)) return Result(HelperProtocolCapabilityStatus.Mismatch);
        if (!OperatingSystem.IsWindows()) return Result(HelperProtocolCapabilityStatus.Unreadable);
        Dictionary<string, SafeFileHandle> held = new(StringComparer.Ordinal);
        try
        {
            HelperProtocolCapabilityStatus status = Read(route, held, out binding);
            return Result(status);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            binding?.Dispose();
            binding = null;
            return Result(HelperProtocolCapabilityStatus.Unreadable);
        }
        finally
        {
            if (binding is null)
                foreach (SafeFileHandle handle in held.Values) handle.Dispose();
        }
    }

    private HelperProtocolCapabilityStatus Read(UpdateRouteContract route, Dictionary<string, SafeFileHandle> held, out HeldHelperBinding? binding)
    {
        binding = null;
        // Step 2: fixed layout.
        DeploymentCapabilityLayout? layout = resolveLayout();
        if (layout is null || string.IsNullOrEmpty(layout.InstallRoot) || string.IsNullOrEmpty(layout.StateRoot) ||
            string.IsNullOrEmpty(layout.ProcessPath) || string.IsNullOrEmpty(layout.CurrentUserSid))
            return HelperProtocolCapabilityStatus.Unreadable;
        string installRoot = Path.GetFullPath(layout.InstallRoot);
        string stateRoot = Path.GetFullPath(layout.StateRoot);
        if (!string.Equals(Path.GetFullPath(layout.ProcessPath), Path.Combine(installRoot, ExpectedProcessLeaf(role)),
                StringComparison.OrdinalIgnoreCase))
            return HelperProtocolCapabilityStatus.Unreadable;

        // ADR-034 §4: an MSI release install carries the release manifest and never the ADR-022 state root (the MSI refuses to
        // install beside it). Both present is inconsistent; the manifest alone selects the release path.
        string releaseManifestPath = Path.Combine(installRoot, ReleaseManifestPolicy.FileName);
        bool releasePresent = File.Exists(releaseManifestPath);
        if (releasePresent && Directory.Exists(stateRoot)) return HelperProtocolCapabilityStatus.Mismatch;
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        Dictionary<string, DeploymentObservedFile> observed = new(StringComparer.Ordinal);
        string? ledgerSha256 = null;
        HelperProtocolCapabilityStatus status = releasePresent
            ? ReadRelease(route, installRoot, releaseManifestPath, held, paths, observed)
            : ReadOwnerOnly(route, layout, installRoot, stateRoot, held, paths, observed, out ledgerSha256);
        if (status != HelperProtocolCapabilityStatus.Supported) return status;

        // Probe spec rev 4 §5.3 pin sources (review M-1, D9): values only, for the Desktop role, and never part of the status.
        // The signer is read while the apphost is held. A release install records no ledger hash.
        HelperSignerFacts? signer = role == DeploymentCapabilityRole.Desktop ? TryReadSigner(paths[InstalledHelperLaunchTrust.HelperExecutableName]) : null;
        binding = HeldHelperBinding.TryCreate(held, paths, observed, InstalledHelperLaunchTrust.HelperExecutableName, ledgerSha256, signer);
        return binding is null ? HelperProtocolCapabilityStatus.Unreadable : HelperProtocolCapabilityStatus.Supported;
    }

    /// <summary>The ADR-022 owner-only path: ledger, pinned candidate manifest, capability statement and binding-set hashes.</summary>
    private HelperProtocolCapabilityStatus ReadOwnerOnly(UpdateRouteContract route, DeploymentCapabilityLayout layout, string installRoot,
        string stateRoot, Dictionary<string, SafeFileHandle> held, Dictionary<string, string> paths,
        Dictionary<string, DeploymentObservedFile> observed, out string? ledgerSha256)
    {
        ledgerSha256 = null;
        string ledgerPath = Path.Combine(stateRoot, LedgerFileName);
        string manifestPath = Path.Combine(stateRoot, CandidateManifestFileName);
        if (!Directory.Exists(stateRoot) || !File.Exists(ledgerPath)) return HelperProtocolCapabilityStatus.Absent;
        bool manifestPresent = File.Exists(manifestPath);
        if (!protection(stateRoot, manifestPresent ? [ledgerPath, manifestPath] : [ledgerPath])) return HelperProtocolCapabilityStatus.Unreadable;

        // Step 3: ledger.
        byte[]? ledger = ReadWhole(ledgerPath, DeploymentCapabilityPolicy.MaximumLedgerBytes);
        if (ledger is null) return HelperProtocolCapabilityStatus.Unreadable;
        DeploymentCapabilityInputs inputs = new(ledger, null, null, layout.CurrentUserSid, layout.InstallRoot, NoObservations);
        if (!DeploymentCapabilityPolicy.PassesThrough(inputs, route, DeploymentCapabilityStage.Ledger, out HelperProtocolCapabilityStatus failure))
            return failure;

        // Steps 4–5: pinned candidate manifest and the capability statement (read through its held handle).
        byte[]? manifest = null;
        if (manifestPresent && (manifest = ReadWhole(manifestPath, DeploymentCapabilityPolicy.MaximumCandidateManifestBytes)) is null)
            return HelperProtocolCapabilityStatus.Unreadable;
        HelperProtocolCapabilityStatus statement = ReadStatement(installRoot, held, out byte[]? capability);
        if (statement != HelperProtocolCapabilityStatus.Supported) return statement;
        inputs = inputs with { CandidateManifestBytes = manifest, CapabilityBytes = capability };
        if (!DeploymentCapabilityPolicy.PassesThrough(inputs, route, DeploymentCapabilityStage.Statement, out failure)) return failure;

        // Step 6: hold and hash every binding-set leaf, bounds first.
        HelperProtocolCapabilityStatus hashed = HoldBindingSet(installRoot, held, paths, observed);
        if (hashed != HelperProtocolCapabilityStatus.Supported) return hashed;
        HelperProtocolCapabilityResult result = DeploymentCapabilityPolicy.Evaluate(inputs with { ObservedHashes = observed }, route);
        if (!result.IsSupported) return result.Status;
        // The ledger hash is of the exact bytes the policy verified above (Desktop role only, values only).
        if (role == DeploymentCapabilityRole.Desktop) ledgerSha256 = Convert.ToHexString(SHA256.HashData(ledger));
        return HelperProtocolCapabilityStatus.Supported;
    }

    /// <summary>
    /// ADR-034 §4, the release path: the protected release manifest, the capability statement and binding-set hashes it
    /// lists, then the §3 pinned signatures of both held executables (same compiled pin). Same bounds and handles as the
    /// owner-only path; nothing else is read.
    /// </summary>
    private HelperProtocolCapabilityStatus ReadRelease(UpdateRouteContract route, string installRoot, string releaseManifestPath,
        Dictionary<string, SafeFileHandle> held, Dictionary<string, string> paths, Dictionary<string, DeploymentObservedFile> observed)
    {
        if (!protection(installRoot, [releaseManifestPath])) return HelperProtocolCapabilityStatus.Unreadable;
        byte[]? manifest = ReadWhole(releaseManifestPath, ReleaseManifestPolicy.MaximumManifestBytes);
        if (manifest is null) return HelperProtocolCapabilityStatus.Unreadable;
        ReleaseCapabilityInputs inputs = new(manifest, null, NoObservations, false);
        if (!ReleaseManifestPolicy.PassesThrough(inputs, route, DeploymentCapabilityStage.Ledger, out HelperProtocolCapabilityStatus failure))
            return failure;

        HelperProtocolCapabilityStatus statement = ReadStatement(installRoot, held, out byte[]? capability);
        if (statement != HelperProtocolCapabilityStatus.Supported) return statement;
        inputs = inputs with { CapabilityBytes = capability };
        if (!ReleaseManifestPolicy.PassesThrough(inputs, route, DeploymentCapabilityStage.Statement, out failure)) return failure;

        HelperProtocolCapabilityStatus hashed = HoldBindingSet(installRoot, held, paths, observed);
        if (hashed != HelperProtocolCapabilityStatus.Supported) return hashed;
        bool samePin = executablesCarrySamePin(
            paths[InstalledHelperLaunchTrust.DesktopExecutableName], paths[InstalledHelperLaunchTrust.HelperExecutableName]);
        return ReleaseManifestPolicy.Evaluate(inputs with { ObservedHashes = observed, ExecutablesCarrySamePin = samePin }, route).Status;
    }

    /// <summary>
    /// Protection of the install root and every present binding-set leaf, then the capability statement read through its held
    /// handle (null when absent; the policy decides). One byte over the statement limit is read so an oversized statement
    /// reaches the policy and is refused there.
    /// </summary>
    private HelperProtocolCapabilityStatus ReadStatement(string installRoot, Dictionary<string, SafeFileHandle> held, out byte[]? capability)
    {
        capability = null;
        string[] presentLeaves = [.. DeploymentCapabilityPolicy.BindingSet.Select(leaf => Path.Combine(installRoot, leaf)).Where(File.Exists)];
        if (!protection(installRoot, presentLeaves)) return HelperProtocolCapabilityStatus.Unreadable;
        string capabilityPath = Path.Combine(installRoot, DeploymentCapabilityPolicy.CapabilityFileName);
        if (!File.Exists(capabilityPath)) return HelperProtocolCapabilityStatus.Supported;
        if (!Hold(held, DeploymentCapabilityPolicy.CapabilityFileName, capabilityPath)) return HelperProtocolCapabilityStatus.Unreadable;
        capability = DeploymentFileAccess.TryReadBounded(held[DeploymentCapabilityPolicy.CapabilityFileName], DeploymentCapabilityPolicy.MaximumCapabilityBytes + 1);
        return capability is null ? HelperProtocolCapabilityStatus.Mismatch : HelperProtocolCapabilityStatus.Supported;
    }

    /// <summary>Holds and hashes every binding-set leaf (bounded per file and per set); any missing or unreadable leaf is Unreadable.</summary>
    private static HelperProtocolCapabilityStatus HoldBindingSet(string installRoot, Dictionary<string, SafeFileHandle> held,
        Dictionary<string, string> paths, Dictionary<string, DeploymentObservedFile> observed)
    {
        long total = 0;
        foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
        {
            string path = Path.Combine(installRoot, leaf);
            if (!File.Exists(path)) return HelperProtocolCapabilityStatus.Unreadable;
            paths[leaf] = path;
            if (!held.ContainsKey(leaf) && !Hold(held, leaf, path)) return HelperProtocolCapabilityStatus.Unreadable;
            if (!DeploymentFileAccess.TryHashBounded(held[leaf], DeploymentCapabilityPolicy.MaximumBindingFileBytes, out DeploymentObservedFile file))
                return HelperProtocolCapabilityStatus.Unreadable;
            total += file.Length;
            if (total > DeploymentCapabilityPolicy.MaximumBindingSetBytes) return HelperProtocolCapabilityStatus.Unreadable;
            observed[leaf] = file;
        }
        return HelperProtocolCapabilityStatus.Supported;
    }

    /// <summary>
    /// Production ADR-034 §3 check for a release install: both held executables carry the same compiled release pin. The
    /// files are held share-read, so the paths name the held files.
    /// </summary>
    internal static bool ExecutablesCarrySameCompiledPin(string desktopPath, string helperPath) =>
        AuthenticodeIdentity.HaveSamePinnedSigner(desktopPath, helperPath, ReleaseSigningPins.Compiled);

    /// <summary>
    /// The embedded Authenticode signing certificate's SHA-1 thumbprint and <c>NotAfter</c> (UTC) of a held file, or null when
    /// the file carries no readable embedded signature. Path-based (as <c>AuthenticodeIdentity.HaveSameSigner</c>); the caller
    /// holds the file share-read, so the path names the held file. No trust decision is made here.
    /// </summary>
    private static HelperSignerFacts? TryReadSigner(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using X509Certificate signed = X509Certificate.CreateFromSignedFile(path);
            using X509Certificate2 certificate = new(signed);
#pragma warning restore SYSLIB0057
            string thumbprint = certificate.Thumbprint;
            if (thumbprint.Length != 40 || !thumbprint.All(static c => c is >= '0' and <= '9' or >= 'A' and <= 'F')) return null;
            return new(thumbprint, new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The values are never part of the status: no failure here may turn a Supported hold into an exception (Unreadable).
            return null;
        }
    }

    /// <summary>Production layout: fixed paths under Program Files, the actual process path and the current user SID.</summary>
    internal static DeploymentCapabilityLayout? ResolveFixedLayout()
    {
        if (!OperatingSystem.IsWindows()) return null;
        string? processPath = Environment.ProcessPath;
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(programFiles)) return null;
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string? sid = identity.User?.Value;
        if (string.IsNullOrEmpty(sid)) return null;
        return new(
            Path.Combine(programFiles, InstalledHelperLaunchTrust.InstallDirectoryName),
            Path.Combine(programFiles, StateDirectoryName),
            processPath,
            sid);
    }

    /// <summary>
    /// Production Helper-role layout (§6 L6): <c>AppContext.BaseDirectory</c> must equal
    /// <c>&lt;known-folder Program Files&gt;\PC Steward\</c> and the process path must be the helper apphost in it; the install
    /// and state roots are the same fixed paths the desktop reader uses. Null on any mismatch or failure.
    /// </summary>
    internal static DeploymentCapabilityLayout? ResolveHelperLayout()
    {
        if (!OperatingSystem.IsWindows()) return null;
        string? processPath = Environment.ProcessPath;
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(programFiles) || !Path.IsPathFullyQualified(programFiles))
            return null;
        string installRoot = Path.Combine(programFiles, InstalledHelperLaunchTrust.InstallDirectoryName);
        if (!string.Equals(AppContext.BaseDirectory, installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath(processPath), Path.Combine(installRoot, InstalledHelperLaunchTrust.HelperExecutableName),
                StringComparison.OrdinalIgnoreCase))
            return null;
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string? sid = identity.User?.Value;
        if (string.IsNullOrEmpty(sid)) return null;
        return new(installRoot, Path.Combine(programFiles, StateDirectoryName), processPath, sid);
    }

    /// <summary>Production protection: no reparse component and caller cannot modify (the ADR-018 launch-trust primitives).</summary>
    internal static bool IsProtectedLayout(string root, IReadOnlyList<string> files)
    {
        string[] list = [.. files];
        return ProtectedLaunchLayout.HasNoReparseComponents(root, list) && ProtectedLaunchLayout.CallerCannotModify(root, list);
    }

    private static bool Hold(Dictionary<string, SafeFileHandle> held, string leaf, string path)
    {
        SafeFileHandle? handle = DeploymentFileAccess.TryOpenShareRead(path);
        if (handle is null) return false;
        held.Add(leaf, handle);
        return true;
    }

    private static byte[]? ReadWhole(string path, int maximumBytes)
    {
        using SafeFileHandle? handle = DeploymentFileAccess.TryOpenShareRead(path);
        return handle is null ? null : DeploymentFileAccess.TryReadBounded(handle, maximumBytes);
    }

    private static HelperProtocolCapabilityResult Result(HelperProtocolCapabilityStatus status) => HelperProtocolCapabilityResult.For(status);
}
