using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2.2 and §8 N1 (M2, M3, M5, L4): <see cref="RequesterAccess"/> over
/// fixture DACLs, evaluated with the derived identification token whose shape each test asserts first. Real fixtures
/// are user-owned (a standard-user runner cannot assign SYSTEM or a High label); SYSTEM-owned and High-label cases run
/// the same AccessCheck on in-memory descriptors. Every test is Windows-only and is reported as failed elsewhere.
/// </summary>
[TestClass]
public sealed class RequesterAccessNativeTests
{
    private const FileSystemRights Read = FileSystemRights.ReadAndExecute;

    [TestMethod]
    public void AProtectedUserOwnedNodeWithAnOwnerRightsRestrictionIsDeniedInBothModesYetStillRequesterOwned()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string parent = fixture.Directory("p"), file = fixture.File(@"p\f"), directory = fixture.Directory(@"p\d");
        fixture.Lock(file);
        fixture.Lock(directory);
        fixture.Lock(parent);
        using SafeFileHandle p = N1NativeFixture.Open(parent, true, out _), f = N1NativeFixture.Open(file, false, out _),
            d = N1NativeFixture.Open(directory, true, out _);

        Assert.AreEqual(RequesterModification.Denied, RequesterAccess.Evaluate(token, f, false, p, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Denied, RequesterAccess.Evaluate(token, d, true, p, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Denied, RequesterAccess.Evaluate(token, d, true, p, AccessMode.Ancestor));
        Assert.AreEqual(RequesterModification.Denied, RequesterAccess.Evaluate(token, p, true, null, AccessMode.Ancestor));
        // Ancestor mode applies to directories only; misuse refuses.
        Assert.AreEqual(RequesterModification.Unreadable, RequesterAccess.Evaluate(token, f, false, p, AccessMode.Ancestor));
        // L4: the OWNER RIGHTS ACE removes the owner's implicit rights, but the node is still owned by the requester.
        Assert.IsTrue(RequesterAccess.IsRequesterOwned(token, f));
        Assert.IsTrue(RequesterAccess.IsRequesterOwned(token, d));
    }

    [TestMethod]
    public void ARequesterWritableFileOrDirectoryIsGranted()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string parent = fixture.Directory("p"), write = fixture.File(@"p\w"), attributes = fixture.File(@"p\a");
        string addFile = fixture.Directory(@"p\af"), modify = fixture.Directory(@"p\m");
        fixture.Protect(write, ownerRights: true, Read | FileSystemRights.WriteData);
        fixture.Protect(attributes, ownerRights: true, Read | FileSystemRights.WriteAttributes);
        fixture.Protect(addFile, ownerRights: true, Read | FileSystemRights.CreateFiles);
        fixture.Protect(modify, ownerRights: true, FileSystemRights.Modify);
        fixture.Lock(parent);
        using SafeFileHandle p = N1NativeFixture.Open(parent, true, out _), w = N1NativeFixture.Open(write, false, out _),
            a = N1NativeFixture.Open(attributes, false, out _), af = N1NativeFixture.Open(addFile, true, out _),
            m = N1NativeFixture.Open(modify, true, out _);

        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, w, false, p, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, w, false, null, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, a, false, p, AccessMode.Node));
        // Adding a file is a Node-mode modification of a directory but not an ancestor replacement right.
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, af, true, p, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Denied, RequesterAccess.Evaluate(token, af, true, p, AccessMode.Ancestor));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, m, true, p, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, m, true, p, AccessMode.Ancestor));
    }

    [TestMethod]
    public void AParentThatGrantsDeleteChildMakesEveryChildModifiable()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string parent = fixture.Directory("p"), file = fixture.File(@"p\f"), directory = fixture.Directory(@"p\d");
        fixture.Lock(file);
        fixture.Lock(directory);
        fixture.Protect(parent, ownerRights: true, Read | FileSystemRights.DeleteSubdirectoriesAndFiles);
        using SafeFileHandle p = N1NativeFixture.Open(parent, true, out _), f = N1NativeFixture.Open(file, false, out _),
            d = N1NativeFixture.Open(directory, true, out _);

        Assert.AreEqual(RequesterModification.Denied, RequesterAccess.Evaluate(token, f, false, null, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, f, false, p, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Denied, RequesterAccess.Evaluate(token, d, true, null, AccessMode.Ancestor));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, d, true, p, AccessMode.Ancestor));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, p, true, null, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, p, true, null, AccessMode.Ancestor));
    }

    [TestMethod]
    public void ARequesterOwnedNodeWithoutAnOwnerRightsAceIsGrantedThroughImplicitWriteDac()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string parent = fixture.Directory("p"), file = fixture.File(@"p\f"), directory = fixture.Directory(@"p\d");
        fixture.Protect(file, ownerRights: false, Read);
        fixture.Protect(directory, ownerRights: false, Read);
        fixture.Lock(parent);
        using SafeFileHandle p = N1NativeFixture.Open(parent, true, out _), f = N1NativeFixture.Open(file, false, out _),
            d = N1NativeFixture.Open(directory, true, out _);

        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, f, false, p, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, d, true, p, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, d, true, p, AccessMode.Ancestor));
        Assert.IsTrue(RequesterAccess.IsRequesterOwned(token, f));
    }

    [TestMethod]
    public void AnAncestorWhereTheRequesterMayOnlyAddSubfoldersPassesAncestorModeButNotNodeMode()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string ancestor = fixture.Directory("programdata");
        fixture.Protect(ancestor, ownerRights: true, Read | FileSystemRights.CreateDirectories);
        using SafeFileHandle a = N1NativeFixture.Open(ancestor, true, out _);

        Assert.AreEqual(RequesterModification.Denied, RequesterAccess.Evaluate(token, a, true, null, AccessMode.Ancestor));
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.Evaluate(token, a, true, null, AccessMode.Node));
    }

    [TestMethod]
    public void HighIntegrityLabelsSystemOwnersAndOwnerRightsDecideInMemoryDescriptors()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        string user = token.Sid;
        const string ReadExecute = "0x1200a9", Modify = "0x1301bf", AddSubdirectory = "0x1200ad";
        (string Sddl, RequesterModification File, RequesterModification Directory, RequesterModification Ancestor)[] cases =
        [
            // A High no-write-up label removes every write-class right from the Medium token, even with Modify in the DACL.
            ($"O:SYG:SYD:P(A;;{Modify};;;{user})S:(ML;;NW;;;HI)", RequesterModification.Denied, RequesterModification.Denied, RequesterModification.Denied),
            ($"O:{user}G:{user}D:P(A;;{Modify};;;{user})S:(ML;;NW;;;HI)", RequesterModification.Denied, RequesterModification.Denied, RequesterModification.Denied),
            ($"O:SYG:SYD:P(A;;{Modify};;;{user})S:(ML;;NW;;;LW)", RequesterModification.Granted, RequesterModification.Granted, RequesterModification.Granted),
            ($"O:SYG:SYD:P(A;;{Modify};;;{user})", RequesterModification.Granted, RequesterModification.Granted, RequesterModification.Granted),
            // SYSTEM-owned with read only: the canonical protected node.
            ($"O:SYG:SYD:P(A;;{ReadExecute};;;{user})", RequesterModification.Denied, RequesterModification.Denied, RequesterModification.Denied),
            ($"O:SYG:SYD:P(A;;{AddSubdirectory};;;{user})", RequesterModification.Granted, RequesterModification.Granted, RequesterModification.Denied),
            // Owner-implied WRITE_DAC, removed by an OWNER RIGHTS ACE unless that ACE itself grants WRITE_DAC.
            ($"O:{user}G:{user}D:P(A;;{ReadExecute};;;{user})", RequesterModification.Granted, RequesterModification.Granted, RequesterModification.Granted),
            ($"O:{user}G:{user}D:P(A;;{ReadExecute};;;OW)(A;;{ReadExecute};;;{user})", RequesterModification.Denied, RequesterModification.Denied, RequesterModification.Denied),
            ($"O:{user}G:{user}D:P(A;;0x1600a9;;;OW)", RequesterModification.Granted, RequesterModification.Granted, RequesterModification.Granted),
            // An explicit deny of DELETE does not hide a granted WRITE_OWNER.
            ($"O:SYG:SYD:P(D;;SD;;;{user})(A;;0x1a00a9;;;{user})", RequesterModification.Granted, RequesterModification.Granted, RequesterModification.Granted),
            // A NULL DACL grants everything.
            ("O:SYG:SYD:NO_ACCESS_CONTROL", RequesterModification.Granted, RequesterModification.Granted, RequesterModification.Granted),
        ];
        foreach ((string sddl, RequesterModification file, RequesterModification directory, RequesterModification ancestor) in cases)
        {
            byte[] descriptor = Binary(sddl);
            Assert.AreEqual(file, RequesterAccess.EvaluateDescriptorForTests(token, descriptor, false, AccessMode.Node), sddl);
            Assert.AreEqual(directory, RequesterAccess.EvaluateDescriptorForTests(token, descriptor, true, AccessMode.Node), sddl);
            Assert.AreEqual(ancestor, RequesterAccess.EvaluateDescriptorForTests(token, descriptor, true, AccessMode.Ancestor), sddl);
        }
        // N1 review H1: a conditional (callback) allow/deny ACE or an object ACE is never evaluated without the claims or
        // resource attributes it may depend on: Unreadable, never Denied. Built in binary form (no reliance on SDDL support for
        // conditional expressions); the opaque payload carries the "artx" conditional-expression signature.
        SecurityIdentifier requester = new(user);
        byte[] conditionalPayload = [0x61, 0x72, 0x74, 0x78, 0x00, 0x00, 0x00, 0x00];
        GenericAce[] nonPlain =
        [
            new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, 0x1F01FF, requester, isCallback: true, conditionalPayload),
            new CommonAce(AceFlags.None, AceQualifier.AccessDenied, 0x1F01FF, requester, isCallback: true, conditionalPayload),
            new ObjectAce(AceFlags.None, AceQualifier.AccessAllowed, 0x1, requester, ObjectAceFlags.ObjectAceTypePresent,
                new Guid("bf967a86-0de6-11d0-a285-00aa003049e2"), Guid.Empty, isCallback: false, opaque: null),
        ];
        foreach (GenericAce ace in nonPlain)
        {
            RawSecurityDescriptor shaped = new($"O:{user}G:{user}D:P(A;;{ReadExecute};;;OW)(A;;{ReadExecute};;;{user})");
            shaped.DiscretionaryAcl!.InsertAce(shaped.DiscretionaryAcl.Count, ace);
            byte[] descriptor = new byte[shaped.BinaryLength];
            shaped.GetBinaryForm(descriptor, 0);
            Assert.AreEqual(RequesterModification.Unreadable, RequesterAccess.EvaluateDescriptorForTests(token, descriptor, false, AccessMode.Node), ace.AceType.ToString());
            Assert.AreEqual(RequesterModification.Unreadable, RequesterAccess.EvaluateDescriptorForTests(token, descriptor, true, AccessMode.Ancestor), ace.AceType.ToString());
        }
        // The same descriptor without the extra ACE is evaluated normally (plain ACEs only): Denied.
        Assert.AreEqual(RequesterModification.Denied, RequesterAccess.EvaluateDescriptorForTests(token,
            Binary($"O:{user}G:{user}D:P(A;;{ReadExecute};;;OW)(A;;{ReadExecute};;;{user})"), false, AccessMode.Node));
        // A descriptor without an owner cannot be evaluated: Unreadable, never Denied.
        Assert.AreEqual(RequesterModification.Unreadable,
            RequesterAccess.EvaluateDescriptorForTests(token, Binary($"D:P(A;;{ReadExecute};;;{user})"), false, AccessMode.Node));
    }

    [TestMethod]
    public void AncestryRunsFromTheLexicalDriveRootAndTheFirstNonDeniedAncestorDecides()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        // System directories: C:\ tolerates Authenticated Users adding subfolders; Windows and System32 are read-only.
        string drivers = Path.Combine(N1NativeFixture.FinalPathOf(Environment.SystemDirectory, directory: true), "drivers");
        Assert.AreEqual(RequesterModification.Denied, RequesterAccess.EvaluateAncestry(token, drivers));
        // The temp chain is requester-modifiable somewhere above the fixture.
        Assert.AreEqual(RequesterModification.Granted, RequesterAccess.EvaluateAncestry(token, fixture.Directory(@"p\root")));
        // A drive root, a non-canonical or a missing ancestor never yields Denied.
        Assert.AreEqual(RequesterModification.Unreadable, RequesterAccess.EvaluateAncestry(token, drivers[..3]));
        Assert.AreEqual(RequesterModification.Unreadable, RequesterAccess.EvaluateAncestry(token, drivers + @"\..\drivers"));
        Assert.AreEqual(RequesterModification.Unreadable, RequesterAccess.EvaluateAncestry(token, drivers.ToLowerInvariant() + "/x"));
        Assert.AreEqual(RequesterModification.Unreadable, RequesterAccess.EvaluateAncestry(token, Path.Combine(drivers, "missing-" + Guid.NewGuid().ToString("N"), "x")));
    }

    [TestMethod]
    public void AKindMismatchOrAFileAsParentIsUnreadable()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string file = fixture.File("f"), directory = fixture.Directory("d");
        fixture.Lock(file);
        fixture.Lock(directory);
        using SafeFileHandle f = N1NativeFixture.Open(file, false, out _), d = N1NativeFixture.Open(directory, true, out _);

        Assert.AreEqual(RequesterModification.Unreadable, RequesterAccess.Evaluate(token, f, true, null, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Unreadable, RequesterAccess.Evaluate(token, d, false, null, AccessMode.Node));
        Assert.AreEqual(RequesterModification.Unreadable, RequesterAccess.Evaluate(token, f, false, f, AccessMode.Node));
    }

    private static byte[] Binary(string sddl)
    {
        RawSecurityDescriptor descriptor = new(sddl);
        byte[] binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);
        return binary;
    }
}
