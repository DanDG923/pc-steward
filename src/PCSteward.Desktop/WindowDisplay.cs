using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using PCSteward.Application;
using WinRT.Interop;

namespace PCSteward.Desktop;

/// <summary>
/// Sharp text fix (2026-11-13): the app is now PerMonitorV2 DPI aware (app.manifest), so AppWindow sizes are physical pixels.
/// A window's first size is chosen in effective pixels and scaled by the display the window opens on, so it keeps the same size
/// on screen at 100%, 150% or 200%. It reads only the window's own DPI; an unknown DPI falls back to 100%.
/// </summary>
internal static class WindowDisplay
{
    private const double DefaultDpi = 96.0;

    /// <summary>Resizes <paramref name="window"/> to <paramref name="effectiveWidth"/> x <paramref name="effectiveHeight"/> effective pixels.</summary>
    public static void ResizeEffective(Window window, int effectiveWidth, int effectiveHeight)
    {
        ArgumentNullException.ThrowIfNull(window);
        double scale = Scale(window);
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(
            WindowMinimumSize.PhysicalPixels(effectiveWidth, scale),
            WindowMinimumSize.PhysicalPixels(effectiveHeight, scale)));
    }

    /// <summary>The display scale of <paramref name="window"/> (1.0 = 100%).</summary>
    public static double Scale(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        uint dpi = GetDpiForWindow(WindowNative.GetWindowHandle(window));
        return dpi == 0 ? 1.0 : dpi / DefaultDpi;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDpiForWindow(IntPtr window);
}

/// <summary>
/// Steady background fix (2026-11-13): the standard <see cref="Microsoft.UI.Xaml.Media.MicaBackdrop"/> follows Windows' rule that an
/// inactive window drops Mica for a flat fallback colour, so PC Steward's background changed colour every time it gained or lost
/// focus. This backdrop is the same Mica Alt (owner colour choice 2026-11-11) but always renders as the active window does. It
/// still follows the app's light/dark theme and high contrast from the default configuration.
/// </summary>
internal sealed partial class SteadyMicaAltBackdrop : Microsoft.UI.Xaml.Media.SystemBackdrop, IDisposable
{
    private MicaController? controller;
    private SystemBackdropConfiguration? configuration;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(connectedTarget);
        base.OnTargetConnected(connectedTarget, xamlRoot);
        configuration = new SystemBackdropConfiguration { IsInputActive = true };
        CopyThemeFromDefault(connectedTarget, xamlRoot);
        controller = new MicaController { Kind = MicaKind.BaseAlt };
        controller.SetSystemBackdropConfiguration(configuration);
        controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        Dispose();
    }

    /// <summary>Theme or high contrast changed: follow it. A focus change also lands here and is ignored on purpose.</summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        CopyThemeFromDefault(target, xamlRoot);
    }

    public void Dispose()
    {
        controller?.Dispose();
        controller = null;
        configuration = null;
        GC.SuppressFinalize(this);
    }

    private void CopyThemeFromDefault(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        if (configuration is null) return;
        SystemBackdropConfiguration defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        configuration.Theme = defaults.Theme;
        configuration.IsHighContrast = defaults.IsHighContrast;
        configuration.IsInputActive = true;
    }
}
