using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.Listing;
using Application.Interfaces;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Integrations.OpenAI;

/// <summary>
/// OpenAI Chat Completions client for listing description rewrite.
/// </summary>
public class OpenAiApiService : IOpenAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiSettings _settings;
    private readonly ILogger<OpenAiApiService> _logger;

    /// <summary>
    /// Initializes the OpenAI API client.
    /// </summary>
    public OpenAiApiService(
        HttpClient httpClient,
        IOptions<OpenAiSettings> settings,
        ILogger<OpenAiApiService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> RewriteProductDescriptionAsync(
        ProductDescriptionRewriteInput input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BusinessException("OpenAI ApiKey chưa được cấu hình (OpenAI:ApiKey).");
        }

        if (string.IsNullOrWhiteSpace(_settings.RewriteDescriptionPrompt))
        {
            throw new BusinessException("OpenAI RewriteDescriptionPrompt chưa được cấu hình.");
        }

        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-5.4" : _settings.Model.Trim();
        var userPrompt = _settings.RewriteDescriptionPrompt
            .Replace("{{PRODUCT_TITLE}}", input.Title, StringComparison.Ordinal)
            .Replace(
                "{{PRODUCT_FACTS}}",
                string.IsNullOrWhiteSpace(input.ProductFacts)
                    ? "(none provided)"
                    : input.ProductFacts,
                StringComparison.Ordinal)
            .Replace("{{PRODUCT_DESCRIPTION}}", input.OriginalDescription, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(input.ExtraInstruction))
        {
            userPrompt += "\n\nADDITIONAL REQUIREMENT\n" + input.ExtraInstruction.Trim();
        }

        var messages = string.IsNullOrWhiteSpace(_settings.RewriteDescriptionSystemPrompt)
            ? new[] { new { role = "user", content = userPrompt } }
            : new[]
            {
                new { role = "system", content = _settings.RewriteDescriptionSystemPrompt },
                new { role = "user", content = userPrompt }
            };

        var text = await SendChatAsync(
            model,
            messages,
            _settings.RewriteTemperature,
            "rewrite",
            cancellationToken,
            input.Seed);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BusinessException("OpenAI không trả về nội dung mô tả.");
        }

        return text;
    }

    /// <inheritdoc />
    public async Task<string> RewriteProductTitleAsync(
        ProductTitleRewriteInput input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BusinessException("OpenAI ApiKey chưa được cấu hình (OpenAI:ApiKey).");
        }

        if (string.IsNullOrWhiteSpace(_settings.RewriteTitlePrompt))
        {
            throw new BusinessException("OpenAI RewriteTitlePrompt chưa được cấu hình.");
        }

        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-5.4" : _settings.Model.Trim();
        var userPrompt = _settings.RewriteTitlePrompt
            .Replace("{{PRODUCT_TITLE}}", input.OriginalTitle, StringComparison.Ordinal)
            .Replace(
                "{{PRODUCT_FACTS}}",
                string.IsNullOrWhiteSpace(input.ProductFacts)
                    ? "(none provided)"
                    : input.ProductFacts,
                StringComparison.Ordinal)
            .Replace(
                "{{REWRITTEN_DESCRIPTION}}",
                input.RewrittenDescription,
                StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(input.ExtraInstruction))
        {
            userPrompt += "\n\nADDITIONAL REQUIREMENT\n" + input.ExtraInstruction.Trim();
        }

        var messages = string.IsNullOrWhiteSpace(_settings.RewriteTitleSystemPrompt)
            ? new[] { new { role = "user", content = userPrompt } }
            : new[]
            {
                new { role = "system", content = _settings.RewriteTitleSystemPrompt },
                new { role = "user", content = userPrompt }
            };

        var text = await SendChatAsync(
            model,
            messages,
            _settings.RewriteTitleTemperature,
            "rewrite title",
            cancellationToken,
            input.Seed);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BusinessException("OpenAI không trả về tiêu đề sản phẩm.");
        }

        return text;
    }

    /// <inheritdoc />
    public async Task<string> StripTitleBrandsAsync(
        string originalTitle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BusinessException("OpenAI ApiKey chưa được cấu hình (OpenAI:ApiKey).");
        }

        if (string.IsNullOrWhiteSpace(_settings.StripTitleBrandsPrompt))
        {
            throw new BusinessException("OpenAI StripTitleBrandsPrompt chưa được cấu hình.");
        }

        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-5.4" : _settings.Model.Trim();
        var userPrompt = _settings.StripTitleBrandsPrompt
            .Replace("{{PRODUCT_TITLE}}", originalTitle ?? string.Empty, StringComparison.Ordinal);

        var messages = string.IsNullOrWhiteSpace(_settings.StripTitleBrandsSystemPrompt)
            ? new[] { new { role = "user", content = userPrompt } }
            : new[]
            {
                new { role = "system", content = _settings.StripTitleBrandsSystemPrompt },
                new { role = "user", content = userPrompt }
            };

        var text = await SendChatAsync(
            model,
            messages,
            _settings.StripTitleBrandsTemperature,
            "strip title brands",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BusinessException("OpenAI không trả về danh sách thương hiệu tiêu đề.");
        }

        return text;
    }

    /// <inheritdoc />
    public async Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BusinessException("OpenAI ApiKey chưa được cấu hình (OpenAI:ApiKey).");
        }

        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-5.4" : _settings.Model.Trim();

        var text = await SendChatAsync(
            model,
            new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            0.1,
            "map Specifics",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BusinessException("OpenAI không trả về JSON map Specifics.");
        }

        return text;
    }

    /// <inheritdoc />
    public async Task<string> CompleteWhatToSellTodayAsync(
        string shopContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BusinessException("OpenAI ApiKey chưa được cấu hình (OpenAI:ApiKey).");
        }

        if (string.IsNullOrWhiteSpace(_settings.WhatToSellTodaySystemPrompt))
        {
            throw new BusinessException("OpenAI WhatToSellTodaySystemPrompt chưa được cấu hình.");
        }

        if (string.IsNullOrWhiteSpace(_settings.WhatToSellTodayUserPrompt))
        {
            throw new BusinessException("OpenAI WhatToSellTodayUserPrompt chưa được cấu hình.");
        }

        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-5.4" : _settings.Model.Trim();
        var userPrompt = _settings.WhatToSellTodayUserPrompt
            .Replace(
                "{{SHOP_CONTEXT}}",
                string.IsNullOrWhiteSpace(shopContext) ? "(none)" : shopContext,
                StringComparison.Ordinal);

        var text = await SendChatAsync(
            model,
            new[]
            {
                new { role = "system", content = _settings.WhatToSellTodaySystemPrompt },
                new { role = "user", content = userPrompt }
            },
            _settings.WhatToSellTodayTemperature,
            "what to sell today",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BusinessException("OpenAI không trả về kết quả «Hôm nay bán gì?».");
        }

        return text;
    }

    /// <inheritdoc />
    public async Task<string> CompleteShopKeywordResearchAsync(
        string shopContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BusinessException("OpenAI ApiKey chưa được cấu hình (OpenAI:ApiKey).");
        }

        if (string.IsNullOrWhiteSpace(_settings.ShopKeywordResearchSystemPrompt))
        {
            throw new BusinessException("OpenAI ShopKeywordResearchSystemPrompt chưa được cấu hình.");
        }

        if (string.IsNullOrWhiteSpace(_settings.ShopKeywordResearchUserPrompt))
        {
            throw new BusinessException("OpenAI ShopKeywordResearchUserPrompt chưa được cấu hình.");
        }

        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-5.4" : _settings.Model.Trim();
        var userPrompt = _settings.ShopKeywordResearchUserPrompt
            .Replace(
                "{{SHOP_CONTEXT}}",
                string.IsNullOrWhiteSpace(shopContext) ? "(none)" : shopContext,
                StringComparison.Ordinal);

        var text = await SendChatAsync(
            model,
            new[]
            {
                new { role = "system", content = _settings.ShopKeywordResearchSystemPrompt },
                new { role = "user", content = userPrompt }
            },
            _settings.ShopKeywordResearchTemperature,
            "shop keyword research",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BusinessException("OpenAI không trả về kết quả «Shop này bán gì».");
        }

        return text;
    }

    /// <inheritdoc />
    public async Task<string> CompleteShopKeywordResearchSuggestAsync(
        string shopContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BusinessException("OpenAI ApiKey chưa được cấu hình (OpenAI:ApiKey).");
        }

        if (string.IsNullOrWhiteSpace(_settings.ShopKeywordResearchSuggestSystemPrompt))
        {
            throw new BusinessException("OpenAI ShopKeywordResearchSuggestSystemPrompt chưa được cấu hình.");
        }

        if (string.IsNullOrWhiteSpace(_settings.ShopKeywordResearchSuggestUserPrompt))
        {
            throw new BusinessException("OpenAI ShopKeywordResearchSuggestUserPrompt chưa được cấu hình.");
        }

        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-5.4" : _settings.Model.Trim();
        var userPrompt = _settings.ShopKeywordResearchSuggestUserPrompt
            .Replace(
                "{{SHOP_CONTEXT}}",
                string.IsNullOrWhiteSpace(shopContext) ? "(none)" : shopContext,
                StringComparison.Ordinal);

        var text = await SendChatAsync(
            model,
            new[]
            {
                new { role = "system", content = _settings.ShopKeywordResearchSuggestSystemPrompt },
                new { role = "user", content = userPrompt }
            },
            _settings.ShopKeywordResearchSuggestTemperature,
            "shop keyword research suggest",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BusinessException("OpenAI không trả về kết quả đề xuất keyword.");
        }

        return text;
    }

    /// <summary>
    /// Posts a chat completion. Models that reject a custom <c>temperature</c> are
    /// retried once with the parameter omitted.
    /// </summary>
    private async Task<string?> SendChatAsync(
        string model,
        object messages,
        double? temperature,
        string operation,
        CancellationToken cancellationToken,
        int? seed = null)
    {
        var raw = await PostChatAsync(model, messages, temperature, seed, cancellationToken);

        if (!raw.Success && temperature.HasValue && IsUnsupportedTemperatureError(raw.Body))
        {
            _logger.LogInformation(
                "OpenAI model {Model} rejected temperature — retrying without it", model);
            raw = await PostChatAsync(model, messages, null, seed, cancellationToken);
        }

        if (!raw.Success)
        {
            _logger.LogWarning(
                "OpenAI {Operation} failed: {Status} {Body}",
                operation,
                raw.StatusCode,
                Truncate(raw.Body, 500));
            var detail = TryExtractErrorMessage(raw.Body) ?? $"HTTP {raw.StatusCode}";
            if (IsGeoBlockedError(raw.Body, detail))
            {
                throw new BusinessException(
                    "OpenAI chặn IP khu vực hiện tại (unsupported_country_region_territory). "
                    + "Cấu hình OpenAI:Proxy (proxy US/EU) trong appsettings rồi restart API.");
            }

            throw new BusinessException($"OpenAI {operation} thất bại: {detail}");
        }

        ChatCompletionResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ChatCompletionResponse>(raw.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OpenAI {Operation} JSON parse failed", operation);
            throw new BusinessException("OpenAI trả về phản hồi không hợp lệ.");
        }

        return payload?.Choices?
            .Select(c => c.Message?.Content)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?
            .Trim();
    }

    private async Task<(bool Success, int StatusCode, string Body)> PostChatAsync(
        string model,
        object messages,
        double? temperature,
        int? seed,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey.Trim());

        // Anonymous payload shape varies with optional temperature + seed — build a dictionary.
        var payload = new Dictionary<string, object?> { ["model"] = model, ["messages"] = messages };
        if (temperature.HasValue)
        {
            payload["temperature"] = temperature.Value;
        }

        if (seed.HasValue)
        {
            payload["seed"] = seed.Value;
        }

        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response.IsSuccessStatusCode, (int)response.StatusCode, body);
    }

    private static bool IsUnsupportedTemperatureError(string raw)
        => raw.Contains("temperature", StringComparison.OrdinalIgnoreCase)
           && (raw.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
               || raw.Contains("not supported", StringComparison.OrdinalIgnoreCase)
               || raw.Contains("does not support", StringComparison.OrdinalIgnoreCase));

    private static bool IsGeoBlockedError(string raw, string detail)
        => raw.Contains("unsupported_country_region_territory", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("Country, region, or territory not supported", StringComparison.OrdinalIgnoreCase);

    private static string? TryExtractErrorMessage(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
