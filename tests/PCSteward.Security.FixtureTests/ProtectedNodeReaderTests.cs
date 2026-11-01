using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2.3 and §8 N1: the protected node reader. Path canonicalization is
/// portable; the opens, holds, bounded reads and the reparse and final-path refusals are Windows-only (reported as
/// failed elsewhere).
/// </summary>
[TestClass]
public sealed class ProtectedNodeReaderTests
{
    [TestMethod]
    [DataRow(@"C:\")]
    [DataRow(@"C:\ProgramData")]
    [DataRow(@"d:\a\b c\file.txt")]
    [DataRow(@"C:\ProgramData\chocolatey\lib\.chocolatey")]
    public void CanonicalLocalPathsAreAccepted(string path) => Assert.IsTrue(ProtectedNodeReader.IsCanonicalLocalPath(path));

    [TestMethod]
    [DataRow("")]
    [DataRow(@"C:")]
    [DataRow(@"C:ProgramData")]
    [DataRow(@"C:\ProgramData\")]
    [DataRow(@"C:\ProgramData\\chocolatey")]
    [DataRow(@"C:\ProgramData\.\chocolatey")]
    [DataRow(@"C:\ProgramData\..\Windows")]
    [DataRow(@"C:\ProgramData\chocolatey.")]
    [DataRow(@"C:\ProgramData\chocolatey ")]
    [DataRow(@"C:\ProgramData/chocolatey")]
    [DataRow(@"C:\ProgramData\choco:stream")]
    [DataRow(@"C:\ProgramData\choco*")]
    [DataRow(@"C:\ProgramData\choco?")]
    [DataRow("C:\\ProgramData\\a\u0001b")]
    [DataRow(@"C:\ProgramData\NUL")]
    [DataRow(@"C:\ProgramData\con.txt")]
    [DataRow(@"C:\ProgramData\COM1 .log")]
    [DataRow(@"\\server\share\x")]
    [DataRow(@"\\?\C:\ProgramData")]
    [DataRow(@"1:\x")]
    public void NonCanonicalPathsAreRefusedBeforeAnyOpen(string path) => Assert.IsFalse(ProtectedNodeReader.IsCanonicalLocalPath(path));

    [TestMethod]
    public void OverlongPathsAreRefused()
    {
        Assert.IsTrue(ProtectedNodeReader.IsCanonicalLocalPath(@"C:\" + string.Join('\\', Enumerable.Repeat(new string('a', 200), 20))));
        Assert.IsFalse(ProtectedNodeReader.IsCanonicalLocalPath(@"C:\" + string.Join('\\', Enumerable.Repeat(new string('a', 200), 21))));
        Assert.IsFalse(ProtectedNodeReader.IsCanonicalLocalPath(@"C:\" + new string('a', 256)));
    }

    [TestMethod]
    public void AFileAndADirectoryOpenWithIdentityLengthAndAShareReadHold()
    {
        using N1NativeFixture fixture = new();
        string file = fixture.File("f.txt", "hello world"), directory = fixture.Directory("d");

        using (SafeFileHandle handle = N1NativeFixture.Open(file, false, out NativeNode node))
        {
            Assert.AreEqual(file, node.FullPath, ignoreCase: true);
            Assert.AreEqual(string.Empty, node.RelativePath);
            Assert.IsFalse(node.Directory);
            Assert.AreEqual(11, node.Length);
            Assert.AreNotEqual(UInt128.Zero, node.Identity.FileId);
            Assert.IsTrue(ProtectedNodeReader.TryReadIdentity(handle, out NativeFileIdentity identity));
            Assert.AreEqual(node.Identity, identity);
            // FILE_SHARE_READ only: nobody can write, delete or rename while the handle is held; reading stays possible.
            Assert.ThrowsExactly<IOException>(() => File.Open(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose());
            Assert.ThrowsExactly<IOException>(() => File.Delete(file));
            Assert.ThrowsExactly<IOException>(() => File.Move(file, file + ".moved"));
            Assert.AreEqual("hello world", File.ReadAllText(file));
        }

        using (SafeFileHandle handle = N1NativeFixture.Open(directory, true, out NativeNode node))
        {
            Assert.IsTrue(node.Directory);
            Assert.AreEqual(0, node.Length);
            Assert.IsFalse(ProtectedNodeReader.TryReadBounded(handle, 1024, out _), "a directory handle has no bounded content");
            Assert.ThrowsExactly<IOException>(() => Directory.Move(directory, directory + "-moved"));
        }
    }

    [TestMethod]
    public void BoundedReadsAndHashesRequireEndOfFileAtTheObservedLength()
    {
        using N1NativeFixture fixture = new();
        byte[] content = Encoding.UTF8.GetBytes(new string('x', 200_000));
        string file = fixture.PathOf("big.bin");
        File.WriteAllBytes(file, content);
        using SafeFileHandle handle = N1NativeFixture.Open(file, false, out _);

        Assert.IsTrue(ProtectedNodeReader.TryReadBounded(handle, content.Length, out byte[] bytes));
        CollectionAssert.AreEqual(content, bytes);
        Assert.IsFalse(ProtectedNodeReader.TryReadBounded(handle, content.Length - 1, out bytes));
        Assert.AreEqual(0, bytes.Length);
        Assert.IsFalse(ProtectedNodeReader.TryReadBounded(handle, -1, out _));

        Assert.IsTrue(ProtectedNodeReader.TryHashBounded(handle, content.Length, out NativeObservedFile observed));
        Assert.AreEqual(new NativeObservedFile(content.Length, Convert.ToHexString(SHA256.HashData(content))), observed);
        Assert.IsFalse(ProtectedNodeReader.TryHashBounded(handle, content.Length - 1, out _));
    }

    [TestMethod]
    public void AJunctionIsRefusedAsAReparsePointAndAPathThroughItAsAFinalPathMismatch()
    {
        using N1NativeFixture fixture = new();
        string target = fixture.Directory("target");
        fixture.File(@"target\f.txt");
        string link = fixture.PathOf("link");
        N1NativeFixture.Junction(link, target);

        Assert.IsNull(ProtectedNodeReader.TryOpen(link, true, out NativeNode node, out ProtectedOpenFailure failure));
        Assert.AreEqual(ProtectedOpenFailure.Reparse, failure);
        Assert.AreEqual(default, node);
        // Without backup semantics the directory junction either fails to open or is seen as a reparse point; never opened.
        Assert.IsNull(ProtectedNodeReader.TryOpen(link, false, out _, out failure));
        Assert.IsTrue(failure is ProtectedOpenFailure.OpenFailed or ProtectedOpenFailure.Reparse, failure.ToString());

        Assert.IsNull(ProtectedNodeReader.TryOpen(Path.Combine(link, "f.txt"), false, out _, out failure));
        Assert.AreEqual(ProtectedOpenFailure.FinalPathMismatch, failure);
        Assert.IsNotNull(ProtectedNodeReader.TryOpen(Path.Combine(target, "f.txt"), false, out _, out failure));
    }

    [TestMethod]
    public void KindMismatchesMissingNodesAndNonCanonicalPathsAreRefusedWithTheirReason()
    {
        using N1NativeFixture fixture = new();
        string file = fixture.File("f.txt"), directory = fixture.Directory("d");

        Assert.IsNull(ProtectedNodeReader.TryOpen(file, true, out _, out ProtectedOpenFailure failure));
        Assert.AreEqual(ProtectedOpenFailure.KindMismatch, failure);
        Assert.IsNull(ProtectedNodeReader.TryOpen(directory, false, out _, out failure));
        Assert.AreEqual(ProtectedOpenFailure.OpenFailed, failure, "a directory cannot be opened without backup semantics");
        Assert.IsNull(ProtectedNodeReader.TryOpen(fixture.PathOf("missing"), false, out _, out failure));
        Assert.AreEqual(ProtectedOpenFailure.OpenFailed, failure);
        Assert.IsNull(ProtectedNodeReader.TryOpen(directory + @"\..\f.txt", false, out _, out failure));
        Assert.AreEqual(ProtectedOpenFailure.InvalidPath, failure);
        Assert.IsNull(ProtectedNodeReader.TryOpen(file.ToUpperInvariant() + ".", false, out _, out failure));
        Assert.AreEqual(ProtectedOpenFailure.InvalidPath, failure);
        // A differently cased request names the same entry; the reported path is the file system's own.
        using SafeFileHandle? upper = ProtectedNodeReader.TryOpen(file.ToUpperInvariant(), false, out NativeNode node, out failure);
        Assert.IsNotNull(upper, failure.ToString());
        Assert.AreEqual(file, node.FullPath, ignoreCase: true);
    }
}
