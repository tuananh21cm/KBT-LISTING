namespace Application.DTOs.Shop;

/// <summary>
/// Query for shop finance overview (On hold snapshot + ledger tiền đã về).
/// </summary>
public class ShopFinanceQuery
{
    /// <summary>Admin only — filter shops by owner. Seller always scoped to JWT.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// Inclusive UTC start for ledger tiền đã về. Null = all-time.
    /// Does not affect On hold snapshot.
    /// </summary>
    public DateTime? From { get; set; }

    /// <summary>
    /// Inclusive UTC end for ledger tiền đã về. Null = all-time.
    /// Does not affect On hold snapshot.
    /// </summary>
    public DateTime? To { get; set; }

    /// <summary>
    /// Shop ops status filter. Default <c>Active</c>. Pass <c>all</c> for every status.
    /// </summary>
    public string? Status { get; set; }
}

/// <summary>Shop finance overview: KPI summary + per-shop rows.</summary>
public class ShopFinanceDto
{
    /// <summary>Aggregated KPIs for the filtered shop set.</summary>
    public ShopFinanceSummaryDto Summary { get; set; } = new();

    /// <summary>One row per shop (SortOrder then name).</summary>
    public List<ShopFinanceItemDto> Items { get; set; } = [];
}

/// <summary>KPI totals. On hold is a TTS snapshot; moneyReceived follows From/To.</summary>
public class ShopFinanceSummaryDto
{
    /// <summary>Number of shops in the filtered set.</summary>
    public int ShopCount { get; set; }

    /// <summary>Shops with no receiving-bank card linked.</summary>
    public int ShopsWithoutBankCount { get; set; }

    /// <summary>Sum of shop On hold totals (TTS snapshot).</summary>
    public decimal OnHoldAmount { get; set; }

    /// <summary>Sum of On hold — waiting for delivery.</summary>
    public decimal OnHoldInTransitAmount { get; set; }

    /// <summary>Sum of On hold — delivered, awaiting settlement.</summary>
    public decimal OnHoldAwaitingSettlementAmount { get; set; }

    /// <summary>Sum of On hold — reserve withheld.</summary>
    public decimal OnHoldReserveAmount { get; set; }

    /// <summary>Net ledger income (Confirmed Credit − Debit) in the date window.</summary>
    public decimal MoneyReceived { get; set; }

    /// <summary>Net Pending ledger amount (current snapshot, not date-filtered).</summary>
    public decimal PendingConfirmationAmount { get; set; }

    /// <summary>
    /// Sum of shop acquisition costs (all-time, not date-filtered). One row per shop.
    /// </summary>
    public decimal AcquisitionCostTotal { get; set; }
}

/// <summary>Per-shop finance row.</summary>
public class ShopFinanceItemDto
{
    /// <summary>Shop identifier.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Browser profile name.</summary>
    public string? ProfileName { get; set; }

    /// <summary>Shop avatar URL.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Ops status (Active / Inactive / Suspended).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Payment due / DELI (days).</summary>
    public int? Deli { get; set; }

    /// <summary>TikTok Finance On hold total.</summary>
    public decimal? OnHoldAmount { get; set; }

    /// <summary>On hold — waiting for package delivery.</summary>
    public decimal? OnHoldInTransitAmount { get; set; }

    /// <summary>On hold — delivered, awaiting settlement.</summary>
    public decimal? OnHoldAwaitingSettlementAmount { get; set; }

    /// <summary>On hold — reserve withheld.</summary>
    public decimal? OnHoldReserveAmount { get; set; }

    /// <summary>Net Confirmed ledger (Credit − Debit) in the date window.</summary>
    public decimal MoneyReceived { get; set; }

    /// <summary>Net Pending ledger for this shop (current snapshot).</summary>
    public decimal PendingConfirmationAmount { get; set; }

    /// <summary>Linked receiving-bank card (null = chưa gắn thẻ).</summary>
    public Guid? ReceivingBankAccountId { get; set; }

    /// <summary>Last TikTok profile/finance snapshot sync.</summary>
    public DateTime? TikTokProfileSyncedAt { get; set; }

    /// <summary>Owner user id (admin list; seller = self).</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Owner display name.</summary>
    public string? OwnerFullName { get; set; }

    /// <summary>Owner username.</summary>
    public string? OwnerUserName { get; set; }

    /// <summary>Shop purchase cost from the unique company-ledger acquisition row. Null when unset.</summary>
    public decimal? AcquisitionCost { get; set; }

    /// <summary>Business date of the acquisition row.</summary>
    public DateTime? AcquisitionOccurredAt { get; set; }
}
