export interface SupplierSellerListParams {
  pageCurrent?: number;
  pageSize?: number;
  search?: string;
  platform?: string;
  ownerUserId?: string;
  period?: string;
  fromDate?: string;
  toDate?: string;
  productPageCurrent?: number;
  productPageSize?: number;
}

export interface SupplierSellerListItem {
  id: string;
  platform: string;
  storeCode: string;
  storeUrl?: string | null;
  storeLogo?: string | null;
  storeTitle?: string | null;
  shopRank?: number | null;
  storeAttention?: number | null;
  shippingDays?: number | null;
  shippingDayPercent?: number | null;
  shipsFromTips?: string | null;
  productCount: number;
  listingCount: number;
  orderCount: number;
  itemQty: number;
  itemRevenue: number;
}

export interface SupplierSellerListPage {
  items: SupplierSellerListItem[];
  total: number;
  pageCurrent: number;
  pageSize: number;
  fromDate: string;
  toDate: string;
}

export interface SupplierSellerListing {
  id: string;
  productName?: string | null;
  pageType: string;
  siteStatus?: string | null;
  mainImage?: string | null;
  shopName?: string | null;
  ownerName?: string | null;
}

export interface SupplierSellerProduct {
  id: string;
  sourceUrl?: string | null;
  sourceUrlKey: string;
  stock?: number | null;
  reviewCount?: number | null;
  averageRating?: number | null;
  isLocalProduct?: boolean | null;
  isSupportQuickShip?: boolean | null;
  listingCount: number;
  listings: SupplierSellerListing[];
}

export interface SupplierSellerDetail extends SupplierSellerListItem {
  fromDate: string;
  toDate: string;
  products: SupplierSellerProduct[];
  productTotal: number;
  productPageCurrent: number;
  productPageSize: number;
}
