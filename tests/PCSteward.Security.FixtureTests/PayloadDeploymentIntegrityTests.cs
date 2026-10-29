using System.Diagnostics;

namespace PCSteward.Security.FixtureTests;

[TestClass]
public sealed class PayloadDeploymentIntegrityTests
{
    [TestMethod]
    public async Task CiLauncherRefusesAccountCreationOutsideHostedRunner()
    {
        // TM-04/TM-06: CI account/ACL setup must never run on a developer machine.
        string root = FindRoot();
        ProcessStartInfo start = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        start.Environment.Remove("GITHUB_ACTIONS");
        start.Environment.Remove("RUNNER_ENVIRONMENT");
        foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(root, "scripts", "Test-CI.ps1") }) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start CI launcher guard fixture.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.AreNotEqual(0, process.ExitCode, await output);
        StringAssert.Contains(await error, "this launcher requires an administrator on a GitHub-hosted runner");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PinnedPayloadRejectsDriftEscapesAndUnapprovedSigningChanges(bool missingHashFunction)
    {
        string root = FindRoot();
        string script = Path.Combine(root, "tests", "PCSteward.Security.FixtureTests", "Fixtures", "PayloadIntegrity.ps1");
        ProcessStartInfo start = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        using TemporaryHostState hostState = new();
        if (missingHashFunction)
        {
            foreach (string argument in new[] { "-NoProfile", "-File", hostState.Bootstrap, script, root }) start.ArgumentList.Add(argument);
        }
        else
        {
            foreach (string argument in new[] { "-NoProfile", "-File", script, "-RepositoryRoot", root }) start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated payload fixtures.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.AreEqual(0, process.ExitCode, $"Payload fixture failure:\n{await output}\n{await error}");
        StringAssert.Contains(await output, "PASS: payload integrity fixtures");
    }

    private sealed class TemporaryHostState : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("pc-steward-psmodules-");
        public string Path => directory.FullName;
        public string Bootstrap => System.IO.Path.Combine(Path, "bootstrap.ps1");

        public TemporaryHostState()
        {
            // Reproduce the CI boundary: Utility cmdlets exist but its hash function is missing.
            File.WriteAllText(Bootstrap,
                "$ErrorActionPreference = 'Stop'\n" +
                "foreach ($module in @('Utility','Management','Security')) { Import-Module (Join-Path $PSHOME ('Modules\\Microsoft.PowerShell.' + $module + '\\Microsoft.PowerShell.' + $module + '.psd1')) -Force }\n" +
                "Remove-Item Function:\\Get-FileHash\n" +
                "$global:PSModuleAutoLoadingPreference = 'None'\n" +
                "& $args[0] -RepositoryRoot $args[1]\nexit $LASTEXITCODE\n");
        }

        public void Dispose() => directory.Delete(recursive: true);
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root unavailable.");
    }
}
