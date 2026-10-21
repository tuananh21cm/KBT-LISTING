/** Base shop fields shared by admin detail + seller shop DTO. */
export interface Shop {
  id: string;
  name: string;
  platformId: string;
  platformCode: string;
  platformName: string;
  externalShopId?: string;
  avatarUrl?: string;
  fourSellerShopId?: string;
  fourSellerConnectionStatus?: 'Connected' | 'NotConnected';
  fourSellerConnectedAt?: string;
  region: string;
  currency: string;
  status: string;
  shopUrl?: string;
  metadata?: string;
  proxy?: string;
  profileName?: string;
  email?: string;
  tiktokPassword?: string;
  dayLive?: string;
  /** Hạn thanh toán (DELI, ngày). Sync TTS bill_finish_period_in_days. */
  deli?: number;
  newPhoneNumber?: string;
  oldBankNumber?: string;
  newBankNumber?: string;
  paymentName?: string;
  receivingBankAccountId?: string;
  receivingBankAccountName?: string;
  receivingBankAccountNumber?: string;
  receivingBankAccountHolderName?: string;
  receivingBankSourceName?: string;
  receivingBankSourceAccountName?: string;
  receivingBankAccountCurrency?: string;
  shopType?: string;
  /** Operational TikTok categories (parent or leaf). Distinct from TTS productCategories. */
  categories?: ShopCatalogCategory[];
  /** First operational category id (denormalized). */
  categoryId?: string;
  /** Joined display names for categories. */
  categoryName?: string;
  ageMin?: number | null;
  ageMax?: number | null;
  priceMinUsd?: number | null;
  priceMaxUsd?: number | null;
  preferQuickShip?: boolean;
  preferLocal?: boolean;
  preferUsWarehouse?: boolean;
  preferSheinClub?: boolean;
  /** Minimum rating 0–5. 0 = do not filter. */
  minRating?: number;
  /** Minimum sold count. 0 = do not filter. */
  minSoldCount?: number;
  /** Keep products with fewer variants than this. 0 = do not filter. */
  maxVariantCount?: number;
  bodySizeCode?: string | null;
  styleCodes?: string[];
  autoPublishEnabled?: boolean;
  autoPublishIntervalSeconds?: number | null;
  autoPublishMinDraftAgeMinutes?: number | null;
  /** Aggregate scheduler status from shop list API. */
  autoPublishSchedulerStatus?: 'processing' | 'waiting' | null;
  /** UTC ISO — countdown target for next auto publish pick. */
  autoPublishNextPickAt?: string | null;
  /** Eligible draft count in FIFO (Queue +N display). */
  autoPublishEligibleDraftCount?: number | null;
  llcName?: string;
  twoFactorAuth?: string;
  sortOrder?: number;
  /** TikTok published listing count (publish_limit.published_num). */
  activeListingCount?: number;
  /** TikTok shop code (e.g. USLC4KELN2). */
  shopCode?: string;
  /** TikTok shop_status (ops status stays separate). */
  tikTokShopStatus?: number;
  timezone?: string;
  tikTokCreatedAt?: string;
  verifiedDocuments?: number;
  emailVerified?: boolean;
  phoneVerified?: boolean;
  /** 1 Cross-border / 2 POP·Local */
  sellerType?: number;
  businessType?: number;
  payEntityType?: number;
  companyCertNum?: string;
  companyCertType?: number;
  accountName?: string;
  isBlocked?: boolean;
  isBlocking?: boolean;
  publishLimitPublished?: number;
  publishLimitTotal?: number;
  tikTokProfileSyncedAt?: string;
  /** Health Center AHR score. */
  ahrScore?: number;
  /** Shop Performance Score (0–5). */
  shopPerformanceScore?: number;
  /** SPS — Product satisfaction (0–5). */
  productSatisfactionScore?: number;
  /** SPS — Fulfillment and logistics (0–5). */
  fulfillmentScore?: number;
  /** SPS — Customer service (0–5). */
  customerServiceScore?: number;
  tikTokShopScoreSyncedAt?: string;
  companyAddress?: ShopAddress;
  ownerPerson?: ShopOwnerPerson;
  logisticsServices?: ShopLogisticsService[];
  productCategories?: ShopProductCategory[];
  /** Masked TikTok settlement bank (not receiving-bank newBankNumber). */
  settlementBankAccountNumber?: string;
  settlementPaymentMethod?: string;
  settlementBeneficiaryName?: string;
  settlementAccountType?: string;
  settlementAccountStatus?: number;
  createdAt: string;
  updatedAt?: string;
}

export interface ShopAddress {
  lineOne?: string;
  lineTwo?: string;
  city?: string;
  state?: string;
  country?: string;
  countryCode?: string;
  postCode?: string;
  addressType?: number;
}

export interface ShopOwnerPerson {
  fullName?: string;
  firstName?: string;
  lastName?: string;
  birthday?: string;
  nationality?: string;
  address?: ShopAddress;
  certNum?: string;
  relatePersonType?: number;
}

export interface ShopProductCategory {
  label: string;
  value: string;
}

/** Operational TikTok category assigned to a shop (parent or leaf). */
export interface ShopCatalogCategory {
  id: string;
  name: string;
}

export interface ShopLogisticsService {
  serviceName?: string;
  deliveryOptionName?: string;
  isDefault?: boolean;
  warehouseId?: string;
}

/**
 * Slim list row — admin `GET /admin/shops` (bank last-4 match only, no full account number).
 * Seller `GET /shops` returns fuller rows; owner / bankMatch fields stay optional (admin list only).
 */
export interface ShopListItem {
  id: string;
  name: string;
  platformId: string;
  platformCode: string;
  platformName: string;
  externalShopId?: string;
  avatarUrl?: string;
  fourSellerShopId?: string;
  fourSellerConnectionStatus?: 'Connected' | 'NotConnected';
  /** UTC ISO — set when 4Seller is first linked (admin + seller shop list). */
  fourSellerConnectedAt?: string;
  region: string;
  status: string;
  shopUrl?: string;
  proxy?: string;
  profileName?: string;
  email?: string;
  /** TikTok account password (admin list + seller list; Excel export). */
  tiktokPassword?: string;
  /** TOTP / 2FA secret (admin list + seller list; Excel export). */
  twoFactorAuth?: string;
  /** LLC company name when shopType is Llc. */
  llcName?: string;
  dayLive?: string;
  /** Hạn thanh toán (DELI). Sync TTS bill_finish_period_in_days. */
  deli?: number;
  newPhoneNumber?: string;
  paymentName?: string;
  receivingBankAccountId?: string;
  /** Admin list: last 4 digits of TikTok settlement bank. */
  settlementBankLast4?: string | null;
  /** Admin list: last 4 digits of linked receiving-bank account. */
  receivingBankLast4?: string | null;
  /**
   * Admin list: last-4 match status —
   * Matched | Mismatched | MissingSettlement | MissingReceivingBank.
   */
  bankMatchStatus?: ShopBankMatchStatus | null;
  shopType?: string;
  categories?: ShopCatalogCategory[];
  categoryId?: string;
  categoryName?: string;
  ageMin?: number | null;
  ageMax?: number | null;
  priceMinUsd?: number | null;
  priceMaxUsd?: number | null;
  preferQuickShip?: boolean;
  preferLocal?: boolean;
  preferUsWarehouse?: boolean;
  preferSheinClub?: boolean;
  minRating?: number;
  minSoldCount?: number;
  maxVariantCount?: number;
  bodySizeCode?: string | null;
  styleCodes?: string[];
  sortOrder?: number;
  createdAt: string;
  /** Admin list only. */
  ownerId?: string;
  ownerFullName?: string;
  ownerUserName?: string;
  /** TikTok published listing count (publish_limit.published_num); alias of publishLimitPublished. */
  activeListingCount?: number;
  shopCode?: string;
  publishLimitPublished?: number;
  /** TikTok listing limit (publish_limit.total_limit). */
  publishLimitTotal?: number;
  timezone?: string;
  sellerType?: number;
  tikTokShopStatus?: number;
  /** Health Center AHR score. */
  ahrScore?: number;
  /** Shop Performance Score (0–5). */
  shopPerformanceScore?: number;
  /** SPS — Product satisfaction (0–5). */
  productSatisfactionScore?: number;
  /** SPS — Fulfillment and logistics (0–5). */
  fulfillmentScore?: number;
  /** SPS — Customer service (0–5). */
  customerServiceScore?: number;
  tikTokShopScoreSyncedAt?: string;
  /** Per-shop auto publish toggle. */
  autoPublishEnabled?: boolean;
  /** Seconds between auto publish picks for this shop. */
  autoPublishIntervalSeconds?: number | null;
  /** Minimum draft age in minutes before auto publish pick. */
  autoPublishMinDraftAgeMinutes?: number | null;
  /** Aggregate scheduler status from shop list API. */
  autoPublishSchedulerStatus?: 'processing' | 'waiting' | null;
  /** UTC ISO — countdown target for next auto publish pick. */
  autoPublishNextPickAt?: string | null;
  /** Eligible draft count in FIFO (Queue +N display). */
  autoPublishEligibleDraftCount?: number | null;
  /**
   * Total online seconds today (Vietnam calendar) from ShopOnlineSessions.
   * Open sessions count through UTC now.
   */
  todayOnlineSeconds?: number;
}

/** Lightweight auto publish scheduler poll row (GET …/auto-publish-scheduler-status). */
export type ShopAutoPublishSchedulerStatusItem = {
  id: string;
  autoPublishSchedulerStatus?: 'processing' | 'waiting' | null;
  autoPublishNextPickAt?: string | null;
  autoPublishEligibleDraftCount?: number | null;
};

export type ShopAutoPublishSchedulerStatusBatch = {
  items: ShopAutoPublishSchedulerStatusItem[];
};

/** @deprecated Prefer ShopListItem — kept for existing admin imports. */
export type AdminShopListItem = ShopListItem & {
  ownerId: string;
  ownerFullName: string;
  ownerUserName: string;
};

/** Minimal option from GET /admin/shops/options (cascading filters). */
export interface ShopOption {
  id: string;
  name: string;
  /** Browser profile name — shown in filter dropdowns for easier pick. */
  profileName?: string | null;
  ownerId: string;
  platformCode: string;
  status: string;
  /** Catalog body/size — used to build SHEIN sourcing queries on trending keywords. */
  bodySizeCode?: string | null;
}

/** @deprecated Prefer ShopOption */
export type AdminShopOption = ShopOption;

/** Full shop detail — admin includes owner; seller omits. */
export interface ShopDetail extends Shop {
  ownerId?: string;
  ownerFullName?: string;
  ownerUserName?: string;
  /** Unique shop purchase cost (USD) from the company ledger. Admin detail only. */
  acquisitionCost?: number | null;
}

/** @deprecated Prefer ShopDetail */
export type AdminShop = ShopDetail & {
  ownerId: string;
  ownerFullName: string;
  ownerUserName: string;
};

export interface ShopExtendedFields {
  proxy?: string;
  profileName?: string;
  email?: string;
  tiktokPassword?: string;
  dayLive?: string;
  deli?: number;
  newPhoneNumber?: string;
  oldBankNumber?: string;
  newBankNumber?: string;
  paymentName?: string;
  receivingBankAccountId?: string | null;
  shopType?: string;
  llcName?: string;
  twoFactorAuth?: string;
}

export interface CreateShopRequest extends ShopExtendedFields {
  ownerId: string;
  platformId: string;
  name: string;
  region?: string;
  externalShopId?: string;
  avatarUrl?: string;
  fourSellerShopId?: string;
  shopUrl?: string;
  status: string;
  metadata?: string;
}

/** @deprecated Prefer CreateShopRequest */
export type CreateAdminShopRequest = CreateShopRequest;

/** Admin update — may reassign owner / platform / receiving bank. */
export interface UpdateAdminShopRequest extends ShopExtendedFields {
  ownerId?: string;
  platformId: string;
  name: string;
  region?: string;
  externalShopId?: string;
  avatarUrl?: string;
  fourSellerShopId?: string;
  shopUrl?: string;
  status: string;
  metadata?: string;
}

/** POST /admin/shops/{id}/reassign-owned-data */
export interface ShopOwnershipTransferResult {
  shopId: string;
  fromOwnerId: string;
  toOwnerId: string;
  ordersRematched: number;
  listingsRematched: number;
  ordersTransferred: number;
  listingsTransferred: number;
  analyticsSnapshotsTransferred: number;
  discountActivitiesTransferred: number;
  listingAddOriginsTransferred: number;
  savedKeywordsTransferred: number;
  ordersMerged: number;
  listingsCollapsed?: number;
  shopsProcessed?: number;
  hasChanges: boolean;
}

/** GET /admin/shops/mapping-health */
export interface ShopMappingHealthGroup {
  fourSellerShopId?: number | null;
  orderCount: number;
  ownerId?: string | null;
  ownerName?: string | null;
  firstOrderAt?: string | null;
  lastOrderAt?: string | null;
  hasLocalShop: boolean;
}

export interface ShopMappingHealth {
  unlinkedOrderCount: number;
  groups: ShopMappingHealthGroup[];
}

/** Seller update — no owner/platform/region/receiving-bank reassignment. */
export interface UpdateSellerShopRequest extends ShopExtendedFields {
  name: string;
  externalShopId?: string;
  avatarUrl?: string;
  fourSellerShopId?: string;
  shopUrl?: string;
  status: string;
  metadata?: string;
}

export type UpdateShopRequest = UpdateAdminShopRequest | UpdateSellerShopRequest;

/** PUT /shops/{id}/catalog-profile and PUT /admin/shops/{id}/catalog-profile. */
export interface UpdateShopCatalogProfileRequest {
  categories: ShopCatalogCategory[];
  /** Legacy single id when `categories` is omitted. */
  categoryId?: string;
  categoryName?: string;
  ageMin?: number | null;
  ageMax?: number | null;
  priceMinUsd?: number | null;
  priceMaxUsd?: number | null;
  preferQuickShip: boolean;
  preferLocal: boolean;
  preferUsWarehouse: boolean;
  preferSheinClub?: boolean;
  minRating?: number;
  minSoldCount?: number;
  maxVariantCount?: number;
  bodySizeCode?: string | null;
  /** Fashion style codes (elegant/casual/…). Empty = clear. */
  styleCodes?: string[];
  autoPublishEnabled?: boolean;
  autoPublishIntervalSeconds?: number | null;
  autoPublishMinDraftAgeMinutes?: number | null;
}

/** Catalog fields shared by list/detail/scraper — used to patch caches after PUT. */
export type ShopCatalogFields = {
  categories?: ShopCatalogCategory[];
  categoryId?: string;
  categoryName?: string;
  ageMin?: number | null;
  ageMax?: number | null;
  priceMinUsd?: number | null;
  priceMaxUsd?: number | null;
  preferQuickShip?: boolean;
  preferLocal?: boolean;
  preferUsWarehouse?: boolean;
  preferSheinClub?: boolean;
  minRating?: number;
  minSoldCount?: number;
  maxVariantCount?: number;
  bodySizeCode?: string | null;
  styleCodes?: string[];
  autoPublishEnabled?: boolean;
  autoPublishIntervalSeconds?: number | null;
  autoPublishMinDraftAgeMinutes?: number | null;
};

/** Slim PUT …/catalog-profile response. */
export type ShopCatalogProfile = ShopCatalogFields & { id: string };

/** GET /admin/shops/auto-publish/system-config */
export interface AutoPublishSystemConfig {
  systemAutoPublishEnabled: boolean;
  defaultIntervalSeconds?: number;
  defaultMinDraftAgeMinutes?: number;
}

/** PUT /shops/{id}/auto-publish and PUT /admin/shops/{id}/auto-publish. */
export interface UpdateShopAutoPublishRequest {
  autoPublishEnabled: boolean;
  autoPublishIntervalSeconds?: number | null;
  autoPublishMinDraftAgeMinutes?: number | null;
}

/** Slim PUT …/auto-publish response. */
export interface ShopAutoPublish {
  id: string;
  autoPublishEnabled: boolean;
  autoPublishIntervalSeconds?: number | null;
  autoPublishMinDraftAgeMinutes?: number | null;
}

export const pickShopAutoPublishFields = (
  shop: ShopAutoPublish,
): Pick<
  ShopListItem,
  'autoPublishEnabled' | 'autoPublishIntervalSeconds' | 'autoPublishMinDraftAgeMinutes'
> => ({
  autoPublishEnabled: !!shop.autoPublishEnabled,
  autoPublishIntervalSeconds: shop.autoPublishIntervalSeconds ?? null,
  autoPublishMinDraftAgeMinutes: shop.autoPublishMinDraftAgeMinutes ?? null,
});

/** GET /age-ranges and GET /admin/age-ranges — Select presets + marketing copy. */
export interface AgeRange {
  id: string;
  code: string;
  label: string;
  ageMin: number;
  ageMax: number;
  painPoint: string;
  desire: string;
  productGroups: string;
  sortOrder: number;
}

/** Match shop AgeMin/AgeMax to a seeded preset (exact bounds). */
export const findAgeRangeByBounds = (
  ageMin: number | null | undefined,
  ageMax: number | null | undefined,
  ranges: readonly AgeRange[],
): AgeRange | undefined => {
  if (ageMin == null || ageMax == null) return undefined;
  return ranges.find((r) => r.ageMin === ageMin && r.ageMax === ageMax);
};

/** Format age for display: preset label when matched, else min–max. */
export const formatShopAgeLabel = (
  ageMin: number | null | undefined,
  ageMax: number | null | undefined,
  ranges: readonly AgeRange[] = [],
): string => {
  if (ageMin == null && ageMax == null) return '—';
  const match = findAgeRangeByBounds(ageMin, ageMax, ranges);
  if (match) return match.label;
  return `${ageMin ?? '—'}–${ageMax ?? '—'}`;
};

/** Hardcoded body/size Select options (catalog profile). */
export interface BodySizeOption {
  code: string;
  label: string;
  meaning: string;
}

export const BODY_SIZE_OPTIONS: readonly BodySizeOption[] = [
  {
    code: 'regular',
    label: 'Regular / Straight Size',
    meaning: 'Thân hình và size thông thường',
  },
  {
    code: 'plus',
    label: 'Plus Size',
    meaning: 'Size lớn, thường 1X+ / 14W+ tùy brand',
  },
  {
    code: 'midsize',
    label: 'Midsize',
    meaning: 'Nằm giữa straight và plus',
  },
  {
    code: 'curvy',
    label: 'Curvy',
    meaning: 'Ngực/hông rõ, eo nhỏ hơn; có thể thuộc regular hoặc plus',
  },
  {
    code: 'petite',
    label: 'Petite',
    meaning: 'Người thấp, cần chiều dài/fit riêng',
  },
  {
    code: 'tall',
    label: 'Tall',
    meaning: 'Người cao, cần inseam/length dài hơn',
  },
] as const;

export const findBodySizeOption = (
  code: string | null | undefined,
): BodySizeOption | undefined => {
  if (!code) return undefined;
  return BODY_SIZE_OPTIONS.find((o) => o.code === code);
};

/** Short label for list chips / tags. */
export const formatBodySizeLabel = (code: string | null | undefined): string => {
  const opt = findBodySizeOption(code);
  if (!opt) return code?.trim() || '—';
  if (opt.code === 'regular') return 'Regular';
  return opt.label;
};

/** Hardcoded fashion-style Select options (catalog profile, multi-select). */
export interface StyleOption {
  code: string;
  label: string;
  meaning: string;
  examples: string;
}

export const STYLE_OPTIONS: readonly StyleOption[] = [
  {
    code: 'elegant',
    label: 'Elegant',
    meaning: 'Thanh lịch, tinh tế, trưởng thành. Form gọn, màu tương đối nhã, ít chi tiết quá nổi bật.',
    examples: 'Midi dress, blouse, tailored pants, wrap dress',
  },
  {
    code: 'casual',
    label: 'Casual',
    meaning: 'Đời thường, dễ mặc, thoải mái. Đây là nhóm rất rộng và phù hợp mặc hằng ngày.',
    examples: 'T-shirt, casual dress, jeans, loose blouse, shorts',
  },
  {
    code: 'vintage',
    label: 'Vintage',
    meaning:
      'Phong cách lấy cảm hứng từ thời trang các thập niên trước. Thường có floral, retro print, puff sleeve, silhouette cổ điển.',
    examples: 'Floral dress, retro blouse, A-line dress',
  },
  {
    code: 'sexy',
    label: 'Sexy',
    meaning: 'Thiết kế nhấn vào vẻ quyến rũ, thường ôm dáng hoặc có cut-out, cổ sâu, hở vai/lưng.',
    examples: 'Bodycon dress, fitted top, cut-out dress',
  },
  {
    code: 'boho',
    label: 'Boho',
    meaning:
      'Bohemian: phóng khoáng, tự do, thiên về vacation/resort/festival. Hay có floral, crochet, embroidery, flowy silhouette.',
    examples: 'Maxi dress, peasant blouse, flowy skirt',
  },
  {
    code: 'cute',
    label: 'Cute',
    meaning: 'Trẻ trung, đáng yêu, nữ tính. Thường có màu sáng/pastel, nơ, hoa nhỏ, ruffle hoặc form trẻ.',
    examples: 'Mini dress, bow top, ruffle blouse',
  },
  {
    code: 'glamorous',
    label: 'Glamorous',
    meaning: 'Lộng lẫy, nổi bật — Shein facet `Glamorous`. Hay sequin, metallic, cut-out, form ôm dự tiệc.',
    examples: 'Sequin dress, metallic top, glam mini',
  },
  {
    code: 'vacation',
    label: 'Vacation',
    meaning: 'Đồ đi biển / resort / du lịch — Shein facet `Vacation`. Form thoáng, họa tiết mùa hè, dễ mặc nắng.',
    examples: 'Resort dress, cover-up, tropical set',
  },
  {
    code: 'modest',
    label: 'Modest',
    meaning: 'Kín đáo nhưng vẫn thời trang. Ít hở da, thường tay dài/tay lỡ, cổ cao hơn và chiều dài váy lớn hơn.',
    examples: 'Maxi dress, long-sleeve dress, loose blouse',
  },
  {
    code: 'party',
    label: 'Party',
    meaning: 'Trang phục dành cho tiệc, club, sinh nhật, sự kiện. Thiết kế nổi bật hơn đồ everyday.',
    examples: 'Sequin dress, cocktail dress, party top',
  },
] as const;

export const findStyleOption = (code: string | null | undefined): StyleOption | undefined => {
  if (!code) return undefined;
  return STYLE_OPTIONS.find((o) => o.code === code);
};

export const formatStyleLabel = (code: string | null | undefined): string => {
  const opt = findStyleOption(code);
  return opt?.label || code?.trim() || '—';
};

/** Keep known style codes, drop unknown, preserve order, unique. */
export const normalizeStyleCodes = (codes: string[] | null | undefined): string[] => {
  if (!Array.isArray(codes) || codes.length === 0) return [];
  const seen = new Set<string>();
  const result: string[] = [];
  for (const raw of codes) {
    const code = String(raw ?? '').trim().toLowerCase();
    if (!code || seen.has(code)) continue;
    if (!STYLE_OPTIONS.some((o) => o.code === code)) continue;
    seen.add(code);
    result.push(code);
  }
  return result;
};

export const pickShopCatalogFields = (shop: ShopCatalogFields): ShopCatalogFields => ({
  categories: shop.categories ?? [],
  categoryId: shop.categoryId,
  categoryName: shop.categoryName,
  ageMin: shop.ageMin ?? null,
  ageMax: shop.ageMax ?? null,
  priceMinUsd: shop.priceMinUsd ?? null,
  priceMaxUsd: shop.priceMaxUsd ?? null,
  preferQuickShip: !!shop.preferQuickShip,
  preferLocal: !!shop.preferLocal,
  preferUsWarehouse: !!shop.preferUsWarehouse,
  preferSheinClub: !!shop.preferSheinClub,
  minRating: Number(shop.minRating) || 0,
  minSoldCount: Number(shop.minSoldCount) || 0,
  maxVariantCount: Number(shop.maxVariantCount) || 0,
  bodySizeCode: shop.bodySizeCode ?? null,
  styleCodes: normalizeStyleCodes(shop.styleCodes),
  autoPublishEnabled: !!shop.autoPublishEnabled,
  autoPublishIntervalSeconds: shop.autoPublishIntervalSeconds ?? null,
  autoPublishMinDraftAgeMinutes: shop.autoPublishMinDraftAgeMinutes ?? null,
});

/** Count operational catalog categories (prefer `categories[]`, fall back to legacy `categoryId`). */
export const shopCatalogCategoryCount = (
  shop: Pick<ShopCatalogFields, 'categories' | 'categoryId'>,
): number => {
  if (Array.isArray(shop.categories) && shop.categories.length > 0) {
    return shop.categories.filter((c) => String(c.id ?? '').trim()).length;
  }
  return shop.categoryId?.trim() ? 1 : 0;
};

/**
 * FE-derived “audience profile complete”: ≥1 category + age bounds + body size + price band.
 * QS / Local / US Warehouse flags are optional. No DB column — flips automatically when fields clear.
 */
export const isShopCatalogAudienceComplete = (shop: ShopCatalogFields): boolean =>
  shopCatalogCategoryCount(shop) >= 1 &&
  shop.ageMin != null &&
  shop.ageMax != null &&
  !!shop.bodySizeCode?.trim() &&
  shop.priceMinUsd != null &&
  shop.priceMaxUsd != null;

export interface ReorderShopsRequest {
  orderedIds: string[];
}

/** @deprecated Prefer ReorderShopsRequest */
export type ReorderAdminShopsRequest = ReorderShopsRequest;

/** Body for PUT /shops/tiktok-profile (extension sync). */
export interface SyncTikTokShopProfileRequest {
  sellerId: string;
  shopName?: string;
  shopCode?: string;
  region?: string;
  companyName?: string;
  logoUrl?: string;
  shopStatus?: number;
  /** TikTok `seller.status` (account): 2 active, 3 deactivated. */
  sellerStatus?: number;
  /**
   * Shop-level 365-day lock from products/list
   * (`post_onboarding_identity_video_verification_reason`).
   * `true` → ops Suspended; false/omit does not unsuspend.
   */
  identityVideoVerificationLock?: boolean;
  timezone?: string;
  createTimeMs?: number | null;
  phoneVerified?: boolean | null;
  emailVerified?: boolean | null;
  verifiedDocuments?: number | null;
  sellerType?: number | null;
  businessType?: number | null;
  shopType?: 'Per' | 'Llc' | null;
  productCategories?: ShopProductCategory[];
  logisticsServices?: ShopLogisticsService[];
  publishLimit?: { totalLimit?: number; publishedNum?: number } | null;
  publishLimitPublished?: number | null;
  publishLimitTotal?: number | null;
  companyAddress?: ShopAddress | null;
  companyCertNum?: string | null;
  companyCertType?: number | null;
  payEntityType?: number | null;
  owner?: ShopOwnerPerson | null;
  accountEmail?: string | null;
  accountMobile?: string | null;
  accountName?: string | null;
  isBlocked?: boolean | null;
  isBlocking?: boolean | null;
  bankAccountNumber?: string | null;
  settlementBankAccountNumber?: string | null;
  settlementPaymentMethod?: string | null;
  settlementBeneficiaryName?: string | null;
  settlementAccountType?: string | null;
  settlementAccountStatus?: number | null;
}

export const SHOP_STATUSES = ['Active', 'Deactivated', 'Suspended'] as const;

/** Nhãn hiển thị ops `Shop.Status` — tiếng Anh, khớp thuật ngữ TikTok Seller Center. */
export const SHOP_STATUS_LABELS: Record<string, string> = {
  Active: 'Active',
  Deactivated: 'Deactivated',
  Suspended: 'Suspended',
};

/** Màu Tag antd theo ops `Shop.Status` — ba hue tách bạch để liếc bảng là phân biệt được. */
export const SHOP_STATUS_COLORS: Record<string, string> = {
  Active: 'green',
  Deactivated: 'orange',
  Suspended: 'red',
};

/** Last-4 match between TikTok settlement bank and receiving-bank card (admin list). */
export const SHOP_BANK_MATCH_STATUSES = [
  'Matched',
  'Mismatched',
  'MissingSettlement',
  'MissingReceivingBank',
] as const;

export type ShopBankMatchStatus = (typeof SHOP_BANK_MATCH_STATUSES)[number];

export const SHOP_BANK_MATCH_LABELS: Record<ShopBankMatchStatus, string> = {
  Matched: 'Khớp',
  Mismatched: 'Lệch',
  MissingSettlement: 'Chưa sync TTS',
  MissingReceivingBank: 'Chưa gắn thẻ',
};

export const SHOP_BANK_MATCH_COLORS: Record<ShopBankMatchStatus, string> = {
  Matched: 'green',
  Mismatched: 'red',
  MissingSettlement: 'default',
  MissingReceivingBank: 'orange',
};

/** Màu Tag antd theo TikTok `shop_status` numeric, khớp hue với ops status tương ứng. */
export const TIKTOK_SHOP_STATUS_COLORS: Record<number, string> = {
  1: 'blue',
  2: 'green',
  3: 'orange',
  4: 'red',
};

/** Per = Shop cá nhân, Llc = Shop công ty */
export const SHOP_TYPES = [
  { label: 'PER', value: 'Per' },
  { label: 'LLC', value: 'Llc' },
] as const;

export const SHOP_TYPE_LABELS: Record<string, string> = {
  Per: 'PER',
  Llc: 'LLC',
};

export const SHOP_REGIONS = [
  { label: 'US - United States', value: 'US' },
  { label: 'UK - United Kingdom', value: 'UK' },
  { label: 'ID - Indonesia', value: 'ID' },
  { label: 'MY - Malaysia', value: 'MY' },
  { label: 'PH - Philippines', value: 'PH' },
  { label: 'SG - Singapore', value: 'SG' },
  { label: 'TH - Thailand', value: 'TH' },
  { label: 'VN - Vietnam', value: 'VN' },
] as const;

export const TIKTOK_SHOP_STATUS_LABELS: Record<number, string> = {
  1: 'Activating',
  2: 'Active',
  3: 'Deactivated',
  4: 'Suspended',
};

export const TIKTOK_SELLER_TYPE_LABELS: Record<number, string> = {
  1: 'Cross-border',
  2: 'POP / Local',
};
