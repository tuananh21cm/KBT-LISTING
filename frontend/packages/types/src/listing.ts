export interface ListingVariation {
  id: string;
  fourSellerSkuId?: number | null;
  sellerSku?: string | null;
  title?: string | null;
  mainImage?: string | null;
  salePrice?: number | null;
  /** First-seen unit price (set once on sync; not overwritten when sale price changes) */
  initialPrice?: number | null;
  originalPrice?: number | null;
  globalPrice?: number | null;
  quantity?: number | null;
  barcode?: string | null;
  variantAttributes?: string | null;
  sortOrder?: number;
  isDefault?: boolean;
}

export interface ListingImage {
  mediaType: string;
  url: string;
  listingSkuId?: string | null;
  /** Color axis for Variant Image (from skuImgs); set even when listingSkuId is missing. */
  colorValue?: string | null;
  tikTokImageId?: string | null;
  sortOrder?: number;
}

export interface ListingAttribute {
  attrId: string;
  attrName?: string | null;
  attrValue?: string | null;
  /** TikTok enum value id when known. */
  valueId?: string | null;
  /** Multi-select values when productAttributes.values[] has more than one entry. */
  values?: ListingAttributeValue[] | null;
  valueType?: string | null;
  isRequired?: boolean;
}

export interface ListingAttributeValue {
  valueId?: string | null;
  valueName?: string | null;
}

export interface ListingCertification {
  certType: string;
  externalId?: string | null;
  name?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  fileUrls?: string | null;
}

export interface ListingStatusCounts {
  all: number;
  draft: number;
  published: number;
  active: number;
  inactive: number;
  removed: number;
  incomplete: number;
  suspended: number;
  private: number;
  pending: number;
  reject: number;
  unsaleable: number;
}

export interface ListingAutoPublishSubStep {
  code: string;
  success: boolean;
  skipped: boolean;
  error?: string | null;
  /** Human-readable detail of what the sub-step decided (audit trail). */
  note?: string | null;
}

export interface ListingAutoPublishStep {
  code: string;
  label?: string | null;
  success: boolean;
  skipped: boolean;
  /** True while the step is executing on a running run. */
  inProgress?: boolean;
  error?: string | null;
  durationMs: number;
  subSteps?: ListingAutoPublishSubStep[] | null;
}

export interface ListingAutoPublishRun {
  id: string;
  listingId: string;
  shopId: string;
  status: string;
  startedAt: string;
  finishedAt?: string | null;
  durationMs: number;
  failedStepCode?: string | null;
  errorMessage?: string | null;
  steps: ListingAutoPublishStep[];
}

export interface ListingListItem {
  id: string;
  fourSellerListingId: number;
  platform: string;
  pageType: string;
  siteStatus?: string | null;
  productName?: string | null;
  mainImage?: string | null;
  site?: string | null;
  currency: string;
  lowPrice?: number | null;
  highPrice?: number | null;
  availableStock?: number | null;
  variationCount: number;
  categoryName?: string | null;
  erpStatus?: string | null;
  publishStatus?: string | null;
  tikTokProductId?: string | null;
  platformUpdateTime?: string | null;
  lastSyncedAt: string;
  lastPushAt?: string | null;
  syncError?: string | null;
  ownerId: string;
  ownerName?: string | null;
  shopId?: string | null;
  shopName?: string | null;
  shopProfileName?: string | null;
  sourceUrl?: string | null;
  /**
   * Local-only origin: `recommended` (extension Search) or `manual` (PDP add).
   * Null/omitted = unknown/legacy — no badge.
   */
  addedFrom?: 'recommended' | 'manual' | null;
  /** Shein comment_num (local). */
  reviewCount?: number | null;
  /** Shein comment_rank_average (local). */
  averageRating?: number | null;
  /** Logo shop NCC (`SupplierSellerInfos.StoreLogo`). */
  storeLogo?: string | null;
  /** Tên shop NCC. */
  storeTitle?: string | null;
  /** URL shop NCC. */
  storeUrl?: string | null;
  /** Rank shop NCC (Shein shopRank). */
  shopRank?: number | null;
  /** Số follow shop NCC (Shein storeAttention). */
  storeAttention?: number | null;
  /** SLA ngày giao shop NCC. */
  shippingDays?: number | null;
  /** % đơn giao trong SLA. */
  shippingDayPercent?: number | null;
  /** Tồn kho nguồn (`SupplierProductInfos.Stock`), khác `availableStock` 4Seller. */
  sourceStock?: number | null;
  /** Hàng local US (`SupplierProductInfos.IsLocalProduct`). */
  isLocalProduct?: boolean | null;
  /** Ít nhất một SKU mall_stock hỗ trợ ship nhanh. */
  isSupportQuickShip?: boolean | null;
  /** Mã nền tảng nguồn (SHEIN / TEMU). */
  sellerPlatform?: string | null;
  /** Mã shop trên nền tảng nguồn. */
  storeCode?: string | null;
  /**
   * Có `SupplierProductInfos.SizeChartImageUrl` không rỗng.
   * `false` → thiếu size chart (auto publish không pick / không badge queue).
   */
  hasSizeChart?: boolean;
  /** `auto` | `manual` — nguồn publish lần cuối. */
  publishSource?: 'auto' | 'manual' | null;
  /** Auto publish fail — scheduler bỏ qua cho đến retry / publish thủ công. */
  autoPublishSkipped?: boolean;
  autoPublishSkippedAt?: string | null;
  autoPublishSkipReason?: string | null;
  lastAutoPublishRunId?: string | null;
  /** Draft only: auto publish queue badge. */
  autoPublishQueueStatus?: 'queue' | 'waiting' | 'processing' | 'failed' | null;
  /** Draft waiting: UTC when shop interval ends (countdown badge). */
  autoPublishNextPickAt?: string | null;
  /** Latest auto publish run status (failed/success/…). */
  lastAutoPublishRunStatus?: string | null;
  variations: ListingVariation[];
}

/** Lightweight auto publish queue poll row (GET …/auto-publish-queue-status). */
export type ListingAutoPublishQueueStatusItem = {
  id: string;
  autoPublishQueueStatus?: 'queue' | 'waiting' | 'processing' | 'failed' | null;
  autoPublishNextPickAt?: string | null;
  lastAutoPublishRunStatus?: string | null;
  autoPublishSkipped?: boolean;
  autoPublishSkipReason?: string | null;
  lastAutoPublishRunId?: string | null;
};

export type ListingAutoPublishQueueStatusBatch = {
  items: ListingAutoPublishQueueStatusItem[];
};

/** Normalized Shein size guide from supplier snapshot (local-only). */
export interface SheinSizeGuideMeasureHint {
  name: string;
  description: string;
  sort: number;
}

export interface SheinSizeGuideDimension {
  id: string;
  name: string;
  nameEn: string;
  sort: number;
}

export interface SheinSizeGuideRow {
  size: string;
  sizeId: string;
  usSize: string | null;
  measurements: Record<string, string>;
}

/** Pre-mapped inch columns for size-chart infographic prompt. */
export interface SheinSizeGuideChartRow {
  size: string;
  sizeId: string;
  usSize: string | null;
  bust: string | null;
  waist: string | null;
  hip: string | null;
  length: string | null;
}

export interface SheinSizeGuide {
  goodsId: string;
  /** Primary unit of `rows` measurements (`inch` from sizeInfoInch; legacy `cm`). */
  unit: 'inch' | 'cm';
  dimensions: SheinSizeGuideDimension[];
  rows: SheinSizeGuideRow[];
  /** Ready-to-render SIZE | US | BUST | WAIST | HIP | LENGTH (inch). */
  chartRows: SheinSizeGuideChartRow[];
  productMeasureGuide: SheinSizeGuideMeasureHint[];
  bodyMeasureGuide: SheinSizeGuideMeasureHint[];
  /** Shein manualMeasurementNote (tolerance text). */
  measureNote?: string | null;
  productMeasureImageUrl?: string | null;
  bodyMeasureImageUrl?: string | null;
  capturedAt: string;
}

export interface ListingDetail extends ListingListItem {
  categoryId?: string | null;
  warehouseId?: string | null;
  sourceUrl?: string | null;
  descriptionHtml?: string | null;
  /** 4Seller searchTerms — SEO keywords. */
  searchTerms?: string[] | null;
  /** 4Seller productHighlights — short selling points. */
  productHighlights?: string[] | null;
  packageWeight?: number | null;
  packageLength?: number | null;
  packageWidth?: number | null;
  packageHeight?: number | null;
  weightUnit?: string | null;
  dimensionUnit?: string | null;
  platformCreateTime?: string | null;
  hasRawPayload: boolean;
  brandId?: string | null;
  brandName?: string | null;
  productType?: string | null;
  sizeChartId?: string | null;
  /** Shein size guide from supplier snapshot (local-only, for auto size chart). */
  sizeGuideJson?: SheinSizeGuide | null;
  /** Bunny CDN URL of user-captured size chart from extension. */
  sizeChartCaptureUrl?: string | null;
  /** 4Seller product video material id (erpVideoId). */
  erpVideoId?: string | null;
  safetyWarning?: string | null;
  siteId?: string | null;
  listingSourceUrl?: string | null;
  fourSellerGroupId?: string | null;
  fourSellerGroupName?: string | null;
  appliedRules?: string | null;
  /**
   * Last applied batch pricing formula (local DB jsonb).
   * Restores calculator + preview on edit; preview discount/profit is reference-only
   * (not used for sales / profit reporting — those use salePrice + order costs).
   */
  appliedPricing?: ListingAppliedPricing | null;
  /** Local-only: AI Rewrite đã áp dụng (Publish checklist). */
  descriptionRewritten?: boolean;
  /** Local-only: Insert Images sau Rewrite (tùy chọn, không chặn Publish). */
  descriptionImagesInserted?: boolean;
  refListingId?: string | null;
  refShopId?: string | null;
  images?: ListingImage[];
  attributes?: ListingAttribute[];
  certifications?: ListingCertification[];
}

/** Flat pricing formula snapshot persisted on Listing (matches backend ListingAppliedPricingDto). */
export interface ListingAppliedPricing {
  templateId?: string | null;
  appliedAt?: string | null;
  supplierShipping: number;
  tax: number;
  discountPercent: number;
  fastSalePercent: number;
  adsFee: number;
  tikTokShipping: number;
  platformFeePercent: number;
  targetMode: 'margin' | 'profit';
  marginPercent: number;
  profitAmount: number;
}

export interface ListingListPage {
  items: ListingListItem[];
  total: number;
  pageCurrent: number;
  pageSize: number;
  statusCounts: ListingStatusCounts;
}

/** Một dòng Compass Product analysis đã lưu (ratio là phân số: 0.12 = +12%). */
export interface ListingAnalyticsRow {
  id: string;
  shopId: string;
  shopName: string;
  /** Profile TikTok shop (Shop.ProfileName). */
  shopProfileName?: string | null;
  ownerId: string;
  ownerUserName?: string | null;
  ownerFullName?: string | null;
  /** Listing nội bộ khớp `tikTokProductId`; null khi chưa sync listing. */
  listingId?: string | null;
  tikTokProductId: string;
  productName?: string | null;
  mainImage?: string | null;
  /** Khi đã link listing: `recommended` | `manual`. */
  addedFrom?: 'recommended' | 'manual' | null;
  reviewCount?: number | null;
  averageRating?: number | null;
  storeTitle?: string | null;
  storeUrl?: string | null;
  shopRank?: number | null;
  storeAttention?: number | null;
  shippingDays?: number | null;
  shippingDayPercent?: number | null;
  isLocalProduct?: boolean | null;
  isSupportQuickShip?: boolean | null;
  currency: string;
  /** Ngày VN của snapshot (yyyy-MM-dd). */
  snapshotDate: string;
  rangeStart: string;
  rangeEnd: string;
  preset: string;
  gmv: number;
  orders: number;
  impressions: number;
  clicks: number;
  ctr?: number | null;
  addToCartUnits?: number | null;
  addToCartBuyers?: number | null;
  gmvRatio?: number | null;
  ordersRatio?: number | null;
  impressionsRatio?: number | null;
  clicksRatio?: number | null;
  ctrRatio?: number | null;
  addToCartUnitsRatio?: number | null;
  addToCartBuyersRatio?: number | null;
  /** `tiktok` = do Compass trả, `computed` = backend tính từ snapshot ngày trước. */
  ratioSource?: 'tiktok' | 'computed' | null;
  syncedAt: string;
}

export interface ListingAnalyticsTotals {
  products: number;
  gmv: number;
  orders: number;
  impressions: number;
  clicks: number;
  addToCartUnits: number;
  ctr?: number | null;
}

export interface ListingAnalyticsPage {
  items: ListingAnalyticsRow[];
  total: number;
  pageCurrent: number;
  pageSize: number;
  /** Null khi scope chưa có dữ liệu. */
  snapshotDate?: string | null;
  previousSnapshotDate?: string | null;
  rangeStart?: string | null;
  rangeEnd?: string | null;
  lastSyncedAt?: string | null;
  totals: ListingAnalyticsTotals;
}

export interface ListingAnalyticsParams {
  pageCurrent?: number;
  pageSize?: number;
  /** yyyy-MM-dd; bỏ trống = ngày mới nhất có dữ liệu. */
  date?: string;
  shopId?: string;
  ownerUserId?: string;
  search?: string;
  sort?: 'gmv' | 'orders' | 'impressions' | 'clicks' | 'ctr' | 'atc' | 'name';
  sortDir?: 'asc' | 'desc';
}

export interface ListingAnalyticsDates {
  dates: string[];
  latest?: string | null;
}

/** Điểm từng trụ của công thức hủy list (trọng số 40/25/15/10/10, relief tối đa −20). */
export interface ListingDelistScoreBreakdown {
  sales: number;
  funnel: number;
  reach: number;
  trend: number;
  keyword: number;
  risingRelief: number;
  /** Trọng số thực sự đo được — điểm cuối = phần đạt / phần này (chuẩn hoá về 100). */
  available: number;
}

/** Một listing yếu kèm điểm, lý do và listing nội bộ để hủy. */
export interface ListingDelistItem {
  tikTokProductId: string;
  /** Có giá trị khi listing đã mirror về local → cho phép bấm Hủy list. */
  listingId?: string | null;
  /** Id listing trên 4Seller khi đã mirror. */
  fourSellerListingId?: number | null;
  siteStatus?: string | null;
  productName?: string | null;
  mainImage?: string | null;
  gmv: number;
  orders: number;
  impressions: number;
  clicks: number;
  ctr?: number | null;
  addToCartUnits: number;
  /** % đổi GMV so kỳ Compass trước (fraction, vd. −0.37 = −37%). */
  gmvRatio?: number | null;
  /** % đổi đơn so kỳ trước. */
  ordersRatio?: number | null;
  /** % đổi impressions so kỳ trước. */
  impressionsRatio?: number | null;
  /** Số đơn cao nhất của một kỳ 7 ngày trong cửa sổ theo dõi. */
  peakOrders: number;
  peakGmv: number;
  daysTracked: number;
  /** Số ngày đã live tính đến ngày snapshot (VN); null = chưa biết ngày list. */
  daysListed?: number | null;
  /** Áp lực hủy 0–100 (càng cao càng nên nhả slot). */
  score: number;
  verdict: 'delist' | 'review';
  breakdown: ListingDelistScoreBreakdown;
  /** Tỷ lệ token của keyword khớp nhất tìm thấy trong tên (0–1); null = shop chưa phân tích keyword. */
  keywordFit?: number | null;
  bestKeyword?: string | null;
  reasons: string[];
}

/** Trung vị của shop trong ngày snapshot — thước đo để so từng sản phẩm. */
export interface ListingDelistBenchmarks {
  medianCtr: number;
  medianAtcRate: number;
  medianOrderRate: number;
  medianImpressions: number;
  gmv: number;
  /** Pacific days each Compass snapshot aggregates (`last7days`). */
  windowDays: number;
  /** Snapshot sync rows scanned for peak lookup. */
  trendWindowDays: number;
  /** Minimum snapshot sync rows before judging (each row is already a full window). */
  minTrackedDays: number;
  /** Minimum calendar days live before judging (matches Compass window). */
  minListedDays: number;
}

/** Keyword đang chờ slot, lấy từ «Shop này bán gì» đã lưu. */
export interface ListingDelistKeyword {
  keyword: string;
  searchVolume: number;
  score?: number | null;
  lane: 'listNow' | 'upcoming';
}

export interface ListingDelistSuggestions {
  /** Null khi scope không thu về đúng 1 shop — khi đó `items` rỗng và có warning. */
  shopId?: string | null;
  shopName?: string | null;
  snapshotDate?: string | null;
  rangeStart?: string | null;
  rangeEnd?: string | null;
  currency: string;
  scanned: number;
  skippedWinners: number;
  skippedTooNew: number;
  skippedHealthy: number;
  delistCount: number;
  reviewCount: number;
  publishLimitTotal?: number | null;
  publishLimitPublished?: number | null;
  remainingSlots: number;
  recommendedDelistCount: number;
  headline?: string | null;
  warnings: string[];
  benchmarks: ListingDelistBenchmarks;
  items: ListingDelistItem[];
  keywords: ListingDelistKeyword[];
}

export interface ListingDelistSuggestionParams {
  /** yyyy-MM-dd; bỏ trống = ngày mới nhất có dữ liệu. */
  date?: string;
  shopId?: string;
  ownerUserId?: string;
  /** Số dòng trả về (mặc định 12, tối đa 50). */
  limit?: number;
  /** Điểm tối thiểu (mặc định 50, tối thiểu 30). */
  minScore?: number;
}

export interface ListingListParams {
  pageCurrent?: number;
  pageSize?: number;
  pageType?: string;
  siteStatus?: string;
  ownerUserId?: string;
  platform?: string;
  shopId?: string;
  search?: string;
  /** Inclusive lower bound (ISO); filters PlatformCreateTime ?? PlatformUpdateTime ?? CreatedAt */
  dateFrom?: string;
  /** Inclusive upper bound (ISO) */
  dateTo?: string;
}

export interface ListingSyncRequest {
  ownerUserId?: string;
  platform?: string;
  statuses?: string[];
  shopId?: string;
  /** Inclusive lower bound (ISO) — forwarded to 4Seller createTime / platformCreateTime */
  dateFrom?: string;
  /** Inclusive upper bound (ISO) */
  dateTo?: string;
  /** Cap per status bucket per seller (list Sync uses 200). */
  maxRecordsPerSeller?: number;
  /**
   * `full` = draft content (page + batch-detail);
   * `status` = published status-only (default).
   * Legacy `syncMode`: `light`/`status` → status, `full` → full.
   */
  syncScope?: 'full' | 'status';
  /** @deprecated Prefer `syncScope`. */
  syncMode?: 'light' | 'full' | 'status';
}

export interface ListingSyncSellerFailure {
  ownerUserId: string;
  ownerName?: string | null;
  message: string;
}

export interface ListingSyncResult {
  syncedSellers: number;
  inserted: number;
  updated: number;
  /** In-flight rows updated via listingId reconcile. */
  reconciledUpdated?: number;
  /** Local mirrors removed after Manual reconcile proved gone on 4Seller. */
  reconciledRemoved?: number;
  /** Local Publishing/UnderReview/pending remaining after sync. */
  stillInFlight?: number;
  failedSellers: ListingSyncSellerFailure[];
  /** True when this account's sync lease was busy. */
  skippedBusy?: boolean;
}

export type ListingSyncJobState = 'running' | 'completed' | 'failed';

export type ListingSyncSellerStatus =
  | 'pending'
  | 'running'
  | 'succeeded'
  | 'failed'
  | 'skipped_busy';

export interface ListingSyncSellerProgress {
  ownerUserId: string;
  ownerName?: string | null;
  status: ListingSyncSellerStatus;
  inserted: number;
  updated: number;
  elapsedMs: number;
  message?: string | null;
}

/** Snapshot of a queued sync; returned by POST /sync and by the status poll. */
export interface ListingSyncJobStatus {
  jobId: string;
  state: ListingSyncJobState;
  startedAtUtc: string;
  finishedAtUtc?: string | null;
  totalSellers: number;
  completedSellers: number;
  syncedSellers: number;
  inserted: number;
  updated: number;
  reconciledUpdated?: number;
  reconciledRemoved?: number;
  stillInFlight?: number;
  skippedBusy?: boolean;
  message?: string | null;
  error?: string | null;
  sellers: ListingSyncSellerProgress[];
}

export interface ListingDeleteResult {
  deletedCount: number;
}

export interface ListingDeactivateResult {
  deactivatedCount: number;
  skippedCount: number;
}

export interface ListingPushResult {
  success: boolean;
  message?: string | null;
  listingId: string;
  pageType?: string | null;
  siteStatus?: string | null;
  tikTokProductId?: string | null;
}

export interface UpdateListingVariationPayload {
  id?: string | null;
  fourSellerSkuId?: number | null;
  salePrice?: number | null;
  /** Cost / first-seen unit price (maps to ListingVariation.initialPrice) */
  initialPrice?: number | null;
  originalPrice?: number | null;
  quantity?: number | null;
  sellerSku?: string | null;
  title?: string | null;
  mainImage?: string | null;
  barcode?: string | null;
  variantAttributes?: string | null;
  sortOrder?: number;
  isDefault?: boolean;
}

export interface UpdateListingImagePayload {
  mediaType: string;
  url: string;
  listingSkuId?: string | null;
  tikTokImageId?: string | null;
  sortOrder?: number;
}

export interface UpdateListingAttributePayload {
  attrId: string;
  attrName?: string | null;
  attrValue?: string | null;
  /** TikTok enum value id when known. */
  valueId?: string | null;
  /** Multi-select values (isMultipleSelected=1). */
  values?: ListingAttributeValue[] | null;
  valueType?: string | null;
  isRequired?: boolean;
}

export interface UpdateListingCertificationPayload {
  certType: string;
  externalId?: string | null;
  name?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  fileUrls?: string | null;
}

export interface UpdateListingPayload {
  productName?: string | null;
  descriptionHtml?: string | null;
  /** 4Seller searchTerms — always sent (empty = clear remote placeholders). */
  searchTerms?: string[] | null;
  /** 4Seller productHighlights — always sent (empty = clear remote placeholders). */
  productHighlights?: string[] | null;
  categoryId?: string | null;
  categoryName?: string | null;
  brandId?: string | null;
  brandName?: string | null;
  productType?: string | null;
  warehouseId?: string | null;
  sourceUrl?: string | null;
  listingSourceUrl?: string | null;
  packageWeight?: number | null;
  packageLength?: number | null;
  packageWidth?: number | null;
  packageHeight?: number | null;
  weightUnit?: string | null;
  dimensionUnit?: string | null;
  sizeChartId?: string | null;
  /** 4Seller product video material id (erpVideoId). */
  erpVideoId?: string | null;
  safetyWarning?: string | null;
  siteId?: string | null;
  fourSellerGroupId?: string | null;
  fourSellerGroupName?: string | null;
  appliedRules?: string | null;
  /** Batch pricing formula snapshot — local jsonb; omit to leave unchanged. */
  appliedPricing?: ListingAppliedPricing | null;
  /** Description prep checklist — persisted on Save Draft / Publish / Update. */
  descriptionRewritten?: boolean | null;
  descriptionImagesInserted?: boolean | null;
  refListingId?: string | null;
  refShopId?: string | null;
  /** Local shop Guid — gán shop Publish; backend map → FourSellerShopId + RawPayload.shopId. */
  shopId?: string | null;
  mainImage?: string | null;
  variations?: UpdateListingVariationPayload[];
  images?: UpdateListingImagePayload[];
  attributes?: UpdateListingAttributePayload[];
  certifications?: UpdateListingCertificationPayload[];
}

/** Lightweight local save after Rewrite / Insert Images (no 4Seller push). */
export interface UpdateListingDescriptionPrepPayload {
  descriptionHtml: string;
  descriptionRewritten: boolean;
  descriptionImagesInserted: boolean;
  /** SEO title from the same Rewrite; omit to leave the stored title untouched. */
  productName?: string;
  /** Null/omit = leave column; array (including []) overwrites. */
  searchTerms?: string[] | null;
  /** Null/omit = leave column; array (including []) overwrites. */
  productHighlights?: string[] | null;
}

export interface ListingDescriptionPrepResult {
  listingId: string;
  descriptionHtml?: string | null;
  descriptionRewritten: boolean;
  descriptionImagesInserted: boolean;
  productName?: string | null;
  searchTerms?: string[] | null;
  productHighlights?: string[] | null;
}

/** Lightweight local save of the supplier source URL (no 4Seller push). */
export interface UpdateListingSourceUrlPayload {
  sourceUrl: string;
}

/** Result of a source-url local save — enough to patch the list card. */
export interface ListingSourceUrlResult {
  listingId: string;
  sourceUrl: string;
  reviewCount?: number | null;
  averageRating?: number | null;
  storeLogo?: string | null;
  storeTitle?: string | null;
  storeUrl?: string | null;
  shopRank?: number | null;
  storeAttention?: number | null;
  shippingDays?: number | null;
  shippingDayPercent?: number | null;
  sourceStock?: number | null;
  isLocalProduct?: boolean | null;
  isSupportQuickShip?: boolean | null;
  sellerPlatform?: string | null;
  storeCode?: string | null;
  /** Linked supplier snapshot has SizeChartImageUrl. */
  hasSizeChart?: boolean;
}

/** TikTok brand option from 4Seller (shop-scoped). */
export interface ListingBrandOption {
  brandId: string;
  originalName?: string | null;
  displayName: string;
  status?: string | null;
}

/** TikTok warehouse option from 4Seller (shop-scoped). */
export interface ListingWarehouseOption {
  warehouseId: string;
  warehouseName: string;
  warehouseType?: number | null;
  warehouseSubType?: number | null;
  warehouseEffectStatus?: number | null;
  isDefault: boolean;
  fbt: boolean;
  allocationMode?: string | null;
}

/** Shop-scoped metadata for listing edit (brands + warehouses). */
export interface ListingShopMetadata {
  shopId: string;
  fourSellerShopId: string;
  brands: ListingBrandOption[];
  warehouses: ListingWarehouseOption[];
  defaultWarehouseId?: string | null;
}

/** Result of uploading media to 4Seller COS. */
export interface ListingCosUploadResult {
  url: string;
  key: string;
  domain: string;
  localMaterialId?: number | null;
  md5: string;
  operation?: string | null;
  fileName: string;
}

/** A curated product fact the rewrite prompt may cite (never invented by AI). */
export interface RewriteListingAttribute {
  name?: string;
  value?: string;
}

/** AI rewrite description request (current form values). */
export interface RewriteListingDescriptionRequest {
  title: string;
  description: string;
  /** Grounds keywords in the right niche. */
  categoryName?: string;
  /** Omitted when the listing uses the "No Brand" placeholder. */
  brandName?: string;
  colors?: string[];
  sizes?: string[];
  /** Mapped Specifics (material, fit, season…) so the copy can cite real data. */
  attributes?: RewriteListingAttribute[];
}

/** AI rewrite result — description HTML for HtmlEditor plus the matching SEO title. */
export interface RewriteListingDescriptionResult {
  descriptionHtml: string;
  /** TikTok-safe SEO title; falls back to the cleaned original when AI cannot write one. */
  title?: string;
  /** Backend TikTok search terms (2–3 primary + 2–3 secondary). Omit/empty = keep form values. */
  searchTerms?: string[] | null;
  /** PDP product highlights (up to 5). Omit/empty = keep form values. */
  productHighlights?: string[] | null;
}

/** System Rewrite / Auto Specifics settings for the listing edit page. */
export interface ListingRewriteConfig {
  titleMode: string;
  allowRepeat: boolean;
  /** When true, Auto Specifics stays enabled after Specifics are already filled. */
  allowSpecificsRepeat: boolean;
  /** When true, ListingEditPage shows Grok auto-generate Size Chart button. */
  autoGenerateSizeChart: boolean;
}

/**
 * Request to map description Key:Value pairs onto category Specifics schema.
 * The pairs come from the supplier snapshot the server stored at sync time, not from the
 * editor, so mapping still works after Rewrite has replaced those rows with prose.
 */
export interface MapListingSpecificsRequest {
  categoryId: string;
  categoryName?: string | null;
  shopId?: string | null;
  title?: string | null;
  existingAttributes?: UpdateListingAttributePayload[];
}

export interface MappedListingSpecific {
  sourceName: string;
  sourceValue: string;
  attrId: string;
  attrName: string;
  attrValue: string;
  valueId?: string | null;
  /** Canonical multi-select values when isMultipleSelected=1. */
  values?: ListingAttributeValue[] | null;
  isRequired: boolean;
  confidence: number;
  matchMethod: string;
  skippedExisting: boolean;
}

export interface UnmappedListingSpecific {
  sourceName: string;
  sourceValue: string;
  reason: string;
}

export interface MissingRequiredSpecific {
  attrId: string;
  attrName: string;
}

/** Hybrid Specifics mapping result for admin review. */
export interface MapListingSpecificsResult {
  matched: MappedListingSpecific[];
  unmatched: UnmappedListingSpecific[];
  missingRequired: MissingRequiredSpecific[];
  schemaAttributeCount: number;
  parsedPairCount: number;
}

/** Move-source row from 4Seller batch-get-move-source-listing. */
export interface ListingMoveSource {
  match: boolean;
  matchId?: number | null;
  sourceCategoryId?: string | null;
  sourceCategoryName?: string | null;
  sourceCategoryPath?: string | null;
  sourcePlatform?: string | null;
  targetShopId: number;
  targetShopName?: string | null;
  targetListingId: number;
  listingName?: string | null;
  listingUrl?: string | null;
  targetCategoryId?: string | null;
  targetCategoryName?: string | null;
  targetCategoryPath?: string | null;
  targetCategoryNodePathId?: string | null;
}

/**
 * Recent-use category from 4Seller.
 * `id` is what bind-category sends as recentCategoryId (not TikTok categoryId).
 */
export interface ListingRecentCategory {
  id: number;
  site: string;
  categoryId: string;
  categoryParentId?: string | null;
  categoryName: string;
  nodePath?: string | null;
  nodePathId?: string | null;
  leafCategory: number;
  categoryVersion?: string | null;
}

export interface BindListingCategoryRequest {
  recentCategoryId: number;
  categoryId?: string | null;
  categoryName?: string | null;
  nodePathId?: string | null;
}

export const NO_BRAND_ID = '0';
export const NO_BRAND_NAME = 'No brand';
