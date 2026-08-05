# Task 35 real-UI capture recovery record

## Objective

Capture production PC Steward screens with real local data while avoiding clipboard dependence and avoiding any exposure of unrelated windows.

## Failed routes

1. The bundled Computer Use package did not match its supplied skill contract. Its expected documentation API was absent, and window enumeration returned `EnumWindows failed: The system cannot find the path specified. (0x80070003)`.
2. GDI `PrintWindow` and screen-copy attempts could not reliably capture the WinUI DirectComposition surface. Black images were deleted.
3. An active-window screenshot attempt could not verify PC Steward foreground ownership and captured an unrelated Chrome window. The invalid image was deleted immediately. The screenshot remained on the Windows clipboard because the attempted clipboard clear was not permitted.
4. A later foreground capture rendered PC Steward but included a OneDrive screenshot prompt triggered by the prior hotkey. That invalid image was also deleted; no attempt was made to dismiss or alter the user's OneDrive state.

## Safe recovery

A temporary, environment-gated `RenderTargetBitmap` path captured only the production XAML content owned by PC Steward. It wrote five PNG files directly to `docs/review/screenshots`, without foreground switching, clipboard access, or another application's pixels. Each image was visually reviewed.

The temporary capture and startup-trace code was then removed, the trace file was deleted, every PC Steward process launched for review was stopped, and the diagnostic-free desktop source rebuilt with zero warnings/errors.

## Result

- Valid retained images: Overview, Applications, Updates, Activity, Settings.
- Invalid black/occluded/unrelated images: deleted.
- Product source: contains no capture-only environment variable or startup trace.
- Remaining host side effect: the Windows clipboard may still contain the unrelated Chrome screenshot; the user can clear or replace it if desired.
