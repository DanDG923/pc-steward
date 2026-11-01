using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Win32 = PCSteward.Provider.Chocolatey.Native.ProtectedNativeMethods;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>The closed reason a tree walk or child lookup refused.</summary>
internal enum TreeWalkRefusal
{
    Reparse,
    RequesterModifiable,
    Unreadable,
    OverBound,
    CaseAliasDuplicate,
    ControlCharacter,
    /// <summary>An empty, dot, device, wildcard, stream or otherwise non-canonical entry name.</summary>
    InvalidName,
    /// <summary>The opened child is not the record the held parent enumeration listed under that name.</summary>
    ParentIdentityMismatch,
    Cancelled
}

/// <summary>Closed result of <see cref="ProtectedTreeWalker.Walk"/>: complete coverage or one refusal.</summary>
internal abstract record TreeWalkResult
{
    private TreeWalkResult() { }

    /// <summary>Every node in depth-first pre-order, the root first (relative path empty).</summary>
    internal sealed record Complete(IReadOnlyList<NativeNode> Nodes) : TreeWalkResult;

    /// <summary>The first refusal and the relative path at which it occurred (empty for the root).</summary>
    internal sealed record Refused(TreeWalkRefusal Reason, string RelativePath) : TreeWalkResult;
}

/// <summary>Walk bounds. <see cref="Profile"/> is the Profile V2 ceiling; tests may only narrow it.</summary>
internal sealed record TreeWalkBounds
{
    internal static readonly TreeWalkBounds Profile = new(32_768, 32, ProtectedNodeReader.MaximumPathLength);

    private TreeWalkBounds(int maximumEntries, int maximumDepth, int maximumPathLength)
    {
        MaximumEntries = maximumEntries;
        MaximumDepth = maximumDepth;
        MaximumPathLength = maximumPathLength;
    }

    /// <summary>Total nodes including the root.</summary>
    internal int MaximumEntries { get; }

    /// <summary>Depth of the deepest node; the root's children are depth 1.</summary>
    internal int MaximumDepth { get; }

    /// <summary>Applies to every full path and relative path.</summary>
    internal int MaximumPathLength { get; }

    /// <summary>Bounds no larger than <see cref="Profile"/> in any dimension; null otherwise.</summary>
    internal static TreeWalkBounds? Narrowed(int maximumEntries, int maximumDepth, int maximumPathLength) =>
        maximumEntries is >= 1 and <= 32_768 && maximumDepth is >= 1 and <= 32 &&
        maximumPathLength is >= 3 and <= ProtectedNodeReader.MaximumPathLength
            ? new TreeWalkBounds(maximumEntries, maximumDepth, maximumPathLength) : null;
}

/// <summary>Result of <see cref="ProtectedTreeWalker.LocateChild"/>.</summary>
internal enum ChildPresence
{
    Present,
    /// <summary>The held parent's enumeration does not list the name (L2). Nothing else is ever absence.</summary>
    Absent,
    Refused
}

/// <summary>One parsed <c>FILE_ID_BOTH_DIR_INFO</c> entry.</summary>
internal readonly record struct NativeDirectoryEntry(string Name, uint Attributes, ulong FileId);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2.3 (L2, §10 answer 2): a bounded, iterative walk of one protected
/// root. Each directory is enumerated with <c>FileIdBothDirectoryInfo</c> on its <b>held</b> handle; each listed entry
/// is name-checked, opened through <see cref="ProtectedNodeReader"/>, matched to the record the held parent listed
/// (same volume, same file ID) and evaluated with <see cref="RequesterAccess"/> in Node mode, with the held parent as
/// the <c>FILE_DELETE_CHILD</c> check, <b>before</b> it is descended into. Directory handles stay open while their
/// children are processed. The walk does not run <see cref="RequesterAccess.EvaluateAncestry"/>; the caller does that
/// first. Any uncertainty refuses.
/// </summary>
internal static class ProtectedTreeWalker
{
    internal const int EntryHeaderBytes = 104;
    private const int EnumerationBufferBytes = 64 * 1024;

    internal static TreeWalkResult Walk(RequesterToken token, string root, TreeWalkBounds bounds,
        Func<string, bool, ChocolateyTreeContentPolicy> classify, CancellationToken cancellationToken) =>
        WalkCore(token, root, bounds, classify, beforeChildOpen: null, cancellationToken);

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): <see cref="Walk"/> with a callback that runs after
    /// the parent enumeration and immediately before each child is opened, so a test can replace a listed child.
    /// </summary>
    internal static TreeWalkResult WalkWithHookForTests(RequesterToken token, string root, TreeWalkBounds bounds,
        Func<string, bool, ChocolateyTreeContentPolicy> classify, Action<string> beforeChildOpen, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beforeChildOpen);
        return WalkCore(token, root, bounds, classify, beforeChildOpen, cancellationToken);
    }

    /// <summary>
    /// Looks up one expected child of a held directory. <see cref="ChildPresence.Absent"/> only when the held parent's
    /// enumeration does not list <paramref name="name"/>; a listed child that is a reparse point, of the other kind,
    /// unopenable or a different record is <see cref="ChildPresence.Refused"/>. Access is not evaluated here.
    /// </summary>
    internal static ChildPresence LocateChild(SafeFileHandle parent, NativeNode parentNode, string name, bool directory,
        CancellationToken cancellationToken, out SafeFileHandle? child, out NativeNode node, out TreeWalkRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(parent);
        child = null;
        node = default;
        refusal = TreeWalkRefusal.InvalidName;
        if (!ProtectedNodeReader.IsCanonicalName(name) || !parentNode.Directory) return ChildPresence.Refused;
        TreeWalkRefusal? failure = TryEnumerate(parent, TreeWalkBounds.Profile.MaximumEntries, cancellationToken, out List<NativeDirectoryEntry> entries);
        if (failure is not null)
        {
            refusal = failure.Value;
            return ChildPresence.Refused;
        }
        NativeDirectoryEntry[] matches = [.. entries.Where(entry => string.Equals(entry.Name.ToUpperInvariant(), name.ToUpperInvariant(), StringComparison.Ordinal))];
        if (matches.Length == 0) return ChildPresence.Absent;
        failure = OpenListedChild(parentNode, matches[0], Join(parentNode.FullPath, matches[0].Name), string.Empty, directory,
            out child, out node);
        if (failure is null) return ChildPresence.Present;
        refusal = failure.Value;
        return ChildPresence.Refused;
    }

    /// <summary>Pure name rule for one enumerated entry; <paramref name="seenUpperInvariant"/> detects case aliases.</summary>
    internal static TreeWalkRefusal? CheckEntryName(string name, ISet<string> seenUpperInvariant)
    {
        ArgumentNullException.ThrowIfNull(seenUpperInvariant);
        if (name is null) return TreeWalkRefusal.InvalidName;
        if (name.Any(char.IsControl)) return TreeWalkRefusal.ControlCharacter;
        if (!ProtectedNodeReader.IsCanonicalName(name)) return TreeWalkRefusal.InvalidName;
        return seenUpperInvariant.Add(name.ToUpperInvariant()) ? null : TreeWalkRefusal.CaseAliasDuplicate;
    }

    /// <summary>
    /// Pure parser of a <c>FILE_ID_BOTH_DIR_INFO</c> chain. Returns false for any entry or name outside the buffer, a
    /// misaligned or backwards offset, or an odd name length. <c>.</c> and <c>..</c> are skipped.
    /// </summary>
    internal static bool TryParseEntries(ReadOnlySpan<byte> buffer, List<NativeDirectoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        int offset = 0;
        while (true)
        {
            if (offset < 0 || buffer.Length - offset < EntryHeaderBytes) return false;
            ReadOnlySpan<byte> entry = buffer[offset..];
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(entry[56..]);
            uint nameBytes = BinaryPrimitives.ReadUInt32LittleEndian(entry[60..]);
            ulong fileId = BinaryPrimitives.ReadUInt64LittleEndian(entry[96..]);
            if (nameBytes == 0 || nameBytes % 2 != 0 || nameBytes > (uint)(entry.Length - EntryHeaderBytes)) return false;
            if (next != 0 && (next % 8 != 0 || next < EntryHeaderBytes + nameBytes || next > (uint)entry.Length)) return false;
            string name = new(MemoryMarshal.Cast<byte, char>(entry.Slice(EntryHeaderBytes, (int)nameBytes)));
            if (name is not ("." or "..")) entries.Add(new NativeDirectoryEntry(name, attributes, fileId));
            if (next == 0) return true;
            offset += (int)next;
        }
    }

    private static TreeWalkResult WalkCore(RequesterToken token, string root, TreeWalkBounds bounds,
        Func<string, bool, ChocolateyTreeContentPolicy> classify, Action<string>? beforeChildOpen, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(classify);
        if (cancellationToken.IsCancellationRequested) return Refuse(TreeWalkRefusal.Cancelled, string.Empty);
        if (root.Length > bounds.MaximumPathLength) return Refuse(TreeWalkRefusal.OverBound, string.Empty);
        if (!ProtectedNodeReader.IsCanonicalLocalPath(root) || root.Length <= 3) return Refuse(TreeWalkRefusal.InvalidName, string.Empty);

        SafeFileHandle? rootHandle = ProtectedNodeReader.TryOpen(root, directory: true, out NativeNode rootNode, out ProtectedOpenFailure failure);
        if (rootHandle is null) return Refuse(failure == ProtectedOpenFailure.Reparse ? TreeWalkRefusal.Reparse : TreeWalkRefusal.Unreadable, string.Empty);
        Stack<Frame> stack = new();
        try
        {
            switch (RequesterAccess.Evaluate(token, rootHandle, directory: true, parentDirectory: null, AccessMode.Node))
            {
                case RequesterModification.Granted: return Refuse(TreeWalkRefusal.RequesterModifiable, string.Empty);
                case RequesterModification.Denied: break;
                default: return Refuse(TreeWalkRefusal.Unreadable, string.Empty);
            }
            List<NativeNode> nodes = [rootNode];
            TreeWalkRefusal? enumerated = TryEnumerate(rootHandle, bounds.MaximumEntries - nodes.Count, cancellationToken, out List<NativeDirectoryEntry> rootEntries);
            if (enumerated is not null) return Refuse(enumerated.Value, string.Empty);
            stack.Push(new Frame(rootHandle, rootNode, 0, rootEntries));
            rootHandle = null;

            while (stack.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested) return Refuse(TreeWalkRefusal.Cancelled, stack.Peek().Node.RelativePath);
                Frame frame = stack.Peek();
                if (frame.Next == frame.Entries.Count)
                {
                    stack.Pop().Handle.Dispose();
                    continue;
                }
                NativeDirectoryEntry entry = frame.Entries[frame.Next++];
                string relative = frame.Node.RelativePath.Length == 0 ? entry.Name : frame.Node.RelativePath + '\\' + entry.Name;
                string full = Join(frame.Node.FullPath, entry.Name);
                bool directory = (entry.Attributes & Win32.FileAttributeDirectory) != 0;
                if (full.Length > bounds.MaximumPathLength || relative.Length > bounds.MaximumPathLength ||
                    nodes.Count >= bounds.MaximumEntries || frame.Depth + 1 > bounds.MaximumDepth)
                    return Refuse(TreeWalkRefusal.OverBound, relative);
                if ((entry.Attributes & Win32.FileAttributeReparsePoint) != 0) return Refuse(TreeWalkRefusal.Reparse, relative);

                ChocolateyTreeContentPolicy policy;
                try { policy = classify(relative, directory); }
                catch (ArgumentException) { return Refuse(TreeWalkRefusal.InvalidName, relative); }

                beforeChildOpen?.Invoke(full);
                TreeWalkRefusal? opened = OpenListedChild(frame.Node, entry, full, relative, directory, out SafeFileHandle? child, out NativeNode node);
                if (opened is not null) return Refuse(opened.Value, relative);
                bool keep = false;
                try
                {
                    RequesterModification verdict = RequesterAccess.Evaluate(token, child!, directory, frame.Handle, AccessMode.Node);
                    if (verdict == RequesterModification.Unreadable) return Refuse(TreeWalkRefusal.Unreadable, relative);
                    // NonLoadingLogContents exempts only regular files from the requester-modification rule.
                    if (verdict == RequesterModification.Granted && (directory || policy != ChocolateyTreeContentPolicy.NonLoadingLogContents))
                        return Refuse(TreeWalkRefusal.RequesterModifiable, relative);
                    nodes.Add(node);
                    if (!directory) continue;
                    enumerated = TryEnumerate(child!, bounds.MaximumEntries - nodes.Count, cancellationToken, out List<NativeDirectoryEntry> entries);
                    if (enumerated is not null) return Refuse(enumerated.Value, relative);
                    stack.Push(new Frame(child!, node, frame.Depth + 1, entries));
                    keep = true;
                }
                finally
                {
                    if (!keep) child!.Dispose();
                }
            }
            return new TreeWalkResult.Complete(nodes);
        }
        finally
        {
            rootHandle?.Dispose();
            while (stack.Count > 0) stack.Pop().Handle.Dispose();
        }
    }

    /// <summary>
    /// Opens a child the held parent listed and requires it to be that record: the same volume as the held parent and the
    /// file ID the enumeration reported (a 128-bit ID that does not fit the enumeration's 64 bits refuses).
    /// </summary>
    private static TreeWalkRefusal? OpenListedChild(NativeNode parent, NativeDirectoryEntry entry, string full, string relative,
        bool directory, out SafeFileHandle? child, out NativeNode node)
    {
        node = default;
        child = null;
        if ((entry.Attributes & Win32.FileAttributeReparsePoint) != 0) return TreeWalkRefusal.Reparse;
        if (((entry.Attributes & Win32.FileAttributeDirectory) != 0) != directory) return TreeWalkRefusal.Unreadable;
        SafeFileHandle? handle = ProtectedNodeReader.TryOpen(full, directory, out NativeNode opened, out ProtectedOpenFailure failure);
        if (handle is null) return failure == ProtectedOpenFailure.Reparse ? TreeWalkRefusal.Reparse : TreeWalkRefusal.Unreadable;
        if (entry.FileId == 0 || opened.Identity.VolumeSerial != parent.Identity.VolumeSerial || opened.Identity.FileId != new UInt128(0, entry.FileId))
        {
            handle.Dispose();
            return TreeWalkRefusal.ParentIdentityMismatch;
        }
        child = handle;
        node = opened with { RelativePath = relative };
        return null;
    }

    /// <summary>Enumerates a held directory handle, name-checking every entry, at most <paramref name="budget"/> entries.</summary>
    private static TreeWalkRefusal? TryEnumerate(SafeFileHandle directory, int budget, CancellationToken cancellationToken,
        out List<NativeDirectoryEntry> entries)
    {
        entries = [];
        byte[] buffer = new byte[EnumerationBufferBytes];
        HashSet<string> seen = new(StringComparer.Ordinal);
        int informationClass = Win32.FileIdBothDirectoryRestartInfoClass;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested) return TreeWalkRefusal.Cancelled;
            if (!Win32.GetFileInformationByHandleEx(directory, informationClass, buffer, (uint)buffer.Length))
            {
                if (Marshal.GetLastWin32Error() != Win32.ErrorNoMoreFiles) return TreeWalkRefusal.Unreadable;
                break;
            }
            informationClass = Win32.FileIdBothDirectoryInfoClass;
            int before = entries.Count;
            if (!TryParseEntries(buffer, entries)) return TreeWalkRefusal.Unreadable;
            for (int index = before; index < entries.Count; index++)
            {
                TreeWalkRefusal? name = CheckEntryName(entries[index].Name, seen);
                if (name is not null) return name;
            }
            if (entries.Count > budget) return TreeWalkRefusal.OverBound;
        }
        entries.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return null;
    }

    private static string Join(string directory, string name) => directory.EndsWith('\\') ? directory + name : directory + '\\' + name;

    private static TreeWalkResult.Refused Refuse(TreeWalkRefusal reason, string relative) => new(reason, relative);

    private sealed class Frame(SafeFileHandle handle, NativeNode node, int depth, List<NativeDirectoryEntry> entries)
    {
        internal SafeFileHandle Handle { get; } = handle;
        internal NativeNode Node { get; } = node;
        internal int Depth { get; } = depth;
        internal List<NativeDirectoryEntry> Entries { get; } = entries;
        internal int Next { get; set; }
    }
}
