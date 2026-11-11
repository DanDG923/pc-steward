namespace PCSteward.SelfUpdate;

/// <summary>What a check concluded about the newest published release.</summary>
public enum ReleaseCheckOutcome
{
    /// <summary>A genuine, newer release this build may offer to the owner.</summary>
    NewerRelease,

    /// <summary>The release is genuine but not newer than the running version.</summary>
    NoNewerRelease,

    /// <summary>The release failed a check. It is shown as "Update found but not trusted", never as "up to date".</summary>
    NotTrusted,
}

/// <summary>Why a release was not trusted. Each value maps to one plain-English sentence on the update screen.</summary>
public enum ReleaseRefusal
{
    None,
    DevelopmentBuild,
    NoReleaseKey,
    MissingReleaseFiles,
    SignatureUnreadable,
    UnknownKey,
    SignatureInvalid,
    ManifestUnreadable,
    KeyMismatch,
    WrongProduct,
    WrongChannel,
    WrongSchema,
    TagMismatch,
    AssetNameMismatch,
    OlderOrRepeatedRelease,
    UpdaterTooOld,
    UnexpectedError,
}

/// <summary>
/// The running build's compiled identity. A sequence of 0 marks a development build, which never self-updates.
/// </summary>
public sealed record RunningRelease(ReleaseVersion Version, long Sequence);

public sealed record ReleaseCheckResult(ReleaseCheckOutcome Outcome, ReleaseRefusal Refusal, ReleaseManifest? Manifest)
{
    internal static ReleaseCheckResult Refused(ReleaseRefusal refusal) => new(ReleaseCheckOutcome.NotTrusted, refusal, null);
}

/// <summary>
/// ADR-032 section 4: decides, before the owner is asked anything, whether a downloaded manifest and signature describe a
/// genuine, newer release. It is a pure function of its inputs and fails closed: any unexpected value or exception is a
/// refusal, and there is no override.
/// </summary>
public static class ReleaseCandidateVerifier
{
    /// <param name="manifestBytes">The exact bytes of <c>pcsteward-release.json</c> as downloaded.</param>
    /// <param name="signatureBytes">The exact bytes of <c>pcsteward-release.json.sig</c> as downloaded.</param>
    /// <param name="tagName">The Git tag GitHub reported for the release (untrusted; it must equal <c>v</c> + version).</param>
    /// <param name="running">This build's compiled version and sequence.</param>
    /// <param name="highestAcceptedSequence">The highest sequence this machine has accepted before (0 if none).</param>
    /// <param name="keys">The compiled release keys. Production passes <see cref="ReleaseTrustAnchors.ReleaseKeys"/>.</param>
    public static ReleaseCheckResult Verify(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureBytes,
        string? tagName,
        RunningRelease running,
        long highestAcceptedSequence,
        IReadOnlyList<ReleaseSigningKey> keys)
    {
        try
        {
            return VerifyCore(manifestBytes, signatureBytes, tagName, running, highestAcceptedSequence, keys);
        }
#pragma warning disable CA1031 // ADR-032 section 4b: every unexpected failure is a refusal, never a pass.
        catch (Exception)
#pragma warning restore CA1031
        {
            return ReleaseCheckResult.Refused(ReleaseRefusal.UnexpectedError);
        }
    }

    private static ReleaseCheckResult VerifyCore(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureBytes,
        string? tagName,
        RunningRelease running,
        long highestAcceptedSequence,
        IReadOnlyList<ReleaseSigningKey> keys)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(keys);
        if (running.Sequence < 1) return ReleaseCheckResult.Refused(ReleaseRefusal.DevelopmentBuild);
        if (keys.Count == 0) return ReleaseCheckResult.Refused(ReleaseRefusal.NoReleaseKey);
        if (!ReleaseSignature.TryRead(signatureBytes, out ReleaseSignature? signature)) return ReleaseCheckResult.Refused(ReleaseRefusal.SignatureUnreadable);
        ReleaseSigningKey[] matching = [.. keys.Where(key => string.Equals(key.KeyId, signature!.KeyId, StringComparison.Ordinal))];
        if (matching.Length != 1) return ReleaseCheckResult.Refused(ReleaseRefusal.UnknownKey);
        if (manifestBytes.Length is 0 or > ReleaseManifest.MaximumBytes) return ReleaseCheckResult.Refused(ReleaseRefusal.ManifestUnreadable);
        if (!matching[0].Verifies(manifestBytes, signature!.Signature)) return ReleaseCheckResult.Refused(ReleaseRefusal.SignatureInvalid);
        if (!ReleaseManifest.TryRead(manifestBytes, out ReleaseManifest? parsed)) return ReleaseCheckResult.Refused(ReleaseRefusal.ManifestUnreadable);
        ReleaseManifest manifest = parsed!;
        if (!string.Equals(manifest.KeyId, signature.KeyId, StringComparison.Ordinal)) return ReleaseCheckResult.Refused(ReleaseRefusal.KeyMismatch);
        if (manifest.Schema != ReleaseTrustAnchors.ManifestSchema) return ReleaseCheckResult.Refused(ReleaseRefusal.WrongSchema);
        if (!string.Equals(manifest.Product, ReleaseTrustAnchors.Product, StringComparison.Ordinal)) return ReleaseCheckResult.Refused(ReleaseRefusal.WrongProduct);
        if (!string.Equals(manifest.Channel, ReleaseTrustAnchors.Channel, StringComparison.Ordinal)) return ReleaseCheckResult.Refused(ReleaseRefusal.WrongChannel);
        if (!string.Equals(tagName, "v" + manifest.Version.Text, StringComparison.Ordinal)) return ReleaseCheckResult.Refused(ReleaseRefusal.TagMismatch);
        if (!string.Equals(manifest.AssetName, ReleaseManifest.ExpectedAssetName(manifest.Version), StringComparison.Ordinal))
            return ReleaseCheckResult.Refused(ReleaseRefusal.AssetNameMismatch);

        long floor = Math.Max(running.Sequence, Math.Max(0, highestAcceptedSequence));
        if (manifest.Version <= running.Version)
        {
            // Genuine but not newer. A higher sequence on a version that is not higher is inconsistent, so it is refused.
            return manifest.Sequence <= running.Sequence
                ? new(ReleaseCheckOutcome.NoNewerRelease, ReleaseRefusal.None, manifest)
                : ReleaseCheckResult.Refused(ReleaseRefusal.OlderOrRepeatedRelease);
        }
        if (manifest.Sequence <= floor) return ReleaseCheckResult.Refused(ReleaseRefusal.OlderOrRepeatedRelease);
        if (manifest.MinimumUpdaterVersion > running.Version) return ReleaseCheckResult.Refused(ReleaseRefusal.UpdaterTooOld);
        return new(ReleaseCheckOutcome.NewerRelease, ReleaseRefusal.None, manifest);
    }
}
