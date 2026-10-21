using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations.Telegram;

/// <summary>
/// Low-level Telegram Bot API client (HTML sendMessage + optional small link preview).
/// </summary>
public class TelegramApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramApiService> _logger;

    /// <summary>
    /// Initializes the Telegram API client.
    /// </summary>
    public TelegramApiService(HttpClient httpClient, ILogger<TelegramApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Sends an HTML text message. When <paramref name="previewUrl"/> is set, Telegram shows a
    /// small link-preview for that image URL (no local download/resize on our server).
    /// </summary>
    public Task SendHtmlMessageAsync(
        string botToken,
        string chatId,
        string text,
        string? previewUrl,
        CancellationToken cancellationToken = default)
    {
        var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
        var body = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["parse_mode"] = "HTML"
        };

        if (!string.IsNullOrWhiteSpace(previewUrl))
        {
            body["link_preview_options"] = new Dictionary<string, object?>
            {
                ["is_disabled"] = false,
                ["url"] = previewUrl.Trim(),
                ["prefer_small_media"] = true,
                ["prefer_large_media"] = false,
                ["show_above_text"] = true
            };
        }
        else
        {
            body["link_preview_options"] = new Dictionary<string, object?>
            {
                ["is_disabled"] = true
            };
        }

        return PostJsonAsync(url, body, cancellationToken);
    }

    private async Task PostJsonAsync(
        string url,
        Dictionary<string, object?> body,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(url, body, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        TelegramApiResponse? payload = null;
        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<TelegramApiResponse>(raw);
        }
        catch (System.Text.Json.JsonException)
        {
            // keep payload null; fall through to error handling
        }

        if (!response.IsSuccessStatusCode || payload is null || !payload.Ok)
        {
            var description = payload?.Description ?? raw;
            var migrateTo = payload?.Parameters?.MigrateToChatId;
            if (migrateTo.HasValue)
            {
                _logger.LogWarning(
                    "Telegram API call failed ({Status}): {Description} — migrate_to_chat_id={MigrateToChatId}. Cập nhật telegram.orders_chat_id / cancel_refund_chat_id trong Settings.",
                    (int)response.StatusCode,
                    description,
                    migrateTo.Value);
                throw new InvalidOperationException(
                    $"Telegram API failed: {description} (migrate_to_chat_id={migrateTo.Value})");
            }

            _logger.LogWarning(
                "Telegram API call failed ({Status}): {Description}",
                (int)response.StatusCode,
                description);
            throw new InvalidOperationException($"Telegram API failed: {description}");
        }
    }

    private sealed class TelegramApiResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("parameters")]
        public TelegramApiErrorParameters? Parameters { get; set; }
    }

    private sealed class TelegramApiErrorParameters
    {
        [JsonPropertyName("migrate_to_chat_id")]
        public long? MigrateToChatId { get; set; }
    }
}
