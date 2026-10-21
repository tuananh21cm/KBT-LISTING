namespace Application.DTOs.Supplier;

/// <summary>Size chart capture status for a supplier product snapshot.</summary>
public class SupplierSizeChartImageStatusDto
{
    /// <summary>Whether a Bunny CDN size chart URL is stored.</summary>
    public bool HasImage { get; set; }

    /// <summary>Public Bunny CDN URL when present.</summary>
    public string? PublicUrl { get; set; }
}
