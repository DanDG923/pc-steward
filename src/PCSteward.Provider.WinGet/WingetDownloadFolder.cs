using System.Security.Cryptography;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// Phase D stage D4b design §5 H7b and §7: reads what one <c>winget download</c> left in its own fresh folder, after that process
/// settled. The folder must hold exactly two plain files and nothing else: one <c>.yaml</c> manifest (read with
/// <see cref="WingetExportedManifest"/>) and one installer, whose streamed SHA-256 must equal the manifest's <c>InstallerSha256</c>.
/// No directory, reparse point, alternate layout or oversized manifest is accepted. When the folder holds exactly one manifest it is
/// read first, so its own refusal (for example declared dependencies, which WinGet also downloads into a sub-folder) is the one
/// reported. The caller compares the returned facts with the
/// review's; this type only says what the folder holds, and never throws for what it finds there or for cancellation.
/// </summary>
internal static class WingetDownloadFolder
{
    private const string ManifestExtension = ".yaml";

    /// <summary>More entries than this is refused without reading further (the expected layout has two).</summary>
    private const int MaximumEntries = 16;

    /// <summary>Reads <paramref name="folder"/>; the facts are non-null exactly when the refusal is None.</summary>
    internal static async Task<(WingetManifestRefusal Refusal, WingetManifestFacts? Facts)> ReadAsync(string folder,
        WingetManifestExpectation expected, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            // The manifest is read first when there is exactly one, so a manifest that declares dependencies is reported as such even
            // though WinGet also downloaded them into a sub-folder. Any manifest refusal is final; the layout is checked after.
            if (!TryListFolder(folder, out List<FileSystemInfo> entries)) return (WingetManifestRefusal.FolderUnexpected, null);
            FileInfo[] manifests = [.. entries.OfType<FileInfo>().Where(IsManifest)];
            if (manifests.Length != 1 || (manifests[0].Attributes & FileAttributes.ReparsePoint) != 0)
                return (WingetManifestRefusal.FolderUnexpected, null);
            byte[]? manifest = ReadBounded(manifests[0]);
            if (manifest is null) return (WingetManifestRefusal.Malformed, null);
            WingetManifestRefusal refusal = WingetExportedManifest.Read(manifest, expected, out WingetManifestFacts? facts);
            if (refusal != WingetManifestRefusal.None) return (refusal, null);
            if (entries.Count != 2 || entries.Find(entry => !ReferenceEquals(entry, manifests[0])) is not FileInfo installer ||
                (installer.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || IsManifest(installer))
                return (WingetManifestRefusal.FolderUnexpected, null);
            string installerSha256 = await HashAsync(installer, cancellationToken).ConfigureAwait(false);
            return string.Equals(installerSha256, facts!.InstallerSha256, StringComparison.Ordinal)
                ? (WingetManifestRefusal.None, facts)
                : (WingetManifestRefusal.InstallerHashMismatch, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or
            OperationCanceledException)
        {
            // Never a match. A cancelled hash reads as a refusal here; the caller checks its own token first.
            return (WingetManifestRefusal.FolderUnexpected, null);
        }
    }

    private static bool IsManifest(FileInfo file) => string.Equals(file.Extension, ManifestExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder's entries (at most a few; more is refused), when the folder itself is a plain directory.</summary>
    private static bool TryListFolder(string folder, out List<FileSystemInfo> entries)
    {
        entries = [];
        DirectoryInfo directory = new(folder);
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        entries = [.. directory.EnumerateFileSystemInfos("*", new EnumerationOptions
        {
            RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false, MatchType = MatchType.Simple,
            ReturnSpecialDirectories = false,
        }).Take(MaximumEntries + 1)];
        return entries.Count <= MaximumEntries;
    }

    /// <summary>The manifest's bytes, or null when it is larger than <see cref="WingetExportedManifest.MaximumBytes"/>.</summary>
    private static byte[]? ReadBounded(FileInfo file)
    {
        using FileStream stream = new(file.FullName, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, Options = FileOptions.SequentialScan,
        });
        if (stream.Length > WingetExportedManifest.MaximumBytes) return null;
        byte[] buffer = new byte[WingetExportedManifest.MaximumBytes + 1];
        int total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0) total += read;
        return total > WingetExportedManifest.MaximumBytes ? null : buffer[..total];
    }

    private static async Task<string> HashAsync(FileInfo file, CancellationToken cancellationToken)
    {
        FileStream stream = new(file.FullName, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read,
            Options = FileOptions.SequentialScan | FileOptions.Asynchronous,
        });
        await using (stream.ConfigureAwait(false))
        {
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }
    }
}
