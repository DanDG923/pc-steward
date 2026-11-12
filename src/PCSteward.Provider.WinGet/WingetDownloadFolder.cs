using System.Security.Cryptography;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// Phase D stage D4b design §5 H7b and §7: reads what one <c>winget download</c> left in its own fresh folder, after that process
/// settled. The folder must hold exactly two plain files and nothing else: one <c>.yaml</c> manifest (read with
/// <see cref="WingetExportedManifest"/>) and one installer, whose streamed SHA-256 must equal the manifest's <c>InstallerSha256</c>.
/// No directory, reparse point, alternate layout or oversized manifest is accepted. The caller compares the returned facts with the
/// review's; this type only says what the folder holds, and never throws for what it finds there or for cancellation.
/// </summary>
internal static class WingetDownloadFolder
{
    private const string ManifestExtension = ".yaml";

    /// <summary>Reads <paramref name="folder"/>; the facts are non-null exactly when the refusal is None.</summary>
    internal static async Task<(WingetManifestRefusal Refusal, WingetManifestFacts? Facts)> ReadAsync(string folder,
        WingetManifestExpectation expected, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            if (!TryFindFiles(folder, out FileInfo? manifestFile, out FileInfo? installerFile)) return (WingetManifestRefusal.FolderUnexpected, null);
            byte[]? manifest = ReadBounded(manifestFile!);
            if (manifest is null) return (WingetManifestRefusal.Malformed, null);
            WingetManifestRefusal refusal = WingetExportedManifest.Read(manifest, expected, out WingetManifestFacts? facts);
            if (refusal != WingetManifestRefusal.None) return (refusal, null);
            string installerSha256 = await HashAsync(installerFile!, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Exactly one <c>.yaml</c> file and one other file, both plain (no reparse point), and no other entry of any kind.</summary>
    private static bool TryFindFiles(string folder, out FileInfo? manifest, out FileInfo? installer)
    {
        manifest = installer = null;
        DirectoryInfo directory = new(folder);
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        FileSystemInfo[] entries = directory.EnumerateFileSystemInfos("*", new EnumerationOptions
        {
            RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false, MatchType = MatchType.Simple,
            ReturnSpecialDirectories = false,
        }).Take(3).ToArray();
        if (entries.Length != 2) return false;
        foreach (FileSystemInfo entry in entries)
        {
            if (entry is not FileInfo file || (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return false;
            if (string.Equals(file.Extension, ManifestExtension, StringComparison.OrdinalIgnoreCase))
            {
                if (manifest is not null) return false;
                manifest = file;
            }
            else
            {
                if (installer is not null) return false;
                installer = file;
            }
        }
        return manifest is not null && installer is not null;
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
