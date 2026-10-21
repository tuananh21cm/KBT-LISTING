import type {
  OnboardConfigResponse,
  PassportAccountInfoResponse,
  PayOnboardInfoResponse,
  ProductPreloadResponse,
  SellerCommonGetResponse,
  TikTokCaptureHeaderItem,
  TikTokCaptureState,
  TikTokLogisticsService,
  TikTokPassportAccountFields,
  TikTokPayOnboardFields,
  TikTokProductCategory,
  TikTokPublishLimit,
  TikTokSettlementAccountFields,
  TikTokStatementStatFields,
  TikTokShopAddress,
  TikTokShopEntityType,
  TikTokShopInfo,
  TikTokShopRelatePerson,
  SettlementAccountResponse,
  StatementStatInfoResponse,
} from './types';

/** Header lõi hay gặp trên seller-us (checklist UI — bổ sung khi quan sát thật). */
export const TTS_CORE_HEADER_NAMES = [
  'authorization',
  'x-tt-token',
  'x-secsdk-csrf-token',
  'x-bogus',
  'x-gorgon',
] as const;

/** Giữ đồng bộ với `TIKTOK_SHOP_STATUS_LABELS` (`frontend/packages/types/src/shop.ts`). */
const SHOP_STATUS_LABEL: Record<number, string> = {
  1: 'Activating',
  2: 'Active',
  3: 'Deactivated',
  4: 'Suspended',
};

/**
 * Map `seller.seller_type` (Seller Center số) → nhãn.
 * Open Partner API: LOCAL | CROSS_BORDER.
 * seller-us: type=2 + cookie `gs_seller_type_for_report=pop` → POP/Local.
 */
const SELLER_TYPE_LABEL: Record<number, string> = {
  1: 'Cross-border',
  2: 'POP / Local',
};

/**
 * Map `seller.business_type` theo onboard `biz_type_US`:
 * 0 individual → PER, 2 solepro → PER, 3 partner → LLC, 4 corp → LLC.
 * Giữ 1 → PER (fallback cũ).
 */
const BUSINESS_TYPE_TO_SHOP_TYPE: Record<number, TikTokShopEntityType> = {
  0: 'Per',
  1: 'Per',
  2: 'Per',
  3: 'Llc',
  4: 'Llc',
};

const BUSINESS_TYPE_LABEL: Record<number, string> = {
  0: 'Individual',
  1: 'Individual',
  2: 'Sole proprietorship',
  3: 'Partnership',
  4: 'Corporation',
};

const COMPANY_ENTITY_RE =
  /\b(llc|l\.l\.c\.?|inc\.?|corp\.?|corporation|ltd\.?|limited|co\.|company|llp|pllc)\b/i;

export function formatShopStatus(status?: number | null): string {
  if (status == null || !Number.isFinite(status)) return '—';
  return SHOP_STATUS_LABEL[status] ?? `Status ${status}`;
}

/**
 * Chọn mã TTS xấu hơn giữa `shop_status` và `seller.status` (4 > 3 > 2 > 1).
 * Tránh hiện Active khi shop đã Deactivated mà account vẫn Active.
 */
export function resolveDisplayShopStatus(
  shopStatus?: number | null,
  sellerStatus?: number | null,
): number | null {
  const codes = [shopStatus, sellerStatus].filter(
    (n): n is number => n != null && Number.isFinite(n),
  );
  if (codes.length === 0) return null;
  return Math.max(...codes);
}

/** Class badge popup theo mã TTS shop/account status. */
export function shopStatusBadgeClass(status?: number | null): string {
  if (status === 2) return 'badge badge-ok';
  if (status === 3 || status === 1) return 'badge badge-warn';
  if (status === 4) return 'badge badge-err';
  return 'meta muted';
}

export function formatSellerType(sellerType?: number | null): string {
  if (sellerType == null || !Number.isFinite(Number(sellerType))) return '—';
  const n = Number(sellerType);
  const label = SELLER_TYPE_LABEL[n];
  return label ? `${label} (${n})` : `Type ${n}`;
}

export function formatShopEntityType(shopType?: TikTokShopEntityType | null): string {
  if (shopType === 'Per') return 'PER';
  if (shopType === 'Llc') return 'LLC';
  return '—';
}

export function formatBusinessTypeLabel(businessType?: number | null): string | null {
  if (businessType == null || !Number.isFinite(Number(businessType))) return null;
  const n = Number(businessType);
  return BUSINESS_TYPE_LABEL[n] ?? null;
}

/**
 * Suy luận PER/LLC từ seller-us (khác `seller_type` POP/Local vs Cross-border).
 *
 * Ưu tiên:
 * 1) Có `company_name` (kể cả DBA kiểu «Carrie's Closet») → LLC
 *    — shop Individual trên Seller Center US thường để trống company_name
 * 2) Không có company_name → theo `business_type` (biz_type_US: 0/2→PER, 3/4→LLC)
 * 3) Không tín hiệu → PER
 */
export function inferShopEntityType(input: {
  businessType?: number | null;
  companyName?: string | null;
}): { shopType: TikTokShopEntityType | null; reason: string } {
  const companyName = (input.companyName || '').trim();
  const biz =
    input.businessType != null && Number.isFinite(Number(input.businessType))
      ? Number(input.businessType)
      : null;

  const fromBiz = biz != null ? BUSINESS_TYPE_TO_SHOP_TYPE[biz] : undefined;
  const bizLabel = biz != null ? BUSINESS_TYPE_LABEL[biz] : undefined;
  const hasCompanyKeyword = companyName.length > 0 && COMPANY_ENTITY_RE.test(companyName);
  const hasCompanyName = companyName.length > 0;

  // company_name là tín hiệu pháp lý mạnh hơn business_type (solepro/individual vẫn có thể điền DBA).
  if (hasCompanyName) {
    const bits = [
      hasCompanyKeyword
        ? `company_name «${companyName}» (từ khóa pháp nhân) → LLC`
        : `có company_name «${companyName}» → LLC`,
    ];
    if (fromBiz === 'Per') {
      bits.push(
        `ghi đè business_type=${biz}${bizLabel ? ` (${bizLabel})` : ''} vốn map PER`,
      );
    } else if (biz != null) {
      bits.push(`business_type=${biz}${bizLabel ? ` (${bizLabel})` : ''}`);
    }
    return { shopType: 'Llc', reason: bits.join('; ') };
  }

  if (fromBiz) {
    return {
      shopType: fromBiz,
      reason: `không có company_name; business_type=${biz}${
        bizLabel ? ` (${bizLabel})` : ''
      } → ${fromBiz === 'Per' ? 'PER' : 'LLC'}`,
    };
  }

  if (biz != null) {
    return {
      shopType: null,
      reason: `business_type=${biz} chưa map; không có company_name`,
    };
  }

  return {
    shopType: 'Per',
    reason: 'không có company_name + không có business_type → PER (cá nhân)',
  };
}

/** Parse `customized_config.product_categories` từ onboard config. */
export function parseProductCategories(
  raw: OnboardConfigResponse | null | undefined,
): TikTokProductCategory[] | null {
  if (!raw || Number(raw.code) !== 0) return null;
  const list = raw.data?.customized_config?.product_categories;
  if (!Array.isArray(list) || list.length === 0) return null;
  const out: TikTokProductCategory[] = [];
  for (const item of list) {
    if (!item) continue;
    const label = String(item.label || '').trim();
    const value = String(item.value ?? '').trim();
    if (!label && !value) continue;
    out.push({ label: label || value, value: value || label });
  }
  return out.length ? out : null;
}

/** Gắn product categories vào shop (giữ cũ nếu lần này thiếu). */
export function mergeProductCategoriesIntoShop(
  shop: TikTokShopInfo,
  categories: TikTokProductCategory[] | null | undefined,
): TikTokShopInfo {
  if (!categories || categories.length === 0) {
    return {
      ...shop,
      productCategories: shop.productCategories?.length ? shop.productCategories : undefined,
    };
  }
  return { ...shop, productCategories: categories };
}

function parseLimitNumber(value: unknown): number | null {
  if (value == null || value === '') return null;
  const n = typeof value === 'number' ? value : Number(String(value).replace(/,/g, '').trim());
  if (!Number.isFinite(n) || n < 0) return null;
  return Math.floor(n);
}

export interface ProductPreloadParseResult {
  logisticsServices: TikTokLogisticsService[] | null;
  publishLimit: TikTokPublishLimit | null;
  /** Vì sao thiếu publishLimit — ghép vào message sync thay vì phải mở devtools. */
  publishLimitReason: string | null;
}

/** Parse logistics + publish_limit từ product_creation/preload. */
export function parseProductPreload(
  raw: ProductPreloadResponse | null | undefined,
): ProductPreloadParseResult {
  if (!raw) {
    return {
      logisticsServices: null,
      publishLimit: null,
      publishLimitReason: 'preload không trả JSON',
    };
  }

  // Có version preload trả thẳng `{ data }` không kèm `code` — chỉ coi là lỗi khi code có mà ≠ 0.
  if (raw.code != null && Number(raw.code) !== 0) {
    return {
      logisticsServices: null,
      publishLimit: null,
      publishLimitReason: `preload code=${raw.code}${raw.message ? ` (${raw.message})` : ''}`,
    };
  }

  if (!raw.data) {
    return {
      logisticsServices: null,
      publishLimit: null,
      publishLimitReason: 'preload thiếu data',
    };
  }

  const seen = new Set<string>();
  const logistics: TikTokLogisticsService[] = [];
  for (const wh of raw.data.warehouse_logistics_services || []) {
    for (const svc of wh.logistics_services || []) {
      const serviceName = String(svc.service_name || '').trim();
      const deliveryOptionName = String(svc.delivery_option_name || '').trim();
      if (!serviceName && !deliveryOptionName) continue;
      const key = `${serviceName}|${deliveryOptionName}`;
      if (seen.has(key)) continue;
      seen.add(key);
      logistics.push({
        serviceName: serviceName || '—',
        deliveryOptionName: deliveryOptionName || '—',
        isDefault: !!svc.is_default,
        warehouseId: wh.warehouse_id ? String(wh.warehouse_id) : undefined,
      });
    }
  }

  const logisticsServices = logistics.length ? logistics : null;
  const data = raw.data as Record<string, unknown>;
  const pl = (raw.data.publish_limit ?? data.publishLimit) as
    | { total_limit?: unknown; totalLimit?: unknown; published_num?: unknown; publishedNum?: unknown }
    | null
    | undefined;

  if (pl == null) {
    const keys = Object.keys(data).slice(0, 12).join(', ');
    return {
      logisticsServices,
      publishLimit: null,
      publishLimitReason: `preload không có publish_limit${keys ? ` (key data: ${keys})` : ''}`,
    };
  }

  const totalRaw = pl.total_limit ?? pl.totalLimit;
  const totalLimit = parseLimitNumber(totalRaw);
  if (totalLimit == null) {
    return {
      logisticsServices,
      publishLimit: null,
      publishLimitReason: `publish_limit.total_limit=${JSON.stringify(totalRaw) ?? 'undefined'} không đọc được`,
    };
  }

  const publishedNum = parseLimitNumber(pl.published_num ?? pl.publishedNum);

  return {
    logisticsServices,
    publishLimit: { totalLimit, publishedNum: publishedNum ?? 0 },
    publishLimitReason: null,
  };
}

/** Gắn logistics + publish limit (giữ cũ nếu lần này thiếu). */
export function mergeProductPreloadIntoShop(
  shop: TikTokShopInfo,
  preload: {
    logisticsServices?: TikTokLogisticsService[] | null;
    publishLimit?: TikTokPublishLimit | null;
    /** Chặng lấy được `publishLimit` lần này; chỉ ghi khi có số mới. */
    publishLimitSource?: string | null;
  } | null | undefined,
): TikTokShopInfo {
  if (!preload) return shop;
  const hasNewLimit = preload.publishLimit != null;
  return {
    ...shop,
    logisticsServices:
      preload.logisticsServices && preload.logisticsServices.length > 0
        ? preload.logisticsServices
        : shop.logisticsServices,
    publishLimit: hasNewLimit ? preload.publishLimit! : shop.publishLimit ?? null,
    publishLimitSource: hasNewLimit
      ? preload.publishLimitSource ?? null
      : shop.publishLimitSource ?? null,
  };
}

export function formatPublishLimit(limit?: TikTokPublishLimit | null): string {
  if (!limit) return '—';
  return `${limit.publishedNum} / ${limit.totalLimit}`;
}

export function formatUsdAmount(value?: number | null): string {
  if (value == null || !Number.isFinite(value)) return '—';
  return value.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
}

export function formatLogisticsService(svc: TikTokLogisticsService): string {
  if (svc.serviceName && svc.deliveryOptionName) {
    return `${svc.serviceName} · ${svc.deliveryOptionName}`;
  }
  return svc.serviceName || svc.deliveryOptionName || '—';
}

/**
 * Epoch ms → `HH:mm dd/MM/yyyy (N days)` (Asia/Ho_Chi_Minh); thiếu → null.
 * N days = số ngày đã tồn tại (làm tròn xuống).
 */
export function formatCreateTimeVn(
  createTimeMs?: number | string | null,
  nowMs = Date.now(),
): string | null {
  if (createTimeMs == null || createTimeMs === '') return null;
  const ms = typeof createTimeMs === 'string' ? Number(createTimeMs) : createTimeMs;
  if (!Number.isFinite(ms) || ms <= 0) return null;
  const d = new Date(ms);
  if (Number.isNaN(d.getTime())) return null;

  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Asia/Ho_Chi_Minh',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).formatToParts(d);

  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((p) => p.type === type)?.value ?? '';
  const stamped = `${get('hour')}:${get('minute')} ${get('day')}/${get('month')}/${get('year')}`;

  const days = Math.max(0, Math.floor((nowMs - ms) / 86_400_000));
  return `${stamped} (${days} days)`;
}

export function formatVerifiedBool(value?: boolean | null): string {
  if (value == null) return '—';
  return value ? 'Đã xác minh' : 'Chưa xác minh';
}

/** 0/1 hoặc boolean → boolean; thiếu → null. */
export function coercePassportFlag(value: unknown): boolean | null {
  if (value == null) return null;
  if (typeof value === 'boolean') return value;
  if (typeof value === 'number' && Number.isFinite(value)) return value !== 0;
  if (typeof value === 'string' && value.trim() !== '') {
    const n = Number(value);
    if (Number.isFinite(n)) return n !== 0;
    const low = value.trim().toLowerCase();
    if (low === 'true') return true;
    if (low === 'false') return false;
  }
  return null;
}

export function formatBlockedFlag(value?: boolean | null): string {
  if (value == null) return '—';
  return value ? 'Có' : 'Không';
}

/** Parse body passport/account/info/v2 → field gắn shop; null nếu không success. */
export function parsePassportAccountInfo(
  raw: PassportAccountInfoResponse | null | undefined,
): TikTokPassportAccountFields | null {
  if (!raw?.data) return null;
  const msg = (raw.message || '').toLowerCase();
  if (msg && msg !== 'success') return null;
  const data = raw.data;
  const name = (data.name || data.screen_name || '').trim() || null;
  const email = (data.email || '').trim() || null;
  const mobile = (data.mobile || '').trim() || null;
  if (!name && !email && !mobile) {
    // Vẫn lấy flag nếu có.
    const isBlocked = coercePassportFlag(data.is_blocked);
    const isBlocking = coercePassportFlag(data.is_blocking);
    if (isBlocked == null && isBlocking == null) return null;
    return { accountName: null, accountEmail: null, accountMobile: null, isBlocked, isBlocking };
  }
  return {
    accountName: name,
    accountEmail: email,
    accountMobile: mobile,
    isBlocked: coercePassportFlag(data.is_blocked),
    isBlocking: coercePassportFlag(data.is_blocking),
  };
}

/** Gắn / ghi đè field passport lên shop (giữ field cũ nếu passport thiếu). */
export function mergePassportIntoShop(
  shop: TikTokShopInfo,
  passport: TikTokPassportAccountFields | null | undefined,
): TikTokShopInfo {
  if (!passport) return shop;
  return {
    ...shop,
    accountName: passport.accountName ?? shop.accountName ?? null,
    accountEmail: preferUnmaskedContact(passport.accountEmail, shop.accountEmail),
    accountMobile: preferUnmaskedContact(passport.accountMobile, shop.accountMobile),
    isBlocked: passport.isBlocked ?? shop.isBlocked ?? null,
    isBlocking: passport.isBlocking ?? shop.isBlocking ?? null,
  };
}

/** Contact có dấu `*` (passport mask) → coi là nhiễu. */
export function isMaskedContact(value?: string | null): boolean {
  return !!value && value.includes('*');
}

/** Ưu tiên giá trị không mask; nếu cả hai mask/thiếu thì lấy `next` rồi `prev`. */
export function preferUnmaskedContact(
  next?: string | null,
  prev?: string | null,
): string | null {
  const a = (next || '').trim() || null;
  const b = (prev || '').trim() || null;
  if (a && !isMaskedContact(a)) return a;
  if (b && !isMaskedContact(b)) return b;
  return a ?? b;
}

function parsePayAddress(
  raw:
    | {
        post_code?: string;
        name_l0?: string;
        name_l1?: string;
        name_l2?: string;
        line_one?: string;
        line_two?: string;
        address_type?: number;
        country_code?: string;
      }
    | null
    | undefined,
): TikTokShopAddress | null {
  if (!raw) return null;
  const lineOne = (raw.line_one || '').trim() || undefined;
  const lineTwo = (raw.line_two || '').trim() || undefined;
  const city = (raw.name_l2 || '').trim() || undefined;
  const state = (raw.name_l1 || '').trim() || undefined;
  const country = (raw.name_l0 || '').trim() || undefined;
  const countryCode = (raw.country_code || '').trim() || undefined;
  const postCode = (raw.post_code || '').trim() || undefined;
  if (!lineOne && !lineTwo && !city && !state && !postCode && !countryCode) return null;
  return {
    lineOne,
    lineTwo,
    city,
    state,
    country,
    countryCode,
    postCode,
    addressType: raw.address_type ?? null,
  };
}

/** Format địa chỉ 1–2 dòng cho UI. */
export function formatShopAddress(addr?: TikTokShopAddress | null): string | null {
  if (!addr) return null;
  const line1 = [addr.lineOne, addr.lineTwo].filter(Boolean).join(', ');
  const regionBits = [addr.city, addr.state, addr.postCode].filter(Boolean).join(', ');
  const tail = [regionBits, addr.countryCode || addr.country].filter(Boolean).join(' · ');
  const out = [line1, tail].filter(Boolean).join(' — ');
  return out || null;
}

/** Parse POST pay/onboard_info/get → field company/owner (null nếu code≠0). */
export function parsePayOnboardInfo(
  raw: PayOnboardInfoResponse | null | undefined,
): TikTokPayOnboardFields | null {
  if (!raw || Number(raw.code) !== 0 || !raw.data?.enterprise) return null;
  const ent = raw.data.enterprise;
  const companyName = (ent.company?.company_name || '').trim() || null;
  const companyEmail = (ent.contact?.email || '').trim() || null;
  const companyMobile =
    (ent.contact?.mobile || ent.contact?.phone || '').trim() || null;
  const companyAddress = parsePayAddress(ent.addresses?.[0]);
  const cert = ent.certificates?.[0];
  const companyCertNum = (cert?.cert_num || '').trim() || null;
  const companyCertType =
    cert?.cert_type != null && Number.isFinite(Number(cert.cert_type))
      ? Number(cert.cert_type)
      : null;
  const payEntityType =
    ent.entity_type != null && Number.isFinite(Number(ent.entity_type))
      ? Number(ent.entity_type)
      : null;
  const shopNameFromPay = (ent.shop_name || '').trim() || null;

  let owner: TikTokShopRelatePerson | null = null;
  const rel = ent.relate_persons?.[0]?.person;
  if (rel) {
    const firstName = (rel.first_name || '').trim() || undefined;
    const lastName = (rel.last_name || '').trim() || undefined;
    const fullName = [firstName, lastName].filter(Boolean).join(' ') || undefined;
    const personCert = (rel.certificates?.[0]?.cert_num || '').trim() || null;
    owner = {
      firstName,
      lastName,
      fullName,
      birthday: (rel.birthday || '').trim() || undefined,
      nationality: (rel.nationality || '').trim() || undefined,
      address: parsePayAddress(rel.addresses?.[0]),
      certNum: personCert,
      relatePersonType: ent.relate_persons?.[0]?.relate_person_type ?? null,
    };
    if (!owner.fullName && !owner.address && !owner.certNum) owner = null;
  }

  if (
    !companyName &&
    !companyEmail &&
    !companyMobile &&
    !companyAddress &&
    !companyCertNum &&
    !owner
  ) {
    return null;
  }

  return {
    companyName,
    companyEmail,
    companyMobile,
    companyAddress,
    companyCertNum,
    companyCertType,
    payEntityType,
    owner,
    shopNameFromPay,
  };
}

/**
 * Gắn pay/onboard_info vào shop.
 * Ưu tiên email/mobile không mask; company_name từ pay ghi đè nếu có;
 * re-infer PER/LLC khi có company_name mới.
 */
export function mergePayOnboardIntoShop(
  shop: TikTokShopInfo,
  pay: TikTokPayOnboardFields | null | undefined,
): TikTokShopInfo {
  if (!pay) return shop;

  const companyName = (pay.companyName || shop.companyName || '').trim() || undefined;
  const businessType = shop.businessType ?? null;
  const entity = inferShopEntityType({ businessType, companyName });

  return {
    ...shop,
    companyName,
    accountEmail: preferUnmaskedContact(pay.companyEmail, shop.accountEmail),
    accountMobile: preferUnmaskedContact(pay.companyMobile, shop.accountMobile),
    companyAddress: pay.companyAddress ?? shop.companyAddress ?? null,
    companyCertNum: pay.companyCertNum ?? shop.companyCertNum ?? null,
    companyCertType: pay.companyCertType ?? shop.companyCertType ?? null,
    payEntityType: pay.payEntityType ?? shop.payEntityType ?? null,
    owner: pay.owner ?? shop.owner ?? null,
    shopType: entity.shopType ?? shop.shopType ?? null,
    shopTypeReason: companyName
      ? entity.reason
      : shop.shopTypeReason ?? entity.reason,
  };
}

/** Parse GET settlement/account/get → field bank (null nếu code≠0). */
export function parseSettlementAccount(
  raw: SettlementAccountResponse | null | undefined,
): TikTokSettlementAccountFields | null {
  if (!raw || Number(raw.code) !== 0 || !raw.data) return null;
  const d = raw.data as Record<string, unknown>;
  const bankAccountNumber =
    String(
      d.bank_account_number ??
        d.bankAccountNumber ??
        d.mask_bank_account_number ??
        d.masked_bank_account_number ??
        '',
    ).trim() || null;
  if (!bankAccountNumber) return null;
  return {
    bankAccountNumber,
    paymentMethod:
      String(d.payment_method ?? d.paymentMethod ?? '').trim() || null,
    beneficiaryName:
      String(d.beneficiary_name ?? d.beneficiaryName ?? '').trim() || null,
    accountType: String(d.account_type ?? d.accountType ?? '').trim() || null,
    settlementStatus:
      d.status != null && Number.isFinite(Number(d.status)) ? Number(d.status) : null,
  };
}

/** Gắn settlement account vào shop (giữ prev nếu payload null). */
export function mergeSettlementAccountIntoShop(
  shop: TikTokShopInfo,
  settlement: TikTokSettlementAccountFields | null | undefined,
): TikTokShopInfo {
  if (!settlement?.bankAccountNumber) return shop;
  return {
    ...shop,
    bankAccountNumber: settlement.bankAccountNumber,
    settlementPaymentMethod:
      settlement.paymentMethod ?? shop.settlementPaymentMethod ?? null,
    settlementBeneficiaryName:
      settlement.beneficiaryName ?? shop.settlementBeneficiaryName ?? null,
    settlementAccountType: settlement.accountType ?? shop.settlementAccountType ?? null,
    settlementAccountStatus:
      settlement.settlementStatus ?? shop.settlementAccountStatus ?? null,
  };
}

const STARLING_IN_TRANSIT = 'unsettled_statement_reasons_in_transit_new';
const STARLING_AWAITING_SETTLEMENT = 'unsettled_statement_reasons_settlement_in_progress_new';
const STARLING_RESERVE = 'finance_payments_table_type_reserve';

function parseMoneyAmount(raw: unknown): number | null {
  if (raw == null || raw === '') return null;
  if (typeof raw === 'number' && Number.isFinite(raw)) return raw;
  const n = Number(String(raw).replace(/,/g, '').trim());
  return Number.isFinite(n) ? n : null;
}

function reasonAmount(
  reasons: Array<{
    title?: { starling_key?: string };
    reason?: number;
    amount?: { amount?: string | number };
  }>,
  starlingKey: string,
  reasonCode: number,
): number {
  const hit =
    reasons.find((r) => r?.title?.starling_key === starlingKey) ??
    reasons.find((r) => Number(r?.reason) === reasonCode);
  return parseMoneyAmount(hit?.amount?.amount) ?? 0;
}

/** Parse GET statement/stat/info → On hold + DELI (null nếu code≠0 hoặc thiếu to_settle). */
export function parseStatementStatInfo(
  raw: StatementStatInfoResponse | null | undefined,
): TikTokStatementStatFields | null {
  if (!raw || Number(raw.code) !== 0 || !raw.data) return null;
  const stat = raw.data.to_settle_amount_stat;
  if (!stat) return null;
  const total = parseMoneyAmount(stat.amount?.amount);
  if (total == null) return null;

  const reasons = Array.isArray(stat.reasons_detail) ? stat.reasons_detail : [];
  const periodRaw = raw.data.seller_quality_stat?.bill_finish_period_in_days;
  const period = periodRaw != null && periodRaw !== '' ? Number(periodRaw) : NaN;
  const deli = Number.isFinite(period) && period >= 0 ? Math.trunc(period) : null;

  return {
    onHoldAmount: total,
    onHoldInTransitAmount: reasonAmount(reasons, STARLING_IN_TRANSIT, 1),
    onHoldAwaitingSettlementAmount: reasonAmount(reasons, STARLING_AWAITING_SETTLEMENT, 3),
    onHoldReserveAmount: reasonAmount(reasons, STARLING_RESERVE, 5),
    deli,
  };
}

export const IDENTITY_VIDEO_VERIFICATION_REASON_KEY =
  'post_onboarding_identity_video_verification_reason';

/**
 * Walk products/list JSON for shop-level identity-video lock (365 ngày).
 * Key nằm ở mọi product khi TTS enforce — page_size=1 đủ detect; shop 0 listing → false.
 */
export function detectIdentityVideoVerificationLock(raw: unknown): boolean {
  if (!raw || typeof raw !== 'object') return false;
  const data = (raw as { data?: { products?: unknown } }).data;
  const products = data?.products;
  if (!Array.isArray(products)) return false;
  for (const product of products) {
    if (!product || typeof product !== 'object') continue;
    const traffic = (product as {
      suspend_reason?: { traffic_control_reasons?: unknown };
    }).suspend_reason?.traffic_control_reasons;
    if (!Array.isArray(traffic)) continue;
    for (const reason of traffic) {
      if (!reason || typeof reason !== 'object') continue;
      const subs = (reason as { sub_reasons?: unknown }).sub_reasons;
      if (!Array.isArray(subs)) continue;
      for (const sub of subs) {
        if (!sub || typeof sub !== 'object') continue;
        const key = (sub as { reason?: { key?: unknown } }).reason?.key;
        if (key === IDENTITY_VIDEO_VERIFICATION_REASON_KEY) return true;
      }
    }
  }
  return false;
}

/** Gắn cờ khóa identity video — chỉ true khi lần scrape này thấy key; omit khi fail. */
export function mergeIdentityLockIntoShop(
  shop: TikTokShopInfo,
  locked: boolean | undefined,
): TikTokShopInfo {
  if (locked !== true) {
    return { ...shop, identityVideoVerificationLock: undefined };
  }
  return { ...shop, identityVideoVerificationLock: true };
}

/** Gắn On hold + DELI vào shop (giữ prev nếu payload null). */
export function mergeStatementStatIntoShop(
  shop: TikTokShopInfo,
  stat: TikTokStatementStatFields | null | undefined,
): TikTokShopInfo {
  if (!stat) return shop;
  return {
    ...shop,
    onHoldAmount: stat.onHoldAmount,
    onHoldInTransitAmount: stat.onHoldInTransitAmount,
    onHoldAwaitingSettlementAmount: stat.onHoldAwaitingSettlementAmount,
    onHoldReserveAmount: stat.onHoldReserveAmount,
    deli: stat.deli ?? shop.deli ?? null,
  };
}

/** Parse body seller/common/get → TikTokShopInfo; null nếu code≠0 hoặc thiếu seller. */
export function parseSellerCommonGet(
  raw: SellerCommonGetResponse | null | undefined,
  fetchedAt = Date.now(),
): TikTokShopInfo | null {
  if (!raw || raw.code !== 0 || !raw.data) return null;
  const data = raw.data;
  const seller = data.seller;
  if (!seller) return null;
  const shopName =
    (seller.shop_name || seller.global_shop_name || '').trim() ||
    `Shop ${seller.seller_id || ''}`.trim();
  const sellerId = String(seller.seller_id || '').trim();
  if (!sellerId && !shopName) return null;

  const createRaw = data.global_seller?.create_time;
  const createMs =
    createRaw == null || createRaw === ''
      ? null
      : Number(createRaw);

  const companyName = data.company_name?.trim() || undefined;
  const businessType =
    seller.business_type != null && Number.isFinite(Number(seller.business_type))
      ? Number(seller.business_type)
      : null;
  const entity = inferShopEntityType({ businessType, companyName });

  return {
    shopName: shopName || sellerId,
    sellerId: sellerId || shopName,
    shopCode: seller.shop_code?.trim() || undefined,
    region: (seller.region_code || seller.shop_region || data.global_seller?.base_region || '')
      .trim() || undefined,
    companyName,
    logoUrl: seller.logo?.url_list?.[0] || undefined,
    shopStatus: seller.shop_status,
    /** Account status: 2 active, 3 deactivated. */
    sellerStatus: seller.status,
    timezone: seller.timezone?.name || data.global_seller?.timezone?.name || undefined,
    createTimeMs: Number.isFinite(createMs as number) ? (createMs as number) : null,
    phoneVerified: data.phone_verified ?? null,
    emailVerified: data.email_verified ?? null,
    verifiedDocuments:
      data.verified_documents != null && Number.isFinite(Number(data.verified_documents))
        ? Number(data.verified_documents)
        : null,
    sellerType: seller.seller_type ?? data.global_seller?.seller_type ?? null,
    businessType,
    shopType: entity.shopType,
    shopTypeReason: entity.reason,
    source: 'capture',
    fetchedAt,
  };
}

export function isCaptureReady(capture: TikTokCaptureState | null | undefined): boolean {
  if (!capture) return false;
  if (capture.shopOk) return true;
  if (!capture.headers) return false;
  const lower = new Set(Object.keys(capture.headers).map((k) => k.toLowerCase()));
  return TTS_CORE_HEADER_NAMES.some((name) => lower.has(name));
}

export function isCapturePartial(capture: TikTokCaptureState | null | undefined): boolean {
  if (!capture?.headers) return false;
  const keys = Object.keys(capture.headers);
  return keys.length > 0 && !isCaptureReady(capture);
}

export function getCaptureHeaderChecklist(
  capture: TikTokCaptureState | null | undefined,
): TikTokCaptureHeaderItem[] {
  const headers = capture?.headers || {};
  const lower = new Map(
    Object.entries(headers).map(([k, v]) => [k.toLowerCase(), { name: k, value: v }]),
  );

  const items: TikTokCaptureHeaderItem[] = TTS_CORE_HEADER_NAMES.map((name) => {
    const hit = lower.get(name);
    return {
      name,
      present: !!hit?.value,
      value: hit?.value,
      detail: hit?.value ? previewHeader(hit.value) : 'thiếu',
    };
  });

  for (const [k, v] of Object.entries(headers)) {
    const low = k.toLowerCase();
    if ((TTS_CORE_HEADER_NAMES as readonly string[]).includes(low)) continue;
    items.push({
      name: k,
      present: !!v,
      value: v,
      detail: previewHeader(v),
    });
  }

  return items;
}

export function captureReadyGaps(capture: TikTokCaptureState | null | undefined): string[] {
  if (!capture) return ['Chưa có dữ liệu capture'];
  if (isCaptureReady(capture)) return [];
  const missing = getCaptureHeaderChecklist(capture)
    .filter((h) => !h.present && (TTS_CORE_HEADER_NAMES as readonly string[]).includes(h.name))
    .map((h) => h.name);
  return missing.length ? [`thiếu header: ${missing.join(', ')}`] : ['Chưa sẵn sàng'];
}

function previewHeader(value: string, max = 28): string {
  const t = value.trim();
  if (t.length <= max) return t;
  return `${t.slice(0, max)}…`;
}
