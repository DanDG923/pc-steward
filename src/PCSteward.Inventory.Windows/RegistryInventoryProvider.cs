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
    public IEnumerable<RegistryUninstallEntry> ReadUninstallEntries(RegistryViewKind view)
    {
        List<RegistryUninstallEntry> entries = [];
        RegistryHive hive = view == RegistryViewKind.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        RegistryView registryView = view == RegistryViewKind.Machine32 ? RegistryView.Registry32 : RegistryView.Registry64;
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, registryView);
        using RegistryKey? uninstallKey = baseKey.OpenSubKey(UninstallPath, writable: false);
        if (uninstallKey is null) return entries;
        foreach (string keyName in uninstallKey.GetSubKeyNames())
        {
            try
            {
                using RegistryKey? entry = uninstallKey.OpenSubKey(keyName, writable: false);
                if (entry is null) continue;
                Dictionary<string, object?> values = entry.GetValueNames().ToDictionary(name => name, name => entry.GetValue(name), StringComparer.OrdinalIgnoreCase);
                entries.Add(new RegistryUninstallEntry(keyName, values));
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
            {
                entries.Add(new RegistryUninstallEntry(keyName, new Dictionary<string, object?>(), "unreadable"));
            }
        }
        return entries;
    }
}

public sealed class RegistryInventoryProvider
{
    private const int MaximumFieldLength = 1024;
    private readonly IUninstallRegistry registry;
    public RegistryInventoryProvider(IUninstallRegistry registry) => this.registry = registry;

    public RegistryInventoryResult ReadAll()
    {
        List<RegistryInventoryRecord> records = [];
        List<string> warnings = [];
        foreach (RegistryViewKind view in Enum.GetValues<RegistryViewKind>())
        {
            try
            {
                foreach (RegistryUninstallEntry entry in registry.ReadUninstallEntries(view))
                {
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
