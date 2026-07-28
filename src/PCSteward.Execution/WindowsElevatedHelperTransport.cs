using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Execution;

internal sealed partial class WindowsElevatedHelperTransport : IElevatedHelperTransport
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(11);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(5);
    internal WindowsElevatedHelperTransport()
    {
    }

    public async Task<ElevatedHelperResult> ExecuteAsync(
        ImmutableUpdateJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!OperatingSystem.IsWindows())
        {
            return Failed("elevated-helper-requires-windows");
        }

        string? desktopPath = Environment.ProcessPath;
        string helperPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "PCSteward.ElevatedHelper.exe"));
        if (desktopPath is null ||
            !string.Equals(Path.GetFileName(desktopPath), "PCSteward.Desktop.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(helperPath))
        {
            return Failed("fixed-signed-helper-or-desktop-path-unavailable");
        }

        string nonce = ElevatedHelperProtocol.CreateNonce();
        string pipeName = $"pcsteward-elevated-{Guid.NewGuid():N}";
        string currentSid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
        int currentSession = Process.GetCurrentProcess().SessionId;
        if (currentSid.Length == 0)
        {
            return Failed("desktop-user-identity-unavailable");
        }

        using NamedPipeServerStream pipe = RestrictedNamedPipe.Create(pipeName, currentSid);
        using Process? helper = LaunchHelper(helperPath, pipeName, nonce, out ElevatedHelperResult? launchFailure);
        if (launchFailure is not null)
        {
            return launchFailure;
        }
        if (helper is null)
        {
            return Failed("elevated-helper-launch-returned-no-process");
        }

        using CancellationTokenSource connectionTimeout = new(ConnectionTimeout);
        using CancellationTokenSource connectionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionTimeout.Token);
        try
        {
            Task connection = pipe.WaitForConnectionAsync(connectionCancellation.Token);
            Task exit = helper.WaitForExitAsync(CancellationToken.None);
            Task first = await Task.WhenAny(connection, exit).ConfigureAwait(false);
            if (first == exit)
            {
                connectionCancellation.Cancel();
                return Failed("elevated-helper-exited-before-connection");
            }
            await connection.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminate(helper);
            return new ElevatedHelperResult(ElevatedHelperOutcome.Cancelled, null, "user-cancelled");
        }
        catch (OperationCanceledException)
        {
            TryTerminate(helper);
            return new ElevatedHelperResult(ElevatedHelperOutcome.TimedOut, null, "elevated-helper-connection-timed-out");
        }
        catch (IOException)
        {
            TryTerminate(helper);
            return Failed("elevated-helper-connection-failed");
        }

        if (!RestrictedNamedPipe.TryGetClientProcessId(pipe, out int clientProcessId) ||
            clientProcessId != helper.Id ||
            !WindowsPeerIdentityVerifier.VerifyPeer(
                clientProcessId,
                helperPath,
                desktopPath,
                currentSession,
                currentSid,
                out _))
        {
            TryTerminate(helper);
            return Failed("elevated-helper-identity-check-failed");
        }

        try
        {
            byte[] request = ElevatedHelperProtocol.CreateRequest(job, nonce, DateTimeOffset.UtcNow);
            await PipeMessageFraming.WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);

            using CancellationTokenSource executionTimeout = new(ExecutionTimeout);
            using CancellationTokenSource executionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, executionTimeout.Token);
            byte[] response = await PipeMessageFraming.ReadAsync(pipe, executionCancellation.Token).ConfigureAwait(false);
            if (!ElevatedHelperProtocol.TryReadResponse(response, job.Id, nonce, out ElevatedHelperResult result, out _))
            {
                TryTerminate(helper);
                return Failed("elevated-helper-response-rejected");
            }

            using CancellationTokenSource exitTimeout = new(ExitTimeout);
            try
            {
                await helper.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryTerminate(helper);
                return Failed("elevated-helper-did-not-exit");
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            pipe.Dispose();
            TryTerminate(helper);
            return new ElevatedHelperResult(ElevatedHelperOutcome.Cancelled, null, "user-cancelled");
        }
        catch (OperationCanceledException)
        {
            pipe.Dispose();
            TryTerminate(helper);
            return new ElevatedHelperResult(ElevatedHelperOutcome.TimedOut, null, "elevated-helper-timed-out");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            TryTerminate(helper);
            return Failed("elevated-helper-crashed-or-disconnected");
        }
    }

    private static Process? LaunchHelper(
        string helperPath,
        string pipeName,
        string nonce,
        out ElevatedHelperResult? failure)
    {
        ProcessStartInfo start = new()
        {
            FileName = helperPath,
            UseShellExecute = true,
            Verb = "runas"
        };
        foreach (string argument in new[]
        {
            "--protocol", ElevatedHelperProtocol.Version,
            "--desktop-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            "--pipe", pipeName,
            "--nonce", nonce
        })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            failure = null;
            return Process.Start(start);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == NativeMethods.ErrorCancelled)
        {
            failure = new ElevatedHelperResult(ElevatedHelperOutcome.UserDenied, null, "user-declined-elevation");
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            failure = Failed("elevated-helper-launch-failed");
            return null;
        }
    }

    private static ElevatedHelperResult Failed(string diagnostic) =>
        new(ElevatedHelperOutcome.Failed, null, diagnostic);

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Closing the authenticated pipe is the primary cancellation signal to the helper.
        }
    }

    [GeneratedRegex("^pcsteward-elevated-[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    internal static partial Regex PipeNamePattern();
}

public static class ElevatedHelperEntryPoint
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        if (!OperatingSystem.IsWindows() ||
            !HelperBootstrap.TryParse(arguments, out HelperBootstrap? bootstrap) ||
            bootstrap is null ||
            !IsElevated())
        {
            return 20;
        }

        string helperPath = Environment.ProcessPath ?? string.Empty;
        string expectedDesktopPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "PCSteward.Desktop.exe"));
        string currentSid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
        int currentSession = Process.GetCurrentProcess().SessionId;
        if (helperPath.Length == 0 || currentSid.Length == 0 || !File.Exists(expectedDesktopPath))
        {
            return 21;
        }

        using NamedPipeClientStream pipe = new(
            ".",
            bootstrap.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        using CancellationTokenSource connectionTimeout = new(TimeSpan.FromSeconds(30));
        try
        {
            await pipe.ConnectAsync(connectionTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return 22;
        }

        if (!RestrictedNamedPipe.TryGetServerProcessId(pipe, out int serverProcessId) ||
            serverProcessId != bootstrap.DesktopProcessId ||
            !WindowsPeerIdentityVerifier.VerifyPeer(
                serverProcessId,
                expectedDesktopPath,
                helperPath,
                currentSession,
                currentSid,
                out _))
        {
            return 23;
        }

        try
        {
            byte[] request = await PipeMessageFraming.ReadAsync(pipe, connectionTimeout.Token).ConfigureAwait(false);
            OneShotHelperRequestValidator validator = new(bootstrap.Nonce);
            if (!validator.TryAccept(request, out ImmutableUpdateJob? job, out _) || job is null)
            {
                return 24;
            }

            using CancellationTokenSource disconnectCancellation = new();
            using CancellationTokenSource providerCancellation = new();
            Task monitor = MonitorDesktopDisconnectAsync(pipe, providerCancellation, disconnectCancellation.Token);
            ElevatedHelperResult result;
            try
            {
                WingetProcessResult providerResult =
                    await new WingetMachineUpdateExecutor().ExecuteAsync(job, providerCancellation.Token).ConfigureAwait(false);
                string output = NonElevatedUpdateRunner.Sanitize(
                    string.Join(' ', providerResult.StandardOutput, providerResult.StandardError));
                ElevatedHelperOutcome outcome = providerResult switch
                {
                    { OutputLimitExceeded: true } => ElevatedHelperOutcome.OutputLimitExceeded,
                    { TimedOut: true } => ElevatedHelperOutcome.TimedOut,
                    { ExitCode: 0 } => ElevatedHelperOutcome.Completed,
                    _ => ElevatedHelperOutcome.ProviderFailed
                };
                result = new ElevatedHelperResult(outcome, providerResult.ExitCode, output);
            }
            catch (OperationCanceledException)
            {
                return 25;
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
            {
                result = new ElevatedHelperResult(
                    ElevatedHelperOutcome.Failed,
                    null,
                    NonElevatedUpdateRunner.Sanitize(exception.Message));
            }
            finally
            {
                disconnectCancellation.Cancel();
                await ObserveMonitorAsync(monitor).ConfigureAwait(false);
            }

            byte[] response = ElevatedHelperProtocol.CreateResponse(
                job.Id,
                bootstrap.Nonce,
                result.Outcome,
                result.ExitCode,
                result.SanitizedOutput);
            await PipeMessageFraming.WriteAsync(pipe, response, CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or InvalidOperationException)
        {
            return 26;
        }
    }

    private static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task MonitorDesktopDisconnectAsync(
        PipeStream pipe,
        CancellationTokenSource providerCancellation,
        CancellationToken cancellationToken)
    {
        byte[] unexpected = new byte[1];
        try
        {
            int read = await pipe.ReadAsync(unexpected, cancellationToken).ConfigureAwait(false);
            providerCancellation.Cancel();
            if (read != 0)
            {
                providerCancellation.Cancel();
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                providerCancellation.Cancel();
            }
        }
    }

    private static async Task ObserveMonitorAsync(Task monitor)
    {
        try
        {
            await monitor.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

internal sealed record HelperBootstrap(int DesktopProcessId, string PipeName, string Nonce)
{
    internal static bool TryParse(string[] arguments, out HelperBootstrap? bootstrap)
    {
        bootstrap = null;
        if (arguments is null || arguments.Length != 8)
        {
            return false;
        }

        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Length; index += 2)
        {
            if (arguments[index] is not ("--protocol" or "--desktop-pid" or "--pipe" or "--nonce") ||
                !fields.TryAdd(arguments[index], arguments[index + 1]))
            {
                return false;
            }
        }

        if (fields.Count != 4 ||
            fields["--protocol"] != ElevatedHelperProtocol.Version ||
            !int.TryParse(fields["--desktop-pid"], NumberStyles.None, CultureInfo.InvariantCulture, out int desktopPid) ||
            desktopPid <= 0 ||
            !WindowsElevatedHelperTransport.PipeNamePattern().IsMatch(fields["--pipe"]))
        {
            return false;
        }

        try
        {
            _ = Convert.FromHexString(fields["--nonce"]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (fields["--nonce"].Length != 64)
        {
            return false;
        }

        bootstrap = new HelperBootstrap(desktopPid, fields["--pipe"], fields["--nonce"]);
        return true;
    }
}

internal static class PipeMessageFraming
{
    internal static async Task WriteAsync(Stream stream, byte[] message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        if (message.Length is <= 0 or > ElevatedHelperProtocol.MaximumMessageBytes)
        {
            throw new InvalidDataException("The helper message length is invalid.");
        }

        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, message.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > ElevatedHelperProtocol.MaximumMessageBytes)
        {
            throw new InvalidDataException("The helper message length is invalid.");
        }

        byte[] message = new byte[length];
        await stream.ReadExactlyAsync(message, cancellationToken).ConfigureAwait(false);
        return message;
    }
}

internal static class RestrictedNamedPipe
{
    internal static NamedPipeServerStream Create(string pipeName, string currentUserSid)
    {
        if (!WindowsElevatedHelperTransport.PipeNamePattern().IsMatch(pipeName))
        {
            throw new ArgumentException("The pipe locator is invalid.", nameof(pipeName));
        }

        string sddl = BuildSecurityDescriptorSddl(currentUserSid);
        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
            sddl,
            1,
            out IntPtr securityDescriptor,
            out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            NativeMethods.SecurityAttributes attributes = new()
            {
                Length = Marshal.SizeOf<NativeMethods.SecurityAttributes>(),
                SecurityDescriptor = securityDescriptor,
                InheritHandle = false
            };
            SafePipeHandle handle = NativeMethods.CreateNamedPipe(
                $@"\\.\pipe\{pipeName}",
                NativeMethods.PipeAccessDuplex | NativeMethods.FileFlagOverlapped,
                NativeMethods.PipeTypeByte | NativeMethods.PipeReadModeByte | NativeMethods.PipeRejectRemoteClients,
                1,
                ElevatedHelperProtocol.MaximumMessageBytes,
                ElevatedHelperProtocol.MaximumMessageBytes,
                0,
                ref attributes);
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        finally
        {
            NativeMethods.LocalFree(securityDescriptor);
        }
    }

    internal static string BuildSecurityDescriptorSddl(string currentUserSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUserSid);
        if (!currentUserSid.StartsWith("S-1-", StringComparison.Ordinal))
        {
            throw new ArgumentException("The current-user SID is invalid.", nameof(currentUserSid));
        }

        return $"D:P(A;;GA;;;{currentUserSid})(A;;GA;;;SY)";
    }

    internal static bool TryGetClientProcessId(PipeStream pipe, out int processId)
    {
        bool succeeded = NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint value);
        processId = succeeded && value <= int.MaxValue ? (int)value : 0;
        return processId != 0;
    }

    internal static bool TryGetServerProcessId(PipeStream pipe, out int processId)
    {
        bool succeeded = NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint value);
        processId = succeeded && value <= int.MaxValue ? (int)value : 0;
        return processId != 0;
    }
}

internal static class WindowsPeerIdentityVerifier
{
    internal static bool VerifyPeer(
        int processId,
        string expectedPath,
        string ownPath,
        int expectedSession,
        string expectedUserSid,
        out string rejection)
    {
        rejection = "peer-identity-invalid";
        if (!NativeMethods.TryGetProcessIdentity(processId, out string? actualPath, out int session, out string? userSid) ||
            !string.Equals(Path.GetFullPath(actualPath), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase) ||
            session != expectedSession ||
            !string.Equals(userSid, expectedUserSid, StringComparison.Ordinal))
        {
            return false;
        }

        if (!AuthenticodeIdentity.IsTrustedAndSameSigner(actualPath, ownPath))
        {
            rejection = "peer-signature-invalid";
            return false;
        }

        rejection = string.Empty;
        return true;
    }
}

internal static class AuthenticodeIdentity
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    internal static bool IsTrustedAndSameSigner(string firstPath, string secondPath)
    {
        if (!VerifyTrust(firstPath) || !VerifyTrust(secondPath))
        {
            return false;
        }

        try
        {
#pragma warning disable SYSLIB0057
            using X509Certificate first = X509Certificate.CreateFromSignedFile(firstPath);
            using X509Certificate second = X509Certificate.CreateFromSignedFile(secondPath);
#pragma warning restore SYSLIB0057
            return first.GetRawCertData().AsSpan().SequenceEqual(second.GetRawCertData());
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool VerifyTrust(string path)
    {
        IntPtr pathPointer = Marshal.StringToCoTaskMemUni(path);
        NativeMethods.WinTrustFileInfo fileInfo = new()
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WinTrustFileInfo>(),
            FilePath = pathPointer
        };
        IntPtr fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            NativeMethods.WinTrustData data = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WinTrustData>(),
                UiChoice = NativeMethods.WtdUiNone,
                RevocationChecks = NativeMethods.WtdRevokeNone,
                UnionChoice = NativeMethods.WtdChoiceFile,
                FileInfo = fileInfoPointer,
                StateAction = NativeMethods.WtdStateActionIgnore,
                ProviderFlags = NativeMethods.WtdCacheOnlyUrlRetrieval
            };
            return NativeMethods.WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref data) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPointer);
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }
}

internal static class NativeMethods
{
    internal const int ErrorCancelled = 1223;
    internal const uint PipeAccessDuplex = 0x00000003;
    internal const uint FileFlagOverlapped = 0x40000000;
    internal const uint PipeTypeByte = 0x00000000;
    internal const uint PipeReadModeByte = 0x00000000;
    internal const uint PipeRejectRemoteClients = 0x00000008;
    internal const uint WtdUiNone = 2;
    internal const uint WtdRevokeNone = 0;
    internal const uint WtdChoiceFile = 1;
    internal const uint WtdStateActionIgnore = 0;
    internal const uint WtdCacheOnlyUrlRetrieval = 0x00001000;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WinTrustFileInfo
    {
        internal uint Size;
        internal IntPtr FilePath;
        internal IntPtr FileHandle;
        internal IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WinTrustData
    {
        internal uint Size;
        internal IntPtr PolicyCallbackData;
        internal IntPtr SipClientData;
        internal uint UiChoice;
        internal uint RevocationChecks;
        internal uint UnionChoice;
        internal IntPtr FileInfo;
        internal uint StateAction;
        internal IntPtr StateData;
        internal IntPtr UrlReference;
        internal uint ProviderFlags;
        internal uint UiContext;
        internal IntPtr SignatureSettings;
    }

    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out IntPtr securityDescriptor,
        out uint securityDescriptorSize);

    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafePipeHandle CreateNamedPipe(
        string name,
        uint openMode,
        uint pipeMode,
        uint maximumInstances,
        int outputBufferSize,
        int inputBufferSize,
        uint defaultTimeout,
        ref SecurityAttributes securityAttributes);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        uint flags,
        [Out] char[] executableName,
        ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(int processId, out uint sessionId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        SafeProcessHandle processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [DllImport("wintrust.dll", SetLastError = true)]
    internal static extern int WinVerifyTrust(
        IntPtr windowHandle,
        in Guid actionId,
        ref WinTrustData trustData);

    internal static bool TryGetProcessIdentity(
        int processId,
        out string path,
        out int session,
        out string userSid)
    {
        path = string.Empty;
        session = 0;
        userSid = string.Empty;
        using SafeProcessHandle process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
        {
            return false;
        }

        char[] pathBuffer = new char[32768];
        uint pathLength = (uint)pathBuffer.Length;
        if (!QueryFullProcessImageName(process, 0, pathBuffer, ref pathLength) ||
            !ProcessIdToSessionId(processId, out uint sessionId) ||
            sessionId > int.MaxValue ||
            !OpenProcessToken(process, TokenQuery, out SafeAccessTokenHandle token))
        {
            return false;
        }

        using (token)
        using (WindowsIdentity identity = new(token.DangerousGetHandle()))
        {
            path = new string(pathBuffer, 0, checked((int)pathLength));
            session = (int)sessionId;
            userSid = identity.User?.Value ?? string.Empty;
        }
        return path.Length != 0 && userSid.Length != 0;
    }
}
