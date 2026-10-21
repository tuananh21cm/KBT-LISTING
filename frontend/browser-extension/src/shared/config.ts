/**
 * Base URL của API kinhbac (kèm /api/v1).
 * Dev/local: `.env.development` → `http://localhost:5000/api/v1` (`npm run build:local` / `npm run dev`).
 * Production: `.env.production` hoặc fallback.
 */
export const API_BASE_URL: string =
  (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/+$/, '') ??
  'https://4seller-api.kbt.global/api/v1';

/** Khóa lưu trong chrome.storage.local. */
export const STORAGE_KEYS = {
  auth: 'kbt_auth',
  fsStatus: 'kbt_fs_status',
  productSources: 'kbt_product_sources',
  platforms: 'kbt_platforms',
  /** Shop 4Seller đang chọn để Generate draft (mảng id, ≥1). */
  selectedShopIds: 'kbt_selected_shop_ids',
  /** Cache danh sách shop scraper (TTL trong background). Bump key khi shape đổi (catalog/localId). */
  shopsCache: 'kbt_shops_cache_v5',
  /** Danh sách key `flow:productId` → FourSellerShopId[] đã thêm (check nút Thêm sản phẩm). */
  addedProductIds: 'kbt_added_product_ids',
  /** productKey → epoch ms lần mark local (grace khi reconcile server chưa kịp draft). */
  addedProductIdsMarkedAt: 'kbt_added_product_ids_marked_at',
  /**
   * Tab đang thêm sản phẩm (in-flight): `{ [tabId]: { url, productKey?, startedAt } }`.
   * Side panel + content script đọc để giữ đúng status khi đổi tab.
   */
  addingByTab: 'kbt_adding_by_tab',
  /**
   * Tiến độ đợt thêm đa-tab: `{ total, success, failed, updatedAt }`.
   * Side panel hiện «Đang gửi (2/5)» / «Đã thêm (2/5)».
   */
  addBatchProgress: 'kbt_add_batch_progress',
  /**
   * Snapshot UI nút Thêm từ content script theo tabId — side panel đồng bộ ready/loading/đã thêm.
   * `{ [tabId]: { href, pageReady, sessionReady, shopsSelected, alreadyAdded, addedStatus, isAdding, isProductPage, at } }`
   */
  contentTabStatus: 'kbt_content_tab_status',
  /** Timestamp lần thêm sản phẩm thành công gần nhất — side panel debounce reload draft. */
  draftsDirtyAt: 'kbt_drafts_dirty_at',
  /** Theme tài khoản (`light` | `dark`) — giữ sau logout giống seller-web. */
  theme: 'kbt_theme',
  /**
   * Kết quả «Hôm nay bán gì?» gần nhất theo shop: `{ [shopLocalId]: { savedAt, data } }`.
   * Mỗi lần Phân tích ghi đè shop đó; chọn lại shop thì load bản cũ (khỏi tốn token).
   */
  whatToSellCache: 'kbt_wts_cache',
  /** State tìm SP Shein tự động (side panel hydrate khi SW sleep). */
  productSearch: 'kbt_product_search',
  /** goodsId link Search đang active (chỉ 1; click SP khác thì thay). */
  productSearchClicked: 'kbt_product_search_clicked',
  /** goodsId đã mở / thêm từ tab Search — PDP add vẫn là recommended. */
  recommendedGoodsIds: 'kbt_recommended_goods_ids',
  /** SHEIN style attr_id đã học từ facet search. */
  sheinStyleAttrs: 'kbt_shein_style_attrs',
  /** Sort by Shein trên tab Tìm sản phẩm (không thuộc hồ sơ shop). */
  sheinSearchSort: 'kbt_shein_search_sort',
  /**
   * Pool SP thô theo `sheinSearchPoolKey` (câu tìm + bộ lọc URL): `{ [key]: SearchPoolCacheEntry }`.
   * TTL 6h, LRU 12 entry — bấm Tìm lại cùng bộ lọc thì không phải phân trang lại.
   */
  searchPoolCache: 'kbt_search_pool_cache',
  /** productKey → ms lần upload Size Chart Bunny thành công (FAB extension). */
  sizeChartUploaded: 'kbt_size_chart_uploaded',
  /**
   * productKey → { url?, tabId?, at, reopenOnClose? } — SP đã thêm nhưng chưa upload Size Chart.
   * FAB/panel: đóng tab chưa upload → mở lại. Quick-add (`reopenOnClose: false`): đóng thì thôi.
   */
  sizeChartPending: 'kbt_size_chart_pending',
  /**
   * Job Thêm nhanh từ list Shein: `{ [goodsId]: { status, message?, listTabId?, url?, pdpTabId?, at } }`.
   * queued | running | success | failed — content sync spinner/badge.
   */
  quickAddJobs: 'kbt_quick_add_jobs',
} as const;

/** Số shop tối đa giữ trong cache «Hôm nay bán gì?» (LRU theo `savedAt`). */
export const WTS_CACHE_MAX_SHOPS = 20;

/**
 * Phiên JWT nằm `chrome.storage.local` đến khi Đăng xuất hoặc gỡ extension
 * (cài lại thì storage bị xóa → phải login). Login/refresh gửi `persistent: true`.
 */
export const AUTH_PERSISTENT = true;

/** Làm mới access token sớm hơn hạn (access JWT ~15 phút). */
export const ACCESS_TOKEN_REFRESH_LEAD_MS = 5 * 60_000;

/** Alarm background — duy trì phiên khi side panel đóng / service worker ngủ. */
export const AUTH_REFRESH_ALARM = 'kbt_auth_refresh';

/**
 * Key `kb_*` đời trước (trùng prefix KBI Craper trên sessionStorage/DOM trang Shein).
 * Background migrate → `kbt_*` rồi xóa; không đọc lại sau migrate.
 */
export const LEGACY_KB_STORAGE_KEYS = Object.values(STORAGE_KEYS).map((key) =>
  key.startsWith('kbt_') ? `kb_${key.slice(4)}` : key,
);

/** TTL cache shop / soft-revalidate phiên 4Seller (ms). */
export const FS_REVALIDATE_MS = 5 * 60 * 1000;
export const SHOPS_CACHE_TTL_MS = 5 * 60 * 1000;
