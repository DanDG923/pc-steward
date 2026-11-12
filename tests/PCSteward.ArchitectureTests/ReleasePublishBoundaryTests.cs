using System.Text.RegularExpressions;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// ADR-032 revision 2 §12 step 4 (owner decision 2026-11-12): the owner's one-paste release publisher. It sends its
/// fine-grained token only to this repository on GitHub's API and upload hosts, keeps the token DPAPI-encrypted, never
/// releases a commit other than the one the owner pinned, checks the draft before any YubiKey touch, signs only through
/// New-PCStewardRelease.ps1, and publishes only after GitHub's digest of the upload equals the signed MSI.
/// </summary>
[TestClass]
public sealed class ReleasePublishBoundaryTests
{
    [TestMethod]
    public void TheTokenGoesOnlyToThisRepositoryAndStaysEncrypted()
    {
        string publish = Script("Publish-PCStewardRelease.ps1");
        StringAssert.Contains(publish, "$api = \"https://api.github.com/repos/$repository\"");
        StringAssert.Contains(publish, "$uploads = \"https://uploads.github.com/repos/$repository\"");
        StringAssert.Contains(publish, "if (!$Uri.StartsWith(\"$api/\", [StringComparison]::Ordinal) -and !$Uri.StartsWith(\"$uploads/\", [StringComparison]::Ordinal)) { throw \"Refusing to send the token to $Uri.\" }");
        // Every request that carries the token is checked first; the storage redirect is followed without it.
        int headerUses = Regex.Count(publish, @"Get-PCStewardApiHeaders\)");
        int checks = Regex.Count(publish, @"Assert-PCStewardRepositoryUri \$");
        Assert.IsGreaterThanOrEqualTo(headerUses, checks, "a request carries the token without the repository check");
        StringAssert.Contains(publish, "$request.AllowAutoRedirect = $false");
        StringAssert.Contains(publish, "$download.AllowAutoRedirect = $false; $download.UserAgent = 'pc-steward-release-script'");
        Assert.IsFalse(Regex.IsMatch(publish, @"\$download\.Headers\.Add"), "the redirected download must not carry the token");
        // Encrypted for this Windows account (DPAPI), fine-grained tokens only, never printed.
        StringAssert.Contains(publish, "[IO.File]::WriteAllText($tokenFile, ($Secure | ConvertFrom-SecureString), [Text.UTF8Encoding]::new($false))");
        StringAssert.Contains(publish, "if ($plain -cnotmatch '\\Agithub_pat_[A-Za-z0-9_]{20,255}\\z')");
        StringAssert.Contains(publish, "[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)");
        Assert.IsFalse(Regex.IsMatch(publish, @"Write-(Output|Host)[^\n]*\$plain"), "the token is printed");
        Assert.IsFalse(publish.Contains("ConvertFrom-SecureString -Key", StringComparison.Ordinal), "the token must use DPAPI, not a script key");
        StringAssert.Contains(publish, "throw 'Run this script from a normal (not elevated) PowerShell window.'");
    }

    [TestMethod]
    public void OnlyThePinnedCommitIsSignedAndPublished()
    {
        string publish = Script("Publish-PCStewardRelease.ps1");
        StringAssert.Contains(publish, "if ($Commit -notmatch '\\A[0-9a-f]{40}\\z')");
        StringAssert.Contains(publish, "if ($LASTEXITCODE -ne 0 -or $localCommit -cne $Commit)");
        StringAssert.Contains(publish, "if ([string]$run.head_sha -cne $Commit) { throw \"Run $RunId built $($run.head_sha), not the expected commit $Commit.\" }");
        StringAssert.Contains(publish, "if ([string]$release.target_commitish -cne $Commit)");
        StringAssert.Contains(publish, "if ($ref.object.type -cne 'commit' -or [string]$ref.object.sha -cne $Commit)");
        // The payload must have GitHub's digest before anything is read from it.
        Assert.IsLessThan(publish.IndexOf("[IO.Compression.ZipFile]::OpenRead($zip)", StringComparison.Ordinal),
            publish.IndexOf("if ('sha256:' + $zipHash.ToLowerInvariant() -cne [string]$payloadArtifact[0].digest)", StringComparison.Ordinal), "the payload is opened before its digest is checked");
        // The draft is checked before the YubiKey is asked for anything, and signing is New-PCStewardRelease.ps1's job.
        Assert.IsLessThan(publish.IndexOf("'New-PCStewardRelease.ps1'", StringComparison.Ordinal),
            publish.IndexOf("$draft = Get-PCStewardDraft $tag", StringComparison.Ordinal), "the draft is checked after signing");
        Assert.AreEqual(1, Regex.Count(publish, "New-PCStewardRelease\\.ps1'"));
        Assert.IsFalse(Regex.IsMatch(publish, "Set-AuthenticodeSignature|signtool|Cert:\\\\", RegexOptions.IgnoreCase), "the publisher signs by itself");
        StringAssert.Contains(publish, "[string]$release.signedWith -cne 'primary'");
    }

    [TestMethod]
    public void TheReleaseIsPublishedOnlyAfterGitHubHoldsTheSignedMsi()
    {
        string publish = Script("Publish-PCStewardRelease.ps1");
        int digestCheck = publish.IndexOf("if ([string]$onDraft[0].digest -cne $msiDigest)", StringComparison.Ordinal);
        int publishCall = publish.IndexOf("@{ draft = $false; make_latest = 'true' }", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, digestCheck);
        Assert.IsLessThan(publishCall, digestCheck, "the draft is published before GitHub's digest of the MSI is checked");
        Assert.AreEqual(1, Regex.Count(publish, "draft = \\$false"));
        // The tag is checked (absent, or a plain tag at the commit) in the draft check run right before publishing: GitHub
        // ignores the draft's target once the tag exists.
        StringAssert.Contains(publish, "$tagRef.object.type -cne 'commit' -or [string]$tagRef.object.sha -cne $Commit");
        Assert.IsLessThan(publishCall, publish.LastIndexOf("$draft = Get-PCStewardDraft $tag", StringComparison.Ordinal), "the draft and tag are not re-checked right before publishing");
        Assert.IsGreaterThan(digestCheck, publish.LastIndexOf("$draft = Get-PCStewardDraft $tag", StringComparison.Ordinal));
        // Afterwards: exactly the check file and the MSI, both with the expected digests, immutable.
        StringAssert.Contains(publish, "$names.Count -ne 2");
        StringAssert.Contains(publish, "[string]$checkAsset[0].digest -cne [string]$checkArtifact[0].digest");
        StringAssert.Contains(publish, "if ($published.immutable -ne $true)");
        // Only an MSI of this release name is ever deleted, and only from a draft that passed the checks.
        Assert.AreEqual(1, Regex.Count(publish, "DELETE"));
        StringAssert.Contains(publish, "foreach ($stale in @($draft.assets | Where-Object { $_.name -ceq $msiName }))");
        Assert.IsLessThan(publish.IndexOf("DELETE", StringComparison.Ordinal), publish.IndexOf("if ($release.draft -ne $true)", StringComparison.Ordinal), "an asset is deleted before the draft check");
    }

    private static string Script(string name) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", name)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
