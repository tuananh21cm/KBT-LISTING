export * from '@drop/types/listing';

/** —— Live 4Seller listing import (legacy; not used by Draft/Published UI) —— */

export type ListingLocalStatus = 'new' | 'imported' | 'outdated';

export interface FourSellerListingVariation {
  id: number;
  mainImage?: string;
  title?: string;
  sellerSku?: string;
  originalPrice?: number;
  globalPrice?: number;
  availableStock?: number;
}

export interface FourSellerListingRecord {
  id: number;
  productName?: string;
  mainImage?: string;
  shopId?: number;
  shopName?: string;
  site?: string;
  currency?: string;
  categoryId?: string;
  categoryName?: string;
  lowPrice?: number;
  highPrice?: number;
  availableStock?: number;
  variationCount: number;
  erpStatus?: string;
  publishStatus?: string;
  sourceUrl?: string;
  warehouseId?: string;
  createTime?: string;
  updateTime?: string;
  descriptionHtml?: string;
  packageWeight?: number;
  packageLength?: number;
  packageWidth?: number;
  packageHeight?: number;
  weightUnit?: string;
  dimensionUnit?: string;
  variationList: FourSellerListingVariation[];
  detailImageUrls: string[];
  rawDetailJson?: string;
  localStatus?: ListingLocalStatus | string;
  productId?: string;
  productTitle?: string;
  importedAt?: string;
}

export interface FourSellerListingPage {
  records: FourSellerListingRecord[];
  total: number;
  pageSize: number;
  pageCurrent: number;
}

export interface FourSellerListingPageRequest {
  pageCurrent?: number;
  pageSize?: number;
  status?: string;
  shopId?: string;
  searchType?: string;
  searchValue?: string[];
  searchMethod?: string;
  orderBy?: string;
  desc?: string;
}

export interface FourSellerListingImportRequest {
  listingIds: number[];
  records?: FourSellerListingRecord[];
  status?: string;
  mode?: 'create' | 'update';
}

export interface FourSellerListingImportItemResult {
  listingId: number;
  status: 'created' | 'updated' | 'skipped' | 'failed' | string;
  productId?: string;
  externalProductId?: string;
  message?: string;
}

export interface FourSellerListingImportResponse {
  created: number;
  updated: number;
  skipped: number;
  failed: number;
  items: FourSellerListingImportItemResult[];
}

export const LISTING_STATUS_OPTIONS = [
  { label: 'Draft', value: 'draft' },
  { label: 'Online', value: 'online' },
  { label: 'Offline', value: 'offline' },
] as const;

/** Maps system Platform.code → 4Seller listing path segment (`/api/listing/{slug}/page`). */
export const toFourSellerListingSlug = (platformCode: string) =>
  platformCode.trim().toLowerCase();

export const LOCAL_STATUS_LABEL: Record<string, string> = {
  new: 'Mới tạo',
  imported: 'Đã thêm',
  outdated: 'Cần cập nhật',
};
