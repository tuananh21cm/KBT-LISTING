namespace Application.Options;

/// <summary>
/// x.ai size-chart prompt template (bound from Xai appsettings section).
/// </summary>
public class XaiSizeChartOptions
{
    /// <summary>Same section as Infrastructure XaiSettings.</summary>
    public const string SectionName = "Xai";

    /// <summary>
    /// Prompt template with <c>{{PRODUCT_TYPE}}</c> and <c>{{SIZE_CHART_DATA}}</c>.
    /// </summary>
    public string SizeChartPrompt { get; set; } = string.Empty;
}
