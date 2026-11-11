using System.Text;

namespace PCSteward.SelfUpdate;

/// <summary>
/// The detached signature file published beside the release manifest. It is exactly three LF-terminated ASCII lines:
/// <code>
/// pcsteward-release-signature:1
/// key:&lt;16 lowercase hex characters&gt;
/// signature:&lt;base64 of the 64-byte ECDSA P-256 IEEE P1363 signature&gt;
/// </code>
/// The key line only selects which compiled key to try; the signed manifest repeats the key ID, so it is covered by the
/// signature as well.
/// </summary>
public sealed record ReleaseSignature(string KeyId, byte[] Signature)
{
    public const int MaximumBytes = 512;
    private const string Header = "pcsteward-release-signature:1";

    public static bool TryRead(ReadOnlySpan<byte> bytes, out ReleaseSignature? signature)
    {
        signature = null;
        if (bytes.Length is 0 or > MaximumBytes) return false;
        foreach (byte value in bytes)
        {
            if (value is not (0x0A or (>= 0x20 and <= 0x7E))) return false;
        }
        string text = Encoding.ASCII.GetString(bytes);
        if (!text.EndsWith('\n')) return false;
        string[] lines = text[..^1].Split('\n');
        if (lines.Length != 3 || !string.Equals(lines[0], Header, StringComparison.Ordinal) ||
            !lines[1].StartsWith("key:", StringComparison.Ordinal) || !lines[2].StartsWith("signature:", StringComparison.Ordinal))
            return false;
        string keyId = lines[1]["key:".Length..];
        if (!ReleaseManifest.IsLowerHex(keyId, 16)) return false;
        string encoded = lines[2]["signature:".Length..];
        byte[] raw = new byte[64];
        if (encoded.Length != 88 || !Convert.TryFromBase64String(encoded, raw, out int written) || written != 64) return false;
        if (!string.Equals(Convert.ToBase64String(raw), encoded, StringComparison.Ordinal)) return false;
        signature = new(keyId, raw);
        return true;
    }

    /// <summary>Writes the canonical file text. Used by tests; the owner's signing script writes the same bytes.</summary>
    public string ToFileText() => Header + "\nkey:" + KeyId + "\nsignature:" + Convert.ToBase64String(Signature) + "\n";
}
