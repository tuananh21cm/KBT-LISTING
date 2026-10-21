using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shared.Constants;

/// <summary>
/// Reads the TikTok publish failure that names a product attribute value it will not accept.
/// <para>
/// Sample, as it arrives through 4Seller's <c>failedMessage</c> (sometimes behind a
/// 【Publish Failed】 prefix): <c>Operation Not Allowed. Cannot use custom value names for product
/// attribute `Washing instructions`: attribute ID `100401` does not support custom value names.
/// Remove or replace the custom value `Do not dry clean` and retry.</c>
/// </para>
/// <para>
/// TikTok reports this whenever a value reaches it without an id it recognises for that attribute —
/// including a value 4Seller still lists with a numeric id — so the message is the only reliable
/// evidence that the value is gone.
/// </para>
/// </summary>
public static partial class TikTokAttributeRejectionErrors
{
    /// <summary>Cheap pre-check before running the regex over every sync error.</summary>
    private const string CustomValueMarker = "custom value";

    /// <summary>Parses attribute id, attribute label and rejected value name from a failure message.</summary>
    public static bool TryParseCustomValueRejection(
        string? message,
        out string attributeId,
        out string attributeName,
        out string valueName)
    {
        attributeId = string.Empty;
        attributeName = string.Empty;
        valueName = string.Empty;

        if (string.IsNullOrWhiteSpace(message)
            || !message.Contains(CustomValueMarker, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = CustomValueRejectionRegex().Match(message);
        if (!match.Success)
        {
            return false;
        }

        attributeId = match.Groups["attrId"].Value.Trim();
        attributeName = match.Groups["attrName"].Value.Trim();
        valueName = match.Groups["value"].Value.Trim();

        return attributeId.Length > 0 && valueName.Length > 0;
    }

    /// <summary>
    /// Finds the <c>valueId</c> a listing payload carried for <paramref name="valueName"/> under
    /// <paramref name="attributeId"/>, or null when the payload only carried the name.
    /// </summary>
    /// <remarks>
    /// Tells apart the two causes of "cannot use custom value names": TikTok dropped the value from
    /// its enum (we sent a valid id and it still refused), versus our mapping never found an id. Only
    /// the first is evidence worth remembering.
    /// </remarks>
    public static string? ReadSentValueId(string? rawPayload, string attributeId, string valueName)
    {
        if (string.IsNullOrWhiteSpace(rawPayload)
            || string.IsNullOrWhiteSpace(attributeId)
            || string.IsNullOrWhiteSpace(valueName))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawPayload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var wantedValue = NormalizeValueKey(valueName);
        foreach (var field in ProductAttributeFields)
        {
            if (!document.RootElement.TryGetProperty(field, out var raw))
            {
                continue;
            }

            // 4Seller stores productAttributes either as an array or as a stringified array.
            using var nested = raw.ValueKind == JsonValueKind.String
                ? SafeParse(raw.GetString())
                : null;
            var array = nested?.RootElement ?? raw;
            if (array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var found = ReadValueIdFromArray(array, attributeId.Trim(), wantedValue);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static readonly string[] ProductAttributeFields =
        ["productAttributes", "attributes", "attributeList"];

    private static string? ReadValueIdFromArray(JsonElement array, string attributeId, string wantedValue)
    {
        foreach (var row in array.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var rowAttributeId = ReadText(row, "attributeId", "attrId", "id");
            if (!string.Equals(rowAttributeId, attributeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (row.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in values.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (NormalizeValueKey(ReadText(value, "valueName", "name")) != wantedValue)
                    {
                        continue;
                    }

                    var valueId = ReadText(value, "valueId", "id");
                    if (IsNumericId(valueId))
                    {
                        return valueId;
                    }
                }
            }

            // Older simplified rows keep a single scalar pair instead of a values array.
            if (NormalizeValueKey(ReadText(row, "attrValue", "value")) == wantedValue)
            {
                var scalarId = ReadText(row, "valueId");
                if (IsNumericId(scalarId))
                {
                    return scalarId;
                }
            }
        }

        return null;
    }

    private static JsonDocument? SafeParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadText(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            var text = property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.ToString(),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static bool IsNumericId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && long.TryParse(value.Trim(), out var parsed) && parsed > 0;

    /// <summary>
    /// Same shape as the Specifics mapper's key normalization (lowercase, alphanumerics only) so both
    /// sides match "Machine washable" to "machinewashable".
    /// </summary>
    private static string NormalizeValueKey(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : NonAlphaNumRegex().Replace(value.Trim().ToLowerInvariant(), string.Empty);

    [GeneratedRegex(
        @"custom value names for product attribute\s+`(?<attrName>[^`]+)`.*?attribute ID\s+`(?<attrId>\d+)`.*?custom value\s+`(?<value>[^`]+)`",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CustomValueRejectionRegex();

    [GeneratedRegex(@"[^a-z0-9]", RegexOptions.IgnoreCase)]
    private static partial Regex NonAlphaNumRegex();
}
