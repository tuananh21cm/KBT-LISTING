using System.Globalization;
using System.Text.Json;
using Application.DTOs.FourSeller;
using Domain.Exceptions;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Parses 4Seller save-draft / publish / update push responses.
/// Root <c>code:0</c> is not enough — listing push often nests outcome under <c>data.code</c> /
/// <c>data.errorMessage</c>.
/// </summary>
public static class FourSellerListingPushResponseParser
{
    /// <summary>Parse push response body; sets <see cref="FourSellerListingPushResultDto.Success"/> false when nested push failed.</summary>
    public static FourSellerListingPushResultDto Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        long? listingId = null;
        string? tikTokProductId = null;
        var success = true;
        string? failureMessage = null;

        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("code", out var nestedCode) &&
                    nestedCode.ValueKind == JsonValueKind.Number &&
                    nestedCode.GetInt32() != 0)
                {
                    success = false;
                    failureMessage = ReadNestedPushError(data)
                                     ?? $"4Seller data.code={nestedCode.GetInt32()}";
                }

                var nestedError = ReadStringProperty(data, "errorMessage", "error_message");
                if (!string.IsNullOrWhiteSpace(nestedError))
                {
                    success = false;
                    failureMessage = nestedError.Trim();
                }

                if (TryReadLong(data, out var id, "id", "listingId", "listing_id") && id > 0)
                {
                    listingId = id;
                }

                tikTokProductId = ReadStringProperty(
                    data,
                    "productId",
                    "product_id",
                    "tiktokProductId",
                    "tikTokProductId");
            }
            else if (data.ValueKind == JsonValueKind.False
                     || data.ValueKind == JsonValueKind.Null)
            {
                // Root code:0 + data:false/null = silent reject; never treat as OK.
                success = false;
                failureMessage = data.ValueKind == JsonValueKind.Null
                    ? "4Seller data=null — publish không được chấp nhận."
                    : "4Seller data=false — publish không được chấp nhận.";
            }
            else if (data.ValueKind == JsonValueKind.True)
            {
                // Some endpoints return { code:0, data:true } with no nested listing id.
            }
            else if (data.ValueKind == JsonValueKind.Number && data.TryGetInt64(out var idNum) && idNum > 0)
            {
                listingId = idNum;
            }
        }

        var message = success
            ? ReadSuccessMessage(root)
            : failureMessage ?? ReadNestedPushError(root) ?? "4Seller từ chối push.";

        return new FourSellerListingPushResultDto
        {
            Success = success,
            Message = message,
            ListingId = listingId,
            TikTokProductId = tikTokProductId,
            RawResponse = Truncate(body, 2000)
        };
    }

    /// <summary>Throws <see cref="BusinessException"/> when the parsed push was rejected.</summary>
    public static FourSellerListingPushResultDto EnsureAccepted(FourSellerListingPushResultDto result)
    {
        if (result.Success)
        {
            return result;
        }

        throw new BusinessException(result.Message ?? "4Seller từ chối push.");
    }

    private static string ReadSuccessMessage(JsonElement root)
    {
        return ReadNestedPushError(root)
               ?? (root.TryGetProperty("msg", out var msg) ? msg.GetString() : null)
               ?? "success";
    }

    private static string? ReadNestedPushError(JsonElement root)
    {
        if (root.TryGetProperty("messages", out var messages))
        {
            var text = ReadJsonElementAsText(messages);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return TranslateFourSellerMessage(text);
            }
        }

        if (root.TryGetProperty("msg", out var msg))
        {
            var text = ReadJsonElementAsText(msg);
            if (!string.IsNullOrWhiteSpace(text) &&
                !string.Equals(text.Trim(), "success", StringComparison.OrdinalIgnoreCase))
            {
                return TranslateFourSellerMessage(text);
            }
        }

        if (root.TryGetProperty("message", out var message))
        {
            var text = ReadJsonElementAsText(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return TranslateFourSellerMessage(text);
            }
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[]
                     {
                         "errorMessage", "error_message", "message", "msg", "errorMsg", "error", "reason"
                     })
            {
                if (!data.TryGetProperty(key, out var nested))
                {
                    continue;
                }

                var text = ReadJsonElementAsText(nested);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return TranslateFourSellerMessage(text);
                }
            }
        }

        return null;
    }

    private static string? ReadStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }

            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.ToString();
            }
        }

        return null;
    }

    private static bool TryReadLong(JsonElement element, out long value, params string[] names)
    {
        value = 0;
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out value))
            {
                return true;
            }

            if (prop.ValueKind == JsonValueKind.String &&
                long.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        return false;
    }

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

    private static string TranslateFourSellerMessage(string message) => message.Trim();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
