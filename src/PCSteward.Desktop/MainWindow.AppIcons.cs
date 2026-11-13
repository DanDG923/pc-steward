using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using PCSteward.Application;
using Windows.Foundation;

namespace PCSteward.Desktop;

public sealed partial class MainWindow
{
    // ADR-035: icons read this session, in memory only.
    private readonly AppIconLoader appIcons = new();

    /// <summary>One tile's place on screen, and the icon read that starts when the tile is on screen (ADR-035 rule 1).</summary>
    private sealed class AppTileWatch
    {
        public bool OnScreen { get; set; }
        public Action? ReadWhenOnScreen { get; set; }
    }

    /// <summary>
    /// UI review 2026-11-13 (item 20; ADR-035): an app's tile. It starts as the two letters and shows the app's own icon when
    /// one can be read under the ADR's rules. An icon already read this session is shown at once; otherwise it is read only
    /// when the tile comes on screen, so a row below the window's edge or in a closed group reads nothing. The icon is
    /// decoration: the app's name is always beside it.
    /// </summary>
    private Border AppTile(string name, InventoryRow? row)
    {
        Border tile = StewardVisuals.Avatar(StewardPresentation.Initials(name));
        if (row is null || AppIconRules.SourceFor(row.Instances) is not { } source) return tile;
        void Show(ImageSource icon) { tile.Background = null; tile.Child = StewardVisuals.AppIconImage(icon); }
        if (!ShowAppIconIfAlreadyRead(source, Show)) WatchTile(tile).ReadWhenOnScreen = () => ShowAppIcon(source, Show);
        return tile;
    }

    /// <summary>The tile of the app with this key in the saved app list; its two letters when the key is not in the list.</summary>
    private Border AppTile(string name, string stableProductKey) =>
        AppTile(name, viewModel.AllRows.FirstOrDefault(row => string.Equals(row.StableProductKey, stableProductKey, StringComparison.Ordinal)));

    /// <summary>
    /// Installed apps builds its rows from a template, builds more of them than the window shows and reuses them as the list
    /// scrolls. Each row that is given an app starts again from its letters, and its icon is read only when the row is on
    /// screen. A row that has been reused for another app by the time the icon arrives is left alone.
    /// </summary>
    private void ShowAppIconsInRows(ListView list) => list.ContainerContentChanging += (_, args) =>
    {
        if (args.ItemContainer.ContentTemplateRoot is not Grid { Children.Count: > 0 } rowGrid || rowGrid.Children[0] is not Grid { Children.Count: 2 } tile
            || tile.Children[0] is not Border letters || tile.Children[1] is not Image image) return;
        if (tile.Tag is not AppTileWatch watch) tile.Tag = watch = WatchTile(tile);
        watch.ReadWhenOnScreen = null;
        image.Source = null;
        image.Visibility = Visibility.Collapsed;
        letters.Visibility = Visibility.Visible;
        if (args.InRecycleQueue || args.Item is not ApplicationListItem item || AppIconRules.SourceFor(item.Row.Instances) is not { } source) return;
        SelectorItem container = args.ItemContainer;
        void Show(ImageSource icon)
        {
            if (!ReferenceEquals(container.Content, item)) return;
            image.Source = icon;
            image.Visibility = Visibility.Visible;
            letters.Visibility = Visibility.Collapsed;
        }
        if (ShowAppIconIfAlreadyRead(source, Show)) return;
        Action read = () => ShowAppIcon(source, Show);
        watch.ReadWhenOnScreen = read;
        // A row given another app where it already is (a new filter or sort) gets no change of place to start its read, so
        // ask once more after this layout: by then a row that moved off screen has been told so.
        if (watch.OnScreen)
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => { if (watch.OnScreen && ReferenceEquals(watch.ReadWhenOnScreen, read)) StartWaitingRead(watch); });
    };

    /// <summary>Follows whether any part of the tile is inside what the window shows, and starts the waiting read when it is.</summary>
    private static AppTileWatch WatchTile(FrameworkElement tile)
    {
        AppTileWatch watch = new();
        tile.EffectiveViewportChanged += (sender, args) =>
        {
            Rect viewport = args.EffectiveViewport;
            watch.OnScreen = AppIconRules.IsOnScreen(viewport.X, viewport.Y, viewport.Width, viewport.Height, sender.ActualWidth, sender.ActualHeight);
            if (watch.OnScreen) StartWaitingRead(watch);
        };
        return watch;
    }

    private static void StartWaitingRead(AppTileWatch watch)
    {
        Action? read = watch.ReadWhenOnScreen;
        watch.ReadWhenOnScreen = null;
        read?.Invoke();
    }

    private int AppIconPixels() => AppIconRules.PixelSize(Content?.XamlRoot?.RasterizationScale ?? 1);

    /// <summary>True when this icon was already read this session, and shown if the app has one: nothing more is read.</summary>
    private bool ShowAppIconIfAlreadyRead(AppIconSource source, Action<ImageSource> show)
    {
        if (!appIcons.TryGetCached(source, AppIconPixels(), out ImageSource? cached)) return false;
        if (cached is not null) show(cached);
        return true;
    }

    /// <summary>Shows the icon now when it was already read this session, otherwise when the read finishes. No icon: nothing happens.</summary>
    private void ShowAppIcon(AppIconSource source, Action<ImageSource> show)
    {
        if (!ShowAppIconIfAlreadyRead(source, show)) _ = ShowAppIconWhenReadAsync(source, AppIconPixels(), show);
    }

    private async Task ShowAppIconWhenReadAsync(AppIconSource source, int pixels, Action<ImageSource> show)
    {
        ImageSource? icon = await appIcons.LoadAsync(source, pixels);
        if (icon is not null && !windowClosed) show(icon);
    }
}
