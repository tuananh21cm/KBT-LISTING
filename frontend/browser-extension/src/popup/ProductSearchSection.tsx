import { useCallback, useEffect, useMemo, useRef, useState, type MouseEvent as ReactMouseEvent } from 'react';
import { createPortal } from 'react-dom';
import { STORAGE_KEYS } from '../shared/config';
import { rememberRecommendedGoodsIds } from '../shared/recommendedGoodsIds';
import type {
  ListSavedKeywordsResponse,
  ProductSearchResponse,
} from '../shared/messages';
import {
  DEFAULT_SEARCH_LIMIT,
  emptyProductSearch,
  sortBySearchScore,
  type ProductSearchCatalog,
  type ProductSearchState,
  type SavedKeywordRow,
  type SheinSearchProduct,
} from '../shared/productSearch';
import {
  getAddedStatus,
  makeAddedProductKey,
  normalizeAddedProductShopMap,
  type AddedProductShopMap,
  type AddedStatus,
} from '../shared/productId';
import {
  BODY_SIZE_OPTIONS,
  STYLE_CHIP_OPTIONS,
  normalizeSearchBodySizeCode,
  normalizeStyleCode,
  normalizeStyleCodes,
  resolveStyleAttrs,
  type SheinStyleAttr,
} from '../shared/sheinStyleAttrs';
import { buildAudienceSearchQuery } from '../shared/audienceSearchQuery';
import { buildSheinFilteredSearchUrl } from '../shared/sheinSearchUrl';
import {
  normalizeSheinSearchSort,
  SHEIN_SEARCH_SORT_OPTIONS,
  sheinSearchSortLabel,
} from '../shared/sheinSearchSort';
import type { FsShopOption } from '../shared/types';
import { send } from './messaging';
import { DraftRatingBlock, formatDraftPrice } from './draftMetrics';

function Spinner() {
  return <span className="spinner" aria-hidden />;
}

/** Popup đã nhận xong SEARCH_PRODUCTS — nếu SW vẫn để status=searching thì mở khóa UI. */
function finishSearchState(state: ProductSearchState): ProductSearchState {
  if (state.status !== 'searching') return state;
  return {
    ...state,
    status: 'error',
    message: state.message || 'Tìm kiếm không hoàn tất.',
    currentKeyword: undefined,
    currentPage: undefined,
  };
}

async function copyToClipboard(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    try {
      const el = document.createElement('textarea');
      el.value = text;
      el.setAttribute('readonly', '');
      el.style.position = 'fixed';
      el.style.left = '-9999px';
      document.body.appendChild(el);
      el.select();
      const ok = document.execCommand('copy');
      document.body.removeChild(el);
      return ok;
    } catch {
      return false;
    }
  }
}

function CopyIcon() {
  return (
    <svg viewBox="0 0 24 24" width="12" height="12" aria-hidden>
      <path
        fill="currentColor"
        d="M16 1H4a2 2 0 0 0-2 2v12h2V3h12V1Zm3 4H8a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2Zm0 16H8V7h11v14Z"
      />
    </svg>
  );
}

function CheckIcon() {
  return (
    <svg viewBox="0 0 24 24" width="12" height="12" aria-hidden>
      <path fill="currentColor" d="M9.2 16.6 4.8 12.2l-1.4 1.4 5.8 5.8 12-12-1.4-1.4z" />
    </svg>
  );
}

function ReloadIcon() {
  return (
    <svg viewBox="0 0 24 24" width="12" height="12" aria-hidden>
      <path
        fill="currentColor"
        d="M17.65 6.35A8 8 0 1 0 19.73 14h-2.08A6 6 0 1 1 12 6c1.66 0 3.14.69 4.22 1.78L13 11h8V3l-3.35 3.35Z"
      />
    </svg>
  );
}

function keywordSearchUrl(
  keyword: string,
  catalog: ProductSearchCatalog,
  learned: SheinStyleAttr[],
): string {
  const { attrIds, attrValues } = resolveStyleAttrs(catalog.styleCodes, learned);
  return buildSheinFilteredSearchUrl({
    keyword: buildAudienceSearchQuery(keyword, catalog.bodySizeCode),
    page: 1,
    priceMinUsd: catalog.priceMinUsd,
    priceMaxUsd: catalog.priceMaxUsd,
    attrIds,
    attrValues,
    preferQuickShip: !!catalog.preferQuickShip,
    sheinSort: catalog.sheinSort,
  });
}

function CopySearchUrlButton({
  keyword,
  catalog,
  learned,
}: {
  keyword: string;
  catalog: ProductSearchCatalog;
  learned: SheinStyleAttr[];
}) {
  const [copied, setCopied] = useState(false);
  const timer = useRef(0);

  useEffect(() => () => window.clearTimeout(timer.current), []);

  return (
    <button
      type="button"
      className={`search-kw-copy${copied ? ' search-kw-copy--ok' : ''}`}
      title={copied ? 'Đã copy URL' : 'Copy URL search Shein'}
      aria-label={copied ? 'Đã copy URL' : 'Copy URL search Shein'}
      onClick={(e) => {
        e.preventDefault();
        e.stopPropagation();
        const url = keywordSearchUrl(keyword, catalog, learned);
        if (!url) return;
        void copyToClipboard(url).then((ok) => {
          if (!ok) return;
          setCopied(true);
          window.clearTimeout(timer.current);
          timer.current = window.setTimeout(() => setCopied(false), 1500);
        });
      }}
    >
      {copied ? <CheckIcon /> : <CopyIcon />}
    </button>
  );
}

function KeywordTags({ row }: { row: SavedKeywordRow }) {
  const laneLabel = researchLaneLabel(row.researchLane);
  const tags = topSearchTags(row);
  return (
    <>
      {laneLabel ? (
        <span
          className={`search-kw-tag search-kw-tag--${row.researchLane === 'upcoming' ? 'upcoming' : 'now'}`}
          title={
            row.researchLane === 'upcoming'
              ? 'Shop này bán gì · Đón đầu'
              : 'Shop này bán gì · List ngay'
          }
        >
          {laneLabel}
        </span>
      ) : null}
      {tags.map((tag) => (
        <span key={tag} className="search-kw-tag">
          {tag}
        </span>
      ))}
    </>
  );
}

function KeywordCombo({
  keywords,
  selectedKeyword,
  onSelect,
  disabled,
  loading,
  emptyMessage,
}: {
  keywords: SavedKeywordRow[];
  selectedKeyword: string;
  onSelect: (keyword: string) => void;
  disabled?: boolean;
  loading?: boolean;
  emptyMessage?: string | null;
}) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const dropRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const [menuPos, setMenuPos] = useState<{
    left: number;
    width: number;
    top?: number;
    bottom?: number;
    maxHeight: number;
  } | null>(null);

  const selected = keywords.find((k) => k.keyword === selectedKeyword) ?? null;
  // Giống ShopCombobox: đang mở thì không khóa trigger / không swap spinner.
  const showLoading = !!loading && !open;

  useEffect(() => {
    if (disabled) setOpen(false);
  }, [disabled]);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: PointerEvent) => {
      const t = e.target as Node;
      if (rootRef.current?.contains(t) || dropRef.current?.contains(t)) return;
      setOpen(false);
    };
    document.addEventListener('pointerdown', onDoc, true);
    return () => document.removeEventListener('pointerdown', onDoc, true);
  }, [open]);

  useEffect(() => {
    if (!open) {
      setMenuPos(null);
      return;
    }
    const place = () => {
      const el = triggerRef.current;
      if (!el) return;
      const r = el.getBoundingClientRect();
      const gap = 4;
      const below = window.innerHeight - r.bottom - 8;
      const above = r.top - 8;
      const openUp = below < 120 && above > below;
      const maxHeight = Math.min(240, Math.max(96, openUp ? above : below));
      setMenuPos(
        openUp
          ? { left: r.left, width: r.width, bottom: window.innerHeight - r.top + gap, maxHeight }
          : { left: r.left, width: r.width, top: r.bottom + gap, maxHeight },
      );
    };
    place();
    window.addEventListener('resize', place);
    return () => window.removeEventListener('resize', place);
  }, [open, keywords.length]);

  const placeholder = showLoading
    ? 'Đang tải keyword…'
    : keywords.length === 0
      ? emptyMessage || 'Chưa có keyword đã lưu'
      : 'Chọn keyword';

  return (
    <div className={`shop-combo search-kw-combo${open ? ' shop-combo--open' : ''}`} ref={rootRef}>
      <button
        ref={triggerRef}
        type="button"
        className="shop-combo-trigger"
        disabled={disabled || showLoading}
        onPointerDown={(e) => {
          if (e.button !== 0 || disabled || showLoading) return;
          e.preventDefault();
          setOpen((v) => !v);
        }}
        title={
          selected
            ? [selected.keyword, researchLaneLabel(selected.researchLane)].filter(Boolean).join(' · ')
            : undefined
        }
        aria-haspopup="listbox"
        aria-expanded={open}
      >
        <span className="shop-combo-value">
          {showLoading ? (
            <>
              <Spinner />
              <span className="shop-combo-placeholder">{placeholder}</span>
            </>
          ) : !selected ? (
            <span className="shop-combo-placeholder">{placeholder}</span>
          ) : (
            <span className="shop-combo-text">{selected.keyword}</span>
          )}
          {keywords.length > 0 && !showLoading ? (
            <span className="shop-combo-count" title={`${keywords.length} keyword`}>
              {keywords.length}
            </span>
          ) : null}
        </span>
        <span className="shop-combo-arrow" aria-hidden />
      </button>
      {open && menuPos
        ? createPortal(
            <div
              ref={dropRef}
              className="shop-combo-dropdown search-kw-combo-menu"
              role="listbox"
              style={{
                left: menuPos.left,
                width: menuPos.width,
                top: menuPos.top,
                bottom: menuPos.bottom,
                maxHeight: menuPos.maxHeight,
              }}
              onPointerDown={(e) => {
                e.preventDefault();
                e.stopPropagation();
              }}
            >
              {loading && keywords.length === 0 ? (
                <div className="shop-combo-empty">Đang tải keyword…</div>
              ) : keywords.length === 0 ? (
                <div className="shop-combo-empty">{emptyMessage || 'Chưa có keyword đã lưu'}</div>
              ) : (
                keywords.map((row) => {
                  const on = row.keyword === selectedKeyword;
                  return (
                    <div
                      key={row.keyword}
                      className={`shop-combo-option${on ? ' shop-combo-option--on' : ''}`}
                      role="option"
                      aria-selected={on}
                      onPointerDown={(e) => {
                        e.preventDefault();
                        e.stopPropagation();
                        if (disabled) return;
                        onSelect(row.keyword);
                        setOpen(false);
                      }}
                    >
                      <span className="shop-combo-option-body">
                        <span className="shop-combo-option-title search-kw-combo-option">
                          <span className="search-kw-combo-name">{row.keyword}</span>
                          <KeywordTags row={row} />
                        </span>
                      </span>
                    </div>
                  );
                })
              )}
            </div>,
            document.body,
          )
        : null}
    </div>
  );
}

function FilterSelect({
  value,
  options,
  onChange,
  disabled,
}: {
  value: string;
  options: { value: string; label: string }[];
  onChange: (value: string) => void;
  disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const dropRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const [menuPos, setMenuPos] = useState<{
    left: number;
    width: number;
    top?: number;
    bottom?: number;
    maxHeight: number;
  } | null>(null);

  const selected = options.find((o) => o.value === value) ?? null;
  const label = selected?.label || '—';
  const empty = !selected || selected.value === '';

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: PointerEvent) => {
      const t = e.target as Node;
      if (rootRef.current?.contains(t) || dropRef.current?.contains(t)) return;
      setOpen(false);
    };
    document.addEventListener('pointerdown', onDoc, true);
    return () => document.removeEventListener('pointerdown', onDoc, true);
  }, [open]);

  useEffect(() => {
    if (!open) {
      setMenuPos(null);
      return;
    }
    const place = () => {
      const el = triggerRef.current;
      if (!el) return;
      const r = el.getBoundingClientRect();
      const gap = 4;
      const below = window.innerHeight - r.bottom - 8;
      const above = r.top - 8;
      const openUp = below < 120 && above > below;
      const maxHeight = Math.min(240, Math.max(96, openUp ? above : below));
      setMenuPos(
        openUp
          ? { left: r.left, width: r.width, bottom: window.innerHeight - r.top + gap, maxHeight }
          : { left: r.left, width: r.width, top: r.bottom + gap, maxHeight },
      );
    };
    place();
    window.addEventListener('resize', place);
    return () => window.removeEventListener('resize', place);
  }, [open, options.length]);

  return (
    <div className={`shop-combo${open ? ' shop-combo--open' : ''}`} ref={rootRef}>
      <button
        ref={triggerRef}
        type="button"
        className="shop-combo-trigger"
        disabled={disabled}
        onPointerDown={(e) => {
          if (e.button !== 0 || disabled) return;
          e.preventDefault();
          setOpen((v) => !v);
        }}
        title={empty ? undefined : label}
        aria-haspopup="listbox"
        aria-expanded={open}
      >
        <span className="shop-combo-value">
          <span className={empty ? 'shop-combo-placeholder' : 'shop-combo-text'}>{label}</span>
        </span>
        <span className="shop-combo-arrow" aria-hidden />
      </button>
      {open && menuPos
        ? createPortal(
            <div
              ref={dropRef}
              className="shop-combo-dropdown search-kw-combo-menu"
              role="listbox"
              style={{
                left: menuPos.left,
                width: menuPos.width,
                top: menuPos.top,
                bottom: menuPos.bottom,
                maxHeight: menuPos.maxHeight,
              }}
              onPointerDown={(e) => {
                e.preventDefault();
                e.stopPropagation();
              }}
            >
              {options.map((o) => {
                const on = o.value === value;
                return (
                  <div
                    key={o.value || '__empty'}
                    className={`shop-combo-option${on ? ' shop-combo-option--on' : ''}`}
                    role="option"
                    aria-selected={on}
                    onPointerDown={(e) => {
                      e.preventDefault();
                      e.stopPropagation();
                      onChange(o.value);
                      setOpen(false);
                    }}
                  >
                    <span className="shop-combo-option-body">
                      <span className="shop-combo-option-title">
                        <span className={o.value === '' ? 'shop-combo-placeholder' : 'shop-combo-option-profile'}>
                          {o.label}
                        </span>
                      </span>
                    </span>
                  </div>
                );
              })}
            </div>,
            document.body,
          )
        : null}
    </div>
  );
}

function parseActiveGoodsId(raw: unknown): string {
  if (typeof raw === 'string') return raw.trim();
  if (Array.isArray(raw)) return String(raw[0] || '').trim();
  return '';
}

/** Click trái mở PDP tab mới — không dùng `<a>` thường vì side panel sẽ tự điều hướng. */
function openNewTabLink(e: ReactMouseEvent<HTMLAnchorElement>, url: string) {
  if (e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return;
  e.preventDefault();
  void send<{ ok: boolean; message?: string }>({ type: 'OPEN_URL_NEW_TAB', url });
}

function SearchProductRow({
  product: p,
  index,
  addedStatus,
  active,
  running,
  onRemove,
  onOpen,
}: {
  product: SheinSearchProduct;
  index: number;
  addedStatus: AddedStatus;
  active: boolean;
  running: boolean;
  onRemove: (goodsId: string) => void;
  onOpen: (goodsId: string) => void;
}) {
  const [previewPos, setPreviewPos] = useState<{ top: number; left: number } | null>(null);

  const priceText = formatDraftPrice(p.price, p.price);
  const hasRating = p.rating != null || p.reviewCount != null;

  return (
    <li className="search-product">
      <div className="search-media">
        <span
          className={`draft-index${
            addedStatus === 'full'
              ? ' search-index--full'
              : addedStatus === 'partial'
                ? ' search-index--partial'
                : ''
          }`}
          title={
            addedStatus === 'full'
              ? 'Đã thêm'
              : addedStatus === 'partial'
                ? 'Thiếu shop (chưa đủ shop đang chọn)'
                : undefined
          }
          aria-hidden={addedStatus === 'none' ? true : undefined}
        >
          {index}
        </span>
        {p.image ? (
          <img
            src={p.image}
            alt=""
            className="search-thumb"
            onMouseEnter={(e) => setPreviewPos(thumbPreviewPos(e.currentTarget))}
            onMouseLeave={() => setPreviewPos(null)}
          />
        ) : (
          <span className="search-thumb" />
        )}
      </div>
      <div className="search-product-body">
        <a
          href={p.url}
          target="_blank"
          rel="noopener"
          className={`search-product-name${active ? ' search-product-name--active' : ''}`}
          title={p.name}
          onClick={(e) => {
            openNewTabLink(e, p.url);
            onOpen(p.goodsId);
          }}
          onAuxClick={(e) => {
            if (e.button === 1) onOpen(p.goodsId);
          }}
        >
          {p.name}
        </a>
        <div className="search-product-meta draft-metrics">
          {p.score != null ? (
            <span
              className={`search-score${
                p.score >= 80 ? ' search-score--high' : p.score < 50 ? ' search-score--low' : ''
              }`}
              title={`Điểm ${p.score}/100 · khớp tiêu đề ${p.scoreTitle ?? '—'} · chứng minh ${p.scoreProven ?? p.scoreSold ?? '—'} · rating ${p.scoreRating ?? '—'} · giá ${p.scorePrice ?? '—'} (title = tỉ lệ cụm khớp, denim=jean; 40% title + 35% Wilson + 15% sao + 10% giá — title không loại cứng)`}
            >
              {p.score}
            </span>
          ) : null}
          {hasRating ? <DraftRatingBlock rating={p.rating} reviewCount={p.reviewCount} /> : null}
          {priceText ? (
            <span className="draft-price" title={`Giá bán: ${priceText}`}>
              {priceText}
            </span>
          ) : null}
          {p.soldCount != null ? (
            <span className="search-product-sold" title={`${p.soldCount.toLocaleString('vi-VN')} đã bán`}>
              sold {p.soldCount.toLocaleString('vi-VN')}
            </span>
          ) : null}
          {(p.isLocal === true || p.isQuickShip === true || p.isSheinClub === true) && (
            <span className="draft-metrics-flags">
              {p.isLocal === true && (
                <span className="draft-ship-badge draft-ship-badge--local" title="Hàng local US">
                  Local
                </span>
              )}
              {p.isQuickShip === true && (
                <span
                  className="draft-ship-badge draft-ship-badge--quick"
                  title="Hỗ trợ ship nhanh (QuickShip)"
                >
                  Nhanh
                </span>
              )}
              {p.isSheinClub === true && (
                <span className="draft-ship-badge draft-ship-badge--club" title="Shein Club">
                  Club
                </span>
              )}
            </span>
          )}
        </div>
        {p.keyword ? <span className="search-product-kw">{p.keyword}</span> : null}
      </div>
      <button
        type="button"
        className="search-del"
        title="Xóa khỏi danh sách"
        onClick={() => onRemove(p.goodsId)}
        disabled={running}
      >
        ×
      </button>
      {previewPos &&
        p.image &&
        createPortal(
          <div
            className="draft-thumb-preview"
            style={{ top: previewPos.top, left: previewPos.left }}
            aria-hidden="true"
          >
            <img src={p.image} alt="" />
          </div>,
          document.body,
        )}
    </li>
  );
}

function thumbPreviewPos(el: HTMLElement): { top: number; left: number } {
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
  return { top, left };
}

function catalogFromShop(shop: FsShopOption): ProductSearchCatalog {
  return {
    categories: shop.categories,
    categoryId: shop.categoryId,
    categoryName: shop.categoryName,
    ageMin: shop.ageMin ?? null,
    ageMax: shop.ageMax ?? null,
    bodySizeCode: shop.bodySizeCode ?? null,
    styleCodes: normalizeStyleCodes(shop.styleCodes),
    priceMinUsd: shop.priceMinUsd ?? null,
    priceMaxUsd: shop.priceMaxUsd ?? null,
    preferQuickShip: !!shop.preferQuickShip,
    preferLocal: !!shop.preferLocal,
    preferUsWarehouse: !!shop.preferUsWarehouse,
    preferSheinClub: !!shop.preferSheinClub,
    minRating: Number(shop.minRating) || 0,
    minSoldCount: Number(shop.minSoldCount) || 0,
    maxVariantCount: Number(shop.maxVariantCount) || 0,
  };
}

function numOrEmpty(v: number | null | undefined): string {
  if (v == null || !Number.isFinite(Number(v))) return '';
  return String(v);
}

function parseNum(raw: string): number | null {
  const n = Number(raw);
  return raw.trim() && Number.isFinite(n) ? n : null;
}

function DecimalInput({
  value,
  onChange,
  disabled,
  min,
  max,
  decimals,
  emptyAsNull,
}: {
  value: number | null | undefined;
  onChange: (n: number | null) => void;
  disabled?: boolean;
  min?: number;
  max?: number;
  decimals?: number;
  emptyAsNull?: boolean;
}) {
  const [focused, setFocused] = useState(false);
  const [draft, setDraft] = useState('');

  const round = (n: number) => {
    if (decimals == null) return n;
    const f = 10 ** decimals;
    return Math.round(n * f) / f;
  };

  const commit = (raw: string) => {
    const t = raw.trim().replace(',', '.');
    if (!t || t === '.') {
      onChange(emptyAsNull ? null : 0);
      return;
    }
    const n = Number(t);
    if (!Number.isFinite(n)) return;
    let next = round(n);
    if (min != null && next < min) next = min;
    if (max != null && next > max) next = max;
    onChange(next);
  };

  return (
    <input
      inputMode="decimal"
      value={focused ? draft : numOrEmpty(value)}
      disabled={disabled}
      onFocus={() => {
        setDraft(numOrEmpty(value));
        setFocused(true);
      }}
      onBlur={() => {
        commit(draft);
        setFocused(false);
      }}
      onChange={(e) => {
        const raw = e.target.value.replace(',', '.');
        if (raw !== '' && !/^\d*\.?\d*$/.test(raw)) return;
        setDraft(raw);
        if (!raw || raw.endsWith('.')) return;
        const n = Number(raw);
        if (!Number.isFinite(n)) return;
        if (min != null && n < min) return;
        if (max != null && n > max) return;
        onChange(round(n));
      }}
    />
  );
}

function researchLaneLabel(lane?: string | null): string | null {
  if (lane === 'listNow') return 'List ngay';
  if (lane === 'upcoming') return 'Đón đầu';
  return null;
}

function topSearchTags(row: SavedKeywordRow): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const raw of row.leadTagTexts ?? []) {
    const tag = String(raw || '').trim();
    if (!tag || !/top search/i.test(tag)) continue;
    const key = tag.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(tag);
  }
  return out;
}

function KeywordLimitField({
  value,
  found,
  showCount,
  disabled,
  onChange,
}: {
  value: string;
  found: number;
  showCount: boolean;
  disabled: boolean;
  onChange: (next: string) => void;
}) {
  const [editing, setEditing] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const limit = Number(value) || DEFAULT_SEARCH_LIMIT;

  useEffect(() => {
    if (!editing) return;
    const el = inputRef.current;
    if (!el) return;
    el.focus();
    el.select();
  }, [editing]);

  if (editing && !disabled) {
    return (
      <input
        ref={inputRef}
        className="search-kw-limit"
        inputMode="numeric"
        title="Số sản phẩm muốn tìm"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onBlur={() => setEditing(false)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === 'Escape') {
            e.preventDefault();
            setEditing(false);
          }
        }}
      />
    );
  }

  return (
    <button
      type="button"
      className="search-kw-limit-view"
      title="Số sản phẩm muốn tìm"
      disabled={disabled}
      onClick={() => {
        if (!disabled) setEditing(true);
      }}
    >
      <span className="search-kw-limit-text">
        {showCount ? (
          <>
            <span className={found > 0 ? 'search-kw-found search-kw-found--hit' : 'search-kw-found'}>
              {found}
            </span>
            /{limit}
          </>
        ) : (
          String(limit)
        )}
      </span>
      <svg
        className="search-kw-limit-edit"
        viewBox="0 0 24 24"
        width="12"
        height="12"
        aria-hidden
      >
        <path
          fill="currentColor"
          d="M4 17.2V20h2.8l8.3-8.3-2.8-2.8L4 17.2Zm14.8-8.2c.3-.3.3-.8 0-1.1l-1.7-1.7a.8.8 0 0 0-1.1 0l-1.3 1.3 2.8 2.8 1.3-1.3Z"
        />
      </svg>
    </button>
  );
}

function formatAge(min?: number | null, max?: number | null): string {
  if (min == null && max == null) return '—';
  return `${min ?? '—'}–${max ?? '—'}`;
}

/** Last segment of "Parent / Child / Leaf" — same as shops table. */
function categoryLeafName(name?: string | null): string {
  const text = name?.trim() ?? '';
  if (!text) return '';
  const parts = text.split('/').map((part) => part.trim()).filter(Boolean);
  return parts[parts.length - 1] || text;
}

function formatCatalogLeaves(
  shop: Pick<FsShopOption, 'categories' | 'categoryName'> | null,
): string {
  if (!shop) return '';
  const fromList = (shop.categories ?? [])
    .map((c) => categoryLeafName(c.name))
    .filter(Boolean);
  if (fromList.length > 0) return fromList.join(' · ');
  const fromJoined = (shop.categoryName ?? '')
    .split('·')
    .map((part) => categoryLeafName(part))
    .filter(Boolean);
  return fromJoined.join(' · ');
}

function catalogSummary(shop: FsShopOption, catalog: ProductSearchCatalog): string {
  const cat = formatCatalogLeaves(shop);
  const body = BODY_SIZE_OPTIONS.find((o) => o.code === catalog.bodySizeCode)?.label;
  const styles = (catalog.styleCodes ?? [])
    .map((code) => STYLE_CHIP_OPTIONS.find((o) => o.code === code)?.label || code)
    .filter(Boolean);
  const parts: string[] = [];
  if (cat) parts.push(cat);
  parts.push(`tuổi ${formatAge(shop.ageMin, shop.ageMax)}`);
  if (body) parts.push(body);
  const minP = catalog.priceMinUsd;
  const maxP = catalog.priceMaxUsd;
  if (minP != null || maxP != null) {
    parts.push(`$${minP ?? '—'}–${maxP ?? '—'}`);
  }
  if (Number(catalog.minRating) > 0) parts.push(`★≥${catalog.minRating}`);
  if (Number(catalog.minSoldCount) > 0) parts.push(`sold≥${catalog.minSoldCount}`);
  if (styles.length) parts.push(styles.join(', '));
  if (catalog.preferQuickShip) parts.push('QuickShip');
  if (catalog.preferLocal) parts.push('Local');
  if (catalog.preferSheinClub) parts.push('Club');
  const sort = normalizeSheinSearchSort(catalog.sheinSort);
  if (sort !== 'recommend') parts.push(sheinSearchSortLabel(sort));
  return parts.join(' · ') || 'Bộ lọc shop';
}

type Props = {
  shop: FsShopOption | null;
  sessionOk: boolean;
  selectedShopIds: number[];
};

export function ProductSearchSection({ shop, sessionOk, selectedShopIds }: Props) {
  const [catalog, setCatalog] = useState<ProductSearchCatalog>({});
  const [keywords, setKeywords] = useState<SavedKeywordRow[]>([]);
  const [kwMsg, setKwMsg] = useState<string | null>(null);
  const [loadingKw, setLoadingKw] = useState(false);
  const [selectedKeyword, setSelectedKeyword] = useState('');
  const [limits, setLimits] = useState<Record<string, string>>({});
  const [search, setSearch] = useState<ProductSearchState>(emptyProductSearch);
  const [addedMap, setAddedMap] = useState<AddedProductShopMap>({});
  const [activeGoodsId, setActiveGoodsId] = useState('');
  const [busy, setBusy] = useState(false);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [learnedAttrs, setLearnedAttrs] = useState<SheinStyleAttr[]>([]);
  const inFlightRef = useRef(false);
  const kwLoadGen = useRef(0);

  const shopLocalId = shop?.localId ?? '';
  const shopCatalogKey = shop
    ? [
        shopLocalId,
        shop.categoryId ?? '',
        shop.bodySizeCode ?? '',
        (shop.styleCodes ?? []).join(','),
        shop.priceMinUsd ?? '',
        shop.priceMaxUsd ?? '',
        shop.minRating ?? '',
        shop.minSoldCount ?? '',
        shop.preferQuickShip ? 1 : 0,
        shop.preferLocal ? 1 : 0,
        shop.preferUsWarehouse ? 1 : 0,
        shop.preferSheinClub ? 1 : 0,
        shop.ageMin ?? '',
        shop.ageMax ?? '',
        shop.maxVariantCount ?? '',
        formatCatalogLeaves(shop),
      ].join('|')
    : '';
  const searching = search.status === 'searching';
  const adding = search.add.status === 'adding';
  const running = searching || adding;

  useEffect(() => {
    if (!shop) {
      setCatalog((c) => ({ sheinSort: c.sheinSort }));
      setKeywords([]);
      setSelectedKeyword('');
      setLimits({});
      return;
    }
    setCatalog((c) => ({
      ...catalogFromShop(shop),
      sheinSort: normalizeSheinSearchSort(c.sheinSort),
    }));
    // Chỉ hydrate khi hồ sơ shop đổi — không reset vì object `shop` mới (GET_STATE),
    // kẻo bỏ QuickShip trên form rồi copy/tìm vẫn lấy cờ cũ.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shopCatalogKey]);

  const loadKeywords = useCallback(async (id: string) => {
    if (!id) return;
    const gen = ++kwLoadGen.current;
    setLoadingKw(true);
    setKwMsg(null);
    let timer = 0;
    try {
      const res = await Promise.race([
        send<ListSavedKeywordsResponse>({ type: 'LIST_SAVED_KEYWORDS', shopLocalId: id }),
        new Promise<never>((_, reject) => {
          timer = window.setTimeout(
            () => reject(new Error('Tải keyword quá lâu. Bấm tải lại.')),
            15_000,
          );
        }),
      ]);
      if (gen !== kwLoadGen.current) return;
      if (!res.ok) {
        setKeywords([]);
        setKwMsg(res.message || 'Không tải được keyword đã lưu.');
        return;
      }
      const items = res.items ?? [];
      setKeywords(items);
      setSelectedKeyword((prev) =>
        items.some((k) => k.keyword === prev) ? prev : (items[0]?.keyword ?? ''),
      );
      setLimits(Object.fromEntries(items.map((k) => [k.keyword, String(DEFAULT_SEARCH_LIMIT)])));
      if (items.length === 0) setKwMsg('Chưa có keyword đã lưu cho shop này.');
    } catch (err) {
      if (gen !== kwLoadGen.current) return;
      setKeywords([]);
      setKwMsg((err as Error).message || 'Không tải được keyword đã lưu.');
    } finally {
      window.clearTimeout(timer);
      if (gen === kwLoadGen.current) setLoadingKw(false);
    }
  }, []);

  useEffect(() => {
    if (!shopLocalId) return;
    void loadKeywords(shopLocalId);
  }, [shopLocalId, loadKeywords]);

  useEffect(() => {
    void send<ProductSearchResponse>({ type: 'GET_PRODUCT_SEARCH' }).then((res) => {
      if (res?.state) setSearch(res.state);
    });
    void chrome.storage.local.get(STORAGE_KEYS.addedProductIds).then((raw) => {
      setAddedMap(normalizeAddedProductShopMap(raw[STORAGE_KEYS.addedProductIds]));
    });
    void chrome.storage.local.get(STORAGE_KEYS.productSearchClicked).then((raw) => {
      setActiveGoodsId(parseActiveGoodsId(raw[STORAGE_KEYS.productSearchClicked]));
    });
    void chrome.storage.local.get(STORAGE_KEYS.sheinStyleAttrs).then((raw) => {
      const rows = raw[STORAGE_KEYS.sheinStyleAttrs];
      if (Array.isArray(rows)) setLearnedAttrs(rows as SheinStyleAttr[]);
    });
    void chrome.storage.local.get(STORAGE_KEYS.sheinSearchSort).then((raw) => {
      const sort = normalizeSheinSearchSort(raw[STORAGE_KEYS.sheinSearchSort]);
      setCatalog((c) => ({ ...c, sheinSort: sort }));
    });
    const onChange = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local') return;
      if (changes[STORAGE_KEYS.productSearch]) {
        const next = changes[STORAGE_KEYS.productSearch].newValue as ProductSearchState | undefined;
        if (next && Array.isArray(next.products)) setSearch(next);
      }
      if (changes[STORAGE_KEYS.addedProductIds]) {
        setAddedMap(normalizeAddedProductShopMap(changes[STORAGE_KEYS.addedProductIds].newValue));
      }
      if (changes[STORAGE_KEYS.productSearchClicked]) {
        setActiveGoodsId(parseActiveGoodsId(changes[STORAGE_KEYS.productSearchClicked].newValue));
      }
      if (changes[STORAGE_KEYS.sheinStyleAttrs]) {
        const rows = changes[STORAGE_KEYS.sheinStyleAttrs].newValue;
        setLearnedAttrs(Array.isArray(rows) ? (rows as SheinStyleAttr[]) : []);
      }
    };
    chrome.storage.onChanged.addListener(onChange);
    return () => chrome.storage.onChanged.removeListener(onChange);
  }, []);

  const selected = useMemo(
    () => keywords.find((k) => k.keyword === selectedKeyword) ?? null,
    [keywords, selectedKeyword],
  );
  const rankedProducts = useMemo(
    () => sortBySearchScore(search.products),
    [search.products],
  );

  const canSearch =
    sessionOk && !!shop?.localId && !!selected && !running && !busy;

  const patchCatalog = <K extends keyof ProductSearchCatalog>(key: K, value: ProductSearchCatalog[K]) => {
    setCatalog((c) => ({ ...c, [key]: value }));
    if (key === 'sheinSort') {
      void chrome.storage.local.set({
        [STORAGE_KEYS.sheinSearchSort]: normalizeSheinSearchSort(value),
      });
    }
  };

  const toggleStyle = (code: string) => {
    const want = normalizeStyleCode(code);
    if (!want) return;
    setCatalog((c) => {
      const cur = normalizeStyleCodes(c.styleCodes);
      return {
        ...c,
        styleCodes: cur.includes(want) ? cur.filter((x) => x !== want) : [...cur, want],
      };
    });
  };

  const runSearch = async () => {
    if (!shop?.localId || !canSearch || inFlightRef.current) return;
    inFlightRef.current = true;
    setBusy(true);
    try {
      const res = await send<ProductSearchResponse>({
        type: 'SEARCH_PRODUCTS',
        shopLocalId: shop.localId,
        shopFsId: shop.id,
        catalog: {
          ...catalog,
          priceMinUsd: parseNum(numOrEmpty(catalog.priceMinUsd)),
          priceMaxUsd: parseNum(numOrEmpty(catalog.priceMaxUsd)),
        },
        keywords: selected
          ? [
              {
                keyword: selected.keyword,
                limit: Math.min(50, Math.max(1, Number(limits[selected.keyword]) || DEFAULT_SEARCH_LIMIT)),
                mustPhrases: selected.mustPhrases,
                shouldPhrases: selected.shouldPhrases,
                minDistinctiveHits: selected.minDistinctiveHits,
              },
            ]
          : [],
      });
      if (res?.state) setSearch(finishSearchState(res.state));
    } catch {
      const res = await send<ProductSearchResponse>({ type: 'GET_PRODUCT_SEARCH' }).catch(
        () => null,
      );
      if (res?.state) setSearch(res.state);
    } finally {
      inFlightRef.current = false;
      setBusy(false);
    }
  };

  const cancel = () => {
    void send({ type: 'SEARCH_CANCEL' });
  };

  const addSelected = async () => {
    if (rankedProducts.length === 0 || running || inFlightRef.current) return;
    inFlightRef.current = true;
    setBusy(true);
    try {
      const res = await send<ProductSearchResponse>({
        type: 'ADD_SEARCHED_PRODUCTS',
        goodsIds: rankedProducts.map((p) => p.goodsId),
      });
      if (res?.state) setSearch(res.state);
    } catch {
      const res = await send<ProductSearchResponse>({ type: 'GET_PRODUCT_SEARCH' }).catch(
        () => null,
      );
      if (res?.state) setSearch(res.state);
    } finally {
      inFlightRef.current = false;
      setBusy(false);
    }
  };

  const removeProduct = (goodsId: string) => {
    void send({ type: 'REMOVE_SEARCHED_PRODUCT', goodsId });
  };

  const markClicked = (goodsId: string) => {
    const id = String(goodsId || '').trim();
    if (!id) return;
    if (id !== activeGoodsId) {
      setActiveGoodsId(id);
      void chrome.storage.local.set({ [STORAGE_KEYS.productSearchClicked]: id });
    }
    void rememberRecommendedGoodsIds([id]);
  };

  if (!shop) {
    return (
      <div className="search-body">
        <div className="hint">Chọn đúng 1 shop và 1 keyword để tìm sản phẩm.</div>
      </div>
    );
  }

  if (!shopLocalId) {
    return (
      <div className="search-body">
        <div className="hint hint--err">
          Shop thiếu mã local — bấm tải lại danh sách shop (không dùng cache cũ).
        </div>
      </div>
    );
  }

  return (
    <div className="search-body">
      <div className="row">
        <span className="card-title">Tìm sản phẩm</span>
        <div className="search-actions">
          {running ? (
            <button type="button" className="btn btn-ghost btn-compact" onClick={cancel}>
              Dừng
            </button>
          ) : null}
          <button
            type="button"
            className="btn btn-primary btn-compact"
            onClick={() => void runSearch()}
            disabled={!canSearch}
            title={
              !sessionOk
                ? 'Cần phiên 4Seller hợp lệ'
                : !selected
                  ? 'Chọn 1 keyword'
                  : 'Tìm trên Shein theo keyword + bộ lọc shop'
            }
          >
            {searching || busy ? <Spinner /> : null}
            {searching ? 'Đang tìm...' : 'Tìm kiếm'}
          </button>
        </div>
      </div>

      <div className={`search-filters${filtersOpen ? ' search-filters--open' : ''}`}>
        <button
          type="button"
          className="search-filters-toggle"
          onClick={() => setFiltersOpen((v) => !v)}
          aria-expanded={filtersOpen}
        >
          <span className="search-filters-title">Bộ lọc</span>
          {!filtersOpen ? (
            <span className="search-filters-summary">{catalogSummary(shop, catalog)}</span>
          ) : null}
          <svg
            viewBox="0 0 24 24"
            width="14"
            height="14"
            aria-hidden
            className={`search-filters-chevron${filtersOpen ? ' search-filters-chevron--up' : ''}`}
          >
            <path
              fill="currentColor"
              d="M12 15.4 5.3 8.7a1 1 0 0 1 1.4-1.4l5.3 5.3 5.3-5.3a1 1 0 1 1 1.4 1.4L12 15.4Z"
            />
          </svg>
        </button>
        {filtersOpen ? (
          <div className="search-filters-body">
            <div className="search-readonly">
              <span>
                <b>Danh mục</b> {formatCatalogLeaves(shop) || '—'}
              </span>
              <span>
                <b>Tuổi</b> {formatAge(shop.ageMin, shop.ageMax)}
              </span>
            </div>

            <div className="search-grid search-grid--pair">
              <label className="field field--compact">
                Body/size
                <FilterSelect
                  value={normalizeSearchBodySizeCode(catalog.bodySizeCode) ?? ''}
                  options={[
                    { value: '', label: '—' },
                    ...BODY_SIZE_OPTIONS.map((o) => ({ value: o.code, label: o.label })),
                  ]}
                  onChange={(v) => patchCatalog('bodySizeCode', v || null)}
                  disabled={running}
                />
              </label>
              <label className="field field--compact">
                Sort by
                <FilterSelect
                  value={normalizeSheinSearchSort(catalog.sheinSort)}
                  options={SHEIN_SEARCH_SORT_OPTIONS.map((o) => ({ value: o.code, label: o.label }))}
                  onChange={(v) => patchCatalog('sheinSort', normalizeSheinSearchSort(v))}
                  disabled={running}
                />
              </label>
            </div>

            <div className="search-grid search-grid--pair">
              <label className="field field--compact">
                Giá min
                <DecimalInput
                  value={catalog.priceMinUsd}
                  onChange={(n) => patchCatalog('priceMinUsd', n)}
                  disabled={running}
                  min={0}
                  emptyAsNull
                />
              </label>
              <label className="field field--compact">
                Giá max
                <DecimalInput
                  value={catalog.priceMaxUsd}
                  onChange={(n) => patchCatalog('priceMaxUsd', n)}
                  disabled={running}
                  min={0}
                  emptyAsNull
                />
              </label>
            </div>
            <div className="search-styles">
              {STYLE_CHIP_OPTIONS.map((o) => {
                const on = normalizeStyleCodes(catalog.styleCodes).includes(o.code);
                return (
                  <button
                    key={o.code}
                    type="button"
                    className={`search-chip${on ? ' search-chip--on' : ''}`}
                    onClick={() => toggleStyle(o.code)}
                    disabled={running}
                  >
                    {o.label}
                  </button>
                );
              })}
            </div>

            <div className="search-flags">
              {(
                [
                  ['preferQuickShip', 'QuickShip'],
                  ['preferLocal', 'Local'],
                  ['preferSheinClub', 'Shein Club'],
                ] as const
              ).map(([key, label]) => (
                <label key={key} className="search-check">
                  <input
                    type="checkbox"
                    checked={!!catalog[key]}
                    onChange={(e) => patchCatalog(key, e.target.checked)}
                    disabled={running}
                  />
                  {label}
                </label>
              ))}
            </div>

            <div className="search-grid search-grid--pair">
              <label className="field field--compact">
                Rating ≥
                <DecimalInput
                  value={catalog.minRating}
                  onChange={(n) => patchCatalog('minRating', n ?? 0)}
                  disabled={running}
                  min={0}
                  max={5}
                  decimals={1}
                />
              </label>
              <label className="field field--compact">
                Sold ≥
                <input
                  inputMode="numeric"
                  value={numOrEmpty(catalog.minSoldCount)}
                  onChange={(e) => patchCatalog('minSoldCount', parseNum(e.target.value) ?? 0)}
                  disabled={running}
                />
              </label>
            </div>
          </div>
        ) : null}
      </div>

      <div className="search-kw-bar">
        <div className="search-kw-bar-head">
          <span className="search-kw-bar-label">Trending keyword</span>
          {selected ? (
            <CopySearchUrlButton
              keyword={selected.keyword}
              catalog={catalog}
              learned={learnedAttrs}
            />
          ) : null}
          {selected ? (
            <KeywordLimitField
              value={limits[selected.keyword] ?? String(DEFAULT_SEARCH_LIMIT)}
              found={search.keywordCounts[selected.keyword] ?? 0}
              showCount={search.status !== 'idle'}
              disabled={running}
              onChange={(next) => setLimits((m) => ({ ...m, [selected.keyword]: next }))}
            />
          ) : null}
          <button
            type="button"
            className="search-kw-copy search-kws-reload"
            title="Tải lại keyword"
            aria-label="Tải lại keyword"
            onClick={() => void loadKeywords(shopLocalId)}
            disabled={loadingKw}
          >
            {loadingKw ? <span className="spinner spinner--dark" aria-hidden /> : <ReloadIcon />}
          </button>
        </div>
        <KeywordCombo
          keywords={keywords}
          selectedKeyword={selectedKeyword}
          onSelect={setSelectedKeyword}
          loading={loadingKw}
          emptyMessage={kwMsg}
        />
      </div>
      {kwMsg && !loadingKw && keywords.length === 0 && !/^Chưa có keyword/.test(kwMsg) ? (
        <div className="hint hint--err">{kwMsg}</div>
      ) : null}

      {search.message && (
        <div className={`hint${search.status === 'error' ? ' hint--err' : ''}`}>{search.message}</div>
      )}

      {rankedProducts.length > 0 && (
        <div className="search-results">
          <div className="search-result-head">
            <span>Kết quả tìm kiếm</span>
          </div>
          <ul className="search-products">
            {rankedProducts.map((p, i) => (
              <SearchProductRow
                key={p.goodsId}
                product={p}
                index={i + 1}
                addedStatus={getAddedStatus(
                  makeAddedProductKey('shein', p.goodsId),
                  addedMap,
                  selectedShopIds,
                )}
                active={activeGoodsId === p.goodsId}
                running={running}
                onRemove={removeProduct}
                onOpen={markClicked}
              />
            ))}
          </ul>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => void addSelected()}
            disabled={running || busy || rankedProducts.length === 0 || !sessionOk}
            title="Mở lần lượt PDP trong danh sách → draft 4Seller"
          >
            {adding ? <Spinner /> : null}
            {adding
              ? search.add.message || 'Đang thêm...'
              : `Thêm sản phẩm (${rankedProducts.length})`}
          </button>
          {search.add.message && search.add.status === 'done' && (
            <div className={search.add.failed ? 'result err' : 'result ok'}>{search.add.message}</div>
          )}
        </div>
      )}
    </div>
  );
}
