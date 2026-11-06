namespace PCSteward.Application;

/// <summary>
/// FR-UPD-15, TM-03/04/13; Chocolatey Confirm design §2 C1 (<c>docs/review/TASK_50_CHOCOLATEY_CONFIRM_SCREEN_DESIGN.md</c>): the
/// owner's admission of the Chocolatey client self-update Confirm, as one compiled constant. It is the only new trust decision in
/// the Confirm screen and it can only make things stricter: while it is false, the review screen shows the not-admitted reason,
/// no review session is prepared and <see cref="ChocolateyClientUpdateReviewSession"/> refuses every Confirm. An architecture test
/// pins its single definition and that no configuration, registry, environment or database value reaches it.
/// <para>
/// Setting it true is its own reviewed commit (design §8 item 5). Even then Confirm stays off until every other condition holds,
/// including a second reviewed release on the compiled list (C3), and every dispatch still re-observes the client (F′), checks the
/// deployment capability and runs the helper's own stage B–D checks.
/// </para>
/// <para>
/// Enabled 2026-11-06: the owner approved real Chocolatey client updates (obligation 8), on the condition that a full security
/// review of the enabled build is done and its findings fixed before anything runs for real (build record §4).
/// </para>
/// </summary>
public static class ChocolateyClientUpdateAdmission
{
    /// <summary>C1. Compiled only; changed only by a reviewed commit.</summary>
    public const bool OwnerApproved = true;

    /// <summary>
    /// The value every consumer reads. A property (not the constant) so code that branches on it stays reachable whichever value
    /// is compiled in.
    /// </summary>
    public static bool IsOwnerApproved => OwnerApproved;
}
