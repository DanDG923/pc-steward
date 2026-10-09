using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PCSteward.Domain;

/// <summary>One provider agreement exactly as the user is shown it (FR-INS-03). Data only.</summary>
public sealed record PackageAgreementText(string Label, string Text, string? Url);

/// <summary>
/// FR-INS-03 / TM-22: a bounded SHA-256 over the canonical agreement text. A job carries the hash of the
/// agreements the user accepted; execution accepts agreements only when a fresh fetch hashes the same.
/// </summary>
public static class PackageAgreementDigest
{
    public const int MaximumAgreements = 16;
    public const int MaximumLabelLength = 512;
    public const int MaximumTextLength = 100_000;
    public const int MaximumUrlLength = 2048;

    /// <summary>An empty list hashes to null: there is nothing to accept.</summary>
    public static bool TryCompute(IReadOnlyList<PackageAgreementText>? agreements, out string? hash, out string rejection)
    {
        hash = null;
        if (agreements is null) { rejection = "agreements-missing"; return false; }
        if (agreements.Count == 0) { rejection = string.Empty; return true; }
        if (agreements.Count > MaximumAgreements) { rejection = "agreements-too-many"; return false; }
        StringBuilder canonical = new();
        canonical.Append("pcsteward-agreements-v1\n").Append(agreements.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (PackageAgreementText? agreement in agreements)
        {
            if (agreement is null) { rejection = "agreement-null"; return false; }
            string label = agreement.Label ?? string.Empty;
            string text = agreement.Text ?? string.Empty;
            string url = agreement.Url ?? string.Empty;
            if (label.Length > MaximumLabelLength || text.Length > MaximumTextLength || url.Length > MaximumUrlLength) { rejection = "agreement-oversize"; return false; }
            if (label.Length == 0 && text.Length == 0 && url.Length == 0) { rejection = "agreement-empty"; return false; }
            // Length prefixes keep the encoding unambiguous for text that itself contains separators.
            Field(canonical, label);
            Field(canonical, text);
            Field(canonical, url);
        }

        hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        rejection = string.Empty;
        return true;
    }

    private static void Field(StringBuilder canonical, string value) =>
        canonical.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
}

/// <summary>A recorded acceptance of one agreement set for one exact package version (FR-INS-03).</summary>
public sealed partial record AgreementAcceptance
{
    public AgreementAcceptance(Guid id, UpdateSource source, string packageId, string version, string agreementHash, DateTimeOffset acceptedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("An acceptance ID is required.", nameof(id));
        if (source != UpdateSource.WinGet) throw new ArgumentException("Only WinGet package agreements are recorded.", nameof(source));
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(agreementHash);
        if (packageId.Length > 256 || !PackageIdPattern().IsMatch(packageId)) throw new ArgumentException("The package ID is outside the allowlisted format.", nameof(packageId));
        if (version.Length > 128 || !VersionPattern().IsMatch(version)) throw new ArgumentException("The version is outside the allowlisted format.", nameof(version));
        if (!HashPattern().IsMatch(agreementHash)) throw new ArgumentException("The agreement hash is invalid.", nameof(agreementHash));
        if (acceptedAt == default) throw new ArgumentException("An acceptance time is required.", nameof(acceptedAt));
        Id = id;
        Source = source;
        PackageId = packageId;
        Version = version;
        AgreementHash = agreementHash;
        AcceptedAt = acceptedAt;
    }

    public Guid Id { get; }
    public UpdateSource Source { get; }
    public string PackageId { get; }
    public string Version { get; }
    public string AgreementHash { get; }
    public DateTimeOffset AcceptedAt { get; }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.+_-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
