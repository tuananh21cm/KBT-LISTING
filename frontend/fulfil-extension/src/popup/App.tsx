import { useCallback, useEffect, useRef, useState, type ClipboardEvent, type KeyboardEvent } from 'react';
import { createPortal } from 'react-dom';
import { APP_VERSION, BRAND_NAME, STORAGE_KEYS, getOrCreateSessionId } from '../shared/config';
import type {
  ActiveTabStatusResponse,
  ClaimOrderResponse,
  ListOrderClaimsResponse,
  ListOrdersResponse,
  MarkPurchasedResponse,
  UnmarkPurchasedResponse,
  SetOrderIssueResponse,
  SheinSyncConfigResponse,
  SheinSyncResponse,
  StateResponse,
} from '../shared/messages';
import type {
  ActiveMatch,
  CheckoutCostSnapshot,
  FulfilOrder,
  FulfilOrderClaim,
  FulfilOrderItem,
} from '../shared/types';
import { resolveBuyUrl } from '../shared/buyUrl';
import { isSheinSyncCaptureReady, sheinCaptureReadyGaps, getSheinCaptureHeaderChecklist, type SheinCaptureState } from '../shared/sheinOrders';
import { shortSheinBadgeStatus } from '../shared/sheinBadgeStatus';
import { collectSheinCookieHeader } from '../shared/sheinSession';
import { send } from './messaging';
import { OrderQuickChatModal } from './OrderQuickChatModal';
import {
  useNotificationsRealtime,
  type OrderClaimChangedItem,
  type OrderClaimChangedPayload,
} from './useNotificationsRealtime';

/** Poll claims khi SignalR chưa kết nối (fallback). */
const CLAIM_POLL_MS = 30_000;
/** Gia hạn lease cho đơn đang ghim (TTL server 15 phút). */
const CLAIM_RENEW_MS = 5 * 60_000;

function formatClaimRemain(expiresAt: string | null | undefined, nowMs: number): string {
  if (!expiresAt) return '';
  const exp = Date.parse(expiresAt);
  if (!Number.isFinite(exp)) return '';
  const remain = Math.max(0, exp - nowMs);
  const totalSec = Math.floor(remain / 1000);
  const m = Math.floor(totalSec / 60);
  const s = totalSec % 60;
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

function formatClaimElapsed(startedAt: string | null | undefined, nowMs: number): string {
  if (!startedAt) return '';
  const start = Date.parse(startedAt);
  if (!Number.isFinite(start)) return '';
  const elapsed = Math.max(0, nowMs - start);
  const totalSec = Math.floor(elapsed / 1000);
  const m = Math.floor(totalSec / 60);
  const s = totalSec % 60;
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

/** Tên ngắn trên badge claim footer (đủ nhận diện, không tràn card). */
function shortClaimName(name: string | null | undefined, max = 10): string {
  const raw = (name || '').trim();
  if (!raw) return 'Khác';
  if (raw.length <= max) return raw;
  return `${raw.slice(0, Math.max(1, max - 1))}…`;
}

/**
 * Sắp xếp: đơn mình giữ lên đầu, đơn người khác giữ xuống cuối; giữ nhóm cùng khách.
 */
function sortOrdersWithClaims(orders: FulfilOrder[]): FulfilOrder[] {
  const rank = (o: FulfilOrder): number => {
    if (o.claim?.heldByThisSession || o.claim?.heldByMyOtherSession) return 0;
    if (o.claim?.userId) return 2;
    return 1;
  };
  return [...orders].sort((a, b) => rank(a) - rank(b));
}

function claimFromPayloadItem(
  item: OrderClaimChangedItem,
  sessionId: string,
  myUserId?: string | null,
): FulfilOrderClaim | null {
  if (item.state === 'released' || item.state === 'purchased') return null;
  if (!item.claimedByUserId || !item.expiresAt) return null;
  const ids = item.sessionIds ?? [];
  const heldByThisSession = ids.includes(sessionId);
  const heldByMyOtherSession =
    !heldByThisSession &&
    !!myUserId &&
    item.claimedByUserId === myUserId &&
    (ids.length > 0 || (item.sessionCount ?? 0) > 0);
  return {
    userId: item.claimedByUserId,
    userName: item.claimedByName ?? null,
    claimedAt: item.claimedAt ?? item.startedAt ?? null,
    expiresAt: item.expiresAt,
    sessionCount: item.sessionCount ?? 0,
    sessionIds: ids,
    heldByThisSession,
    heldByMyOtherSession,
  };
}

function mergeClaimOntoOrder(
  order: FulfilOrder,
  item: OrderClaimChangedItem,
  sessionId: string,
  myUserId?: string | null,
): FulfilOrder {
  if (item.state === 'purchased') {
    return {
      ...order,
      fulfilPurchasedAt: order.fulfilPurchasedAt || new Date().toISOString(),
      claim: null,
    };
  }
  return { ...order, claim: claimFromPayloadItem(item, sessionId, myUserId) };
}

function mergeClaimPayload(
  orders: FulfilOrder[],
  payload: OrderClaimChangedPayload,
  sessionId: string,
  myUserId?: string | null,
): FulfilOrder[] {
  if (payload.items.length === 0) return orders;
  const byId = new Map(payload.items.map((i) => [i.orderId, i]));
  return orders
    .map((o) => {
      const hit = byId.get(o.id);
      if (!hit) return o;
      return mergeClaimOntoOrder(o, hit, sessionId, myUserId);
    })
    .filter((o) => {
      const hit = byId.get(o.id);
      if (hit?.state === 'purchased' && !o.fulfilPurchasedAt) return false;
      return true;
    });
}

/** Full sync từ GET fulfil-claims — xóa badge trên đơn không còn lease active. */
function replaceClaimsFromActiveList(
  orders: FulfilOrder[],
  items: OrderClaimChangedItem[],
  sessionId: string,
  myUserId?: string | null,
): FulfilOrder[] {
  const byId = new Map(items.map((i) => [i.orderId, i]));
  return orders.map((o) => {
    const hit = byId.get(o.id);
    if (hit) return mergeClaimOntoOrder(o, hit, sessionId, myUserId);
    if (o.claim) return { ...o, claim: null };
    return o;
  });
}

function claimItemsFromOpenResponse(res: {
  claim?: OrderClaimChangedItem | null;
  siblingClaims?: OrderClaimChangedItem[] | null;
  releasedClaims?: OrderClaimChangedItem[] | null;
}): OrderClaimChangedItem[] {
  const items: OrderClaimChangedItem[] = [];
  for (const r of res.releasedClaims ?? []) {
    if (r?.orderId) items.push(r);
  }
  for (const s of res.siblingClaims ?? []) {
    if (s?.orderId) items.push(s);
  }
  if (res.claim?.orderId) items.push(res.claim);
  return items;
}

function siblingIdsForOrder(order: FulfilOrder, list: FulfilOrder[]): string[] {
  const key = customerKey(order);
  if (!key) return [];
  return list.filter((o) => o.id !== order.id && customerKey(o) === key).map((o) => o.id);
}

/** Ô tìm: gọi API mọi trạng thái khi ≥ N ký tự (không chỉ hàng đợi chưa mua). */
const SEARCH_MIN_LEN = 2;
const SEARCH_DEBOUNCE_MS = 350;

/** Lỗi mất phiên — cần đưa UI về form đăng nhập, không giữ status sync cũ. */
function isAuthErrorMessage(msg?: string | null): boolean {
  return !!msg && /hết hạn|Chưa đăng nhập/i.test(msg);
}

function Spinner({ dark }: { dark?: boolean }) {
  return <span className={`spinner${dark ? ' spinner--dark' : ''}`} aria-hidden />;
}

function itemThumb(item: FulfilOrderItem): string | undefined {
  return item.sourceImgUrl || item.imgUrl || undefined;
}

const THUMB_PREVIEW_PAD = 8;
const THUMB_PREVIEW_SIZE = 280;

function ThumbSlot({
  src,
  emptyAriaHidden,
}: {
  src?: string;
  emptyAriaHidden?: boolean;
}) {
  const slotRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState({ top: 0, left: 0, size: THUMB_PREVIEW_SIZE });

  const hide = useCallback(() => setOpen(false), []);

  const show = useCallback(() => {
    if (!src || !slotRef.current) return;
    const r = slotRef.current.getBoundingClientRect();
    const size = Math.min(
      THUMB_PREVIEW_SIZE,
      Math.max(160, window.innerWidth - THUMB_PREVIEW_PAD * 2),
    );
    let left = r.right + THUMB_PREVIEW_PAD;
    if (left + size > window.innerWidth - THUMB_PREVIEW_PAD) {
      left = THUMB_PREVIEW_PAD;
    }
    let top = r.top;
    if (top + size > window.innerHeight - THUMB_PREVIEW_PAD) {
      top = Math.max(THUMB_PREVIEW_PAD, window.innerHeight - size - THUMB_PREVIEW_PAD);
    }
    if (top < THUMB_PREVIEW_PAD) top = THUMB_PREVIEW_PAD;
    setPos({ top, left, size });
    setOpen(true);
  }, [src]);

  useEffect(() => {
    if (!open) return;
    window.addEventListener('scroll', hide, true);
    window.addEventListener('resize', hide);
    return () => {
      window.removeEventListener('scroll', hide, true);
      window.removeEventListener('resize', hide);
    };
  }, [open, hide]);

  return (
    <>
      <div
        className={`thumb-slot${src ? ' thumb-slot--preview' : ''}`}
        ref={slotRef}
        onMouseEnter={show}
        onMouseLeave={hide}
      >
        {src ? (
          <img className="thumb" src={src} alt="" />
        ) : (
          <div className="thumb thumb--empty" aria-hidden={emptyAriaHidden || undefined} />
        )}
      </div>
      {open && src
        ? createPortal(
            <div
              className="thumb-preview-pop"
              style={{ top: pos.top, left: pos.left, width: pos.size, height: pos.size }}
            >
              <img src={src} alt="" />
            </div>,
            document.body,
          )
        : null}
    </>
  );
}

function buyUrl(item: FulfilOrderItem): string | undefined {
  return resolveBuyUrl(item);
}

/** Naive API datetime (no Z/offset) = UTC digits — append Z so Date.parse is not browser-local. */
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

function formatOrderDate(iso?: string | null): string | null {
  if (!iso) return null;
  const ms = parseApiInstantMs(iso);
  if (!Number.isFinite(ms)) return null;
  return new Date(ms).toLocaleString('vi-VN', {
    timeZone: 'Asia/Ho_Chi_Minh',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Ngày đặt: ưu tiên paid, fallback create. */
function orderPlacedLabel(order: FulfilOrder): string | null {
  return formatOrderDate(order.orderPaidTime ?? order.orderCreateTime);
}

/** Mốc bắt đầu chờ fulfil — khớp timeline admin (Đặt = orderCreateTime). */
function orderWaitFromMs(order: FulfilOrder): number | null {
  const raw = order.orderCreateTime ?? order.orderPaidTime;
  if (!raw) return null;
  const ms = parseApiInstantMs(raw);
  return Number.isFinite(ms) ? ms : null;
}

/** Compact duration giống CompactFulfilTimeline — vd: `42m`, `2h10m`, `5d9h`. */
function formatDurationShort(fromMs: number, toMs: number): string {
  if (!Number.isFinite(fromMs) || !Number.isFinite(toMs) || toMs < fromMs) return '—';
  let sec = Math.floor((toMs - fromMs) / 1000);
  if (sec < 60) return '<1m';
  const days = Math.floor(sec / 86400);
  sec %= 86400;
  const hours = Math.floor(sec / 3600);
  sec %= 3600;
  const mins = Math.floor(sec / 60);
  if (days > 0) return hours > 0 ? `${days}d${hours}h` : `${days}d`;
  if (hours > 0) return mins > 0 ? `${hours}h${mins}m` : `${hours}h`;
  return `${mins}m`;
}

/** Cấp cảnh báo chưa mua: mỗi 12h tăng 1 bậc (0…4). */
const PURCHASE_WAIT_URGENCY_MAX = 4;
const PURCHASE_WAIT_URGENCY_HOURS = 12;

function purchaseWaitUrgencyLevel(fromMs: number | null, toMs: number): number {
  if (fromMs == null || !Number.isFinite(fromMs) || !Number.isFinite(toMs) || toMs < fromMs) {
    return 0;
  }
  const hours = (toMs - fromMs) / 3_600_000;
  return Math.min(PURCHASE_WAIT_URGENCY_MAX, Math.max(0, Math.floor(hours / PURCHASE_WAIT_URGENCY_HOURS)));
}

/** Chỉ cho gỡ stamp tay khi chưa gắn billno SHEIN thật. */
function canUnmarkPurchased(order: FulfilOrder): boolean {
  return !!order.fulfilPurchasedAt && !order.hasSupplierBillno;
}

/** Thực nhận (Rev) — khớp admin drawer: sellerNet → totalPaid ≠ 0 → amount. */
function orderRevenue(order: FulfilOrder): number | null {
  if (order.sellerNetAmount != null && Number.isFinite(order.sellerNetAmount)) {
    return order.sellerNetAmount;
  }
  const paid = order.totalPaidAmount;
  if (paid != null && Number.isFinite(paid) && paid !== 0) return paid;
  const amount = order.amount;
  if (amount != null && Number.isFinite(amount)) return amount;
  return null;
}

function formatMoney(amount: number, currency?: string | null): string {
  const n = Number(amount);
  const text = Number.isFinite(n)
    ? n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    : '—';
  const cur = (currency || 'USD').trim().toUpperCase() || 'USD';
  // USD → `$36.45`; khác → `36.45 EUR` cho gọn.
  if (cur === 'USD') return `$${text}`;
  return `${text} ${cur}`;
}

function formatProfitMoney(amount: number, currency?: string | null): string {
  const signed = amount > 0 ? '+' : '';
  return `${signed}${formatMoney(amount, currency)}`;
}

/** Đưa đơn lên đầu list tạm thời (Tải lại / API vẫn trả thứ tự hàng đợi thật). */
function promoteOrderToTop(
  list: FulfilOrder[],
  orderId: string,
  order?: FulfilOrder | null,
): FulfilOrder[] {
  const idx = list.findIndex((o) => o.id === orderId);
  if (idx < 0) {
    return order ? [order, ...list] : list;
  }
  const picked = order ?? list[idx];
  if (idx === 0) {
    return picked === list[0] ? list : [picked, ...list.slice(1)];
  }
  return [picked, ...list.slice(0, idx), ...list.slice(idx + 1)];
}

/** Bộ lọc tuổi đơn (giờ chờ từ lúc đặt) — 0 = tất cả. */
const WAIT_HOURS_OPTIONS = [
  { value: 0, label: 'Tất cả' },
  { value: 1, label: '1 giờ' },
  { value: 2, label: '2 giờ' },
  { value: 4, label: '4 giờ' },
  { value: 6, label: '6 giờ' },
  { value: 8, label: '8 giờ' },
  { value: 10, label: '10 giờ' },
] as const;

const DEFAULT_WAIT_HOURS = 2;

function parseWaitHoursFilter(raw: unknown): number {
  const n = typeof raw === 'number' ? raw : Number(raw);
  if (!Number.isFinite(n)) return DEFAULT_WAIT_HOURS;
  if (WAIT_HOURS_OPTIONS.some((o) => o.value === n)) return n;
  return DEFAULT_WAIT_HOURS;
}

/** Đơn đã chờ ≥ minHours (mốc orderCreateTime, khớp badge chờ). */
function orderMeetsWaitHours(order: FulfilOrder, minHours: number, nowMs: number): boolean {
  if (minHours <= 0) return true;
  const fromMs = orderWaitFromMs(order);
  if (fromMs == null) return false;
  return nowMs - fromMs >= minHours * 3_600_000;
}

/** Đồng hồ chung — tránh N interval trên từng card. */
function useSharedNow(enabled: boolean, intervalMs = 30_000): number {
  const [nowMs, setNowMs] = useState(() => Date.now());
  useEffect(() => {
    if (!enabled) return;
    setNowMs(Date.now());
    const id = window.setInterval(() => setNowMs(Date.now()), intervalMs);
    return () => window.clearInterval(id);
  }, [enabled, intervalMs]);
  return nowMs;
}

/** Có claim hiện MM:SS (còn lại / đã giữ) → cần tick 1s. */
function orderNeedsClaimSecondTick(order: FulfilOrder): boolean {
  const claim = order.claim;
  if (!claim) return false;
  if (claim.heldByThisSession) return true;
  return !order.fulfilPurchasedAt && !!claim.expiresAt;
}

/** Đếm ngược chu kỳ Auto Sync — chỉ giây, dạng `(120)`. */
function formatSyncCountdown(totalSeconds: number): string {
  return `(${Math.max(0, Math.floor(totalSeconds))})`;
}

function splitBuyerName(full: string): { first: string; last: string } {
  const parts = full.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return { first: '', last: '' };
  if (parts.length === 1) return { first: parts[0]!, last: '' };
  return { first: parts[0]!, last: parts.slice(1).join(' ') };
}

/** Phone copy: chỉ số quốc gia Mỹ, bỏ +1 / 1 đầu. */
function phoneForCopy(phone: string): string {
  let digits = phone.replace(/\D/g, '');
  if (digits.length === 11 && digits.startsWith('1')) digits = digits.slice(1);
  return digits;
}

/** Khoá phone — mirror SheinMatchKeys.PhoneKey (chỉ số; bỏ leading 1 khi 11 số). */
function phoneKey(phone: string | null | undefined): string {
  if (!phone?.trim()) return '';
  return phoneForCopy(phone);
}

/** Khoá ZIP — mirror SheinMatchKeys.ZipKey (tối đa 5 chữ số đầu). */
function zipKey(postal: string | null | undefined): string {
  if (!postal?.trim()) return '';
  const digits = postal.replace(/\D/g, '');
  return digits.length <= 5 ? digits : digits.slice(0, 5);
}

/** phone|zip — null khi thiếu một trong hai. */
function customerKey(order: FulfilOrder): string | null {
  const phone = phoneKey(order.buyerPhone);
  const zip = zipKey(order.postalCode);
  if (!phone || !zip) return null;
  return `${phone}|${zip}`;
}

/**
 * Gom đơn trùng phone+zip liền nhau (giữ thứ tự gốc trong/nhóm), đánh dấu id cùng khách.
 * Chỉ xét list đã filter — đơn thiếu phone/zip không tham gia.
 */
function sortSameCustomerAdjacent(orders: FulfilOrder[]): {
  orders: FulfilOrder[];
  sameCustomerIds: Set<string>;
} {
  const keys = orders.map(customerKey);
  const counts = new Map<string, number>();
  for (const k of keys) {
    if (!k) continue;
    counts.set(k, (counts.get(k) ?? 0) + 1);
  }
  const duplicateKeys = new Set(
    [...counts.entries()].filter(([, c]) => c >= 2).map(([k]) => k),
  );
  const sameCustomerIds = new Set<string>();
  for (let i = 0; i < orders.length; i++) {
    const k = keys[i];
    if (k && duplicateKeys.has(k)) sameCustomerIds.add(orders[i]!.id);
  }

  const seenGroup = new Set<string>();
  const result: FulfilOrder[] = [];
  for (let i = 0; i < orders.length; i++) {
    const k = keys[i];
    if (k && duplicateKeys.has(k)) {
      if (seenGroup.has(k)) continue;
      seenGroup.add(k);
      for (let j = i; j < orders.length; j++) {
        if (keys[j] === k) result.push(orders[j]!);
      }
    } else {
      result.push(orders[i]!);
    }
  }
  return { orders: result, sameCustomerIds };
}

async function copyText(value: string): Promise<boolean> {
  const text = value.trim();
  if (!text) return false;
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    try {
      const ta = document.createElement('textarea');
      ta.value = text;
      ta.setAttribute('readonly', '');
      ta.style.position = 'fixed';
      ta.style.left = '-9999px';
      document.body.appendChild(ta);
      ta.select();
      const ok = document.execCommand('copy');
      ta.remove();
      return ok;
    } catch {
      return false;
    }
  }
}

/** Hiển thị 4 số đầu…4 số cuối — id ngắn ≤8 ký tự giữ nguyên. */
function shortOrderId(id: string): string {
  const s = id.trim();
  if (s.length <= 8) return s;
  return `${s.slice(0, 4)}...${s.slice(-4)}`;
}

/** Mã đơn — mặc định rút gọn; `full` hiện đủ (list còn chỗ). Click copy full. */
function OrderIdCopy({
  orderId,
  className,
  full: showFull,
}: {
  orderId: string;
  className?: string;
  full?: boolean;
}) {
  const [copied, setCopied] = useState(false);
  const full = orderId.trim();
  if (!full) return null;
  const label = showFull ? full : shortOrderId(full);

  return (
    <button
      type="button"
      className={['order-id-copy', copied ? 'order-id-copy--copied' : '', className || '']
        .filter(Boolean)
        .join(' ')}
      title={copied ? `Đã copy ${full}` : `Copy ${full}`}
      aria-label={`Copy mã đơn ${full}`}
      onClick={(ev) => {
        ev.preventDefault();
        ev.stopPropagation();
        void (async () => {
          const ok = await copyText(full);
          if (!ok) return;
          setCopied(true);
          window.setTimeout(() => setCopied(false), 1000);
        })();
      }}
    >
      {copied ? 'Đã copy' : label}
    </button>
  );
}

function CopyField({
  label,
  value,
  copyValue,
  multiline,
  className,
}: {
  label: string;
  value: string;
  /** Giá trị ghi clipboard (mặc định = value). */
  copyValue?: string;
  multiline?: boolean;
  className?: string;
}) {
  const [copied, setCopied] = useState(false);
  const display = value.trim() || '—';
  const payload = (copyValue ?? value).trim();
  const canCopy = !!payload;

  return (
    <button
      type="button"
      className={[
        'copy-field',
        multiline ? 'copy-field--multiline' : '',
        copied ? 'copy-field--copied' : '',
        className || '',
      ]
        .filter(Boolean)
        .join(' ')}
      disabled={!canCopy}
      title={canCopy ? `Copy ${label}` : undefined}
      onClick={() => {
        if (!canCopy) return;
        void (async () => {
          const ok = await copyText(payload);
          if (!ok) return;
          setCopied(true);
          window.setTimeout(() => setCopied(false), 1000);
        })();
      }}
    >
      <span className="copy-field-label">{label}</span>
      <span className="copy-field-value">{display}</span>
      {copied ? <span className="copy-field-hint">✓</span> : null}
    </button>
  );
}

function LoginForm({
  onSubmit,
  loading,
  error,
}: {
  onSubmit: (pin: string) => void;
  loading: boolean;
  error: string | null;
}) {
  const [digits, setDigits] = useState<string[]>(['', '', '', '']);
  const inputsRef = useRef<(HTMLInputElement | null)[]>([]);
  const submittedRef = useRef(false);

  useEffect(() => {
    if (error) {
      setDigits(['', '', '', '']);
      submittedRef.current = false;
      window.setTimeout(() => inputsRef.current[0]?.focus(), 0);
    }
  }, [error]);

  useEffect(() => {
    if (!loading) {
      submittedRef.current = false;
    }
  }, [loading]);

  const submitPin = (pin: string) => {
    if (loading || submittedRef.current) return;
    if (!/^\d{4}$/.test(pin)) return;
    submittedRef.current = true;
    onSubmit(pin);
  };

  const applyDigits = (next: string[]) => {
    setDigits(next);
    const pin = next.join('');
    if (pin.length === 4 && next.every((d) => d !== '')) {
      submitPin(pin);
    }
  };

  const handleChange = (index: number, raw: string) => {
    const cleaned = raw.replace(/\D/g, '');
    if (cleaned.length === 0) {
      const next = [...digits];
      next[index] = '';
      applyDigits(next);
      return;
    }
    if (cleaned.length > 1) {
      const chars = cleaned.slice(0, 4).split('');
      const next = ['', '', '', ''];
      chars.forEach((c, i) => {
        next[i] = c;
      });
      applyDigits(next);
      const focusAt = Math.min(chars.length, 3);
      window.setTimeout(() => inputsRef.current[focusAt]?.focus(), 0);
      return;
    }
    const next = [...digits];
    next[index] = cleaned;
    applyDigits(next);
    if (index < 3) {
      inputsRef.current[index + 1]?.focus();
    }
  };

  const handleKeyDown = (index: number, e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Backspace' && !digits[index] && index > 0) {
      e.preventDefault();
      const next = [...digits];
      next[index - 1] = '';
      setDigits(next);
      inputsRef.current[index - 1]?.focus();
    }
  };

  const handlePaste = (e: ClipboardEvent) => {
    e.preventDefault();
    const text = e.clipboardData.getData('text').replace(/\D/g, '').slice(0, 4);
    if (!text) return;
    const next = ['', '', '', ''];
    text.split('').forEach((c, i) => {
      next[i] = c;
    });
    applyDigits(next);
    const focusAt = Math.min(text.length, 3);
    window.setTimeout(() => inputsRef.current[focusAt]?.focus(), 0);
  };

  return (
    <div className="app app--login">
      <div className={`login-card${loading ? ' login-card--busy' : ''}`}>
        <div className="login-brand">
          <img className="login-logo" src="/icons/icon-128.png" alt="" />
          <h1 className="login-title">{BRAND_NAME}</h1>
          <p className="login-version">v{APP_VERSION}</p>
          <p className="login-sub">Nhập mã PIN để tiếp tục</p>
        </div>

        <div
          className="login-pin"
          role="group"
          aria-label="Mã PIN 4 số"
          onPaste={handlePaste}
        >
          {digits.map((digit, index) => (
            <input
              key={index}
              id={index === 0 ? 'kb-fulfil-pin-0' : undefined}
              ref={(el) => {
                inputsRef.current[index] = el;
              }}
              className={`login-pin-box${digit ? ' login-pin-box--filled' : ''}`}
              type="text"
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete={index === 0 ? 'one-time-code' : 'off'}
              maxLength={1}
              value={digit}
              disabled={loading}
              aria-label={`Chữ số ${index + 1}`}
              onChange={(e) => handleChange(index, e.target.value)}
              onKeyDown={(e) => handleKeyDown(index, e)}
              autoFocus={index === 0}
            />
          ))}
        </div>

        <div className="login-status" aria-live="polite">
          {loading ? (
            <span className="login-status-row">
              <span className="spinner spinner--dark" />
              Đang đăng nhập…
            </span>
          ) : error ? (
            <span className="login-status-error">{error}</span>
          ) : (
            <span className="login-status-hint">Tự đăng nhập khi đủ 4 số</span>
          )}
        </div>
      </div>
    </div>
  );
}

function ActiveMatchPanel({
  match,
  marking,
  reloading,
  messaging,
  variantFit,
  purchasePhase,
  checkoutPhase,
  checkoutCost,
  nowMs,
  onMark,
  onUnmark,
  onRelease,
  releasing = false,
  onReload,
  onMessage,
}: {
  match: ActiveMatch;
  marking: boolean;
  reloading: boolean;
  messaging: boolean;
  variantFit?: { fit: 'ok' | 'bad' | 'warn'; detail: string } | null;
  /** Cart / checkout — không còn kiểm tra size trên PDP. */
  purchasePhase?: boolean;
  /** Đang ở trang checkout — hiện giá thực trả + profit. */
  checkoutPhase?: boolean;
  checkoutCost?: CheckoutCostSnapshot | null;
  nowMs: number;
  onMark: () => void;
  /** Undo «Đã mua» bấm nhầm. */
  onUnmark: () => void;
  /** Nhả claim phiên hiện tại. */
  onRelease?: () => void;
  releasing?: boolean;
  onReload: () => void;
  onMessage: () => void;
}) {
  const { item, order } = match;
  const alreadyPurchased = !!order.fulfilPurchasedAt;
  const showUnmark = canUnmarkPurchased(order);
  const claimHeldHere = !!order.claim?.heldByThisSession;
  const claimElapsed = claimHeldHere
    ? formatClaimElapsed(order.claim?.claimedAt, nowMs)
    : '';
  const { first: firstName, last: lastName } = splitBuyerName(order.buyerName || '');
  const phoneRaw = order.buyerPhone?.trim() || '';
  const phoneCopy = phoneForCopy(phoneRaw);
  const street = order.address?.trim() || '';
  const city = order.city?.trim() || '';
  const state = order.state?.trim() || '';
  const zip = order.postalCode?.trim() || '';
  const fromMs = orderWaitFromMs(order);
  const waitLabel = fromMs != null ? formatDurationShort(fromMs, nowMs) : null;
  const fitClass = alreadyPurchased
    ? 'active-match--ok'
    : variantFit?.fit === 'ok'
      ? 'active-match--ok'
      : variantFit?.fit === 'bad'
        ? 'active-match--bad'
        : 'active-match--warn';

  const badgeClass = alreadyPurchased
    ? 'badge-ok'
    : variantFit?.fit === 'ok'
      ? 'badge-ok'
      : variantFit?.fit === 'bad'
        ? 'badge-err'
        : 'badge-warn';
  const badgePrefix = alreadyPurchased
    ? '✓'
    : variantFit?.fit === 'ok'
      ? '✓'
      : variantFit?.fit === 'bad'
        ? '✗'
        : '…';
  const badgeText = alreadyPurchased
    ? 'Đã mua'
    : variantFit?.detail
      ? variantFit.detail
      : purchasePhase
        ? 'Đang mua hàng…'
        : 'Đang kiểm tra size…';
  const orderTags = order.issueTags ?? [];
  const orderMessage =
    orderTags.map((t) => t.label).join(', ') || order.message?.trim() || '';
  const hasMessageHint = !!orderMessage;
  const revenue = orderRevenue(order);
  const revenueLabel = revenue != null ? formatMoney(revenue, order.currency) : null;
  const costAmount =
    checkoutPhase && checkoutCost && Number.isFinite(checkoutCost.amount)
      ? checkoutCost.amount
      : null;
  const costLabel =
    costAmount != null
      ? formatMoney(costAmount, checkoutCost?.currency || order.currency || 'USD')
      : null;
  const profit = revenue != null && costAmount != null ? revenue - costAmount : null;
  const profitClass =
    profit == null
      ? ''
      : profit < 0
        ? 'is-loss'
        : profit < 10
          ? 'is-low'
          : 'is-ok';
  const profitLabel =
    profit == null ? null : formatProfitMoney(profit, order.currency || 'USD');

  return (
    <section className={`card active-match ${fitClass}`}>
      <div className="active-match-head">
        <div className="card-title">
          {alreadyPurchased ? 'Sản phẩm đã mua' : 'Sản phẩm đang mua'}
        </div>
        <span className={`badge ${badgeClass}`}>
          {badgePrefix} {badgeText}
        </span>
      </div>
      <div className="active-match-row">
        <ThumbSlot src={itemThumb(item)} />
        <div className="active-match-body">
          <div className="item-title" title={item.title || undefined}>
            {item.title || '(Không tên)'}
          </div>
          <div
            className="meta item-meta-line"
            title={[
              item.sellerSku ? `SKU: ${item.sellerSku}` : null,
              item.variantAttr ? `Biến thể: ${item.variantAttr}` : null,
              `x${item.quantity}`,
            ]
              .filter(Boolean)
              .join(' · ')}
          >
            {item.variantAttr ? (
              <span className="line-item-variant">{item.variantAttr}</span>
            ) : null}
            <span className="line-item-qty">{`x${item.quantity}`}</span>
            {item.sellerSku ? (
              <span className="line-item-sku">{`SKU: ${item.sellerSku}`}</span>
            ) : null}
          </div>
          {revenueLabel || costLabel || profitLabel || (checkoutPhase && revenueLabel) ? (
            <div className="active-match-finance">
              {revenueLabel ? (
                <div
                  className="active-match-paid"
                  title="Rev (seller net) — matches order detail drawer"
                >
                  <span className="active-match-paid-label">Rev</span>
                  <span className="active-match-paid-value">{revenueLabel}</span>
                </div>
              ) : null}
              {checkoutPhase && costLabel ? (
                <div
                  className="active-match-paid active-match-cost"
                  title={
                    checkoutCost?.raw
                      ? `Checkout total on SHEIN (${checkoutCost.raw})`
                      : 'Checkout total on SHEIN'
                  }
                >
                  <span className="active-match-paid-label">Cost</span>
                  <span className="active-match-paid-value">{costLabel}</span>
                </div>
              ) : null}
              {checkoutPhase && profitLabel ? (
                <div
                  className={`active-match-paid active-match-profit ${profitClass}`}
                  title="Profit = Rev − Cost (checkout)"
                >
                  <span className="active-match-paid-label">Profit</span>
                  <span className="active-match-paid-value">{profitLabel}</span>
                </div>
              ) : checkoutPhase && revenueLabel ? (
                <div className="active-match-paid active-match-profit is-pending">
                  <span className="active-match-paid-label">Profit</span>
                  <span className="active-match-paid-value">…</span>
                </div>
              ) : null}
            </div>
          ) : null}
        </div>
      </div>
      <div className="active-match-order-meta">
        {waitLabel ? (
          <span className="wait-duration" title="Thời gian từ lúc khách đặt đến giờ">
            [{waitLabel}]
          </span>
        ) : null}
        <OrderIdCopy orderId={order.platformOrderId || order.id} />
        {order.ownerName ? <span className="meta muted">· {order.ownerName}</span> : null}
        {orderPlacedLabel(order) ? (
          <span className="meta muted">· {orderPlacedLabel(order)}</span>
        ) : null}
      </div>
      <div className="buyer-box">
        <div className="meta-label">Khách đặt</div>
        <div className="copy-grid">
          <CopyField label="First" value={firstName} className="copy-span-3" />
          <CopyField label="Last" value={lastName} className="copy-span-3" />
          <CopyField
            label="Phone"
            value={phoneCopy || phoneRaw || '—'}
            copyValue={phoneCopy}
            className="copy-span-6"
          />
          <CopyField label="Address" value={street} multiline className="copy-span-6" />
          <CopyField label="City" value={city} className="copy-span-2" />
          <CopyField label="State" value={state} className="copy-span-2" />
          <CopyField label="ZIP" value={zip} className="copy-span-2" />
        </div>
        {(order.buyerMemo || order.sellerMemo || order.internalMemo) && (
          <div className="buyer-notes">
            {order.buyerMemo ? <div className="meta muted">Buyer: {order.buyerMemo}</div> : null}
            {order.sellerMemo ? <div className="meta muted">Seller: {order.sellerMemo}</div> : null}
            {order.internalMemo ? <div className="meta muted">Internal: {order.internalMemo}</div> : null}
          </div>
        )}
        {orderMessage ? (
          <div className="order-message-line" title={orderMessage}>
            {orderMessage}
          </div>
        ) : null}
      </div>
      <div className="action-row">
        <div className="action-row-primary">
          {!alreadyPurchased ? (
            <button
              className={`btn ${variantFit?.fit === 'bad' ? 'btn-danger' : 'btn-primary'}`}
              type="button"
              disabled={marking || reloading || messaging || releasing}
              onClick={onMark}
            >
              {marking ? <Spinner /> : null}
              {marking ? 'Đang lưu…' : 'Đã mua'}
            </button>
          ) : showUnmark ? (
            <button
              className="btn-icon-view btn-unmark"
              type="button"
              disabled={marking || reloading || messaging || releasing}
              title="Gỡ đánh dấu đã mua (bấm nhầm)"
              aria-label="Chuyển về chưa mua"
              onClick={onUnmark}
            >
              {marking ? <Spinner dark /> : <RollbackIcon />}
            </button>
          ) : null}
          {!alreadyPurchased && claimHeldHere && onRelease ? (
            <button
              className="btn btn-ghost btn-release"
              type="button"
              disabled={marking || reloading || messaging || releasing}
              title={
                (order.claim?.sessionCount ?? 0) > 1
                  ? `Nhả phiên này (còn ${(order.claim?.sessionCount ?? 1) - 1} phiên khác)`
                  : 'Nhả đơn về hàng đợi'
              }
              onClick={onRelease}
            >
              {releasing ? <Spinner dark /> : null}
              {releasing ? 'Đang nhả…' : claimElapsed ? `Nhả · ${claimElapsed}` : 'Nhả'}
            </button>
          ) : null}
        </div>
        <div className="action-row-tools">
          <button
            className={`btn-icon-view btn-message${hasMessageHint ? ' has-message' : ''}`}
            type="button"
            disabled={marking || reloading || messaging || releasing}
            title={orderMessage ? `Tag lỗi: ${orderMessage}` : 'Tag lỗi đơn hàng'}
            aria-label="Tag lỗi đơn hàng"
            onClick={onMessage}
          >
            {messaging ? <Spinner dark /> : <MessageIcon />}
          </button>
          <button
            className="btn-icon-view btn-refresh"
            type="button"
            disabled={marking || reloading || messaging}
            title="Tải lại trang web hiện tại"
            aria-label="Tải lại trang"
            onClick={onReload}
          >
            {reloading ? <Spinner dark /> : <RefreshIcon />}
          </button>
        </div>
      </div>
    </section>
  );
}

/** Icon rollback — gỡ stamp «Đã mua» bấm nhầm. */
function RollbackIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path
        fill="currentColor"
        d="M12.5 8c-2.65 0-5.05.99-6.9 2.6L2 7v9h9l-3.62-3.62c1.39-1.16 3.16-1.88 5.12-1.88 3.54 0 6.55 2.31 7.6 5.5l2.37-.78C21.08 11.03 17.15 8 12.5 8z"
      />
    </svg>
  );
}

/** Icon tải lại trang. */
function RefreshIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path
        fill="currentColor"
        d="M17.65 6.35A7.95 7.95 0 0 0 12 4V1L7 6l5 5V7c2.76 0 5 2.24 5 5a5 5 0 0 1-8.66 3.54l-1.42 1.42A7 7 0 0 0 19 12c0-1.93-.78-3.68-2.05-4.95zM6 12c0-1.48.54-2.83 1.44-3.88l1.46 1.46A4.96 4.96 0 0 0 7 12c0 2.76 2.24 5 5 5v3l5-5-5-5v3a5 5 0 0 1-5-5z"
      />
    </svg>
  );
}

/** Icon xem — ghim lên Sản phẩm đang mua / xem capture. */
function ViewIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path
        fill="currentColor"
        d="M12 5C5 5 1.73 12 1.73 12S5 19 12 19s10.27-7 10.27-7S19 5 12 5zm0 12a5 5 0 1 1 0-10 5 5 0 0 1 0 10zm0-8a3 3 0 1 0 .001 6.001A3 3 0 0 0 12 9z"
      />
    </svg>
  );
}

/** Icon thông báo đơn. */
function MessageIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path
        fill="currentColor"
        d="M20 2H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h4l4 4 4-4h4c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-2 12H6v-2h12v2zm0-3H6V9h12v2zm0-3H6V6h12v2z"
      />
    </svg>
  );
}

/** Icon giỏ hàng — badge trạng thái mua trên card đơn. */
function CartIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path
        fill="currentColor"
        d="M7 18c-1.1 0-1.99.9-1.99 2S5.9 22 7 22s2-.9 2-2-.9-2-2-2zm10 0c-1.1 0-1.99.9-1.99 2S15.9 22 17 22s2-.9 2-2-.9-2-2-2zM7.16 14h9.45c.75 0 1.41-.41 1.75-1.03l3.58-6.49A1 1 0 0 0 21.08 5H5.21L4.27 2H1v2h2l3.6 7.59-1.35 2.44C4.52 15.37 5.48 17 7 17h12v-2H7.42c-.14 0-.25-.11-.25-.25l.03-.12.96-1.63z"
      />
    </svg>
  );
}

function formatCaptureTime(at?: number): string {
  if (!at || !Number.isFinite(at)) return '—';
  return new Date(at).toLocaleString('vi-VN', {
    timeZone: 'Asia/Ho_Chi_Minh',
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

/** Panel inspect session: checklist header bắt buộc + tài khoản + cookie SHEIN (đóng/mở bằng icon mắt). */
function CaptureInspectPanel({ capture }: { capture: SheinCaptureState | null }) {
  const ready = isSheinSyncCaptureReady(capture);
  const gaps = sheinCaptureReadyGaps(capture);
  const headerChecklist = getSheinCaptureHeaderChecklist(capture);
  const okCount = headerChecklist.filter((h) => h.present).length;
  const accountEmail = capture?.accountEmail?.trim() || '';
  const accountId = capture?.accountId?.trim() || '';
  const [copiedKey, setCopiedKey] = useState<string | null>(null);
  const [cookieHeader, setCookieHeader] = useState('');
  const [cookiesLoading, setCookiesLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    setCookiesLoading(true);
    void collectSheinCookieHeader().then((value) => {
      if (cancelled) return;
      setCookieHeader(value);
      setCookiesLoading(false);
    });
    return () => {
      cancelled = true;
    };
  }, [capture?.at]);

  const copyValue = (key: string, value?: string) => {
    const payload = (value || '').trim();
    if (!payload) return;
    void (async () => {
      const ok = await copyText(payload);
      if (!ok) return;
      setCopiedKey(key);
      window.setTimeout(() => {
        setCopiedKey((cur) => (cur === key ? null : cur));
      }, 1200);
    })();
  };

  const cookieCopied = copiedKey === 'cookie';
  const emailCopied = copiedKey === 'accountEmail';
  const idCopied = copiedKey === 'accountId';

  return (
    <div className="capture-inspect">
      <div className="capture-inspect-head">
        <strong>Headers & cookie session</strong>
        {capture ? (
          <span className="capture-inspect-meta muted">{formatCaptureTime(capture.at)}</span>
        ) : null}
      </div>
      <div
        className={`capture-inspect-status ${ready ? 'is-ready' : 'is-pending'}`}
        title={ready ? 'Đủ dữ liệu để Sync Order' : gaps.join(' · ')}
      >
        {ready ? 'Sẵn sàng Sync Order' : `Chưa sẵn sàng — ${gaps.join(', ')}`}
      </div>

      {!capture ? (
        <div className="meta muted">Chưa có dữ liệu capture.</div>
      ) : (
        <>
          <div className="capture-inspect-block">
            <div className="capture-inspect-label">Tài khoản SHEIN</div>
            <ul className="capture-inspect-check">
              <li className={accountEmail ? 'is-ok' : 'is-missing'}>
                <button
                  type="button"
                  className={`capture-inspect-check-btn${emailCopied ? ' is-copied' : ''}`}
                  disabled={!accountEmail}
                  title={accountEmail ? (emailCopied ? 'Đã copy email' : 'Copy email') : undefined}
                  aria-label={accountEmail ? 'Copy email tài khoản SHEIN' : 'Thiếu email tài khoản SHEIN'}
                  onClick={() => copyValue('accountEmail', accountEmail)}
                >
                  <span className="capture-inspect-check-mark" aria-hidden>
                    {emailCopied ? '✓' : accountEmail ? '✓' : '✗'}
                  </span>
                  <code>email</code>
                  <span className="capture-inspect-check-value" title={accountEmail || 'thiếu'}>
                    {emailCopied ? 'Đã copy' : accountEmail || 'thiếu'}
                  </span>
                </button>
              </li>
              <li className={accountId ? 'is-ok' : 'is-missing'}>
                <button
                  type="button"
                  className={`capture-inspect-check-btn${idCopied ? ' is-copied' : ''}`}
                  disabled={!accountId}
                  title={accountId ? (idCopied ? 'Đã copy account id' : 'Copy account id') : undefined}
                  aria-label={accountId ? 'Copy account id SHEIN' : 'Thiếu account id SHEIN'}
                  onClick={() => copyValue('accountId', accountId)}
                >
                  <span className="capture-inspect-check-mark" aria-hidden>
                    {idCopied ? '✓' : accountId ? '✓' : '✗'}
                  </span>
                  <code>account id</code>
                  <span className="capture-inspect-check-value" title={accountId || 'thiếu'}>
                    {idCopied ? 'Đã copy' : accountId || 'thiếu'}
                  </span>
                </button>
              </li>
            </ul>
          </div>
          <div className="capture-inspect-block">
            <div className="capture-inspect-label">
              Headers bắt buộc ({okCount}/{headerChecklist.length})
              <span className="capture-inspect-hint muted"> · click để copy</span>
            </div>
            <ul className="capture-inspect-check">
              {headerChecklist.map((item) => {
                const canCopy = item.present && !!item.value?.trim();
                const justCopied = copiedKey === item.key;
                return (
                  <li key={item.key} className={item.present ? 'is-ok' : 'is-missing'}>
                    <button
                      type="button"
                      className={`capture-inspect-check-btn${justCopied ? ' is-copied' : ''}`}
                      disabled={!canCopy}
                      title={
                        canCopy
                          ? justCopied
                            ? `Đã copy ${item.label}`
                            : `Copy ${item.label}`
                          : undefined
                      }
                      aria-label={
                        canCopy ? `Copy giá trị ${item.label}` : `${item.label} còn thiếu`
                      }
                      onClick={() => copyValue(item.key, item.value)}
                    >
                      <span className="capture-inspect-check-mark" aria-hidden>
                        {justCopied ? '✓' : item.present ? '✓' : '✗'}
                      </span>
                      <code>{item.label}</code>
                      <span className="capture-inspect-check-value" title={item.value || item.detail}>
                        {justCopied ? 'Đã copy' : item.detail}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </div>
        </>
      )}

      <div className="capture-inspect-block">
        <div className="capture-inspect-label">
          Cookie SHEIN
          <span className="capture-inspect-hint muted"> · click để copy</span>
        </div>
        {cookiesLoading ? (
          <div className="meta muted">Đang tải cookie…</div>
        ) : !cookieHeader ? (
          <div className="meta muted">Chưa có cookie shein.com — mở/đăng nhập SHEIN rồi thử lại.</div>
        ) : (
          <button
            type="button"
            className={`capture-inspect-cookie${cookieCopied ? ' is-copied' : ''}`}
            title={cookieCopied ? 'Đã copy Cookie header' : 'Copy Cookie header'}
            aria-label="Copy Cookie SHEIN"
            onClick={() => copyValue('cookie', cookieHeader)}
          >
            {cookieCopied ? 'Đã copy' : cookieHeader}
          </button>
        )}
      </div>
    </div>
  );
}

function OrderCard({
  order,
  isActive,
  sameCustomer,
  nowMs,
  showFulfilAccount = false,
  showPaidByUser = false,
  unmarking = false,
  actionsLocked = false,
  siblingOrderIds,
  onOpenItem,
  onMessage,
  onUnmark,
}: {
  order: FulfilOrder;
  isActive?: boolean;
  sameCustomer?: boolean;
  nowMs: number;
  /** Tab Đơn đã mua — hiện tên TK mua cạnh mã đơn. */
  showFulfilAccount?: boolean;
  /** Tab Đơn đã mua + Admin — hiện tên fulfil đã stamp mua. */
  showPaidByUser?: boolean;
  unmarking?: boolean;
  /** Đang mark/unmark đơn khác — khóa nút. */
  actionsLocked?: boolean;
  onOpenItem: (orderId: string, item: FulfilOrderItem, siblingOrderIds?: string[]) => void;
  onMessage: (order: FulfilOrder) => void;
  /** Hiện khi đã stamp — gỡ về chưa mua. */
  onUnmark?: (order: FulfilOrder) => void;
  /** Đơn anh em cùng phone+zip trong list hiện tại. */
  siblingOrderIds?: string[];
}) {
  const placedAt = orderPlacedLabel(order);
  const sellerName = order.ownerName?.trim() || null;
  const fromMs = orderWaitFromMs(order);
  const waitLabel = fromMs != null ? formatDurationShort(fromMs, nowMs) : null;
  const purchased = !!order.fulfilPurchasedAt;
  const showUnmark = canUnmarkPurchased(order);
  const fulfilAccount = showFulfilAccount
    ? order.fulfilAccountName?.trim() || null
    : null;
  const paidByName = showPaidByUser
    ? order.paidByUserName?.trim() || null
    : null;
  const waitUrgency = purchased ? 0 : purchaseWaitUrgencyLevel(fromMs, nowMs);
  const orderTags = order.issueTags ?? [];
  const orderMessage =
    orderTags.map((t) => t.label).join(', ') || order.message?.trim() || '';
  const issueDone = (order.issueStatus || '').toLowerCase() === 'done';
  const hasMessageHint = !!orderMessage;
  const revenue = orderRevenue(order);
  const revenueLabel = revenue != null ? formatMoney(revenue, order.currency) : null;
  const claim = order.claim;
  const claimRemain = claim ? formatClaimRemain(claim.expiresAt, nowMs) : '';
  const claimBlocked =
    !!claim?.userId && !claim.heldByThisSession && !claim.heldByMyOtherSession;
  const claimBadgeTitle = !purchased && claim
    ? claim.heldByThisSession
      ? `Bạn đang giữ${claimRemain ? ` · còn ${claimRemain}` : ''}${
          claim.sessionCount > 1 ? ` · ${claim.sessionCount} phiên` : ''
        }`
      : claim.heldByMyOtherSession
        ? `Bạn đang giữ ở phiên khác${
            claim.sessionCount > 1 ? ` · ${claim.sessionCount} phiên` : ''
          }`
        : `${claim.userName || 'Ai đó'} đang mua${claimRemain ? ` · còn ${claimRemain}` : ''}`
    : null;
  const claimBadge = !purchased && claim
    ? claim.heldByThisSession
      ? `Giữ${claimRemain ? ` · ${claimRemain}` : ''}${
          claim.sessionCount > 1 ? ` ·${claim.sessionCount}` : ''
        }`
      : claim.heldByMyOtherSession
        ? `Phiên khác${claim.sessionCount > 1 ? ` ·${claim.sessionCount}` : ''}`
        : `${shortClaimName(claim.userName)}${claimRemain ? ` · ${claimRemain}` : ''}`
    : null;
  return (
    <article
      className={`order-card${isActive ? ' order-card--active' : ''}${
        claimBlocked ? ' order-card--claimed' : ''
      }${claim?.heldByThisSession || claim?.heldByMyOtherSession ? ' order-card--mine' : ''}`}
    >
      <header className="order-card-header">
        <div className="order-card-top">
          <div className="order-card-top-main">
            <span
              className={
                purchased
                  ? 'badge order-purchase-badge badge-ok'
                  : `badge order-purchase-badge badge-warn purchase-wait-u${waitUrgency}`
              }
              title={
                [
                  purchased
                    ? `Đã mua${order.fulfilPurchasedAt ? ` · ${order.fulfilPurchasedAt}` : ''}`
                    : 'Chưa mua (FulfilPurchasedAt trống)',
                  waitLabel ? `Chờ ${waitLabel}` : null,
                  !purchased
                    ? `Mức cảnh báo ${waitUrgency}/${PURCHASE_WAIT_URGENCY_MAX} (mỗi ${PURCHASE_WAIT_URGENCY_HOURS}h)`
                    : null,
                ]
                  .filter(Boolean)
                  .join(' · ')
              }
              aria-label={
                purchased
                  ? `Đã mua${waitLabel ? `, chờ ${waitLabel}` : ''}`
                  : `Chưa mua${waitLabel ? `, chờ ${waitLabel}` : ''}`
              }
            >
              <CartIcon />
              {waitLabel ? <span className="wait-duration">{waitLabel}</span> : null}
            </span>
            {paidByName ? (
              <span
                className="order-fulfil-account"
                title={`Người mua: ${paidByName}`}
              >
                {paidByName}
              </span>
            ) : null}
            {fulfilAccount ? (
              <span
                className="order-fulfil-account"
                title={`Tài khoản mua hàng: ${fulfilAccount}`}
              >
                {fulfilAccount}
              </span>
            ) : null}
            <OrderIdCopy orderId={order.platformOrderId || order.id} full />
          </div>
          <div className="order-card-top-end">
            {showUnmark && onUnmark ? (
              <button
                type="button"
                className="btn-icon-view btn-unmark-card"
                disabled={unmarking || actionsLocked}
                title="Gỡ đánh dấu đã mua (bấm nhầm)"
                aria-label="Chuyển về chưa mua"
                onClick={(ev) => {
                  ev.preventDefault();
                  ev.stopPropagation();
                  onUnmark(order);
                }}
              >
                {unmarking ? <Spinner dark /> : <RollbackIcon />}
              </button>
            ) : null}
            {sameCustomer ? (
              <span
                className="badge badge-warn order-same-customer"
                title="Trùng phone + ZIP với đơn khác trong danh sách"
              >
                Cùng khách
              </span>
            ) : null}
            <button
              type="button"
              className={`btn-icon-view${hasMessageHint ? ' has-message' : ''}`}
              title={orderMessage ? `Tag lỗi: ${orderMessage}` : 'Tag lỗi đơn hàng'}
              aria-label="Tag lỗi đơn hàng"
              onClick={(ev) => {
                ev.preventDefault();
                ev.stopPropagation();
                onMessage(order);
              }}
            >
              <MessageIcon />
            </button>
          </div>
        </div>
      </header>
      <ul className="line-list">
        {order.lineItems.map((item) => {
          const url = buyUrl(item);
          return (
            <li key={item.id}>
              <button
                type="button"
                className="line-item"
                disabled={!url || claimBlocked}
                title={
                  claimBlocked
                    ? claimBadge || 'Đơn đang được fulfil khác giữ'
                    : url
                      ? 'Mở trang mua'
                      : 'Chưa có SourceUrl'
                }
                onClick={() =>
                  url &&
                  !claimBlocked &&
                  onOpenItem(order.id, item, siblingOrderIds)
                }
              >
                <ThumbSlot src={itemThumb(item)} emptyAriaHidden />
                <div className="line-body">
                  <div className="line-item-top">
                    <div className="item-title" title={item.title || undefined}>
                      {item.title || '(Không tên)'}
                    </div>
                    {revenueLabel ? (
                      <span className="order-rev" title="Rev (seller net)">
                        {revenueLabel}
                      </span>
                    ) : null}
                  </div>
                  <div
                    className="meta line-item-attrs"
                    title={[
                      item.variantAttr || null,
                      `x${item.quantity}`,
                      item.sellerSku ? `SKU: ${item.sellerSku}` : null,
                    ]
                      .filter(Boolean)
                      .join(' · ')}
                  >
                    <span className="line-item-variant">
                      {item.variantAttr?.trim() || '—'}
                    </span>
                    <span className="line-item-qty">{`x${item.quantity}`}</span>
                    {item.sellerSku ? (
                      <span className="line-item-sku">{`SKU: ${item.sellerSku}`}</span>
                    ) : null}
                  </div>
                  <div
                    className="meta muted line-item-context"
                    title={[
                      placedAt,
                      order.shopProfileName?.trim() || '—',
                      sellerName,
                    ]
                      .filter(Boolean)
                      .join(' · ')}
                  >
                    {placedAt ?? null}
                    {placedAt ? ' · ' : ''}
                    {order.shopProfileName?.trim() || '—'}
                    {sellerName ? ` · ${sellerName}` : ''}
                  </div>
                </div>
              </button>
            </li>
          );
        })}
      </ul>
      {claimBadge || orderMessage ? (
        <footer className="order-card-footer">
          {claimBadge ? (
            <span
              className={`badge ${
                claim?.heldByThisSession || claim?.heldByMyOtherSession
                  ? 'badge-ok'
                  : 'badge-muted'
              } order-claim-badge`}
              title={claimBadgeTitle || claimBadge}
            >
              {claimBadge}
            </span>
          ) : (
            <span className="order-card-footer-spacer" aria-hidden />
          )}
          {orderMessage ? (
            <button
              type="button"
              className={`tag tag-message${issueDone ? ' tag-message--done' : ''}`}
              title={issueDone ? `${orderMessage} · Đã xong` : `${orderMessage} · Đang xử lý`}
              aria-label={`Tag lỗi: ${orderMessage}`}
              onClick={(ev) => {
                ev.preventDefault();
                ev.stopPropagation();
                onMessage(order);
              }}
            >
              {issueDone ? '✓ ' : ''}
              {orderMessage}
            </button>
          ) : null}
        </footer>
      ) : null}
    </article>
  );
}

export default function App() {
  const [booting, setBooting] = useState(true);
  const [bootError, setBootError] = useState<string | null>(null);
  const [state, setState] = useState<StateResponse | null>(null);
  const [loginLoading, setLoginLoading] = useState(false);
  const [loginError, setLoginError] = useState<string | null>(null);

  const [tabStatus, setTabStatus] = useState<ActiveTabStatusResponse | null>(null);
  const [orders, setOrders] = useState<FulfilOrder[]>([]);
  /** Đơn đã stamp FulfilPurchasedAt trong ngày VN (tab «Đã mua»). */
  const [purchasedOrders, setPurchasedOrders] = useState<FulfilOrder[]>([]);
  const [ordersLoading, setOrdersLoading] = useState(false);
  const [ordersError, setOrdersError] = useState<string | null>(null);
  /** Tab list: hàng đợi chưa mua | đã mua hôm nay (cùng nguồn). */
  const [ordersTab, setOrdersTab] = useState<'needsPurchase' | 'purchasedToday'>(
    'needsPurchase',
  );
  /** Tuổi đơn tối thiểu (giờ) — mặc định 2; 0 = tất cả. */
  const [waitHoursFilter, setWaitHoursFilter] = useState(DEFAULT_WAIT_HOURS);
  /** Tìm nhanh trên hàng đợi (mã / seller / shop / SP…). */
  const [ordersQuery, setOrdersQuery] = useState('');
  /** null = đang xem hàng đợi theo tab; mảng = kết quả API search (mọi trạng thái). */
  const [searchResults, setSearchResults] = useState<FulfilOrder[] | null>(null);
  const [searchLoading, setSearchLoading] = useState(false);
  const searchSeqRef = useRef(0);
  const [marking, setMarking] = useState(false);
  /** OrderId đang gỡ stamp «Đã mua» từ card list. */
  const [unmarkingOrderId, setUnmarkingOrderId] = useState<string | null>(null);
  const [reloading, setReloading] = useState(false);
  const [messaging, setMessaging] = useState(false);
  const [messageOrder, setMessageOrder] = useState<FulfilOrder | null>(null);
  const [actionMsg, setActionMsg] = useState<string | null>(null);
  const [hubConnected, setHubConnected] = useState(false);
  const [releasing, setReleasing] = useState(false);
  const sessionIdRef = useRef<string>('');
  const myUserIdRef = useRef<string | null>(null);

  const [syncing, setSyncing] = useState(false);
  /** Background Auto Sync / Sync Order in-flight (`kb_fulfil_shein_sync_running`). */
  const [bgSyncing, setBgSyncing] = useState(false);
  const [syncStatus, setSyncStatus] = useState<SheinSyncResponse | null>(null);
  /** Checkbox local — bật/tắt auto-sync (lưu chrome.storage; lịch chạy ở background). */
  const [autoSync, setAutoSync] = useState(false);
  /** Giây còn lại tới lần Auto Sync kế tiếp (null = không đếm). */
  const [syncCountdown, setSyncCountdown] = useState<number | null>(null);
  /** Snapshot capture hiện tại (headers + màu Sync). */
  const [sheinCapture, setSheinCapture] = useState<SheinCaptureState | null>(null);
  const [showCaptureView, setShowCaptureView] = useState(false);
  /** Background đang warm-up My Orders sau login / CTA. */
  const [sheinPreparing, setSheinPreparing] = useState(false);
  const sheinCaptureReady = isSheinSyncCaptureReady(sheinCapture);
  const sheinCapturePartial = !!sheinCapture && !sheinCaptureReady;
  /** Tick chung cho badge chờ `5d9h` trên list + panel đang mua.
   * Khi có claim MM:SS thì 1s; còn lại 30s đủ cho duration thô. */
  const claimSecondTick =
    orders.some(orderNeedsClaimSecondTick) ||
    purchasedOrders.some(orderNeedsClaimSecondTick) ||
    (searchResults?.some(orderNeedsClaimSecondTick) ?? false) ||
    (!!tabStatus?.match?.order && orderNeedsClaimSecondTick(tabStatus.match.order));
  const nowMs = useSharedNow(
    orders.length > 0 ||
      purchasedOrders.length > 0 ||
      (searchResults?.length ?? 0) > 0 ||
      !!tabStatus?.match,
    claimSecondTick ? 1_000 : 30_000,
  );

  const pollRef = useRef<number | null>(null);
  const sourceIdRef = useRef<string | undefined>(undefined);
  const ordersTabRef = useRef<'needsPurchase' | 'purchasedToday'>('needsPurchase');
  ordersTabRef.current = ordersTab;
  const autoSyncTickRef = useRef<number | null>(null);
  const mainRef = useRef<HTMLDivElement>(null);
  const syncingRef = useRef(false);

  const scrollMainToTop = () => {
    mainRef.current?.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const applyLoggedOut = useCallback(() => {
    setState({ authenticated: false, user: null, productSources: [] });
    setOrders([]);
    setPurchasedOrders([]);
    setOrdersTab('needsPurchase');
    setSearchResults(null);
    setSearchLoading(false);
    setOrdersQuery('');
    setTabStatus(null);
    setSyncStatus(null);
    setAutoSync(false);
    setBgSyncing(false);
    sourceIdRef.current = undefined;
    setSyncCountdown(null);
    setMessageOrder(null);
  }, []);

  /** Đồng bộ lại state đăng nhập từ background (khi storage auth bị xóa / sync báo mất phiên). */
  const refreshAuthState = useCallback(async () => {
    try {
      const s = (await send({ type: 'GET_STATE' })) as StateResponse;
      if (!s.authenticated) {
        applyLoggedOut();
        return false;
      }
      setState(s);
      return true;
    } catch {
      applyLoggedOut();
      return false;
    }
  }, [applyLoggedOut]);

  const loadOrders = useCallback(async (productSourceId?: string) => {
    setOrdersLoading(true);
    setOrdersError(null);
    try {
      const res = (await send({
        type: 'LIST_ORDERS',
        mode: 'needsPurchase',
        productSourceId,
        platformOrderStatus: 'AWAITING_SHIPMENT',
      })) as ListOrdersResponse;
      if (!res.ok) {
        setOrders([]);
        setOrdersError(res.message || 'Không tải được đơn.');
        if (isAuthErrorMessage(res.message)) await refreshAuthState();
        return;
      }
      // Hàng đợi chỉ giữ đơn chưa đánh dấu mua (phòng API/cache lệch).
      const next = (res.orders ?? []).filter((o) => !o.fulfilPurchasedAt);
      setOrders(next);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Không tải được đơn.';
      setOrdersError(msg);
      if (isAuthErrorMessage(msg)) await refreshAuthState();
    } finally {
      setOrdersLoading(false);
    }
  }, [refreshAuthState]);

  const loadPurchasedToday = useCallback(async (productSourceId?: string) => {
    setOrdersLoading(true);
    setOrdersError(null);
    try {
      const res = (await send({
        type: 'LIST_ORDERS',
        mode: 'purchasedToday',
        productSourceId,
      })) as ListOrdersResponse;
      if (!res.ok) {
        setPurchasedOrders([]);
        setOrdersError(res.message || 'Không tải được đơn đã mua.');
        if (isAuthErrorMessage(res.message)) await refreshAuthState();
        return;
      }
      setPurchasedOrders((res.orders ?? []).filter((o) => !!o.fulfilPurchasedAt));
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Không tải được đơn đã mua.';
      setOrdersError(msg);
      if (isAuthErrorMessage(msg)) await refreshAuthState();
    } finally {
      setOrdersLoading(false);
    }
  }, [refreshAuthState]);

  const loadOrdersForTab = useCallback(
    async (
      tab: 'needsPurchase' | 'purchasedToday',
      productSourceId?: string,
    ) => {
      if (tab === 'purchasedToday') {
        await loadPurchasedToday(productSourceId);
        return;
      }
      await loadOrders(productSourceId);
    },
    [loadOrders, loadPurchasedToday],
  );

  const runOrderSearch = useCallback(
    async (keyword: string) => {
      const q = keyword.trim();
      if (q.length < SEARCH_MIN_LEN) {
        setSearchResults(null);
        setSearchLoading(false);
        return;
      }
      const seq = ++searchSeqRef.current;
      setSearchLoading(true);
      setOrdersError(null);
      try {
        const res = (await send({
          type: 'LIST_ORDERS',
          search: q,
        })) as ListOrdersResponse;
        if (seq !== searchSeqRef.current) return;
        if (!res.ok) {
          setSearchResults([]);
          setOrdersError(res.message || 'Không tìm được đơn.');
          if (isAuthErrorMessage(res.message)) await refreshAuthState();
          return;
        }
        setSearchResults(res.orders ?? []);
      } catch (err) {
        if (seq !== searchSeqRef.current) return;
        const msg = err instanceof Error ? err.message : 'Không tìm được đơn.';
        setSearchResults([]);
        setOrdersError(msg);
        if (isAuthErrorMessage(msg)) await refreshAuthState();
      } finally {
        if (seq === searchSeqRef.current) setSearchLoading(false);
      }
    },
    [refreshAuthState],
  );

  // Ô tìm ≥ 2 ký tự → API mọi trạng thái/nguồn; xóa / ngắn hơn → về hàng đợi chưa mua.
  useEffect(() => {
    const raw = ordersQuery.trim();
    if (raw.length < SEARCH_MIN_LEN) {
      searchSeqRef.current += 1;
      setSearchResults(null);
      setSearchLoading(false);
      return;
    }
    const t = window.setTimeout(() => {
      void runOrderSearch(raw);
    }, SEARCH_DEBOUNCE_MS);
    return () => window.clearTimeout(t);
  }, [ordersQuery, runOrderSearch]);

  useNotificationsRealtime({
    enabled: !!state?.authenticated,
    onClaimChanged: (payload) => {
      const sid = sessionIdRef.current;
      const uid = myUserIdRef.current;
      const purchasedIds = new Set(
        payload.items.filter((i) => i.state === 'purchased').map((i) => i.orderId),
      );
      setOrders((prev) => {
        const merged = mergeClaimPayload(prev, payload, sid, uid);
        return merged.filter((o) => !purchasedIds.has(o.id));
      });
      setPurchasedOrders((prev) => mergeClaimPayload(prev, payload, sid, uid));
      setSearchResults((prev) =>
        prev ? mergeClaimPayload(prev, payload, sid, uid) : prev,
      );
      setTabStatus((prev) => {
        if (!prev?.match) return prev;
        const hit = payload.items.find((i) => i.orderId === prev.match!.orderId);
        if (!hit) return prev;
        if (hit.state === 'purchased' || hit.state === 'released') {
          return { ...prev, match: null, variantFit: null };
        }
        const nextOrder = mergeClaimOntoOrder(prev.match.order, hit, sid, uid);
        // Phiên này không còn giữ → bỏ ghim (đồng bộ với nút Nhả đơn).
        if (!nextOrder.claim?.heldByThisSession) {
          return { ...prev, match: null, variantFit: null };
        }
        return {
          ...prev,
          match: { ...prev.match, order: nextOrder },
        };
      });
    },
    onConnectionChange: setHubConnected,
  });

  // Load session id once for claim badge flags.
  useEffect(() => {
    void getOrCreateSessionId().then((id) => {
      sessionIdRef.current = id;
    });
  }, []);

  useEffect(() => {
    myUserIdRef.current = state?.user?.id ?? null;
  }, [state?.user?.id]);

  // Fallback poll claims khi hub chưa kết nối.
  useEffect(() => {
    if (!state?.authenticated || hubConnected) return;
    if (ordersTab !== 'needsPurchase') return;
    const tick = async () => {
      const res = (await send({ type: 'LIST_ORDER_CLAIMS' })) as ListOrderClaimsResponse;
      if (!res.ok || !res.items) return;
      const items = res.items
        .filter((i): i is NonNullable<typeof i> => !!i)
        .map((i) => ({
          orderId: i.orderId,
          state: i.state,
          claimedByUserId: i.claimedByUserId,
          claimedByName: i.claimedByName,
          claimedAt: i.claimedAt,
          expiresAt: i.expiresAt,
          sessionCount: i.sessionCount,
          sessionIds: i.sessionIds,
          startedAt: i.startedAt,
        }));
      const sid = sessionIdRef.current;
      const uid = myUserIdRef.current;
      setOrders((prev) => replaceClaimsFromActiveList(prev, items, sid, uid));
    };
    void tick();
    const id = window.setInterval(() => void tick(), CLAIM_POLL_MS);
    return () => window.clearInterval(id);
  }, [state?.authenticated, hubConnected, ordersTab]);

  // Renew claim cho đơn đang ghim (TTL 15 phút).
  useEffect(() => {
    if (!state?.authenticated) return;
    const orderId = tabStatus?.match?.orderId;
    const held = tabStatus?.match?.order?.claim?.heldByThisSession;
    if (!orderId || !held || tabStatus?.match?.order?.fulfilPurchasedAt) return;
    const id = window.setInterval(() => {
      void send({ type: 'RENEW_ORDER_CLAIM', orderId });
    }, CLAIM_RENEW_MS);
    return () => window.clearInterval(id);
  }, [
    state?.authenticated,
    tabStatus?.match?.orderId,
    tabStatus?.match?.order?.claim?.heldByThisSession,
    tabStatus?.match?.order?.fulfilPurchasedAt,
  ]);

  const handleSheinSync = useCallback(async (opts?: { ensureCapture?: boolean }) => {
    if (syncingRef.current) return;
    if (!isSheinSyncCaptureReady(sheinCapture)) return;
    syncingRef.current = true;
    setSyncing(true);
    // Background sẽ xóa nextAutoSyncAt khi sync; hiện 0 trong lúc chờ.
    setSyncCountdown(0);
    // Xóa lỗi cũ ngay khi bấm Sync — khớp trạng thái với nút đang xử lý.
    setSyncStatus({ ok: true, message: 'Đang sync…' });
    try {
      const ensureCapture = opts?.ensureCapture === true;
      const res = (await send({
        type: 'SHEIN_SYNC',
        ensureCapture,
        manual: ensureCapture === true,
      })) as SheinSyncResponse;
      setSyncStatus(res);
      // Background có thể đã xóa auth (refresh fail) trong khi React vẫn tưởng đã login.
      if (!res.ok && isAuthErrorMessage(res.message)) {
        await refreshAuthState();
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Lỗi sync — thử bấm Sync Order lại.';
      setSyncStatus({ ok: false, message });
      if (isAuthErrorMessage(message)) await refreshAuthState();
    } finally {
      syncingRef.current = false;
      setSyncing(false);
      // Chu kỳ Auto Sync do background arm lại — đọc timestamp mới.
      try {
        const raw = await chrome.storage.local.get(STORAGE_KEYS.sheinNextAutoSyncAt);
        const nextAt = Number(raw[STORAGE_KEYS.sheinNextAutoSyncAt]);
        if (Number.isFinite(nextAt) && nextAt > Date.now()) {
          setSyncCountdown(Math.ceil((nextAt - Date.now()) / 1000));
        }
      } catch {
        // ignore
      }
    }
  }, [refreshAuthState, sheinCapture]);

  const handlePrepareSheinSession = useCallback(async (forceRefresh = false) => {
    if (syncingRef.current || sheinPreparing) return;
    setSyncStatus({ ok: true, message: 'Đang chuẩn bị session SHEIN…' });
    try {
      const res = (await send({
        type: 'PREPARE_SHEIN_SESSION',
        forceRefresh: forceRefresh === true,
      })) as SheinSyncResponse;
      setSyncStatus(res);
      if (!res.ok && isAuthErrorMessage(res.message)) {
        await refreshAuthState();
      }
    } catch (err) {
      const message =
        err instanceof Error ? err.message : 'Không chuẩn bị được session — thử lại.';
      setSyncStatus({ ok: false, message });
      if (isAuthErrorMessage(message)) await refreshAuthState();
    }
  }, [refreshAuthState, sheinPreparing]);

  const refreshTabStatus = useCallback(async () => {
    try {
      const res = (await send({ type: 'GET_ACTIVE_TAB_STATUS' })) as ActiveTabStatusResponse;
      setTabStatus(res);
      const nextSourceId = res.sourceId;
      if (nextSourceId !== sourceIdRef.current) {
        sourceIdRef.current = nextSourceId;
        if (nextSourceId) {
          setPurchasedOrders([]);
          void loadOrdersForTab(ordersTabRef.current, nextSourceId);
        } else {
          setOrders([]);
          setPurchasedOrders([]);
        }
      }
    } catch {
      // ignore transient
    }
  }, [loadOrdersForTab]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const s = (await send({ type: 'GET_STATE' })) as StateResponse;
        if (cancelled) return;
        setState(s);
        if (s.authenticated) {
          await refreshTabStatus();
        }
      } catch (err) {
        if (!cancelled) {
          setBootError(err instanceof Error ? err.message : 'Không khởi tạo được extension.');
        }
      } finally {
        if (!cancelled) setBooting(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [refreshTabStatus]);

  // Background xóa `kb_fulfil_auth` (refresh fail / hết phiên) → side panel phải về form login.
  useEffect(() => {
    const onAuthStorage = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local' || !changes[STORAGE_KEYS.auth]) return;
      if (changes[STORAGE_KEYS.auth].newValue == null) {
        applyLoggedOut();
      }
    };
    chrome.storage.onChanged.addListener(onAuthStorage);
    return () => chrome.storage.onChanged.removeListener(onAuthStorage);
  }, [applyLoggedOut]);

  useEffect(() => {
    if (!state?.authenticated) {
      if (pollRef.current) {
        window.clearInterval(pollRef.current);
        pollRef.current = null;
      }
      return;
    }

    void refreshTabStatus();
    pollRef.current = window.setInterval(() => {
      void refreshTabStatus();
    }, 3000);

    const onActivated = () => void refreshTabStatus();
    const onUpdated = (
      _tabId: number,
      changeInfo: chrome.tabs.TabChangeInfo,
    ) => {
      if (changeInfo.status === 'complete' || changeInfo.url) void refreshTabStatus();
    };
    const onStorage = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local') return;
      if (changes.kb_fulfil_variant_fit || changes.kb_fulfil_active_match || changes.kb_fulfil_checkout_cost) {
        void refreshTabStatus();
      }
    };
    chrome.tabs.onActivated.addListener(onActivated);
    chrome.tabs.onUpdated.addListener(onUpdated);
    chrome.storage.onChanged.addListener(onStorage);

    return () => {
      if (pollRef.current) window.clearInterval(pollRef.current);
      chrome.tabs.onActivated.removeListener(onActivated);
      chrome.tabs.onUpdated.removeListener(onUpdated);
      chrome.storage.onChanged.removeListener(onStorage);
    };
  }, [state?.authenticated, refreshTabStatus]);

  // Đọc cấu hình chu kỳ (auth check) + trạng thái checkbox Auto Sync + capture ready sau khi đăng nhập.
  useEffect(() => {
    if (!state?.authenticated) {
      setAutoSync(false);
      setSheinCapture(null);
      setShowCaptureView(false);
      setSheinPreparing(false);
      setBgSyncing(false);
      return;
    }
    let cancelled = false;

    const applyCapture = (capture: SheinCaptureState | undefined) => {
      setSheinCapture(capture ?? null);
    };

    void (async () => {
      try {
        const [cfg, stored] = await Promise.all([
          send({ type: 'GET_SHEIN_SYNC_CONFIG' }) as Promise<SheinSyncConfigResponse>,
          chrome.storage.local.get([
            STORAGE_KEYS.sheinAutoSync,
            STORAGE_KEYS.sheinCapture,
            STORAGE_KEYS.sheinSyncStatus,
            STORAGE_KEYS.sheinSyncRunning,
            STORAGE_KEYS.sheinSessionPreparing,
            STORAGE_KEYS.waitHoursFilter,
          ]),
        ]);
        if (cancelled) return;
        setAutoSync(stored[STORAGE_KEYS.sheinAutoSync] === true);
        setWaitHoursFilter(parseWaitHoursFilter(stored[STORAGE_KEYS.waitHoursFilter]));
        applyCapture(stored[STORAGE_KEYS.sheinCapture] as SheinCaptureState | undefined);
        setSheinPreparing(stored[STORAGE_KEYS.sheinSessionPreparing] === true);
        setBgSyncing(stored[STORAGE_KEYS.sheinSyncRunning] === true);
        const lastStatus = stored[STORAGE_KEYS.sheinSyncStatus] as SheinSyncResponse | undefined;
        // Đã login mà còn status "Chưa đăng nhập." từ trước → bỏ, không hiện lỗi giả.
        if (lastStatus?.message && isAuthErrorMessage(lastStatus.message)) {
          void chrome.storage.local.remove(STORAGE_KEYS.sheinSyncStatus);
        } else if (lastStatus?.message) {
          setSyncStatus(lastStatus);
        }
        if (cfg && cfg.ok === false && isAuthErrorMessage(cfg.message)) {
          await refreshAuthState();
        }
      } catch {
        // ignore — không có cấu hình thì background dùng interval mặc định
      }
    })();

    const onCaptureStorage = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local') return;
      if (changes[STORAGE_KEYS.sheinCapture]) {
        applyCapture(changes[STORAGE_KEYS.sheinCapture].newValue as SheinCaptureState | undefined);
      }
      if (changes[STORAGE_KEYS.sheinAutoSync]) {
        setAutoSync(changes[STORAGE_KEYS.sheinAutoSync].newValue === true);
      }
      if (changes[STORAGE_KEYS.sheinSessionPreparing]) {
        setSheinPreparing(changes[STORAGE_KEYS.sheinSessionPreparing].newValue === true);
      }
      if (changes[STORAGE_KEYS.sheinSyncRunning]) {
        setBgSyncing(changes[STORAGE_KEYS.sheinSyncRunning].newValue === true);
      }
      // Progress từ background (mở trang / load / capture / detail) → khớp status với nút Sync.
      if (changes[STORAGE_KEYS.sheinSyncStatus]) {
        const next = changes[STORAGE_KEYS.sheinSyncStatus].newValue as
          | SheinSyncResponse
          | undefined;
        if (next?.message) {
          setSyncStatus(next);
          if (!next.ok && isAuthErrorMessage(next.message)) {
            void refreshAuthState();
          }
        }
      }
    };
    chrome.storage.onChanged.addListener(onCaptureStorage);

    return () => {
      cancelled = true;
      chrome.storage.onChanged.removeListener(onCaptureStorage);
    };
  }, [state?.authenticated, refreshAuthState]);

  const handleAutoSyncChange = useCallback(
    (checked: boolean) => {
      if (checked && !sheinCaptureReady) return;
      setAutoSync(checked);
      void chrome.storage.local.set({ [STORAGE_KEYS.sheinAutoSync]: checked });
    },
    [sheinCaptureReady],
  );

  // Chưa sẵn sàng session → tắt Auto Sync (không để alarm chạy khi thiếu email/account id).
  useEffect(() => {
    if (sheinPreparing || sheinCaptureReady || !autoSync) return;
    setAutoSync(false);
    void chrome.storage.local.set({ [STORAGE_KEYS.sheinAutoSync]: false });
  }, [sheinCaptureReady, sheinPreparing, autoSync]);

  const handleWaitHoursFilterChange = useCallback((value: number) => {
    const next = parseWaitHoursFilter(value);
    setWaitHoursFilter(next);
    void chrome.storage.local.set({ [STORAGE_KEYS.waitHoursFilter]: next });
  }, []);

  // Auto Sync chạy ngầm trên background (chrome.alarms) — panel chỉ đếm ngược từ storage.
  useEffect(() => {
    if (!state?.authenticated || !autoSync) {
      if (autoSyncTickRef.current) {
        window.clearInterval(autoSyncTickRef.current);
        autoSyncTickRef.current = null;
      }
      setSyncCountdown(null);
      return;
    }

    const refreshCountdown = (nextAt: number | undefined | null) => {
      if (nextAt == null || !Number.isFinite(nextAt)) {
        // Đang sync hoặc chưa arm — hiện 0 nếu checkbox bật.
        setSyncCountdown(0);
        return;
      }
      const remaining = Math.ceil((nextAt - Date.now()) / 1000);
      setSyncCountdown(remaining > 0 ? remaining : 0);
    };

    void (async () => {
      const raw = await chrome.storage.local.get(STORAGE_KEYS.sheinNextAutoSyncAt);
      refreshCountdown(raw[STORAGE_KEYS.sheinNextAutoSyncAt] as number | undefined);
    })();

    autoSyncTickRef.current = window.setInterval(() => {
      void chrome.storage.local.get(STORAGE_KEYS.sheinNextAutoSyncAt).then((raw) => {
        refreshCountdown(raw[STORAGE_KEYS.sheinNextAutoSyncAt] as number | undefined);
      });
    }, 1000);

    const onStorage = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local' || !changes[STORAGE_KEYS.sheinNextAutoSyncAt]) return;
      refreshCountdown(changes[STORAGE_KEYS.sheinNextAutoSyncAt].newValue as number | undefined);
    };
    chrome.storage.onChanged.addListener(onStorage);

    return () => {
      if (autoSyncTickRef.current) {
        window.clearInterval(autoSyncTickRef.current);
        autoSyncTickRef.current = null;
      }
      chrome.storage.onChanged.removeListener(onStorage);
    };
  }, [state?.authenticated, autoSync]);

  const handleLogin = async (pin: string) => {
    setLoginLoading(true);
    setLoginError(null);
    try {
      const res = (await send({ type: 'LOGIN', pin })) as StateResponse & {
        ok?: boolean;
        message?: string;
      };
      if (res.ok === false || !res.authenticated) {
        setLoginError(res.message || 'Đăng nhập thất bại.');
        return;
      }
      setSyncStatus(null);
      setState(res);
      sourceIdRef.current = undefined;
      await refreshTabStatus();
      // Auth xong → panel đã mở; warm-up SHEIN (force) không chặn form login.
      void handlePrepareSheinSession(true);
    } catch (err) {
      setLoginError(err instanceof Error ? err.message : 'Đăng nhập thất bại.');
    } finally {
      setLoginLoading(false);
    }
  };

  const handleLogout = async () => {
    await send({ type: 'LOGOUT' });
    applyLoggedOut();
  };

  const handleOpenItem = async (
    orderId: string,
    item: FulfilOrderItem,
    siblingOrderIds?: string[],
  ) => {
    if (!buyUrl(item)) return;
    setActionMsg(null);
    const res = (await send({
      type: 'OPEN_ORDER_ITEM',
      orderId,
      siblingOrderIds,
      item: {
        id: item.id,
        sellerSku: item.sellerSku,
        sourceUrl: item.sourceUrl,
        listingUrl: item.listingUrl,
        variantAttr: item.variantAttr,
        title: item.title,
        quantity: item.quantity,
      },
    })) as {
      ok: boolean;
      message?: string;
      url?: string;
      conflict?: boolean;
      claim?: OrderClaimChangedItem;
      siblingClaims?: OrderClaimChangedItem[];
      releasedClaims?: OrderClaimChangedItem[];
      serverTime?: string;
    };
    if (!res.ok) {
      const msg = res.message || 'Không mở được sản phẩm.';
      // Đã mua — không spam toast «không thể giữ».
      if (!/đã được đánh dấu mua|không thể giữ/i.test(msg)) {
        setActionMsg(msg);
      }
      if (res.conflict) {
        void send({ type: 'LIST_ORDER_CLAIMS' }).then((r) => {
          const claims = r as ListOrderClaimsResponse;
          if (!claims.ok || !claims.items) return;
          const items = claims.items
            .filter((i): i is NonNullable<typeof i> => !!i)
            .map((i) => ({
              orderId: i.orderId,
              state: i.state,
              claimedByUserId: i.claimedByUserId,
              claimedByName: i.claimedByName,
              claimedAt: i.claimedAt,
              expiresAt: i.expiresAt,
              sessionCount: i.sessionCount,
              sessionIds: i.sessionIds,
              startedAt: i.startedAt,
            }));
          setOrders((prev) =>
            replaceClaimsFromActiveList(
              prev,
              items,
              sessionIdRef.current,
              myUserIdRef.current,
            ),
          );
        });
      }
      return;
    }
    const sid = sessionIdRef.current;
    const uid = myUserIdRef.current;
    const claimItems = claimItemsFromOpenResponse(res);
    setOrders((prev) => {
      const merged =
        claimItems.length > 0
          ? mergeClaimPayload(prev, { items: claimItems, serverTime: res.serverTime }, sid, uid)
          : prev;
      return promoteOrderToTop(merged, orderId);
    });
    setPurchasedOrders((prev) =>
      claimItems.length > 0
        ? mergeClaimPayload(prev, { items: claimItems, serverTime: res.serverTime }, sid, uid)
        : prev,
    );
    setSearchResults((prev) => {
      if (!prev) return prev;
      const merged =
        claimItems.length > 0
          ? mergeClaimPayload(prev, { items: claimItems, serverTime: res.serverTime }, sid, uid)
          : prev;
      return promoteOrderToTop(merged, orderId);
    });
    scrollMainToTop();
    void refreshTabStatus();
  };

  const handleMarkPurchased = async (match: ActiveMatch, force = false) => {
    setMarking(true);
    setActionMsg(null);
    try {
      const res = (await send({
        type: 'MARK_PURCHASED',
        orderId: match.orderId,
        force,
      })) as MarkPurchasedResponse;
      if (!res.ok) {
        if (res.needsForce && !force) {
          const ok = window.confirm(
            `${res.message || 'Đơn đang được người khác giữ.'}\nVẫn đánh dấu đã mua?`,
          );
          if (ok) {
            await handleMarkPurchased(match, true);
          } else {
            setActionMsg(res.message || 'Không đánh dấu được.');
          }
          return;
        }
        setActionMsg(res.message || 'Không đánh dấu được.');
        return;
      }
      setActionMsg(res.message);
      setTabStatus((prev) => (prev ? { ...prev, match: null, variantFit: null } : prev));
      setOrders((prev) => prev.filter((o) => o.id !== match.orderId));
      setPurchasedOrders([]);
      setSearchResults((prev) =>
        prev
          ? prev.map((o) =>
              o.id === match.orderId
                ? { ...o, fulfilPurchasedAt: o.fulfilPurchasedAt || new Date().toISOString(), claim: null }
                : o,
            )
          : prev,
      );
      const sourceId = tabStatus?.sourceId ?? sourceIdRef.current;
      if (sourceId) {
        if (ordersTabRef.current === 'purchasedToday') {
          await loadPurchasedToday(sourceId);
        } else {
          await loadOrders(sourceId);
        }
      }
      const q = ordersQuery.trim();
      if (q.length >= SEARCH_MIN_LEN) {
        await runOrderSearch(q);
      }
    } catch (err) {
      setActionMsg(err instanceof Error ? err.message : 'Không đánh dấu được.');
    } finally {
      setMarking(false);
    }
  };

  const handleReleaseClaim = async (orderId: string) => {
    setReleasing(true);
    setActionMsg(null);
    try {
      const res = (await send({
        type: 'RELEASE_ORDER_CLAIM',
        orderId,
      })) as ClaimOrderResponse;
      setActionMsg(res.message || (res.ok ? 'Đã nhả đơn.' : 'Không nhả được.'));
      if (!res.ok) return;
      const sid = sessionIdRef.current;
      const uid = myUserIdRef.current;
      if (res.claim) {
        const item: OrderClaimChangedItem = {
          orderId: res.claim.orderId,
          state: res.claim.state,
          claimedByUserId: res.claim.claimedByUserId,
          claimedByName: res.claim.claimedByName,
          claimedAt: res.claim.claimedAt,
          expiresAt: res.claim.expiresAt,
          sessionCount: res.claim.sessionCount,
          sessionIds: res.claim.sessionIds,
          startedAt: res.claim.startedAt,
        };
        setOrders((prev) =>
          prev.map((o) =>
            o.id === orderId ? mergeClaimOntoOrder(o, item, sid, uid) : o,
          ),
        );
        setSearchResults((prev) =>
          prev
            ? prev.map((o) =>
                o.id === orderId ? mergeClaimOntoOrder(o, item, sid, uid) : o,
              )
            : prev,
        );
      } else {
        setOrders((prev) =>
          prev.map((o) => (o.id === orderId ? { ...o, claim: null } : o)),
        );
        setSearchResults((prev) =>
          prev
            ? prev.map((o) => (o.id === orderId ? { ...o, claim: null } : o))
            : prev,
        );
      }
      // Bỏ ghim / bỏ chọn — không còn active-match panel hay highlight card.
      setTabStatus((prev) =>
        prev?.match?.orderId === orderId
          ? { ...prev, match: null, variantFit: null }
          : prev,
      );
    } catch (err) {
      setActionMsg(err instanceof Error ? err.message : 'Không nhả được đơn.');
    } finally {
      setReleasing(false);
    }
  };

  const handleUnmarkPurchased = async (orderId: string) => {
    setMarking(true);
    setUnmarkingOrderId(orderId);
    setActionMsg(null);
    try {
      const res = (await send({
        type: 'UNMARK_PURCHASED',
        orderId,
      })) as UnmarkPurchasedResponse;
      if (!res.ok) {
        setActionMsg(res.message || 'Không gỡ đánh dấu được.');
        return;
      }
      setActionMsg(res.message);
      // Ẩn khỏi tab đã mua; cập nhật panel + search local.
      setPurchasedOrders((prev) => prev.filter((o) => o.id !== orderId));
      setSearchResults((prev) =>
        prev
          ? prev.map((o) =>
              o.id === orderId ? { ...o, fulfilPurchasedAt: null } : o,
            )
          : prev,
      );
      setTabStatus((prev) => {
        if (!prev?.match || prev.match.orderId !== orderId) return prev;
        return {
          ...prev,
          match: {
            ...prev.match,
            order: { ...prev.match.order, fulfilPurchasedAt: null },
          },
        };
      });
      const sourceId = tabStatus?.sourceId ?? sourceIdRef.current;
      if (sourceId) {
        if (ordersTabRef.current === 'purchasedToday') {
          await loadPurchasedToday(sourceId);
        } else {
          await loadOrders(sourceId);
        }
      }
      const q = ordersQuery.trim();
      if (q.length >= SEARCH_MIN_LEN) {
        await runOrderSearch(q);
      }
    } catch (err) {
      setActionMsg(err instanceof Error ? err.message : 'Không gỡ đánh dấu được.');
    } finally {
      setMarking(false);
      setUnmarkingOrderId(null);
    }
  };

  const applyOrderIssueLocal = (orderId: string, patch: Partial<FulfilOrder>) => {
    setOrders((prev) => prev.map((o) => (o.id === orderId ? { ...o, ...patch } : o)));
    setPurchasedOrders((prev) =>
      prev.map((o) => (o.id === orderId ? { ...o, ...patch } : o)),
    );
    setSearchResults((prev) =>
      prev ? prev.map((o) => (o.id === orderId ? { ...o, ...patch } : o)) : prev,
    );
    setTabStatus((prev) => {
      if (!prev?.match || prev.match.orderId !== orderId) return prev;
      return {
        ...prev,
        match: {
          ...prev.match,
          order: { ...prev.match.order, ...patch },
        },
      };
    });
    setMessageOrder((prev) => (prev && prev.id === orderId ? { ...prev, ...patch } : prev));
  };

  const handleSaveOrderMessage = async (payload: {
    tagIds?: string[];
    status?: 'Processing' | 'Done';
  }) => {
    if (!messageOrder) return;
    const orderId = messageOrder.id;
    setMessaging(true);
    try {
      let order: FulfilOrder | undefined;
      if (payload.tagIds) {
        const tagsRes = (await send({
          type: 'SET_ORDER_ISSUE_TAGS',
          orderId,
          tagIds: payload.tagIds,
        })) as SetOrderIssueResponse;
        if (!tagsRes.ok) {
          setActionMsg(tagsRes.message || 'Không lưu được tag.');
          return;
        }
        order = tagsRes.order;
      }
      if (payload.status) {
        const stRes = (await send({
          type: 'SET_ORDER_ISSUE_STATUS',
          orderId,
          status: payload.status,
        })) as SetOrderIssueResponse;
        if (!stRes.ok) {
          setActionMsg(stRes.message || 'Không lưu được trạng thái.');
          return;
        }
        order = stRes.order ?? order;
      }
      if (order) applyOrderIssueLocal(orderId, order);
      // Không toast success mỗi lần gắn/gỡ (debounce như web) — chỉ báo lỗi.
    } catch (err) {
      setActionMsg(err instanceof Error ? err.message : 'Không lưu được tag.');
    } finally {
      setMessaging(false);
    }
  };

  const handleReloadTab = async () => {
    setReloading(true);
    setActionMsg(null);
    try {
      const res = (await send({ type: 'RELOAD_ACTIVE_TAB' })) as { ok: boolean; message?: string };
      if (!res.ok) {
        setActionMsg(res.message || 'Không tải lại được trang.');
        return;
      }
      // Đợi trang load rồi sync lại trạng thái / khớp size.
      window.setTimeout(() => void refreshTabStatus(), 1200);
    } catch (err) {
      setActionMsg(err instanceof Error ? err.message : 'Không tải lại được trang.');
    } finally {
      setReloading(false);
    }
  };

  if (booting) {
    return (
      <div className="app loading">
        <Spinner dark /> Đang tải…
      </div>
    );
  }

  if (bootError) {
    return (
      <div className="app loading">
        <div className="boot-error">
          <div>{bootError}</div>
          <button className="btn btn-primary" type="button" onClick={() => location.reload()}>
            Thử lại
          </button>
        </div>
      </div>
    );
  }

  if (!state?.authenticated) {
    return <LoginForm onSubmit={handleLogin} loading={loginLoading} error={loginError} />;
  }

  const userLabel = state.user?.fullName || state.user?.username || '—';
  const match = tabStatus?.match ?? null;
  const checkoutPhase = !!tabStatus?.url && /\/checkout/i.test(tabStatus.url);
  const searching = searchResults != null;
  const onPurchasedTab = !searching && ordersTab === 'purchasedToday';
  const isAdmin = (state.user?.roles ?? []).includes('Admin');
  const listBase = searching
    ? searchResults
    : onPurchasedTab
      ? purchasedOrders
      : orders;
  const filteredOrders = searching || onPurchasedTab
    ? listBase
    : listBase.filter((o) => {
        if (waitHoursFilter > 0 && !orderMeetsWaitHours(o, waitHoursFilter, nowMs)) return false;
        return true;
      });
  const { orders: adjacentOrders, sameCustomerIds } = sortSameCustomerAdjacent(filteredOrders);
  const displayedOrders = searching || onPurchasedTab
    ? adjacentOrders
    : sortOrdersWithClaims(adjacentOrders);
  const filteredCount = filteredOrders.length;
  const listBusy = searching ? searchLoading : ordersLoading;

  const handleOrdersTabChange = (tab: 'needsPurchase' | 'purchasedToday') => {
    if (tab === ordersTab) return;
    setOrdersTab(tab);
    setOrdersError(null);
    const sourceId = tabStatus?.sourceId ?? sourceIdRef.current;
    if (sourceId) {
      void loadOrdersForTab(tab, sourceId);
    }
  };

  const syncBusy = syncing || bgSyncing;

  const sourceBadge = shortSheinBadgeStatus({
    syncing: syncBusy,
    preparing: sheinPreparing,
    captureReady: sheinCaptureReady,
    syncStatus,
  });

  return (
    <div className="app app--main">
      <header className="header">
        <div className="brand">
          <img className="brand-logo" src="/icons/icon-48.png" alt="" />
          <div className="title">
            <div className="title-brand-row">
              <span className="title-brand">{BRAND_NAME}</span>
              <span className="title-version">v{APP_VERSION}</span>
              <a
                className="header-logout"
                href="#logout"
                role="button"
                onClick={(e) => {
                  e.preventDefault();
                  void handleLogout();
                }}
              >
                Đăng xuất
              </a>
            </div>
            <span className="title-sub">{userLabel}</span>
          </div>
        </div>
      </header>

      <div className="main" ref={mainRef}>
      <section className="card source-bar">
        <div className="source-bar-row">
          {sourceBadge &&
          (sourceBadge.tone === 'err' ||
            (syncStatus?.ok === false && !sheinCaptureReady)) ? (
            <button
              type="button"
              className="badge badge-err source-bar-badge-btn"
              title={sourceBadge.title || syncStatus?.message || 'Bấm để reload trang đơn SHEIN'}
              disabled={sheinPreparing || syncBusy}
              onClick={() => void handlePrepareSheinSession(true)}
            >
              {sheinPreparing ? 'Reload…' : 'Reload'}
            </button>
          ) : sourceBadge ? (
            <span
              className={`badge ${
                sourceBadge.tone === 'ok'
                  ? 'badge-ok'
                  : sourceBadge.tone === 'err'
                    ? 'badge-err'
                    : 'badge-warn'
              }`}
              title={sourceBadge.title}
            >
              {sourceBadge.label}
            </span>
          ) : tabStatus?.sourceName ? (
            <span className="badge badge-ok">{tabStatus.sourceName}</span>
          ) : (
            <span className="badge badge-warn">Không nhận diện</span>
          )}
          {tabStatus?.productId ? (
            <span className="meta muted source-bar-id" title={tabStatus.productId}>
              {tabStatus.productId}
            </span>
          ) : tabStatus?.status && !tabStatus.sourceName && !sheinCaptureReady ? (
            <span className="meta muted source-bar-id" title={tabStatus.status}>
              {tabStatus.status}
            </span>
          ) : null}
          <div className="source-bar-sync-actions">
            <label
              className={`source-bar-auto-sync${!sheinCaptureReady || sheinPreparing ? ' is-disabled' : ''}`}
              title={
                sheinPreparing
                  ? 'Đang chuẩn bị session SHEIN…'
                  : !sheinCaptureReady
                    ? 'Cần bắt đủ header + email + account id SHEIN trước'
                    : 'Tự động Sync Order theo chu kỳ (chạy ngầm cả khi đóng panel — cần tab SHEIN hoặc session đã bắt)'
              }
            >
              <input
                type="checkbox"
                checked={autoSync}
                disabled={!sheinCaptureReady || sheinPreparing || syncBusy}
                onChange={(e) => handleAutoSyncChange(e.target.checked)}
              />
              <span>Auto Sync</span>
            </label>
            <button
              type="button"
              className={`btn-icon-view source-bar-capture-view${
                sheinCaptureReady ? ' is-ready' : sheinCapturePartial ? ' is-pending' : ''
              }${showCaptureView ? ' is-open' : ''}`}
              title={
                showCaptureView
                  ? 'Ẩn headers & cookie session'
                  : sheinCaptureReady
                    ? 'Đã bắt đủ — xem headers & cookie session'
                    : sheinCapturePartial
                      ? `Chưa sẵn sàng Sync — ${sheinCaptureReadyGaps(sheinCapture).join(', ')}`
                      : 'Xem headers & cookie session đã bắt'
              }
              aria-label={showCaptureView ? 'Ẩn headers & cookie session' : 'Xem headers & cookie session'}
              aria-expanded={showCaptureView}
              aria-pressed={showCaptureView}
              onClick={() => setShowCaptureView((v) => !v)}
            >
              <ViewIcon />
            </button>
            {!sheinCaptureReady ? (
              <button
                type="button"
                className="btn btn-sm btn-ghost source-bar-sync"
                onClick={() => void handlePrepareSheinSession()}
                disabled={sheinPreparing || syncBusy}
                title={
                  sheinPreparing
                    ? 'Đang mở trang đơn và bắt session…'
                    : 'Mở My Orders để bắt header + email + account id'
                }
              >
                {sheinPreparing ? <Spinner /> : null}
                {sheinPreparing ? 'Đang chuẩn bị…' : 'Chuẩn bị session'}
              </button>
            ) : (
              <button
                type="button"
                className="btn btn-sm source-bar-sync btn-primary"
                onClick={() => void handleSheinSync({ ensureCapture: true })}
                disabled={syncBusy || sheinPreparing}
                title={
                  syncBusy
                    ? syncStatus?.message || 'Đang sync…'
                    : syncCountdown != null
                      ? `Auto Sync · ${Math.max(0, Math.floor(syncCountdown))}s`
                      : 'Sync đơn SHEIN'
                }
              >
                {syncBusy ? <Spinner /> : null}
                {syncBusy
                  ? 'Sync…'
                  : syncCountdown != null
                    ? `${formatSyncCountdown(syncCountdown)} Sync Order`
                    : 'Sync Order'}
              </button>
            )}
          </div>
        </div>
        {showCaptureView ? <CaptureInspectPanel capture={sheinCapture} /> : null}
      </section>

      {match ? (
        <ActiveMatchPanel
          match={match}
          marking={marking}
          reloading={reloading}
          messaging={messaging}
          variantFit={tabStatus?.variantFit ?? null}
          purchasePhase={
            !!tabStatus?.url &&
            /\/(cart|checkout|payment|pay)(\/|\?|$)/i.test(tabStatus.url)
          }
          checkoutPhase={checkoutPhase}
          checkoutCost={tabStatus?.checkoutCost ?? null}
          nowMs={nowMs}
          onMark={() => void handleMarkPurchased(match)}
          onUnmark={() => void handleUnmarkPurchased(match.orderId)}
          onRelease={
            match.order.claim?.heldByThisSession
              ? () => void handleReleaseClaim(match.orderId)
              : undefined
          }
          releasing={releasing}
          onReload={() => void handleReloadTab()}
          onMessage={() => setMessageOrder(match.order)}
        />
      ) : null}

      {actionMsg ? <div className="alert alert-ok">{actionMsg}</div> : null}
      {ordersError ? <div className="alert alert-error">{ordersError}</div> : null}

      <section className="orders-section">
        <div className="section-head">
          {searching ? (
            <div className="card-title">
              Kết quả tìm
              {tabStatus?.sourceName ? ` · ${tabStatus.sourceName}` : ''}
              {filteredCount > 0 || listBase.length > 0 ? ` (${filteredCount})` : ''}
            </div>
          ) : (
            <div className="orders-tabs" role="tablist" aria-label="Hàng đợi đơn">
              <button
                type="button"
                role="tab"
                className={`orders-tab${ordersTab === 'needsPurchase' ? ' is-active' : ''}`}
                aria-selected={ordersTab === 'needsPurchase'}
                disabled={!tabStatus?.sourceId || listBusy}
                onClick={() => handleOrdersTabChange('needsPurchase')}
              >
                Đơn cần mua
                {ordersTab === 'needsPurchase' && (filteredCount > 0 || orders.length > 0)
                  ? ` (${filteredCount}${
                      waitHoursFilter > 0 && filteredCount !== orders.length
                        ? `/${orders.length}`
                        : ''
                    })`
                  : orders.length > 0
                    ? ` (${orders.length})`
                    : ''}
              </button>
              <button
                type="button"
                role="tab"
                className={`orders-tab${ordersTab === 'purchasedToday' ? ' is-active' : ''}`}
                aria-selected={ordersTab === 'purchasedToday'}
                disabled={!tabStatus?.sourceId || listBusy}
                onClick={() => handleOrdersTabChange('purchasedToday')}
              >
                Đơn đã mua
                {ordersTab === 'purchasedToday' && purchasedOrders.length > 0
                  ? ` (${purchasedOrders.length})`
                  : ''}
              </button>
            </div>
          )}
        </div>
        {tabStatus?.sourceId ? (
          <div className="orders-toolbar">
            <div className="orders-search">
              <input
                className="orders-search-input"
                type="search"
                value={ordersQuery}
                placeholder="Tìm mọi đơn: mã, seller, shop…"
                disabled={ordersLoading && listBase.length === 0 && !searching}
                onChange={(e) => setOrdersQuery(e.target.value)}
                aria-label="Tìm đơn mọi trạng thái và nguồn"
              />
              {ordersQuery.trim() ? (
                <button
                  type="button"
                  className="orders-search-clear"
                  title="Xóa tìm kiếm"
                  aria-label="Xóa tìm kiếm"
                  onClick={() => setOrdersQuery('')}
                >
                  ×
                </button>
              ) : null}
            </div>
            {!searching && !onPurchasedTab ? (
              <label
                className="wait-filter"
                title="Chỉ hiện đơn đã chờ ít nhất N giờ (từ lúc khách đặt)"
              >
                <span className="sr-only">Chờ từ</span>
                <select
                  className="kb-select wait-filter-select"
                  value={waitHoursFilter}
                  disabled={!tabStatus?.sourceId}
                  onChange={(e) => handleWaitHoursFilterChange(Number(e.target.value))}
                >
                  {WAIT_HOURS_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </label>
            ) : null}
            <button
              className="btn btn-ghost"
              type="button"
              disabled={listBusy || (!searching && !tabStatus?.sourceId)}
              title={
                searching
                  ? 'Tải lại kết quả tìm'
                  : onPurchasedTab
                    ? isAdmin
                      ? 'Tải lại đơn đã mua hôm nay'
                      : 'Tải lại đơn bạn đã mua hôm nay'
                    : 'Tải lại hàng đợi — cũng chạy tự động sau mỗi lần Đã mua'
              }
              onClick={() => {
                if (searching) {
                  void runOrderSearch(ordersQuery);
                  return;
                }
                if (!tabStatus?.sourceId) return;
                void loadOrdersForTab(ordersTab, tabStatus.sourceId);
              }}
            >
              {listBusy ? <Spinner dark /> : null}
              Tải lại
            </button>
          </div>
        ) : null}

        {!tabStatus?.sourceId ? (
          <div className="empty">Mở trang sản phẩm thuộc nguồn đã cấu hình để lọc đơn.</div>
        ) : listBusy && listBase.length === 0 ? (
          <div className="empty">
            <Spinner dark />{' '}
            {searching
              ? 'Đang tìm…'
              : onPurchasedTab
                ? 'Đang tải đơn đã mua…'
                : 'Đang tải đơn…'}
          </div>
        ) : listBase.length === 0 ? (
          <div className="empty">
            {searching
              ? `Không khớp «${ordersQuery.trim()}» — thử từ khóa khác.`
              : onPurchasedTab
                ? isAdmin
                  ? 'Chưa có đơn đã mua hôm nay cho nguồn này.'
                  : 'Bạn chưa mua đơn nào hôm nay cho nguồn này.'
                : 'Không có đơn AWAITING_SHIPMENT chưa mua cho nguồn này.'}
          </div>
        ) : filteredOrders.length === 0 ? (
          <div className="empty">
            {`Không có đơn chờ ≥ ${waitHoursFilter} giờ — chọn «Tất cả» hoặc tải lại.`}
          </div>
        ) : (
          <div className="order-list">
            {displayedOrders.map((order) => (
              <OrderCard
                key={order.id}
                order={order}
                isActive={tabStatus?.match?.orderId === order.id}
                sameCustomer={sameCustomerIds.has(order.id)}
                nowMs={nowMs}
                showFulfilAccount={onPurchasedTab}
                showPaidByUser={onPurchasedTab && isAdmin}
                unmarking={unmarkingOrderId === order.id}
                actionsLocked={marking || (!!unmarkingOrderId && unmarkingOrderId !== order.id)}
                siblingOrderIds={siblingIdsForOrder(order, displayedOrders)}
                onOpenItem={(orderId, item, siblings) =>
                  void handleOpenItem(orderId, item, siblings)
                }
                onMessage={(o) => setMessageOrder(o)}
                onUnmark={
                  canUnmarkPurchased(order)
                    ? (o) => void handleUnmarkPurchased(o.id)
                    : undefined
                }
              />
            ))}
          </div>
        )}
      </section>
      </div>

      <OrderQuickChatModal
        order={messageOrder}
        open={!!messageOrder}
        savingTags={messaging}
        suggest={
          messageOrder &&
          tabStatus?.match?.orderId === messageOrder.id &&
          tabStatus.variantFit?.fit === 'bad'
            ? tabStatus.variantFit.detail
            : null
        }
        onClose={() => {
          if (!messaging) setMessageOrder(null);
        }}
        onOpenProduct={(orderId, item) => void handleOpenItem(orderId, item)}
        onSaveTags={(msg) => void handleSaveOrderMessage(msg)}
      />
    </div>
  );
}
