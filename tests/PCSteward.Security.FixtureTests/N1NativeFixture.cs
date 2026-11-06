using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PCSteward.Provider.Chocolatey.Native;
using Win32 = PCSteward.Provider.Chocolatey.Native.ProtectedNativeMethods;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Task 50 design rev 6 §8 N1: a GUID temp root whose fixture nodes get explicit, protected DACLs and an explicit owner
/// (the user SID; a standard-user runner cannot assign SYSTEM, so SYSTEM-owned descriptors are evaluated in memory).
/// Every fixture DACL grants FullControl to the logon SID, which <see cref="RequesterToken.ForTestsFromDerivedToken"/>
/// makes deny-only: the test process can always clean up, and that ACE is never credited to the evaluated token.
/// </summary>
internal sealed class N1NativeFixture : IDisposable
{
    internal static readonly SecurityIdentifier OwnerRights = new("S-1-3-4");
    internal static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);

    internal N1NativeFixture()
    {
        RequireWindows("N1 fixture ACLs");
        string created = Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), $"pcsteward-n1-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(created);
        Root = FinalPathOf(created, directory: true);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        User = identity.User ?? throw new InvalidOperationException("No user SID.");
        Assert.IsTrue(NativeTokenReader.TryReadGroups(identity.AccessToken, out IReadOnlyList<NativeTokenGroup> groups));
        Logon = new SecurityIdentifier(groups.First(group => (group.Attributes & Win32.SeGroupLogonId) == Win32.SeGroupLogonId).Sid);
        GrantOwnerFullControl(Root, User);
    }

    /// <summary>
    /// CI hardening: the CI user's TEMP sits under the checkout, where that user is granted only Modify, so whether
    /// <see cref="Protect"/> may set the owner (WRITE_OWNER) depended on what the runner's inherited ACL happened to
    /// provide (intermittent <c>UnauthorizedAccessException</c> in <see cref="LockTree"/>). The creator owns the new root and
    /// therefore holds WRITE_DAC implicitly, so it adds one inheritable FullControl ACE for itself, keeping every inherited
    /// ACE. Nodes the tests lock still get their own protected DACLs, so no evaluated verdict changes.
    /// </summary>
    private static void GrantOwnerFullControl(string root, SecurityIdentifier user)
    {
        DirectoryInfo directory = new(root);
        DirectorySecurity security = directory.GetAccessControl(AccessControlSections.Access);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    internal string Root { get; }
    internal SecurityIdentifier User { get; }
    internal SecurityIdentifier Logon { get; }

    internal string PathOf(string relative) => Path.Combine(Root, relative);

    internal string Directory(string relative)
    {
        string path = PathOf(relative);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    internal string File(string relative, string content = "fixture")
    {
        string path = PathOf(relative);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Replaces the node's owner and DACL: protected (no inheritance), logon SID FullControl, OWNER RIGHTS read/execute when
    /// <paramref name="ownerRights"/> (which removes the owner's implicit READ_CONTROL/WRITE_DAC), plus
    /// <paramref name="userRights"/> for the user SID (none when zero), with <paramref name="owner"/> or the user as owner.
    /// </summary>
    internal void Protect(string path, bool ownerRights, FileSystemRights userRights, SecurityIdentifier? owner = null)
    {
        bool directory = System.IO.Directory.Exists(path);
        FileSystemSecurity security = directory ? new DirectorySecurity() : new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner ?? User);
        security.AddAccessRule(new FileSystemAccessRule(Logon, FileSystemRights.FullControl, AccessControlType.Allow));
        if (ownerRights) security.AddAccessRule(new FileSystemAccessRule(OwnerRights, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        if (userRights != 0) security.AddAccessRule(new FileSystemAccessRule(User, userRights, AccessControlType.Allow));
        if (directory) new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security);
        else new FileInfo(path).SetAccessControl((FileSecurity)security);
    }

    /// <summary>The usual protected shape: user-owned, OWNER RIGHTS read/execute, user read/execute.</summary>
    internal void Lock(string path) => Protect(path, ownerRights: true, FileSystemRights.ReadAndExecute);

    /// <summary>Owner = user, protected DACL with exactly <paramref name="aces"/> (no logon cleanup ACE: delete through the parent).</summary>
    internal void ProtectExactly(string path, params (SecurityIdentifier Sid, FileSystemRights Rights)[] aces)
    {
        FileSecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(User);
        foreach ((SecurityIdentifier sid, FileSystemRights rights) in aces)
            security.AddAccessRule(new FileSystemAccessRule(sid, rights, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    /// <summary>Locks every node below <paramref name="root"/> and the root itself (create junctions afterwards).</summary>
    internal void LockTree(string root)
    {
        // Children before parents: locking a parent with a protected DACL re-propagates inheritance and strips the ACEs a
        // child inherited (including WRITE_OWNER), so a later lock on that child would be refused. Pre-order reversed puts
        // every child ahead of its parent.
        string[] entries = [.. System.IO.Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)];
        for (int index = entries.Length - 1; index >= 0; index--) Lock(entries[index]);
        Lock(root);
    }

    internal static void Junction(string link, string target)
    {
        using Process process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            ArgumentList = { "/d", "/c", "mklink", "/J", link, target },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("cmd.exe did not start.");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, "mklink /J failed");
    }

    internal static SafeFileHandle Open(string path, bool directory, out NativeNode node)
    {
        SafeFileHandle? handle = ProtectedNodeReader.TryOpen(path, directory, out node, out ProtectedOpenFailure failure);
        Assert.IsNotNull(handle, $"{path}: {failure}");
        return handle;
    }

    /// <summary>
    /// Asserts the derived token's shape through <see cref="TokenFacts"/> (§8; the factory bypasses Validate): exactly
    /// Medium, only allow-listed privileges, not restricted, not AppContainer, Administrators deny-only (or absent on a
    /// standard-user runner), and every logon SID deny-only.
    /// </summary>
    internal static RequesterToken DerivedToken()
    {
        RequesterToken token = RequesterToken.ForTestsFromDerivedToken();
        Assert.AreEqual(RequesterTokenPolicy.MediumIntegrityRid, token.Facts.IntegrityRid);
        Assert.IsTrue(token.Facts.Privileges.All(RequesterTokenPolicy.AllowedPrivileges.Contains), string.Join(",", token.Facts.Privileges));
        Assert.IsFalse(token.Facts.Restricted || token.Facts.AppContainer);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        Assert.AreEqual(identity.User!.Value, token.Sid);
        Assert.IsTrue(NativeTokenReader.TryReadGroups(identity.AccessToken, out IReadOnlyList<NativeTokenGroup> processGroups));
        AdministratorsMembership expected = processGroups.Any(group => group.Sid == NativeTokenReader.AdministratorsSid)
            ? AdministratorsMembership.DenyOnly : AdministratorsMembership.Absent;
        Assert.AreEqual(expected, token.Facts.Administrators);
        Assert.IsTrue(NativeTokenReader.TryReadGroups(token.Handle, out IReadOnlyList<NativeTokenGroup> groups));
        string[] logonSids = [.. processGroups.Where(group => (group.Attributes & Win32.SeGroupLogonId) == Win32.SeGroupLogonId).Select(group => group.Sid)];
        Assert.AreNotEqual(0, logonSids.Length, "the process token has no logon SID");
        foreach (string logon in logonSids)
            Assert.IsTrue(groups.Any(group => group.Sid == logon) &&
                groups.Where(group => group.Sid == logon).All(group => (group.Attributes & Win32.SeGroupUseForDenyOnly) != 0),
                "the logon SID must be deny-only in the derived token");
        return token;
    }

    internal static void RequireWindows(string evidence)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException($"Windows-native test ({evidence}): it runs on Windows CI and is reported as failed on other platforms.");
    }

    /// <summary>The long, final path of a node (the temp path may contain 8.3 short names).</summary>
    internal static string FinalPathOf(string path, bool directory)
    {
        using SafeFileHandle handle = Win32.CreateFile(path, Win32.ReadOnlyOpenAccess, 7, IntPtr.Zero, Win32.OpenExisting,
            directory ? Win32.FileFlagBackupSemantics : 0, IntPtr.Zero);
        Assert.IsFalse(handle.IsInvalid, path);
        Assert.IsTrue(ProtectedNodeReader.TryGetFinalPath(handle, out string final), path);
        return final;
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Root, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
