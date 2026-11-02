using PCSteward.Application;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Provider.Chocolatey;

/// <summary>
/// FR-UPD-15, FR-UI-03/04; ADR-031 (Option 2) and design mapping §3.1/§3.3: read-only display facts of the compiled reviewed
/// release list for the Desktop's Chocolatey client update surface — the listed normalized versions and the expected signer.
/// It reads only <see cref="ChocolateyReleaseList.Listed"/> (versions) and the compiled signer name; it observes, launches,
/// elevates and admits nothing, and the facts never decide trust (the native observer re-derives any offer from held files).
/// </summary>
public static class ChocolateyClientReleaseCatalog
{
    /// <summary>The compiled list's facts. Today exactly 2.7.5.</summary>
    public static ChocolateyClientReleaseCatalogFacts Current { get; } = Create();

    private static ChocolateyClientReleaseCatalogFacts Create()
    {
        IReadOnlyList<ChocolateyReviewedRelease> releases = ChocolateyReleaseList.Listed.Releases;
        string[] versions = new string[releases.Count];
        for (int index = 0; index < versions.Length; index++) versions[index] = releases[index].Version;
        return new ChocolateyClientReleaseCatalogFacts(Array.AsReadOnly(versions), ChocolateyEngineTrust.ExpectedSignerSimpleName);
    }
}
