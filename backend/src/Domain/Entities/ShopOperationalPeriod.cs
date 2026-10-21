using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Inclusive Vietnam-calendar window when a shop was operational under a given owner.
/// <see cref="EndedOn"/> null means the period is still open.
/// </summary>
public class ShopOperationalPeriod : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Shop this period belongs to.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Owner at the time the shop was operational in this window.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Inclusive VN calendar start day.</summary>
    public DateOnly StartedOn { get; set; }

    /// <summary>Inclusive VN calendar end day; null while still open.</summary>
    public DateOnly? EndedOn { get; set; }

    /// <summary>Why the period opened (Active, Backfill, OrderHistory, …).</summary>
    public string OpenSource { get; set; } = string.Empty;

    /// <summary>Why the period closed (Deactivated, Suspended, OwnerChanged, Deleted, Backfill).</summary>
    public string? CloseSource { get; set; }

    /// <summary>Navigation to shop.</summary>
    public Shop Shop { get; set; } = null!;
}
