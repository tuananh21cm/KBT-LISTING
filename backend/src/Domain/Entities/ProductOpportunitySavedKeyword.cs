using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Saved trending keyword for one shop — survives snapshot replace so it can later
/// feed AI search-URL generation. Unique per shop + normalized keyword.
/// </summary>
public class ProductOpportunitySavedKeyword : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Shop owner (denormalized from <see cref="ShopId"/> for seller scope).</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Shop this bookmark belongs to. Cascade-deleted with the shop.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Keyword as displayed (<c>lead_name</c>).</summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>Trimmed lower-case keyword for uniqueness.</summary>
    public string KeywordNormalized { get; set; } = string.Empty;

    /// <summary>TikTok <c>lead_id</c> at save time.</summary>
    public string? LeadId { get; set; }

    /// <summary>Thumbnail at save time.</summary>
    public string? PicUrl { get; set; }

    /// <summary>User who clicked save (admin or seller).</summary>
    public Guid? SavedByUserId { get; set; }

    /// <summary>Owner navigation.</summary>
    public ApplicationUser Owner { get; set; } = null!;

    /// <summary>Shop this bookmark belongs to.</summary>
    public Shop Shop { get; set; } = null!;

    /// <summary>Actor navigation.</summary>
    public ApplicationUser? SavedBy { get; set; }
}
