import type {
  AuthState,
  OnboardConfigResponse,
  PassportAccountInfoResponse,
  PayOnboardInfoResponse,
  ProductPreloadResponse,
  SellerUsSessionStatus,
  SettlementAccountResponse,
  ShopDailyTaskPlan,
  StatementStatInfoResponse,
  TikTokCaptureState,
  TikTokShopInfo,
  UnifiedSyncState,
} from './types';
import type { TaskDwellMap } from './taskDwell';
import type { SellerCommonGetResponse } from './types';
import type { SyncHealthRecordItem } from './healthRecords';
import type { ShopScoreSnapshot } from './shopScores';

/** Message types trao đổi giữa popup/content và background. */
export type ExtMessage =
  | { type: 'GET_STATE' }
  | { type: 'LOGIN'; username: string; password: string }
  | { type: 'LOGOUT' }
  | { type: 'SET_THEME'; theme: 'light' | 'dark' }
  | { type: 'REFRESH_SHOP' }
  /** Unified sync: profile + payout + keywords + analytics + tải nhiệm vụ. */
  | { type: 'SYNC_ALL' }
  /** Sync nhanh: chỉ Finance + Health Center + Shop Score. */
  | { type: 'SYNC_FAST' }
  | { type: 'OPEN_SELLER_US' }
  /** Panel → background: tải lại danh sách nhiệm vụ truy cập URL hôm nay. */
  | { type: 'REFRESH_TASKS' }
  /** Panel → background: điều hướng tab seller-us hiện tại tới URL nhiệm vụ. */
  | { type: 'OPEN_TASK_URL'; url: string; taskId?: string }
  /** Panel → background: bật/tắt Auto chạy lần lượt nhiệm vụ hôm nay + Sync All khi xong. */
  | { type: 'SET_AUTO_DAILY_TASKS'; enabled: boolean }
  /** Content → background: trang seller-us đã sẵn sàng (load thật hoặc SPA đổi URL). */
  | { type: 'SELLER_PAGE_READY'; url: string }
  /** Content (MAIN hook) → background: số fetch/XHR đang chạy trên trang. */
  | { type: 'SELLER_PAGE_NETWORK'; inflight: number }
  /** Background → content: kiểm tra listener còn sống. */
  | { type: 'PING_CONTENT' }
  /**
   * Background → content: hook MAIN world (`sellerProductBridge.js`) còn sống không.
   * Luôn probe lại, không đọc cache — background dùng kết quả để quyết định inject.
   */
  | { type: 'PING_PRODUCT_BRIDGE' }
  /** Background → content: hỏi MAIN hook số XHR đang bay. */
  | { type: 'REQUEST_NETWORK_SYNC' }
  /** Background → content: seller APIs (common + passport + onboard + preload + pay + settlement + products/list). */
  | { type: 'FETCH_SELLER_COMMON_GET' }
  /**
   * Background → content: chỉ gọi lại `product_creation/preload` (publish_limit),
   * dùng khi bước Info thiếu limit — nhẹ hơn chạy lại cả chùm seller/common/get.
   */
  | { type: 'FETCH_PRODUCT_PRELOAD'; sellerId: string }
  /** Background → content: scrape payout/payment list (+ statement orders when possible). */
  | {
      type: 'FETCH_PAYOUT_PAYMENTS';
      sellerId: string;
      /** ISO date floor yyyy-MM-dd (UTC midnight). */
      maxDate?: string | null;
      /** Stop when payment success ≤ this (ISO) or paymentId matches. */
      cursorOccurredAt?: string | null;
      cursorPaymentId?: string | null;
    }
  /** Background → content: scrape Product opportunity leads (trending keywords). */
  | {
      type: 'FETCH_OPPORTUNITY_LEADS';
      sellerId: string;
      /** First category path (compat). Prefer categoryInfoGroups. */
      categoryInfos: { cateId: number; level: number }[];
      /** One ancestor path per shop catalog category — scrape separately then merge. */
      categoryInfoGroups?: { cateId: number; level: number }[][];
    }
  /** Background → content: scrape Compass Product analysis (GMV / traffic per product). */
  | {
      type: 'FETCH_LISTING_ANALYTICS';
      sellerId: string;
      /** Inclusive yyyy-MM-dd in shop timezone; content resolves last7days when omitted. */
      start?: string | null;
      end?: string | null;
    }
  /** Background → content: scrape Growth Center violation + warning records. */
  | { type: 'FETCH_HEALTH_RECORDS'; sellerId: string }
  /** Background → content: đọc AHR score + Shop Performance Score. */
  | { type: 'FETCH_SHOP_SCORES'; sellerId: string }
  /**
   * Content → background: `publish_limit` hớt được từ response product_creation/preload của
   * chính trang (bridge MAIN world) — không tốn request nào, số sạch nhất.
   */
  | {
      type: 'SELLER_PRELOAD_CAPTURED';
      sellerId?: string;
      raw?: ProductPreloadResponse | null;
    }
  /** Content → background: báo kết quả fetch (kể cả auto khi load trang). */
  | {
      type: 'SELLER_COMMON_GET_RESULT';
      ok: boolean;
      message?: string;
      status?: number;
      url?: string;
      at?: number;
      raw?: SellerCommonGetResponse | null;
      accountOk?: boolean;
      accountRaw?: PassportAccountInfoResponse | null;
      accountMessage?: string;
      onboardOk?: boolean;
      onboardRaw?: OnboardConfigResponse | null;
      onboardMessage?: string;
      preloadOk?: boolean;
      preloadRaw?: ProductPreloadResponse | null;
      preloadMessage?: string;
      preloadSource?: string;
      payOnboardOk?: boolean;
      payOnboardRaw?: PayOnboardInfoResponse | null;
      payOnboardMessage?: string;
      settlementOk?: boolean;
      settlementRaw?: SettlementAccountResponse | null;
      settlementMessage?: string;
      statementStatOk?: boolean;
      statementStatRaw?: StatementStatInfoResponse | null;
      statementStatMessage?: string;
      /** Chỉ true khi products/list chứa key khóa identity video (365 ngày). */
      identityVideoVerificationLock?: boolean;
      harvestUsed?: number;
      harvestTotal?: number;
    };

export interface StateResponse {
  authenticated: boolean;
  user: AuthState['user'];
  theme: 'light' | 'dark';
  shopInfo: TikTokShopInfo | null;
  capture: TikTokCaptureState | null;
  sellerUsTabOpen: boolean;
  sellerUsStatus: SellerUsSessionStatus;
  /** Unified Sync progress (4 bước sync + nhiệm vụ hệ thống). */
  unifiedSync: UnifiedSyncState | null;
  /** Nhiệm vụ truy cập URL của hôm nay (cache từ lượt tải gần nhất). */
  dailyTasks: ShopDailyTaskPlan | null;
  /** Nhiệm vụ đang chờ tab seller-us load — hiển thị spinner trên dòng tương ứng. */
  taskLoadingTaskId: string | null;
  /** Đếm ngược thời gian xem trang theo từng nhiệm vụ (sau khi trang load xong). */
  taskDwells: TaskDwellMap;
  /** Auto đang bật: background tự mở lần lượt nhiệm vụ rồi Sync All. */
  autoDailyTasks: boolean;
}

export interface LoginResponse extends StateResponse {
  ok: boolean;
  message?: string;
}

export interface ThemeResponse {
  ok: boolean;
  theme: 'light' | 'dark';
  message?: string;
}

export interface ActionResponse {
  ok: boolean;
  message?: string;
}

export interface FetchSellerCommonGetResponse {
  ok: boolean;
  message?: string;
  status?: number;
  url?: string;
  at?: number;
  raw?: SellerCommonGetResponse | null;
  accountOk?: boolean;
  accountRaw?: PassportAccountInfoResponse | null;
  accountMessage?: string;
  onboardOk?: boolean;
  onboardRaw?: OnboardConfigResponse | null;
  onboardMessage?: string;
  preloadOk?: boolean;
  preloadRaw?: ProductPreloadResponse | null;
  preloadMessage?: string;
  /** Chặng lấy được preload: `bridge` (page tự ký) hoặc `isolated <biến thể>`. */
  preloadSource?: string;
  payOnboardOk?: boolean;
  payOnboardRaw?: PayOnboardInfoResponse | null;
  payOnboardMessage?: string;
  settlementOk?: boolean;
  settlementRaw?: SettlementAccountResponse | null;
  settlementMessage?: string;
  statementStatOk?: boolean;
  statementStatRaw?: StatementStatInfoResponse | null;
  statementStatMessage?: string;
  identityVideoVerificationLock?: boolean;
  /** Số slot dùng payload trang tự gọi (hớt được) trên tổng số slot của bước Info. */
  harvestUsed?: number;
  harvestTotal?: number;
}

export interface FetchProductPreloadResponse {
  ok: boolean;
  status?: number;
  message?: string;
  sellerId?: string;
  raw?: ProductPreloadResponse | null;
  source?: string;
}

export interface SyncTikTokPayoutOrderDto {
  tradeOrderId: string;
  settlementAmount?: number;
  earningAmount?: number;
  fees?: number;
  settlementTime?: string;
}

export interface SyncTikTokPayoutItemDto {
  paymentId: string;
  amount: number;
  currency?: string;
  successTime?: string;
  bankCardMask?: string;
  note?: string;
  orders?: SyncTikTokPayoutOrderDto[];
}

export interface FetchPayoutPaymentsResponse {
  ok: boolean;
  message?: string;
  sellerId?: string;
  payments?: SyncTikTokPayoutItemDto[];
  /** How many payments got ≥1 order line from statement/order APIs. */
  ordersLinked?: number;
}

export interface SyncProductOpportunityCategoryDto {
  cateId: string;
  cateName: string;
  level?: number;
}

export interface SyncProductOpportunityItemDto {
  leadId: string;
  leadName: string;
  picUrl?: string | null;
  picUrls?: string[];
  searchVolume: number;
  onlineProducts?: number | null;
  onlineProductsRaw?: string | null;
  searchVolumeRingRatio?: string | null;
  onlineProductsRingRatio?: string | null;
  orderCount?: number | null;
  orderRaw?: string | null;
  orderRingRatio?: string | null;
  gmv?: number | null;
  gmvRaw?: string | null;
  gmvRingRatio?: string | null;
  categories?: SyncProductOpportunityCategoryDto[];
  level1CateId?: string | null;
  level1CateName?: string | null;
  level2CateId?: string | null;
  level2CateName?: string | null;
  level3CateId?: string | null;
  level3CateName?: string | null;
  tagCodes: string[];
  leadTagTexts?: string[];
  leadSourceTag?: string | null;
  leadClusterSourceTag?: string | null;
  opportunityType?: number | null;
}

export interface FetchOpportunityLeadsResponse {
  ok: boolean;
  message?: string;
  sellerId?: string;
  items?: SyncProductOpportunityItemDto[];
  totalProductCount?: number;
}

/** One Compass product row. Ratio là phân số (0.12 = +12%). */
export interface SyncListingAnalyticsItemDto {
  productId: string;
  productName?: string;
  mainImage?: string;
  currency?: string;
  gmv?: number;
  orders?: number;
  impressions?: number;
  clicks?: number;
  ctr?: number;
  addToCartUnits?: number;
  addToCartBuyers?: number;
  gmvRatio?: number;
  ordersRatio?: number;
  impressionsRatio?: number;
  clicksRatio?: number;
  ctrRatio?: number;
  addToCartUnitsRatio?: number;
  addToCartBuyersRatio?: number;
  /** Raw `stats_v3.total` — backend lưu vào jsonb để không mất metric chưa map. */
  rawStats?: Record<string, unknown>;
}

export interface FetchListingAnalyticsResponse {
  ok: boolean;
  message?: string;
  sellerId?: string;
  preset?: string;
  rangeStart?: string;
  rangeEnd?: string;
  items?: SyncListingAnalyticsItemDto[];
  /** `total` TikTok báo cho toàn bộ bộ lọc (dùng để biết đã scrape hết chưa). */
  total?: number;
  /** Tên các key trong `stats_v3.total` của dòng đầu — soi nhanh khi TikTok đổi schema. */
  statsKeys?: string[];
  /** Shop có nhiều product hơn trần ingest: chỉ giữ top GMV, phần đuôi bị bỏ. */
  truncated?: boolean;
}

export interface FetchHealthRecordsResponse {
  ok: boolean;
  message?: string;
  sellerId?: string;
  items?: SyncHealthRecordItem[];
}

export interface FetchShopScoresResponse {
  ok: boolean;
  message?: string;
  sellerId?: string;
  scores?: ShopScoreSnapshot;
  /** Request lẻ hỏng nhưng vẫn còn điểm khác — báo để người dùng biết thiếu gì. */
  warnings?: string[];
  /** Số slot (AHR / SES) dùng payload trang tự gọi thay vì tự fetch. */
  harvestUsed?: number;
  harvestTotal?: number;
}
