using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PCSteward.Application;

namespace PCSteward.AdvisoryAi.Windows;

/// <summary>
/// FR-AI-03 (ADR-028 Q8, Task 67 stage 3): the one exact current-user generic credential <see cref="IAdvisoryAiCredentialStore.Target"/>
/// in Windows Credential Manager, local-machine persistence (this user on this computer, never roaming), no user name, the key as
/// its ASCII bytes. Only that target is ever written, read or deleted: there is no list, search, prefix or other target. A save is
/// checked by reading it back; a removal is reported only when a fresh read finds nothing. No network call is made.
/// </summary>
public sealed class WindowsCredentialManagerStore : IAdvisoryAiCredentialStore
{
    private readonly ICredentialManagerApi api;

    public WindowsCredentialManagerStore()
        : this(new NativeCredentialManagerApi())
    {
    }

    /// <summary>For tests: a fake Credential Manager that returns the documented Win32 errors.</summary>
    internal WindowsCredentialManagerStore(ICredentialManagerApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    public Task<AdvisoryAiCredentialWrite> SaveAsync(AdvisoryAiApiKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] blob = new byte[key.Length];
        try
        {
            // AdvisoryAiApiKey admits printable ASCII only, so each character is one byte and the blob fits Credential Manager's limit.
            ReadOnlySpan<char> characters = key.Reveal();
            for (int index = 0; index < characters.Length; index++) blob[index] = (byte)characters[index];
            if (api.Write(IAdvisoryAiCredentialStore.Target, blob) != CredentialError.None) return Task.FromResult(AdvisoryAiCredentialWrite.Failed);
            using AdvisoryAiApiKey? stored = Read(out _);
            bool same = stored is not null && stored.Reveal().SequenceEqual(characters);
            return Task.FromResult(same ? AdvisoryAiCredentialWrite.Saved : AdvisoryAiCredentialWrite.Failed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    public Task<AdvisoryAiCredentialPresence> CheckPresenceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using AdvisoryAiApiKey? key = Read(out int error);
        AdvisoryAiCredentialPresence presence = key is not null ? AdvisoryAiCredentialPresence.Present
            : error == CredentialError.NotFound ? AdvisoryAiCredentialPresence.Absent
            : AdvisoryAiCredentialPresence.Unknown;
        return Task.FromResult(presence);
    }

    public Task<AdvisoryAiApiKey?> ReadForDispatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(out _));
    }

    public Task<AdvisoryAiCredentialRemoval> RemoveAndVerifyAsync(CancellationToken cancellationToken)
    {
        int deleted = api.Delete(IAdvisoryAiCredentialStore.Target);
        if (deleted is not (CredentialError.None or CredentialError.NotFound)) return Task.FromResult(AdvisoryAiCredentialRemoval.Incomplete);
        byte[]? remaining = null;
        try
        {
            int check = api.Read(IAdvisoryAiCredentialStore.Target, out remaining);
            if (check != CredentialError.NotFound) return Task.FromResult(AdvisoryAiCredentialRemoval.Incomplete);
            return Task.FromResult(deleted == CredentialError.None ? AdvisoryAiCredentialRemoval.Removed : AdvisoryAiCredentialRemoval.AlreadyAbsent);
        }
        finally
        {
            if (remaining is not null) CryptographicOperations.ZeroMemory(remaining);
        }
    }

    /// <summary>
    /// The stored key, or null with the Win32 error. A blob that is empty, larger than <see cref="AdvisoryAiApiKey.MaximumUtf8Bytes"/>
    /// or not a valid key is treated as unreadable (error <see cref="CredentialError.InvalidData"/>), never repaired or shown.
    /// </summary>
    private AdvisoryAiApiKey? Read(out int error)
    {
        byte[]? blob = null;
        char[]? characters = null;
        try
        {
            error = api.Read(IAdvisoryAiCredentialStore.Target, out blob);
            if (error != CredentialError.None) return null;
            if (blob is null || blob.Length == 0 || blob.Length > AdvisoryAiApiKey.MaximumUtf8Bytes)
            {
                error = CredentialError.InvalidData;
                return null;
            }
            characters = new char[blob.Length];
            for (int index = 0; index < blob.Length; index++) characters[index] = (char)blob[index];
            if (AdvisoryAiApiKey.TryCreate(characters, out AdvisoryAiApiKey? key) != AdvisoryAiKeyProblem.None)
            {
                error = CredentialError.InvalidData;
                return null;
            }
            return key;
        }
        finally
        {
            if (blob is not null) CryptographicOperations.ZeroMemory(blob);
            if (characters is not null) Array.Clear(characters);
        }
    }
}

/// <summary>The Win32 errors the adapter distinguishes (ADR-028 Q8: CredDeleteW's documented failures).</summary>
internal static class CredentialError
{
    public const int None = 0;
    public const int AccessDenied = 5;
    public const int InvalidData = 13;
    public const int InvalidFlags = 1004;
    public const int NotFound = 1168;
    public const int NoSuchLogonSession = 1312;
}

/// <summary>
/// The three Credential Manager calls the adapter makes, each for one exact generic target. Every method returns
/// <see cref="CredentialError.None"/> or the Win32 error; <see cref="Read"/> returns a copy of the blob, which the caller clears.
/// </summary>
internal interface ICredentialManagerApi
{
    int Write(string target, byte[] blob);

    int Read(string target, out byte[]? blob);

    int Delete(string target);
}

/// <summary>advapi32 CredWriteW, CredReadW, CredDeleteW and CredFree; nothing else.</summary>
internal sealed class NativeCredentialManagerApi : ICredentialManagerApi
{
    private const int TypeGeneric = 1;
    private const int PersistLocalMachine = 2;

    public int Write(string target, byte[] blob)
    {
        IntPtr targetName = Marshal.StringToHGlobalUni(target);
        IntPtr data = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
        try
        {
            Marshal.Copy(blob, 0, data, blob.Length);
            NativeCredential credential = new()
            {
                Type = TypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = blob.Length,
                CredentialBlob = data,
                Persist = PersistLocalMachine
            };
            return CredWriteW(ref credential, 0) ? CredentialError.None : Marshal.GetLastPInvokeError();
        }
        finally
        {
            Marshal.Copy(new byte[blob.Length], 0, data, blob.Length);
            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(targetName);
        }
    }

    public int Read(string target, out byte[]? blob)
    {
        blob = null;
        if (!CredReadW(target, TypeGeneric, 0, out IntPtr pointer)) return Marshal.GetLastPInvokeError();
        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.Type != TypeGeneric || credential.CredentialBlobSize < 0 || credential.CredentialBlobSize > AdvisoryAiApiKey.MaximumUtf8Bytes)
                return CredentialError.InvalidData;
            blob = new byte[credential.CredentialBlobSize];
            if (blob.Length > 0) Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return CredentialError.None;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public int Delete(string target) => CredDeleteW(target, TypeGeneric, 0) ? CredentialError.None : Marshal.GetLastPInvokeError();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public int LastWrittenLow;
        public int LastWrittenHigh;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(IntPtr buffer);
}
