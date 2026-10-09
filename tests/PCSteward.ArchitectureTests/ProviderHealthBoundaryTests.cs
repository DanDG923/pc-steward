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

    [TestMethod]
    public void ChocolateyHealthPinsTheClientAndRevalidatesStrictLayoutBeforeLaunch()
    {
        string probe = Read("src", "PCSteward.Provider.Chocolatey", "ChocolateyProviderHealthProbe.cs");
        StringAssert.Contains(probe, "client.Discover(requireCallerCannotModifyLayout: true)");
        StringAssert.Contains(probe, "client.ValidateUnchanged(discovery.Client, requireCallerCannotModifyLayout: true)");
        StringAssert.Contains(probe, "new WindowsChocolateyProcess(client).RunAsync(VersionArguments");
        string native = Read("src", "PCSteward.Provider.Chocolatey", "ChocolateyProvider.cs");
        StringAssert.Contains(native, "executable = healthClient;");
        StringAssert.Contains(native, "MachineTrustedChocolatey.ValidateUnchanged(executable, requireCallerCannotModifyLayout: true)");
        StringAssert.Contains(native, "ChocolateyClientIdentity current = ResolveExecutable(requireCallerCannotModifyLayout)");
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
