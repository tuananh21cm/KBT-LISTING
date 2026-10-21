using Shared.Constants;

namespace Shared.Helpers;

/// <summary>
/// Parses a pasted block of request headers into a dictionary.
///
/// Chrome DevTools' "Copy request headers" produces exactly <c>name: value</c> per line, which is
/// what an operator has at hand when re-capturing a SHEIN session by hand. Accepting that shape
/// directly avoids the step where they hand-build JSON and typo a quote.
/// </summary>
public static class SheinSessionHeaderTextParser
{
    /// <summary>
    /// Reads <c>name: value</c> lines. Blank lines, <c>#</c> comments, HTTP request lines
    /// (<c>GET /… HTTP/2</c>), pseudo-headers (<c>:authority</c>) and denied headers are dropped.
    /// Later lines win, so pasting over a duplicated header behaves as expected.
    /// </summary>
    public static Dictionary<string, string> Parse(string? text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // HTTP/2 pseudo-headers (`:authority: us.shein.com`) come along with a DevTools copy
            // and are rebuilt by HttpClient — keeping them would only fail header validation.
            if (line.StartsWith(':'))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim().ToLowerInvariant();
            var value = line[(separator + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0)
            {
                continue;
            }

            // A header name can't contain whitespace; that shape is a request line, not a header.
            if (name.Any(char.IsWhiteSpace))
            {
                continue;
            }

            if (SheinSessionKeys.DeniedHeaders.Contains(name)
                || value.Length > SheinSessionKeys.MaxHeaderValueLength)
            {
                continue;
            }

            result[name] = value;

            if (result.Count >= SheinSessionKeys.MaxHeaders)
            {
                break;
            }
        }

        return result;
    }
}
