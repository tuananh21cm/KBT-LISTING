using System.Text.Json;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Publish / unpublishable text from 4Seller page or detail JSON.
/// Handles string or array payloads, field aliases, and nested info objects.
/// </summary>
public static class FourSellerFailedMessageReader
{
    private static readonly string[] FieldNames =
    {
        "failedMessage", "failed_message",
        "failMessage", "fail_message",
        "failedMsg", "failed_msg",
        "failMsg", "fail_msg",
        "errorMessage", "error_message",
        "rejectReason", "reject_reason",
        "unpublishableReason", "unpublishable_reason",
        "unPublishableMessage", "unpublishableMessage",
        "tipMessage", "tip_message",
        "warningMessage", "warning_message",
        "warnMessage", "warn_message",
        "publishFailReason", "publish_fail_reason",
        "auditFailReason", "audit_fail_reason",
        "checkMessage", "check_message",
        "requireMessage", "require_message"
    };

    private static readonly string[] NestedObjectNames =
    {
        "publishInfo", "publish_info",
        "checkInfo", "check_info",
        "warningInfo", "warning_info",
        "productCheckInfo", "product_check_info",
        "auditInfo", "audit_info"
    };

    public static string? Read(JsonElement element)
    {
        var direct = ReadFromElement(element);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        foreach (var nestName in NestedObjectNames)
        {
            if (!element.TryGetProperty(nestName, out var nest) || nest.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var nested = ReadFromElement(nest);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }

        return null;
    }

    private static string? ReadFromElement(JsonElement element)
    {
        foreach (var name in FieldNames)
        {
            if (!element.TryGetProperty(name, out var prop))
            {
                continue;
            }

            var text = ReadJsonElementAsText(prop)?.Trim();
            if (IsMeaningfulFailedMessage(text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool IsMeaningfulFailedMessage(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && !string.Equals(text, "null", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);

    private static string? ReadJsonElementAsText(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => string.Join(
                "; ",
                element.EnumerateArray()
                    .Select(ReadJsonElementAsText)
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
            _ => null
        };
}
