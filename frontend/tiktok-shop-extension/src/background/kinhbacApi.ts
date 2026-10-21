import {
  ACCESS_TOKEN_REFRESH_LEAD_MS,
  API_BASE_URL,
  AUTH_PERSISTENT,
  STORAGE_KEYS,
} from '../shared/config';
import type { SyncListingAnalyticsItemDto } from '../shared/messages';
import { normalizeTheme, type ThemeMode } from '../shared/theme';
import type { AuthState, ShopDailyTask, TikTokShopInfo } from '../shared/types';

interface ApiResponse<T> {
  success: boolean;
  message?: string;
  data: T;
  errors?: unknown;
}

interface LoginData {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
}

interface MeData {
  id: string;
  username: string;
  fullName?: string;
  roles?: string[];
  permissions?: string[];
  theme?: string;
}

export async function getCachedTheme(): Promise<ThemeMode> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.theme);
  return normalizeTheme(raw[STORAGE_KEYS.theme] as string | undefined);
}

export async function setCachedTheme(mode: ThemeMode): Promise<void> {
  await chrome.storage.local.set({ [STORAGE_KEYS.theme]: mode });
}

export async function getAuth(): Promise<AuthState | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.auth);
  const auth = (raw[STORAGE_KEYS.auth] as AuthState | undefined) ?? null;
  if (!auth) return null;

  if (!auth.savedAt) {
    const stamped: AuthState = { ...auth, savedAt: new Date().toISOString() };
    await chrome.storage.local.set({ [STORAGE_KEYS.auth]: stamped });
    return stamped;
  }

  return auth;
}

export async function setAuth(state: AuthState | null): Promise<void> {
  if (state) {
    await chrome.storage.local.set({ [STORAGE_KEYS.auth]: state });
  } else {
    await chrome.storage.local.remove(STORAGE_KEYS.auth);
  }
}

class ApiError extends Error {
  status: number;
  constructor(message: string, status: number) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...(init?.headers ?? {}),
    },
  });
  const body = (await res.json().catch(() => null)) as ApiResponse<T> | null;
  if (!res.ok || !body || body.success === false) {
    const msg = body?.message || `Lỗi API (${res.status}).`;
    throw new ApiError(msg, res.status);
  }
  return body.data;
}

/** Chỉ Seller (hoặc Admin / quyền seller.*) được dùng extension. */
function assertSellerAccess(me: MeData): void {
  const roles = me.roles ?? [];
  if (roles.includes('Seller') || roles.includes('Admin')) return;
  const permissions = me.permissions ?? [];
  if (permissions.some((p) => p.startsWith('seller.'))) return;
  throw new Error('Chỉ tài khoản Seller mới được đăng nhập vào extension.');
}

export async function login(username: string, password: string): Promise<AuthState> {
  const data = await apiFetch<LoginData>('/auth/login', {
    method: 'POST',
    body: JSON.stringify({ username, password, persistent: AUTH_PERSISTENT }),
  });

  try {
    const me = await apiFetch<MeData>('/auth/me', {
      headers: { Authorization: `Bearer ${data.accessToken}` },
    });
    assertSellerAccess(me);

    const theme = normalizeTheme(me.theme);
    const full: AuthState = {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      expiresAt: data.expiresAt,
      savedAt: new Date().toISOString(),
      user: {
        id: me.id,
        username: me.username,
        fullName: me.fullName,
        theme,
      },
    };
    await setCachedTheme(theme);
    await setAuth(full);
    return full;
  } catch (err) {
    await apiFetch('/auth/logout', {
      method: 'POST',
      headers: { Authorization: `Bearer ${data.accessToken}` },
      body: JSON.stringify({ refreshToken: data.refreshToken }),
    }).catch(() => undefined);
    await setAuth(null);
    throw err;
  }
}

export async function logout(): Promise<void> {
  const auth = await getAuth();
  if (auth?.refreshToken) {
    await apiFetch('/auth/logout', {
      method: 'POST',
      headers: { Authorization: `Bearer ${auth.accessToken}` },
      body: JSON.stringify({ refreshToken: auth.refreshToken }),
    }).catch(() => undefined);
  }
  await setAuth(null);
}

function accessTokenNeedsRefresh(auth: AuthState, leadMs = ACCESS_TOKEN_REFRESH_LEAD_MS): boolean {
  const expMs = Date.parse(auth.expiresAt);
  if (!Number.isFinite(expMs)) return true;
  return expMs - Date.now() < leadMs;
}

function isRefreshAuthFailure(err: unknown): boolean {
  return err instanceof ApiError && err.status === 401;
}

/** Một lượt refresh tại một thời điểm — tránh rotate token song song gây revoke lẫn nhau. */
let refreshInFlight: Promise<AuthState> | null = null;

async function rotateRefreshToken(auth: AuthState): Promise<AuthState> {
  const data = await apiFetch<LoginData>('/auth/refresh', {
    method: 'POST',
    body: JSON.stringify({ refreshToken: auth.refreshToken, persistent: AUTH_PERSISTENT }),
  });
  const next: AuthState = {
    ...auth,
    accessToken: data.accessToken,
    refreshToken: data.refreshToken,
    expiresAt: data.expiresAt,
    savedAt: auth.savedAt,
  };
  await setAuth(next);
  return next;
}

async function refreshWithLock(auth: AuthState): Promise<AuthState> {
  if (refreshInFlight) {
    try {
      return await refreshInFlight;
    } catch (err) {
      const latest = await getAuth();
      if (latest && latest.refreshToken !== auth.refreshToken && !accessTokenNeedsRefresh(latest)) {
        return latest;
      }
      throw err;
    }
  }

  refreshInFlight = (async () => {
    const current = (await getAuth()) ?? auth;
    if (!accessTokenNeedsRefresh(current)) return current;
    return rotateRefreshToken(current);
  })();

  try {
    return await refreshInFlight;
  } finally {
    refreshInFlight = null;
  }
}

async function ensureValidToken(): Promise<AuthState> {
  let auth = await getAuth();
  if (!auth) throw new Error('Chưa đăng nhập kinhbac.');
  if (!accessTokenNeedsRefresh(auth)) return auth;

  try {
    auth = await refreshWithLock(auth);
  } catch (err) {
    if (isRefreshAuthFailure(err)) {
      await setAuth(null);
      throw new Error('Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại.');
    }
    throw err;
  }
  return auth;
}

/**
 * Gọi khi service worker thức dậy / alarm định kỳ — giữ phiên dài hạn, không logout khi lỗi mạng.
 */
export async function maintainAuthSession(): Promise<void> {
  const auth = await getAuth();
  if (!auth || !accessTokenNeedsRefresh(auth)) return;
  try {
    await refreshWithLock(auth);
  } catch (err) {
    if (isRefreshAuthFailure(err)) {
      await setAuth(null);
    }
  }
}

async function authedApi<T>(path: string, init?: RequestInit): Promise<T> {
  let auth = await ensureValidToken();
  try {
    return await apiFetch<T>(path, {
      ...init,
      headers: { ...(init?.headers ?? {}), Authorization: `Bearer ${auth.accessToken}` },
    });
  } catch (err) {
    const status = err instanceof ApiError ? err.status : 0;
    if (status !== 401) throw err;
    const current = await getAuth();
    if (!current) throw err;
    try {
      auth = await refreshWithLock(current);
    } catch (refreshErr) {
      if (isRefreshAuthFailure(refreshErr)) {
        await setAuth(null);
        throw new Error('Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại.');
      }
      throw refreshErr;
    }
    return apiFetch<T>(path, {
      ...init,
      headers: { ...(init?.headers ?? {}), Authorization: `Bearer ${auth.accessToken}` },
    });
  }
}

export function fetchMe(): Promise<MeData> {
  return authedApi<MeData>('/auth/me');
}

export async function syncThemeFromAccount(): Promise<ThemeMode> {
  try {
    const me = await fetchMe();
    const theme = normalizeTheme(me.theme);
    const auth = await getAuth();
    if (auth?.user) {
      await setAuth({
        ...auth,
        user: { ...auth.user, theme },
      });
    }
    await setCachedTheme(theme);
    return theme;
  } catch {
    return getCachedTheme();
  }
}

export async function persistTheme(mode: ThemeMode): Promise<ThemeMode> {
  const next = normalizeTheme(mode);
  await setCachedTheme(next);

  const auth = await getAuth();
  if (!auth) return next;

  try {
    const data = await authedApi<{ theme: string }>('/auth/theme', {
      method: 'PUT',
      body: JSON.stringify({ theme: next }),
    });
    const confirmed = normalizeTheme(data.theme);
    await setCachedTheme(confirmed);
    if (auth.user) {
      await setAuth({
        ...auth,
        user: { ...auth.user, theme: confirmed },
      });
    }
    return confirmed;
  } catch {
    if (auth.user) {
      await setAuth({
        ...auth,
        user: { ...auth.user, theme: next },
      });
    }
    return next;
  }
}

/** Map TikTokShopInfo → PUT /shops/tiktok-profile body. */
export function buildTikTokProfileSyncBody(
  info: TikTokShopInfo,
  runId?: string,
  /** Ghi chú chẩn đoán của lượt Info (nguồn publish_limit + tỉ lệ harvest) → sync log. */
  syncNote?: string | null,
) {
  return {
    ...(runId ? { runId } : {}),
    ...(syncNote ? { syncNote } : {}),
    sellerId: info.sellerId,
    shopName: info.shopName || undefined,
    shopCode: info.shopCode || undefined,
    region: info.region || undefined,
    companyName: info.companyName || undefined,
    logoUrl: info.logoUrl || undefined,
    shopStatus: info.shopStatus ?? undefined,
    sellerStatus: info.sellerStatus ?? undefined,
    timezone: info.timezone || undefined,
    createTimeMs: info.createTimeMs ?? undefined,
    phoneVerified: info.phoneVerified ?? undefined,
    emailVerified: info.emailVerified ?? undefined,
    verifiedDocuments: info.verifiedDocuments ?? undefined,
    sellerType: info.sellerType ?? undefined,
    businessType: info.businessType ?? undefined,
    shopType: info.shopType ?? undefined,
    productCategories: info.productCategories?.length ? info.productCategories : undefined,
    logisticsServices: info.logisticsServices?.length ? info.logisticsServices : undefined,
    publishLimit: info.publishLimit
      ? {
          totalLimit: info.publishLimit.totalLimit,
          publishedNum: info.publishLimit.publishedNum,
        }
      : undefined,
    publishLimitPublished: info.publishLimit?.publishedNum,
    publishLimitTotal: info.publishLimit?.totalLimit,
    companyAddress: info.companyAddress ?? undefined,
    companyCertNum: info.companyCertNum ?? undefined,
    companyCertType: info.companyCertType ?? undefined,
    payEntityType: info.payEntityType ?? undefined,
    owner: info.owner ?? undefined,
    accountEmail: info.accountEmail ?? undefined,
    accountMobile: info.accountMobile ?? undefined,
    accountName: info.accountName ?? undefined,
    isBlocked: info.isBlocked ?? undefined,
    isBlocking: info.isBlocking ?? undefined,
    bankAccountNumber: info.bankAccountNumber ?? undefined,
    settlementBankAccountNumber: info.bankAccountNumber ?? undefined,
    settlementPaymentMethod: info.settlementPaymentMethod ?? undefined,
    settlementBeneficiaryName: info.settlementBeneficiaryName ?? undefined,
    settlementAccountType: info.settlementAccountType ?? undefined,
    settlementAccountStatus: info.settlementAccountStatus ?? undefined,
    onHoldAmount: info.onHoldAmount ?? undefined,
    onHoldInTransitAmount: info.onHoldInTransitAmount ?? undefined,
    onHoldAwaitingSettlementAmount: info.onHoldAwaitingSettlementAmount ?? undefined,
    onHoldReserveAmount: info.onHoldReserveAmount ?? undefined,
    deli: info.deli ?? undefined,
    identityVideoVerificationLock:
      info.identityVideoVerificationLock === true ? true : undefined,
  };
}

/**
 * Sync captured TikTok shop profile onto an existing Kinhbac shop
 * (match ExternalShopId = sellerId). Failures are soft — caller should not block UI.
 */
export async function syncTikTokShopProfile(
  info: TikTokShopInfo,
  runId?: string,
  syncNote?: string | null,
): Promise<void> {
  if (!info.sellerId?.trim()) {
    throw new Error('Thiếu sellerId — không sync được shop.');
  }
  const auth = await getAuth();
  if (!auth) {
    throw new Error('Chưa đăng nhập kinhbac.');
  }
  await authedApi('/shops/tiktok-profile', {
    method: 'PUT',
    body: JSON.stringify(buildTikTokProfileSyncBody(info, runId, syncNote)),
  });
}

export interface TikTokShopSyncConfig {
  enabled: boolean;
  intervalSeconds: number;
}

/** Settings → Auto Sync chu kỳ (seller JWT). */
export async function getTikTokShopSyncConfig(): Promise<TikTokShopSyncConfig> {
  return authedApi<TikTokShopSyncConfig>('/shops/tiktok-profile/sync-config');
}

export interface TikTokUnifiedSyncConfig {
  info: boolean;
  finance: boolean;
  keywords: boolean;
  analytics: boolean;
  healthCenter: boolean;
  shopScore: boolean;
}

const ALL_UNIFIED_SYNC_STEPS_ON: TikTokUnifiedSyncConfig = {
  info: true,
  finance: true,
  keywords: true,
  analytics: true,
  healthCenter: true,
  shopScore: true,
};

/** Settings → bước Sync All / Sync Fast được phép chạy. Lỗi mạng → all on (fail-open). */
export async function getTikTokUnifiedSyncConfig(): Promise<TikTokUnifiedSyncConfig> {
  try {
    const raw = await authedApi<Partial<TikTokUnifiedSyncConfig>>(
      '/shops/tiktok-unified-sync/config',
    );
    return {
      info: raw.info !== false,
      finance: raw.finance !== false,
      keywords: raw.keywords !== false,
      analytics: raw.analytics !== false,
      healthCenter: raw.healthCenter !== false,
      shopScore: raw.shopScore !== false,
    };
  } catch {
    return { ...ALL_UNIFIED_SYNC_STEPS_ON };
  }
}

export interface TikTokPayoutSyncConfig {
  enabled: boolean;
  intervalSeconds: number;
  maxDate?: string | null;
  notifyNewPayment?: boolean;
}

export interface TikTokPayoutSyncCursor {
  latestOccurredAt?: string | null;
  latestExternalTransactionId?: string | null;
}

export interface SyncTikTokPayoutOrderPayload {
  tradeOrderId: string;
  settlementAmount?: number;
  earningAmount?: number;
  fees?: number;
  settlementTime?: string;
}

export interface SyncTikTokPayoutItemPayload {
  paymentId: string;
  amount: number;
  currency?: string;
  successTime?: string;
  bankCardMask?: string;
  note?: string;
  orders?: SyncTikTokPayoutOrderPayload[];
}

export interface SyncTikTokPayoutsPayload {
  runId?: string;
  externalShopId: string;
  sellerId?: string;
  payments: SyncTikTokPayoutItemPayload[];
}

export interface SyncTikTokPayoutsResult {
  shopId: string;
  inserted: number;
  updated: number;
  ordersMarkedPaid: number;
  telegramSent: number;
  skipped: number;
}

export async function getTikTokPayoutSyncConfig(): Promise<TikTokPayoutSyncConfig> {
  return authedApi<TikTokPayoutSyncConfig>('/shops/tiktok-payouts/sync-config');
}

export async function getTikTokPayoutSyncCursor(
  externalShopId: string,
): Promise<TikTokPayoutSyncCursor> {
  const q = encodeURIComponent(externalShopId);
  return authedApi<TikTokPayoutSyncCursor>(
    `/shops/tiktok-payouts/cursor?externalShopId=${q}`,
  );
}

export async function syncTikTokPayouts(
  payload: SyncTikTokPayoutsPayload,
): Promise<SyncTikTokPayoutsResult> {
  return authedApi<SyncTikTokPayoutsResult>('/shops/tiktok-payouts/sync', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export interface TikTokOpportunitySyncConfig {
  enabled: boolean;
  intervalSeconds: number;
}

export interface TikTokOpportunityCategoryInfo {
  cateId: number;
  level: number;
}

export interface TikTokOpportunitySyncContext {
  shopId: string;
  hasCatalog: boolean;
  categoryInfos: TikTokOpportunityCategoryInfo[];
  categoryInfoGroups?: TikTokOpportunityCategoryInfo[][];
  catalogNames?: string[];
}

export interface SyncProductOpportunityCategoryPayload {
  cateId: string;
  cateName: string;
  level?: number;
}

export interface SyncProductOpportunityItemPayload {
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
  categories?: SyncProductOpportunityCategoryPayload[];
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

export interface SyncProductOpportunitiesPayload {
  runId?: string;
  externalShopId: string;
  sellerId?: string;
  items: SyncProductOpportunityItemPayload[];
}

export interface SyncProductOpportunitiesResult {
  shopId: string;
  inserted: number;
  linked?: number;
  skipped?: number;
}

export async function getTikTokOpportunitySyncConfig(): Promise<TikTokOpportunitySyncConfig> {
  return authedApi<TikTokOpportunitySyncConfig>('/shops/tiktok-opportunities/sync-config');
}

export async function getTikTokOpportunitySyncContext(
  externalShopId: string,
): Promise<TikTokOpportunitySyncContext> {
  const q = encodeURIComponent(externalShopId);
  return authedApi<TikTokOpportunitySyncContext>(
    `/shops/tiktok-opportunities/sync-context?externalShopId=${q}`,
  );
}

export async function syncTikTokOpportunities(
  payload: SyncProductOpportunitiesPayload,
): Promise<SyncProductOpportunitiesResult> {
  return authedApi<SyncProductOpportunitiesResult>('/shops/tiktok-opportunities/sync', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export interface SyncListingAnalyticsPayload {
  runId?: string;
  externalShopId: string;
  sellerId?: string;
  preset?: string;
  /** yyyy-MM-dd theo timezone shop. */
  rangeStart?: string;
  rangeEnd?: string;
  items: SyncListingAnalyticsItemDto[];
}

export interface SyncListingAnalyticsResult {
  shopId: string;
  /** Ngày VN mà backend ghi (dedupe key). */
  snapshotDate: string;
  inserted: number;
  updated: number;
  linked: number;
  skipped: number;
}

export async function syncTikTokListingAnalytics(
  payload: SyncListingAnalyticsPayload,
): Promise<SyncListingAnalyticsResult> {
  return authedApi<SyncListingAnalyticsResult>('/shops/tiktok-listing-analytics/sync', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export interface SyncHealthActionPayload {
  title: string;
  actionStartTime?: number;
  actionEndTime?: number;
}

export interface SyncHealthRecordItemPayload {
  recordId: string;
  recordType: 'violation' | 'warning';
  reason?: string;
  detail?: string;
  categoryType?: string;
  actions?: SyncHealthActionPayload[];
  recordStatus: number;
  createTime: number;
}

export interface SyncTikTokHealthRecordsPayload {
  runId?: string;
  externalShopId: string;
  sellerId?: string;
  items: SyncHealthRecordItemPayload[];
}

export interface SyncTikTokHealthRecordsResult {
  shopId: string;
  inserted: number;
  updated: number;
  skipped: number;
}

export async function syncTikTokHealthRecords(
  payload: SyncTikTokHealthRecordsPayload,
): Promise<SyncTikTokHealthRecordsResult> {
  return authedApi<SyncTikTokHealthRecordsResult>('/shops/tiktok-health-records/sync', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export interface SyncTikTokShopScoresPayload {
  runId?: string;
  externalShopId: string;
  sellerId?: string;
  ahrScore?: number | null;
  performanceScore?: number | null;
  productSatisfactionScore?: number | null;
  fulfillmentScore?: number | null;
  customerServiceScore?: number | null;
}

export interface SyncTikTokShopScoresResult {
  shopId: string;
  updatedFields: string[];
}

export async function syncTikTokShopScores(
  payload: SyncTikTokShopScoresPayload,
): Promise<SyncTikTokShopScoresResult> {
  return authedApi<SyncTikTokShopScoresResult>('/shops/tiktok-shop-scores/sync', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export type TikTokShopSyncType =
  | 'profile'
  | 'payout'
  | 'opportunity'
  | 'analytics'
  | 'health_center'
  | 'shop_score';

export interface RecordTikTokSyncLogPayload {
  runId: string;
  externalShopId?: string;
  syncType: TikTokShopSyncType;
  status: 'skipped' | 'failed';
  errorMessage?: string;
}

/** Ghi log skipped/failed khi chưa gọi sync endpoint. */
export async function recordTikTokSyncLog(payload: RecordTikTokSyncLogPayload): Promise<void> {
  await authedApi('/shops/tiktok-sync/log', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export interface ShopDailyTaskPlanResponse {
  date: string;
  shopId?: string | null;
  items: ShopDailyTask[];
}

/** Nhiệm vụ truy cập URL admin giao cho shop hôm nay (kèm cờ đã xong). Chỉ đọc — không tự chạy. */
export async function getShopDailyTasks(
  externalShopId: string,
): Promise<ShopDailyTaskPlanResponse> {
  const q = encodeURIComponent(externalShopId);
  return authedApi<ShopDailyTaskPlanResponse>(`/shop-tasks/extension/daily?externalShopId=${q}`);
}

export interface ReportShopTaskVisitPayload {
  definitionId: string;
  externalShopId: string;
  visitedUrl?: string;
  status: 'done' | 'failed';
  errorMessage?: string;
}

/**
 * Báo người dùng vừa mở trang của một nhiệm vụ. Backend tự so khớp lại URL nên client không cần
 * (và không được) tự quyết định là đã xong.
 */
export async function reportShopTaskVisit(payload: ReportShopTaskVisitPayload): Promise<void> {
  await authedApi('/shop-tasks/extension/visit', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export interface ShopOnlineHeartbeatRequest {
  externalShopId: string;
  clientSessionId: string;
  startedAt: string;
  lastSeenAt: string;
}

export interface ShopOnlineCloseRequest {
  externalShopId: string;
  clientSessionId: string;
  endedAt: string;
  reason: string;
}

export interface ShopOnlineSessionResult {
  id: string;
  shopId: string;
  externalShopId: string;
  clientSessionId: string;
  startedAt: string;
  lastSeenAt: string;
  endedAt?: string | null;
  closeReason?: string | null;
  isOpen: boolean;
}

/** Heartbeat online session — no JWT (ExternalShopId only). */
export async function postOnlineHeartbeat(
  payload: ShopOnlineHeartbeatRequest,
): Promise<ShopOnlineSessionResult> {
  return apiFetch<ShopOnlineSessionResult>('/shops/tiktok-online/heartbeat', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

/** Close online session — no JWT. Soft-fail at caller. */
export async function postOnlineClose(
  payload: ShopOnlineCloseRequest,
): Promise<ShopOnlineSessionResult> {
  return apiFetch<ShopOnlineSessionResult>('/shops/tiktok-online/close', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

