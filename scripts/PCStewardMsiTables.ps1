# ADR-034 security review H1 (FR-UPD-15, TM-04/13): a canonical, normalised text form of every table in an MSI, and its
# SHA-256. Dot-source only. Windows PowerShell 5.1 and pwsh 7 both run it; it reads the package and changes nothing.
#   Get-PCStewardMsiTables  the canonical lines and their SHA-256
#
# The release-payload workflow builds the MSI once, unsigned, from the CI payload, and publishes this digest in the artefact
# (msi-reference.json). The signed MSI built on the owner's PC must give the same digest, so its tables (what Windows
# Installer does: actions, custom actions, folders, permissions, registry, shortcuts, file names, versions and hashes) are
# the CI build's. Only the values signing is expected to change are normalised to "*":
#   - Property ProductCode (WiX makes a new one for every build);
#   - FileSize of the two signed executables and of the release manifest (signatures and hashes add bytes);
#   - MsiFileHash of those three files (an unversioned release manifest is hashed).
# The files themselves are checked after install against the CI candidate manifest; streams in tables (for example the
# Icon) are compared by their SHA-256.
$script:PCStewardMsiSignedLeaves = @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe', 'PCSteward.ReleaseManifest.json')

if (-not ('PCStewardMsiTables' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

public static class PCStewardMsiTables
{
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string databasePath, IntPtr persist, out IntPtr database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewGetColumnInfo(IntPtr view, int columnInfo, out IntPtr record);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiRecordGetFieldCount(IntPtr record);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern int MsiRecordIsNull(IntPtr record, uint field);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiRecordGetStringW(IntPtr record, uint field, StringBuilder value, ref uint size);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiRecordReadStream(IntPtr record, uint field, byte[] buffer, ref uint size);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(IntPtr handle);

    private const uint Success = 0, NoMoreItems = 259, MoreData = 234;
    private const int ColumnNames = 0, ColumnTypes = 1;

    private sealed class Table { public string Name; public string[] Columns; public string[] Types; public List<string[]> Rows = new List<string[]>(); }

    // Every table, sorted by name; each as one line of column names and types, then one line per row, rows sorted. Values
    // are quoted, escaped strings; a stream column holds "sha256:" and the stream's hash; a null value is null. The values
    // signing is expected to change are "*" (see the script header); signedLeaves are the installed file names concerned.
    public static string[] Dump(string databasePath, string[] signedLeaves)
    {
        IntPtr database;
        uint status = MsiOpenDatabaseW(databasePath, IntPtr.Zero, out database);
        if (status != Success) { throw new InvalidOperationException("MsiOpenDatabase failed with " + status + "."); }
        try
        {
            List<string> names = new List<string>();
            foreach (string[] row in Read(database, "_Tables").Rows) { names.Add(row[0]); }
            names.Sort(StringComparer.Ordinal);
            Dictionary<string, Table> tables = new Dictionary<string, Table>(StringComparer.Ordinal);
            foreach (string name in names) { tables[name] = Read(database, name); }
            Normalise(tables, new HashSet<string>(signedLeaves, StringComparer.Ordinal));
            List<string> lines = new List<string>();
            foreach (string name in names)
            {
                Table table = tables[name];
                string[] quoted = new string[table.Columns.Length];
                for (int index = 0; index < quoted.Length; index++) { quoted[index] = Quote(table.Columns[index]); }
                lines.Add("T " + Quote(name) + " [" + String.Join(",", quoted) + "] " + Quote(String.Join(",", table.Types)));
                List<string> rendered = new List<string>();
                foreach (string[] row in table.Rows)
                {
                    StringBuilder line = new StringBuilder("R " + Quote(name));
                    foreach (string value in row) { line.Append(' ').Append(value == null ? "null" : Quote(value)); }
                    rendered.Add(line.ToString());
                }
                rendered.Sort(StringComparer.Ordinal);
                lines.AddRange(rendered);
            }
            return lines.ToArray();
        }
        finally { MsiCloseHandle(database); }
    }

    private static int Column(Dictionary<string, Table> tables, string table, string column)
    {
        Table found;
        if (!tables.TryGetValue(table, out found)) { return -1; }
        int index = Array.IndexOf(found.Columns, column);
        if (index < 0) { throw new InvalidDataException(table + " has no " + column + " column."); }
        return index;
    }

    private static void Normalise(Dictionary<string, Table> tables, HashSet<string> signedLeaves)
    {
        int property = Column(tables, "Property", "Property"), value = Column(tables, "Property", "Value");
        if (property >= 0) { foreach (string[] row in tables["Property"].Rows) { if (row[property] == "ProductCode") { row[value] = "*"; } } }
        HashSet<string> signedKeys = new HashSet<string>(StringComparer.Ordinal);
        int key = Column(tables, "File", "File"), fileName = Column(tables, "File", "FileName"), fileSize = Column(tables, "File", "FileSize");
        if (key >= 0)
        {
            foreach (string[] row in tables["File"].Rows)
            {
                // FileName is "SHORT|Long name" or one name; the long name is the one installed.
                string name = row[fileName] ?? "";
                int bar = name.IndexOf('|');
                if (signedLeaves.Contains(bar >= 0 ? name.Substring(bar + 1) : name)) { signedKeys.Add(row[key]); row[fileSize] = "*"; }
            }
        }
        int hashed = Column(tables, "MsiFileHash", "File_");
        if (hashed >= 0)
        {
            foreach (string[] row in tables["MsiFileHash"].Rows)
            {
                if (!signedKeys.Contains(row[hashed])) { continue; }
                for (int index = 0; index < row.Length; index++) { if (index != hashed) { row[index] = "*"; } }
            }
        }
    }

    private static Table Read(IntPtr database, string name)
    {
        if (name.IndexOf('`') >= 0) { throw new InvalidDataException("Unexpected table name."); }
        string query = "SELECT * FROM `" + name + "`";
        IntPtr view;
        uint status = MsiDatabaseOpenViewW(database, query, out view);
        if (status != Success) { throw new InvalidOperationException("MsiDatabaseOpenView failed with " + status + " for: " + query); }
        try
        {
            status = MsiViewExecute(view, IntPtr.Zero);
            if (status != Success) { throw new InvalidOperationException("MsiViewExecute failed with " + status + " for: " + query); }
            Table table = new Table();
            table.Name = name; table.Columns = ColumnInfo(view, ColumnNames); table.Types = ColumnInfo(view, ColumnTypes);
            if (table.Columns.Length != table.Types.Length || table.Columns.Length == 0) { throw new InvalidDataException("Column information is inconsistent for: " + query); }
            while (true)
            {
                IntPtr record;
                status = MsiViewFetch(view, out record);
                if (status == NoMoreItems) { break; }
                if (status != Success) { throw new InvalidOperationException("MsiViewFetch failed with " + status + " for: " + query); }
                try
                {
                    if (MsiRecordGetFieldCount(record) != (uint)table.Columns.Length) { throw new InvalidDataException("A record does not match its columns for: " + query); }
                    string[] values = new string[table.Columns.Length];
                    for (int column = 0; column < values.Length; column++)
                    {
                        uint field = (uint)column + 1;
                        char kind = table.Types[column].Length > 0 ? table.Types[column][0] : '?';
                        if (MsiRecordIsNull(record, field) != 0) { values[column] = null; }
                        else if (kind == 'v' || kind == 'V') { values[column] = "sha256:" + StreamHash(record, field); }
                        else { values[column] = ReadString(record, field); }
                    }
                    table.Rows.Add(values);
                }
                finally { MsiCloseHandle(record); }
            }
            return table;
        }
        finally { MsiCloseHandle(view); }
    }

    private static string[] ColumnInfo(IntPtr view, int kind)
    {
        IntPtr record;
        uint status = MsiViewGetColumnInfo(view, kind, out record);
        if (status != Success) { throw new InvalidOperationException("MsiViewGetColumnInfo failed with " + status + "."); }
        try
        {
            uint count = MsiRecordGetFieldCount(record);
            string[] values = new string[count];
            for (uint field = 1; field <= count; field++) { values[field - 1] = ReadString(record, field); }
            return values;
        }
        finally { MsiCloseHandle(record); }
    }

    private static string StreamHash(IntPtr record, uint field)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] buffer = new byte[65536];
            while (true)
            {
                uint size = (uint)buffer.Length;
                uint status = MsiRecordReadStream(record, field, buffer, ref size);
                if (status != Success) { throw new InvalidOperationException("MsiRecordReadStream failed with " + status + "."); }
                if (size == 0) { break; }
                sha.TransformBlock(buffer, 0, (int)size, null, 0);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "");
        }
    }

    private static string ReadString(IntPtr record, uint field)
    {
        uint size = 256;
        StringBuilder value = new StringBuilder((int)size);
        uint status = MsiRecordGetStringW(record, field, value, ref size);
        if (status == MoreData)
        {
            size++;
            value = new StringBuilder((int)size);
            status = MsiRecordGetStringW(record, field, value, ref size);
        }
        if (status != Success) { throw new InvalidOperationException("MsiRecordGetString failed with " + status + "."); }
        return value.ToString();
    }

    public static string Quote(string value)
    {
        StringBuilder quoted = new StringBuilder("\"");
        foreach (char character in value)
        {
            if (character == '"' || character == '\\') { quoted.Append('\\').Append(character); }
            else if (character < 0x20 || character > 0x7E) { quoted.Append("\\u").Append(((int)character).ToString("x4")); }
            else { quoted.Append(character); }
        }
        return quoted.Append('"').ToString();
    }
}
'@
}

# The canonical lines of every table in $Path, with the values signing changes normalised (see the header), and the
# SHA-256 of those lines joined by LF (ASCII only by construction).
function Get-PCStewardMsiTables {
    param([Parameter(Mandatory)][string]$Path)
    $lines = [PCStewardMsiTables]::Dump([IO.Path]::GetFullPath($Path), [string[]]$script:PCStewardMsiSignedLeaves)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::ASCII.GetBytes([string]::Join("`n", $lines)))).Replace('-', '') } finally { $sha.Dispose() }
    [pscustomobject]@{ Lines = $lines; Sha256 = $hash }
}
