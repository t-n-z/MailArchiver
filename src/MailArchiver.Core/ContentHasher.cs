using System.Security.Cryptography;
using System.Text;

namespace MailArchiver;

/// <summary>
/// Hashes a message's identity from source-derived fields (never from the exported file
/// bytes — those are not byte-stable run to run). Two tiers:
///  - <see cref="ComputeQuickKey"/> — cheap, header-only; lets a re-run skip an
///    already-archived message without loading its body.
///  - <see cref="Compute"/> — full content hash; the authoritative dedup key.
///
/// Each <see cref="IMailMessage"/> implementation decides <em>which</em> fields go into
/// each tier (what is "cheap" differs by format); this class only applies SHA-256.
/// </summary>
public static class ContentHasher
{
    public static string ComputeQuickKey(IMailMessage message) => Sha256Hex(message.QuickKeyMaterial);

    public static string Compute(IMailMessage message) => Sha256Hex(message.ContentHashMaterial);

    private static string Sha256Hex(string material) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material ?? string.Empty)));
}
