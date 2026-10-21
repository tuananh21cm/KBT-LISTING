export interface ListingVariationRow {
  /** Local variation id (mirror listing) — must be preserved across form rebuild/save. */
  id?: string;
  /** 4Seller SKU id when known. */
  fourSellerSkuId?: number;
  sortOrder: number;
  mainSpec: string;
  specValue: string;
  /** Multi-axis attributes (Color / Size / Number of Items). Prefer this over parsing specValue. */
  attrs?: Record<string, string>;
  sellerSku: string;
  costPrice?: number;
  salePrice: number;
  originalPrice?: number;
  quantity: number;
  barcode?: string;
  isDefault: boolean;
}

export interface ListingImageRow {
  mediaType: string;
  url: string;
  sortOrder: number;
  listingSkuId?: string;
  tikTokImageId?: string;
  /** Color axis for Variant Image gallery (mapped to listingSkuId on save). */
  colorValue?: string;
}

export interface ListingAttributeRow {
  attrId: string;
  attrName?: string;
  attrValue?: string;
  /** TikTok enum value id when known. */
  valueId?: string;
  /** Multi-select values (isMultipleSelected=1). */
  values?: { valueId?: string; valueName?: string }[];
  valueType?: string;
  isRequired: boolean;
}

export interface ListingCertificationRow {
  certType: string;
  externalId?: string;
  name?: string;
  phone?: string;
  email?: string;
  address?: string;
  fileUrls?: string;
}

export interface ProductEditFormValues {
  sourceId: string;
  sourceName?: string;
  sourceLogoUrl?: string;
  externalProductId: string;
  externalSku?: string;
  title: string;
  description?: string;
  sourceUrl?: string;
  status: string;
  productName?: string;
  categoryId?: string;
  categoryName?: string;
  brandId?: string;
  brandName?: string;
  productType?: string;
  numberOfItems?: number;
  mainSpecification?: string;
  descriptionHtml?: string;
  /** 4Seller searchTerms — SEO keywords. */
  searchTerms?: string[];
  /** 4Seller productHighlights — short selling points. */
  productHighlights?: string[];
  warehouseId?: string;
  packageWeight?: number;
  packageLength?: number;
  packageWidth?: number;
  packageHeight?: number;
  weightUnit?: string;
  dimensionUnit?: string;
  sizeChartId?: string;
  /** 4Seller product video material id (erpVideoId / localMaterialId). */
  erpVideoId?: string;
  safetyWarning?: string;
  listingSourceUrl?: string;
  fourSellerGroupId?: string;
  fourSellerGroupName?: string;
  appliedRules?: string;
  refListingId?: string;
  refShopId?: string;
  /** Local shop Guid — shop Publish (4Seller), giống combobox trên Product Info. */
  shopId?: string;
  siteId?: string;
  variations: ListingVariationRow[];
  images: ListingImageRow[];
  attributes: ListingAttributeRow[];
  certifications: ListingCertificationRow[];
}
