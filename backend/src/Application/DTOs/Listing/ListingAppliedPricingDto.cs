namespace Application.DTOs.Listing;

/// <summary>
/// Snapshot of the pricing formula last applied on a listing (batch sale-price calculator).
/// Local-only — not pushed to 4Seller. Preview discount/profit on the edit UI is reference-only;
/// business sales/profit use listing <c>SalePrice</c> / order costs, not this snapshot.
/// </summary>
public class ListingAppliedPricingDto
{
    /// <summary>Optional pricing template id used when applying.</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>UTC time the formula was applied.</summary>
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

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

    /// <summary>TikTok shipping charged to customer (USD).</summary>
    public decimal TikTokShipping { get; set; }

    /// <summary>Platform fee percent of discounted sale amount.</summary>
    public decimal PlatformFeePercent { get; set; } = 6;

    /// <summary>Target mode: <c>margin</c> or <c>profit</c>.</summary>
    public string TargetMode { get; set; } = "margin";

    /// <summary>Target profit as percent of total import cost (margin mode).</summary>
    public decimal MarginPercent { get; set; }

    /// <summary>Fixed target profit in USD (profit mode).</summary>
    public decimal ProfitAmount { get; set; }
}
