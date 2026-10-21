namespace Application.DTOs.Shop;

/// <summary>
/// Update shop request DTO for admin management.
/// </summary>
public class UpdateAdminShopDto : UpdateShopDto
{
    /// <summary>Optional new seller owner identifier.</summary>
    public Guid? OwnerId { get; set; }
}
