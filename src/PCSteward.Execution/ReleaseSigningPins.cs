namespace PCSteward.Execution;

/// <summary>
/// ADR-034 §2–§3: the compiled release-key pins. Each pin is the SHA-256 (64 uppercase hex characters) of the DER
/// SubjectPublicKeyInfo of one self-signed release signing certificate. Nothing is added to the Windows trust stores; these
/// constants are the only anchor <see cref="AuthenticodeIdentity.TryReadPinnedSigner"/> accepts for a release image.
/// <para>
/// Both keys were created in the owner's key session on 2026-11-11 (ADR-034 build order step 6). The primary pin was checked
/// away from the owner's PC against Yubico's attestation of slot 9c, which chains to the Yubico Attestation Root 1 and
/// records PIN policy ALWAYS and touch policy ALWAYS. The backup key has no hardware attestation (security review M3
/// residual). Neither value is ever read from a file, the registry, the environment or the network.
/// </para>
/// </summary>
internal static class ReleaseSigningPins
{
    /// <summary>The primary key, generated inside the owner's YubiKey (PIV slot 9c, PIN and touch for every signature).</summary>
    internal const string Primary = "F8204BDBE73D3F4D898B3A1ECC0142DEE1AC3A136E7AE5D37FF8F3AD8DD2496A";

    /// <summary>The offline backup key (password-protected PFX on removable drives, kept apart from the YubiKey); used only to move the primary pin.</summary>
    internal const string Backup = "1F001AE0DA222A346C77F9831EC7A68B7F8910CAAD84CBF7F3502D6966499BA3";

    /// <summary>The well-formed compiled pins, primary first.</summary>
    internal static IReadOnlyList<string> Compiled { get; } = [.. new[] { Primary, Backup }.Where(IsWellFormed)];

    /// <summary>True for exactly 64 uppercase hexadecimal characters.</summary>
    internal static bool IsWellFormed(string? pin) =>
        pin is { Length: 64 } && pin.All(static character => character is (>= '0' and <= '9') or (>= 'A' and <= 'F'));
}
