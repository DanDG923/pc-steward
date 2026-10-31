namespace PCSteward.Provider.Chocolatey;

/// <summary>File-only comparison data. Registration digests cover native ID/version/archive identities and bytes; null means observed absence.</summary>
internal sealed record ChocolateyClientStateData(string EngineSha256, string CodeDigest,
    string? ClientRegistrationDigest, string? LicensedExtensionRegistrationDigest);
internal enum ChocolateyClientComparison { Unchanged, ClientChanged, Unverifiable }
internal sealed record ChocolateyPostClientResult<T>(ChocolateyClientComparison Comparison, T? Value);

/// <summary>Orders file comparison before optional verification. It never creates native proof, Success or a dispatch capability.</summary>
internal static class ChocolateyPostUpdateClientCheck
{
    internal static ChocolateyClientComparison Compare(ChocolateyClientStateData before, ChocolateyClientStateData? after)
    {
        ArgumentNullException.ThrowIfNull(before);
        if (!Valid(before) || before.EngineSha256 != ChocolateyClientReleasePolicy.EngineSha256 || after is null || !Valid(after))
            return ChocolateyClientComparison.Unverifiable;
        return before == after ? ChocolateyClientComparison.Unchanged : ChocolateyClientComparison.ClientChanged;
    }

    internal static async Task<ChocolateyPostClientResult<T>> RunAsync<T>(ChocolateyClientStateData before,
        Func<CancellationToken, Task<ChocolateyClientStateData?>> readFilesOnly,
        Func<CancellationToken, Task<T>> queryTarget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(readFilesOnly);
        ArgumentNullException.ThrowIfNull(queryTarget);
        cancellationToken.ThrowIfCancellationRequested();
        ChocolateyClientStateData? after = await readFilesOnly(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        ChocolateyClientComparison comparison = Compare(before, after);
        if (comparison != ChocolateyClientComparison.Unchanged) return new(comparison, default);
        return new(comparison, await queryTarget(cancellationToken).ConfigureAwait(false));
    }

    private static bool Valid(ChocolateyClientStateData data) => Hash(data.EngineSha256) && Hash(data.CodeDigest) &&
        (data.ClientRegistrationDigest is null || Hash(data.ClientRegistrationDigest)) &&
        (data.LicensedExtensionRegistrationDigest is null || Hash(data.LicensedExtensionRegistrationDigest));
    private static bool Hash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
