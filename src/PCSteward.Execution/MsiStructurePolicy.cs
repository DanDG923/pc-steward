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
/// PC Steward MSI (ADR-034 §1) runs no code of its own, so a package is refused when it has any row in a table that runs code,
/// installs or controls a service, chains or embeds another installer or UI, registers self-registering or ODBC components, or
/// changes environment variables; and when its <c>CustomAction</c> table holds anything other than error-message actions
/// (type 19, which only shows text and stops the install: WiX's refused-downgrade message). Pure: it judges tables already
/// read by <see cref="MsiTableDigest.Read"/>.
/// </summary>
internal static class MsiStructurePolicy
{
    /// <summary>Windows Installer custom action type 19: display an error message and end the installation. It runs no code.</summary>
    internal const int ErrorMessageCustomActionType = 19;

    internal static readonly IReadOnlyList<string> ForbiddenTables =
    [
        "Binary", "ServiceInstall", "ServiceControl", "MsiServiceConfig", "MsiServiceConfigFailureActions", "MsiEmbeddedChainer",
        "MsiEmbeddedUI", "SelfReg", "ODBCDriver", "ODBCTranslator", "ODBCDataSource", "ODBCSourceAttribute", "ODBCAttribute",
        "Environment",
    ];

    internal static MsiStructureVerdict Check(IReadOnlyDictionary<string, MsiTable> tables, out string detail)
    {
        ArgumentNullException.ThrowIfNull(tables);
        detail = "";
        foreach (string forbidden in ForbiddenTables)
        {
            if (tables.TryGetValue(forbidden, out MsiTable? table) && table.Rows.Count > 0)
            {
                detail = forbidden;
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
