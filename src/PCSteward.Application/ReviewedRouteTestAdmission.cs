using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13; ADR-031 design §8.6 admission gate 1 (test-only): lets
/// <see cref="ReviewedUpdateReview.HasSupportedMode"/> admit a <see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/>
/// operation inside one test's asynchronous flow, so the full client-route workflow (claim, review, confirm, dispatch,
/// finalization, history) can run before the route is admitted. The production default is the compiled rule unchanged:
/// <see cref="Admits"/> is true only inside a scope from <see cref="EnterClientSelfUpdateForTests"/>, which no <c>src</c>
/// code calls (architecture test on source and compiled IL), so in production it is always false. The scope is an
/// <see cref="AsyncLocal{T}"/> flag: it flows only into the entering test's own awaits and tasks, never into another test
/// or thread. It touches neither the deployment capability allow-list nor any dispatcher's <c>Supports</c>, so a real
/// helper still refuses the route; nothing here is persisted or read from data.
/// </summary>
internal static class ReviewedRouteTestAdmission
{
    private static readonly AsyncLocal<bool> ClientSelfUpdateAdmitted = new();

    /// <summary>
    /// True only inside a test scope, for the exact current-schema client self-update shape (Chocolatey Community machine
    /// registration, provider-script-controlled mode and architecture). Always false in production.
    /// </summary>
    internal static bool Admits(ReviewedUpdateOperation operation) =>
        ClientSelfUpdateAdmitted.Value && operation.HasCurrentSchema &&
        operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1 &&
        operation.Selection.Provider == UpdateProvider.Chocolatey && operation.Selection.Source == UpdateSource.ChocolateyCommunity &&
        operation.Selection.Scope == UpdateScope.Machine &&
        string.Equals(operation.Selection.PackageId, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal) &&
        operation.Selection.Architecture.Binding == ReviewedArchitectureBinding.ProviderScriptSelected &&
        operation.Mode == ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled);

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): admits the client self-update route for the calling
    /// test's asynchronous flow until the returned scope is disposed (which restores the previous value).
    /// </summary>
    internal static IDisposable EnterClientSelfUpdateForTests()
    {
        bool previous = ClientSelfUpdateAdmitted.Value;
        ClientSelfUpdateAdmitted.Value = true;
        return new Scope(previous);
    }

    private sealed class Scope(bool previous) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) ClientSelfUpdateAdmitted.Value = previous;
        }
    }
}
