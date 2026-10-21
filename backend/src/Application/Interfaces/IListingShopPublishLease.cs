namespace Application.Interfaces;

/// <summary>Per-shop lease: one auto publish at a time per shop.</summary>
public interface IListingShopPublishLease
{
    /// <summary>Try acquire shop lease.</summary>
    bool TryBegin(Guid shopId, out IDisposable? handle);

    /// <summary>Force release when cancelled.</summary>
    void ForceRelease(Guid shopId);
}
