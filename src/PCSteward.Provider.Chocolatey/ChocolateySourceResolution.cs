namespace PCSteward.Provider.Chocolatey;

/// <summary>Pure projection of the pinned resolver's enabled-source precedence; never source trust or a native binding.</summary>
internal static class ChocolateySourceResolution
{
    internal static bool TryResolve(ChocolateyConfigurationObservation configuration, out ChocolateyConfiguredSource? matchingSource)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        matchingSource = null;
        if (configuration.Sources.Count > 64) return false;
        IEnumerable<ChocolateyConfiguredSource> enabled = configuration.Sources.Where(source => !source.Disabled);
        if (configuration.Sources.Any(source => source.Priority > 0))
            enabled = enabled.Where(source => source.Priority != 0).OrderBy(source => source.Priority)
                .Concat(configuration.Sources.Where(source => !source.Disabled && source.Priority == 0));
        ChocolateyConfiguredSource[] sources = enabled.ToArray();
        string fixedSource = ChocolateyUpdateSourceConnector.CommunitySourceUrl;
        ChocolateyConfiguredSource? key = sources.FirstOrDefault(source => Equal(source.Value, fixedSource));
        if (key is not null)
        {
            // Native key matching supplies transport options but does not replace the CLI source string.
            matchingSource = key;
            return true;
        }
        ChocolateyConfiguredSource? name = sources.FirstOrDefault(source => Equal(source.Id, fixedSource));
        // A name-only match substitutes its key. If that key were the fixed URL it would have matched above.
        return name is null;
    }

    private static bool Equal(string left, string right) => StringComparer.InvariantCultureIgnoreCase.Equals(left, right);
}
