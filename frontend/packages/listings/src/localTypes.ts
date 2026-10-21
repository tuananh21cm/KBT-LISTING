/** Slim product-listing payload shapes used by listingFormUtils (not the full product domain). */

export interface ListingSkuItem {
  id?: string;
  sortOrder: number;
  fourSellerSkuId?: string;
  tikTokSkuId?: string;
  sellerSku?: string;
  salePrice?: number;
  originalPrice?: number;
  currency?: string;
  quantity?: number;
  barcode?: string;
  variantAttributes?: string;
  packageWeight?: number;
  packageLength?: number;
  packageWidth?: number;
  packageHeight?: number;
  isDefault: boolean;
}

export interface ListingImageItem {
  id?: string;
  listingSkuId?: string;
  mediaType: string;
  url: string;
  tikTokImageId?: string;
  sortOrder: number;
}

export interface ListingAttributeItem {
  id?: string;
  attrId: string;
  attrName?: string;
  attrValue?: string;
  valueType?: string;
  isRequired: boolean;
}

export interface ListingCertificationItem {
  id?: string;
  certType: string;
  externalId?: string;
  name?: string;
  phone?: string;
  email?: string;
  address?: string;
  fileUrls?: string;
}

export interface CreateAdminListingRequest {
  shopId: string;
  tikTokProductId?: string;
  tikTokSkuId?: string;
  fourSellerListingId?: string;
  listingStatus: string;
  publishStatus?: string;
  syncStatus?: string;
  productName?: string;
  categoryId?: string;
  categoryName?: string;
  brandId?: string;
  brandName?: string;
  productType?: string;
  descriptionHtml?: string;
  warehouseId?: string;
  packageWeight?: number;
  packageLength?: number;
  packageWidth?: number;
  packageHeight?: number;
  weightUnit?: string;
  dimensionUnit?: string;
  sizeChartId?: string;
  safetyWarning?: string;
  listingSourceUrl?: string;
  fourSellerGroupId?: string;
  fourSellerGroupName?: string;
  appliedRules?: string;
  refListingId?: string;
  refShopId?: string;
  siteId?: string;
  skus: ListingSkuItem[];
  images: ListingImageItem[];
  attributes: ListingAttributeItem[];
  certifications: ListingCertificationItem[];
}

export interface UpdateAdminListingRequest {
  tikTokProductId?: string;
  tikTokSkuId?: string;
  fourSellerListingId?: string;
  listingStatus: string;
  publishStatus?: string;
  syncStatus?: string;
  productName?: string;
  categoryId?: string;
  categoryName?: string;
  brandId?: string;
  brandName?: string;
  productType?: string;
  descriptionHtml?: string;
  warehouseId?: string;
  packageWeight?: number;
  packageLength?: number;
  packageWidth?: number;
  packageHeight?: number;
  weightUnit?: string;
  dimensionUnit?: string;
  sizeChartId?: string;
  safetyWarning?: string;
  listingSourceUrl?: string;
  fourSellerGroupId?: string;
  fourSellerGroupName?: string;
  appliedRules?: string;
  refListingId?: string;
  refShopId?: string;
  siteId?: string;
  metadata?: string;
  skus: ListingSkuItem[];
  images: ListingImageItem[];
  attributes: ListingAttributeItem[];
  certifications: ListingCertificationItem[];
}

export interface ProductSourceOption {
  id: string;
  code: string;
  name: string;
  logoUrl?: string;
  domains?: string;
}

export interface UserFile {
  id: string;
  originalFileName: string;
  storagePath: string;
  publicUrl?: string;
  contentType: string;
  fileSizeBytes: number;
  fileHash: string;
  metadata?: string;
  linkedEntityType?: string;
  linkedEntityId?: string;
  isDuplicate: boolean;
  createdAt: string;
}

export const SHOP_REGIONS = [
  { label: 'US - United States', value: 'US' },
  { label: 'UK - United Kingdom', value: 'UK' },
  { label: 'ID - Indonesia', value: 'ID' },
  { label: 'MY - Malaysia', value: 'MY' },
  { label: 'PH - Philippines', value: 'PH' },
  { label: 'SG - Singapore', value: 'SG' },
  { label: 'TH - Thailand', value: 'TH' },
  { label: 'VN - Vietnam', value: 'VN' },
] as const;
