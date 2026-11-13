using PCSteward.Inventory.Windows;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class MsixInventoryProviderTests
{
    private static readonly string[] ExpectedIdentities = ["Contoso.Studio_g54w8ztgkx496", "Contoso.Runtime_1.0.0.0_x64__g54w8ztgkx496", "Contoso.Studio_3.3.0.4529_neutral_split.scale-100_g54w8ztgkx496"];

    [TestMethod]
    public void RetainsCurrentUserPackageIdentityAndConservativeClassification()
    {
        FakeCatalog catalog = new([new MsixPackageInfo("contoso_1234567890abc", "contoso_1.2.3.0_x64__1234567890abc", "Contoso", "1.2.3.0", "x64", "1234567890abc", "C:\\Apps\\Contoso", false, false, false), new MsixPackageInfo("framework_1234567890abc", "framework_1.0_x64__1234567890abc", "Framework", "1.0", "x64", "1234567890abc", null, true, false, false)]);
        MsixInventoryResult result = new MsixInventoryProvider(catalog).ReadCurrentUserPackages(DateTimeOffset.UnixEpoch);
        Assert.AreEqual(2, result.Records.Count);
        Assert.AreEqual("Application", result.Records.Single(record => record.PackageFamilyName == "contoso_1234567890abc").Classification);
        Assert.AreEqual("Framework", result.Records.Single(record => record.PackageFamilyName == "framework_1234567890abc").Classification);
    }
    [TestMethod]
    public void UsesTheNamesWindowsShowsAndFallsBackToThePackageIdentity()
    {
        FakeCatalog catalog = new(
        [
            new MsixPackageInfo("Contoso.Studio_g54w8ztgkx496", "Contoso.Studio_3.3.0.4529_x64__g54w8ztgkx496", "Contoso.Studio", "3.3.0.4529", "X64", "g54w8ztgkx496", null, false, false, false, " Contoso Studio ", "Contoso Audio, Inc."),
            new MsixPackageInfo("Contoso.Tool_g54w8ztgkx496", "Contoso.Tool_1.0.0.0_x64__g54w8ztgkx496", "Contoso.Tool", "1.0.0.0", "X64", "g54w8ztgkx496", null, false, false, false, "ms-resource:AppName", null),
            new MsixPackageInfo("Contoso.Bad", "Contoso.Bad_1.0.0.0_x64__g54w8ztgkx496", "Contoso.Bad", "1.0.0.0", "X64", "g54w8ztgkx496", null, false, false, false)
        ]);

        MsixInventoryResult result = new MsixInventoryProvider(catalog).ReadCurrentUserPackages(DateTimeOffset.UnixEpoch);

        MsixInventoryRecord studio = result.Records.Single(record => record.PackageFamilyName == "Contoso.Studio_g54w8ztgkx496");
        Assert.AreEqual(("Contoso Studio", "Contoso Audio, Inc."), (studio.DisplayName, studio.Publisher));
        MsixInventoryRecord tool = result.Records.Single(record => record.PackageFamilyName == "Contoso.Tool_g54w8ztgkx496");
        Assert.AreEqual(("Contoso.Tool", "g54w8ztgkx496"), (tool.DisplayName, tool.Publisher));
        Assert.AreEqual(2, result.Records.Count);
        CollectionAssert.Contains(result.Warnings.ToArray(), "msix:malformed-package");
    }

    [TestMethod]
    public void AnAppIsNamedByItsFamilyAndAFrameworkOrResourcePackageByItsFullName()
    {
        FakeCatalog catalog = new(
        [
            new MsixPackageInfo("Contoso.Studio_g54w8ztgkx496", "Contoso.Studio_3.3.0.4529_x64__g54w8ztgkx496", "Contoso.Studio", "3.3.0.4529", "X64", "g54w8ztgkx496", null, false, false, false),
            new MsixPackageInfo("Contoso.Runtime_g54w8ztgkx496", "Contoso.Runtime_1.0.0.0_x64__g54w8ztgkx496", "Contoso.Runtime", "1.0.0.0", "X64", "g54w8ztgkx496", null, true, false, false),
            new MsixPackageInfo("Contoso.Studio_g54w8ztgkx496", "Contoso.Studio_3.3.0.4529_neutral_split.scale-100_g54w8ztgkx496", "Contoso.Studio", "3.3.0.4529", "Neutral", "g54w8ztgkx496", null, false, true, false)
        ]);

        string[] identities = [.. new MsixInventoryProvider(catalog).ReadCurrentUserPackages(DateTimeOffset.UnixEpoch).Records.Select(MsixInventoryProvider.ProviderIdentity)];

        CollectionAssert.AreEqual(ExpectedIdentities, identities);
    }

    private sealed class FakeCatalog(IEnumerable<MsixPackageInfo> packages) : ICurrentUserPackageCatalog { public IEnumerable<MsixPackageInfo> FindPackagesForCurrentUser() => packages; }
}
