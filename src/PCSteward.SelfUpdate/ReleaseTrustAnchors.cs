namespace PCSteward.SelfUpdate;

/// <summary>
/// The compiled trust anchors for PC Steward's own releases (ADR-032 sections 1 and 2). The release public keys are added
/// here, in code, once the owner has generated them on their own PC. Until then the list is empty and every release is
/// refused as "no release key built into this version". Nothing at run time can add, replace or relax a key.
/// </summary>
public static class ReleaseTrustAnchors
{
    /// <summary>The only product name a release manifest may carry.</summary>
    public const string Product = "PCSteward";

    /// <summary>The only channel this build accepts (owner decision: one beta channel for now).</summary>
    public const string Channel = "beta";

    /// <summary>The only manifest schema this build understands.</summary>
    public const int ManifestSchema = 1;

    /// <summary>The fixed public repository releases come from. Display and request construction only, never trust.</summary>
    public const string RepositoryOwner = "DanDG923";

    /// <summary>The fixed public repository releases come from. Display and request construction only, never trust.</summary>
    public const string RepositoryName = "pc-steward";

    /// <summary>The release public keys compiled into this build. A manifest signed by any one of them is genuine.</summary>
    public static IReadOnlyList<ReleaseSigningKey> ReleaseKeys { get; } = [];
}
