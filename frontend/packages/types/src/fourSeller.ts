export type FourSellerConnectionStatus = 'Connected' | 'NotConnected';

export type FourSellerMatchBy = 'platformShopName' | 'sellingPartnerId' | 'fourSellerId';

export interface FourSellerAuthRedirectResponse {
  authorizeUrl: string;
}

export interface FourSellerShopRecord {
  id: number;
  platform: string;
  platformShopName: string;
  shopName: string;
  sellingPartnerId: string;
  statusDesc: string;
  marketplaceId: string;
  createTime?: string;
}

export interface FourSellerShopCheckResponse {
  isConnected: boolean;
  matchedBy?: FourSellerMatchBy;
  matchedShop?: FourSellerShopRecord;
  shopSynced?: boolean;
  fourSellerConnectedAt?: string;
}

export interface FourSellerSyncAccountFailure {
  ownerUserId: string;
  ownerName?: string;
  message: string;
}

/** 4Seller shop that sync could not apply to a unique local granted shop. */
export interface FourSellerUnmatchedShop {
  fourSellerShopId: number;
  shopName?: string | null;
  platformShopName?: string | null;
  sellingPartnerId?: string | null;
  marketplaceId?: string | null;
  cookieOwnerName?: string | null;
  /** not_connected | no_local_shop | ambiguous_external_id | fourseller_id_taken */
  reason: string;
}

export interface FourSellerSyncResponse {
  totalLocalShops: number;
  fourSellerShopCount: number;
  syncedCount: number;
  unmatchedCount: number;
  createdCount: number;
  updatedCount: number;
  skippedCount: number;
  /** Cross-owner FourSellerShopId refresh via TikTok ExternalShopId */
  relinkedCount?: number;
  /** Orders whose local ShopId was (re)linked after shop sync */
  ordersRelinkedCount?: number;
  /** Admin multi-cookie: accounts synced OK */
  syncedAccounts?: number;
  failedAccounts?: FourSellerSyncAccountFailure[];
  unmatchedShops?: FourSellerUnmatchedShop[];
}
