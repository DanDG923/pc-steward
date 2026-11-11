using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PCSteward.Domain;
using PCSteward.Execution;
using Status = PCSteward.Execution.HelperProtocolCapabilityStatus;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// ADR-034 §4 and §5: scripts/New-PCStewardReleaseManifest.ps1 writes the release manifest that the release reader accepts.
/// Under Windows PowerShell 5.1 and pwsh 7 it writes the same bytes (ordinal path order, uppercase SHA-256, UTF-8 without
/// BOM), skips an earlier manifest, and refuses a bad version or a path the manifest cannot list. Windows-native: on other
/// platforms each row reports <see cref="PlatformNotSupportedException"/> (never silently skipped).
/// </summary>
[TestClass]
public sealed class ReleaseManifestScriptTests
{
    private const string Version = "0.1.0-private-beta";

    [TestMethod]
    [DataRow("pwsh")]
    [DataRow("windows-powershell")]
    public async Task TheScriptWritesTheManifestTheReaderAccepts(string shell)
    {
        string executable = ResolveShell(shell);
        ReleaseManifestPolicyTests.Release release = ReleaseManifestPolicyTests.Release.Create();
        DirectoryInfo payload = Directory.CreateTempSubdirectory("pc-steward-release-manifest-");
        try
        {
            WritePayload(payload.FullName, release);
            (int exitCode, string output, string error) = await RunAsync(executable, payload.FullName, Version);
            Assert.AreEqual(0, exitCode, $"{output}\n{error}");
            byte[] manifest = File.ReadAllBytes(Path.Combine(payload.FullName, ReleaseManifestPolicy.FileName));
            CollectionAssert.AreEqual(Expected(release), manifest, Encoding.UTF8.GetString(manifest));

            using (JsonDocument summary = JsonDocument.Parse(output.Trim()))
            {
                Assert.AreEqual(Convert.ToHexString(SHA256.HashData(manifest)), summary.RootElement.GetProperty("manifestSha256").GetString());
                Assert.AreEqual(release.Contents.Count, summary.RootElement.GetProperty("fileCount").GetInt32());
                Assert.AreEqual(Version, summary.RootElement.GetProperty("productVersion").GetString());
            }
            Assert.IsTrue(ReleaseManifestPolicy.TryRead(manifest, out ReleaseManifest? parsed));
            Assert.AreEqual(Version, parsed!.ProductVersion);
            foreach (UpdateRouteContract route in DeploymentCapabilityPolicy.CapabilityOperationAllowlist)
                Assert.AreEqual(Status.Supported, ReleaseManifestPolicy.Evaluate(new(manifest, release.Capability, release.Observed, true), route).Status);

            // A second run skips the earlier manifest and writes the same bytes.
            (exitCode, output, error) = await RunAsync(executable, payload.FullName, Version);
            Assert.AreEqual(0, exitCode, $"{output}\n{error}");
            CollectionAssert.AreEqual(manifest, File.ReadAllBytes(Path.Combine(payload.FullName, ReleaseManifestPolicy.FileName)));
        }
        finally
        {
            payload.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow("pwsh")]
    [DataRow("windows-powershell")]
    public async Task TheScriptRefusesABadVersionOrAnUnlistablePath(string shell)
    {
        string executable = ResolveShell(shell);
        ReleaseManifestPolicyTests.Release release = ReleaseManifestPolicyTests.Release.Create();
        foreach ((string name, string version, string? extraFile) in new (string, string, string?)[]
        {
            ("two-part version", "1.0", null),
            ("leading zero", "01.0.0", null),
            ("non-ASCII path", Version, "Assets\\café.png"),
            ("path over 240 characters", Version, new string('a', ReleaseManifestPolicy.MaximumPathLength + 1)),
        })
        {
            DirectoryInfo payload = Directory.CreateTempSubdirectory("pc-steward-release-manifest-");
            try
            {
                WritePayload(payload.FullName, release);
                if (extraFile is not null) File.WriteAllText(Path.Combine(payload.FullName, extraFile), "x");
                (int exitCode, _, _) = await RunAsync(executable, payload.FullName, version);
                Assert.AreNotEqual(0, exitCode, name);
                Assert.IsFalse(File.Exists(Path.Combine(payload.FullName, ReleaseManifestPolicy.FileName)), name);
            }
            finally
            {
                payload.Delete(recursive: true);
            }
        }
    }

    /// <summary>The bytes the script must write: entries in ordinal path order, uppercase hashes, no whitespace.</summary>
    private static byte[] Expected(ReleaseManifestPolicyTests.Release release)
    {
        StringBuilder json = new();
        json.Append(CultureInfo.InvariantCulture, $"{{\"format\":\"{ReleaseManifestPolicy.Format}\",\"productVersion\":\"{Version}\",\"files\":[");
        bool first = true;
        foreach (string path in release.Contents.Keys.Order(StringComparer.Ordinal))
        {
            byte[] bytes = release.Contents[path];
            if (!first) json.Append(',');
            first = false;
            json.Append(CultureInfo.InvariantCulture,
                $"{{\"path\":\"{path.Replace("\\", "\\\\", StringComparison.Ordinal)}\",\"bytes\":{bytes.Length},\"sha256\":\"{Convert.ToHexString(SHA256.HashData(bytes))}\"}}");
        }
        json.Append("]}");
        return new UTF8Encoding(false).GetBytes(json.ToString());
    }

    private static void WritePayload(string root, ReleaseManifestPolicyTests.Release release)
    {
        foreach ((string path, byte[] bytes) in release.Contents)
        {
            string target = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
        // The script lists every file under the root; these three are the ones it requires by name.
        foreach (string required in new[] { InstalledHelperLaunchTrust.DesktopExecutableName, InstalledHelperLaunchTrust.HelperExecutableName, DeploymentCapabilityPolicy.CapabilityFileName })
            Assert.IsTrue(File.Exists(Path.Combine(root, required)), required);
    }

    private static string ResolveShell(string shell)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows-native test (ADR-034 §5 release manifest script): it runs on Windows CI and is reported as failed on other platforms.");
        if (shell == "windows-powershell")
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, "pwsh.exe");
            if (File.Exists(candidate)) return candidate;
        }
        throw new AssertFailedException("PowerShell 7 (pwsh) is required on Windows CI.");
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string executable, string payloadRoot, string version)
    {
        string script = Path.Combine(FindRepositoryRoot(), "scripts", "New-PCStewardReleaseManifest.ps1");
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-PayloadRoot", payloadRoot, "-ProductVersion", version })
            start.ArgumentList.Add(argument);
        // Windows PowerShell must load its own modules, not the ones of a pwsh that started this test.
        start.Environment.Remove("PSModulePath");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
