namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Task 50 B-exec signing diagnosis; FR-UPD-15, TM-03/TM-13, ADR-022. The Windows CI job owner-only-signing-diagnostic is
/// informational: it never gates, reads only, mirrors the installer's signing call and certificate parameters, keeps to a
/// disposable GitHub-hosted runner and removes what it creates. These checks keep those properties from drifting.
/// </summary>
[TestClass]
public sealed class OwnerOnlySigningDiagnosticCiTests
{
    private const string Script = "Test-OwnerOnlySigningDiagnosticCI.ps1";

    [TestMethod]
    public void SigningDiagnosticJobIsInformationalReadOnlyAndPinnedToTheOwnersCandidateSource()
    {
        string workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        int job = workflow.IndexOf("  owner-only-signing-diagnostic:\n", StringComparison.Ordinal);
        Assert.IsTrue(job > 0, "the informational signing-diagnostic job is missing");
        string section = workflow[job..];
        StringAssert.Contains(section, "runs-on: windows-latest");
        StringAssert.Contains(section, "continue-on-error: true");
        StringAssert.Matches(section, new System.Text.RegularExpressions.Regex(@"permissions:\s+contents: read"));
        Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Count(section, "persist-credentials: false"), "both checkouts drop credentials");
        StringAssert.Contains(section, "ref: febf1ac95c207aadf0f3efe742b854d81f903037");
        StringAssert.Contains(section, "./owner-candidate-source/scripts/Test-OwnerOnlyCandidateBuilderCI.ps1");
        StringAssert.Contains(section, "powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/" + Script);
        Assert.IsFalse(section.Contains("secrets.", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NativeHelperSigningDiagnosticJobIsInformationalReadOnlyAndBuildsThisCommit()
    {
        // ADR-033 redeploy pre-flight: the same diagnostic on this commit's candidate, whose helper is the native AOT image.
        string workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        int job = workflow.IndexOf("  native-helper-signing-diagnostic:\n", StringComparison.Ordinal);
        Assert.IsTrue(job > 0, "the native-helper signing-diagnostic job is missing");
        int next = workflow.IndexOf("\n  owner-only-signing-diagnostic:\n", job, StringComparison.Ordinal);
        Assert.IsTrue(next > job, "the native-helper job must come before owner-only-signing-diagnostic");
        string section = workflow[job..next];
        StringAssert.Contains(section, "runs-on: windows-latest");
        StringAssert.Contains(section, "continue-on-error: true");
        StringAssert.Matches(section, new System.Text.RegularExpressions.Regex(@"permissions:\s+contents: read"));
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Count(section, @"uses: actions/checkout@v\d+\n"), "only this commit is checked out");
        StringAssert.Contains(section, "persist-credentials: false");
        StringAssert.Contains(section, "fetch-depth: 0");
        StringAssert.Contains(section, "global-json-file: global.json");
        StringAssert.Contains(section, "pwsh -NoProfile -File ./scripts/Test-OwnerOnlyCandidateBuilderCI.ps1");
        StringAssert.Contains(section, "powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/" + Script);
        Assert.IsFalse(section.Contains("ref:", StringComparison.Ordinal), "no other source commit");
        Assert.IsFalse(section.Contains("secrets.", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SigningDiagnosticMirrorsTheInstallerAndCleansUp()
    {
        string root = FindRepositoryRoot();
        string diagnostic = File.ReadAllText(Path.Combine(root, "scripts", Script));
        string installer = File.ReadAllText(Path.Combine(root, "scripts", "Install-OwnerOnlyProtectedTestDeployment.ps1"));
        // The same certificate parameters and signing call as the installer.
        const string Parameters = "-Type CodeSigningCert -Subject $subject -CertStoreLocation 'Cert:\\CurrentUser\\My' -KeyExportPolicy NonExportable -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(14)";
        StringAssert.Contains(installer, "New-SelfSignedCertificate " + Parameters);
        StringAssert.Contains(diagnostic, "New-SelfSignedCertificate " + Parameters);
        StringAssert.Contains(installer, "Set-AuthenticodeSignature -FilePath $path -Certificate $certificate -HashAlgorithm SHA256");
        StringAssert.Contains(diagnostic, "Set-AuthenticodeSignature -FilePath $Path -Certificate $Certificate -HashAlgorithm SHA256");
        // The returned object's fields, which the installer's check reads, are recorded.
        foreach (string field in new[] { "$set.Status", "$set.StatusMessage", "$set.SignerCertificate.Thumbprint" }) StringAssert.Contains(diagnostic, field);
        // Disposable elevated GitHub-hosted runner only; no CurrentUser Root (headless UI prompt); everything created is removed.
        StringAssert.Contains(diagnostic, "$env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'");
        Assert.IsFalse(diagnostic.Contains("@('Root', 'CurrentUser')", StringComparison.Ordinal));
        Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Count(diagnostic, System.Text.RegularExpressions.Regex.Escape("@(@('Root', 'LocalMachine'), @('TrustedPublisher', 'LocalMachine'), @('TrustedPublisher', 'CurrentUser'))")), "trusted stores are the stores cleaned");
        StringAssert.Contains(diagnostic, "-DeleteKey");
        StringAssert.Contains(diagnostic, "foreach ($directory in @($protectedRoot, $work))");
        // The candidate is read, never signed in place.
        StringAssert.Contains(diagnostic, "throw \"A diagnostic source changed: $key\"");
        foreach (string forbidden in new[] { "Invoke-Expression", "-Verb", "Invoke-WebRequest", "Start-Process" }) Assert.IsFalse(diagnostic.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
