namespace Application.DTOs.Shop;

/// <summary>
/// Create shop request DTO for admin assignment.
/// </summary>
public class CreateAdminShopDto : CreateShopDto
{
    /// <summary>Seller user identifier to assign as owner.</summary>
    public Guid OwnerId { get; set; }
}
