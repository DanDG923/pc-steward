using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, TM-04/13; ADR-034 security review H1 and ADR-032 revision 2 §5–§6: the canonical text of every table in an
/// MSI and its SHA-256, exactly as <c>scripts/PCStewardMsiTables.ps1</c> computes it for <c>msi-reference.json</c>. Equal
/// digests mean the signed MSI's tables are the CI build's: the same actions and (no) custom actions, folders, permissions,
/// registry, shortcuts, file names, versions and hashes. Only the values signing is expected to change are "*": the
/// ProductCode property, and the FileSize and MsiFileHash of the two executables and the release manifest. Stream columns
/// are compared by their SHA-256.
/// <para>
/// The database is opened read-only through <c>msi.dll</c>; nothing is installed and no package code runs. Lines are sorted
/// ordinally (tables by name, rows within a table) and joined by LF before hashing; ASCII only by construction.
/// </para>
/// </summary>
/// <summary>One MSI table as read: its columns and its rows (null for a null field, <c>sha256:</c> plus hex for a stream).</summary>
internal sealed record MsiTable(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows);

/// <summary>
/// The canonical lines, their digest and the tables of one MSI database (normalised like the lines, so the ProductCode
/// property reads "*"), and the package's own ProductCode as stored, when it has exactly one well-formed one.
/// </summary>
internal sealed record MsiDatabaseSnapshot(IReadOnlyList<string> Lines, string Sha256, IReadOnlyDictionary<string, MsiTable> Tables, string? ProductCode = null);

internal static class MsiTableDigest
{
    internal static readonly IReadOnlyList<string> SignedLeaves = ["PCSteward.Desktop.exe", "PCSteward.ElevatedHelper.exe", ReleaseManifestPolicy.FileName];

    private const uint Success = 0, NoMoreItems = 259, MoreData = 234;
    private const int ColumnNames = 0, ColumnTypes = 1;
    private static readonly IntPtr ReadOnly = IntPtr.Zero;

    /// <summary>The canonical lines and their SHA-256 (64 uppercase hex characters).</summary>
    internal static (IReadOnlyList<string> Lines, string Sha256) Compute(string databasePath)
    {
        MsiDatabaseSnapshot snapshot = Read(databasePath);
        return (snapshot.Lines, snapshot.Sha256);
    }

    /// <summary>
    /// The canonical lines, their SHA-256 and the tables they were rendered from (after the same normalisation), so that
    /// <see cref="MsiStructurePolicy"/> can judge the package's structure from the one read-only pass.
    /// </summary>
    internal static MsiDatabaseSnapshot Read(string databasePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        List<string> lines = Dump(databasePath, out Dictionary<string, Table> tables, out string? productCode);
        return new(lines, Hash(lines), tables.ToDictionary(pair => pair.Key,
            pair => new MsiTable(pair.Key, pair.Value.Columns, [.. pair.Value.Rows.Select(row => (IReadOnlyList<string?>)row)]), StringComparer.Ordinal), productCode);
    }

    /// <summary>SHA-256 of the lines joined by LF, as ASCII.</summary>
    internal static string Hash(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join('\n', lines))));
    }

    private sealed class Table(string name, string[] columns, string[] types)
    {
        public string Name { get; } = name;
        public string[] Columns { get; } = columns;
        public string[] Types { get; } = types;
        public List<string?[]> Rows { get; } = [];
    }

    private static List<string> Dump(string databasePath, out Dictionary<string, Table> tables, out string? productCode)
    {
        uint status = NativeMethods.MsiOpenDatabaseW(databasePath, ReadOnly, out IntPtr database);
        if (status != Success) throw new InvalidOperationException("MsiOpenDatabase failed with " + status + ".");
        try
        {
            List<string> names = [.. ReadTable(database, "_Tables").Rows.Select(row => row[0] ?? throw new InvalidDataException("A table has no name."))];
            names.Sort(StringComparer.Ordinal);
            tables = new(StringComparer.Ordinal);
            foreach (string name in names) tables[name] = ReadTable(database, name);
            // The real ProductCode, read before normalisation replaces it (the helper removes only this product).
            productCode = MsiProductRegistration.ProductCode(tables.ToDictionary(pair => pair.Key,
                pair => new MsiTable(pair.Key, pair.Value.Columns, [.. pair.Value.Rows.Select(row => (IReadOnlyList<string?>)row.ToArray())]), StringComparer.Ordinal));
            Normalise(tables);
            List<string> lines = [];
            foreach (string name in names)
            {
                Table table = tables[name];
                lines.Add("T " + Quote(name) + " [" + string.Join(",", table.Columns.Select(Quote)) + "] " + Quote(string.Join(",", table.Types)));
                List<string> rendered = [];
                foreach (string?[] row in table.Rows)
                {
                    StringBuilder line = new("R " + Quote(name));
                    foreach (string? value in row) line.Append(' ').Append(value is null ? "null" : Quote(value));
                    rendered.Add(line.ToString());
                }
                rendered.Sort(StringComparer.Ordinal);
                lines.AddRange(rendered);
            }
            return lines;
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(database);
        }
    }

    private static int Column(Dictionary<string, Table> tables, string table, string column)
    {
        if (!tables.TryGetValue(table, out Table? found)) return -1;
        int index = Array.IndexOf(found.Columns, column);
        return index >= 0 ? index : throw new InvalidDataException(table + " has no " + column + " column.");
    }

    private static void Normalise(Dictionary<string, Table> tables)
    {
        int property = Column(tables, "Property", "Property"), value = Column(tables, "Property", "Value");
        if (property >= 0)
        {
            foreach (string?[] row in tables["Property"].Rows)
                if (row[property] == "ProductCode") row[value] = "*";
        }
        HashSet<string> signedKeys = new(StringComparer.Ordinal);
        int key = Column(tables, "File", "File"), fileName = Column(tables, "File", "FileName"), fileSize = Column(tables, "File", "FileSize");
        if (key >= 0)
        {
            foreach (string?[] row in tables["File"].Rows)
            {
                // FileName is "SHORT|Long name" or one name; the long name is the one installed.
                string name = row[fileName] ?? "";
                int bar = name.IndexOf('|', StringComparison.Ordinal);
                if (SignedLeaves.Contains(bar >= 0 ? name[(bar + 1)..] : name, StringComparer.Ordinal))
                {
                    signedKeys.Add(row[key] ?? "");
                    row[fileSize] = "*";
                }
            }
        }
        int hashed = Column(tables, "MsiFileHash", "File_");
        if (hashed >= 0)
        {
            foreach (string?[] row in tables["MsiFileHash"].Rows)
            {
                if (!signedKeys.Contains(row[hashed] ?? "")) continue;
                for (int index = 0; index < row.Length; index++)
                    if (index != hashed) row[index] = "*";
            }
        }
    }

    private static Table ReadTable(IntPtr database, string name)
    {
        if (name.Contains('`', StringComparison.Ordinal)) throw new InvalidDataException("Unexpected table name.");
        string query = "SELECT * FROM `" + name + "`";
        uint status = NativeMethods.MsiDatabaseOpenViewW(database, query, out IntPtr view);
        if (status != Success) throw new InvalidOperationException("MsiDatabaseOpenView failed with " + status + " for: " + query);
        try
        {
            status = NativeMethods.MsiViewExecute(view, IntPtr.Zero);
            if (status != Success) throw new InvalidOperationException("MsiViewExecute failed with " + status + " for: " + query);
            Table table = new(name, ColumnInfo(view, ColumnNames), ColumnInfo(view, ColumnTypes));
            if (table.Columns.Length != table.Types.Length || table.Columns.Length == 0) throw new InvalidDataException("Column information is inconsistent for: " + query);
            while (true)
            {
                status = NativeMethods.MsiViewFetch(view, out IntPtr record);
                if (status == NoMoreItems) break;
                if (status != Success) throw new InvalidOperationException("MsiViewFetch failed with " + status + " for: " + query);
                try
                {
                    if (NativeMethods.MsiRecordGetFieldCount(record) != (uint)table.Columns.Length) throw new InvalidDataException("A record does not match its columns for: " + query);
                    string?[] values = new string?[table.Columns.Length];
                    for (int column = 0; column < values.Length; column++)
                    {
                        uint field = (uint)column + 1;
                        char kind = table.Types[column].Length > 0 ? table.Types[column][0] : '?';
                        if (NativeMethods.MsiRecordIsNull(record, field) != 0) values[column] = null;
                        else if (kind is 'v' or 'V') values[column] = "sha256:" + StreamHash(record, field);
                        else values[column] = ReadString(record, field);
                    }
                    table.Rows.Add(values);
                }
                finally
                {
                    _ = NativeMethods.MsiCloseHandle(record);
                }
            }
            return table;
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(view);
        }
    }

    private static string[] ColumnInfo(IntPtr view, int kind)
    {
        uint status = NativeMethods.MsiViewGetColumnInfo(view, kind, out IntPtr record);
        if (status != Success) throw new InvalidOperationException("MsiViewGetColumnInfo failed with " + status + ".");
        try
        {
            uint count = NativeMethods.MsiRecordGetFieldCount(record);
            string[] values = new string[count];
            for (uint field = 1; field <= count; field++) values[field - 1] = ReadString(record, field);
            return values;
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(record);
        }
    }

    private static string StreamHash(IntPtr record, uint field)
    {
        using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        long total = 0;
        while (true)
        {
            uint size = (uint)buffer.Length;
            uint status = NativeMethods.MsiRecordReadStream(record, field, buffer, ref size);
            if (status != Success) throw new InvalidOperationException("MsiRecordReadStream failed with " + status + ".");
            if (size == 0) break;
            total += size;
            if (total > 64L * 1024 * 1024) throw new InvalidDataException("An MSI table stream is larger than its bound.");
            sha.AppendData(buffer, 0, (int)size);
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private static string ReadString(IntPtr record, uint field)
    {
        uint size = 256;
        char[] value = new char[size];
        uint status = NativeMethods.MsiRecordGetStringW(record, field, value, ref size);
        if (status == MoreData)
        {
            if (size > 1024 * 1024) throw new InvalidDataException("An MSI value is larger than its bound.");
            size++;
            value = new char[size];
            status = NativeMethods.MsiRecordGetStringW(record, field, value, ref size);
        }
        if (status != Success) throw new InvalidOperationException("MsiRecordGetString failed with " + status + ".");
        return new string(value, 0, (int)size);
    }

    /// <summary>The same quoting as the script: backslash-escaped quote and backslash, \uXXXX outside printable ASCII.</summary>
    internal static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        StringBuilder quoted = new("\"");
        foreach (char character in value)
        {
            if (character is '"' or '\\') quoted.Append('\\').Append(character);
            else if (character is < (char)0x20 or > (char)0x7E) quoted.Append("\\u").Append(((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
            else quoted.Append(character);
        }
        return quoted.Append('"').ToString();
    }

    private static class NativeMethods
    {
        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint MsiOpenDatabaseW(string databasePath, IntPtr persist, out IntPtr database);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);

        [DllImport("msi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint MsiViewExecute(IntPtr view, IntPtr record);

        [DllImport("msi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint MsiViewFetch(IntPtr view, out IntPtr record);

        [DllImport("msi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint MsiViewGetColumnInfo(IntPtr view, int columnInfo, out IntPtr record);

        [DllImport("msi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint MsiRecordGetFieldCount(IntPtr record);

        [DllImport("msi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int MsiRecordIsNull(IntPtr record, uint field);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint MsiRecordGetStringW(IntPtr record, uint field, [Out] char[] value, ref uint size);

        [DllImport("msi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint MsiRecordReadStream(IntPtr record, uint field, [Out] byte[] buffer, ref uint size);

        [DllImport("msi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint MsiCloseHandle(IntPtr handle);
    }
}
