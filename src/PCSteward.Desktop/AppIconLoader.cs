using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using PCSteward.Application;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Deployment;
using Windows.Storage.Streams;

namespace PCSteward.Desktop;

/// <summary>
/// ADR-035 (accepted, option A; UI review 2026-11-13 item 20): reads an app's own icon for a row that is on screen. It runs in
/// the desktop app only, without administrator rights. A classic app's icon is the <c>DisplayIcon</c> of the uninstall entry the
/// scan recorded: the path must pass <see cref="AppIconRules"/> (a full path on a fixed local drive that goes through no link),
/// the file is opened for reading only and one image is taken out of its bytes by <see cref="AppIconFile"/>. The file is never
/// run, never loaded as code and never handed to the Windows shell. A Store-type app's logo is the one Windows' package API
/// returns. Icons are kept in memory for the session, up to a limit; nothing is written or sent, and the <c>DisplayIcon</c>
/// text is not kept. Any refusal or error gives no icon, and the row keeps its two-letter tile.
/// </summary>
internal sealed class AppIconLoader
{
    private const string UninstallEntries = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";
    private const ulong MaximumLogoBytes = 4 * 1024 * 1024;
    // Used on the UI thread only.
    private readonly Dictionary<string, ImageSource?> cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<ImageSource?>> pending = new(StringComparer.Ordinal);
    // ADR-035 rule 5: a few reads at a time. Counted on the UI thread; a read past the limit waits for its turn.
    private readonly Queue<TaskCompletionSource> waitingReads = new();
    private int runningReads;

    /// <summary>True when this icon was already read this session; <paramref name="icon"/> is null when the app has none to show.</summary>
    public bool TryGetCached(AppIconSource source, int pixels, out ImageSource? icon) => cache.TryGetValue(Key(source, pixels), out icon);

    /// <summary>The icon, or null. Call on the UI thread; the file is read off it. Never throws.</summary>
    public Task<ImageSource?> LoadAsync(AppIconSource source, int pixels)
    {
        string key = Key(source, pixels);
        if (cache.TryGetValue(key, out ImageSource? cached)) return Task.FromResult(cached);
        if (pending.TryGetValue(key, out Task<ImageSource?>? running)) return running;
        Task<ImageSource?> load = LoadAndRememberAsync(source, pixels, key);
        if (!load.IsCompleted) pending[key] = load;
        return load;
    }

    private static string Key(AppIconSource source, int pixels) => $"{pixels}|{source.CacheKey}";

    private async Task<ImageSource?> LoadAndRememberAsync(AppIconSource source, int pixels, string key)
    {
        ImageSource? icon;
        try
        {
            await TurnAsync();
            try { icon = source.Kind == AppIconSourceKind.StorePackage ? await ReadStoreLogoAsync(source.Identity, pixels) : await ReadClassicIconAsync(source, pixels); }
            finally { EndTurn(); }
        }
        // A read that was too slow is tried again the next time the row is shown; it is not remembered as "no icon".
        catch (TimeoutException) { pending.Remove(key); return null; }
        // ADR-035 rule 7: any other refusal or error means no icon, and is not reported.
        catch (Exception) { icon = null; }
        pending.Remove(key);
        if (cache.Count < AppIconRules.MaximumCachedIcons) cache[key] = icon;
        return icon;
    }

    private Task TurnAsync()
    {
        if (runningReads < AppIconRules.MaximumConcurrentReads) { runningReads++; return Task.CompletedTask; }
        TaskCompletionSource turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        waitingReads.Enqueue(turn);
        return turn.Task;
    }

    /// <summary>Hands this read's place to the next one waiting, or frees it.</summary>
    private void EndTurn()
    {
        if (waitingReads.TryDequeue(out TaskCompletionSource? next)) next.SetResult();
        else runningReads--;
    }

    private static async Task<ImageSource?> ReadClassicIconAsync(AppIconSource source, int pixels)
    {
        byte[]? iconFile = await Task.Run(() => ReadClassicIconFile(source, pixels)).WaitAsync(AppIconRules.ReadTimeout);
        if (iconFile is null) return null;
        using InMemoryRandomAccessStream stream = new();
        await stream.WriteAsync(iconFile.AsBuffer());
        stream.Seek(0);
        BitmapImage image = new() { DecodePixelType = DecodePixelType.Physical, DecodePixelWidth = pixels, DecodePixelHeight = pixels };
        await image.SetSourceAsync(stream);
        return image;
    }

    /// <summary>Runs off the UI thread. Reads one registry value and, if every rule passes, one icon image from one local file.</summary>
    private static byte[]? ReadClassicIconFile(AppIconSource source, int pixels)
    {
        if (!AppIconRules.IsEntryName(source.Identity)) return null;
        string? displayIcon;
        using (RegistryKey hive = RegistryKey.OpenBaseKey(source.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, source.View32 ? RegistryView.Registry32 : RegistryView.Registry64))
        using (RegistryKey? entry = hive.OpenSubKey(UninstallEntries + source.Identity, writable: false))
            displayIcon = entry?.GetValue(AppIconRules.RegistryValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (!AppIconRules.TryParseDisplayIcon(displayIcon, out string path, out int index)) return null;
        if (!AppIconRules.IsAllowedDrive(new DriveInfo(AppIconRules.DriveRoot(path)).DriveType)) return null;
        // Every folder on the way, then the file: a link of any kind is refused rather than followed.
        IReadOnlyList<string> steps = AppIconRules.PathSteps(path);
        for (int step = 0; step < steps.Count; step++)
            if (!AppIconRules.MayPassThrough(File.GetAttributes(steps[step]), isLastStep: step == steps.Count - 1)) return null;
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.None);
        return AppIconFile.Read(file, AppIconRules.IsIconFile(path), index, pixels);
    }

    private static async Task<ImageSource?> ReadStoreLogoAsync(string packageFamilyName, int pixels)
    {
        RandomAccessStreamReference? logo = await Task.Run(() =>
        {
            Package? package = new PackageManager().FindPackagesForUser(string.Empty, packageFamilyName).FirstOrDefault(candidate => !candidate.IsFramework && !candidate.IsResourcePackage);
            return package?.GetLogoAsRandomAccessStreamReference(new Size(pixels, pixels));
        }).WaitAsync(AppIconRules.ReadTimeout);
        if (logo is null) return null;
        using IRandomAccessStreamWithContentType stream = await logo.OpenReadAsync().AsTask().WaitAsync(AppIconRules.ReadTimeout);
        if (stream.Size == 0 || stream.Size > MaximumLogoBytes) return null;
        BitmapImage image = new() { DecodePixelType = DecodePixelType.Physical, DecodePixelWidth = pixels };
        await image.SetSourceAsync(stream);
        return image;
    }
}
