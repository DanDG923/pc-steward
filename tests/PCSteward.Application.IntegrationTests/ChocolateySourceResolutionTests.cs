using System.Text;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/01; TM-13: observe enabled-source alias resolution, never supply command authority.</summary>
[TestClass]
public sealed class ChocolateySourceResolutionTests
{
    private const string Community = "https://community.chocolatey.org/api/v2/";

    [TestMethod]
    [DataRow("", true)]
    [DataRow("<source id='ordinary' value='https://example.invalid' disabled='false'/>", true)]
    [DataRow("<source id='https://community.chocolatey.org/api/v2/' value='https://example.invalid' disabled='false'/>", false)]
    [DataRow("<source id='HTTPS://COMMUNITY.CHOCOLATEY.ORG/API/V2/' value='https://example.invalid' disabled='false'/>", false)]
    [DataRow("<source id='https://community.chocolatey.org/api/v2/' value='https://example.invalid' disabled='true'/>", true)]
    public void EnabledNameAliasesCannotRedirectTheFixedSource(string sources, bool expected) =>
        Assert.AreEqual(expected, ChocolateySourceResolution.TryResolve(Config(sources), out _));

    [TestMethod]
    public void DisabledExactKeyCannotMaskAnEnabledNameSubstitution()
    {
        ChocolateyConfigurationObservation config = Config($"<source id='default' value='{Community}' disabled='true'/>" +
            $"<source id='{Community}' value='https://example.invalid' disabled='false'/>");
        Assert.IsFalse(ChocolateySourceResolution.TryResolve(config, out ChocolateyConfiguredSource? source));
        Assert.IsNull(source);
    }

    [TestMethod]
    [DataRow("https://community.chocolatey.org/api/v2/")]
    [DataRow("HTTPS://COMMUNITY.CHOCOLATEY.ORG/API/V2/")]
    public void EnabledKeyWinsAndKeepsTheCompiledInputUrlRatherThanSubstitutingKeyBytes(string key)
    {
        ChocolateyConfigurationObservation config = Config($"<source id='default' value='{key}' disabled='false' bypassProxy='true' certificate='native-configured-cert'/>" +
            $"<source id='{Community}' value='https://example.invalid' disabled='false'/>");
        Assert.IsTrue(ChocolateySourceResolution.TryResolve(config, out ChocolateyConfiguredSource? source));
        Assert.AreEqual("default", source!.Id);
        Assert.IsTrue(source.BypassProxy);
        Assert.IsTrue(source.HasClientCertificate);
    }

    private static ChocolateyConfigurationObservation Config(string sources)
    {
        Assert.IsTrue(ChocolateyConfigurationParser.TryRead(Encoding.UTF8.GetBytes($"<chocolatey><sources>{sources}</sources></chocolatey>"), out ChocolateyConfigurationObservation? config));
        return config!;
    }
}
