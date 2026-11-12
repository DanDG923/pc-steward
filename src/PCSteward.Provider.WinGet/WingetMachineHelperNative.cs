using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// Phase D stage D4b design §5 H4: finds the App Installer <c>winget.exe</c> for the elevated helper <b>without WinRT</b> (ADR-033:
/// no helper code uses WinRT types). It asks the Win32 package API for the installed packages of the fixed family
/// <c>Microsoft.DesktopAppInstaller_8wekyb3d8bbwe</c> and their install folders, then keeps only a <c>winget.exe</c> under the machine
/// <c>Program Files\WindowsApps</c> root with no reparse point on the file or its folder and a trusted Microsoft signature (the same
/// rules as the desktop's <see cref="MachineTrustedWinget"/>). PATH, aliases and the environment are never consulted.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HelperTrustedWinget
{
    internal const string AppInstallerFamily = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";
    private const int ErrorInsufficientBuffer = 122;
    private const int MaximumPackages = 16;

    /// <summary>The trusted client path, or null when none qualifies.</summary>
    internal static string? TryResolve()
    {
        try
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(programFiles) || !Path.IsPathFullyQualified(programFiles)) return null;
            string root = Path.GetFullPath(Path.Combine(programFiles, "WindowsApps"));
            if (IsReparseOrMissing(root, directory: true)) return null;
            foreach (string folder in InstalledFolders().OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                string candidate = Path.GetFullPath(Path.Combine(folder, "winget.exe"));
                if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsReparseOrMissing(Path.GetDirectoryName(candidate)!, directory: true) || IsReparseOrMissing(candidate, directory: false)) continue;
                if (MachineTrustedWinget.IsMicrosoftTrusted(candidate)) return candidate;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            // Refused below.
        }
        return null;
    }

    /// <summary>The fixed family of the C++ runtime framework App Installer depends on (D4b design §5.1).</summary>
    internal const string RuntimeFamily = "Microsoft.VCLibs.140.00.UWPDesktop_8wekyb3d8bbwe";

    /// <summary>
    /// D4b design §5.1: <c>winget.exe</c> links the C++ runtime from the <see cref="RuntimeFamily"/> framework package, which the loader
    /// adds to the search path only for a process with package identity. The helper's <c>winget.exe</c> has none, so its fixed
    /// <c>PATH</c> names that framework's folder: found through the same package API, under the machine <c>WindowsApps</c> root, with
    /// no reparse point, the architecture of <paramref name="wingetPath"/>'s package, and the runtime DLL present. Null when none
    /// qualifies (then only an installed system-wide runtime can satisfy the loader, and otherwise WinGet does not start).
    /// </summary>
    internal static string? TryResolveRuntimeFolder(string wingetPath)
    {
        try
        {
            string? architecture = PackageArchitecture(Path.GetFileName(Path.GetDirectoryName(wingetPath)));
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.DoNotVerify);
            if (architecture is null || string.IsNullOrWhiteSpace(programFiles) || !Path.IsPathFullyQualified(programFiles)) return null;
            string root = Path.GetFullPath(Path.Combine(programFiles, "WindowsApps"));
            if (IsReparseOrMissing(root, directory: true)) return null;
            foreach (string folder in InstalledFolders(RuntimeFamily, "Microsoft.VCLibs.140.00.UWPDesktop_")
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                string full = Path.GetFullPath(folder);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(PackageArchitecture(Path.GetFileName(full)), architecture, StringComparison.Ordinal) ||
                    IsReparseOrMissing(full, directory: true) || IsReparseOrMissing(Path.Combine(full, "vcruntime140.dll"), directory: false)) continue;
                return full;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Refused below.
        }
        return null;
    }

    /// <summary>The architecture field of a package folder name (<c>Name_Version_Architecture_ResourceId_PublisherId</c>).</summary>
    internal static string? PackageArchitecture(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return null;
        string[] parts = folderName.Split('_');
        return parts.Length == 5 && parts[2] is "x64" or "x86" or "arm64" or "arm" or "neutral" ? parts[2] : null;
    }

    private static List<string> InstalledFolders() => InstalledFolders(AppInstallerFamily, "Microsoft.DesktopAppInstaller_");

    private static List<string> InstalledFolders(string family, string fullNamePrefix)
    {
        List<string> folders = [];
        uint count = 0, bufferLength = 0;
        int status = GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref bufferLength, IntPtr.Zero);
        if (status != ErrorInsufficientBuffer || count is 0 or > MaximumPackages || bufferLength is 0 or > 64 * 1024) return folders;
        IntPtr names = Marshal.AllocHGlobal(IntPtr.Size * (int)count);
        IntPtr buffer = Marshal.AllocHGlobal((int)bufferLength * sizeof(char));
        try
        {
            if (GetPackagesByPackageFamily(family, ref count, names, ref bufferLength, buffer) != 0) return folders;
            for (int index = 0; index < (int)count; index++)
            {
                string? fullName = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, index * IntPtr.Size));
                if (string.IsNullOrEmpty(fullName) || !fullName.StartsWith(fullNamePrefix, StringComparison.Ordinal) ||
                    !fullName.EndsWith("__8wekyb3d8bbwe", StringComparison.Ordinal)) continue;
                if (PackagePath(fullName) is { } path) folders.Add(path);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(names);
        }
        return folders;
    }

    private static string? PackagePath(string fullName)
    {
        uint length = 0;
        if (GetPackagePathByFullName(fullName, ref length, null) != ErrorInsufficientBuffer || length is 0 or > 4096) return null;
        char[] path = new char[length];
        return GetPackagePathByFullName(fullName, ref length, path) == 0 ? new string(path, 0, (int)length - 1) : null;
    }

    private static bool IsReparseOrMissing(string path, bool directory)
    {
        if (directory ? !Directory.Exists(path) : !File.Exists(path)) return true;
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    // Plain DllImport like the other helper P/Invoke (no unsafe code in this project), loaded only from System32.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetPackagesByPackageFamily(string packageFamilyName, ref uint count, IntPtr packageFullNames,
        ref uint bufferLength, IntPtr buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, [Out] char[]? path);
}

/// <summary>
/// D4b design §5 H6: reads the reviewed HKLM uninstall registration in the elevated helper (plain registry reads, no WinRT or COM)
/// and returns its comparison digest (<see cref="WinGetMachineRegistration.Digest"/>), or null when the key is missing, unreadable,
/// a system component, or has no usable <c>DisplayVersion</c>. The current user's hive is never read.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HelperMachineRegistration
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    internal static string? TryDigest(WinGetMachineRegistrationView view, string keyName, out string? displayVersion)
    {
        displayVersion = null;
        if (string.IsNullOrWhiteSpace(keyName) || keyName.Length > WinGetMachineRegistration.MaximumFieldLength ||
            keyName.IndexOfAny(['\\', '/', '\n', '\r', '\0']) >= 0) return null;
        try
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                view == WinGetMachineRegistrationView.Machine32 ? RegistryView.Registry32 : RegistryView.Registry64);
            using RegistryKey? uninstall = machine.OpenSubKey(UninstallPath, writable: false);
            using RegistryKey? entry = uninstall?.OpenSubKey(keyName, writable: false);
            if (entry is null) return null;
            if (entry.GetValue("SystemComponent") is int system && system == 1) return null;
            if (entry.GetValueKind("DisplayVersion") != RegistryValueKind.String || entry.GetValue("DisplayVersion") is not string version) return null;
            displayVersion = version;
            return WinGetMachineRegistration.Digest(view, keyName, version);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The most uninstall keys read per view; more refuses (an unbounded walk is never run elevated).</summary>
    internal const int MaximumKeysPerView = 4096;

    /// <summary>
    /// D4b design §5.1 H6: finds the one HKLM registration, in either view, whose digest equals <paramref name="registrationDigest"/>
    /// from the reviewed <c>wgm1</c> evidence. The digest binds the view, the key name and <c>DisplayVersion</c>, so the review's exact
    /// registration is found again without the request naming a registry path. False when there is no match, more than one, more
    /// keys than <see cref="MaximumKeysPerView"/>, or any read fails.
    /// </summary>
    internal static bool TryFind(string registrationDigest, out WinGetMachineRegistrationView view, out string keyName, out string displayVersion)
    {
        view = default;
        keyName = displayVersion = string.Empty;
        if (!ReviewedUpdateOperation.IsUpperHex64(registrationDigest)) return false;
        int matches = 0;
        try
        {
            foreach (WinGetMachineRegistrationView candidateView in new[] { WinGetMachineRegistrationView.Machine64, WinGetMachineRegistrationView.Machine32 })
            {
                using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                    candidateView == WinGetMachineRegistrationView.Machine32 ? RegistryView.Registry32 : RegistryView.Registry64);
                using RegistryKey? uninstall = machine.OpenSubKey(UninstallPath, writable: false);
                if (uninstall is null) continue;
                string[] names = uninstall.GetSubKeyNames();
                if (names.Length > MaximumKeysPerView) return false;
                foreach (string name in names)
                {
                    if (TryDigest(candidateView, name, out string? version) is not { } digest || version is null ||
                        !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                            System.Text.Encoding.ASCII.GetBytes(digest), System.Text.Encoding.ASCII.GetBytes(registrationDigest))) continue;
                    matches++;
                    view = candidateView;
                    keyName = name;
                    displayVersion = version;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return false;
        }
        if (matches == 1) return true;
        view = default;
        keyName = displayVersion = string.Empty;
        return false;
    }
}

/// <summary>
/// D4b design §5 H7 and the §3 build note: the WinGet <c>settings.json</c> (and its <c>.backup</c>, which WinGet reads when the main
/// file does not parse) of the requesting user, checked with <see cref="WingetUserSettingsPolicy"/> in both places WinGet may read:
/// <list type="bullet">
/// <item>the unpackaged location <c>&lt;LocalAppData known folder&gt;\Microsoft\WinGet\Settings\defaultState</c>, which is what the
/// helper's <c>winget.exe</c> uses, because it is started by path and so has no package identity. The known folder is resolved the
/// way WinGet resolves it (same user, same API), never from an environment value;</item>
/// <item>the packaged <c>LocalState</c> of App Installer, under the profile folder from the administrator-protected profile list.</item>
/// </list>
/// A missing folder is WinGet's defaults. A reparse point anywhere on either folder chain refuses.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HelperWingetSettings
{
    private const string ProfileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private static readonly string[] UnpackagedChain = ["Microsoft", "WinGet", "Settings", "defaultState"];

    internal static bool Allows(string requesterSid)
    {
        try
        {
            if (ProfileFolder(requesterSid) is not { } profile) return false;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathFullyQualified(localAppData)) return false;
            return FolderAllows(localAppData, UnpackagedChain) &&
                FolderAllows(profile, ["AppData", "Local", "Packages", HelperTrustedWinget.AppInstallerFamily, "LocalState"]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Walks <paramref name="chain"/> below <paramref name="root"/>; a missing folder passes (defaults), a reparse point refuses.</summary>
    private static bool FolderAllows(string root, string[] chain)
    {
        string current = Path.GetFullPath(root);
        if (!Directory.Exists(current)) return true;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        foreach (string part in chain)
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current)) return !File.Exists(current);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        }
        return FileAllows(Path.Combine(current, "settings.json")) && FileAllows(Path.Combine(current, "settings.json.backup"));
    }

    private static bool FileAllows(string path)
    {
        if (!File.Exists(path)) return true;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > WingetUserSettingsPolicy.MaximumBytes) return false;
        byte[] content = new byte[stream.Length];
        stream.ReadExactly(content);
        return WingetUserSettingsPolicy.Allows(content);
    }

    private static string? ProfileFolder(string sid)
    {
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? profile = machine.OpenSubKey(Path.Combine(ProfileListPath, new SecurityIdentifier(sid).Value), writable: false);
        if (profile?.GetValue("ProfileImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string raw) return null;
        string? expanded = ExpandSystemDrive(raw);
        return expanded is not null && Path.IsPathFullyQualified(expanded) ? Path.GetFullPath(expanded) : null;
    }

    /// <summary>
    /// The profile path's only accepted variable is a leading <c>%SystemDrive%</c>, taken from the system directory rather than the
    /// environment (which the owner's session can set). Any other variable refuses, so no environment value can move the checked
    /// folder away from the one WinGet reads.
    /// </summary>
    internal static string? ExpandSystemDrive(string raw)
    {
        const string SystemDrive = "%SystemDrive%";
        string path = raw;
        if (path.StartsWith(SystemDrive, StringComparison.OrdinalIgnoreCase))
        {
            string? drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
            if (string.IsNullOrEmpty(drive)) return null;
            path = drive + path[SystemDrive.Length..];
        }
        return path.Contains('%', StringComparison.Ordinal) ? null : path;
    }
}

/// <summary>
/// D4b design §6 S1: a fresh, administrator-only working folder for one elevated WinGet process, used as its <c>TEMP</c> and
/// <c>TMP</c>, with a <c>d</c> sub-folder for the H7b download. It is created directly in the system <c>Temp</c> folder, which
/// standard users can add to but cannot list, and whose entries they cannot rename or delete; its 128-bit random name cannot be
/// guessed. The folder is created with Administrators as owner and a protected DACL granting only SYSTEM and Administrators, and
/// is checked again after creation (owner, DACL, no reparse point, empty), so a folder that already existed is refused rather than
/// repaired. Each WinGet process gets its own folder, so the download folder named on one command line reveals nothing about
/// another run. <see cref="Dispose"/> deletes the folder.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class HelperProtectedRunFolder : IDisposable
{
    private const string NamePrefix = "PCSteward-";

    private HelperProtectedRunFolder(string path, string downloads)
    {
        Path = path;
        Downloads = downloads;
    }

    /// <summary>The run folder (the WinGet process's <c>TEMP</c> and <c>TMP</c>).</summary>
    internal string Path { get; }

    /// <summary>A sub-folder for the H7b download and its exported manifest.</summary>
    internal string Downloads { get; }

    internal static HelperProtectedRunFolder? TryCreate()
    {
        try
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(windows) || !System.IO.Path.IsPathFullyQualified(windows)) return null;
            string root = System.IO.Path.Combine(windows, "Temp");
            if (!IsPlainDirectory(windows) || !IsPlainDirectory(root)) return null;
            string run = System.IO.Path.Combine(root, NamePrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
            if (Directory.Exists(run) || File.Exists(run)) return null;
            new DirectoryInfo(run).Create(Protected());
            if (!IsFreshProtected(run, expectEmpty: true)) return null;
            string downloads = System.IO.Path.Combine(run, "d");
            new DirectoryInfo(downloads).Create(Protected());
            return IsFreshProtected(downloads, expectEmpty: true) && IsFreshProtected(run, expectEmpty: false)
                ? new HelperProtectedRunFolder(run, downloads)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
            System.Security.SecurityException or InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True when the folder is a plain directory with the expected owner and protected DACL (and, if asked, empty).</summary>
    internal static bool IsFreshProtected(string directory, bool expectEmpty)
    {
        if (!IsPlainDirectory(directory)) return false;
        if (expectEmpty && Directory.EnumerateFileSystemEntries(directory).Any()) return false;
        return HasExpectedProtection(new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access));
    }

    public void Dispose()
    {
        try
        {
            if (IsPlainDirectory(Path)) DeleteTree(Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the next run's owner; the folder stays administrator-only.
        }
    }

    /// <summary>Deletes a tree without following reparse points (a link is removed, never entered).</summary>
    private static void DeleteTree(string directory)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0) DeleteTree(entry);
            else if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry, recursive: false);
            else
            {
                File.SetAttributes(entry, FileAttributes.Normal);
                File.Delete(entry);
            }
        }
        Directory.Delete(directory, recursive: false);
    }

    private static bool IsPlainDirectory(string path) =>
        Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);

    private static DirectorySecurity Protected()
    {
        DirectorySecurity security = new();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    /// <summary>Owner Administrators, protected DACL, and every allow rule for SYSTEM or Administrators only (no deny needed).</summary>
    private static bool HasExpectedProtection(DirectorySecurity security)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !owner.Equals(Administrators)) return false;
        if (!security.AreAccessRulesProtected) return false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.IdentityReference is not SecurityIdentifier sid || !(sid.Equals(Administrators) || sid.Equals(LocalSystem))) return false;
        }
        return true;
    }
}

/// <summary>
/// D4b design §5.1: the complete environment of a helper WinGet process, never merged with the helper's own (which comes from the
/// owner's registry). Every value comes from a system or known-folder API: the Windows and program folders, the requester's profile
/// folders (the same user, resolved the way WinGet resolves them), <c>TEMP</c> and <c>TMP</c> set to the fresh protected run folder
/// (§6 S1), and a fixed <c>PATH</c> of system folders plus, when found, the App Installer C++ runtime framework folder
/// (<see cref="HelperTrustedWinget.TryResolveRuntimeFolder"/>). Throws for any value that is not a plain local path.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HelperWingetEnvironment
{
    internal static IReadOnlyDictionary<string, string> Create(string runFolder, string? runtimeFolder)
    {
        string windows = Plain(Environment.GetFolderPath(Environment.SpecialFolder.Windows, Environment.SpecialFolderOption.DoNotVerify));
        string system = Plain(Environment.SystemDirectory);
        string programFiles = Plain(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.DoNotVerify));
        string programFilesX86 = Plain(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolderOption.DoNotVerify));
        string common = Plain(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolderOption.DoNotVerify));
        string commonX86 = Plain(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86, Environment.SpecialFolderOption.DoNotVerify));
        string programData = Plain(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify));
        string profile = Plain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify));
        string roaming = Plain(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify));
        string local = Plain(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify));
        string run = Plain(runFolder);
        List<string> path = [system, windows, Path.Combine(system, "Wbem"), Path.Combine(system, "WindowsPowerShell", "v1.0")];
        if (runtimeFolder is not null) path.Add(Plain(runtimeFolder));
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = windows, ["windir"] = windows, ["SystemDrive"] = Path.GetPathRoot(windows)!.TrimEnd('\\'),
            ["ComSpec"] = Path.Combine(system, "cmd.exe"), ["PATH"] = string.Join(';', path), ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD",
            ["ProgramFiles"] = programFiles, ["ProgramW6432"] = programFiles, ["ProgramFiles(x86)"] = programFilesX86,
            ["CommonProgramFiles"] = common, ["CommonProgramW6432"] = common, ["CommonProgramFiles(x86)"] = commonX86,
            ["ProgramData"] = programData, ["ALLUSERSPROFILE"] = programData,
            ["USERPROFILE"] = profile, ["APPDATA"] = roaming, ["LOCALAPPDATA"] = local,
            ["TEMP"] = run, ["TMP"] = run, ["OS"] = "Windows_NT",
            ["PROCESSOR_ARCHITECTURE"] = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
                System.Runtime.InteropServices.Architecture.X86 => "x86",
                _ => "AMD64",
            },
        };
        return environment.AsReadOnly();
    }

    /// <summary>A drive-absolute, canonical path with no character that could split a list or expand a variable.</summary>
    internal static string Plain(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length is < 3 or > 4096 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' ||
            path.Any(static character => char.IsControl(character) || ";\"%<>|?*/".Contains(character)) || path.AsSpan(2).Contains(':'))
            throw new ArgumentException("The path is not a plain local path.", nameof(path));
        string normalized = Path.GetFullPath(path).TrimEnd('\\');
        if (!string.Equals(path.TrimEnd('\\'), normalized, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The path must already be canonical.", nameof(path));
        return normalized.Length == 2 ? normalized + '\\' : normalized;
    }
}
