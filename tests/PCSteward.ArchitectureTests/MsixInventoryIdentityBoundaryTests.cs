namespace PCSteward.ArchitectureTests;

/// <summary>
/// MSIX design note §3.1: the desktop and scheduled-host MSIX inventories name an app by its package family name, keep frameworks and
/// resource packages on their full name (so WinGet's family link never reaches them), and key the raw record by the same identity as
/// the installation, so the details page still finds its technical records.
/// </summary>
[TestClass]
public sealed class MsixInventoryIdentityBoundaryTests
{
    [TestMethod]
    public void DesktopMsixInventoryUsesOneIdentityForProductInstanceAndRawRecord()
    {
        string source = Read("src", "PCSteward.Desktop", "AdditionalLocalInventoryProviders.cs");

        StringAssert.Contains(source, "string identity = MsixInventoryProvider.ProviderIdentity(record);");
        StringAssert.Contains(source, "products.Add(new CanonicalProduct(productId, identity, record.DisplayName))");
        StringAssert.Contains(source, "record.Provider, identity, record.Version, \"User\"");
        StringAssert.Contains(source, "new RawProviderRecord(Guid.NewGuid(), Guid.Empty, record.Provider, identity,");
    }

    [TestMethod]
    public void ScheduledMsixInventorySkipsFrameworksAndUsesTheFamilyForInstanceAndRawRecord()
    {
        string source = Read("src", "PCSteward.ScheduledHost", "ScheduledInventoryProviders.cs");

        StringAssert.Contains(source, "if (package.IsFramework || package.IsResourcePackage) continue;");
        StringAssert.Contains(source, "productId, \"msix\", family,");
        StringAssert.Contains(source, "new RawProviderRecord(Guid.NewGuid(), Guid.Empty, \"msix\", family,");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([CompiledIlScanner.FindRepositoryRoot(), .. parts]));
}
