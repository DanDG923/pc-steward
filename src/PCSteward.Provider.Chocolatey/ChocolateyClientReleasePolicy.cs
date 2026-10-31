namespace PCSteward.Provider.Chocolatey;

/// <summary>Compiled reviewed identity data only; pins never establish native trust, consent or eligibility by themselves.</summary>
internal static class ChocolateyClientReleasePolicy
{
    internal const string Version = "2.7.5.0";
    internal const string AssetSha256 = "F86F99A18D236C2F99E6F67A20BE7ADC65C6C7756BF6CF3B43759E7EC0AA5839";
    internal const string EngineSha256 = "4AB5785C91D6CB3CC0E9890BCA29077F7D6BAAC0DFC935DBE2DA37047ED761C0";

    internal static bool IsClientTarget(string id) => id.Equals("chocolatey", StringComparison.OrdinalIgnoreCase) ||
        id.Equals("chocolatey.extension", StringComparison.OrdinalIgnoreCase);
}
