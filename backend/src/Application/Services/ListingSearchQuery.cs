using System.Globalization;
using Domain.Entities;

namespace Application.Services;

/// <summary>
/// Listing list <c>search</c> filter — shared by admin + seller list endpoints.
/// </summary>
public static class ListingSearchQuery
{
    /// <summary>Apply optional text/id search to a listing query.</summary>
    public static IQueryable<Listing> ApplySearch(IQueryable<Listing> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var term = search.Trim();

        if (Guid.TryParse(term, out var localId))
        {
            return query.Where(l => l.Id == localId);
        }

        if (long.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
        {
            return query.Where(l =>
                l.FourSellerListingId == numericId
                || l.FourSellerShopId == numericId
                || (l.TikTokProductId != null && l.TikTokProductId.Contains(term))
                || (l.ProductName != null && l.ProductName.Contains(term))
                || l.Variations.Any(v =>
                    v.FourSellerSkuId == numericId
                    || (v.SellerSku != null && v.SellerSku.Contains(term))
                    || (v.Title != null && v.Title.Contains(term))));
        }

        return query.Where(l =>
            (l.ProductName != null && l.ProductName.Contains(term))
            || (l.TikTokProductId != null && l.TikTokProductId.Contains(term))
            || l.FourSellerListingId.ToString().Contains(term)
            || (l.FourSellerShopId != null && l.FourSellerShopId.ToString().Contains(term))
            || l.Id.ToString().Contains(term)
            || (l.CategoryName != null && l.CategoryName.Contains(term))
            || (l.CategoryId != null && l.CategoryId.Contains(term))
            || (l.SourceUrl != null && l.SourceUrl.Contains(term))
            || (l.Shop != null && l.Shop.Name.Contains(term))
            || (l.Shop != null && l.Shop.ProfileName != null && l.Shop.ProfileName.Contains(term))
            || (l.Shop != null && l.Shop.FourSellerShopId != null && l.Shop.FourSellerShopId.Contains(term))
            || (l.Shop != null && l.Shop.ExternalShopId != null && l.Shop.ExternalShopId.Contains(term))
            || (l.Owner != null && l.Owner.FullName != null && l.Owner.FullName.Contains(term))
            || (l.Owner != null && l.Owner.UserName != null && l.Owner.UserName.Contains(term))
            || (l.SupplierProductInfo != null && l.SupplierProductInfo.SourceUrlKey.Contains(term))
            || (l.SupplierProductInfo != null
                && l.SupplierProductInfo.SourceUrl != null
                && l.SupplierProductInfo.SourceUrl.Contains(term))
            || (l.SupplierProductInfo != null
                && l.SupplierProductInfo.Seller != null
                && l.SupplierProductInfo.Seller.StoreTitle != null
                && l.SupplierProductInfo.Seller.StoreTitle.Contains(term))
            || (l.SupplierProductInfo != null
                && l.SupplierProductInfo.Seller != null
                && l.SupplierProductInfo.Seller.StoreCode.Contains(term))
            || (l.SupplierProductInfo != null
                && l.SupplierProductInfo.Seller != null
                && l.SupplierProductInfo.Seller.SoldByTips != null
                && l.SupplierProductInfo.Seller.SoldByTips.Contains(term))
            || l.Variations.Any(v =>
                (v.SellerSku != null && v.SellerSku.Contains(term))
                || (v.Title != null && v.Title.Contains(term))
                || (v.FourSellerSkuId != null && v.FourSellerSkuId.ToString().Contains(term))));
    }
}
