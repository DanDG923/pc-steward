using System.Text;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/01; TM-13: parse exact native identity without silently selecting malformed alternatives.</summary>
[TestClass]
public sealed class ChocolateyNuspecIdentityReaderTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd")]
    [DataRow("http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd")]
    [DataRow("http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd")]
    [DataRow("http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd")]
    [DataRow("http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd")]
    [DataRow("http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd")]
    [DataRow("http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd")]
    public void KnownNativeSchemasProduceOnlyBoundedIdentityData(string schema)
    {
        string xml = $"<package xmlns='{schema}'><metadata><id>git.install</id><version>01.2.0.0</version><description><![CDATA[not identity]]></description></metadata></package>";
        Assert.IsTrue(Read(xml, out ChocolateyNuspecIdentity? identity));
        Assert.AreEqual("git.install", identity!.Id);
        Assert.AreEqual("1.2.0", identity.Version.Normalized);
        Assert.AreEqual("01.2.0.0", identity.Version.Original);
    }

    [TestMethod]
    public void LegacyNamespaceOnMetadataRatherThanPackageUsesItsDefaultNamespace()
    {
        const string xml = "<package><metadata xmlns='http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd'><id>git</id><version>1.0</version></metadata></package>";
        Assert.IsTrue(Read(xml, out ChocolateyNuspecIdentity? identity));
        Assert.AreEqual("git", identity!.Id);
    }

    [TestMethod]
    [DataRow("<package><metadata><id>git</id><id>other</id><version>1.0</version></metadata></package>")]
    [DataRow("<package><metadata><id>git</id><version>1.0</version><version>2.0</version></metadata></package>")]
    [DataRow("<package><metadata><id>git</id><version>1.0</version></metadata><metadata><id>other</id><version>2.0</version></metadata></package>")]
    [DataRow("<package xmlns='urn:unknown'><metadata><id>git</id><version>1.0</version></metadata></package>")]
    [DataRow("<package><metadata><id xmlns='urn:unknown'>git</id><version>1.0</version></metadata></package>")]
    [DataRow("<package><metadata><id>git</id><id xmlns='urn:unknown'>other</id><version>1.0</version></metadata></package>")]
    [DataRow("<package><metadata><id>gi<nested/>t</id><version>1.0</version></metadata></package>")]
    [DataRow("<package><metadata><id>../git</id><version>1.0</version></metadata></package>")]
    [DataRow("<package><metadata><id>git</id><version>1.0+metadata</version></metadata></package>")]
    [DataRow("<package><metadata><id>git</id></metadata></package>")]
    [DataRow("<package><metadata><ID>git</ID><version>1.0</version></metadata></package>")]
    [DataRow("<?injected instruction?><package/>")]
    [DataRow("<!DOCTYPE package [<!ENTITY id 'git'>]><package/>")]
    [DataRow("<other><metadata><id>git</id><version>1.0</version></metadata></other>")]
    public void AmbiguousUnsupportedOrMalformedIdentityReturnsNoObservation(string xml)
    {
        Assert.IsFalse(Read(xml, out ChocolateyNuspecIdentity? identity));
        Assert.IsNull(identity);
    }

    [TestMethod]
    public void SizeEncodingAndDepthAreBoundedWithoutLoadingAnArchive()
    {
        const string xml = "<package><metadata><id>git</id><version>1.0</version></metadata></package>";
        Assert.IsTrue(Read(xml.PadRight(65536), out _));
        Assert.IsFalse(Read(xml.PadRight(65537), out _));
        byte[] utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray();
        Assert.IsTrue(ChocolateyNuspecIdentityReader.TryRead(utf16, out _));
        Assert.IsFalse(ChocolateyNuspecIdentityReader.TryRead([0xC3, 0x28], out _));
        Assert.IsFalse(Read("<package>" + string.Concat(Enumerable.Repeat("<nested>", 1000)), out _));
    }

    private static bool Read(string xml, out ChocolateyNuspecIdentity? identity) =>
        ChocolateyNuspecIdentityReader.TryRead(Encoding.UTF8.GetBytes(xml), out identity);
}
