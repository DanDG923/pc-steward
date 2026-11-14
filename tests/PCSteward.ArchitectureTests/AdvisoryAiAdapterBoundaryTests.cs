using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-AI-03/04 (ADR-028 Q1/F11/Q8, Task 67 stage 3): the advisory AI adapters are narrow. The project references only Application and
/// only the official OpenAI package. Its only native calls are CredWriteW, CredReadW, CredDeleteW and CredFree, for the one target
/// the Application port compiles; it never lists, searches or renames credentials. The transport has no retry, follows no redirect,
/// keeps certificate validation, has no logging or tracing and reads no environment. Only the desktop app may reference it.
/// </summary>
[TestClass]
public sealed class AdvisoryAiAdapterBoundaryTests
{
    private const string Project = "PCSteward.AdvisoryAi.Windows";
    private static readonly string[] AllowedReferences = ["../PCSteward.Application/PCSteward.Application.csproj"];
    private static readonly string[] AllowedPackages = ["OpenAI"];
    private static readonly string[] EntryPoints = ["CredWriteW", "CredReadW", "CredDeleteW", "CredFree"];
    private static readonly string[] MayReference = ["PCSteward.Desktop"];

    [TestMethod]
    public void TheAdapterProjectReferencesOnlyApplicationAndTheOpenAiPackageAndOnlyTheDesktopMayUseIt()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root, "src", Project, Project + ".csproj"));
        CollectionAssert.AreEqual(AllowedReferences, project.Descendants("ProjectReference").Select(reference => (string?)reference.Attribute("Include")).ToArray());
        CollectionAssert.AreEqual(AllowedPackages, project.Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include")).ToArray());

        foreach (string directory in Directory.EnumerateDirectories(Path.Combine(root, "src")))
        {
            string name = Path.GetFileName(directory);
            if (name == Project || MayReference.Contains(name)) continue;
            string projectFile = Path.Combine(directory, name + ".csproj");
            if (File.Exists(projectFile)) Assert.IsFalse(File.ReadAllText(projectFile).Contains(Project, StringComparison.Ordinal), $"{name} references the AI adapters");
        }
    }

    [TestMethod]
    public void TheOnlyNativeCallsAreTheFourCredentialCallsForTheOneTarget()
    {
        string code = Code();
        CollectionAssert.AreEquivalent(EntryPoints, Regex.Matches(code, @"static\s+extern\s+\w+\s+(\w+)\s*\(").Select(match => match.Groups[1].Value).ToArray());
        Assert.AreEqual(4, Regex.Count(code, @"\[DllImport\(""advapi32\.dll"""));
        Assert.AreEqual(4, Regex.Count(code, @"\[DllImport\("));
        foreach (string forbidden in new[] { "CredEnumerate", "CredFindBestCredential", "CredRename", "CredGetTargetInfo", "CredMarshal", "CredProtect", "PCSteward/", "ComImport", "LibraryImport" })
            Assert.IsFalse(code.Contains(forbidden, StringComparison.Ordinal), $"the adapter uses {forbidden}");
        // ADR-028 addendum A1: the one target is the registered provider's compiled target, copied once in the constructor.
        Assert.AreEqual(4, Regex.Count(code, @"api\.(Write|Read|Delete)\("), "four credential calls in the store");
        Assert.AreEqual(4, Regex.Count(code, @"api\.(Write|Read|Delete)\(target\b"), "every call names the store's one target");
        Assert.AreEqual(1, Regex.Count(code, @"\btarget = "), "the target is set once");
        StringAssert.Contains(code, "target = provider.CredentialTarget;");
        StringAssert.Contains(code, "is not AdvisoryAiProvider registered || !ReferenceEquals(registered, provider)");
        StringAssert.Contains(code, "private const int TypeGeneric = 1;");
        StringAssert.Contains(code, "private const int PersistLocalMachine = 2;");
    }

    [TestMethod]
    public void TheTransportHasOneAttemptNoRedirectNoLoggingAndKeepsCertificateValidation()
    {
        string code = Code();
        StringAssert.Contains(code, "RetryPolicy = new ClientRetryPolicy(maxRetries: 0)");
        StringAssert.Contains(code, "AllowAutoRedirect = false");
        StringAssert.Contains(code, "ActivityHeadersPropagator = null");
        StringAssert.Contains(code, "EnableDistributedTracing = false");
        StringAssert.Contains(code, "EnableLogging = false, EnableMessageLogging = false, EnableMessageContentLogging = false");
        StringAssert.Contains(code, "BufferResponse = false");
        StringAssert.Contains(code, "Endpoint = SdkEndpoint");
        foreach (string forbidden in new[]
        {
            "ServerCertificateCustomValidationCallback", "RemoteCertificateValidationCallback", "DangerousAcceptAnyServerCertificateValidator",
            "Environment.", "OPENAI_", "OrganizationId", "ProjectId", "UserAgentApplicationId", "Console.", "Debug.", "Trace.", "ILogger", "File.", "Registry"
        })
            Assert.IsFalse(code.Contains(forbidden, StringComparison.Ordinal), $"the adapter uses {forbidden}");
    }

    private static string Code() => string.Join(Environment.NewLine,
        Directory.EnumerateFiles(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", Project), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(File.ReadAllText));
}
