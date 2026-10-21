namespace Application.Interfaces;

/// <summary>
/// x.ai Grok Imagine image generation/editing client.
/// </summary>
public interface IXaiImageService
{
    /// <summary>
    /// Generates a size-chart image using product photo as visual reference (image edit).
    /// Falls back to text-to-image when <paramref name="productImageUrl"/> is empty.
    /// </summary>
    /// <returns>PNG/JPEG bytes and whether text-only fallback was used.</returns>
    Task<(byte[] ImageBytes, bool UsedTextOnlyFallback)> GenerateSizeChartAsync(
        string prompt,
        string? productImageUrl,
        CancellationToken cancellationToken = default);
}
