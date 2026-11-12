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
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Execution;

internal sealed record HelperLaunchPaths(string DesktopPath, string HelperPath);
internal sealed record HelperLaunchTrustResult(bool Trusted, string Rejection);
internal delegate Process? ElevatedHelperLauncher(
    string helperPath,
    string pipeName,
    string nonce,
    out ElevatedHelperResult? failure);

internal sealed partial class WindowsElevatedHelperTransport : IElevatedHelperTransport
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(11);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(5);
    private readonly Func<HelperLaunchPaths?> resolvePaths;
    private readonly Func<HelperLaunchPaths, HelperLaunchTrustResult> validatePreLaunchTrust;
    private readonly ElevatedHelperLauncher launchHelper;
    private readonly Action<HelperLaunchPaths>? afterInitialTrust;
    private readonly Func<ImmutableUpdateJob, string?> validateProviderPreLaunchTrust;

    internal WindowsElevatedHelperTransport() : this(
        InstalledHelperLaunchTrust.ResolveFixedPaths,
        InstalledHelperLaunchTrust.Validate,
        LaunchHelper,
        null,
        ValidateProviderPreLaunchTrust)
    {
    }

    internal WindowsElevatedHelperTransport(Action<HelperLaunchPaths> afterInitialTrust) : this(
        InstalledHelperLaunchTrust.ResolveFixedPaths,
        InstalledHelperLaunchTrust.Validate,
        LaunchHelper,
        afterInitialTrust,
        ValidateProviderPreLaunchTrust)
    {
    }

    internal WindowsElevatedHelperTransport(
        Func<HelperLaunchPaths?> resolvePaths,
        Func<HelperLaunchPaths, HelperLaunchTrustResult> validatePreLaunchTrust,
        ElevatedHelperLauncher launchHelper,
        Action<HelperLaunchPaths>? afterInitialTrust = null,
        Func<ImmutableUpdateJob, string?>? validateProviderPreLaunchTrust = null)
    {
        this.resolvePaths = resolvePaths ?? throw new ArgumentNullException(nameof(resolvePaths));
        this.validatePreLaunchTrust = validatePreLaunchTrust ?? throw new ArgumentNullException(nameof(validatePreLaunchTrust));
        this.launchHelper = launchHelper ?? throw new ArgumentNullException(nameof(launchHelper));
        this.afterInitialTrust = afterInitialTrust;
        this.validateProviderPreLaunchTrust = validateProviderPreLaunchTrust ?? ValidateProviderPreLaunchTrust;
    }

    public Task<ElevatedHelperResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
        ExecuteAsync(job, null, cancellationToken);

    public async Task<ElevatedHelperResult> ExecuteAsync(
        ImmutableUpdateJob job,
        UpdateItemProgress? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!OperatingSystem.IsWindows())
        {
            return Failed("elevated-helper-requires-windows");
        }
        string? providerTrustFailure = validateProviderPreLaunchTrust(job);
        if (providerTrustFailure is not null)
        {
            return Failed(providerTrustFailure);
        }

        HelperLaunchPaths? paths = resolvePaths();
        if (paths is null)
        {
            return Failed("fixed-signed-helper-or-desktop-path-unavailable");
        }
        HelperLaunchTrustResult initialTrust = validatePreLaunchTrust(paths);
        if (!initialTrust.Trusted)
        {
            return Failed(initialTrust.Rejection);
        }
        try
        {
            afterInitialTrust?.Invoke(paths);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Failed("elevated-helper-prelaunch-evidence-hook-failed");
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
        // Validate again immediately before Process.Start. The protected install-root ACL is what
        // makes the checked signed identity stable across this final path-based runas boundary.
        HelperLaunchTrustResult finalTrust = validatePreLaunchTrust(paths);
        if (!finalTrust.Trusted)
        {
            return Failed(finalTrust.Rejection);
        }
        if (cancellationToken.IsCancellationRequested) return new(ElevatedHelperOutcome.Cancelled, null, "operation-cancelled");
        progress?.ElevationRequested();
        Process? helper = launchHelper(paths.HelperPath, pipeName, nonce, out ElevatedHelperResult? launchFailure);
        if (helper is null)
        {
            return launchFailure ?? Failed("elevated-helper-launch-returned-no-process");
        }
        HelperProcessWork owned = new(helper);
        ElevatedHelperResult Retain(ElevatedHelperResult result) => result with { OwnedCompletion = owned.OwnedCompletion };
        try
        {
            if (launchFailure is not null)
            {
                TryTerminate(helper);
                return Retain(launchFailure);
            }
            using CancellationTokenSource connectionTimeout = new(ConnectionTimeout);
            using CancellationTokenSource connectionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionTimeout.Token);
            try
            {
                Task connection = pipe.WaitForConnectionAsync(connectionCancellation.Token);
                Task first = await Task.WhenAny(connection, owned.ExitObserved).ConfigureAwait(false);
                if (first == owned.ExitObserved)
                {
                    await connectionCancellation.CancelAsync().ConfigureAwait(false);
                    try { await connection.ConfigureAwait(false); }
                    catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
                    TryTerminate(helper);
                    return Retain(Failed(owned.ExitObserved.IsCompletedSuccessfully
                        ? "elevated-helper-exited-before-connection" : "elevated-helper-exit-observation-failed"));
                }
                await connection.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryTerminate(helper);
                return Retain(new(ElevatedHelperOutcome.Cancelled, null, "operation-cancelled"));
            }
            catch (OperationCanceledException)
            {
                TryTerminate(helper);
                return Retain(new(ElevatedHelperOutcome.TimedOut, null, "elevated-helper-connection-timed-out"));
            }
            catch (IOException)
            {
                TryTerminate(helper);
                return Retain(Failed("elevated-helper-connection-failed"));
            }

            if (!RestrictedNamedPipe.TryGetClientProcessId(pipe, out int clientProcessId) ||
                clientProcessId != helper.Id ||
                !WindowsPeerIdentityVerifier.VerifyPeer(clientProcessId, paths.HelperPath, paths.DesktopPath,
                    currentSession, currentSid, out _))
            {
                TryTerminate(helper);
                return Retain(Failed("elevated-helper-identity-check-failed"));
            }
            try
            {
                byte[] request = ElevatedHelperProtocol.CreateRequest(job, nonce, DateTimeOffset.UtcNow);
                await PipeMessageFraming.WriteAsync(pipe, request, connectionCancellation.Token).ConfigureAwait(false);
                progress?.HandedToHelper();
                using CancellationTokenSource executionTimeout = new(ExecutionTimeout);
                using CancellationTokenSource executionCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, executionTimeout.Token);
                byte[] response = await PipeMessageFraming.ReadAsync(pipe, executionCancellation.Token).ConfigureAwait(false);
                if (!ElevatedHelperProtocol.TryReadResponse(response, job.Id, nonce, out ElevatedHelperResult result, out _))
                {
                    TryTerminate(helper);
                    return Retain(Failed("elevated-helper-response-rejected"));
                }
                using CancellationTokenSource exitTimeout = new(ExitTimeout);
                try { await owned.ExitObserved.WaitAsync(exitTimeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    TryTerminate(helper);
                    return Retain(Failed("elevated-helper-did-not-exit"));
                }
                return Retain(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                pipe.Dispose();
                TryTerminate(helper);
                return Retain(new(ElevatedHelperOutcome.Cancelled, null, "operation-cancelled"));
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                TryTerminate(helper);
                return Retain(new(ElevatedHelperOutcome.TimedOut, null, "elevated-helper-timed-out"));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
            {
                TryTerminate(helper);
                return Retain(Failed("elevated-helper-crashed-or-disconnected"));
            }
        }
        finally { owned.SealTransport(); }
    }

    private static string? ValidateProviderPreLaunchTrust(ImmutableUpdateJob job)
    {
        if (job.Selection.Provider != UpdateProvider.Chocolatey)
        {
            return null;
        }

        try
        {
            ChocolateyMachineUpdateExecutor.ValidateDesktopCallerClientTrust();
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or Win32Exception)
        {
            return "chocolatey-client-desktop-trust-rejected";
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

        // Security review H1: no UAC prompt while a user-writable environment source holds a .NET runtime control name.
        if (!ElevatedRuntimeEnvironmentGuard.PermitsElevatedLaunch())
        {
            failure = Failed("elevated-helper-runtime-environment-refused");
            return null;
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

internal sealed record HelperLaunchTrustEvidence(
    bool FixedInstalledLayout,
    bool DesktopPresent,
    bool HelperPresent,
    bool NoReparseComponents,
    bool CallerCannotModifyLayout,
    bool DesktopSignatureTrusted,
    bool HelperSignatureTrusted,
    bool SameSigner);

internal static class InstalledHelperLaunchTrust
{
    internal const string InstallDirectoryName = "PC Steward";
    internal const string DesktopExecutableName = "PCSteward.Desktop.exe";
    internal const string HelperExecutableName = "PCSteward.ElevatedHelper.exe";

    internal static HelperLaunchPaths? ResolveFixedPaths()
    {
        string? actualDesktop = Environment.ProcessPath;
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(actualDesktop) || string.IsNullOrWhiteSpace(programFiles))
        {
            return null;
        }

        string installedRoot = Path.GetFullPath(Path.Combine(programFiles, InstallDirectoryName));
        return new(
            Path.GetFullPath(actualDesktop),
            Path.Combine(installedRoot, HelperExecutableName));
    }

    internal static HelperLaunchTrustResult Validate(HelperLaunchPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            string programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            string installedRoot = Path.GetFullPath(Path.Combine(programFiles, InstallDirectoryName));
            string expectedDesktop = Path.Combine(installedRoot, DesktopExecutableName);
            string expectedHelper = Path.Combine(installedRoot, HelperExecutableName);
            bool fixedLayout =
                string.Equals(Path.GetFullPath(paths.DesktopPath), expectedDesktop, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFullPath(paths.HelperPath), expectedHelper, StringComparison.OrdinalIgnoreCase);
            bool desktopPresent = File.Exists(expectedDesktop);
            bool helperPresent = File.Exists(expectedHelper);
            bool noReparse = desktopPresent && helperPresent &&
                ProtectedLaunchLayout.HasNoReparseComponents(installedRoot, expectedDesktop, expectedHelper);
            bool callerCannotModify = noReparse &&
                ProtectedLaunchLayout.CallerCannotModify(installedRoot, expectedDesktop, expectedHelper);
            // ADR-034 §3: both images carry the same compiled release pin; otherwise the ADR-022 rule (trusted by Windows,
            // same raw signer certificate) applies until the owner-only test route is retired.
            bool samePin = desktopPresent && helperPresent &&
                AuthenticodeIdentity.HaveSamePinnedSigner(expectedDesktop, expectedHelper, ReleaseSigningPins.Compiled);
            bool desktopTrusted = desktopPresent && (samePin || AuthenticodeIdentity.IsTrusted(expectedDesktop));
            bool helperTrusted = helperPresent && (samePin || AuthenticodeIdentity.IsTrusted(expectedHelper));
            bool sameSigner = samePin || (desktopTrusted && helperTrusted &&
                AuthenticodeIdentity.HaveSameSigner(expectedDesktop, expectedHelper));
            return Evaluate(new(
                fixedLayout,
                desktopPresent,
                helperPresent,
                noReparse,
                callerCannotModify,
                desktopTrusted,
                helperTrusted,
                sameSigner));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or CryptographicException)
        {
            return new(false, "elevated-helper-prelaunch-trust-unavailable");
        }
    }

    internal static HelperLaunchTrustResult Evaluate(HelperLaunchTrustEvidence evidence)
    {
        if (!evidence.FixedInstalledLayout) return new(false, "elevated-helper-fixed-installed-path-invalid");
        if (!evidence.DesktopPresent || !evidence.HelperPresent) return new(false, "elevated-helper-installed-binary-missing");
        if (!evidence.NoReparseComponents) return new(false, "elevated-helper-reparse-layout-rejected");
        if (!evidence.CallerCannotModifyLayout) return new(false, "elevated-helper-caller-writable-layout-rejected");
        if (!evidence.DesktopSignatureTrusted || !evidence.HelperSignatureTrusted) return new(false, "elevated-helper-signature-untrusted");
        if (!evidence.SameSigner) return new(false, "elevated-helper-signer-mismatch");
        return new(true, string.Empty);
    }
}

internal static class ProtectedLaunchLayout
{
    private static readonly uint[] FileModificationRights =
    [
        NativeMethods.FileWriteData,
        NativeMethods.FileAppendData,
        NativeMethods.FileWriteEa,
        NativeMethods.FileWriteAttributes,
        NativeMethods.Delete,
        NativeMethods.WriteDac,
        NativeMethods.WriteOwner
    ];
    private static readonly uint[] DirectoryModificationRights =
    [
        NativeMethods.FileAddFile,
        NativeMethods.FileAddSubdirectory,
        NativeMethods.FileDeleteChild,
        NativeMethods.Delete,
        NativeMethods.WriteDac,
        NativeMethods.WriteOwner
    ];
    private static readonly uint[] AncestorReplacementRights =
    [
        NativeMethods.FileDeleteChild,
        NativeMethods.Delete,
        NativeMethods.WriteDac,
        NativeMethods.WriteOwner
    ];

    internal static bool HasNoReparseComponents(string installedRoot, params string[] files)
    {
        foreach (string directory in EnumerateAncestors(installedRoot))
        {
            if (!Directory.Exists(directory) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }

        return files.All(file =>
            File.Exists(file) &&
            (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0);
    }

    internal static bool CallerCannotModify(string installedRoot, params string[] files)
    {
        foreach (string directory in EnumerateAncestors(installedRoot))
        {
            // Creating an unrelated sibling at a volume/Program Files ancestor does not permit
            // replacement of the already-existing protected child. At ancestors, deny the rights
            // that can delete/rename the path component or change its ACL/owner. At the installed
            // root itself, additionally deny creation and mutation anywhere inside the layout.
            IEnumerable<uint> rights = string.Equals(
                Path.GetFullPath(directory),
                Path.GetFullPath(installedRoot),
                StringComparison.OrdinalIgnoreCase)
                ? DirectoryModificationRights
                : AncestorReplacementRights;
            if (!TryProbeRights(directory, directory: true, rights, out bool canModify) || canModify)
            {
                return false;
            }
        }

        foreach (string file in files)
        {
            if (!TryProbeRights(file, directory: false, FileModificationRights, out bool canModify) || canModify)
            {
                return false;
            }
        }
        return true;
    }

    private static Stack<string> EnumerateAncestors(string installedRoot)
    {
        Stack<string> ancestors = new();
        DirectoryInfo? current = new(Path.GetFullPath(installedRoot));
        while (current is not null)
        {
            ancestors.Push(current.FullName);
            current = current.Parent;
        }
        return ancestors;
    }

    private static bool TryProbeRights(string path, bool directory, IEnumerable<uint> rights, out bool canModify)
    {
        canModify = false;
        foreach (uint right in rights)
        {
            using SafeFileHandle handle = NativeMethods.CreateFileForTrustProbe(
                path,
                right,
                NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete,
                IntPtr.Zero,
                NativeMethods.OpenExisting,
                NativeMethods.FileFlagOpenReparsePoint | (directory ? NativeMethods.FileFlagBackupSemantics : 0),
                IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                canModify = true;
                return true;
            }
            if (Marshal.GetLastWin32Error() != NativeMethods.ErrorAccessDenied)
            {
                return false;
            }
        }
        return true;
    }
}

public static partial class ElevatedHelperEntryPoint
{
    /// <summary>
    /// ADR-032 revision 2 §6–§7: the finish line (<c>--finish-self-update &lt;leaf&gt;</c>) and the self-update session line
    /// (<c>--kind pcsteward-self-update</c>) are selected first, by their exact shapes, so neither reaches another mode.
    /// Task 50 design rev 6 §5.1 and qualification probe spec rev 4 §2 (dispatch order is normative): a ten-argument line with
    /// <c>--kind</c> first goes to the probe bootstrap (<see cref="RunProbeAsync"/>), which never reads a request; otherwise
    /// <c>--protocol 2</c> goes to the reviewed bootstrap (<see cref="RunReviewedAsync"/>); every other command line takes the
    /// unchanged protocol-1 path, which refuses anything but <c>--protocol 1</c> with exit code 20. Security review H1: before
    /// any of these, a helper whose own environment holds a .NET runtime control name ends with the refusal exit code
    /// (<see cref="ElevatedRuntimeEnvironmentGuard.RefusedExitCode"/>), before it connects or reads anything.
    /// </summary>
    public static Task<int> RunAsync(string[] arguments) =>
        !ElevatedRuntimeEnvironmentGuard.ProcessEnvironmentIsClean() ? Task.FromResult(ElevatedRuntimeEnvironmentGuard.RefusedExitCode) :
        SelectsSelfUpdateFinish(arguments) ? RunSelfUpdateFinishAsync(arguments) :
        SelectsSelfUpdate(arguments) ? RunSelfUpdateAsync(arguments) :
        SelectsProbe(arguments) ? RunProbeAsync(arguments) :
        SelectsProtocol2(arguments) ? RunReviewedAsync(arguments) : RunProtocol1Async(arguments);

    private static Task<int> RunProtocol1Async(string[] arguments) =>
        RunAsync(
            arguments,
            static (job, cancellationToken) => job.Selection.Provider switch
            {
                UpdateProvider.WinGet => ((IProtectedMachineUpdateExecutor)new WingetMachineUpdateExecutor()).ExecuteProtectedAsync(job, cancellationToken),
                UpdateProvider.Chocolatey => ((IProtectedMachineUpdateExecutor)new ChocolateyMachineUpdateExecutor()).ExecuteProtectedAsync(job, cancellationToken),
                _ => throw new InvalidOperationException("Unsupported update provider.")
            });

    internal static Task<int> RunNoOpEvidenceAsync(string[] arguments) =>
        RunAsync(
            arguments,
            static (_, _) => Task.FromResult(
                new ProviderProcessResult(
                    0,
                    "approved-no-op-installed-helper-evidence",
                    string.Empty,
                    false)));

    private static async Task<int> RunAsync(
        string[] arguments,
        Func<ImmutableUpdateJob, CancellationToken, Task<ProviderProcessResult>> executeProvider)
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
                ProviderProcessResult providerResult =
                    await executeProvider(job, providerCancellation.Token).ConfigureAwait(false);
                string output = NonElevatedUpdateRunner.Sanitize(
                    string.Join(' ', providerResult.StandardOutput, providerResult.StandardError));
                if (providerResult.OutputIncomplete || providerResult.ExecutionUncertain)
                    output = NonElevatedUpdateRunner.Sanitize($"provider-result-incomplete; verification-required; {output}");
                if (providerResult.Cancelled)
                    output = NonElevatedUpdateRunner.Sanitize($"operation-cancelled; {output}");
                ElevatedHelperOutcome outcome = providerResult switch
                {
                    { OutputLimitExceeded: true } => ElevatedHelperOutcome.OutputLimitExceeded,
                    { TimedOut: true } => ElevatedHelperOutcome.TimedOut,
                    { Cancelled: true } => ElevatedHelperOutcome.Cancelled,
                    { OutputIncomplete: true } => ElevatedHelperOutcome.Failed,
                    { ExecutionUncertain: true } => ElevatedHelperOutcome.Failed,
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
        // Protocol 1 and protocol 2 each use their own fixed locator; each helper bootstrap accepts only its own (§5.1).
        if (!IsAcceptedLocator(pipeName))
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

    internal static bool IsAcceptedLocator(string pipeName) =>
        WindowsElevatedHelperTransport.PipeNamePattern().IsMatch(pipeName) || WindowsReviewedHelperTransport.PipeNamePattern().IsMatch(pipeName);

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

internal static partial class WindowsPeerIdentityVerifier
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

/// <summary>
/// ADR-018 signer checks for PC Steward's own executables, amended by ADR-034 §3. A pair of images is accepted when both
/// carry the same compiled release pin (<see cref="ReleaseSigningPins"/>), or, while the ADR-022 owner-only test route still
/// exists (ADR-034 build order step 7 removes it), when Windows trusts both and they carry the same raw signer certificate.
/// Every <c>WinVerifyTrust</c> call goes through <see cref="VerifyTrust"/>: no UI, no revocation check, cache-only URL
/// retrieval, so no check here does network I/O.
/// </summary>
internal static class AuthenticodeIdentity
{
    /// <summary><c>CERT_E_UNTRUSTEDROOT</c> (winerror.h): the only failure ADR-034 §3 step 1 tolerates for a pinned key.</summary>
    internal const int CertUntrustedRoot = unchecked((int)0x800B0109);
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    internal static bool IsTrustedAndSameSigner(string firstPath, string secondPath)
    {
        if (HaveSamePinnedSigner(firstPath, secondPath, ReleaseSigningPins.Compiled))
        {
            return true;
        }

        if (!IsTrusted(firstPath) || !IsTrusted(secondPath))
        {
            return false;
        }

        return HaveSameSigner(firstPath, secondPath);
    }

    internal static bool IsTrusted(string path) => VerifyTrust(path, out _) == 0;

    /// <summary>ADR-034 §3 step 4: both images carry a pin from <paramref name="pins"/>, and it is the same pin.</summary>
    internal static bool HaveSamePinnedSigner(string firstPath, string secondPath, IReadOnlyList<string> pins) =>
        TryReadPinnedSigner(firstPath, pins, out string? first) &&
        TryReadPinnedSigner(secondPath, pins, out string? second) &&
        string.Equals(first, second, StringComparison.Ordinal);

    /// <summary>
    /// ADR-034 §3 steps 1–3. <c>WinVerifyTrust</c> must return success or exactly <see cref="CertUntrustedRoot"/> (any other
    /// result, such as a bad digest, no signature or a malformed signature, is a rejection); the signer is the leaf of the
    /// chain <c>WinVerifyTrust</c> built from the signature it verified, never a second parse of the file; that chain must be
    /// the single self-signed certificate; and the SHA-256 of its DER SubjectPublicKeyInfo must equal one of
    /// <paramref name="pins"/>. <paramref name="pin"/> is the matched pin. An empty pin list accepts nothing.
    /// </summary>
    internal static bool TryReadPinnedSigner(string path, IReadOnlyList<string> pins, out string? pin)
    {
        pin = null;
        if (pins is null || pins.Count == 0 || string.IsNullOrEmpty(path))
        {
            return false;
        }

        int status = VerifyTrust(path, out VerifiedSigner? signer);
        if (status is not (0 or CertUntrustedRoot) || signer is null || signer.ChainLength != 1)
        {
            return false;
        }

        byte[] actual = Convert.FromHexString(signer.SubjectPublicKeyInfoSha256);
        foreach (string candidate in pins)
        {
            if (ReleaseSigningPins.IsWellFormed(candidate) &&
                CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(candidate)))
            {
                pin = candidate;
                return true;
            }
        }

        return false;
    }

    internal static bool HaveSameSigner(string firstPath, string secondPath)
    {
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

    /// <summary>
    /// One <c>WinVerifyTrust</c> verification of the embedded signature, returning its status. When the provider built a
    /// signer chain, <paramref name="signer"/> describes it (chain length and the leaf's SubjectPublicKeyInfo hash), read from
    /// the same state data before the state is closed; otherwise it is null. Any read failure leaves it null.
    /// </summary>
    private static int VerifyTrust(string path, out VerifiedSigner? signer)
    {
        signer = null;
        IntPtr pathPointer = Marshal.StringToCoTaskMemUni(path);
        NativeMethods.WinTrustFileInfo fileInfo = new()
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WinTrustFileInfo>(),
            FilePath = pathPointer
        };
        IntPtr fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WinTrustFileInfo>());
        NativeMethods.WinTrustData data = default;
        bool stateOpen = false;
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            data = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WinTrustData>(),
                UiChoice = NativeMethods.WtdUiNone,
                RevocationChecks = NativeMethods.WtdRevokeNone,
                UnionChoice = NativeMethods.WtdChoiceFile,
                FileInfo = fileInfoPointer,
                StateAction = NativeMethods.WtdStateActionVerify,
                ProviderFlags = NativeMethods.WtdCacheOnlyUrlRetrieval
            };
            int status = NativeMethods.WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref data);
            stateOpen = data.StateData != IntPtr.Zero;
            if (stateOpen)
            {
                signer = ReadSigner(data.StateData);
            }

            return status;
        }
        finally
        {
            if (stateOpen)
            {
                data.StateAction = NativeMethods.WtdStateActionClose;
                _ = NativeMethods.WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref data);
            }

            Marshal.FreeHGlobal(fileInfoPointer);
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    /// <summary>
    /// The primary signer's chain from <c>WinVerifyTrust</c>'s provider state: <c>CRYPT_PROVIDER_SGNR.csCertChain</c> (offset
    /// 12: DWORD <c>cbStruct</c>, then the 8-byte <c>FILETIME</c>) and the leaf <c>CRYPT_PROVIDER_CERT.pCert</c> (DWORD
    /// <c>cbStruct</c>, then the pointer at pointer alignment). Null when any part is missing or unreadable.
    /// </summary>
    private static VerifiedSigner? ReadSigner(IntPtr stateData)
    {
        try
        {
            IntPtr provider = NativeMethods.WTHelperProvDataFromStateData(stateData);
            IntPtr primary = provider == IntPtr.Zero ? IntPtr.Zero : NativeMethods.WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (primary == IntPtr.Zero)
            {
                return null;
            }

            int chainLength = Marshal.ReadInt32(primary, 12);
            IntPtr leaf = NativeMethods.WTHelperGetProvCertFromChain(primary, 0);
            IntPtr context = leaf == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(leaf, IntPtr.Size);
            if (chainLength <= 0 || context == IntPtr.Zero)
            {
                return null;
            }

            using X509Certificate2 certificate = new(context);
            byte[] subjectPublicKeyInfo = certificate.PublicKey.ExportSubjectPublicKeyInfo();
            return new(chainLength, Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo)));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private sealed record VerifiedSigner(int ChainLength, string SubjectPublicKeyInfoSha256);
}

internal static class NativeMethods
{
    internal const int ErrorCancelled = 1223;
    internal const int ErrorAccessDenied = 5;
    internal const uint FileWriteData = 0x00000002;
    internal const uint FileAppendData = 0x00000004;
    internal const uint FileWriteEa = 0x00000010;
    internal const uint FileAddFile = FileWriteData;
    internal const uint FileAddSubdirectory = FileAppendData;
    internal const uint FileDeleteChild = 0x00000040;
    internal const uint FileWriteAttributes = 0x00000100;
    internal const uint Delete = 0x00010000;
    internal const uint WriteDac = 0x00040000;
    internal const uint WriteOwner = 0x00080000;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint FileShareDelete = 0x00000004;
    internal const uint OpenExisting = 3;
    internal const uint FileFlagBackupSemantics = 0x02000000;
    internal const uint FileFlagOpenReparsePoint = 0x00200000;
    internal const uint PipeAccessDuplex = 0x00000003;
    internal const uint FileFlagOverlapped = 0x40000000;
    internal const uint PipeTypeByte = 0x00000000;
    internal const uint PipeReadModeByte = 0x00000000;
    internal const uint PipeRejectRemoteClients = 0x00000008;
    internal const uint WtdUiNone = 2;
    internal const uint WtdRevokeNone = 0;
    internal const uint WtdChoiceFile = 1;
    internal const uint WtdStateActionVerify = 1;
    internal const uint WtdStateActionClose = 2;
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

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFileForTrustProbe(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

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

    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr providerData,
        uint signerIndex,
        [MarshalAs(UnmanagedType.Bool)] bool counterSigner,
        uint counterSignerIndex);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificateIndex);

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
