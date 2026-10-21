using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Interfaces;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Integrations.Xai;

/// <summary>
/// x.ai Grok Imagine client for size-chart image generation.
/// </summary>
public class XaiImageService : IXaiImageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly XaiSettings _settings;
    private readonly ILogger<XaiImageService> _logger;

    /// <summary>Initializes the x.ai image client.</summary>
    public XaiImageService(
        HttpClient httpClient,
        IOptions<XaiSettings> settings,
        ILogger<XaiImageService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(byte[] ImageBytes, bool UsedTextOnlyFallback)> GenerateSizeChartAsync(
        string prompt,
        string? productImageUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BusinessException("x.ai ApiKey chưa được cấu hình (Xai:ApiKey).");
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new BusinessException("Prompt tạo Size Chart trống.");
        }

        var model = string.IsNullOrWhiteSpace(_settings.Model)
            ? "grok-imagine-image"
            : _settings.Model.Trim();
        var resolution = NormalizeResolution(_settings.Resolution);
        var aspectRatio = string.IsNullOrWhiteSpace(_settings.AspectRatio)
            ? "1:1"
            : _settings.AspectRatio.Trim();

        var hasProductImage = !string.IsNullOrWhiteSpace(productImageUrl);
        if (hasProductImage)
        {
            try
            {
                var bytes = await PostImageAsync(
                    "images/edits",
                    BuildEditBody(model, prompt, resolution, aspectRatio, productImageUrl!.Trim()),
                    cancellationToken);
                return (bytes, false);
            }
            catch (BusinessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "x.ai image edit failed for product reference; falling back to text-only generation.");
            }
        }

        var generationBytes = await PostImageAsync(
            "images/generations",
            BuildGenerationBody(model, prompt, resolution, aspectRatio),
            cancellationToken);
        return (generationBytes, true);
    }

    private async Task<byte[]> PostImageAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey.Trim());
        request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "x.ai image {Path} failed ({Status}): {Body}",
                path,
                (int)response.StatusCode,
                Truncate(responseBody, 400));
            throw new BusinessException(
                $"x.ai tạo ảnh thất bại ({(int)response.StatusCode}): {Truncate(responseBody, 200)}");
        }

        var bytes = ExtractBase64Image(responseBody);
        if (bytes.Length == 0)
        {
            throw new BusinessException("x.ai không trả về dữ liệu ảnh.");
        }

        return bytes;
    }

    private static object BuildEditBody(
        string model,
        string prompt,
        string resolution,
        string aspectRatio,
        string productImageUrl) =>
        new
        {
            model,
            prompt,
            resolution,
            aspect_ratio = aspectRatio,
            response_format = "b64_json",
            image = new
            {
                url = productImageUrl,
                type = "image_url"
            }
        };

    private static object BuildGenerationBody(
        string model,
        string prompt,
        string resolution,
        string aspectRatio) =>
        new
        {
            model,
            prompt,
            resolution,
            aspect_ratio = aspectRatio,
            response_format = "b64_json",
            n = 1
        };

    private static string NormalizeResolution(string? value)
    {
        var v = (value ?? "1k").Trim().ToLowerInvariant();
        return v is "2k" ? "2k" : "1k";
    }

    private static byte[] ExtractBase64Image(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("b64_json", out var b64El))
                {
                    var b64 = b64El.GetString();
                    if (!string.IsNullOrWhiteSpace(b64))
                    {
                        return Convert.FromBase64String(b64);
                    }
                }
            }
        }

        return [];
    }

    private static string Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text[..max] + "…";
}
