using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, TM-03/04/13; Task 50 design rev 6 §3 step 2 (L3): Authenticode trust of the <b>held</b> root engine handle.
/// <c>WinVerifyTrust(WINTRUST_ACTION_GENERIC_VERIFY_V2)</c> runs with <c>WTD_CHOICE_FILE</c> and <c>hFile</c> set to the held
/// handle (no UI, no revocation, cache-only URL retrieval, as <see cref="MachineTrustedChocolatey"/>); the signer is the
/// leaf certificate of the chain WinVerifyTrust itself built (its provider state data), never a second read of the file;
/// the file version is read while the read-share-only handle is held, so the file cannot be replaced in between. The
/// <c>bin</c> shim and <see cref="MachineTrustedChocolatey.ResolveExecutable"/> are never used. Any failure is untrusted.
/// </summary>
internal static class ChocolateyEngineTrust
{
    /// <summary>The same signer as <c>MachineTrustedChocolatey</c>'s expected simple name (an architecture test pins equality).</summary>
    internal const string ExpectedSignerSimpleName = "Chocolatey Software, Inc";

    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private const uint UiNone = 2;
    private const uint RevokeNone = 0;
    private const uint ChoiceFile = 1;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;
    private const uint CacheOnlyUrlRetrieval = 0x00001000;

    /// <summary>Embedded signature trusted, signed by the expected vendor, and file version exactly 2.7.5.0.</summary>
    internal static bool IsTrustedEngine(SafeFileHandle held, string canonicalPath) =>
        TryVerifyEmbeddedSignature(held, canonicalPath, out string? signer) &&
        string.Equals(signer, ExpectedSignerSimpleName, StringComparison.Ordinal) &&
        TryReadFileVersion(held, canonicalPath, out string? version) &&
        string.Equals(version, ChocolateyClientReleasePolicy.Version, StringComparison.Ordinal);

    /// <summary>
    /// True when WinVerifyTrust accepts the embedded signature of the held file, with the leaf signer's simple name from
    /// the chain it verified. The path only selects the subject interface package; the bytes come from the handle.
    /// </summary>
    internal static bool TryVerifyEmbeddedSignature(SafeFileHandle held, string canonicalPath, out string? signerSimpleName)
    {
        ArgumentNullException.ThrowIfNull(held);
        signerSimpleName = null;
        if (!OperatingSystem.IsWindows() || held.IsInvalid || held.IsClosed || !ProtectedNodeReader.IsCanonicalLocalPath(canonicalPath)) return false;
        bool added = false;
        held.DangerousAddRef(ref added);
        IntPtr path = Marshal.StringToCoTaskMemUni(canonicalPath);
        IntPtr fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        WinTrustData data = default;
        bool stateOpen = false;
        try
        {
            if (!added) return false;
            Marshal.StructureToPtr(new WinTrustFileInfo
            {
                Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = path, FileHandle = held.DangerousGetHandle()
            }, fileInfo, false);
            data = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(), UiChoice = UiNone, RevocationChecks = RevokeNone, UnionChoice = ChoiceFile,
                FileInfo = fileInfo, StateAction = StateActionVerify, ProviderFlags = CacheOnlyUrlRetrieval
            };
            int status = WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref data);
            stateOpen = data.StateData != IntPtr.Zero;
            if (status != 0 || !stateOpen) return false;
            IntPtr provider = WTHelperProvDataFromStateData(data.StateData);
            IntPtr signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            IntPtr leaf = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
            // CRYPT_PROVIDER_CERT: DWORD cbStruct, then PCCERT_CONTEXT pCert at pointer alignment.
            IntPtr context = leaf == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(leaf, IntPtr.Size);
            if (context == IntPtr.Zero) return false;
            using X509Certificate2 certificate = new(context);
            signerSimpleName = certificate.GetNameInfo(X509NameType.SimpleName, false);
            return !string.IsNullOrEmpty(signerSimpleName);
        }
        catch (CryptographicException)
        {
            signerSimpleName = null;
            return false;
        }
        finally
        {
            if (stateOpen)
            {
                data.StateAction = StateActionClose;
                _ = WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref data);
            }
            Marshal.FreeHGlobal(fileInfo);
            Marshal.FreeCoTaskMem(path);
            if (added) held.DangerousRelease();
        }
    }

    /// <summary>The fixed file version (major.minor.build.private) read while <paramref name="held"/> keeps the file in place.</summary>
    internal static bool TryReadFileVersion(SafeFileHandle held, string canonicalPath, out string? version)
    {
        ArgumentNullException.ThrowIfNull(held);
        version = null;
        if (!OperatingSystem.IsWindows() || held.IsInvalid || held.IsClosed || !ProtectedNodeReader.IsCanonicalLocalPath(canonicalPath)) return false;
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(canonicalPath);
            version = string.Join('.', new[] { info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart }
                .Select(part => part.ToString(CultureInfo.InvariantCulture)));
            return !held.IsClosed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(IntPtr windowHandle, in Guid actionId, ref WinTrustData trustData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr providerData, uint signerIndex,
        [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificateIndex);
}
