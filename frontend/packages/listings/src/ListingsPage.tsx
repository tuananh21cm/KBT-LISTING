import { memo, useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  Dropdown,
  Empty,
  Input,
  Pagination,
  Popover,
  Select,
  Space,
  Tag,
  Tabs,
  Tooltip,
  Typography,
  message,
} from 'antd';
import {
  DeleteOutlined,
  CopyOutlined,
  DownOutlined,
  EditOutlined,
  ExclamationCircleFilled,
  MenuOutlined,
  PlayCircleOutlined,
  ReloadOutlined,
  StopOutlined,
  SyncOutlined,
  UpOutlined,
  HistoryOutlined,
} from '@ant-design/icons';
import { type Dayjs } from 'dayjs';
import {
  formatDateTimeVn,
  rangeForPresetVn,
  toUtcIsoRange,
} from '@drop/utils/datetime';
import { needsPagination } from '@drop/utils/pagination';
import { useQueryClient } from '@tanstack/react-query';
import {
  getListingRatingTier,
  LISTING_RATING_DEFAULTS,
  LISTING_RATING_SETTING_KEYS,
  ListingStars,
  parseListingRatingThreshold,
  LISTING_AUTO_PUBLISH_QUEUE_LABELS,
  LISTING_AUTO_PUBLISH_QUEUE_TOOLTIPS,
  ShopAutoPublishSchedulerBadge,
  type ListingRatingTier,
} from '@drop/ui';
import type {
  ListingListItem,
  ListingStatusCounts,
  ListingSyncSellerStatus,
  ListingVariation,
} from '@drop/types/listing';
import type { PlatformOption } from '@drop/types/platform';
import { Modal, PageHeader, PageContentSpin, PlatformAvatar, AdminDateRangeFilter, useIsMobile } from '@drop/ui';
import { useListingsDeps } from './ListingsDepsContext';
import type { ListingsPortalConfig } from './portalConfig';
import { BindCategoryModal } from './BindCategoryModal';
import { AutoPublishTimelineModal } from './AutoPublishTimelineModal';
import { firstImage } from './listingImage';
import { ProductThumb } from './ProductThumb';
import { ListingSupplierLine } from './listingSupplierLine';
import { formatSyncErrorDisplay } from './listingFormUtils';
import './listings.css';

/** Warm the lazy ListingEditPage chunk (shared HTTP cache across tabs). */
const preloadListingEditChunk = () => {
  void import('./ListingEditPage');
};

/** Mở URL tab mới qua thẻ `<a>` — không điều hướng tab hiện tại (window.open+noopener trả null dù thành công). */
const openUrlInNewTab = (url: string) => {
  const link = document.createElement('a');
  link.href = url;
  link.target = '_blank';
  link.rel = 'noopener noreferrer';
  document.body.appendChild(link);
  link.click();
  link.remove();
};

const { Text } = Typography;

const PAGE_SIZE = 25;
const PAGE_SIZE_OPTIONS = [25, 50, 100];
const VARIANT_PREVIEW_COUNT = 3;
/** Manual list Sync: light pull cap per status bucket (no date window). */
const SYNC_MAX_RECORDS = 200;
/** When UI date range is set, match background published lookback coverage. */
const SYNC_MAX_RECORDS_WITH_DATE = 500;
/** Thumb spin only while optimistic publish grace is still plausible. */
const PUBLISH_PENDING_THUMB_SPIN_MS = 30 * 60 * 1000;
/** Published site statuses for status-only bulk sync (all buckets except draft). */
const PUBLISHED_SYNC_STATUSES = [
  'active',
  'inactive',
  'suspended',
  'removed',
  'incomplete',
  'private',
  'pending',
  'reject',
  'unsaleable',
] as const;

const SYNC_SELLER_STATUS_LABELS: Record<ListingSyncSellerStatus, string> = {
  pending: 'chờ',
  running: 'đang chạy',
  succeeded: 'xong',
  failed: 'lỗi',
  skipped_busy: 'đang bận',
};

const COL_TEMPLATE =
  'minmax(260px, 2.4fr) minmax(100px, 0.75fr) minmax(140px, 1fr) minmax(120px, 0.9fr) minmax(120px, 0.85fr)';

const COL_HEADERS = ['Sản phẩm', 'Tồn / SKU', 'Thời gian', 'Danh mục', 'Variants'];

type DatePreset = '' | 'day' | '7d' | '30d';

const DATE_PRESETS: { value: DatePreset; label: string }[] = [
  { value: '', label: 'Tất cả' },
  { value: 'day', label: 'Ngày' },
  { value: '7d', label: '7 Days' },
  { value: '30d', label: '30 Days' },
];

const rangeForPreset = (preset: Exclude<DatePreset, ''>): [Dayjs, Dayjs] => rangeForPresetVn(preset);

const matchPreset = (range: [Dayjs, Dayjs] | null): DatePreset => {
  if (!range) return '';
  const [from, to] = range;
  for (const preset of ['day', '7d', '30d'] as const) {
    const [pFrom, pTo] = rangeForPreset(preset);
    if (from.isSame(pFrom, 'minute') && to.isSame(pTo, 'minute')) return preset;
  }
  return '';
};

const SITE_STATUS_TABS: { key: string; label: string; countKey: keyof ListingStatusCounts }[] = [
  { key: '', label: 'Tất cả', countKey: 'all' },
  { key: 'active', label: 'Active', countKey: 'active' },
  { key: 'inactive', label: 'Inactive', countKey: 'inactive' },
  { key: 'suspended', label: 'Suspended', countKey: 'suspended' },
  { key: 'removed', label: 'Removed', countKey: 'removed' },
  { key: 'incomplete', label: 'Incomplete', countKey: 'incomplete' },
  { key: 'private', label: 'Private', countKey: 'private' },
  { key: 'pending', label: 'Reviewing', countKey: 'pending' },
  { key: 'reject', label: 'Reject', countKey: 'reject' },
  { key: 'unsaleable', label: 'Unsaleable', countKey: 'unsaleable' },
];

const STATUS_LABEL: Record<string, string> = {
  draft: 'Draft',
  published: 'Published',
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

const statusLabel = (status?: string | null) => {
  if (!status) return '—';
  const key = status.trim().toLowerCase();
  return STATUS_LABEL[key] || status.charAt(0).toUpperCase() + status.slice(1).toLowerCase();
};

const statusDotColor = (status?: string | null): string => {
  const key = (status || '').toLowerCase();
  if (key === 'active') return '#52c41a';
  if (key === 'draft' || key === 'pending' || key === 'incomplete') return '#faad14';
  if (key === 'inactive' || key === 'private') return '#8c8c8c';
  if (key === 'suspended' || key === 'removed' || key === 'reject' || key === 'unsaleable') return '#ff4d4f';
  return '#1677ff';
};

const formatPrice = (item: ListingListItem) => {
  if (item.lowPrice == null && item.highPrice == null) return null;
  const low = item.lowPrice ?? item.highPrice!;
  const high = item.highPrice ?? item.lowPrice!;
  if (low === high) return low;
  return { low, high };
};

const formatInitialPrice = (item: ListingListItem) => {
  const prices = (item.variations ?? [])
    .map((variant) => variant.initialPrice)
    .filter((price): price is number => price != null && Number.isFinite(price));
  if (prices.length === 0) return null;
  const low = Math.min(...prices);
  const high = Math.max(...prices);
  if (low === high) return low;
  return { low, high };
};

/** Drafts with no cost (InitialPrice) and no variants usually need a TikTok category bind first. */
const needsCategoryStep = (item: ListingListItem) => {
  const noCost = formatInitialPrice(item) == null;
  const noVariants = (item.variationCount || item.variations?.length || 0) === 0;
  return noCost && noVariants;
};

const formatAmount = (amount: number) =>
  Number(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const MoneyAmount = ({
  amount,
  currency,
  hint,
  style,
}: {
  amount: number | { low: number; high: number };
  currency?: string | null;
  hint?: string | null;
  style?: CSSProperties;
}) => {
  const code = (currency || 'USD').trim().toUpperCase() || 'USD';
  const label =
    typeof amount === 'number'
      ? `$${formatAmount(amount)}`
      : `$${formatAmount(amount.low)} – $${formatAmount(amount.high)}`;
  return (
    <Tooltip title={hint ? `${hint} · ${code}` : code}>
      <Text strong style={{ fontSize: 12, whiteSpace: 'nowrap', color: 'var(--listing-money)', ...style }}>
        {label}
      </Text>
    </Tooltip>
  );
};

const formatDateTime = (value?: string | null) => formatDateTimeVn(value);

const copyText = async (text: string, successMsg = 'Đã copy') => {
  try {
    await navigator.clipboard.writeText(text);
    message.success(successMsg);
  } catch {
    message.error('Không copy được');
  }
};

const metaLineStyle: CSSProperties = {
  fontSize: 12,
  lineHeight: '17px',
  color: 'var(--listing-text-secondary)',
};

const isPublishPending = (listing: ListingListItem) => {
  const publish = (listing.publishStatus || '').trim().toLowerCase();
  const site = (listing.siteStatus || '').trim().toLowerCase();
  if (site === 'active') return false;
  return publish === 'publishing' || publish === 'underreview' || site === 'pending';
};

/** Spin on thumb only right after push — stale Reviewing keeps the yellow status dot only. */
const shouldSpinPublishPendingThumb = (listing: ListingListItem) => {
  if (!isPublishPending(listing)) return false;
  const pushedAt = listing.lastPushAt ? Date.parse(listing.lastPushAt) : NaN;
  if (!Number.isFinite(pushedAt)) return false;
  return Date.now() - pushedAt < PUBLISH_PENDING_THUMB_SPIN_MS;
};

const shouldShowAutoPublishLog = (listing: ListingListItem) =>
  listing.publishSource === 'auto' ||
  listing.lastAutoPublishRunId ||
  listing.autoPublishSkipped ||
  listing.autoPublishQueueStatus === 'processing' ||
  listing.autoPublishQueueStatus === 'failed';

const isAutoPublishQueueBadgeClickable = (listing: ListingListItem) =>
  listing.autoPublishQueueStatus === 'processing' || listing.autoPublishQueueStatus === 'failed';

const missingSizeChartIcon = (listing: ListingListItem) => {
  if (listing.hasSizeChart !== false) return null;
  return (
    <Tooltip title="Thiếu size chart — auto publish không đưa vào hàng chờ">
      <ExclamationCircleFilled
        aria-label="Thiếu size chart"
        style={{ color: 'var(--ant-color-error)', fontSize: 12, flexShrink: 0 }}
      />
    </Tooltip>
  );
};

const AUTO_PUBLISH_TAG_STYLE: CSSProperties = {
  margin: 0,
  fontSize: 10,
  lineHeight: '16px',
  padding: '0 4px',
};

const autoPublishQueueBadge = (
  listing: ListingListItem,
  onOpenLog?: (listing: ListingListItem) => void,
  options?: {
    canRetry?: boolean;
    retrying?: boolean;
    onRetry?: (listing: ListingListItem) => void;
  },
) => {
  const status = listing.autoPublishQueueStatus;
  if (!status) return null;

  const clickable = isAutoPublishQueueBadgeClickable(listing) && !!onOpenLog;
  const openLog = (e: MouseEvent | KeyboardEvent) => {
    e.stopPropagation();
    onOpenLog?.(listing);
  };
  const canRetry = options?.canRetry && !!options.onRetry;
  const retrying = options?.retrying ?? false;
  const retryAutoPublish = (e: MouseEvent | KeyboardEvent) => {
    e.stopPropagation();
    options?.onRetry?.(listing);
  };

  if (status === 'queue') {
    return (
      <Tooltip title={LISTING_AUTO_PUBLISH_QUEUE_TOOLTIPS.queue}>
        <Tag color="blue" style={AUTO_PUBLISH_TAG_STYLE}>
          {LISTING_AUTO_PUBLISH_QUEUE_LABELS.queue}
        </Tag>
      </Tooltip>
    );
  }

  if (status === 'waiting') {
    return (
      <ShopAutoPublishSchedulerBadge
        status="waiting"
        nextPickAt={listing.autoPublishNextPickAt}
      />
    );
  }

  if (status === 'processing') {
    return (
      <Tooltip title={LISTING_AUTO_PUBLISH_QUEUE_TOOLTIPS.processing}>
        <Tag
          color="processing"
          style={{
            ...AUTO_PUBLISH_TAG_STYLE,
            cursor: clickable ? 'pointer' : undefined,
          }}
          role={clickable ? 'button' : undefined}
          tabIndex={clickable ? 0 : undefined}
          onClick={clickable ? openLog : undefined}
          onKeyDown={
            clickable
              ? (e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openLog(e);
                  }
                }
              : undefined
          }
        >
          <SyncOutlined spin style={{ marginRight: 3, fontSize: 10 }} />
          {LISTING_AUTO_PUBLISH_QUEUE_LABELS.processing}
        </Tag>
      </Tooltip>
    );
  }

  if (status === 'failed') {
    const tip =
      listing.autoPublishSkipReason?.trim() ||
      listing.lastAutoPublishRunStatus ||
      'Auto publish thất bại — bấm xem chi tiết từng bước';
    return (
      <span
        style={{ display: 'inline-flex', alignItems: 'center', gap: 2, flexShrink: 0 }}
        onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => e.stopPropagation()}
      >
        <Tooltip title={tip}>
          <Tag
            color="error"
            style={{
              ...AUTO_PUBLISH_TAG_STYLE,
              cursor: clickable ? 'pointer' : undefined,
            }}
            role={clickable ? 'button' : undefined}
            tabIndex={clickable ? 0 : undefined}
            onClick={clickable ? openLog : undefined}
            onKeyDown={
              clickable
                ? (e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      openLog(e);
                    }
                  }
                : undefined
            }
          >
            {LISTING_AUTO_PUBLISH_QUEUE_LABELS.failed}
          </Tag>
        </Tooltip>
        {canRetry ? (
          <Tooltip title={LISTING_AUTO_PUBLISH_QUEUE_TOOLTIPS.retry}>
            <Button
              type="text"
              size="small"
              aria-label="Auto publish lại"
              icon={<ReloadOutlined />}
              loading={retrying}
              onClick={retryAutoPublish}
              style={{
                width: 18,
                height: 18,
                minWidth: 18,
                padding: 0,
                fontSize: 10,
                lineHeight: 1,
              }}
            />
          </Tooltip>
        ) : null}
      </span>
    );
  }

  return null;
};

const publishSourceBadge = (
  listing: ListingListItem,
  onOpenLog?: (listing: ListingListItem) => void,
) => {
  if (listing.pageType?.toLowerCase() !== 'published') return null;
  const clickable = listing.publishSource === 'auto' && !!onOpenLog;
  const openLog = (e: MouseEvent | KeyboardEvent) => {
    e.stopPropagation();
    onOpenLog?.(listing);
  };
  if (listing.publishSource === 'auto') {
    return (
      <Tooltip title={clickable ? 'Publish tự động — bấm xem log' : 'Publish tự động'}>
        <Tag
          color="purple"
          style={{
            ...AUTO_PUBLISH_TAG_STYLE,
            cursor: clickable ? 'pointer' : undefined,
          }}
          role={clickable ? 'button' : undefined}
          tabIndex={clickable ? 0 : undefined}
          onClick={clickable ? openLog : undefined}
          onKeyDown={
            clickable
              ? (e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openLog(e);
                  }
                }
              : undefined
          }
        >
          Auto
        </Tag>
      </Tooltip>
    );
  }
  if (listing.publishSource === 'manual') {
    return (
      <Tag style={AUTO_PUBLISH_TAG_STYLE}>
        Manual
      </Tag>
    );
  }
  return null;
};

const CARD_THUMB_SIZE = 52;

const isHttpUrl = (value: string) => {
  try {
    const parsed = new URL(value.trim());
    return (
      (parsed.protocol === 'http:' || parsed.protocol === 'https:') && !!parsed.hostname
    );
  } catch {
    return false;
  }
};

function ListingSourceMeta({
  listing,
  reviewLine,
  trailing,
}: {
  listing: ListingListItem;
  reviewLine: ReactNode;
  trailing?: ReactNode;
}) {
  return (
    <>
      <div className="listing-card-line listing-card-line--row">{reviewLine}</div>
      <div className="listing-card-line listing-card-line--row">
        <ListingSupplierLine source={listing} trailing={trailing} />
      </div>
    </>
  );
}

const VariantBlock = ({ variant }: { variant: ListingVariation }) => (
  <div style={{ display: 'flex', gap: 8, minWidth: 0 }}>
    <ProductThumb src={firstImage(variant.mainImage)} />
    <div style={{ minWidth: 0, flex: 1 }}>
      <div
        style={{
          lineHeight: 1.4,
          fontWeight: 500,
          display: '-webkit-box',
          WebkitLineClamp: 2,
          WebkitBoxOrient: 'vertical',
          overflow: 'hidden',
        }}
        title={variant.title || undefined}
      >
        {variant.title || '—'}
      </div>
      <div style={metaLineStyle}>
        SKU: {variant.sellerSku || '—'}
        {variant.initialPrice != null ? ` · Init $${formatAmount(variant.initialPrice)}` : ''}
        {variant.salePrice != null ? ` · $${formatAmount(variant.salePrice)}` : ''}
        {variant.quantity != null ? ` · ×${variant.quantity}` : ''}
      </div>
    </div>
  </div>
);

function ListingCardThumb({
  src,
  loading,
  index,
}: {
  src?: string | null;
  loading?: boolean;
  index: number;
}) {
  return (
    <div className="listing-thumb-wrap">
      <span className="listing-thumb-index" aria-hidden="true">
        {index}
      </span>
      <ProductThumb src={src} loading={loading} size={CARD_THUMB_SIZE} />
    </div>
  );
}

function ListingSourceUrlEditor({
  saving,
  onSave,
}: {
  saving?: boolean;
  onSave: (url: string) => Promise<void>;
}) {
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState('');

  const submit = async () => {
    const url = value.trim();
    if (!url) {
      message.warning('Dán URL nguồn sản phẩm NCC');
      return;
    }
    if (!isHttpUrl(url)) {
      message.warning('URL phải bắt đầu bằng http(s)');
      return;
    }
    try {
      await onSave(url);
      setOpen(false);
      setValue('');
    } catch {
      // Hook already toasted the API error; keep the popover open to retry.
    }
  };

  return (
    <Popover
      trigger="click"
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setValue('');
      }}
      placement="bottomLeft"
      destroyOnHidden
      content={
        <div
          className="listing-source-url-pop"
          onClick={(e) => e.stopPropagation()}
          onKeyDown={(e) => e.stopPropagation()}
        >
          <Input
            autoFocus
            size="small"
            placeholder="https://us.shein.com/..."
            value={value}
            onChange={(e) => setValue(e.target.value)}
            onPressEnter={() => void submit()}
            disabled={saving}
            aria-label="URL nguồn supplier"
          />
          <Button type="primary" size="small" loading={saving} onClick={() => void submit()}>
            Lưu
          </Button>
        </div>
      }
    >
      <Button
        type="text"
        size="small"
        className={`listing-card-title-edit${open ? ' is-open' : ''}`}
        icon={<EditOutlined />}
        aria-label="Thêm URL nguồn"
        title="Thêm URL nguồn"
        onClick={(e) => e.stopPropagation()}
      />
    </Popover>
  );
}

function ListingTitleLine({
  listing,
  title,
  productUrl,
  canManage,
  onSaveSourceUrl,
  savingSourceUrl,
}: {
  listing: ListingListItem;
  title: string;
  productUrl: string | null;
  canManage: boolean;
  onSaveSourceUrl?: (listing: ListingListItem, sourceUrl: string) => Promise<void>;
  savingSourceUrl?: boolean;
}) {
  const tooltip = listing.tikTokProductId ? `${title} · TT: ${listing.tikTokProductId}` : title;
  const titleNode = productUrl ? (
    <a
      href={productUrl}
      target="_blank"
      rel="noreferrer"
      title={`${tooltip} · Mở URL nguồn`}
      className="listing-card-line listing-card-line--title"
    >
      {title}
    </a>
  ) : (
    <div
      className="listing-card-line listing-card-line--title listing-card-line--title-no-source"
      title={tooltip ? `${tooltip} (chưa có URL nguồn)` : 'Chưa có URL nguồn'}
    >
      {title}
    </div>
  );
  const originBadge =
    listing.addedFrom === 'recommended' ? (
      <span className="listing-ship-badge listing-ship-badge--recommended" title="Thêm từ lọc sản phẩm extension">
        Đề xuất
      </span>
    ) : null;
  const showSourceEditor = !productUrl && canManage && !!onSaveSourceUrl;

  if (!originBadge && !showSourceEditor) {
    return titleNode;
  }

  return (
    <div className="listing-card-title-row">
      {titleNode}
      {originBadge}
      {showSourceEditor ? (
        <ListingSourceUrlEditor
          saving={savingSourceUrl}
          onSave={(url) => onSaveSourceUrl!(listing, url)}
        />
      ) : null}
    </div>
  );
}

const ListingCard = memo(function ListingCard({
  listing,
  index,
  platforms,
  onEdit,
  onPrefetchEdit,
  canManage,
  selected,
  onSelectChange,
  onDelete,
  onActivate,
  onDeactivate,
  onDuplicate,
  onSync,
  activating,
  deleting,
  deactivating,
  duplicating,
  syncing,
  variantsOpen,
  onToggleVariants,
  isMobile,
  ratingSuccessMin = LISTING_RATING_DEFAULTS.successMin,
  ratingWarningMin = LISTING_RATING_DEFAULTS.warningMin,
  showOwnerName = true,
  onSaveSourceUrl,
  savingSourceUrl,
  onOpenAutoPublishLog,
  onRetryAutoPublish,
  retryingAutoPublishId,
  canRetryAutoPublish,
}: {
  listing: ListingListItem;
  index: number;
  platforms: PlatformOption[];
  onEdit: (listing: ListingListItem, opts?: { newTab?: boolean }) => void;
  onPrefetchEdit?: (id: string) => void;
  canManage: boolean;
  selected?: boolean;
  onSelectChange?: (id: string, checked: boolean) => void;
  onDelete: (listing: ListingListItem) => void;
  onActivate: (listing: ListingListItem) => void;
  onDeactivate: (listing: ListingListItem) => void;
  onDuplicate: (listing: ListingListItem) => void;
  onSync: (listing: ListingListItem) => void;
  activating?: boolean;
  deleting?: boolean;
  deactivating?: boolean;
  duplicating?: boolean;
  syncing?: boolean;
  variantsOpen: boolean;
  onToggleVariants: (id: string) => void;
  isMobile?: boolean;
  ratingSuccessMin?: number;
  ratingWarningMin?: number;
  showOwnerName?: boolean;
  onSaveSourceUrl?: (listing: ListingListItem, sourceUrl: string) => Promise<void>;
  savingSourceUrl?: boolean;
  onOpenAutoPublishLog?: (listing: ListingListItem) => void;
  onRetryAutoPublish?: (listing: ListingListItem) => void;
  retryingAutoPublishId?: string | null;
  canRetryAutoPublish?: boolean;
}) {
  const autoPublishBadgeOptions = {
    canRetry: canRetryAutoPublish,
    retrying: retryingAutoPublishId === listing.id,
    onRetry: onRetryAutoPublish,
  };
  const status = listing.siteStatus || listing.pageType;
  const listingId = String(listing.fourSellerListingId || listing.tikTokProductId || '');
  const isDraft = listing.pageType?.toLowerCase() === 'draft';
  // Draft: only show cost (InitialPrice / giá nhập). Do not fall back to listing low/high (sale/page).
  const initialPrice = isDraft ? formatInitialPrice(listing) : null;
  const price = isDraft ? initialPrice : formatPrice(listing);
  const priceLabel = isDraft ? 'Giá nhập' : 'Giá';
  const img = firstImage(listing.mainImage);
  const title = listing.productName || '—';
  const productUrl = listing.sourceUrl?.trim() || null;
  const ratingNum =
    listing.averageRating != null && Number.isFinite(Number(listing.averageRating))
      ? Number(listing.averageRating)
      : null;
  const ratingTier: ListingRatingTier | null =
    ratingNum != null
      ? getListingRatingTier(ratingNum, ratingSuccessMin, ratingWarningMin)
      : null;
  const reviewLine =
    ratingNum != null || listing.reviewCount != null ? (
      <div
        className={
          ratingTier ? `listing-rating listing-rating--${ratingTier}` : 'listing-rating'
        }
        title={
          ratingNum != null
            ? `${ratingNum.toFixed(2)}` +
              (listing.reviewCount != null
                ? ` (${listing.reviewCount.toLocaleString()})`
                : '')
            : listing.reviewCount != null
              ? `(${listing.reviewCount.toLocaleString()})`
              : undefined
        }
      >
        {ratingNum != null && <ListingStars rating={ratingNum} tier={ratingTier} />}
        {ratingNum != null && (
          <span className="listing-rating-value">{ratingNum.toFixed(2)}</span>
        )}
        {listing.reviewCount != null && (
          <span className="listing-review-count">
            ({listing.reviewCount.toLocaleString()})
          </span>
        )}
      </div>
    ) : null;
  const variantCount = listing.variationCount || listing.variations?.length || 0;
  const variants = listing.variations ?? [];
  const [showAllVariants, setShowAllVariants] = useState(false);
  const visibleVariants = showAllVariants ? variants : variants.slice(0, VARIANT_PREVIEW_COUNT);
  const hasMoreVariants = variants.length > VARIANT_PREVIEW_COUNT;
  const syncErrorDisplay = listing.syncError
    ? formatSyncErrorDisplay(listing.syncError)
    : null;

  useEffect(() => {
    if (!variantsOpen) setShowAllVariants(false);
  }, [variantsOpen]);

  const actionItems = [
    {
      key: 'edit',
      label: 'Chỉnh sửa',
      icon: <EditOutlined />,
      onClick: () => onEdit(listing, { newTab: true }),
    },
    ...(canManage
      ? [
          {
            key: 'duplicate',
            label: 'Duplicate',
            icon: <CopyOutlined />,
            title: 'Tạo bản sao mới',
            disabled: duplicating || !listing.fourSellerListingId,
            onClick: () => onDuplicate(listing),
          },
        ]
      : []),
    ...(canManage && isDraft
      ? [
          {
            key: 'sync',
            label: 'Đồng bộ',
            icon: <SyncOutlined />,
            title: 'Kéo đầy đủ draft từ 4Seller',
            onClick: () => onSync(listing),
          },
        ]
      : []),
    ...(canManage && listing.siteStatus?.toLowerCase() === 'inactive'
      ? [
          {
            key: 'activate',
            label: 'Activate',
            icon: <PlayCircleOutlined />,
            title: 'Active listing',
            onClick: () => onActivate(listing),
          },
        ]
      : []),
    ...(canManage && listing.siteStatus?.toLowerCase() === 'active'
      ? [
          {
            key: 'deactivate',
            label: 'Deactivate',
            icon: <StopOutlined />,
            title: 'Hủy đăng',
            onClick: () => onDeactivate(listing),
          },
        ]
      : []),
    ...(canManage
      ? [
          {
            key: 'delete',
            danger: true,
            label: 'Delete',
            icon: <DeleteOutlined />,
            onClick: () => onDelete(listing),
          },
        ]
      : []),
  ];

  const headerMetaParts = [
    showOwnerName && listing.ownerName ? listing.ownerName : null,
    listing.shopName || null,
    listing.shopProfileName || null,
  ].filter(Boolean) as string[];

  const headerActions = (
    <div style={{ display: 'flex', alignItems: 'center', gap: isMobile ? 2 : 4, flexShrink: 0 }}>
      {price != null ? (
        <MoneyAmount
          amount={price}
          currency={listing.currency}
          hint={isDraft ? priceLabel : null}
          style={{ marginRight: isMobile ? 2 : 4 }}
        />
      ) : (
        <Text type="secondary" style={{ fontSize: 12, marginRight: isMobile ? 2 : 4 }}>
          —
        </Text>
      )}
      {onOpenAutoPublishLog &&
      shouldShowAutoPublishLog(listing) &&
      !isAutoPublishQueueBadgeClickable(listing) &&
      !(listing.pageType?.toLowerCase() === 'published' && listing.publishSource === 'auto') ? (
        <Tooltip title="Log auto publish">
          <Button
            type="text"
            size="small"
            icon={<HistoryOutlined />}
            aria-label="Log auto publish"
            onClick={() => onOpenAutoPublishLog(listing)}
          />
        </Tooltip>
      ) : null}
      <Tooltip title="Chỉnh sửa (tab mới)">
        <Button
          type="text"
          size="small"
          icon={<EditOutlined />}
          aria-label="Chỉnh sửa"
          onMouseEnter={() => onPrefetchEdit?.(listing.id)}
          onFocus={() => onPrefetchEdit?.(listing.id)}
          onClick={(e) => {
            e.stopPropagation();
            onEdit(listing, { newTab: true });
          }}
        />
      </Tooltip>
      {actionItems.length > 0 && (
        <Dropdown
          menu={{
            items: actionItems,
            style: { minWidth: 140 },
          }}
          trigger={['click']}
        >
          <Button
            type="text"
            size="small"
            icon={<MenuOutlined />}
            aria-label="Listing actions"
            loading={deleting || activating || deactivating || syncing}
          />
        </Dropdown>
      )}
    </div>
  );

  const syncErrorAlert = syncErrorDisplay ? (
    <div
      className="listing-sync-error-alert-wrap"
      style={{ padding: isMobile ? '0 10px 4px' : '0 12px 4px' }}
    >
      <Alert
        type="warning"
        showIcon
        className="listing-sync-error-alert"
        icon={<ExclamationCircleFilled style={{ color: '#ff4d4f', fontSize: 14 }} />}
        style={{
          marginTop: 0,
          background: 'var(--listing-error-bg)',
          border: '1px solid var(--listing-error-border)',
        }}
        title={
          <Tooltip title={syncErrorDisplay.body}>
            <span className="listing-sync-error-alert__text">
              {syncErrorDisplay.body}
            </span>
          </Tooltip>
        }
        action={
          <Button type="link" size="small" onClick={() => onEdit(listing, { newTab: true })}>
            Sửa chi tiết
          </Button>
        }
      />
    </div>
  ) : null;

  const variantsPanel =
    variantsOpen && !isMobile ? (
      <div
        style={{
          borderTop: '1px solid var(--listing-border)',
          padding: '10px 12px',
          background: 'var(--listing-surface-muted)',
          display: 'flex',
          flexDirection: 'column',
          gap: 8,
        }}
      >
        {variants.length > 0 ? (
          visibleVariants.map((v) => <VariantBlock key={v.id} variant={v} />)
        ) : (
          <Text type="secondary" style={{ fontSize: 12 }}>
            Chưa có variant trên list — mở Edit để xem đầy đủ.
          </Text>
        )}
        {hasMoreVariants ? (
          <Button
            type="link"
            size="small"
            style={{ padding: 0, height: 'auto', fontSize: 12, alignSelf: 'flex-start' }}
            onClick={() => setShowAllVariants((open) => !open)}
          >
            {showAllVariants ? (
              <>
                Thu gọn <UpOutlined />
              </>
            ) : (
              <>
                Xem thêm {variants.length - VARIANT_PREVIEW_COUNT} variant <DownOutlined />
              </>
            )}
          </Button>
        ) : null}
      </div>
    ) : null;

  if (isMobile) {
    return (
      <div
        style={{
          border: '1px solid var(--listing-border)',
          borderRadius: 8,
          background: selected ? 'var(--listing-surface-selected)' : 'var(--listing-surface)',
          marginBottom: 8,
          overflow: 'hidden',
        }}
      >
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 6,
            padding: '2px 4px 2px 10px',
            background: 'var(--listing-surface-muted)',
            borderBottom: '1px solid var(--listing-border)',
            minHeight: 28,
            height: 28,
          }}
        >
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 5,
              minWidth: 0,
              flex: 1,
              fontSize: 12,
              lineHeight: 1,
            }}
          >
            {canManage && onSelectChange ? (
              <Checkbox
                checked={!!selected}
                onChange={(e) => onSelectChange(listing.id, e.target.checked)}
                onClick={(e) => e.stopPropagation()}
                aria-label="Chọn listing"
              />
            ) : null}
            {listing.platform ? (
              <PlatformAvatar platformCode={listing.platform} platforms={platforms} size={16} />
            ) : null}
            {listingId ? (
              <Tooltip title={listingId}>
                <Text
                  strong
                  role="button"
                  tabIndex={0}
                  onClick={() => void copyText(listingId)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      void copyText(listingId);
                    }
                  }}
                  style={{
                    fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
                    fontSize: 12,
                    cursor: 'pointer',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    minWidth: 0,
                  }}
                >
                  {listingId}
                </Text>
              </Tooltip>
            ) : (
              <Text type="secondary">—</Text>
            )}
            <Tooltip title={statusLabel(status)}>
              <span
                aria-label={statusLabel(status)}
                style={{
                  width: 7,
                  height: 7,
                  borderRadius: '50%',
                  background: statusDotColor(status),
                  flexShrink: 0,
                  display: 'inline-block',
                }}
              />
            </Tooltip>
            {isDraft
              ? autoPublishQueueBadge(listing, onOpenAutoPublishLog, autoPublishBadgeOptions)
              : publishSourceBadge(listing, onOpenAutoPublishLog)}
            {headerMetaParts.length > 0 ? (
              <Tooltip title={headerMetaParts.join(' · ')}>
                <span
                  style={{
                    color: 'var(--listing-text-tertiary)',
                    textTransform: 'uppercase',
                    letterSpacing: '0.02em',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    flexShrink: 1,
                    minWidth: 0,
                    maxWidth: 140,
                  }}
                >
                  {headerMetaParts.join(' · ')}
                </span>
              </Tooltip>
            ) : null}
            {missingSizeChartIcon(listing)}
          </div>
          {headerActions}
        </div>

        <div style={{ padding: '6px 10px', display: 'flex', gap: 8, minWidth: 0 }}>
          <ListingCardThumb src={img} loading={shouldSpinPublishPendingThumb(listing)} index={index} />
          <div className="listing-card-lines">
            <ListingTitleLine
              listing={listing}
              title={title}
              productUrl={productUrl}
              canManage={canManage}
              onSaveSourceUrl={onSaveSourceUrl}
              savingSourceUrl={savingSourceUrl}
            />
            <ListingSourceMeta
              listing={listing}
              reviewLine={reviewLine}
              trailing={
                [
                  listing.availableStock != null ? `Tồn ${listing.availableStock}` : null,
                  variantCount > 0 ? `${variantCount} SKU` : null,
                ]
                  .filter(Boolean)
                  .join(' · ') || '—'
              }
            />
          </div>
        </div>

        {syncErrorAlert}
      </div>
    );
  }

  return (
    <div
      style={{
        border: '1px solid var(--listing-border)',
        borderRadius: 8,
        background: selected ? 'var(--listing-surface-selected)' : 'var(--listing-surface)',
        marginBottom: 10,
        overflow: 'hidden',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          padding: '2px 12px',
          background: 'var(--listing-surface-muted)',
          borderBottom: '1px solid var(--listing-border)',
          minHeight: 28,
          height: 28,
        }}
      >
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '4px 8px',
            minWidth: 0,
            flex: 1,
            fontSize: 12,
            lineHeight: 1,
            overflow: 'hidden',
          }}
        >
          {canManage && onSelectChange ? (
            <Checkbox
              checked={!!selected}
              onChange={(e) => onSelectChange(listing.id, e.target.checked)}
              onClick={(e) => e.stopPropagation()}
              aria-label="Chọn listing"
            />
          ) : null}
          {listing.platform ? (
            <PlatformAvatar platformCode={listing.platform} platforms={platforms} />
          ) : (
            <span style={{ width: 20, height: 20, flexShrink: 0 }} aria-hidden />
          )}
          {listingId ? (
            <Tooltip title={listingId}>
              <Text
                strong
                role="button"
                tabIndex={0}
                onClick={() => void copyText(listingId)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    void copyText(listingId);
                  }
                }}
                style={{
                  fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
                  fontSize: 12,
                  cursor: 'pointer',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  minWidth: 0,
                }}
              >
                {listingId}
              </Text>
            </Tooltip>
          ) : (
            <Text type="secondary">—</Text>
          )}
          <Tooltip title={statusLabel(status)}>
            <span
              aria-label={statusLabel(status)}
              style={{
                width: 7,
                height: 7,
                borderRadius: '50%',
                background: statusDotColor(status),
                flexShrink: 0,
                display: 'inline-block',
              }}
            />
          </Tooltip>
          {isDraft
            ? autoPublishQueueBadge(listing, onOpenAutoPublishLog, autoPublishBadgeOptions)
            : publishSourceBadge(listing, onOpenAutoPublishLog)}
          {headerMetaParts.length > 0 ? (
            <Tooltip
              title={[
                showOwnerName && listing.ownerName ? 'Quản lý' : null,
                listing.shopName ? 'Shop' : null,
                listing.shopProfileName ? 'Profile' : null,
              ]
                .filter(Boolean)
                .join(' · ')}
            >
              <span
                style={{
                  color: 'var(--listing-text-tertiary)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.02em',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  minWidth: 0,
                  flexShrink: 1,
                }}
              >
                {headerMetaParts.join(' · ')}
              </span>
            </Tooltip>
          ) : null}
          {missingSizeChartIcon(listing)}
        </div>

        {headerActions}
      </div>

      <div
        className="admin-card-grid listing-card-body"
        style={{ gridTemplateColumns: COL_TEMPLATE }}
      >
        <div className="listing-card-product">
          <ListingCardThumb src={img} loading={shouldSpinPublishPendingThumb(listing)} index={index} />
          <div className="listing-card-lines">
            <ListingTitleLine
              listing={listing}
              title={title}
              productUrl={productUrl}
              canManage={canManage}
              onSaveSourceUrl={onSaveSourceUrl}
              savingSourceUrl={savingSourceUrl}
            />
            <ListingSourceMeta listing={listing} reviewLine={reviewLine} />
          </div>
        </div>

        <div className="admin-col-meta listing-card-col">
          <div className="listing-card-line listing-card-line--primary">
            {listing.availableStock ?? '—'}
          </div>
          <div className="listing-card-line">
            {variantCount > 0 ? `${variantCount} SKU` : '—'}
          </div>
        </div>

        <div className="admin-col-meta listing-card-col">
          <div className="listing-card-line">
            <span className="listing-card-label">Cập nhật </span>
            <span className="listing-card-value">{formatDateTime(listing.platformUpdateTime)}</span>
          </div>
          <div className="listing-card-line">
            <span className="listing-card-label">{listing.lastPushAt ? 'Push ' : 'Sync '}</span>
            <span className="listing-card-value">
              {formatDateTime(listing.lastPushAt || listing.lastSyncedAt)}
            </span>
          </div>
        </div>

        <div className="admin-col-meta listing-card-col">
          <div className="listing-card-line listing-card-line--primary" title={listing.categoryName || undefined}>
            {listing.categoryName || '—'}
          </div>
          <div className="listing-card-line" title={listing.site || undefined}>
            {listing.site || '—'}
          </div>
        </div>

        <div className="admin-col-meta listing-card-col">
          {variantCount > 0 ? (
            <Button
              type="link"
              size="small"
              className="listing-card-variant-btn"
              onClick={() => onToggleVariants(listing.id)}
            >
              Variants ({variantCount}) {variantsOpen ? <UpOutlined /> : <DownOutlined />}
            </Button>
          ) : (
            <div className="listing-card-line listing-card-line--primary">—</div>
          )}
        </div>
      </div>

      {syncErrorAlert}
      {variantsPanel}
    </div>
  );
});

export type SharedListingsPageProps = {
  config: ListingsPortalConfig;
};

export const ListingsPage = ({ config }: SharedListingsPageProps) => {
  const deps = useListingsDeps();
  const location = useLocation();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const queryClient = useQueryClient();
  const canManage = deps.hasPermission(config.managePermission);
  const canViewSettings =
    config.useAdminSettingsForRating && deps.hasPermission('admin.settings.view');
  const isMobile = useIsMobile();
  const settingsQuery = deps.useAdminSettings?.({ enabled: !!canViewSettings });
  const adminSettings = settingsQuery?.data ?? [];
  const ratingSuccessMin = useMemo(
    () =>
      config.useAdminSettingsForRating
        ? parseListingRatingThreshold(
            adminSettings.find((s) => s.key === LISTING_RATING_SETTING_KEYS.successMin)?.value,
            LISTING_RATING_DEFAULTS.successMin,
          )
        : LISTING_RATING_DEFAULTS.successMin,
    [adminSettings, config.useAdminSettingsForRating],
  );
  const ratingWarningMin = useMemo(
    () =>
      config.useAdminSettingsForRating
        ? parseListingRatingThreshold(
            adminSettings.find((s) => s.key === LISTING_RATING_SETTING_KEYS.warningMin)?.value,
            LISTING_RATING_DEFAULTS.warningMin,
          )
        : LISTING_RATING_DEFAULTS.warningMin,
    [adminSettings, config.useAdminSettingsForRating],
  );

  const pageType: 'draft' | 'published' = location.pathname.includes('/published')
    ? 'published'
    : 'draft';
  const siteStatus =
    pageType === 'published' ? (searchParams.get('siteStatus') || '').trim() : '';

  const [ownerUserId, setOwnerUserId] = useState<string | undefined>();
  const [platformCode, setPlatformCode] = useState<string | undefined>();
  const [shopId, setShopId] = useState<string | undefined>();
  const [search, setSearch] = useState('');
  const [searchApplied, setSearchApplied] = useState('');
  const [datePreset, setDatePreset] = useState<DatePreset>('7d');
  const [dateRange, setDateRange] = useState<[Dayjs, Dayjs] | null>(() => rangeForPreset('7d'));
  const [pageCurrent, setPageCurrent] = useState(1);
  const [pageSize, setPageSize] = useState(deps.defaultPageSize || PAGE_SIZE);
  const [variantOpenIds, setVariantOpenIds] = useState<Set<string>>(new Set());
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [bindCategoryListing, setBindCategoryListing] = useState<ListingListItem | null>(null);
  const [autoPublishListing, setAutoPublishListing] = useState<ListingListItem | null>(null);

  const autoPublishRunsQuery = deps.useListingAutoPublishRuns?.(
    autoPublishListing?.id,
    { enabled: !!autoPublishListing },
  );
  const retryAutoPublishMutation = deps.useRetryListingAutoPublish?.();
  const canOpenAutoPublishLog = !!deps.useListingAutoPublishRuns;
  const canRetryAutoPublish = canManage && !!retryAutoPublishMutation;
  const retryingAutoPublishId =
    retryAutoPublishMutation?.isPending && retryAutoPublishMutation.variables
      ? retryAutoPublishMutation.variables
      : null;

  const handleRetryAutoPublish = useCallback(
    (listing: ListingListItem) => {
      if (!retryAutoPublishMutation) return;
      void retryAutoPublishMutation.mutateAsync(listing.id).then(() => {
        setAutoPublishListing((prev) =>
          prev?.id === listing.id
            ? { ...prev, autoPublishSkipped: false, autoPublishSkipReason: null }
            : prev,
        );
      });
    },
    [retryAutoPublishMutation],
  );

  const handleCloseAutoPublishTimeline = useCallback(() => {
    setAutoPublishListing(null);
  }, []);

  const handleRetryFromAutoPublishTimeline = useCallback(() => {
    if (!autoPublishListing) return;
    handleRetryAutoPublish(autoPublishListing);
  }, [autoPublishListing, handleRetryAutoPublish]);

  // Reset paging when switching Draft ↔ Published via sidebar.
  useEffect(() => {
    setPageCurrent(1);
    setVariantOpenIds(new Set());
    setSelectedIds(new Set());
  }, [pageType]);

  const setSiteStatus = (next: string) => {
    const nextParams = new URLSearchParams(searchParams);
    if (next) nextParams.set('siteStatus', next);
    else nextParams.delete('siteStatus');
    setSearchParams(nextParams, { replace: true });
    setPageCurrent(1);
  };

  const sellersQuery = deps.useSellers?.();
  const sellers = sellersQuery?.data ?? [];
  const { data: platforms = [] } = deps.usePlatformOptions();
  const { data: shops = [] } = deps.useShopOptions();

  const listParams = useMemo(
    () => {
      const { dateFrom, dateTo } = toUtcIsoRange(dateRange?.[0], dateRange?.[1]);
      return {
        pageCurrent,
        pageSize,
        pageType,
        siteStatus: pageType === 'published' && siteStatus ? siteStatus : undefined,
        ownerUserId: config.showSellerFilter ? ownerUserId : undefined,
        platform: platformCode,
        shopId,
        search: searchApplied || undefined,
        dateFrom,
        dateTo,
      };
    },
    [
      pageCurrent,
      pageSize,
      pageType,
      siteStatus,
      ownerUserId,
      platformCode,
      shopId,
      searchApplied,
      dateRange,
      config.showSellerFilter,
    ],
  );

  const { data, isLoading, isFetching, refetch, isError, error } = deps.useListingsList(listParams);
  const syncMutation = deps.useSyncListings();
  const [syncJobId, setSyncJobId] = useState<string | null>(null);
  const syncJob = deps.useListingSyncJob(syncJobId).data;
  const syncOneMutation = deps.useSyncOneListing();
  const deleteMutation = deps.useDeleteListing();
  const deleteByFilterMutation = deps.useDeleteListingsByFilter();
  const bulkDeleteMutation = deps.useBulkDeleteListings();
  const bulkDeactivateMutation = deps.useBulkDeactivateListings();
  const activateMutation = deps.useActivateListing();
  const deactivateMutation = deps.useDeactivateListing();
  const duplicateMutation = deps.useDuplicateListing();
  const updateSourceUrlMutation = deps.useUpdateListingSourceUrl();

  const shopOptions = useMemo(() => {
    return shops
      .filter((s) => s.status === 'Active')
      .filter((s) => !config.showSellerFilter || !ownerUserId || s.ownerId === ownerUserId)
      .filter((s) => {
        if (!platformCode) return true;
        return (s.platformCode || '').toLowerCase() === platformCode.toLowerCase();
      })
      .map((s) => {
        const profile = s.profileName?.trim();
        const shopName = s.name?.trim() || 'Shop';
        return {
          value: s.id,
          label: profile ? `${profile} (${shopName})` : shopName,
        };
      });
  }, [shops, ownerUserId, platformCode, config.showSellerFilter]);

  const counts = data?.statusCounts;
  const items = data?.items ?? [];
  const total = data?.total ?? 0;
  const syncRunning =
    syncMutation.isPending || (!!syncJobId && (!syncJob || syncJob.state === 'running'));
  const busy =
    isLoading ||
    syncRunning ||
    deleteByFilterMutation.isPending ||
    bulkDeleteMutation.isPending ||
    bulkDeactivateMutation.isPending;

  const pageIds = useMemo(() => items.map((item) => item.id), [items]);
  const selectedCount = selectedIds.size;
  const allPageSelected = pageIds.length > 0 && pageIds.every((id) => selectedIds.has(id));
  const somePageSelected = pageIds.some((id) => selectedIds.has(id));

  // Clear selection when filters / tab change (keep across paging).
  useEffect(() => {
    setSelectedIds(new Set());
  }, [siteStatus, ownerUserId, platformCode, shopId, searchApplied, dateRange]);

  const handleSelectChange = useCallback((id: string, checked: boolean) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }, []);

  const handleSelectAllPage = useCallback(
    (checked: boolean) => {
      setSelectedIds((prev) => {
        const next = new Set(prev);
        if (checked) {
          for (const id of pageIds) next.add(id);
        } else {
          for (const id of pageIds) next.delete(id);
        }
        return next;
      });
    },
    [pageIds],
  );

  const handleBulkDelete = useCallback(() => {
    const ids = Array.from(selectedIds);
    if (ids.length === 0) return;
    Modal.confirm({
      title: `Xóa ${ids.length} listing đã chọn?`,
      content: 'Sẽ xóa trên 4Seller và local. Bạn có muốn tiếp tục?',
      okText: 'Xóa',
      okButtonProps: { danger: true },
      cancelText: 'Hủy',
      onOk: async () => {
        await bulkDeleteMutation.mutateAsync(ids);
        setSelectedIds(new Set());
      },
    });
  }, [selectedIds, bulkDeleteMutation]);

  const handleBulkDeactivate = useCallback(() => {
    const ids = Array.from(selectedIds);
    if (ids.length === 0) return;
    Modal.confirm({
      title: `Deactivate ${ids.length} listing đã chọn?`,
      content:
        'Các listing đang Active sẽ chuyển Inactive trên TikTok; bản mirror vẫn giữ. Listing không Active sẽ được bỏ qua.',
      okText: 'Deactivate',
      okButtonProps: { danger: true },
      cancelText: 'Hủy',
      onOk: async () => {
        await bulkDeactivateMutation.mutateAsync(ids);
        setSelectedIds(new Set());
      },
    });
  }, [selectedIds, bulkDeactivateMutation]);

  // While a multi-account sync runs, show how many sellers are done instead of a bare spinner.
  const syncLabel =
    syncRunning && syncJob && syncJob.totalSellers > 1
      ? `${syncJob.completedSellers}/${syncJob.totalSellers}`
      : 'Sync';

  const syncTooltip = useMemo(() => {
    if (!syncRunning || !syncJob || syncJob.sellers.length === 0) {
      if (pageType === 'draft') {
        return 'Kéo đầy đủ draft từ 4Seller (mô tả, variant, ảnh…)';
      }
      return dateRange
        ? 'Cập nhật trạng thái trong khoảng ngày đang lọc (createTime; không ghi đè nội dung đã sửa)'
        : 'Cập nhật trạng thái listing từ 4Seller (không ghi đè nội dung đã sửa). Chọn khoảng ngày để sync đúng cửa sổ đang xem.';
    }

    const done = syncJob.sellers
      .filter((s) => s.status !== 'pending' && s.status !== 'running')
      .map((s) => `${s.ownerName?.trim() || 'Seller'}: ${SYNC_SELLER_STATUS_LABELS[s.status]}`);
    const running = syncJob.sellers.filter((s) => s.status === 'running').length;

    return [
      `Đang đồng bộ ${syncJob.completedSellers}/${syncJob.totalSellers} tài khoản`,
      running > 0 ? `${running} đang chạy` : null,
      ...done,
    ]
      .filter(Boolean)
      .join(' — ');
  }, [syncRunning, syncJob, pageType, dateRange]);

  // The job outlives the request that started it, so the result toast and the list refresh are
  // driven by the poll reaching a terminal state rather than by the mutation resolving.
  const handledSyncJobRef = useRef<string | null>(null);
  useEffect(() => {
    if (!syncJob || syncJob.state === 'running') return;
    if (handledSyncJobRef.current === syncJob.jobId) return;
    handledSyncJobRef.current = syncJob.jobId;

    if (syncJob.state === 'failed') {
      message.error(syncJob.error || 'Đồng bộ listing thất bại.');
    } else {
      const baseMsg = syncJob.message || 'Đồng bộ listing thành công.';
      // Prefer server message (already includes reconcile metrics); fall back if older API.
      const hasReconcileInMsg = /reconcile|Reviewing/i.test(baseMsg);
      const reconcileHint =
        !hasReconcileInMsg &&
        ((syncJob.reconciledUpdated ?? 0) > 0 ||
          (syncJob.reconciledRemoved ?? 0) > 0 ||
          (syncJob.stillInFlight ?? 0) > 0)
          ? ` Reviewing: cập nhật ${syncJob.reconciledUpdated ?? 0}, gỡ ${syncJob.reconciledRemoved ?? 0}, còn ${syncJob.stillInFlight ?? 0}.`
          : '';
      const problems = syncJob.sellers.filter(
        (s) => s.status === 'failed' || s.status === 'skipped_busy',
      );
      if (problems.length > 0) {
        const detail = problems
          .map((s) => `${s.ownerName?.trim() || s.ownerUserId}: ${s.message ?? 'lỗi'}`)
          .join(' | ');
        message.warning(`${baseMsg}${reconcileHint} — ${detail}`, 10);
      } else {
        message.success(`${baseMsg}${reconcileHint}`, 8);
      }
    }

    setSyncJobId(null);
    void refetch();
  }, [syncJob, refetch]);

  const handleSync = async () => {
    const isDraftPage = pageType === 'draft';
    const selectedShop = shopId ? shops.find((s) => s.id === shopId) : undefined;
    const fourSellerShopId = selectedShop?.fourSellerShopId != null
      ? String(selectedShop.fourSellerShopId).trim()
      : undefined;
    // Date preset/range on the list is forwarded for published status sync so createTime*
    // matches what the user is looking at (skips delta watermark). Draft stays uncapped-window.
    const { dateFrom, dateTo } = !isDraftPage && dateRange
      ? toUtcIsoRange(dateRange[0], dateRange[1])
      : { dateFrom: undefined, dateTo: undefined };
    // When a shop is selected, scope the pull to that 4Seller shop (and its owner) so delta
    // watermarks for the whole seller do not hide older drafts of this shop.
    const response = await syncMutation.mutateAsync({
      ownerUserId: config.showSellerFilter
        ? ownerUserId || selectedShop?.ownerId || undefined
        : undefined,
      shopId: fourSellerShopId || undefined,
      platform: (platformCode || 'tiktok').toLowerCase(),
      statuses: isDraftPage ? ['draft'] : [...PUBLISHED_SYNC_STATUSES],
      syncScope: isDraftPage ? 'full' : 'status',
      maxRecordsPerSeller:
        dateFrom && dateTo ? SYNC_MAX_RECORDS_WITH_DATE : SYNC_MAX_RECORDS,
      dateFrom,
      dateTo,
    });

    // The pull continues server-side; the poll below drives the spinner and the final toast.
    if (response.data?.jobId) {
      setSyncJobId(response.data.jobId);
    }
  };

  const handleDeleteByFilter = useCallback(() => {
    Modal.confirm({
      title: 'Xóa listing theo bộ lọc hiện tại?',
      content: `Sẽ xóa khoảng ${total} listing khớp bộ lọc đang chọn trên 4Seller và local. Bạn có muốn tiếp tục?`,
      okText: 'Xóa',
      okButtonProps: { danger: true },
      cancelText: 'Hủy',
      onOk: async () => {
        await deleteByFilterMutation.mutateAsync(listParams);
        setPageCurrent(1);
        setSelectedIds(new Set());
      },
    });
  }, [total, listParams, deleteByFilterMutation]);

  const handleDeleteOne = useCallback((listing: ListingListItem) => {
    Modal.confirm({
      title: 'Xóa listing?',
      content: 'Bạn có chắc chắn muốn xóa listing này?',
      okText: 'Xóa',
      okButtonProps: { danger: true },
      cancelText: 'Hủy',
      onOk: async () => {
        await deleteMutation.mutateAsync(listing.id);
        setSelectedIds((prev) => {
          if (!prev.has(listing.id)) return prev;
          const next = new Set(prev);
          next.delete(listing.id);
          return next;
        });
      },
    });
  }, [deleteMutation]);

  const handleDeactivate = useCallback((listing: ListingListItem) => {
    Modal.confirm({
      title: 'Deactivate listing?',
      content: 'Bạn có chắc chắn muốn hủy đăng listing này?',
      okText: 'Deactivate',
      okButtonProps: { danger: true },
      cancelText: 'Hủy',
      onOk: () => deactivateMutation.mutateAsync(listing.id),
    });
  }, [deactivateMutation]);

  const handleActivate = useCallback((listing: ListingListItem) => {
    Modal.confirm({
      title: 'Activate listing?',
      content: `Active listing ${listing.fourSellerListingId} lại trên 4Seller.`,
      okText: 'Activate',
      cancelText: 'Hủy',
      onOk: () => activateMutation.mutateAsync(listing.id),
    });
  }, [activateMutation]);

  const handleDuplicate = useCallback((listing: ListingListItem) => {
    Modal.confirm({
      title: 'Duplicate listing?',
      content: 'Bạn có chắc chắn muốn tạo bản sao mới?',
      okText: 'Duplicate',
      cancelText: 'Hủy',
      onOk: () => duplicateMutation.mutateAsync(listing.id),
    });
  }, [duplicateMutation]);

  const handleSyncOne = useCallback((listing: ListingListItem) => {
    void syncOneMutation.mutateAsync(listing.id);
  }, [syncOneMutation]);

  const handleSaveSourceUrl = useCallback(
    (listing: ListingListItem, sourceUrl: string) =>
      updateSourceUrlMutation.mutateAsync({ id: listing.id, sourceUrl }).then(() => undefined),
    [updateSourceUrlMutation],
  );

  const handlePrefetchEdit = useCallback(
    (id: string) => {
      preloadListingEditChunk();
      void deps.prefetchListingDetail(queryClient, id);
    },
    [queryClient, deps],
  );

  const openEdit = useCallback(
    (id: string, opts?: { newTab?: boolean }) => {
      preloadListingEditChunk();
      const url = `/listings/${id}/edit`;

      if (opts?.newTab !== false) {
        openUrlInNewTab(url);
      } else {
        navigate(url);
      }

      // Prefetch nền: HTTP cache dùng chung giữa các tab; không chặn mở tab.
      void Promise.race([
        deps.prefetchListingDetail(queryClient, id),
        new Promise<void>((resolve) => {
          window.setTimeout(resolve, 400);
        }),
      ]).catch(() => {
        /* edit page will fetch */
      });
    },
    [queryClient, deps, navigate],
  );

  const handleEdit = useCallback(
    (listing: ListingListItem, opts?: { newTab?: boolean }) => {
      if (needsCategoryStep(listing)) {
        setBindCategoryListing(listing);
        return;
      }
      void openEdit(listing.id, opts);
    },
    [openEdit],
  );

  const toggleVariants = useCallback((listingId: string) => {
    setVariantOpenIds((prev) => {
      const next = new Set(prev);
      if (next.has(listingId)) next.delete(listingId);
      else next.add(listingId);
      return next;
    });
  }, []);

  const pageTitle = pageType === 'published' ? 'Published' : 'Draft';

  return (
    <div>
      <PageHeader
        title={pageTitle}
        extra={
          <Space wrap size={isMobile ? 4 : undefined}>
            {canManage && selectedCount > 0 && pageType === 'published' ? (
              <Button
                danger
                icon={<StopOutlined />}
                onClick={handleBulkDeactivate}
                loading={bulkDeactivateMutation.isPending}
                disabled={
                  bulkDeactivateMutation.isPending || bulkDeleteMutation.isPending
                }
                aria-label={`Deactivate ${selectedCount} đã chọn`}
              >
                {isMobile ? `(${selectedCount})` : `Deactivate (${selectedCount})`}
              </Button>
            ) : null}
            {canManage && selectedCount > 0 ? (
              <Button
                danger
                icon={<DeleteOutlined />}
                onClick={handleBulkDelete}
                loading={bulkDeleteMutation.isPending}
                disabled={
                  bulkDeleteMutation.isPending || bulkDeactivateMutation.isPending
                }
                aria-label={`Xóa ${selectedCount} đã chọn`}
              >
                {isMobile ? `(${selectedCount})` : `Xóa đã chọn (${selectedCount})`}
              </Button>
            ) : null}
            {canManage ? (
              <Button
                danger
                icon={<DeleteOutlined />}
                onClick={handleDeleteByFilter}
                disabled={total === 0}
                aria-label="Xóa theo bộ lọc"
              >
                {isMobile ? null : 'Xóa'}
              </Button>
            ) : null}
            {canManage ? (
              <Tooltip title={syncTooltip}>
                <Button
                  type="primary"
                  icon={<SyncOutlined />}
                  loading={syncRunning}
                  disabled={syncRunning}
                  onClick={handleSync}
                  aria-label="Sync"
                >
                  {isMobile ? null : syncLabel}
                </Button>
              </Tooltip>
            ) : null}
            <Button
              icon={<ReloadOutlined />}
              onClick={() => refetch()}
              loading={isFetching}
              aria-label="Tải lại"
            />
          </Space>
        }
      />

      <div className="admin-filters">
        <Tooltip
          title={
            'Tìm theo: tên SP · MSKU / variant · 4Seller listing id · TikTok product id · ' +
            'id local · shop TikTok · shop 4Seller · seller · danh mục · URL nguồn · shop NCC (Shein)'
          }
        >
          <Input.Search
            className="admin-filters-search"
            allowClear
            placeholder="Tên · SKU · ID · shop · URL · danh mục"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onSearch={(v) => {
              setSearchApplied(v.trim());
              setPageCurrent(1);
            }}
          />
        </Tooltip>
        <div className="admin-filters-grid">
          {config.showSellerFilter ? (
            <Select
              allowClear
              placeholder="Seller"
              style={{ width: isMobile ? '100%' : 150 }}
              value={ownerUserId}
              onChange={(v) => {
                setOwnerUserId(v);
                setShopId(undefined);
                setPageCurrent(1);
              }}
              options={sellers.map((s) => ({
                value: s.id,
                label: s.fullName || s.userName,
              }))}
            />
          ) : null}
          <Select
            allowClear
            placeholder="Platform"
            style={{ width: isMobile ? '100%' : 130 }}
            value={platformCode}
            onChange={(v) => {
              setPlatformCode(v);
              setShopId(undefined);
              setPageCurrent(1);
            }}
            options={platforms.map((p) => ({
              value: p.code,
              label: p.name || p.code,
            }))}
          />
          <Select
            allowClear
            placeholder="Shop"
            style={{ width: isMobile ? '100%' : 160 }}
            value={shopId}
            onChange={(v) => {
              setShopId(v);
              setPageCurrent(1);
            }}
            options={shopOptions}
          />
          <AdminDateRangeFilter
            variant="grid"
            allowClear
            value={dateRange}
            presetOptions={DATE_PRESETS}
            presetValue={datePreset}
            presetSpanClassName="admin-filters-span admin-filters-end"
            onChange={(next) => {
              setDateRange(next);
              setDatePreset(matchPreset(next));
              setPageCurrent(1);
            }}
            onPresetChange={(preset) => {
              const next = preset as DatePreset;
              setDatePreset(next);
              setDateRange(next ? rangeForPreset(next) : null);
              setPageCurrent(1);
            }}
          />
        </div>
      </div>

      {pageType === 'published' && (
        <Tabs
          className="admin-status-tabs"
          size="small"
          activeKey={siteStatus}
          onChange={(key) => {
            setSiteStatus(key);
          }}
          items={SITE_STATUS_TABS.map((tab) => ({
            key: tab.key,
            label: (
              <span>
                {tab.label}{' '}
                <Badge
                  count={counts?.[tab.countKey] ?? 0}
                  overflowCount={99999}
                  style={{ marginLeft: 4 }}
                />
              </span>
            ),
          }))}
          style={{ marginBottom: 8 }}
        />
      )}

      {isError ? (
        <Empty description={(error as Error)?.message || 'Không tải được listing'} />
      ) : (
        <>
          <div className="admin-list-scroll" style={{ overflowX: isMobile ? 'visible' : 'auto' }}>
            {!isMobile ? (
              <div
                className="listing-list-header"
                style={{
                  gridTemplateColumns: COL_TEMPLATE,
                  minWidth: 900,
                }}
              >
                {COL_HEADERS.map((label, i) => (
                  <div key={label} className={i === 0 ? 'listing-list-header-product' : undefined}>
                    {i === 0 && canManage ? (
                      <Checkbox
                        checked={allPageSelected}
                        indeterminate={somePageSelected && !allPageSelected}
                        disabled={pageIds.length === 0}
                        onChange={(e) => handleSelectAllPage(e.target.checked)}
                        aria-label="Chọn tất cả trang này"
                      />
                    ) : null}
                    {label}
                  </div>
                ))}
              </div>
            ) : canManage && pageIds.length > 0 ? (
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  marginBottom: 8,
                  padding: '0 2px',
                }}
              >
                <Checkbox
                  checked={allPageSelected}
                  indeterminate={somePageSelected && !allPageSelected}
                  onChange={(e) => handleSelectAllPage(e.target.checked)}
                  aria-label="Chọn tất cả trang này"
                >
                  Chọn trang này
                </Checkbox>
                {selectedCount > 0 ? (
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    {selectedCount} đã chọn
                  </Text>
                ) : null}
              </div>
            ) : null}

            <PageContentSpin spinning={busy} tip="Đang tải listing…">
              <div
                className="admin-list-body"
                style={{ minWidth: isMobile ? 0 : 900, minHeight: 240 }}
              >
                {items.length === 0 && !busy ? (
                  <Empty
                    description="Chưa có listing — nhấn Sync để kéo từ 4Seller"
                    style={{ margin: '48px 0' }}
                  />
                ) : (
                  items.map((listing, i) => (
                    <ListingCard
                      key={listing.id}
                      listing={listing}
                      index={(pageCurrent - 1) * pageSize + i + 1}
                      platforms={platforms}
                      onEdit={handleEdit}
                      onPrefetchEdit={handlePrefetchEdit}
                      canManage={canManage}
                      selected={selectedIds.has(listing.id)}
                      onSelectChange={canManage ? handleSelectChange : undefined}
                      onDelete={handleDeleteOne}
                      onActivate={handleActivate}
                      onDeactivate={handleDeactivate}
                      onDuplicate={handleDuplicate}
                      onSync={handleSyncOne}
                      activating={
                        activateMutation.isPending && activateMutation.variables === listing.id
                      }
                      deleting={deleteMutation.isPending && deleteMutation.variables === listing.id}
                      deactivating={
                        deactivateMutation.isPending && deactivateMutation.variables === listing.id
                      }
                      duplicating={
                        duplicateMutation.isPending && duplicateMutation.variables === listing.id
                      }
                      syncing={syncOneMutation.isPending && syncOneMutation.variables === listing.id}
                      onSaveSourceUrl={canManage ? handleSaveSourceUrl : undefined}
                      savingSourceUrl={
                        updateSourceUrlMutation.isPending &&
                        updateSourceUrlMutation.variables?.id === listing.id
                      }
                      variantsOpen={variantOpenIds.has(listing.id)}
                      onToggleVariants={toggleVariants}
                      isMobile={isMobile}
                      ratingSuccessMin={ratingSuccessMin}
                      ratingWarningMin={ratingWarningMin}
                      showOwnerName={config.showOwnerName}
                      onOpenAutoPublishLog={
                        canOpenAutoPublishLog
                          ? (listing) => setAutoPublishListing(listing)
                          : undefined
                      }
                      onRetryAutoPublish={canRetryAutoPublish ? handleRetryAutoPublish : undefined}
                      retryingAutoPublishId={retryingAutoPublishId}
                      canRetryAutoPublish={canRetryAutoPublish}
                    />
                  ))
                )}
              </div>
            </PageContentSpin>
          </div>

          {needsPagination(total, pageSize) ? (
            <div
              style={{
                display: 'flex',
                justifyContent: isMobile ? 'center' : 'flex-end',
                marginTop: 16,
              }}
            >
              <Pagination
                size="small"
                current={pageCurrent}
                pageSize={pageSize}
                total={total}
                showSizeChanger={!isMobile}
                simple={isMobile}
                pageSizeOptions={PAGE_SIZE_OPTIONS.map(String)}
                showTotal={isMobile ? undefined : (t) => `${t} listing`}
                onChange={(page, size) => {
                  setPageCurrent(page);
                  setPageSize(size);
                }}
              />
            </div>
          ) : null}
        </>
      )}

      <BindCategoryModal
        listing={bindCategoryListing}
        open={!!bindCategoryListing}
        onClose={() => setBindCategoryListing(null)}
        onSuccess={(listingId) => {
          void openEdit(listingId, { newTab: true });
        }}
      />

      <AutoPublishTimelineModal
        open={!!autoPublishListing}
        listingId={autoPublishListing?.id ?? null}
        listingTitle={autoPublishListing?.productName}
        autoPublishSkipped={autoPublishListing?.autoPublishSkipped}
        autoPublishSkipReason={autoPublishListing?.autoPublishSkipReason}
        runs={autoPublishRunsQuery?.data}
        loading={!!autoPublishRunsQuery?.isLoading && !autoPublishRunsQuery?.data}
        refreshing={!!autoPublishRunsQuery?.isFetching && !!autoPublishRunsQuery?.data}
        retrying={retryAutoPublishMutation?.isPending}
        canRetry={canManage && !!retryAutoPublishMutation}
        onClose={handleCloseAutoPublishTimeline}
        onRetry={handleRetryFromAutoPublishTimeline}
      />
    </div>
  );
};
