using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// ADR-034 §3 and §5: scripts/PCStewardReleaseSigning.ps1 under Windows PowerShell 5.1 and pwsh 7. Its key pin equals the
/// SHA-256 of .NET's SubjectPublicKeyInfo export for RSA 2048, RSA 3072 and ECDSA P-256 certificates (the value
/// ReleaseSigningPins holds), and malformed DER is refused. Its signature check accepts a file signed by a pinned,
/// untrusted self-signed key and refuses a tampered file, another key, an unsigned file and a certificate not valid for
/// code signing. The signing function refuses a certificate that does not carry the pin it is given. Windows-native: on
/// other platforms each row reports <see cref="PlatformNotSupportedException"/> (never silently skipped).
/// </summary>
[TestClass]
public sealed class ReleaseSigningScriptTests
{
    [TestMethod]
    [DataRow("pwsh")]
    [DataRow("windows-powershell")]
    public async Task ThePinAndSignatureCheckHoldTheirContracts(string shell)
    {
        string executable = ResolveShell(shell);
        string root = FindRepositoryRoot();
        DirectoryInfo work = Directory.CreateTempSubdirectory("pc-steward-release-signing-");
        try
        {
            ProcessStartInfo start = new(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                Path.Combine(root, "tests", "PCSteward.Security.FixtureTests", "Fixtures", "ReleaseSigningScripts.ps1"),
                "-RepositoryRoot", root, "-WorkRoot", work.FullName,
            })
                start.ArgumentList.Add(argument);
            start.Environment.Remove("PSModulePath");
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(180));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.AreEqual(0, process.ExitCode, $"{await output}\n{await error}");

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(work.FullName, "results.json")));
            JsonElement results = document.RootElement;
            Assert.AreEqual(shell == "pwsh" ? "Core" : "Desktop", results.GetProperty("edition").GetString());
            foreach (JsonProperty certificate in results.GetProperty("pins").EnumerateObject())
            {
                using X509Certificate2 parsed = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificate.Value.GetProperty("der").GetString()!));
                string expected = Convert.ToHexString(SHA256.HashData(parsed.PublicKey.ExportSubjectPublicKeyInfo()));
                Assert.AreEqual(expected, certificate.Value.GetProperty("pin").GetString(), certificate.Name);
            }
            Assert.IsFalse(results.GetProperty("badDer").GetProperty("accepted").GetBoolean());

            JsonElement checks = results.GetProperty("checks");
            string primaryPin = results.GetProperty("pins").GetProperty("rsa3072").GetProperty("pin").GetString()!;
            foreach (string accepted in new[] { "signed", "signedEitherPin" })
            {
                Assert.IsTrue(checks.GetProperty(accepted).GetProperty("accepted").GetBoolean(), $"{accepted}: {checks.GetProperty(accepted)}");
                Assert.AreEqual(primaryPin, checks.GetProperty(accepted).GetProperty("value").GetString(), accepted);
            }
            foreach (string refused in new[] { "tampered", "otherKey", "unsigned", "notCodeSigning", "wrongCertificatePin" })
                Assert.IsFalse(checks.GetProperty(refused).GetProperty("accepted").GetBoolean(), $"{refused}: {checks.GetProperty(refused)}");
            StringAssert.Contains(checks.GetProperty("wrongCertificatePin").GetProperty("value").GetString(), "does not carry the expected pin");
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    private static string ResolveShell(string shell)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows-native test (ADR-034 §5 release signing scripts): it runs on Windows CI and is reported as failed on other platforms.");
        if (shell == "windows-powershell")
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, "pwsh.exe");
            if (File.Exists(candidate)) return candidate;
        }
        throw new AssertFailedException("PowerShell 7 (pwsh) is required on Windows CI.");
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
