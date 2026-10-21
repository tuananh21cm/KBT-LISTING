namespace Application.DTOs.Order;

/// <summary>
/// Result of a multi-seller order sync run.
/// </summary>
public class OrderSyncResultDto
{
    /// <summary>Sellers successfully synced.</summary>
    public int SyncedSellers { get; set; }

    /// <summary>New orders inserted.</summary>
    public int Inserted { get; set; }

    /// <summary>Existing orders updated.</summary>
    public int Updated { get; set; }

    /// <summary>Per-seller failures (cookie expired, API error, …).</summary>
    public List<OrderSyncSellerFailureDto> FailedSellers { get; set; } = [];
}

/// <summary>One seller sync failure.</summary>
public class OrderSyncSellerFailureDto
{
    /// <summary>Seller user id.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Seller display name / username.</summary>
    public string? OwnerName { get; set; }

    /// <summary>Error message.</summary>
    public string Message { get; set; } = string.Empty;
}
