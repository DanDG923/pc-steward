using System.Text.Json;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

[TestClass]
public sealed class FixtureCatalogMatchingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    [TestMethod]
    public void CanonicalFixtureCatalogControlsAndRelevantEdgesHaveZeroFalsePositiveActionableMappings()
    {
        FixtureCatalog catalog = LoadCatalog();
        Assert.AreEqual(320, catalog.Records.Count, "The checked-in catalog must retain all deterministic records.");
        Assert.AreEqual(5, catalog.MappingCases.Count, "The catalog defines five mandatory mapping controls.");

        PackageMapping?[] controls = catalog.MappingCases.Select(mappingCase => ResolveMapping(catalog.Records.Single(record => record.FixtureId == mappingCase.FixtureId), mappingCase.CandidatePackageId)).ToArray();
        for (int index = 0; index < catalog.MappingCases.Count; index++) Assert.AreEqual(catalog.MappingCases[index].ExpectedActionable, controls[index]?.IsActionable == true, catalog.MappingCases[index].Reason);

        FixtureRecord[] edges = catalog.Records.Where(record => record.FixtureId.StartsWith("EDGE-", StringComparison.Ordinal)).ToArray();
        Assert.AreEqual(20, edges.Length, "All named adversarial controls must be exercised.");
        foreach (FixtureRecord edge in edges)
        {
            PackageMapping? mapping = ResolveMapping(edge, "Contoso.Edge." + edge.FixtureId[5..]);
            bool expectedActionable = string.Equals(edge.ExpectedMapping, "exact", StringComparison.Ordinal);
            Assert.AreEqual(expectedActionable, mapping?.IsActionable == true, $"{edge.FixtureId}: {edge.ExpectedMapping}");
        }

        Assert.AreEqual(0, controls.Where((mapping, index) => !catalog.MappingCases[index].ExpectedActionable && mapping?.IsActionable == true).Count());
    }

    private static PackageMapping? ResolveMapping(FixtureRecord record, string packageId)
    {
        bool exact = record.ExpectedMapping is "exact" or "channel-conflict" or "incomparable-version" or "architecture-conflict";
        string installedVersion = record.ExpectedMapping == "incomparable-version" ? "release-candidate" : record.Version ?? "1.0.0";
        string candidateVersion = record.ExpectedMapping == "incomparable-version" ? "2026-R2" : "99.0.0";
        string productArchitecture = record.ExpectedMapping == "architecture-conflict" ? "x64" : record.Architecture ?? "x64";
        string candidateArchitecture = record.ExpectedMapping == "architecture-conflict" ? "x86" : productArchitecture;
        string productChannel = record.ExpectedMapping == "channel-conflict" ? "Stable" : record.Channel ?? "Stable";
        string candidateChannel = record.ExpectedMapping == "channel-conflict" ? "Preview" : productChannel;
        string? publisher = record.ExpectedMapping is "false-match-reject" or "low-confidence" ? null : "Contoso";
        ProductMatchProfile product = new("fixture:" + record.FixtureId, record.DisplayName ?? "Unknown", exact ? packageId : null, null, null, publisher, installedVersion, record.Scope ?? "Machine", productArchitecture, productChannel, [record.DisplayName ?? string.Empty], publisher is null ? null : [publisher], exact ? "winget" : null);
        PackageMatchCandidate candidate = new("winget", packageId, record.DisplayName ?? "Unknown", null, null, record.ExpectedMapping == "false-match-reject" ? "Fabrikam" : publisher, candidateVersion, record.Scope ?? "Machine", candidateArchitecture, candidateChannel, true, ProductAliases: [record.DisplayName ?? string.Empty], PublisherAliases: publisher is null ? ["Fabrikam"] : [publisher]);
        return ProductPackageMatching.Resolve(product, [candidate]).Mapping;
    }

    private static FixtureCatalog LoadCatalog()
    {
        string path = Path.Combine(FindRepositoryRoot(), "spikes", "performance-fixtures", "fixtures", "provider-records.json");
        return JsonSerializer.Deserialize<FixtureCatalog>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("Fixture catalog could not be read.");
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException("PCSteward.slnx was not found above the test output directory.");
    }

    private sealed record FixtureCatalog(List<FixtureRecord> Records, List<MappingCase> MappingCases);
    private sealed record FixtureRecord(string FixtureId, string? DisplayName, string? Version, string? Scope, string? Architecture, string? Channel, string ExpectedMapping);
    private sealed record MappingCase(string FixtureId, string CandidatePackageId, bool ExpectedActionable, string Reason);
}
