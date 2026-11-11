using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, TM-03/04/12/13/26/27; ADR-032 revision 2 §6–§7: the Windows side of the elevated self-update session and the
/// finish copy. Fixed locations only: <c>%ProgramFiles%</c> is the known folder and the profile folder comes from the
/// administrator-only ProfileList key; no environment value is read. Every input file is opened once without following a
/// reparse point and must be a plain file with one data stream whose final path is the path asked for. Folders are created
/// new with their descriptor set at creation. Processes start from fixed System32 or work folder paths with no shell.
/// Everything fails closed: any exception is a refusal.
/// </summary>
internal sealed partial class WindowsSelfUpdateHost : ISelfUpdateSessionHost, ISelfUpdateFinishHost
{
    /// <summary>PC Steward's fixed MSI UpgradeCode (ADR-034, <c>packaging/msi/Package.wxs</c>).</summary>
    internal const string UpgradeCode = "{2AD17330-BAD5-46BE-A409-20814B26673C}";

    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\";
    private const string SystemDriveToken = @"%SystemDrive%\";
    private const int MaximumTreeEntries = 8192;
    private const int MaximumTreeDepth = 16;
    private const int CopyChunkBytes = 1024 * 1024;
    private const int MaximumHelperImageBytes = 64 * 1024 * 1024;
    private static readonly TimeSpan ClosePoll = TimeSpan.FromSeconds(1);

    private WindowsSelfUpdateHost(string programFiles)
    {
        ProgramFiles = programFiles;
        InstallRoot = Path.Combine(programFiles, InstalledHelperLaunchTrust.InstallDirectoryName);
    }

    public string ProgramFiles { get; }

    public string InstallRoot { get; }

    public string? ProcessPath => Environment.ProcessPath;

    /// <summary>The host over the known <c>%ProgramFiles%</c> folder, or null off Windows or when it cannot be resolved.</summary>
    internal static WindowsSelfUpdateHost? TryCreate()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.DoNotVerify);
            return string.IsNullOrWhiteSpace(programFiles) || !Path.IsPathFullyQualified(programFiles) ? null : new(Path.GetFullPath(programFiles));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    // ---- Session (ADR-032 revision 2 §6) ----

    public string? ProfileFolder(string userSid)
    {
        if (!OperatingSystem.IsWindows() || userSid is null || !SidPattern().IsMatch(userSid)) return null;
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ProfileListKey + userSid, writable: false);
            if (key?.GetValue("ProfileImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string raw || raw.Length is 0 or > 260)
                return null;
            string path = raw;
            if (raw.StartsWith(SystemDriveToken, StringComparison.OrdinalIgnoreCase))
            {
                // The one variable Windows writes here; the drive comes from the system directory, not the environment.
                string? drive = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(drive)) return null;
                path = drive + raw[SystemDriveToken.Length..];
            }
            if (path.Contains('%', StringComparison.Ordinal) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
                !string.Equals(Path.GetFullPath(path), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return null;
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    public bool CreateProtectedFolder(string path)
    {
        if (!OperatingSystem.IsWindows() || !IsUnderProgramFiles(path)) return false;
        IntPtr descriptor = IntPtr.Zero;
        try
        {
            if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(SelfUpdateAclPolicy.WorkFolderDescriptor, 1, out descriptor, IntPtr.Zero))
                return false;
            NativeMethods.SecurityAttributes attributes = new()
            {
                Length = Marshal.SizeOf<NativeMethods.SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = 0,
            };
            if (!NativeMethods.CreateDirectory(path, ref attributes)) return false;
            return IsProtectedWorkFolder(path);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
        finally
        {
            if (descriptor != IntPtr.Zero) NativeMethods.LocalFree(descriptor);
        }
    }

    public void RemoveOlderWorkFolders(string keepLeaf)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            List<(string Path, DateTime Created)> earlier = [];
            foreach (string folder in Directory.EnumerateDirectories(ProgramFiles, SelfUpdateInstallPolicy.WorkFolderPrefix + "*"))
            {
                string leaf = Path.GetFileName(folder);
                if (!SelfUpdateInstallPolicy.IsWorkFolderName(leaf) || string.Equals(leaf, keepLeaf, StringComparison.OrdinalIgnoreCase) ||
                    !IsProtectedWorkFolder(folder))
                    continue;
                earlier.Add((folder, Directory.GetCreationTimeUtc(folder)));
            }
            // The newest earlier folder keeps its result until the desktop has read it.
            foreach ((string folder, _) in earlier.OrderByDescending(entry => entry.Created).Skip(1))
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Best effort: a folder left behind is removed by a later update.
        }
    }

    public SelfUpdateCopyVerdict CopyInput(string source, string destination, long maximumBytes, string expectedSha256)
    {
        if (!OperatingSystem.IsWindows() || !IsUnderProgramFiles(destination)) return SelfUpdateCopyVerdict.Unreadable;
        // Security review M1: a copy that is not the approved file is removed again, so nothing the helper could read but the
        // requesting user could not stays in the work folder, which every user can read.
        bool created = false;
        SelfUpdateCopyVerdict verdict = SelfUpdateCopyVerdict.Unreadable;
        try
        {
            verdict = CopyCore(source, destination, maximumBytes, expectedSha256, ref created);
            return verdict;
        }
        finally
        {
            if (created && verdict != SelfUpdateCopyVerdict.Copied)
            {
                try { File.Delete(destination); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private SelfUpdateCopyVerdict CopyCore(string source, string destination, long maximumBytes, string expectedSha256, ref bool created)
    {
        try
        {
            using SafeFileHandle? input = OpenPlainFile(source);
            if (input is null) return SelfUpdateCopyVerdict.Unreadable;
            long length = RandomAccess.GetLength(input);
            if (length <= 0 || length > maximumBytes) return SelfUpdateCopyVerdict.Unreadable;
            string copiedHash;
            using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                created = true;
                byte[] buffer = new byte[CopyChunkBytes];
                long offset = 0;
                while (offset < length)
                {
                    int read = RandomAccess.Read(input, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)), offset);
                    if (read <= 0) return SelfUpdateCopyVerdict.Unreadable;
                    hash.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                    offset += read;
                }
                if (RandomAccess.Read(input, buffer.AsSpan(0, 1), offset) != 0) return SelfUpdateCopyVerdict.Changed;
                output.Flush(flushToDisk: true);
                copiedHash = Convert.ToHexString(hash.GetHashAndReset());
            }
            if (!string.Equals(copiedHash, expectedSha256, StringComparison.Ordinal) ||
                !string.Equals(HashFile(destination, maximumBytes), expectedSha256, StringComparison.Ordinal))
                return SelfUpdateCopyVerdict.Changed;
            return SelfUpdateCopyVerdict.Copied;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return SelfUpdateCopyVerdict.Unreadable;
        }
    }

    public byte[]? ReadFile(string path, int maximumBytes)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using SafeFileHandle? handle = OpenPlainFile(path);
            return handle is null ? null : DeploymentFileAccess.TryReadBounded(handle, maximumBytes);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    public string? HashFile(string path, long maximumBytes)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using SafeFileHandle? handle = OpenPlainFile(path);
            return handle is not null && DeploymentFileAccess.TryHashBounded(handle, maximumBytes, out DeploymentObservedFile observed) ? observed.Sha256 : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    public async Task<HelperGitHubVerdict> ConfirmAsync(long runId, string sourceCommit, string checkZipSha256, CancellationToken cancellationToken)
    {
        try
        {
            using HelperGitHubConfirmation confirmation = new();
            return await confirmation.ConfirmAsync(runId, sourceCommit, checkZipSha256, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            return HelperGitHubVerdict.CouldNotReach;
        }
    }

    public bool CarriesCompiledPin(string path)
    {
        try { return AuthenticodeIdentity.TryReadPinnedSigner(path, ReleaseSigningPins.Compiled, out string? pin) && pin is not null; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    public MsiDatabaseSnapshot? ReadMsiTables(string msiPath)
    {
        try { return MsiTableDigest.Read(msiPath); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }

    public async Task<int?> RunMsiexecAsync(IReadOnlyList<string> arguments, string workFolder, TimeSpan limit, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !IsProtectedWorkFolder(workFolder)) return null;
        string system = Environment.SystemDirectory;
        ProcessStartInfo start = new(Path.Combine(system, "msiexec.exe"), MsiexecCommandLine(arguments))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = system,
        };
        // Security review M2: none of the environment this elevated process inherited from the user's registry reaches msiexec.
        start.Environment.Clear();
        foreach ((string name, string value) in CompiledEnvironment(workFolder)) start.Environment[name] = value;
        using Process? process = Process.Start(start);
        if (process is null) return null;
        using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(limit);
        try
        {
            await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException) { }
            return null;
        }
    }

    /// <summary>
    /// Security review M2: the whole environment of msiexec and the finish copy, built from the system directory and known
    /// folders (never from an environment value), with the temporary folder set to the protected work folder. An elevated
    /// process started through <c>runas</c> inherits the owner's <c>HKCU\Environment</c>, which any program running as the owner
    /// can write, so nothing of it is passed on.
    /// </summary>
    internal static IReadOnlyList<(string Name, string Value)> CompiledEnvironment(string workFolder)
    {
        string system = Environment.SystemDirectory;
        string windows = Path.GetDirectoryName(system) ?? throw new InvalidOperationException("No Windows folder.");
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.DoNotVerify);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolderOption.DoNotVerify);
        string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolderOption.DoNotVerify);
        string commonX86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86, Environment.SpecialFolderOption.DoNotVerify);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        (string Name, string Value)[] environment =
        [
            ("ALLUSERSPROFILE", programData), ("CommonProgramFiles", common), ("CommonProgramFiles(x86)", commonX86), ("CommonProgramW6432", common),
            ("ComSpec", Path.Combine(system, "cmd.exe")), ("PATH", string.Join(';', system, windows, Path.Combine(system, "Wbem"))), ("PATHEXT", ".COM;.EXE"),
            ("ProgramData", programData), ("ProgramFiles", programFiles), ("ProgramFiles(x86)", programFilesX86), ("ProgramW6432", programFiles),
            ("SystemDrive", (Path.GetPathRoot(windows) ?? string.Empty).TrimEnd('\\')), ("SystemRoot", windows), ("TEMP", workFolder), ("TMP", workFolder),
            ("windir", windows),
        ];
        if (environment.Any(entry => string.IsNullOrEmpty(entry.Value) || entry.Value.Contains('\0', StringComparison.Ordinal) || entry.Value.Contains('=', StringComparison.Ordinal)))
            throw new InvalidOperationException("A compiled environment value is empty or malformed.");
        return environment;
    }

    /// <summary>
    /// msiexec's own command-line form: options as given, a <c>NAME=value</c> property as <c>NAME="value"</c>, and any other
    /// argument quoted. No argument may hold a quote, so nothing can be split or injected.
    /// </summary>
    internal static string MsiexecCommandLine(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        StringBuilder line = new();
        foreach (string argument in arguments)
        {
            if (string.IsNullOrEmpty(argument) || argument.Contains('"', StringComparison.Ordinal) || argument.Any(char.IsControl))
                throw new ArgumentException("An msiexec argument is empty or holds a quote or control character.", nameof(arguments));
            if (line.Length > 0) line.Append(' ');
            Match property = PropertyPattern().Match(argument);
            if (argument.StartsWith('/') && !argument.Contains(' ', StringComparison.Ordinal)) line.Append(argument);
            else if (property.Success) line.Append(property.Groups["name"].Value).Append("=\"").Append(property.Groups["value"].Value).Append('"');
            else line.Append('"').Append(argument).Append('"');
        }
        return line.ToString();
    }

    public SelfUpdateImage? ReadImage(string imageRoot, string packageLeaf)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            if (!TryWalk(imageRoot, out List<string> files, out List<string> directories)) return null;
            string root = Path.GetFullPath(imageRoot).TrimEnd('\\') + "\\";
            string[] manifestFolders = [.. files
                .Where(file => string.Equals(Path.GetFileName(file), ReleaseManifestPolicy.FileName, StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetDirectoryName(file)!)];
            if (manifestFolders.Length != 1 || !directories.Contains(manifestFolders[0], StringComparer.OrdinalIgnoreCase)) return null;
            string installFolder = manifestFolders[0];
            string inside = installFolder.TrimEnd('\\') + "\\";
            List<SelfUpdateTreeFile> tree = [];
            foreach (string file in files)
            {
                if (string.Equals(file, root + packageLeaf, StringComparison.OrdinalIgnoreCase)) continue;
                if (!file.StartsWith(inside, StringComparison.OrdinalIgnoreCase)) return null;
                if (!TryHashTreeFile(installFolder, file, out SelfUpdateTreeFile? entry)) return null;
                tree.Add(entry!);
            }
            return tree.Count == 0 ? null : new(installFolder, tree);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    public bool BothCarrySameReleasePin(string first, string second)
    {
        try { return AuthenticodeIdentity.HaveSamePinnedSigner(first, second, ReleaseSigningPins.Compiled); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    public bool CopyOwnImage(string destination, string expectedSha256)
    {
        if (!OperatingSystem.IsWindows() || !IsUnderProgramFiles(destination) || Environment.ProcessPath is not { } self ||
            !string.Equals(Path.GetFullPath(self), Path.Combine(InstallRoot, InstalledHelperLaunchTrust.HelperExecutableName), StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            byte[]? image = ReadFile(self, MaximumHelperImageBytes);
            if (image is null || !string.Equals(Convert.ToHexString(SHA256.HashData(image)), expectedSha256, StringComparison.Ordinal)) return false;
            return WriteNewFile(destination, image);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    public bool WriteNewFile(string path, byte[] bytes)
    {
        if (!OperatingSystem.IsWindows() || bytes is null || !IsUnderProgramFiles(path)) return false;
        try
        {
            using (FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            byte[]? back = ReadFile(path, bytes.Length);
            return back is not null && back.AsSpan().SequenceEqual(bytes);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    public bool StartFinish(string helperCopyPath, string workFolderLeaf)
    {
        if (!OperatingSystem.IsWindows() || !IsUnderProgramFiles(helperCopyPath)) return false;
        try
        {
            char[] commandLine = (SelfUpdateHelperProtocol.FinishCommandLine(helperCopyPath, workFolderLeaf) + "\0").ToCharArray();
            // Security review M2: the finish copy gets the compiled environment, not the one this process inherited.
            StringBuilder block = new();
            foreach ((string name, string value) in CompiledEnvironment(Path.Combine(ProgramFiles, workFolderLeaf)).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
                block.Append(name).Append('=').Append(value).Append('\0');
            block.Append('\0');
            IntPtr environment = Marshal.StringToHGlobalUni(block.ToString());
            try
            {
                NativeMethods.StartupInfo startup = new() { Size = Marshal.SizeOf<NativeMethods.StartupInfo>() };
                if (!NativeMethods.CreateProcess(helperCopyPath, commandLine, IntPtr.Zero, IntPtr.Zero, inheritHandles: false,
                        NativeMethods.CreateNoWindow | NativeMethods.CreateUnicodeEnvironment, environment, Environment.SystemDirectory, ref startup,
                        out NativeMethods.ProcessInformation information))
                    return false;
                NativeMethods.CloseHandle(information.Thread);
                NativeMethods.CloseHandle(information.Process);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(environment);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    // ---- Finish copy (ADR-032 revision 2 §7) ----

    public bool IsProtectedWorkFolder(string path)
    {
        if (!OperatingSystem.IsWindows() || !IsUnderProgramFiles(path)) return false;
        try
        {
            return ProtectedLaunchLayout.HasNoReparseComponents(path) &&
                TryReadDescriptor(path, directory: true, owner: true, out string sddl) && SelfUpdateAclPolicy.IsProtectedWorkFolder(sddl);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    public async Task<bool> WaitForPcStewardToCloseAsync(int desktopProcessId, TimeSpan limit, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return false;
        long started = Stopwatch.GetTimestamp();
        string inside = InstallRoot.TrimEnd('\\') + "\\";
        while (true)
        {
            if (Closed(desktopProcessId, inside)) return true;
            if (Stopwatch.GetElapsedTime(started) >= limit) return false;
            await Task.Delay(ClosePoll, cancellationToken).ConfigureAwait(false);
        }
    }

    public IReadOnlyList<SelfUpdateTreeFile>? ReadInstalledTree()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            if (!ProtectedLaunchLayout.HasNoReparseComponents(InstallRoot) || !TryWalk(InstallRoot, out List<string> files, out _)) return null;
            List<SelfUpdateTreeFile> tree = [];
            foreach (string file in files)
            {
                if (!TryHashTreeFile(InstallRoot, file, out SelfUpdateTreeFile? entry)) return null;
                tree.Add(entry!);
            }
            return tree;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    public bool InstalledTreeIsProtected()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            if (!TryReadDescriptor(InstallRoot, directory: true, owner: false, out string root) || !SelfUpdateAclPolicy.IsProtectedInstallRoot(root) ||
                !TryWalk(InstallRoot, out List<string> files, out List<string> directories))
                return false;
            foreach (string directory in directories.Where(directory => !string.Equals(directory, Path.GetFullPath(InstallRoot), StringComparison.OrdinalIgnoreCase)))
                if (!TryReadDescriptor(directory, directory: true, owner: false, out string sddl) || !SelfUpdateAclPolicy.IsInheritedInstallEntry(sddl, directory: true))
                    return false;
            foreach (string file in files)
                if (!TryReadDescriptor(file, directory: false, owner: false, out string sddl) || !SelfUpdateAclPolicy.IsInheritedInstallEntry(sddl, directory: false))
                    return false;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    public IReadOnlyList<SelfUpdateRelatedProduct>? RelatedProducts()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            List<SelfUpdateRelatedProduct> products = [];
            for (uint index = 0; index < 64; index++)
            {
                char[] code = new char[39];
                uint status = NativeMethods.MsiEnumRelatedProducts(UpgradeCode, 0, index, code);
                if (status == NativeMethods.ErrorNoMoreItems) return products;
                if (status != 0) return null;
                string productCode = new string(code).TrimEnd('\0');
                char[] version = new char[64];
                uint length = (uint)version.Length;
                if (NativeMethods.MsiGetProductInfo(productCode, "VersionString", version, ref length) != 0 || length >= version.Length) return null;
                products.Add(new(productCode, new string(version, 0, (int)length)));
            }
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    public long BootTimeUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (Environment.TickCount64 / 1000);

    // ---- Shared primitives ----

    private bool IsUnderProgramFiles(string path) =>
        !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) &&
        Path.GetFullPath(path).StartsWith(ProgramFiles.TrimEnd('\\') + "\\" + SelfUpdateInstallPolicy.WorkFolderPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Opens an existing file for reading (read sharing only) without following a reparse point, and keeps it only when it is
    /// a plain disk file (no reparse point, not a directory) with exactly one data stream and one name (security review M1: no
    /// hard link to a file elsewhere) and its final path is <paramref name="path"/>, so no link anywhere on the path is followed.
    /// </summary>
    private static SafeFileHandle? OpenPlainFile(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return null;
        string full = Path.GetFullPath(path);
        SafeFileHandle handle = NativeMethods.CreateFile(full, NativeMethods.GenericRead, (uint)FileShare.Read, IntPtr.Zero, NativeMethods.OpenExisting,
            NativeMethods.FileFlagOpenReparsePoint | NativeMethods.FileFlagSequentialScan, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        try
        {
            if (NativeMethods.GetFileType(handle) == NativeMethods.FileTypeDisk &&
                NativeMethods.GetFileInformationByHandleEx(handle, NativeMethods.FileAttributeTagInfoClass, out NativeMethods.FileAttributeTagInfo tag,
                    (uint)Marshal.SizeOf<NativeMethods.FileAttributeTagInfo>()) &&
                (tag.FileAttributes & (NativeMethods.FileAttributeReparsePoint | NativeMethods.FileAttributeDirectory)) == 0 &&
                NativeMethods.GetFileInformationByHandleEx(handle, NativeMethods.FileStandardInfoClass, out NativeMethods.FileStandardInfo standard,
                    (uint)Marshal.SizeOf<NativeMethods.FileStandardInfo>()) &&
                standard.NumberOfLinks == 1 && standard.DeletePending == 0 && standard.Directory == 0 &&
                HasOneDataStream(handle) &&
                DeploymentFileAccess.TryGetFinalPath(handle, out string final) && string.Equals(final, full, StringComparison.OrdinalIgnoreCase))
                return handle;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        handle.Dispose();
        return null;
    }

    /// <summary><c>FileStreamInfo</c> lists exactly one stream, the unnamed <c>::$DATA</c>.</summary>
    private static bool HasOneDataStream(SafeFileHandle handle)
    {
        byte[] buffer = new byte[64 * 1024];
        if (!NativeMethods.GetFileInformationByHandleEx(handle, NativeMethods.FileStreamInfoClass, buffer, (uint)buffer.Length)) return false;
        uint next = BitConverter.ToUInt32(buffer, 0);
        int nameLength = (int)BitConverter.ToUInt32(buffer, 4);
        if (next != 0 || nameLength is <= 0 or > 64 || 24 + nameLength > buffer.Length) return false;
        return string.Equals(Encoding.Unicode.GetString(buffer, 24, nameLength), "::$DATA", StringComparison.Ordinal);
    }

    /// <summary>Walks a tree without following anything: a reparse point anywhere, too many entries or too deep refuses.</summary>
    private static bool TryWalk(string root, out List<string> files, out List<string> directories)
    {
        files = [];
        directories = [];
        string start = Path.GetFullPath(root).TrimEnd('\\');
        DirectoryInfo rootInfo = new(start);
        if (!rootInfo.Exists || (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        Stack<(string Path, int Depth)> pending = new();
        pending.Push((start, 0));
        directories.Add(start);
        EnumerationOptions options = new() { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false, ReturnSpecialDirectories = false };
        while (pending.Count > 0)
        {
            (string directory, int depth) = pending.Pop();
            foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
            {
                if (files.Count + directories.Count > MaximumTreeEntries || (entry.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    if (depth + 1 > MaximumTreeDepth) return false;
                    directories.Add(entry.FullName);
                    pending.Push((entry.FullName, depth + 1));
                }
                else files.Add(entry.FullName);
            }
        }
        return true;
    }

    private static bool TryHashTreeFile(string root, string file, out SelfUpdateTreeFile? entry)
    {
        entry = null;
        string relative = Path.GetRelativePath(root, file);
        if (!ReleaseManifestPolicy.IsRelativeInstallPath(relative) && !string.Equals(relative, ReleaseManifestPolicy.FileName, StringComparison.Ordinal)) return false;
        using SafeFileHandle? handle = OpenPlainFile(file);
        if (handle is null || !DeploymentFileAccess.TryHashBounded(handle, SelfUpdateFinish.MaximumInstalledFileBytes, out DeploymentObservedFile observed)) return false;
        entry = new(relative, observed.Length, observed.Sha256);
        return true;
    }

    /// <summary>The owner (optionally) and DACL of a file or folder as SDDL, read through a handle that does not follow a reparse point.</summary>
    private static bool TryReadDescriptor(string path, bool directory, bool owner, out string sddl)
    {
        sddl = string.Empty;
        uint flags = NativeMethods.FileFlagOpenReparsePoint | (directory ? NativeMethods.FileFlagBackupSemantics : 0);
        using SafeFileHandle handle = NativeMethods.CreateFile(Path.GetFullPath(path), NativeMethods.ReadControl | NativeMethods.FileReadAttributes,
            (uint)(FileShare.Read | FileShare.Write | FileShare.Delete), IntPtr.Zero, NativeMethods.OpenExisting, flags, IntPtr.Zero);
        if (handle.IsInvalid) return false;
        if (!NativeMethods.GetFileInformationByHandleEx(handle, NativeMethods.FileAttributeTagInfoClass, out NativeMethods.FileAttributeTagInfo tag,
                (uint)Marshal.SizeOf<NativeMethods.FileAttributeTagInfo>()) ||
            (tag.FileAttributes & NativeMethods.FileAttributeReparsePoint) != 0 ||
            ((tag.FileAttributes & NativeMethods.FileAttributeDirectory) != 0) != directory)
            return false;
        uint information = NativeMethods.DaclSecurityInformation | (owner ? NativeMethods.OwnerSecurityInformation : 0);
        IntPtr descriptor = IntPtr.Zero;
        IntPtr text = IntPtr.Zero;
        try
        {
            if (NativeMethods.GetSecurityInfo(handle, NativeMethods.SeFileObject, information, out _, out _, out _, out _, out descriptor) != 0 ||
                !NativeMethods.ConvertSecurityDescriptorToStringSecurityDescriptor(descriptor, 1, information, out text, out _))
                return false;
            sddl = Marshal.PtrToStringUni(text) ?? string.Empty;
            return sddl.Length > 0;
        }
        finally
        {
            if (text != IntPtr.Zero) NativeMethods.LocalFree(text);
            if (descriptor != IntPtr.Zero) NativeMethods.LocalFree(descriptor);
        }
    }

    /// <summary>The desktop PID no longer names a running PC Steward desktop and no running process has an image under the install root.</summary>
    private static bool Closed(int desktopProcessId, string inside)
    {
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                string? image = ImagePath(process.Id);
                if (image is null) continue;
                if (image.StartsWith(inside, StringComparison.OrdinalIgnoreCase)) return false;
                if (process.Id == desktopProcessId &&
                    string.Equals(Path.GetFileName(image), InstalledHelperLaunchTrust.DesktopExecutableName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        return true;
    }

    private static string? ImagePath(int processId)
    {
        using SafeProcessHandle handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid) return null;
        char[] buffer = new char[32768];
        uint length = (uint)buffer.Length;
        return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref length) && length > 0 ? new string(buffer, 0, (int)length) : null;
    }

    [GeneratedRegex("^S-1-5-21-[0-9]{1,10}-[0-9]{1,10}-[0-9]{1,10}-[0-9]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex SidPattern();

    [GeneratedRegex("^(?<name>[A-Z][A-Z0-9_]*)=(?<value>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyPattern();

    private static class NativeMethods
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint ReadControl = 0x00020000;
        internal const uint FileReadAttributes = 0x0080;
        internal const uint OpenExisting = 3;
        internal const uint FileFlagOpenReparsePoint = 0x00200000;
        internal const uint FileFlagBackupSemantics = 0x02000000;
        internal const uint FileFlagSequentialScan = 0x08000000;
        internal const uint FileAttributeDirectory = 0x10;
        internal const uint FileAttributeReparsePoint = 0x400;
        internal const uint FileTypeDisk = 1;
        internal const int FileAttributeTagInfoClass = 9;
        internal const int FileStandardInfoClass = 1;
        internal const int FileStreamInfoClass = 7;
        internal const uint OwnerSecurityInformation = 0x1;
        internal const uint DaclSecurityInformation = 0x4;
        internal const int SeFileObject = 1;
        internal const uint ProcessQueryLimitedInformation = 0x1000;
        internal const uint CreateNoWindow = 0x08000000;
        internal const uint CreateUnicodeEnvironment = 0x00000400;
        internal const uint ErrorNoMoreItems = 259;

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileAttributeTagInfo
        {
            internal uint FileAttributes;
            internal uint ReparseTag;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileStandardInfo
        {
            internal long AllocationSize;
            internal long EndOfFile;
            internal uint NumberOfLinks;
            internal byte DeletePending;
            internal byte Directory;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SecurityAttributes
        {
            internal int Length;
            internal IntPtr SecurityDescriptor;
            internal int InheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct StartupInfo
        {
            internal int Size;
            internal IntPtr Reserved;
            internal IntPtr Desktop;
            internal IntPtr Title;
            internal int X;
            internal int Y;
            internal int XSize;
            internal int YSize;
            internal int XCountChars;
            internal int YCountChars;
            internal int FillAttribute;
            internal int Flags;
            internal short ShowWindow;
            internal short Reserved2Size;
            internal IntPtr Reserved2;
            internal IntPtr StdInput;
            internal IntPtr StdOutput;
            internal IntPtr StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation
        {
            internal IntPtr Process;
            internal IntPtr Thread;
            internal int ProcessId;
            internal int ThreadId;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint GetFileType(SafeFileHandle file);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out FileAttributeTagInfo information, uint bufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, [Out] byte[] information, uint bufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out FileStandardInfo information, uint bufferSize);

        [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateDirectory(string path, ref SecurityAttributes securityAttributes);

        [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor, uint revision, out IntPtr securityDescriptor, IntPtr size);

        [DllImport("advapi32.dll", EntryPoint = "ConvertSecurityDescriptorToStringSecurityDescriptorW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertSecurityDescriptorToStringSecurityDescriptor(IntPtr securityDescriptor, uint revision, uint information,
            out IntPtr text, out uint length);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint information, out IntPtr owner, out IntPtr group,
            out IntPtr dacl, out IntPtr sacl, out IntPtr securityDescriptor);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr LocalFree(IntPtr memory);

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcess(string applicationName, [In, Out] char[] commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory,
            ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] name, ref uint size);

        [DllImport("msi.dll", EntryPoint = "MsiEnumRelatedProductsW", CharSet = CharSet.Unicode)]
        internal static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, [Out] char[] productCode);

        [DllImport("msi.dll", EntryPoint = "MsiGetProductInfoW", CharSet = CharSet.Unicode)]
        internal static extern uint MsiGetProductInfo(string product, string property, [Out] char[] value, ref uint length);
    }
}
