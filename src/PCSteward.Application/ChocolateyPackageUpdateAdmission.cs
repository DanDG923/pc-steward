namespace PCSteward.Application;

/// <summary>
/// FR-UPD-15, TM-03/04/13; Chocolatey package Confirm addendum §3 C1 (<c>docs/review/TASK_50_CHOCOLATEY_PACKAGE_CONFIRM_ADDENDUM.md</c>):
/// the owner's admission of the Confirm for Chocolatey Community package updates on the runtime-limit route
/// (<c>UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3</c>), as one compiled constant. It is separate from
/// <see cref="ChocolateyClientUpdateAdmission"/> because package scripts run as administrator: either route can be turned off alone.
/// It can only make things stricter: while it is false, the package entry shows today's read-only disclosure, no package review
/// session is prepared and <see cref="ChocolateyPackageUpdateReviewSession"/> refuses every Confirm. An architecture test pins its
/// single definition and that no configuration, registry, environment or database value reaches it.
/// <para>
/// Setting it true is its own reviewed commit. Even then Confirm stays off until every other condition holds, and every dispatch
/// still re-observes the package (F′), checks the deployment capability and runs the helper's own stage B–D checks, where stage C
/// confirms the exact candidate from protected machine data.
/// </para>
/// </summary>
public static class ChocolateyPackageUpdateAdmission
{
    /// <summary>C1. Compiled only; changed only by a reviewed commit.</summary>
    public const bool OwnerApproved = false;

    /// <summary>
    /// The value every consumer reads. A property (not the constant) so code that branches on it stays reachable whichever value
    /// is compiled in.
    /// </summary>
    public static bool IsOwnerApproved => OwnerApproved;
}
