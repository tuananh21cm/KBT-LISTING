namespace Application.DTOs.FourSeller;

/// <summary>
/// Response after starting save-to-platform schedule.
/// </summary>
public class FourSellerSaveToPlatformResponse
{
    /// <summary>Schedule code used to poll progress.</summary>
    public string ScheduleCode { get; set; } = string.Empty;
}
