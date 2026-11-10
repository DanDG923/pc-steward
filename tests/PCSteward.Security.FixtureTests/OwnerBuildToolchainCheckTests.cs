using System.Text.RegularExpressions;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Task 50 CB3.7c (ADR-033 redeploy; Codex security review of PR #32, owner decision 2026-11-10): the owner's read-only toolchain
/// check compares the user-local .NET SDK and the restored NuGet packages with Microsoft's published bytes before the elevated
/// redeploy. These checks keep the script read-only and Windows PowerShell 5.1 safe, and keep its CI job informational.
/// </summary>
[TestClass]
public sealed class OwnerBuildToolchainCheckTests
{
    private static readonly string[] BuilderProjects = ["src/PCSteward.ScheduledHost/PCSteward.ScheduledHost.csproj", "src/PCSteward.Desktop/PCSteward.Desktop.csproj", "src/PCSteward.ElevatedHelper/PCSteward.ElevatedHelper.csproj"];

    [TestMethod]
    public void ToolchainCheckJobIsInformationalAndReadOnly()
    {
        string workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        int job = workflow.IndexOf("  owner-build-toolchain-check:\n", StringComparison.Ordinal);
        Assert.IsTrue(job > 0, "the toolchain-check job is missing");
        int next = workflow.IndexOf("\n  owner-only-candidate-builder:\n", job, StringComparison.Ordinal);
        Assert.IsTrue(next > job, "the toolchain-check job must come before owner-only-candidate-builder");
        string section = workflow[job..next];
        StringAssert.Contains(section, "runs-on: windows-latest");
        StringAssert.Contains(section, "continue-on-error: true");
        StringAssert.Matches(section, new Regex(@"permissions:\s+contents: read"));
        StringAssert.Contains(section, "persist-credentials: false");
        StringAssert.Contains(section, "global-json-file: global.json");
        StringAssert.Contains(section, "pwsh -NoProfile -File ./scripts/Test-OwnerBuildToolchainCI.ps1");
        Assert.IsFalse(section.Contains("secrets.", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OwnerCheckIsReadOnlyAsciiAndCoversEveryArea()
    {
        string root = FindRepositoryRoot();
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "scripts", "Test-OwnerBuildToolchain.ps1"));
        Assert.IsTrue(bytes.All(b => b < 0x80), "Windows PowerShell 5.1 reads a BOM-less script as ANSI, so it must stay ASCII");
        string script = System.Text.Encoding.ASCII.GetString(bytes);
        foreach (string forbidden in new[] { "Remove-Item", "Set-Content", "Add-Content", "Out-File", "Start-Process", "Invoke-Expression", "-Verb RunAs", "Set-ItemProperty", "New-ItemProperty", "SetEnvironmentVariable", "git.exe -C $Worktree checkout", " reset ", " clean " })
            Assert.IsFalse(script.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"the owner check must not contain {forbidden}");
        // Its only writes: the work folder and the downloaded zip inside it.
        Assert.AreEqual(1, Regex.Count(script, @"New-Item -ItemType Directory"), "one directory creation (the work folder)");
        StringAssert.Contains(script, "New-Item -ItemType Directory -Path $WorkFolder -Force");
        Assert.AreEqual(1, Regex.Count(script, @"Move-Item"), "one move (the finished download)");
        Assert.AreEqual(1, Regex.Count(script, @"DownloadFile\("), "one download (the SDK zip)");
        foreach (string area in new[] { "('GIT path=", "'WORKTREE head='", "'SDK-ZIP name='", "'SDK-FILES zipEntries='", "'PACKAGES used='", "'BUILD-HOOKS msbuildUserCurrent='", "'CB3.7c-OK'" })
            StringAssert.Contains(script, area);
        // The tolerated Git filters are exactly Git for Windows' system-scope Git LFS defaults.
        StringAssert.Contains(script, "$lfsNames = @('filter.lfs.clean', 'filter.lfs.smudge', 'filter.lfs.process', 'filter.lfs.required')");
        StringAssert.Contains(script, "$p[0] -cne 'system'");
        // NuGet's generated imports are checked element by element, not only by their Import paths (Codex review of PR #32).
        StringAssert.Contains(script, "function Get-NuGetGeneratedFileProblems");
        StringAssert.Contains(script, "if ($text.Contains('$([')) { $problems.Add('property function') }");
        // Git comes only from the signed Program Files install, and what it returns is re-hashed to the pinned commit
        // (Codex security review of PR #32); untracked files are listed from the folder, not from Git.
        StringAssert.Contains(script, "function Get-TrustedGit");
        StringAssert.Contains(script, "Get-AuthenticodeSignature -LiteralPath $path");
        StringAssert.Contains(script, "$commitVerified = (Get-GitObjectId 'commit' $commitBytes) -ceq $commit");
        StringAssert.Contains(script, "$folderIds[$folder] = Get-GitObjectId 'tree' $content.ToArray()");
        Assert.IsFalse(script.Contains("Get-Command -Name 'git.exe'", StringComparison.OrdinalIgnoreCase), "git must not be resolved from Path");
        Assert.IsFalse(script.Contains("ls-files", StringComparison.Ordinal), "untracked files must not be listed by Git");
        // Older side-by-side workload manifests pass only against pinned package bytes, and workload state is never tolerated.
        StringAssert.Contains(script, "$sideBySide['sdk-manifests\\' + $m.band + '\\' + $m.id + '\\' + $m.version] = $m");
        StringAssert.Contains(script, "foreach ($stateFolder in @('metadata\\workloads'))");
        // Output never prints the user's profile path.
        StringAssert.Contains(script, "$Text.Replace($script:Profile, '<PROFILE>')");
    }

    [TestMethod]
    public void CommittedPinsNameTheOwnersBuildCommitAndTheBuilderProjects()
    {
        string path = Path.Combine(FindRepositoryRoot(), "docs", "review", "task50-owner-build-toolchain-pins.json");
        if (!File.Exists(path)) Assert.Inconclusive("The pin file is committed once CI has produced it.");
        using System.Text.Json.JsonDocument pins = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        System.Text.Json.JsonElement rootElement = pins.RootElement;
        Assert.AreEqual("pc-steward-owner-build-toolchain-pins:1", rootElement.GetProperty("schema").GetString());
        Assert.AreEqual("d9a754b8da980a84eb4641cd58c98e27bdfd4444", rootElement.GetProperty("sourceCommit").GetString());
        Assert.AreEqual("10.0.403", rootElement.GetProperty("sdkVersion").GetString());
        StringAssert.Matches(rootElement.GetProperty("sdkZip").GetProperty("sha512").GetString(), new Regex(@"\A[0-9a-f]{128}\z"));
        StringAssert.StartsWith(rootElement.GetProperty("sdkZip").GetProperty("url").GetString(), "https://builds.dotnet.microsoft.com/");
        CollectionAssert.AreEqual(
            BuilderProjects,
            rootElement.GetProperty("projects").EnumerateArray().Select(p => p.GetString()).ToArray());
        string[] ids = rootElement.GetProperty("packages").EnumerateArray().Select(p => p.GetProperty("id").GetString()!.ToLowerInvariant()).ToArray();
        CollectionAssert.Contains(ids, "microsoft.dotnet.ilcompiler");
        CollectionAssert.Contains(ids, "runtime.win-x64.microsoft.dotnet.ilcompiler");
        foreach (System.Text.Json.JsonElement package in rootElement.GetProperty("packages").EnumerateArray())
            StringAssert.Matches(package.GetProperty("sha512").GetString(), new Regex(@"\A[A-Za-z0-9+/]{86}==\z"));
        // The owner's older 10.0.111 workload manifests (CB3.7c amendment): ten signed nuget.org packages, 93 files.
        System.Text.Json.JsonElement[] manifests = rootElement.GetProperty("sdkSideBySideManifests").EnumerateArray().ToArray();
        Assert.AreEqual(10, manifests.Length);
        foreach (System.Text.Json.JsonElement manifest in manifests)
        {
            Assert.AreEqual("10.0.111", manifest.GetProperty("version").GetString());
            Assert.AreEqual("10.0.100", manifest.GetProperty("band").GetString());
            StringAssert.StartsWith(manifest.GetProperty("id").GetString(), "microsoft.net.workload.");
            StringAssert.StartsWith(manifest.GetProperty("url").GetString(), "https://api.nuget.org/v3-flatcontainer/");
            StringAssert.Matches(manifest.GetProperty("sha512").GetString(), new Regex(@"\A[A-Za-z0-9+/]{86}==\z"));
            foreach (System.Text.Json.JsonProperty file in manifest.GetProperty("files").EnumerateObject())
                StringAssert.Matches(file.Value.GetString(), new Regex(@"\A[0-9A-F]{64}\z"));
        }
        Assert.AreEqual(93, manifests.Sum(m => m.GetProperty("files").EnumerateObject().Count()));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
