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
        // UI review 2026-11-13 (fault 3): a package Windows itself signed is classified System, so it keeps its full name (it is
        // never offered) and Installed apps can list it only on request. An unreadable signature is an ordinary app.
        StringAssert.Contains(source, "isFramework, isResource, IsSystemSigned(package),");
        StringAssert.Contains(source, "try { return package.SignatureKind == PackageSignatureKind.System; }");
    }

    [TestMethod]
    public void ScheduledMsixInventorySkipsFrameworksAndUsesTheFamilyForInstanceAndRawRecord()
    {
        string source = Read("src", "PCSteward.ScheduledHost", "ScheduledInventoryProviders.cs");

        // The scheduled host lists apps only. It skips what the desktop names by full package name (frameworks, resource packs and
        // Windows-signed system packages), so a package both inventories list always has one product key.
        StringAssert.Contains(source, "if (package.IsFramework || package.IsResourcePackage || IsSystemSigned(package)) continue;");
        StringAssert.Contains(source, "try { return package.SignatureKind == PackageSignatureKind.System; }");
        StringAssert.Contains(source, "productId, \"msix\", family,");
        StringAssert.Contains(source, "new RawProviderRecord(Guid.NewGuid(), Guid.Empty, \"msix\", family,");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([CompiledIlScanner.FindRepositoryRoot(), .. parts]));
}
