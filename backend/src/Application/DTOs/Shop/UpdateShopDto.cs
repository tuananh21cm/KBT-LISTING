namespace Application.DTOs.Shop;

/// <summary>
/// Update shop request DTO.
/// </summary>
public class UpdateShopDto
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Platform identifier.</summary>
    public Guid PlatformId { get; set; }

    /// <summary>External shop ID.</summary>
    public string? ExternalShopId { get; set; }

    /// <summary>Shop avatar image URL.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>4seller shop ID.</summary>
    public string? FourSellerShopId { get; set; }

    /// <summary>Shop region.</summary>
    public string? Region { get; set; }

    /// <summary>Shop URL.</summary>
    public string? ShopUrl { get; set; }

    /// <summary>Status.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Metadata JSON.</summary>
    public string? Metadata { get; set; }

    /// <summary>Proxy IP:port.</summary>
    public string? Proxy { get; set; }

    /// <summary>Browser profile name (đã login).</summary>
    public string? ProfileName { get; set; }

    /// <summary>Shop account email.</summary>
    public string? Email { get; set; }

    /// <summary>TikTok account password.</summary>
    public string? TiktokPassword { get; set; }

    /// <summary>Days live.</summary>
    public string? DayLive { get; set; }

    /// <summary>DELI metric.</summary>
    public int? Deli { get; set; }

    /// <summary>New phone number.</summary>
    public string? NewPhoneNumber { get; set; }

    /// <summary>Old bank account number.</summary>
    public string? OldBankNumber { get; set; }

    /// <summary>New bank account number.</summary>
    public string? NewBankNumber { get; set; }

    /// <summary>Payment recipient name (tên thanh toán tiền).</summary>
    public string? PaymentName { get; set; }

    /// <summary>Linked receiving bank account id (thẻ nhận tiền).</summary>
    public Guid? ReceivingBankAccountId { get; set; }

    /// <summary>Shop ownership type: Per (cá nhân) or Llc (công ty).</summary>
    public string? ShopType { get; set; }

    /// <summary>LLC company name.</summary>
    public string? LlcName { get; set; }

    /// <summary>Two-factor authentication secret.</summary>
    public string? TwoFactorAuth { get; set; }
}
