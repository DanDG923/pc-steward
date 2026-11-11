using System.Security.Cryptography;
using System.Text;

namespace PCSteward.Execution;

/// <summary>How a copied input compared with its approved hash.</summary>
internal enum SelfUpdateCopyVerdict { Copied, Unreadable, Changed }

/// <summary>
/// The administrative image after <c>msiexec /a</c>: the one folder that holds the release manifest (the install image) and
/// every file in it, relative to that folder, hashed through held handles. Null from the host means the layout was refused.
/// </summary>
internal sealed record SelfUpdateImage(string InstallFolder, IReadOnlyList<SelfUpdateTreeFile> Files);

/// <summary>
/// The machine side of the elevated self-update session (ADR-032 revision 2 §6). Every member is a bounded, fail-closed
/// Windows operation on paths the session builds from compiled parts and the request's hashes; none takes a path from the
/// desktop. The Windows implementation is <see cref="WindowsSelfUpdateHost"/>; fixture tests substitute a fake.
/// </summary>
internal interface ISelfUpdateSessionHost
{
    /// <summary>The fixed install root, <c>%ProgramFiles%\PC Steward</c> (known folder, never an environment value).</summary>
    string InstallRoot { get; }

    /// <summary>The fixed folder that holds the work folders: <c>%ProgramFiles%</c> (known folder).</summary>
    string ProgramFiles { get; }

    /// <summary>The user's profile folder from the administrator-only ProfileList key for <paramref name="userSid"/>, or null.</summary>
    string? ProfileFolder(string userSid);

    /// <summary>Creates <paramref name="path"/> new, with the protected work folder descriptor at creation. False if it exists or fails.</summary>
    bool CreateProtectedFolder(string path);

    /// <summary>
    /// Creates <paramref name="path"/> new, with the administrators-only descriptor at creation (Mythos review S2). False if it
    /// exists or fails.
    /// </summary>
    bool CreatePrivateFolder(string path);

    /// <summary>Best effort: removes earlier protected work folders except <paramref name="keepLeaf"/> and the newest earlier one.</summary>
    void RemoveOlderWorkFolders(string keepLeaf);

    /// <summary>
    /// Opens <paramref name="source"/> once without following a reparse point, refuses a reparse point, a non-regular file, a
    /// second data stream or more than <paramref name="maximumBytes"/>, hashes it through that handle while copying it to the
    /// new file <paramref name="destination"/>, then hashes the copy again.
    /// </summary>
    SelfUpdateCopyVerdict CopyInput(string source, string destination, long maximumBytes, string expectedSha256);

    /// <summary>The whole file through a held handle whose final path is <paramref name="path"/>, at most <paramref name="maximumBytes"/>; null otherwise.</summary>
    byte[]? ReadFile(string path, int maximumBytes);

    /// <summary>The helper's own GitHub confirmation (two anonymous GETs).</summary>
    Task<HelperGitHubVerdict> ConfirmAsync(long runId, string sourceCommit, string checkZipSha256, CancellationToken cancellationToken);

    /// <summary>True when the file's Authenticode signer is one of the compiled release pins.</summary>
    bool CarriesCompiledPin(string path);

    /// <summary>The MSI's tables read read-only (<see cref="MsiTableDigest.Read"/>), or null.</summary>
    MsiDatabaseSnapshot? ReadMsiTables(string msiPath);

    /// <summary>
    /// Runs <c>%SystemRoot%\System32\msiexec.exe</c> with the arguments, no shell, working directory System32, and a compiled
    /// environment whose temporary folder is <paramref name="privateFolder"/>, the work folder's administrators-only folder
    /// (refused otherwise). Null when it did not finish in time.
    /// </summary>
    Task<int?> RunMsiexecAsync(IReadOnlyList<string> arguments, string privateFolder, TimeSpan limit, CancellationToken cancellationToken);

    /// <summary>
    /// True when the current user's registry holds any Windows Installer registration for <paramref name="productCode"/>, for
    /// any product the machine registration lists under PC Steward's UpgradeCode, or for that UpgradeCode, or when it cannot
    /// be read (security review R1, Mythos review S1).
    /// </summary>
    bool HasPerUserRegistration(string productCode);

    /// <summary>The extracted image's install folder and files, or null when its layout breaks the §6 step 8 rule.</summary>
    SelfUpdateImage? ReadImage(string imageRoot, string packageLeaf);

    /// <summary>Both files carry the same compiled release pin.</summary>
    bool BothCarrySameReleasePin(string first, string second);

    /// <summary>Writes the running helper's own image to the new file <paramref name="destination"/> when it hashes to <paramref name="expectedSha256"/>, then hashes the copy again.</summary>
    bool CopyOwnImage(string destination, string expectedSha256);

    /// <summary>Writes a new file (it must not exist) and reads it back.</summary>
    bool WriteNewFile(string path, byte[] bytes);

    /// <summary>Starts the finish copy with the fixed application path and command line, no window, no handle inheritance.</summary>
    bool StartFinish(string helperCopyPath, string workFolderLeaf);
}

/// <summary>What the session did with the pipe.</summary>
internal enum SelfUpdateSessionExit { ResponseWritten, NoResponse, ResponseWriteFailed }

/// <summary>The session's own result: the exit kind, the response sent (if any) and a fixed diagnostic token.</summary>
internal sealed record SelfUpdateSessionResult(SelfUpdateSessionExit Exit, SelfUpdateResponse? Response, string Diagnostic);

/// <summary>The peer facts the bootstrap verified, and the requester check from the held item (the capability check's protection principal).</summary>
internal sealed record SelfUpdatePeer(int DesktopProcessId, string UserSid, string ImagePath, DeploymentLayoutProtectionCheck RequesterCannotModify);

/// <summary>Session budgets: the frame read, the whole execution (from connection), GitHub, extraction and the response write.</summary>
internal sealed record SelfUpdateSessionBudgets(TimeSpan FrameRead, TimeSpan Execution, TimeSpan GitHub, TimeSpan Extract, TimeSpan ResponseWrite)
{
    internal static SelfUpdateSessionBudgets Production { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(11), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5));
}

/// <summary>
/// FR-UPD-04/15, TM-03/04/12/13/20/23/24/26; ADR-032 revision 2 §6: the elevated self-update session over an already connected
/// pipe whose peer the bootstrap verified. It reads one request; anything not exactly correlated with the launch nonce gets
/// no response. It then runs §6 steps 2–12 in order, stopping at the first refusal, and writes one response: ready (with the
/// work folder leaf) or refused (with one compiled code). The request carries no path: both inputs are found by their
/// approved hashes under the requesting user's fixed download folder, copied into a new administrator-only work folder and
/// trusted only after that copy has been checked: the compiled pin, the record the owner approved, GitHub's own answer for
/// the check file, the MSI structure rule and table digest, and the extracted image against the CI build file by file. The
/// finish copy is started only after all of that, and it installs only after PC Steward has closed. A desktop disconnect or
/// a spent budget stops the session before the finish copy starts.
/// </summary>
internal static class SelfUpdateSession
{
    internal const int MaximumCheckZipBytes = ReleaseCheckPolicy.MaximumZipBytes;
    internal const long MaximumMsiBytes = 512L * 1024 * 1024;
    internal const int MaximumExecutableBytes = (int)DeploymentCapabilityPolicy.MaximumBindingFileBytes;

    /// <summary>The result detail of a job the session closed because it stopped before the finish copy started (Mythos review S3).</summary>
    internal const string SessionStoppedDetail = "session-stopped";

    /// <summary>The download folder under the requesting user's profile; the desktop writes both inputs there by hash.</summary>
    internal static readonly IReadOnlyList<string> DownloadFolderParts = ["AppData", "Local", "PCSteward", "Downloads", "SelfUpdate"];

    internal static Task<SelfUpdateSessionResult> RunAsync(Stream pipe, string nonce, SelfUpdatePeer peer, ISelfUpdateInstallCheck installCheck,
        ISelfUpdateSessionHost host, TimeProvider clock, long connectedTimestamp, CancellationToken cancellationToken) =>
        RunAsync(pipe, nonce, peer, installCheck, host, clock, connectedTimestamp, SelfUpdateSessionBudgets.Production, cancellationToken);

    internal static async Task<SelfUpdateSessionResult> RunAsync(Stream pipe, string nonce, SelfUpdatePeer peer, ISelfUpdateInstallCheck installCheck,
        ISelfUpdateSessionHost host, TimeProvider clock, long connectedTimestamp, SelfUpdateSessionBudgets budgets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(installCheck);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(budgets);

        SelfUpdateRequest? request = await ReadRequestAsync(pipe, nonce, budgets.FrameRead, cancellationToken).ConfigureAwait(false);
        if (request is null) return new(SelfUpdateSessionExit.NoResponse, null, "request-not-correlated");

        TimeSpan remaining = budgets.Execution - clock.GetElapsedTime(connectedTimestamp) - budgets.ResponseWrite;
        if (remaining <= TimeSpan.Zero) return await RespondAsync(pipe, Refused(nonce, SelfUpdateSessionRefusal.TimedOut), budgets, clock).ConfigureAwait(false);

        using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        work.CancelAfter(remaining);
        using CancellationTokenSource stopMonitor = new();
        Task monitor = MonitorDisconnectAsync(pipe, work, stopMonitor.Token);
        SelfUpdateResponse response;
        try
        {
            response = await new Steps(request, peer, installCheck, host, budgets, work.Token).RunAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            response = Refused(nonce, SelfUpdateSessionRefusal.TimedOut);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            response = Refused(nonce, SelfUpdateSessionRefusal.InternalFault);
        }
        finally
        {
            await stopMonitor.CancelAsync().ConfigureAwait(false);
            try { await monitor.WaitAsync(budgets.ResponseWrite, clock, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
        return await RespondAsync(pipe, response, budgets, clock).ConfigureAwait(false);
    }

    private static async Task<SelfUpdateRequest?> ReadRequestAsync(Stream pipe, string nonce, TimeSpan budget, CancellationToken cancellationToken)
    {
        try
        {
            if (!SelfUpdateHelperProtocol.IsUpperHex64(nonce)) return null;
            using CancellationTokenSource read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            read.CancelAfter(budget);
            byte[] frame = await PipeMessageFraming.ReadAsync(pipe, read.Token).ConfigureAwait(false);
            if (!SelfUpdateInstallPolicy.TryReadRequest(frame, out SelfUpdateRequest? request) || request is null ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(request.Nonce), Encoding.ASCII.GetBytes(nonce)))
                return null;
            return request;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static async Task<SelfUpdateSessionResult> RespondAsync(Stream pipe, SelfUpdateResponse response, SelfUpdateSessionBudgets budgets, TimeProvider clock)
    {
        string diagnostic = response.Ready ? "ready" : "refused; " + SelfUpdateHelperProtocol.Code(response.Reason!.Value);
        byte[] frame;
        try { frame = SelfUpdateHelperProtocol.WriteResponse(response); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new(SelfUpdateSessionExit.NoResponse, null, "response-refused-by-codec; " + diagnostic);
        }
        try
        {
            using CancellationTokenSource window = new(budgets.ResponseWrite, clock);
            await PipeMessageFraming.WriteAsync(pipe, frame, window.Token).WaitAsync(window.Token).ConfigureAwait(false);
            return new(SelfUpdateSessionExit.ResponseWritten, response, diagnostic);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new(SelfUpdateSessionExit.ResponseWriteFailed, response, diagnostic);
        }
    }

    /// <summary>Any read completion or failure means the desktop closed the pipe or broke the protocol: stop the work.</summary>
    private static async Task MonitorDisconnectAsync(Stream pipe, CancellationTokenSource work, CancellationToken stop)
    {
        byte[] unexpected = new byte[1];
        try
        {
            _ = await pipe.ReadAsync(unexpected, stop).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (stop.IsCancellationRequested) return;
        }
        if (stop.IsCancellationRequested) return;
        try { await work.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
    }

    private static SelfUpdateResponse Refused(string nonce, SelfUpdateSessionRefusal reason) => new(nonce, false, null, null, null, reason);

    /// <summary>Thrown inside the steps to stop at the first refusal.</summary>
    private sealed class RefusedException(SelfUpdateSessionRefusal reason) : Exception
    {
        internal SelfUpdateSessionRefusal Reason { get; } = reason;
    }

    private sealed class Steps(SelfUpdateRequest request, SelfUpdatePeer peer, ISelfUpdateInstallCheck installCheck, ISelfUpdateSessionHost host,
        SelfUpdateSessionBudgets budgets, CancellationToken token)
    {
        internal async Task<SelfUpdateResponse> RunAsync()
        {
            try { return await RunCoreAsync().ConfigureAwait(false); }
            catch (RefusedException refused) { return Refused(request.Nonce, refused.Reason); }
        }

        private async Task<SelfUpdateResponse> RunCoreAsync()
        {
            // Step 2: an intact MSI release install, the peer its protected desktop.
            Require(await Task.Run(() => installCheck.EvaluateInstalledRelease(peer.RequesterCannotModify, peer.ImagePath), CancellationToken.None)
                .WaitAsync(token).ConfigureAwait(false) is { IsSupported: true }, SelfUpdateSessionRefusal.InstallNotIntact);
            byte[] installedManifest = host.ReadFile(Path.Combine(host.InstallRoot, ReleaseManifestPolicy.FileName), ReleaseManifestPolicy.MaximumManifestBytes)
                ?? throw new RefusedException(SelfUpdateSessionRefusal.InstallNotIntact);
            Require(ReleaseManifestPolicy.TryRead(installedManifest, out ReleaseManifest? installed) && installed is not null, SelfUpdateSessionRefusal.InstallNotIntact);
            // Security review L1: only a newer release, whatever Windows Installer's own downgrade rule would do with an older one.
            Require(SelfUpdateInstallPolicy.IsNewerVersion(request.ToVersion, installed!.ProductVersion), SelfUpdateSessionRefusal.VersionNotNewer);

            // Step 4: the inputs live under the requesting user's own profile, found through the administrator-only ProfileList.
            string profile = host.ProfileFolder(peer.UserSid) ?? throw new RefusedException(SelfUpdateSessionRefusal.ProfileUnavailable);
            Require(Path.IsPathFullyQualified(profile), SelfUpdateSessionRefusal.ProfileUnavailable);
            string downloads = Path.Combine([profile, .. DownloadFolderParts]);

            // Step 5: a new administrator-only work folder. Users may read it (the desktop reads the result there); everything
            // the helper reads from the owner's folder, unpacks or logs goes in its administrators-only private folder (Mythos review S2).
            string leaf = SelfUpdateInstallPolicy.WorkFolderName(request.JobId);
            string workFolder = Path.Combine(host.ProgramFiles, leaf);
            string privateFolder = Path.Combine(workFolder, SelfUpdateHelperProtocol.PrivateFolder);
            Require(host.CreateProtectedFolder(workFolder) && host.CreatePrivateFolder(privateFolder), SelfUpdateSessionRefusal.WorkFolderUnavailable);
            token.ThrowIfCancellationRequested();

            string zipName = SelfUpdateInstallPolicy.CheckZipFileName(request.CheckZipSha256);
            string msiName = SelfUpdateInstallPolicy.MsiFileName(request.MsiSha256);
            string zipPath = Path.Combine(privateFolder, zipName);
            string msiPath = Path.Combine(privateFolder, msiName);
            Copy(Path.Combine(downloads, zipName), zipPath, MaximumCheckZipBytes, request.CheckZipSha256);
            token.ThrowIfCancellationRequested();
            Copy(Path.Combine(downloads, msiName), msiPath, MaximumMsiBytes, request.MsiSha256);
            token.ThrowIfCancellationRequested();

            // Step 6: the record the owner approved, confirmed with GitHub by the helper itself.
            byte[] zip = host.ReadFile(zipPath, MaximumCheckZipBytes) ?? throw new RefusedException(SelfUpdateSessionRefusal.InputUnreadable);
            Require(string.Equals(Convert.ToHexString(SHA256.HashData(zip)), request.CheckZipSha256, StringComparison.Ordinal), SelfUpdateSessionRefusal.InputChanged);
            ReleaseCheckRecord? record = null;
            Require(ReleaseCheckPolicy.TryReadZip(zip, out byte[]? recordBytes) && recordBytes is not null &&
                ReleaseCheckPolicy.TryRead(recordBytes, out record) && record is not null, SelfUpdateSessionRefusal.CheckFileRefused);
            Require(SelfUpdateInstallPolicy.RecordMatchesRequest(record!, request, ReleaseSigningPins.Compiled), SelfUpdateSessionRefusal.RecordNotApproved);
            HelperGitHubVerdict verdict;
            using (CancellationTokenSource github = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                github.CancelAfter(budgets.GitHub);
                try
                {
                    verdict = await host.ConfirmAsync(request.RunId, request.SourceCommit, request.CheckZipSha256, github.Token)
                        .WaitAsync(github.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    verdict = HelperGitHubVerdict.CouldNotReach;
                }
            }
            Require(verdict == HelperGitHubVerdict.Confirmed, verdict switch
            {
                HelperGitHubVerdict.RunNotGenuine => SelfUpdateSessionRefusal.RunNotGenuine,
                HelperGitHubVerdict.ArtifactGone => SelfUpdateSessionRefusal.ArtifactGone,
                HelperGitHubVerdict.ArtifactMismatch => SelfUpdateSessionRefusal.ArtifactMismatch,
                _ => SelfUpdateSessionRefusal.CouldNotConfirm,
            });
            // Mythos review S4: earlier jobs' folders go only once GitHub has confirmed this request, so made-up requests
            // cannot clear away the folder whose result the desktop has yet to read.
            host.RemoveOlderWorkFolders(leaf);
            token.ThrowIfCancellationRequested();

            // Step 7: the MSI copy itself, before Windows Installer touches it.
            Require(host.CarriesCompiledPin(msiPath), SelfUpdateSessionRefusal.MsiNotPinned);
            MsiDatabaseSnapshot tables = host.ReadMsiTables(msiPath) ?? throw new RefusedException(SelfUpdateSessionRefusal.MsiUnreadable);
            Require(MsiStructurePolicy.Check(tables.Tables, out _) == MsiStructureVerdict.Allowed, SelfUpdateSessionRefusal.MsiStructureRefused);
            Require(string.Equals(tables.Sha256, record!.MsiTablesSha256, StringComparison.OrdinalIgnoreCase), SelfUpdateSessionRefusal.MsiTablesDiffer);
            string productCode = tables.ProductCode ?? throw new RefusedException(SelfUpdateSessionRefusal.MsiStructureRefused);
            // Security review R1: Windows Installer would consult the owner's own registrations of this product, which a same-user
            // program can write, so msiexec never runs while one exists.
            Require(!host.HasPerUserRegistration(productCode), SelfUpdateSessionRefusal.PerUserRegistration);
            token.ThrowIfCancellationRequested();

            // Step 8: an administrative image (it unpacks files and runs no custom action of the package).
            string imageRoot = Path.Combine(privateFolder, SelfUpdateHelperProtocol.ImageFolder);
            int? extracted = await host.RunMsiexecAsync(["/a", msiPath, "/qn", "TARGETDIR=" + imageRoot, "/l*v",
                Path.Combine(privateFolder, SelfUpdateHelperProtocol.ExtractLogName)], privateFolder, budgets.Extract, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Require(extracted == 0, SelfUpdateSessionRefusal.ExtractFailed);
            Require(!host.HasPerUserRegistration(productCode), SelfUpdateSessionRefusal.PerUserRegistration);
            SelfUpdateImage image = host.ReadImage(imageRoot, msiName) ?? throw new RefusedException(SelfUpdateSessionRefusal.ImageLayoutRefused);

            // Step 9: exactly the CI build plus its release manifest, both executables on the same compiled pin.
            byte[]? manifestBytes = host.ReadFile(Path.Combine(image.InstallFolder, ReleaseManifestPolicy.FileName), ReleaseManifestPolicy.MaximumManifestBytes);
            SelfUpdateTreeVerdict content = SelfUpdateInstallPolicy.CheckExtracted(record, request.JobId, image.Files, manifestBytes,
                path => host.ReadFile(Path.Combine(image.InstallFolder, path), MaximumExecutableBytes) ?? throw new RefusedException(SelfUpdateSessionRefusal.ContentDiffers),
                out SelfUpdateExpectedInstall? expected);
            Require(content == SelfUpdateTreeVerdict.Matches && expected is not null, SelfUpdateSessionRefusal.ContentDiffers);
            Require(host.BothCarrySameReleasePin(Path.Combine(image.InstallFolder, InstalledHelperLaunchTrust.DesktopExecutableName),
                Path.Combine(image.InstallFolder, InstalledHelperLaunchTrust.HelperExecutableName)), SelfUpdateSessionRefusal.ExecutablesNotPinned);
            token.ThrowIfCancellationRequested();

            // Step 10: a copy of this (installed, manifest-listed) helper to run the install once PC Steward has closed.
            ReleaseManifestFile helperEntry = installed.Files.SingleOrDefault(file =>
                string.Equals(file.Path, InstalledHelperLaunchTrust.HelperExecutableName, StringComparison.Ordinal))
                ?? throw new RefusedException(SelfUpdateSessionRefusal.InstallNotIntact);
            string bin = Path.Combine(workFolder, SelfUpdateHelperProtocol.HelperCopyFolder);
            string helperCopy = Path.Combine(bin, InstalledHelperLaunchTrust.HelperExecutableName);
            Require(host.CreateProtectedFolder(bin) && host.CopyOwnImage(helperCopy, helperEntry.Sha256.ToUpperInvariant()), SelfUpdateSessionRefusal.HelperCopyFailed);

            // Steps 11–12. Mythos review S3: once a job file may exist, a session that stops before the finish copy has started
            // closes the job with a result, so the finish copy (which never runs a job that has one) cannot install it later.
            bool started = false;
            try
            {
                // Step 11: the job and the expected installed tree, for the finish copy and the desktop after a restart.
                Require(host.WriteNewFile(Path.Combine(workFolder, SelfUpdateHelperProtocol.JobFileName), SelfUpdateHelperProtocol.WriteJob(new(request.JobId,
                        request.ToVersion, request.RunId, request.SourceCommit, request.CheckZipSha256, request.MsiSha256, peer.DesktopProcessId, productCode))) &&
                    host.WriteNewFile(Path.Combine(workFolder, SelfUpdateInstallPolicy.ExpectedInstallFileName), SelfUpdateInstallPolicy.WriteExpected(expected!)),
                    SelfUpdateSessionRefusal.JobFilesFailed);

                // Step 12: the finish copy, only while the desktop is still connected and the budget holds.
                token.ThrowIfCancellationRequested();
                Require(host.StartFinish(helperCopy, leaf), SelfUpdateSessionRefusal.FinishNotStarted);
                started = true;
            }
            finally
            {
                if (!started) CloseJob(workFolder, expected!);
            }
            return new(request.Nonce, true, leaf, request.ToVersion, expected!.ManifestSha256, null);
        }

        /// <summary>Best effort (Mythos review S3): a result that refuses the job, written where the finish copy looks for one.</summary>
        private void CloseJob(string workFolder, SelfUpdateExpectedInstall expected)
        {
            try
            {
                _ = host.WriteNewFile(Path.Combine(workFolder, SelfUpdateHelperProtocol.ResultFileName), SelfUpdateHelperProtocol.WriteResult(new(request.JobId,
                    SelfUpdateFinishOutcome.RefusedBeforeInstall, null, request.ToVersion, expected.ManifestSha256, null, SessionStoppedDetail)));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }

        private void Copy(string source, string destination, long maximumBytes, string sha256)
        {
            SelfUpdateCopyVerdict copied = host.CopyInput(source, destination, maximumBytes, sha256);
            Require(copied != SelfUpdateCopyVerdict.Unreadable, SelfUpdateSessionRefusal.InputUnreadable);
            Require(copied == SelfUpdateCopyVerdict.Copied, SelfUpdateSessionRefusal.InputChanged);
        }

        private static void Require(bool condition, SelfUpdateSessionRefusal reason)
        {
            if (!condition) throw new RefusedException(reason);
        }
    }
}
