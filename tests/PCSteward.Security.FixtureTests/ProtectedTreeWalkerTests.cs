using System.Buffers.Binary;
using System.Security.AccessControl;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2.3 and §8 N1 (L2, §10 answer 2): the bounded protected-tree walk.
/// The entry parser, the name rules and the bounds factory are portable. The walks run on Windows against fixture
/// trees whose every node is locked for the derived token; each Windows test is reported as failed elsewhere.
/// Case-alias duplicates and control characters cannot be created through Win32 on a default NTFS directory, so they
/// are proven on the pure name rule and the parser.
/// </summary>
[TestClass]
public sealed class ProtectedTreeWalkerTests
{
    private static readonly Func<string, bool, ChocolateyTreeContentPolicy> Classify = ChocolateyTreeCoveragePolicy.Classify;
    private static readonly string[] ExpectedOrder = ["", "a.txt", "d", @"d\b.txt", @"d\e", @"d\e\c.txt", "logs", @"logs\x.log"];
    private static readonly bool[] ExpectedKinds = [true, false, true, false, true, false, true, false];

    [TestMethod]
    public void TheEntryParserReadsAChainAndSkipsDotEntries()
    {
        byte[] buffer = Chain((".", 0x10, 1), ("..", 0x10, 2), ("a.txt", 0x20, 0x1234), ("Dir", 0x10, ulong.MaxValue - 1));
        List<NativeDirectoryEntry> entries = [];

        Assert.IsTrue(ProtectedTreeWalker.TryParseEntries(buffer, entries));
        CollectionAssert.AreEqual(new[] { new NativeDirectoryEntry("a.txt", 0x20, 0x1234), new NativeDirectoryEntry("Dir", 0x10, ulong.MaxValue - 1) },
            entries);
    }

    [TestMethod]
    public void TheEntryParserRefusesEveryMalformedChain()
    {
        byte[] good = Chain(("a", 0x20, 1), ("b", 0x20, 2));
        Assert.IsFalse(Parse(good.AsSpan(0, 50).ToArray()), "truncated header");
        Assert.IsFalse(Parse(Mutate(good, 60, 3)), "odd name length");
        Assert.IsFalse(Parse(Mutate(good, 60, 0)), "empty name");
        Assert.IsFalse(Parse(Mutate(good, 60, 0x10000)), "name beyond the buffer");
        Assert.IsFalse(Parse(Mutate(good, 0, 108)), "misaligned next offset");
        Assert.IsFalse(Parse(Mutate(good, 0, 8)), "next offset inside the header");
        Assert.IsFalse(Parse(Mutate(good, 0, 0x10000)), "next offset beyond the buffer");
    }

    [TestMethod]
    public void NameRulesRefuseControlCharactersNonCanonicalNamesAndCaseAliases()
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        Assert.IsNull(ProtectedTreeWalker.CheckEntryName("lib", seen));
        Assert.AreEqual(TreeWalkRefusal.CaseAliasDuplicate, ProtectedTreeWalker.CheckEntryName("LIB", seen));
        Assert.IsNull(ProtectedTreeWalker.CheckEntryName("café", seen));
        Assert.AreEqual(TreeWalkRefusal.CaseAliasDuplicate, ProtectedTreeWalker.CheckEntryName("CAFÉ", seen));
        Assert.AreEqual(TreeWalkRefusal.ControlCharacter, ProtectedTreeWalker.CheckEntryName("a\u0007b", seen));
        Assert.AreEqual(TreeWalkRefusal.ControlCharacter, ProtectedTreeWalker.CheckEntryName("a\u0085", seen));
        foreach (string name in new[] { "", "a.", "a ", "NUL", "aux.txt", "a:b", "a*", "a?", "a|b", "a\"b", "a<b" })
            Assert.AreEqual(TreeWalkRefusal.InvalidName, ProtectedTreeWalker.CheckEntryName(name, seen), name);
    }

    [TestMethod]
    public void BoundsCanOnlyBeNarrowedBelowTheProfileCeiling()
    {
        Assert.AreEqual(32_768, TreeWalkBounds.Profile.MaximumEntries);
        Assert.AreEqual(32, TreeWalkBounds.Profile.MaximumDepth);
        Assert.AreEqual(4096, TreeWalkBounds.Profile.MaximumPathLength);
        Assert.AreEqual(TreeWalkBounds.Profile, TreeWalkBounds.Narrowed(32_768, 32, 4096));
        Assert.IsNull(TreeWalkBounds.Narrowed(32_769, 32, 4096));
        Assert.IsNull(TreeWalkBounds.Narrowed(10, 33, 4096));
        Assert.IsNull(TreeWalkBounds.Narrowed(10, 3, 4097));
        Assert.IsNull(TreeWalkBounds.Narrowed(0, 3, 100));
        Assert.IsNull(TreeWalkBounds.Narrowed(10, 0, 100));
    }

    [TestMethod]
    public void ALockedTreeWalksCompletelyInDepthFirstOrderWithTheRootFirst()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string root = BuildTree(fixture);
        fixture.LockTree(root);

        TreeWalkResult result = ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None);

        TreeWalkResult.Complete complete = AssertComplete(result);
        CollectionAssert.AreEqual(ExpectedOrder, complete.Nodes.Select(node => node.RelativePath).ToArray());
        Assert.AreEqual(root, complete.Nodes[0].FullPath, ignoreCase: true);
        Assert.AreEqual(Path.Combine(root, "d", "b.txt"), complete.Nodes[3].FullPath, ignoreCase: true);
        Assert.IsTrue(complete.Nodes.All(node => node.Identity.FileId != UInt128.Zero && node.Identity.VolumeSerial == complete.Nodes[0].Identity.VolumeSerial));
        Assert.AreEqual(complete.Nodes.Count, complete.Nodes.Select(node => node.Identity).Distinct().Count());
        CollectionAssert.AreEqual(ExpectedKinds, complete.Nodes.Select(node => node.Directory).ToArray());
        Assert.AreEqual(3, complete.Nodes[1].Length);
    }

    [TestMethod]
    public void ARequesterModifiableNodeRefusesExceptARegularFileUnderLogs()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string root = BuildTree(fixture);
        fixture.LockTree(root);

        fixture.Protect(Path.Combine(root, "logs", "x.log"), ownerRights: true, FileSystemRights.Modify);
        AssertComplete(ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None));

        fixture.Protect(Path.Combine(root, "d", "e", "c.txt"), ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.WriteData);
        AssertRefused(TreeWalkRefusal.RequesterModifiable, @"d\e\c.txt",
            ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None));

        // A parent granting FILE_DELETE_CHILD refuses at the parent itself, before its children are reached.
        fixture.Protect(Path.Combine(root, "d"), ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.DeleteSubdirectoriesAndFiles);
        AssertRefused(TreeWalkRefusal.RequesterModifiable, "d",
            ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None));

        fixture.Protect(Path.Combine(root, "logs"), ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.CreateFiles);
        fixture.Lock(Path.Combine(root, "d"));
        fixture.Lock(Path.Combine(root, "d", "e", "c.txt"));
        AssertRefused(TreeWalkRefusal.RequesterModifiable, "logs",
            ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None));

        // A requester-owned root without an OWNER RIGHTS restriction refuses at the root.
        fixture.Protect(root, ownerRights: false, FileSystemRights.ReadAndExecute);
        AssertRefused(TreeWalkRefusal.RequesterModifiable, "",
            ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None));
    }

    [TestMethod]
    public void AJunctionInsideTheTreeOrAsTheRootRefusesAsReparse()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string root = BuildTree(fixture);
        string outside = fixture.Directory("outside");
        fixture.LockTree(root);
        fixture.Lock(outside);
        N1NativeFixture.Junction(Path.Combine(root, "d", "j"), outside);

        AssertRefused(TreeWalkRefusal.Reparse, @"d\j", ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None));

        string link = fixture.PathOf("rootlink");
        N1NativeFixture.Junction(link, outside);
        AssertRefused(TreeWalkRefusal.Reparse, "", ProtectedTreeWalker.Walk(token, link, TreeWalkBounds.Profile, Classify, CancellationToken.None));
    }

    [TestMethod]
    public void AChildReplacedAfterTheParentEnumerationIsAParentIdentityMismatch()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string root = BuildTree(fixture);
        fixture.LockTree(root);
        string target = Path.Combine(root, "d", "b.txt");
        int replaced = 0;

        TreeWalkResult result = ProtectedTreeWalker.WalkWithHookForTests(token, root, TreeWalkBounds.Profile, Classify, full =>
        {
            if (!string.Equals(full, target, StringComparison.OrdinalIgnoreCase)) return;
            File.Move(target, fixture.PathOf("moved.txt"));
            File.WriteAllText(target, "new");
            fixture.Lock(target);
            replaced++;
        }, CancellationToken.None);

        Assert.AreEqual(1, replaced);
        AssertRefused(TreeWalkRefusal.ParentIdentityMismatch, @"d\b.txt", result);
    }

    [TestMethod]
    public void AbsenceComesOnlyFromTheHeldParentEnumerationAndAListedUnopenableChildIsUnreadable()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string root = fixture.Directory("root");
        fixture.File(@"root\present.txt");
        fixture.Directory(@"root\sub");
        string unreadable = fixture.File(@"root\unreadable.txt");
        fixture.LockTree(root);
        // The test process cannot open it; it is deleted through the parent's FILE_DELETE_CHILD.
        fixture.ProtectExactly(unreadable, (N1NativeFixture.OwnerRights, FileSystemRights.Synchronize));
        using SafeFileHandle parent = N1NativeFixture.Open(root, true, out NativeNode parentNode);

        Assert.AreEqual(ChildPresence.Present, Locate(parent, parentNode, "present.txt", false, out SafeFileHandle? child, out NativeNode node, out _));
        using (child)
        {
            Assert.AreEqual(Path.Combine(root, "present.txt"), node.FullPath, ignoreCase: true);
        }
        Assert.AreEqual(ChildPresence.Absent, Locate(parent, parentNode, "missing.txt", false, out child, out _, out _));
        Assert.IsNull(child);
        Assert.AreEqual(ChildPresence.Refused, Locate(parent, parentNode, "unreadable.txt", false, out child, out _, out TreeWalkRefusal refusal));
        Assert.IsNull(child);
        Assert.AreEqual(TreeWalkRefusal.Unreadable, refusal);
        Assert.AreEqual(ChildPresence.Refused, Locate(parent, parentNode, "sub", false, out _, out _, out refusal));
        Assert.AreEqual(TreeWalkRefusal.Unreadable, refusal, "a listed child of the other kind is not absent");
        Assert.AreEqual(ChildPresence.Refused, Locate(parent, parentNode, "..", true, out _, out _, out refusal));
        Assert.AreEqual(TreeWalkRefusal.InvalidName, refusal);

        AssertRefused(TreeWalkRefusal.Unreadable, "unreadable.txt",
            ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None));
        AssertRefused(TreeWalkRefusal.Unreadable, "", ProtectedTreeWalker.Walk(token, fixture.PathOf("missing"), TreeWalkBounds.Profile, Classify, CancellationToken.None));
        AssertRefused(TreeWalkRefusal.Unreadable, "",
            ProtectedTreeWalker.Walk(token, Path.Combine(root, "present.txt"), TreeWalkBounds.Profile, Classify, CancellationToken.None));
    }

    [TestMethod]
    public void EntryDepthAndPathBoundsRefuseAsOverBound()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string root = BuildTree(fixture);
        fixture.LockTree(root);

        AssertComplete(ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Narrowed(8, 3, 4096)!, Classify, CancellationToken.None));
        // Seven nodes are accepted; enumerating logs would make an eighth.
        AssertRefused(TreeWalkRefusal.OverBound, "logs",
            ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Narrowed(7, 3, 4096)!, Classify, CancellationToken.None));
        AssertRefused(TreeWalkRefusal.OverBound, "",
            ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Narrowed(3, 3, 4096)!, Classify, CancellationToken.None));
        AssertRefused(TreeWalkRefusal.OverBound, @"d\e\c.txt",
            ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Narrowed(100, 2, 4096)!, Classify, CancellationToken.None));
        AssertRefused(TreeWalkRefusal.OverBound, "a.txt",
            ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Narrowed(100, 3, root.Length + 5)!, Classify, CancellationToken.None));
        AssertRefused(TreeWalkRefusal.OverBound, "",
            ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Narrowed(100, 3, root.Length - 1)!, Classify, CancellationToken.None));
    }

    [TestMethod]
    public void CancellationBeforeOrDuringTheWalkRefuses()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string root = BuildTree(fixture);
        fixture.LockTree(root);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        AssertRefused(TreeWalkRefusal.Cancelled, "", ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, cancelled.Token));

        using CancellationTokenSource during = new();
        TreeWalkResult result = ProtectedTreeWalker.WalkWithHookForTests(token, root, TreeWalkBounds.Profile, Classify,
            _ => during.Cancel(), during.Token);
        Assert.AreEqual(TreeWalkRefusal.Cancelled, Assert.IsInstanceOfType<TreeWalkResult.Refused>(result).Reason);
    }

    /// <summary>root\a.txt, root\d\b.txt, root\d\e\c.txt, root\logs\x.log; the walk's depth is 3.</summary>
    private static string BuildTree(N1NativeFixture fixture)
    {
        string root = fixture.Directory("root");
        fixture.File(@"root\a.txt", "abc");
        fixture.Directory(@"root\d\e");
        fixture.File(@"root\d\b.txt");
        fixture.File(@"root\d\e\c.txt");
        fixture.Directory(@"root\logs");
        fixture.File(@"root\logs\x.log");
        return root;
    }

    private static ChildPresence Locate(SafeFileHandle parent, NativeNode parentNode, string name, bool directory,
        out SafeFileHandle? child, out NativeNode node, out TreeWalkRefusal refusal) =>
        ProtectedTreeWalker.LocateChild(parent, parentNode, name, directory, CancellationToken.None, out child, out node, out refusal);

    private static TreeWalkResult.Complete AssertComplete(TreeWalkResult result) =>
        result as TreeWalkResult.Complete ?? throw new AssertFailedException($"expected Complete, got {result}");

    private static void AssertRefused(TreeWalkRefusal reason, string relative, TreeWalkResult result) =>
        Assert.AreEqual(new TreeWalkResult.Refused(reason, relative), result);

    private static bool Parse(byte[] buffer) => ProtectedTreeWalker.TryParseEntries(buffer, []);

    private static byte[] Mutate(byte[] source, int offset, uint value)
    {
        byte[] copy = (byte[])source.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    /// <summary>Builds a FILE_ID_BOTH_DIR_INFO chain with 8-aligned entries.</summary>
    private static byte[] Chain(params (string Name, uint Attributes, ulong FileId)[] entries)
    {
        List<byte> bytes = [];
        for (int index = 0; index < entries.Length; index++)
        {
            byte[] name = Encoding.Unicode.GetBytes(entries[index].Name);
            int size = (ProtectedTreeWalker.EntryHeaderBytes + name.Length + 7) / 8 * 8;
            byte[] entry = new byte[size];
            BinaryPrimitives.WriteUInt32LittleEndian(entry, index == entries.Length - 1 ? 0u : (uint)size);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(56), entries[index].Attributes);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(60), (uint)name.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(96), entries[index].FileId);
            name.CopyTo(entry, ProtectedTreeWalker.EntryHeaderBytes);
            bytes.AddRange(entry);
        }
        return [.. bytes];
    }
}
