import type {
  ActiveMatch,
  AuthState,
  CheckoutCostSnapshot,
  FulfilOrder,
  ProductSourceOption,
  VariantFitSnapshot,
} from './types';

/** Message types trao đổi giữa popup/content và background service worker. */
export type ExtMessage =
  | { type: 'GET_STATE' }
  | { type: 'LOGIN'; pin: string }
  | { type: 'LOGOUT' }
  | {
      type: 'LIST_ORDERS';
      productSourceId?: string;
      platformOrderStatus?: string;
      /**
       * `needsPurchase` (mặc định) = hàng đợi chưa mua;
       * `purchasedToday` = đã stamp FulfilPurchasedAt trong ngày VN.
       * Bỏ qua khi có `search`.
       */
      mode?: 'needsPurchase' | 'purchasedToday';
      /** Có search → lookup toàn cục mọi trạng thái/nguồn (không đụng cache hàng đợi). */
      search?: string;
    }
  | { type: 'GET_ACTIVE_TAB_STATUS' }
  | { type: 'OPEN_PRODUCT_URL'; url: string }
  | {
      type: 'OPEN_ORDER_ITEM';
      /** Bắt buộc khi mở từ tab Đã mua / search — cache hàng đợi không chứa đơn đã stamp. */
      orderId?: string;
      /** Đơn anh em cùng khách (phone+zip) — claim kèm. */
      siblingOrderIds?: string[];
      item: {
        id: string;
        sellerSku?: string | null;
        sourceUrl?: string | null;
        listingUrl?: string | null;
        variantAttr?: string | null;
        title?: string | null;
        quantity: number;
      };
    }
  | { type: 'SET_ACTIVE_ORDER_ITEM'; orderId: string; itemId?: string }
  | { type: 'MARK_PURCHASED'; orderId: string; force?: boolean }
  /** Gỡ stamp FulfilPurchasedAt (undo «Đã mua» bấm nhầm). */
  | { type: 'UNMARK_PURCHASED'; orderId: string }
  | { type: 'CLAIM_ORDER'; orderId: string; siblingOrderIds?: string[] }
  | { type: 'RELEASE_ORDER_CLAIM'; orderId: string; allSessions?: boolean }
  | { type: 'LIST_ORDER_CLAIMS' }
  | { type: 'RENEW_ORDER_CLAIM'; orderId: string }
  /** Legacy: maps to issue-tag replace/clear via PUT …/message. */
  | { type: 'UPDATE_ORDER_MESSAGE'; orderId: string; message: string | null }
  | { type: 'LIST_ISSUE_TAGS' }
  | { type: 'CREATE_ISSUE_TAG'; label: string }
  | { type: 'UPDATE_ISSUE_TAG'; tagId: string; label: string }
  | { type: 'DELETE_ISSUE_TAG'; tagId: string }
  | { type: 'SET_ORDER_ISSUE_TAGS'; orderId: string; tagIds: string[] }
  | { type: 'SET_ORDER_ISSUE_STATUS'; orderId: string; status: string }
  | { type: 'RELOAD_ACTIVE_TAB' }
  | { type: 'OPEN_SIDE_PANEL' }
  | { type: 'REFRESH_ACTIVE_BUYER' }
  /** ensureCapture: click Sync Order — nếu thiếu body thì mở /user/orders/list để bắt.
   *  manual: bấm nút Sync Order (khác Auto Sync) — lấy hết list, không skip theo snapshot. */
  | { type: 'SHEIN_SYNC'; ensureCapture?: boolean; manual?: boolean }
  /** Chỉ mở/reload My Orders + bắt header/email/accountId — không sync đơn. */
  | { type: 'PREPARE_SHEIN_SESSION'; forceRefresh?: boolean }
  | { type: 'SHEIN_FETCH_ORDERS' }
  | { type: 'GET_SHEIN_SYNC_CONFIG' }
  | { type: 'WHOAMI' };

export interface StateResponse {
  authenticated: boolean;
  user: AuthState['user'];
  productSources: ProductSourceOption[];
}

export interface ListOrdersResponse {
  ok: boolean;
  message?: string;
  orders: FulfilOrder[];
  total: number;
  productSourceId?: string;
  sourceName?: string;
}

export interface ActiveTabStatusResponse {
  ok: boolean;
  message?: string;
  ready: boolean;
  status?: string;
  url?: string;
  tabId?: number;
  isProductPage?: boolean;
  sourceName?: string;
  sourceId?: string;
  flow?: string;
  productId?: string;
  match?: ActiveMatch | null;
  /** Khớp size/màu trên PDP (nếu content đã đánh giá). */
  variantFit?: VariantFitSnapshot | null;
  /** Tổng thanh toán DOM trên trang checkout (để tính profit vs Rev). */
  checkoutCost?: CheckoutCostSnapshot | null;
}

export interface MarkPurchasedResponse {
  ok: boolean;
  message: string;
  /** true khi cần confirm force (đơn người khác đang giữ). */
  needsForce?: boolean;
}

export interface ClaimOrderResponse {
  ok: boolean;
  message: string;
  claim?: {
    orderId: string;
    state: string;
    claimedByUserId?: string | null;
    claimedByName?: string | null;
    claimedAt?: string | null;
    expiresAt?: string | null;
    sessionCount: number;
    sessionIds: string[];
    startedAt?: string | null;
  };
  serverTime?: string;
}

export interface ListOrderClaimsResponse {
  ok: boolean;
  message?: string;
  items: ClaimOrderResponse['claim'][];
  serverTime?: string;
}

export interface UnmarkPurchasedResponse {
  ok: boolean;
  message: string;
}

export interface UpdateOrderMessageResponse {
  ok: boolean;
  message: string;
  /** Giá trị đã lưu (null nếu xoá). */
  orderMessage?: string | null;
  issueTags?: { id: string; label: string; isSystem: boolean }[];
  issueStatus?: string | null;
}

export interface ListIssueTagsResponse {
  ok: boolean;
  message?: string;
  tags?: { id: string; label: string; isSystem: boolean }[];
}

export interface CreateIssueTagResponse {
  ok: boolean;
  message?: string;
  tag?: { id: string; label: string; isSystem: boolean };
}

export interface UpdateIssueTagResponse {
  ok: boolean;
  message?: string;
  tag?: { id: string; label: string; isSystem: boolean };
}

export interface DeleteIssueTagResponse {
  ok: boolean;
  message?: string;
}

export interface SetOrderIssueResponse {
  ok: boolean;
  message?: string;
  order?: FulfilOrder;
}

export interface RefreshActiveBuyerResponse {
  ok: boolean;
  message?: string;
  match?: ActiveMatch | null;
}

export interface SetActiveOrderItemResponse {
  ok: boolean;
  message?: string;
  match?: ActiveMatch | null;
}

export interface SheinSyncResponse {
  ok: boolean;
  message?: string;
  /** Số đơn SHEIN lấy được. */
  fetched?: number;
  /** Số đơn hệ thống khớp được. */
  matched?: number;
  /** Số đơn được cập nhật tracking. */
  updatedTracking?: number;
  /** Số đơn Auto Mark Shipped thành công qua 4Seller. */
  autoShipped?: number;
  /** Số lỗi Auto Mark Ship có thể retry. */
  autoShipFailed?: number;
  /** Số đơn cần chọn Shipping Service thủ công. */
  autoShipBlocked?: number;
  /** Số đơn được cập nhật chi phí. */
  updatedCost?: number;
  /** Số đơn SHEIN không khớp đơn nào. */
  unmatched?: number;
  /** Số billno vẫn thiếu order detail tin cậy sau sync. */
  incompleteDetail?: number;
  /**
   * Mã lượt sync để tra nhật ký backend
   * (`GET dev/diagnostics/shein-sync-runs/{runId}`) khi con số trông sai.
   */
  runId?: string;
}

export interface SheinSyncConfigResponse {
  ok: boolean;
  message?: string;
  enabled: boolean;
  intervalSeconds: number;
  /** `new` = match đơn mới + refresh open set; `all` = cập nhật lại toàn bộ. */
  updateMode?: 'new' | 'all';
  /** Số luồng song song get_order_info (1–20). */
  concurrentThreads?: number;
  refreshLookbackDays?: number;
  /** Chỉ còn force_billnos (chẩn đoán). Open-set từ DB đã bỏ — sync theo list SHEIN. */
  refreshBillnos?: string[];
  /** Đính kèm JSON gốc SHEIN vào payload sync để chẩn đoán. Mặc định tắt. */
  captureRawPayload?: boolean;
  /** `realtime` = extension gọi SHEIN; `local` = legacy backend session (không còn dùng). */
  headerSource?: 'local' | 'realtime';
}

export type ExtResponse<T = unknown> =
  | ({ ok: true } & T)
  | { ok: false; message: string };
