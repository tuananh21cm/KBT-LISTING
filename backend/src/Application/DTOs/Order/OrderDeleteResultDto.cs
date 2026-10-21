namespace Application.DTOs.Order;

/// <summary>
/// Result of deleting local orders (does not call 4Seller).
/// </summary>
public class OrderDeleteResultDto
{
    /// <summary>Number of orders removed from the local database.</summary>
    public int DeletedCount { get; set; }
}
