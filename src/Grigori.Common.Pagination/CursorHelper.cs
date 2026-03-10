using System.Text;

namespace Grigori.Common.Pagination;

/// <summary>
/// Helper for encoding/decoding opaque cursors to hide internal format from clients.
/// </summary>
public static class CursorHelper
{
    public static string Encode(Guid id) => Convert.ToBase64String(id.ToByteArray());

    public static string Encode(int id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString()));

    public static Guid DecodeGuid(string cursor) => new(Convert.FromBase64String(cursor));

    public static int DecodeInt(string cursor) => int.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)));

    public static bool TryDecodeGuid(string cursor, out Guid result)
    {
        try
        {
            result = DecodeGuid(cursor);
            return true;
        }
        catch
        {
            result = Guid.Empty;
            return false;
        }
    }

    public static bool TryDecodeInt(string cursor, out int result)
    {
        try
        {
            result = DecodeInt(cursor);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }
}
