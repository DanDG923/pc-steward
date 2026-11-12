namespace PCSteward.Execution;

/// <summary>A product installed under PC Steward's UpgradeCode: its product code (braced GUID) and version.</summary>
internal sealed record SelfUpdateRelatedProduct(string ProductCode, string Version);

/// <summary>
/// The machine side of the finish copy (ADR-032 revision 2 §7). Every member is a bounded, fail-closed Windows operation on
/// fixed paths; the Windows implementation is <see cref="WindowsSelfUpdateHost"/>.
/// </summary>
internal interface ISelfUpdateFinishHost
{
    string InstallRoot { get; }

    string ProgramFiles { get; }

    /// <summary>The running process's own image path.</summary>
    string? ProcessPath { get; }

    /// <summary>The folder carries the protected work folder descriptor, is owned by SYSTEM or Administrators and is no reparse point (nor any ancestor).</summary>
    bool IsProtectedWorkFolder(string path);

    /// <summary>The folder carries the administrators-only private folder descriptor (Mythos review S2), with the same owner and reparse rules.</summary>
    bool IsPrivateFolder(string path);

    byte[]? ReadFile(string path, int maximumBytes);

    /// <summary>SHA-256 (uppercase hex) of the file through a held handle, at most <paramref name="maximumBytes"/>; null otherwise.</summary>
    string? HashFile(string path, long maximumBytes);

    /// <summary>Waits until the desktop process has exited and no process runs an image under the install root. False when the limit passes first.</summary>
    Task<bool> WaitForPcStewardToCloseAsync(int desktopProcessId, TimeSpan limit, CancellationToken cancellationToken);

    /// <summary>As <see cref="ISelfUpdateSessionHost.RunMsiexecAsync"/>: compiled environment, temporary folder <paramref name="privateFolder"/>.</summary>
    Task<int?> RunMsiexecAsync(IReadOnlyList<string> arguments, string privateFolder, TimeSpan limit, CancellationToken cancellationToken);

    /// <summary>Every file under the install root (relative paths), hashed through held handles; null on a reparse point or any read failure.</summary>
    IReadOnlyList<SelfUpdateTreeFile>? ReadInstalledTree();

    /// <summary>The install root and every entry under it carry the ADR-034 protected DACL (explicit on the root, inherited below).</summary>
    bool InstalledTreeIsProtected();

    bool BothCarrySameReleasePin(string first, string second);

    /// <summary>
    /// Every product installed for the whole machine under PC Steward's UpgradeCode, from the administrator-only registration
    /// only (security review R2), or null when it cannot be read.
    /// </summary>
    IReadOnlyList<SelfUpdateRelatedProduct>? RelatedProducts();

    /// <summary>As <see cref="ISelfUpdateSessionHost.HasPerUserRegistration"/> (security review R1, Mythos review S1).</summary>
    bool HasPerUserRegistration(string productCode);

    /// <summary>
    /// True only when the administrator-only Windows Installer machine policy <c>DisableUserInstalls</c> is 1, so Windows
    /// Installer ignores every per-user registration (Mythos review S1); false when it is anything else or cannot be read.
    /// </summary>
    bool UserInstallsIgnored();

    /// <summary>
    /// Takes the machine-wide policy lock, sets <c>DisableUserInstalls</c> to 1 and returns the scope that puts the owner's own
    /// state back and releases the lock when disposed (owner's choice: only during PC Steward updates); null, with nothing
    /// changed, when another run holds the lock or the policy cannot be set.
    /// </summary>
    IDisposable? IgnoreUserInstalls();

    /// <summary>The current boot time, as Unix seconds (UTC).</summary>
    long BootTimeUnixSeconds();

    bool WriteNewFile(string path, byte[] bytes);
}

/// <summary>
/// FR-UPD-04/15, TM-03/04/12/13/26/27; ADR-032 revision 2 §7: the finish copy. It runs from the protected work folder's
/// <c>bin</c>, never from the install root it replaces, and acts only on the job its own folder holds:
/// <list type="number">
/// <item>it refuses (exit <see cref="Refused"/>, nothing written) unless it runs elevated as exactly
/// <c>%ProgramFiles%\&lt;leaf&gt;\bin\PCSteward.ElevatedHelper.exe</c>, the work folder and <c>bin</c> are protected, its
/// <c>private</c> folder (MSI copy, logs, temporary files) is administrators-only, and
/// <c>job.json</c> and <c>expected-install.json</c> parse and name the same job, and the job has no <c>result.json</c> yet;</item>
/// <item>it waits at most <see cref="CloseWait"/> for PC Steward to close;</item>
/// <item>it hashes the MSI copy again and runs <c>msiexec /i</c> (at most <see cref="InstallLimit"/>);</item>
/// <item>it refuses while the owner's own registry registers the product or PC Steward's UpgradeCode (security review R1);</item>
/// <item>on exit 0 it checks the installed tree, its DACLs, the executables' pin, that no per-user registration appeared and
/// that the machine registration lists exactly one PC Steward product, this MSI's own, at the new version; on any difference
/// it uninstalls that product only (security review R2); 3010 and 1641 record a needed restart with the boot time; any other
/// code is a failed (rolled back) install;</item>
/// <item>it writes <c>result.json</c> and launches nothing.</item>
/// </list>
/// </summary>
internal static class SelfUpdateFinish
{
    internal const int Refused = 20;
    internal const int ResultWritten = 0;
    internal const int ResultNotWritten = 30;
    internal static readonly TimeSpan CloseWait = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan InstallLimit = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan UninstallLimit = TimeSpan.FromMinutes(30);
    internal const long MaximumInstalledFileBytes = 512L * 1024 * 1024;

    internal static async Task<int> RunAsync(string workFolderLeaf, bool elevated, ISelfUpdateFinishHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!elevated || !SelfUpdateInstallPolicy.IsWorkFolderName(workFolderLeaf)) return Refused;
        string workFolder;
        SelfUpdateJobFile job;
        SelfUpdateExpectedInstall expected;
        try
        {
            if (string.IsNullOrEmpty(host.ProgramFiles) || !Path.IsPathFullyQualified(host.ProgramFiles)) return Refused;
            workFolder = Path.Combine(host.ProgramFiles, workFolderLeaf);
            string bin = Path.Combine(workFolder, SelfUpdateHelperProtocol.HelperCopyFolder);
            if (host.ProcessPath is not { } self ||
                !string.Equals(Path.GetFullPath(self), Path.Combine(bin, InstalledHelperLaunchTrust.HelperExecutableName), StringComparison.OrdinalIgnoreCase) ||
                !host.IsProtectedWorkFolder(workFolder) || !host.IsProtectedWorkFolder(bin) ||
                !host.IsPrivateFolder(Path.Combine(workFolder, SelfUpdateHelperProtocol.PrivateFolder)))
                return Refused;
            if (host.ReadFile(Path.Combine(workFolder, SelfUpdateHelperProtocol.JobFileName), SelfUpdateHelperProtocol.MaximumFileBytes) is not { } jobBytes ||
                !SelfUpdateHelperProtocol.TryReadJob(jobBytes, out SelfUpdateJobFile? parsedJob) || parsedJob is null ||
                host.ReadFile(Path.Combine(workFolder, SelfUpdateInstallPolicy.ExpectedInstallFileName), SelfUpdateInstallPolicy.MaximumExpectedBytes) is not { } expectedBytes ||
                !SelfUpdateInstallPolicy.TryReadExpected(expectedBytes, out SelfUpdateExpectedInstall? parsedExpected) || parsedExpected is null ||
                parsedJob.JobId != parsedExpected.JobId || !string.Equals(parsedJob.ToVersion, parsedExpected.ToVersion, StringComparison.Ordinal) ||
                !string.Equals(SelfUpdateInstallPolicy.WorkFolderName(parsedJob.JobId), workFolderLeaf, StringComparison.Ordinal))
                return Refused;
            // Security review L2: one run per job. A job that already has a result is never installed again.
            if (host.ReadFile(Path.Combine(workFolder, SelfUpdateHelperProtocol.ResultFileName), SelfUpdateHelperProtocol.MaximumFileBytes) is not null)
                return Refused;
            job = parsedJob;
            expected = parsedExpected;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Refused;
        }

        SelfUpdateResultFile result;
        try
        {
            result = await InstallAsync(workFolder, job, expected, host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result = Result(job, expected, SelfUpdateFinishOutcome.RefusedBeforeInstall, null, "internal-fault");
        }
        try
        {
            return host.WriteNewFile(Path.Combine(workFolder, SelfUpdateHelperProtocol.ResultFileName), SelfUpdateHelperProtocol.WriteResult(result))
                ? ResultWritten : ResultNotWritten;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ResultNotWritten;
        }
    }

    private static async Task<SelfUpdateResultFile> InstallAsync(string workFolder, SelfUpdateJobFile job, SelfUpdateExpectedInstall expected,
        ISelfUpdateFinishHost host, CancellationToken cancellationToken)
    {
        // Step 2: PC Steward must have closed, so the MSI replaces no running file.
        if (!await host.WaitForPcStewardToCloseAsync(job.DesktopProcessId, CloseWait, cancellationToken).ConfigureAwait(false))
            return Result(job, expected, SelfUpdateFinishOutcome.PcStewardDidNotClose, null, "pc-steward-did-not-close");

        // Step 3: the MSI copy is still the approved one.
        string privateFolder = Path.Combine(workFolder, SelfUpdateHelperProtocol.PrivateFolder);
        string msiPath = Path.Combine(privateFolder, SelfUpdateInstallPolicy.MsiFileName(job.MsiSha256));
        if (!string.Equals(host.HashFile(msiPath, SelfUpdateSession.MaximumMsiBytes), job.MsiSha256, StringComparison.Ordinal))
            return Result(job, expected, SelfUpdateFinishOutcome.RefusedBeforeInstall, null, "msi-changed");

        // Mythos review S1: no msiexec unless Windows Installer ignores per-user registrations (the DisableUserInstalls machine
        // policy). By the owner's choice the finish copy sets it for its own install, check and uninstall only, and the scope
        // puts the owner's state back when the run ends. Security review R1: nor while the owner's own registry registers this
        // product or PC Steward's UpgradeCode.
        using IDisposable? policy = host.IgnoreUserInstalls();
        if (policy is null || !host.UserInstallsIgnored())
            return Result(job, expected, SelfUpdateFinishOutcome.RefusedBeforeInstall, null, "user-installs-allowed");
        if (host.HasPerUserRegistration(job.ProductCode))
            return Result(job, expected, SelfUpdateFinishOutcome.RefusedBeforeInstall, null, "per-user-registration");

        // Step 4: the install. Windows Installer rolls a failed major upgrade back to the old version.
        int? exit = await host.RunMsiexecAsync(["/i", msiPath, "/qn", "/norestart", "/l*v", Path.Combine(privateFolder, SelfUpdateHelperProtocol.InstallLogName)],
            privateFolder, InstallLimit, cancellationToken).ConfigureAwait(false);
        if (exit is null) return Result(job, expected, SelfUpdateFinishOutcome.InstallFailed, null, "msiexec-did-not-finish");
        if (exit is 3010 or 1641)
            return Result(job, expected, SelfUpdateFinishOutcome.RestartNeeded, exit, "restart-needed") with { BootTimeUnixSeconds = host.BootTimeUnixSeconds() };
        if (exit != 0) return Result(job, expected, SelfUpdateFinishOutcome.InstallFailed, exit, "install-failed");

        // Step 5: exactly the expected tree, protected, pinned, and one machine product: this MSI's, at the new version.
        string? difference = Difference(job, expected, host, out bool installed);
        if (difference is null) return Result(job, expected, SelfUpdateFinishOutcome.Installed, exit, "installed");
        // Security review R2: only this MSI's own product is ever removed, and only when the machine registration lists it.
        if (!installed) return Result(job, expected, SelfUpdateFinishOutcome.MismatchUninstallFailed, exit, difference + "; no-product-to-remove");
        if (!host.UserInstallsIgnored() || host.HasPerUserRegistration(job.ProductCode))
            return Result(job, expected, SelfUpdateFinishOutcome.MismatchUninstallFailed, exit, difference + "; uninstall-refused");
        int? removed = await host.RunMsiexecAsync(["/x", job.ProductCode, "/qn", "/norestart", "/l*v", Path.Combine(privateFolder, SelfUpdateHelperProtocol.UninstallLogName)],
            privateFolder, UninstallLimit, cancellationToken).ConfigureAwait(false);
        return removed is 0 or 3010 or 1641
            ? Result(job, expected, SelfUpdateFinishOutcome.MismatchUninstalled, exit, difference)
            : Result(job, expected, SelfUpdateFinishOutcome.MismatchUninstallFailed, exit, difference + "; uninstall-failed");
    }

    /// <summary>
    /// Null when the install matches; otherwise a fixed code. <paramref name="installed"/> says whether the machine registration
    /// lists this MSI's own product, the only one the finish copy may remove.
    /// </summary>
    private static string? Difference(SelfUpdateJobFile job, SelfUpdateExpectedInstall expected, ISelfUpdateFinishHost host, out bool installed)
    {
        IReadOnlyList<SelfUpdateRelatedProduct>? related = host.RelatedProducts();
        installed = related is not null && related.Any(entry => string.Equals(entry.ProductCode, job.ProductCode, StringComparison.OrdinalIgnoreCase));
        if (related is null || related.Count != 1 || !installed || !string.Equals(related[0].Version, expected.ToVersion, StringComparison.Ordinal))
            return "product-registration-differs";
        if (host.HasPerUserRegistration(job.ProductCode)) return "per-user-registration";
        IReadOnlyList<SelfUpdateTreeFile>? tree = host.ReadInstalledTree();
        if (tree is null) return "installed-tree-unreadable";
        SelfUpdateTreeVerdict verdict = SelfUpdateInstallPolicy.CheckInstalled(expected, tree);
        if (verdict != SelfUpdateTreeVerdict.Matches) return "installed-tree-differs";
        if (!host.InstalledTreeIsProtected()) return "installed-dacl-differs";
        if (!host.BothCarrySameReleasePin(Path.Combine(host.InstallRoot, InstalledHelperLaunchTrust.DesktopExecutableName),
                Path.Combine(host.InstallRoot, InstalledHelperLaunchTrust.HelperExecutableName)))
            return "installed-executables-not-pinned";
        return null;
    }

    private static SelfUpdateResultFile Result(SelfUpdateJobFile job, SelfUpdateExpectedInstall expected, SelfUpdateFinishOutcome outcome, int? exitCode, string detail) =>
        new(job.JobId, outcome, exitCode, job.ToVersion, expected.ManifestSha256, null, detail);
}
