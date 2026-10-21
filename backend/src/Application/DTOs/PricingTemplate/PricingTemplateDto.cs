namespace Application.DTOs.PricingTemplate;

/// <summary>
/// Pricing template response DTO.
/// </summary>
public class PricingTemplateDto
{
    /// <summary>Template identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Supplier / dropship shipping fee (USD).</summary>
    public decimal SupplierShipping { get; set; }

    /// <summary>Import tax / duty (USD).</summary>
    public decimal Tax { get; set; }

    /// <summary>TikTok Discount percent.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>TikTok Fast Sale percent.</summary>
    public decimal FastSalePercent { get; set; }

    /// <summary>Fixed ads fee (USD).</summary>
    public decimal AdsFee { get; set; }

    /// <summary>TikTok shipping charged to customer (USD).</summary>
    public decimal TikTokShipping { get; set; }

    /// <summary>Platform fee percent.</summary>
    public decimal PlatformFeePercent { get; set; }

    /// <summary>Target mode: margin | profit.</summary>
    public string TargetMode { get; set; } = "margin";

    /// <summary>Margin percent of total import (margin mode).</summary>
    public decimal MarginPercent { get; set; }

    /// <summary>Fixed profit USD (profit mode).</summary>
    public decimal ProfitAmount { get; set; }

    /// <summary>Whether this is the user's default template.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated at (UTC).</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Create pricing template request.
/// </summary>
public class CreatePricingTemplateDto
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Supplier shipping fee (USD).</summary>
    public decimal SupplierShipping { get; set; }

    /// <summary>Import tax (USD).</summary>
    public decimal Tax { get; set; }

    /// <summary>Discount percent.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>Fast Sale percent.</summary>
    public decimal FastSalePercent { get; set; }

    /// <summary>Ads fee (USD).</summary>
    public decimal AdsFee { get; set; }

    /// <summary>TikTok shipping to customer (USD).</summary>
    public decimal TikTokShipping { get; set; }

    /// <summary>Platform fee percent.</summary>
    public decimal PlatformFeePercent { get; set; } = 6;

    /// <summary>Target mode: margin | profit.</summary>
    public string TargetMode { get; set; } = "margin";

    /// <summary>Margin percent.</summary>
    public decimal MarginPercent { get; set; } = 50;

    /// <summary>Profit amount USD.</summary>
    public decimal ProfitAmount { get; set; }

    /// <summary>Set as default template for the user.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// Update pricing template request.
/// </summary>
public class UpdatePricingTemplateDto
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Supplier shipping fee (USD).</summary>
    public decimal SupplierShipping { get; set; }

    /// <summary>Import tax (USD).</summary>
    public decimal Tax { get; set; }

    /// <summary>Discount percent.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>Fast Sale percent.</summary>
    public decimal FastSalePercent { get; set; }

    /// <summary>Ads fee (USD).</summary>
    public decimal AdsFee { get; set; }

    /// <summary>TikTok shipping to customer (USD).</summary>
    public decimal TikTokShipping { get; set; }

    /// <summary>Platform fee percent.</summary>
    public decimal PlatformFeePercent { get; set; } = 6;

    /// <summary>Target mode: margin | profit.</summary>
    public string TargetMode { get; set; } = "margin";

    /// <summary>Margin percent.</summary>
    public decimal MarginPercent { get; set; } = 50;

    /// <summary>Profit amount USD.</summary>
    public decimal ProfitAmount { get; set; }

    /// <summary>Set as default template for the user.</summary>
    public bool IsDefault { get; set; }
}
