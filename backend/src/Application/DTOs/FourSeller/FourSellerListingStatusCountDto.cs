namespace Application.DTOs.FourSeller;

/// <summary>
/// Status counts from 4Seller <c>POST /api/listing/get-status-count</c>.
/// </summary>
public class FourSellerListingStatusCountDto
{
    /// <summary>Active listings.</summary>
    public int ActiveCount { get; set; }

    /// <summary>Inactive listings.</summary>
    public int InactiveCount { get; set; }

    /// <summary>Removed listings.</summary>
    public int RemovedCount { get; set; }

    /// <summary>Incomplete listings.</summary>
    public int IncompleteCount { get; set; }

    /// <summary>Suspended listings.</summary>
    public int SuspendedCount { get; set; }

    /// <summary>Private listings.</summary>
    public int PrivateCount { get; set; }

    /// <summary>Pending listings.</summary>
    public int PendingCount { get; set; }

    /// <summary>Rejected listings.</summary>
    public int RejectCount { get; set; }

    /// <summary>Unsaleable listings.</summary>
    public int UnsaleableCount { get; set; }
}
