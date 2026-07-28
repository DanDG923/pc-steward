using PCSteward.Inventory.Windows;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class MsixInventoryProviderTests
{
    [TestMethod]
    public void RetainsCurrentUserPackageIdentityAndConservativeClassification()
    {
        FakeCatalog catalog = new([new MsixPackageInfo("contoso_123", "contoso_1.2.3.0_x64__123", "Contoso", "1.2.3.0", "x64", "123", "C:\\Apps\\Contoso", false, false, false), new MsixPackageInfo("framework_123", "framework_1.0_x64__123", "Framework", "1.0", "x64", "123", null, true, false, false)]);
        MsixInventoryResult result = new MsixInventoryProvider(catalog).ReadCurrentUserPackages(DateTimeOffset.UnixEpoch);
        Assert.AreEqual(2, result.Records.Count);
        Assert.AreEqual("Application", result.Records.Single(record => record.PackageFamilyName == "contoso_123").Classification);
        Assert.AreEqual("Framework", result.Records.Single(record => record.PackageFamilyName == "framework_123").Classification);
    }
    private sealed class FakeCatalog(IEnumerable<MsixPackageInfo> packages) : ICurrentUserPackageCatalog { public IEnumerable<MsixPackageInfo> FindPackagesForCurrentUser() => packages; }
}
