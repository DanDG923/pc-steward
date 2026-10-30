using System.Security.AccessControl;
using System.Security.Principal;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PCSteward.Storage;

internal static class OperationGateSecurity
{
    private const int FullMutexAccess = 0x001F0001;

    internal static string NameFor(SecurityIdentifier user) => $"Global\\PCSteward.DataOperation.v2.{user.Value}";

    internal static RawSecurityDescriptor DescriptorFor(SecurityIdentifier user) =>
        new($"O:{user.Value}G:{user.Value}D:P(A;;0x001F0001;;;{user.Value})");

    internal static bool IsTrusted(RawSecurityDescriptor descriptor, SecurityIdentifier user) =>
        descriptor.Owner == user &&
        (descriptor.ControlFlags & (ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected)) ==
            (ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected) &&
        descriptor.DiscretionaryAcl is { Count: 1 } acl &&
        acl[0] is CommonAce { AceQualifier: AceQualifier.AccessAllowed, AceFlags: AceFlags.None, IsCallback: false } ace &&
        ace.AccessMask == FullMutexAccess && ace.SecurityIdentifier == user;
}

/// <summary>Own on the existing dedicated lease thread; never transfer ownership across an await.</summary>
public sealed class CurrentUserOperationGate : IDisposable
{
    private readonly Mutex mutex;
    private readonly SecurityIdentifier user;
    private readonly object lifecycle = new();
    private bool inUse;
    private bool disposed;

    private CurrentUserOperationGate(Mutex mutex, SecurityIdentifier user, string name)
    {
        this.mutex = mutex;
        this.user = user;
        Name = name;
    }

    internal string Name { get; }

    public static CurrentUserOperationGate Open() => OpenCore(null);

    internal static CurrentUserOperationGate OpenForTesting(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A unique fixture identity is required.", nameof(id));
        return OpenCore(id);
    }

    private static CurrentUserOperationGate OpenCore(Guid? fixture)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new UnauthorizedAccessException("Current user identity is unavailable.");
        string name = OperationGateSecurity.NameFor(user) + (fixture is { } id ? ".test." + id.ToString("N") : string.Empty);
        RawSecurityDescriptor descriptor = OperationGateSecurity.DescriptorFor(user);
        byte[] bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        IntPtr nativeDescriptor = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, nativeDescriptor, bytes.Length);
            GateNativeMethods.SecurityAttributes attributes = new()
            {
                Length = Marshal.SizeOf<GateNativeMethods.SecurityAttributes>(),
                Descriptor = nativeDescriptor
            };
            // READ_CONTROL, SYNCHRONIZE and MUTEX_MODIFY_STATE only; no handle inheritance.
            SafeWaitHandle handle = GateNativeMethods.CreateMutexEx(ref attributes, name, 0, 0x00120001);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "Cannot open the secured current-user operation gate.");
            }
            Mutex? mutex = null;
            try
            {
                mutex = new();
                mutex.SafeWaitHandle = handle;
                ValidateSecurity(mutex, user);
                return new(mutex, user, name);
            }
            catch
            {
                mutex?.Dispose();
                handle.Dispose();
                throw;
            }
        }
        finally { Marshal.FreeHGlobal(nativeDescriptor); }
    }

    /// <summary>Null means bounded contention. Cancellation throws; abandonment permits recovery only.</summary>
    public OperationGateOwnership? TryAcquire(TimeSpan maximumWait, CancellationToken cancellationToken)
    {
        if (maximumWait < TimeSpan.Zero || maximumWait > TimeSpan.FromMinutes(30))
            throw new ArgumentOutOfRangeException(nameof(maximumWait));
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (inUse) throw new InvalidOperationException("Gate ownership cannot be acquired recursively or concurrently on one handle.");
            inUse = true;
        }
        bool owned = false;
        bool transferred = false;
        bool recoveryRequired = false;
        try
        {
            ValidateSecurity(mutex, user);
            int signalled;
            try
            {
                signalled = WaitHandle.WaitAny([cancellationToken.WaitHandle, mutex], (int)Math.Ceiling(maximumWait.TotalMilliseconds));
            }
            catch (AbandonedMutexException exception) when (exception.MutexIndex == 1)
            {
                signalled = 1;
                recoveryRequired = true;
            }
            if (signalled == WaitHandle.WaitTimeout) return null;
            if (signalled == 0) throw new OperationCanceledException(cancellationToken);
            owned = true;
            cancellationToken.ThrowIfCancellationRequested();
            ValidateSecurity(mutex, user);
            OperationGateOwnership result = new(this, recoveryRequired);
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                try { if (owned) mutex.ReleaseMutex(); }
                finally { lock (lifecycle) inUse = false; }
            }
        }
    }

    private static void ValidateSecurity(Mutex mutex, SecurityIdentifier user)
    {
        uint error = GateNativeMethods.GetSecurityInfo(mutex.SafeWaitHandle, 6, 0x00000005,
            out _, out _, out _, out _, out IntPtr descriptor);
        try
        {
            if (error != 0) throw new Win32Exception((int)error, "Cannot inspect operation-gate object security.");
            uint length = GateNativeMethods.GetSecurityDescriptorLength(descriptor);
            if (length is 0 or > 4096) throw new UnauthorizedAccessException("Operation-gate security descriptor is invalid.");
            byte[] bytes = new byte[length];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            if (!OperationGateSecurity.IsTrusted(new RawSecurityDescriptor(bytes, 0), user))
                throw new UnauthorizedAccessException("Operation-gate owner or permissions are untrusted.");
        }
        finally { if (descriptor != IntPtr.Zero) GateNativeMethods.LocalFree(descriptor); }
    }

    internal void Release()
    {
        try { mutex.ReleaseMutex(); }
        finally { lock (lifecycle) inUse = false; }
    }

    public void Dispose()
    {
        lock (lifecycle)
        {
            if (inUse) throw new InvalidOperationException("Release gate ownership before closing its handle.");
            if (disposed) return;
            disposed = true;
            mutex.Dispose();
        }
    }
}

public sealed class OperationGateOwnership : IDisposable
{
    private readonly CurrentUserOperationGate gate;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private bool released;

    internal OperationGateOwnership(CurrentUserOperationGate gate, bool recoveryRequired)
    {
        this.gate = gate;
        RecoveryRequired = recoveryRequired;
    }

    public bool RecoveryRequired { get; }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("Operation ownership must be released by its acquiring thread.");
        if (released) return;
        gate.Release();
        released = true;
    }
}

internal static class GateNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr Descriptor;
        internal int InheritHandle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateMutexExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern SafeWaitHandle CreateMutexEx(ref SecurityAttributes attributes, string name, uint flags, uint access);

    [DllImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetSecurityInfo(SafeWaitHandle handle, int objectType, uint information,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);

    [DllImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetSecurityDescriptorLength(IntPtr descriptor);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr LocalFree(IntPtr memory);
}
