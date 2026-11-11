namespace PCSteward.Execution;

/// <summary>
/// ADR-034 §2–§3: the compiled release-key pins. Each pin is the SHA-256 (64 uppercase hex characters) of the DER
/// SubjectPublicKeyInfo of one self-signed release signing certificate. Nothing is added to the Windows trust stores; these
/// constants are the only anchor <see cref="AuthenticodeIdentity.TryReadPinnedSigner"/> accepts for a release image.
/// <para>
/// The keys do not exist yet: they are created in the owner's key session (ADR-034 build order step 6, approved separately
/// at the time), and their public-key hashes are committed here before any release build is signed. Until then both are
/// empty, an empty pin matches nothing, and the pinned check accepts no file. Neither value is ever read from a file, the
/// registry, the environment or the network.
/// </para>
/// </summary>
internal static class ReleaseSigningPins
{
    /// <summary>The primary key, generated inside the owner's YubiKey (PIV slot 9c, PIN and touch for every signature).</summary>
    internal const string Primary = "";

    /// <summary>The offline backup key (password-protected PFX on two removable drives); used only to move the primary pin.</summary>
    internal const string Backup = "";

    /// <summary>The well-formed compiled pins, primary first. Empty while no key has been created.</summary>
    internal static IReadOnlyList<string> Compiled { get; } = [.. new[] { Primary, Backup }.Where(IsWellFormed)];

    /// <summary>True for exactly 64 uppercase hexadecimal characters.</summary>
    internal static bool IsWellFormed(string? pin) =>
        pin is { Length: 64 } && pin.All(static character => character is (>= '0' and <= '9') or (>= 'A' and <= 'F'));
}
