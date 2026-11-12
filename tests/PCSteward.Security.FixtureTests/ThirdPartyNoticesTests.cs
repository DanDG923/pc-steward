using System.Diagnostics;
using System.Text.Json;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Phase C close-out (NFR-MNT-01/05, ADR-008). scripts/PCStewardThirdPartyNotices.ps1 writes THIRD-PARTY-NOTICES.txt for the
/// release payload (New-OwnerOnlyCandidate.ps1) and the private-beta package (New-PrivateBetaPackage.ps1). These tests parse
/// it, apply a small AST policy (no network, process, file-writing or dynamic-code command), and run its real functions on a
/// synthetic NuGet package folder under PowerShell 7 and Windows PowerShell 5.1 (the candidate builder's runtime): every
/// mapped package and the Native AOT helper's runtime packages get exactly one section, notice text that is not ASCII
/// survives, no local path leaks, and an unmapped package, an unused map entry, a changed licence or copyright, a missing or
/// empty notice, an inexact runtime pack, a missing helper runtime package or a doubled pin each stop the build. The real
/// packages are checked by the Windows CI package-licence-map and candidate-builder jobs.
/// </summary>
[TestClass]
public sealed class ThirdPartyNoticesTests
{
    private static readonly string[] Refusals =
    [
        "unmappedPackage", "unusedMapEntry", "changedCopyright", "changedSpdx", "emptyNotice", "missingNotice", "inexactRuntimePack",
        "missingHelperRuntime", "helperCopyright", "twoInProcComPins", "relativePackageFolder", "missingDeps",
    ];

    [TestMethod]
    [DataRow("pwsh")]
    [DataRow("windows-powershell")]
    public async Task NoticesCoverEveryPackageOnceAndRefuseAnythingUnreviewed(string shell)
    {
        string? executable = ResolveShell(shell);
        if (executable is null)
        {
            if (shell == "windows-powershell") Assert.Inconclusive("Windows PowerShell 5.1 runs only on Windows; this row is Windows-CI evidence.");
            Assert.Inconclusive("PowerShell 7 (pwsh) is not on PATH; the notices fixture needs it on this platform.");
        }
        string root = FindRepositoryRoot();
        DirectoryInfo work = Directory.CreateTempSubdirectory("pc-steward-notices-");
        try
        {
            (int exitCode, string output, string error) = await RunAsync(executable!, root, TimeSpan.FromSeconds(180),
                "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "tests", "PCSteward.Security.FixtureTests", "Fixtures", "ThirdPartyNotices.ps1"),
                "-RepositoryRoot", root, "-WorkRoot", work.FullName);
            Assert.AreEqual(0, exitCode, $"Fixture failed:\n{output}\n{error}");
            StringAssert.Contains(output, "PASS: third-party notices fixtures");
            using JsonDocument results = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(work.FullName, "results.json")));
            JsonElement result = results.RootElement;

            Assert.AreEqual(0, result.GetProperty("parseErrors").GetInt32());
            Assert.IsTrue(result.GetProperty("asciiSource").GetBoolean(), "Windows PowerShell 5.1 reads a script without a BOM as ANSI");
            string[] violations = [.. result.GetProperty("astViolations").EnumerateArray().Select(item => item.GetString()!)];
            Assert.AreEqual(0, violations.Length, string.Join("\n", violations));

            JsonElement accepted = result.GetProperty("accepted");
            Assert.AreEqual(accepted.GetProperty("expectedPackageCount").GetInt32(), accepted.GetProperty("packageCount").GetInt32());
            foreach (string check in new[] { "everySectionOnce", "nonAsciiNoticeKept", "copyrightKept", "standardMit", "standardApache", "noLocalPath", "endsWithNewLine", "stable" })
                Assert.IsTrue(accepted.GetProperty(check).GetBoolean(), check);

            JsonElement refusals = result.GetProperty("refusals");
            CollectionAssert.AreEquivalent(Refusals, refusals.EnumerateObject().Select(item => item.Name).ToArray());
            foreach (string refusal in Refusals)
                Assert.IsFalse(string.IsNullOrEmpty(refusals.GetProperty(refusal).GetString()), $"{refusal} must stop the build");
            StringAssert.Contains(refusals.GetProperty("unmappedPackage").GetString()!, "No approved licence/notice mapping");
            StringAssert.Contains(refusals.GetProperty("unusedMapEntry").GetString()!, "does not exactly reconcile");
            StringAssert.Contains(refusals.GetProperty("changedCopyright").GetString()!, "Unexpected copyright");
            StringAssert.Contains(refusals.GetProperty("changedSpdx").GetString()!, "Unexpected SPDX");
            StringAssert.Contains(refusals.GetProperty("emptyNotice").GetString()!, "is empty");
            StringAssert.Contains(refusals.GetProperty("missingNotice").GetString()!, "is missing");
            StringAssert.Contains(refusals.GetProperty("inexactRuntimePack").GetString()!, "not exact");
            StringAssert.Contains(refusals.GetProperty("missingHelperRuntime").GetString()!, "exactly one version");
            StringAssert.Contains(refusals.GetProperty("helperCopyright").GetString()!, "Unexpected copyright");
            StringAssert.Contains(refusals.GetProperty("twoInProcComPins").GetString()!, "exactly once");
            StringAssert.Contains(refusals.GetProperty("relativePackageFolder").GetString()!, "absolute folder");
            StringAssert.Contains(refusals.GetProperty("missingDeps").GetString()!, "dependency manifest is missing");
        }
        finally { work.Delete(recursive: true); }
    }

    [TestMethod]
    public void TheReleaseBuilderAndThePrivateBetaPackageShareOneNoticesScript()
    {
        string root = FindRepositoryRoot();
        string builder = File.ReadAllText(Path.Combine(root, "scripts", "New-OwnerOnlyCandidate.ps1"));
        string package = File.ReadAllText(Path.Combine(root, "scripts", "New-PrivateBetaPackage.ps1"));
        foreach (string script in new[] { builder, package })
            StringAssert.Contains(script, ". (Join-Path $PSScriptRoot 'PCStewardThirdPartyNotices.ps1')");
        StringAssert.Contains(builder, "$script:NoticesLeaf = 'THIRD-PARTY-NOTICES.txt'");
        StringAssert.Contains(builder, "New-PCStewardReleaseThirdPartyNotices -RepositoryRoot $repositoryRoot -PayloadRoot $payload");
        // Written before the manifest, so the notices are a pinned payload file like any other.
        Assert.IsTrue(builder.IndexOf("New-PCStewardReleaseThirdPartyNotices -RepositoryRoot", StringComparison.Ordinal)
            < builder.IndexOf("$entries = Get-OwnerOnlyCandidateFileEntries -Root $payload", StringComparison.Ordinal));
        Assert.IsFalse(package.Contains("$licenseMap = [ordered]@{", StringComparison.Ordinal), "one licence map only");
        StringAssert.Contains(package, "Get-PCStewardLicenceEvidence -Packages $packages");
    }

    private static string? ResolveShell(string shell)
    {
        if (shell == "windows-powershell")
        {
            if (!OperatingSystem.IsWindows()) return null;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            Assert.IsTrue(File.Exists(path), "Windows PowerShell 5.1 is required on Windows.");
            return path;
        }
        string leaf = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, leaf);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string executable, string workingDirectory, TimeSpan timeout, params string[] arguments)
    {
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the notices fixture.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(timeout);
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
