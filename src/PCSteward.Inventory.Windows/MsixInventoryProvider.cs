using PCSteward.Domain;

namespace PCSteward.Inventory.Windows;

public sealed record MsixPackageInfo(string FamilyName, string FullName, string Name, string Version, string Architecture, string PublisherId, string? InstallLocation, bool IsFramework, bool IsResourcePackage, bool IsSystemPackage,
    string? DisplayName = null, string? PublisherDisplayName = null);
/// <summary>
/// One MSIX package installed for the current user. <see cref="DisplayName"/> and <see cref="Publisher"/> are the names Windows shows
/// (Package.DisplayName and Package.PublisherDisplayName), or the package name and publisher ID when Windows gives none.
/// </summary>
public sealed record MsixInventoryRecord(string Provider, string PackageFamilyName, string PackageFullName, string DisplayName, string Version, string Architecture, string PublisherId, string? InstallLocation, string Classification, DateTimeOffset RetrievedAt,
    string? Publisher = null);
public sealed record MsixInventoryResult(IReadOnlyCollection<MsixInventoryRecord> Records, IReadOnlyCollection<string> Warnings);

public interface ICurrentUserPackageCatalog
{
    IEnumerable<MsixPackageInfo> FindPackagesForCurrentUser();
}

public sealed class MsixInventoryProvider
{
    private readonly ICurrentUserPackageCatalog catalog;
    public MsixInventoryProvider(ICurrentUserPackageCatalog catalog) => this.catalog = catalog;
    public MsixInventoryResult ReadCurrentUserPackages(DateTimeOffset retrievedAt)
    {
        List<MsixInventoryRecord> records = [];
        List<string> warnings = [];
        try
        {
            foreach (MsixPackageInfo package in catalog.FindPackagesForCurrentUser())
            {
                try
                {
                    if (!PackageFamilyName.IsValid(package.FamilyName) || string.IsNullOrWhiteSpace(package.FullName)) throw new InvalidOperationException("Package identity is required.");
                    string classification = package.IsFramework ? "Framework" : package.IsResourcePackage ? "Resource" : package.IsSystemPackage ? "System" : "Application";
                    records.Add(new MsixInventoryRecord("msix", Text(package.FamilyName), Text(package.FullName), ShownName(package.DisplayName) ?? Text(package.Name), Text(package.Version), Text(package.Architecture), Text(package.PublisherId), SafePath(package.InstallLocation), classification, retrievedAt,
                        ShownName(package.PublisherDisplayName) ?? Text(package.PublisherId)));
                }
                catch (InvalidOperationException) { warnings.Add("msix:malformed-package"); }
            }
        }
        catch (UnauthorizedAccessException) { warnings.Add("msix:catalog-access-denied"); }
        return new MsixInventoryResult(records, warnings);
    }
    /// <summary>
    /// The provider identity of a package in inventory (MSIX design note §3.1). An app is named by its package family name, the same for
    /// every version, so WinGet's family link can find it and an update keeps the same product. A framework or resource package keeps
    /// its full package name: it is never offered, so it must never meet WinGet's family link, and it never shares a product with the app.
    /// </summary>
    public static string ProviderIdentity(MsixInventoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return string.Equals(record.Classification, "Application", StringComparison.Ordinal) ? record.PackageFamilyName : record.PackageFullName;
    }

    /// <summary>
    /// A name Windows shows for the package, or null when there is none to use: empty, an unresolved <c>ms-resource:</c> reference,
    /// longer than 256 characters or containing control characters.
    /// </summary>
    public static string? ShownName(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > 256 || trimmed.Any(char.IsControl) ||
            trimmed.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }
    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, 1024)];
    private static string? SafePath(string? path) => string.IsNullOrWhiteSpace(path) || path.Length > 1024 ? null : path;
}
