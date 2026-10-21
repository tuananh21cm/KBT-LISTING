/** Mã nhiệm vụ hệ thống (khớp `Shared.Constants.ShopTaskKeys`). */
export type ShopTaskKey = 'sync_extension' | 'url_visit' | 'listing_fill' | 'delist_cleanup';

/** Nhóm hiển thị trên UI. */
export type ShopTaskGroup = 'daily' | 'optimize';

/** Trạng thái nhiệm vụ (khớp `Shared.Constants.ShopTaskStatuses`). */
export type ShopTaskStatus = 'done' | 'pending' | 'attention' | 'blocked' | 'failed';

/** Một nhiệm vụ của shop trong ngày đang xem. */
export interface ShopTaskItem {
  key: ShopTaskKey;
  /** Chỉ có khi `key` là `url_visit`. */
  definitionId?: string | null;
  title: string;
  group: ShopTaskGroup;
  status: ShopTaskStatus;
  detail: string;
  /**
   * Tiến độ / chỉ số phụ thuộc `key`:
   * - `sync_extension`: số bước sync đã xong / 4
   * - `listing_fill`: published / publish limit
   * - `delist_cleanup`: `delistCount` / `scanned` từ summary sau sync analytics
   */
  current?: number | null;
  target?: number | null;
  url?: string | null;
  completedAt?: string | null;
}

/** Bảng nhiệm vụ của một shop. */
export interface ShopTaskShop {
  shopId: string;
  shopName: string;
  profileName?: string | null;
  /** Avatar TikTok (sync profile). */
  avatarUrl?: string | null;
  externalShopId?: string | null;
  /** Trạng thái ops shop: Active / Deactivated / Suspended. */
  shopStatus?: string;
  ownerId: string;
  ownerFullName: string;
  ownerUserName: string;
  publishLimitPublished?: number | null;
  publishLimitTotal?: number | null;
  remainingSlots?: number | null;
  /** Số listing nên hủy từ summary analytics mới nhất; null khi chưa sync. */
  delistCount?: number | null;
  /** Số listing cần xem xét từ summary analytics mới nhất; null khi chưa sync. */
  reviewCount?: number | null;
  completedCount: number;
  totalCount: number;
  attentionCount: number;
  /** Số đơn tab Đã giao (`DELIVERED`). */
  deliveredOrderCount: number;
  /** Số đơn tab Refund (`REFUND`). */
  refundOrderCount: number;
  /** Có Product Discount ONGOING (mirror local; không gồm Flash Deal). */
  hasProductDiscount?: boolean;
  /** BeginTimeUtc của PD ONGOING sắp hết hạn nhất; null khi không có. */
  productDiscountBeginTimeUtc?: string | null;
  /** EndTimeUtc sớm nhất trong các PD ONGOING; null khi không có. */
  productDiscountEndTimeUtc?: string | null;
  /** Có Flash Deal ONGOING (discountType FLASHSALE). */
  hasFlashDeal?: boolean;
  /** BeginTimeUtc của Flash ONGOING sắp hết hạn nhất; null khi không có. */
  flashDealBeginTimeUtc?: string | null;
  /** EndTimeUtc sớm nhất trong các Flash ONGOING; null khi không có. */
  flashDealEndTimeUtc?: string | null;
  /**
   * Thời điểm sync Compass Product analysis mới nhất (UTC) —
   * MAX(SyncedAt) trên SnapshotDate mới nhất của shop; null khi chưa có snapshot.
   */
  analyticsSyncedAt?: string | null;
  /**
   * Tổng giây online trong ngày board (VN) từ ShopOnlineSessions.
   * Phiên mở: board = hôm nay thì tính đến UTC now.
   */
  todayOnlineSeconds?: number;
  /** Tổng GMV trên snapshot Compass mới nhất. */
  analyticsGmv?: number | null;
  /** Currency của analyticsGmv (thường USD). */
  analyticsCurrency?: string | null;
  /** Tổng Impressions các product trên snapshot Compass mới nhất; null khi chưa có. */
  analyticsImpressions?: number | null;
  /** CTR shop-level = clicks ÷ impressions. */
  analyticsCtr?: number | null;
  /** Tổng Clicks trên snapshot Compass mới nhất. */
  analyticsClicks?: number | null;
  /** Tổng Add-to-cart units trên snapshot Compass mới nhất. */
  analyticsAddToCartUnits?: number | null;
  /** Tổng Orders (Compass) trên snapshot mới nhất. */
  analyticsOrders?: number | null;
  healthSummary?: ShopHealthSummary | null;
  /** Health Center AHR score. */
  ahrScore?: number | null;
  /** Shop Performance Score (0–5). */
  shopPerformanceScore?: number | null;
  /** SPS — Product satisfaction (0–5). */
  productSatisfactionScore?: number | null;
  /** SPS — Fulfillment and logistics (0–5). */
  fulfillmentScore?: number | null;
  /** SPS — Customer service (0–5). */
  customerServiceScore?: number | null;
  tasks: ShopTaskItem[];
}

/** Một hình thức phạt từ Health Center. */
export interface ShopHealthAction {
  title: string;
  actionStartTime?: string | null;
  actionEndTime?: string | null;
}

/** Một dòng violation/warning rút gọn từ board API. */
export interface ShopHealthRecordBrief {
  recordId: string;
  recordType: 'violation' | 'warning';
  reason: string;
  detail: string;
  categoryType: string;
  actions: ShopHealthAction[];
  createTime: string;
  handledAt?: string | null;
}

/** Tóm tắt Health Center cho shop trên bảng nhiệm vụ. */
export interface ShopHealthSummary {
  activeViolationCount: number;
  activeWarningCount: number;
  handledViolationCount: number;
  handledWarningCount: number;
  pendingRecent: ShopHealthRecordBrief[];
  handledRecent: ShopHealthRecordBrief[];
}

/** Body đánh dấu health record đã xử lý. */
export interface MarkShopHealthRecordHandledPayload {
  shopId: string;
  recordId: string;
}

/** Tổng hợp toàn bảng. */
export interface ShopTaskSummary {
  shopCount: number;
  completedShopCount: number;
  totalTaskCount: number;
  completedTaskCount: number;
  attentionTaskCount: number;
  /** Tổng publish limit (được phép list) trên shop trong board. */
  publishLimitTotalSum: number;
  /** Tổng listing đã published trên shop trong board. */
  publishLimitPublishedSum: number;
  /** Tổng listing nên hủy từ summary analytics mới nhất từng shop. */
  delistCountSum: number;
  /** Tổng listing cần xem xét từ summary analytics mới nhất từng shop. */
  reviewCountSum: number;
}

/** Kết quả `GET shop-tasks/board`. */
export interface ShopTaskBoard {
  /** yyyy-MM-dd theo lịch VN. */
  date: string;
  summary: ShopTaskSummary;
  shops: ShopTaskShop[];
  warnings: string[];
}

/** Bộ lọc bảng nhiệm vụ. */
export interface ShopTaskBoardParams {
  date?: string;
  ownerId?: string;
  shopId?: string;
  search?: string;
}

/** Định nghĩa nhiệm vụ truy cập URL. */
export interface ShopTaskDefinition {
  id: string;
  name: string;
  url: string;
  urlMatchPattern?: string | null;
  description?: string | null;
  viewDurationSeconds: number;
  sortOrder: number;
  isActive: boolean;
  createdAt: string;
}

/** Body tạo / sửa nhiệm vụ truy cập URL. */
export interface SaveShopTaskDefinitionPayload {
  name: string;
  url: string;
  urlMatchPattern?: string | null;
  description?: string | null;
  viewDurationSeconds: number;
  sortOrder: number;
  isActive: boolean;
}

/** Body sắp xếp lại nhiệm vụ truy cập URL (id đầu = sortOrder 0). */
export interface ReorderShopTaskDefinitionsRequest {
  orderedIds: string[];
}
