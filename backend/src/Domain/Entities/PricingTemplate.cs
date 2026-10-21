using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Per-user reusable pricing formula preset for batch sale-price calculation (TikTok US dropship).
/// Cost is always taken from each SKU — not stored on the template.
/// </summary>
public class PricingTemplate : BaseEntity
{
    /// <summary>Template identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owner user (templates are private per account).</summary>
    public Guid CreatedById { get; set; }

    /// <summary>Display name (unique per user).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Supplier / dropship shipping fee (USD).</summary>
    public decimal SupplierShipping { get; set; }

    /// <summary>Import tax / duty (USD).</summary>
    public decimal Tax { get; set; }

    /// <summary>TikTok Discount percent of sale price.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>TikTok Fast Sale percent of sale price.</summary>
    public decimal FastSalePercent { get; set; }

    /// <summary>Fixed ads fee (USD).</summary>
    public decimal AdsFee { get; set; }

    /// <summary>TikTok shipping charged to customer (USD) — credited to seller revenue.</summary>
    public decimal TikTokShipping { get; set; }

    /// <summary>Platform fee percent of discounted sale amount.</summary>
    public decimal PlatformFeePercent { get; set; } = 6;

    /// <summary>Target mode: <c>margin</c> or <c>profit</c>.</summary>
    public string TargetMode { get; set; } = "margin";

    /// <summary>Target profit as percent of total import cost (when TargetMode = margin).</summary>
    public decimal MarginPercent { get; set; } = 50;

    /// <summary>Fixed target profit in USD (when TargetMode = profit).</summary>
    public decimal ProfitAmount { get; set; }

    /// <summary>Whether this is the user's default template (at most one per user).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Owner navigation.</summary>
    public ApplicationUser CreatedBy { get; set; } = null!;
}
