import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type MouseEvent as ReactMouseEvent,
} from 'react';
import { createPortal } from 'react-dom';
import type {
  ActiveTabStatusResponse,
  AddProductResponse,
  DeleteDraftResponse,
  ListDraftsResponse,
  ListTabCountsResponse,
  ListShopsResponse,
  StateResponse,
  WhatToSellTodayResponse,
  WhatToSellTodayResult,
} from '../shared/messages';
import type { DraftItem, FourSellerStatus, FsShopOption } from '../shared/types';
import { applyDocumentTheme, normalizeTheme, type ThemeMode } from '../shared/theme';
import { STORAGE_KEYS } from '../shared/config';
import { buildSheinSearchUrl } from '../shared/sheinSearchUrl';
import { send } from './messaging';
import { DraftRatingBlock, formatDraftPrice } from './draftMetrics';
import { loadWhatToSell, saveWhatToSell } from './whatToSellCache';
import { ProductSearchSection } from './ProductSearchSection';

function Spinner({ dark }: { dark?: boolean }) {
  return <span className={`spinner${dark ? ' spinner--dark' : ''}`} aria-hidden />;
}

function DraftStoreIcon() {
  return (
    <svg className="draft-store-icon" viewBox="0 0 16 16" width="12" height="12" aria-hidden>
      <path
        fill="currentColor"
        d="M2 3.5A1.5 1.5 0 0 1 3.5 2h9A1.5 1.5 0 0 1 14 3.5V6h.25a.75.75 0 0 1 0 1.5H14V13a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 13V7.5h-.25a.75.75 0 0 1 0-1.5H2V3.5ZM3.5 3a.5.5 0 0 0-.5.5V6h10V3.5a.5.5 0 0 0-.5-.5h-9ZM3 8v5h3.5V9.75h3V13H13V8H3Z"
      />
    </svg>
  );
}

function DraftRankIcon() {
  return (
    <svg className="draft-stat-icon" viewBox="0 0 16 16" width="12" height="12" aria-hidden>
      <path
        fill="currentColor"
        d="M8 1.25A4.25 4.25 0 0 0 5.4 8.9L4.15 14.2a.4.4 0 0 0 .58.45L8 12.85l3.27 1.8a.4.4 0 0 0 .58-.45L10.6 8.9A4.25 4.25 0 0 0 8 1.25Zm0 1.5a2.75 2.75 0 1 1 0 5.5 2.75 2.75 0 0 1 0-5.5Z"
      />
    </svg>
  );
}

function DraftFollowIcon() {
  return (
    <svg className="draft-stat-icon" viewBox="0 0 16 16" width="12" height="12" aria-hidden>
      <path
        fill="currentColor"
        d="M6.25 7.75a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Zm5.1.35a2.15 2.15 0 1 0-2.05-3.45 3.35 3.35 0 0 1 .55 3.45ZM1.5 13.15C1.5 11.1 3.45 9.6 6.25 9.6s4.75 1.5 4.75 3.55V14h-9.5v-.85Zm9.35-.05c0-1.05-.4-1.95-1.1-2.65 1.55.2 3.25 1 3.25 2.7V14h-2.15v-.9Z"
      />
    </svg>
  );
}

function DraftSlaIcon() {
  return (
    <svg className="draft-stat-icon" viewBox="0 0 16 16" width="12" height="12" aria-hidden>
      <path
        fill="currentColor"
        d="M8 1.25a6.75 6.75 0 1 0 0 13.5 6.75 6.75 0 0 0 0-13.5ZM8 2.75a5.25 5.25 0 1 1 0 10.5A5.25 5.25 0 0 1 8 2.75Zm.55 1.6a.6.6 0 0 0-1.2 0v3.35c0 .2.1.39.27.5l2.2 1.45a.6.6 0 0 0 .66-.99L8.55 7.4V4.35Z"
      />
    </svg>
  );
}

function DraftStoreLink({
  title,
  url,
}: {
  title?: string | null;
  url?: string | null;
}) {
  if (!title && !url) return null;
  const inner = (
    <>
      <DraftStoreIcon />
      {title ? <span className="draft-store-title">{title}</span> : null}
    </>
  );
  if (url) {
    return (
      <a className="draft-store" href={url} target="_blank" rel="noreferrer" title={title || url}>
        {inner}
      </a>
    );
  }
  return (
    <span className="draft-store" title={title || undefined}>
      {inner}
    </span>
  );
}

/** SLA shop NCC: `88% ≤ 7` từ dayPercentDesc. */
function formatShippingSla(
  days: number | null | undefined,
  percent: number | null | undefined,
): string {
  const hasDays = days != null && Number.isFinite(days);
  const hasPercent = percent != null && Number.isFinite(percent);
  if (hasPercent && hasDays) return `${percent}% ≤ ${days}`;
  if (hasPercent) return `${percent}%`;
  if (hasDays) return `≤ ${days}`;
  return '';
}

function formatShopRank(rank?: number | null): string {
  if (rank == null || !Number.isFinite(Number(rank))) return '';
  const n = Number(rank);
  return Number.isInteger(n) ? String(n) : n.toFixed(2).replace(/\.?0+$/, '');
}

function formatStoreAttention(count?: number | null): string {
  if (count == null || !Number.isFinite(Number(count)) || Number(count) < 0) return '';
  const n = Math.floor(Number(count));
  if (n >= 1_000_000) {
    const v = n / 1_000_000;
    return `${v >= 10 ? Math.round(v) : v.toFixed(1).replace(/\.0$/, '')}M`;
  }
  if (n >= 1000) {
    const v = n / 1000;
    return `${v >= 10 ? Math.round(v) : v.toFixed(1).replace(/\.0$/, '')}k`;
  }
  return n.toLocaleString('vi-VN');
}

function ThemeToggle({
  mode,
  disabled,
  onToggle,
}: {
  mode: ThemeMode;
  disabled?: boolean;
  onToggle: () => void;
}) {
  const isDark = mode === 'dark';
  const label = isDark ? 'Chế độ sáng' : 'Chế độ tối';
  return (
    <button
      type="button"
      className="theme-toggle"
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onToggle}
    >
      {isDark ? (
        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden>
          <path
            fill="currentColor"
            d="M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10Zm0-5a1 1 0 0 1 1 1v1a1 1 0 1 1-2 0V3a1 1 0 0 1 1-1Zm0 17a1 1 0 0 1 1 1v1a1 1 0 1 1-2 0v-1a1 1 0 0 1 1-1ZM3 11a1 1 0 1 0 0 2h1a1 1 0 1 0 0-2H3Zm17 0a1 1 0 1 0 0 2h1a1 1 0 1 0 0-2h-1ZM5.64 5.64a1 1 0 0 1 1.41 0l.71.71a1 1 0 1 1-1.41 1.41l-.71-.71a1 1 0 0 1 0-1.41Zm12.02 12.02a1 1 0 0 1 1.41 0l.71.71a1 1 0 0 1-1.41 1.41l-.71-.71a1 1 0 0 1 0-1.41ZM18.36 5.64a1 1 0 0 1 0 1.41l-.71.71a1 1 0 1 1-1.41-1.41l.71-.71a1 1 0 0 1 1.41 0ZM6.34 17.66a1 1 0 0 1 0 1.41l-.71.71a1 1 0 1 1-1.41-1.41l.71-.71a1 1 0 0 1 1.41 0Z"
          />
        </svg>
      ) : (
        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden>
          <path
            fill="currentColor"
            d="M12.1 2a1 1 0 0 1 .9 1.45A7 7 0 1 0 19.55 13a1 1 0 0 1 1.9-.45A9 9 0 1 1 12.1 2Z"
          />
        </svg>
      )}
    </button>
  );
}

/** «21:04» nếu lưu hôm nay, ngược lại «21:04 30/07» — để biết kết quả cũ tới đâu. */
function formatSavedAt(ts: number): string {
  const saved = new Date(ts);
  const now = new Date();
  const time = saved.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
  const sameDay =
    saved.getFullYear() === now.getFullYear() &&
    saved.getMonth() === now.getMonth() &&
    saved.getDate() === now.getDate();
  return sameDay
    ? time
    : `${time} ${saved.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' })}`;
}

function shopTriggerLabel(shop: FsShopOption): string {
  const profile = shop.profileName?.trim() || '';
  const name = shop.shopName?.trim() || `Shop #${shop.id}`;
  if (profile && name) return `${profile} · ${name}`;
  return profile || name;
}

function shopProductCategoryLabels(shop: FsShopOption): string[] {
  return (shop.productCategories ?? [])
    .map((c) => (typeof c === 'string' ? c : c.label)?.trim())
    .filter((t): t is string => !!t);
}

function fsBadge(fs: FourSellerStatus | null | undefined): { tone: 'ok' | 'err' | 'warn'; label: string } {
  if (!fs) return { tone: 'err', label: 'Kiểm tra phiên' };
  if (fs.isLoggedIn) return { tone: 'ok', label: '4Seller Hợp lệ' };
  if (fs.loginStatus === 'expired') return { tone: 'warn', label: '4Seller Hết hạn' };
  if (!fs.hasCookie) return { tone: 'err', label: '4Seller Chưa có cookie' };
  return { tone: 'err', label: 'Kiểm tra phiên' };
}

function PlatformAvatar({ shop }: { shop: FsShopOption }) {
  const letter = (shop.platform || '?').charAt(0).toUpperCase();
  if (shop.platformAvatarUrl) {
    return (
      <img
        className="platform-avatar"
        src={shop.platformAvatarUrl}
        alt={shop.platform || ''}
        title={shop.platform}
      />
    );
  }
  return (
    <span className="platform-avatar platform-avatar--fallback" title={shop.platform}>
      {letter}
    </span>
  );
}

/** Multi-select combobox: trigger 1 dòng; niche chỉ trong dropdown. */
function ShopCombobox({
  shops,
  selectedIds,
  disabled,
  loading,
  emptyMessage,
  onChange,
}: {
  shops: FsShopOption[];
  selectedIds: number[];
  disabled?: boolean;
  loading?: boolean;
  emptyMessage?: string;
  onChange: (ids: number[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const selectedIdsRef = useRef(selectedIds);
  selectedIdsRef.current = selectedIds;
  const selected = shops.filter((s) => selectedIds.includes(s.id));
  // Khi dropdown đang mở: giữ label cũ, không swap sang spinner (tránh nhảy layout).
  const showLoading = loading && !open;

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: PointerEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', onDoc, true);
    return () => document.removeEventListener('pointerdown', onDoc, true);
  }, [open]);

  const toggle = (id: number) => {
    const current = selectedIdsRef.current;
    onChange(current.includes(id) ? current.filter((x) => x !== id) : [...current, id]);
  };

  let placeholder = 'Chọn shop';
  if (showLoading) placeholder = 'Đang tải shop…';
  else if (selected.length === 0 && emptyMessage) placeholder = emptyMessage;

  const triggerText =
    selected.length === 0
      ? null
      : selected.length === 1
        ? shopTriggerLabel(selected[0])
        : `${selected.length} shop đã chọn`;

  return (
    <div className={`shop-combo${open ? ' shop-combo--open' : ''}`} ref={rootRef}>
      <button
        type="button"
        className="shop-combo-trigger"
        disabled={disabled || showLoading}
        onPointerDown={(e) => {
          if (e.button !== 0 || disabled || showLoading) return;
          // Tránh click “lọt” sau khi chọn option làm đóng/mở lại.
          e.preventDefault();
          setOpen((v) => !v);
        }}
        aria-haspopup="listbox"
        aria-expanded={open}
        title={
          selected.length > 1
            ? selected.map((s) => shopTriggerLabel(s)).join('\n')
            : selected[0]
              ? shopTriggerLabel(selected[0])
              : undefined
        }
      >
        <span className="shop-combo-value">
          {showLoading ? (
            <>
              <Spinner dark />
              <span className="shop-combo-placeholder">{placeholder}</span>
            </>
          ) : selected.length === 0 ? (
            <span className="shop-combo-placeholder">{placeholder}</span>
          ) : (
            <>
              <PlatformAvatar shop={selected[0]} />
              <span className="shop-combo-text">{triggerText}</span>
              <span
                className={`shop-combo-count${selected.length < 2 ? ' shop-combo-count--hidden' : ''}`}
                aria-hidden={selected.length < 2}
              >
                {selected.length}
              </span>
            </>
          )}
        </span>
        <span className="shop-combo-arrow" aria-hidden />
      </button>
      {open ? (
        <div
          className="shop-combo-dropdown"
          role="listbox"
          aria-multiselectable
          onPointerDown={(e) => {
            e.preventDefault();
            e.stopPropagation();
          }}
        >
          {loading && shops.length === 0 ? (
            <div className="shop-combo-empty">Đang tải shop…</div>
          ) : shops.length === 0 ? (
            <div className="shop-combo-empty">{emptyMessage || 'Chưa có shop Active.'}</div>
          ) : (
            shops.map((s) => {
              const checked = selectedIds.includes(s.id);
              const cats = shopProductCategoryLabels(s);
              const act = s.activeCount ?? 0;
              const profile = s.profileName?.trim() || '—';
              const name = s.shopName?.trim() || `Shop #${s.id}`;
              return (
                <div
                  key={s.id}
                  className={`shop-combo-option${checked ? ' shop-combo-option--on' : ''}`}
                  role="option"
                  aria-selected={checked}
                  onPointerDown={(e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    if (!disabled) toggle(s.id);
                  }}
                >
                  <span
                    className={`shop-combo-check${checked ? ' shop-combo-check--on' : ''}`}
                    aria-hidden
                  />
                  <PlatformAvatar shop={s} />
                  <span className="shop-combo-option-body">
                    <span className="shop-combo-option-title">
                      <span className="shop-combo-option-profile">{profile}</span>
                      <span className="shop-combo-option-name">{name}</span>
                    </span>
                    <span className="shop-combo-option-sub">
                      {cats.length > 0 ? (
                        <span className="shop-combo-option-niches">{cats.join(' · ')}</span>
                      ) : (
                        <span className="shop-combo-option-niches shop-combo-option-niches--muted">
                          Chưa có categories TTS
                        </span>
                      )}
                      <span className="shop-combo-option-dot" aria-hidden>
                        ·
                      </span>
                      <span className="shop-combo-option-active">{act} active</span>
                    </span>
                  </span>
                </div>
              );
            })
          )}
        </div>
      ) : null}
    </div>
  );
}

const LOGO_URL = chrome.runtime.getURL('icons/icon-128.png');
const BRAND_NAME = 'KBT CRAPER';
const BOOT_TIMEOUT_MS = 8000;
const RESULT_AUTO_HIDE_MS = 4500;
/** Khớp debounce sync draft backend (~12s) + buffer — tránh LIST_DRAFTS mỗi lần thêm. */
const DRAFTS_RELOAD_DEBOUNCE_MS = 14000;
/** Lần 2/3 nếu backend sync bị SyncGate trì hoãn rồi retry. */
const DRAFTS_RELOAD_FOLLOWUP_MS = [16_000, 30_000] as const;
const LIST_PAGE_SIZE = 20;

function formatPublishTabCount(published: number, limit: number): string {
  const inner = limit > 0 ? `${published}/${limit}` : String(published);
  return `(${inner})`;
}

const SITE_STATUS_LABEL: Record<string, string> = {
  draft: 'Draft',
  active: 'Active',
  inactive: 'Inactive',
  suspended: 'Suspended',
  removed: 'Removed',
  incomplete: 'Incomplete',
  private: 'Private',
  pending: 'Reviewing',
  reject: 'Reject',
  unsaleable: 'Unsaleable',
};

function formatSiteStatus(status?: string | null): string | null {
  const key = (status || '').trim().toLowerCase();
  if (!key) return null;
  return SITE_STATUS_LABEL[key] ?? status!.trim();
}

function LoginForm({
  theme,
  themeSaving,
  onToggleTheme,
  onLoggedIn,
}: {
  theme: ThemeMode;
  themeSaving?: boolean;
  onToggleTheme: () => void;
  onLoggedIn: (s: StateResponse) => void;
}) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError(null);
    try {
      const res = await send<{ ok: boolean; message?: string } & StateResponse>({
        type: 'LOGIN',
        username,
        password,
      });
      if (!res.ok) throw new Error(res.message || 'Đăng nhập thất bại.');
      onLoggedIn(res);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-card">
      <div className="login-theme">
        <ThemeToggle mode={theme} disabled={themeSaving} onToggle={onToggleTheme} />
      </div>
      <div className="login-brand">
        <img className="login-logo" src={LOGO_URL} alt={BRAND_NAME} />
        <h1 className="login-title">{BRAND_NAME}</h1>
        <p className="login-sub">Đăng nhập bằng tài khoản Seller</p>
      </div>
      <form className="login-form" onSubmit={submit}>
        <div className="field">
          <label htmlFor="kbt-username">Tên đăng nhập</label>
          <input
            id="kbt-username"
            placeholder="Nhập tên đăng nhập"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            autoComplete="username"
            autoFocus
          />
        </div>
        <div className="field">
          <label htmlFor="kbt-password">Mật khẩu</label>
          <input
            id="kbt-password"
            type="password"
            placeholder="Nhập mật khẩu"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
          />
        </div>
        {error && <div className="error">{error}</div>}
        <button
          className="btn btn-primary"
          type="submit"
          disabled={loading || !username || !password}
        >
          {loading ? (
            <>
              <Spinner />
              Đang đăng nhập...
            </>
          ) : (
            'Đăng nhập'
          )}
        </button>
      </form>
    </div>
  );
}

function formatDraftAddedAt(iso?: string): { display: string; full: string } | null {
  if (!iso) return null;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  const opts = {
    timeZone: 'Asia/Ho_Chi_Minh',
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  } as const;
  const full = d.toLocaleString('vi-VN', { ...opts, year: 'numeric' });
  const display = d.toLocaleString('vi-VN', opts).replace(',', '');
  return { display, full };
}

function DraftRow({
  item,
  index,
  deleting,
  onDelete,
}: {
  item: DraftItem;
  index: number;
  deleting?: boolean;
  onDelete: (item: DraftItem) => void;
}) {
  const [previewPos, setPreviewPos] = useState<{ top: number; left: number } | null>(null);
  const isPublished = (item.pageType || '').toLowerCase() === 'published';
  const nameClass = `draft-name${item.sourceUrl ? ' draft-name--link' : ''}${
    isPublished ? '' : ' draft-name--deletable'
  }`;
  const nameNode = item.sourceUrl ? (
    <a
      className={nameClass}
      href={item.sourceUrl}
      target="_blank"
      rel="noreferrer"
      title={item.name}
    >
      {item.name}
    </a>
  ) : (
    <div className={nameClass} title={item.name}>
      {item.name}
    </div>
  );
  const siteStatusLabel = isPublished ? formatSiteStatus(item.siteStatus) : null;
  const addedAt = formatDraftAddedAt(item.createTime);
  const priceText = formatDraftPrice(item.lowPrice, item.highPrice);
  const priceTitle = isPublished
    ? `Giá: ${priceText ?? ''}`
    : `Giá nhập (cost): ${priceText ?? ''}`;
  const hasRating = item.averageRating != null || item.reviewCount != null;
  const shippingSla = formatShippingSla(item.shippingDays, item.shippingDayPercent);
  const shopRank = formatShopRank(item.shopRank);
  const attention = formatStoreAttention(item.storeAttention);
  const hasFlags =
    item.isLocalProduct === true ||
    item.isSupportQuickShip === true ||
    !!siteStatusLabel;
  const hasMetrics = hasRating || !!priceText || hasFlags;
  const hasSupplier =
    !!item.storeTitle ||
    !!item.storeUrl ||
    !!shopRank ||
    !!attention ||
    !!shippingSla;

  const showPreview = (el: HTMLElement) => {
    const rect = el.getBoundingClientRect();
    const size = 200;
    const gap = 8;
    let left = rect.right + gap;
    let top = rect.top;
    if (left + size > window.innerWidth - 8) {
      left = Math.max(8, rect.left - size - gap);
    }
    if (top + size > window.innerHeight - 8) {
      top = Math.max(8, window.innerHeight - size - 8);
    }
    setPreviewPos({ top, left });
  };

  return (
    <div className={`draft-row${deleting ? ' draft-row--deleting' : ''}`}>
      <div className="draft-media">
        <span className="draft-index" aria-hidden="true">
          {index}
        </span>
        {item.image ? (
          <img
            className="draft-thumb"
            src={item.image}
            alt=""
            onMouseEnter={(e) => showPreview(e.currentTarget)}
            onMouseLeave={() => setPreviewPos(null)}
          />
        ) : (
          <div className="draft-thumb draft-thumb--empty" />
        )}
      </div>
      <div className="draft-info">
        <div className="draft-head">
          {nameNode}
          {!isPublished && (
            <button
              type="button"
              className="draft-delete"
              title="Xóa draft"
              aria-label={`Xóa draft ${item.name}`}
              disabled={deleting}
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                onDelete(item);
              }}
            >
              {deleting ? (
                <Spinner dark />
              ) : (
                <svg
                  className="draft-delete-icon"
                  viewBox="0 0 16 16"
                  width="12"
                  height="12"
                  aria-hidden="true"
                >
                  <path
                    fill="currentColor"
                    d="M5.5 1a.5.5 0 0 0-.5.5V2H2.5a.5.5 0 0 0 0 1H3v9.5A1.5 1.5 0 0 0 4.5 14h7a1.5 1.5 0 0 0 1.5-1.5V3h.5a.5.5 0 0 0 0-1H11v-.5a.5.5 0 0 0-.5-.5h-5zM10 2H6v-.5h4V2zM4 3h8v9.5a.5.5 0 0 1-.5.5h-7a.5.5 0 0 1-.5-.5V3zm2 2.5a.5.5 0 0 1 1 0v5a.5.5 0 0 1-1 0v-5zm3 0a.5.5 0 0 1 1 0v5a.5.5 0 0 1-1 0v-5z"
                  />
                </svg>
              )}
            </button>
          )}
        </div>
        {hasMetrics && (
          <div className="draft-metrics">
            {hasRating && (
              <DraftRatingBlock rating={item.averageRating} reviewCount={item.reviewCount} />
            )}
            {priceText && (
              <span className="draft-price" title={priceTitle}>
                {priceText}
              </span>
            )}
            {siteStatusLabel && (
              <span className={`draft-status draft-status--${(item.siteStatus || '').toLowerCase()}`}>
                {siteStatusLabel}
              </span>
            )}
            {(item.isLocalProduct === true || item.isSupportQuickShip === true) && (
              <span className="draft-metrics-flags">
                {item.isLocalProduct === true && (
                  <span className="draft-ship-badge draft-ship-badge--local" title="Hàng local US">
                    Local
                  </span>
                )}
                {item.isSupportQuickShip === true && (
                  <span
                    className="draft-ship-badge draft-ship-badge--quick"
                    title="Ít nhất một SKU mall_stock hỗ trợ ship nhanh"
                  >
                    Nhanh
                  </span>
                )}
              </span>
            )}
          </div>
        )}
        {(hasSupplier || addedAt) && (
          <div className="draft-meta">
            <div className="draft-supplier">
              <DraftStoreLink title={item.storeTitle} url={item.storeUrl} />
              {(shopRank || attention || shippingSla) && (
                <span className="draft-supplier-stats">
                  {shopRank ? (
                    <span className="draft-stat draft-stat--rank" title={`Rank shop NCC: ${shopRank}`}>
                      <DraftRankIcon />
                      <span>{shopRank}</span>
                    </span>
                  ) : null}
                  {attention ? (
                    <span className="draft-stat draft-stat--follow" title={`Follow shop NCC: ${attention}`}>
                      <DraftFollowIcon />
                      <span>{attention}</span>
                    </span>
                  ) : null}
                  {shippingSla ? (
                    <span className="draft-stat draft-stat--sla" title="Tỷ lệ giao hàng shop Shein">
                      <DraftSlaIcon />
                      <span>{shippingSla}</span>
                    </span>
                  ) : null}
                </span>
              )}
            </div>
            {addedAt && (
              <span className="draft-added-at" title={`Thêm lúc ${addedAt.full}`}>
                {addedAt.display}
              </span>
            )}
          </div>
        )}
      </div>
      {previewPos &&
        item.image &&
        createPortal(
          <div
            className="draft-thumb-preview"
            style={{ top: previewPos.top, left: previewPos.left }}
            aria-hidden="true"
          >
            <img src={item.image} alt="" />
          </div>,
          document.body,
        )}
    </div>
  );
}

export function App() {
  const [state, setState] = useState<StateResponse | null>(null);
  const [bootError, setBootError] = useState<string | null>(null);
  const [drafts, setDrafts] = useState<DraftItem[]>([]);
  const [draftsMsg, setDraftsMsg] = useState<string | null>(null);
  const [listTab, setListTab] = useState<'draft' | 'published'>('draft');
  const [panelTab, setPanelTab] = useState<'search' | 'draft' | 'published'>('search');
  const [listPage, setListPage] = useState(1);
  const [listTotal, setListTotal] = useState(0);
  const [draftTotal, setDraftTotal] = useState(0);
  const [publishLimitPublished, setPublishLimitPublished] = useState(0);
  const [publishLimitTotal, setPublishLimitTotal] = useState(0);
  const [adding, setAdding] = useState(false);
  const [tabAdding, setTabAdding] = useState(false);
  const [addResult, setAddResult] = useState<AddProductResponse | null>(null);
  const [pageMsg, setPageMsg] = useState<{ ok: boolean; message: string } | null>(null);
  const [loadingDrafts, setLoadingDrafts] = useState(false);
  const [deletingDraftId, setDeletingDraftId] = useState<string | null>(null);
  const [rechecking, setRechecking] = useState(false);
  const [tabReady, setTabReady] = useState(false);
  const [tabLoading, setTabLoading] = useState(false);
  const [alreadyAdded, setAlreadyAdded] = useState(false);
  const [addedStatus, setAddedStatus] = useState<'none' | 'partial' | 'full'>('none');
  const [addedShopHit, setAddedShopHit] = useState(0);
  const [addedShopTotal, setAddedShopTotal] = useState(0);
  const [refreshingPage, setRefreshingPage] = useState(false);
  const [addProgress, setAddProgress] = useState<{
    total: number;
    success: number;
    failed: number;
    inFlight: number;
  } | null>(null);
  const [shops, setShops] = useState<FsShopOption[]>([]);
  const [selectedShopIds, setSelectedShopIds] = useState<number[]>([]);
  const [loadingShops, setLoadingShops] = useState(false);
  const [theme, setTheme] = useState<ThemeMode>('light');
  const [themeSaving, setThemeSaving] = useState(false);
  const [shopsMsg, setShopsMsg] = useState<string | null>(null);
  const [whatToSellLoading, setWhatToSellLoading] = useState(false);
  const [whatToSellResult, setWhatToSellResult] = useState<WhatToSellTodayResult | null>(null);
  const [whatToSellMsg, setWhatToSellMsg] = useState<string | null>(null);
  const [whatToSellCollapsed, setWhatToSellCollapsed] = useState(false);
  const [whatToSellSavedAt, setWhatToSellSavedAt] = useState<number | null>(null);
  const bootedRef = useRef(false);
  /** Bỏ qua response GET_ACTIVE_TAB_STATUS cũ khi đổi tab nhanh. */
  const pollSeqRef = useRef(0);
  /** Tab/sản phẩm side panel đang bind — tránh sticky "Đã thêm" khi sang tab khác. */
  const trackedTabIdRef = useRef<number | null>(null);
  const trackedProductKeyRef = useRef<string | null>(null);
  const trackedUrlRef = useRef<string | null>(null);
  const draftsReloadTimerRef = useRef(0);
  const draftsReloadFollowUpTimersRef = useRef<number[]>([]);
  const listTabRef = useRef(listTab);
  const listPageRef = useRef(listPage);
  const panelTabRef = useRef(panelTab);
  const selectedShopIdsRef = useRef<number[]>([]);
  listTabRef.current = listTab;
  listPageRef.current = listPage;
  panelTabRef.current = panelTab;
  selectedShopIdsRef.current = selectedShopIds;
  /** Bỏ qua response SET_SELECTED_SHOPS cũ khi click chọn shop liên tiếp. */
  const shopPersistSeqRef = useRef(0);
  /** Bỏ GET_STATE cũ khi StrictMode mount 2 lần hoặc LOGIN xong trước GET_STATE. */
  const hydrateSeqRef = useRef(0);
  /** Bỏ qua kết quả đọc cache «Hôm nay bán gì?» của shop đã bỏ chọn. */
  const whatToSellLoadSeqRef = useRef(0);

  const applyTabStatus = useCallback((res: ActiveTabStatusResponse) => {
    if (res.tabId !== undefined && trackedTabIdRef.current !== null) {
      // Đổi tab giữa lúc request: bỏ response của tab cũ.
      if (res.tabId !== trackedTabIdRef.current) return;
    }
    if (res.tabId !== undefined) trackedTabIdRef.current = res.tabId;
    trackedUrlRef.current = res.url ?? null;
    trackedProductKeyRef.current = res.productKey ?? null;

    const addingNow = !!res.adding;
    setTabAdding(addingNow);
    // Request local đã xong / tab khác — đừng giữ spinner của click cũ.
    if (!addingNow) setAdding(false);

    setTabReady(!!res.ready);
    setTabLoading((res.pageReady === false || res.status === 'loading') && !addingNow);
    const status =
      res.addedStatus === 'partial' || res.addedStatus === 'full' || res.addedStatus === 'none'
        ? res.addedStatus
        : res.alreadyAdded
          ? 'full'
          : 'none';
    setAddedStatus(status);
    setAlreadyAdded(status === 'full');
    setAddedShopHit(res.addedShopHit ?? 0);
    setAddedShopTotal(res.addedShopTotal ?? 0);
    if (res.addProgress && res.addProgress.total > 0) {
      setAddProgress(res.addProgress);
    }
  }, []);

  /** Đọc ngay map in-flight theo tabId — không chờ poll (tránh ~1s hiện sai «Thêm sản phẩm»). */
  const peekTabAdding = useCallback(async (tabId: number): Promise<boolean> => {
    try {
      const raw = await chrome.storage.local.get(STORAGE_KEYS.addingByTab);
      const map = raw[STORAGE_KEYS.addingByTab] as Record<string, unknown> | undefined;
      return !!(map && map[String(tabId)]);
    } catch {
      return false;
    }
  }, []);

  const resetTabUiForSwitch = useCallback(
    (tabId?: number) => {
      if (tabId !== undefined) trackedTabIdRef.current = tabId;
      trackedUrlRef.current = null;
      trackedProductKeyRef.current = null;
      setAdding(false);
      setTabReady(false);
      setAlreadyAdded(false);
      setAddedStatus('none');
      setAddedShopHit(0);
      setAddedShopTotal(0);
      setAddResult(null);

      if (tabId === undefined) {
        setTabAdding(false);
        setTabLoading(true);
        return;
      }

      // Sync tức thì từ storage trước khi poll trả về.
      void peekTabAdding(tabId).then((addingNow) => {
        if (trackedTabIdRef.current !== tabId) return;
        setTabAdding(addingNow);
        setTabLoading(!addingNow);
      });
    },
    [peekTabAdding],
  );
  const loadDrafts = useCallback(async (opts?: {
    pageType?: 'draft' | 'published';
    pageCurrent?: number;
    shopIds?: number[];
  }) => {
    const pageType = opts?.pageType ?? listTabRef.current;
    const shopIds = opts?.shopIds ?? selectedShopIdsRef.current;
    const pageCurrent = opts?.pageCurrent ?? listPageRef.current;

    if (!shopIds.length) {
      setDrafts([]);
      setListTotal(0);
      setDraftsMsg('Chọn shop để xem listing.');
      return;
    }

    setLoadingDrafts(true);
    setDraftsMsg(null);
    try {
      const res = await send<ListDraftsResponse>({
        type: 'LIST_DRAFTS',
        pageType,
        shopIds,
        pageCurrent,
        pageSize: LIST_PAGE_SIZE,
      });
      setDrafts(res.drafts ?? []);
      setListTotal(res.total ?? 0);
      if (typeof res.pageCurrent === 'number' && res.pageCurrent > 0) {
        setListPage(res.pageCurrent);
        listPageRef.current = res.pageCurrent;
      }
      if (!res.ok) setDraftsMsg(res.message ?? 'Không tải được danh sách.');
      else if (!res.drafts?.length) {
        setDraftsMsg(
          pageType === 'published' ? 'Chưa có listing published nào.' : 'Chưa có draft nào.',
        );
      } else setDraftsMsg(null);
    } catch (err) {
      const msg = (err as Error).message;
      setDraftsMsg(msg);
      if (/hết hạn|Chưa đăng nhập/i.test(msg)) {
        setState((prev) => (prev ? { ...prev, authenticated: false, user: null } : prev));
      }
    } finally {
      setLoadingDrafts(false);
    }
  }, []);

  const loadTabCounts = useCallback(async (shopIds?: number[]) => {
    const ids = shopIds ?? selectedShopIdsRef.current;
    if (!ids.length) {
      setDraftTotal(0);
      setPublishLimitPublished(0);
      setPublishLimitTotal(0);
      return;
    }
    try {
      const res = await send<ListTabCountsResponse>({ type: 'LIST_TAB_COUNTS', shopIds: ids });
      if (!res?.ok) return;
      setDraftTotal(res.draftTotal ?? 0);
      setPublishLimitPublished(res.publishLimitPublished ?? 0);
      setPublishLimitTotal(res.publishLimitTotal ?? 0);
    } catch {
      // Giữ số cũ khi mạng lỗi — tab badge không được nhảy 0.
    }
  }, []);

  const deleteDraft = useCallback(async (item: DraftItem) => {
    if (!item.id || deletingDraftId) return;
    if ((item.pageType || '').toLowerCase() === 'published') return;
    setDeletingDraftId(item.id);
    setDraftsMsg(null);
    try {
      const res = await send<DeleteDraftResponse>({
        type: 'DELETE_DRAFT',
        id: item.id,
        sourceUrl: item.sourceUrl,
        fourSellerShopId: item.fourSellerShopId,
        pageType: item.pageType,
      });
      if (!res.ok) {
        setDraftsMsg(res.message || 'Không xóa được listing.');
        return;
      }
      const nextPage =
        drafts.length <= 1 && listPageRef.current > 1
          ? listPageRef.current - 1
          : listPageRef.current;
      setListPage(nextPage);
      listPageRef.current = nextPage;
      await loadDrafts({ pageCurrent: nextPage });
      void loadTabCounts();
    } catch (err) {
      setDraftsMsg((err as Error).message || 'Không xóa được listing.');
    } finally {
      setDeletingDraftId(null);
    }
  }, [deletingDraftId, drafts.length, loadDrafts, loadTabCounts]);

  const clearDraftReloadTimers = useCallback(() => {
    window.clearTimeout(draftsReloadTimerRef.current);
    for (const id of draftsReloadFollowUpTimersRef.current) {
      window.clearTimeout(id);
    }
    draftsReloadFollowUpTimersRef.current = [];
  }, []);

  /** Sau thêm sản phẩm: reload Draft (14s + follow-up). Đang ở Search thì không nhảy tab. */
  const scheduleLoadDrafts = useCallback(() => {
    clearDraftReloadTimers();
    const reloadDraftPage = () => {
      if (panelTabRef.current !== 'search') {
        setPanelTab('draft');
        setListTab('draft');
        listTabRef.current = 'draft';
        setListPage(1);
        listPageRef.current = 1;
      }
      void loadDrafts({ pageType: 'draft', pageCurrent: 1 });
      void loadTabCounts();
    };
    draftsReloadTimerRef.current = window.setTimeout(() => {
      reloadDraftPage();
      draftsReloadFollowUpTimersRef.current = DRAFTS_RELOAD_FOLLOWUP_MS.map((ms) =>
        window.setTimeout(reloadDraftPage, ms),
      );
    }, DRAFTS_RELOAD_DEBOUNCE_MS);
  }, [clearDraftReloadTimers, loadDrafts, loadTabCounts]);

  const loadShops = useCallback(async () => {
    setLoadingShops(true);
    setShopsMsg(null);
    try {
      const res = await send<ListShopsResponse>({ type: 'LIST_SHOPS' });
      setShops(res.shops ?? []);
      const nextIds = res.selectedShopIds ?? [];
      setSelectedShopIds(nextIds);
      selectedShopIdsRef.current = nextIds;
      if (!res.ok) {
        setShopsMsg(res.message ?? 'Không tải được danh sách shop.');
      } else if (!(res.shops?.length)) {
        setShopsMsg('Chưa có shop Active gắn 4Seller.');
      }
      setListPage(1);
      listPageRef.current = 1;
      if (panelTabRef.current !== 'search') {
        void loadDrafts({ shopIds: nextIds, pageCurrent: 1 });
      }
      void loadTabCounts(nextIds);
    } catch (err) {
      setShopsMsg((err as Error).message || 'Không tải được danh sách shop.');
    } finally {
      setLoadingShops(false);
    }
  }, [loadDrafts, loadTabCounts]);

  /** Sync auth/FS state + shop selection into the fields the UI actually renders. */
  const applyState = useCallback((res: StateResponse) => {
    bootedRef.current = true;
    const nextTheme = normalizeTheme(res.theme ?? res.user?.theme);
    setTheme(nextTheme);
    applyDocumentTheme(nextTheme);
    setState(res);
    setBootError(null);
    setShops(res.shops ?? []);
    const nextIds = res.selectedShopIds ?? [];
    setSelectedShopIds(nextIds);
    selectedShopIdsRef.current = nextIds;
    if (res.authenticated && res.shops?.length) {
      setShopsMsg(null);
    }
  }, []);

  const toggleTheme = useCallback(async () => {
    const next: ThemeMode = theme === 'dark' ? 'light' : 'dark';
    const previous = theme;
    setTheme(next);
    applyDocumentTheme(next);
    setThemeSaving(true);
    try {
      const res = await send<{ ok: boolean; theme?: ThemeMode; message?: string }>({
        type: 'SET_THEME',
        theme: next,
      });
      const confirmed = normalizeTheme(res.theme ?? next);
      setTheme(confirmed);
      applyDocumentTheme(confirmed);
      setState((prev) =>
        prev
          ? {
              ...prev,
              theme: confirmed,
              user: prev.user ? { ...prev.user, theme: confirmed } : prev.user,
            }
          : prev,
      );
    } catch {
      setTheme(previous);
      applyDocumentTheme(previous);
    } finally {
      setThemeSaving(false);
    }
  }, [theme]);

  /** After auth becomes true (login or remount), bind shops/counts without waiting for remount. */
  const hydrateAuthenticated = useCallback(
    (res: StateResponse) => {
      applyState(res);
      if (!res.authenticated) {
        setDraftTotal(0);
        setPublishLimitPublished(0);
        setPublishLimitTotal(0);
        return;
      }
      setListPage(1);
      listPageRef.current = 1;
      const shopIds = res.selectedShopIds ?? [];
      void loadTabCounts(shopIds);
      if (panelTabRef.current !== 'search') {
        void loadDrafts({ shopIds, pageCurrent: 1 });
      }
    },
    [applyState, loadDrafts, loadTabCounts],
  );

  const refresh = useCallback(async () => {
    const seq = ++hydrateSeqRef.current;
    try {
      const res = await send<StateResponse>({ type: 'GET_STATE' });
      if (seq !== hydrateSeqRef.current) return;
      hydrateAuthenticated(res);
    } catch (err) {
      if (seq !== hydrateSeqRef.current) return;
      setBootError((err as Error).message || 'Không kết nối được extension.');
      setState(null);
    }
  }, [hydrateAuthenticated]);

  const switchPanelTab = (next: 'search' | 'draft' | 'published') => {
    if (next === panelTab) return;
    setPanelTab(next);
    if (next === 'search') return;
    setListTab(next);
    listTabRef.current = next;
    setListPage(1);
    listPageRef.current = 1;
    void loadDrafts({ pageType: next, pageCurrent: 1 });
  };

  const goListPage = (nextPage: number) => {
    if (nextPage < 1 || nextPage === listPage) return;
    setListPage(nextPage);
    listPageRef.current = nextPage;
    void loadDrafts({ pageCurrent: nextPage });
  };

  const persistShopIds = async (next: number[]) => {
    const seq = ++shopPersistSeqRef.current;
    setSelectedShopIds(next);
    selectedShopIdsRef.current = next;
    setListPage(1);
    listPageRef.current = 1;
    if (panelTabRef.current !== 'search') {
      void loadDrafts({ shopIds: next, pageCurrent: 1 });
    }
    void loadTabCounts(next);
    try {
      const res = await send<{ ok: boolean; selectedShopIds?: number[] }>({
        type: 'SET_SELECTED_SHOPS',
        shopIds: next,
      });
      if (seq !== shopPersistSeqRef.current) return;
      const confirmed = res.selectedShopIds ?? next;
      const same =
        confirmed.length === next.length && confirmed.every((id, i) => id === next[i]);
      setSelectedShopIds((cur) =>
        cur.length === confirmed.length && cur.every((id, i) => id === confirmed[i])
          ? cur
          : confirmed,
      );
      selectedShopIdsRef.current = confirmed;
      if (!same) {
        if (panelTabRef.current !== 'search') {
          void loadDrafts({ shopIds: confirmed, pageCurrent: 1 });
        }
        void loadTabCounts(confirmed);
      }
    } catch {
      if (seq !== shopPersistSeqRef.current) return;
    }
  };

  const singleSelectedShop =
    selectedShopIds.length === 1
      ? shops.find((s) => Number(s.id) === Number(selectedShopIds[0])) ?? null
      : null;
  const singleShopProductCategories = singleSelectedShop
    ? shopProductCategoryLabels(singleSelectedShop)
    : [];
  const canAnalyzeWhatToSell = Boolean(singleSelectedShop?.localId) && !whatToSellLoading;
  const selectedShopLocalId = singleSelectedShop?.localId ?? '';

  // Đổi shop → nạp lại bản phân tích đã lưu của shop đó (không tự gọi API).
  useEffect(() => {
    const seq = ++whatToSellLoadSeqRef.current;
    setWhatToSellResult(null);
    setWhatToSellSavedAt(null);
    setWhatToSellMsg(null);
    setWhatToSellCollapsed(false);
    if (!selectedShopLocalId) {
      return;
    }
    void (async () => {
      const entry = await loadWhatToSell(selectedShopLocalId);
      if (seq !== whatToSellLoadSeqRef.current || !entry) return;
      setWhatToSellResult(entry.data);
      setWhatToSellSavedAt(entry.savedAt);
    })();
  }, [selectedShopLocalId]);

  /** Link keyword mở ngay trên tab đang xem — side panel giữ nguyên. */
  const openKeywordLink = (e: ReactMouseEvent<HTMLAnchorElement>, url: string) => {
    if (e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return;
    e.preventDefault();
    void send<{ ok: boolean; message?: string }>({ type: 'OPEN_URL_ACTIVE_TAB', url });
  };

  const runWhatToSellToday = async () => {
    if (!singleSelectedShop?.localId) {
      setWhatToSellMsg(
        selectedShopIds.length === 1
          ? 'Shop thiếu local id — tải lại danh sách shop.'
          : 'Chọn đúng 1 shop để phân tích.',
      );
      return;
    }
    setWhatToSellLoading(true);
    setWhatToSellMsg(null);
    setWhatToSellResult(null);
    setWhatToSellSavedAt(null);
    setWhatToSellCollapsed(false);
    try {
      const res = await send<WhatToSellTodayResponse>({
        type: 'WHAT_TO_SELL_TODAY',
        shopLocalId: singleSelectedShop.localId,
      });
      if (!res.ok || !res.data) {
        setWhatToSellMsg(res.message || 'Không phân tích được.');
        return;
      }
      setWhatToSellResult(res.data);
      setWhatToSellSavedAt(Date.now());
      await saveWhatToSell(singleSelectedShop.localId, res.data);
    } catch (err) {
      setWhatToSellMsg((err as Error).message || 'Không phân tích được.');
    } finally {
      setWhatToSellLoading(false);
    }
  };

  const pollTabStatus = useCallback(async () => {
    const seq = ++pollSeqRef.current;
    try {
      const res = await send<ActiveTabStatusResponse>({ type: 'GET_ACTIVE_TAB_STATUS' });
      if (seq !== pollSeqRef.current) return;
      applyTabStatus(res);
    } catch {
      if (seq !== pollSeqRef.current) return;
      setTabReady(false);
      setTabLoading(false);
      setAlreadyAdded(false);
    }
  }, [applyTabStatus]);

  useEffect(() => {
    void refresh();
    const timer = window.setTimeout(() => {
      if (!bootedRef.current) {
        setBootError('Không kết nối được extension. Thử tải lại side panel.');
      }
    }, BOOT_TIMEOUT_MS);
    return () => window.clearTimeout(timer);
  }, [refresh]);

  useEffect(() => {
    if (!state?.authenticated) return;
    void pollTabStatus();
    let debounceTimer = 0;
    const schedulePoll = () => {
      window.clearTimeout(debounceTimer);
      debounceTimer = window.setTimeout(() => void pollTabStatus(), 200);
    };
    // Fallback nhẹ — chủ yếu dựa vào tab events.
    const interval = window.setInterval(() => void pollTabStatus(), 3000);
    const onUpdated = (
      tabId: number,
      changeInfo: chrome.tabs.TabChangeInfo,
      tab: chrome.tabs.Tab,
    ) => {
      // Chỉ theo dõi tab đang active; gắn trackedTabId để bỏ response lệch.
      if (!tab.active) return;
      if (trackedTabIdRef.current !== null && tabId !== trackedTabIdRef.current) {
        // Tab khác vừa được active (race với onActivated) — cập nhật tracked.
        trackedTabIdRef.current = tabId;
      } else if (trackedTabIdRef.current === null) {
        trackedTabIdRef.current = tabId;
      }
      if (tabId !== trackedTabIdRef.current) return;

      if (changeInfo.status === 'loading') {
        setTabReady(false);
        setAlreadyAdded(false);
        setAddedStatus('none');
        setTabLoading(true);
      } else if (changeInfo.status === 'complete' || changeInfo.url) {
        setTabLoading(false);
        // URL đổi trong cùng tab → reset "Đã thêm" đến khi poll xong.
        if (changeInfo.url) {
          setAlreadyAdded(false);
          setAddedStatus('none');
          trackedUrlRef.current = changeInfo.url;
          trackedProductKeyRef.current = null;
        }
        schedulePoll();
      }
    };
    const onActivated = (info: chrome.tabs.TabActiveInfo) => {
      // Đổi tab ngay: invalidate poll cũ + peek in-flight + poll tức thì (không debounce).
      pollSeqRef.current += 1;
      resetTabUiForSwitch(info.tabId);
      void pollTabStatus();
    };
    const onFocus = () => {
      if (document.visibilityState === 'visible') schedulePoll();
    };
    chrome.tabs.onUpdated.addListener(onUpdated);
    chrome.tabs.onActivated.addListener(onActivated);
    document.addEventListener('visibilitychange', onFocus);
    window.addEventListener('focus', onFocus);

    // In-flight add thay đổi (tab khác / content script) → cập nhật nút ngay, không đợi interval.
    const onStorage = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local') return;
      if (changes[STORAGE_KEYS.draftsDirtyAt]) {
        scheduleLoadDrafts();
      }
      if (
        changes[STORAGE_KEYS.contentTabStatus] ||
        changes[STORAGE_KEYS.addedProductIds] ||
        changes[STORAGE_KEYS.addBatchProgress]
      ) {
        if (changes[STORAGE_KEYS.addBatchProgress]) {
          const raw = changes[STORAGE_KEYS.addBatchProgress].newValue as
            | {
                total?: number;
                success?: number;
                failed?: number;
                inFlight?: number;
              }
            | undefined;
          if (raw && typeof raw.total === 'number' && raw.total > 0) {
            setAddProgress({
              total: raw.total,
              success: raw.success ?? 0,
              failed: raw.failed ?? 0,
              inFlight: raw.inFlight ?? 0,
            });
          }
        }
        schedulePoll();
      }
      if (!changes[STORAGE_KEYS.addingByTab]) return;
      const tabId = trackedTabIdRef.current;
      if (tabId === null) return;
      const map = changes[STORAGE_KEYS.addingByTab].newValue as Record<string, unknown> | undefined;
      const addingNow = !!(map && map[String(tabId)]);
      setTabAdding(addingNow);
      if (addingNow) {
        setTabReady(false);
        setTabLoading(false);
      } else {
        schedulePoll();
      }
    };
    chrome.storage.onChanged.addListener(onStorage);

    return () => {
      window.clearInterval(interval);
      window.clearTimeout(debounceTimer);
      chrome.tabs.onUpdated.removeListener(onUpdated);
      chrome.tabs.onActivated.removeListener(onActivated);
      chrome.storage.onChanged.removeListener(onStorage);
      document.removeEventListener('visibilitychange', onFocus);
      window.removeEventListener('focus', onFocus);
    };
  }, [state?.authenticated, pollTabStatus, resetTabUiForSwitch, scheduleLoadDrafts]);

  useEffect(() => {
    return () => clearDraftReloadTimers();
  }, [clearDraftReloadTimers]);

  useEffect(() => {
    if (!addResult?.ok && !pageMsg?.ok) return;
    const timer = window.setTimeout(() => {
      setAddResult((cur) => (cur?.ok ? null : cur));
      setPageMsg((cur) => (cur?.ok ? null : cur));
    }, RESULT_AUTO_HIDE_MS);
    return () => window.clearTimeout(timer);
  }, [addResult, pageMsg]);

  const addCurrentTab = async () => {
    setAdding(true);
    setTabAdding(true);
    setAddResult(null);
    setPageMsg(null);
    const expectedTabId = trackedTabIdRef.current;
    const expectedUrl = trackedUrlRef.current;
    try {
      const res = await send<AddProductResponse>({ type: 'ADD_CURRENT_TAB' });
      if (res.ok) {
        // Chỉ đánh dấu Đã thêm nếu vẫn đang đứng trên đúng tab/URL vừa thêm.
        const stillSameTab =
          (expectedTabId === null || trackedTabIdRef.current === expectedTabId) &&
          (!res.url || !expectedUrl || res.url === expectedUrl || trackedUrlRef.current === res.url);
        if (stillSameTab) {
          setAlreadyAdded(true);
          setAddedStatus('full');
          setTabReady(false);
        }
        setAddResult(null);
        void pollTabStatus();
      } else {
        setAddResult(res);
        if (/hết hạn|Chưa đăng nhập/i.test(res.message || '')) {
          await refresh();
        } else if (trackedTabIdRef.current === expectedTabId) {
          void pollTabStatus();
        }
      }
    } catch (err) {
      const msg = (err as Error).message;
      setAddResult({ ok: false, message: msg });
      if (/hết hạn|Chưa đăng nhập/i.test(msg)) await refresh();
    } finally {
      setAdding(false);
      if (trackedTabIdRef.current === expectedTabId) {
        // BG đã clear map; poll để sync — tránh nháy sai nếu đã sang tab khác.
        void peekTabAdding(expectedTabId ?? -1).then((still) => {
          if (trackedTabIdRef.current === expectedTabId) setTabAdding(still);
        });
      }
    }
  };

  const refreshPage = async () => {
    setRefreshingPage(true);
    setPageMsg(null);
    setAddResult(null);
    setTabReady(false);
    setTabLoading(true);
    const seq = ++pollSeqRef.current;
    const tabId = trackedTabIdRef.current ?? undefined;
    try {
      const res = await send<ActiveTabStatusResponse>({
        type: 'RELOAD_ACTIVE_TAB',
        tabId,
      });
      if (seq !== pollSeqRef.current) return;
      applyTabStatus(res);
      setPageMsg({
        ok: !!res.ok,
        message:
          res.message ||
          (res.ok
            ? res.ready
              ? 'Đã tải lại trang.'
              : 'Đã tải lại trang. Chưa nhận diện được trang sản phẩm.'
            : 'Tải lại trang thất bại.'),
      });
    } catch (err) {
      if (seq !== pollSeqRef.current) return;
      setPageMsg({ ok: false, message: (err as Error).message });
      setTabLoading(false);
    } finally {
      setRefreshingPage(false);
      if (seq === pollSeqRef.current) void pollTabStatus();
    }
  };

  const recheck = async () => {
    setRechecking(true);
    setShopsMsg(null);
    try {
      await send({ type: 'REFRESH_FS_STATUS' });
      await refresh();
      const res = await send<ListShopsResponse>({ type: 'LIST_SHOPS' });
      setShops(res.shops ?? []);
      const nextIds = res.selectedShopIds ?? [];
      setSelectedShopIds(nextIds);
      selectedShopIdsRef.current = nextIds;
      if (!res.ok) setShopsMsg(res.message ?? 'Không tải được danh sách shop.');
      else if (!(res.shops?.length)) setShopsMsg('Chưa có shop Active gắn 4Seller.');
      else setShopsMsg(null);
      void loadTabCounts(nextIds);
      if (panelTabRef.current !== 'search') {
        void loadDrafts({ shopIds: nextIds, pageCurrent: 1 });
      }
    } finally {
      setRechecking(false);
    }
  };

  const logout = async () => {
    await send({ type: 'LOGOUT' });
    setDrafts([]);
    setShops([]);
    setSelectedShopIds([]);
    setShopsMsg(null);
    setAddResult(null);
    setPageMsg(null);
    await refresh();
  };

  if (!state) {
    return (
      <div className="app loading">
        {bootError ? (
          <div className="boot-error">
            <div className="error">{bootError}</div>
            <button type="button" className="btn btn-primary" onClick={() => void refresh()}>
              Thử lại
            </button>
          </div>
        ) : (
          <>
            <Spinner dark />
            Đang tải...
          </>
        )}
      </div>
    );
  }

  if (!state.authenticated) {
    return (
      <div className="app app--login">
        <LoginForm
          theme={theme}
          themeSaving={themeSaving}
          onToggleTheme={() => void toggleTheme()}
          onLoggedIn={(res) => {
            hydrateSeqRef.current += 1;
            hydrateAuthenticated(res);
          }}
        />
      </div>
    );
  }

  const fs = state.fourSeller;
  const badge = fsBadge(fs);
  const waitingPage = tabLoading || refreshingPage;
  const isAddingNow = adding || tabAdding;
  const sessionOk = !!fs?.isLoggedIn;
  const shopsOk = selectedShopIds.length > 0;
  const batchTotal = addProgress?.total ?? 0;
  const batchSuccess = addProgress?.success ?? 0;
  const batchFailed = addProgress?.failed ?? 0;
  const batchInFlight = addProgress?.inFlight ?? 0;
  const batchHasProgress = batchTotal > 0;
  const batchInProgress = batchHasProgress && batchInFlight > 0;
  const progressSuffix = batchHasProgress ? ` (${batchSuccess}/${batchTotal})` : '';
  const canAdd =
    tabReady &&
    !isAddingNow &&
    !refreshingPage &&
    sessionOk &&
    !alreadyAdded &&
    shopsOk;

  // Nhãn / title: đa-tab → «Đang gửi (2/5)» / «Đã thêm (2/5)».
  let addLabel = 'Thêm sản phẩm';
  let addTitle = 'Thêm sản phẩm từ tab hiện tại';
  let showAddSpinner = false;
  if (isAddingNow || batchInProgress) {
    addLabel = `Đang gửi${progressSuffix || '...'}`;
    addTitle = batchHasProgress
      ? `Thành công ${batchSuccess}/${batchTotal} · đang chạy ${batchInFlight}` +
        (batchFailed ? ` · lỗi ${batchFailed}` : '')
      : 'Đang thêm sản phẩm...';
    showAddSpinner = true;
  } else if (alreadyAdded) {
    addLabel = batchHasProgress && batchTotal > 1 ? `Đã thêm${progressSuffix}` : 'Đã thêm';
    addTitle = batchHasProgress
      ? `Đợt thêm: ${batchSuccess} thành công / ${batchTotal}` +
        (batchFailed ? ` · ${batchFailed} lỗi` : '')
      : 'Sản phẩm này đã được thêm cho mọi shop đang chọn';
  } else if (addedStatus === 'partial') {
    addLabel =
      addedShopTotal > 0
        ? `Thiếu shop (${addedShopHit}/${addedShopTotal})`
        : 'Thiếu shop';
    addTitle =
      addedShopTotal > 0
        ? `Đã có ở ${addedShopHit}/${addedShopTotal} shop — bấm để thêm shop còn thiếu`
        : 'Một số shop đã có sản phẩm — bấm để thêm shop còn thiếu';
  } else if (waitingPage) {
    addLabel = 'Đang tải trang...';
    addTitle = 'Đang chờ trang tải xong...';
    showAddSpinner = true;
  } else if (!sessionOk) {
    addLabel = 'Kiểm tra phiên';
    addTitle = 'Phiên 4Seller không hợp lệ — bấm Kiểm tra phiên';
  } else if (!shopsOk) {
    addLabel = 'Chọn shop';
    addTitle = 'Chọn ít nhất 1 shop';
  } else if (!tabReady) {
    addLabel = 'Thêm sản phẩm';
    addTitle = 'Cần mở trang chi tiết sản phẩm (SHEIN/Temu/…) đã cấu hình';
  }

  return (
    <div className="app app--main">
      <header className="header">
        <div className="header-row">
          <div className="brand">
            <img className="brand-logo" src={LOGO_URL} alt={BRAND_NAME} />
            <div className="title">
              <div className="title-top">
                <span className="title-brand">{BRAND_NAME}</span>
                <span className="header-actions">
                  <ThemeToggle
                    mode={theme}
                    disabled={themeSaving}
                    onToggle={() => void toggleTheme()}
                  />
                  <button type="button" className="link header-logout" onClick={logout}>
                    Đăng xuất
                  </button>
                </span>
              </div>
              <div className="title-sub-row">
                <span className="title-sub">
                  {(state.user?.fullName || state.user?.username) ? (
                    <span className="title-seller">
                      {state.user.fullName || state.user.username}
                    </span>
                  ) : (
                    'Product scraper'
                  )}
                </span>
                <button
                  type="button"
                  className={`link session-link session-link--${badge.tone}`}
                  onClick={recheck}
                  disabled={rechecking}
                  title="Kiểm tra phiên 4Seller"
                >
                  {rechecking ? (
                    <>
                      <Spinner dark />
                      Đang kiểm tra...
                    </>
                  ) : (
                    badge.label
                  )}
                </button>
              </div>
            </div>
          </div>
        </div>
        {!fs?.isLoggedIn && fs?.message && <div className="hint">{fs.message}</div>}
      </header>

      <div className="main">
        <section className="card">
          <div className="field field--compact">
            <ShopCombobox
              shops={shops}
              selectedIds={selectedShopIds}
              disabled={!fs?.isLoggedIn}
              loading={loadingShops || rechecking}
              emptyMessage={shopsMsg ?? undefined}
              onChange={(ids) => void persistShopIds(ids)}
            />
          </div>
          {shopsMsg && !loadingShops && (
            <div className={`hint${/Không|Lỗi|hết hạn|thất bại/i.test(shopsMsg) ? ' hint--err' : ''}`}>
              {shopsMsg}
              {/Không|Lỗi|hết hạn|thất bại/i.test(shopsMsg) && (
                <>
                  {' '}
                  <button type="button" className="link" onClick={() => void loadShops()}>
                    Thử lại
                  </button>
                </>
              )}
            </div>
          )}
          <div className="action-row">
            <button
              type="button"
              className={`btn ${
                alreadyAdded
                  ? 'btn-added'
                  : addedStatus === 'partial'
                    ? 'btn-added-partial'
                    : 'btn-primary'
              }`}
              onClick={addCurrentTab}
              disabled={!canAdd}
              title={addTitle}
            >
              {showAddSpinner ? <Spinner /> : null}
              {addLabel}
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-refresh"
              onClick={refreshPage}
              disabled={refreshingPage || isAddingNow}
              title="Tải lại trang web hiện tại để lấy dữ liệu mới"
            >
              {refreshingPage ? <Spinner dark /> : null}
              Tải lại trang
            </button>
          </div>
          {addResult && (
            <div className={addResult.ok ? 'result ok' : 'result err'}>{addResult.message}</div>
          )}
          {pageMsg && (
            <div className={pageMsg.ok ? 'result ok' : 'result err'}>{pageMsg.message}</div>
          )}
        </section>

        <section className="card card--wts">
          <div className="row">
            <span className="card-title card-title--wts">Hôm nay bán gì?</span>
            <div className="wts-actions">
              {whatToSellResult && (
                <button
                  type="button"
                  className="wts-toggle"
                  onClick={() => setWhatToSellCollapsed((v) => !v)}
                  aria-expanded={!whatToSellCollapsed}
                  aria-label={whatToSellCollapsed ? 'Mở rộng phân tích' : 'Thu gọn phân tích'}
                  title={whatToSellCollapsed ? 'Mở rộng phân tích' : 'Thu gọn phân tích'}
                >
                  <svg
                    viewBox="0 0 24 24"
                    width="14"
                    height="14"
                    aria-hidden
                    className={whatToSellCollapsed ? 'wts-chevron' : 'wts-chevron wts-chevron--up'}
                  >
                    <path
                      fill="currentColor"
                      d="M12 15.4 5.3 8.7a1 1 0 0 1 1.4-1.4l5.3 5.3 5.3-5.3a1 1 0 1 1 1.4 1.4L12 15.4Z"
                    />
                  </svg>
                </button>
              )}
              <button
                type="button"
                className="btn btn-primary btn-compact"
                onClick={() => void runWhatToSellToday()}
                disabled={!canAnalyzeWhatToSell}
                title={
                  selectedShopIds.length !== 1
                    ? 'Chọn đúng 1 shop'
                    : !singleSelectedShop?.localId
                      ? 'Shop thiếu local id'
                      : 'Phân tích AI + 5 link Shein'
                }
              >
                {whatToSellLoading ? (
                  <>
                    <Spinner />
                    Đang phân tích...
                  </>
                ) : (
                  'Phân tích'
                )}
              </button>
            </div>
          </div>
          {selectedShopIds.length !== 1 && (
            <div className="hint">Chọn đúng 1 shop để phân tích bestsellers.</div>
          )}
          {selectedShopIds.length === 1 && singleShopProductCategories.length === 0 && (
            <div className="hint hint--warn">
              Chưa có ProductCategories — sync TTS hoặc dùng sản phẩm đã bán.
            </div>
          )}
          {whatToSellMsg && <div className="hint hint--err">{whatToSellMsg}</div>}
          {whatToSellResult && (
            <div className="wts">
              <p className="wts-meta">
                {whatToSellSavedAt
                  ? `Lưu ${formatSavedAt(whatToSellSavedAt)}`
                  : whatToSellResult.asOfDate ?? ''}{' '}
                · {whatToSellResult.periodDays}d · Nhập $
                {whatToSellResult.minPrice}–{whatToSellResult.maxPrice}
                {whatToSellResult.avgCost != null
                  ? ` (TB $${Number(whatToSellResult.avgCost).toFixed(2)})`
                  : ''}
                {whatToSellResult.avgSalePrice != null
                  ? ` · Bán TB $${Number(whatToSellResult.avgSalePrice).toFixed(2)}`
                  : ''}
                {whatToSellResult.season ? ` · ${whatToSellResult.season}` : ''}
                {whatToSellResult.upcomingEvents?.length
                  ? ` · ${whatToSellResult.upcomingEvents[0]}`
                  : ''}
              </p>
              {!whatToSellCollapsed && (
                <>
                  <ul className="wts-points">
                    {(whatToSellResult.analysisPoints?.length
                      ? whatToSellResult.analysisPoints
                      : whatToSellResult.analysis
                        ? whatToSellResult.analysis.split('\n').filter(Boolean)
                        : []
                    ).map((point, i) => (
                      <li key={i}>{point}</li>
                    ))}
                  </ul>
                  <div className="wts-block">
                    <div className="wts-block-title wts-block-title--trend">Đang win</div>
                    <div className="wts-links">
                      {(whatToSellResult.searchesTrend ?? []).map((s, i) => {
                        const href = buildSheinSearchUrl(s.keywords);
                        return (
                        <a
                          key={`trend-${s.keywords}-${i}`}
                          className="wts-link"
                          href={href}
                          onClick={(e) => openKeywordLink(e, href)}
                        >
                          <span className="wts-link-kw">{s.keywords}</span>
                          {s.rationale && <span className="wts-link-why">{s.rationale}</span>}
                        </a>
                        );
                      })}
                    </div>
                  </div>
                  <div className="wts-block">
                    <div className="wts-block-title wts-block-title--upcoming">Sắp tới</div>
                    <div className="wts-links">
                      {(whatToSellResult.searchesUpcoming ?? []).map((s, i) => {
                        const href = buildSheinSearchUrl(s.keywords);
                        return (
                        <a
                          key={`up-${s.keywords}-${i}`}
                          className="wts-link"
                          href={href}
                          onClick={(e) => openKeywordLink(e, href)}
                        >
                          <span className="wts-link-kw">{s.keywords}</span>
                          {s.rationale && <span className="wts-link-why">{s.rationale}</span>}
                        </a>
                        );
                      })}
                    </div>
                  </div>
                </>
              )}
            </div>
          )}
        </section>

        <div className="panel-head">
          <div className="list-tabs" role="tablist" aria-label="Search, Draft hoặc Publish">
            <button
              type="button"
              role="tab"
              className={`list-tab${panelTab === 'search' ? ' list-tab--active' : ''}`}
              aria-selected={panelTab === 'search'}
              onClick={() => switchPanelTab('search')}
            >
              Search
            </button>
            <button
              type="button"
              role="tab"
              className={`list-tab${panelTab === 'draft' ? ' list-tab--active' : ''}`}
              aria-selected={panelTab === 'draft'}
              onClick={() => switchPanelTab('draft')}
            >
              Draft
              <span className="list-tab-count" title="Số draft local">
                ({draftTotal})
              </span>
            </button>
            <button
              type="button"
              role="tab"
              className={`list-tab${panelTab === 'published' ? ' list-tab--active' : ''}`}
              aria-selected={panelTab === 'published'}
              onClick={() => switchPanelTab('published')}
            >
              Publish
              <span
                className="list-tab-count"
                title="Listing active TikTok (published / limit)"
              >
                {formatPublishTabCount(publishLimitPublished, publishLimitTotal)}
              </span>
            </button>
          </div>
          {panelTab !== 'search' ? (
            <button
              type="button"
              className="link"
              onClick={() => {
                setListPage(1);
                listPageRef.current = 1;
                void loadDrafts({ pageCurrent: 1 });
                void loadTabCounts();
              }}
              disabled={loadingDrafts}
            >
              {loadingDrafts ? (
                <>
                  <Spinner dark />
                  Đang tải...
                </>
              ) : (
                'Làm mới'
              )}
            </button>
          ) : null}
        </div>
        <section className="card card--fill">
          <div className="search-panel" hidden={panelTab !== 'search'}>
            <ProductSearchSection
              shop={singleSelectedShop}
              sessionOk={sessionOk}
              selectedShopIds={selectedShopIds}
            />
          </div>
          {panelTab !== 'search' ? (
            <>
          {draftsMsg && <div className="hint">{draftsMsg}</div>}
          <div className="draft-list">
            {drafts.map((d, i) => (
              <DraftRow
                key={`${d.platform}-${d.id}`}
                item={d}
                index={(listPage - 1) * LIST_PAGE_SIZE + i + 1}
                deleting={deletingDraftId === d.id}
                onDelete={deleteDraft}
              />
            ))}
          </div>
          {listTotal > LIST_PAGE_SIZE && (
            <div className="list-pager">
              <button
                type="button"
                className="list-pager-btn"
                disabled={loadingDrafts || listPage <= 1}
                aria-label="Trang trước"
                onClick={() => goListPage(listPage - 1)}
              >
                ‹
              </button>
              <span className="list-pager-status">
                {listPage} / {Math.max(1, Math.ceil(listTotal / LIST_PAGE_SIZE))}
              </span>
              <button
                type="button"
                className="list-pager-btn"
                disabled={
                  loadingDrafts || listPage >= Math.ceil(listTotal / LIST_PAGE_SIZE)
                }
                aria-label="Trang sau"
                onClick={() => goListPage(listPage + 1)}
              >
                ›
              </button>
            </div>
          )}
            </>
          ) : null}
        </section>
      </div>
    </div>
  );
}
