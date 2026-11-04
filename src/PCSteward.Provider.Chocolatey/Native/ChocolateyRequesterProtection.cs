using Microsoft.Win32.SafeHandles;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §6 (rev-4 L1) and probe spec rev 4 §3: the one G1 protection rule both
/// the update item and the probe item use as the protection principal, moved here unchanged from the update item so the two
/// cannot diverge. Ancestry on the root, then Node mode on the root and on every file directly inside it; any other verdict,
/// path shape or failure refuses. Values only: no state is kept between calls.
/// </summary>
internal static class ChocolateyRequesterProtection
{
    /// <summary>
    /// §6 (rev-4 L1): ancestry on the root, then Node mode on the root and on every file directly inside it.
    /// <paramref name="ancestryBase"/> is null in production (ancestry from the lexical drive root); only a test-only fixture
    /// factory of an item passes a base (see <see cref="Ancestry"/>).
    /// </summary>
    internal static bool CannotModify(RequesterToken token, string root, IReadOnlyList<string> files, string? ancestryBase)
    {
        if (!OperatingSystem.IsWindows() || !ProtectedNodeReader.IsCanonicalLocalPath(root) || root.Length <= 3) return false;
        if (Ancestry(token, root, ancestryBase) != RequesterModification.Denied) return false;
        using SafeFileHandle? directory = ProtectedNodeReader.TryOpen(root, directory: true, out _);
        if (directory is null ||
            RequesterAccess.Evaluate(token, directory, directory: true, parentDirectory: null, AccessMode.Node) != RequesterModification.Denied)
            return false;
        foreach (string file in files)
        {
            if (!ProtectedNodeReader.IsCanonicalLocalPath(file) || !file.StartsWith(root + '\\', StringComparison.OrdinalIgnoreCase) ||
                file.IndexOf('\\', root.Length + 1) >= 0)
                return false;
            using SafeFileHandle? handle = ProtectedNodeReader.TryOpen(file, directory: false, out _);
            if (handle is null ||
                RequesterAccess.Evaluate(token, handle, directory: false, directory, AccessMode.Node) != RequesterModification.Denied)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Production (<paramref name="ancestryBase"/> null): <see cref="RequesterAccess.EvaluateAncestry(RequesterToken, string)"/>
    /// from the lexical drive root. Test fixture only: below the ancestry base, Ancestor mode on every directory from the base
    /// down to the parent (as the observer's own test seam).
    /// </summary>
    private static RequesterModification Ancestry(RequesterToken token, string path, string? ancestryBase)
    {
        if (ancestryBase is null || !path.StartsWith(ancestryBase + '\\', StringComparison.OrdinalIgnoreCase))
            return RequesterAccess.EvaluateAncestry(token, path);
        string[] segments = path[(ancestryBase.Length + 1)..].Split('\\');
        string directory = ancestryBase;
        ulong? volume = null;
        for (int index = 0; ; index++)
        {
            using (SafeFileHandle? handle = ProtectedNodeReader.TryOpen(directory, directory: true, out NativeNode node))
            {
                if (handle is null || (volume is not null && volume != node.Identity.VolumeSerial)) return RequesterModification.Unreadable;
                volume = node.Identity.VolumeSerial;
                RequesterModification verdict = RequesterAccess.Evaluate(token, handle, directory: true, parentDirectory: null, AccessMode.Ancestor);
                if (verdict != RequesterModification.Denied) return verdict;
            }
            if (index == segments.Length - 1) return RequesterModification.Denied;
            directory += '\\' + segments[index];
        }
    }
}
