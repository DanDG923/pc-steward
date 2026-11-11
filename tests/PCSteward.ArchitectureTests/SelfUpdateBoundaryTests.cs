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
    public void TheSharedRunRulesArePure()
    {
        string source = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution", "ReleaseRunEvidence.cs"));
        foreach (string forbidden in new[] { "Http", "Socket", "Process", "Registry", "File.", "Directory.", "Environment.", "DllImport" })
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
        StringAssert.Contains(source, "AllowDuplicateProperties = false");
        StringAssert.Contains(source, "internal const long RepositoryId = 1331300752;");
        StringAssert.Contains(source, "\".github/workflows/release-payload.yml\"");
    }
}
