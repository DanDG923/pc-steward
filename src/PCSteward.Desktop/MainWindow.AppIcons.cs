using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using PCSteward.Application;

namespace PCSteward.Desktop;

public sealed partial class MainWindow
{
    // ADR-035: icons read this session, in memory only.
    private readonly AppIconLoader appIcons = new();

    /// <summary>
    /// UI review 2026-11-13 (item 20; ADR-035): an app's tile. It starts as the two letters and shows the app's own icon when
    /// one can be read under the ADR's rules. The icon is decoration: the app's name is always beside it.
    /// </summary>
    private Border AppTile(string name, InventoryRow? row)
    {
        Border tile = StewardVisuals.Avatar(StewardPresentation.Initials(name));
        if (row is not null && AppIconRules.SourceFor(row.Instances) is { } source)
            ShowAppIcon(source, icon => { tile.Background = null; tile.Child = StewardVisuals.AppIconImage(icon); });
        return tile;
    }

    /// <summary>The tile of the app with this key in the saved app list; its two letters when the key is not in the list.</summary>
    private Border AppTile(string name, string stableProductKey) =>
        AppTile(name, viewModel.AllRows.FirstOrDefault(row => string.Equals(row.StableProductKey, stableProductKey, StringComparison.Ordinal)));

    /// <summary>
    /// Installed apps builds its rows from a template and reuses them as the list scrolls, so each row that comes on screen
    /// starts again from its letters and then asks for its own icon. A row that has been reused for another app by the time
    /// the icon arrives is left alone.
    /// </summary>
    private void ShowAppIconsInRows(ListView list) => list.ContainerContentChanging += (_, args) =>
    {
        if (args.ItemContainer.ContentTemplateRoot is not Grid { Children.Count: > 0 } rowGrid || rowGrid.Children[0] is not Grid { Children.Count: 2 } tile
            || tile.Children[0] is not Border letters || tile.Children[1] is not Image image) return;
        image.Source = null;
        image.Visibility = Visibility.Collapsed;
        letters.Visibility = Visibility.Visible;
        if (args.InRecycleQueue || args.Item is not ApplicationListItem item || AppIconRules.SourceFor(item.Row.Instances) is not { } source) return;
        SelectorItem container = args.ItemContainer;
        ShowAppIcon(source, icon =>
        {
            if (!ReferenceEquals(container.Content, item)) return;
            image.Source = icon;
            image.Visibility = Visibility.Visible;
            letters.Visibility = Visibility.Collapsed;
        });
    };

    /// <summary>Shows the icon now when it was already read this session, otherwise when the read finishes. No icon: nothing happens.</summary>
    private void ShowAppIcon(AppIconSource source, Action<ImageSource> show)
    {
        int pixels = AppIconRules.PixelSize(Content?.XamlRoot?.RasterizationScale ?? 1);
        if (appIcons.TryGetCached(source, pixels, out ImageSource? cached))
        {
            if (cached is not null) show(cached);
            return;
        }
        _ = ShowAppIconWhenReadAsync(source, pixels, show);
    }

    private async Task ShowAppIconWhenReadAsync(AppIconSource source, int pixels, Action<ImageSource> show)
    {
        ImageSource? icon = await appIcons.LoadAsync(source, pixels);
        if (icon is not null && !windowClosed) show(icon);
    }
}
