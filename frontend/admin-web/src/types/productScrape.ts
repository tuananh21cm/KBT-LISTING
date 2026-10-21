export interface FourSellerMoveDraftRecord {
  id: number;
  status: number;
  image?: string | null;
  sourceUrl?: string | null;
  variantCount: number;
  lowPrice?: number | null;
  highPrice?: number | null;
  currency?: string | null;
  creator?: string | null;
  createTime?: string | null;
  productName?: string | null;
  ownedById?: string | null;
  ownedByName?: string | null;
}

export interface FourSellerMoveDraftPage {
  records: FourSellerMoveDraftRecord[];
  total: number;
  pageSize: number;
  pageCurrent: number;
}

export interface FourSellerMoveDraftPageRequest {
  pageCurrent?: number;
  pageSize?: number;
  searchValue?: string | null;
  batchId?: string | null;
  /** Exit crawl pool: abandon unmatched pending for batchId. */
  abandonPending?: boolean;
  /** Default true — only my drafts. Admin can set false for all. */
  mineOnly?: boolean;
  /** Admin-only filter by owner user id. */
  createdById?: string | null;
}

export interface FourSellerCrawlUrlsRequest {
  urls: string[];
}

export interface FourSellerCrawlUrlsResponse {
  batchId: string;
  accepted: boolean;
  count: number;
  fourSellerResponse?: string;
}

export interface FourSellerSaveToPlatformRequest {
  moveDraftId: number[];
  shopIdList: number[];
}

export interface FourSellerSaveToPlatformResponse {
  scheduleCode: string;
}

export interface FourSellerScheduleProgress {
  total: number;
  succeed: number;
  failed: number;
  failedList: unknown[];
}
