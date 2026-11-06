using PCSteward.Domain;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Provider.Chocolatey;

/// <summary>
/// Compiled reviewed identity data only; pins never establish native trust, consent or eligibility by themselves.
/// ADR-031 (design mapping §3.1): the single-release pin became the compiled reviewed list
/// (<see cref="ChocolateyReleaseList.Listed"/>). The three constants remain as the independently reviewed 2.7.5 pins: the
/// list's 2.7.5 entry uses <see cref="Version"/> and a test cross-checks the other two against it; production trust
/// decisions use list membership (<see cref="IsListedEngine"/>, <see cref="IsListedAsset"/>), never these constants alone.
/// </summary>
internal static class ChocolateyClientReleasePolicy
{
    internal const string Version = "2.7.5.0";
    internal const string AssetSha256 = "F86F99A18D236C2F99E6F67A20BE7ADC65C6C7756BF6CF3B43759E7EC0AA5839";
    internal const string EngineSha256 = "4AB5785C91D6CB3CC0E9890BCA29077F7D6BAAC0DFC935DBE2DA37047ED761C0";

    /// <summary>
    /// The package identities of the client itself; package jobs refuse them (observer step 7). The rule is the shared Domain
    /// predicate, so the review gate's package-route admission refuses exactly the same identities.
    /// </summary>
    internal static bool IsClientTarget(string id) => ReviewedUpdateOperation.IsChocolateyClientIdentity(id);

    /// <summary>The root engine digest of a listed reviewed release.</summary>
    internal static bool IsListedEngine(string? engineSha256) => ChocolateyReleaseList.Listed.FindByEngine(engineSha256) is not null;

    /// <summary>The published asset digest of a listed reviewed release.</summary>
    internal static bool IsListedAsset(string? assetSha256) => ChocolateyReleaseList.Listed.ContainsAsset(assetSha256);

    /// <summary>
    /// Task 50 qualification probe spec §4.2 Q10 (values only): the normalized versions of the compiled listed releases, strictly
    /// ascending, so the probe response codec can check <c>observedClientVersion</c> without naming a native type.
    /// </summary>
    internal static IReadOnlyList<string> ListedVersions() => [.. ChocolateyReleaseList.Listed.Releases.Select(release => release.Version)];
}
