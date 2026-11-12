using System.Text.RegularExpressions;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, TM-20/26; ADR-032 revision 2 §6–§7 and the self-updater security review (finding R1/R2): the Windows Installer
/// registration facts the elevated helper needs, as pure functions.
/// <list type="bullet">
/// <item>Windows Installer keys a product under its <em>packed</em> GUID (each group reversed, the last eight bytes nibble-swapped).</item>
/// <item>A program running as the owner can write the owner's own registrations under <c>HKCU\Software\Microsoft\Installer</c>.
/// Windows Installer consults them when it decides how to install or remove a product, so the helper refuses to run
/// <c>msiexec</c> for PC Steward while any of <see cref="PerUserKeys"/> exists.</item>
/// <item>The products installed for the whole machine are listed under <see cref="MachineUpgradeCodeKey"/> in <c>HKLM</c>, which
/// only administrators can write; the helper never asks the merged (machine plus user) list.</item>
/// </list>
/// </summary>
internal static partial class MsiProductRegistration
{
    /// <summary>PC Steward's fixed MSI UpgradeCode (ADR-034, <c>packaging/msi/Package.wxs</c>).</summary>
    internal const string UpgradeCode = "{2AD17330-BAD5-46BE-A409-20814B26673C}";

    /// <summary>
    /// The Windows Installer machine policy key (below <c>HKLM</c>, administrators only) and its <c>DisableUserInstalls</c> value.
    /// Set to 1, Windows Installer ignores every per-user registration, so a same-user program can no longer steer an elevated
    /// install through one (Mythos review S1, confirmed on Windows CI). The helper runs <c>msiexec</c> only while it is set. By
    /// the owner's choice it sets the value just before each <c>msiexec</c> and puts the owner's own state back straight after.
    /// </summary>
    internal const string MachinePolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\Installer";

    /// <summary>The policy value name; see <see cref="MachinePolicyKey"/>.</summary>
    internal const string DisableUserInstallsValue = "DisableUserInstalls";

    /// <summary>True only for the DWORD 1 that tells Windows Installer to ignore per-user installs and registrations.</summary>
    internal static bool IgnoresUserInstalls(object? value) => value is int number && number == 1;

    /// <summary>
    /// The value, next to the policy, that holds the owner's own <c>DisableUserInstalls</c> state while PC Steward has set it, so
    /// that state is put back even when a run stopped before restoring it. Windows Installer ignores it.
    /// </summary>
    internal const string RestoreMarkerValue = "PCStewardRestoreDisableUserInstalls";

    /// <summary>
    /// The machine-wide lock that every PC Steward change of <see cref="DisableUserInstallsValue"/> holds from before it sets
    /// the policy until it has put the owner's state back (Codex review of the scoped policy): a file of this name directly in
    /// <c>%ProgramFiles%</c>, where only administrators can create files, opened with no sharing and deleted on close. The
    /// helper and <c>Install-PCStewardRelease.ps1</c> use the same name, so two runs never restore each other's policy.
    /// </summary>
    internal const string PolicyLockFileName = "PCSteward-installer-policy.lock";

    /// <summary>The marker text for the owner's own state: <c>absent</c>, or <c>dword:</c> and the number.</summary>
    internal static string RestoreMarker(int? previous) =>
        previous is { } number ? "dword:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "absent";

    /// <summary>Reads a marker written by <see cref="RestoreMarker"/>; false for anything else.</summary>
    internal static bool TryReadRestoreMarker(object? value, out int? previous)
    {
        previous = null;
        if (value is not string text) return false;
        if (string.Equals(text, "absent", StringComparison.Ordinal)) return true;
        if (!text.StartsWith("dword:", StringComparison.Ordinal) ||
            !int.TryParse(text.AsSpan(6), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int number) ||
            !string.Equals(RestoreMarker(number), text, StringComparison.Ordinal))
            return false;
        previous = number;
        return true;
    }

    /// <summary>The per-machine product codes registered under PC Steward's UpgradeCode (value names, packed), below <c>HKLM</c>.</summary>
    internal static string MachineUpgradeCodeKey => @"SOFTWARE\Classes\Installer\UpgradeCodes\" + Pack(UpgradeCode);

    [GeneratedRegex(@"\A\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}\z", RegexOptions.CultureInvariant)]
    private static partial Regex BracedGuid();

    [GeneratedRegex(@"\A[0-9A-F]{32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackedGuid();

    /// <summary>A Windows Installer product code: an uppercase GUID in braces.</summary>
    internal static bool IsProductCode(string? value) => value is not null && BracedGuid().IsMatch(value);

    /// <summary>The packed form of an uppercase braced GUID; <see cref="ArgumentException"/> for anything else.</summary>
    internal static string Pack(string bracedGuid)
    {
        if (!IsProductCode(bracedGuid)) throw new ArgumentException("Not an uppercase braced GUID.", nameof(bracedGuid));
        string hex = bracedGuid.Replace("-", "", StringComparison.Ordinal)[1..^1];
        return Reverse(hex[..8]) + Reverse(hex[8..12]) + Reverse(hex[12..16]) + SwapPairs(hex[16..]);
    }

    /// <summary>The braced GUID of a packed one, or null when <paramref name="packed"/> is not 32 uppercase hex digits.</summary>
    internal static string? Unpack(string? packed)
    {
        if (packed is null || !PackedGuid().IsMatch(packed)) return null;
        string hex = Reverse(packed[..8]) + Reverse(packed[8..12]) + Reverse(packed[12..16]) + SwapPairs(packed[16..]);
        return "{" + hex[..8] + "-" + hex[8..12] + "-" + hex[12..16] + "-" + hex[16..20] + "-" + hex[20..] + "}";
    }

    /// <summary>
    /// The current user's Windows Installer keys (below <c>HKCU</c>) that would make Windows Installer treat any of
    /// <paramref name="productCodes"/>, or any PC Steward product, as registered for that user: each product and its features,
    /// and PC Steward's UpgradeCode. Each is listed under <c>Software\Microsoft\Installer</c> and under the user's own classes,
    /// <c>Software\Classes\Installer</c>, which the merged classes view puts in front of the machine's (Mythos review S1).
    /// The caller passes the new package's product code and every one the machine registration lists, since an upgrade acts
    /// on the installed product too.
    /// </summary>
    internal static IReadOnlyList<string> PerUserKeys(IEnumerable<string> productCodes)
    {
        ArgumentNullException.ThrowIfNull(productCodes);
        string[] packed = [.. productCodes.Select(Pack).Distinct(StringComparer.Ordinal)];
        if (packed.Length == 0) throw new ArgumentException("At least one product code is required.", nameof(productCodes));
        List<string> keys = [];
        foreach (string root in PerUserRoots)
        {
            foreach (string product in packed)
            {
                keys.Add(root + @"Products\" + product);
                keys.Add(root + @"Features\" + product);
            }
            keys.Add(root + @"UpgradeCodes\" + Pack(UpgradeCode));
        }
        return keys;
    }

    /// <summary>The two places below <c>HKCU</c> where a per-user Windows Installer registration can live.</summary>
    private static readonly string[] PerUserRoots = [@"Software\Microsoft\Installer\", @"Software\Classes\Installer\"];

    /// <summary>The package's <c>ProductCode</c> property, when its Property table holds exactly one well-formed one; null otherwise.</summary>
    internal static string? ProductCode(IReadOnlyDictionary<string, MsiTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        if (!tables.TryGetValue("Property", out MsiTable? table)) return null;
        int name = -1, value = -1;
        for (int index = 0; index < table.Columns.Count; index++)
        {
            if (string.Equals(table.Columns[index], "Property", StringComparison.Ordinal)) name = index;
            if (string.Equals(table.Columns[index], "Value", StringComparison.Ordinal)) value = index;
        }
        if (name < 0 || value < 0) return null;
        string[] found = [.. table.Rows
            .Where(row => row.Count == table.Columns.Count && string.Equals(row[name], "ProductCode", StringComparison.Ordinal))
            .Select(row => row[value] ?? "")];
        return found.Length == 1 && IsProductCode(found[0]) ? found[0] : null;
    }

    private static string Reverse(string text)
    {
        char[] characters = text.ToCharArray();
        Array.Reverse(characters);
        return new string(characters);
    }

    private static string SwapPairs(string text)
    {
        char[] characters = text.ToCharArray();
        for (int index = 0; index + 1 < characters.Length; index += 2)
            (characters[index], characters[index + 1]) = (characters[index + 1], characters[index]);
        return new string(characters);
    }
}
