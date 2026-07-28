namespace PCSteward.Inventory.Windows;

public sealed record MsixPackageInfo(string FamilyName, string FullName, string Name, string Version, string Architecture, string PublisherId, string? InstallLocation, bool IsFramework, bool IsResourcePackage, bool IsSystemPackage);
public sealed record MsixInventoryRecord(string Provider, string PackageFamilyName, string PackageFullName, string DisplayName, string Version, string Architecture, string PublisherId, string? InstallLocation, string Classification, DateTimeOffset RetrievedAt);
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
                    if (string.IsNullOrWhiteSpace(package.FamilyName) || string.IsNullOrWhiteSpace(package.FullName)) throw new InvalidOperationException("Package identity is required.");
                    string classification = package.IsFramework ? "Framework" : package.IsResourcePackage ? "Resource" : package.IsSystemPackage ? "System" : "Application";
                    records.Add(new MsixInventoryRecord("msix", Text(package.FamilyName), Text(package.FullName), Text(package.Name), Text(package.Version), Text(package.Architecture), Text(package.PublisherId), SafePath(package.InstallLocation), classification, retrievedAt));
                }
                catch (InvalidOperationException) { warnings.Add("msix:malformed-package"); }
            }
        }
        catch (UnauthorizedAccessException) { warnings.Add("msix:catalog-access-denied"); }
        return new MsixInventoryResult(records, warnings);
    }
    private static string Text(string value) => value.Trim()[..Math.Min(value.Trim().Length, 1024)];
    private static string? SafePath(string? path) => string.IsNullOrWhiteSpace(path) || path.Length > 1024 ? null : path;
}
