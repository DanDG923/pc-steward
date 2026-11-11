using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PCSteward.Execution;

namespace PCSteward.InstallCheck;

/// <summary>
/// ADR-032 revision 2 §7–§8 CI evidence on a real install (the owner chose CI for the install step; the first full run through
/// the screens is the owner's own update). Two modes:
/// <list type="bullet">
/// <item><c>--self-update-prepare &lt;msi&gt; &lt;case&gt;</c> (elevated) lays out a job exactly as the session's steps 5 and 8–11
/// leave it, through the production Windows host: a new protected work folder and its administrators-only private folder,
/// the MSI copied by hash into the private folder, its administrative image there, the expected tree read from that image, <c>job.json</c>, <c>expected-install.json</c> and a copy of the installed
/// helper in <c>bin</c>. Case <c>match</c> records the image as it is; case <c>mismatch</c> records one file's hash changed, so
/// the finish copy must uninstall. It prints the work folder leaf.</item>
/// <item><c>--self-update-decide &lt;leaf&gt; &lt;from&gt;</c> (standard user) is the reopened desktop's §8 read of that job through
/// the production desktop pieces, with this install's own version as the running version. It prints the state and code, and
/// whether this user is kept out of the job's private folder.</item>
/// </list>
/// The finish copy itself is the real helper copy, started by the CI script with <c>--finish-self-update &lt;leaf&gt;</c>.
/// </summary>
internal static class SelfUpdateFinishCheck
{
    internal static int Prepare(string msiPath, string mode)
    {
        if (mode is not ("match" or "mismatch")) return 2;
        WindowsSelfUpdateHost host = WindowsSelfUpdateHost.TryCreate() ?? throw new InvalidOperationException("No Windows host.");
        Guid jobId = Guid.NewGuid();
        string leaf = SelfUpdateInstallPolicy.WorkFolderName(jobId);
        string work = Path.Combine(host.ProgramFiles, leaf);
        string bin = Path.Combine(work, SelfUpdateHelperProtocol.HelperCopyFolder);
        string privateFolder = Path.Combine(work, SelfUpdateHelperProtocol.PrivateFolder);
        if (!host.CreateProtectedFolder(work) || !host.CreatePrivateFolder(privateFolder)) return Fail("create-work-folder");
        string msiSha = Sha256(msiPath);
        string msiName = SelfUpdateInstallPolicy.MsiFileName(msiSha);
        string msiCopy = Path.Combine(privateFolder, msiName);
        if (host.CopyInput(msiPath, msiCopy, SelfUpdateSession.MaximumMsiBytes, msiSha) != SelfUpdateCopyVerdict.Copied) return Fail("copy-msi");
        if (host.ReadMsiTables(msiCopy) is not { } tables || tables.ProductCode is not { } productCode) return Fail("product-code");
        if (host.HasPerUserRegistration(productCode)) return Fail("per-user-registration");
        string image = Path.Combine(privateFolder, SelfUpdateHelperProtocol.ImageFolder);
        int? extracted = host.RunMsiexecAsync(["/a", msiCopy, "/qn", "TARGETDIR=" + image, "/l*v", Path.Combine(privateFolder, SelfUpdateHelperProtocol.ExtractLogName)],
            privateFolder, TimeSpan.FromMinutes(10), CancellationToken.None).GetAwaiter().GetResult();
        if (extracted != 0)
        {
            // The CI log is the only place the run's files can be read from outside it, so the extract log's outcome lines go there.
            string log = Path.Combine(privateFolder, SelfUpdateHelperProtocol.ExtractLogName);
            if (File.Exists(log))
                foreach (string line in File.ReadLines(log).Where(line => !line.Contains("Note: 1:", StringComparison.Ordinal) &&
                             (line.Contains("Return value 3", StringComparison.Ordinal) || line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                              line.Contains("policy", StringComparison.OrdinalIgnoreCase) || line.Contains("Product: ", StringComparison.Ordinal) ||
                              line.Contains("MainEngineThread", StringComparison.Ordinal))).Take(40))
                    Console.Error.WriteLine("extract.log: " + (line.Length > 300 ? line[..300] : line));
            return Fail("extract " + extracted);
        }
        if (host.ReadImage(image, msiName) is not { } extractedImage) return Fail("image-layout");
        byte[] manifest = host.ReadFile(Path.Combine(extractedImage.InstallFolder, ReleaseManifestPolicy.FileName), ReleaseManifestPolicy.MaximumManifestBytes)
            ?? throw new InvalidOperationException("No release manifest in the image.");
        if (!ReleaseManifestPolicy.TryRead(manifest, out ReleaseManifest? parsed) || parsed is null) return Fail("manifest");
        string toVersion = parsed.ProductVersion.Split('-', 2)[0];
        List<SelfUpdateTreeFile> files = [.. extractedImage.Files];
        if (mode == "mismatch")
        {
            int index = files.FindIndex(file => !string.Equals(file.Path, ReleaseManifestPolicy.FileName, StringComparison.OrdinalIgnoreCase));
            files[index] = files[index] with { Sha256 = new string('0', 64) };
        }
        SelfUpdateExpectedInstall expected = new(jobId, toVersion, Convert.ToHexString(SHA256.HashData(manifest)), files);

        using Process gone = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })!;
        gone.WaitForExit();
        SelfUpdateJobFile job = new(jobId, toVersion, 4242, new string('a', 40), new string('B', 64), msiSha, gone.Id, productCode);
        if (!host.WriteNewFile(Path.Combine(work, SelfUpdateHelperProtocol.JobFileName), SelfUpdateHelperProtocol.WriteJob(job)) ||
            !host.WriteNewFile(Path.Combine(work, SelfUpdateInstallPolicy.ExpectedInstallFileName), SelfUpdateInstallPolicy.WriteExpected(expected)))
            return Fail("job-files");
        string installedHelper = Path.Combine(host.InstallRoot, InstalledHelperLaunchTrust.HelperExecutableName);
        if (!host.CreateProtectedFolder(bin) ||
            host.CopyInput(installedHelper, Path.Combine(bin, InstalledHelperLaunchTrust.HelperExecutableName), 64L * 1024 * 1024, Sha256(installedHelper)) != SelfUpdateCopyVerdict.Copied)
            return Fail("helper-copy");
        Console.WriteLine(leaf);
        return 0;
    }

    internal static int Decide(string leaf, string fromVersion)
    {
        if (!SelfUpdateInstallPolicy.IsWorkFolderName(leaf)) return 2;
        WindowsSelfUpdateHost host = WindowsSelfUpdateHost.TryCreate() ?? throw new InvalidOperationException("No Windows host.");
        string work = Path.Combine(host.ProgramFiles, leaf);
        if (host.ReadFile(Path.Combine(work, SelfUpdateHelperProtocol.JobFileName), SelfUpdateHelperProtocol.MaximumFileBytes) is not { } jobBytes ||
            !SelfUpdateHelperProtocol.TryReadJob(jobBytes, out SelfUpdateJobFile? job) || job is null ||
            host.ReadFile(Path.Combine(work, SelfUpdateInstallPolicy.ExpectedInstallFileName), SelfUpdateInstallPolicy.MaximumExpectedBytes) is not { } expectedBytes ||
            !SelfUpdateInstallPolicy.TryReadExpected(expectedBytes, out SelfUpdateExpectedInstall? expected) || expected is null)
            return Fail("read-job");
        // The reopened desktop's own version: the installed release manifest's, as PCSteward.Desktop.exe's assembly version would give.
        string running = string.Empty;
        if (host.ReadFile(Path.Combine(host.InstallRoot, ReleaseManifestPolicy.FileName), ReleaseManifestPolicy.MaximumManifestBytes) is { } installed &&
            ReleaseManifestPolicy.TryRead(installed, out ReleaseManifest? manifest) && manifest is not null)
            running = manifest.ProductVersion.Split('-', 2)[0];
        SelfUpdateDesktopJob desktopJob = new(job.JobId, fromVersion, job.ToVersion, job.RunId, job.SourceCommit, job.CheckZipSha256, job.MsiSha256,
            DateTimeOffset.UtcNow.AddMinutes(-5), leaf, expected.ManifestSha256);
        SelfUpdateDesktopFiles files = SelfUpdateDesktopFiles.TryCreate() ?? throw new InvalidOperationException("No desktop files.");
        // The reopened desktop's install check runs as PCSteward.Desktop.exe; this check runs from elsewhere, so it gives the
        // production Desktop-role reader the installed desktop's path, as Program.cs does for the capability check.
        using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        DeploymentCapabilityLayout layout = new(host.InstallRoot, Path.Combine(programFiles, OwnerOnlyDeploymentCapabilityReader.StateDirectoryName),
            Path.Combine(host.InstallRoot, InstalledHelperLaunchTrust.DesktopExecutableName), identity.User?.Value ?? string.Empty);
        OwnerOnlyDeploymentCapabilityReader reader = new(DeploymentCapabilityRole.Desktop, () => layout, OwnerOnlyDeploymentCapabilityReader.IsProtectedLayout);
        SelfUpdateJobVerdict verdict = files.Evaluate(desktopJob, running, DateTimeOffset.UtcNow, reader);
        // Mythos review S2: this standard user must not be able to list the private folder (MSI copy, image, logs).
        bool privateHidden;
        try
        {
            _ = Directory.GetFileSystemEntries(Path.Combine(work, SelfUpdateHelperProtocol.PrivateFolder));
            privateHidden = false;
        }
        catch (UnauthorizedAccessException)
        {
            privateHidden = true;
        }
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["state"] = verdict.State.ToString(),
            ["code"] = verdict.Code,
            ["running"] = running,
            ["privateHidden"] = privateHidden ? "true" : "false",
        }));
        return 0;
    }

    private static string Sha256(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static int Fail(string step)
    {
        Console.Error.WriteLine("self-update prepare failed: " + step.ToString(CultureInfo.InvariantCulture));
        return 1;
    }
}
