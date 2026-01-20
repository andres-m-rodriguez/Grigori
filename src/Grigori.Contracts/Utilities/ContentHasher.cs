using System.Security.Cryptography;
using System.Text;

namespace Grigori.Contracts.Utilities;

/// <summary>
/// Utility class for computing content hashes for deduplication.
/// </summary>
public static class ContentHasher
{
    /// <summary>
    /// Computes a SHA256 hash of the given content.
    /// </summary>
    /// <param name="content">The content to hash.</param>
    /// <returns>A lowercase hex string representation of the hash.</returns>
    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
