using System.Text.Json;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Pure classifier for 4Seller <c>is-login</c> responses. Isolated so unit tests can cover
/// every edge case without HTTP.
/// </summary>
public static class FourSellerSessionProbe
{
    /// <summary>
    /// Classifies an is-login response.
    /// <see cref="FourSellerSessionState.Expired"/> only when HTTP is successful AND the JSON
    /// body has <c>code:0</c> with <c>data</c> that is not true (false/null/missing).
    /// Everything else that is not a clear login is <see cref="FourSellerSessionState.Unverified"/>.
    /// </summary>
    public static FourSellerSessionState Classify(int httpStatus, string? body, out string detail)
    {
        if (httpStatus < 200 || httpStatus > 299)
        {
            detail =
                $"Không xác minh được phiên 4Seller (HTTP {httpStatus} is-login): {Truncate(body, 160)}. " +
                "Đây KHÔNG phải cookie hết hạn — thử lại sau hoặc kiểm tra proxy.";
            return FourSellerSessionState.Unverified;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            detail =
                "Không xác minh được phiên 4Seller (is-login trả body rỗng). " +
                "Đây KHÔNG phải cookie hết hạn — thử lại sau.";
            return FourSellerSessionState.Unverified;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!root.TryGetProperty("code", out var codeProp) ||
                codeProp.ValueKind != JsonValueKind.Number)
            {
                detail =
                    "Không xác minh được phiên 4Seller (is-login thiếu code). " +
                    $"Body: {Truncate(body, 160)}. Đây KHÔNG phải cookie hết hạn.";
                return FourSellerSessionState.Unverified;
            }

            var code = codeProp.GetInt32();
            if (code != 0)
            {
                var messages = TryReadMessages(root);
                detail =
                    $"Không xác minh được phiên 4Seller (is-login code={code}" +
                    (string.IsNullOrWhiteSpace(messages) ? string.Empty : $", messages={messages}") +
                    "). Đây KHÔNG phải cookie hết hạn — thử lại sau.";
                return FourSellerSessionState.Unverified;
            }

            // code == 0: inspect data
            if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.True)
            {
                detail = string.Empty;
                return FourSellerSessionState.LoggedIn;
            }

            // code 0 + data false / null / missing → confirmed not logged in
            detail =
                "Cookie 4Seller đã hết hạn hoặc chưa đăng nhập. " +
                "Đăng nhập lại 4Seller và cập nhật cookie tại Hồ sơ cá nhân.";
            return FourSellerSessionState.Expired;
        }
        catch (JsonException)
        {
            detail =
                $"Không xác minh được phiên 4Seller (is-login không trả JSON hợp lệ): {Truncate(body, 160)}. " +
                "Đây KHÔNG phải cookie hết hạn — có thể là trang chặn / lỗi mạng tạm thời.";
            return FourSellerSessionState.Unverified;
        }
    }

    private static string? TryReadMessages(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messagesProp))
        {
            return null;
        }

        return messagesProp.ValueKind switch
        {
            JsonValueKind.String => messagesProp.GetString(),
            JsonValueKind.Array => string.Join("; ",
                messagesProp.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
            _ => messagesProp.ToString()
        };
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}
