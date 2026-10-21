using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Ledger entry for a fulfil purchase account / card.
/// </summary>
public class FulfilTransaction : BaseEntity
{
    /// <summary>Auto-created when updating order cost fees.</summary>
    public const string SourceCostUpdate = "CostUpdate";

    /// <summary>Manually entered on transaction history pages.</summary>
    public const string SourceManual = "Manual";

    /// <summary>Card charge / spend (trừ tiền).</summary>
    public const string DirectionDebit = "Debit";

    /// <summary>Card top-up (nạp tiền).</summary>
    public const string DirectionCredit = "Credit";

    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Purchase account (optional — card may pay for non-account services).</summary>
    public Guid? FulfilAccountId { get; set; }

    /// <summary>Payment card (optional for manual ledger entries).</summary>
    public Guid? FulfilCardId { get; set; }

    /// <summary>Linked order when from cost update (null for manual).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// Lần mua NCC mà khoản chi này trả cho. Mỗi lần checkout SHEIN là một lần thẻ bị trừ riêng, nên
    /// một đơn Joint Shipment sinh một dòng sổ cái cho mỗi billno — đúng mức chi tiết để đối chiếu
    /// với sao kê thẻ. <c>null</c> cho dòng nhập tay ở sổ cái (không gắn đơn nào).
    /// </summary>
    public Guid? SupplierPurchaseId { get; set; }

    /// <summary>Absolute transaction amount (&gt;= 0). CostUpdate = SupplierTotal (totalPrice hóa đơn / thẻ).</summary>
    public decimal Amount { get; set; }

    /// <summary>Snapshot coupon theo lần mua (CostUpdate); Manual để null.</summary>
    public decimal? CouponAmount { get; set; }

    /// <summary>Snapshot ví cashback Shein theo lần mua (CostUpdate); Manual để null.</summary>
    public decimal? WalletAmount { get; set; }

    /// <summary>Snapshot giảm membership/club theo lần mua (CostUpdate); Manual để null.</summary>
    public decimal? MembershipAmount { get; set; }

    /// <summary>Snapshot giảm điểm SHEIN Points theo lần mua (CostUpdate); Manual để null.</summary>
    public decimal? PointsAmount { get; set; }

    /// <summary><see cref="DirectionDebit"/> or <see cref="DirectionCredit"/>.</summary>
    public string Direction { get; set; } = DirectionDebit;

    /// <summary>Currency code.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Optional note.</summary>
    public string? Note { get; set; }

    /// <summary><see cref="SourceCostUpdate"/> or <see cref="SourceManual"/>.</summary>
    public string Source { get; set; } = SourceManual;

    /// <summary>Order handler (người xử lý đơn) for sales attribution.</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>Who created the row.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>When the spend occurred.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Account navigation.</summary>
    public FulfilAccount? FulfilAccount { get; set; }

    /// <summary>Card navigation.</summary>
    public FulfilCard? FulfilCard { get; set; }

    /// <summary>Order navigation.</summary>
    public Order? Order { get; set; }

    /// <summary>Supplier purchase navigation.</summary>
    public SupplierPurchase? SupplierPurchase { get; set; }

    /// <summary>Order handler navigation.</summary>
    public ApplicationUser? PaidByUser { get; set; }

    /// <summary>Creator navigation.</summary>
    public ApplicationUser CreatedByUser { get; set; } = null!;
}
