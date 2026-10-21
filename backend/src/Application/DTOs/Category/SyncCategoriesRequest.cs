namespace Application.DTOs.Category;

/// <summary>
/// Request to sync the system-wide TikTok category catalog for a marketplace site.
/// </summary>
public class SyncCategoriesRequest
{
    /// <summary>
    /// Marketplace site (US, UK, …). Defaults to US when omitted.
    /// </summary>
    public string? Site { get; set; }
}
