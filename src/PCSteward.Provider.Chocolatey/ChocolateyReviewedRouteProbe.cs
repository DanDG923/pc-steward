using PCSteward.Application;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Provider.Chocolatey;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §(L6), admission obligation 5 (N6 review M1): the Desktop's read-only
/// split-token probe for the runtime-limit Chocolatey review, probed afresh at each review. It only reads the desktop's own
/// token through <c>ChocolateyReviewedRouteComposition.ProbeSplitTokenState</c> (a static method; the composition is not
/// constructed). It observes nothing, starts nothing and admits nothing. Any fault is <see cref="ReviewedRouteSplitTokenState.Unknown"/>.
/// </summary>
public static class ChocolateyReviewedRouteProbe
{
    public static ReviewedRouteSplitTokenState ProbeSplitTokenState()
    {
        try { return ChocolateyReviewedRouteComposition.ProbeSplitTokenState(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return ReviewedRouteSplitTokenState.Unknown; }
    }
}
