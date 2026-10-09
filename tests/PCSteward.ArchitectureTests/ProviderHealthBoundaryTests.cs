using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

[TestClass]
public sealed class ProviderHealthBoundaryTests
{
    [TestMethod]
    public void HealthContractContainsOnlyDisplayEvidence()
    {
        string contract = Read("src", "PCSteward.Application", "ProviderHealth.cs");
        foreach (string forbidden in new[] { "Executable", "Arguments", "ImmutableUpdateJob", "ApprovedOperationBatch", "SourceConsent", "IsReachable", "ProcessStartInfo" })
            Assert.IsFalse(contract.Contains(forbidden, StringComparison.Ordinal), $"Health exposes {forbidden}.");
        StringAssert.Contains(contract, "IReadOnlyList<string> ConfiguredSources");
        StringAssert.Contains(contract, "DateTimeOffset CheckedAt");
    }

    [TestMethod]
    public void HealthProbesDoNotDependOnExecutionOrSavedConsent()
    {
        foreach (string provider in new[] { "WinGet", "Chocolatey" })
        {
            string name = provider == "WinGet" ? "Winget" : provider;
            string source = Read("src", "PCSteward.Provider." + provider, name + "ProviderHealthProbe.cs");
            foreach (string forbidden in new[] { "ImmutableUpdateJob", "ApprovedOperationBatch", "UpdateJobReview", "SourceConsent", "runas", "--yes", "--accept-source-agreements", "HttpClient", "WebClient", "PowerShell" })
                Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), $"{name} health uses {forbidden}.");
        }
    }

    private static string Read(params string[] components)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return File.ReadAllText(Path.Combine(new[] { current.FullName }.Concat(components).ToArray()));
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
