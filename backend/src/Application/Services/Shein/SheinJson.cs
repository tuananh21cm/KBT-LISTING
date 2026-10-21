using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Application.Services.Shein;

/// <summary>
/// Loose JSON accessors mirroring the JavaScript semantics the SHEIN payload readers were
/// written against: a missing key and a null value are the same thing, numbers and numeric
/// strings are interchangeable, and a wrong-shaped node never throws.
/// </summary>
internal static class SheinJson
{
    private static readonly Regex FloatPrefix = new(
        @"^[+-]?(\d+(\.\d*)?|\.\d+)",
        RegexOptions.Compiled);

    private static readonly Regex NonMoneyChars = new(@"[^0-9.,\-]", RegexOptions.Compiled);

    /// <summary>Returns the node as an object, or null when it is not one.</summary>
    public static JsonObject? Obj(JsonNode? node) => node as JsonObject;

    /// <summary>Returns the node as an array, or null when it is not one.</summary>
    public static JsonArray? Arr(JsonNode? node) => node as JsonArray;

    /// <summary>Reads a property, returning null for a missing key or a JSON null.</summary>
    public static JsonNode? Get(JsonObject? obj, string key)
    {
        if (obj is null)
        {
            return null;
        }

        return obj.TryGetPropertyValue(key, out var value) ? value : null;
    }

    /// <summary>Returns the first key present with a non-null value (JS <c>a ?? b ?? c</c>).</summary>
    public static JsonNode? Pick(JsonObject? obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Get(obj, key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>True when the key exists at all, even holding null (JS <c>key in obj</c>).</summary>
    public static bool Has(JsonObject? obj, string key) => obj?.ContainsKey(key) == true;

    /// <summary>Elements of an array property, or empty when absent/not an array.</summary>
    public static IEnumerable<JsonNode?> Items(JsonObject? obj, params string[] keys)
    {
        var node = Pick(obj, keys);
        return node is JsonArray array ? array : [];
    }

    /// <summary>Object elements of an array property, skipping non-objects.</summary>
    public static IEnumerable<JsonObject> ObjectItems(JsonObject? obj, params string[] keys) =>
        Items(obj, keys).OfType<JsonObject>();

    /// <summary>String form of a scalar node (numbers become their literal text).</summary>
    public static string? Str(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var s))
        {
            return s;
        }

        return value.ToJsonString().Trim('"');
    }

    /// <summary>Trimmed string, or null when absent/empty.</summary>
    public static string? Text(JsonObject? obj, params string[] keys)
    {
        var raw = Str(Pick(obj, keys))?.Trim();
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    /// <summary>Trimmed string of a node, or empty.</summary>
    public static string TextOf(JsonNode? node) => Str(node)?.Trim() ?? string.Empty;

    /// <summary>JavaScript <c>Number(value)</c>: null for NaN, 0 for the empty string.</summary>
    public static double? ToNumber(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var d))
        {
            return d;
        }

        if (value.TryGetValue<bool>(out var b))
        {
            return b ? 1 : 0;
        }

        var raw = Str(value)?.Trim();
        if (raw is null)
        {
            return null;
        }

        if (raw.Length == 0)
        {
            return 0;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// JavaScript truthiness for the flag shapes SHEIN mixes: <c>true</c>, <c>1</c>, <c>"1"</c>,
    /// <c>"true"</c>, <c>"yes"</c>, <c>"y"</c>.
    /// </summary>
    public static bool Truthy(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue<bool>(out var b))
        {
            return b;
        }

        if (value.TryGetValue<double>(out var d))
        {
            return Math.Abs(d - 1) < double.Epsilon;
        }

        var raw = Str(value)?.Trim().ToLowerInvariant();
        return raw is "1" or "true" or "yes" or "y";
    }

    /// <summary>Money value in the shapes SHEIN uses: number, string, or an amount object.</summary>
    public static decimal? ReadMoney(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonValue value:
                return ParseMoney(value);
            case JsonObject obj:
                foreach (var key in MoneyKeys)
                {
                    if (!obj.ContainsKey(key))
                    {
                        continue;
                    }

                    var parsed = ParseMoney(Get(obj, key));
                    if (parsed.HasValue)
                    {
                        return parsed;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    /// <summary>Money keys probed inside an amount object, in SHEIN's own precedence.</summary>
    public static readonly string[] MoneyKeys =
        ["usdAmount", "amount", "value", "price", "amountWithSymbol", "priceWithSymbol"];

    /// <summary>Money value of the first present key.</summary>
    public static decimal? Money(JsonObject? obj, params string[] keys) => ReadMoney(Pick(obj, keys));

    private static decimal? ParseMoney(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var dec))
        {
            return dec;
        }

        if (value.TryGetValue<double>(out var dbl))
        {
            return double.IsFinite(dbl) ? (decimal)dbl : null;
        }

        var raw = Str(value);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Bỏ ký hiệu tiền tệ, chuẩn hoá dấu phân cách, rồi lấy đoạn số ở đầu (parseFloat của
        // JavaScript dừng ở ký tự không hợp lệ đầu tiên).
        var cleaned = NormalizeSeparators(NonMoneyChars.Replace(raw, string.Empty));
        var match = FloatPrefix.Match(cleaned);
        if (!match.Success)
        {
            return null;
        }

        return decimal.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Đưa chuỗi số về dạng invariant (chỉ dấu chấm thập phân, không phân cách nghìn).
    ///
    /// Không được mặc định dấu phẩy là phân cách nghìn: storefront ngoài Mỹ trả
    /// <c>"1.234,56"</c>, xoá thẳng dấu phẩy cho ra <c>1.23456</c> — sai hai bậc độ lớn mà không hề
    /// báo lỗi.
    ///
    /// Ba luật, theo đúng thứ tự: có cả hai loại dấu thì dấu <b>ở sau</b> là thập phân; nhiều dấu
    /// cùng loại thì tất cả là phân cách nghìn; một dấu duy nhất thì dấu chấm luôn là thập phân
    /// (mặc định US, giữ nguyên hành vi cũ cho <c>"16.0400"</c>) còn dấu phẩy chỉ là thập phân khi
    /// theo sau nó có 1–2 chữ số — <c>"1,234"</c> là một nghìn hai trăm ba mươi tư.
    /// </summary>
    private static string NormalizeSeparators(string cleaned)
    {
        var dots = cleaned.Count(c => c == '.');
        var commas = cleaned.Count(c => c == ',');
        if (dots == 0 && commas == 0) return cleaned;

        var decimalAt = -1;
        if (dots > 0 && commas > 0)
        {
            decimalAt = Math.Max(cleaned.LastIndexOf('.'), cleaned.LastIndexOf(','));
        }
        else if (dots == 1)
        {
            decimalAt = cleaned.LastIndexOf('.');
        }
        else if (commas == 1)
        {
            var at = cleaned.LastIndexOf(',');
            if (cleaned.Length - at - 1 is >= 1 and <= 2) decimalAt = at;
        }

        var sb = new System.Text.StringBuilder(cleaned.Length);
        for (var i = 0; i < cleaned.Length; i++)
        {
            var c = cleaned[i];
            if (c is '.' or ',')
            {
                if (i == decimalAt) sb.Append('.');
                continue;
            }
            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Serializes a node back to compact JSON, or null.</summary>
    public static string? ToJson(JsonNode? node) =>
        node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
}
