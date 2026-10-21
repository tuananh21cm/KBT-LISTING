namespace Infrastructure.Integrations.Xai;

/// <summary>
/// x.ai Grok Imagine credentials and size-chart prompt from appsettings.
/// </summary>
public class XaiSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Xai";

    /// <summary>x.ai API key (Bearer).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>API base URL (must end with /v1 or /v1/).</summary>
    public string BaseUrl { get; set; } = "https://api.x.ai/v1/";

    /// <summary>Imagine model id (e.g. grok-imagine-image).</summary>
    public string Model { get; set; } = "grok-imagine-image";

    /// <summary>Output resolution: 1k or 2k.</summary>
    public string Resolution { get; set; } = "1k";

    /// <summary>Aspect ratio (1:1 for square size charts).</summary>
    public string AspectRatio { get; set; } = "1:1";

    /// <summary>
    /// Prompt template with <c>{{PRODUCT_TYPE}}</c> and <c>{{SIZE_CHART_DATA}}</c> placeholders.
    /// </summary>
    public string SizeChartPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Optional HTTP(S)/SOCKS egress proxy (same formats as OpenAI:Proxy).
    /// </summary>
    public string? Proxy { get; set; }
}
