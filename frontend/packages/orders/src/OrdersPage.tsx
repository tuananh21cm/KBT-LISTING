import { Fragment, useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
  Badge,
  Button,
  Checkbox,
  Drawer,
  Dropdown,
  Empty,
  Input,
  message,
  Modal,
  Pagination,
  Popconfirm,
  Popover,
  Select,
  Space,
  Tag,
  Tabs,
  Tooltip,
  Typography,
} from 'antd';
import {
  CheckCircleFilled,
  ClockCircleOutlined,
  CloudUploadOutlined,
  CopyOutlined,
  DeleteOutlined,
  EyeOutlined,
  CarOutlined,
  TagsOutlined,
  MenuOutlined,
  ReloadOutlined,
  SendOutlined,
  ShopOutlined,
  SyncOutlined,
} from '@ant-design/icons';
import type { UspsScanPushResult, UspsScanSkippedOrder } from '@drop/types/order';
import { type Dayjs } from 'dayjs';
import {
  formatDateTimeVn,
  rangeForPresetVn,
  toUtcIsoRange,
} from '@drop/utils/datetime';
import type {
  OrderItem,
  OrderListItem,
  OrderStatusCounts,
  OrderSupplierLink,
} from '@drop/types/order';
import { PageContentSpin, PageContentSpinCenter } from '@drop/ui/PageSpin';
import { PageHeader } from '@drop/ui/PageHeader';
import { AdminDateRangeFilter } from '@drop/ui';
import { needsPagination } from '@drop/utils/pagination';
import { useIsMobile } from '@drop/ui/useIsMobile';
import {
  getListingRatingTier,
  LISTING_RATING_DEFAULTS,
  LISTING_RATING_SETTING_KEYS,
  parseListingRatingThreshold,
  type ListingRatingTier,
} from '@drop/ui/listingRatingSettings';
import { ListingStars } from '@drop/ui/ListingStars';
import {
  SupplierPurchaseSection,
  isSupplierCostDetailComplete,
  settledTrueSpend,
  type SupplierPurchaseLink,
} from '@drop/ui/SupplierPurchaseCost';
import '@drop/ui/supplierPurchaseCost.css';
import { MarkShipModal } from './MarkShipModal';
import { findExactCarrierMatch } from './carrierMatch';
import { UpdateMessageModal } from './UpdateMessageModal';
import { UpdateNotesModal } from './UpdateNotesModal';
import { useOrdersDeps } from './OrdersDepsContext';
import type { OrdersPortalConfig } from './portalConfig';
import './orders.css';

const { Text } = Typography;

type DatePreset = '' | 'day' | 'week' | 'month' | '30d';

const DATE_PRESETS: { value: DatePreset; label: string }[] = [
  { value: '', label: 'Tất cả' },
  { value: 'day', label: 'Ngày' },
  { value: 'week', label: 'Tuần' },
  { value: 'month', label: 'Tháng' },
  { value: '30d', label: '30Days' },
];

const rangeForPreset = (preset: Exclude<DatePreset, ''>): [Dayjs, Dayjs] => rangeForPresetVn(preset);

const matchPreset = (range: [Dayjs, Dayjs] | null): DatePreset => {
  if (!range) return '';
  const [from, to] = range;
  for (const preset of ['day', 'week', 'month', '30d'] as const) {
    const [pFrom, pTo] = rangeForPreset(preset);
    if (from.isSame(pFrom, 'minute') && to.isSame(pTo, 'minute')) return preset;
  }
  return '';
};

const copyOrderId = async (text: string) => {
  await navigator.clipboard.writeText(text);
  message.success('Đã copy');
};

const STATUS_TABS: { key: string; label: string; shortLabel: string; countKey: keyof OrderStatusCounts }[] = [
  { key: '', label: 'Tất cả', shortLabel: 'Tất cả', countKey: 'all' },
  { key: 'PENDING_PROCESS', label: 'Chờ xử lý', shortLabel: 'Chờ', countKey: 'pendingProcess' },
  { key: 'PURCHASED', label: 'Đã mua', shortLabel: 'Mua', countKey: 'purchased' },
  { key: 'AWAITING_COLLECTION_GROUP', label: 'Chờ lấy', shortLabel: 'Lấy', countKey: 'awaitingCollection' },
  { key: 'IN_TRANSIT', label: 'Đang giao', shortLabel: 'Giao', countKey: 'inTransit' },
  { key: 'DELIVERED', label: 'Đã giao', shortLabel: 'Xong', countKey: 'delivered' },
  { key: 'CANCELLED', label: 'Đã hủy', shortLabel: 'Hủy', countKey: 'canceled' },
  { key: 'REFUND', label: 'Refund', shortLabel: 'Refund', countKey: 'refund' },
];

/** Tabs nội bộ queue USPS hoặc Thank Card. */
const USPS_HANDLING_TABS: {
  key: string;
  label: string;
  shortLabel: string;
  countKey: keyof OrderStatusCounts;
}[] = [
  { key: 'USPS_UNPROCESSED', label: 'Chưa xử lý', shortLabel: 'Chưa', countKey: 'uspsUnprocessed' },
  { key: 'USPS_PROCESSED', label: 'Đã xử lý', shortLabel: 'Xong', countKey: 'uspsProcessed' },
];

const THANK_CARD_HANDLING_TABS: {
  key: string;
  label: string;
  shortLabel: string;
  countKey: keyof OrderStatusCounts;
}[] = [
  { key: 'THANK_CARD_PENDING', label: 'Chưa xử lý', shortLabel: 'Chưa', countKey: 'thankCardPending' },
  { key: 'THANK_CARD_PROCESSED', label: 'Đã xử lý', shortLabel: 'Xong', countKey: 'thankCardProcessed' },
];

/** Một hàng tab trên `/orders/refund` — chỉ một tab active. */
const REFUND_PAGE_TABS: {
  key: string;
  label: string;
  shortLabel: string;
}[] = [
  { key: 'REFUND_QUEUE_ALL', label: 'Tất cả', shortLabel: 'Tất cả' },
  { key: 'REFUND_QUEUE_UNPROCESSED', label: 'Chờ xử lý', shortLabel: 'Chờ' },
  { key: 'REFUND_QUEUE_PROCESSED', label: 'Đã xử lý', shortLabel: 'Xong' },
  { key: 'REFUND', label: 'Đơn hoàn', shortLabel: 'Hoàn' },
  { key: 'CANCELLED', label: 'Đơn hủy', shortLabel: 'Hủy' },
];

const refundPageTabCount = (
  key: string,
  counts: OrderStatusCounts | undefined,
): number => {
  switch (key) {
    case 'REFUND_QUEUE_ALL':
      return (counts?.refund ?? 0) + (counts?.canceled ?? 0);
    case 'REFUND_QUEUE_UNPROCESSED':
      return (counts?.refundUnprocessed ?? 0) + (counts?.cancelUnprocessed ?? 0);
    case 'REFUND_QUEUE_PROCESSED':
      return (counts?.refundProcessed ?? 0) + (counts?.cancelProcessed ?? 0);
    case 'REFUND':
      return counts?.refund ?? 0;
    case 'CANCELLED':
      return counts?.canceled ?? 0;
    default:
      return 0;
  }
};

/** Tabs nội bộ sider Chờ mua hàng (`/orders/pending`). */
const PENDING_QUEUE_TABS: {
  key: string;
  label: string;
  shortLabel: string;
  countKey: keyof OrderStatusCounts;
}[] = [
  { key: '', label: 'Tất cả', shortLabel: 'Tất cả', countKey: 'pendingProcess' },
  { key: 'new', label: 'Đơn mới', shortLabel: 'Mới', countKey: 'pendingNew' },
  { key: '48h', label: '48 Giờ', shortLabel: '48h', countKey: 'pending48h' },
  { key: 'over_48h', label: 'Quá 48h', shortLabel: '>48h', countKey: 'pendingOver48h' },
  { key: 'out_stock', label: 'Hết hàng', shortLabel: 'Hết', countKey: 'pendingOutStock' },
];

const isOrdersPendingPath = (pathname: string) =>
  pathname === '/orders/pending' || pathname.startsWith('/orders/pending/');

const isOrdersUspsPath = (pathname: string) =>
  pathname === '/orders/usps' || pathname.startsWith('/orders/usps/');

const isOrdersThankCardPath = (pathname: string) =>
  pathname === '/orders/thank-card' || pathname.startsWith('/orders/thank-card/');

const isOrdersRefundPath = (pathname: string) =>
  pathname === '/orders/refund' || pathname.startsWith('/orders/refund/');

const isOrdersHandlingPath = (pathname: string) =>
  isOrdersUspsPath(pathname) || isOrdersThankCardPath(pathname) || isOrdersRefundPath(pathname);

/** Normalize deep-link / legacy status query values to workflow tab keys. */
const normalizePlatformOrderStatus = (raw: string): string => {
  const trimmed = raw.trim();
  const legacy: Record<string, string> = {
    on_hold: 'PENDING_PROCESS',
    to_ship: 'PENDING_PROCESS',
    ON_HOLD: 'PENDING_PROCESS',
    AWAITING_SHIPMENT: 'PENDING_PROCESS',
    AWAITING_COLLECTION: 'AWAITING_COLLECTION_GROUP',
    IN_TRANSIT_GROUP: 'IN_TRANSIT',
    shipped: 'DELIVERED',
    canceled: 'CANCELLED',
    cancelled: 'CANCELLED',
    USPS_THANK_CARD_PENDING: 'USPS_UNPROCESSED',
  };
  if (legacy[trimmed]) return legacy[trimmed];
  const lower = trimmed.toLowerCase();
  if (legacy[lower]) return legacy[lower];
  const upper = trimmed.toUpperCase();
  if (legacy[upper]) return legacy[upper];
  return upper === 'CANCELED' ? 'CANCELLED' : upper;
};

/** Sider shortcuts: /orders/pending|usps|thank-card → tab; /orders → query or Tất cả. */
const platformOrderStatusFromLocation = (
  pathname: string,
  searchParams: URLSearchParams,
): string => {
  if (isOrdersPendingPath(pathname)) {
    return 'PENDING_PROCESS';
  }
  const handling = (searchParams.get('handling') || '').trim().toLowerCase();
  if (isOrdersUspsPath(pathname)) {
    return handling === 'processed' ? 'USPS_PROCESSED' : 'USPS_UNPROCESSED';
  }
  if (isOrdersThankCardPath(pathname)) {
    return handling === 'processed' ? 'THANK_CARD_PROCESSED' : 'THANK_CARD_PENDING';
  }
  if (isOrdersRefundPath(pathname)) {
    // Một chiều tab: kind XOR handling (không kết hợp → tránh 2 tab active).
    const kind = (searchParams.get('kind') || '').trim().toLowerCase();
    if (kind === 'cancel') return 'CANCELLED';
    if (kind === 'refund') return 'REFUND';
    if (handling === 'processed') return 'REFUND_QUEUE_PROCESSED';
    if (handling === 'unprocessed' || handling === 'pending') {
      return 'REFUND_QUEUE_UNPROCESSED';
    }
    return 'REFUND_QUEUE_ALL';
  }
  const raw = (
    searchParams.get('platformOrderStatus') ||
    searchParams.get('orderStatus') ||
    searchParams.get('statusTab') ||
    ''
  ).trim();
  return raw ? normalizePlatformOrderStatus(raw) : '';
};

/** Sub-filter query on `/orders/pending` (`?pending=`). */
const pendingFilterFromLocation = (pathname: string, searchParams: URLSearchParams): string => {
  if (!isOrdersPendingPath(pathname)) return '';
  const raw = (searchParams.get('pending') || '').trim().toLowerCase();
  if (raw === '24h') return 'new'; // legacy deep-link → Đơn mới
  if (raw === 'new' || raw === 'out_stock' || raw === '48h' || raw === 'over_48h') {
    return raw;
  }
  return '';
};

/** Path for handling-queue tab switches only (USPS / Thank Card / Refund). */
const ordersPathForStatus = (status: string): string => {
  if (status === 'USPS_UNPROCESSED' || status === 'USPS_PROCESSED') return '/orders/usps';
  if (status === 'THANK_CARD_PENDING' || status === 'THANK_CARD_PROCESSED') {
    return '/orders/thank-card';
  }
  if (
    status === 'REFUND' ||
    status === 'CANCELLED' ||
    status === 'REFUND_QUEUE_ALL' ||
    status === 'REFUND_UNPROCESSED' ||
    status === 'REFUND_PROCESSED' ||
    status === 'CANCEL_UNPROCESSED' ||
    status === 'CANCEL_PROCESSED' ||
    status === 'REFUND_QUEUE_UNPROCESSED' ||
    status === 'REFUND_QUEUE_PROCESSED'
  ) {
    return '/orders/refund';
  }
  if (status === 'USPS_THANK_CARD_PENDING' || status === 'USPS_THANK_CARD_PROCESSED') {
    return '/orders/usps';
  }
  return '/orders';
};

/**
 * Map admin workflow tab → 4Seller remote orderStatus buckets for sync API.
 *
 * Chỉ liệt kê tab mà một bucket đơn lẻ thật sự kéo ít dữ liệu hơn bucket `all`. 4Seller chỉ lọc
 * đúng với `to_ship` và `shipped`; `on_hold` và `canceled` trả về **toàn bộ** tập đơn bất kể tên
 * bucket, nên map chúng vào đây chỉ tốn thêm round-trip mà không lọc được gì. Tab không có mặt
 * trong map gửi `orderStatuses` rỗng → backend dùng một truy vấn `all`, vừa ít request hơn vừa
 * phủ rộng hơn. Đo lại bằng `GET /dev/diagnostics/order-page-status-coverage`.
 */
const PLATFORM_TAB_TO_FOURSELLER_SYNC: Record<string, string[]> = {
  PURCHASED: ['to_ship'],
  AWAITING_COLLECTION_GROUP: ['shipped'],
  IN_TRANSIT: ['shipped'],
  IN_TRANSIT_GROUP: ['shipped'],
  DELIVERED: ['shipped'],
  // Legacy raw keys (bookmarks / deep-links)
  AWAITING_SHIPMENT: ['to_ship'],
  AWAITING_COLLECTION: ['shipped'],
};

const STATUS_LABEL: Record<string, string> = {
  PENDING_PROCESS: 'Chờ xử lý',
  PURCHASED: 'Đã mua',
  AWAITING_COLLECTION_GROUP: 'Chờ lấy',
  IN_TRANSIT_GROUP: 'Đang giao',
  ON_HOLD: 'Tạm giữ',
  AWAITING_SHIPMENT: 'Chờ giao',
  AWAITING_COLLECTION: 'Chờ lấy',
  IN_TRANSIT: 'Đang giao',
  DELIVERED: 'Đã giao',
  CANCELLED: 'Đã hủy',
  CANCELED: 'Đã hủy',
  REFUND: 'Refund',
  REFUNDED: 'Refund',
  on_hold: 'Tạm giữ',
  awaiting_shipment: 'Chờ giao',
  awaiting_collection: 'Chờ lấy',
  in_transit: 'Đang giao',
  delivered: 'Đã giao',
  cancelled: 'Đã hủy',
  canceled: 'Đã hủy',
  refund: 'Refund',
  unknown: 'Không rõ',
};

const PAGE_SIZE_OPTIONS = ['20', '50', '100', '200'];

const COL_TEMPLATE =
  'minmax(260px, 2.4fr) minmax(130px, 0.95fr) minmax(120px, 0.9fr) minmax(140px, 1fr) minmax(120px, 0.9fr)';

const COL_HEADERS = ['Sản phẩm', 'Khách hàng', 'Thời gian', 'Vận chuyển', 'Trạng thái'];

const formatAmount = (amount: number) =>
  Number(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const formatMoney = (amount: number, currency: string) =>
  `${formatAmount(amount)} ${currency}`;

/** Pass-through DB money — thiếu/null hiển thị 0, không suy/tính từ field khác. */
const formatDbMoney = (amount: number | null | undefined, currency = 'USD') =>
  formatMoney(amount ?? 0, currency);

/** Giảm giá: prefix `-` (DB lưu dương). */
const formatDbDiscountMoney = (amount: number | null | undefined, currency = 'USD') =>
  `-${formatAmount(Math.abs(amount ?? 0))} ${currency}`;

/** System Rev = seller net when enriched; else totalPaid if ≠ 0, else amount (match backend OrderRevenue). */
const resolveOrderRevenue = (order: {
  sellerNetAmount?: number | null;
  totalPaidAmount?: number | null;
  amount?: number | null;
}) => {
  if (order.sellerNetAmount != null) return order.sellerNetAmount;
  const paid = order.totalPaidAmount ?? 0;
  return paid !== 0 ? paid : (order.amount ?? 0);
};

const MoneyAmount = ({
  amount,
  currency,
  style,
}: {
  amount: number;
  currency?: string | null;
  style?: CSSProperties;
}) => {
  const code = (currency || 'USD').trim().toUpperCase() || 'USD';
  return (
    <Tooltip title={code}>
      <Text strong style={{ fontSize: 12, whiteSpace: 'nowrap', color: 'var(--order-money)', ...style }}>
        {`$${formatAmount(amount)}`}
      </Text>
    </Tooltip>
  );
};

const formatDateTime = (value?: string | null) => formatDateTimeVn(value);

/** Supplier order URL đã lưu, hoặc dựng từ mã đơn (fallback SHEIN us). */
const resolveSupplierOrderUrl = (order: {
  supplierOrderUrl?: string | null;
  supplierOrderNo?: string | null;
}): string | null => {
  const stored = order.supplierOrderUrl?.trim();
  if (stored) return stored;
  const orderNo = order.supplierOrderNo?.trim();
  if (!orderNo) return null;
  // Chỉ fallback URL SHEIN khi chưa lưu link (đơn Temu nên lưu SupplierOrderUrl khi sync).
  return `https://us.shein.com/user/orders/detail/${orderNo}`;
};

/** URL trang chi tiết đơn NCC — ưu tiên field đơn, rồi supplierLinks. */
const resolveOrderSupplierCopyUrl = (order: {
  supplierOrderNo?: string | null;
  supplierOrderUrl?: string | null;
  supplierLinks?: Array<{
    supplierOrderNo?: string | null;
    supplierOrderUrl?: string | null;
  }> | null;
}): string | null => {
  const primary = resolveSupplierOrderUrl(order);
  if (primary) return primary;
  for (const link of order.supplierLinks ?? []) {
    const url = resolveSupplierOrderUrl(link);
    if (url) return url;
  }
  return null;
};

const copyOrderSupplierUrl = async (order: {
  supplierOrderNo?: string | null;
  supplierOrderUrl?: string | null;
  supplierLinks?: Array<{
    supplierOrderNo?: string | null;
    supplierOrderUrl?: string | null;
  }> | null;
}) => {
  const url = resolveOrderSupplierCopyUrl(order);
  if (!url) {
    message.warning('Chưa có mã đơn NCC');
    return;
  }
  await navigator.clipboard.writeText(url);
  message.success('Đã copy link SHEIN');
};

const formatWeight = (order: OrderListItem) => {
  if (order.packageWeight == null) return '—';
  const unit = (order.packageWeightUnit || 'oz').toLowerCase();
  if (unit === 'oz') {
    const totalOz = order.packageWeight;
    const lb = Math.floor(totalOz / 16);
    const oz = Math.round((totalOz % 16) * 10) / 10;
    return `${lb} lb ${oz} oz`;
  }
  return `${order.packageWeight} ${order.packageWeightUnit}`;
};

const formatDimensions = (order: OrderListItem) => {
  if (order.packageLength == null && order.packageWidth == null && order.packageHeight == null) {
    return '—';
  }
  const unit = order.packageDimensionUnit === 'in' ? 'in' : order.packageDimensionUnit || '';
  return `${order.packageLength ?? 0}×${order.packageWidth ?? 0}×${order.packageHeight ?? 0} ${unit}`.trim();
};

const statusColor = (status?: string | null) => {
  const key = (status ?? '').trim().toUpperCase();
  switch (key) {
    case 'ON_HOLD':
      return '#8c8c8c';
    case 'AWAITING_SHIPMENT':
    case 'PENDING_PROCESS':
      return '#1677ff';
    case 'AWAITING_COLLECTION':
    case 'AWAITING_COLLECTION_GROUP':
      return '#13c2c2';
    case 'IN_TRANSIT':
    case 'IN_TRANSIT_GROUP':
    case 'PURCHASED':
      return '#faad14';
    case 'DELIVERED':
      return '#52c41a';
    case 'CANCELLED':
    case 'CANCELED':
      return '#ff4d4f';
    case 'REFUND':
    case 'REFUNDED':
      return '#c41d7f';
    default:
      return '#8c8c8c';
  }
};

const formatShippingMethod = (value?: string | null) => {
  if (!value) return '—';
  const lower = value.toLowerCase();
  if (lower.includes('seller shipping')) return 'Seller Shipping';
  if (lower.includes('tiktok shipping') || lower.includes('platform shipping')) return 'Tiktok Shipping';
  return value;
};

type ShippingMethodKind = 'seller' | 'tiktok';

type ShippingMethodMeta = {
  kind: ShippingMethodKind;
  label: string;
  shortLabel: string;
  color: string;
  bg: string;
  Icon: typeof ShopOutlined;
};

const SHIPPING_METHOD_META: Record<ShippingMethodKind, ShippingMethodMeta> = {
  seller: {
    kind: 'seller',
    label: 'Seller Shipping',
    shortLabel: 'Seller',
    color: 'var(--order-ship-seller-fg)',
    bg: 'var(--order-ship-seller-bg)',
    Icon: ShopOutlined,
  },
  tiktok: {
    kind: 'tiktok',
    label: 'Tiktok Shipping',
    shortLabel: 'TikTok',
    color: 'var(--order-ship-tiktok-fg)',
    bg: 'var(--order-ship-tiktok-bg)',
    Icon: SendOutlined,
  },
};

/** Prefer shippingMethod (fulfil type) over shippingService (carrier name). */
const resolveShippingMethodKind = (
  method?: string | null,
  service?: string | null,
): ShippingMethodMeta | null => {
  for (const value of [method, service]) {
    if (!value) continue;
    const lower = value.toLowerCase();
    if (lower.includes('seller shipping')) return SHIPPING_METHOD_META.seller;
    if (lower.includes('tiktok shipping') || lower.includes('platform shipping')) {
      return SHIPPING_METHOD_META.tiktok;
    }
  }
  return null;
};

/** Mirror backend TikTokSellerNetCalculator.IsTikTokPlatformShipping (shippingMethod only). */
const isTikTokPlatformShipping = (order: Pick<OrderListItem, 'shippingMethod'>): boolean => {
  const lower = (order.shippingMethod || '').toLowerCase();
  return lower.includes('tiktok shipping') || lower.includes('platform shipping');
};

/** Warehouse-style SHEIN codes (C…/N…) — not last-mile carrier. */
const isWarehouseTrackingNo = (trackingNo: string, shippingService?: string | null) =>
  /^[CN]\d{10,}$/i.test(trackingNo.trim()) ||
  (shippingService || '').toLowerCase().includes('warehouse');

type OrderTrackingExtra = {
  trackingNo: string;
  /** carrier | warehouse | published */
  kind: 'carrier' | 'warehouse' | 'published';
  carrier?: string | null;
};

type OrderTrackingDisplay = {
  trackingNo: string;
  /** carrier | warehouse | published */
  kind: 'carrier' | 'warehouse' | 'published';
  carrier?: string | null;
  /** Other distinct tracking numbers on supplier links. */
  extraCount: number;
  extras: OrderTrackingExtra[];
};

const toTrackingKind = (
  raw: string,
  trackingNo: string,
  shippingService?: string | null,
): OrderTrackingDisplay['kind'] => {
  if (raw === 'carrier' || raw === 'warehouse') return raw;
  return isWarehouseTrackingNo(trackingNo, shippingService) ? 'warehouse' : 'published';
};

/** Kind label for tip — omit "Carrier" (carrier name already says enough). */
const trackingKindLabel = (kind: OrderTrackingDisplay['kind']) =>
  kind === 'warehouse' ? 'Kho SHEIN' : kind === 'published' ? 'Đã lên sàn' : null;

const formatTrackingTipLine = (t: {
  trackingNo: string;
  kind: OrderTrackingDisplay['kind'];
  carrier?: string | null;
}) =>
  [t.trackingNo, t.carrier?.trim() || null, trackingKindLabel(t.kind)]
    .filter(Boolean)
    .join(' · ');

/**
 * Best tracking to show on the list card: published Order.TrackingNo first,
 * else carrier on supplier links, else any link tracking (warehouse).
 */
const resolveOrderTracking = (order: OrderListItem): OrderTrackingDisplay | null => {
  const linkTracks = (order.supplierLinks ?? [])
    .map((l) => {
      const trackingNo = l.trackingNo?.trim() || '';
      const rawKind = (l.trackingKind || '').toLowerCase();
      return {
        trackingNo,
        kind: toTrackingKind(rawKind, trackingNo, order.shippingService),
        carrier: l.carrier,
      };
    })
    .filter((l) => !!l.trackingNo);

  const asExtras = (primaryNo: string): OrderTrackingExtra[] =>
    linkTracks
      .filter((l) => l.trackingNo.toLowerCase() !== primaryNo.toLowerCase())
      .map((l) => ({
        trackingNo: l.trackingNo,
        kind: l.kind,
        carrier: l.carrier,
      }));

  const published = order.trackingNo?.trim();
  if (published) {
    const match = linkTracks.find(
      (l) => l.trackingNo.toLowerCase() === published.toLowerCase(),
    );
    const kind: OrderTrackingDisplay['kind'] =
      match?.kind ??
      toTrackingKind('', published, order.shippingService);
    const extras = asExtras(published);
    return {
      trackingNo: published,
      kind,
      carrier: match?.carrier,
      extraCount: extras.length,
      extras,
    };
  }

  const carrier = linkTracks.find((l) => l.kind === 'carrier');
  if (carrier) {
    const extras = asExtras(carrier.trackingNo);
    return {
      trackingNo: carrier.trackingNo,
      kind: 'carrier',
      carrier: carrier.carrier,
      extraCount: extras.length,
      extras,
    };
  }

  if (linkTracks[0]) {
    const primary = linkTracks[0];
    const extras = asExtras(primary.trackingNo);
    return {
      trackingNo: primary.trackingNo,
      kind: primary.kind,
      carrier: primary.carrier,
      extraCount: extras.length,
      extras,
    };
  }

  return null;
};

/**
 * Mã vận chuyển của các đơn đang chọn — theo đúng thứ tự đơn hiển thị trên list (không theo
 * thứ tự tick). Chỉ lấy đơn đang hiện ở trang hiện tại; đơn chưa có mã bị bỏ qua.
 */
const selectedTrackingNos = (
  visibleOrders: OrderListItem[],
  selectedIds: ReadonlySet<string>,
): string[] =>
  visibleOrders
    .filter((order) => selectedIds.has(order.id))
    .map((order) => resolveOrderTracking(order)?.trackingNo?.trim())
    .filter((trackingNo): trackingNo is string => !!trackingNo);

/** Copy mỗi mã một dòng. */
const copyTrackingNos = async (codes: string[]) => {
  if (codes.length === 0) {
    message.warning('Không có mã vận chuyển');
    return;
  }
  try {
    await navigator.clipboard.writeText(codes.join('\n'));
    message.success(`Đã copy ${codes.length} mã vận chuyển`);
  } catch {
    message.error('Không copy được');
  }
};

const USPS_SCAN_SKIP_LABELS: Record<string, string> = {
  not_found: 'Không tìm thấy đơn',
  not_owned: 'Không thuộc seller',
  no_tracking: 'Chưa có mã VC',
  not_usps: 'Không phải USPS',
  already_pushed: 'Đã đẩy scan',
  duplicate_tracking: 'Trùng mã trong lần chọn',
  pending_batch: 'Đang chờ xác nhận batch trước',
  already_in_transit: 'Đã In-transit / Delivered',
  terminal_commerce: 'Đã hoàn/hủy',
};

const formatUspsScanSkipLine = (row: UspsScanSkippedOrder) => {
  const label = USPS_SCAN_SKIP_LABELS[row.reason] ?? row.reason;
  const id = row.platformOrderId || row.orderId.slice(0, 8);
  const track = row.trackingNo ? ` · ${row.trackingNo}` : '';
  return `${id}${track}: ${label}`;
};

const showUspsScanPushResult = (result: UspsScanPushResult, apiMessage?: string) => {
  const skippedLines =
    result.skipped.length > 0
      ? result.skipped.slice(0, 12).map(formatUspsScanSkipLine)
      : [];
  const skippedExtra =
    result.skipped.length > 12 ? `\n… và ${result.skipped.length - 12} đơn khác` : '';

  if (result.status === 'SendUnknown') {
    Modal.error({
      title: 'Gửi batch không rõ kết quả',
      centered: true,
      content: (
        <div style={{ fontSize: 12 }}>
          <p>{result.error || apiMessage}</p>
          <p>
            Batch #{result.batchId ?? '—'} — <strong>không đẩy lại</strong> các mã này cho đến khi
            admin kiểm tra trên PrintPackShip (tránh trừ tiền hai lần).
          </p>
        </div>
      ),
    });
    return;
  }

  if (result.status === 'SendFailed') {
    Modal.error({
      title: 'Gửi batch thất bại',
      centered: true,
      content: (
        <div style={{ fontSize: 12 }}>
          <p>{result.error || apiMessage || 'PrintPackShip từ chối gửi batch.'}</p>
          {result.batchId != null ? <p>Batch #{result.batchId}</p> : null}
        </div>
      ),
    });
    return;
  }

  if (result.status === 'Sent') {
    const moneyBits = [
      result.pricePerLabel != null ? `$${result.pricePerLabel}/mã` : null,
      result.totalCharged != null ? `tổng ~$${result.totalCharged}` : null,
      result.balanceCurrent != null ? `số dư $${result.balanceCurrent}` : null,
    ].filter(Boolean);
    const body = (
      <div style={{ fontSize: 12 }}>
        <p>
          Batch #{result.batchId} — đã đánh dấu {result.sentCount} đơn.
          {moneyBits.length > 0 ? ` (${moneyBits.join(' · ')})` : ''}
        </p>
        {result.pagesFailed > 0 ? (
          <p style={{ color: 'var(--app-warning, #d48806)' }}>
            {result.pagesFailed} trang scan lỗi — đối chiếu trên PrintPackShip.
          </p>
        ) : null}
        {skippedLines.length > 0 ? (
          <pre style={{ margin: 0, whiteSpace: 'pre-wrap', fontSize: 11 }}>
            Bỏ qua:{'\n'}
            {skippedLines.join('\n')}
            {skippedExtra}
          </pre>
        ) : null}
      </div>
    );
    if (result.pagesFailed > 0) {
      Modal.warning({ title: 'Đã đẩy track scan (có trang lỗi)', content: body, centered: true });
    } else {
      Modal.success({ title: 'Đã đẩy track scan', content: body, centered: true });
    }
    return;
  }

  if (result.sentCount === 0 && result.skipped.length > 0) {
    Modal.warning({
      title: 'Không có đơn hợp lệ để đẩy',
      centered: true,
      content: (
        <pre style={{ margin: 0, whiteSpace: 'pre-wrap', fontSize: 11 }}>
          {skippedLines.join('\n')}
          {skippedExtra}
        </pre>
      ),
    });
  }
};

/** Auto Mark Ship còn treo trong khi SHEIN đã có mã carrier — lỗi rõ, hoặc chờ quá lâu. */
type AutoMarkShipNotice = { text: string; tone: 'danger' | 'warning' };

/**
 * Chờ bao lâu mới coi `pending` là kẹt. Auto Mark Ship chạy trong lượt shein-sync (vài chục giây
 * một lượt), nên vài phút đầu là bình thường; ngưỡng này chỉ lọc ra những link mà hàng đợi sync
 * đã không chạm tới nữa.
 */
const AUTO_MARK_SHIP_STUCK_MS = 2 * 60 * 60 * 1000;

/**
 * Vì sao đơn đã có mã carrier SHEIN mà sàn vẫn chưa nhận mã.
 *
 * Lỗi rõ (`failed` / `blocked` / `unconfirmed`) hiện nguyên văn message của backend. Riêng
 * `pending` không có message nào cả — nó là hàng đợi bị hoãn theo ngân sách của lượt sync — nên
 * nếu chỉ hiện lỗi thì người xử lý thấy nút Đẩy track mà không hiểu tại sao tự động không chạy.
 */
const resolveAutoMarkShipNotice = (order: OrderListItem): AutoMarkShipNotice | null => {
  if (order.trackingNo?.trim()) return null;
  let stuckPending = false;
  for (const link of order.supplierLinks ?? []) {
    const kind = (link.trackingKind || '').toLowerCase();
    if (kind !== 'carrier' || !link.trackingNo?.trim()) continue;
    const status = (link.autoMarkShipStatus || '').toLowerCase();
    if (status === 'failed' || status === 'blocked' || status === 'unconfirmed') {
      const err = link.autoMarkShipError?.trim();
      if (err) return { text: err, tone: 'danger' };
      continue;
    }
    if (isAutoMarkShipStuckPending(link, order.inTransitAt)) stuckPending = true;
  }
  if (!stuckPending) return null;
  return {
    text: 'Auto Mark Ship chưa đẩy được mã lên sàn — bấm Đẩy track để xử lý ngay.',
    tone: 'warning',
  };
};

/**
 * Link đang `pending` mà mã carrier SHEIN đã cấp quá lâu, tức hàng đợi Auto Mark Ship không còn
 * chạm tới nó nữa. Mốc chờ tính từ lúc SHEIN đổi trạng thái chứ không phải lần thử gần nhất: link
 * bị hoãn theo ngân sách của lượt sync giữ `autoMarkShipAttemptedAt` rỗng nên không có lần thử nào
 * để đo.
 */
const isAutoMarkShipStuckPending = (
  link: OrderSupplierLink,
  fallbackSince?: string | null,
): boolean => {
  const kind = (link.trackingKind || '').toLowerCase();
  if (kind !== 'carrier' || !link.trackingNo?.trim()) return false;
  const status = (link.autoMarkShipStatus || '').toLowerCase();
  if (status && status !== 'pending') return false;
  const since = parseIsoMs(link.supplierStatusAt) ?? parseIsoMs(fallbackSince);
  return since !== null && Date.now() - since >= AUTO_MARK_SHIP_STUCK_MS;
};

/** Carrier supplier link preferred for manual push track (Mark Ship). */
const resolveCarrierPushLink = (order: OrderListItem): OrderSupplierLink | null => {
  const links = order.supplierLinks ?? [];
  const withCarrier = links.filter(
    (l) => (l.trackingKind || '').toLowerCase() === 'carrier' && !!l.trackingNo?.trim(),
  );
  if (withCarrier.length === 0) return null;
  const tracking = resolveOrderTracking(order);
  if (tracking?.kind === 'carrier' && tracking.trackingNo) {
    const match = withCarrier.find(
      (l) => (l.trackingNo || '').trim().toLowerCase() === tracking.trackingNo.toLowerCase(),
    );
    if (match) return match;
  }
  return withCarrier[0] ?? null;
};

/** Show push-track icon: manage + AWAITING_SHIPMENT + carrier track + not yet published. */
const canPushSupplierTrack = (order: OrderListItem, canManage: boolean): boolean => {
  if (!canManage) return false;
  if (isTikTokPlatformShipping(order)) return false;
  if ((order.platformOrderStatus || '').toUpperCase() !== 'AWAITING_SHIPMENT') return false;
  if (order.trackingNo?.trim()) return false;
  return !!resolveCarrierPushLink(order);
};

/** Sync track menu: tab Đã mua / Chờ lấy / Đang giao (hoặc đang lọc các tab đó) + có billno SHEIN. */
const orderHasSheinBillno = (order: OrderListItem): boolean =>
  !!order.supplierOrderNo?.trim() ||
  !!order.supplierLinks?.some((l) => !!l.supplierOrderNo?.trim());

const canSyncTrack = (
  order: OrderListItem,
  canManage: boolean,
  /** Tab đang lọc trên list — fallback khi `workflowTab` thiếu/lệch. */
  listTab?: string | null,
): boolean => {
  if (!canManage) return false;
  const tab = (order.workflowTab || '').toUpperCase();
  const filter = (listTab || '').toUpperCase();
  const onPurchasedOrTransit =
    tab === 'AWAITING_COLLECTION_GROUP' ||
    tab === 'IN_TRANSIT' ||
    tab === 'IN_TRANSIT_GROUP' ||
    tab === 'PURCHASED' ||
    filter === 'AWAITING_COLLECTION_GROUP' ||
    filter === 'IN_TRANSIT' ||
    filter === 'IN_TRANSIT_GROUP' ||
    filter === 'PURCHASED';
  if (!onPurchasedOrTransit) return false;
  return orderHasSheinBillno(order);
};

/** Format Sync track toast detail from per-billno outcomes. */
const formatSyncTrackToast = (
  summary: string | undefined,
  notes: string[] | undefined,
  billnos: Array<{
    billno: string;
    fetchOk: boolean;
    error?: string | null;
    autoMarkShipError?: string | null;
    skipReason?: string | null;
    autoMarkShipStatus?: string | null;
  }> | undefined,
): { level: 'success' | 'warning' | 'error'; text: string } => {
  const soft = (billnos ?? [])
    .filter(
      (b) =>
        !b.fetchOk ||
        !!b.error?.trim() ||
        !!b.autoMarkShipError?.trim() ||
        (b.autoMarkShipStatus || '').toLowerCase() === 'failed' ||
        (b.autoMarkShipStatus || '').toLowerCase() === 'blocked',
    )
    .map(
      (b) =>
        `${b.billno}: ${b.error || b.autoMarkShipError || b.skipReason || b.autoMarkShipStatus || 'lỗi'}`,
    );
  // Dedupe: backend used to echo softFails into notes (same lines twice in the toast).
  const softSet = new Set(soft);
  const noteExtra = (notes ?? []).filter((n) => {
    const t = n.trim();
    if (!t) return false;
    if (softSet.has(t)) return false;
    return !soft.some((s) => t.endsWith(s) || s.endsWith(t) || t.includes(s));
  });
  const extra = [...noteExtra, ...soft];
  const head = summary?.trim() || 'Sync track xong.';
  if (soft.length === 0 && noteExtra.length === 0) {
    return { level: 'success', text: head };
  }
  if (soft.length > 0 && soft.length === (billnos?.length ?? 0)) {
    return { level: 'error', text: `${head}\n${extra.join('\n')}` };
  }
  return { level: 'warning', text: `${head}\n${extra.join('\n')}` };
};

type MarkShipModalState = {
  order: OrderListItem;
  initialTrackingNo?: string | null;
  suggestedCarrier?: string | null;
};

/** One push-track click — `key` bumps so re-clicks and Strict Mode re-runs are not blocked. */
type PushTrackSession = {
  order: OrderListItem;
  key: number;
};

/**
 * Loads 4Seller shipping services for one push-track click, then either confirms exact
 * carrier match + Mark Ship, or opens MarkShipModal for manual service selection.
 */
const PushTrackOrchestrator = ({
  session,
  onDone,
  onNeedModal,
}: {
  session: PushTrackSession | null;
  onDone: () => void;
  onNeedModal: (state: MarkShipModalState) => void;
}) => {
  const deps = useOrdersDeps();
  const order = session?.order ?? null;
  const sessionKey = session?.key ?? 0;
  const link = order ? resolveCarrierPushLink(order) : null;
  const trackingNo = link?.trackingNo?.trim() ?? '';
  const supplierCarrier = link?.carrier?.trim() ?? '';
  const enabled = !!order && !!link && !!trackingNo;

  const {
    data: services = [],
    isLoading,
    isFetching,
    isFetched,
    isError,
    error,
  } = deps.useShippingServices(order?.ownerId, order?.platform, enabled);

  const markShip = deps.useMarkShipOrder();
  const markShipAsyncRef = useRef(markShip.mutateAsync);
  markShipAsyncRef.current = markShip.mutateAsync;
  const servicesRef = useRef(services);
  servicesRef.current = services;
  const processedKeyRef = useRef(0);
  const confirmDestroyRef = useRef<(() => void) | null>(null);
  const onDoneRef = useRef(onDone);
  const onNeedModalRef = useRef(onNeedModal);
  onDoneRef.current = onDone;
  onNeedModalRef.current = onNeedModal;

  useEffect(() => {
    if (!order || !sessionKey) {
      if (!order) {
        processedKeyRef.current = 0;
        confirmDestroyRef.current?.();
        confirmDestroyRef.current = null;
      }
      return;
    }
    if (!link || !trackingNo || !enabled) return;
    // Wait until the shipping-services query has settled — never treat "no data yet" as empty list.
    if (isLoading || (isFetching && !isFetched)) return;
    if (processedKeyRef.current === sessionKey) return;

    processedKeyRef.current = sessionKey;

    const openManual = (suggested?: string | null) => {
      confirmDestroyRef.current?.();
      confirmDestroyRef.current = null;
      onNeedModalRef.current({
        order,
        initialTrackingNo: trackingNo,
        suggestedCarrier: suggested ?? (supplierCarrier || null),
      });
      onDoneRef.current();
    };

    if (isError) {
      message.warning(
        (error as Error)?.message || 'Không tải được Shipping Service — chọn thủ công.',
      );
      openManual(supplierCarrier || null);
      return;
    }

    if (!isFetched) {
      // Query disabled (e.g. missing ownerId) — fall back to manual pick.
      openManual(supplierCarrier || null);
      return;
    }

    if (!supplierCarrier || servicesRef.current.length === 0) {
      openManual(supplierCarrier || null);
      return;
    }

    const matched = findExactCarrierMatch(supplierCarrier, servicesRef.current);
    if (!matched?.carrier?.trim()) {
      openManual(supplierCarrier);
      return;
    }

    const shippingService = matched.carrier.trim();
    confirmDestroyRef.current?.();
    const confirm = Modal.confirm({
      title: 'Đẩy track lên sàn?',
      content: (
        <>
          {shippingService} · {trackingNo}
          <br />
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            Gửi thẳng lên 4Seller ngay — có thể mất 1–2 phút chờ kết quả sau khi bấm Đẩy track.
          </Typography.Text>
        </>
      ),
      okText: 'Đẩy track',
      cancelText: 'Hủy',
      onOk: async () => {
        try {
          const response = await markShipAsyncRef.current({
            id: order.id,
            data: { shippingService, trackingNo },
          });
          if (!response.success) {
            message.error(response.message || 'Mark Shipped thất bại.');
          }
        } catch {
          // Mutation onError already toasts + invalidates; swallow so Ant Design closes confirm.
        } finally {
          confirmDestroyRef.current = null;
          onDoneRef.current();
        }
      },
      onCancel: () => {
        confirmDestroyRef.current = null;
        onDoneRef.current();
      },
    });
    confirmDestroyRef.current = () => {
      confirm.destroy();
    };

    return () => {
      confirmDestroyRef.current?.();
      confirmDestroyRef.current = null;
      // Strict Mode tears down confirm then re-runs effect — undo lock for the same click.
      if (processedKeyRef.current === sessionKey) {
        processedKeyRef.current = 0;
      }
    };
  }, [
    order,
    sessionKey,
    link,
    trackingNo,
    supplierCarrier,
    enabled,
    isLoading,
    isFetching,
    isFetched,
    isError,
    error,
  ]);

  return null;
};

const PushTrackIconButton = ({
  order,
  onPush,
  loading = false,
  size = 'small',
}: {
  order: OrderListItem;
  onPush: (order: OrderListItem) => void;
  loading?: boolean;
  size?: 'small' | 'middle';
}) => (
  <Tooltip title={loading ? 'Đang tải Shipping Service…' : 'Đẩy track lên sàn'}>
    <span
      style={{ display: 'inline-flex', alignItems: 'center', lineHeight: 1 }}
      onClick={(e) => e.stopPropagation()}
    >
      <Button
        type="link"
        size={size}
        icon={<CarOutlined />}
        loading={loading}
        onClick={(e) => {
          e.stopPropagation();
          onPush(order);
        }}
        style={{ padding: 0, height: 'auto', lineHeight: 1 }}
        aria-label="Đẩy track lên sàn"
      />
    </span>
  </Tooltip>
);

const AutoMarkShipNoticeLine = ({
  notice,
  primary,
}: {
  notice: AutoMarkShipNotice;
  primary?: boolean;
}) => (
  <Tooltip title={notice.text}>
    <Text
      type={notice.tone}
      style={{
        ...(primary ? metaLineStyle : subtleLineStyle),
        ...ellipsisLineStyle,
        display: 'block',
        margin: 0,
        color:
          notice.tone === 'danger' ? 'var(--order-deadline, #cf1322)' : undefined,
        fontSize: primary ? 12 : 11,
        lineHeight: primary ? '17px' : '15px',
      }}
    >
      {notice.text}
    </Text>
  </Tooltip>
);

const TrackingCopyLine = ({
  tracking,
  primary,
}: {
  tracking: OrderTrackingDisplay;
  primary?: boolean;
}) => {
  const label =
    tracking.extraCount > 0
      ? `${tracking.trackingNo} +${tracking.extraCount}`
      : tracking.trackingNo;

  const tip =
    tracking.extras.length > 0 ? (
      <div style={{ lineHeight: 1.55, maxWidth: 320 }}>
        <div style={{ fontWeight: 600, marginBottom: 4 }}>
          {formatTrackingTipLine(tracking)}
        </div>
        <div style={{ opacity: 0.85, marginBottom: 2 }}>
          Mã khác (+{tracking.extras.length}):
        </div>
        {tracking.extras.map((extra) => (
          <div
            key={extra.trackingNo}
            style={{
              fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
              wordBreak: 'break-all',
            }}
          >
            {formatTrackingTipLine(extra)}
          </div>
        ))}
      </div>
    ) : (
      formatTrackingTipLine(tracking)
    );

  return (
    <Tooltip title={tip}>
      <Text
        role="button"
        tabIndex={0}
        onClick={() => void copyOrderId(tracking.trackingNo)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            void copyOrderId(tracking.trackingNo);
          }
        }}
        style={{
          ...(primary ? primaryLineStyle : metaLineStyle),
          ...ellipsisLineStyle,
          cursor: 'pointer',
          display: 'block',
          margin: 0,
          fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
          letterSpacing: '-0.01em',
          color: tracking.kind === 'warehouse' ? 'var(--order-text-secondary)' : undefined,
        }}
      >
        {label}
      </Text>
    </Tooltip>
  );
};

const statusLabel = (status?: string | null) => {
  const raw = (status ?? '').trim();
  if (!raw) return 'Không rõ';
  return STATUS_LABEL[raw] || STATUS_LABEL[raw.toUpperCase()] || STATUS_LABEL[raw.toLowerCase()] || raw;
};

/** TikTok/platform status for card text + color; workflow tab is ops queue only. */
const platformStatus = (order: {
  workflowTab?: string | null;
  platformOrderStatus?: string | null;
  orderStatus?: string | null;
}) => order.platformOrderStatus || order.workflowTab || order.orderStatus || '';

/** Internal workflow tab (PENDING_PROCESS / PURCHASED / AWAITING_COLLECTION_GROUP / IN_TRANSIT / …). */
const workflowStatus = (order: { workflowTab?: string | null }) => order.workflowTab || '';

/** Cancel/refund reason from TikTok — show under Đã hủy / Refund. */
const orderCancelReason = (order: { cancelReason?: string | null }) => {
  const reason = order.cancelReason?.trim();
  return reason || null;
};

/** Human-readable 4Seller return case status — second line under the reason. */
const orderReturnStatus = (order: { returnStatus?: string | null }) => {
  const text = order.returnStatus?.trim();
  return text || null;
};

/** Local operational notice — prefix trước tên SP; ẩn khi IssueStatus=Done. */
const orderMessage = (order: {
  message?: string | null;
  issueStatus?: string | null;
}) => {
  if ((order.issueStatus || '').toLowerCase() === 'done') return null;
  const text = order.message?.trim();
  return text || null;
};

const orderStatusSecondaryLineStyle: CSSProperties = {
  fontSize: 11,
  lineHeight: '16px',
  display: '-webkit-box',
  WebkitLineClamp: 2,
  WebkitBoxOrient: 'vertical',
  overflow: 'hidden',
  wordBreak: 'break-word',
};

const isCancelOrRefundStatus = (status?: string | null) => {
  const key = (status ?? '').trim().toUpperCase();
  return key === 'CANCELLED' || key === 'CANCELED' || key === 'REFUND' || key === 'REFUNDED';
};

/**
 * Dòng 2 cột thời gian trên OrderCard: ưu tiên mốc cuối của đơn (Refund → Hủy → Giao)
 * thay vì hạn giao — hạn chỉ còn ý nghĩa khi đơn còn mở. Đơn hoàn/hủy theo status
 * nhưng chưa stamp clock vẫn hiện nhãn terminal (giá trị «—») thay vì hạn ship cũ.
 */
const orderCardEndTimeLine = (
  order: Pick<
    OrderListItem,
    | 'refundedAt'
    | 'canceledAt'
    | 'deliveredAt'
    | 'latestShipDate'
    | 'workflowTab'
    | 'platformOrderStatus'
  >,
): { label: string; at: string | null | undefined; valueColor: string } => {
  if (order.refundedAt) {
    return { label: 'Refund', at: order.refundedAt, valueColor: '#c41d7f' };
  }
  if (order.canceledAt) {
    return { label: 'Hủy', at: order.canceledAt, valueColor: '#cf1322' };
  }
  const tab = (order.workflowTab || '').toUpperCase();
  const platform = (order.platformOrderStatus || '').toUpperCase();
  if (tab === 'REFUND' || platform === 'REFUND' || platform === 'REFUNDED') {
    return { label: 'Refund', at: null, valueColor: '#c41d7f' };
  }
  if (tab === 'CANCELLED' || platform === 'CANCELLED' || platform === 'CANCELED') {
    return { label: 'Hủy', at: null, valueColor: '#cf1322' };
  }
  if (order.deliveredAt) {
    return { label: 'Giao', at: order.deliveredAt, valueColor: 'var(--order-text)' };
  }
  return {
    label: 'Hạn',
    at: order.latestShipDate,
    valueColor: 'var(--order-deadline)',
  };
};

const statusTooltipTitle = (order: {
  workflowTab?: string | null;
  platformOrderStatus?: string | null;
  orderStatus?: string | null;
  cancelReason?: string | null;
  returnStatus?: string | null;
}): ReactNode => {
  const status = platformStatus(order);
  const label = statusLabel(status);
  const raw = (order.platformOrderStatus || '').trim();
  const mappedFromRaw = raw ? statusLabel(raw) : '';
  const showRawCode = Boolean(raw) && mappedFromRaw !== raw && mappedFromRaw === label;
  const platformLine = showRawCode ? `Sàn: ${label} (${raw})` : `Sàn: ${label}`;

  const internalKey = workflowStatus(order);
  const internalLabel = internalKey ? statusLabel(internalKey) : '';
  const showInternal =
    Boolean(internalKey) && internalLabel.toUpperCase() !== label.toUpperCase();

  const cancelOrRefund = isCancelOrRefundStatus(status);
  const reason = cancelOrRefund ? orderCancelReason(order) : null;
  const returnStatus = cancelOrRefund ? orderReturnStatus(order) : null;

  if (!showInternal && !reason && !returnStatus && !showRawCode) {
    return label;
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <div>{platformLine}</div>
      {showInternal ? <div>Nội bộ: {internalLabel}</div> : null}
      {reason ? <div>{reason}</div> : null}
      {returnStatus ? <div>{returnStatus}</div> : null}
    </div>
  );
};

const metaLineStyle: CSSProperties = {
  fontSize: 12,
  lineHeight: '17px',
  color: 'var(--order-text-secondary)',
};

const primaryLineStyle: CSSProperties = {
  fontSize: 12,
  lineHeight: '17px',
  fontWeight: 500,
  color: 'var(--order-text)',
};

const subtleLineStyle: CSSProperties = {
  fontSize: 11,
  lineHeight: '16px',
  color: 'var(--order-text-tertiary)',
};

const ellipsisLineStyle: CSSProperties = {
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  minWidth: 0,
};

const ORDER_CARD_THUMB_SIZE = 52;

const ProductThumb = ({
  src,
  href,
  size = ORDER_CARD_THUMB_SIZE,
}: {
  src?: string | null;
  href?: string | null;
  size?: number;
}) => {
  const imgRef = useRef<HTMLImageElement | null>(null);
  const [inView, setInView] = useState(false);
  const [previewReady, setPreviewReady] = useState(false);

  useEffect(() => {
    setInView(false);
    setPreviewReady(false);
  }, [src]);

  useEffect(() => {
    if (!src || inView) return;
    const el = imgRef.current;
    if (!el) return;

    if (typeof IntersectionObserver === 'undefined') {
      setInView(true);
      return;
    }

    const io = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setInView(true);
          io.disconnect();
        }
      },
      { rootMargin: '240px 0px', threshold: 0.01 },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [src, inView]);

  const thumb = !src ? (
    <div
      style={{
        width: size,
        height: size,
        background: 'var(--order-thumb-bg)',
        borderRadius: 4,
        flexShrink: 0,
      }}
    />
  ) : (
    <img
      ref={imgRef}
      src={inView ? src : undefined}
      alt=""
      width={size}
      height={size}
      referrerPolicy="no-referrer"
      loading="lazy"
      decoding="async"
      style={{
        objectFit: 'cover',
        borderRadius: 4,
        flexShrink: 0,
        display: 'block',
        background: 'var(--order-thumb-bg)',
        cursor: href ? 'pointer' : undefined,
      }}
      onError={(e) => {
        e.currentTarget.style.visibility = 'hidden';
      }}
    />
  );

  const wrapped = href ? (
    <a href={href} target="_blank" rel="noreferrer" style={{ flexShrink: 0, lineHeight: 0 }}>
      {thumb}
    </a>
  ) : (
    thumb
  );

  if (!src) return wrapped;

  return (
    <Popover
      content={
        previewReady ? (
          <img
            src={src}
            alt=""
            referrerPolicy="no-referrer"
            decoding="async"
            style={{
              display: 'block',
              maxWidth: 320,
              maxHeight: 320,
              objectFit: 'contain',
              borderRadius: 4,
            }}
          />
        ) : (
          <div style={{ width: size, height: size, background: 'var(--order-thumb-bg)', borderRadius: 4 }} />
        )
      }
      trigger="hover"
      placement="right"
      mouseEnterDelay={0.25}
      arrow={false}
      styles={{ container: { padding: 1 } }}
      onOpenChange={(open) => {
        if (open) setPreviewReady(true);
      }}
    >
      <span style={{ flexShrink: 0, lineHeight: 0, display: 'inline-block' }}>{wrapped}</span>
    </Popover>
  );
};

const OrderItemRating = ({
  averageRating,
  reviewCount,
  successMin = LISTING_RATING_DEFAULTS.successMin,
  warningMin = LISTING_RATING_DEFAULTS.warningMin,
}: {
  averageRating?: number | null;
  reviewCount?: number | null;
  successMin?: number;
  warningMin?: number;
}) => {
  const ratingNum =
    averageRating != null && Number.isFinite(Number(averageRating)) ? Number(averageRating) : null;
  if (ratingNum == null && reviewCount == null) return null;

  const ratingTier: ListingRatingTier | null =
    ratingNum != null ? getListingRatingTier(ratingNum, successMin, warningMin) : null;

  return (
    <span
      className={ratingTier ? `listing-rating listing-rating--${ratingTier}` : 'listing-rating'}
      style={{ marginTop: 0, flexShrink: 0 }}
      title={
        ratingNum != null
          ? `${ratingNum.toFixed(2)}` +
            (reviewCount != null ? ` (${reviewCount.toLocaleString()})` : '')
          : reviewCount != null
            ? `(${reviewCount.toLocaleString()})`
            : undefined
      }
    >
      {ratingNum != null ? (
        <>
          <ListingStars rating={ratingNum} tier={ratingTier} />
          <span className="listing-rating-value">{ratingNum.toFixed(2)}</span>
        </>
      ) : null}
      {reviewCount != null ? (
        <span className="listing-review-count">({reviewCount.toLocaleString()})</span>
      ) : null}
    </span>
  );
};

/** SLA shop NCC: `80.34% ≤ 4`. */
const formatShippingSla = (
  days: number | null | undefined,
  percent: number | null | undefined,
): string => {
  const hasDays = days != null && Number.isFinite(days);
  const hasPercent = percent != null && Number.isFinite(percent);
  if (hasPercent && hasDays) return `${percent}% ≤ ${days}`;
  if (hasPercent) return `${percent}%`;
  if (hasDays) return `≤ ${days}`;
  return '';
};

const formatShopRank = (rank?: number | null): string => {
  if (rank == null || !Number.isFinite(Number(rank))) return '';
  const n = Number(rank);
  return Number.isInteger(n) ? String(n) : n.toFixed(2).replace(/\.?0+$/, '');
};

const formatStoreAttention = (count?: number | null): string => {
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
};

const OrderStoreIcon = () => (
  <svg className="listing-stat-icon" viewBox="0 0 16 16" width="12" height="12" aria-hidden>
    <path
      fill="currentColor"
      d="M2 3.5A1.5 1.5 0 0 1 3.5 2h9A1.5 1.5 0 0 1 14 3.5V6h.25a.75.75 0 0 1 0 1.5H14V13a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 13V7.5h-.25a.75.75 0 0 1 0-1.5H2V3.5ZM3.5 3a.5.5 0 0 0-.5.5V6h10V3.5a.5.5 0 0 0-.5-.5h-9ZM3 8v5h3.5V9.75h3V13H13V8H3Z"
    />
  </svg>
);

const OrderRankIcon = () => (
  <svg className="listing-stat-icon" viewBox="0 0 16 16" width="12" height="12" aria-hidden>
    <path
      fill="currentColor"
      d="M8 1.25A4.25 4.25 0 0 0 5.4 8.9L4.15 14.2a.4.4 0 0 0 .58.45L8 12.85l3.27 1.8a.4.4 0 0 0 .58-.45L10.6 8.9A4.25 4.25 0 0 0 8 1.25Zm0 1.5a2.75 2.75 0 1 1 0 5.5 2.75 2.75 0 0 1 0-5.5Z"
    />
  </svg>
);

const OrderFollowIcon = () => (
  <svg className="listing-stat-icon" viewBox="0 0 16 16" width="12" height="12" aria-hidden>
    <path
      fill="currentColor"
      d="M6.25 7.75a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Zm5.1.35a2.15 2.15 0 1 0-2.05-3.45 3.35 3.35 0 0 1 .55 3.45ZM1.5 13.15C1.5 11.1 3.45 9.6 6.25 9.6s4.75 1.5 4.75 3.55V14h-9.5v-.85Zm9.35-.05c0-1.05-.4-1.95-1.1-2.65 1.55.2 3.25 1 3.25 2.7V14h-2.15v-.9Z"
    />
  </svg>
);

const OrderShipFlags = ({ item }: { item: OrderItem }) => {
  if (item.isLocalProduct !== true && item.isSupportQuickShip !== true) return null;
  return (
    <span className="listing-supplier-flags">
      {item.isLocalProduct === true && (
        <span className="listing-ship-badge listing-ship-badge--local" title="Hàng local US">
          Local
        </span>
      )}
      {item.isSupportQuickShip === true && (
        <span
          className="listing-ship-badge listing-ship-badge--quick"
          title="Ít nhất một SKU mall_stock hỗ trợ ship nhanh"
        >
          Nhanh
        </span>
      )}
    </span>
  );
};

const OrderSupplierLine = ({ item }: { item: OrderItem }) => {
  const shopRank = formatShopRank(item.shopRank);
  const attention = formatStoreAttention(item.storeAttention);
  const shippingSla = formatShippingSla(item.shippingDays, item.shippingDayPercent);
  const hasStore = !!item.storeTitle || !!item.storeUrl;
  const hasShopMeta = !!shopRank || !!attention;
  const hasFlags = item.isLocalProduct === true || item.isSupportQuickShip === true;
  if (!hasStore && !hasShopMeta && !shippingSla && !hasFlags) return null;

  const storeInner = (
    <>
      <OrderStoreIcon />
      {item.storeTitle ? <span className="listing-store-title">{item.storeTitle}</span> : null}
    </>
  );

  return (
    <div className="listing-supplier-shop">
      {hasStore || hasShopMeta ? (
        <span className="listing-store-group">
          {item.storeUrl ? (
            <a
              className="listing-store"
              href={item.storeUrl}
              target="_blank"
              rel="noreferrer"
              title={item.storeTitle || item.storeUrl}
              onClick={(e) => e.stopPropagation()}
            >
              {storeInner}
            </a>
          ) : hasStore ? (
            <span className="listing-store" title={item.storeTitle || undefined}>
              {storeInner}
            </span>
          ) : null}
          {hasShopMeta ? (
            <span className="listing-supplier-stats">
              {shopRank ? (
                <span className="listing-stat listing-stat--rank" title={`Rate shop NCC: ${shopRank}`}>
                  <OrderRankIcon />
                  <span>{shopRank}</span>
                </span>
              ) : null}
              {attention ? (
                <span
                  className="listing-stat listing-stat--follow"
                  title={`Follow shop NCC: ${attention}`}
                >
                  <OrderFollowIcon />
                  <span>{attention}</span>
                </span>
              ) : null}
            </span>
          ) : null}
        </span>
      ) : null}
      {shippingSla ? (
        <span className="listing-ship-sla" title="SLA giao hàng shop NCC">
          {shippingSla}
        </span>
      ) : null}
      <OrderShipFlags item={item} />
    </div>
  );
};

/** Thông báo Orders.Message — prefix đỏ IN HOA trước tên SP: `[HẾT HÀNG] Title`. */
const formatProductTitleWithNotice = (title: string | null | undefined, notice?: string | null) => {
  const name = (title || '').trim() || '—';
  const msg = notice?.trim();
  if (!msg) return { full: name, notice: null as string | null, name };
  const noticeUpper = msg.toLocaleUpperCase('vi-VN');
  return { full: `[${noticeUpper}] ${name}`, notice: noticeUpper, name };
};

const ProductBlock = ({
  item,
  compact,
  tracking,
  pushTrack,
  index,
  notice,
  ratingSuccessMin,
  ratingWarningMin,
}: {
  item: OrderItem;
  compact?: boolean;
  tracking?: OrderTrackingDisplay | null;
  /** Icon đẩy track cạnh mã vận đơn (mobile compact). */
  pushTrack?: ReactNode;
  /** Số thứ tự card — overlay góc ảnh (chỉ dòng đầu). */
  index?: number;
  /** Thông báo vận hành — chỉ dòng SP đầu: `[msg] tên`. */
  notice?: string | null;
  ratingSuccessMin?: number;
  ratingWarningMin?: number;
}) => {
  const metaParts = [
    item.sellerSku ? `SKU: ${item.sellerSku}` : null,
    item.variantAttr || null,
    item.quantity > 1 ? `×${item.quantity}` : null,
  ].filter(Boolean);

  const rating = (
    <OrderItemRating
      averageRating={item.averageRating}
      reviewCount={item.reviewCount}
      successMin={ratingSuccessMin}
      warningMin={ratingWarningMin}
    />
  );

  const productUrl = item.sourceUrl?.trim() || item.listingUrl?.trim() || null;

  const thumb = (
    <ProductThumb
      src={item.sourceImgUrl || item.imgUrl}
      href={productUrl}
      size={ORDER_CARD_THUMB_SIZE}
    />
  );

  const titleParts = formatProductTitleWithNotice(item.title, notice);
  const titleNode = titleParts.notice ? (
    <>
      <span style={{ color: '#ff4d4f', fontWeight: 600, fontSize: 11 }}>[{titleParts.notice}]</span>{' '}
      {titleParts.name}
    </>
  ) : (
    titleParts.name
  );

  return (
    <div className="order-product-block">
      {index != null ? (
        <div className="order-thumb-wrap" title={`#${index}`} aria-label={`Thứ tự ${index}`}>
          <span className="order-thumb-index" aria-hidden="true">
            {index}
          </span>
          {thumb}
        </div>
      ) : (
        thumb
      )}
      <div className="order-card-lines">
        {productUrl ? (
          <a
            href={productUrl}
            target="_blank"
            rel="noreferrer"
            title={titleParts.full}
            className="order-card-line order-card-line--title"
            onClick={(e) => e.stopPropagation()}
          >
            {titleNode}
          </a>
        ) : (
          <div
            className="order-card-line order-card-line--title order-card-line--title-no-source"
            title={titleParts.full ? `${titleParts.full} (chưa có URL nguồn)` : 'Chưa có URL nguồn'}
          >
            {titleNode}
          </div>
        )}
        <div className="order-card-line order-card-line--row">
          {rating}
          {!compact && metaParts.length > 0 ? (
            <span className="order-card-line-meta" title={metaParts.join(' · ')}>
              {metaParts.join(' · ')}
            </span>
          ) : null}
          {compact && (item.variantAttr || item.quantity > 1) ? (
            <span className="order-card-line-meta">
              {[item.variantAttr, item.quantity > 1 ? `×${item.quantity}` : null]
                .filter(Boolean)
                .join(' · ')}
            </span>
          ) : null}
          {compact && tracking ? (
            <span
              style={{
                marginLeft: 'auto',
                minWidth: 0,
                maxWidth: '55%',
                display: 'inline-flex',
                alignItems: 'center',
                gap: 4,
              }}
            >
              <TrackingCopyLine tracking={tracking} />
              {pushTrack}
            </span>
          ) : null}
        </div>
        <div className="order-card-line order-card-line--row">
          <OrderSupplierLine item={item} />
        </div>
      </div>
    </div>
  );
};

const NOTE_BADGE_META: {
  key: 'buyerMemo' | 'sellerMemo' | 'internalMemo';
  label: string;
  color: string;
  bg: string;
  letter: string;
}[] = [
  { key: 'buyerMemo', label: 'Buyer Notes', color: 'var(--order-note-buyer-fg)', bg: 'var(--order-note-buyer-bg)', letter: 'B' },
  { key: 'sellerMemo', label: 'Seller Notes', color: 'var(--order-note-seller-fg)', bg: 'var(--order-note-seller-bg)', letter: 'S' },
  { key: 'internalMemo', label: 'Internal Notes', color: 'var(--order-note-internal-fg)', bg: 'var(--order-note-internal-bg)', letter: 'I' },
];

type FulfilMilestoneKey = keyof Pick<
  OrderListItem,
  | 'orderCreateTime'
  | 'fulfilPurchasedAt'
  | 'trackingAddedAt'
  | 'inTransitAt'
  | 'deliveredAt'
  | 'canceledAt'
  | 'refundedAt'
>;

const FULFIL_MILESTONES: {
  key: FulfilMilestoneKey;
  label: string;
  short: string;
}[] = [
  { key: 'orderCreateTime', label: 'Khách đặt', short: 'Đặt' },
  { key: 'fulfilPurchasedAt', label: 'Fulfil mua', short: 'Mua' },
  // "Mã lên sàn" chứ không phải "có mã": mốc này là clock OrderShippedTime của 4Seller, tức lúc mã
  // vận đơn được công bố lên sàn (đúng 153/153 đơn đo được). SHEIN thường đã rời kho trước đó, nên
  // Transit sớm hơn Track ở 49/153 đơn — nhãn cũ "Có tracking" khiến thứ tự này trông như lỗi.
  { key: 'trackingAddedAt', label: 'Mã lên sàn', short: 'Track' },
  { key: 'inTransitAt', label: 'Rời kho SHEIN', short: 'Transit' },
  { key: 'deliveredAt', label: 'Delivered', short: 'Giao' },
  { key: 'canceledAt', label: 'Đã hủy', short: 'Hủy' },
  { key: 'refundedAt', label: 'Refund', short: 'Refund' },
];

const supplierStatusTag = (
  status?: string | null,
): { color: string; label: string } | null => {
  switch ((status || '').toLowerCase()) {
    case 'processing':
      return { color: 'default', label: 'SHEIN: Processing' };
    case 'shipped':
      return { color: 'blue', label: 'SHEIN: Shipped' };
    case 'delivered':
      return { color: 'green', label: 'SHEIN: Delivered' };
    case 'canceled':
    case 'cancelled':
      return { color: 'red', label: 'SHEIN: Canceled' };
    case 'refunded':
      return { color: 'magenta', label: 'SHEIN: Refunded' };
    default:
      return null;
  }
};

/** Compact duration for timeline segments (static or live wait). vd: `42m`, `2h10m`, `1d18h`. */
const formatDurationShort = (fromMs: number, toMs: number): string => {
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
};

const parseIsoMs = (value?: string | null): number | null => {
  if (!value) return null;
  const ms = Date.parse(value);
  return Number.isNaN(ms) ? null : ms;
};

/** One shared clock for live “đang chờ” segments — avoids N setIntervals on the list. */
const useSharedNow = (enabled: boolean, intervalMs = 30_000) => {
  const [nowMs, setNowMs] = useState(() => Date.now());
  useEffect(() => {
    if (!enabled) return;
    setNowMs(Date.now());
    const id = window.setInterval(() => setNowMs(Date.now()), intervalMs);
    return () => window.clearInterval(id);
  }, [enabled, intervalMs]);
  return nowMs;
};

const orderNeedsLiveFulfilTick = (order: OrderListItem) => {
  if (order.refundedAt || order.deliveredAt) return false;
  return !!order.orderCreateTime;
};

const DetailSection = ({
  title,
  extra,
  children,
  surface,
  className,
}: {
  title: string;
  extra?: ReactNode;
  children: ReactNode;
  surface?: boolean;
  className?: string;
}) => (
  <div
    className={[
      'order-detail-section',
      surface ? 'order-detail-section--surface' : '',
      className,
    ]
      .filter(Boolean)
      .join(' ')}
  >
    <div className="order-detail-section-head">
      <Text strong className="order-detail-section-title">
        {title}
      </Text>
      {extra}
    </div>
    {children}
  </div>
);

const DetailField = ({
  label,
  children,
  span = 1,
  className,
}: {
  label: string;
  children: ReactNode;
  span?: 1 | 2;
  className?: string;
}) => (
  <div
    className={[
      'order-detail-field',
      span === 2 ? 'order-detail-field--span2' : '',
      className,
    ]
      .filter(Boolean)
      .join(' ')}
  >
    <div className="order-detail-field-label">{label}</div>
    <div className="order-detail-field-value">{children}</div>
  </div>
);

/**
 * Chặng "đang chờ" = chặng chưa tới đầu tiên nằm SAU mốc đã tới cuối cùng. Vì Track có thể bị
 * bỏ qua (đơn giao xong mà SHEIN chưa từng gán carrier), không thể lấy chặng chưa tới đầu tiên
 * — sẽ hiện "đang chờ carrier" trên một đơn đã giao.
 */
const findFulfilFrontierIdx = (milestones: FulfilTimelineNode[]): number => {
  let lastReached = -1;
  milestones.forEach((m, i) => {
    if (m.reached) lastReached = i;
  });
  return milestones.findIndex((m, i) => !m.reached && i > lastReached);
};

const FulfilTimeline = ({ order, nowMs }: { order: OrderListItem; nowMs: number }) => {
  // Đơn Hủy/Refund: nối node kết thúc ngay sau mốc cuối đã đạt (bỏ Transit/Giao trống).
  const { nodes: milestones, terminal } = buildFulfilTimeline(order);
  const datedCount = milestones.filter((m) => !!m.at).length;
  const currentIdx = terminal ? -1 : findFulfilFrontierIdx(milestones);

  return (
    <div>
      <div
        className="order-detail-fulfil-head"
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 6,
        }}
      >
        <Text type="secondary">
          {datedCount}/{milestones.length} mốc
          {order.paidByUserName ? (
            <>
              {' · '}
              Người đi đơn: <Text strong>{order.paidByUserName}</Text>
            </>
          ) : null}
        </Text>
      </div>
      <div style={{ display: 'flex', flexDirection: 'column' }}>
        {milestones.map((m, idx) => {
          const at = m.at;
          const dated = !!at;
          const reached = m.reached;
          const isTerminalMilestone = m.terminal;
          const isCurrent = !reached && idx === currentIdx;
          const isLast = idx === milestones.length - 1;
          const next = milestones[idx + 1];
          const nextIsTerminal = !!next && next.terminal;
          const lineDated = dated && !!next && !!next.at;
          const fromMs = parseIsoMs(at);
          const nextMs = next ? parseIsoMs(next.at) : null;
          // Chỉ tính khoảng khi cả hai đầu có mốc thật (không bịa "—"/"<1m").
          let segmentLabel: string | null = null;
          let segmentLive = false;
          if (!isLast && fromMs != null) {
            if (lineDated && nextMs != null && nextMs >= fromMs) {
              segmentLabel = formatDurationShort(fromMs, nextMs);
            } else if (
              !terminal &&
              !!next &&
              !next.at &&
              currentIdx === idx + 1 &&
              !nextIsTerminal
            ) {
              segmentLabel = formatDurationShort(fromMs, nowMs);
              segmentLive = true;
            }
          }

          const terminalColor = m.key === 'refundedAt' ? '#eb2f96' : '#ff4d4f';
          const terminalTextColor = m.key === 'refundedAt' ? '#c41d7f' : '#cf1322';
          const accent = isTerminalMilestone ? terminalColor : '#52c41a';

          return (
            <div
              key={m.key}
              className="order-detail-fulfil-row"
              style={{ display: 'flex', gap: 8 }}
            >
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  width: 16,
                  flexShrink: 0,
                }}
              >
                {dated ? (
                  <CheckCircleFilled
                    style={{ fontSize: 14, color: accent, marginTop: 1 }}
                  />
                ) : reached ? (
                  <span
                    aria-hidden
                    style={{
                      width: 10,
                      height: 10,
                      borderRadius: '50%',
                      border: `2px solid ${accent}`,
                      background: 'var(--order-dot-empty-bg)',
                      marginTop: 3,
                      boxSizing: 'border-box',
                    }}
                  />
                ) : isCurrent ? (
                  <ClockCircleOutlined style={{ fontSize: 14, color: '#1677ff', marginTop: 1 }} />
                ) : (
                  <span
                    aria-hidden
                    style={{
                      width: 8,
                      height: 8,
                      borderRadius: '50%',
                      border: '2px solid var(--order-dot-empty-border)',
                      background: 'var(--order-dot-empty-bg)',
                      marginTop: 4,
                      boxSizing: 'border-box',
                    }}
                  />
                )}
                {!isLast ? (
                  <div
                    style={{
                      flex: 1,
                      width: 2,
                      marginTop: 3,
                      borderRadius: 1,
                      background: lineDated
                        ? isTerminalMilestone || nextIsTerminal
                          ? next && next.key === 'refundedAt'
                            ? '#ffadd2'
                            : '#ffccc7'
                          : '#52c41a'
                        : segmentLive || isCurrent
                          ? '#91caff'
                          : next && next.reached
                            ? '#d9f7be'
                            : 'var(--order-timeline-pending)',
                      minHeight: 12,
                    }}
                  />
                ) : null}
              </div>
              <div
                className="order-detail-fulfil-body"
                style={{ flex: 1, paddingBottom: isLast ? 0 : 6, minWidth: 0 }}
              >
                <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 6 }}>
                  <Text
                    className="order-detail-fulfil-label"
                    strong={dated || reached || isCurrent}
                    type={dated || reached || isCurrent ? undefined : 'secondary'}
                    style={{
                      color:
                        dated && isTerminalMilestone ? terminalTextColor : undefined,
                    }}
                  >
                    {m.label}
                  </Text>
                  <Text
                    className="order-detail-fulfil-time"
                    type={dated ? undefined : 'secondary'}
                    style={{
                      fontVariantNumeric: 'tabular-nums',
                      whiteSpace: 'nowrap',
                      color: isCurrent
                        ? '#1677ff'
                        : dated && isTerminalMilestone
                          ? terminalTextColor
                          : undefined,
                    }}
                  >
                    {dated
                      ? formatDateTime(at)
                      : reached
                        ? 'đã tới · chưa rõ giờ'
                        : m.key === 'trackingAddedAt'
                          ? 'Chưa có carrier'
                          : isCurrent
                            ? 'Đang chờ'
                            : '—'}
                  </Text>
                </div>
                {segmentLabel ? (
                  <Text
                    className="order-detail-fulfil-seg"
                    style={{
                      fontVariantNumeric: 'tabular-nums',
                      color: segmentLive ? '#1677ff' : 'var(--order-text-tertiary)',
                      display: 'block',
                    }}
                  >
                    {segmentLive ? `Đợi ${segmentLabel}` : segmentLabel}
                  </Text>
                ) : null}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};

/** 5 mốc chính (ẩn Hủy/Refund) — đơn bình thường luôn đủ số cột → căn hàng giữa các order. */
const HEADER_FULFIL_MILESTONES = FULFIL_MILESTONES.filter(
  (m) => m.key !== 'refundedAt' && m.key !== 'canceledAt',
);

type FulfilTimelineNode = {
  key: FulfilMilestoneKey;
  label: string;
  short: string;
  /** Real event timestamp (ISO) — only ever a genuine clock, never sync-run time. */
  at: string | null | undefined;
  /** Whether this stage has been reached (derived from status), even if its clock is unknown. */
  reached: boolean;
  terminal: boolean;
};

/**
 * Suy ra chặng đã "tới" từ trạng thái nền tảng / NCC (tách khỏi thời điểm thật).
 * Cho phép UI hiển thị chấm rỗng + "?" khi đã tới chặng nhưng chưa có mốc thời gian chính xác,
 * thay vì bịa số hoặc để trống hoàn toàn.
 *
 * Đơn điệu ở mọi chặng TRỪ Track: SHEIN chuyển "Shipped" ngay khi rời kho, mã carrier chặng
 * cuối thường có sau đó — nên Transit đã tới mà Track chưa là trạng thái thật, không phải lỗi.
 * Suy Track từ Transit sẽ hiện "Track?" (đã qua, thiếu giờ) trong khi thực tế là chưa có mã nào.
 */
const computeFulfilReached = (
  order: OrderListItem,
): Record<FulfilMilestoneKey, boolean> => {
  const platform = (order.platformOrderStatus || '').toUpperCase();
  const links = order.supplierLinks ?? [];
  const supplierShipped = links.some((l) => {
    const s = (l.supplierStatus || '').toLowerCase();
    return s === 'shipped' || s === 'delivered';
  });
  const supplierDelivered = links.some(
    (l) => (l.supplierStatus || '').toLowerCase() === 'delivered',
  );
  const hasCarrierTracking =
    links.some((l) => (l.trackingKind || '') === 'carrier' && !!l.trackingNo) ||
    // Order.trackingNo có thể là mã kho SHEIN C…/N… — không tính là đã tới Track.
    (!!order.trackingNo &&
      !/^[CN]\d{10,}$/i.test(order.trackingNo.trim()) &&
      !(order.shippingService || '').toLowerCase().includes('warehouse'));

  const deliveredReached =
    !!order.deliveredAt || platform === 'DELIVERED' || supplierDelivered;
  const transitReached =
    deliveredReached ||
    !!order.inTransitAt ||
    platform === 'IN_TRANSIT' ||
    supplierShipped;
  const trackReached =
    !!order.trackingAddedAt || hasCarrierTracking || platform === 'AWAITING_COLLECTION';
  const purchasedReached =
    transitReached ||
    trackReached ||
    !!order.fulfilPurchasedAt ||
    !!order.fulfilAccountId ||
    links.length > 0;
  const createReached = !!order.orderCreateTime || purchasedReached;

  return {
    orderCreateTime: createReached,
    fulfilPurchasedAt: purchasedReached,
    trackingAddedAt: trackReached,
    inTransitAt: transitReached,
    deliveredAt: deliveredReached,
    canceledAt: !!order.canceledAt,
    refundedAt: !!order.refundedAt,
  };
};

/**
 * Xếp Track và Transit theo đúng thứ tự xảy ra khi cả hai đã có mốc thật.
 *
 * Track (mã vận đơn lên sàn, 4Seller) và Transit (hàng rời kho SHEIN) là hai sự kiện độc lập ở hai
 * hệ thống nên thứ tự giữa chúng không cố định: đo lại sau khi dọn mốc bịa, 49/153 đơn (32%) có hàng
 * rời kho **trước** khi mã lên sàn, sớm hơn trung vị 3,8 giờ. Dải này là một trục thời gian, nên giữ
 * thứ tự cứng sẽ tạo ra một chặng lùi — và vì bộ đếm khoảng cách không hiện được số âm, đoạn đó
 * trông như bị thiếu bộ đếm.
 *
 * Chỉ đảo khi cả hai đã có clock thật: lúc đó cả hai đều đã đạt nên vị trí chặng đang chờ
 * (`findFulfilFrontierIdx`) không đổi.
 */
const orderTrackTransitChronologically = (
  nodes: FulfilTimelineNode[],
): FulfilTimelineNode[] => {
  const trackIdx = nodes.findIndex((n) => n.key === 'trackingAddedAt');
  const transitIdx = nodes.findIndex((n) => n.key === 'inTransitAt');
  if (trackIdx < 0 || transitIdx !== trackIdx + 1) return nodes;

  const track = nodes[trackIdx];
  const transit = nodes[transitIdx];
  if (!track || !transit) return nodes;

  const trackMs = parseIsoMs(track.at);
  const transitMs = parseIsoMs(transit.at);
  if (trackMs == null || transitMs == null || transitMs >= trackMs) return nodes;

  const swapped = [...nodes];
  swapped[trackIdx] = transit;
  swapped[transitIdx] = track;
  return swapped;
};

/**
 * Chuỗi mốc để render timeline theo đúng lộ trình thời gian:
 * - Đơn thường: 5 mốc cố định (Đặt → Mua → Track → Transit → Giao), riêng cặp Track/Transit xếp
 *   theo thứ tự xảy ra thật. Mỗi node mang cả `reached` (đã tới chặng, suy từ trạng thái) và `at`
 *   (mốc thời gian thật, có thể trống).
 * - Đơn đã Hủy/Refund: chỉ giữ các mốc đã có thời gian rồi nối thẳng node kết thúc
 *   (Refund ưu tiên hơn Hủy) — không hiển thị Transit/Giao trống.
 */
const buildFulfilTimeline = (
  order: OrderListItem,
): { nodes: FulfilTimelineNode[]; terminal: boolean } => {
  const reached = computeFulfilReached(order);
  const terminalKey: FulfilMilestoneKey | null = order.refundedAt
    ? 'refundedAt'
    : order.canceledAt
      ? 'canceledAt'
      : null;
  if (!terminalKey) {
    return {
      nodes: orderTrackTransitChronologically(
        HEADER_FULFIL_MILESTONES.map((m) => ({
          ...m,
          at: order[m.key],
          reached: reached[m.key],
          terminal: false,
        })),
      ),
      terminal: false,
    };
  }
  const doneBase = orderTrackTransitChronologically(
    HEADER_FULFIL_MILESTONES.filter((m) => !!order[m.key]).map((m) => ({
      ...m,
      at: order[m.key],
      reached: reached[m.key],
      terminal: false,
    })),
  );
  const term = FULFIL_MILESTONES.find((m) => m.key === terminalKey)!;
  return {
    nodes: [...doneBase, { ...term, at: order[terminalKey], reached: true, terminal: true }],
    terminal: true,
  };
};

/** Slot cố định trên header desktop — timeline/actions neo phải, chấm Đặt thẳng hàng giữa các card. */
const ORDER_HEADER_TIMELINE_WIDTH = 420;
const ORDER_HEADER_ACTIONS_WIDTH = 220;

/** Cam nhạt cho carrier phụ (GOFO/FedEx…) — không cạnh tranh với USPS đỏ/xanh. */
const CARRIER_BADGE_MUTED_FG = '#b45309';
const CARRIER_BADGE_MUTED_BG = '#fff7ed';
const CARRIER_BADGE_MUTED_BORDER = '#fdba74';
/** Slot cố định cho mọi badge carrier (USPS/Kho/GOFO…) — tránh nhảy layout giữa các card. */
const CARRIER_BADGE_SLOT_WIDTH = 56;

/**
 * Tạm khóa click badge USPS (đỏ/xanh) — đang kiểm chứng luồng auto scan.
 * Đặt lại `true` khi muốn bật toggle thủ công.
 */
const USPS_BADGE_MANUAL_TOGGLE_ENABLED = false;
/** Slot hẹp cho badge xử lý REF. */
const PROCESS_BADGE_SLOT_WIDTH = 36;
/** Slot cho badge ThankCard viết tắt TKS. */
const THANK_BADGE_SLOT_WIDTH = 36;

const isUspsCarrierLabel = (label: string) => label.toLowerCase().includes('usps');

/**
 * Tên carrier last-mile để hiện badge header.
 * Ưu tiên: carrier trên track/link → ShippingService (không phải Seller/TikTok) →
 * fallback "Track" khi đã có mã last-mile nhưng chưa có tên hãng.
 */
const resolveOrderCarrierLabel = (order: OrderListItem): string | null => {
  const tracking = resolveOrderTracking(order);
  const links = order.supplierLinks ?? [];

  const named =
    tracking?.carrier?.trim() ||
    links.find(
      (l) =>
        (l.trackingKind || '').toLowerCase() === 'carrier' && !!l.carrier?.trim(),
    )?.carrier?.trim() ||
    links.find((l) => !!l.carrier?.trim())?.carrier?.trim() ||
    null;

  if (named) return named.replace(/\.+$/, '').trim() || named;

  const service = (order.shippingService || '').trim();
  if (
    service &&
    !resolveShippingMethodKind(null, service) &&
    !service.toLowerCase().includes('warehouse')
  ) {
    return service.replace(/\.+$/, '').trim() || service;
  }

  if (tracking?.kind === 'carrier' && tracking.trackingNo) return 'Track';
  if (
    links.some(
      (l) =>
        (l.trackingKind || '').toLowerCase() === 'carrier' && !!l.trackingNo?.trim(),
    )
  ) {
    return 'Track';
  }

  return null;
};

const profitColor = (profit: number | null) => {
  if (profit == null) return 'var(--order-text-tertiary)';
  if (profit < 0) return '#ff4d4f';
  if (profit < 10) return '#fa8c16';
  return '#389e0d';
};

/** Header finance: Cost → Rev → Pft (tabular, profit nổi). Label qua tooltip. */
const OrderFinanceStrip = ({
  revenue,
  cost,
  currency,
}: {
  revenue: number;
  cost: number | null | undefined;
  currency?: string | null;
}) => {
  const profit = cost != null ? revenue - cost : null;
  const code = (currency || 'USD').trim().toUpperCase() || 'USD';

  return (
    <Tooltip
      title={
        <div style={{ fontVariantNumeric: 'tabular-nums', lineHeight: 1.55 }}>
          <div>Chi mua hàng (Cost = gross NCC + BuyTrack/ThankCard): {cost != null ? `$${formatAmount(cost)}` : '—'}</div>
          <div>Thực nhận (Rev): ${formatAmount(revenue)} {code}</div>
          <div>
            Profit = Rev − Cost:{' '}
            {profit != null ? `$${formatAmount(profit)}` : '—'}
          </div>
        </div>
      }
    >
      <div
        role="group"
        aria-label="Chi phí, thực nhận, profit"
        style={{
          display: 'flex',
          alignItems: 'baseline',
          gap: 4,
          fontVariantNumeric: 'tabular-nums',
          lineHeight: 1,
          cursor: 'default',
          userSelect: 'none',
          whiteSpace: 'nowrap',
        }}
      >
        <span style={{ fontSize: 11, fontWeight: 500, color: 'var(--order-cost)' }}>
          {cost != null ? `$${formatAmount(cost)}` : '—'}
        </span>
        <span style={{ fontSize: 10, color: 'var(--order-text-quaternary)' }}>→</span>
        <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--order-money)' }}>
          ${formatAmount(revenue)}
        </span>
        <span
          style={{
            fontSize: 12,
            fontWeight: 700,
            color: profitColor(profit),
          }}
        >
          {profit != null ? `${profit > 0 ? '+' : ''}$${formatAmount(profit)}` : '—'}
        </span>
      </div>
    </Tooltip>
  );
};

/** Mã kho SHEIN (C…/N…) khi chưa có carrier last-mile — dùng tip badge Kho. */
const resolveOrderWarehouseTrackingNo = (order: OrderListItem): string | null => {
  const tracking = resolveOrderTracking(order);
  if (
    tracking &&
    (tracking.kind === 'warehouse' ||
      isWarehouseTrackingNo(tracking.trackingNo, order.shippingService))
  ) {
    return tracking.trackingNo;
  }
  for (const l of order.supplierLinks ?? []) {
    const preserved = l.warehouseTrackingNo?.trim();
    if (preserved) return preserved;
    const no = l.trackingNo?.trim();
    if (!no) continue;
    if ((l.trackingKind || '').toLowerCase() === 'warehouse') return no;
    if (isWarehouseTrackingNo(no, l.carrier)) return no;
  }
  return null;
};

/** Badge carrier đầu identity — slot cố định + `|` tách mã đơn (USPS đỏ/xanh; Kho / carrier khác cam nhạt). */
const CarrierProcessBadge = ({
  order,
  canManage,
  onToggleUspsProcessed,
}: {
  order: OrderListItem;
  canManage: boolean;
  onToggleUspsProcessed?: ToggleUspsProcessedFn;
}) => {
  const raw = resolveOrderCarrierLabel(order);
  const warehouseNo = !raw ? resolveOrderWarehouseTrackingNo(order) : null;
  const isWarehouse = !raw && !!warehouseNo;
  const label = isWarehouse
    ? 'Kho'
    : raw?.replace(/\.+$/, '').trim() || raw;
  const isUsps = !!label && !isWarehouse && isUspsCarrierLabel(label);
  const processed = !!order.uspsProcessedAt;
  const uspsToggleEnabled =
    USPS_BADGE_MANUAL_TOGGLE_ENABLED && canManage && !!onToggleUspsProcessed;
  const color = isUsps ? (processed ? 'success' : 'error') : undefined;
  const style = isUsps
    ? {
        margin: 0,
        width: CARRIER_BADGE_SLOT_WIDTH,
        boxSizing: 'border-box' as const,
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontWeight: 600,
        lineHeight: '18px',
        flexShrink: 0,
        borderWidth: 1,
        borderStyle: 'solid' as const,
        borderColor: processed ? '#389e0d' : '#cf1322',
        cursor: uspsToggleEnabled ? 'pointer' : 'default',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap' as const,
      }
    : {
        margin: 0,
        width: CARRIER_BADGE_SLOT_WIDTH,
        boxSizing: 'border-box' as const,
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        lineHeight: '18px',
        fontWeight: 500,
        flexShrink: 0,
        color: CARRIER_BADGE_MUTED_FG,
        background: CARRIER_BADGE_MUTED_BG,
        borderColor: CARRIER_BADGE_MUTED_BORDER,
        cursor: 'default',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap' as const,
      };

  const tracking = resolveOrderTracking(order);
  const tip = !label
    ? undefined
    : isWarehouse
      ? `Kho SHEIN · ${warehouseNo}`
      : isUsps
        ? processed
          ? `USPS đã xử lý${order.uspsProcessedAt ? ` · ${formatDateTimeVn(order.uspsProcessedAt)}` : ''}${uspsToggleEnabled ? ' — click để bỏ đánh dấu' : ' — chỉ đọc (auto scan)'}`
          : `USPS chưa xử lý${uspsToggleEnabled ? ' — click để đánh dấu đã xử lý' : ' — chỉ đọc (auto scan)'}`
        : [label, tracking?.trackingNo].filter(Boolean).join(' · ');

  const onClick = () => {
    if (!isUsps || !uspsToggleEnabled || !onToggleUspsProcessed) return;
    const next = !processed;
    const hadScanBatch = !!order.uspsScanBatchId;
    Modal.confirm({
      title: next ? 'Đánh dấu đã xử lý USPS?' : 'Bỏ đánh dấu xử lý USPS?',
      content: `Đơn ${order.platformOrderId || order.fourSellerOrderId}${
        next
          ? ' — cộng phí BuyTrack vào chi phí mua hàng / sổ cái'
          : hadScanBatch
            ? ` — gỡ phí BuyTrack về 0 và xóa liên kết batch #${order.uspsScanBatchId} (tiền PrintPackShip đã trừ, không hoàn)`
            : ' — gỡ phí BuyTrack về 0'
      }`,
      okText: 'Xác nhận',
      cancelText: 'Hủy',
      centered: true,
      onOk: () => onToggleUspsProcessed(order, next),
    });
  };

  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        flexShrink: 0,
        gap: 6,
        height: '100%',
      }}
    >
      <span
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: CARRIER_BADGE_SLOT_WIDTH,
          flexShrink: 0,
        }}
      >
        {label ? (
          <Tooltip title={tip}>
            <Tag
              color={color}
              style={style}
              title={isWarehouse ? `Kho SHEIN · ${warehouseNo}` : undefined}
              onClick={(e) => {
                e.stopPropagation();
                onClick();
              }}
            >
              {label}
            </Tag>
          </Tooltip>
        ) : null}
      </span>
      <span
        aria-hidden
        style={{
          color: 'var(--order-text-quaternary)',
          fontSize: 12,
          lineHeight: 1,
          userSelect: 'none',
          flexShrink: 0,
        }}
      >
        |
      </span>
    </span>
  );
};

/** True when ThankCard stamped, or warehouse track còn việc (chưa last-mile / mark ship). */
const orderShowsThankCardBadge = (order: OrderListItem) => {
  if (order.thankCardProcessedAt) return true;
  const platform = (order.platformOrderStatus || '').toUpperCase();
  if (
    platform === 'AWAITING_COLLECTION' ||
    platform === 'IN_TRANSIT' ||
    platform === 'DELIVERED'
  ) {
    return false;
  }
  const links = order.supplierLinks ?? [];
  const hasCarrier =
    links.some((l) => (l.trackingKind || '').toLowerCase() === 'carrier' && !!l.trackingNo?.trim()) ||
    (!!order.trackingNo?.trim() &&
      !/^[CN]\d{10,}$/i.test(order.trackingNo.trim()) &&
      !(order.shippingService || '').toLowerCase().includes('warehouse'));
  if (hasCarrier) return false;

  const tracking = resolveOrderTracking(order);
  if (tracking?.kind === 'warehouse') return true;
  if (tracking && isWarehouseTrackingNo(tracking.trackingNo, order.shippingService)) return true;
  return links.some((l) => {
    if (l.warehouseTrackingNo?.trim()) return true;
    const no = l.trackingNo?.trim();
    if (!no) return false;
    if ((l.trackingKind || '').toLowerCase() === 'warehouse') return true;
    return isWarehouseTrackingNo(no, l.carrier);
  });
};

/** Badge ThankCard — đỏ chưa mua / xanh đã mua; click toggle như USPS. */
const ThankCardProcessBadge = ({
  order,
  canManage,
  onToggleThankCardProcessed,
}: {
  order: OrderListItem;
  canManage: boolean;
  onToggleThankCardProcessed?: ToggleThankCardProcessedFn;
}) => {
  if (!orderShowsThankCardBadge(order)) return null;

  const processed = !!order.thankCardProcessedAt;
  const warehouseNo = resolveOrderWarehouseTrackingNo(order);
  const tip = processed
    ? `ThankCard đã mua${order.thankCardProcessedAt ? ` · ${formatDateTimeVn(order.thankCardProcessedAt)}` : ''}${
        order.thankCardAmount ? ` · $${order.thankCardAmount}` : ''
      }${warehouseNo ? ` · Kho ${warehouseNo}` : ''}${canManage ? ' — click để bỏ đánh dấu' : ''}`
    : `Chưa mua ThankCard${warehouseNo ? ` · Kho ${warehouseNo}` : ''}${canManage ? ' — click để đánh dấu đã mua' : ''}`;

  const onClick = () => {
    if (!canManage || !onToggleThankCardProcessed) return;
    const next = !processed;
    Modal.confirm({
      title: next ? 'Đánh dấu đã mua ThankCard?' : 'Bỏ đánh dấu mua ThankCard?',
      content: `Đơn ${order.platformOrderId || order.fourSellerOrderId}${
        next ? ' — cộng phí ThankCard vào chi phí mua hàng / sổ cái' : ' — gỡ phí ThankCard về 0'
      }`,
      okText: 'Xác nhận',
      cancelText: 'Hủy',
      onOk: () => onToggleThankCardProcessed(order, next),
    });
  };

  return (
    <Tooltip title={tip}>
      <Tag
        color={processed ? 'success' : 'error'}
        style={{
          margin: 0,
          width: THANK_BADGE_SLOT_WIDTH,
          boxSizing: 'border-box',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontWeight: 600,
          lineHeight: '18px',
          flexShrink: 0,
          borderWidth: 1,
          borderStyle: 'solid',
          borderColor: processed ? '#389e0d' : '#cf1322',
          cursor: canManage && onToggleThankCardProcessed ? 'pointer' : 'default',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
        }}
        onClick={(e) => {
          e.stopPropagation();
          onClick();
        }}
      >
        TKS
      </Tag>
    </Tooltip>
  );
};

/** True when order is TikTok REFUND or CANCELLED (workflow or platform status). */
const orderShowsRefundBadge = (order: OrderListItem) => {
  if (order.refundProcessedAt) return true;
  const tab = (order.workflowTab || '').toUpperCase();
  if (tab === 'REFUND' || tab === 'CANCELLED') return true;
  const platform = (order.platformOrderStatus || '').toUpperCase();
  return (
    platform === 'REFUND' ||
    platform === 'REFUNDED' ||
    platform === 'CANCELLED' ||
    platform === 'CANCELED'
  );
};

const orderIsCancelKind = (order: OrderListItem) => {
  const tab = (order.workflowTab || '').toUpperCase();
  if (tab === 'CANCELLED') return true;
  const platform = (order.platformOrderStatus || '').toUpperCase();
  return platform === 'CANCELLED' || platform === 'CANCELED';
};

/** Queue handling cho menu Done — chỉ tab chờ USPS / Thank Card / Refund chưa stamp. */
type HandlingDoneQueue = 'usps' | 'thankCard' | 'refund';

type ToggleUspsProcessedFn = (
  order: OrderListItem,
  isUspsProcessed: boolean,
  options?: { applyFee?: boolean },
) => void;

type ToggleThankCardProcessedFn = (
  order: OrderListItem,
  isThankCardProcessed: boolean,
  options?: { applyFee?: boolean },
) => void;

const resolveHandlingDoneQueue = (
  listTab: string | null | undefined,
  order: OrderListItem,
): HandlingDoneQueue | null => {
  const tab = (listTab || '').toUpperCase();
  if (tab === 'USPS_UNPROCESSED' && !order.uspsProcessedAt) return 'usps';
  if (tab === 'THANK_CARD_PENDING' && !order.thankCardProcessedAt) return 'thankCard';
  if (
    (tab === 'REFUND_QUEUE_UNPROCESSED' || tab === 'REFUND_QUEUE_ALL') &&
    !order.refundProcessedAt
  ) {
    return 'refund';
  }
  return null;
};

const confirmMarkHandlingDone = (
  order: OrderListItem,
  queue: HandlingDoneQueue,
  handlers: {
    onToggleUspsProcessed?: ToggleUspsProcessedFn;
    onToggleThankCardProcessed?: ToggleThankCardProcessedFn;
    onToggleRefundProcessed?: (order: OrderListItem, isRefundProcessed: boolean) => void;
  },
) => {
  const orderLabel = order.platformOrderId || order.fourSellerOrderId;
  if (queue === 'usps') {
    if (!handlers.onToggleUspsProcessed) return;
    Modal.confirm({
      title: 'Đánh dấu đã xử lý USPS?',
      content: `Đơn ${orderLabel} — chỉ đánh dấu đã xử lý, không cộng phí BuyTrack`,
      okText: 'Xác nhận',
      cancelText: 'Hủy',
      onOk: () => handlers.onToggleUspsProcessed?.(order, true, { applyFee: false }),
    });
    return;
  }
  if (queue === 'thankCard') {
    if (!handlers.onToggleThankCardProcessed) return;
    Modal.confirm({
      title: 'Đánh dấu đã xử lý ThankCard?',
      content: `Đơn ${orderLabel} — chỉ đánh dấu đã xử lý, không cộng phí ThankCard`,
      okText: 'Xác nhận',
      cancelText: 'Hủy',
      onOk: () => handlers.onToggleThankCardProcessed?.(order, true, { applyFee: false }),
    });
    return;
  }
  if (!handlers.onToggleRefundProcessed) return;
  const kindLabel = orderIsCancelKind(order) ? 'Đơn hủy' : 'Refund';
  Modal.confirm({
    title: `Đánh dấu đã xử lý ${kindLabel}?`,
    content: `Đơn ${orderLabel}`,
    okText: 'Xác nhận',
    cancelText: 'Hủy',
    onOk: () => handlers.onToggleRefundProcessed?.(order, true),
  });
};

/** Badge REFUND/HỦY — đỏ chờ xử lý / xanh đã xử lý; click toggle như USPS. */
const RefundProcessBadge = ({
  order,
  canManage,
  onToggleRefundProcessed,
}: {
  order: OrderListItem;
  canManage: boolean;
  onToggleRefundProcessed?: (order: OrderListItem, isRefundProcessed: boolean) => void;
}) => {
  if (!orderShowsRefundBadge(order)) return null;

  const isCancel = orderIsCancelKind(order);
  const label = isCancel ? 'HỦY' : 'REF';
  const kindLabel = isCancel ? 'Đơn hủy' : 'Refund';
  const processed = !!order.refundProcessedAt;
  const tip = processed
    ? `${kindLabel} đã xử lý${order.refundProcessedAt ? ` · ${formatDateTimeVn(order.refundProcessedAt)}` : ''}${
        canManage ? ' — click để bỏ đánh dấu' : ''
      }`
    : `${kindLabel} chờ xử lý${canManage ? ' — click để đánh dấu đã xử lý' : ''}`;

  const onClick = () => {
    if (!canManage || !onToggleRefundProcessed) return;
    const next = !processed;
    Modal.confirm({
      title: next ? `Đánh dấu đã xử lý ${kindLabel}?` : `Bỏ đánh dấu xử lý ${kindLabel}?`,
      content: `Đơn ${order.platformOrderId || order.fourSellerOrderId}`,
      okText: 'Xác nhận',
      cancelText: 'Hủy',
      onOk: () => onToggleRefundProcessed(order, next),
    });
  };

  return (
    <Tooltip title={tip}>
      <Tag
        color={processed ? 'success' : 'error'}
        style={{
          margin: 0,
          width: PROCESS_BADGE_SLOT_WIDTH,
          boxSizing: 'border-box',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontWeight: 600,
          lineHeight: '18px',
          flexShrink: 0,
          borderWidth: 1,
          borderStyle: 'solid',
          borderColor: processed ? '#389e0d' : '#cf1322',
          cursor: canManage && onToggleRefundProcessed ? 'pointer' : 'default',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
        }}
        onClick={(e) => {
          e.stopPropagation();
          onClick();
        }}
      >
        {label}
      </Tag>
    </Tooltip>
  );
};

/**
 * Compact fulfil strip — list payload only.
 * - `inline`: desktop header (max 420px)
 * - `stretch`: mobile full-width, Đặt sát trái · Giao sát phải
 */
const CompactFulfilTimeline = ({
  order,
  nowMs,
  inline,
  stretch,
}: {
  order: OrderListItem;
  nowMs: number;
  inline?: boolean;
  stretch?: boolean;
}) => {
  const { nodes: milestones, terminal } = buildFulfilTimeline(order);
  // Frontier = chặng chưa tới đầu tiên sau mốc cuối đã đạt (trạng thái "đang chờ" + gap sống).
  const currentIdx = terminal ? -1 : findFulfilFrontierIdx(milestones);
  // Đơn Hủy/Refund có số node động → dựng grid theo số node hiện có.
  const gridTemplateColumns = milestones.map(() => 'auto').join(' minmax(12px, 1fr) ');

  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns,
        alignItems: 'center',
        columnGap: stretch ? 4 : 5,
        width: '100%',
        maxWidth: stretch ? undefined : inline ? ORDER_HEADER_TIMELINE_WIDTH : undefined,
        height: 20,
        boxSizing: 'border-box',
      }}
      aria-label="Quy trình fulfil"
    >
      {milestones.map((m, idx) => {
        const at = m.at;
        const dated = !!at; // có mốc thời gian thật
        const reached = m.reached;
        const isTerminal = m.terminal;
        const isCurrent = !reached && idx === currentIdx;
        const isFirst = idx === 0;
        const isLast = idx === milestones.length - 1;
        const next = milestones[idx + 1];
        const fromMs = parseIsoMs(at);
        const nextMs = next ? parseIsoMs(next.at) : null;
        // Gap chỉ hiển thị khi cả hai đầu có mốc thật (không bịa "—"/"<1m").
        let gapLabel: string | null = null;
        let gapLive = false;
        if (!isLast && fromMs != null) {
          if (nextMs != null && nextMs >= fromMs) {
            gapLabel = formatDurationShort(fromMs, nextMs);
          } else if (!terminal && !!next && !next.at && currentIdx === idx + 1) {
            gapLabel = formatDurationShort(fromMs, nowMs);
            gapLive = true;
          }
        }

        const terminalDotColor = m.key === 'refundedAt' ? '#eb2f96' : '#ff4d4f';
        const terminalTextColor = m.key === 'refundedAt' ? '#c41d7f' : '#cf1322';

        const fulfilAccountTip =
          m.key === 'fulfilPurchasedAt' && order.fulfilAccountName?.trim()
            ? ` · TK ${order.fulfilAccountName.trim()}`
            : '';
        const tip = dated
          ? m.key === 'trackingAddedAt'
            ? `${m.label} (4Seller công bố mã / Mark Ship): ${formatDateTime(at)}`
            : m.key === 'inTransitAt'
              ? `${m.label} (SHEIN delivery_time): ${formatDateTime(at)}`
              : m.key === 'fulfilPurchasedAt'
                ? `${m.label}${fulfilAccountTip}: ${formatDateTime(at)}`
                : `${m.label}: ${formatDateTime(at)}`
          : reached
            ? m.key === 'fulfilPurchasedAt'
              ? `${m.label}${fulfilAccountTip}: đã tới chặng (chưa có mốc thời gian chính xác)`
              : `${m.label}: đã tới chặng (chưa có mốc thời gian chính xác)`
            : m.key === 'trackingAddedAt'
              ? `${m.label}: chưa có mã carrier (mã kho SHEIN C…/N… không tính)`
              : isCurrent
                ? `${m.label}${fulfilAccountTip}: đang chờ`
                : `${m.label}${fulfilAccountTip}`;

        // Trạng thái chấm: có mốc = tô đặc; đã tới nhưng chưa có mốc = vòng rỗng xanh;
        // đang chờ = xanh dương; tương lai = xám.
        const accent = isTerminal ? terminalDotColor : '#52c41a';
        const dotBg = dated ? accent : isCurrent ? '#1677ff' : 'var(--order-dot-empty-bg)';
        const dotBorder = dated
          ? accent
          : reached
            ? accent
            : isCurrent
              ? '#1677ff'
              : 'var(--order-dot-empty-border)';
        const labelColor = dated
          ? isTerminal
            ? terminalTextColor
            : 'var(--order-text-secondary)'
          : reached
            ? 'var(--order-text-secondary)'
            : isCurrent
              ? '#1677ff'
              : 'var(--order-text-quaternary)';
        const lineColor =
          !!next && !!next.at && fromMs != null
            ? next.terminal
              ? next.key === 'refundedAt'
                ? '#ffadd2'
                : '#ffccc7'
              : '#b7eb8f'
            : gapLive
              ? '#91caff'
              : next && next.reached
                ? '#d9f7be'
                : 'var(--order-timeline-pending)';

        return (
          <Fragment key={m.key}>
            <Tooltip title={tip}>
              <span
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 3,
                  whiteSpace: 'nowrap',
                  cursor: 'default',
                  justifySelf: stretch ? (isFirst ? 'start' : isLast ? 'end' : 'center') : undefined,
                }}
              >
                <span
                  aria-hidden
                  style={{
                    width: 7,
                    height: 7,
                    borderRadius: '50%',
                    flexShrink: 0,
                    background: dotBg,
                    border: `1.5px solid ${dotBorder}`,
                    boxSizing: 'border-box',
                    boxShadow: isCurrent ? '0 0 0 3px rgba(22,119,255,0.15)' : undefined,
                  }}
                />
                <Text
                  style={{
                    fontSize: 11,
                    lineHeight: 1,
                    color: labelColor,
                    fontWeight: dated || reached || isCurrent ? 600 : 400,
                  }}
                >
                  {m.short}
                  {reached && !dated ? (
                    <span style={{ opacity: 0.6, marginLeft: 1 }}>?</span>
                  ) : null}
                </Text>
              </span>
            </Tooltip>
            {!isLast ? (
              <span
                aria-hidden
                style={{
                  position: 'relative',
                  height: 2,
                  minWidth: 0,
                  borderRadius: 1,
                  background: lineColor,
                }}
              >
                {gapLabel ? (
                  <span
                    style={{
                      position: 'absolute',
                      left: '50%',
                      top: '50%',
                      transform: 'translate(-50%, -50%)',
                      padding: '0 2px',
                      background: 'var(--order-timeline-gap-bg)',
                      fontSize: 11,
                      lineHeight: 1,
                      fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
                      fontVariantNumeric: 'tabular-nums',
                      fontWeight: gapLive ? 600 : 400,
                      color: gapLive ? '#1677ff' : 'var(--order-text-tertiary)',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    {gapLabel}
                  </span>
                ) : null}
              </span>
            ) : null}
          </Fragment>
        );
      })}
    </div>
  );
};

const OrderDetailBody = ({
  detail,
  ratingSuccessMin,
  ratingWarningMin,
  showOwnerName = false,
}: {
  detail: OrderListItem & { items?: OrderItem[] };
  ratingSuccessMin?: number;
  ratingWarningMin?: number;
  showOwnerName?: boolean;
}) => {
  const nowMs = useSharedNow(orderNeedsLiveFulfilTick(detail));
  const orderId = detail.platformOrderId || String(detail.fourSellerOrderId);
  const items = detail.items || detail.lineItems || [];
  const address = [detail.address, detail.city, detail.state, detail.postalCode, detail.countryCode]
    .filter(Boolean)
    .join(', ');
  const notes = [
    { key: 'buyer', label: 'Buyer', color: 'var(--order-note-buyer-fg)', bg: 'var(--order-note-buyer-bg)', text: detail.buyerMemo },
    { key: 'seller', label: 'Seller', color: 'var(--order-note-seller-fg)', bg: 'var(--order-note-seller-bg)', text: detail.sellerMemo },
    { key: 'internal', label: 'Internal', color: 'var(--order-note-internal-fg)', bg: 'var(--order-note-internal-bg)', text: detail.internalMemo },
  ].filter((n) => typeof n.text === 'string' && n.text.trim().length > 0);

  const shopBits = [
    showOwnerName ? detail.ownerName : null,
    detail.shopName,
    detail.shopProfileName,
  ].filter(Boolean);
  const fulfilBits = [
    detail.fulfilAccountName,
    detail.fulfilCardNumberMasked || detail.fulfilCardName,
  ].filter(Boolean);

  // Các số Supplier* của đơn là TỔNG của những billno còn hiệu lực (huỷ/hoàn không được cộng),
  // nên chỉ chúng mới đối chiếu được với khối tổng ở trên.
  const payableSupplierLinks = (detail.supplierLinks ?? []).filter((l) => {
    const status = (l.supplierStatus || '').toLowerCase();
    if (status === 'canceled' || status === 'cancelled' || status === 'refunded') return false;
    if (l.isCharged === false) return false;
    if (l.supplierUnpaid === true) return false;
    return true;
  });
  const purchaseLinks: SupplierPurchaseLink[] = payableSupplierLinks.map((l) => ({
    supplierOrderNo: l.supplierOrderNo,
    supplierOrderUrl: l.supplierOrderUrl,
    supplierStatus: l.supplierStatus,
    supplierUnpaid: l.supplierUnpaid,
    supplierRetailPrice: l.supplierRetailPrice,
    supplierSubtotal: l.supplierSubtotal,
    supplierShippingFee: l.supplierShippingFee,
    supplierRetailDeliveryFee: l.supplierRetailDeliveryFee,
    supplierOnTimeGuarantee: l.supplierOnTimeGuarantee,
    supplierCouponPrice: l.supplierCouponPrice,
    supplierMembershipDiscount: l.supplierMembershipDiscount,
    supplierPointsDiscount: l.supplierPointsDiscount,
    supplierWalletPrice: l.supplierWalletPrice,
    supplierTax: l.supplierTax,
    supplierTotal: l.supplierTotal,
    trackingNo: l.trackingNo,
    carrier: l.carrier,
    trackingKind: l.trackingKind,
    fallbackCarrier: detail.shippingService,
  }));
  const purchaseIncomplete =
    purchaseLinks.length > 0
      ? purchaseLinks.some((l) => !isSupplierCostDetailComplete(l))
      : detail.supplierSubtotal == null || detail.totalCost == null;
  const singleLink = purchaseLinks.length === 1 ? purchaseLinks[0] : undefined;

  return (
    <div className="order-detail-body">
      {/* Header: tags + mã đơn · meta · stats */}
      <div className="order-detail-section order-detail-section--surface order-detail-hero">
        <div className="order-detail-hero-top">
          <div className="order-detail-hero-tags">
            {detail.platform ? (
              <Tag style={{ margin: 0, textTransform: 'uppercase' }}>{detail.platform}</Tag>
            ) : null}
            <Tag
              style={{
                margin: 0,
                color: '#fff',
                border: 'none',
                backgroundColor: statusColor(platformStatus(detail)),
              }}
            >
              {statusLabel(platformStatus(detail))}
            </Tag>
            {isCancelOrRefundStatus(platformStatus(detail)) && orderCancelReason(detail) ? (
              <Tooltip title={orderCancelReason(detail)!}>
                <Text type="danger" className="order-detail-cancel-reason">
                  {orderCancelReason(detail)}
                </Text>
              </Tooltip>
            ) : null}
            {isCancelOrRefundStatus(platformStatus(detail)) && orderReturnStatus(detail) ? (
              <Tooltip title={orderReturnStatus(detail)!}>
                <Text className="order-detail-return-status" style={{ color: '#faad14' }}>
                  {orderReturnStatus(detail)}
                </Text>
              </Tooltip>
            ) : null}
            {detail.isSettlementPaid ? (
              <Tag color="success" style={{ margin: 0 }}>
                Đã đối soát
              </Tag>
            ) : null}
          </div>
          <div className="order-detail-hero-id-row">
            <Text strong className="order-detail-hero-id">
              {orderId}
            </Text>
            <Tooltip title={orderId}>
              <Button
                type="text"
                size="small"
                icon={<CopyOutlined />}
                onClick={() => void copyOrderId(orderId)}
                aria-label="Copy mã đơn"
                style={{ flexShrink: 0 }}
              />
            </Tooltip>
          </div>
        </div>

        {(shopBits.length > 0 || fulfilBits.length > 0) ? (
          <div className="order-detail-meta">
            {showOwnerName && detail.ownerName ? (
              <div className="order-detail-meta-item">
                <span className="order-detail-meta-label">Seller</span>
                <span className="order-detail-meta-value" title={detail.ownerName}>
                  {detail.ownerName}
                </span>
              </div>
            ) : null}
            {detail.shopName ? (
              <div className="order-detail-meta-item">
                <span className="order-detail-meta-label">Shop</span>
                <span className="order-detail-meta-value" title={detail.shopName}>
                  {detail.shopName}
                </span>
              </div>
            ) : null}
            {detail.shopProfileName ? (
              <div className="order-detail-meta-item">
                <span className="order-detail-meta-label">Profile</span>
                <span className="order-detail-meta-value" title={detail.shopProfileName}>
                  {detail.shopProfileName}
                </span>
              </div>
            ) : null}
            {detail.fulfilAccountName ? (
              <div className="order-detail-meta-item">
                <span className="order-detail-meta-label">TK</span>
                <span className="order-detail-meta-value" title={detail.fulfilAccountName}>
                  {detail.fulfilAccountName}
                </span>
              </div>
            ) : null}
            {(detail.fulfilCardNumberMasked || detail.fulfilCardName) ? (
              <div className="order-detail-meta-item">
                <span className="order-detail-meta-label">Thẻ</span>
                <span
                  className="order-detail-meta-value"
                  title={detail.fulfilCardNumberMasked || detail.fulfilCardName || undefined}
                >
                  {detail.fulfilCardNumberMasked || detail.fulfilCardName}
                </span>
              </div>
            ) : null}
          </div>
        ) : null}

        <div className="order-detail-stats">
          <DetailField label="Thực nhận" className="order-detail-stat--money">
            {formatMoney(resolveOrderRevenue(detail), detail.currency)}
          </DetailField>
          <DetailField label="Ordered">{formatDateTime(detail.orderCreateTime)}</DetailField>
          <DetailField label="Paid">{formatDateTime(detail.orderPaidTime)}</DetailField>
          <DetailField label="Leave">{formatDateTime(detail.latestShipDate)}</DetailField>
        </div>
      </div>

      <div className="order-detail-panels">
        <div className="order-detail-section" style={{ height: '100%' }}>
          {/* Shared với Fulfil hover — header = TrueSpend NCC (không gồm BuyTrack/ThankCard). */}
          <SupplierPurchaseSection
            headerTotal={settledTrueSpend({
              supplierTotal: detail.supplierTotal,
              supplierWalletPrice: detail.supplierWalletPrice,
              supplierMembershipDiscount: detail.supplierMembershipDiscount,
            })}
            values={{
              supplierRetailPrice: detail.supplierRetailPrice,
              supplierSubtotal: detail.supplierSubtotal,
              supplierShippingFee: detail.supplierShippingFee,
              supplierRetailDeliveryFee: detail.supplierRetailDeliveryFee,
              supplierOnTimeGuarantee: detail.supplierOnTimeGuarantee,
              supplierCouponPrice: detail.supplierCouponPrice,
              supplierMembershipDiscount: detail.supplierMembershipDiscount,
              supplierPointsDiscount: detail.supplierPointsDiscount,
              supplierWalletPrice: detail.supplierWalletPrice,
              supplierTax: detail.supplierTax,
              supplierTotal: detail.supplierTotal,
            }}
            incomplete={purchaseIncomplete}
            multiLinks={purchaseLinks.length > 1 ? purchaseLinks : undefined}
            multiCount={purchaseLinks.length}
            tracking={
              singleLink
                ? {
                    trackingNo: singleLink.trackingNo || detail.trackingNo,
                    carrier: singleLink.carrier,
                    trackingKind: singleLink.trackingKind,
                    fallbackCarrier: detail.shippingService,
                  }
                : purchaseLinks.length === 0
                  ? {
                      trackingNo: detail.trackingNo,
                      carrier: null,
                      trackingKind: null,
                      fallbackCarrier: detail.shippingService,
                    }
                  : null
            }
          />
          {(detail.buyTrackAmount || detail.thankCardAmount) ? (
            <div style={{ marginTop: 8, fontSize: 12, color: 'var(--order-text-secondary)' }}>
              Phí xử lý
              {detail.buyTrackAmount ? ` · BuyTrack $${Number(detail.buyTrackAmount).toFixed(2)}` : ''}
              {detail.thankCardAmount ? ` · ThankCard $${Number(detail.thankCardAmount).toFixed(2)}` : ''}
              {' · '}
              Cost{' '}
              {detail.totalCost != null ? `$${Number(detail.totalCost).toFixed(2)}` : '—'}
            </div>
          ) : null}
        </div>

        {detail.pricingDetailSyncedAt ? (
          <div className="order-detail-section" style={{ height: '100%' }}>
            {/* Cùng layout/font với Mua hàng — chỉ hiện field > 0. */}
            <section className="supplier-purchase-section">
              <div className="supplier-purchase-section-head">
                <Text strong className="supplier-purchase-section-title">
                  {(() => {
                    const ship = formatShippingMethod(
                      detail.shippingMethod || detail.shippingService,
                    );
                    return ship !== '—' ? `Doanh thu (${ship})` : 'Doanh thu';
                  })()}
                </Text>
                <Text strong className="supplier-purchase-sell">
                  ${formatAmount(resolveOrderRevenue(detail))}
                </Text>
              </div>
              <div className="supplier-purchase-grid">
                {(
                  [
                    {
                      key: 'before',
                      label: 'Giá trước giảm',
                      value: detail.productAmountBeforeDiscount,
                    },
                    {
                      key: 'platDisc',
                      label: 'Nền tảng giảm',
                      value: detail.productPlatformDiscount,
                      discount: true,
                    },
                    {
                      key: 'sellerDisc',
                      label: 'Seller giảm',
                      value: detail.productSellerDiscount,
                      discount: true,
                    },
                    {
                      key: 'shipBefore',
                      label: 'Ship trước giảm',
                      value: detail.shippingFeeBeforeDiscount,
                    },
                    {
                      key: 'shipSeller',
                      label: 'Ship seller giảm',
                      value: detail.shippingFeeSellerDiscount,
                      discount: true,
                    },
                    { key: 'tax', label: 'Thuế', value: detail.platformTotalTax },
                    { key: 'other', label: 'Phí khác', value: detail.platformOtherFee },
                    {
                      key: 'paid',
                      label: 'Khách trả',
                      value: detail.totalPaidAmount ?? detail.amount,
                      strong: true,
                    },
                  ] as const
                )
                  .filter((r) => r.value != null && Number(r.value) > 0)
                  .map((r) => (
                    <div key={r.key} className="supplier-purchase-field">
                      <span className="supplier-purchase-field-label">{r.label}</span>
                      <span
                        className={[
                          'strong' in r && r.strong
                            ? 'supplier-purchase-field-strong'
                            : 'supplier-purchase-field-value',
                          'discount' in r && r.discount ? 'supplier-purchase-discount' : '',
                        ]
                          .filter(Boolean)
                          .join(' ')}
                      >
                        {'discount' in r && r.discount
                          ? formatDbDiscountMoney(r.value, detail.currency)
                          : formatDbMoney(r.value, detail.currency)}
                      </span>
                    </div>
                  ))}
              </div>
            </section>
          </div>
        ) : null}
      </div>

      <DetailSection title="Quy trình fulfil">
        <FulfilTimeline order={detail} nowMs={nowMs} />
      </DetailSection>

      <DetailSection title="Vận chuyển">
        <div className="order-detail-grid">
          <DetailField label="Tracking" span={2}>
            {detail.trackingNo ? (
              <Space size={6} wrap>
                <Text copyable style={{ fontSize: 12 }}>
                  {detail.trackingNo}
                </Text>
                {(() => {
                  const primary = (detail.supplierLinks ?? []).find(
                    (l) => l.trackingNo && l.trackingNo === detail.trackingNo,
                  );
                  const kind = primary?.trackingKind;
                  if (kind === 'warehouse') {
                    return <Tag color="orange">Kho SHEIN</Tag>;
                  }
                  if (kind === 'carrier') {
                    return <Tag color="blue">Carrier</Tag>;
                  }
                  return null;
                })()}
              </Space>
            ) : (
              (() => {
                const markShipNotice = resolveAutoMarkShipNotice(detail);
                return markShipNotice ? (
                  <Text type={markShipNotice.tone} style={{ fontSize: 12 }}>
                    {markShipNotice.text}
                  </Text>
                ) : (
                  '—'
                );
              })()
            )}
          </DetailField>
          {(() => {
            const links = detail.supplierLinks ?? [];
            if (links.length > 0) {
              return (
                <DetailField label={`Đơn NCC${links.length > 1 ? ` ×${links.length}` : ''}`} span={2}>
                  <Space direction="vertical" size={6} style={{ width: '100%' }}>
                    {links.map((link) => {
                      const url =
                        link.supplierOrderUrl?.trim() ||
                        (link.supplierOrderNo
                          ? `https://us.shein.com/user/orders/detail/${link.supplierOrderNo}`
                          : null);
                      const autoStatus = (link.autoMarkShipStatus || '').toLowerCase();
                      const showAutoError =
                        !detail.trackingNo?.trim() &&
                        (link.trackingKind || '').toLowerCase() === 'carrier' &&
                        !!link.trackingNo?.trim() &&
                        (autoStatus === 'failed' ||
                          autoStatus === 'blocked' ||
                          autoStatus === 'unconfirmed') &&
                        !!link.autoMarkShipError?.trim();
                      return (
                        <Space key={link.id} direction="vertical" size={2} style={{ width: '100%' }}>
                          <Space size={8} wrap>
                            <Text copyable style={{ fontSize: 12 }}>
                              {link.supplierOrderNo}
                            </Text>
                            {link.relationBillno ? (
                              <Tag color="purple" title={`relation_billno: ${link.relationBillno}`}>
                                Joint
                              </Tag>
                            ) : null}
                            {link.trackingKind === 'warehouse' ? (
                              <Tag color="orange">Kho</Tag>
                            ) : link.trackingKind === 'carrier' ? (
                              <Tag color="blue">Carrier</Tag>
                            ) : null}
                            {(() => {
                              const st = supplierStatusTag(link.supplierStatus);
                              if (!st) return null;
                              return (
                                <Tag
                                  color={st.color}
                                  title={link.supplierStatusRaw || link.supplierStatus || undefined}
                                >
                                  {st.label}
                                </Tag>
                              );
                            })()}
                            {autoStatus === 'failed' ||
                            autoStatus === 'blocked' ||
                            autoStatus === 'unconfirmed' ? (
                              <Tag
                                color="red"
                                title={
                                  autoStatus === 'unconfirmed'
                                    ? `${link.autoMarkShipError || 'Chờ xác nhận từ 4Seller'}. Bấm Đẩy track để gửi lại ngay nếu sàn vẫn trống.`
                                    : link.autoMarkShipError || undefined
                                }
                              >
                                {autoStatus === 'blocked'
                                  ? 'Mark Ship thủ công'
                                  : autoStatus === 'unconfirmed'
                                    ? 'Mark Ship chờ xác nhận'
                                    : 'Mark Ship lỗi'}
                              </Tag>
                            ) : !detail.trackingNo?.trim() &&
                              isAutoMarkShipStuckPending(link, detail.inTransitAt) ? (
                              <Tag
                                color="gold"
                                title="SHEIN đã có mã carrier nhưng lượt sync chưa đẩy được lên sàn."
                              >
                                Mark Ship chờ sync
                              </Tag>
                            ) : null}
                            {link.trackingNo ? (
                              <Text type="secondary" style={{ fontSize: 12 }} copyable>
                                {link.trackingNo}
                              </Text>
                            ) : null}
                            {url ? (
                              <Tooltip title={url}>
                                <Button
                                  type="link"
                                  size="small"
                                  onClick={() => void copyOrderId(url)}
                                  style={{ paddingInline: 0, height: 'auto' }}
                                >
                                  Đơn hàng
                                </Button>
                              </Tooltip>
                            ) : null}
                          </Space>
                          {showAutoError ? (
                            <Text type="danger" style={{ fontSize: 12 }}>
                              {link.autoMarkShipError}
                            </Text>
                          ) : null}
                        </Space>
                      );
                    })}
                  </Space>
                </DetailField>
              );
            }
            const supplierUrl = resolveSupplierOrderUrl(detail);
            if (!supplierUrl && !detail.supplierOrderNo) return null;
            return (
              <DetailField label="Đơn NCC" span={2}>
                <Space size={8} wrap>
                  {detail.supplierOrderNo ? (
                    <Text copyable style={{ fontSize: 12 }}>
                      {detail.supplierOrderNo}
                    </Text>
                  ) : null}
                  {supplierUrl ? (
                    <Tooltip title={supplierUrl}>
                      <Button
                        type="link"
                        size="small"
                        onClick={() => void copyOrderId(supplierUrl)}
                        style={{ paddingInline: 0, height: 'auto' }}
                      >
                        Đơn hàng
                      </Button>
                    </Tooltip>
                  ) : null}
                </Space>
              </DetailField>
            );
          })()}
          <DetailField label="Shipping">
            {(() => {
              const shipKind = resolveShippingMethodKind(detail.shippingMethod, detail.shippingService);
              if (shipKind) {
                const carrierRaw = (detail.shippingService || '').trim();
                const carrierLooksLikeKind =
                  !!carrierRaw &&
                  (carrierRaw.toLowerCase().includes('seller shipping') ||
                    carrierRaw.toLowerCase().includes('tiktok shipping') ||
                    carrierRaw.toLowerCase().includes('platform shipping'));
                const carrierLabel =
                  carrierRaw && !carrierLooksLikeKind ? carrierRaw : null;
                return (
                  <Space size={8} wrap>
                    <ShippingMethodBadge
                      method={detail.shippingMethod}
                      service={detail.shippingService}
                      showLabel
                      size={20}
                    />
                    {carrierLabel ? (
                      <Text type="secondary" style={{ fontSize: 12 }}>
                        {carrierLabel}
                      </Text>
                    ) : null}
                  </Space>
                );
              }
              return formatShippingMethod(detail.shippingMethod || detail.shippingService);
            })()}
          </DetailField>
          <DetailField label="Package">{formatWeight(detail)}</DetailField>
          <DetailField label="Kích thước" span={2}>
            {formatDimensions(detail)}
          </DetailField>
        </div>
      </DetailSection>

      <div className="order-detail-buyer-grid">
        <DetailSection title="Người mua">
          <div className="order-detail-grid">
            <DetailField label="Họ tên">{detail.buyerName || '—'}</DetailField>
            <DetailField label="SĐT">{detail.buyerPhone || '—'}</DetailField>
            <DetailField label="Email" span={2}>
              {detail.buyerEmail || '—'}
            </DetailField>
            <DetailField label="Địa chỉ" span={2}>
              {address || '—'}
            </DetailField>
          </div>
        </DetailSection>
      </div>

      {notes.length > 0 ? (
        <DetailSection title="Ghi chú">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {notes.map((n) => (
              <div
                key={n.key}
                className="order-detail-note"
                style={{ background: n.bg, borderLeftColor: n.color }}
              >
                <Text strong className="order-detail-note-label" style={{ color: n.color }}>
                  {n.label}
                </Text>
                <Text className="order-detail-note-text">{n.text}</Text>
              </div>
            ))}
          </div>
        </DetailSection>
      ) : null}

      <DetailSection title={`Sản phẩm (${items.length})`} surface>
        {items.length === 0 ? (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Không có sản phẩm" />
        ) : (
          <div>
            {items.map((item, index) => (
              <div key={item.id} className="order-detail-product-row">
                <div style={{ flex: 1, minWidth: 0 }}>
                  <ProductBlock
                    item={item}
                    notice={index === 0 ? orderMessage(detail) : undefined}
                    ratingSuccessMin={ratingSuccessMin}
                    ratingWarningMin={ratingWarningMin}
                  />
                </div>
                <div className="order-detail-product-price">
                  <MoneyAmount amount={item.itemPrice} currency={item.currency || detail.currency} />
                </div>
              </div>
            ))}
          </div>
        )}
      </DetailSection>
    </div>
  );
};

const OrderNoteBadges = ({ order }: { order: OrderListItem }) => {
  const notes = NOTE_BADGE_META.filter((m) => {
    const value = order[m.key];
    return typeof value === 'string' && value.trim().length > 0;
  });
  if (notes.length === 0) return null;

  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, marginLeft: 4 }}>
      {notes.map((m) => {
        const text = (order[m.key] || '').trim();
        return (
          <Tooltip
            key={m.key}
            title={
              <div style={{ maxWidth: 280, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
                <div style={{ fontWeight: 600, marginBottom: 4 }}>{m.label}</div>
                <div>{text}</div>
              </div>
            }
          >
            <span
              aria-label={m.label}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                width: 16,
                height: 16,
                borderRadius: 3,
                background: m.bg,
                color: m.color,
                fontSize: 10,
                fontWeight: 700,
                lineHeight: 1,
                cursor: 'default',
                border: `1px solid ${m.color}33`,
                flexShrink: 0,
              }}
            >
              {m.letter}
            </span>
          </Tooltip>
        );
      })}
    </span>
  );
};

/** Compact Seller / TikTok shipping icon for order card header + shipping column. */
const ShippingMethodBadge = ({
  method,
  service,
  showLabel = false,
  size = 16,
}: {
  method?: string | null;
  service?: string | null;
  showLabel?: boolean;
  size?: number;
}) => {
  const meta = resolveShippingMethodKind(method, service);
  if (!meta) return null;
  const { Icon, label, shortLabel, color, bg, kind } = meta;
  const tooltip =
    kind === 'tiktok' ? `${label} — Không Đẩy track; TikTok tự giao.` : label;

  return (
    <Tooltip title={tooltip}>
      <span
        aria-label={label}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: showLabel ? 4 : 0,
          height: size,
          minWidth: size,
          paddingInline: showLabel ? 5 : 0,
          borderRadius: 3,
          background: bg,
          color,
          border: `1px solid ${color}33`,
          flexShrink: 0,
          cursor: 'default',
          lineHeight: 1,
          boxSizing: 'border-box',
          justifyContent: 'center',
        }}
      >
        <Icon style={{ fontSize: Math.max(10, size - 4), lineHeight: 1 }} />
        {showLabel ? (
          <span style={{ fontSize: 11, fontWeight: 600, whiteSpace: 'nowrap' }}>{shortLabel}</span>
        ) : null}
      </span>
    </Tooltip>
  );
};

const OrderCard = ({
  order,
  seq,
  onOpen,
  onOpenIssueTags,
  canManage,
  onMarkShip,
  onPushTrack,
  pushTrackPendingOrderId,
  onSyncTrack,
  syncTrackLoading = false,
  onSyncPlatformStatus,
  syncPlatformStatusLoading = false,
  markShipPendingOrderId,
  onUpdateNotes,
  onToggleUspsProcessed,
  onToggleThankCardProcessed,
  onToggleRefundProcessed,
  onDelete,
  deleting,
  isMobile,
  nowMs,
  ratingSuccessMin,
  ratingWarningMin,
  showOwnerName = false,
  enableDelete = false,
  listTab,
  selected = false,
  onSelectChange,
}: {
  order: OrderListItem;
  /** Số thứ tự toàn list (đã tính theo trang). */
  seq: number;
  onOpen: (id: string) => void;
  onOpenIssueTags: (order: OrderListItem) => void;
  canManage: boolean;
  onMarkShip: (order: OrderListItem) => void;
  onPushTrack: (order: OrderListItem) => void;
  /** Order id đang chờ tải shipping-services / mở confirm Mark Ship. */
  pushTrackPendingOrderId?: string | null;
  onSyncTrack?: (order: OrderListItem) => void;
  /** Sync track đang chạy cho đúng đơn này (độc lập với đơn khác). */
  syncTrackLoading?: boolean;
  /** Sync trạng thái sàn từ 4Seller (exact orderNo). */
  onSyncPlatformStatus?: (order: OrderListItem) => void;
  syncPlatformStatusLoading?: boolean;
  /** Order id đang Confirm Mark Ship trong modal. */
  markShipPendingOrderId?: string | null;
  onUpdateNotes: (order: OrderListItem) => void;
  onToggleUspsProcessed?: ToggleUspsProcessedFn;
  onToggleThankCardProcessed?: ToggleThankCardProcessedFn;
  onToggleRefundProcessed?: (order: OrderListItem, isRefundProcessed: boolean) => void;
  onDelete?: (order: OrderListItem) => void;
  deleting?: boolean;
  isMobile?: boolean;
  nowMs: number;
  ratingSuccessMin?: number;
  ratingWarningMin?: number;
  showOwnerName?: boolean;
  enableDelete?: boolean;
  /** Tab workflow đang lọc (PURCHASED / AWAITING_COLLECTION_GROUP / IN_TRANSIT / …). */
  listTab?: string | null;
  /** Đơn đang được chọn để copy nhanh mã vận chuyển (queue USPS). */
  selected?: boolean;
  /** Có mặt ⇒ hiện checkbox chọn đơn. */
  onSelectChange?: (id: string, checked: boolean) => void;
}) => {
  const total = resolveOrderRevenue(order);
  // Cost card = API totalCost = gross NCC + BuyTrack + ThankCard; Profit = Rev − Cost.
  const purchaseCost = order.totalCost;
  const location = [order.city, order.state].filter(Boolean).join(', ');
  const orderId = String(order.platformOrderId || order.fourSellerOrderId || '');
  const fulfilAccountName = order.fulfilAccountName?.trim() || null;
  const supplierCopyUrl = resolveOrderSupplierCopyUrl(order);
  const showMarkShip =
    canManage &&
    !isTikTokPlatformShipping(order) &&
    (order.platformOrderStatus || '').toUpperCase() === 'AWAITING_SHIPMENT';
  const showPushTrack = canPushSupplierTrack(order, canManage);
  const showSyncTrack = !!onSyncTrack && canSyncTrack(order, canManage, listTab);
  const showSyncPlatformStatus =
    !!onSyncPlatformStatus && canManage && !!order.platformOrderId?.trim();
  const markShipLoading = markShipPendingOrderId === order.id;
  const actionsBusy =
    syncTrackLoading || syncPlatformStatusLoading || markShipLoading || !!deleting;
  const lineItems = order.lineItems?.length ? order.lineItems : [];
  const issueStatusNorm = (order.issueStatus || '').toLowerCase();
  const issueDone = issueStatusNorm === 'done';
  const hasIssueSignal =
    !!(order.message && order.message.trim()) || (order.issueTags?.length ?? 0) > 0;
  const issueProcessing =
    issueStatusNorm === 'processing' || (hasIssueSignal && !issueDone);
  const issueTagBtnStyle = issueDone
    ? {
        color: '#52c41a',
        background: 'rgba(82, 196, 26, 0.12)',
      }
    : issueProcessing
      ? {
          color: '#fa8c16',
          background: 'rgba(250, 140, 22, 0.12)',
        }
      : undefined;
  const pushTrackBtn = showPushTrack ? (
    <PushTrackIconButton
      order={order}
      onPush={onPushTrack}
      loading={pushTrackPendingOrderId === order.id}
    />
  ) : null;
  const handlingDoneQueue = canManage ? resolveHandlingDoneQueue(listTab, order) : null;
  const showHandlingDone =
    !!handlingDoneQueue &&
    ((handlingDoneQueue === 'usps' && !!onToggleUspsProcessed) ||
      (handlingDoneQueue === 'thankCard' && !!onToggleThankCardProcessed) ||
      (handlingDoneQueue === 'refund' && !!onToggleRefundProcessed));
  const actionItems = [
    ...(showMarkShip
      ? [
          {
            key: 'mark-ship',
            label: 'Mark Shipped',
            disabled: markShipLoading,
            onClick: () => onMarkShip(order),
          },
        ]
      : []),
    ...(showSyncTrack
      ? [
          {
            key: 'sync-track',
            label: 'Sync track',
            disabled: syncTrackLoading,
            onClick: () => onSyncTrack?.(order),
          },
        ]
      : []),
    ...(showSyncPlatformStatus
      ? [
          {
            key: 'sync-platform-status',
            label: 'Sync trạng thái sàn',
            title:
              'Tra lại trạng thái đơn trên 4Seller (dùng khi đơn cũ bị kẹt trạng thái)',
            disabled: syncPlatformStatusLoading,
            onClick: () => onSyncPlatformStatus?.(order),
          },
        ]
      : []),
    ...(canManage
      ? [
          ...(showHandlingDone && handlingDoneQueue
            ? [
                {
                  key: 'done',
                  label: 'Done',
                  disabled: actionsBusy,
                  onClick: () =>
                    confirmMarkHandlingDone(order, handlingDoneQueue, {
                      onToggleUspsProcessed,
                      onToggleThankCardProcessed,
                      onToggleRefundProcessed,
                    }),
                },
              ]
            : []),
          {
            key: 'update-notes',
            label: 'Ghi chú',
            disabled: actionsBusy,
            onClick: () => onUpdateNotes(order),
          },
          {
            key: 'update-message',
            label: 'Tag lỗi',
            disabled: actionsBusy,
            onClick: () => onOpenIssueTags(order),
          },
          ...(enableDelete && onDelete
            ? [
                {
                  key: 'delete',
                  danger: true,
                  disabled: actionsBusy,
                  label: (
                    <Tooltip title="Xóa khỏi hệ thống — không xóa trên 4Seller">
                      <span style={{ whiteSpace: 'nowrap' }}>Xóa đơn</span>
                    </Tooltip>
                  ),
                  onClick: () => onDelete(order),
                },
              ]
            : []),
        ]
      : []),
  ];

  const profileName = order.shopProfileName?.trim() || null;

  const selectCheckbox = onSelectChange ? (
    <Checkbox
      checked={selected}
      onChange={(e) => onSelectChange(order.id, e.target.checked)}
      onClick={(e) => e.stopPropagation()}
      aria-label="Chọn đơn"
    />
  ) : null;

  const headerActions = (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'flex-end',
        gap: 2,
        width: isMobile ? undefined : ORDER_HEADER_ACTIONS_WIDTH,
        flexShrink: 0,
      }}
    >
      {!isMobile ? (
        <div style={{ marginRight: 6 }}>
          <OrderFinanceStrip revenue={total} cost={purchaseCost} currency={order.currency} />
        </div>
      ) : null}
      <Tooltip title="Chi tiết">
        <Button
          type="text"
          size="small"
          icon={<EyeOutlined />}
          aria-label="Chi tiết"
          onClick={() => onOpen(order.id)}
        />
      </Tooltip>
      <Tooltip
        title={
          issueDone
            ? order.message?.trim()
              ? `Đã xong: ${order.message.trim()}`
              : 'Tag lỗi · Đã xong'
            : issueProcessing && order.message?.trim()
              ? `Đang xử lý: ${order.message.trim()}`
              : issueProcessing
                ? 'Tag lỗi · Đang xử lý'
                : 'Tag lỗi'
        }
      >
        <Button
          type="text"
          size="small"
          icon={<TagsOutlined />}
          aria-label="Tag lỗi"
          onClick={() => onOpenIssueTags(order)}
          style={issueTagBtnStyle}
        />
      </Tooltip>
      {actionItems.length > 0 ? (
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
            aria-label="Order actions"
            loading={actionsBusy}
          />
        </Dropdown>
      ) : (
        <span style={{ width: 24, flexShrink: 0 }} aria-hidden />
      )}
    </div>
  );

  if (isMobile) {
    return (
      <div
        style={{
          border: '1px solid var(--order-border)',
          borderRadius: 8,
          background: selected ? 'var(--order-surface-selected)' : 'var(--order-surface)',
          marginBottom: 8,
          overflow: 'hidden',
        }}
      >
        {/* Identity + finance + actions */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 6,
            padding: '2px 4px 2px 4px',
            background: 'var(--order-surface-muted)',
            borderBottom: '1px solid var(--order-border)',
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
            {selectCheckbox}
            <CarrierProcessBadge
              order={order}
              canManage={canManage}
              onToggleUspsProcessed={onToggleUspsProcessed}
            />
            {order.uspsScanBatchId != null ? (
              <Tooltip
                title={`Scan batch #${order.uspsScanBatchId}${
                  order.uspsScanSentAt ? ` · ${formatDateTimeVn(order.uspsScanSentAt)}` : ''
                }${
                  order.buyTrackAmount != null && order.buyTrackAmount > 0
                    ? ` · $${order.buyTrackAmount}/mã`
                    : ''
                }`}
              >
                <Tag className="app-tag-compact" style={{ margin: 0, fontSize: 11, lineHeight: '18px' }}>
                  #{order.uspsScanBatchId}
                </Tag>
              </Tooltip>
            ) : null}
            <ThankCardProcessBadge
              order={order}
              canManage={canManage}
              onToggleThankCardProcessed={onToggleThankCardProcessed}
            />
            <RefundProcessBadge
              order={order}
              canManage={canManage}
              onToggleRefundProcessed={onToggleRefundProcessed}
            />
            {orderId ? (
              <Tooltip title={orderId}>
                <Text
                  strong
                  role="button"
                  tabIndex={0}
                  onClick={(e) => {
                    e.stopPropagation();
                    void copyOrderId(orderId);
                  }}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      e.stopPropagation();
                      void copyOrderId(orderId);
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
                  {orderId}
                </Text>
              </Tooltip>
            ) : (
              <Text type="secondary">—</Text>
            )}
            <Tooltip title={statusTooltipTitle(order)}>
              <span
                aria-label={statusLabel(platformStatus(order))}
                style={{
                  width: 7,
                  height: 7,
                  borderRadius: '50%',
                  background: statusColor(platformStatus(order)),
                  flexShrink: 0,
                  display: 'inline-block',
                }}
              />
            </Tooltip>
            <ShippingMethodBadge method={order.shippingMethod} service={order.shippingService} />
            {(showOwnerName ? order.ownerName : null) || profileName ? (
              <Tooltip title={[showOwnerName && order.ownerName && 'Seller', profileName && 'Profile'].filter(Boolean).join(' · ')}>
                <span
                  style={{
                    color: 'var(--order-text-tertiary)',
                    textTransform: 'uppercase',
                    letterSpacing: '0.02em',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    flexShrink: 1,
                    minWidth: 0,
                    maxWidth: 120,
                  }}
                >
                  {[showOwnerName ? order.ownerName : null, profileName].filter(Boolean).join(' · ')}
                </span>
              </Tooltip>
            ) : null}
            {fulfilAccountName ? (
              supplierCopyUrl ? (
                <Tooltip title={`TK mua: ${fulfilAccountName} · Copy: ${supplierCopyUrl}`}>
                  <span
                    role="button"
                    tabIndex={0}
                    className="order-link"
                    onClick={(e) => {
                      e.stopPropagation();
                      void copyOrderSupplierUrl(order);
                    }}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        e.stopPropagation();
                        void copyOrderSupplierUrl(order);
                      }
                    }}
                    style={{
                      textTransform: 'uppercase',
                      letterSpacing: '0.02em',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      flexShrink: 0,
                      maxWidth: 88,
                      fontWeight: 600,
                    }}
                  >
                    {fulfilAccountName}
                  </span>
                </Tooltip>
              ) : (
                <Tooltip title={`TK mua: ${fulfilAccountName}`}>
                  <span
                    style={{
                      color: 'var(--order-text-secondary)',
                      textTransform: 'uppercase',
                      letterSpacing: '0.02em',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      flexShrink: 0,
                      maxWidth: 88,
                      fontWeight: 600,
                    }}
                  >
                    {fulfilAccountName}
                  </span>
                </Tooltip>
              )
            ) : null}
            <OrderNoteBadges order={order} />
          </div>
          {headerActions}
        </div>

        {/* Timeline */}
        <div style={{ padding: '4px 10px', borderBottom: '1px solid var(--order-border)' }}>
          <CompactFulfilTimeline order={order} nowMs={nowMs} stretch />
        </div>

        {/* Product + finance + meta */}
        <div style={{ padding: '8px 10px', display: 'flex', flexDirection: 'column', gap: 6 }}>
          {lineItems.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              {lineItems.map((item, index) => (
                <ProductBlock
                  key={item.id}
                  item={item}
                  compact
                  index={index === 0 ? seq : undefined}
                  notice={index === 0 ? orderMessage(order) : undefined}
                  tracking={index === 0 ? resolveOrderTracking(order) : undefined}
                  pushTrack={index === 0 ? pushTrackBtn : undefined}
                  ratingSuccessMin={ratingSuccessMin}
                  ratingWarningMin={ratingWarningMin}
                />
              ))}
            </div>
          ) : (
            <div className="order-thumb-wrap" title={`#${seq}`} aria-label={`Thứ tự ${seq}`}>
              <span className="order-thumb-index" aria-hidden="true">
                {seq}
              </span>
              <div
                style={{
                  width: ORDER_CARD_THUMB_SIZE,
                  height: ORDER_CARD_THUMB_SIZE,
                  background: 'var(--order-thumb-bg)',
                  borderRadius: 4,
                }}
              />
            </div>
          )}
          {(() => {
            const markShipNotice = resolveAutoMarkShipNotice(order);
            return markShipNotice ? <AutoMarkShipNoticeLine notice={markShipNotice} /> : null;
          })()}

          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              minWidth: 0,
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
                lineHeight: 1.3,
                color: 'var(--order-text-secondary)',
              }}
            >
              <span
                style={{
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  minWidth: 0,
                }}
              >
                {[order.buyerName, order.shopName].filter(Boolean).join(' · ') || '—'}
              </span>
              <Text
                type="secondary"
                style={{
                  fontSize: 12,
                  whiteSpace: 'nowrap',
                  flexShrink: 0,
                }}
              >
                {formatDateTime(order.orderCreateTime)}
              </Text>
            </div>
            <div style={{ flexShrink: 0 }}>
              <OrderFinanceStrip revenue={total} cost={purchaseCost} currency={order.currency} />
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div
      style={{
        border: '1px solid var(--order-border)',
        borderRadius: 8,
        background: selected ? 'var(--order-surface-selected)' : 'var(--order-surface)',
        marginBottom: 10,
        overflow: 'hidden',
      }}
    >
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: `minmax(0, 1fr) ${ORDER_HEADER_TIMELINE_WIDTH}px ${ORDER_HEADER_ACTIONS_WIDTH}px`,
          alignItems: 'center',
          columnGap: 8,
          padding: '2px 12px 2px 12px',
          background: 'var(--order-surface-muted)',
          borderBottom: '1px solid var(--order-border)',
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
            fontSize: 12,
            lineHeight: 1,
            boxSizing: 'border-box',
            height: '100%',
          }}
        >
          {selectCheckbox}
          <CarrierProcessBadge
            order={order}
            canManage={canManage}
            onToggleUspsProcessed={onToggleUspsProcessed}
          />
          {order.uspsScanBatchId != null ? (
            <Tooltip
              title={`Scan batch #${order.uspsScanBatchId}${
                order.uspsScanSentAt ? ` · ${formatDateTimeVn(order.uspsScanSentAt)}` : ''
              }${
                order.buyTrackAmount != null && order.buyTrackAmount > 0
                  ? ` · $${order.buyTrackAmount}/mã`
                  : ''
              }`}
            >
              <Tag className="app-tag-compact" style={{ margin: 0, fontSize: 11, lineHeight: '18px' }}>
                #{order.uspsScanBatchId}
              </Tag>
            </Tooltip>
          ) : null}
          <ThankCardProcessBadge
            order={order}
            canManage={canManage}
            onToggleThankCardProcessed={onToggleThankCardProcessed}
          />
          <RefundProcessBadge
            order={order}
            canManage={canManage}
            onToggleRefundProcessed={onToggleRefundProcessed}
          />
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '4px 8px',
              minWidth: 0,
              flex: 1,
              overflow: 'hidden',
              height: '100%',
            }}
          >
          {orderId ? (
            <Tooltip title={orderId}>
              <Text
                strong
                role="button"
                tabIndex={0}
                onClick={(e) => {
                  e.stopPropagation();
                  void copyOrderId(orderId);
                }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    e.stopPropagation();
                    void copyOrderId(orderId);
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
                {orderId}
              </Text>
            </Tooltip>
          ) : (
            <Text type="secondary">—</Text>
          )}
          <Tooltip title={statusTooltipTitle(order)}>
            <span
              aria-label={statusLabel(platformStatus(order))}
              style={{
                width: 7,
                height: 7,
                borderRadius: '50%',
                background: statusColor(platformStatus(order)),
                flexShrink: 0,
                display: 'inline-block',
              }}
            />
          </Tooltip>
          {(showOwnerName ? order.ownerName : null) || profileName ? (
            <Tooltip title={[showOwnerName && order.ownerName && 'Quản lý', profileName && 'Profile'].filter(Boolean).join(' · ')}>
              <span
                style={{
                  color: 'var(--order-text-tertiary)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.02em',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  minWidth: 0,
                  flexShrink: 1,
                }}
              >
                {[showOwnerName ? order.ownerName : null, profileName].filter(Boolean).join(' · ')}
              </span>
            </Tooltip>
          ) : null}
          {fulfilAccountName ? (
            supplierCopyUrl ? (
              <Tooltip title={`TK mua: ${fulfilAccountName} · Copy: ${supplierCopyUrl}`}>
                <span
                  role="button"
                  tabIndex={0}
                  className="order-link"
                  onClick={(e) => {
                    e.stopPropagation();
                    void copyOrderSupplierUrl(order);
                  }}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      e.stopPropagation();
                      void copyOrderSupplierUrl(order);
                    }
                  }}
                  style={{
                    textTransform: 'uppercase',
                    letterSpacing: '0.02em',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    flexShrink: 0,
                    maxWidth: 88,
                    fontWeight: 600,
                  }}
                >
                  {fulfilAccountName}
                </span>
              </Tooltip>
            ) : (
              <Tooltip title={`TK mua: ${fulfilAccountName}`}>
                <span
                  style={{
                    color: 'var(--order-text-secondary)',
                    textTransform: 'uppercase',
                    letterSpacing: '0.02em',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    flexShrink: 0,
                    maxWidth: 88,
                    fontWeight: 600,
                  }}
                >
                  {fulfilAccountName}
                </span>
              </Tooltip>
            )
          ) : null}
          <OrderNoteBadges order={order} />
          </div>
        </div>

        <div
          style={{
            width: ORDER_HEADER_TIMELINE_WIDTH,
            paddingRight: 12,
            borderRight: '1px solid var(--order-border-header)',
            boxSizing: 'border-box',
          }}
        >
          <div>
            <CompactFulfilTimeline order={order} nowMs={nowMs} inline />
          </div>
        </div>

        {headerActions}
      </div>

      <div
        className="admin-card-grid order-card-body"
        style={{
          display: 'grid',
          gridTemplateColumns: COL_TEMPLATE,
          gap: 12,
          padding: '8px 12px',
          alignItems: 'start',
        }}
      >
        <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
          {lineItems.length > 0 ? (
            lineItems.map((item, index) => (
              <ProductBlock
                key={item.id}
                item={item}
                index={index === 0 ? seq : undefined}
                notice={index === 0 ? orderMessage(order) : undefined}
                ratingSuccessMin={ratingSuccessMin}
                ratingWarningMin={ratingWarningMin}
              />
            ))
          ) : (
            <div className="order-thumb-wrap" title={`#${seq}`} aria-label={`Thứ tự ${seq}`}>
              <span className="order-thumb-index" aria-hidden="true">
                {seq}
              </span>
              <div
                style={{
                  width: ORDER_CARD_THUMB_SIZE,
                  height: ORDER_CARD_THUMB_SIZE,
                  background: 'var(--order-thumb-bg)',
                  borderRadius: 4,
                }}
              />
            </div>
          )}
        </div>

        <div className="admin-col-meta order-card-col" style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
          <div style={{ ...primaryLineStyle, ...ellipsisLineStyle }} title={order.buyerName || undefined}>
            {order.buyerName || '—'}
          </div>
          <div style={{ ...metaLineStyle, ...ellipsisLineStyle }} title={location || undefined}>
            {location || '—'}
          </div>
        </div>

        <div className="admin-col-meta order-card-col" style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
          <div style={{ ...subtleLineStyle, color: 'var(--order-text-secondary)', ...ellipsisLineStyle }}>
            <span style={{ color: 'var(--order-text-tertiary)' }}>Đặt </span>
            <span style={{ color: 'var(--order-text)' }}>{formatDateTime(order.orderCreateTime)}</span>
          </div>
          {(() => {
            const end = orderCardEndTimeLine(order);
            return (
              <div style={{ ...subtleLineStyle, color: 'var(--order-text-secondary)', ...ellipsisLineStyle }}>
                <span style={{ color: 'var(--order-text-tertiary)' }}>{end.label} </span>
                <span style={{ color: end.valueColor, fontWeight: 600 }}>
                  {formatDateTime(end.at)}
                </span>
              </div>
            );
          })()}
        </div>

        <div className="admin-col-meta order-card-col" style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2, overflow: 'hidden' }}>
          {(() => {
            const tracking = resolveOrderTracking(order);
            return (
              <>
                {tracking || pushTrackBtn ? (
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 4,
                      minWidth: 0,
                    }}
                  >
                    {tracking ? (
                      <span style={{ minWidth: 0, flex: 1 }}>
                        <TrackingCopyLine tracking={tracking} primary />
                      </span>
                    ) : null}
                    {pushTrackBtn}
                  </div>
                ) : null}
                {(() => {
                  const markShipNotice = resolveAutoMarkShipNotice(order);
                  return markShipNotice ? (
                    <AutoMarkShipNoticeLine notice={markShipNotice} primary />
                  ) : null;
                })()}
              </>
            );
          })()}
        </div>

        <div className="admin-col-meta order-card-col" style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, height: 17, minWidth: 0 }}>
            <ShippingMethodBadge method={order.shippingMethod} service={order.shippingService} />
            <span
              style={{
                width: 7,
                height: 7,
                borderRadius: '50%',
                background: statusColor(platformStatus(order)),
                flexShrink: 0,
              }}
            />
            <Tooltip title={statusTooltipTitle(order)}>
              <span
                style={{
                  ...primaryLineStyle,
                  ...ellipsisLineStyle,
                  color: statusColor(platformStatus(order)),
                }}
              >
                {statusLabel(platformStatus(order))}
              </span>
            </Tooltip>
          </div>
          {orderCancelReason(order) ? (
            <div
              style={{
                ...orderStatusSecondaryLineStyle,
                color: statusColor(platformStatus(order)),
              }}
              title={orderCancelReason(order)!}
            >
              {orderCancelReason(order)}
            </div>
          ) : null}
          {orderReturnStatus(order) ? (
            <div
              style={{
                ...orderStatusSecondaryLineStyle,
                color: '#faad14',
              }}
              title={orderReturnStatus(order)!}
            >
              {orderReturnStatus(order)}
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
};

export type SharedOrdersPageProps = {
  config: OrdersPortalConfig;
};

export const OrdersPage = ({ config }: SharedOrdersPageProps) => {
  const deps = useOrdersDeps();
  const isMobile = useIsMobile();
  const canManage = deps.hasPermission(config.managePermission);
  const canViewSettings =
    config.useAdminSettingsForRating && deps.hasPermission('admin.settings.view');
  const settingsQuery = deps.useAdminSettings?.({ enabled: !!canViewSettings });
  const adminSettings = settingsQuery?.data ?? [];
  const ratingSuccessMin = useMemo(
    () =>
      config.useAdminSettingsForRating
        ? parseListingRatingThreshold(
            adminSettings.find((x) => x.key === LISTING_RATING_SETTING_KEYS.successMin)?.value,
            LISTING_RATING_DEFAULTS.successMin,
          )
        : LISTING_RATING_DEFAULTS.successMin,
    [adminSettings, config.useAdminSettingsForRating],
  );
  const ratingWarningMin = useMemo(
    () =>
      config.useAdminSettingsForRating
        ? parseListingRatingThreshold(
            adminSettings.find((x) => x.key === LISTING_RATING_SETTING_KEYS.warningMin)?.value,
            LISTING_RATING_DEFAULTS.warningMin,
          )
        : LISTING_RATING_DEFAULTS.warningMin,
    [adminSettings, config.useAdminSettingsForRating],
  );
  const PAGE_SIZE = deps.defaultPageSize;
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const [platformOrderStatus, setPlatformOrderStatus] = useState(() =>
    platformOrderStatusFromLocation(location.pathname, searchParams),
  );
  const [pendingFilter, setPendingFilter] = useState(() =>
    pendingFilterFromLocation(location.pathname, searchParams),
  );
  const [ownerUserId, setOwnerUserId] = useState<string | undefined>();
  const [platformCode, setPlatformCode] = useState<string | undefined>();
  const [shopId, setShopId] = useState<string | undefined>();
  const [search, setSearch] = useState('');
  const [datePreset, setDatePreset] = useState<DatePreset>('30d');
  const [dateRange, setDateRange] = useState<[Dayjs, Dayjs] | null>(() => rangeForPreset('30d'));
  const [pagination, setPagination] = useState({ current: 1, pageSize: PAGE_SIZE });
  const [detailId, setDetailId] = useState<string | null>(() => searchParams.get('detail'));
  /** Chọn đơn để copy nhanh mã vận chuyển — chỉ dùng trên queue USPS. */
  const [selectedIds, setSelectedIds] = useState<ReadonlySet<string>>(() => new Set());
  /** Ref đồng bộ — hai lần tick liên tiếp trước khi re-render vẫn cộng dồn đúng. */
  const selectedIdsRef = useRef(selectedIds);

  /** Từ khóa đang gõ — dùng thẳng cho API (không tách state debounce dễ lệch với ô input). */
  const searchApplied = search.trim();

  useEffect(() => {
    const fromUrl = searchParams.get('detail');
    setDetailId(fromUrl);
  }, [searchParams]);

  /**
   * Queue sider (pending / usps / thank-card / refund): giữ path + tab — search trong queue.
   * Trang chính `/orders` + tab status: về Tất cả để lookup toàn cục (không kẹt DELIVERED…).
   */
  useEffect(() => {
    if (!searchApplied) return;
    const onQueue =
      isOrdersPendingPath(location.pathname) ||
      isOrdersHandlingPath(location.pathname) ||
      isOrdersRefundPath(location.pathname);
    if (onQueue) return;
    if (!platformOrderStatus && !pendingFilter) return;
    setPlatformOrderStatus('');
    setPendingFilter('');
    const next = new URLSearchParams();
    const detail = searchParams.get('detail');
    if (detail) next.set('detail', detail);
    const s = next.toString();
    navigate({ pathname: '/orders', search: s ? `?${s}` : '' });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- intentional narrow deps
  }, [searchApplied]);

  // Sync workflow tab when sider shortcuts (/orders, /orders/pending, /orders/usps) or status query change.
  const statusQueryKey = [
    searchParams.get('platformOrderStatus') || '',
    searchParams.get('orderStatus') || '',
    searchParams.get('statusTab') || '',
    searchParams.get('handling') || '',
    searchParams.get('pending') || '',
    searchParams.get('kind') || '',
  ].join('|');
  useEffect(() => {
    const next = platformOrderStatusFromLocation(location.pathname, searchParams);
    setPlatformOrderStatus((prev) => {
      if (prev === next) return prev;
      setPagination((p) => ({ ...p, current: 1 }));
      return next;
    });
    const nextPending = pendingFilterFromLocation(location.pathname, searchParams);
    setPendingFilter((prev) => {
      if (prev === nextPending) return prev;
      setPagination((p) => ({ ...p, current: 1 }));
      return nextPending;
    });
    // statusQueryKey covers status-related search params without reacting to ?detail=
    // eslint-disable-next-line react-hooks/exhaustive-deps -- intentional: only path + status query
  }, [location.pathname, statusQueryKey]);

  const navigateToStatusTab = (key: string) => {
    // /orders (Tất cả): chỉ đổi query — không nhảy sang shortcut sider (/orders/pending…).
    // Queue USPS / Thank Card / Refund: giữ path shortcut + ?handling=.
    const pathname = isOrdersHandlingPath(location.pathname)
      ? ordersPathForStatus(key)
      : '/orders';
    const next = new URLSearchParams();
    const detail = searchParams.get('detail');
    if (detail) next.set('detail', detail);
    if (pathname === '/orders' && key) {
      next.set('platformOrderStatus', key);
    }
    if (pathname === '/orders/refund') {
      if (key === 'CANCELLED') next.set('kind', 'cancel');
      else if (key === 'REFUND') next.set('kind', 'refund');
      else if (key === 'REFUND_QUEUE_PROCESSED') next.set('handling', 'processed');
      else if (key === 'REFUND_QUEUE_UNPROCESSED') next.set('handling', 'unprocessed');
      // REFUND_QUEUE_ALL: không kind/handling
    } else if (
      (pathname === '/orders/usps' || pathname === '/orders/thank-card') &&
      (key === 'USPS_PROCESSED' || key === 'THANK_CARD_PROCESSED')
    ) {
      next.set('handling', 'processed');
    }
    const search = next.toString();
    navigate({ pathname, search: search ? `?${search}` : '' });
  };

  const navigatePendingFilter = (key: string) => {
    const next = new URLSearchParams();
    const detail = searchParams.get('detail');
    if (detail) next.set('detail', detail);
    if (key) next.set('pending', key);
    const search = next.toString();
    navigate({ pathname: '/orders/pending', search: search ? `?${search}` : '' });
  };

  const closeOrderDetail = () => {
    setDetailId(null);
    if (!searchParams.has('detail')) return;
    const next = new URLSearchParams(searchParams);
    next.delete('detail');
    setSearchParams(next, { replace: true });
  };
  const [markShipModal, setMarkShipModal] = useState<MarkShipModalState | null>(null);
  const [markShipPendingOrderId, setMarkShipPendingOrderId] = useState<string | null>(null);
  const [pushTrackSession, setPushTrackSession] = useState<PushTrackSession | null>(null);
  /** Sync track đang chạy theo từng order id — nhiều đơn song song. */
  const [syncTrackPendingIds, setSyncTrackPendingIds] = useState<ReadonlySet<string>>(
    () => new Set(),
  );
  const syncTrackPendingIdsRef = useRef(syncTrackPendingIds);
  syncTrackPendingIdsRef.current = syncTrackPendingIds;
  /** Sync trạng thái sàn đang chạy theo từng order id. */
  const [syncPlatformStatusPendingIds, setSyncPlatformStatusPendingIds] = useState<
    ReadonlySet<string>
  >(() => new Set());
  const syncPlatformStatusPendingIdsRef = useRef(syncPlatformStatusPendingIds);
  syncPlatformStatusPendingIdsRef.current = syncPlatformStatusPendingIds;
  const [noteOrder, setNoteOrder] = useState<OrderListItem | null>(null);
  const [reloading, setReloading] = useState(false);

  const [issueTagOrder, setIssueTagOrder] = useState<OrderListItem | null>(null);

  const sellersQuery = deps.useSellers?.();
  const sellers = sellersQuery?.data ?? [];
  const { data: platforms = [] } = deps.usePlatformOptions();
  const { data: shops = [] } = deps.useShopOptions();

  const shopOptions = useMemo(() => {
    return shops
      .filter((shop) => {
        if (shop.status && shop.status !== 'Active') return false;
        if (config.showSellerFilter && ownerUserId && shop.ownerId !== ownerUserId) return false;
        if (
          platformCode &&
          (shop.platformCode || '').toLowerCase() !== platformCode.toLowerCase()
        ) {
          return false;
        }
        return true;
      })
      .map((shop) => {
        const profile = shop.profileName?.trim();
        const shopName = shop.name?.trim() || 'Shop';
        return {
          value: shop.id,
          label: profile ? `${profile} (${shopName})` : shopName,
        };
      });
  }, [shops, ownerUserId, platformCode, config.showSellerFilter]);

  /** Có từ khóa → bỏ khoảng ngày. Queue sider giữ tab; `/orders` bỏ tab (xem effect trên). */
  const searchIgnoresDate = !!searchApplied;
  const filterDateRange = searchIgnoresDate ? null : dateRange;
  const searchKeepsWorkflowTab =
    isOrdersPendingPath(location.pathname) ||
    isOrdersHandlingPath(location.pathname) ||
    isOrdersRefundPath(location.pathname);

  const listParams = (() => {
    const { dateFrom, dateTo } = toUtcIsoRange(filterDateRange?.[0], filterDateRange?.[1]);
    return {
      pageCurrent: pagination.current,
      pageSize: pagination.pageSize,
      // Queue: luôn gửi tab. `/orders` + search: không gửi (tránh kẹt tab trước khi effect về Tất cả).
      platformOrderStatus:
        searchApplied && !searchKeepsWorkflowTab
          ? undefined
          : platformOrderStatus || undefined,
      ownerUserId: config.showSellerFilter ? ownerUserId || undefined : undefined,
      platform: platformCode || undefined,
      shopId: shopId || undefined,
      search: searchApplied || undefined,
      // Không gửi date khi search — khớp BE + UX “tìm không tính ngày”.
      dateFrom: searchApplied ? undefined : dateFrom,
      dateTo: searchApplied ? undefined : dateTo,
      pendingFilter:
        isOrdersPendingPath(location.pathname) && pendingFilter &&
        (!searchApplied || searchKeepsWorkflowTab)
          ? pendingFilter
          : undefined,
    };
  })();

  const { data, isLoading, isFetching, refetch } = deps.useOrdersList(listParams);
  const { data: statusCounts } = deps.useOrderStatusCounts(listParams);
  const syncOrders = deps.useSyncOrders();
  const [syncJobId, setSyncJobId] = useState<string | null>(null);
  const syncJob = deps.useOrderSyncJob(syncJobId).data;
  const syncRunning =
    syncOrders.isPending || (!!syncJobId && (!syncJob || syncJob.state === 'running'));
  const syncAfterSales = deps.useSyncAfterSales?.();
  const deleteOrder = deps.useDeleteOrder?.();
  const deleteOrdersByFilter = deps.useDeleteOrdersByFilter?.();
  const syncTrackOrder = deps.useSyncTrackOrder();
  const syncPlatformStatusOrder = deps.useSyncPlatformStatus?.();
  const updateUspsProcessed = deps.useUpdateOrderUspsProcessed();
  const pushUspsScan = deps.usePushUspsScan();
  const updateThankCardProcessed = deps.useUpdateOrderThankCardProcessed();
  const updateRefundProcessed = deps.useUpdateOrderRefundProcessed();
  const { data: detail, isLoading: detailLoading } = deps.useOrderDetail(detailId);

  // Job outlives the start request — toast + list refresh come from the poll, not mutateAsync.
  const handledSyncJobRef = useRef<string | null>(null);
  useEffect(() => {
    if (!syncJob || syncJob.state === 'running') return;
    if (handledSyncJobRef.current === syncJob.jobId) return;
    handledSyncJobRef.current = syncJob.jobId;

    if (syncJob.state === 'failed') {
      message.error(syncJob.error || 'Đồng bộ đơn hàng thất bại.');
    } else {
      const failures = syncJob.failedSellers ?? [];
      const onlyBusy =
        failures.length === 1 &&
        (failures[0]?.message?.includes('đang chạy') ?? false);

      if (onlyBusy) {
        message.warning(failures[0]?.message || 'Đồng bộ đang chạy. Thử lại sau.');
      } else {
        const failureDetail = failures
          .map((f) => {
            const name = f.ownerName?.trim() || f.ownerUserId;
            return `${name}: ${f.message}`;
          })
          .join(' | ');

        if (failures.length > 0) {
          message.warning(
            `${syncJob.message || 'Đồng bộ xong có lỗi.'}${failureDetail ? ` — ${failureDetail}` : ''}`,
            8,
          );
        } else {
          message.success(syncJob.message || 'Đồng bộ đơn hàng thành công.');
        }
      }
    }

    setSyncJobId(null);
    void refetch();
  }, [syncJob, refetch]);

  const handleToggleUspsProcessed = (
    order: OrderListItem,
    isUspsProcessed: boolean,
    options?: { applyFee?: boolean },
  ) =>
    updateUspsProcessed.mutateAsync({
      id: order.id,
      data: { isUspsProcessed, applyFee: options?.applyFee ?? true },
    });

  const handleToggleThankCardProcessed = (
    order: OrderListItem,
    isThankCardProcessed: boolean,
    options?: { applyFee?: boolean },
  ) =>
    updateThankCardProcessed.mutateAsync({
      id: order.id,
      data: { isThankCardProcessed, applyFee: options?.applyFee ?? true },
    });

  const handleToggleRefundProcessed = (order: OrderListItem, isRefundProcessed: boolean) =>
    updateRefundProcessed.mutateAsync({
      id: order.id,
      data: { isRefundProcessed },
    });

  const handleReload = async () => {
    setReloading(true);
    try {
      await refetch();
    } finally {
      setReloading(false);
    }
  };

  const handleSync = async () => {
    const range = toUtcIsoRange(dateRange?.[0], dateRange?.[1]);
    const response = await syncOrders.mutateAsync({
      ownerUserId: config.showSellerFilter ? ownerUserId : undefined,
      orderStatuses: platformOrderStatus
        ? PLATFORM_TAB_TO_FOURSELLER_SYNC[platformOrderStatus] ?? undefined
        : undefined,
      ...range,
      // Chỉ có tác dụng với tab nằm trong PLATFORM_TAB_TO_FOURSELLER_SYNC: bucket mở (to_ship)
      // bỏ lọc create-date để đơn mở cũ vẫn được refresh, shipped vẫn theo khoảng ngày. Tab khác
      // gửi orderStatuses rỗng → backend dùng bucket "all" và luôn áp dụng khoảng ngày.
      openStatusesIgnoreDateFilter: !!(range.dateFrom && range.dateTo),
    });
    // Pull continues server-side; poll drives spinner + toast.
    if (response.data?.jobId) {
      setSyncJobId(response.data.jobId);
    }
  };

  const handleSyncAfterSales = async () => {
    if (!syncAfterSales) return;
    // No date range: return cases are paged by last change, and the backend walks back to its own
    // watermark. Owner filter still applies so a single seller can be re-checked on its own.
    await syncAfterSales.mutateAsync({
      ownerUserId: config.showSellerFilter ? ownerUserId ?? undefined : undefined,
    });
    await refetch();
  };

  const handleDeleteOne = (order: OrderListItem) => {
    if (!config.enableDelete || !deleteOrder) return;
    const label = order.platformOrderId || String(order.fourSellerOrderId);
    Modal.confirm({
      title: 'Xóa đơn hàng local?',
      content: `Xóa đơn ${label} khỏi hệ thống (không xóa trên 4Seller). Có thể Đồng bộ lại sau.`,
      okText: 'Xóa',
      okButtonProps: { danger: true },
      cancelText: 'Hủy',
      onOk: () => deleteOrder.mutateAsync(order.id),
    });
  };

  const handleSyncTrack = async (order: OrderListItem) => {
    // Ref đồng bộ — chặn double-click cùng đơn trước khi re-render.
    if (syncTrackPendingIdsRef.current.has(order.id)) return;
    const nextPending = new Set(syncTrackPendingIdsRef.current);
    nextPending.add(order.id);
    syncTrackPendingIdsRef.current = nextPending;
    setSyncTrackPendingIds(nextPending);
    try {
      const response = await syncTrackOrder.mutateAsync(order.id);
      if (!response.success || !response.data) {
        message.error(response.message || 'Sync track thất bại.');
        return;
      }
      const toast = formatSyncTrackToast(
        response.message || response.data.summary,
        response.data.notes,
        response.data.billnos,
      );
      if (toast.level === 'success') message.success(toast.text);
      else if (toast.level === 'warning') message.warning(toast.text);
      else message.error(toast.text);
    } catch (error: unknown) {
      type AxiosErrorBody = { message?: string; errors?: string[] };
      const axiosMsg: AxiosErrorBody | undefined =
        error &&
        typeof error === 'object' &&
        'response' in error
          ? (error as { response?: { data?: AxiosErrorBody } }).response?.data
          : undefined;
      const detail =
        (typeof axiosMsg?.message === 'string' && axiosMsg.message) ||
        (error instanceof Error ? error.message : 'Sync track thất bại.');
      const errors = Array.isArray(axiosMsg?.errors) ? axiosMsg.errors.filter(Boolean) : [];
      message.error(errors.length > 0 ? `${detail}\n${errors.join('\n')}` : detail);
    } finally {
      const cleared = new Set(syncTrackPendingIdsRef.current);
      cleared.delete(order.id);
      syncTrackPendingIdsRef.current = cleared;
      setSyncTrackPendingIds(cleared);
    }
  };

  const handleSyncPlatformStatus = async (order: OrderListItem) => {
    if (!syncPlatformStatusOrder) return;
    if (syncPlatformStatusPendingIdsRef.current.has(order.id)) return;
    const nextPending = new Set(syncPlatformStatusPendingIdsRef.current);
    nextPending.add(order.id);
    syncPlatformStatusPendingIdsRef.current = nextPending;
    setSyncPlatformStatusPendingIds(nextPending);
    try {
      const response = await syncPlatformStatusOrder.mutateAsync(order.id);
      if (!response.success || !response.data) {
        message.error(response.message || 'Sync trạng thái sàn thất bại.');
        return;
      }
      const text = response.message || response.data.summary || 'Đã đồng bộ trạng thái sàn.';
      if (!response.data.found) {
        message.warning(text);
      } else if (response.data.changed) {
        message.success(text);
      } else {
        message.info(text);
      }
    } catch (error: unknown) {
      type AxiosErrorBody = { message?: string; errors?: string[] };
      const axiosMsg: AxiosErrorBody | undefined =
        error &&
        typeof error === 'object' &&
        'response' in error
          ? (error as { response?: { data?: AxiosErrorBody } }).response?.data
          : undefined;
      const detail =
        (typeof axiosMsg?.message === 'string' && axiosMsg.message) ||
        (error instanceof Error ? error.message : 'Sync trạng thái sàn thất bại.');
      const errors = Array.isArray(axiosMsg?.errors) ? axiosMsg.errors.filter(Boolean) : [];
      message.error(errors.length > 0 ? `${detail}\n${errors.join('\n')}` : detail);
    } finally {
      const cleared = new Set(syncPlatformStatusPendingIdsRef.current);
      cleared.delete(order.id);
      syncPlatformStatusPendingIdsRef.current = cleared;
      setSyncPlatformStatusPendingIds(cleared);
    }
  };

  const handleDeleteByFilter = async () => {
    if (!config.enableDelete || !deleteOrdersByFilter) return;
    await deleteOrdersByFilter.mutateAsync({
      platformOrderStatus:
        searchApplied && !searchKeepsWorkflowTab
          ? undefined
          : platformOrderStatus || undefined,
      ownerUserId: config.showSellerFilter ? ownerUserId : undefined,
      platform: platformCode,
      shopId,
      search: searchApplied || undefined,
      pendingFilter:
        isOrdersPendingPath(location.pathname) && pendingFilter &&
        (!searchApplied || searchKeepsWorkflowTab)
          ? pendingFilter
          : undefined,
      ...(searchApplied ? {} : toUtcIsoRange(filterDateRange?.[0], filterDateRange?.[1])),
    });
    setPagination((p) => ({ ...p, current: 1 }));
    await refetch();
  };

  const orders = data?.items ?? [];

  /** Checkbox chọn đơn chỉ bật trên `/orders/usps` (cả tab Chưa xử lý / Đã xử lý). */
  const showSelection = isOrdersUspsPath(location.pathname);
  const pageIds = orders.map((order) => order.id);
  const allPageSelected = pageIds.length > 0 && pageIds.every((id) => selectedIds.has(id));
  const somePageSelected = pageIds.some((id) => selectedIds.has(id));

  // Đổi trang / tab / bộ lọc / rời queue USPS: bỏ selection cũ. Selection luôn nằm trong
  // trang đang xem nên nút Copy hiện đúng số mã người dùng đang thấy.
  useEffect(() => {
    if (selectedIdsRef.current.size === 0) return;
    selectedIdsRef.current = new Set();
    setSelectedIds(selectedIdsRef.current);
  }, [
    location.pathname,
    platformOrderStatus,
    pendingFilter,
    ownerUserId,
    platformCode,
    shopId,
    searchApplied,
    dateRange,
    pagination.current,
    pagination.pageSize,
  ]);

  /** Tick/untick chỉ đổi selection — copy là hành động riêng trên thanh dưới đây. */
  const applySelection = (next: Set<string>) => {
    selectedIdsRef.current = next;
    setSelectedIds(next);
  };

  const handleSelectChange = (id: string, checked: boolean) => {
    const next = new Set(selectedIdsRef.current);
    if (checked) next.add(id);
    else next.delete(id);
    applySelection(next);
  };

  const handleSelectAllPage = (checked: boolean) => {
    const next = new Set(selectedIdsRef.current);
    for (const id of pageIds) {
      if (checked) next.add(id);
      else next.delete(id);
    }
    applySelection(next);
  };

  const handleClearSelection = () => applySelection(new Set());

  /** Mã của đơn đang chọn trên trang hiện tại — dùng cho cả nhãn nút và nội dung copy. */
  const selectedCodes = showSelection ? selectedTrackingNos(orders, selectedIds) : [];
  const selectedCount = showSelection
    ? orders.filter((order) => selectedIds.has(order.id)).length
    : 0;

  const handlePushUspsScan = async () => {
    const orderIds = orders
      .filter((order) => selectedIds.has(order.id) && !!resolveOrderTracking(order)?.trackingNo?.trim())
      .map((order) => order.id);
    if (orderIds.length === 0) {
      message.warning('Không có đơn có mã vận chuyển để đẩy');
      return;
    }
    try {
      const response = await pushUspsScan.mutateAsync({ orderIds, notes: '' });
      if (!response.success || !response.data) {
        Modal.error({
          title: 'Đẩy track scan thất bại',
          content: response.message || 'Không đẩy được batch.',
          centered: true,
        });
        return;
      }
      showUspsScanPushResult(response.data, response.message);
      if (response.data.status === 'Sent' || response.data.sentCount > 0) {
        handleClearSelection();
      }
    } catch (error: unknown) {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      Modal.error({
        title: 'Đẩy track scan thất bại',
        content:
          (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Không đẩy được batch.'),
        centered: true,
      });
    }
  };

  const confirmPushUspsScan = () => {
    if (selectedCodes.length === 0 || pushUspsScan.isPending) return;
    Modal.confirm({
      title: `Đẩy ${selectedCodes.length} mã lên PrintPackShip?`,
      content: 'Bước gửi batch sẽ trừ tiền tài khoản — không thể hoàn lại.',
      okText: 'Đẩy',
      cancelText: 'Hủy',
      centered: true,
      onOk: () => handlePushUspsScan(),
    });
  };

  /** Hành động chỉ hiện sau khi tick — cùng hàng tabs USPS (căn phải). */
  const selectionActionBar =
    showSelection && selectedCount > 0 ? (
      <div className="orders-selection-bar">
        <Tooltip title="Copy mã vận chuyển của các đơn đã chọn — mỗi mã một dòng, theo thứ tự hiển thị">
          <Button
            type="primary"
            icon={<CopyOutlined />}
            disabled={selectedCodes.length === 0}
            onClick={() => void copyTrackingNos(selectedCodes)}
          >
            {`Copy mã VC (${selectedCodes.length})`}
          </Button>
        </Tooltip>
        {canManage && platformOrderStatus === 'USPS_UNPROCESSED' ? (
          <Tooltip title="Tạo batch + gửi scan track USPS (trừ tiền PrintPackShip)">
            <Button
              icon={<CloudUploadOutlined />}
              loading={pushUspsScan.isPending}
              disabled={selectedCodes.length === 0}
              onClick={confirmPushUspsScan}
            >
              {`Đẩy track scan (${selectedCodes.length})`}
            </Button>
          </Tooltip>
        ) : null}
        <Button type="text" onClick={handleClearSelection}>
          Bỏ chọn
        </Button>
        {selectedCodes.length < selectedCount ? (
          <Text type="secondary" style={{ fontSize: 11 }}>
            {selectedCount - selectedCodes.length} đơn chưa có mã
          </Text>
        ) : null}
      </div>
    ) : null;

  const totalOrders = data?.total ?? 0;
  const counts = statusCounts ?? undefined;
  /** Một clock chung cho list — không thêm query; chỉ tick khi còn đơn đang chờ mốc. */
  const liveFulfilTick = useMemo(() => orders.some(orderNeedsLiveFulfilTick), [orders]);
  const nowMs = useSharedNow(liveFulfilTick);
  /** Spinner trên vùng list khi load / refetch / sync — header & filter vẫn hiện ngay. */
  const listBusy =
    isLoading ||
    isFetching ||
    syncRunning ||
    !!syncAfterSales?.isPending ||
    !!deleteOrder?.isPending ||
    !!deleteOrdersByFilter?.isPending;

  return (
    <div className="orders-page">
      <PageHeader
        title={isMobile ? 'Đơn hàng' : 'Quản lý Đơn hàng'}
        extra={
          <Space wrap size={isMobile ? 4 : undefined}>
            {canManage && config.enableDelete ? (
              <Popconfirm
                title="Xóa đơn theo bộ lọc hiện tại?"
                description={`Sẽ xóa khoảng ${totalOrders} đơn khớp bộ lọc đang chọn khỏi hệ thống local (không xóa trên 4Seller).`}
                okText="Xóa"
                okButtonProps={{ danger: true, loading: !!deleteOrdersByFilter?.isPending }}
                cancelText="Hủy"
                disabled={totalOrders === 0 || listBusy}
                onConfirm={() => void handleDeleteByFilter()}
              >
                <Button
                  danger
                  icon={<DeleteOutlined />}
                  disabled={totalOrders === 0 || syncRunning}
                  loading={!!deleteOrdersByFilter?.isPending}
                  aria-label="Xóa theo bộ lọc"
                >
                  {isMobile ? null : 'Xóa'}
                </Button>
              </Popconfirm>
            ) : null}
            {canManage && syncAfterSales && isOrdersRefundPath(location.pathname) ? (
              <Tooltip title="Kéo yêu cầu hoàn/trả từ 4Seller: đặt trạng thái Refund cho đơn đã hoàn tiền và trả đơn về trạng thái cũ khi yêu cầu bị đóng">
                <Button
                  icon={<SyncOutlined />}
                  onClick={() => void handleSyncAfterSales()}
                  loading={syncAfterSales.isPending}
                  disabled={syncRunning}
                  aria-label="Sync hoàn/hủy"
                >
                  {isMobile ? null : 'Sync hoàn/hủy'}
                </Button>
              </Tooltip>
            ) : null}
            {canManage ? (
              <Tooltip title="Đồng bộ đơn & trạng thái thương mại từ 4Seller (chạy nền, không giữ HTTP). Mặc định mỗi tài khoản 4Seller chỉ chạy một truy vấn lấy mọi trạng thái, luôn theo khoảng ngày đã chọn. Riêng các tab Đã mua / Chờ lấy / Đang giao / Đã giao kéo đúng bucket của tab, trong đó bucket chờ giao không giới hạn ngày. Trạng thái hoàn tiền do Sync hoàn/hủy; timeline fulfil do Sync Fulfil extension SHEIN.">
                <Button
                  type="primary"
                  icon={<SyncOutlined />}
                  onClick={() => void handleSync()}
                  loading={syncRunning}
                  disabled={!!syncAfterSales?.isPending}
                  aria-label="Sync"
                >
                  {isMobile ? null : 'Sync'}
                </Button>
              </Tooltip>
            ) : null}
            <Button
              icon={<ReloadOutlined />}
              onClick={() => void handleReload()}
              loading={reloading}
              aria-label="Tải lại"
            />
          </Space>
        }
      />

      <div className="admin-filters">
        <Input.Search
          className="admin-filters-search"
          placeholder="Tìm mã đơn / mã NCC / tracking / tên / SĐT / email / địa chỉ"
          allowClear
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPagination((p) => ({ ...p, current: 1 }));
          }}
          onSearch={(v) => {
            setSearch(v.trim());
            setPagination((p) => ({ ...p, current: 1 }));
          }}
        />
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
              setPagination((p) => ({ ...p, current: 1 }));
            }}
            options={sellers.map((seller) => ({
              value: seller.id,
              label: seller.fullName || seller.userName,
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
              setPagination((p) => ({ ...p, current: 1 }));
            }}
            options={platforms.map((p) => ({
              value: p.code,
              label: p.name,
            }))}
          />
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Shop"
            style={{ width: isMobile ? '100%' : 220 }}
            value={shopId}
            onChange={(v) => {
              setShopId(v);
              setPagination((p) => ({ ...p, current: 1 }));
            }}
            options={shopOptions}
          />
          <AdminDateRangeFilter
            variant="grid"
            allowClear
            value={filterDateRange}
            disabled={searchIgnoresDate}
            disabledPlaceholder={['Tìm toàn bộ thời gian', '']}
            presetOptions={DATE_PRESETS}
            presetValue={searchIgnoresDate ? '' : datePreset}
            presetSpanClassName="admin-filters-span admin-filters-end"
            presetTooltip="Đang tìm kiếm nên bỏ qua lọc theo ngày. Xóa từ khóa để lọc lại theo ngày."
            onChange={(next) => {
              setDateRange(next);
              setDatePreset(matchPreset(next));
              setPagination((p) => ({ ...p, current: 1 }));
            }}
            onPresetChange={(preset) => {
              const next = preset as DatePreset;
              setDatePreset(next);
              setDateRange(next ? rangeForPreset(next) : null);
              setPagination((p) => ({ ...p, current: 1 }));
            }}
          />
        </div>
      </div>

      {isOrdersPendingPath(location.pathname) ? (
      <div
        style={{
          display: 'flex',
          alignItems: isMobile ? 'stretch' : 'center',
          justifyContent: 'space-between',
          gap: isMobile ? 8 : 12,
          marginBottom: 8,
          flexDirection: isMobile ? 'column' : 'row',
          flexWrap: 'wrap',
        }}
      >
        <Tabs
          className="admin-status-tabs"
          size="small"
          activeKey={pendingFilter}
          onChange={(key) => {
            setPendingFilter(key);
            setPagination((p) => ({ ...p, current: 1 }));
            navigatePendingFilter(key);
          }}
          items={PENDING_QUEUE_TABS.map((t) => ({
            key: t.key,
            label: (
              <span>
                {isMobile ? t.shortLabel : t.label}{' '}
                <Badge
                  count={counts?.[t.countKey] ?? 0}
                  overflowCount={99999}
                  style={{ marginLeft: 4 }}
                />
              </span>
            ),
          }))}
          style={{ marginBottom: 0, flex: 1, minWidth: 0, width: isMobile ? '100%' : undefined }}
        />
      </div>
      ) : (
      <div
        style={{
          display: 'flex',
          alignItems: isMobile ? 'stretch' : 'center',
          justifyContent: 'space-between',
          gap: isMobile ? 8 : 12,
          marginBottom: 8,
          flexDirection: isMobile ? 'column' : 'row',
          flexWrap: 'wrap',
        }}
      >
        {isOrdersRefundPath(location.pathname) ? (
          <Tabs
            className="admin-status-tabs"
            size="small"
            activeKey={platformOrderStatus}
            onChange={(key) => {
              setPlatformOrderStatus(key);
              setPagination((p) => ({ ...p, current: 1 }));
              navigateToStatusTab(key);
            }}
            items={REFUND_PAGE_TABS.map((t) => ({
              key: t.key,
              label: (
                <span>
                  {isMobile ? t.shortLabel : t.label}{' '}
                  <Badge
                    count={refundPageTabCount(t.key, counts)}
                    overflowCount={99999}
                    style={{ marginLeft: 4 }}
                  />
                </span>
              ),
            }))}
            style={{ marginBottom: 0, flex: 1, minWidth: 0, width: isMobile ? '100%' : undefined }}
          />
        ) : isOrdersHandlingPath(location.pathname) ? (
          <Tabs
            className="admin-status-tabs"
            size="small"
            activeKey={platformOrderStatus}
            onChange={(key) => {
              setPlatformOrderStatus(key);
              setPagination((p) => ({ ...p, current: 1 }));
              navigateToStatusTab(key);
            }}
            items={(isOrdersThankCardPath(location.pathname)
              ? THANK_CARD_HANDLING_TABS
              : USPS_HANDLING_TABS
            ).map((t) => ({
              key: t.key,
              label: (
                <span>
                  {isMobile ? t.shortLabel : t.label}{' '}
                  <Badge
                    count={counts?.[t.countKey] ?? 0}
                    overflowCount={99999}
                    style={{ marginLeft: 4 }}
                  />
                </span>
              ),
            }))}
            style={{ marginBottom: 0, flex: 1, minWidth: 0, width: isMobile ? '100%' : undefined }}
          />
        ) : (
          <Tabs
            className="admin-status-tabs"
            size="small"
            activeKey={platformOrderStatus}
            onChange={(key) => {
              setPlatformOrderStatus(key);
              setPagination((p) => ({ ...p, current: 1 }));
              navigateToStatusTab(key);
            }}
            items={STATUS_TABS.map((t) => ({
              key: t.key,
              label: (
                <span>
                  {isMobile ? t.shortLabel : t.label}{' '}
                  <Badge
                    count={counts?.[t.countKey] ?? 0}
                    overflowCount={99999}
                    style={{ marginLeft: 4 }}
                  />
                </span>
              ),
            }))}
            style={{ marginBottom: 0, flex: 1, minWidth: 0, width: isMobile ? '100%' : undefined }}
          />
        )}
        {/* USPS: hành động copy mã VC cùng hàng tabs, căn phải (mobile: xuống dưới tabs). */}
        {selectionActionBar}
      </div>
      )}

      <div className="admin-list-scroll" style={{ overflowX: isMobile ? 'visible' : 'auto' }}>
        {!isMobile ? (
          <div
            className="admin-list-header"
            style={{
              display: 'grid',
              gridTemplateColumns: COL_TEMPLATE,
              gap: 10,
              padding: '6px 12px',
              marginBottom: 6,
              fontWeight: 600,
              color: 'var(--order-text-tertiary)',
              borderBottom: '1px solid var(--order-border)',
              minWidth: isMobile ? 0 : 860,
            }}
          >
            {COL_HEADERS.map((label, index) => (
              <div
                key={label}
                style={
                  index === 0 && showSelection
                    ? { display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }
                    : undefined
                }
              >
                {index === 0 && showSelection ? (
                  <Tooltip title="Chọn đơn để copy mã vận chuyển (mỗi mã một dòng)">
                    <Checkbox
                      checked={allPageSelected}
                      indeterminate={somePageSelected && !allPageSelected}
                      disabled={pageIds.length === 0}
                      onChange={(e) => handleSelectAllPage(e.target.checked)}
                      aria-label="Chọn tất cả trang này"
                    />
                  </Tooltip>
                ) : null}
                {label}
              </div>
            ))}
          </div>
        ) : showSelection && pageIds.length > 0 ? (
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
          </div>
        ) : null}

        <PageContentSpin spinning={listBusy} tip="Đang tải đơn hàng…">
          <div
            className="admin-list-body"
            style={{ minWidth: isMobile ? 0 : 860, minHeight: listBusy && orders.length === 0 ? 280 : 240 }}
          >
            {orders.length === 0 && !listBusy ? (
              <Empty description="Chưa có đơn hàng" style={{ margin: '48px 0' }} />
            ) : (
              orders.map((order, index) => (
                <OrderCard
                  key={order.id}
                  order={order}
                  seq={(pagination.current - 1) * pagination.pageSize + index + 1}
                  onOpen={setDetailId}
                  onOpenIssueTags={setIssueTagOrder}
                  canManage={canManage}
                  onMarkShip={(order) => {
                    const link = resolveCarrierPushLink(order);
                    setMarkShipModal({
                      order,
                      initialTrackingNo: link?.trackingNo ?? null,
                      suggestedCarrier: null,
                    });
                  }}
                  onPushTrack={(order) =>
                    setPushTrackSession({ order, key: Date.now() })
                  }
                  pushTrackPendingOrderId={pushTrackSession?.order.id ?? null}
                  onSyncTrack={canManage ? handleSyncTrack : undefined}
                  syncTrackLoading={syncTrackPendingIds.has(order.id)}
                  onSyncPlatformStatus={
                    canManage && syncPlatformStatusOrder
                      ? handleSyncPlatformStatus
                      : undefined
                  }
                  syncPlatformStatusLoading={syncPlatformStatusPendingIds.has(order.id)}
                  markShipPendingOrderId={markShipPendingOrderId}
                  onUpdateNotes={setNoteOrder}
                  onToggleUspsProcessed={canManage ? handleToggleUspsProcessed : undefined}
                  onToggleThankCardProcessed={canManage ? handleToggleThankCardProcessed : undefined}
                  onToggleRefundProcessed={canManage ? handleToggleRefundProcessed : undefined}
                  onDelete={config.enableDelete ? handleDeleteOne : undefined}
                  deleting={!!deleteOrder?.isPending && deleteOrder.variables === order.id}
                  showOwnerName={config.showOwnerName}
                  enableDelete={config.enableDelete}
                  isMobile={isMobile}
                  nowMs={nowMs}
                  ratingSuccessMin={ratingSuccessMin}
                  ratingWarningMin={ratingWarningMin}
                  listTab={platformOrderStatus}
                  selected={selectedIds.has(order.id)}
                  onSelectChange={showSelection ? handleSelectChange : undefined}
                />
              ))
            )}
          </div>
        </PageContentSpin>
      </div>

      {needsPagination(data?.total ?? 0, pagination.pageSize) ? (
        <div
          style={{
            display: 'flex',
            justifyContent: isMobile ? 'center' : 'flex-end',
            marginTop: 16,
          }}
        >
          <Pagination
            size="small"
            current={pagination.current}
            pageSize={pagination.pageSize}
            total={data?.total ?? 0}
            showSizeChanger={!isMobile}
            simple={isMobile}
            pageSizeOptions={PAGE_SIZE_OPTIONS}
            showTotal={isMobile ? undefined : (total) => `${total} đơn`}
            onChange={(current, pageSize) => {
              const nextSize = Number(pageSize) || PAGE_SIZE;
              setPagination((prev) => ({
                current: nextSize !== prev.pageSize ? 1 : current,
                pageSize: nextSize,
              }));
            }}
          />
        </div>
      ) : null}

      <Drawer
        className="order-detail-drawer"
        title="Chi tiết đơn hàng"
        size={isMobile ? '100%' : 720}
        open={!!detailId}
        onClose={closeOrderDetail}
        destroyOnHidden
      >
        {detailLoading ? <PageContentSpinCenter tip="Đang tải chi tiết…" minHeight={200} /> : null}
        {detail ? (
          <OrderDetailBody
            detail={detail}
            ratingSuccessMin={ratingSuccessMin}
            ratingWarningMin={ratingWarningMin}
            showOwnerName={config.showOwnerName}
          />
        ) : null}
      </Drawer>

      <PushTrackOrchestrator
        session={pushTrackSession}
        onDone={() => setPushTrackSession(null)}
        onNeedModal={(state) => setMarkShipModal(state)}
      />

      <MarkShipModal
        order={markShipModal?.order ?? null}
        open={!!markShipModal}
        initialTrackingNo={markShipModal?.initialTrackingNo}
        suggestedCarrier={markShipModal?.suggestedCarrier}
        onClose={() => setMarkShipModal(null)}
        onPendingOrderIdChange={setMarkShipPendingOrderId}
      />

      <UpdateMessageModal
        order={issueTagOrder}
        open={!!issueTagOrder}
        onClose={() => setIssueTagOrder(null)}
      />

      <UpdateNotesModal
        order={noteOrder}
        open={!!noteOrder}
        onClose={() => setNoteOrder(null)}
      />
    </div>
  );
};
