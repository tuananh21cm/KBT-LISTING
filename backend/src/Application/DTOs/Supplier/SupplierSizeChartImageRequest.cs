namespace Application.DTOs.Supplier;

/// <summary>Link a Bunny CDN size chart image to a supplier product snapshot.</summary>
public class SupplierSizeChartImageRequest
{
    /// <summary>Source product URL (same as post-html).</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>Public Bunny CDN URL from <c>POST /files/upload</c>.</summary>
    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>Optional uploaded file id (audit only).</summary>
    public Guid? FileId { get; set; }
}
