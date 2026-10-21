import type {
  AuthState,
  DraftItem,
  FourSellerStatus,
  FsShopOption,
  ProductSourceOption,
} from './types';
import type {
  ProductSearchCatalog,
  ProductSearchKeywordInput,
  ProductSearchState,
  SavedKeywordRow,
  SheinSearchCapture,
} from './productSearch';
import type { SheinSearchSession } from './sheinSearchApi';
import type { SheinSizeGuide } from './sheinSizeGuide';

/** Message types trao đổi giữa popup/content và background service worker. */
export type ExtMessage =
  | { type: 'GET_STATE' }
  | { type: 'LOGIN'; username: string; password: string }
  | { type: 'LOGOUT' }
  | { type: 'REFRESH_FS_STATUS' }
  /** Content script gửi HTML đã lấy để đẩy lên 4Seller. */
  | {
      type: 'ADD_PRODUCT';
      url: string;
      html: string;
      /** Shein `comment_num` (optional). */
      reviewCount?: number | null;
      /** Shein `comment_rank_average` (optional). */
      averageRating?: number | null;
      platform?: string | null;
      stock?: number | null;
      storeCode?: string | null;
      storeLogo?: string | null;
      storeTitle?: string | null;
      soldByTips?: string | null;
      shipsFromTips?: string | null;
      shopRank?: number | null;
      storeAttention?: number | null;
      shippingDays?: number | null;
      shippingDayPercent?: number | null;
      shippingDayPercentDesc?: string | null;
      isLocalProduct?: boolean | null;
      isSupportQuickShip?: boolean | null;
      /** Shein size guide (local-only). */
      sizeGuide?: SheinSizeGuide | null;
    }
  /** Popup yêu cầu đẩy sản phẩm từ tab đang mở (BG tự lấy HTML). */
  | { type: 'ADD_CURRENT_TAB' }
  /** Content script hỏi tabId của chính nó. */
  | { type: 'WHOAMI' }
  /** Trạng thái tab đang active (đã load xong chưa). */
  | { type: 'GET_ACTIVE_TAB_STATUS' }
  /** Reload tab đang theo dõi (side panel) hoặc tab http active. */
  | { type: 'RELOAD_ACTIVE_TAB'; tabId?: number }
  | {
      type: 'LIST_DRAFTS';
      pageType?: 'draft' | 'published';
      shopIds?: number[];
      pageCurrent?: number;
      pageSize?: number;
    }
  | { type: 'LIST_TAB_COUNTS'; shopIds?: number[] }
  /** Xóa 1 listing local (+ 4Seller) và bỏ shop khỏi kbt_added_product_ids. */
  | { type: 'DELETE_DRAFT'; id: string; sourceUrl?: string; fourSellerShopId?: number; pageType?: string }
  /** Làm mới danh sách shop 4Seller. */
  | { type: 'LIST_SHOPS' }
  /** Chọn 1 hoặc nhiều shop (≥1) để Generate store draft. */
  | { type: 'SET_SELECTED_SHOPS'; shopIds: number[] }
  /** AI «Hôm nay bán gì?» — chỉ khi đã chọn đúng 1 shop (local Guid). */
  | { type: 'WHAT_TO_SELL_TODAY'; shopLocalId: string }
  /** Keyword đã lưu của shop (GET /product-opportunities/saved-keywords). */
  | { type: 'LIST_SAVED_KEYWORDS'; shopLocalId: string }
  /** Tìm SP Shein theo đúng 1 keyword đã chọn + catalog (warmup URL lọc + BFF). */
  | {
      type: 'SEARCH_PRODUCTS';
      shopLocalId: string;
      shopFsId: number;
      catalog: ProductSearchCatalog;
      keywords: ProductSearchKeywordInput[];
    }
  | { type: 'SEARCH_CANCEL' }
  | { type: 'GET_PRODUCT_SEARCH' }
  | { type: 'REMOVE_SEARCHED_PRODUCT'; goodsId: string }
  /** Content MAIN-world hook gửi JSON search. */
  | { type: 'SHEIN_SEARCH_CAPTURE'; capture: SheinSearchCapture }
  /** Content MAIN-world hook gửi header + template URL search. */
  | { type: 'SHEIN_SEARCH_SESSION'; session: SheinSearchSession }
  /** Thêm draft lần lượt các SP đã chọn (mở PDP → post-html). */
  | { type: 'ADD_SEARCHED_PRODUCTS'; goodsIds: string[] }
  /**
   * Thêm nhanh từ card list Shein: BG mở PDP tab nền → capture → post-html.
   * `url` = href PDP tuyệt đối nếu card có; thiếu thì BG `buildSheinPdpUrl(goodsId)`.
   */
  | { type: 'QUICK_ADD_LIST_PRODUCT'; goodsId: string; url?: string }
  /** Mở URL ngay trên tab http đang xem cạnh side panel (không mở tab mới). */
  | { type: 'OPEN_URL_ACTIVE_TAB'; url: string }
  /** Mở URL tab mới cạnh tab http đang xem (click tên SP Search). */
  | { type: 'OPEN_URL_NEW_TAB'; url: string }
  /** Đổi theme tài khoản (local + `PUT /auth/theme` khi đã login). */
  | { type: 'SET_THEME'; theme: 'light' | 'dark' }
  /** Content script: mở side panel (khi chưa đăng nhập / chưa chọn shop). */
  | { type: 'OPEN_SIDE_PANEL' }
  /** Upload Size Chart PNG/JPG đã chụp thủ công (SHEIN PDP). */
  | {
      type: 'UPLOAD_SIZE_CHART';
      url: string;
      fileName: string;
      mimeType: string;
      fileBase64: string;
    }
  /** Kiểm tra đã có ảnh Size Chart trên server chưa. */
  | { type: 'GET_SIZE_CHART_STATUS'; url: string };

export interface StateResponse {
  authenticated: boolean;
  user: AuthState['user'];
  /** Theme hiện tại (cache / tài khoản). */
  theme: 'light' | 'dark';
  fourSeller: FourSellerStatus | null;
  productSources: ProductSourceOption[];
  shops: FsShopOption[];
  /** Shop đang chọn (≥1) để Generate store draft. */
  selectedShopIds: number[];
}

export interface AddProductResponse {
  ok: boolean;
  message: string;
  /** Số shop trong lần đẩy này (0 hoặc 1). */
  shopCount?: number;
  /** Tên shop đã chọn (nếu có). */
  shopName?: string;
  /** URL đã submit. */
  url?: string;
}

export type QuickAddListStatus = 'queued' | 'running' | 'success' | 'failed' | 'pending';

export interface QuickAddListProductResponse {
  ok: boolean;
  message: string;
  goodsId?: string;
}

export interface QuickAddJobRecord {
  status: Exclude<QuickAddListStatus, 'pending'>;
  message?: string;
  listTabId?: number;
  url?: string;
  pdpTabId?: number;
  at: number;
}

export interface UploadSizeChartResponse {
  ok: boolean;
  message: string;
  publicUrl?: string;
}

export interface SizeChartStatusResponse {
  ok: boolean;
  hasImage: boolean;
  message?: string;
}

export interface ListDraftsResponse {
  ok: boolean;
  message?: string;
  drafts: DraftItem[];
  total: number;
  pageCurrent: number;
  pageSize: number;
}

export interface ListTabCountsResponse {
  ok: boolean;
  message?: string;
  draftTotal: number;
  publishLimitPublished: number;
  publishLimitTotal: number;
}

export interface DeleteDraftResponse {
  ok: boolean;
  message: string;
}

export interface ListShopsResponse {
  ok: boolean;
  message?: string;
  shops: FsShopOption[];
  selectedShopIds: number[];
}

export interface WhatToSellTodaySearch {
  keywords: string;
  url: string;
  rationale?: string | null;
}

export interface WhatToSellTodayResult {
  analysis: string;
  analysisPoints: string[];
  productCategories: string[];
  avgCost?: number | null;
  avgSalePrice?: number | null;
  minPrice: number;
  maxPrice: number;
  periodDays: number;
  productCountUsed: number;
  asOfDate?: string;
  season?: string;
  upcomingEvents: string[];
  searchesTrend: WhatToSellTodaySearch[];
  searchesUpcoming: WhatToSellTodaySearch[];
}

export interface WhatToSellTodayResponse {
  ok: boolean;
  message?: string;
  data?: WhatToSellTodayResult;
}

export interface ListSavedKeywordsResponse {
  ok: boolean;
  message?: string;
  items: SavedKeywordRow[];
}

export interface ProductSearchResponse {
  ok: boolean;
  message?: string;
  state?: ProductSearchState;
}

export interface ActiveTabStatusResponse {
  ok: boolean;
  message?: string;
  /** Tab http(s) hợp lệ, đã load xong, và là trang sản phẩm nguồn đã cấu hình. */
  ready: boolean;
  status?: string;
  url?: string;
  /** chrome.tabs.Tab.id — dùng để side panel bỏ qua response cũ khi đổi tab. */
  tabId?: number;
  /** Có nhận diện được trang chi tiết sản phẩm không. */
  isProductPage?: boolean;
  /** Tên nguồn (SHEIN, Temu…). */
  sourceName?: string;
  /** Mã luồng: shein | temu | amazon | … */
  flow?: string;
  /** Product id trích từ URL (nếu có). */
  productId?: string;
  /** Key `flow:id` (nếu có) — bind trạng thái Đã thêm theo sản phẩm, không theo UI cũ. */
  productKey?: string;
  /** Tab này đang có request thêm sản phẩm in-flight. */
  adding?: boolean;
  /** Đã thêm đủ mọi shop đang chọn (local storage). */
  alreadyAdded?: boolean;
  /**
   * none | partial | full theo shop đang chọn.
   * partial = còn shop thiếu → vẫn cho thêm (UI cam).
   */
  addedStatus?: 'none' | 'partial' | 'full';
  /** Số shop đã có / đang chọn (vd. partial 1/2). */
  addedShopHit?: number;
  addedShopTotal?: number;
  /**
   * Content script đã settle DOM (cùng tiêu chí nút nổi).
   * Khi có — side panel phải khớp; thiếu thì fallback `status === 'complete'`.
   */
  pageReady?: boolean;
  /** Tiến độ đợt thêm nhiều tab: thành công / tổng đã bắt đầu. */
  addProgress?: {
    total: number;
    success: number;
    failed: number;
    inFlight: number;
  };
}

export type ExtResponse<T = unknown> =
  | ({ ok: true } & T)
  | { ok: false; message: string };

/** Message background gửi xuống content script để lấy HTML. */
export type ContentMessage =
  | { type: 'CAPTURE_HTML' }
  | { type: 'GET_SHEIN_SEARCH_CAPTURE' }
  | { type: 'GET_SHEIN_SEARCH_SESSION' }
  | { type: 'RESET_SHEIN_SEARCH_SESSION' }
  /** Xóa local/session/IndexedDB/Cache Storage trên tab Shein — không đụng cookie. */
  | { type: 'CLEAR_SHEIN_SITE_CACHE' }
  | {
      type: 'FETCH_SHEIN_SEARCH';
      url: string;
      method?: string;
      body?: string;
      headers?: Record<string, string>;
      /** `page` pdsearch — content `replaceState` trước XHR, không load trang. */
      page?: number;
    }
  /**
   * Bấm phân trang của chính SPA pdsearch: nội dung đổi, **không** load lại trang.
   * SPA tự phát request đã ký nên không có chữ ký bot như forge XHR.
   */
  | { type: 'SHEIN_SEARCH_GOTO_PAGE'; page: number; query: string }
  /** Cùng origin: `location.assign` để PDP giữ Referer trang Shein hiện tại. */
  | { type: 'NAVIGATE_ASSIGN'; url: string }
  /** Tiến trình Thêm nhanh trên card list (tab list gốc). */
  | {
      type: 'QUICK_ADD_LIST_STATUS';
      goodsId: string;
      status: QuickAddListStatus;
      message?: string;
    }
  /**
   * BG: gắn cảnh báo đóng tab + toast — SP đã thêm, chưa Size Chart.
   * `reopenOnClose: false` = quick-add (tab mở tự động): không beforeunload, đóng thì thôi.
   * (beforeunload thường bị Chrome bỏ trên tab mở nền không có user gesture.)
   */
  | { type: 'SIZE_CHART_LEAVE_GUARD'; message?: string; reopenOnClose?: boolean };

export interface FetchSheinSearchResponse {
  ok: boolean;
  status?: number;
  json?: unknown;
  message?: string;
}
export interface CaptureHtmlResponse {
  url: string;
  html: string;
  reviewCount?: number | null;
  averageRating?: number | null;
  platform?: string | null;
  stock?: number | null;
  storeCode?: string | null;
  storeLogo?: string | null;
  storeTitle?: string | null;
  soldByTips?: string | null;
  shipsFromTips?: string | null;
  shopRank?: number | null;
  storeAttention?: number | null;
  shippingDays?: number | null;
  shippingDayPercent?: number | null;
  shippingDayPercentDesc?: string | null;
  isLocalProduct?: boolean | null;
  isSupportQuickShip?: boolean | null;
  /** Shein size guide normalized (local-only). */
  sizeGuide?: SheinSizeGuide | null;
}
