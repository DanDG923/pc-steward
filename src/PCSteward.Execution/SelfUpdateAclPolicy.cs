using System.Text.RegularExpressions;

namespace PCSteward.Execution;

/// <summary>
/// ADR-032 revision 2 §6 step 5, §7 step 5 and ADR-034: the exact security descriptors of the self-update folders and the
/// installed tree, judged from SDDL text the Windows host reads from a handle. Pure: no file or security API is called here.
/// ACE order is not significant; the ACE set, the protection flag and the owner are.
/// </summary>
internal static partial class SelfUpdateAclPolicy
{
    /// <summary>The descriptor the helper creates each work folder (and its <c>bin</c>) with.</summary>
    internal const string WorkFolderDescriptor = "O:BAG:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;AU)";

    private static readonly string[] WorkFolderAces = ["A;OICI;FA;;;SY", "A;OICI;FA;;;BA", "A;OICI;0x1200a9;;;AU"];
    private static readonly string[] InstallRootAces = ["A;OICI;FA;;;SY", "A;OICI;FA;;;BA", "A;OICI;0x1200a9;;;BU"];
    private static readonly string[] InheritedFileAces = ["A;ID;FA;;;SY", "A;ID;FA;;;BA", "A;ID;0x1200a9;;;BU"];
    private static readonly string[] InheritedFolderAces = ["A;OICIID;FA;;;SY", "A;OICIID;FA;;;BA", "A;OICIID;0x1200a9;;;BU"];
    private static readonly string[] TrustedOwners = ["SY", "BA"];

    /// <summary>Owner SYSTEM or Administrators, a protected DACL of exactly the three work folder ACEs. <paramref name="sddl"/> holds owner and DACL.</summary>
    internal static bool IsProtectedWorkFolder(string? sddl) =>
        TryParse(sddl, out string? owner, out string flags, out string[] aces) && owner is not null && TrustedOwners.Contains(owner, StringComparer.Ordinal) &&
        flags.Contains('P', StringComparison.Ordinal) && SameSet(aces, WorkFolderAces);

    /// <summary>The ADR-034 install root: a protected DACL of exactly SYSTEM and Administrators full control and Users read and execute.</summary>
    internal static bool IsProtectedInstallRoot(string? sddl) =>
        TryParse(sddl, out _, out string flags, out string[] aces) && flags.Contains('P', StringComparison.Ordinal) && SameSet(aces, InstallRootAces);

    /// <summary>An entry below the install root: not protected, and only the three ACEs inherited from the root.</summary>
    internal static bool IsInheritedInstallEntry(string? sddl, bool directory) =>
        TryParse(sddl, out _, out string flags, out string[] aces) && !flags.Contains('P', StringComparison.Ordinal) &&
        SameSet(aces, directory ? InheritedFolderAces : InheritedFileAces);

    private static bool TryParse(string? sddl, out string? owner, out string flags, out string[] aces)
    {
        owner = null;
        flags = string.Empty;
        aces = [];
        if (string.IsNullOrEmpty(sddl)) return false;
        Match match = DescriptorPattern().Match(sddl);
        if (!match.Success) return false;
        owner = match.Groups["owner"].Success ? match.Groups["owner"].Value : null;
        flags = match.Groups["flags"].Value;
        aces = [.. match.Groups["ace"].Captures.Select(capture => capture.Value)];
        return aces.Length > 0;
    }

    private static bool SameSet(string[] actual, string[] expected) =>
        actual.Length == expected.Length && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length &&
        actual.All(ace => expected.Contains(ace, StringComparer.Ordinal));

    [GeneratedRegex(@"^(?:O:(?<owner>[A-Z]{2}|S-1-[0-9-]+))?(?:G:(?:[A-Z]{2}|S-1-[0-9-]+))?D:(?<flags>[PAIR]*)(?:\((?<ace>[^()]*)\))+$", RegexOptions.CultureInvariant)]
    private static partial Regex DescriptorPattern();
}
