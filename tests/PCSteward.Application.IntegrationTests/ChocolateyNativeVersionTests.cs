using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/01; TM-13: native identity normalization cannot hide legacy registrations or store variants.</summary>
[TestClass]
public sealed class ChocolateyNativeVersionTests
{
    private static readonly string[] LegacyNames = ["git.install.1.0.0", "git.install.1.0", "git.install.1.0.0.0"];
    private static readonly string[] PrereleaseNames = ["git.1.0.0-beta.2", "git.1.0.0.0-beta.2", "git.1.0-beta.2"];
    private static readonly string[] RevisionNames = ["git.1.2.3.4"];
    [TestMethod]
    [DataRow("1", "1.0.0", "1")]
    [DataRow("1.2", "1.2.0", "1.2")]
    [DataRow("01.02.003", "1.2.3", "01.02.003")]
    [DataRow("1.2.3.0", "1.2.3", "1.2.3.0")]
    [DataRow("1.2.3.4", "1.2.3.4", "1.2.3.4")]
    [DataRow("1.0-beta", "1.0.0-beta", "1.0-beta")]
    [DataRow("1.0-beta.2", "1.0.0-beta.2", "1.0.0-beta.2")]
    [DataRow("1.0.0-0", "1.0.0-0", "1.0.0-0")]
    public void NativeNormalizedAndLegacyStringFormsRemainDistinct(string text, string normalized, string nativeString)
    {
        Assert.IsTrue(ChocolateyNativeVersion.TryParse(text, out ChocolateyNativeVersion? result));
        Assert.AreEqual(text, result!.Original);
        Assert.AreEqual(normalized, result.Normalized);
        Assert.AreEqual(nativeString, result.NativeString);
        Assert.AreEqual(text.Contains('-', StringComparison.Ordinal), result.IsPrerelease);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" 1.0")]
    [DataRow("1.0 ")]
    [DataRow("1..0")]
    [DataRow("1.2.3.4.5")]
    [DataRow("1.2.3+metadata")]
    [DataRow("1.0-")]
    [DataRow("1.0-01")]
    [DataRow("1.0-alpha..1")]
    [DataRow("1.0-alpha_1")]
    [DataRow("2147483648.0")]
    [DataRow("+1.0")]
    [DataRow("v1.0")]
    public void UnsupportedRepresentationHasNoIdentity(string text)
    {
        Assert.IsFalse(ChocolateyNativeVersion.TryParse(text, out ChocolateyNativeVersion? result));
        Assert.IsNull(result);
    }

    [TestMethod]
    public void StoreAlternativesFollowNativeOrderAndRetainDistinctLegacyPaths()
    {
        Assert.IsTrue(ChocolateyNativeVersion.TryParse("1.0", out ChocolateyNativeVersion? version));
        IReadOnlyList<string> names = version!.GetStoreDirectoryNames("git.install");
        CollectionAssert.AreEqual(LegacyNames, names.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)names).Clear());
        Assert.IsTrue(ChocolateyNativeVersion.TryParse("1.0-beta.2", out version));
        CollectionAssert.AreEqual(PrereleaseNames, version!.GetStoreDirectoryNames("git").ToArray());
        Assert.IsTrue(ChocolateyNativeVersion.TryParse("1.2.3.4", out version));
        CollectionAssert.AreEqual(RevisionNames, version!.GetStoreDirectoryNames("git").ToArray());
    }

    [TestMethod]
    public void PackageNamesCannotTurnStoreAlternativesIntoPaths()
    {
        Assert.IsTrue(ChocolateyNativeVersion.TryParse("1.0", out ChocolateyNativeVersion? version));
        foreach (string id in new[] { "../git", "git/other", "git\\other", "git:other", ".", "con", "AUX.package" })
            Assert.ThrowsExactly<ArgumentException>(() => version!.GetStoreDirectoryNames(id));
        Assert.IsFalse(ChocolateyNativeVersion.TryParse(new string('1', 129), out _));
    }
}
