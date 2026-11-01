using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, TM-03/04/13; Task 50 design rev 6 §4 (rev-3 M-B): the pinned stage-C installed recheck
/// <c>list &lt;ID&gt; --exact --limit-output --no-color</c>, byte-equal to the protocol-1 post-update literal. Fixed argument
/// data only; it starts nothing. It formats only when the operation has the exact accepted runtime-limit route shape:
/// it applies <see cref="ChocolateyReviewedUpdateArguments.Build"/> itself, so the route, guard contract, provider,
/// source, scope, channel, architecture binding, mode, supported ID, non-client target, normalized versions, stable
/// candidate and strictly-newer predicates are the same predicates by construction, and then rechecks the ID rules.
/// </summary>
internal static class ChocolateyExactListArguments
{
    /// <summary>Throws <see cref="ArgumentException"/> when the reviewed binding is unavailable for the fixed mapping.</summary>
    internal static IReadOnlyList<string> Build(ReviewedUpdateOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        // Same predicates as the fixed upgrade: a refusal there is a refusal here (the upgrade arguments are discarded).
        _ = ChocolateyReviewedUpdateArguments.Build(operation);
        string id = operation.Selection.PackageId;
        if (!ChocolateyNativePackageId.IsSupported(id) || ChocolateyClientReleasePolicy.IsClientTarget(id))
            throw new ArgumentException("The reviewed binding is unavailable for the fixed Chocolatey mapping.", nameof(operation));
        return Array.AsReadOnly(new[] { "list", id, "--exact", "--limit-output", "--no-color" });
    }
}
