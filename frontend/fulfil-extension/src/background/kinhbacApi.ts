import { API_BASE_URL, AUTH_SESSION_MAX_MS, FULFIL_SESSION_HEADER, STORAGE_KEYS, getOrCreateSessionId } from '../shared/config';
import type { SheinSessionPayload } from '../shared/sheinSession';
import type {
  AuthState,
  FulfilOrder,
  FulfilOrderClaim,
  FulfilOrderItem,
  ProductSourceOption,
} from '../shared/types';

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
}

interface OrderListPage {
  items: Array<{
    id: string;
    platformOrderId?: string | null;
    platform?: string | null;
    orderStatus: string;
    platformOrderStatus?: string | null;
    shopName?: string | null;
    shopProfileName?: string | null;
    ownerName?: string | null;
    amount?: number | null;
    totalPaidAmount?: number | null;
    sellerNetAmount?: number | null;
    currency?: string | null;
    buyerName?: string | null;
    buyerPhone?: string | null;
    buyerEmail?: string | null;
    address?: string | null;
    city?: string | null;
    state?: string | null;
    postalCode?: string | null;
    countryCode?: string | null;
    buyerMemo?: string | null;
    sellerMemo?: string | null;
    internalMemo?: string | null;
    message?: string | null;
    issueTags?: { id: string; label: string; isSystem: boolean }[];
    issueStatus?: string | null;
    fulfilPurchasedAt?: string | null;
    fulfilAccountName?: string | null;
    paidByUserName?: string | null;
    fulfilClaimedByUserId?: string | null;
    fulfilClaimedByName?: string | null;
    fulfilClaimedAt?: string | null;
    fulfilClaimExpiresAt?: string | null;
    fulfilClaimSessionCount?: number;
    supplierOrderNo?: string | null;
    supplierLinks?: { supplierOrderNo?: string | null }[];
    orderCreateTime?: string | null;
    orderPaidTime?: string | null;
    lineItems?: FulfilOrderItem[];
  }>;
  total: number;
}

function isSessionWithinMaxAge(auth: AuthState): boolean {
  const savedMs = Date.parse(auth.savedAt);
  if (!Number.isFinite(savedMs)) return false;
  return Date.now() - savedMs <= AUTH_SESSION_MAX_MS;
}

/** Naive API datetime (no Z/offset) = UTC — append Z so Date.parse không lệch local. */
const NAIVE_UTC_DATETIME_RE =
  /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?$/;

function parseApiInstantMs(iso: string): number {
  const t = iso.trim();
  if (NAIVE_UTC_DATETIME_RE.test(t)) {
    const normalized = (t.includes('T') ? t : t.replace(' ', 'T')) + 'Z';
    return Date.parse(normalized);
  }
  return Date.parse(t);
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

  if (!isSessionWithinMaxAge(auth)) {
    await setAuth(null);
    return null;
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

export { ApiError };

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const sessionId = await getOrCreateSessionId();
  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      [FULFIL_SESSION_HEADER]: sessionId,
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

/** Chỉ Fulfil hoặc Admin được dùng extension. */
function assertFulfilAccess(me: MeData): void {
  const roles = me.roles ?? [];
  if (roles.includes('Fulfil') || roles.includes('Admin')) return;
  throw new Error('Chỉ tài khoản Fulfil hoặc Admin mới được đăng nhập vào extension.');
}

export async function login(pin: string): Promise<AuthState> {
  const data = await apiFetch<LoginData>('/auth/login-pin', {
    method: 'POST',
    body: JSON.stringify({ pin }),
  });

  try {
    const me = await apiFetch<MeData>('/auth/me', {
      headers: { Authorization: `Bearer ${data.accessToken}` },
    });
    assertFulfilAccess(me);

    const full: AuthState = {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      expiresAt: data.expiresAt,
      savedAt: new Date().toISOString(),
      user: {
        id: me.id,
        username: me.username,
        fullName: me.fullName,
        roles: me.roles ?? [],
      },
    };
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

/**
 * Bổ sung roles vào auth đã lưu (phiên cũ trước khi lưu roles).
 * Không lỗi nếu /auth/me thất bại — trả auth nguyên.
 */
export async function ensureAuthRoles(auth: AuthState): Promise<AuthState> {
  if (auth.user?.roles) return auth;
  try {
    const me = await apiFetch<MeData>('/auth/me', {
      headers: { Authorization: `Bearer ${auth.accessToken}` },
    });
    const next: AuthState = {
      ...auth,
      user: auth.user
        ? {
            ...auth.user,
            fullName: me.fullName ?? auth.user.fullName,
            roles: me.roles ?? [],
          }
        : {
            id: me.id,
            username: me.username,
            fullName: me.fullName,
            roles: me.roles ?? [],
          },
    };
    await setAuth(next);
    return next;
  } catch {
    return auth;
  }
}

async function refresh(auth: AuthState): Promise<AuthState> {
  if (!isSessionWithinMaxAge(auth)) {
    await setAuth(null);
    throw new Error('Phiên đăng nhập đã quá 30 ngày. Vui lòng đăng nhập lại.');
  }
  const data = await apiFetch<LoginData>('/auth/refresh', {
    method: 'POST',
    body: JSON.stringify({ refreshToken: auth.refreshToken }),
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

async function ensureValidToken(): Promise<AuthState> {
  let auth = await getAuth();
  if (!auth) throw new Error('Chưa đăng nhập kinhbac.');
  const expMs = parseApiInstantMs(auth.expiresAt);
  if (Number.isFinite(expMs) && expMs - Date.now() < 30_000) {
    try {
      auth = await refresh(auth);
    } catch {
      await setAuth(null);
      throw new Error('Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại.');
    }
  }
  return auth;
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
      auth = await refresh(current);
    } catch {
      await setAuth(null);
      throw new Error('Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại.');
    }
    return apiFetch<T>(path, {
      ...init,
      headers: { ...(init?.headers ?? {}), Authorization: `Bearer ${auth.accessToken}` },
    });
  }
}

export function getProductSources(): Promise<ProductSourceOption[]> {
  return authedApi<ProductSourceOption[]>('/product-sources/options');
}

function mapOrder(raw: OrderListPage['items'][number]): FulfilOrder {
  const hasSupplierBillno =
    !!raw.supplierOrderNo?.trim() ||
    (Array.isArray(raw.supplierLinks) &&
      raw.supplierLinks.some((l) => !!l.supplierOrderNo?.trim()));
  return {
    id: raw.id,
    platformOrderId: raw.platformOrderId,
    platform: raw.platform,
    orderStatus: raw.platformOrderStatus || raw.orderStatus,
    shopName: raw.shopName,
    shopProfileName: raw.shopProfileName,
    ownerName: raw.ownerName,
    amount: raw.amount ?? null,
    totalPaidAmount: raw.totalPaidAmount ?? null,
    sellerNetAmount: raw.sellerNetAmount ?? null,
    currency: raw.currency ?? 'USD',
    buyerName: raw.buyerName,
    buyerPhone: raw.buyerPhone,
    buyerEmail: raw.buyerEmail,
    address: raw.address,
    city: raw.city,
    state: raw.state,
    postalCode: raw.postalCode,
    countryCode: raw.countryCode,
    buyerMemo: raw.buyerMemo,
    sellerMemo: raw.sellerMemo,
    internalMemo: raw.internalMemo,
    message: raw.message ?? null,
    issueTags: Array.isArray(raw.issueTags) ? raw.issueTags : [],
    issueStatus: raw.issueStatus ?? 'None',
    fulfilPurchasedAt: raw.fulfilPurchasedAt,
    fulfilAccountName: raw.fulfilAccountName ?? null,
    paidByUserName: raw.paidByUserName ?? null,
    hasSupplierBillno,
    claim: mapClaimFromList(raw),
    orderCreateTime: raw.orderCreateTime,
    orderPaidTime: raw.orderPaidTime,
    lineItems: Array.isArray(raw.lineItems) ? raw.lineItems : [],
  };
}

/** Map lease fields from list/detail; session flags filled later via enrichClaimsWithSession. */
function mapClaimFromList(
  raw: OrderListPage['items'][number],
): FulfilOrderClaim | null {
  const userId = raw.fulfilClaimedByUserId?.trim();
  const expiresAt = raw.fulfilClaimExpiresAt;
  if (!userId || !expiresAt) return null;
  const expMs = parseApiInstantMs(expiresAt);
  if (!Number.isFinite(expMs) || expMs <= Date.now()) return null;
  return {
    userId,
    userName: raw.fulfilClaimedByName ?? null,
    claimedAt: raw.fulfilClaimedAt ?? null,
    expiresAt,
    sessionCount:
      typeof raw.fulfilClaimSessionCount === 'number'
        ? raw.fulfilClaimSessionCount
        : 1,
    sessionIds: [],
    heldByThisSession: false,
    heldByMyOtherSession: false,
  };
}

/** Gắn heldByThisSession / heldByMyOtherSession từ sessionId local + sessionIds server. */
export async function enrichClaimsWithSession(
  orders: FulfilOrder[],
): Promise<FulfilOrder[]> {
  const sessionId = await getOrCreateSessionId();
  const auth = await getAuth();
  const myUserId = auth?.user?.id;
  return orders.map((o) => {
    if (!o.claim) return o;
    const ids = o.claim.sessionIds ?? [];
    // List API không trả sessionIds — không đoán «phiên khác» chỉ vì sessionCount.
    // Cần LIST_ORDER_CLAIMS / claim response để gắn đúng cờ phiên.
    if (ids.length === 0) {
      return {
        ...o,
        claim: {
          ...o.claim,
          heldByThisSession: false,
          heldByMyOtherSession: false,
        },
      };
    }
    const heldByThisSession = ids.includes(sessionId);
    const heldByMyOtherSession =
      !heldByThisSession &&
      !!myUserId &&
      o.claim.userId === myUserId;
    return {
      ...o,
      claim: { ...o.claim, heldByThisSession, heldByMyOtherSession },
    };
  });
}

/**
 * Đồng bộ lease từ GET fulfil-claims: gắn sessionIds đúng phiên,
 * xóa badge claim trên đơn không còn trong danh sách active.
 */
export async function syncOrdersWithActiveClaims(
  orders: FulfilOrder[],
): Promise<FulfilOrder[]> {
  let active: { items: FulfilClaimApiItem[]; serverTime: string };
  try {
    active = await listActiveClaims();
  } catch {
    return enrichClaimsWithSession(orders);
  }
  const byId = new Map(active.items.map((i) => [i.orderId, i]));
  return Promise.all(
    orders.map(async (o) => {
      const hit = byId.get(o.id);
      if (hit) return applyClaimItemToOrder(o, hit);
      if (o.claim) return { ...o, claim: null };
      return o;
    }),
  );
}

/** API list max pageSize; chunk lớn để ít round-trip khi lấy hết hàng đợi. */
const NEEDS_PURCHASE_PAGE_SIZE = 200;
/** Trần an toàn: 50 × 200 = 10_000 đơn — tránh vòng vô hạn nếu total lệch. */
const NEEDS_PURCHASE_MAX_PAGES = 50;

export async function listNeedsPurchaseOrders(params: {
  productSourceId?: string;
  platformOrderStatus?: string;
  /** Cỡ trang mỗi request (1–200). Mặc định 200; hàm vẫn phân trang đến hết. */
  pageSize?: number;
}): Promise<{ orders: FulfilOrder[]; total: number }> {
  const pageSize = Math.min(
    NEEDS_PURCHASE_PAGE_SIZE,
    Math.max(1, Math.floor(params.pageSize ?? NEEDS_PURCHASE_PAGE_SIZE)),
  );
  const allRaw: OrderListPage['items'] = [];
  let total = 0;

  // Hàng đợi fulfil: lấy hết đơn cũ → mới (AWAITING_SHIPMENT, chưa FulfilPurchasedAt).
  for (let pageCurrent = 1; pageCurrent <= NEEDS_PURCHASE_MAX_PAGES; pageCurrent++) {
    const q = new URLSearchParams();
    q.set('needsPurchase', 'true');
    q.set('pageCurrent', String(pageCurrent));
    q.set('pageSize', String(pageSize));
    q.set('sortDir', 'asc');
    if (params.platformOrderStatus) q.set('platformOrderStatus', params.platformOrderStatus);
    if (params.productSourceId) q.set('productSourceId', params.productSourceId);

    const page = await authedApi<OrderListPage>(`/admin/orders?${q.toString()}`);
    const items = page.items ?? [];
    total = page.total ?? total;
    allRaw.push(...items);

    if (items.length === 0 || items.length < pageSize || allRaw.length >= total) {
      break;
    }
  }

  // needsPurchase=true đã lọc FulfilPurchasedAt null; lọc thêm phòng lệch schema/cache.
  // Sync active claims để badge «Bạn đang giữ» chỉ đúng phiên/đơn còn lease.
  const orders = await syncOrdersWithActiveClaims(
    allRaw.map(mapOrder).filter((o) => !o.fulfilPurchasedAt),
  );
  return {
    orders,
    total: total || orders.length,
  };
}

/**
 * Đơn đã stamp FulfilPurchasedAt trong ngày lịch VN (server `purchasedToday=true`).
 * Non-Admin: server ép `paidByUserId` = chính user — chỉ thấy đơn mình mua.
 * Admin: thấy tất cả (có thể lọc `paidByUserId` nếu gửi thêm).
 * Lọc theo nguồn SP khi có productSourceId; sort mua gần nhất trước.
 */
export async function listPurchasedTodayOrders(params: {
  productSourceId?: string;
  pageSize?: number;
}): Promise<{ orders: FulfilOrder[]; total: number }> {
  const q = new URLSearchParams();
  q.set('purchasedToday', 'true');
  q.set('pageCurrent', '1');
  q.set('pageSize', String(params.pageSize ?? 50));
  q.set('sortDir', 'desc');
  if (params.productSourceId) q.set('productSourceId', params.productSourceId);

  const page = await authedApi<OrderListPage>(`/admin/orders?${q.toString()}`);
  const orders = (page.items ?? [])
    .map(mapOrder)
    .filter((o) => !!o.fulfilPurchasedAt);
  return {
    orders,
    total: page.total ?? orders.length,
  };
}

/**
 * Tìm đơn theo `search` — mọi workflow tab / đã mua hay chưa / mọi nguồn.
 * Không gửi needsPurchase / platformOrderStatus / productSourceId (lookup toàn cục).
 */
export async function searchOrders(params: {
  search: string;
  pageSize?: number;
}): Promise<{ orders: FulfilOrder[]; total: number }> {
  const keyword = params.search.trim();
  if (!keyword) return { orders: [], total: 0 };

  const q = new URLSearchParams();
  q.set('pageCurrent', '1');
  q.set('pageSize', String(params.pageSize ?? 50));
  q.set('sortDir', 'desc');
  q.set('search', keyword);

  const page = await authedApi<OrderListPage>(`/admin/orders?${q.toString()}`);
  const orders = await syncOrdersWithActiveClaims((page.items ?? []).map(mapOrder));
  return {
    orders,
    total: page.total ?? orders.length,
  };
}

/** Chi tiết đơn — đủ phone / address / zip cho side panel. */
export async function getOrderDetail(orderId: string): Promise<FulfilOrder> {
  const raw = await authedApi<OrderListPage['items'][number]>(`/admin/orders/${orderId}`);
  return mapOrder(raw);
}

/** Đánh dấu đã mua — chỉ set FulfilPurchasedAt (UTC now). */
export async function markOrderPurchased(
  orderId: string,
  force = false,
): Promise<void> {
  await authedApi(`/admin/orders/${orderId}/milestones`, {
    method: 'PUT',
    body: JSON.stringify({
      fulfilPurchasedAt: new Date().toISOString(),
      ...(force ? { force: true } : {}),
    }),
  });
}

export interface FulfilClaimApiItem {
  orderId: string;
  state: string;
  claimedByUserId?: string | null;
  claimedByName?: string | null;
  claimedAt?: string | null;
  expiresAt?: string | null;
  sessionCount: number;
  sessionIds?: string[];
  startedAt?: string | null;
}

export interface FulfilClaimApiResult {
  claim: FulfilClaimApiItem;
  siblingClaims?: FulfilClaimApiItem[];
  releasedClaims?: FulfilClaimApiItem[];
  conflicts?: {
    orderId: string;
    claimedByUserId?: string | null;
    claimedByName?: string | null;
    expiresAt?: string | null;
    message: string;
  }[];
  serverTime: string;
}

/** Giữ / renew đơn cho phiên Chrome profile hiện tại. */
export async function claimOrder(
  orderId: string,
  siblingOrderIds?: string[],
): Promise<FulfilClaimApiResult> {
  return authedApi<FulfilClaimApiResult>(`/admin/orders/${orderId}/fulfil-claim`, {
    method: 'POST',
    body: JSON.stringify({
      siblingOrderIds: siblingOrderIds?.length ? siblingOrderIds : undefined,
    }),
  });
}

/** Nhả claim của phiên hiện tại (hoặc tất cả phiên khi allSessions). */
export async function releaseOrderClaim(
  orderId: string,
  allSessions = false,
): Promise<FulfilClaimApiResult> {
  const q = allSessions ? '?allSessions=true' : '';
  return authedApi<FulfilClaimApiResult>(
    `/admin/orders/${orderId}/fulfil-claim${q}`,
    { method: 'DELETE' },
  );
}

/** Nhả mọi claim đang mở của phiên Chrome hiện tại (logout / orphan cleanup). */
export async function releaseSessionClaims(): Promise<{
  items: FulfilClaimApiItem[];
  serverTime: string;
}> {
  const raw = await authedApi<{
    items?: FulfilClaimApiItem[];
    serverTime?: string;
  }>('/admin/orders/fulfil-claims/session', { method: 'DELETE' });
  return {
    items: Array.isArray(raw?.items) ? raw.items : [],
    serverTime: raw?.serverTime ?? new Date().toISOString(),
  };
}

/** Danh sách claim còn hiệu lực (poll fallback / sync panel). */
export async function listActiveClaims(): Promise<{
  items: FulfilClaimApiItem[];
  serverTime: string;
}> {
  const raw = await authedApi<{
    items?: FulfilClaimApiItem[];
    serverTime?: string;
  }>('/admin/orders/fulfil-claims');
  return {
    items: Array.isArray(raw?.items) ? raw.items : [],
    serverTime: raw?.serverTime ?? new Date().toISOString(),
  };
}

/** Áp claim API item lên FulfilOrder.claim. */
export async function applyClaimItemToOrder(
  order: FulfilOrder,
  item: FulfilClaimApiItem | null | undefined,
): Promise<FulfilOrder> {
  if (!item || item.state === 'released' || item.state === 'purchased') {
    return { ...order, claim: null };
  }
  const sessionId = await getOrCreateSessionId();
  const auth = await getAuth();
  const myUserId = auth?.user?.id;
  const ids = item.sessionIds ?? [];
  const heldByThisSession = ids.includes(sessionId);
  const heldByMyOtherSession =
    !heldByThisSession &&
    !!myUserId &&
    item.claimedByUserId === myUserId &&
    (ids.length > 0 || item.sessionCount > 0);
  return {
    ...order,
    claim: {
      userId: item.claimedByUserId ?? '',
      userName: item.claimedByName ?? null,
      claimedAt: item.claimedAt ?? null,
      expiresAt: item.expiresAt ?? null,
      sessionCount: item.sessionCount ?? 0,
      sessionIds: ids,
      heldByThisSession,
      heldByMyOtherSession,
    },
  };
}

/**
 * Merge claim / renew response into local orders cache:
 * apply releasedClaims first (session focus moved), then primary + siblings.
 */
export async function applyClaimResultToOrders(
  orders: FulfilOrder[],
  res: FulfilClaimApiResult,
): Promise<FulfilOrder[]> {
  const byId = new Map<string, FulfilClaimApiItem>();
  for (const r of res.releasedClaims ?? []) {
    byId.set(r.orderId, r);
  }
  for (const s of res.siblingClaims ?? []) {
    byId.set(s.orderId, s);
  }
  if (res.claim) {
    byId.set(res.claim.orderId, res.claim);
  }

  return Promise.all(
    orders.map(async (o) => {
      const hit = byId.get(o.id);
      return hit ? applyClaimItemToOrder(o, hit) : o;
    }),
  );
}

/** Gỡ đánh dấu đã mua (bấm nhầm) — clear FulfilPurchasedAt. */
export async function unmarkOrderPurchased(orderId: string): Promise<void> {
  await authedApi(`/admin/orders/${orderId}/milestones`, {
    method: 'PUT',
    body: JSON.stringify({ clearFulfilPurchasedAt: true }),
  });
}

/** Cập nhật thông báo vận hành local (`Orders.Message`, max 500; rỗng = xoá). Legacy → issue tags. */
export async function updateOrderMessage(
  orderId: string,
  message: string | null,
): Promise<string | null> {
  const trimmed = message?.trim() || null;
  const raw = await authedApi<{
    message?: string | null;
    issueTags?: { id: string; label: string; isSystem: boolean }[];
    issueStatus?: string | null;
  }>(`/admin/orders/${orderId}/message`, {
    method: 'PUT',
    body: JSON.stringify({ message: trimmed }),
  });
  return raw?.message ?? trimmed;
}

export async function listIssueTags(): Promise<
  { id: string; label: string; isSystem: boolean }[]
> {
  return (await authedApi<{ id: string; label: string; isSystem: boolean }[]>(
    '/admin/issue-tags',
  )) ?? [];
}

export async function createIssueTag(
  label: string,
): Promise<{ id: string; label: string; isSystem: boolean }> {
  return authedApi('/admin/issue-tags', {
    method: 'POST',
    body: JSON.stringify({ label }),
  });
}

export async function updateIssueTag(
  tagId: string,
  label: string,
): Promise<{ id: string; label: string; isSystem: boolean }> {
  return authedApi(`/admin/issue-tags/${tagId}`, {
    method: 'PUT',
    body: JSON.stringify({ label }),
  });
}

export async function deleteIssueTag(tagId: string): Promise<void> {
  await authedApi(`/admin/issue-tags/${tagId}`, {
    method: 'DELETE',
  });
}

export async function setOrderIssueTags(
  orderId: string,
  tagIds: string[],
): Promise<FulfilOrder> {
  const raw = await authedApi<OrderListPage['items'][number]>(
    `/admin/orders/${orderId}/issue-tags`,
    {
      method: 'PUT',
      body: JSON.stringify({ tagIds }),
    },
  );
  return mapOrder(raw);
}

export async function setOrderIssueStatus(
  orderId: string,
  status: string,
): Promise<FulfilOrder> {
  const raw = await authedApi<OrderListPage['items'][number]>(
    `/admin/orders/${orderId}/issue-status`,
    {
      method: 'PUT',
      body: JSON.stringify({ status }),
    },
  );
  return mapOrder(raw);
}

export interface SheinSyncApiResult {
  fetched: number;
  matched: number;
  updatedTracking: number;
  /** Carrier trackings published via 4Seller Mark Ship (separate from local updatedTracking). */
  autoShipped: number;
  autoShipFailed: number;
  autoShipBlocked: number;
  updatedCost: number;
  unmatched: number;
  /** Billnos fail phone+zip — extension detail Shipping rồi rematch. */
  unmatchedBillnos?: string[];
  /** Billnos cần gọi get_order_info (phase list). */
  needsDetailBillnos?: string[];
  /** Billnos đã match nhưng cost breakdown chưa đủ tin cậy. */
  incompleteCostBillnos?: string[];
  /** Billno đã gắn sẵn và không có gì để ghi (chỉ ở chế độ `new`). */
  unchanged?: number;
  /**
   * `true` khi backend quyết định xong nhưng `SaveChanges` vỡ nên không dòng nào xuống DB.
   * Lô đó phải được coi là **chưa xử lý**: ghi snapshot/fingerprint cho nó sẽ khiến Auto Sync
   * không bao giờ gửi lại các billno trong lô.
   */
  saveFailed?: boolean;
  /**
   * Số billno bị bỏ qua theo từng mã lý do (`no_local_match`, `sibling_linked`, …).
   * Backend đếm sẵn nên thông báo không phải dò chuỗi tiếng Việt trong `notes` nữa.
   */
  skipCounts?: Record<string, number>;
  /** Từng billno bị bỏ qua kèm mã lý do và chi tiết đối chiếu. */
  skipOutcomes?: { billno: string; reason: string; detail?: string | null }[];
  /** Billno đã khớp → mã lý do phase list vẫn phải gọi get_order_info. */
  needsDetailReasons?: Record<string, string>;
  notes?: string[];
}

/** Đẩy danh sách đơn SHEIN về backend để so khớp & cập nhật tracking/chi phí. */
export async function postSheinOrders(
  orders: unknown[],
  phase: 'list' | 'detail' = 'list',
  updateMode?: 'new' | 'all',
  runId?: string,
  batchIndex?: number,
): Promise<SheinSyncApiResult> {
  const payload = JSON.stringify({
    phase,
    orders,
    ...(updateMode ? { updateMode } : {}),
    ...(runId ? { runId } : {}),
    ...(batchIndex ? { batchIndex } : {}),
  });

  return await authedApi<SheinSyncApiResult>('/admin/orders/shein-sync', {
    method: 'POST',
    body: payload,
  });
}

export interface SheinSyncConfig {
  enabled: boolean;
  intervalSeconds: number;
  /** `new` = match đơn mới + refresh open set; `all` = cập nhật lại toàn bộ. */
  updateMode?: 'new' | 'all';
  /** Số luồng song song get_order_info (1–20). */
  concurrentThreads?: number;
  /** Lookback days for open-set refresh (mode new). */
  refreshLookbackDays?: number;
  /** Open supplier billnos to refresh via get_order_info (mode new). */
  refreshBillnos?: string[];
  /**
   * Billno còn việc Auto Mark Ship (chờ đẩy mã lên sàn / chờ reconcile). Ép POST kể cả khi
   * fingerprint list không đổi — mã vận đơn đứng yên chính là lúc Auto Sync hay bỏ qua cả lượt.
   */
  pendingAutoMarkShipBillnos?: string[];
  /** Đính kèm JSON gốc SHEIN vào payload sync để chẩn đoán. Mặc định tắt. */
  captureRawPayload?: boolean;
  /** `realtime` = extension gọi SHEIN trên trình duyệt; `local` = backend gọi bằng session đã lưu. */
  headerSource?: 'local' | 'realtime';
}

/** Đọc cấu hình chu kỳ auto-sync SHEIN từ backend (Settings). */
export async function getSheinSyncConfig(): Promise<SheinSyncConfig> {
  return authedApi<SheinSyncConfig>('/admin/orders/shein-sync/config');
}

export interface SheinSyncRunResult {
  summary: string;
}

/** Local mode: backend sync ngay một tài khoản bằng session đã lưu. */
export async function postSheinSyncRun(accountId: string): Promise<SheinSyncRunResult> {
  return authedApi<SheinSyncRunResult>('/admin/orders/shein-sync/run', {
    method: 'POST',
    body: JSON.stringify({ accountId }),
  });
}

export interface SheinSessionSavedResult {
  accountId: string;
  accountName: string;
  accountEmail?: string | null;
  headerCount: number;
  hasCookie: boolean;
  hasProxy: boolean;
  ready: boolean;
  missingHeaders?: string[];
  capturedAt: string;
}

/** Lưu cookie + header SHEIN lên tài khoản fulfil khớp `accountEmail`. */
export async function postSheinSession(
  payload: SheinSessionPayload,
): Promise<SheinSessionSavedResult> {
  return authedApi<SheinSessionSavedResult>('/admin/fulfil/accounts/shein-session', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}
