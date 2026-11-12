using PCSteward.Storage;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// D4b stage 2b (Codex review of PR #75; AGENTS.md deletion boundary): the whole-PC review's download folders live only under the
/// fixed review area of the download cache, every create and delete re-checks the cache root and its ancestors for reparse points,
/// deletion is handle-bound and never follows a reparse point, and no other path is ever deleted.
/// </summary>
[TestClass]
[TestCategory("FR-UPD-15")]
public sealed class ReviewDownloadFoldersTests
{
    [TestMethod]
    public void AFolderIsCreatedEmptyInTheReviewAreaAndDeletedWithItsContents()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Handle-bound deletion is Windows-only; this row is Windows-CI evidence.");
        using Fixture fixture = new();
        ReviewDownloadFolders folders = new(fixture.Cache);

        string? folder = folders.CreateFolder();

        Assert.IsNotNull(folder);
        Assert.AreEqual(Path.Combine(fixture.Cache, "WinGetReview"), Path.GetDirectoryName(folder));
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(folder));
        File.WriteAllText(Path.Combine(folder, "manifest.yaml"), "x");
        Directory.CreateDirectory(Path.Combine(folder, "Dependencies"));
        File.WriteAllText(Path.Combine(folder, "Dependencies", "runtime.msi"), "x");
        Assert.IsTrue(folders.DeleteFolder(folder));
        Assert.IsFalse(Directory.Exists(folder));
        Assert.IsTrue(folders.DeleteFolder(folder), "an absent folder is already deleted");
    }

    [TestMethod]
    public void OnlyAReviewFolderIsEverDeleted()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Handle-bound deletion is Windows-only; this row is Windows-CI evidence.");
        using Fixture fixture = new();
        ReviewDownloadFolders folders = new(fixture.Cache);
        string area = Path.Combine(fixture.Cache, "WinGetReview");
        string other = Path.Combine(fixture.Cache, "Other", new string('A', 32));
        Directory.CreateDirectory(other);
        string lower = Path.Combine(area, new string('a', 32));
        Directory.CreateDirectory(lower);

        foreach (string path in new[] { fixture.Cache, area, other, lower, fixture.Outside, Path.Combine(area, new string('A', 31)), "WinGetReview\\" + new string('A', 32), "" })
            Assert.IsFalse(folders.DeleteFolder(path), path);

        Assert.IsTrue(Directory.Exists(other));
        Assert.IsTrue(Directory.Exists(lower));
        Assert.AreEqual("sentinel", File.ReadAllText(fixture.Sentinel));
    }

    [TestMethod]
    public void AJunctionedAncestorOrReviewAreaIsRefused()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Junctions are Windows-only; this row is Windows-CI evidence.");
        using Fixture fixture = new();
        string parent = Path.Combine(fixture.Base, "junctioned-parent");
        Junction(parent, fixture.OutsideFolder);
        Assert.ThrowsExactly<System.Security.SecurityException>(() => { _ = new ReviewDownloadFolders(Path.Combine(parent, "downloads")); });

        ReviewDownloadFolders folders = new(fixture.Cache);
        Junction(Path.Combine(fixture.Cache, "WinGetReview"), fixture.OutsideFolder);
        Assert.IsNull(folders.CreateFolder());
        Assert.IsEmpty(Directory.EnumerateDirectories(fixture.OutsideFolder), "nothing was created through the junction");
    }

    [TestMethod]
    public void AJunctionInsideAReviewFolderIsNeverFollowedOrRemoved()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Junctions are Windows-only; this row is Windows-CI evidence.");
        using Fixture fixture = new();
        ReviewDownloadFolders folders = new(fixture.Cache);
        string folder = folders.CreateFolder()!;
        File.WriteAllText(Path.Combine(folder, "installer.msi"), "x");
        Junction(Path.Combine(folder, "Dependencies"), fixture.OutsideFolder);

        Assert.IsFalse(folders.DeleteFolder(folder), "left for Clear downloaded files");

        Assert.AreEqual("sentinel", File.ReadAllText(fixture.Sentinel));
        Assert.IsTrue(Directory.Exists(Path.Combine(folder, "Dependencies")));
    }

    private static void Junction(string link, string target)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
        };
        process.Start();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, process.StandardError.ReadToEnd());
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture()
        {
            Directory.CreateDirectory(Cache);
            Directory.CreateDirectory(OutsideFolder);
            File.WriteAllText(Sentinel, "sentinel");
        }

        internal string Base { get; } = Path.Combine(Path.GetTempPath(), "pcs-review-folders-" + Guid.NewGuid().ToString("N"));
        internal string Cache => Path.Combine(Base, "downloads");
        internal string OutsideFolder => Path.Combine(Base, "outside");
        internal string Outside => Path.Combine(OutsideFolder, new string('B', 32));
        internal string Sentinel => Path.Combine(OutsideFolder, "sentinel.txt");

        // A recursive delete removes a junction itself and never enters it.
        public void Dispose() => Directory.Delete(Base, recursive: true);
    }
}
