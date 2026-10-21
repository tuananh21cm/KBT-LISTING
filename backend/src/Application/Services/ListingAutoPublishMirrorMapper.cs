using Application.DTOs.Listing;

namespace Application.Services;

/// <summary>Maps listing detail DTO to mirror update payload for push.</summary>
internal static class ListingAutoPublishMirrorMapper
{
    /// <summary>
    /// Applies AI rewrite output onto in-memory detail before mirror persist.
    /// Search terms and highlights follow ListingEditPage: empty AI arrays keep existing values.
    /// </summary>
    internal static void ApplyRewriteResult(
        ListingDetailDto detail,
        RewriteListingDescriptionResultDto rewriteResult)
    {
        detail.ProductName = rewriteResult.Title ?? detail.ProductName;
        detail.DescriptionHtml = rewriteResult.DescriptionHtml ?? detail.DescriptionHtml;
        detail.DescriptionRewritten = true;

        if (rewriteResult.SearchTerms.Count > 0)
        {
            detail.SearchTerms = rewriteResult.SearchTerms;
        }

        if (rewriteResult.ProductHighlights.Count > 0)
        {
            detail.ProductHighlights = rewriteResult.ProductHighlights;
        }
    }

    public static UpdateListingMirrorDto ToUpdateDto(ListingDetailDto detail)
    {
        return new UpdateListingMirrorDto
        {
            ProductName = detail.ProductName,
            DescriptionHtml = detail.DescriptionHtml,
            SearchTerms = detail.SearchTerms,
            ProductHighlights = detail.ProductHighlights,
            CategoryId = detail.CategoryId,
            CategoryName = detail.CategoryName,
            BrandId = detail.BrandId,
            BrandName = detail.BrandName,
            ProductType = detail.ProductType,
            WarehouseId = detail.WarehouseId,
            SourceUrl = detail.SourceUrl,
            ListingSourceUrl = detail.ListingSourceUrl,
            PackageWeight = detail.PackageWeight,
            PackageLength = detail.PackageLength,
            PackageWidth = detail.PackageWidth,
            PackageHeight = detail.PackageHeight,
            WeightUnit = detail.WeightUnit,
            DimensionUnit = detail.DimensionUnit,
            SizeChartId = detail.SizeChartId,
            ErpVideoId = detail.ErpVideoId,
            SafetyWarning = detail.SafetyWarning,
            SiteId = detail.SiteId,
            FourSellerGroupId = detail.FourSellerGroupId,
            FourSellerGroupName = detail.FourSellerGroupName,
            AppliedRules = detail.AppliedRules,
            AppliedPricing = detail.AppliedPricing,
            DescriptionRewritten = detail.DescriptionRewritten,
            DescriptionImagesInserted = detail.DescriptionImagesInserted,
            RefListingId = detail.RefListingId,
            RefShopId = detail.RefShopId,
            ShopId = detail.ShopId,
            MainImage = detail.MainImage,
            Variations = detail.Variations.Select(v => new UpdateListingVariationDto
            {
                Id = v.Id,
                FourSellerSkuId = v.FourSellerSkuId,
                SalePrice = v.SalePrice,
                InitialPrice = v.InitialPrice,
                OriginalPrice = v.OriginalPrice,
                Quantity = v.Quantity,
                SellerSku = v.SellerSku,
                Title = v.Title,
                MainImage = v.MainImage,
                Barcode = v.Barcode,
                VariantAttributes = v.VariantAttributes,
                SortOrder = v.SortOrder,
                IsDefault = v.IsDefault
            }).ToList(),
            Images = detail.Images.Select(img => new UpdateListingMirrorImageDto
            {
                MediaType = img.MediaType,
                Url = img.Url,
                ListingSkuId = img.ListingSkuId,
                TikTokImageId = img.TikTokImageId,
                SortOrder = img.SortOrder
            }).ToList(),
            Attributes = detail.Attributes.Select(a => new UpdateListingMirrorAttributeDto
            {
                AttrId = a.AttrId,
                AttrName = a.AttrName,
                AttrValue = a.AttrValue,
                ValueId = a.ValueId,
                Values = a.Values?.Select(v => new UpdateListingMirrorAttributeValueDto
                {
                    ValueId = v.ValueId,
                    ValueName = v.ValueName
                }).ToList(),
                ValueType = a.ValueType,
                IsRequired = a.IsRequired
            }).ToList(),
            Certifications = detail.Certifications.Select(c => new UpdateListingMirrorCertificationDto
            {
                CertType = c.CertType,
                ExternalId = c.ExternalId,
                Name = c.Name,
                Phone = c.Phone,
                Email = c.Email,
                Address = c.Address,
                FileUrls = c.FileUrls
            }).ToList()
        };
    }
}
