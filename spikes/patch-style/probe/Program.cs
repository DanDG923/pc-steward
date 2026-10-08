using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Management.Deployment;
using PatchStyle.Prototype;

namespace PatchStyle.Probe;

// Task 45 owner-run probe. READ-ONLY: it never installs, upgrades, uninstalls, accepts agreements,
// changes sources or writes outside the output path given on the command line.
//
//   winget-preflight <catalog.json> <out.json>
//       For each WinGet catalog identity: exact-ID match count, default/available versions,
//       applicable installer per scope (type, scope, architecture, elevation requirement),
//       agreement labels (not text), and package-family/product-code counts.
//   detect-db <catalog.json> <pcsteward.db> <out.json>
//       Opens the PC Steward database READ-ONLY (it includes publishers) and runs the prototype
//       catalog detector. Writes counts plus per-catalog-entry outcomes only; other installed app
//       names are not written.
//   detect <catalog.json> <pcsteward-inventory-export.json> <out.json>
//       Same, from a JSON inventory export. The export has no publisher, so name matches cannot be Exact.
internal static class Program
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["winget-preflight", string catalog, string output] => await WinGetPreflightAsync(catalog, output).ConfigureAwait(false),
                ["detect", string catalog, string export, string output] => Detect(catalog, ReadExport(export), output),
                ["detect-db", string catalog, string database, string output] => Detect(catalog, ReadDatabase(database), output),
                _ => Usage()
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or SqliteException)
        {
            Console.Error.WriteLine(exception.GetType().Name + ": " + exception.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: winget-preflight <catalog.json> <out.json> | detect-db <catalog.json> <pcsteward.db> <out.json> | detect <catalog.json> <export.json> <out.json>");
        return 64;
    }

    private static CatalogLoadResult LoadCatalog(string path)
    {
        CatalogLoadResult catalog = CatalogLoader.Load(File.ReadAllBytes(path), null);
        if (!catalog.IsValid) throw new IOException("Catalog invalid: " + string.Join(", ", catalog.Errors));
        return catalog;
    }

    private static async Task<int> WinGetPreflightAsync(string catalogPath, string outputPath)
    {
        CatalogLoadResult catalog = LoadCatalog(catalogPath);
        PackageManager manager = new();
        PackageCatalogReference reference = manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.OpenWindowsCatalog);
        using CancellationTokenSource connectTimeout = new(TimeSpan.FromSeconds(60));
        ConnectResult connection = await reference.ConnectAsync().AsTask(connectTimeout.Token).ConfigureAwait(false);
        if (connection.Status != ConnectResultStatus.Ok) throw new IOException("WinGet catalog connect failed: " + connection.Status);
        List<object> rows = [];
        foreach (CatalogEntry entry in catalog.Entries)
        {
            foreach (CatalogProvider provider in entry.Providers.Where(provider => provider.Provider == "winget" && provider.Source == "winget"))
            {
                rows.Add(await ProbePackageAsync(connection.PackageCatalog, entry.Id, provider.PackageId).ConfigureAwait(false));
            }
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(new { probe = "winget-preflight", capturedAtUtc = DateTimeOffset.UtcNow, rows }, Indented));
        Console.WriteLine($"Wrote {rows.Count} rows to {outputPath}");
        return 0;
    }

    private static async Task<object> ProbePackageAsync(PackageCatalog catalog, string entryId, string packageId)
    {
        try
        {
            FindPackagesOptions options = new() { ResultLimit = 2 };
            options.Filters.Add(new PackageMatchFilter { Field = PackageMatchField.Id, Option = PackageFieldMatchOption.Equals, Value = packageId });
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
            FindPackagesResult result = await catalog.FindPackagesAsync(options).AsTask(timeout.Token).ConfigureAwait(false);
            MatchResult[] matches = [.. result.Matches];
            if (result.Status != FindPackagesResultStatus.Ok || matches.Length != 1)
            {
                return new { entryId, packageId, status = result.Status.ToString(), matchCount = matches.Length, result.WasLimitExceeded };
            }

            CatalogPackage package = matches[0].CatalogPackage;
            PackageVersionInfo version = package.DefaultInstallVersion;
            CatalogPackageMetadata metadata = version.GetCatalogPackageMetadata();
            return new
            {
                entryId,
                packageId,
                status = "Ok",
                matchCount = 1,
                exactId = string.Equals(package.Id, packageId, StringComparison.OrdinalIgnoreCase),
                defaultVersion = version.Version,
                availableVersionCount = package.AvailableVersions.Count,
                channel = version.Channel,
                installed = package.InstalledVersion is not null,
                user = Installer(version, PackageInstallScope.User),
                machine = Installer(version, PackageInstallScope.System),
                agreementLabels = metadata.Agreements.Select(agreement => agreement.Label).ToArray(),
                license = metadata.License,
                packageFamilyNameCount = version.PackageFamilyNames.Count,
                productCodeCount = version.ProductCodes.Count
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new { entryId, packageId, status = "Error:" + exception.GetType().Name, hresult = exception.HResult };
        }
    }

    private static object Installer(PackageVersionInfo version, PackageInstallScope scope)
    {
        InstallOptions options = new() { PackageInstallScope = scope, PackageInstallMode = PackageInstallMode.Silent };
        if (!version.HasApplicableInstaller(options)) return new { applicable = false };
        PackageInstallerInfo installer = version.GetApplicableInstaller(options);
        return new
        {
            applicable = true,
            installerType = installer.InstallerType.ToString(),
            installerScope = installer.Scope.ToString(),
            architecture = installer.Architecture.ToString(),
            elevation = installer.ElevationRequirement.ToString()
        };
    }

    private static List<InstalledRecord> ReadExport(string exportPath)
    {
        using JsonDocument export = JsonDocument.Parse(File.ReadAllBytes(exportPath));
        List<InstalledRecord> records = [];
        foreach (JsonElement product in export.RootElement.GetProperty("products").EnumerateArray())
        {
            string name = product.GetProperty("displayName").GetString() ?? string.Empty;
            JsonElement[] instances = product.TryGetProperty("instances", out JsonElement list) ? [.. list.EnumerateArray()] : [];
            if (instances.Length == 0) { records.Add(new(name, null, "unknown", null)); continue; }
            foreach (JsonElement instance in instances)
            {
                string provider = instance.GetProperty("provider").GetString() ?? "unknown";
                string? identity = instance.TryGetProperty("providerIdentity", out JsonElement id) ? id.GetString() : null;
                records.Add(new(name, null, provider, identity));
            }
        }

        return records;
    }

    private static List<InstalledRecord> ReadDatabase(string databasePath)
    {
        SqliteConnectionStringBuilder builder = new() { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        using SqliteConnection connection = new(builder.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT p.display_name, i.publisher, i.provider, i.provider_identity FROM installation_instances i JOIN canonical_products p ON p.id = i.product_id;";
        using SqliteDataReader reader = command.ExecuteReader();
        List<InstalledRecord> records = [];
        while (reader.Read())
        {
            records.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return records;
    }

    private static int Detect(string catalogPath, IReadOnlyList<InstalledRecord> records, string outputPath)
    {
        CatalogLoadResult catalog = LoadCatalog(catalogPath);
        CatalogDetector detector = new(catalog.Entries);
        List<DetectionOutcome> outcomes = [.. records.Select(detector.Detect)];
        var perEntry = catalog.Entries.Select(entry => new
        {
            entryId = entry.Id,
            providerIdentity = outcomes.Count(outcome => outcome.Kind == DetectionKind.ProviderIdentity && outcome.EntryIds.Contains(entry.Id)),
            nameAndPublisher = outcomes.Count(outcome => outcome.Kind == DetectionKind.ArpPatternWithPublisher && outcome.EntryIds.Contains(entry.Id)),
            nameOnly = outcomes.Count(outcome => outcome.Kind == DetectionKind.ArpPatternWithoutPublisher && outcome.EntryIds.Contains(entry.Id)),
            ambiguous = outcomes.Count(outcome => outcome.Kind == DetectionKind.Ambiguous && outcome.EntryIds.Contains(entry.Id))
        }).ToArray();
        object summary = new
        {
            probe = "detect",
            capturedAtUtc = DateTimeOffset.UtcNow,
            records = outcomes.Count,
            providerIdentity = outcomes.Count(outcome => outcome.Kind == DetectionKind.ProviderIdentity),
            nameAndPublisher = outcomes.Count(outcome => outcome.Kind == DetectionKind.ArpPatternWithPublisher),
            nameOnly = outcomes.Count(outcome => outcome.Kind == DetectionKind.ArpPatternWithoutPublisher),
            ambiguous = outcomes.Count(outcome => outcome.Kind == DetectionKind.Ambiguous),
            none = outcomes.Count(outcome => outcome.Kind == DetectionKind.None),
            perEntry
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(summary, Indented));
        Console.WriteLine($"Wrote detection summary to {outputPath}");
        return 0;
    }
}
