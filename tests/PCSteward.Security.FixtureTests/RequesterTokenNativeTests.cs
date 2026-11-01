using System.Diagnostics;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey.Native;
using Win32 = PCSteward.Provider.Chocolatey.Native.ProtectedNativeMethods;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13; Task 50 design rev 6 §2.1 and §8 N1 (H2, H3, M5): the native token factories refuse with
/// their specific reason. CI runs without UAC split tokens (elevation type Default), so the positive linked-token match
/// is owner-host evidence (§9); these tests prove the refusals, the peer-handle rules and the derived token's shape.
/// Each expected verdict is derived from the runner's own elevation type, so the tests are deterministic on CI
/// (Default) and also correct on a split-token host. Windows-only; reported as failed elsewhere.
/// </summary>
[TestClass]
public sealed class RequesterTokenNativeTests
{
    [TestMethod]
    public void TheDerivedTokenIsAMediumIdentificationTokenWithOnlyAllowListedPrivileges()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        Assert.IsTrue(NativeTokenReader.IsIdentificationImpersonation(token.Handle));
        Assert.IsTrue(token.OwnerSids.Contains(token.Sid));
        Assert.IsFalse(token.OwnerSids.Contains(NativeTokenReader.AdministratorsSid), "a deny-only group never owns for the requester");
    }

    [TestMethod]
    public void TheHelperFactoryRefusesWithoutALinkedTokenOrWithAMismatchedSplitHalf()
    {
        N1NativeFixture.RequireWindows("linked-token refusal on the runner");
        using PeerProcess peer = PeerProcess.TryOpen(Environment.ProcessId) ?? throw new AssertFailedException("peer open failed");
        string sid = CurrentSid();

        RequesterToken? token = RequesterToken.TryForVerifiedPeer(peer, Environment.ProcessId, sid, out TokenVerdict verdict);

        Assert.IsNull(token);
        TokenVerdict expected = OwnElevation() switch
        {
            // CI: a standard user (or an administrator without split tokens) has nothing linked.
            TokenElevationKind.Default => TokenVerdict.NoLinkedToken,
            // Non-elevated split admin: the linked token is the elevated half.
            TokenElevationKind.Limited => TokenVerdict.EvaluationNotLimited,
            // Elevated test run: this process as peer is not the filtered desktop.
            _ => TokenVerdict.PeerNotLimited,
        };
        Assert.AreEqual(expected, verdict);
    }

    [TestMethod]
    public void APeerThatExitedBeforeTokenDuplicationIsRefused()
    {
        N1NativeFixture.RequireWindows("exited peer");
        using Process child = StartChild(["/d", "/c", "exit", "0"]);
        // The Process object holds a handle, so the exited process object can still be opened by ID.
        using PeerProcess peer = PeerProcess.TryOpen(child.Id) ?? throw new AssertFailedException("peer open failed");
        child.WaitForExit();

        Assert.IsTrue(peer.HasExited);
        Assert.IsNull(RequesterToken.TryForVerifiedPeer(peer, child.Id, CurrentSid(), out TokenVerdict verdict));
        Assert.AreEqual(TokenVerdict.PeerExited, verdict);
    }

    [TestMethod]
    public void APeerHandleWhosePidDiffersFromThePipePidIsRefusedFirst()
    {
        N1NativeFixture.RequireWindows("peer PID mismatch");
        using Process child = StartChild(["/d", "/c", "ping", "-n", "3", "127.0.0.1", ">nul"]);
        try
        {
            using PeerProcess peer = PeerProcess.TryOpen(Environment.ProcessId) ?? throw new AssertFailedException("peer open failed");
            Assert.AreEqual(Environment.ProcessId, peer.ProcessId);
            Assert.IsFalse(peer.HasExited);

            Assert.IsNull(RequesterToken.TryForVerifiedPeer(peer, child.Id, CurrentSid(), out TokenVerdict verdict));
            Assert.AreEqual(TokenVerdict.PeerProcessIdMismatch, verdict);
            Assert.IsNull(RequesterToken.TryForVerifiedPeer(peer, 0, CurrentSid(), out verdict));
            Assert.AreEqual(TokenVerdict.PeerProcessIdMismatch, verdict);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            child.WaitForExit();
        }
    }

    [TestMethod]
    public void TheDesktopFactoryAppliesTheSameRulesToItsOwnIdentificationToken()
    {
        N1NativeFixture.RequireWindows("desktop token rules");
        using RequesterToken? token = RequesterToken.TryFromCurrentProcess(out TokenVerdict verdict);
        if (OwnElevation() == TokenElevationKind.Limited)
        {
            // A non-elevated split-token host: acceptance depends on that host's privileges and integrity level.
            Assert.AreEqual(verdict == TokenVerdict.Accepted, token is not null);
            if (token is not null) Assert.IsTrue(NativeTokenReader.IsIdentificationImpersonation(token.Handle));
            return;
        }
        // CI (Default) and elevated (Full) runs are not filtered desktops.
        Assert.IsNull(token);
        Assert.AreEqual(TokenVerdict.PeerNotLimited, verdict);
    }

    [TestMethod]
    public void PeerIdentityComesFromTheHeldHandle()
    {
        N1NativeFixture.RequireWindows("peer identity");
        Assert.IsNull(PeerProcess.TryOpen(0));
        Assert.IsNull(PeerProcess.TryOpen(-4));
        using PeerProcess peer = PeerProcess.TryOpen(Environment.ProcessId) ?? throw new AssertFailedException("peer open failed");

        Assert.IsTrue(peer.TryReadIdentity(out string image, out int session, out string sid));
        Assert.AreEqual(N1NativeFixture.FinalPathOf(Environment.ProcessPath!, directory: false),
            N1NativeFixture.FinalPathOf(image, directory: false), ignoreCase: true);
        Assert.AreEqual(Process.GetCurrentProcess().SessionId, session);
        Assert.AreEqual(CurrentSid(), sid);
    }

    private static Process StartChild(string[] arguments)
    {
        ProcessStartInfo start = new(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new AssertFailedException("cmd.exe did not start");
    }

    private static string CurrentSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    }

    private static TokenElevationKind OwnElevation()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        Assert.IsTrue(NativeTokenReader.TryReadInt32(identity.AccessToken, Win32.TokenElevationTypeClass, out int elevation));
        return (TokenElevationKind)elevation;
    }
}
