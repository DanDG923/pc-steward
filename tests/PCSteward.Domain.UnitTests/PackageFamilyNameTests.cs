using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

[TestClass]
public sealed class PackageFamilyNameTests
{
    [TestMethod]
    [DataRow("Microsoft.WindowsTerminal_8wekyb3d8bbwe")]
    [DataRow("microsoft.windowsterminal_8wekyb3d8bbwe")]
    [DataRow("Elgato.WaveLink_g54w8ztgkx496")]
    [DataRow(" Contoso-App.Studio_1234567890abc ")]
    public void AcceptsWindowsPackageFamilyNames(string value) => Assert.IsTrue(PackageFamilyName.IsValid(value));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("Ab_8wekyb3d8bbwe")]
    [DataRow("Contoso.Studio")]
    [DataRow("Contoso.Studio_8wekyb3d8bbw")]
    [DataRow("Contoso.Studio_8wekyb3d8bbwee")]
    [DataRow("Contoso Studio_8wekyb3d8bbwe")]
    [DataRow("Contoso\\Studio_8wekyb3d8bbwe")]
    [DataRow("Contoso.Studio_8wekyb3d8-bwe")]
    [DataRow("Contoso.Studio_1.0.0.0_x64__8wekyb3d8bbwe")]
    public void RefusesAnythingElse(string? value) => Assert.IsFalse(PackageFamilyName.IsValid(value));

    [TestMethod]
    public void RefusesANameLongerThanFiftyCharacters()
    {
        Assert.IsTrue(PackageFamilyName.IsValid(new string('a', 50) + "_8wekyb3d8bbwe"));
        Assert.IsFalse(PackageFamilyName.IsValid(new string('a', 51) + "_8wekyb3d8bbwe"));
    }
}
