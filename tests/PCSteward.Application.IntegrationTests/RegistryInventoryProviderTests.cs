using PCSteward.Inventory.Windows;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class RegistryInventoryProviderTests
{
    private static readonly string[] VisibleKeys = ["{VISIBLE-ZERO}", "{TEXT-FLAG}", "{BUNDLE}"];

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
    public void SkipsEntriesWindowsHidesAsSystemComponents()
    {
        // Owner's PC, 2026-11-13: each .NET Desktop Runtime bundle has a hidden MSI entry with the same name and an internal version.
        FakeRegistry registry = new();
        registry.Entries[RegistryViewKind.Machine64] =
        [
            Entry("{HIDDEN}", "Microsoft Windows Desktop Runtime - 8.0.33 (x64)", "64.132.61678", "Microsoft Corporation", systemComponent: 1),
            Entry("{VISIBLE-ZERO}", "Tool", "1.0", "Contoso", systemComponent: 0),
            Entry("{TEXT-FLAG}", "Other", "1.0", "Contoso", systemComponent: "1")
        ];
        registry.Entries[RegistryViewKind.Machine32] = [Entry("{BUNDLE}", "Microsoft Windows Desktop Runtime - 8.0.33 (x64)", "8.0.33.36622", "Microsoft Corporation")];

        RegistryInventoryResult result = new RegistryInventoryProvider(registry).ReadAll();

        CollectionAssert.AreEquivalent(VisibleKeys, result.Records.Select(record => record.KeyName).ToArray());
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

    [TestMethod]
    public void ReportsIoAndSecurityFailuresWithoutCrashingTheScan()
    {
        FakeRegistry registry = new() { Failure = RegistryViewKind.CurrentUser, FailureException = new IOException("malformed value") };
        registry.Entries[RegistryViewKind.Machine64] = [Entry("machine", "Machine", "1", "P")];

        RegistryInventoryResult result = new RegistryInventoryProvider(registry).ReadAll();

        Assert.HasCount(1, result.Records);
        CollectionAssert.Contains(result.Warnings.ToArray(), "CurrentUser:io-error");
    }

    [TestMethod]
    public void SkipsOneUnreadableEntryAndContinuesTheSameRegistryView()
    {
        FakeRegistry registry = new();
        registry.Entries[RegistryViewKind.Machine64] =
        [
            new RegistryUninstallEntry("blocked", new Dictionary<string, object?>(), "unreadable"),
            Entry("good", "Good", "1", "Publisher")
        ];

        RegistryInventoryResult result = new RegistryInventoryProvider(registry).ReadAll();

        Assert.HasCount(1, result.Records);
        Assert.AreEqual("good", result.Records.Single().KeyName);
        CollectionAssert.Contains(result.Warnings.ToArray(), "Machine64:blocked:unreadable");
    }

    [TestMethod]
    public void StopsAViewAtItsEntryBudgetAndKeepsReadingTheOtherViews()
    {
        // Codex security scan finding 8: owner-writable HKCU keys cannot make the scan read without bound.
        FakeRegistry registry = new();
        registry.Entries[RegistryViewKind.CurrentUser] = Enumerable.Range(0, RegistryInventoryProvider.MaximumEntriesPerView + 50).Select(index => Entry("user" + index, "Tool " + index, "1", "P"));
        registry.Entries[RegistryViewKind.Machine64] = [Entry("machine", "Machine", "1", "P")];

        RegistryInventoryResult result = new RegistryInventoryProvider(registry).ReadAll();

        Assert.AreEqual(RegistryInventoryProvider.MaximumEntriesPerView + 1, result.Records.Count);
        CollectionAssert.Contains(result.Warnings.ToArray(), "CurrentUser:entry-budget-exceeded");
    }

    [TestMethod]
    public void CancellationStopsTheWalkBetweenEntries()
    {
        using CancellationTokenSource cancellation = new();
        int produced = 0;
        IEnumerable<RegistryUninstallEntry> Endless()
        {
            while (true)
            {
                if (++produced == 10) cancellation.Cancel();
                yield return Entry("key" + produced, "Name", "1", "P");
            }
        }
        FakeRegistry registry = new();
        registry.Entries[RegistryViewKind.Machine64] = Endless();

        Assert.ThrowsExactly<OperationCanceledException>(() => new RegistryInventoryProvider(registry).ReadAll(cancellation.Token));
        Assert.AreEqual(10, produced);
    }

    private static RegistryUninstallEntry Entry(string key, string? name, string? version, string? publisher, object? systemComponent = null)
    {
        Dictionary<string, object?> values = new() { ["DisplayName"] = name, ["DisplayVersion"] = version, ["Publisher"] = publisher, ["InstallDate"] = "20260101" };
        if (systemComponent is not null) values["SystemComponent"] = systemComponent;
        return new(key, values);
    }

    private sealed class FakeRegistry : IUninstallRegistry
    {
        public Dictionary<RegistryViewKind, IEnumerable<RegistryUninstallEntry>> Entries { get; } = [];
        public RegistryViewKind? Deny { get; init; }
        public RegistryViewKind? Failure { get; init; }
        public Exception? FailureException { get; init; }
        public IEnumerable<RegistryUninstallEntry> ReadUninstallEntries(RegistryViewKind view)
        {
            if (Deny == view) throw new UnauthorizedAccessException();
            if (Failure == view) throw FailureException!;
            return Entries.TryGetValue(view, out IEnumerable<RegistryUninstallEntry>? entries) ? entries : [];
        }
    }
}
