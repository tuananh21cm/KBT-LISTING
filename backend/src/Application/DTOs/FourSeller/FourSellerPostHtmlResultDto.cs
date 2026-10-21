namespace Application.DTOs.FourSeller;

/// <summary>
/// Result of proxying <c>postHtml-file-shop</c> to 4Seller.
/// </summary>
public class FourSellerPostHtmlResultDto
{
    /// <summary>True when 4Seller accepted the upload.</summary>
    public bool Ok { get; set; }

    /// <summary>User-facing status message.</summary>
    public string Message { get; set; } = string.Empty;
}
