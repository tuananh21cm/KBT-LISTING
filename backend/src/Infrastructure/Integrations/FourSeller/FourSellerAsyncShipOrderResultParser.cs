using System.Globalization;
using System.Text.Json;
using Application.DTOs.FourSeller;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Parses 4Seller <c>get-async-ship-order-result</c> poll responses.
/// Success may appear as boolean <c>true</c> or string <c>"1"</c> with nested counters.
/// </summary>
public static class FourSellerAsyncShipOrderResultParser
{
    /// <summary>Parse async mark-ship poll response body.</summary>
    public static FourSellerAsyncShipOrderResultDto Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var msg = ReadApiErrorMessage(root);

        if (!root.TryGetProperty("data", out var data) ||
            data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new FourSellerAsyncShipOrderResultDto
            {
                Status = "Pending",
                Message = msg
            };
        }

        if (data.ValueKind == JsonValueKind.True)
        {
            return new FourSellerAsyncShipOrderResultDto { Status = "Success", Message = msg };
        }

        if (data.ValueKind == JsonValueKind.False)
        {
            return new FourSellerAsyncShipOrderResultDto { Status = "Failed", Message = msg };
        }

        if (data.ValueKind == JsonValueKind.String)
        {
            return ParseStringData(data.GetString(), msg);
        }

        if (data.ValueKind == JsonValueKind.Array)
        {
            return ParseArrayData(data, msg);
        }

        if (data.ValueKind == JsonValueKind.Object)
        {
            return ParseObjectData(data, msg);
        }

        return new FourSellerAsyncShipOrderResultDto { Status = "Pending", Message = msg };
    }

    private static FourSellerAsyncShipOrderResultDto ParseStringData(string? text, string? msg)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("processing", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("running", StringComparison.OrdinalIgnoreCase))
        {
            return new FourSellerAsyncShipOrderResultDto { Status = "Pending", Message = trimmed };
        }

        if (trimmed.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return new FourSellerAsyncShipOrderResultDto { Status = "Failed", Message = trimmed };
        }

        return new FourSellerAsyncShipOrderResultDto { Status = "Success", Message = trimmed };
    }

    private static FourSellerAsyncShipOrderResultDto ParseArrayData(JsonElement data, string? msg)
    {
        if (data.GetArrayLength() == 0)
        {
            return new FourSellerAsyncShipOrderResultDto { Status = "Success", Message = msg };
        }

        var hasFail = false;
        var messages = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            var itemMsg = ReadString(item, "msg", "message", "errorMsg", "errorMessage");
            if (!string.IsNullOrWhiteSpace(itemMsg))
            {
                messages.Add(itemMsg);
            }

            if (item.TryGetProperty("success", out var s) && IsJsonFalsy(s))
            {
                hasFail = true;
            }

            var statusText = ReadString(item, "status", "result") ?? string.Empty;
            if (statusText.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                statusText.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                hasFail = true;
            }
        }

        return new FourSellerAsyncShipOrderResultDto
        {
            Status = hasFail ? "Failed" : "Success",
            Message = messages.Count > 0 ? string.Join("; ", messages) : msg
        };
    }

    private static FourSellerAsyncShipOrderResultDto ParseObjectData(JsonElement data, string? msg)
    {
        if (data.TryGetProperty("success", out var successProp))
        {
            if (IsJsonFalsy(successProp))
            {
                return new FourSellerAsyncShipOrderResultDto
                {
                    Status = "Failed",
                    Message = ReadString(data, "msg", "message", "errorMsg") ?? msg
                };
            }

            if (IsJsonTruthy(successProp))
            {
                var failed = ReadJsonCount(data, "failed");
                if (failed is null or 0)
                {
                    return new FourSellerAsyncShipOrderResultDto
                    {
                        Status = "Success",
                        Message = ReadString(data, "msg", "message") ?? msg
                    };
                }

                return new FourSellerAsyncShipOrderResultDto
                {
                    Status = "Failed",
                    Message = ReadString(data, "msg", "message", "errorMsg") ?? msg
                };
            }
        }

        if (data.TryGetProperty("data", out var innerData) &&
            innerData.ValueKind == JsonValueKind.String &&
            string.Equals(innerData.GetString()?.Trim(), "[]", StringComparison.Ordinal) &&
            ReadJsonCount(data, "failed") is null or 0)
        {
            return new FourSellerAsyncShipOrderResultDto
            {
                Status = "Success",
                Message = ReadString(data, "msg", "message") ?? msg
            };
        }

        var status = ReadString(data, "status", "state", "result") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(status) ||
            status.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("processing", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("running", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            if (data.TryGetProperty("finished", out var finished) &&
                finished.ValueKind == JsonValueKind.False)
            {
                return new FourSellerAsyncShipOrderResultDto { Status = "Pending", Message = msg };
            }

            if (data.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.False)
            {
                return new FourSellerAsyncShipOrderResultDto { Status = "Pending", Message = msg };
            }

            var hasKeys = data.EnumerateObject().Any();
            if (!hasKeys || string.IsNullOrWhiteSpace(status))
            {
                return new FourSellerAsyncShipOrderResultDto { Status = "Pending", Message = msg };
            }
        }

        if (status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return new FourSellerAsyncShipOrderResultDto
            {
                Status = "Failed",
                Message = ReadString(data, "msg", "message", "errorMsg") ?? status
            };
        }

        if (data.TryGetProperty("finished", out var fin) && fin.ValueKind == JsonValueKind.True)
        {
            return new FourSellerAsyncShipOrderResultDto
            {
                Status = "Success",
                Message = ReadString(data, "msg", "message") ?? msg
            };
        }

        if (status.Contains("success", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("done", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return new FourSellerAsyncShipOrderResultDto
            {
                Status = "Success",
                Message = ReadString(data, "msg", "message") ?? msg
            };
        }

        return new FourSellerAsyncShipOrderResultDto { Status = "Pending", Message = msg ?? status };
    }

    internal static bool IsJsonTruthy(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var n) && n > 0,
            JsonValueKind.String => IsTruthyString(element.GetString()),
            _ => false
        };

    internal static bool IsJsonFalsy(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.False => true,
            JsonValueKind.True => false,
            JsonValueKind.Number => element.TryGetInt64(out var n) && n == 0,
            JsonValueKind.String => IsFalsyString(element.GetString()),
            _ => false
        };

    internal static int? ReadJsonCount(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(
                prop.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }

    private static bool IsTruthyString(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        return t.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFalsyString(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        return t.Equals("0", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadApiErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("messages", out var messages))
        {
            var text = ReadJsonElementAsText(messages);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        if (root.TryGetProperty("msg", out var msg))
        {
            var text = ReadJsonElementAsText(msg);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        if (root.TryGetProperty("message", out var message))
        {
            var text = ReadJsonElementAsText(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, params string[] names)
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
