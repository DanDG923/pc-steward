using PCSteward.Application;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Provider.Chocolatey;

/// <summary>
/// FR-UPD-15, FR-UI-03/04, TM-03/04/07/13; ADR-031 ("offered only from a listed, currently trusted client"), Task 50 design
/// §8.6 gate 3: the Desktop's read-only, non-elevated check of the installed Chocolatey client, run on demand when the client
/// update review opens. It runs the existing Desktop-role, client-mode profile observation over the compiled reviewed list
/// (through <c>ChocolateyDesktopObservation.ObserveInstalledClientAsync</c>) and returns only the display result: the verified
/// listed release, or a closed reason. It never launches the client, holds no file, returns no handle or command, never
/// elevates and admits nothing. Any fault is <see cref="ChocolateyInstalledClientVerification.Unavailable"/> (or Cancelled).
/// </summary>
public static class ChocolateyInstalledClientProbe
{
    public static async Task<ChocolateyInstalledClientVerification> ObserveAsync(CancellationToken cancellationToken)
    {
        try { return await ChocolateyDesktopObservation.ObserveInstalledClientAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return cancellationToken.IsCancellationRequested ? ChocolateyInstalledClientVerification.Cancelled : ChocolateyInstalledClientVerification.Unavailable;
        }
    }
}
