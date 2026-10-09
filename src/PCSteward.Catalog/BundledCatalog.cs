namespace PCSteward.Catalog;

/// <summary>Loads the catalog embedded in the signed PC Steward payload (FR-CAT-01).</summary>
public static class BundledCatalog
{
    internal const string ResourceName = "PCSteward.Catalog.pcsteward-catalog.v1.json";

    public static CatalogLoadResult Load()
    {
        using Stream? stream = typeof(BundledCatalog).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null || stream.Length is 0 or > CatalogValidator.MaximumBytes) return CatalogValidator.Validate([]);
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return CatalogValidator.Validate(bytes);
    }
}
