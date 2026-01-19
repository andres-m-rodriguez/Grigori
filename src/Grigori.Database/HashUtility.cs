using System.Security.Cryptography;
using System.Text;

namespace Grigori.Database;

public static class HashUtility
{
    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
