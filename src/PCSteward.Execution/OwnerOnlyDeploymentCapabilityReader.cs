using System.Collections.ObjectModel;
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
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        this.role = role;
        this.resolveLayout = resolveLayout ?? throw new ArgumentNullException(nameof(resolveLayout));
        this.protection = protection ?? throw new ArgumentNullException(nameof(protection));
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
        string[] leafPaths = [.. DeploymentCapabilityPolicy.BindingSet.Select(leaf => Path.Combine(installRoot, leaf))];
        string[] presentLeaves = [.. leafPaths.Where(File.Exists)];
        if (!protection(installRoot, presentLeaves)) return HelperProtocolCapabilityStatus.Unreadable;
        string capabilityPath = Path.Combine(installRoot, DeploymentCapabilityPolicy.CapabilityFileName);
        byte[]? capability = null;
        if (File.Exists(capabilityPath))
        {
            if (!Hold(held, DeploymentCapabilityPolicy.CapabilityFileName, capabilityPath)) return HelperProtocolCapabilityStatus.Unreadable;
            // One byte over the statement limit is read so an oversized statement reaches the policy and is refused there.
            capability = DeploymentFileAccess.TryReadBounded(held[DeploymentCapabilityPolicy.CapabilityFileName], DeploymentCapabilityPolicy.MaximumCapabilityBytes + 1);
            if (capability is null) return HelperProtocolCapabilityStatus.Mismatch;
        }
        inputs = inputs with { CandidateManifestBytes = manifest, CapabilityBytes = capability };
        if (!DeploymentCapabilityPolicy.PassesThrough(inputs, route, DeploymentCapabilityStage.Statement, out failure)) return failure;

        // Step 6: hold and hash every binding-set leaf, bounds first.
        if (presentLeaves.Length != leafPaths.Length) return HelperProtocolCapabilityStatus.Unreadable;
        Dictionary<string, DeploymentObservedFile> observed = new(StringComparer.Ordinal);
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        long total = 0;
        for (int index = 0; index < leafPaths.Length; index++)
        {
            string leaf = DeploymentCapabilityPolicy.BindingSet[index];
            paths[leaf] = leafPaths[index];
            if (!held.ContainsKey(leaf) && !Hold(held, leaf, leafPaths[index])) return HelperProtocolCapabilityStatus.Unreadable;
            if (!DeploymentFileAccess.TryHashBounded(held[leaf], DeploymentCapabilityPolicy.MaximumBindingFileBytes, out DeploymentObservedFile file))
                return HelperProtocolCapabilityStatus.Unreadable;
            total += file.Length;
            if (total > DeploymentCapabilityPolicy.MaximumBindingSetBytes) return HelperProtocolCapabilityStatus.Unreadable;
            observed[leaf] = file;
        }
        HelperProtocolCapabilityResult result = DeploymentCapabilityPolicy.Evaluate(inputs with { ObservedHashes = observed }, route);
        if (!result.IsSupported) return result.Status;

        binding = HeldHelperBinding.TryCreate(held, paths, observed, InstalledHelperLaunchTrust.HelperExecutableName);
        return binding is null ? HelperProtocolCapabilityStatus.Unreadable : HelperProtocolCapabilityStatus.Supported;
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
