namespace Application.DTOs.FourSeller;

/// <summary>
/// Result of uploading a file to 4Seller COS (<c>/api/cos/get-sign</c> + PUT).
/// </summary>
public class FourSellerCosUploadResultDto
{
    /// <summary>Public CDN URL (<c>https://{domain}{key}</c>).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>COS object key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>CDN domain (e.g. 4seller-res.meiyunji.net).</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>4Seller local material id (use as sizeChartId / erpVideoId).</summary>
    public long? LocalMaterialId { get; set; }

    /// <summary>File MD5 (hex) used for get-sign.</summary>
    public string Md5 { get; set; } = string.Empty;

    /// <summary>get-sign operation (e.g. add).</summary>
    public string? Operation { get; set; }

    /// <summary>Original file name.</summary>
    public string FileName { get; set; } = string.Empty;
}
