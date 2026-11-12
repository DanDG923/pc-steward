using Microsoft.Win32;

namespace PCSteward.Inventory.Windows;

public enum RegistryViewKind { Machine64, Machine32, CurrentUser }

public sealed record RegistryUninstallEntry(string KeyName, IReadOnlyDictionary<string, object?> Values, string? ReadWarning = null);
public sealed record RegistryInventoryRecord(string Provider, string KeyName, string? DisplayName, string? DisplayVersion, string? Publisher, string? InstallDate, string Scope, string Architecture);
public sealed record RegistryInventoryResult(IReadOnlyCollection<RegistryInventoryRecord> Records, IReadOnlyCollection<string> Warnings);

public interface IUninstallRegistry
{
    IEnumerable<RegistryUninstallEntry> ReadUninstallEntries(RegistryViewKind view);
}

public sealed class WindowsUninstallRegistry : IUninstallRegistry
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>The only values inventory reads from an uninstall entry (Codex security scan finding 8: never every value).</summary>
    internal static readonly string[] InventoryValueNames = ["DisplayName", "DisplayVersion", "Publisher", "InstallDate"];

    /// <summary>
    /// Lazy: one entry is opened and read per step, so the caller's budget and cancellation checks stop the walk between
    /// entries instead of after the whole view has been read.
    /// </summary>
    public IEnumerable<RegistryUninstallEntry> ReadUninstallEntries(RegistryViewKind view)
    {
        RegistryHive hive = view == RegistryViewKind.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        RegistryView registryView = view == RegistryViewKind.Machine32 ? RegistryView.Registry32 : RegistryView.Registry64;
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, registryView);
        using RegistryKey? uninstallKey = baseKey.OpenSubKey(UninstallPath, writable: false);
        if (uninstallKey is null) yield break;
        foreach (string keyName in uninstallKey.GetSubKeyNames())
        {
            RegistryUninstallEntry? entry = ReadEntry(uninstallKey, keyName);
            if (entry is not null) yield return entry;
        }
    }

    private static RegistryUninstallEntry? ReadEntry(RegistryKey uninstallKey, string keyName)
    {
        try
        {
            using RegistryKey? entry = uninstallKey.OpenSubKey(keyName, writable: false);
            if (entry is null) return null;
            Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (string name in InventoryValueNames)
            {
                object? value = entry.GetValue(name);
                if (value is not null) values[name] = value;
            }
            return new RegistryUninstallEntry(keyName, values);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        {
            return new RegistryUninstallEntry(keyName, new Dictionary<string, object?>(), "unreadable");
        }
    }
}

public sealed class RegistryInventoryProvider
{
    private const int MaximumFieldLength = 1024;
    private readonly IUninstallRegistry registry;
    public RegistryInventoryProvider(IUninstallRegistry registry) => this.registry = registry;

    /// <summary>
    /// The most uninstall entries read from one registry view (Codex security scan finding 8). A real PC has a few hundred;
    /// past this the view is reported as over budget and the rest is not read.
    /// </summary>
    public const int MaximumEntriesPerView = 4096;

    public RegistryInventoryResult ReadAll(CancellationToken cancellationToken = default)
    {
        List<RegistryInventoryRecord> records = [];
        List<string> warnings = [];
        foreach (RegistryViewKind view in Enum.GetValues<RegistryViewKind>())
        {
            try
            {
                int read = 0;
                foreach (RegistryUninstallEntry entry in registry.ReadUninstallEntries(view))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++read > MaximumEntriesPerView)
                    {
                        warnings.Add($"{view}:entry-budget-exceeded");
                        break;
                    }
                    if (entry.ReadWarning is not null)
                    {
                        warnings.Add($"{view}:{Text(entry.KeyName)}:{entry.ReadWarning}");
                        continue;
                    }
                    try { records.Add(ToRecord(entry, view)); }
                    catch (Exception exception) when (exception is ArgumentException or InvalidCastException) { warnings.Add($"{view}:{entry.KeyName}:malformed"); }
                }
            }
            catch (UnauthorizedAccessException) { warnings.Add($"{view}:access-denied"); }
            catch (System.Security.SecurityException) { warnings.Add($"{view}:security-error"); }
            catch (IOException) { warnings.Add($"{view}:io-error"); }
        }
        return new RegistryInventoryResult(records, warnings);
    }

    private static RegistryInventoryRecord ToRecord(RegistryUninstallEntry entry, RegistryViewKind view) => new("registry", Text(entry.KeyName), Value(entry, "DisplayName"), Value(entry, "DisplayVersion"), Value(entry, "Publisher"), Value(entry, "InstallDate"), view == RegistryViewKind.CurrentUser ? "User" : "Machine", view == RegistryViewKind.Machine32 ? "x86" : "x64");
    private static string? Value(RegistryUninstallEntry entry, string name) => entry.Values.TryGetValue(name, out object? value) ? Text(value?.ToString()) : null;
    private static string Text(string? text) => string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim()[..Math.Min(text.Trim().Length, MaximumFieldLength)];
}
