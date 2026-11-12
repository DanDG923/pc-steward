using System.Security;
using System.Security.Cryptography;
using PCSteward.Application;

namespace PCSteward.Storage;

/// <summary>
/// D4b stage 2b: the folders a whole-PC WinGet review downloads into, <c>%LOCALAPPDATA%\PCSteward\downloads\WinGetReview\&lt;32 hex&gt;</c>.
/// Every create and delete first re-checks the cache root and each of its ancestors from the volume down for reparse points, then
/// works only on the fixed review area. Deletion opens each object relative to the verified cache root without following a reparse
/// point (the same handle-bound deletion as Clear downloaded files), and a reparse point found inside a review folder is never
/// entered or removed: the folder is left for Clear downloaded files, which reports it.
/// </summary>
public sealed class ReviewDownloadFolders : IReviewDownloadFolders
{
    internal const string Area = "WinGetReview";

    public ReviewDownloadFolders(string? cacheRoot = null) =>
        CacheRoot = PathSafety.EnsureRoot(cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCSteward", "downloads"));

    public string CacheRoot { get; }

    public string? CreateFolder()
    {
        try
        {
            _ = PathSafety.EnsureRoot(CacheRoot);
            Directory.CreateDirectory(PathSafety.ResolveLedgerPath(CacheRoot, Area));
            string relative = Path.Combine(Area, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
            string folder = PathSafety.ResolveLedgerPath(CacheRoot, relative);
            if (Directory.Exists(folder) || File.Exists(folder)) return null;
            Directory.CreateDirectory(folder);
            // Checked again once it exists: neither the root, an ancestor, the area nor the folder may have become a reparse point.
            _ = PathSafety.EnsureRoot(CacheRoot);
            _ = PathSafety.ResolveLedgerPath(CacheRoot, relative);
            return Directory.EnumerateFileSystemEntries(folder).Any() ? null : folder;
        }
        catch (Exception exception) when (exception is SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool DeleteFolder(string folder)
    {
        if (!TryGetRelative(folder, out string relative)) return false;
        try
        {
            _ = PathSafety.EnsureRoot(CacheRoot);
            string full = PathSafety.ResolveLedgerPath(CacheRoot, relative);
            if (!Directory.Exists(full)) return true;
            bool clean = true;
            foreach (FileSystemObject item in PathSafety.EnumerateOwnedObjects(full)
                .OrderByDescending(item => item.RelativePath.Count(character => character == Path.DirectorySeparatorChar))
                .ThenBy(item => item.IsDirectory))
            {
                if (item.IsUnsafe) { clean = false; continue; }
                PathSafety.DeleteResult result = PathSafety.DeleteExactObject(CacheRoot, Path.Combine(relative, item.RelativePath), item.IsDirectory);
                clean &= result.Deleted || result.AlreadyAbsent;
            }
            if (!clean) return false;
            PathSafety.DeleteResult self = PathSafety.DeleteExactObject(CacheRoot, relative, directory: true);
            return self.Deleted || self.AlreadyAbsent;
        }
        catch (Exception exception) when (exception is SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Only <c>WinGetReview\&lt;32 upper-case hex&gt;</c> directly under the cache root, as <see cref="CreateFolder"/> names it.</summary>
    private bool TryGetRelative(string folder, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return false;
        string[] parts = Path.GetRelativePath(CacheRoot, Path.GetFullPath(folder)).Split(Path.DirectorySeparatorChar);
        if (parts.Length != 2 || !string.Equals(parts[0], Area, StringComparison.Ordinal) || parts[1].Length != 32 ||
            !parts[1].All(character => character is (>= '0' and <= '9') or (>= 'A' and <= 'F')))
            return false;
        relative = Path.Combine(parts);
        return true;
    }
}
