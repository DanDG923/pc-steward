using System.Security.Cryptography;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-04/15, TM-12/26; ADR-032 revision 2 §5 and §8: the desktop's files for PC Steward's own update, all unelevated.
/// <list type="bullet">
/// <item>The per-user folder <c>%LocalAppData%\PCSteward\downloads\SelfUpdate</c> (inside the ADR-019 downloads root, so a
/// factory reset removes it): the MSI and check zip under names built from their SHA-256, and the desktop job file.</item>
/// <item>The first check of a downloaded MSI: the compiled pin, the structure rule and the tables digest. The helper repeats
/// all of it where a same-user program cannot interfere.</item>
/// <item>The §8 read when PC Steward opens: the job's protected work folder (descriptor, owner, no reparse point) and its two
/// files, this install's release check and manifest, the boot time and, after a restart, the read-only tree and DACL
/// check. Nothing here writes outside the per-user folder or needs elevation.</item>
/// </list>
/// </summary>
internal sealed class SelfUpdateDesktopFiles
{
    private readonly WindowsSelfUpdateHost machine;

    private SelfUpdateDesktopFiles(string folder, WindowsSelfUpdateHost machine)
    {
        Folder = folder;
        this.machine = machine;
    }

    /// <summary>The per-user self-update folder.</summary>
    internal string Folder { get; }

    /// <summary>The files over this user's folder and the known <c>%ProgramFiles%</c>, or null off Windows.</summary>
    internal static SelfUpdateDesktopFiles? TryCreate()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(local) || !Path.IsPathFullyQualified(local) || WindowsSelfUpdateHost.TryCreate() is not { } machine) return null;
            return new(Path.Combine(Path.GetFullPath(local), "PCSteward", "downloads", SelfUpdateDesktop.FolderName), machine);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    internal string MsiPath(string msiSha256) => Path.Combine(Folder, SelfUpdateInstallPolicy.MsiFileName(msiSha256));

    internal string CheckZipPath(string checkZipSha256) => Path.Combine(Folder, SelfUpdateInstallPolicy.CheckZipFileName(checkZipSha256));

    /// <summary>The work folder's install log, readable by administrators only.</summary>
    internal string InstallLogPath(SelfUpdateDesktopJob job) => Path.Combine(machine.ProgramFiles, job.WorkFolder, SelfUpdateDesktop.InstallLogName);

    // ---- Downloads (§5) ----

    /// <summary>A new partial file for a download; the caller writes to it and then calls <see cref="CommitDownload"/> or <see cref="Discard"/>.</summary>
    internal FileStream CreatePartial(out string partialPath)
    {
        EnsureFolder();
        partialPath = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".partial");
        return new FileStream(partialPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.SequentialScan,
        });
    }

    /// <summary>Moves a finished partial file to the name its SHA-256 gives. False (and the partial removed) on any failure.</summary>
    internal bool CommitDownload(string partialPath, string finalPath)
    {
        try
        {
            if (!IsInFolder(partialPath) || !IsInFolder(finalPath) || !ProtectedLaunchLayout.HasNoReparseComponents(Folder)) { Discard(partialPath); return false; }
            File.Move(partialPath, finalPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(partialPath);
            return false;
        }
    }

    /// <summary>Writes the check zip under its SHA-256 name.</summary>
    internal bool WriteCheckZip(byte[] bytes, string checkZipSha256)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), checkZipSha256, StringComparison.Ordinal)) return false;
        string partial = string.Empty;
        try
        {
            using (FileStream stream = CreatePartial(out partial)) stream.Write(bytes);
            return CommitDownload(partial, CheckZipPath(checkZipSha256));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(partial);
            return false;
        }
    }

    /// <summary>
    /// Deletes one file in the folder, ignoring a file already gone. The delete goes through a handle whose final path must be
    /// the checked path, so a junction or link put in place of the folder or any ancestor cannot redirect it.
    /// </summary>
    internal void Discard(string path)
    {
        if (!string.IsNullOrEmpty(path) && IsInFolder(path)) _ = WindowsSelfUpdateHost.DeleteExactFile(path);
    }

    /// <summary>§5: the downloaded MSI carries a compiled pin, passes the structure rule, and its tables give the record's digest.</summary>
    internal PcStewardUpdatePrecheck Precheck(string msiPath, string msiTablesSha256)
    {
        if (!IsInFolder(msiPath)) return PcStewardUpdatePrecheck.Unreadable;
        if (!machine.CarriesCompiledPin(msiPath)) return PcStewardUpdatePrecheck.NotPinned;
        if (machine.ReadMsiTables(msiPath) is not { } tables) return PcStewardUpdatePrecheck.Unreadable;
        if (MsiStructurePolicy.Check(tables.Tables, out _) != MsiStructureVerdict.Allowed) return PcStewardUpdatePrecheck.StructureRefused;
        return string.Equals(tables.Sha256, msiTablesSha256, StringComparison.Ordinal) ? PcStewardUpdatePrecheck.Passed : PcStewardUpdatePrecheck.TablesDiffer;
    }

    // ---- The desktop job ----

    internal SelfUpdateDesktopJob? ReadJob()
    {
        try
        {
            string path = Path.Combine(Folder, SelfUpdateDesktop.JobFileName);
            if (!File.Exists(path)) return null;
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > SelfUpdateDesktop.MaximumJobBytes) return null;
            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return SelfUpdateDesktop.TryReadJob(bytes, out SelfUpdateDesktopJob? job) ? job : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    internal bool WriteJob(SelfUpdateDesktopJob job)
    {
        string partial = string.Empty;
        try
        {
            byte[] bytes = SelfUpdateDesktop.WriteJob(job);
            using (FileStream stream = CreatePartial(out partial)) stream.Write(bytes);
            return CommitDownload(partial, Path.Combine(Folder, SelfUpdateDesktop.JobFileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Discard(partial);
            return false;
        }
    }

    /// <summary>Forgets a decided job: its job file and its two downloads. The protected work folder stays until the helper's next run.</summary>
    internal void Forget(SelfUpdateDesktopJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Discard(Path.Combine(Folder, SelfUpdateDesktop.JobFileName));
        Discard(MsiPath(job.MsiSha256));
        Discard(CheckZipPath(job.CheckZipSha256));
    }

    // ---- §8 ----

    /// <summary>Reads what §8 needs, once, and decides. <paramref name="runningVersion"/> is this process's MSI version.</summary>
    internal SelfUpdateJobVerdict Evaluate(SelfUpdateDesktopJob job, string runningVersion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(job);
        string workFolder = Path.Combine(machine.ProgramFiles, job.WorkFolder);
        bool intact;
        try { intact = new OwnerOnlyDeploymentCapabilityReader().EvaluateInstalledRelease().IsSupported; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { intact = false; }
        bool folderProtected = machine.IsProtectedWorkFolder(workFolder);
        SelfUpdateOutcomeInputs inputs = new(
            runningVersion,
            intact,
            machine.HashFile(Path.Combine(machine.InstallRoot, ReleaseManifestPolicy.FileName), ReleaseManifestPolicy.MaximumManifestBytes),
            folderProtected,
            folderProtected ? machine.ReadFile(Path.Combine(workFolder, SelfUpdateHelperProtocol.ResultFileName), SelfUpdateHelperProtocol.MaximumFileBytes) : null,
            folderProtected ? machine.ReadFile(Path.Combine(workFolder, SelfUpdateInstallPolicy.ExpectedInstallFileName), SelfUpdateInstallPolicy.MaximumExpectedBytes) : null,
            machine.BootTimeUnixSeconds(),
            expected => machine.ReadInstalledTree() is { } tree &&
                SelfUpdateInstallPolicy.CheckInstalled(expected, tree) == SelfUpdateTreeVerdict.Matches && machine.InstalledTreeIsProtected(),
            now);
        return SelfUpdateDesktop.Decide(job, inputs);
    }

    private void EnsureFolder()
    {
        Directory.CreateDirectory(Folder);
        if (!ProtectedLaunchLayout.HasNoReparseComponents(Folder)) throw new IOException("The self-update folder is a reparse point.");
    }

    private bool IsInFolder(string path) =>
        !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Folder, StringComparison.OrdinalIgnoreCase);
}
