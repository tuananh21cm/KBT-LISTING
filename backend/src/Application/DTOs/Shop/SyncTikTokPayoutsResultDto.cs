namespace Application.DTOs.Shop;

/// <summary>Result of a TikTok payout sync batch.</summary>
public class SyncTikTokPayoutsResultDto
{
    /// <summary>Shop id matched.</summary>
    public Guid ShopId { get; set; }

    /// <summary>New ledger rows inserted.</summary>
    public int Inserted { get; set; }

    /// <summary>Existing ledger rows updated.</summary>
    public int Updated { get; set; }

    /// <summary>Orders marked IsSettlementPaid.</summary>
    public int OrdersMarkedPaid { get; set; }

    /// <summary>Telegram notifications sent for new payouts.</summary>
    public int TelegramSent { get; set; }

    /// <summary>Payments skipped (invalid / empty id).</summary>
    public int Skipped { get; set; }
}
