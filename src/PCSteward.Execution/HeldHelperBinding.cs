using Microsoft.Win32.SafeHandles;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; adapter gates §A.2, §A.4 step 3 (A-M1, A-M2) and §R A2-L5: the binding set held from before
/// the helper launch. Every binding-set leaf (including closure files the helper loads lazily, such as
/// <c>PCSteward.Provider.Chocolatey.dll</c>) stays open through a read-only handle with <see cref="FileShare.Read"/> only, so
/// none of them can be written, deleted, renamed or replaced while held; reads and image loads stay possible
/// (<b>Windows evidence required</b>, A-Q1). It also carries the hashes verified against the ledger and the helper
/// apphost's <see cref="WindowsFileIdentity"/> captured before launch.
/// <para>
/// Lifetime (A-M2): the protocol-2 transport disposes it only after helper exit has been observed and the transport is
/// sealed, or, on the no-exit/termination-failure path, after teardown completes. Disposing earlier would reopen a
/// replacement window for lazily loaded files. After disposal every check fails closed. It is never persisted or cached.
/// </para>
/// <para>
/// A2-L5 fallback path (only if A-Q1 evidence shows the hold blocks the launch): the transport calls
/// <see cref="ReleaseHandles"/> before starting the helper. That closes the handles but keeps <see cref="HelperIdentity"/>
/// and <see cref="VerifiedHashes"/>, so <see cref="PeerImageMatchesHelper"/> and <see cref="RevalidateAfterPeer"/> still
/// work against the pre-launch reference identity. Only <see cref="Dispose"/> ends verification.
/// </para>
/// </summary>
internal sealed class HeldHelperBinding : IDisposable
{
    private readonly IReadOnlyDictionary<string, SafeFileHandle> handles;
    private readonly IReadOnlyDictionary<string, string> paths;
    private int disposed;
    private int released;

    private HeldHelperBinding(
        IReadOnlyDictionary<string, SafeFileHandle> handles,
        IReadOnlyDictionary<string, string> paths,
        IReadOnlyDictionary<string, DeploymentObservedFile> verifiedHashes,
        string helperPath,
        WindowsFileIdentity helperIdentity)
    {
        this.handles = handles;
        this.paths = paths;
        VerifiedHashes = verifiedHashes;
        HelperPath = helperPath;
        HelperIdentity = helperIdentity;
    }

    /// <summary>Binding-set leaf → (length, SHA-256) read through the held handles and equal to the ledger entries.</summary>
    internal IReadOnlyDictionary<string, DeploymentObservedFile> VerifiedHashes { get; }

    /// <summary>The fixed helper apphost path the identity was read from.</summary>
    internal string HelperPath { get; }

    /// <summary>
    /// The reference identity: the helper apphost's <c>FileIdInfo</c> read from its held handle before launch. Both the
    /// primary peer check and the fallback compare against this value (A2-L5).
    /// </summary>
    internal WindowsFileIdentity HelperIdentity { get; }

    internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

    /// <summary>True while every binding-set leaf is still held (neither released nor disposed).</summary>
    internal bool IsHolding => Volatile.Read(ref released) == 0 && !IsDisposed;

    /// <summary>
    /// Fallback only (A2-L5): closes the held handles before launch while keeping the reference identity and verified
    /// hashes for <see cref="RevalidateAfterPeer"/>. Idempotent. The primary path never calls this.
    /// </summary>
    internal void ReleaseHandles()
    {
        if (Interlocked.Exchange(ref released, 1) != 0) return;
        foreach (SafeFileHandle handle in handles.Values) handle.Dispose();
    }

    /// <summary>
    /// Takes ownership of the open handles. Called only by <see cref="OwnerOnlyDeploymentCapabilityReader"/> after the full
    /// policy returned Supported. Returns null (and the caller keeps ownership) when the helper identity cannot be read.
    /// </summary>
    internal static HeldHelperBinding? TryCreate(
        IReadOnlyDictionary<string, SafeFileHandle> handles,
        IReadOnlyDictionary<string, string> paths,
        IReadOnlyDictionary<string, DeploymentObservedFile> verifiedHashes,
        string helperLeaf)
    {
        ArgumentNullException.ThrowIfNull(handles);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(verifiedHashes);
        if (handles.Count != DeploymentCapabilityPolicy.BindingSet.Count || paths.Count != handles.Count || verifiedHashes.Count != handles.Count ||
            !handles.TryGetValue(helperLeaf, out SafeFileHandle? helper) || !paths.TryGetValue(helperLeaf, out string? helperPath) ||
            !DeploymentFileAccess.TryReadIdentity(helper, out WindowsFileIdentity identity))
            return null;
        return new(handles, paths, verifiedHashes, helperPath, identity);
    }

    /// <summary>
    /// Primary peer check (A.4 step 3.4, A-M1), run after <c>VerifyPeer</c>: opens the peer's main-module image from its
    /// process image path with no write or delete share, reads its <c>FileIdInfo</c> and requires it to equal
    /// <see cref="HelperIdentity"/>. A mismatch or any read failure returns false, which the transport treats like a
    /// <c>VerifyPeer</c> failure (the request is not written).
    /// </summary>
    internal bool PeerImageMatchesHelper(string peerImagePath)
    {
        if (IsDisposed || string.IsNullOrEmpty(peerImagePath)) return false;
        return DeploymentFileAccess.TryReadIdentity(peerImagePath, out WindowsFileIdentity peer) && peer == HelperIdentity;
    }

    /// <summary>
    /// Fallback if Windows evidence shows the share-read hold does not survive the launch (A-Q1, §R A2-L5): after
    /// <c>VerifyPeer</c> and before writing the request, re-open every binding-set leaf by its fixed path, re-hash it
    /// (bounded) against <see cref="VerifiedHashes"/>, and compare the peer image identity with the reference identity
    /// <see cref="HelperIdentity"/> captured before launch. This detects replacement only: a file swapped and restored
    /// between the pre-launch hold and this check is not detected, so the protected ACL and <c>VerifyPeer</c> remain the
    /// controls for that window.
    /// </summary>
    internal bool RevalidateAfterPeer(string peerImagePath)
    {
        if (!PeerImageMatchesHelper(peerImagePath)) return false;
        long total = 0;
        foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
        {
            if (!paths.TryGetValue(leaf, out string? path) || !VerifiedHashes.TryGetValue(leaf, out DeploymentObservedFile expected)) return false;
            using SafeFileHandle? handle = DeploymentFileAccess.TryOpenShareRead(path);
            if (handle is null || !DeploymentFileAccess.TryHashBounded(handle, DeploymentCapabilityPolicy.MaximumBindingFileBytes, out DeploymentObservedFile actual))
                return false;
            total += actual.Length;
            if (total > DeploymentCapabilityPolicy.MaximumBindingSetBytes || actual != expected) return false;
        }
        return !IsDisposed;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        ReleaseHandles();
    }
}
