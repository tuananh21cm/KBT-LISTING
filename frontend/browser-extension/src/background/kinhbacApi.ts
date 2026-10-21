import {
  ACCESS_TOKEN_REFRESH_LEAD_MS,
  API_BASE_URL,
  AUTH_PERSISTENT,
  STORAGE_KEYS,
} from '../shared/config';
import { normalizeTheme, type ThemeMode } from '../shared/theme';
import type { AuthState, PlatformOption, ProductSourceOption } from '../shared/types';

interface ApiResponse<T> {
  success?: boolean;
  Success?: boolean;
  message?: string;
  Message?: string;
  data?: T;
  Data?: T;
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
  hasFourSellerCookie?: boolean;
  fourSellerCookieLoginStatus?: string;
  theme?: string;
}

interface FourSellerLoginCheckData {
  isLoggedIn: boolean;
  message?: string;
  hasFourSellerCookie: boolean;
  fourSellerCookieLoginStatus: string;
}

interface PostHtmlResult {
  ok: boolean;
  message: string;
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

  // Phiên cũ (chưa có savedAt): đóng dấu lần đọc đầu → bắt đầu đếm 30 ngày.
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
    cache: 'no-store',
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...(init?.headers ?? {}),
    },
  });
  const body = (await res.json().catch(() => null)) as ApiResponse<T> | null;
  const failed = !res.ok || !body || body.success === false || body.Success === false;
  if (failed) {
    const msg =
      body?.message ||
      body?.Message ||
      (res.status === 403
        ? 'Không có quyền truy cập API này.'
        : res.status === 401
          ? 'Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại.'
          : `Lỗi API (${res.status}).`);
    throw new ApiError(msg, res.status);
  }
  return (body.data ?? body.Data) as T;
}

/** Chỉ Seller (hoặc Admin / quyền seller.*) được dùng extension. */
function assertSellerAccess(me: MeData): void {
  const roles = me.roles ?? [];
  if (roles.includes('Seller') || roles.includes('Admin')) return;
  const permissions = me.permissions ?? [];
  if (permissions.some((p) => p.startsWith('seller.'))) return;
  throw new Error('Chỉ tài khoản Seller mới được đăng nhập vào extension.');
}

/** Đăng nhập kinhbac, lưu token + hồ sơ user. Từ chối tài khoản không có quyền seller. */
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
    // Không giữ session nếu không đủ quyền / me thất bại.
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

/** Đảm bảo access token còn hạn (refresh nếu sắp/đã hết hạn). */
async function ensureValidToken(): Promise<AuthState> {
  let auth = await getAuth();
  if (!auth) throw new Error('Chưa đăng nhập KBT Craper.');
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

/** Gọi API kinhbac có kèm token, chỉ refresh khi HTTP 401. */
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

/** Kiểm tra session 4Seller qua proxy API (cookie không rời server). */
export function checkFourSellerLogin(): Promise<FourSellerLoginCheckData> {
  return authedApi<FourSellerLoginCheckData>('/auth/four-seller-cookie/check-login', {
    method: 'POST',
    body: JSON.stringify({}),
  });
}

/** Lấy hồ sơ hiện tại (kèm theme). */
export function fetchMe(): Promise<MeData> {
  return authedApi<MeData>('/auth/me');
}

/**
 * Đồng bộ theme từ `/auth/me` vào auth + `kbt_theme`.
 * Fail im lặng (giữ cache) — không chặn mở panel.
 */
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

/** Persist theme: local luôn; API khi đã đăng nhập. */
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
    // Giữ local dù API lỗi — user vẫn thấy theme đã chọn.
    if (auth.user) {
      await setAuth({
        ...auth,
        user: { ...auth.user, theme: next },
      });
    }
    return next;
  }
}

import type { SheinSizeGuide } from '../shared/sheinSizeGuide';

/** Đẩy HTML trang sản phẩm vào draft 4Seller qua proxy API. */
export function postHtmlFileShop(payload: {
  url: string;
  html: string;
  shopIdList: number[];
  site?: string;
  siteList?: string[];
  /** Shein comment_num — lưu local Listing, không gửi 4Seller. */
  reviewCount?: number | null;
  /** Shein comment_rank_average — lưu local Listing, không gửi 4Seller. */
  averageRating?: number | null;
  platform?: string | null;
  stock?: number | null;
  shippingDays?: number | null;
  shippingDayPercent?: number | null;
  shippingDayPercentDesc?: string | null;
  storeCode?: string | null;
  storeLogo?: string | null;
  storeTitle?: string | null;
  soldByTips?: string | null;
  shipsFromTips?: string | null;
  shopRank?: number | null;
  storeAttention?: number | null;
  storeUrl?: string | null;
  isLocalProduct?: boolean | null;
  isSupportQuickShip?: boolean | null;
  /** Local-only: recommended = Search list; manual = PDP không thuộc Search. */
  addedFrom?: 'recommended' | 'manual' | null;
  /** Shein size guide — local Listing only, not forwarded to 4Seller. */
  sizeGuideJson?: SheinSizeGuide | null;
}): Promise<PostHtmlResult> {
  return authedApi<PostHtmlResult>('/fourseller/crawl/post-html', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

interface FileUploadResult {
  id: string;
  publicUrl?: string | null;
  originalFileName?: string;
}

/** Upload file lên Bunny CDN qua API kinhbac. */
export async function uploadUserFile(
  file: Blob,
  fileName: string,
): Promise<FileUploadResult> {
  const auth = await ensureValidToken();
  const form = new FormData();
  form.append('file', file, fileName);
  form.append('linkedEntityType', 'supplier_size_chart');
  form.append('metadata', JSON.stringify({ source: 'extension' }));

  const res = await fetch(`${API_BASE_URL}/files/upload`, {
    method: 'POST',
    cache: 'no-store',
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${auth.accessToken}`,
    },
    body: form,
  });
  const body = (await res.json().catch(() => null)) as ApiResponse<FileUploadResult> | null;
  const failed = !res.ok || !body || body.success === false || body.Success === false;
  if (failed) {
    const msg =
      body?.message ||
      body?.Message ||
      (res.status === 403
        ? 'Không có quyền upload file (seller.files.manage).'
        : res.status === 401
          ? 'Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại.'
          : `Upload file thất bại (${res.status}).`);
    throw new ApiError(msg, res.status);
  }
  return (body.data ?? body.Data) as FileUploadResult;
}

/** Gắn URL Bunny Size Chart vào snapshot sản phẩm nguồn. */
export function linkSupplierSizeChartImage(payload: {
  sourceUrl: string;
  publicUrl: string;
  fileId?: string;
}): Promise<{ ok?: boolean; message?: string }> {
  return authedApi('/fourseller/supplier-product/size-chart-image', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export interface SupplierSizeChartImageStatus {
  hasImage: boolean;
  publicUrl?: string | null;
}

export function getSupplierSizeChartImageStatus(
  sourceUrl: string,
): Promise<SupplierSizeChartImageStatus> {
  const q = encodeURIComponent(sourceUrl.trim());
  return authedApi<SupplierSizeChartImageStatus>(
    `/fourseller/supplier-product/size-chart-image?sourceUrl=${q}`,
  );
}

export function getProductSources(): Promise<ProductSourceOption[]> {
  return authedApi<ProductSourceOption[]>('/product-sources/options');
}

export function getPlatforms(): Promise<PlatformOption[]> {
  return authedApi<PlatformOption[]>('/platforms/options');
}

/** Listing local của user đăng nhập (1 trang Draft/Publish). */
export interface LocalListingDraft {
  id: string;
  fourSellerListingId?: number;
  platform?: string;
  pageType?: string;
  siteStatus?: string | null;
  productName?: string;
  mainImage?: string;
  shopName?: string;
  shopProfileName?: string;
  /** 4Seller shop id — unmark đúng shop khi xóa. */
  fourSellerShopId?: number | null;
  sourceUrl?: string;
  createdAt?: string;
  platformCreateTime?: string;
  platformUpdateTime?: string;
  lastSyncedAt?: string;
  lowPrice?: number | null;
  highPrice?: number | null;
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
}

export interface LocalListingRecentPage {
  items: LocalListingDraft[];
  total: number;
  pageCurrent: number;
  pageSize: number;
}

export function getMyRecentListings(params: {
  pageSize?: number;
  pageCurrent?: number;
  pageType?: 'draft' | 'published';
  fourSellerShopIds?: number[];
} = {}): Promise<LocalListingRecentPage> {
  const pageSize = params.pageSize ?? 20;
  const pageCurrent = params.pageCurrent ?? 1;
  const pageType = params.pageType === 'published' ? 'published' : 'draft';
  const qs = new URLSearchParams({
    pageSize: String(pageSize),
    pageCurrent: String(pageCurrent),
    pageType,
  });
  const shopIds = (params.fourSellerShopIds ?? []).filter((id) => Number.isFinite(id) && id > 0);
  if (shopIds.length) qs.set('fourSellerShopIds', shopIds.join(','));
  return authedApi<LocalListingRecentPage>(`/listings/drafts?${qs.toString()}`);
}

export interface ListingTabCounts {
  draftTotal: number;
  publishLimitPublished: number;
  publishLimitTotal: number;
}

export function getListingTabCounts(fourSellerShopIds: number[]): Promise<ListingTabCounts> {
  const shopIds = fourSellerShopIds.filter((id) => Number.isFinite(id) && id > 0);
  const qs = new URLSearchParams();
  if (shopIds.length) qs.set('fourSellerShopIds', shopIds.join(','));
  const suffix = qs.toString();
  return authedApi<ListingTabCounts>(
    suffix ? `/listings/tab-counts?${suffix}` : '/listings/tab-counts',
  );
}

/** Cặp sourceUrl + shop cho sync cache “đã thêm” (chỉ 2 field). */
export interface AddedSourceShopRow {
  sourceUrl: string;
  fourSellerShopId: number;
}

export function getAddedSourceShops(): Promise<AddedSourceShopRow[]> {
  return authedApi<AddedSourceShopRow[]>('/listings/added-source-shops');
}

/** Xóa draft listing local (+ 4Seller) thuộc user đăng nhập. */
export function deleteMyDraftListing(id: string): Promise<void> {
  return authedApi<void>(`/listings/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

/** Shop Active local cho KBT Craper. */
export interface ScraperShopOption {
  id: string;
  fourSellerShopId: number | string;
  name: string;
  profileName?: string;
  platformCode?: string;
  platformAvatarUrl?: string;
  region?: string;
  productCategories?: { label: string; value: string }[];
  categories?: { id: string; name: string }[];
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
  activeCount?: number;
}

export async function getScraperShopOptions(): Promise<ScraperShopOption[]> {
  const rows = await authedApi<ScraperShopOption[] | null>('/shops/scraper-options');
  return Array.isArray(rows) ? rows : [];
}

export interface SavedKeywordApiItem {
  keyword: string;
  picUrl?: string | null;
  leadTagTexts?: string[] | null;
  searchVolume?: number;
  rationale?: string | null;
  fit?: string | null;
  researchLane?: 'listNow' | 'upcoming' | null;
  mustPhrases?: { phrase: string; anyOf: string[] }[] | null;
  shouldPhrases?: { phrase: string; anyOf: string[] }[] | null;
  minDistinctiveHits?: number | null;
}

export async function getSavedKeywords(
  shopLocalId: string,
): Promise<{ items: SavedKeywordApiItem[] }> {
  const qs = new URLSearchParams({ shopId: shopLocalId });
  const data = await authedApi<
    | SavedKeywordApiItem[]
    | { items?: SavedKeywordApiItem[]; Items?: SavedKeywordApiItem[] }
    | null
  >(`/product-opportunities/saved-keywords?${qs.toString()}`);
  if (Array.isArray(data)) return { items: data };
  const items = data?.items ?? data?.Items;
  return { items: Array.isArray(items) ? items : [] };
}

export interface UpdateCatalogProfilePayload {
  categories: { id: string; name: string }[];
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
  styleCodes?: string[];
}

export interface CatalogProfileResult extends UpdateCatalogProfilePayload {
  id: string;
}

export function updateShopCatalogProfile(
  shopLocalId: string,
  payload: UpdateCatalogProfilePayload,
): Promise<CatalogProfileResult> {
  return authedApi<CatalogProfileResult>(
    `/shops/${encodeURIComponent(shopLocalId)}/catalog-profile`,
    {
      method: 'PUT',
      body: JSON.stringify(payload),
    },
  );
}

/** AI «Hôm nay bán gì?» cho 1 shop (local Guid). */
export interface WhatToSellTodayApiResult {
  analysis: string;
  analysisPoints?: string[];
  productCategories?: string[];
  avgCost?: number | null;
  avgSalePrice?: number | null;
  minPrice: number;
  maxPrice: number;
  periodDays: number;
  productCountUsed: number;
  asOfDate?: string;
  season?: string;
  upcomingEvents?: string[];
  searchesTrend?: Array<{
    keywords: string;
    url: string;
    rationale?: string | null;
  }>;
  /** Legacy name before the trend / upcoming split. */
  searchesNow?: Array<{
    keywords: string;
    url: string;
    rationale?: string | null;
  }>;
  searchesUpcoming?: Array<{
    keywords: string;
    url: string;
    rationale?: string | null;
  }>;
  /** Legacy fallback. */
  searches?: Array<{
    keywords: string;
    url: string;
    rationale?: string | null;
  }>;
}

export function postWhatToSellToday(shopLocalId: string): Promise<WhatToSellTodayApiResult> {
  return authedApi<WhatToSellTodayApiResult>(
    `/shops/${encodeURIComponent(shopLocalId)}/what-to-sell-today`,
    { method: 'POST' },
  );
}
