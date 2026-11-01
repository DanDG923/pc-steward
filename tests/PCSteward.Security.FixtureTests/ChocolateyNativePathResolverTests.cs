using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13; Task 50 design rev 6 §2.4 (L1): known-folder and token-profile resolution, compared with the
/// framework's own known-folder answers. Windows-only; reported as failed elsewhere. No test reads or writes the
/// runner's Chocolatey tree.
/// </summary>
[TestClass]
public sealed class ChocolateyNativePathResolverTests
{
    [System.Runtime.InteropServices.DllImport("userenv.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserProfileDirectory(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token, char[]? buffer, ref uint size);

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);

    private static string KnownFolderResults(Guid folder)
    {
        string Probe(uint flags)
        {
            int result = SHGetKnownFolderPath(folder, flags, IntPtr.Zero, out IntPtr pointer);
            if (pointer != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pointer);
            return $"0x{result:X8}";
        }
        return $"verify={Probe(0)},dontVerify={Probe(0x4000)}";
    }

    /// <summary>Names the component that failed, so a CI failure is diagnosable (token SID, profile lookup, known folders).</summary>
    private static string Diagnose(RequesterToken token)
    {
        using System.Security.Principal.WindowsIdentity self = System.Security.Principal.WindowsIdentity.GetCurrent();
        uint size = 0;
        bool first = GetUserProfileDirectory(token.Handle, null, ref size);
        int firstError = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        string profile = "n/a";
        int secondError = 0;
        if (size is > 0 and < 32768)
        {
            char[] buffer = new char[size];
            profile = GetUserProfileDirectory(token.Handle, buffer, ref size) ? new string(buffer).TrimEnd('\0') : "failed";
            secondError = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        }
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return $"sidEqual={string.Equals(self.User?.Value, token.Sid, StringComparison.Ordinal)}; profileProbe={first}/{firstError}/size={size}; " +
            $"profile={profile}/{secondError}/canonical={ProtectedNodeReader.IsCanonicalLocalPath(profile)}; " +
            $"roamingCanonical={ProtectedNodeReader.IsCanonicalLocalPath(roaming)}; localCanonical={ProtectedNodeReader.IsCanonicalLocalPath(local)}; " +
            $"roamingKnownFolder={KnownFolderResults(new Guid("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D"))}; " +
            $"localKnownFolder={KnownFolderResults(new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091"))}";
    }

    [TestMethod]
    public void MachineRootsComeFromKnownFoldersWithFixedChocolateyConcatenations()
    {
        N1NativeFixture.RequireWindows("known folders");
        ChocolateyNativeRoots roots = ChocolateyNativePathResolver.TryResolveMachine() ?? throw new AssertFailedException("unresolved");

        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), roots.ProgramData, ignoreCase: true);
        Assert.AreEqual(roots.ProgramData + @"\chocolatey", roots.Root);
        Assert.AreEqual(roots.ProgramData + @"\ChocolateyHttpCache", roots.HttpCache);
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.Windows), roots.WindowsRoot, ignoreCase: true);
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), roots.ProgramFiles, ignoreCase: true);
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), roots.ProgramFilesX86, ignoreCase: true);
    }

    [TestMethod]
    public void LaunchPathsTakeTheProfileFromTheEvaluationTokenAndAppDataFromTheOwnContext()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        ChocolateyNativeRoots roots = ChocolateyNativePathResolver.TryResolveMachine() ?? throw new AssertFailedException("unresolved");
        string cache = roots.ProgramData + @"\PCSteward\cache";

        ChocolateyLaunchPaths paths = ChocolateyNativePathResolver.TryResolveLaunchPaths(token, roots, cache)
            ?? throw new AssertFailedException("launch paths unresolved: " + Diagnose(token));

        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), paths.UserProfile, ignoreCase: true);
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), paths.RoamingAppData, ignoreCase: true);
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), paths.LocalAppData, ignoreCase: true);
        Assert.AreEqual(roots.Root, paths.ChocolateyRoot);
        Assert.AreEqual(cache, paths.ProtectedCache);
        Assert.AreEqual(roots.WindowsRoot, paths.WindowsRoot);
        // The compiled environment accepts every resolved path.
        Assert.IsNotNull(ChocolateyCompiledEnvironment.Create(paths));
    }

    [TestMethod]
    public void NonCanonicalInputsRefuse()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        ChocolateyNativeRoots roots = ChocolateyNativePathResolver.TryResolveMachine() ?? throw new AssertFailedException("unresolved");

        Assert.IsNull(ChocolateyNativePathResolver.TryResolveLaunchPaths(token, roots, roots.ProgramData + @"\cache\"));
        Assert.IsNull(ChocolateyNativePathResolver.TryResolveLaunchPaths(token, roots, @"\\server\share\cache"));
        Assert.IsNull(ChocolateyNativePathResolver.TryResolveLaunchPaths(token, roots with { Root = roots.Root + @"\..\x" }, roots.ProgramData + @"\cache"));
        Assert.IsNull(ChocolateyNativePathResolver.TryResolveLaunchPaths(token, roots with { WindowsRoot = "Windows" }, roots.ProgramData + @"\cache"));
    }
}
