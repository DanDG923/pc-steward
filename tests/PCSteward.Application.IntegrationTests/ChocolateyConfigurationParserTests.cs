using System.Text;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/01; TM-03/04/13: bounded native configuration data never grants eligibility.</summary>
[TestClass]
public sealed class ChocolateyConfigurationParserTests
{
    [TestMethod]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, true, true)]
    public void NativeSetExplicitlyDeterminesWhetherEnabledOverridesTheDefault(bool explicitlySet, bool enabled, bool expected)
    {
        string xml = $"<chocolatey><features><feature name='ChecksumFiles' enabled='{enabled.ToString().ToLowerInvariant()}' setExplicitly='{explicitlySet.ToString().ToLowerInvariant()}'/></features></chocolatey>";
        Assert.IsTrue(Read(xml, out ChocolateyConfigurationObservation? result));
        Assert.AreEqual(expected, result!.Features["checksumFiles"]);
        Assert.AreEqual(enabled, result.ObservedFeatures["checksumFiles"].Enabled);
        Assert.AreEqual(explicitlySet, result.ObservedFeatures["checksumFiles"].SetExplicitly);
        Assert.IsFalse(result.ObservedFeatures.ContainsKey("powershellHost"));
        Assert.IsTrue(result.Features["powershellHost"]);
        Assert.IsFalse(result.Features["useRememberedArgumentsForUpgrades"]);
    }

    [TestMethod]
    public void DefaultAndTypedSettingsSourcesAreCopiedWithoutRetainingCredentials()
    {
        const string xml = """
            <chocolatey xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <!-- serializer shape, not executable content -->
              <config><add key="cacheLocation" value="D:\Protected Cache" description="fixture"/>
                <add key="proxyPassword" value="fixture-password"/><add key="webRequestTimeoutSeconds" value="17"/></config>
              <sources><source id="fixture" value="https://example.invalid/" disabled="false" priority="3" bypassProxy="1"
                selfService="0" adminOnly="true" user="fixture-user" password="fixture-password" certificate="fixture-cert" certificatePassword="fixture-password"/></sources>
              <apiKeys><apiKeys source="fixture" key="fixture-api-key"/></apiKeys>
            </chocolatey>
            """;
        Assert.IsTrue(Read(xml, out ChocolateyConfigurationObservation? result));
        Assert.AreEqual(@"D:\Protected Cache", result!.CacheLocation);
        Assert.AreEqual(2700, result.CommandExecutionTimeoutSeconds);
        Assert.AreEqual(17, result.WebRequestTimeoutSeconds);
        Assert.IsTrue(result.ProxyCredentialsConfigured);
        Assert.AreEqual(30, result.Features.Count);
        ChocolateyConfiguredSource source = result.Sources.Single();
        Assert.AreEqual("fixture", source.Id); Assert.AreEqual("https://example.invalid/", source.Value);
        Assert.AreEqual(3, source.Priority); Assert.IsTrue(source.BypassProxy); Assert.IsTrue(source.AdminOnly);
        Assert.IsTrue(source.HasCredentials); Assert.IsTrue(source.HasClientCertificate);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, bool>)result.Features)["checksumFiles"] = false);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ChocolateyConfiguredSource>)result.Sources).Clear());
        Assert.IsFalse(result.ToString()!.Contains("fixture-password", StringComparison.Ordinal));
        Assert.IsFalse(source.ToString()!.Contains("fixture-password", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("<Chocolatey/>")]
    [DataRow("<chocolatey xmlns='urn:unknown'/>")]
    [DataRow("<chocolatey extra='unknown'/>")]
    [DataRow("<chocolatey><features/><features/></chocolatey>")]
    [DataRow("<chocolatey><features><feature name='checksumFiles'/><feature name='ChecksumFiles'/></features></chocolatey>")]
    [DataRow("<chocolatey><features><feature name='usePowerShellHost'/></features></chocolatey>")]
    [DataRow("<chocolatey><features><feature name='checksumFiles ' enabled='true'/></features></chocolatey>")]
    [DataRow("<chocolatey><features><feature name='checksumFiles' enabled='True'/></features></chocolatey>")]
    [DataRow("<chocolatey><features><feature name='checksumFiles' injected='true'/></features></chocolatey>")]
    [DataRow("<chocolatey><features><feature name='checksumFiles'><nested/></feature></features></chocolatey>")]
    [DataRow("<chocolatey><config><add key='cacheLocation' value='a'/><add key='CACHELOCATION' value='b'/></config></chocolatey>")]
    [DataRow("<chocolatey><config><add key='unknown' value='b'/></config></chocolatey>")]
    [DataRow("<chocolatey><config><add key='cacheLocation'/></config></chocolatey>")]
    [DataRow("<chocolatey><sources><source id='a' value='x'/><source id='A' value='y'/></sources></chocolatey>")]
    [DataRow("<chocolatey><sources><source id='a' value='x'/><source id='b' value='X'/></sources></chocolatey>")]
    [DataRow("<chocolatey><sources><source id='a' value='x' priority='overflow'/></sources></chocolatey>")]
    [DataRow("<chocolatey><unknown/></chocolatey>")]
    [DataRow("<chocolatey/><?injected instruction?>")]
    [DataRow("<!DOCTYPE chocolatey [<!ENTITY e 'test'>]><chocolatey/>")]
    [DataRow("<chocolatey/><chocolatey/>")]
    [DataRow("<chocolatey>")]
    [DataRow("")]
    public void UnsupportedAmbiguousOrMalformedXmlIsRejectedWithoutAResult(string xml)
    {
        Assert.IsFalse(Read(xml, out ChocolateyConfigurationObservation? result));
        Assert.IsNull(result);
    }

    [TestMethod]
    [DataRow("<add key='containsLegacyPackageInstalls' value='true'/>")]
    [DataRow("<add key='containsLegacyPackageInstalls' value='true' description='fixture'/>")]
    [DataRow("<add key='ContainsLegacyPackageInstalls' value='true'/>")]
    public void TheLegacyPackageInstallsKeyIsAdmittedOnlyWithItsTemplateValue(string entry)
    {
        // Profile V2 revision 2.1: the pinned 2.7.5 template writes value="true" and no engine code reads the key.
        Assert.IsTrue(Read($"<chocolatey><config><add key='cacheLocation' value='D:\\Cache'/>{entry}</config></chocolatey>", out ChocolateyConfigurationObservation? result));
        Assert.AreEqual(@"D:\Cache", result!.CacheLocation);
        Assert.AreEqual("containsLegacyPackageInstalls", ChocolateyConfigurationParser.LegacyPackageInstallsKey);
        Assert.AreEqual("true", ChocolateyConfigurationParser.LegacyPackageInstallsTemplateValue);
    }

    [TestMethod]
    [DataRow("<add key='containsLegacyPackageInstalls' value='false'/>")]
    [DataRow("<add key='containsLegacyPackageInstalls' value='True'/>")]
    [DataRow("<add key='containsLegacyPackageInstalls' value='TRUE'/>")]
    [DataRow("<add key='containsLegacyPackageInstalls' value=''/>")]
    [DataRow("<add key='containsLegacyPackageInstalls' value=' true'/>")]
    [DataRow("<add key='containsLegacyPackageInstalls' value='true '/>")]
    [DataRow("<add key='containsLegacyPackageInstalls' value='1'/>")]
    [DataRow("<add key='containsLegacyPackageInstalls' value='yes'/>")]
    [DataRow("<add key='containsLegacyPackageInstalls'/>")]
    [DataRow("<add key='containsLegacyPackageInstalls' value='true'/><add key='CONTAINSLEGACYPACKAGEINSTALLS' value='true'/>")]
    [DataRow("<add key='containsLegacyPackageInstalls ' value='true'/>")]
    [DataRow("<add key='containsLegacyPackageInstall' value='true'/>")]
    [DataRow("<add key='containsLegacyPackageInstalls' value='true' extra='x'/>")]
    // Independent review (Low): the value rule must apply to every case-insensitive spelling of the key.
    [DataRow("<add key='ContainsLegacyPackageInstalls' value='false'/>")]
    [DataRow("<add key='CONTAINSLEGACYPACKAGEINSTALLS' value='True'/>")]
    [DataRow("<add key='containslegacypackageinstalls' value=''/>")]
    public void EveryOtherLegacyPackageInstallsValueOrShapeRefuses(string entry) =>
        Assert.IsFalse(Read($"<chocolatey><config>{entry}</config></chocolatey>", out _), entry);

    [TestMethod]
    public void EncodingInputAndCollectionBoundsAreEnforcedBeforeAUsableSnapshot()
    {
        byte[] utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<chocolatey/>")).ToArray();
        Assert.IsTrue(ChocolateyConfigurationParser.TryRead(utf16, out _));
        byte[] utf32 = Encoding.UTF32.GetPreamble().Concat(Encoding.UTF32.GetBytes("<chocolatey/>")).ToArray();
        Assert.IsFalse(ChocolateyConfigurationParser.TryRead(utf32, out _));
        Assert.IsTrue(Read("<chocolatey/>".PadRight(65536), out _));
        Assert.IsFalse(Read("<chocolatey/>".PadRight(65537), out _));
        Assert.IsFalse(Read("<chocolatey><config><add key='cacheLocation' value='" + new string('a', 4097) + "'/></config></chocolatey>", out _));
        string sources = string.Concat(Enumerable.Range(0, 65).Select(i => $"<source id='id{i}' value='url{i}'/>"));
        Assert.IsFalse(Read("<chocolatey><sources>" + sources + "</sources></chocolatey>", out _));
        Assert.IsFalse(Read("<chocolatey>" + string.Concat(Enumerable.Repeat("<nested>",1000)), out _));
        Assert.IsFalse(ChocolateyConfigurationParser.TryRead([0xC3,0x28], out _));
    }

    private static bool Read(string xml, out ChocolateyConfigurationObservation? result) =>
        ChocolateyConfigurationParser.TryRead(Encoding.UTF8.GetBytes(xml), out result);
}
