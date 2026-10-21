namespace Application.DTOs.Shop;

/// <summary>Lightweight auto publish scheduler badges for shop list rows.</summary>
public class ShopAutoPublishSchedulerStatusBatchDto
{
    /// <summary>Per-shop scheduler status (only auto-publish-enabled ids are returned).</summary>
    public List<ShopAutoPublishSchedulerStatusItemDto> Items { get; set; } = [];
}

/// <summary>Scheduler fields needed to refresh list badges without a full list reload.</summary>
public class ShopAutoPublishSchedulerStatusItemDto
{
    /// <summary>Local shop id.</summary>
    public Guid Id { get; set; }

    /// <summary>processing | waiting — null when no badge.</summary>
    public string? AutoPublishSchedulerStatus { get; set; }

    /// <summary>UTC — countdown target for next auto publish pick.</summary>
    public DateTime? AutoPublishNextPickAt { get; set; }

    /// <summary>Eligible draft count in FIFO (Queue +N display).</summary>
    public int AutoPublishEligibleDraftCount { get; set; }
}
