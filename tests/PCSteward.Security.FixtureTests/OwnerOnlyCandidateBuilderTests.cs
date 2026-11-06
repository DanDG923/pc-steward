using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Task 50 gap G3; FR-UPD-15, TM-03/TM-13, ADR-022 and its protocol-2 alignment note. scripts/New-OwnerOnlyCandidate.ps1
/// builds the unsigned v2 candidate for the owner-only protected test deployment. These tests parse it, apply AST policy
/// (no Invoke-Expression, no -Verb RunAs, no network or signing, structured dotnet arguments), run the REAL pure functions
/// from the parsed script on synthetic inputs, and, under Windows PowerShell 5.1, feed the written manifest to the
/// installer's own Read-OwnerOnlyCandidateManifest. The end-to-end build runs in the Windows CI candidate-builder job.
/// </summary>
[TestClass]
public sealed class OwnerOnlyCandidateBuilderTests
{
    private static readonly string[] StepNames =
    [
        "restore-scheduledhost", "restore-desktop", "restore-helper", "build-scheduledhost", "build-desktop", "build-helper", "publish-desktop", "publish-helper",
    ];

    private static readonly string[] InvokedExecutables = ["dotnet", "git", "windowspowershell"];
    private static readonly string[] ExpectedXbf = ["App.xbf", "Resources\\ColorTokens.xbf", "Views\\Sub\\Page.xbf"];
    private static readonly string[] ManifestKeys = ["bytes", "path", "sha256"];
    private static readonly string[] ProbeAssemblies = ["PCSteward.Execution.dll", "PCSteward.Provider.Chocolatey.dll", "PCSteward.Desktop.dll"];

    private static readonly string[] FixturePaths =
    [
        "App.xbf", "Assets\\pc-steward.ico", "B.txt", "PCSteward.DeploymentCapability.txt", "PCSteward.Desktop.exe", "PCSteward.Desktop.pri",
        "PCSteward.ElevatedHelper.exe", "PCSteward.ScheduledHost.dll", "Resources\\ColorTokens.xbf", "Resources\\Typography.xbf", "a.txt", "empty.bin",
        "en-us\\Microsoft.ui.xaml.dll.mui", "with space.dat",
    ];

    [TestMethod]
    [DataRow("pwsh")]
    [DataRow("windows-powershell")]
    public async Task BuilderParsesPassesAstPolicyAndWritesAManifestTheInstallerAccepts(string shell)
    {
        string? executable = ResolveShell(shell);
        if (executable is null)
        {
            // The installer's manifest reader compiles Windows file primitives under Windows PowerShell 5.1: Windows-CI evidence.
            if (shell == "windows-powershell") Assert.Inconclusive("Windows PowerShell 5.1 runs only on Windows; this row (installer reader acceptance) is Windows-CI evidence.");
            Assert.Inconclusive("PowerShell 7 (pwsh) is not on PATH; the builder fixture needs it on this platform.");
        }
        string root = FindRepositoryRoot();
        DirectoryInfo work = Directory.CreateTempSubdirectory("pc-steward-candidate-builder-");
        try
        {
            (int exitCode, string output, string error) = await RunAsync(executable!, root, TimeSpan.FromSeconds(180),
                "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "tests", "PCSteward.Security.FixtureTests", "Fixtures", "OwnerOnlyCandidateBuilder.ps1"),
                "-RepositoryRoot", root, "-WorkRoot", work.FullName);
            Assert.AreEqual(0, exitCode, $"Fixture failed:\n{output}\n{error}");
            StringAssert.Contains(output, "PASS: owner-only candidate builder fixtures");
            using JsonDocument results = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(work.FullName, "results.json")));
            JsonElement result = results.RootElement;

            foreach (JsonProperty parsed in result.GetProperty("parseErrors").EnumerateObject()) Assert.AreEqual(0, parsed.Value.GetInt32(), parsed.Name);

            // AST policy: no violations; dotnet/git/Windows PowerShell only through resolved variables with structured arguments.
            string[] violations = [.. result.GetProperty("violations").EnumerateArray().Select(item => item.GetString()!)];
            Assert.AreEqual(0, violations.Length, string.Join("\n", violations));
            // The policy is live: a canary script trips each dynamic-code and process-creation ban.
            string[] canary = [.. result.GetProperty("canaryViolations").EnumerateArray().Select(item => item.GetString()!)];
            foreach (string ban in new[] { "canary: [scriptblock]::Create", "canary: $ExecutionContext", "canary: New-Object Diagnostics.Process", "canary: type Diagnostics.Process" })
                Assert.IsTrue(canary.Contains(ban), $"{ban} not detected:\n{string.Join("\n", canary)}");
            Assert.AreEqual(2, canary.Count(item => item == "canary: [scriptblock]::Create"), "both [scriptblock] spellings");
            Assert.AreEqual(2, canary.Count(item => item == "canary: New-Object Diagnostics.Process"), "positional and -TypeName forms");
            JsonElement invocations = result.GetProperty("invocations");
            CollectionAssert.AreEquivalent(InvokedExecutables, invocations.EnumerateObject().Select(item => item.Name).ToArray());
            Assert.AreEqual(2, invocations.GetProperty("dotnet").GetInt32(), "dotnet: the splatted build step and --version only");
            Assert.AreEqual(3, invocations.GetProperty("git").GetInt32(), "git: rev-parse, status and the probe-mark merge-base --is-ancestor only");
            Assert.AreEqual(1, invocations.GetProperty("windowspowershell").GetInt32(), "one Windows PowerShell 5.1 self-validation child");
            Assert.IsTrue(result.GetProperty("splattedDotnet").GetBoolean(), "build steps must run as & $Dotnet @Arguments");
            string[] dotSources = [.. result.GetProperty("dotSources").EnumerateArray().Select(item => item.GetString()!)];
            Assert.AreEqual(1, dotSources.Length);
            StringAssert.Contains(dotSources[0], "OwnerOnlyPayloadIntegrity.ps1");
            Assert.AreEqual(StepNames.Length, result.GetProperty("buildStepArgumentArrays").GetInt32());

            AssertBuildSteps(result.GetProperty("steps"));
            AssertProbeMark(result.GetProperty("probeMark"), result.GetProperty("probeTypeNames"));
            AssertLineage(result.GetProperty("lineage"));
            AssertSdkRule(result.GetProperty("sdk"));
            foreach (JsonProperty rule in result.GetProperty("outputRoots").EnumerateObject())
                Assert.AreEqual(rule.Name is "temp" or "runner-temp" ? "accepted" : "refused", rule.Value.GetString(), rule.Name);

            // Manifest writer: exact format, independently re-derived from the synthetic tree.
            JsonElement manifest = result.GetProperty("manifest");
            AssertManifest(manifest.GetProperty("path").GetString()!, manifest.GetProperty("payloadRoot").GetString()!, manifest.GetProperty("sha256").GetString()!);
            Assert.AreEqual(FixturePaths.Length, manifest.GetProperty("entries").GetInt32());
            Assert.IsTrue(result.GetProperty("rendering").GetProperty("stable").GetBoolean());
            Assert.IsTrue(result.GetProperty("rendering").GetProperty("reversedInputSameBytes").GetBoolean(), "input order must not change the manifest bytes");
            // Each refusal for its own reason, not merely any exception.
            Dictionary<string, string> refusalReasons = new()
            {
                ["overwrite"] = "already exists", ["noncanonical"] = "Noncanonical candidate path", ["traversal"] = "Noncanonical candidate path",
                ["forwardSlash"] = "Noncanonical candidate path", ["reservedName"] = "Noncanonical candidate path", ["caseCollision"] = "Duplicate or case-colliding candidate path",
                ["lowerCaseHash"] = "Invalid candidate entry", ["negativeLength"] = "Invalid candidate entry", ["tooMany"] = "1 to 512 files",
                ["oversize"] = "exceeds the 256 MiB manifest bound",
            };
            JsonElement writerRefusals = result.GetProperty("writerRefusals");
            CollectionAssert.AreEquivalent(refusalReasons.Keys.ToArray(), writerRefusals.EnumerateObject().Select(item => item.Name).ToArray());
            foreach ((string name, string reason) in refusalReasons)
            {
                JsonElement refusal = writerRefusals.GetProperty(name);
                Assert.AreEqual(JsonValueKind.String, refusal.ValueKind, $"writer must refuse: {name}");
                StringAssert.Contains(refusal.GetString(), reason, name);
            }

            JsonElement copy = result.GetProperty("copy");
            Assert.IsTrue(copy.GetProperty("first").GetBoolean());
            Assert.IsFalse(copy.GetProperty("identical").GetBoolean());
            StringAssert.Contains(copy.GetProperty("conflict").GetString(), "Conflicting payload file from fixture");
            StringAssert.Contains(copy.GetProperty("conflict").GetString(), "differs from an earlier output");
            StringAssert.Contains(result.GetProperty("reparseRefused").GetString(), "Reparse point refused");
            CollectionAssert.AreEqual(ExpectedXbf,
                result.GetProperty("expectedXbf").EnumerateArray().Select(item => item.GetString()).ToArray());

            JsonElement integrity = result.GetProperty("integrity");
            if (shell == "windows-powershell")
            {
                Assert.IsTrue(integrity.GetProperty("ran").GetBoolean());
                Assert.IsTrue(integrity.GetProperty("accepted").GetBoolean());
                Assert.AreEqual("pinned-unsigned-v2", integrity.GetProperty("payloadOrigin").GetString());
                Assert.AreEqual("2", integrity.GetProperty("helperProtocol").GetString());
                Assert.AreEqual(FixturePaths.Length, integrity.GetProperty("files").GetInt32());
                Assert.AreEqual(1024, integrity.GetProperty("imageBytes").GetInt64());
                Assert.AreEqual(JsonValueKind.String, integrity.GetProperty("driftRefused").ValueKind, "a byte changed after pinning must be refused");
                Assert.IsTrue(integrity.GetProperty("restoredAccepted").GetBoolean(), "the drift probe must restore the pinned bytes");
            }
            else
            {
                // Not silently skipped: the windows-powershell row runs the installer reader on Windows CI.
                Assert.IsFalse(integrity.GetProperty("ran").GetBoolean());
                StringAssert.Contains(integrity.GetProperty("reason").GetString(), "Windows PowerShell 5.1");
            }
        }
        finally { work.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task BuilderRefusesUnsafeInvocationsBeforeAnyBuildStep()
    {
        string root = FindRepositoryRoot();
        string builder = Path.Combine(root, "scripts", "New-OwnerOnlyCandidate.ps1");
        if (!OperatingSystem.IsWindows())
        {
            string? pwsh = ResolveShell("pwsh");
            if (pwsh is null) Assert.Inconclusive("PowerShell 7 (pwsh) is not on PATH; the refusal check needs it on this platform.");
            string target = Path.Combine(Path.GetTempPath(), "pc-steward-candidate-" + Guid.NewGuid().ToString("N"));
            (int code, string output, string error) = await RunAsync(pwsh!, root, TimeSpan.FromSeconds(60), "-NoProfile", "-NonInteractive", "-File", builder, "-OutputRoot", target);
            Assert.AreNotEqual(0, code, output);
            StringAssert.Contains(error + output, "requires Windows");
            Assert.IsFalse(Directory.Exists(target));
            return;
        }

        string windowsPowerShell = ResolveShell("windows-powershell")!;
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        DirectoryInfo existing = CreateExistingOutputRoot(root);
        try
        {
            (string Path, string Message)[] refusals =
            [
                (Path.Combine(programFiles, "PCSteward-candidate-" + Guid.NewGuid().ToString("N")), "protected or repository location"),
                (Path.Combine(root, "artifacts", "candidate-" + Guid.NewGuid().ToString("N")), "protected or repository location"),
                (existing.FullName, "must not exist"),
                ("relative\\candidate", "absolute local drive path"),
            ];
            foreach ((string target, string message) in refusals)
            {
                (int code, string output, string error) = await RunAsync(windowsPowerShell, root, TimeSpan.FromSeconds(60), "-NoProfile", "-NonInteractive", "-File", builder, "-OutputRoot", target);
                Assert.AreNotEqual(0, code, $"{target}\n{output}");
                StringAssert.Contains(error + output, message, target);
                StringAssert.DoesNotMatch(output, new System.Text.RegularExpressions.Regex("== restore"), "no build step may run");
                if (target != existing.FullName) Assert.IsFalse(Directory.Exists(target), target);
            }
            Assert.AreEqual(0, existing.GetFileSystemInfos().Length, "an existing OutputRoot is never written");
        }
        finally { existing.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task CandidateBuilderCiLauncherRefusesAccountCreationOutsideHostedRunner()
    {
        // TM-04/TM-06 pattern: disposable CI accounts are never created on a developer machine.
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("The CI launcher guard runs under Windows PowerShell 5.1; this row is Windows-CI evidence.");
        string root = FindRepositoryRoot();
        ProcessStartInfo start = new(ResolveShell("windows-powershell")!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root,
        };
        start.Environment.Remove("GITHUB_ACTIONS");
        start.Environment.Remove("RUNNER_ENVIRONMENT");
        foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(root, "scripts", "Test-OwnerOnlyCandidateBuilderCI.ps1") }) start.ArgumentList.Add(argument);
        (int code, string output, string error) = await RunAsync(start, TimeSpan.FromSeconds(60));
        Assert.AreNotEqual(0, code, output);
        // Windows PowerShell wraps redirected error text at the console width; compare with whitespace collapsed.
        StringAssert.Contains(System.Text.RegularExpressions.Regex.Replace(error, @"\s+", " "), "this launcher requires an administrator on a GitHub-hosted runner");
    }

    /// <summary>
    /// An existing directory the builder must refuse only as "must not exist": not under TEMP (CI points TEMP inside the
    /// checkout, a refused repository location, and TEMP may be an 8.3 short path) and writable by the CI standard user,
    /// whose inherited LOCALAPPDATA names the inaccessible runneradmin profile. The first writable candidate wins.
    /// </summary>
    private static DirectoryInfo CreateExistingOutputRoot(string repositoryRoot)
    {
        string leaf = "pc-steward-candidate-existing-" + Guid.NewGuid().ToString("N");
        string?[] parents =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("RUNNER_TEMP"),
            Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)),
        ];
        List<string> skipped = [];
        foreach (string? parent in parents)
        {
            if (string.IsNullOrEmpty(parent) || parent.Contains('~', StringComparison.Ordinal) || !Path.IsPathFullyQualified(parent)) continue;
            string candidate = Path.Combine(parent, leaf);
            if (candidate.StartsWith(repositoryRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue;
            try { return Directory.CreateDirectory(candidate); }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { skipped.Add($"{candidate}: {exception.Message}"); }
        }
        Assert.Fail("No writable location outside the repository for the existing-OutputRoot refusal:\n" + string.Join("\n", skipped));
        throw new UnreachableException();
    }

    [TestMethod]
    public void WindowsCiRunsTheCandidateBuilderWithThePinnedSdk()
    {
        string workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml"));
        int job = workflow.IndexOf("owner-only-candidate-builder:", StringComparison.Ordinal);
        Assert.IsTrue(job > 0, "the Windows candidate-builder job is missing");
        string section = workflow[job..];
        StringAssert.Contains(section, "runs-on: windows-latest");
        StringAssert.Contains(section, "global-json-file: global.json");
        StringAssert.Contains(section, "scripts/Test-OwnerOnlyCandidateBuilderCI.ps1");
        StringAssert.Matches(section, new System.Text.RegularExpressions.Regex(@"permissions:\s+contents: read"));
        StringAssert.Contains(section, "persist-credentials: false");
        // S7 obligation 1: the probe-build mark needs the commit lineage, so the builder job fetches full history.
        int next = section.IndexOf("\n  owner-only-signing-diagnostic:", StringComparison.Ordinal);
        StringAssert.Contains(next > 0 ? section[..next] : section, "fetch-depth: 0");
        // The existing build-and-test job keeps its own steps.
        StringAssert.Contains(workflow[..job], "pwsh -NoProfile -File ./scripts/Test-CI.ps1");
    }

    /// <summary>
    /// Task 50 S7 obligation 1 (qualification probe spec §9; FR-UPD-15, TM-03/04/07/13): rule pc-steward-probe-build-mark:1, run
    /// from the parsed builder over synthetic payload assemblies. Only a clean build of a commit that descends from the S6 probe
    /// commit, whose payload carries every compiled probe entry type, is marked included.
    /// </summary>
    private static void AssertProbeMark(JsonElement marks, JsonElement typeNames)
    {
        Dictionary<string, (bool Included, string Detail)> expected = new()
        {
            ["included"] = (true, "included"),
            ["dirty"] = (false, "source-dirty"),
            ["not-descendant"] = (false, "commit-lacks-probe"),
            ["missing-desktop"] = (false, "payload-missing:PCSteward.Desktop.dll"),
            ["missing-gate-type"] = (false, "type-missing:PCSteward.Desktop.dll/QualificationProbeLaunchGate"),
            ["missing-codec-type"] = (false, "type-missing:PCSteward.Execution.dll/QualificationProbeWireCodec"),
            ["missing-native-type"] = (false, "type-missing:PCSteward.Provider.Chocolatey.dll/NativeChocolateyProbePort"),
            ["prefix-only"] = (false, "type-missing:PCSteward.Desktop.dll/QualificationProbeLaunchGate"),
        };
        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), marks.EnumerateObject().Select(item => item.Name).ToArray());
        foreach ((string name, (bool included, string detail)) in expected)
        {
            Assert.AreEqual(included, marks.GetProperty(name).GetProperty("included").GetBoolean(), name);
            Assert.AreEqual(detail, marks.GetProperty(name).GetProperty("detail").GetString(), name);
        }
        CollectionAssert.AreEqual(ProbeAssemblies,
            typeNames.EnumerateObject().Select(item => item.Name).ToArray());
        // Every listed type is a real src type of the probe slices.
        string root = FindRepositoryRoot();
        string sources = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")).Select(File.ReadAllText));
        foreach (JsonProperty leaf in typeNames.EnumerateObject())
            foreach (string type in leaf.Value.EnumerateArray().Select(item => item.GetString()!))
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(sources, @"\b(class|record|interface|struct)\s+" + type + @"\b"), $"{leaf.Name}: {type}");

        // The record fields and constants, and the S6 commit is the reviewed probe slice (probe spec §11.6).
        string builder = File.ReadAllText(Path.Combine(root, "scripts", "New-OwnerOnlyCandidate.ps1"));
        StringAssert.Contains(builder, "$script:CandidateSchema = 'pc-steward-owner-only-candidate-build-v2'");
        StringAssert.Contains(builder, "$script:ProbeMarkRule = 'pc-steward-probe-build-mark:1'");
        StringAssert.Contains(builder, "$script:ProbeS6Commit = '2172f955a751ccc464b27075053c835790bc412f'");
        StringAssert.Contains(builder, "& $Git @('--no-replace-objects', '-C', $RepositoryRoot, 'merge-base', '--is-ancestor', $script:ProbeS6Commit, $Commit)");
        // Review L8: every builder Git call runs isolated and without replace objects.
        Assert.AreEqual(3, System.Text.RegularExpressions.Regex.Count(builder, @"& \$Git @\('--no-replace-objects', '-C', \$RepositoryRoot"));
        Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Count(builder, @"\$saved = Enter-OwnerOnlyCandidateGitIsolation"));
        Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Count(builder, @"finally \{ Exit-OwnerOnlyCandidateGitIsolation -Saved \$saved \}"));
        StringAssert.Contains(builder, "$descends = ($LASTEXITCODE -eq 0)");
        StringAssert.Contains(builder, "probeIncluded = [bool]$probeMark.Included");
        string ci = File.ReadAllText(Path.Combine(root, "scripts", "Test-OwnerOnlyCandidateBuilderCI.ps1"));
        StringAssert.Contains(ci, "$summary.probeIncluded -ne $true");
        StringAssert.Contains(ci, "$failures.Add('probe build mark')");
    }

    /// <summary>
    /// Review L8: the lineage check through a stand-in git. Exit 0 counts as descent only when git was called with
    /// --no-replace-objects first, GIT_NO_REPLACE_OBJECTS=1 and no inherited GIT_DIR; exits 1 (not an ancestor) and 128 (for
    /// example an unknown commit) give probeIncluded = false. The caller's Git variables are restored afterwards.
    /// </summary>
    private static void AssertLineage(JsonElement lineage)
    {
        foreach ((string name, bool descends) in new[] { ("exit-0", true), ("exit-1", false), ("exit-128", false) })
        {
            JsonElement row = lineage.GetProperty(name);
            Assert.AreEqual(descends, row.GetProperty("descends").GetBoolean(), name);
            Assert.AreEqual(descends, row.GetProperty("included").GetBoolean(), name);
            Assert.AreEqual(descends ? "included" : "commit-lacks-probe", row.GetProperty("detail").GetString(), name);
            Assert.IsTrue(row.GetProperty("gitDirRestored").GetBoolean(), name);
            Assert.IsTrue(row.GetProperty("noReplaceRestored").GetBoolean(), $"{name}: {row}");
        }
    }

    private static void AssertBuildSteps(JsonElement steps)
    {
        JsonElement[] rows = [.. steps.EnumerateArray()];
        CollectionAssert.AreEqual(StepNames, rows.Select(row => row.GetProperty("name").GetString()).ToArray());
        string[] profile = ["--configuration", "Release", "--runtime", "win-x64", "--self-contained", "false", "--no-restore"];
        foreach (JsonElement row in rows)
        {
            string name = row.GetProperty("name").GetString()!;
            Assert.AreEqual("System.String[]", row.GetProperty("type").GetString(), name);
            string[] arguments = [.. row.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()!)];
            string project = arguments[1];
            // One argument per value: the project path keeps its space and is never split or quoted into a command line.
            Assert.IsTrue(project.StartsWith(@"C:\src\pc steward", StringComparison.Ordinal) && project.EndsWith(".csproj", StringComparison.Ordinal), $"{name}: {project}");
            foreach (string argument in arguments.Where(argument => argument.StartsWith("--", StringComparison.Ordinal)))
                Assert.IsFalse(argument.Contains(' ', StringComparison.Ordinal) || argument.Contains('"', StringComparison.Ordinal), $"{name}: {argument}");
            string leaf = name.EndsWith("scheduledhost", StringComparison.Ordinal) ? "PCSteward.ScheduledHost.csproj"
                : name.EndsWith("desktop", StringComparison.Ordinal) ? "PCSteward.Desktop.csproj" : "PCSteward.ElevatedHelper.csproj";
            Assert.IsTrue(project.EndsWith(leaf, StringComparison.Ordinal), $"{name}: {project}");
            if (name.StartsWith("restore-", StringComparison.Ordinal))
            {
                CollectionAssert.AreEqual(new[] { "restore", project, "--runtime", "win-x64" }, arguments, name);
                continue;
            }
            if (name.StartsWith("build-", StringComparison.Ordinal))
            {
                // A build with --output redirects the ScheduledHost RID output the desktop targets read (Task 48 failure).
                CollectionAssert.AreEqual(new[] { "build", project }.Concat(profile).Append("--disable-build-servers").ToArray(), arguments, name);
                continue;
            }
            string output = name == "publish-desktop" ? @"C:\out dir\publish-staging\desktop" : @"C:\out dir\publish-staging\helper";
            CollectionAssert.AreEqual(new[] { "publish", project }.Concat(profile).Concat(["--no-build", "--disable-build-servers", "--output", output]).ToArray(), arguments, name);
        }
    }

    private static void AssertSdkRule(JsonElement sdk)
    {
        Dictionary<string, string> expected = new()
        {
            ["exact"] = "exact", ["patch-up"] = "rolled", ["patch-down"] = "refused", ["band-up"] = "refused", ["band-down"] = "refused", ["minor"] = "refused",
            ["major"] = "refused", ["prerelease"] = "refused", ["disable-up"] = "refused", ["disable-exact"] = "exact", ["feature-roll"] = "refused", ["garbage"] = "refused",
        };
        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), sdk.EnumerateObject().Select(item => item.Name).ToArray());
        foreach ((string name, string outcome) in expected) Assert.AreEqual(outcome, sdk.GetProperty(name).GetString(), name);
    }

    private static void AssertManifest(string manifestPath, string payloadRoot, string reportedSha256)
    {
        byte[] bytes = File.ReadAllBytes(manifestPath);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), reportedSha256, "reported hash must be the written bytes");
        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "UTF-8 without BOM");
        string text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.IsTrue(text.StartsWith("[\n", StringComparison.Ordinal) && text.EndsWith("]\n", StringComparison.Ordinal) && !text.Contains('\r', StringComparison.Ordinal));
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement[] entries = [.. document.RootElement.EnumerateArray()];
        string[] paths = [.. entries.Select(entry => entry.GetProperty("path").GetString()!)];
        CollectionAssert.AreEqual(FixturePaths, paths, "exact path set, ordinal order");
        string[] sorted = [.. paths];
        Array.Sort(sorted, StringComparer.Ordinal);
        CollectionAssert.AreEqual(sorted, paths);
        foreach (JsonElement entry in entries)
        {
            CollectionAssert.AreEqual(ManifestKeys, entry.EnumerateObject().Select(property => property.Name).ToArray(), "closed key set");
            string path = entry.GetProperty("path").GetString()!;
            Assert.IsFalse(path.Contains('/', StringComparison.Ordinal), path);
            string file = Path.Combine([payloadRoot, .. path.Split('\\')]);
            byte[] content = File.ReadAllBytes(file);
            Assert.AreEqual(content.LongLength, entry.GetProperty("bytes").GetInt64(), path);
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(content)), entry.GetProperty("sha256").GetString(), path);
        }
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

    private static Task<(int ExitCode, string Output, string Error)> RunAsync(string executable, string workingDirectory, TimeSpan timeout, params string[] arguments)
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
        return RunAsync(start, timeout);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start, TimeSpan timeout)
    {
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the candidate-builder fixture.");
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
