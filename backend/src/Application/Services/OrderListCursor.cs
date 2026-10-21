using System.Globalization;

namespace Application.Services;

/// <summary>
/// Opaque cursor for the orders list, encoding the sort position as
/// <c>(orderTimeKey, Id)</c> — the same pair the list sorts on, which is what makes the seek
/// unambiguous when several orders share a timestamp.
///
/// Format is deliberately boring (<c>ticks.guid</c>, base64url) so a stale cursor from an older
/// build decodes to nothing and the caller falls back to page one instead of paging wrongly.
/// </summary>
public static class OrderListCursor
{
    /// <summary>Encodes a sort position.</summary>
    public static string Encode(DateTime timeKey, Guid id)
    {
        var raw = $"{timeKey.Ticks.ToString(CultureInfo.InvariantCulture)}.{id:N}";
        return Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Decodes a cursor, or returns null when it is blank or not ours.</summary>
    public static (DateTime TimeKey, Guid Id)? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var padded = cursor.Trim().Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var raw = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(padded));

            var dot = raw.IndexOf('.');
            if (dot <= 0)
            {
                return null;
            }

            if (!long.TryParse(raw[..dot], CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParseExact(raw[(dot + 1)..], "N", out var id))
            {
                return null;
            }

            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                return null;
            }

            return (new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
