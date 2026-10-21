namespace Application.DTOs.FourSeller;

/// <summary>
/// Progress of a 4Seller crawl/save schedule job.
/// </summary>
public class FourSellerScheduleProgressDto
{
    /// <summary>Total items in the job.</summary>
    public int Total { get; set; }

    /// <summary>Succeeded count.</summary>
    public int Succeed { get; set; }

    /// <summary>Failed count.</summary>
    public int Failed { get; set; }

    /// <summary>Failed item details (opaque from 4Seller).</summary>
    public List<object> FailedList { get; set; } = [];
}
