/**
 * Base URL của API KBT (kèm /api/v1).
 * Dev/local: `.env.development` → `http://localhost:5000/api/v1`
 * Production: `.env.production` → `https://4seller-api.kbt.global/api/v1` (fallback cũng vậy).
 */
export const API_BASE_URL: string =
  (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/+$/, '') ??
  'https://4seller-api.kbt.global/api/v1';

export const BRAND_NAME = 'KBT SELLER';

/** Khóa lưu trong chrome.storage.local — tách khỏi scraper / fulfil. */
export const STORAGE_KEYS = {
  auth: 'kb_tts_auth',
  theme: 'kb_tts_theme',
  /** Capture headers từ webRequest / fetch seller-us. */
  capture: 'kb_tts_capture',
  /** Shop đã normalize từ seller/common/get. */
  shopInfo: 'kb_tts_shop_info',
  /** Unified Sync orchestrator (4 bước sync + bước nhiệm vụ hệ thống). */
  unifiedSync: 'kb_tts_unified_sync',
  /** Kế hoạch nhiệm vụ truy cập URL hôm nay (cache để side panel hiển thị). */
  dailyTasks: 'kb_tts_daily_tasks',
  /** Nhiệm vụ đang chờ tab seller-us load xong sau khi điều hướng. */
  taskNavigation: 'kb_tts_task_navigation',
  /** Snapshot tab seller-us (url + loading/complete) — spinner theo trạng thái trang thật. */
  sellerUsTab: 'kb_tts_seller_us_tab',
  /** Đếm thời gian xem trang sau khi load xong (taskId → dwell). */
  taskDwells: 'kb_tts_task_dwells',
  /** Auto: tự mở lần lượt nhiệm vụ URL hôm nay rồi Sync All khi xong. */
  autoDailyTasks: 'kb_tts_auto_daily_tasks',
  /** Phiên online seller-us đang mở / chờ reconcile sau restart. */
  onlineSession: 'kb_tts_online_session',
  /** Tổng giây online đã đóng trong ngày VN (cộng phiên mở trên UI). */
  onlineToday: 'kb_tts_online_today',
} as const;

/**
 * Phiên JWT nằm `chrome.storage.local` đến khi Đăng xuất hoặc gỡ extension
 * (cài lại thì storage bị xóa → phải login). Login/refresh gửi `persistent: true`.
 */
export const AUTH_PERSISTENT = true;

/** Làm mới access token sớm hơn hạn (access JWT ~15 phút). */
export const ACCESS_TOKEN_REFRESH_LEAD_MS = 5 * 60_000;

/** Alarm background — duy trì phiên khi side panel đóng / service worker ngủ. */
export const AUTH_REFRESH_ALARM = 'kb_tts_auth_refresh';

/** Alarm bump lastSeen + heartbeat online (MV3 wake). */
export const ONLINE_HEARTBEAT_ALARM = 'kb_tts_online_heartbeat';

/** Chu kỳ alarm online (phút) — tối thiểu Chrome ~1. */
export const ONLINE_HEARTBEAT_PERIOD_MINUTES = 1;

/** Debounce gọi API heartbeat khi tab event dày. */
export const ONLINE_HEARTBEAT_DEBOUNCE_MS = 20_000;

/** Host Seller Center US. */
export const SELLER_US_HOST = 'seller-us.tiktok.com';
export const SELLER_US_ORIGIN = `https://${SELLER_US_HOST}`;

/** Endpoint lấy thông tin shop (GET, credentials:include trên tab seller-us). */
export const SELLER_COMMON_GET_PATH = '/api/v3/seller/common/get';
export const SELLER_COMMON_GET_QUERY =
  'need_verify_account=true&default_region=US&version=3';
export const SELLER_COMMON_GET_URL = `${SELLER_US_ORIGIN}${SELLER_COMMON_GET_PATH}?${SELLER_COMMON_GET_QUERY}`;

/** Passport account info (email/mobile/name/block) — không kèm X-Bogus; cookie + CSRF đủ trên tab. */
export const PASSPORT_ACCOUNT_INFO_PATH = '/passport/account/info/v2/';
export const PASSPORT_ACCOUNT_INFO_QUERY =
  'get_info_type=2&aid=4068&account_sdk_source=web&sdk_version=2.1.3-tiktokbeta.1&language=en';
export const PASSPORT_ACCOUNT_INFO_URL = `${SELLER_US_ORIGIN}${PASSPORT_ACCOUNT_INFO_PATH}?${PASSPORT_ACCOUNT_INFO_QUERY}`;

/** Onboard config (product_categories, biz_type_US…). Cần `oec_seller_id` / `seller_id`. */
export const ONBOARD_CONFIG_PATH = '/api/v1/seller/onboard/v2/config/get';
export const ONBOARD_CONFIG_BASE_QUERY =
  'locale=en&language=en&aid=4068&app_name=i18n_ecom_shop&device_platform=web&cookie_enabled=true';

/** Product creation preload (logistics + publish_limit). Cùng query base với onboard. */
export const PRODUCT_PRELOAD_PATH = '/api/v1/product/product_creation/preload';
export const PRODUCT_PRELOAD_BASE_QUERY = ONBOARD_CONFIG_BASE_QUERY;

/**
 * Pay onboard info (company + address + relate person, email không mask).
 * POST body `{ user_type: 1, biz_type: 1 }` — credentials:include trên tab seller-us.
 */
export const PAY_ONBOARD_INFO_PATH = '/api/v1/pay/onboard_info/get';
export const PAY_ONBOARD_INFO_BASE_QUERY = ONBOARD_CONFIG_BASE_QUERY;
export const PAY_ONBOARD_INFO_BODY = { user_type: 1, biz_type: 1 } as const;

/**
 * Settlement / payout bank account (masked bank_account_number).
 * GET — credentials:include trên tab seller-us.
 */
export const SETTLEMENT_ACCOUNT_PATH = '/api/v1/seller/settlement/account/get';
export const SETTLEMENT_ACCOUNT_BASE_QUERY = ONBOARD_CONFIG_BASE_QUERY;

/**
 * Finance On hold snapshot (to_settle + DELI bill_finish_period_in_days).
 * GET — credentials:include trên tab seller-us. Query: amount_stat_type=1&statement_version=1.
 */
export const STATEMENT_STAT_INFO_PATH = '/api/v1/pay/statement/stat/info';

/** Payout / payment history list (finance → payouts). */
export const PAYMENT_LIST_PATH = '/api/v1/pay/payout/seller/payment/list';

/** payment_id → statement_records (page_type=5). */
export const STATEMENT_LIST_DETAIL_PATH = '/api/v1/pay/statement/list/detail';

/** Statement order lines (orders settled in a statement / payout). */
export const STATEMENT_ORDER_LIST_PATH = '/api/v1/pay/statement/order/list';

/** Product opportunities / trending keywords lead list. */
export const OPPORTUNITY_LEAD_LIST_PATH =
  '/api/v1/product/oc/seller_product_opportunity/seller/lead/list';

/**
 * Compass → Product analysis list (POST, cookie + credentials:include trên tab seller-us).
 * Body phân trang: `request.list_control.pagination.page` bắt đầu từ 0.
 */
export const INSIGHTS_PRODUCT_LIST_PATH = '/api/v3/insights/seller/ttp/product/list';

/** Compass tính ngày theo timezone shop, không theo giờ máy. */
export const INSIGHTS_TIMEZONE_NAME = 'America/Los_Angeles';

/** Growth Center — violation records. */
export const VIOLATION_RECORDS_PATH =
  '/api/v1/seller/growth_center/shop/violation/records/list';

/** Growth Center — warning records. */
export const WARNING_RECORDS_PATH =
  '/api/v1/seller/growth_center/shop/warning/records/list';

/** Health Center — AHR score (`data.score`). */
export const AHR_SCORE_PATH = '/api/v1/account/ahr/score';

/** Health Center — Shop Performance Score overview (tổng + 3 nhóm điểm). */
export const SES_SCORE_OVERVIEW_PATH = '/api/v1/seller/experience-score/score-overview';
