using System.Globalization;
using System.Text.RegularExpressions;

namespace PCSteward.Domain;

public enum VersionComparisonOutcome
{
    Newer,
    Equal,
    Older,
    Incomparable,
    ConnectorDeferred
}

public enum VersionComparisonExplanation
{
    NumericDotted,
    PrereleaseNumericDotted,
    CandidatePrereleaseDisallowed,
    CandidateIsOlderDowngradeBlocked,
    MissingInstalledVersion,
    MissingCandidateVersion,
    MalformedInstalledVersion,
    MalformedCandidateVersion,
    VendorSuffixUnsupported,
    ConnectorSpecificSemanticsRequired
}

public sealed record VersionComparisonRequest(
    string? InstalledVersion,
    string? CandidateVersion,
    bool RequiresConnectorSpecificSemantics = false,
    bool AllowPrereleaseCandidate = false);

public sealed record VersionComparisonResult(VersionComparisonOutcome Outcome, VersionComparisonExplanation Explanation);

public static partial class VersionComparison
{
    private const int MaximumNumericComponents = 8;

    public static VersionComparisonResult Compare(VersionComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequiresConnectorSpecificSemantics)
        {
            return new(VersionComparisonOutcome.ConnectorDeferred, VersionComparisonExplanation.ConnectorSpecificSemanticsRequired);
        }

        if (string.IsNullOrWhiteSpace(request.InstalledVersion))
        {
            return new(VersionComparisonOutcome.Incomparable, VersionComparisonExplanation.MissingInstalledVersion);
        }

        if (string.IsNullOrWhiteSpace(request.CandidateVersion))
        {
            return new(VersionComparisonOutcome.Incomparable, VersionComparisonExplanation.MissingCandidateVersion);
        }

        if (!TryParse(request.InstalledVersion, isInstalledVersion: true, out ParsedVersion installed, out VersionComparisonExplanation installedFailure))
        {
            return new(VersionComparisonOutcome.Incomparable, installedFailure);
        }

        if (!TryParse(request.CandidateVersion, isInstalledVersion: false, out ParsedVersion candidate, out VersionComparisonExplanation candidateFailure))
        {
            return new(VersionComparisonOutcome.Incomparable, candidateFailure);
        }

        if (candidate.IsPrerelease && !installed.IsPrerelease && !request.AllowPrereleaseCandidate)
        {
            return new(VersionComparisonOutcome.Incomparable, VersionComparisonExplanation.CandidatePrereleaseDisallowed);
        }

        int comparison = CompareParsed(installed, candidate);
        if (comparison < 0)
        {
            return new(VersionComparisonOutcome.Newer, ExplanationFor(installed, candidate));
        }

        if (comparison > 0)
        {
            return new(VersionComparisonOutcome.Older, VersionComparisonExplanation.CandidateIsOlderDowngradeBlocked);
        }

        return new(VersionComparisonOutcome.Equal, ExplanationFor(installed, candidate));
    }

    private static int CompareParsed(ParsedVersion installed, ParsedVersion candidate)
    {
        int componentCount = Math.Max(installed.NumericComponents.Count, candidate.NumericComponents.Count);
        for (int index = 0; index < componentCount; index++)
        {
            uint installedPart = index < installed.NumericComponents.Count ? installed.NumericComponents[index] : 0;
            uint candidatePart = index < candidate.NumericComponents.Count ? candidate.NumericComponents[index] : 0;
            int partComparison = installedPart.CompareTo(candidatePart);
            if (partComparison != 0)
            {
                return partComparison;
            }
        }

        if (installed.PrereleaseRank != candidate.PrereleaseRank)
        {
            return installed.PrereleaseRank.CompareTo(candidate.PrereleaseRank);
        }

        return installed.PrereleaseNumber.CompareTo(candidate.PrereleaseNumber);
    }

    private static VersionComparisonExplanation ExplanationFor(ParsedVersion installed, ParsedVersion candidate) =>
        installed.IsPrerelease || candidate.IsPrerelease
            ? VersionComparisonExplanation.PrereleaseNumericDotted
            : VersionComparisonExplanation.NumericDotted;

    private static bool TryParse(string version, bool isInstalledVersion, out ParsedVersion parsed, out VersionComparisonExplanation failure)
    {
        Match match = SafeVersionPattern().Match(version.Trim());
        if (!match.Success)
        {
            parsed = null!;
            failure = version.Contains('+', StringComparison.Ordinal) || version.Contains(' ', StringComparison.Ordinal)
                ? VersionComparisonExplanation.VendorSuffixUnsupported
                : isInstalledVersion ? VersionComparisonExplanation.MalformedInstalledVersion : VersionComparisonExplanation.MalformedCandidateVersion;
            return false;
        }

        string[] components = match.Groups["numeric"].Value.Split('.');
        if (components.Length > MaximumNumericComponents || !components.All(component => uint.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            parsed = null!;
            failure = isInstalledVersion ? VersionComparisonExplanation.MalformedInstalledVersion : VersionComparisonExplanation.MalformedCandidateVersion;
            return false;
        }

        string prereleaseLabel = match.Groups["label"].Value;
        string prereleaseNumberText = match.Groups["prereleaseNumber"].Value;
        int prereleaseRank = prereleaseLabel.Length == 0 ? 4 : prereleaseLabel.ToLowerInvariant() switch
        {
            "alpha" => 0,
            "beta" => 1,
            "preview" => 2,
            "rc" => 3,
            _ => throw new InvalidOperationException("The regular expression permits only recognized prerelease labels.")
        };

        uint prereleaseNumber = prereleaseNumberText.Length == 0 ? 0 : uint.Parse(prereleaseNumberText, CultureInfo.InvariantCulture);
        parsed = new(components.Select(component => uint.Parse(component, CultureInfo.InvariantCulture)).ToArray(), prereleaseRank, prereleaseNumber);
        failure = VersionComparisonExplanation.NumericDotted;
        return true;
    }

    private sealed record ParsedVersion(IReadOnlyList<uint> NumericComponents, int PrereleaseRank, uint PrereleaseNumber)
    {
        public bool IsPrerelease => PrereleaseRank < 4;
    }

    [GeneratedRegex(@"^(?<numeric>\d+(?:\.\d+)*)(?:-(?<label>alpha|beta|preview|rc)(?:\.(?<prereleaseNumber>\d+))?)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SafeVersionPattern();
}
