namespace PCSteward.Application;

/// <summary>Closed outcomes of the Desktop's read-only check of the installed Chocolatey client (ADR-031 design §8.6 gate 3).</summary>
public enum ChocolateyInstalledClientCheck
{
    /// <summary>Nothing has been checked yet: summaries never check; the review dialog checks when it opens.</summary>
    NotChecked,
    /// <summary>The review dialog's check is running.</summary>
    Checking,
    /// <summary>The verified observer matched every Chocolatey file to exactly one listed release.</summary>
    Verified,
    /// <summary>The check completed and refused: the installed client is not exactly a reviewed release in a protected setup.</summary>
    Refused,
    /// <summary>No check could run (this account's token was refused, a fault, or no reviewed release is compiled in).</summary>
    Unavailable,
    /// <summary>The check did not finish within its time bound.</summary>
    TimedOut,
    /// <summary>The check was cancelled (the dialog closed).</summary>
    Cancelled,
}

/// <summary>
/// FR-UPD-15, FR-UI-03/04, TM-03/04/07/13; ADR-031 ("offered only from a listed, currently trusted client"), design §8.6
/// gate 3: the narrow, display-oriented result of the Desktop-role, read-only, non-elevated observation of the installed
/// Chocolatey client. It carries only the installed listed release's normalized version (when verified) or a closed reason;
/// never a handle, path, command or fingerprint. It admits nothing: the route stays non-admitted, and any future job still
/// re-observes the client before dispatch.
/// <para><see cref="Verified"/> is the only way to carry a release; an architecture test pins its single <c>src</c> call
/// site to the native installed-client observation, so the Desktop cannot fabricate a verified release from scan data.</para>
/// </summary>
public sealed class ChocolateyInstalledClientVerification
{
    private ChocolateyInstalledClientVerification(ChocolateyInstalledClientCheck check, string? installedRelease)
    {
        Check = check;
        InstalledRelease = installedRelease;
    }

    public static ChocolateyInstalledClientVerification NotChecked { get; } = new(ChocolateyInstalledClientCheck.NotChecked, null);
    public static ChocolateyInstalledClientVerification Checking { get; } = new(ChocolateyInstalledClientCheck.Checking, null);
    public static ChocolateyInstalledClientVerification Refused { get; } = new(ChocolateyInstalledClientCheck.Refused, null);
    public static ChocolateyInstalledClientVerification Unavailable { get; } = new(ChocolateyInstalledClientCheck.Unavailable, null);
    public static ChocolateyInstalledClientVerification TimedOut { get; } = new(ChocolateyInstalledClientCheck.TimedOut, null);
    public static ChocolateyInstalledClientVerification Cancelled { get; } = new(ChocolateyInstalledClientCheck.Cancelled, null);

    /// <summary>
    /// The verified installed release (its normalized version). Only the native installed-client observation calls this
    /// (architecture-pinned). A malformed version is <see cref="Unavailable"/>, never a verified release.
    /// </summary>
    public static ChocolateyInstalledClientVerification Verified(string installedRelease) =>
        ChocolateyClientUpdatePresentation.IsReleaseVersion(installedRelease)
            ? new(ChocolateyInstalledClientCheck.Verified, installedRelease)
            : Unavailable;

    public ChocolateyInstalledClientCheck Check { get; }

    /// <summary>The verified installed release; non-null exactly when <see cref="Check"/> is <see cref="ChocolateyInstalledClientCheck.Verified"/>.</summary>
    public string? InstalledRelease { get; }

    /// <summary>True when the check completed without verifying the client (refused, unavailable, timed out or cancelled).</summary>
    public bool CouldNotVerify => Check is ChocolateyInstalledClientCheck.Refused or ChocolateyInstalledClientCheck.Unavailable or
        ChocolateyInstalledClientCheck.TimedOut or ChocolateyInstalledClientCheck.Cancelled;

    /// <summary>
    /// The Desktop's outer bound on one check: the native observation's own budget (60 s) plus its abandonment margin, with slack.
    /// </summary>
    public static readonly TimeSpan DisplayCheckTimeout = TimeSpan.FromSeconds(75);

    /// <summary>
    /// Runs the supplied check under a token linked to <paramref name="cancellationToken"/> that is also cancelled after
    /// <paramref name="timeout"/>, and stops waiting at <paramref name="timeout"/> (<see cref="TimedOut"/>). Every fault, a null
    /// result, or an undefined or non-final value is <see cref="Unavailable"/>; once <paramref name="cancellationToken"/> is
    /// cancelled the result is <see cref="Cancelled"/>. A check can therefore never fail or hang the view.
    /// </summary>
    public static async Task<ChocolateyInstalledClientVerification> RunAsync(
        Func<CancellationToken, Task<ChocolateyInstalledClientVerification>> check, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (cancellationToken.IsCancellationRequested) return Cancelled;
        if (timeout <= TimeSpan.Zero) return TimedOut;
        // The linked source lives until the check itself returns (it is disposed by the continuation), so a caller cancellation
        // or the bound always reaches a check that is still running, even after this method has stopped waiting for it.
        CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<ChocolateyInstalledClientVerification> work;
        try
        {
            bounded.CancelAfter(timeout);
            work = check(bounded.Token) ?? Task.FromResult<ChocolateyInstalledClientVerification>(null!);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            bounded.Dispose();
            return cancellationToken.IsCancellationRequested ? Cancelled : Unavailable;
        }
        _ = work.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), bounded, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        ChocolateyInstalledClientVerification? result;
        try { result = await work.WaitAsync(timeout, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (cancellationToken.IsCancellationRequested) return Cancelled;
            if (exception is TimeoutException)
            {
                // Stop the abandoned check now (its source may already be disposed if it has just returned).
                try { bounded.Cancel(); }
                catch (ObjectDisposedException) { }
                return TimedOut;
            }
            return bounded.IsCancellationRequested ? TimedOut : Unavailable;
        }
        if (cancellationToken.IsCancellationRequested) return Cancelled;
        if (result is null || !Enum.IsDefined(result.Check) || result.Check is ChocolateyInstalledClientCheck.NotChecked or ChocolateyInstalledClientCheck.Checking)
            return Unavailable;
        // The caller did not cancel, so a cancelled check was stopped by the bound (or reported cancellation spuriously).
        if (result.Check == ChocolateyInstalledClientCheck.Cancelled) return bounded.IsCancellationRequested ? TimedOut : Unavailable;
        return result;
    }
}
