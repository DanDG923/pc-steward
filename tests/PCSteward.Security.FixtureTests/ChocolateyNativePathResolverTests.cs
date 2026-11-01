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

        ChocolateyLaunchPaths paths = ChocolateyNativePathResolver.TryResolveLaunchPaths(token, roots, cache) ?? throw new AssertFailedException("unresolved");

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
