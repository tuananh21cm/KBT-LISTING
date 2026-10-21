/** Trạng thái đăng nhập kinhbac lưu trong storage đến khi Đăng xuất hoặc gỡ extension. */
export interface AuthState {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  savedAt: string;
  user: {
    id: string;
    username: string;
    fullName?: string;
    theme?: 'light' | 'dark';
  } | null;
}

/** Logo shop từ seller/common/get. */
export interface TikTokShopLogo {
  height?: number;
  width?: number;
  url_list?: string[];
}

/** Khối seller trong response seller/common/get. */
export interface TikTokSellerInfo {
  /** Account status: 2 active, 3 deactivated. */
  status?: number;
  seller_type?: number;
  business_type?: number;
  shop_name?: string;
  seller_id?: string;
  shop_code?: string;
  region_code?: string;
  logo?: TikTokShopLogo;
  timezone?: { name?: string };
  global_shop_name?: string;
  shop_region?: string;
  shop_status?: number;
}

/** Payload data của seller/common/get (subset dùng UI). */
export interface SellerCommonGetData {
  seller?: TikTokSellerInfo;
  company_name?: string;
  phone_verified?: boolean;
  email_verified?: boolean;
  verified_documents?: number;
  global_seller?: {
    create_time?: string | number;
    global_seller_id?: string;
    seller_type?: number;
    base_region?: string;
    timezone?: { name?: string };
  };
}

/** Response envelope seller/common/get. */
export interface SellerCommonGetResponse {
  code?: number;
  message?: string;
  data?: SellerCommonGetData;
}

/** Payload data của passport/account/info/v2 (subset dùng UI). */
export interface PassportAccountInfoData {
  email?: string;
  mobile?: string;
  name?: string;
  screen_name?: string;
  is_blocked?: number | boolean;
  is_blocking?: number | boolean;
  user_id?: number | string;
  user_id_str?: string;
  avatar_url?: string;
}

/** Response envelope passport/account/info/v2 (`message === "success"`). */
export interface PassportAccountInfoResponse {
  message?: string;
  data?: PassportAccountInfoData;
}

/** Phân loại pháp lý shop — khớp kinhbac `ShopType` (`Per` | `Llc`). */
export type TikTokShopEntityType = 'Per' | 'Llc';

/** Category từ onboard `customized_config.product_categories`. */
export interface TikTokProductCategory {
  label: string;
  value: string;
}

/** Response onboard config/get. */
export interface OnboardConfigResponse {
  code?: number;
  message?: string;
  data?: {
    customized_config?: {
      product_categories?: Array<{ label?: string; value?: string | number }>;
    };
    tcc_config?: string;
    feature_states?: unknown[];
  };
}

/** Logistics service từ product creation preload. */
export interface TikTokLogisticsService {
  serviceName: string;
  deliveryOptionName: string;
  isDefault?: boolean;
  warehouseId?: string;
}

/** Publish listing limit từ preload. */
export interface TikTokPublishLimit {
  totalLimit: number;
  publishedNum: number;
}

/** Response product_creation/preload (subset). */
export interface ProductPreloadResponse {
  code?: number;
  message?: string;
  data?: {
    warehouse_logistics_services?: Array<{
      warehouse_id?: string;
      logistics_services?: Array<{
        service_id?: string;
        service_name?: string;
        delivery_option_name?: string;
        is_default?: boolean;
        is_reachable?: boolean;
      }>;
    }>;
    /** TikTok gửi số hoặc chuỗi số tùy version. */
    publish_limit?: {
      total_limit?: number | string;
      published_num?: number | string;
    };
  };
}

/** Field passport gắn vào shop card. */
export interface TikTokPassportAccountFields {
  accountEmail?: string | null;
  accountMobile?: string | null;
  accountName?: string | null;
  isBlocked?: boolean | null;
  isBlocking?: boolean | null;
}

/** Địa chỉ từ pay/onboard_info (company hoặc person). */
export interface TikTokShopAddress {
  lineOne?: string;
  lineTwo?: string;
  city?: string;
  state?: string;
  country?: string;
  countryCode?: string;
  postCode?: string;
  addressType?: number | null;
}

/** Người liên quan (owner/officer) từ pay/onboard_info.relate_persons. */
export interface TikTokShopRelatePerson {
  fullName?: string;
  firstName?: string;
  lastName?: string;
  birthday?: string;
  nationality?: string;
  address?: TikTokShopAddress | null;
  certNum?: string | null;
  relatePersonType?: number | null;
}

/** Subset response POST /api/v1/pay/onboard_info/get. */
export interface PayOnboardInfoResponse {
  code?: number;
  message?: string;
  data?: {
    enterprise?: {
      company?: {
        company_name?: string;
        reg_region?: string;
      };
      contact?: {
        email?: string;
        mobile?: string;
        phone?: string;
      };
      certificates?: Array<{
        cert_type?: number;
        cert_num?: string;
        cert_region?: string;
      }>;
      addresses?: Array<{
        post_code?: string;
        name_l0?: string;
        name_l1?: string;
        name_l2?: string;
        line_one?: string;
        line_two?: string;
        address_type?: number;
        country_code?: string;
      }>;
      relate_persons?: Array<{
        person?: {
          first_name?: string;
          last_name?: string;
          nationality?: string;
          birthday?: string;
          certificates?: Array<{
            cert_type?: number;
            cert_num?: string;
            cert_region?: string;
          }>;
          addresses?: Array<{
            post_code?: string;
            name_l0?: string;
            name_l1?: string;
            name_l2?: string;
            line_one?: string;
            line_two?: string;
            address_type?: number;
            country_code?: string;
          }>;
          contact?: { email?: string; mobile?: string };
        };
        relate_person_type?: number;
        office_bearer?: number;
        relate_person_type_list?: number[];
      }>;
      entity_type?: number;
      shop_name?: string;
    };
    user_type?: number;
    is_v2?: boolean;
  };
}

/** Field đã normalize từ pay/onboard_info để merge vào shop. */
export interface TikTokPayOnboardFields {
  companyName?: string | null;
  companyEmail?: string | null;
  companyMobile?: string | null;
  companyAddress?: TikTokShopAddress | null;
  companyCertNum?: string | null;
  companyCertType?: number | null;
  payEntityType?: number | null;
  owner?: TikTokShopRelatePerson | null;
  shopNameFromPay?: string | null;
}

/** Response GET /api/v1/seller/settlement/account/get. */
export interface SettlementAccountResponse {
  code?: number;
  message?: string;
  data?: {
    payment_method?: string;
    beneficiary_name?: string;
    bank_account_number?: string;
    account_type?: string;
    status?: number;
  };
  country_code?: string;
}

/** Field đã normalize từ settlement account. */
export interface TikTokSettlementAccountFields {
  bankAccountNumber?: string | null;
  paymentMethod?: string | null;
  beneficiaryName?: string | null;
  accountType?: string | null;
  settlementStatus?: number | null;
}

/** Response GET /api/v1/pay/statement/stat/info (Finance On hold + settlement period). */
export interface StatementStatInfoResponse {
  code?: number;
  message?: string;
  data?: {
    to_settle_amount_stat?: {
      amount?: { amount?: string | number };
      reasons_detail?: Array<{
        title?: { starling_key?: string };
        reason?: number;
        amount?: { amount?: string | number };
      }>;
    };
    seller_quality_stat?: {
      bill_finish_period_in_days?: number | string;
    };
  };
}

/** Field đã normalize từ statement/stat/info. */
export interface TikTokStatementStatFields {
  onHoldAmount: number;
  onHoldInTransitAmount: number;
  onHoldAwaitingSettlementAmount: number;
  onHoldReserveAmount: number;
  /** `bill_finish_period_in_days` → Shop.Deli; null nếu thiếu quality_stat. */
  deli?: number | null;
}

/** Data đã normalize để hiển thị side panel. */
export interface TikTokShopInfo extends TikTokPassportAccountFields {
  shopName: string;
  sellerId: string;
  shopCode?: string;
  region?: string;
  companyName?: string;
  logoUrl?: string;
  shopStatus?: number;
  /** `seller.status` — account: 2 hoạt động, 3 deactivated. */
  sellerStatus?: number;
  timezone?: string;
  createTimeMs?: number | null;
  phoneVerified?: boolean | null;
  emailVerified?: boolean | null;
  verifiedDocuments?: number | null;
  sellerType?: number | null;
  /**
   * `seller.business_type` thô (US onboard: 0 individual, 2 solepro, 3 partner, 4 corp).
   */
  businessType?: number | null;
  /** PER / LLC suy luận (khớp admin `ShopType`). */
  shopType?: TikTokShopEntityType | null;
  /** Giải thích ngắn cách suy luận (tooltip UI). */
  shopTypeReason?: string | null;
  /** Danh sách category từ onboard config. */
  productCategories?: TikTokProductCategory[];
  /** Phương thức giao hàng (dedupe theo service + delivery option). */
  logisticsServices?: TikTokLogisticsService[];
  /** Giới hạn listing: published / total. */
  publishLimit?: TikTokPublishLimit | null;
  /** Chặng lấy được số trên: `page-harvest` / `bridge` / `isolated …` — để đọc log sync. */
  publishLimitSource?: string | null;
  /** Địa chỉ công ty (pay/onboard_info). */
  companyAddress?: TikTokShopAddress | null;
  /** EIN / cert công ty (pay/onboard_info.certificates). */
  companyCertNum?: string | null;
  companyCertType?: number | null;
  /** `enterprise.entity_type` từ pay onboard. */
  payEntityType?: number | null;
  /** Owner / relate person chính. */
  owner?: TikTokShopRelatePerson | null;
  /** Settlement payout bank (masked), từ settlement/account/get. */
  bankAccountNumber?: string | null;
  settlementPaymentMethod?: string | null;
  settlementBeneficiaryName?: string | null;
  settlementAccountType?: string | null;
  settlementAccountStatus?: number | null;
  /** Tổng On hold — `to_settle_amount_stat.amount.amount`. */
  onHoldAmount?: number | null;
  /** Chờ giao hàng — starling `unsettled_statement_reasons_in_transit_new`. */
  onHoldInTransitAmount?: number | null;
  /** Đã giao chờ TT — `unsettled_statement_reasons_settlement_in_progress_new`. */
  onHoldAwaitingSettlementAmount?: number | null;
  /** Tiền giữ lại — `finance_payments_table_type_reserve`. */
  onHoldReserveAmount?: number | null;
  /** Hạn thanh toán (ngày) — `bill_finish_period_in_days` → Shop.Deli. */
  deli?: number | null;
  /**
   * Shop-level khóa bán 365 ngày từ products/list
   * (`post_onboarding_identity_video_verification_reason`).
   * Chỉ true khi JSON lần scrape này chứa key — omit khi API fail.
   */
  identityVideoVerificationLock?: boolean;
  /** Luôn `capture` khi lấy từ seller-us (không còn mock cứng). */
  source: 'capture';
  fetchedAt?: number;
}

/** Header đã bắt từ request seller-us. */
export interface TikTokCaptureHeaderItem {
  name: string;
  present: boolean;
  value?: string;
  detail?: string;
}

/** Snapshot capture session seller-us. */
export interface TikTokCaptureState {
  headers: Record<string, string>;
  at: number;
  lastUrl?: string;
  /** Đã GET seller/common/get thành công (code===0). */
  shopOk?: boolean;
}

/** Trạng thái tab / session seller-us (UI). */
export type SellerUsSessionStatus =
  | 'logged_in'
  | 'tab_open'
  | 'no_tab'
  | 'no_capture';

/** Phase sync profile shop lên API Kinhbac. */
export type KinhbacSyncPhase = 'idle' | 'pending' | 'syncing' | 'ok' | 'error';

/** Snapshot tiến trình sync (chrome.storage + StateResponse). */
export interface KinhbacSyncState {
  phase: KinhbacSyncPhase;
  message?: string;
  shopName?: string;
  sellerId?: string;
  at: number;
}

/** Trạng thái một bước trong unified Sync. */
export type UnifiedSyncStepStatus =
  | 'pending'
  | 'running'
  | 'completed'
  | 'failed'
  | 'skipped';

export interface UnifiedSyncStepState {
  status: UnifiedSyncStepStatus;
  message?: string;
  at?: number;
}

/** Orchestrator chạy các bước sync + tải nhiệm vụ hệ thống khi bấm nút Sync. */
export interface UnifiedSyncState {
  runId: string;
  /** `fast` = chỉ Finance + Health Center + Shop Score. State cũ thiếu field → coi là `all`. */
  mode?: 'all' | 'fast';
  phase: 'idle' | 'running' | 'done';
  profile: UnifiedSyncStepState;
  payout: UnifiedSyncStepState;
  opportunity: UnifiedSyncStepState;
  analytics: UnifiedSyncStepState;
  /** Growth Center violation/warning scrape + sync. */
  healthCenter: UnifiedSyncStepState;
  /** AHR score + Shop Performance Score scrape + sync. */
  shopScore: UnifiedSyncStepState;
  /** Tải danh sách nhiệm vụ truy cập URL hôm nay (không tự mở trang). */
  tasks: UnifiedSyncStepState;
  startedAt: number;
  finishedAt?: number;
}

/** Một nhiệm vụ truy cập URL shop cần làm hôm nay. */
export interface ShopDailyTask {
  id: string;
  name: string;
  url: string;
  /** Prefix so khớp URL đang mở (backend đã tính sẵn từ URL đích). */
  urlMatchPattern: string;
  /** Số giây phải ở lại trang sau khi load xong; 0 = tính ngay. */
  viewDurationSeconds: number;
  /** Đã hoàn thành hôm nay. */
  done: boolean;
}

/** Snapshot tab seller-us đang theo dõi — panel dùng để hiện spinner khi trang đang tải. */
export interface SellerUsTabSnapshot {
  tabId: number;
  url: string;
  status: 'loading' | 'complete';
  updatedAt: number;
}

/** Nhiệm vụ đang chờ tab seller-us khớp URL và load xong (spinner trên panel). */
export interface TaskNavigationState {
  taskId: string;
  targetUrl: string;
  startedAt: number;
  /** Tab đang điều hướng; thiếu (cache cũ) thì observer nhận mọi tab seller-us. */
  tabId?: number;
  /** URL lúc bấm / lúc bắt đầu load — complete của document cũ bị bỏ qua. */
  fromUrl?: string;
  /** Epoch khi URL đích đã xuất hiện (complete / SPA) — chưa mạng idle thì vẫn spinner. */
  destSeenAt?: number;
  /** Bấm nhiệm vụ khi tab đã đứng đúng URL (complete). */
  alreadyThere?: boolean;
  /** Đã thấy fetch/XHR của trang sau khi bắt đầu nav — đợi mạng yên, không tin document complete. */
  sawNetwork?: boolean;
}

/** Cache nhiệm vụ hôm nay để side panel hiển thị mà không gọi lại API. */
export interface ShopDailyTaskPlan {
  /** yyyy-MM-dd theo lịch VN, do backend trả về. */
  date: string;
  /** Seller id đã dùng để lấy danh sách — đổi shop thì cache này không còn đúng. */
  sellerId: string;
  items: ShopDailyTask[];
  fetchedAt: number;
}

export type TikTokShopSyncTypeKey =
  | 'profile'
  | 'payout'
  | 'opportunity'
  | 'analytics'
  | 'health_center'
  | 'shop_score';