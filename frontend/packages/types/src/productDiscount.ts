/** Product discount activity list query (local DB). */
export type ProductDiscountListParams = {
  page?: number;
  pageSize?: number;
  shopId?: string;
  ownerUserId?: string;
  promotionStatus?: string;
  discountType?: string;
  search?: string;
};

export type ProductDiscountListItem = {
  id: string;
  ownerUserId: string;
  ownerName?: string | null;
  shopId?: string | null;
  shopName: string;
  fourSellerActivityId: number;
  platformActivityId: string;
  activityName: string;
  discountType: string;
  promotionStatus: string;
  productLevel: string;
  beginTimeUtc: string;
  endTimeUtc: string;
  productCount: number;
  productFailCount: number;
  errorMessage?: string | null;
  syncedAtUtc: string;
};

export type ProductDiscountListPage = {
  items: ProductDiscountListItem[];
  total: number;
  page: number;
  pageSize: number;
};

export type ProductDiscountSyncRequest = {
  ownerUserId?: string;
};

export type ProductDiscountSyncJobState = 'running' | 'completed' | 'failed';

export type ProductDiscountSyncSellerProgress = {
  ownerUserId: string;
  ownerName?: string | null;
  status: 'pending' | 'running' | 'succeeded' | 'failed' | string;
  inserted: number;
  updated: number;
  skipped: number;
  elapsedMs: number;
  message?: string | null;
};

export type ProductDiscountSyncJobStatus = {
  jobId: string;
  state: ProductDiscountSyncJobState | string;
  startedAtUtc: string;
  finishedAtUtc?: string | null;
  totalSellers: number;
  completedSellers: number;
  syncedSellers: number;
  inserted: number;
  updated: number;
  skipped: number;
  message?: string | null;
  error?: string | null;
  sellers: ProductDiscountSyncSellerProgress[];
};
