using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Listing;

/// <summary>
/// Request to map free-form description specifics onto the category's 4Seller attribute schema.
/// Form-only suggestion — does not persist.
/// </summary>
public class MapListingSpecificsRequest
{
    /// <summary>TikTok category id (leaf).</summary>
    [Required]
    [MaxLength(64)]
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>Optional category display name (for logging / AI context).</summary>
    [MaxLength(512)]
    public string? CategoryName { get; set; }

    /// <summary>
    /// Local shop Guid used to resolve 4Seller <c>shopId</c> for the attribute-schema call.
    /// Falls back to the listing's current shop when omitted.
    /// </summary>
    public Guid? ShopId { get; set; }

    /// <summary>Product title (helps AI disambiguation).</summary>
    [MaxLength(255)]
    public string? Title { get; set; }

    /// <summary>Current description HTML or plain text containing <c>Key: Value</c> pairs.</summary>
    // No description field: the service reads the supplier snapshot from the database. Taking
    // it from the editor meant Auto Specifics had to run before Rewrite, because a rewritten
    // description no longer carries the Key: Value rows it parses.

    /// <summary>
    /// Existing Specifics rows already on the form. Matched suggestions will not overwrite these
    /// unless the existing value is empty.
    /// </summary>
    public List<UpdateListingMirrorAttributeDto>? ExistingAttributes { get; set; }
}
