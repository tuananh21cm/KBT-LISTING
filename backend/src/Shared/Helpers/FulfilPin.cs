using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Shared.Helpers;

/// <summary>
/// Fulfil extension PIN (exactly 4 digits) — normalize, validate, HMAC-SHA256 hex hash.
/// </summary>
public static partial class FulfilPin
{
    /// <summary>Trims whitespace; empty when null/whitespace.</summary>
    public static string Normalize(string? pin) => pin?.Trim() ?? string.Empty;

    /// <summary>True when the value is exactly four decimal digits.</summary>
    public static bool IsValid(string? pin) => PinRegex().IsMatch(Normalize(pin));

    /// <summary>
    /// Deterministic HMAC-SHA256 of the PIN with the given pepper, hex lowercase (64 chars).
    /// Used for unique-index lookup on PIN-only login.
    /// </summary>
    public static string ComputeHash(string pin, string pepper)
    {
        var normalized = Normalize(pin);
        var key = Encoding.UTF8.GetBytes(pepper);
        var data = Encoding.UTF8.GetBytes(normalized);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [GeneratedRegex(@"^\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex PinRegex();
}
