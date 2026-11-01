using PCSteward.Application;

namespace PCSteward.Provider.Chocolatey;

/// <summary>
/// The single injected history grammar for helper-reported versions (adapter gates §B.2, B-Q1). Delegates to the same
/// <see cref="ChocolateyVersionGrammar"/> facade the protocol-2 codec uses, so it is not a second grammar. Never trust or authority.
/// </summary>
public sealed class ChocolateyHelperVersionGrammar : IReviewedHelperVersionGrammar
{
    public bool IsNormalized(string value) => ChocolateyVersionGrammar.IsNormalized(value, RecordedHelperCandidateCheck.MaximumVersionLength);
}
