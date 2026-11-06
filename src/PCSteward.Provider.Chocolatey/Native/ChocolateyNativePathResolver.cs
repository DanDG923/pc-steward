using System.Runtime.InteropServices;
using System.Security.Principal;
using Win32 = PCSteward.Provider.Chocolatey.Native.ProtectedNativeMethods;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>Machine roots from known folders. <see cref="Root"/> and <see cref="HttpCache"/> are fixed concatenations, as in the native client.</summary>
internal sealed record ChocolateyNativeRoots(string ProgramData, string Root, string HttpCache, string WindowsRoot,
    string ProgramFiles, string ProgramFilesX86);

/// <summary>
/// FR-UPD-15, TM-03/04/13; Task 50 design rev 6 §2.4 (L1): native path resolution with no environment input. Machine
/// folders come from <c>SHGetKnownFolderPath</c>; the requester profile comes from <c>GetUserProfileDirectory</c> on the
/// evaluation token (<c>TOKEN_QUERY</c> only); RoamingAppData and LocalAppData come from <c>SHGetKnownFolderPath</c> with
/// no token, in this process's own context, which must be the requester's SID. The identification token is never
/// passed to an API that impersonates. Every result must be a canonical local drive path, or resolution fails.
/// Resolution only formats paths: provenance, protection and identity are proven separately.
/// </summary>
internal static class ChocolateyNativePathResolver
{
    private static readonly Guid ProgramDataFolder = new("62AB5D82-FDC1-4DC3-A9DD-070D1D495D97");
    private static readonly Guid WindowsFolder = new("F38BF404-1D43-42F2-9305-67DE0B28FC23");
    private static readonly Guid ProgramFilesFolder = new("905E63B6-C1BF-494E-B29C-65B732D3D21A");
    private static readonly Guid ProgramFilesX86Folder = new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E");
    private const uint KnownFolderDontVerify = 0x00004000;
    private static readonly Guid RoamingAppDataFolder = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");
    private static readonly Guid LocalAppDataFolder = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");

    internal static ChocolateyNativeRoots? TryResolveMachine()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) return null;
        string? programData = TryKnownFolder(ProgramDataFolder);
        string? windows = TryKnownFolder(WindowsFolder);
        string? programFiles = TryKnownFolder(ProgramFilesFolder);
        string? programFilesX86 = TryKnownFolder(ProgramFilesX86Folder);
        if (programData is null || windows is null || programFiles is null || programFilesX86 is null) return null;
        string root = programData + @"\chocolatey", httpCache = programData + @"\ChocolateyHttpCache";
        return ProtectedNodeReader.IsCanonicalLocalPath(root) && ProtectedNodeReader.IsCanonicalLocalPath(httpCache)
            ? new ChocolateyNativeRoots(programData, root, httpCache, windows, programFiles, programFilesX86) : null;
    }

    internal static ChocolateyLaunchPaths? TryResolveLaunchPaths(RequesterToken token, ChocolateyNativeRoots roots, string protectedCache)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(roots);
        if (!OperatingSystem.IsWindows() || !ProtectedNodeReader.IsCanonicalLocalPath(protectedCache)) return null;
        if (!new[] { roots.ProgramData, roots.Root, roots.HttpCache, roots.WindowsRoot, roots.ProgramFiles, roots.ProgramFilesX86 }
                .All(ProtectedNodeReader.IsCanonicalLocalPath))
            return null;
        // N1 review L2: the Chocolatey roots are the fixed native concatenations, whatever constructed the record.
        if (!string.Equals(roots.Root, roots.ProgramData + @"\chocolatey", StringComparison.Ordinal) ||
            !string.Equals(roots.HttpCache, roots.ProgramData + @"\ChocolateyHttpCache", StringComparison.Ordinal))
            return null;
        using (WindowsIdentity self = WindowsIdentity.GetCurrent())
        {
            // The known folders below are this process's own; they describe the requester only when the SIDs are equal.
            if (!string.Equals(self.User?.Value, token.Sid, StringComparison.Ordinal)) return null;
        }
        string? profile = TryUserProfile(token);
        string? roaming = TryKnownFolder(RoamingAppDataFolder);
        string? local = TryKnownFolder(LocalAppDataFolder);
        if (profile is null || roaming is null || local is null) return null;
        return new ChocolateyLaunchPaths(roots.WindowsRoot, roots.ProgramFiles, roots.ProgramFilesX86, profile, roaming, local,
            roots.Root, protectedCache);
    }

    private static string? TryUserProfile(RequesterToken token)
    {
        uint size = 0;
        Win32.GetUserProfileDirectory(token.Handle, null, ref size);
        if (size is 0 or > ProtectedNodeReader.MaximumPathLength + 1) return null;
        char[] buffer = new char[size];
        if (!Win32.GetUserProfileDirectory(token.Handle, buffer, ref size) || size == 0 || size > buffer.Length) return null;
        string path = new string(buffer, 0, (int)size).TrimEnd('\0');
        return ProtectedNodeReader.IsCanonicalLocalPath(path) ? path : null;
    }

    private static string? TryKnownFolder(Guid folder)
    {
        // KF_FLAG_DONT_VERIFY, as the .NET folder API does: a profile folder that does not exist yet (a fresh profile)
        // must not fail resolution. These paths only feed the compiled launch environment; nothing here asserts protection.
        int result = Win32.SHGetKnownFolderPath(folder, KnownFolderDontVerify, IntPtr.Zero, out IntPtr pointer);
        try
        {
            if (result != 0 || pointer == IntPtr.Zero) return null;
            string? path = Marshal.PtrToStringUni(pointer);
            return ProtectedNodeReader.IsCanonicalLocalPath(path) ? path : null;
        }
        finally
        {
            if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
        }
    }
}
