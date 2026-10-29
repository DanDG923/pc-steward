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
    public async Task PinnedPayloadRejectsDriftEscapesAndUnapprovedSigningChanges()
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
        foreach (string argument in new[] { "-NoProfile", "-File", script, "-RepositoryRoot", root }) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated payload fixtures.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.AreEqual(0, process.ExitCode, $"Payload fixture failure:\n{await output}\n{await error}");
        StringAssert.Contains(await output, "PASS: payload integrity fixtures");
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root unavailable.");
    }
}
