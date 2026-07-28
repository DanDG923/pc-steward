using PCSteward.Inventory.Windows;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class RegistryInventoryProviderTests
{
    [TestMethod]
    public void PreservesViewsBoundsFieldsAndContinuesAfterMalformedEntries()
    {
        FakeRegistry registry = new();
        registry.Entries[RegistryViewKind.Machine64] = [Entry("widget", "Widget", "1.0", "Contoso"), Entry("missing", null, null, null), Entry("large", new string('a', 2000), "1", "P")];
        registry.Entries[RegistryViewKind.Machine32] = [Entry("widget", "Widget 32", "2.0", "Contoso")];
        registry.Entries[RegistryViewKind.CurrentUser] = [Entry("user", "User Tool", "3.0", "Fabrikam")];

        RegistryInventoryResult result = new RegistryInventoryProvider(registry).ReadAll();

        Assert.AreEqual(5, result.Records.Count);
        Assert.AreEqual("x64", result.Records.Single(record => record.KeyName == "widget" && record.Architecture == "x64").Architecture);
        Assert.AreEqual("x86", result.Records.Single(record => record.KeyName == "widget" && record.DisplayName == "Widget 32").Architecture);
        Assert.AreEqual("User", result.Records.Single(record => record.KeyName == "user").Scope);
        Assert.AreEqual(1024, result.Records.Single(record => record.KeyName == "large").DisplayName?.Length);
    }

    [TestMethod]
    public void ReportsAccessDeniedWithoutFailingOtherViews()
    {
        FakeRegistry registry = new() { Deny = RegistryViewKind.Machine32 };
        registry.Entries[RegistryViewKind.Machine64] = [Entry("machine", "Machine", "1", "P")];
        RegistryInventoryResult result = new RegistryInventoryProvider(registry).ReadAll();
        Assert.AreEqual(1, result.Records.Count);
        CollectionAssert.Contains(result.Warnings.ToArray(), "Machine32:access-denied");
    }

    private static RegistryUninstallEntry Entry(string key, string? name, string? version, string? publisher) => new(key, new Dictionary<string, object?> { ["DisplayName"] = name, ["DisplayVersion"] = version, ["Publisher"] = publisher, ["InstallDate"] = "20260101" });

    private sealed class FakeRegistry : IUninstallRegistry
    {
        public Dictionary<RegistryViewKind, IEnumerable<RegistryUninstallEntry>> Entries { get; } = [];
        public RegistryViewKind? Deny { get; init; }
        public IEnumerable<RegistryUninstallEntry> ReadUninstallEntries(RegistryViewKind view)
        {
            if (Deny == view) throw new UnauthorizedAccessException();
            return Entries.TryGetValue(view, out IEnumerable<RegistryUninstallEntry>? entries) ? entries : [];
        }
    }
}
