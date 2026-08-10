using System.Security.Cryptography;
using System.Text;

namespace Grigori.Integrations.GitHub;

/// <summary>
/// Validates the <c>X-Hub-Signature-256</c> header GitHub sends with every delivery. This is
/// the only thing standing between the endpoint and the open internet, so it compares in
/// fixed time and treats every malformed header as a rejection rather than an error.
/// </summary>
public static class GitHubWebhookSignature
{
    private const string Prefix = "sha256=";

    public static bool IsValid(ReadOnlySpan<byte> payload, string? header, string secret)
    {
        if (string.IsNullOrEmpty(header))
            return false;

        Span<byte> computed = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload, computed);

        // Compared as hex rather than decoding the header first: a malformed hex string is
        // then simply a mismatch instead of a parse failure needing its own branch, and
        // FixedTimeEquals still does the work. GitHub always sends lowercase.
        var expected = string.Concat(Prefix, Convert.ToHexStringLower(computed));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(header));
    }
}
