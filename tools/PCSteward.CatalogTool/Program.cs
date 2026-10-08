using Microsoft.Data.Sqlite;
using PCSteward.Catalog;

namespace PCSteward.CatalogTool;

// validate <catalog.json>          Strict validation; prints version, entry count and SHA-256, or the error codes.
// seed <pcsteward.db> <out.json>   Opens the database READ-ONLY and writes a draft catalog for owner review.
//                                  Refuses to overwrite an existing file.
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["validate", string catalog] => Validate(catalog),
                ["seed", string database, string output] => Seed(database, output),
                _ => Usage()
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            Console.Error.WriteLine(exception.GetType().Name + ": " + exception.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: validate <catalog.json> | seed <pcsteward.db> <out.json>");
        return 64;
    }

    private static int Validate(string path)
    {
        CatalogLoadResult result = CatalogValidator.Validate(File.ReadAllBytes(path));
        if (!result.IsLoaded)
        {
            Console.Error.WriteLine("Catalog INVALID (catalog features would be disabled):");
            foreach (string error in result.Errors) Console.Error.WriteLine("  " + error);
            return 1;
        }

        PersonalCatalog catalog = result.Catalog!;
        Console.WriteLine($"Catalog valid: version {catalog.CatalogVersion}, {catalog.Entries.Count} entries, sha256 {catalog.Sha256}");
        return 0;
    }

    private static int Seed(string databasePath, string outputPath)
    {
        if (File.Exists(outputPath)) throw new IOException("The output file already exists; choose a new path.");
        SqliteConnectionStringBuilder builder = new() { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        List<CatalogSeedInstance> instances = [];
        using (SqliteConnection connection = new(builder.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT p.display_name, i.publisher, i.provider, i.provider_identity, i.source_id FROM installation_instances i JOIN canonical_products p ON p.id = i.product_id;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                instances.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        CatalogSeedResult result = CatalogSeedProposer.Propose(instances, DateOnly.FromDateTime(DateTime.UtcNow));
        using (FileStream stream = new(outputPath, FileMode.CreateNew, FileAccess.Write))
        using (StreamWriter writer = new(stream))
        {
            writer.Write(result.ProposalJson);
            writer.Write('\n');
        }

        Console.WriteLine($"Draft written: {result.ProposedEntries} proposed entries from {instances.Count} installed instances; {result.Skipped.Count} skipped.");
        foreach (string reason in result.Skipped.Distinct(StringComparer.Ordinal)) Console.WriteLine("  skipped: " + reason);
        Console.WriteLine("Review names, publishers, categories, scopes and patterns before merging into catalog/pcsteward-catalog.v1.json.");
        return 0;
    }
}
