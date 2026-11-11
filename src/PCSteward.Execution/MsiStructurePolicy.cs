using System.Globalization;

namespace PCSteward.Execution;

/// <summary>Why <see cref="MsiStructurePolicy"/> refused a package.</summary>
internal enum MsiStructureVerdict
{
    Allowed,
    ForbiddenTable,
    CustomActionNotAllowed,
    UnreadableTable,
}

/// <summary>
/// FR-UPD-15, TM-20; ADR-032 revision 2 §6 step 4: a rule about what an MSI may contain that does not depend on the release
/// check file, applied by the elevated helper before <c>msiexec</c> touches the package (even the administrative extract). The
/// PC Steward MSI (ADR-034 §1) runs no code of its own and writes nothing but its files, its folder's permissions and one
/// Start menu shortcut. Security review R5: the rule is an allow-list. A package is refused when any table outside
/// <see cref="AllowedTables"/> has a row (so registry, COM, services, self-registration, file moves and removals, environment
/// changes, embedded installers or UI and any table Windows Installer adds later are all refused), and when its
/// <c>CustomAction</c> table holds anything other than error-message actions (type 19, which only shows text and stops the
/// install: WiX's refused-downgrade message). Pure: it judges tables already read by <see cref="MsiTableDigest.Read"/>.
/// </summary>
internal static class MsiStructurePolicy
{
    /// <summary>Windows Installer custom action type 19: display an error message and end the installation. It runs no code.</summary>
    internal const int ErrorMessageCustomActionType = 19;

    /// <summary>The tables the PC Steward MSI is built with (<c>packaging/msi/Package.wxs</c>); no other table may hold a row.</summary>
    internal static readonly IReadOnlyList<string> AllowedTables =
    [
        "_Validation", "AdminExecuteSequence", "AdminUISequence", "AdvtExecuteSequence", "AppSearch", "Component", "CreateFolder",
        "CustomAction", "Directory", "DrLocator", "Feature", "FeatureComponents", "File", "Icon", "InstallExecuteSequence",
        "InstallUISequence", "LaunchCondition", "Media", "MsiFileHash", "MsiLockPermissionsEx", "Property", "Shortcut", "Upgrade",
    ];

    internal static MsiStructureVerdict Check(IReadOnlyDictionary<string, MsiTable> tables, out string detail)
    {
        ArgumentNullException.ThrowIfNull(tables);
        detail = "";
        foreach (MsiTable table in tables.Values.OrderBy(table => table.Name, StringComparer.Ordinal))
        {
            if (table.Rows.Count > 0 && !AllowedTables.Contains(table.Name, StringComparer.Ordinal))
            {
                detail = table.Name;
                return MsiStructureVerdict.ForbiddenTable;
            }
        }
        if (!tables.TryGetValue("CustomAction", out MsiTable? actions)) return MsiStructureVerdict.Allowed;
        int name = IndexOf(actions, "Action"), type = IndexOf(actions, "Type"), source = IndexOf(actions, "Source");
        if (name < 0 || type < 0 || source < 0)
        {
            detail = "CustomAction";
            return MsiStructureVerdict.UnreadableTable;
        }
        foreach (IReadOnlyList<string?> row in actions.Rows)
        {
            if (row.Count != actions.Columns.Count) { detail = "CustomAction"; return MsiStructureVerdict.UnreadableTable; }
            // Exactly type 19 with no option bits, and no source: an error message cannot name a binary, file, property or folder.
            if (!int.TryParse(row[type], NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value != ErrorMessageCustomActionType ||
                !string.IsNullOrEmpty(row[source]))
            {
                detail = row[name] ?? "";
                return MsiStructureVerdict.CustomActionNotAllowed;
            }
        }
        return MsiStructureVerdict.Allowed;
    }

    private static int IndexOf(MsiTable table, string column)
    {
        for (int index = 0; index < table.Columns.Count; index++)
            if (string.Equals(table.Columns[index], column, StringComparison.Ordinal)) return index;
        return -1;
    }
}
