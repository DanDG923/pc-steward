using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// ADR-032 revision 2 §4, TM-24, TM-25: PC Steward's own update check lives in <c>PCSteward.SelfUpdate</c>, which no host that
/// runs unattended or elevated references; inside it only <c>GitHubReleaseClient.cs</c> touches the network, nothing touches
/// files, processes, the registry or the environment, and every address is built from compiled parts. The run and artefact
/// rules the elevated helper repeats live in <c>PCSteward.Execution</c> and are pure.
/// </summary>
[TestClass]
public sealed class SelfUpdateBoundaryTests
{
    private static readonly string[] HostsWithoutSelfUpdate =
        ["PCSteward.ScheduledHost", "PCSteward.ElevatedHelper", "PCSteward.Execution", "PCSteward.Application", "PCSteward.Domain"];
    private static readonly string[] SelfUpdateProjectReferences = ["../PCSteward.Execution/PCSteward.Execution.csproj"];
    private static readonly string[] GitHubHosts = ["api.github.com", "github.com"];

    [TestMethod]
    public void SelfUpdateIsReferencedByNoHostThatRunsUnattendedOrElevated()
    {
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        foreach (string project in HostsWithoutSelfUpdate)
        {
            IEnumerable<string?> references = XDocument.Load(Path.Combine(src, project, project + ".csproj"))
                .Descendants("ProjectReference").Select(element => (string?)element.Attribute("Include"));
            Assert.IsFalse(references.Any(reference => reference?.Contains("SelfUpdate", StringComparison.Ordinal) == true), project);
        }

        string[] selfUpdateReferences = [.. XDocument.Load(Path.Combine(src, "PCSteward.SelfUpdate", "PCSteward.SelfUpdate.csproj"))
            .Descendants("ProjectReference").Select(element => (string?)element.Attribute("Include") ?? "")];
        CollectionAssert.AreEqual(SelfUpdateProjectReferences, selfUpdateReferences);
        Assert.IsFalse(XDocument.Load(Path.Combine(src, "PCSteward.SelfUpdate", "PCSteward.SelfUpdate.csproj")).Descendants("PackageReference").Any());
    }

    [TestMethod]
    public void OnlyTheClientUsesTheNetworkAndNothingTouchesTheMachine()
    {
        string selfUpdate = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.SelfUpdate");
        string[] files = [.. Directory.GetFiles(selfUpdate, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                           !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))];
        Assert.IsNotEmpty(files);
        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            foreach (string forbidden in new[] { "Process", "Registry", "File.", "FileStream", "Directory.", "Environment.", "PowerShell", "cmd.exe", "DllImport", "LibraryImport", "unsafe" })
                Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), Path.GetFileName(file) + ": " + forbidden);
            if (!string.Equals(Path.GetFileName(file), "GitHubReleaseClient.cs", StringComparison.Ordinal))
                Assert.IsFalse(Regex.IsMatch(source, @"\bHttp\w*(Client|Handler|Request|Response)"), Path.GetFileName(file) + " must not use the network");
        }
    }

    [TestMethod]
    public void TheClientBuildsAddressesFromCompiledPartsAndSendsNoCredentials()
    {
        string source = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.SelfUpdate", "GitHubReleaseClient.cs"));
        // Every literal https address is one of the compiled GitHub hosts.
        foreach (Match match in Regex.Matches(source, "\"https://([^/\"]+)/"))
            CollectionAssert.Contains(GitHubHosts, match.Groups[1].Value, match.Value);
        Assert.IsFalse(Regex.IsMatch(source, @"\bnew\s+Uri\s*\(\s*[a-z]\w*\.(Url|Href|BrowserDownloadUrl|Location)\b"), "no address is taken from a response");
        foreach (string forbidden in new[] { "Authorization", "Bearer", "PreAuthenticate = true", "UseDefaultCredentials = true", "AllowAutoRedirect = true", "UseCookies = true", "Credentials = new" })
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
        StringAssert.Contains(source, "AllowAutoRedirect = false");
        StringAssert.Contains(source, "UseCookies = false");
    }

    [TestMethod]
    public void OnlyTheTwoGitHubClientsUseTheNetwork()
    {
        // ADR-032 revision 2 §4 and §6 step 3: the desktop's update check and the helper's own confirmation are the only
        // network code in PC Steward.
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string[] allowed = [Path.Combine(src, "PCSteward.SelfUpdate", "GitHubReleaseClient.cs"), Path.Combine(src, "PCSteward.Execution", "HelperGitHubConfirmation.cs")];
        foreach (string file in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                           !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            bool network = Regex.IsMatch(File.ReadAllText(file), @"\b(HttpClient|HttpMessageHandler|SocketsHttpHandler|HttpWebRequest|WebClient|TcpClient|UdpClient|System\.Net\.Sockets)\b");
            Assert.AreEqual(allowed.Contains(file, StringComparer.Ordinal), network, file);
        }
    }

    [TestMethod]
    public void TheHelperConfirmationIsConstrained()
    {
        string source = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution", "HelperGitHubConfirmation.cs"));
        foreach (string required in new[]
        {
            "UseProxy = false", "UseCookies = false", "Credentials = null", "AllowAutoRedirect = false", "PreAuthenticate = false",
            "X509ChainTrustMode.CustomRootTrust", "StoreLocation.LocalMachine", "X509RevocationMode.Online", "ReleaseRunEvidence.CheckRun",
            "ReleaseRunEvidence.CheckArtifact", "internal const string Host = \"api.github.com\";",
        })
            StringAssert.Contains(source, required);
        foreach (string forbidden in new[] { "StoreLocation.CurrentUser", "Authorization", "Environment.", "Registry", "Headers.Location", "AllowAutoRedirect = true", "DangerousAcceptAnyServerCertificateValidator" })
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
        // The one https string literal is joined to the compiled API host.
        Assert.AreEqual(1, Regex.Count(source, "\"https://"));
        StringAssert.Contains(source, "new(\"https://\" + Host + \"/repos/\" + ReleaseCheckPolicy.Repository + \"/\")");
    }

    [TestMethod]
    public void TheSharedRunRulesArePure()
    {
        string source = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution", "ReleaseRunEvidence.cs"));
        foreach (string forbidden in new[] { "Http", "Socket", "Process", "Registry", "File.", "Directory.", "Environment.", "DllImport" })
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
        StringAssert.Contains(source, "AllowDuplicateProperties = false");
        StringAssert.Contains(source, "internal const long RepositoryId = 1331300752;");
        StringAssert.Contains(source, "\".github/workflows/release-payload.yml\"");
    }

    /// <summary>
    /// ADR-032 revision 2 §7–§8 CI evidence: the MSI CI job runs the real finish copy from a work folder the production host
    /// laid out, for a mismatching and a matching job and from the wrong path, and the desktop's read of each as a standard user.
    /// </summary>
    [TestMethod]
    public void TheMsiCiJobRunsTheRealFinishCopy()
    {
        string script = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "scripts", "Test-PCStewardMsiCI.ps1"));
        foreach (string check in new[]
                 {
                     "self-update-finish-uninstalls-an-install-unlike-the-expected-tree", "self-update-desktop-reads-the-uninstalled-job-as-failed",
                     "self-update-finish-refuses-outside-the-work-folder", "self-update-finish-refuses-a-per-user-registration",
                     "self-update-finish-installs-and-verifies-the-upgrade",
                     "self-update-desktop-reads-the-installed-job-as-updated", "self-update-uninstalls-cleanly",
                     "self-update-private-folder-hidden-from-standard-users", "self-update-finish-refuses-without-the-user-installs-policy",
                     "release-install-refuses-without-the-user-installs-policy",
                 })
            StringAssert.Contains(script, "Set-Check '" + check + "'");
        // Mythos review S1: the per-user transform experiment runs, records its outcome, and puts the logging policy back.
        StringAssert.Contains(script, "$evidence.perUserTransformExperiment = $experiment");
        StringAssert.Contains(script, "Invoke-Msi @('/ju', ");
        StringAssert.Contains(script, "Remove-ItemProperty -LiteralPath $loggingPolicy -Name 'Logging'");
        StringAssert.Contains(script, "Join-Path $programFiles \"$Leaf\\bin\\PCSteward.ElevatedHelper.exe\"");
        StringAssert.Contains(script, "--self-update-prepare $msis['0.1.2'].Path $Mode");
        StringAssert.Contains(script, "$decided = Invoke-AsStandardUser 'SelfUpdateDecide' $name $password");
        string prepare = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "tests", "PCSteward.InstallCheck", "SelfUpdateFinishCheck.cs"));
        // The job is laid out through the production host and read back through the production desktop pieces.
        foreach (string call in new[] { "WindowsSelfUpdateHost.TryCreate()", "host.CreateProtectedFolder(work)", "host.CreatePrivateFolder(privateFolder)", "host.CopyInput(msiPath", "host.ReadImage(", "SelfUpdateDesktopFiles.TryCreate()", "files.Evaluate(" })
            StringAssert.Contains(prepare, call);
    }

    /// <summary>
    /// Self-updater security review R1/R2: the owner's own registry (HKCU), which any same-user program can write, never decides
    /// what msiexec installs or removes. The helper and the release install script read installed products from the
    /// administrator-only machine registration only, refuse to run msiexec while a per-user registration exists, and remove only
    /// the MSI's own product code.
    /// </summary>
    [TestMethod]
    public void TheOwnersRegistryNeverSteersMsiexec()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        string host = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Execution", "WindowsSelfUpdateHost.cs"));
        Assert.IsFalse(host.Contains("MsiEnumRelatedProducts", StringComparison.Ordinal), "the merged machine-plus-user product list");
        Assert.IsFalse(Regex.IsMatch(host, @"MsiGetProductInfo\b|MsiGetProductInfoW"), "the merged product information");
        StringAssert.Contains(host, "Registry.LocalMachine.OpenSubKey(MsiProductRegistration.MachineUpgradeCodeKey, writable: false)");
        StringAssert.Contains(host, "NativeMethods.MsiGetProductInfoEx(productCode, null, NativeMethods.MsiInstallContextMachine, \"VersionString\"");
        Assert.AreEqual(1, Regex.Count(host, @"Registry\.CurrentUser"), "HKCU is only checked for registrations");

        string session = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Execution", "SelfUpdateSession.cs"));
        int extract = session.IndexOf("[\"/a\", msiPath", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, extract);
        Assert.IsLessThan(extract, session.IndexOf("Require(!host.HasPerUserRegistration(productCode), SelfUpdateSessionRefusal.PerUserRegistration);", StringComparison.Ordinal));
        // Mythos review S1 (confirmed on Windows CI): no msiexec runs unless the DisableUserInstalls machine policy is set.
        int policy = session.IndexOf("Require(host.UserInstallsIgnored(), SelfUpdateSessionRefusal.UserInstallsAllowed);", StringComparison.Ordinal);
        Assert.IsTrue(policy > 0 && policy < extract, "the session checks the policy before the administrative unpack");
        StringAssert.Contains(host, "RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)");
        StringAssert.Contains(host, "MsiProductRegistration.IgnoresUserInstalls(policy.GetValue(MsiProductRegistration.DisableUserInstallsValue))");
        string finish = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Execution", "SelfUpdateFinish.cs"));
        Assert.IsLessThan(finish.IndexOf("[\"/i\", msiPath", StringComparison.Ordinal), finish.IndexOf("if (host.HasPerUserRegistration(job.ProductCode))", StringComparison.Ordinal));
        StringAssert.Contains(finish, "[\"/x\", job.ProductCode,");
        int finishPolicy = finish.IndexOf("if (!host.UserInstallsIgnored())", StringComparison.Ordinal);
        Assert.IsTrue(finishPolicy > 0 && finishPolicy < finish.IndexOf("[\"/i\", msiPath", StringComparison.Ordinal), "the finish copy checks the policy before installing");
        int removePolicy = finish.IndexOf("if (!host.UserInstallsIgnored() || host.HasPerUserRegistration(job.ProductCode))", StringComparison.Ordinal);
        Assert.IsTrue(removePolicy > 0 && removePolicy < finish.IndexOf("[\"/x\", job.ProductCode,", StringComparison.Ordinal), "and before uninstalling");

        string install = File.ReadAllText(Path.Combine(root, "scripts", "Install-PCStewardRelease.ps1"));
        Assert.IsFalse(install.Contains("'RelatedProducts'", StringComparison.Ordinal), "the merged machine-plus-user product list");
        Assert.IsLessThan(install.IndexOf("Invoke-Msiexec @('/i'", StringComparison.Ordinal), install.IndexOf("if (Test-PerUserRegistration $productCode) { throw", StringComparison.Ordinal));
        StringAssert.Contains(install, "$null = Invoke-Msiexec @('/x', $productCode) 'msiexec-uninstall.log'");
        int installPolicy = install.IndexOf("if (!(Test-UserInstallsIgnored)) { throw", StringComparison.Ordinal);
        Assert.IsTrue(installPolicy > 0 && installPolicy < install.IndexOf("Invoke-Msiexec @('/i'", StringComparison.Ordinal), "the install script checks the policy before msiexec");
        StringAssert.Contains(install, "$perUser = (Test-PerUserRegistration $productCode) -or !(Test-UserInstallsIgnored)");
        Assert.IsFalse(Regex.IsMatch(install, @"Invoke-Msiexec @\('/x', \$product\."), "never another product's code");
    }

    /// <summary>
    /// ADR-032 revision 2 §12 step 3: the publishing workflow starts only by hand, holds only the two permissions it needs, uses
    /// no third-party action, and creates a draft only after the run, the artefact digest and the record agree.
    /// </summary>
    [TestMethod]
    public void ThePublishingWorkflowOnlyDraftsAReleaseFromACheckedRun()
    {
        string workflow = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), ".github", "workflows", "publish-release.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        StringAssert.Contains(workflow, "on:\n  workflow_dispatch:\n");
        Assert.IsFalse(Regex.IsMatch(workflow, @"^\s*(push|pull_request\w*|schedule|workflow_run|release):", RegexOptions.Multiline), "hand-started only");
        StringAssert.Contains(workflow, "permissions:\n  contents: write\n  actions: read\n\njobs:");
        Assert.AreEqual(1, Regex.Count(workflow, @"^permissions:", RegexOptions.Multiline), "no job-level permissions");
        Assert.IsFalse(Regex.IsMatch(workflow, @"^\s*-?\s*uses:", RegexOptions.Multiline), "no third-party action");
        foreach (string required in new[]
        {
            "[[ \"$RUN_ID\" =~ ^[1-9][0-9]{0,19}$ ]]",
            ".path == \".github/workflows/release-payload.yml\" and .event == \"workflow_dispatch\"",
            ".status == \"completed\" and .conclusion == \"success\" and (.head_branch == \"phase/4-beta\" or .head_branch == \"main\")",
            "[[ \"$actual\" == \"$digest\" ]]",
            "[[ \"$entries\" == \"release-check.json\" ]]",
            ".runId == $id and .sourceCommit == $commit",
            "--draft --target \"$commit\"",
        })
            StringAssert.Contains(workflow, required);
        // The run id reaches the shell only through the environment, never by template expansion inside the script.
        Assert.IsFalse(workflow.Contains("${{ inputs.run_id }}\"", StringComparison.Ordinal) || Regex.IsMatch(workflow, @"run: \|[\s\S]*\$\{\{"), "no expression inside the script");
        foreach (string forbidden in new[] { "gh release upload", "--clobber", "gh release edit", "secrets.", "--latest" })
            Assert.IsFalse(workflow.Contains(forbidden, StringComparison.Ordinal), forbidden);
    }

    /// <summary>
    /// ADR-032 revision 2 §5–§8 (desktop side): the façade and its pieces never touch the network, write only inside the
    /// per-user self-update folder, and launch only through the guarded self-update transport.
    /// </summary>
    [TestMethod]
    public void TheDesktopSidePiecesStayInTheirFolderAndOffTheNetwork()
    {
        string execution = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution");
        foreach (string file in new[] { "PcStewardSelfUpdate.cs", "SelfUpdateDesktop.cs", "SelfUpdateDesktopFiles.cs", "SelfUpdateDesktopTransport.cs" })
        {
            string source = File.ReadAllText(Path.Combine(execution, file));
            Assert.IsFalse(Regex.IsMatch(source, @"\bHttp\w*(Client|Handler|Request|Response)|Socket"), file + " must not use the network");
            Assert.IsFalse(source.Contains("Registry", StringComparison.Ordinal), file);
        }
        string pure = File.ReadAllText(Path.Combine(execution, "SelfUpdateDesktop.cs"));
        foreach (string forbidden in new[] { "File.", "FileStream", "Directory.", "Process.", "System.Diagnostics", "Environment.", "DllImport" })
            Assert.IsFalse(pure.Contains(forbidden, StringComparison.Ordinal), "SelfUpdateDesktop.cs: " + forbidden);
        string files = File.ReadAllText(Path.Combine(execution, "SelfUpdateDesktopFiles.cs"));
        StringAssert.Contains(files, "Path.Combine(Path.GetFullPath(local), \"PCSteward\", \"downloads\", SelfUpdateDesktop.FolderName)");
        // Every write, move and delete goes through a path checked to be directly inside the folder.
        foreach (Match call in Regex.Matches(files, @"File\.(Move|Delete)\("))
        {
            string before = files[..call.Index];
            Assert.IsTrue(before.LastIndexOf("IsInFolder(", StringComparison.Ordinal) > before.LastIndexOf("internal ", StringComparison.Ordinal), "unchecked " + call.Value);
        }
        Assert.IsFalse(Regex.IsMatch(files, @"File\.(WriteAll|Copy|Replace|Create\()|Directory\.Delete"), "no other file writes");
        string facade = File.ReadAllText(Path.Combine(execution, "PcStewardSelfUpdate.cs"));
        Assert.IsFalse(Regex.IsMatch(facade, @"Process\.|runas|File\.(Move|Delete|Write)"), "the façade launches and writes only through its pieces");
    }

    /// <summary>
    /// ADR-032 revision 2 §8 (FR-UPD-04/15): while a PC Steward update may have left an install unlike a signed release, the one
    /// package review and confirmation site refuses, after waiting for this launch's decision of the recorded job.
    /// </summary>
    [TestMethod]
    public void AFailedSelfUpdateThatBlocksAlsoRefusesPackageUpdates()
    {
        string desktop = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Desktop");
        string job = File.ReadAllText(Path.Combine(desktop, "MainWindow.UpdateJob.cs"));
        int start = job.IndexOf("private async Task ReviewAndExecuteAsync(", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0);
        int wait = job.IndexOf("try { await statusLoad; }", start, StringComparison.Ordinal);
        int refuse = job.IndexOf("if (selfUpdateStatus is { BlocksUpdates: true })", start, StringComparison.Ordinal);
        int prepare = job.IndexOf("preparingReview = true;", start, StringComparison.Ordinal);
        Assert.IsTrue(wait > start && refuse > wait && prepare > refuse, "the block is checked before any review is prepared");
        StringAssert.Contains(File.ReadAllText(Path.Combine(desktop, "MainWindow.cs")), "selfUpdateStatusLoad = LoadSelfUpdateStatusAsync();");
    }
}
