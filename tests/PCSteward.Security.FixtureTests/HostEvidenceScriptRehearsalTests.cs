using System.Diagnostics;
using System.Text;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 host-evidence plan revision 2 §4 and §12 (CI rehearsal of the single
/// approved host run). The committed probe script must parse with Windows PowerShell 5.1's own parser, and its
/// <c>-Mode SelfCheck</c> must compile the embedded C# with the same Add-Type call that Run uses, plus the synthetic CodeDom
/// builds. SelfCheck reads nothing outside its own directory, launches nothing and probes nothing. The script is copied into
/// a GUID-named temp directory, which is deleted afterwards; the repository copy is never executed.
/// <para>
/// The Windows PowerShell tests fail with <see cref="PlatformNotSupportedException"/> off Windows (reported, never silently
/// skipped), like the repository's other native tests. The byte-format test is portable.
/// </para>
/// </summary>
[TestClass]
public sealed class HostEvidenceScriptRehearsalTests
{
    private const string ScriptName = "Task50-HostEvidence.ps1";

    [TestMethod]
    public void ScriptIsAsciiCrlfAndCheckedInWithoutNewlineConversion()
    {
        string root = FindRepositoryRoot();
        byte[] bytes = File.ReadAllBytes(ScriptPath(root));
        Assert.IsTrue(bytes.Length > 0);
        for (int index = 0; index < bytes.Length; index++)
        {
            Assert.IsTrue(bytes[index] < 0x80, $"non-ASCII byte at offset {index}");
            if (bytes[index] == (byte)'\n') Assert.IsTrue(index > 0 && bytes[index - 1] == (byte)'\r', $"bare LF at offset {index}");
            if (bytes[index] == (byte)'\r') Assert.IsTrue(index + 1 < bytes.Length && bytes[index + 1] == (byte)'\n', $"bare CR at offset {index}");
        }
        // The approved SHA-256 binds the CRLF bytes; a checkout must not rewrite them.
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, ".gitattributes")), "scripts/evidence/Task50-HostEvidence.ps1 -text");
        string text = Encoding.ASCII.GetString(bytes);
        StringAssert.Contains(text, "[ValidateSet('Run', 'Cleanup', 'SelfCheck')]");
        Assert.IsFalse(text.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("Start-Process", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task WindowsPowerShell51ParserReportsZeroErrors()
    {
        RequireWindows("Windows PowerShell 5.1 parse of the host-evidence script");
        string root = FindRepositoryRoot();
        DirectoryInfo work = CreateWorkDirectory();
        try
        {
            string copy = Path.Combine(work.FullName, ScriptName);
            File.Copy(ScriptPath(root), copy);
            // Parse only: ParseFile builds the AST and never runs the script.
            string command =
                "$tokens = $null; $errors = $null; " +
                "[void][System.Management.Automation.Language.Parser]::ParseFile('" + copy.Replace("'", "''", StringComparison.Ordinal) + "', [ref]$tokens, [ref]$errors); " +
                "'PARSE-ERRORS=' + $errors.Count; foreach ($e in $errors) { 'LINE ' + $e.Extent.StartLineNumber + ': ' + $e.Message }";
            (int exitCode, string output, string error) = await RunWindowsPowerShellAsync(work.FullName,
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
            Assert.AreEqual(0, exitCode, $"Parser host failed:\n{output}\n{error}");
            StringAssert.Contains(output, "PARSE-ERRORS=0", $"Parse errors:\n{output}\n{error}");
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task SelfCheckCompilesTheEmbeddedCSharpUnderWindowsPowerShell51()
    {
        RequireWindows("Windows PowerShell 5.1 SelfCheck of the host-evidence script");
        string root = FindRepositoryRoot();
        DirectoryInfo work = CreateWorkDirectory();
        try
        {
            string copy = Path.Combine(work.FullName, ScriptName);
            File.Copy(ScriptPath(root), copy);
            DirectoryInfo scratch = work.CreateSubdirectory("scratch");
            (int exitCode, string output, string error) = await RunWindowsPowerShellAsync(work.FullName,
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", copy,
                "-Mode", "SelfCheck", "-ScratchDirectory", scratch.FullName);
            Assert.AreEqual(0, exitCode, $"SelfCheck failed:\n{output}\n{error}");
            StringAssert.Contains(output, "SELFCHECK-HOST: Desktop 5.1.");
            StringAssert.Contains(output, "SELFCHECK-RELEASE-ENTRIES: 56");
            StringAssert.Contains(output, "SELFCHECK-OK");
            Assert.IsFalse(output.Contains("SELFCHECK-FAIL", StringComparison.Ordinal), output);
            // SelfCheck writes only below the scratch directory; the copied script is the only other entry.
            CollectionAssert.AreEquivalent(new[] { ScriptName, "scratch" }, work.GetFileSystemInfos().Select(entry => entry.Name).ToArray());
            Assert.IsTrue(File.Exists(Path.Combine(scratch.FullName, "aq1-lazy.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(scratch.FullName, "aq1-managed.exe")));
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    // ---- Helpers ----

    private static void RequireWindows(string evidence)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException($"Windows-native test ({evidence}): it runs on Windows CI and is reported as failed on other platforms.");
    }

    private static string ScriptPath(string root) => Path.Combine(root, "scripts", "evidence", ScriptName);

    private static DirectoryInfo CreateWorkDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pc-steward-host-evidence-rehearsal-" + Guid.NewGuid().ToString("N")));

    private static async Task<(int ExitCode, string Output, string Error)> RunWindowsPowerShellAsync(string workingDirectory, params string[] arguments)
    {
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.IsTrue(File.Exists(executable), "Windows PowerShell 5.1 is required on Windows.");
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows PowerShell.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(180));
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
