using System.Security.Cryptography;

namespace PCSteward.SelfUpdate;

/// <summary>
/// One compiled release public key (ADR-032 section 1): an ECDSA P-256 point. The key ID is the first eight bytes of
/// SHA-256 over <c>X || Y</c>, as 16 lowercase hex characters, so the owner's signing script and the app derive the same ID
/// from the same public key. Keys are code, never configuration, database rows or downloaded data.
/// </summary>
public sealed class ReleaseSigningKey
{
    private const int CoordinateLength = 32;
    private readonly byte[] x;
    private readonly byte[] y;

    private ReleaseSigningKey(string name, byte[] x, byte[] y)
    {
        Name = name;
        this.x = x;
        this.y = y;
        byte[] point = [.. x, .. y];
        KeyId = Convert.ToHexStringLower(SHA256.HashData(point).AsSpan(0, 8));
    }

    /// <summary>Owner-facing label, for example "primary" or "backup". Never used for trust decisions.</summary>
    public string Name { get; }

    public string KeyId { get; }

    /// <summary>Creates a key from the 32-byte X and Y coordinates written as 64 hex characters each.</summary>
    public static ReleaseSigningKey FromHex(string name, string xHex, string yHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        byte[] x = Convert.FromHexString(xHex);
        byte[] y = Convert.FromHexString(yHex);
        if (x.Length != CoordinateLength || y.Length != CoordinateLength) throw new ArgumentException("A P-256 coordinate is 32 bytes.");
        ReleaseSigningKey key = new(name, x, y);
        key.Create().Dispose();
        return key;
    }

    /// <summary>Verifies a 64-byte IEEE P1363 (r || s) ECDSA P-256 signature over SHA-256 of <paramref name="data"/>.</summary>
    internal bool Verifies(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != 2 * CoordinateLength) return false;
        using ECDsa ecdsa = Create();
        return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private ECDsa Create() => ECDsa.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        Q = new ECPoint { X = (byte[])x.Clone(), Y = (byte[])y.Clone() },
    });
}
