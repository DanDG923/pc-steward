using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 Part C re-run plan (docs/review/TASK_50_PART_C_RERUN_PLAN.md). The owner pastes
/// the plan's PowerShell blocks into Windows PowerShell 5.1, each once, so every block must parse with that parser, be
/// ASCII, and the D2 diagnostic must stay read-only. Blocks are only parsed, never run. The Windows PowerShell test fails
/// with <see cref="PlatformNotSupportedException"/> off Windows (reported, never silently skipped), like the repository's
/// other native tests; the other tests are portable.
/// </summary>
[TestClass]
public sealed partial class PartCRerunPlanBlockTests
{
    private const string PlanRelativePath = "docs/review/TASK_50_PART_C_RERUN_PLAN.md";
    private const int ExpectedBlockCount = 15;

    [TestMethod]
    public void PlanHasTheExpectedAsciiBlocks()
    {
        IReadOnlyList<string> blocks = Blocks();
        Assert.AreEqual(ExpectedBlockCount, blocks.Count);
        for (int index = 0; index < blocks.Count; index++)
            Assert.IsTrue(blocks[index].All(character => character < 0x80), $"block {index} is not ASCII");
    }

    [TestMethod]
    public void D2IsReadOnlyAndPrintsNoValues()
    {
        string d2 = Blocks().Single(block => block.Contains("'D2 ELEVATED '", StringComparison.Ordinal));
        foreach (string forbidden in new[]
                 {
                     "Set-", "New-Item", "Remove-", "Add-", "Copy-", "Move-", "Rename-", "Clear-", "Out-File", "Start-Process",
                     "Invoke-Expression", "icacls", "choco.exe", "choco ", "Set-Acl", ".Save(", "WriteAll", ">",
                 })
            Assert.IsFalse(d2.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"D2 contains '{forbidden}'");
        // The only things printed are fixed tokens built from booleans, counts and fixed class names; $cv (the configured
        // path) is never concatenated into an output line.
        Assert.IsFalse(Regex.IsMatch(d2, @"'D2 [^']*' \+ \$cv\b"), "D2 prints the cacheLocation value");
        StringAssert.Contains(d2, "'D2 LEGACY-KEY state='");
        StringAssert.Contains(d2, "'D2 CACHELOCATION absolute-local exists='");
    }

    [TestMethod]
    public void RedeployBlocksCarryTheRefreshedPins()
    {
        string text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), PlanRelativePath));
        IReadOnlyList<string> blocks = Blocks();
        Assert.IsTrue(blocks.Any(block => block.Contains("'PCSteward-Task50D'", StringComparison.Ordinal) && block.Contains("'5606da2'", StringComparison.Ordinal)));
        // The previous build commit and the removed signer must not be the old-signer pin in any CB2 block.
        Assert.IsFalse(blocks.Any(block => block.Contains("ebb40618debccac3f489571f9c6f7e709f3a2e93", StringComparison.Ordinal)));
        Assert.IsFalse(blocks.Any(block => block.Contains("-ceq '473DB701E6349AF18F672151E44AC166F95C53FD' -and", StringComparison.Ordinal)));
        Assert.IsTrue(blocks.Count(block => block.Contains("5606da23c303429c7c4711e9f5cfd43640c79289", StringComparison.Ordinal)) >= 3);
        Assert.IsTrue(blocks.Count(block => block.Contains("67B59B4C9BC3A006FBE39A72967E05F7B8C375DA", StringComparison.Ordinal)) >= 4);
        StringAssert.Contains(text, "2026-11-20T18:53:44Z");
    }

    [TestMethod]
    public void ProbeBlocksCarryTheRecordedPinsAndNoPlaceholders()
    {
        IReadOnlyList<string> blocks = Blocks();
        Assert.IsFalse(blocks.Any(block => block.Contains("<CB2.", StringComparison.Ordinal)), "a block still has a placeholder");
        IReadOnlyList<string> probes = blocks.Where(block => block.Contains("Task50-QualificationProbe.ps1", StringComparison.Ordinal) && block.Contains("-Mode ", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(2, probes.Count);
        foreach (string probe in probes)
        {
            StringAssert.Contains(probe, "-ExpectedBuildRecordSha256 '5877F0736DFF2134C19F42A7C53706B19979CAF885C63209F0BAB524ED4759A4'");
            StringAssert.Contains(probe, "-ExpectedManifestSha256 'F16B2AAC62EAE59866B9E24A5B5D1548612E4EC994FF57125F226DB1547FB954'");
            StringAssert.Contains(probe, "-ExpectedLedgerSha256 'C04D177CC383C255825E21D3CAF8D0E4D13C9E438EA6C27B394C73C1769A0F1C'");
            StringAssert.Contains(probe, "-ExpectedSignerThumbprint 'C7372974A9E6257671E106F11E660BC29C928849'");
            StringAssert.Contains(probe, "-ExpectedSignerNotAfterUtc '2026-11-20T22:01:22Z'");
        }
    }

    [TestMethod]
    public async Task EveryBlockParsesUnderWindowsPowerShell51()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows-native test (Windows PowerShell 5.1 parse of the Part C re-run plan blocks): it runs on Windows CI and is reported as failed on other platforms.");
        IReadOnlyList<string> blocks = Blocks();
        DirectoryInfo work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pc-steward-rerun-plan-parse-" + Guid.NewGuid().ToString("N")));
        try
        {
            for (int index = 0; index < blocks.Count; index++)
                File.WriteAllText(Path.Combine(work.FullName, $"block-{index:D2}.ps1"), blocks[index].Replace("\n", "\r\n", StringComparison.Ordinal), Encoding.ASCII);
            // Parse only: ParseFile builds the AST and never runs the block.
            string command =
                "$total = 0; foreach ($f in Get-ChildItem -LiteralPath '" + work.FullName.Replace("'", "''", StringComparison.Ordinal) + "' -Filter 'block-*.ps1' | Sort-Object Name) { " +
                "$tokens = $null; $errors = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$tokens, [ref]$errors); " +
                "$total += $errors.Count; foreach ($e in $errors) { $f.Name + ' LINE ' + $e.Extent.StartLineNumber + ': ' + $e.Message } }; 'PARSE-ERRORS=' + $total";
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            Assert.IsTrue(File.Exists(executable), "Windows PowerShell 5.1 is required on Windows.");
            ProcessStartInfo start = new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = work.FullName,
            };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows PowerShell.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(180));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            string stdout = await output, stderr = await error;
            Assert.AreEqual(0, process.ExitCode, $"Parser host failed:\n{stdout}\n{stderr}");
            StringAssert.Contains(stdout, "PARSE-ERRORS=0", $"Parse errors:\n{stdout}\n{stderr}");
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    private static IReadOnlyList<string> Blocks()
    {
        string text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), PlanRelativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
        return [.. BlockPattern().Matches(text).Select(match => match.Groups[1].Value)];
    }

    [GeneratedRegex(@"```powershell\n(.*?)\n```", RegexOptions.Singleline)]
    private static partial Regex BlockPattern();

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
