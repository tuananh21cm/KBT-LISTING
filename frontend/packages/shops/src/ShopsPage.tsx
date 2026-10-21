import { useEffect, useMemo, useRef, useState, type ComponentProps, type CSSProperties, type DragEvent, type MouseEvent, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Avatar,
  Badge,
  Button,
  Checkbox,
  Descriptions,
  Empty,
  Form,
  Input,
  InputNumber,
  message,
  Popconfirm,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tabs,
  Tag,
  Tooltip,
  Typography,
  Upload,
} from 'antd';
import type { UploadFile } from 'antd/es/upload/interface';
import { Modal, PageHeader, PlatformAvatar, ShopScoreBadges, ShopAutoPublishSchedulerBadge, useIsMobile, LISTING_AUTO_PUBLISH_DEFAULTS, LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES } from '@drop/ui';
import {
  PlusOutlined,
  EditOutlined,
  DeleteOutlined,
  CopyOutlined,
  ShopOutlined,
  ReloadOutlined,
  HolderOutlined,
  EyeOutlined,
  CheckCircleOutlined,
  AppstoreOutlined,
  SearchOutlined,
  ThunderboltOutlined,
  FileExcelOutlined,
  SortAscendingOutlined,
  SortDescendingOutlined,
  UploadOutlined,
  InboxOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type {
  AgeRange,
  CreateShopRequest,
  ShopCatalogCategory,
  ShopDetail,
  ShopListItem,
  UpdateAdminShopRequest,
} from '@drop/types/shop';
import {
  SHOP_STATUSES,
  SHOP_STATUS_LABELS,
  SHOP_STATUS_COLORS,
  SHOP_BANK_MATCH_STATUSES,
  SHOP_BANK_MATCH_LABELS,
  SHOP_BANK_MATCH_COLORS,
  TIKTOK_SHOP_STATUS_COLORS,
  SHOP_REGIONS,
  SHOP_TYPES,
  SHOP_TYPE_LABELS,
  BODY_SIZE_OPTIONS,
  STYLE_OPTIONS,
  findAgeRangeByBounds,
  formatBodySizeLabel,
  formatShopAgeLabel,
  formatStyleLabel,
  isShopCatalogAudienceComplete,
  normalizeStyleCodes,
  shopCatalogCategoryCount,
} from '@drop/types/shop';
import type { ShopBankMatchStatus } from '@drop/types/shop';
import { formatReceivingBankOptionLabel } from '@drop/types/receivingBank';
import { generateGoogleAuthCode } from '@drop/utils/totp';
import { formatDateTimeVn } from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import { compareProfileNames } from '@drop/utils/profileNameSort';
import type { ShopsPortalConfig } from './portalConfig';
import { useShopsDeps } from './ShopsDepsContext';
import type { CategorySearchHit, ShopsSellerOption } from './ShopsDepsContext';
import {
  normalizeShopMatchKey,
  parseShopImportFile,
  resolveShopTypeFromCompany,
  SHOP_IMPORT_COLUMN_LABELS,
  type ShopImportRow,
} from './importShopsExcel';
import {
  formatShopAddress,
  formatTikTokSellerType,
  formatTikTokShopStatus,
  formatTimezoneShort,
  buildTikTokShopStoreUrl,
} from './tiktokProfileFormat';
import { exportShopsExcel } from './exportShopsExcel';
import './shops.css';

const CATALOG_CATEGORY_MAX = 20;
/** Default USD price band when shop has no priceMin/priceMax yet. */
const DEFAULT_PRICE_MIN_USD = 7;
const DEFAULT_PRICE_MAX_USD = 11;

const MOBILE_SHOP_COLUMN_KEYS = new Set([
  'index',
  'profileName',
  'name',
  'status',
  'actions',
]);

/** Reorder items currently visible on the page, then put them back into the same slots of the full list. */
const mergeVisiblePageReorder = (
  allIds: string[],
  visiblePageIds: string[],
  reorderedVisibleIds: string[],
): string[] => {
  if (
    visiblePageIds.length === 0 ||
    visiblePageIds.length !== reorderedVisibleIds.length ||
    visiblePageIds.every((id, i) => id === reorderedVisibleIds[i])
  ) {
    return allIds;
  }

  const next = [...allIds];
  const slots: number[] = [];
  for (const id of visiblePageIds) {
    const slot = next.indexOf(id);
    if (slot < 0) return allIds;
    slots.push(slot);
  }

  slots.forEach((slot, i) => {
    const id = reorderedVisibleIds[i];
    if (id !== undefined) next[slot] = id;
  });
  return next;
};

type ShopListSortBy = 'createdAt' | 'profile' | 'listing';
type ShopListSortDir = 'asc' | 'desc';

interface ShopListFilters {
  search: string;
  ownerId?: string;
  platformId?: string;
  region?: string;
  shopType?: string;
  categoryName?: string;
  status?: string;
  bankMatch?: string;
  sortBy: ShopListSortBy;
  sortDir: ShopListSortDir;
}

const SHOP_LIST_SORT_OPTIONS: { label: string; value: ShopListSortBy }[] = [
  { label: 'Theo tên Profile', value: 'profile' },
  { label: 'Theo ngày thêm', value: 'createdAt' },
  { label: 'Theo Listing', value: 'listing' },
];

const buildDefaultFilters = (config: ShopsPortalConfig): ShopListFilters => ({
  search: '',
  status: config.defaultStatusFilter || undefined,
  sortBy: 'createdAt',
  sortDir: 'asc',
});

const compareShopList = (
  a: ShopListItem,
  b: ShopListItem,
  sortBy: ShopListSortBy,
  sortDir: ShopListSortDir,
) => {
  // Dòng thiếu dữ liệu luôn xuống cuối, không đảo theo chiều sort.
  const dir = sortDir === 'desc' ? -1 : 1;

  if (sortBy === 'profile') {
    const byProfile = compareProfileNames(a.profileName, b.profileName, dir);
    if (byProfile !== 0) return byProfile;
  } else if (sortBy === 'listing') {
    const la = a.publishLimitPublished;
    const lb = b.publishLimitPublished;
    if (la == null || lb == null) {
      if (la != null) return -1;
      if (lb != null) return 1;
    } else if (la !== lb) {
      return dir * (la - lb);
    }
  } else {
    const ta = a.createdAt ? Date.parse(a.createdAt) : 0;
    const tb = b.createdAt ? Date.parse(b.createdAt) : 0;
    if (ta !== tb) return dir * (ta - tb);
  }

  return a.id.localeCompare(b.id);
};

const matchesSearch = (shop: ShopListItem, search: string) => {
  const keyword = search.trim().toLowerCase();
  if (!keyword) return true;

  const haystack = [
    shop.name,
    shop.ownerFullName,
    shop.ownerUserName,
    shop.platformName,
    shop.region,
    shop.proxy,
    shop.profileName,
    shop.email,
    shop.dayLive,
    shop.deli,
    shop.newPhoneNumber,
    shop.paymentName,
    shop.shopType,
    shop.shopCode,
    shop.categoryName,
    ...(shop.categories ?? []).map((c) => c.name),
    shop.externalShopId,
    shop.fourSellerShopId,
    shop.shopUrl,
    shop.settlementBankLast4,
    shop.receivingBankLast4,
  ]
    .filter(Boolean)
    .join(' ')
    .toLowerCase();

  return haystack.includes(keyword);
};

/** `done` = already written in this run, so a retry after partial failure can't duplicate it. */
type ShopImportAction = 'create' | 'update' | 'skip' | 'done';

interface ShopImportPreviewRow extends ShopImportRow {
  action: ShopImportAction;
  /** Existing shop this row upserts onto. */
  matchedShopId?: string;
  matchedShopName?: string;
  /** Which key resolved the match — shown so the user can spot wrong pairings. */
  matchedBy?: 'id' | 'name';
  /** Seller resolved from `sellerUserName`. */
  ownerId?: string;
  ownerLabel?: string;
  /** Update row whose file seller differs from the shop's current owner. */
  ownerChanged?: boolean;
  currentOwnerLabel?: string;
  /** Derived from the Company cell, never picked by hand. */
  shopType: string;
}

/** Rows still pending a write — excludes both errors and rows already sent. */
const pendingImportRows = (rows: ShopImportPreviewRow[]) =>
  rows.filter((r) => r.action === 'create' || r.action === 'update');

interface ShopImportFormValues {
  platformId: string;
  region: string;
}

/**
 * Resolve each parsed row into create / update / skip against the shops already loaded.
 * External shop ID wins; an empty or unmatched ID falls back to the shop name so an
 * existing row gets its ID filled in rather than cloned.
 */
const buildShopImportPreview = (
  parsed: ShopImportRow[],
  shops: ShopListItem[],
  sellers: ShopsSellerOption[],
): ShopImportPreviewRow[] => {
  const byExternalId = new Map<string, ShopListItem[]>();
  const byName = new Map<string, ShopListItem[]>();
  for (const shop of shops) {
    const idKey = normalizeShopMatchKey(shop.externalShopId);
    if (idKey) byExternalId.set(idKey, [...(byExternalId.get(idKey) ?? []), shop]);
    const nameKey = normalizeShopMatchKey(shop.name);
    if (nameKey) byName.set(nameKey, [...(byName.get(nameKey) ?? []), shop]);
  }

  const sellerByUserName = new Map<string, ShopsSellerOption>();
  for (const seller of sellers) {
    const key = normalizeShopMatchKey(seller.userName);
    if (key) sellerByUserName.set(key, seller);
  }

  /** One target per run: a second row on the same shop would overwrite the first. */
  const claimed = new Set<string>();

  return parsed.map((row) => {
    const shopType = resolveShopTypeFromCompany(row.company);
    const skip = (error: string): ShopImportPreviewRow => ({
      ...row,
      error,
      action: 'skip',
      shopType,
    });

    if (row.error) return skip(row.error);

    const idKey = normalizeShopMatchKey(row.externalShopId);
    const nameKey = normalizeShopMatchKey(row.name);

    let matched: ShopListItem | undefined;
    let matchedBy: 'id' | 'name' | undefined;

    if (idKey) {
      matched = byExternalId.get(idKey)?.[0];
      if (matched) matchedBy = 'id';
    }

    if (!matched && nameKey) {
      const sameName = byName.get(nameKey) ?? [];
      if (sameName.length > 1) {
        return skip(`Có ${sameName.length} shop cùng tên — thêm External shop ID để xác định`);
      }
      const candidate = sameName[0];
      if (candidate) {
        const candidateIdKey = normalizeShopMatchKey(candidate.externalShopId);
        // Overwriting a different ID would re-point orders/listings at the wrong TikTok shop.
        if (idKey && candidateIdKey && candidateIdKey !== idKey) {
          return skip(
            `Shop «${candidate.name}» đang giữ External shop ID «${candidate.externalShopId}» khác file`,
          );
        }
        matched = candidate;
        matchedBy = 'name';
      }
    }

    const sellerKey = normalizeShopMatchKey(row.sellerUserName);
    let owner: ShopsSellerOption | undefined;
    if (sellerKey) {
      owner = sellerByUserName.get(sellerKey);
      if (!owner) return skip(`Không có seller «${row.sellerUserName}» trên hệ thống`);
    } else if (!matched) {
      return skip('Thiếu Seller username (bắt buộc khi thêm shop mới)');
    }

    const claimKey = matched ? matched.id : `new:${idKey || nameKey}`;
    if (claimed.has(claimKey)) {
      return skip('Trùng với một dòng phía trên trong file');
    }
    claimed.add(claimKey);

    return {
      ...row,
      action: matched ? 'update' : 'create',
      matchedShopId: matched?.id,
      matchedShopName: matched?.name,
      matchedBy,
      ownerId: owner?.id,
      ownerLabel: owner?.userName ?? owner?.fullName ?? undefined,
      ownerChanged: !!matched && !!owner && matched.ownerId !== owner.id,
      currentOwnerLabel: matched?.ownerUserName ?? matched?.ownerFullName ?? undefined,
      shopType,
    };
  });
};

/** Create payload — only the five file columns plus the platform/region picked in the modal. */
const buildShopImportCreatePayload = (
  row: ShopImportPreviewRow,
  values: ShopImportFormValues,
): CreateShopRequest => ({
  ownerId: row.ownerId!,
  platformId: values.platformId,
  region: values.region,
  name: row.name,
  status: 'Active',
  externalShopId: row.externalShopId || undefined,
  profileName: row.profileName || undefined,
  shopType: row.shopType,
  llcName: row.company || undefined,
});

/**
 * Update payload — `PUT /admin/shops/{id}` replaces every field it receives, so the
 * untouched ones are carried over from the shop detail. Empty file cells keep the
 * stored value; an empty Company cell is the one deliberate clear (shop becomes PER).
 */
const buildShopImportUpdatePayload = (
  detail: ShopDetail,
  row: ShopImportPreviewRow,
  values: ShopImportFormValues,
): UpdateAdminShopRequest => ({
  ownerId: row.ownerId ?? detail.ownerId,
  platformId: values.platformId,
  region: values.region,
  name: row.name,
  status: detail.status,
  externalShopId: row.externalShopId || detail.externalShopId,
  profileName: row.profileName || detail.profileName,
  shopType: row.shopType,
  llcName: row.company || undefined,
  avatarUrl: detail.avatarUrl,
  fourSellerShopId: detail.fourSellerShopId,
  shopUrl: detail.shopUrl,
  metadata: detail.metadata,
  proxy: detail.proxy,
  email: detail.email,
  tiktokPassword: detail.tiktokPassword,
  dayLive: detail.dayLive,
  deli: detail.deli,
  newPhoneNumber: detail.newPhoneNumber,
  oldBankNumber: detail.oldBankNumber,
  newBankNumber: detail.newBankNumber,
  paymentName: detail.paymentName,
  receivingBankAccountId: detail.receivingBankAccountId ?? null,
  twoFactorAuth: detail.twoFactorAuth,
});

interface ShopFormValues {
  ownerId: string;
  platformId: string;
  name: string;
  region: string;
  externalShopId?: string;
  avatarUrl?: string;
  fourSellerShopId?: string;
  shopUrl?: string;
  status: string;
  proxy?: string;
  profileName?: string;
  email?: string;
  tiktokPassword?: string;
  dayLive?: string;
  deli?: number;
  newPhoneNumber?: string;
  newBankNumber?: string;
  receivingBankAccountId?: string;
  paymentName?: string;
  shopType?: string;
  llcName?: string;
  twoFactorAuth?: string;
  acquisitionCost?: number | null;
}

interface AutoPublishFormValues {
  autoPublishEnabled?: boolean;
  autoPublishIntervalSeconds?: number | null;
  autoPublishMinDraftAgeMinutes?: number | null;
}

interface CatalogFormValues {
  categories?: ShopCatalogCategory[];
  /** Virtual field — maps to ageMin/ageMax on submit. */
  ageRangeCode?: string;
  priceMinUsd?: number | null;
  priceMaxUsd?: number | null;
  preferQuickShip: boolean;
  preferLocal: boolean;
  preferUsWarehouse: boolean;
  preferSheinClub: boolean;
  minRating?: number | null;
  minSoldCount?: number | null;
  maxVariantCount?: number | null;
  bodySizeCode?: string;
  styleCodes?: string[];
}

const resolveFourSellerStatus = (shop: Pick<ShopListItem, 'fourSellerConnectionStatus' | 'fourSellerShopId'>) =>
  shop.fourSellerConnectionStatus ??
  (shop.fourSellerShopId ? 'Connected' : 'NotConnected');

const fourSellerStatusLabel: Record<string, string> = {
  Connected: 'Đã kết nối',
  NotConnected: 'Chưa kết nối',
};

const fourSellerStatusColors: Record<string, string> = {
  Connected: 'green',
  NotConnected: 'orange',
};

const shopTypeTag = (shopType?: string) => {
  if (!shopType) return null;
  const label = shopType === 'Llc' ? 'LLC' : shopType === 'Per' ? 'PER' : shopType;
  return (
    <Tag
      color={shopType === 'Llc' ? 'blue' : 'default'}
      style={{
        margin: 0,
        flexShrink: 0,
        fontSize: 10,
        lineHeight: '16px',
        padding: '0 4px',
        height: 16,
        borderRadius: 4,
      }}
    >
      {label}
    </Tag>
  );
};

/** Last segment of a "Parent / Child / Leaf" path — compact tag/table label. */
const categoryLeafName = (name?: string | null) => {
  const text = name?.trim() ?? '';
  if (!text) return '';
  const parts = text.split('/').map((part) => part.trim()).filter(Boolean);
  return parts[parts.length - 1] || text;
};

const shopCatalogCategories = (
  shop: Pick<ShopListItem, 'categories' | 'categoryId' | 'categoryName'>,
): ShopCatalogCategory[] => {
  if (shop.categories?.length) {
    return shop.categories.filter((c) => c.id);
  }
  const id = shop.categoryId?.trim();
  if (!id) return [];
  return [{ id, name: shop.categoryName?.trim() || id }];
};

const effectiveAutoPublishIntervalSeconds = (
  value: number | null | undefined,
  defaultSeconds: number,
) => value ?? defaultSeconds;

const effectiveAutoPublishMinDraftAgeMinutes = (
  value: number | null | undefined,
  defaultMinutes: number,
) =>
  Math.max(
    LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES,
    value ?? defaultMinutes,
  );

const catalogProductFilterFlags = (
  shop: Pick<
    ShopListItem,
    | 'preferQuickShip'
    | 'preferLocal'
    | 'preferUsWarehouse'
    | 'preferSheinClub'
    | 'minRating'
    | 'minSoldCount'
    | 'maxVariantCount'
  >,
): { key: string; label: string; color?: string }[] => {
  const flags: { key: string; label: string; color?: string }[] = [];
  const minRating = Number(shop.minRating) || 0;
  if (minRating > 0) {
    const label = minRating % 1 === 0 ? String(minRating) : minRating.toFixed(1);
    flags.push({ key: 'rating', label: `≥${label}★` });
  }
  const minSold = Number(shop.minSoldCount) || 0;
  if (minSold > 0) flags.push({ key: 'sold', label: `Sold ≥${minSold}` });
  const maxVar = Number(shop.maxVariantCount) || 0;
  if (maxVar > 0) flags.push({ key: 'variants', label: `<${maxVar} var` });
  if (shop.preferQuickShip) flags.push({ key: 'qs', label: 'QS', color: 'blue' });
  if (shop.preferLocal) flags.push({ key: 'local', label: 'Local', color: 'cyan' });
  if (shop.preferUsWarehouse) flags.push({ key: 'uswh', label: 'US WH', color: 'geekblue' });
  if (shop.preferSheinClub) flags.push({ key: 'club', label: 'Club', color: 'purple' });
  return flags;
};

const catalogCell = (shop: Pick<
  ShopListItem,
  'categories' | 'categoryName' | 'categoryId' | 'preferQuickShip' | 'preferLocal' | 'preferUsWarehouse' | 'preferSheinClub' | 'minRating' | 'minSoldCount' | 'maxVariantCount' | 'bodySizeCode' | 'styleCodes'
>) => {
  const flags: { key: string; label: string; color?: string }[] = [];
  const bodyLabel = formatBodySizeLabel(shop.bodySizeCode);
  if (shop.bodySizeCode && bodyLabel !== '—') {
    flags.push({ key: 'body', label: bodyLabel });
  }
  const styles = normalizeStyleCodes(shop.styleCodes);
  styles.forEach((code) => {
    flags.push({ key: `style-${code}`, label: formatStyleLabel(code) });
  });
  flags.push(...catalogProductFilterFlags(shop));
  const cats = shopCatalogCategories(shop);
  const fullNames = cats.map((c) => c.name);
  const name = cats.map((c) => categoryLeafName(c.name)).join(' · ') || categoryLeafName(shop.categoryName);
  const extra = cats.length > 1 ? ` (+${cats.length - 1})` : '';

  return (
    <span className="shop-catalog-cell">
      {name ? (
        <span className="shop-catalog-cell-name" title={fullNames.join('\n') || name}>
          {cats.length > 1 ? `${categoryLeafName(cats[0]?.name)}${extra}` : name}
        </span>
      ) : (
        <Tag color="orange" className="shop-catalog-cell-tag">
          Chưa chọn
        </Tag>
      )}
      {flags.map((flag) => (
        <Tag key={flag.key} color={flag.color} className="shop-catalog-cell-tag">
          {flag.label}
        </Tag>
      ))}
    </span>
  );
};

const shopDeliBadge = (deli?: number | null) => {
  if (deli == null) return null;
  const unit = deli === 1 ? 'day' : 'days';
  return (
    <Tooltip title={`Hạn thanh toán: ${deli} ngày (TTS bill_finish_period_in_days)`}>
      <span className="shop-deli-badge" aria-label={`Hạn thanh toán ${deli} ngày`}>
        {deli} {unit}
      </span>
    </Tooltip>
  );
};

/** Format today-online seconds as `2h 15m` / `45m` / `—`. */
const formatTodayOnline = (seconds?: number | null): string => {
  if (seconds == null || seconds <= 0) return '—';
  const total = Math.floor(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  if (hours <= 0) return `${minutes}m`;
  if (minutes <= 0) return `${hours}h`;
  return `${hours}h ${minutes}m`;
};

const normalizeProxy = (proxy?: string | null) => proxy?.trim() ?? '';

/** Keep Profile / Shop text on one optical baseline. */
const tableCellRow: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 6,
  maxWidth: '100%',
  height: 22,
  lineHeight: '22px',
};

const ellipsisStyle: CSSProperties = {
  display: 'block',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  lineHeight: '22px',
};

const copyToClipboard = async (text: string, successMessage = 'Đã copy') => {
  await navigator.clipboard.writeText(text);
  message.success(successMessage);
};

const maskAccountNumber = (number?: string | null) => {
  if (!number) return '';
  const digits = String(number).replace(/\D/g, '');
  if (digits.length <= 8) return digits;
  return `${digits.slice(0, 4)}****${digits.slice(-4)}`;
};

const copyableEllipsis = (
  value?: string | number | null,
  displayOrRecord?: unknown,
  opts?: { mono?: boolean },
) => {
  if (value === null || value === undefined || value === '') return '—';
  const text = String(value);
  // Table `render` passes (value, record, index); direct callers may pass a display string.
  const shown = typeof displayOrRecord === 'string' ? displayOrRecord : text;

  const handleCopy = (e: MouseEvent) => {
    e.stopPropagation();
    void copyToClipboard(text);
  };

  return (
    <Tooltip title={text}>
      <span
        role="button"
        tabIndex={0}
        onClick={handleCopy}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            void copyToClipboard(text);
          }
        }}
        style={{
          ...ellipsisStyle,
          cursor: 'pointer',
          ...(opts?.mono
            ? {
                fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
                fontVariantNumeric: 'tabular-nums',
                letterSpacing: '0.06em',
              }
            : null),
        }}
      >
        {shown}
      </span>
    </Tooltip>
  );
};

const twoFactorCopyCell = (value?: string) => {
  if (!value) return '—';
  const text = String(value);

  const handleGenerateAndCopy = (e: MouseEvent) => {
    e.stopPropagation();
    e.preventDefault();
    try {
      const code = generateGoogleAuthCode(text);
      void copyToClipboard(code, `Mã 2FA: ${code}`);
    } catch {
      message.error('Không tạo được mã 2FA. Kiểm tra lại secret.');
    }
  };

  return (
    <span
      role="button"
      tabIndex={0}
      title={text}
      onClick={handleGenerateAndCopy}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          try {
            const code = generateGoogleAuthCode(text);
            void copyToClipboard(code, `Mã 2FA: ${code}`);
          } catch {
            message.error('Không tạo được mã 2FA. Kiểm tra lại secret.');
          }
        }
      }}
      style={{ ...ellipsisStyle, cursor: 'pointer', userSelect: 'none' }}
    >
      {text}
    </span>
  );
};

const maskedCopyCell = (value?: string, copyLabel = 'mật khẩu') => {
  if (!value) return '—';

  const handleCopy = (e: MouseEvent) => {
    e.stopPropagation();
    void copyToClipboard(value, `Đã copy ${copyLabel}`);
  };

  return (
    <span
      role="button"
      tabIndex={0}
      onClick={handleCopy}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          void copyToClipboard(value, `Đã copy ${copyLabel}`);
        }
      }}
      style={{ ...ellipsisStyle, cursor: 'pointer', userSelect: 'none' }}
    >
      *******
    </span>
  );
};

type CopyOnHoverInputProps = ComponentProps<typeof Input>;

const CopyOnHoverInput = ({ value, ...props }: CopyOnHoverInputProps) => {
  const [hovered, setHovered] = useState(false);

  const handleCopy = async (e: MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    const text = typeof value === 'string' ? value : '';
    if (!text) return;
    await navigator.clipboard.writeText(text);
    message.success('Đã copy 2FA');
  };

  return (
    <div
      style={{ position: 'relative' }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
    >
      <Input value={value} {...props} style={{ paddingRight: 28, ...props.style }} />
      {hovered && value && (
        <Button
          type="text"
          size="small"
          icon={<CopyOutlined />}
          aria-label="Copy 2FA"
          onClick={handleCopy}
          style={{
            position: 'absolute',
            top: '50%',
            right: 4,
            zIndex: 1,
            transform: 'translateY(-50%)',
            color: 'var(--shop-icon)',
          }}
        />
      )}
    </div>
  );
};

/** Flat section for edit/create form — dense divider style. */
const ShopFormSection = ({
  title,
  children,
  first,
}: {
  title: string;
  children: ReactNode;
  first?: boolean;
}) => (
  <div className={`shop-form-section${first ? ' is-first' : ''}`}>
    <div className="shop-view-section-title">{title}</div>
    {children}
  </div>
);

const ShopAutoPublishModalBody = ({
  shop,
  canManage,
  systemAutoPublishEnabled,
  defaultIntervalSeconds,
  defaultMinDraftAgeMinutes,
}: {
  shop: ShopListItem;
  canManage: boolean;
  systemAutoPublishEnabled: boolean;
  defaultIntervalSeconds: number;
  defaultMinDraftAgeMinutes: number;
}) => {
  const autoPublishForm = Form.useFormInstance<AutoPublishFormValues>();
  const autoEnabled = Form.useWatch('autoPublishEnabled', autoPublishForm);
  const shopActive = shop.status === 'Active';
  const gateBlocked = !canManage || !systemAutoPublishEnabled || !shopActive;
  const inputsDisabled = gateBlocked;

  return (
    <div className="shop-auto-publish-body">
      {!systemAutoPublishEnabled ? (
        <Alert
          type="warning"
          showIcon
          message="Auto publish hệ thống đang tắt"
          description="Bật tại Settings → Listing auto publish trước khi bật cho shop."
          className="shop-auto-publish-alert"
        />
      ) : null}
      {systemAutoPublishEnabled && !shopActive ? (
        <Alert
          type="info"
          showIcon
          message={`Shop ${SHOP_STATUS_LABELS[shop.status] ?? shop.status}`}
          description="Chỉ shop Active mới bật được auto publish."
          className="shop-auto-publish-alert"
        />
      ) : null}

      <div
        className={`shop-auto-publish-panel${autoEnabled && !gateBlocked ? ' is-on' : ''}${
          gateBlocked ? ' is-gated' : ''
        }`}
      >
        <div className="shop-auto-publish-toggle-row">
          <div className="shop-auto-publish-toggle-icon" aria-hidden>
            <ThunderboltOutlined />
          </div>
          <div className="shop-auto-publish-toggle-copy">
            <span className="shop-auto-publish-toggle-label">Bật auto publish</span>
            <span className="shop-auto-publish-toggle-desc">
              Scheduler pick draft FIFO và publish tự động cho shop này
            </span>
          </div>
          <Form.Item name="autoPublishEnabled" valuePropName="checked" noStyle>
            <Switch
              disabled={inputsDisabled}
              className="shop-auto-publish-switch"
              onChange={(checked) => {
                if (!checked) {
                  message.info('Đã hủy auto publish cho shop này');
                }
              }}
            />
          </Form.Item>
        </div>

        <div className={`shop-auto-publish-fields${!autoEnabled ? ' is-off' : ''}`}>
          <Form.Item
            name="autoPublishIntervalSeconds"
            label="Chu kỳ publish (s)"
            tooltip="Khoảng cách giữa hai lần auto publish của shop"
            rules={[{ type: 'number', min: 1, message: '≥ 1 giây' }]}
            className="shop-auto-publish-field"
          >
            <InputNumber
              min={1}
              precision={0}
              addonAfter="giây"
              disabled={inputsDisabled}
              style={{ width: '100%' }}
            />
          </Form.Item>
          <Form.Item
            name="autoPublishMinDraftAgeMinutes"
            label="Chờ draft tối thiểu (p)"
            tooltip="Draft mới hơn giá trị này sẽ không vào luồng auto publish"
            rules={[{ type: 'number', min: LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES, message: `≥ ${LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES} phút` }]}
            className="shop-auto-publish-field"
          >
            <InputNumber
              min={LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES}
              precision={0}
              addonAfter="phút"
              disabled={inputsDisabled}
              style={{ width: '100%' }}
            />
          </Form.Item>
        </div>
      </div>

      <div className="shop-auto-publish-defaults">
        <span className="shop-auto-publish-defaults-label">Mặc định hệ thống</span>
        <span className="shop-auto-publish-default-chip">{defaultIntervalSeconds}s / lần</span>
        <span className="shop-auto-publish-default-chip">
          {`${defaultMinDraftAgeMinutes}p chờ draft`}
        </span>
        <span className="shop-auto-publish-defaults-hint">Settings → Listing auto publish</span>
      </div>
    </div>
  );
};

export type SharedShopsPageProps = { config: ShopsPortalConfig };

export const ShopsPage = ({ config }: SharedShopsPageProps) => {
  const isMobile = useIsMobile();
  const queryClient = useQueryClient();
  const deps = useShopsDeps();
  const canManage = deps.hasPermission(config.managePermission);
  const canCreate = canManage && config.enableCreate;
  const canSetAcquisition =
    config.requireOwnerOnForm &&
    deps.hasPermission('admin.company-ledger.manage') &&
    !!deps.setAcquisitionCost;
  const canDelete = canManage && config.enableDelete;
  const canReorder = canManage && config.enableReorder;
  const canImport = canCreate && !!deps.importShops;

  const { data: shops = [], isLoading, isFetching, refetch } = deps.useShopsList();
  const createShop = deps.useCreateShop?.();
  const updateShop = deps.useUpdateShop();
  const updateCatalog = deps.useUpdateShopCatalogProfile();
  const updateAutoPublish = deps.useUpdateShopAutoPublish();
  const reassignOwnedData = deps.useReassignOwnedData?.();
  const reassignAllOwnedData = deps.useReassignAllOwnedData?.();
  const mappingHealthQuery = deps.useMappingHealth?.();
  const { data: ageRanges = [], isLoading: ageRangesLoading } = deps.useAgeRanges();
  const deleteShop = deps.useDeleteShop?.();
  const { CategoryCascader, searchCategories, recommendCategory } = deps;
  const reorderShops = deps.useReorderShops?.();
  const autoPublishSystemQuery = deps.useAutoPublishSystemConfig?.();
  const systemAutoPublishEnabled = autoPublishSystemQuery
    ? autoPublishSystemQuery.data?.systemAutoPublishEnabled ?? false
    : true;
  const autoPublishDefaultInterval =
    autoPublishSystemQuery?.data?.defaultIntervalSeconds ??
    LISTING_AUTO_PUBLISH_DEFAULTS.defaultIntervalSeconds;
  const autoPublishDefaultMinDraftAge =
    autoPublishSystemQuery?.data?.defaultMinDraftAgeMinutes ??
    LISTING_AUTO_PUBLISH_DEFAULTS.defaultMinDraftAgeMinutes;

  const [modalOpen, setModalOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [importFile, setImportFile] = useState<File | null>(null);
  const [importFileList, setImportFileList] = useState<UploadFile[]>([]);
  const [importRows, setImportRows] = useState<ShopImportPreviewRow[]>([]);
  const [importParsing, setImportParsing] = useState(false);
  const [importSubmitting, setImportSubmitting] = useState(false);
  const [importProgress, setImportProgress] = useState<{ done: number; total: number } | null>(
    null,
  );
  const [importForm] = Form.useForm<ShopImportFormValues>();
  const [editingShop, setEditingShop] = useState<ShopDetail | null>(null);
  const [editLoading, setEditLoading] = useState(false);
  const [viewingShopId, setViewingShopId] = useState<string | null>(null);
  const [viewingCardId, setViewingCardId] = useState<string | null>(null);
  const [filters, setFilters] = useState<ShopListFilters>(() => buildDefaultFilters(config));
  const [pagination, setPagination] = useState({ current: 1, pageSize: deps.defaultPageSize });
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const [dragOverId, setDragOverId] = useState<string | null>(null);
  const dragSourceIdRef = useRef<string | null>(null);
  const [form] = Form.useForm<ShopFormValues>();
  const [catalogForm] = Form.useForm<CatalogFormValues>();
  const [autoPublishForm] = Form.useForm<AutoPublishFormValues>();
  const [catalogShop, setCatalogShop] = useState<ShopListItem | null>(null);
  const [autoPublishShop, setAutoPublishShop] = useState<ShopListItem | null>(null);
  /** User explicitly cleared unmatched custom ageMin/ageMax. */
  const [clearCustomAge, setClearCustomAge] = useState(false);
  const [categoryPath, setCategoryPath] = useState<string[][] | undefined>();
  const [categoryPickerOpen, setCategoryPickerOpen] = useState(false);
  const [pickerPath, setPickerPath] = useState<string[][] | undefined>();
  const [pickerCategories, setPickerCategories] = useState<ShopCatalogCategory[]>([]);
  const [categorySearch, setCategorySearch] = useState('');
  const [searchHits, setSearchHits] = useState<CategorySearchHit[]>([]);
  const [aiHits, setAiHits] = useState<CategorySearchHit[]>([]);
  const [searchLoading, setSearchLoading] = useState(false);
  const [aiPending, setAiPending] = useState(false);
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchTokenRef = useRef(0);
  const catalogCategoriesValue = Form.useWatch('categories', catalogForm);
  const ageRangeCodeValue = Form.useWatch('ageRangeCode', catalogForm) as string | undefined;
  const selectedAgeRange: AgeRange | undefined = useMemo(
    () => (ageRangeCodeValue ? ageRanges.find((r) => r.code === ageRangeCodeValue) : undefined),
    [ageRangeCodeValue, ageRanges],
  );
  const selectedCatalogCategories: ShopCatalogCategory[] = Array.isArray(
    catalogCategoriesValue,
  )
    ? catalogCategoriesValue
    : [];
  const catalogSite = (catalogShop?.region || 'US').trim().toUpperCase() || 'US';

  const loadFormOptions = modalOpen || importOpen;
  const sellersQuery = deps.useSellers?.(loadFormOptions);
  const sellers = sellersQuery?.data ?? [];
  const sellersLoading = sellersQuery?.isLoading ?? false;
  const { data: platforms = [], isLoading: platformsLoading } = deps.usePlatformOptions();
  const receivingBanksQuery = deps.useReceivingBankOptions?.(
    modalOpen && config.enableReceivingBank,
  );
  const receivingBanks = receivingBanksQuery?.data ?? [];
  const receivingBanksLoading = receivingBanksQuery?.isLoading ?? false;

  const {
    data: viewingShop,
    isLoading: viewingShopLoading,
    isFetching: viewingShopFetching,
  } = deps.useShopDetail(viewingShopId, config.enableViewModal);

  const bankQuery = deps.useReceivingBankDetail?.(
    viewingCardId,
    config.enableReceivingBank && !!viewingCardId,
  );
  const viewingCard = bankQuery?.data;
  const bankAccountLoading = bankQuery?.isLoading ?? false;
  const bankAccountFetching = bankQuery?.isFetching ?? false;

  const sellerOptions = useMemo(
    () =>
      sellers.map((seller) => ({
        label: `${seller.fullName} (${seller.userName})`,
        value: seller.id,
      })),
    [sellers],
  );

  /** Seller filter options from list payload (no extra sellers API on page load). */
  const sellerFilterOptions = useMemo(() => {
    const byId = new Map<string, string>();
    for (const shop of shops) {
      if (shop.ownerId && !byId.has(shop.ownerId)) {
        byId.set(
          shop.ownerId,
          shop.ownerUserName
            ? `${shop.ownerFullName ?? ''} (${shop.ownerUserName})`
            : (shop.ownerFullName ?? shop.ownerId),
        );
      }
    }
    return [...byId.entries()]
      .map(([value, label]) => ({ value, label }))
      .sort((a, b) => a.label.localeCompare(b.label));
  }, [shops]);

  /** Banks linked to another shop (not the one being edited). */
  const assignedReceivingBankIds = useMemo(() => {
    const ids = new Set<string>();
    for (const shop of shops) {
      if (
        shop.receivingBankAccountId &&
        shop.id !== editingShop?.id
      ) {
        ids.add(shop.receivingBankAccountId);
      }
    }
    return ids;
  }, [shops, editingShop?.id]);

  const receivingBankOptions = useMemo(() => {
    const options = receivingBanks.map((bank) => {
      const usedByOther = assignedReceivingBankIds.has(bank.id);
      const usedByCurrent = bank.id === editingShop?.receivingBankAccountId;
      const used = usedByOther || usedByCurrent;
      return {
        value: bank.id,
        label: formatReceivingBankOptionLabel(bank),
        used,
        /** Already on another shop — keep visible with badge but not selectable. */
        disabled: usedByOther,
      };
    });

    if (
      editingShop?.receivingBankAccountId &&
      !options.some((o) => o.value === editingShop.receivingBankAccountId)
    ) {
      options.unshift({
        value: editingShop.receivingBankAccountId,
        label:
          formatReceivingBankOptionLabel({
            sourceName: editingShop.receivingBankSourceName,
            sourceAccountName: editingShop.receivingBankSourceAccountName,
            accountHolderName: editingShop.receivingBankAccountHolderName,
            name: editingShop.receivingBankAccountName,
            accountNumber: editingShop.receivingBankAccountNumber,
            currency: editingShop.receivingBankAccountCurrency,
          }) || editingShop.receivingBankAccountId,
        used: true,
        disabled: false,
      });
    }

    // Free cards first, then in-use (disabled).
    return options.sort((a, b) => Number(a.disabled) - Number(b.disabled));
  }, [receivingBanks, editingShop, assignedReceivingBankIds]);

  const renderReceivingBankOption = (
    label: ReactNode,
    used?: boolean,
  ) => (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 8,
        width: '100%',
        minWidth: 0,
      }}
    >
      <span
        style={{
          minWidth: 0,
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
        }}
      >
        {label}
      </span>
      <Tag
        color={used ? 'orange' : 'green'}
        style={{
          margin: 0,
          flexShrink: 0,
          fontSize: 11,
          lineHeight: '18px',
          padding: '0 6px',
        }}
      >
        {used ? 'Đã gắn' : 'Chưa gắn'}
      </Tag>
    </span>
  );

  const platformOptions = useMemo(() => {
    if (platforms.length > 0) {
      return platforms.map((platform) => ({
        label: platform.name,
        value: platform.id,
      }));
    }

    const byId = new Map<string, string>();
    for (const shop of shops) {
      if (shop.platformId && !byId.has(shop.platformId)) {
        byId.set(shop.platformId, shop.platformName || shop.platformId);
      }
    }
    return [...byId.entries()].map(([value, label]) => ({ label, value }));
  }, [platforms, shops]);

  const regionOptions = useMemo(() => {
    const fromData = shops.map((shop) => shop.region).filter(Boolean);
    const regions = [...new Set([...SHOP_REGIONS.map((r) => r.value), ...fromData])].sort();
    return regions.map((region) => ({ label: region, value: region }));
  }, [shops]);

  const categoryFilterOptions = useMemo(() => {
    const names = shops.flatMap((shop) => shopCatalogCategories(shop).map((c) => c.name.trim()).filter(Boolean));
    return [...new Set(names)]
      .sort((a, b) => a.localeCompare(b))
      .map((name) => ({ label: name, value: name }));
  }, [shops]);

  const filteredShops = useMemo(() => {
    const filtered = shops.filter((shop) => {
      if (!matchesSearch(shop, filters.search)) return false;
      if (filters.ownerId && shop.ownerId !== filters.ownerId) return false;
      if (filters.platformId && shop.platformId !== filters.platformId) return false;
      if (filters.region && shop.region !== filters.region) return false;
      if (filters.shopType && shop.shopType !== filters.shopType) return false;
      if (filters.categoryName) {
        const cats = shopCatalogCategories(shop);
        if (!cats.some((c) => c.name === filters.categoryName)) return false;
      }
      if (filters.status && shop.status !== filters.status) return false;
      if (filters.bankMatch && shop.bankMatchStatus !== filters.bankMatch) return false;
      return true;
    });
    return filtered.sort((a, b) => compareShopList(a, b, filters.sortBy, filters.sortDir));
  }, [shops, filters]);

  const shopStatusCounts = useMemo(() => {
    const counts = new Map<string, number>();
    let all = 0;

    for (const shop of shops) {
      if (!matchesSearch(shop, filters.search)) continue;
      if (filters.ownerId && shop.ownerId !== filters.ownerId) continue;
      if (filters.platformId && shop.platformId !== filters.platformId) continue;
      if (filters.region && shop.region !== filters.region) continue;
      if (filters.shopType && shop.shopType !== filters.shopType) continue;
      if (filters.categoryName) {
        const cats = shopCatalogCategories(shop);
        if (!cats.some((c) => c.name === filters.categoryName)) continue;
      }
      if (filters.bankMatch && shop.bankMatchStatus !== filters.bankMatch) continue;

      all += 1;
      counts.set(shop.status, (counts.get(shop.status) ?? 0) + 1);
    }

    return { all, byStatus: counts };
  }, [
    shops,
    filters.search,
    filters.ownerId,
    filters.platformId,
    filters.region,
    filters.shopType,
    filters.categoryName,
    filters.bankMatch,
  ]);

  const hasActiveFilters =
    !!filters.search ||
    !!filters.ownerId ||
    !!filters.platformId ||
    !!filters.region ||
    !!filters.shopType ||
    !!filters.categoryName ||
    !!filters.bankMatch ||
    (!!filters.status && filters.status !== (config.defaultStatusFilter || undefined));

  const pageStart = (pagination.current - 1) * pagination.pageSize;
  const visiblePageShops = useMemo(
    () => filteredShops.slice(pageStart, pageStart + pagination.pageSize),
    [filteredShops, pageStart, pagination.pageSize],
  );

  const canDragSort = canReorder && !!reorderShops && !reorderShops.isPending && !isMobile;

  const clearDragState = () => {
    dragSourceIdRef.current = null;
    setDraggingId(null);
    setDragOverId(null);
  };

  const handleDropReorder = (targetId: string) => {
    const sourceId = dragSourceIdRef.current;
    clearDragState();
    if (!sourceId || sourceId === targetId || !canDragSort) return;

    const visibleIds = visiblePageShops.map((shop) => shop.id);
    const fromIndex = visibleIds.indexOf(sourceId);
    const toIndex = visibleIds.indexOf(targetId);
    if (fromIndex < 0 || toIndex < 0) return;

    const reorderedVisible = [...visibleIds];
    const [moved] = reorderedVisible.splice(fromIndex, 1);
    if (!moved) return;
    reorderedVisible.splice(toIndex, 0, moved);

    const nextOrderedIds = mergeVisiblePageReorder(
      shops.map((shop) => shop.id),
      visibleIds,
      reorderedVisible,
    );

    if (nextOrderedIds.every((id, i) => id === shops[i]?.id)) return;
    void reorderShops?.mutateAsync(nextOrderedIds);
  };

  const updateFilters = (patch: Partial<ShopListFilters>) => {
    setFilters((prev) => ({ ...prev, ...patch }));
    setPagination((prev) => ({ ...prev, current: 1 }));
  };

  const resetFilters = () => {
    setFilters((prev) => ({
      ...buildDefaultFilters(config),
      sortBy: prev.sortBy,
      sortDir: prev.sortDir,
    }));
    setPagination((prev) => ({ ...prev, current: 1 }));
  };

  const fillEditForm = (shop: ShopDetail) => {
    form.setFieldsValue({
      ownerId: shop.ownerId,
      platformId: shop.platformId,
      name: shop.name,
      region: shop.region,
      externalShopId: shop.externalShopId,
      avatarUrl: shop.avatarUrl,
      fourSellerShopId: shop.fourSellerShopId,
      shopUrl: shop.shopUrl,
      status: shop.status,
      proxy: shop.proxy,
      profileName: shop.profileName,
      email: shop.email,
      tiktokPassword: shop.tiktokPassword,
      dayLive: shop.dayLive,
      deli: shop.deli,
      newPhoneNumber: shop.newPhoneNumber,
      newBankNumber: shop.newBankNumber,
      receivingBankAccountId: shop.receivingBankAccountId,
      paymentName: shop.paymentName,
      shopType: shop.shopType,
      llcName: shop.llcName,
      twoFactorAuth: shop.twoFactorAuth,
      acquisitionCost: shop.acquisitionCost ?? 0,
    });
  };

  const openCreate = () => {
    setEditingShop(null);
    form.resetFields();
    form.setFieldsValue({
      status: 'Active',
      region: 'US',
      acquisitionCost: 0,
    });
    setModalOpen(true);
  };

  const resetImportState = () => {
    setImportFile(null);
    setImportFileList([]);
    setImportRows([]);
    setImportProgress(null);
  };

  const openImport = () => {
    resetImportState();
    importForm.resetFields();
    importForm.setFieldsValue({ region: 'US' });
    setImportOpen(true);
  };

  const closeImport = () => {
    if (importSubmitting) return;
    setImportOpen(false);
    resetImportState();
  };

  const handleImportProcess = async () => {
    try {
      await importForm.validateFields();
    } catch {
      return;
    }
    if (!importFile) {
      message.warning('Chọn file Excel trước.');
      return;
    }

    if (sellersLoading) {
      message.warning('Đang tải danh sách seller. Thử lại sau vài giây.');
      return;
    }

    setImportParsing(true);
    try {
      const parsed = parseShopImportFile(await importFile.arrayBuffer());
      const preview = buildShopImportPreview(parsed, shops, sellers);
      setImportRows(preview);
      setImportProgress(null);

      const toCreate = preview.filter((r) => r.action === 'create').length;
      const toUpdate = preview.filter((r) => r.action === 'update').length;
      const skipped = preview.length - toCreate - toUpdate;
      message.success(
        skipped > 0
          ? `Đã xử lý ${preview.length} dòng: ${toCreate} mới, ${toUpdate} cập nhật, ${skipped} lỗi.`
          : `Đã xử lý ${preview.length} dòng: ${toCreate} mới, ${toUpdate} cập nhật.`,
      );
    } catch (error) {
      setImportRows([]);
      message.error(error instanceof Error ? error.message : 'Không đọc được file Excel.');
    } finally {
      setImportParsing(false);
    }
  };

  const handleImportSubmit = async () => {
    const importApi = deps.importShops;
    if (!importApi) return;

    let values: ShopImportFormValues;
    try {
      values = await importForm.validateFields();
    } catch {
      return;
    }

    const validRows = pendingImportRows(importRows);
    if (validRows.length === 0) {
      message.warning('Không có dòng hợp lệ để import. Hãy Xử lý file trước.');
      return;
    }

    const runImport = async () => {
      setImportSubmitting(true);
      setImportProgress({ done: 0, total: validRows.length });

      let created = 0;
      let updated = 0;
      let failed = 0;
      const errors: string[] = [];

      try {
        for (let i = 0; i < validRows.length; i++) {
          const row = validRows[i]!;
          try {
            if (row.action === 'update' && row.matchedShopId) {
              // The list row omits bank numbers / metadata, and PUT replaces whatever it
              // receives — read the detail so untouched fields survive the import.
              const detail = await deps.fetchShopDetail(queryClient, row.matchedShopId);
              const response = await importApi.update(
                row.matchedShopId,
                buildShopImportUpdatePayload(detail, row, values),
              );
              if (!response.success) {
                throw new Error(response.message || 'Cập nhật thất bại');
              }
              updated += 1;
            } else {
              const response = await importApi.create(buildShopImportCreatePayload(row, values));
              if (!response.success || !response.data) {
                throw new Error(response.message || 'Tạo thất bại');
              }
              created += 1;
            }
            // Mark written so a retry after partial failure can't send this row twice.
            setImportRows((prev) =>
              prev.map((r) =>
                r.rowIndex === row.rowIndex ? { ...r, action: 'done', error: undefined } : r,
              ),
            );
          } catch (error) {
            failed += 1;
            const reason = error instanceof Error ? error.message : 'Lỗi không xác định';
            errors.push(`Dòng ${row.rowIndex} (${row.name}): ${reason}`);
            setImportRows((prev) =>
              prev.map((r) =>
                r.rowIndex === row.rowIndex ? { ...r, action: 'skip', error: reason } : r,
              ),
            );
          }

          setImportProgress({ done: i + 1, total: validRows.length });
        }

        await importApi.invalidate();

        if (failed === 0) {
          message.success(`Import xong: ${created} shop mới, ${updated} cập nhật.`);
          setImportOpen(false);
          resetImportState();
          return;
        }

        // Keep the modal open so failed rows stay readable in the preview table.
        message.error(
          `Import xong: ${created} mới, ${updated} cập nhật, ${failed} lỗi. ${errors[0] ?? ''}`,
        );
      } finally {
        setImportSubmitting(false);
      }
    };

    // Same warning the edit form shows, once for the whole run instead of per row.
    const ownerChanges = validRows.filter((r) => r.ownerChanged);
    if (ownerChanges.length > 0) {
      Modal.confirm({
        title: `Chuyển ${ownerChanges.length} shop sang seller khác?`,
        content:
          'File gán seller khác với seller đang giữ shop. Đơn hàng, listing và dữ liệu gắn shop '
          + 'sẽ chuyển sang seller mới (cookie 4Seller / Mark Ship theo seller mới). Tiếp tục?',
        okText: 'Chuyển giao và import',
        cancelText: 'Hủy',
        onOk: runImport,
      });
      return;
    }

    await runImport();
  };

  const openEdit = async (shop: Pick<ShopListItem, 'id'> | Pick<ShopDetail, 'id'>) => {
    setViewingShopId(null);
    setEditLoading(true);
    setModalOpen(true);
    try {
      const detail = await deps.fetchShopDetail(queryClient, shop.id);
      setEditingShop(detail);
      fillEditForm(detail);
    } catch (error) {
      message.error(error instanceof Error ? error.message : 'Không tải được shop.');
      setModalOpen(false);
    } finally {
      setEditLoading(false);
    }
  };

  const openView = (shop: ShopListItem) => {
    setViewingShopId(shop.id);
  };

  const openViewCard = (shop: ShopListItem) => {
    if (!shop.receivingBankAccountId) return;
    setViewingCardId(shop.receivingBankAccountId);
  };

  const openCatalog = (shop: ShopListItem) => {
    setCatalogShop(shop);
    setClearCustomAge(false);
    const cats = shopCatalogCategories(shop);
    setCategoryPath(cats.length ? cats.map((c) => [c.id]) : undefined);
    const matched = findAgeRangeByBounds(shop.ageMin, shop.ageMax, ageRanges);
    const hasPrice = shop.priceMinUsd != null || shop.priceMaxUsd != null;
    catalogForm.setFieldsValue({
      categories: cats,
      ageRangeCode: matched?.code,
      priceMinUsd: hasPrice ? (shop.priceMinUsd ?? undefined) : DEFAULT_PRICE_MIN_USD,
      priceMaxUsd: hasPrice ? (shop.priceMaxUsd ?? undefined) : DEFAULT_PRICE_MAX_USD,
      preferQuickShip: !!shop.preferQuickShip,
      preferLocal: !!shop.preferLocal,
      preferUsWarehouse: !!shop.preferUsWarehouse,
      preferSheinClub: !!shop.preferSheinClub,
      minRating: Number(shop.minRating) || 0,
      minSoldCount: Number(shop.minSoldCount) || 0,
      maxVariantCount: Number(shop.maxVariantCount) || 0,
      bodySizeCode: shop.bodySizeCode || undefined,
      styleCodes: normalizeStyleCodes(shop.styleCodes),
    });
  };

  const openAutoPublish = (shop: ShopListItem) => {
    setAutoPublishShop(shop);
    autoPublishForm.setFieldsValue({
      autoPublishEnabled: !!shop.autoPublishEnabled,
      autoPublishIntervalSeconds: effectiveAutoPublishIntervalSeconds(
        shop.autoPublishIntervalSeconds,
        autoPublishDefaultInterval,
      ),
      autoPublishMinDraftAgeMinutes: effectiveAutoPublishMinDraftAgeMinutes(
        shop.autoPublishMinDraftAgeMinutes,
        autoPublishDefaultMinDraftAge,
      ),
    });
  };

  const closeAutoPublish = () => {
    setAutoPublishShop(null);
    autoPublishForm.resetFields();
  };

  // Age ranges may load after modal open — hydrate Select when presets arrive.
  useEffect(() => {
    if (!catalogShop || ageRanges.length === 0) return;
    if (catalogForm.isFieldTouched('ageRangeCode')) return;
    const matched = findAgeRangeByBounds(catalogShop.ageMin, catalogShop.ageMax, ageRanges);
    const current = catalogForm.getFieldValue('ageRangeCode') as string | undefined;
    if (matched?.code && current !== matched.code) {
      catalogForm.setFieldValue('ageRangeCode', matched.code);
    }
  }, [ageRanges, catalogShop, catalogForm]);

  const closeCatalog = () => {
    setCategoryPickerOpen(false);
    setCatalogShop(null);
    setClearCustomAge(false);
    setCategoryPath(undefined);
    setPickerPath(undefined);
    setPickerCategories([]);
    setCategorySearch('');
    setSearchHits([]);
    setAiHits([]);
    catalogForm.resetFields();
  };

  const openCategoryPicker = () => {
    const cats = (catalogForm.getFieldValue('categories') as ShopCatalogCategory[] | undefined) ?? [];
    setPickerCategories(cats.filter((c) => c.id));
    setPickerPath(categoryPath);
    setCategorySearch('');
    setSearchHits([]);
    setAiHits([]);
    setSearchLoading(false);
    setCategoryPickerOpen(true);
  };

  const closeCategoryPicker = () => {
    setCategoryPickerOpen(false);
    setCategorySearch('');
    setSearchHits([]);
    setAiHits([]);
    setSearchLoading(false);
    setAiPending(false);
    if (searchTimerRef.current) {
      clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    searchTokenRef.current += 1;
  };

  const applyCategoryPicker = () => {
    const categories = pickerCategories.filter((c) => String(c.id ?? '').trim());
    if (categories.length === 0) {
      message.error('Chọn ít nhất một category.');
      return;
    }
    setCategoryPath(pickerPath);
    catalogForm.setFieldsValue({ categories });
    closeCategoryPicker();
  };

  const applyPickerSelection = (
    path: string[][] | undefined,
    items: ShopCatalogCategory[],
  ) => {
    if (items.length > CATALOG_CATEGORY_MAX) {
      message.warning(`Tối đa ${CATALOG_CATEGORY_MAX} category.`);
      return;
    }
    setPickerPath(path);
    setPickerCategories(items);
  };

  const togglePickerHit = (hit: CategorySearchHit) => {
    const exists = pickerCategories.some((c) => c.id === hit.id);
    if (exists) {
      applyPickerSelection(
        (() => {
          const next = (pickerPath ?? []).filter((p) => p[p.length - 1] !== hit.id);
          return next.length ? next : undefined;
        })(),
        pickerCategories.filter((c) => c.id !== hit.id),
      );
      return;
    }
    applyPickerSelection(
      [...(pickerPath ?? []), hit.pathIds.length ? hit.pathIds : [hit.id]],
      [...pickerCategories, { id: hit.id, name: hit.name }],
    );
  };

  const handleCategorySearchChange = (value: string) => {
    setCategorySearch(value);
    if (searchTimerRef.current) {
      clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    const term = value.trim();
    if (term.length === 0) {
      searchTokenRef.current += 1;
      setSearchHits([]);
      setAiHits([]);
      setSearchLoading(false);
      return;
    }
    if (term.length < 2) {
      searchTokenRef.current += 1;
      setSearchHits([]);
      setSearchLoading(false);
      return;
    }
    setSearchLoading(true);
    const token = ++searchTokenRef.current;
    searchTimerRef.current = setTimeout(() => {
      void (async () => {
        try {
          const hits = await searchCategories({ site: catalogSite, search: term });
          if (token !== searchTokenRef.current) return;
          setSearchHits(hits);
        } catch (error) {
          if (token !== searchTokenRef.current) return;
          setSearchHits([]);
          message.error(error instanceof Error ? error.message : 'Không tìm được category.');
        } finally {
          if (token === searchTokenRef.current) setSearchLoading(false);
        }
      })();
    }, 300);
  };

  const applyAiCategory = async () => {
    const title = categorySearch.trim();
    if (!title) {
      message.warning('Nhập từ khóa trước khi AI gợi ý category.');
      return;
    }
    setAiPending(true);
    try {
      const hit = await recommendCategory({ site: catalogSite, title });
      if (!hit.id) {
        message.error('AI không gợi ý được category.');
        return;
      }
      setAiHits([hit]);
      const already = pickerCategories.some((c) => c.id === hit.id);
      if (!already) {
        applyPickerSelection(
          [...(pickerPath ?? []), hit.pathIds.length ? hit.pathIds : [hit.id]],
          [...pickerCategories, { id: hit.id, name: hit.name }],
        );
      }
      message.success(
        hit.pathLabel ? `Category: ${hit.pathLabel}` : `Category: ${hit.name}`,
      );
    } catch (error) {
      message.error(error instanceof Error ? error.message : 'AI không gợi ý được category.');
    } finally {
      setAiPending(false);
    }
  };

  const localSearchActive = categorySearch.trim().length >= 2;
  const displayedHits = localSearchActive
    ? [...aiHits.filter((a) => !searchHits.some((h) => h.id === a.id)), ...searchHits]
    : aiHits;
  const showSearchList = localSearchActive || aiHits.length > 0;

  useEffect(
    () => () => {
      if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    },
    [],
  );

  const handleCatalogSubmit = async () => {
    if (!catalogShop) return;
    const values = await catalogForm.validateFields();
    const categories = (values.categories ?? []).filter((c) => String(c.id ?? '').trim());
    if (categories.length === 0) {
      message.error('Chọn ít nhất một category cho shop.');
      return;
    }
    if (
      values.priceMinUsd != null &&
      values.priceMaxUsd != null &&
      values.priceMinUsd > values.priceMaxUsd
    ) {
      message.error('Giá tối thiểu không được lớn hơn giá tối đa.');
      return;
    }

    let ageMin: number | null = null;
    let ageMax: number | null = null;
    if (values.ageRangeCode) {
      const range = ageRanges.find((r) => r.code === values.ageRangeCode);
      if (!range) {
        message.error('Khoảng tuổi không hợp lệ.');
        return;
      }
      ageMin = range.ageMin;
      ageMax = range.ageMax;
    } else if (
      !clearCustomAge &&
      !catalogForm.isFieldTouched('ageRangeCode') &&
      !findAgeRangeByBounds(catalogShop.ageMin, catalogShop.ageMax, ageRanges)
    ) {
      // Unmatched custom ages: keep existing until user picks/clears.
      ageMin = catalogShop.ageMin ?? null;
      ageMax = catalogShop.ageMax ?? null;
    }

    try {
      const response = await updateCatalog.mutateAsync({
        id: catalogShop.id,
        data: {
          categories: categories.map((c) => ({
            id: String(c.id).trim(),
            name: c.name?.trim() || String(c.id).trim(),
          })),
          ageMin,
          ageMax,
          priceMinUsd: values.priceMinUsd ?? null,
          priceMaxUsd: values.priceMaxUsd ?? null,
          preferQuickShip: !!values.preferQuickShip,
          preferLocal: !!values.preferLocal,
          preferUsWarehouse: !!values.preferUsWarehouse,
          preferSheinClub: !!values.preferSheinClub,
          minRating: Number(values.minRating) || 0,
          minSoldCount: Number(values.minSoldCount) || 0,
          maxVariantCount: Number(values.maxVariantCount) || 0,
          bodySizeCode: values.bodySizeCode || null,
          styleCodes: normalizeStyleCodes(values.styleCodes),
        },
      });
      if (response.success) closeCatalog();
    } catch {
      // Hook already toasts network/API errors; keep the modal open.
    }
  };

  const closeViewCard = () => {
    setViewingCardId(null);
  };

  const receivingBankLabel = (shop: ShopDetail) => {
    if (shop.receivingBankSourceName && shop.receivingBankSourceAccountName) {
      return `${shop.receivingBankSourceName} · ${shop.receivingBankSourceAccountName}`;
    }
    return (
      shop.receivingBankSourceAccountName ||
      shop.receivingBankSourceName ||
      shop.receivingBankAccountHolderName ||
      shop.receivingBankAccountName ||
      undefined
    );
  };

  const buildPayload = (values: ShopFormValues) => {
    const base = {
      name: values.name,
      externalShopId: values.externalShopId,
      avatarUrl: values.avatarUrl,
      fourSellerShopId: values.fourSellerShopId,
      shopUrl: values.shopUrl,
      status: values.status,
      proxy: values.proxy,
      profileName: values.profileName,
      email: values.email,
      tiktokPassword: values.tiktokPassword,
      dayLive: values.dayLive,
      deli: values.deli,
      newPhoneNumber: values.newPhoneNumber,
      paymentName: values.paymentName,
      shopType: values.shopType,
      llcName: values.llcName,
      twoFactorAuth: values.twoFactorAuth,
    };

    if (config.portal === 'seller') {
      return {
        ...base,
        newBankNumber: values.newBankNumber,
      };
    }

    return {
      ...base,
      ownerId: values.ownerId,
      platformId: values.platformId,
      region: values.region,
      receivingBankAccountId: values.receivingBankAccountId || null,
    };
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    const nextCost = values.acquisitionCost != null && values.acquisitionCost > 0 ? values.acquisitionCost : null;
    const prevCost =
      editingShop?.acquisitionCost != null && editingShop.acquisitionCost > 0
        ? editingShop.acquisitionCost
        : null;
    const costChanged =
      canSetAcquisition &&
      (nextCost == null
        ? prevCost != null
        : prevCost == null || Math.abs(nextCost - prevCost) >= 0.001);

    const saveCost = async (shopId: string) => {
      if (!costChanged) return;
      await deps.setAcquisitionCost?.mutateAsync({ shopId, amount: nextCost });
    };

    if (editingShop) {
      const ownerChanged =
        config.requireOwnerOnForm &&
        !!values.ownerId &&
        values.ownerId !== editingShop.ownerId;

      const runUpdate = async () => {
        const response = await updateShop.mutateAsync({
          id: editingShop.id,
          data: buildPayload(values),
        });
        if (!response.success) return;
        try {
          await saveCost(editingShop.id);
        } catch (error) {
          message.error(error instanceof Error ? error.message : 'Không lưu được giá mua shop.');
          return;
        }
        setModalOpen(false);
      };

      if (ownerChanged) {
        Modal.confirm({
          title: 'Chuyển shop sang seller khác?',
          content:
            'Đơn hàng, listing và dữ liệu gắn shop sẽ chuyển sang seller mới (cookie 4Seller / Mark Ship theo seller mới). Tiếp tục?',
          okText: 'Chuyển giao',
          cancelText: 'Hủy',
          onOk: runUpdate,
        });
        return;
      }

      await runUpdate();
    } else {
      if (!createShop) return;
      const response = await createShop.mutateAsync(buildPayload(values) as never);
      if (!response.success || !response.data?.id) return;
      try {
        await saveCost(response.data.id);
      } catch (error) {
        message.error(error instanceof Error ? error.message : 'Shop đã tạo, nhưng chưa lưu được giá mua.');
        return;
      }
      setModalOpen(false);
    }
  };

  const handleReassignOwnedData = () => {
    if (!editingShop || !reassignOwnedData) return;
    Modal.confirm({
      title: 'Đồng bộ ownership dữ liệu shop?',
      content:
        'Chuyển đơn/listing còn gắn seller cũ sang seller hiện tại của shop; gộp shell listing trùng TikTok product (giữ bản đủ data).',
      okText: 'Đồng bộ',
      cancelText: 'Hủy',
      onOk: async () => {
        await reassignOwnedData.mutateAsync(editingShop.id);
      },
    });
  };

  const handleReassignAllOwnedData = () => {
    if (!reassignAllOwnedData) return;
    Modal.confirm({
      title: 'Đồng bộ ownership tất cả shop?',
      content:
        'Quét mọi shop còn đơn/listing lệch seller hoặc shell trùng TikTok product, chuyển về seller hiện tại và gộp bản trùng. Có thể mất vài phút.',
      okText: 'Đồng bộ tất cả',
      cancelText: 'Hủy',
      onOk: async () => {
        await reassignAllOwnedData.mutateAsync();
      },
    });
  };

  const handleAutoPublishSubmit = async () => {
    if (!autoPublishShop) return;
    const values = await autoPublishForm.validateFields();
    const response = await updateAutoPublish.mutateAsync({
      id: autoPublishShop.id,
      data: {
        autoPublishEnabled: systemAutoPublishEnabled ? !!values.autoPublishEnabled : false,
        autoPublishIntervalSeconds:
          values.autoPublishIntervalSeconds != null
            ? Math.max(1, Math.floor(Number(values.autoPublishIntervalSeconds)))
            : null,
        autoPublishMinDraftAgeMinutes:
          values.autoPublishMinDraftAgeMinutes != null
            ? Math.max(
                LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES,
                Math.floor(Number(values.autoPublishMinDraftAgeMinutes)),
              )
            : null,
      },
    });
    if (response.success) closeAutoPublish();
  };

  const columns: ColumnsType<ShopListItem> = [
    {
      title: '#',
      key: 'index',
      fixed: isMobile ? undefined : 'left',
      width: isMobile ? 48 : 64,
      align: 'center',
      render: (_, record, index) => {
        const displayIndex = pageStart + index + 1;
        if (!canReorder) return displayIndex;

        return (
          <Tooltip title="Nhấn giữ để kéo thay đổi thứ tự">
            <span
              className={`shop-sort-handle${reorderShops?.isPending || isMobile ? ' is-disabled' : ''}`}
              draggable={canDragSort}
              onDragStart={(e: DragEvent<HTMLSpanElement>) => {
                if (!canDragSort) {
                  e.preventDefault();
                  return;
                }
                dragSourceIdRef.current = record.id;
                setDraggingId(record.id);
                e.dataTransfer.effectAllowed = 'move';
                e.dataTransfer.setData('text/plain', record.id);
              }}
              onDragEnd={clearDragState}
              onDragOver={(e: DragEvent<HTMLSpanElement>) => {
                if (!canDragSort || !dragSourceIdRef.current) return;
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
                if (dragOverId !== record.id) setDragOverId(record.id);
              }}
              onDrop={(e: DragEvent<HTMLSpanElement>) => {
                e.preventDefault();
                e.stopPropagation();
                handleDropReorder(record.id);
              }}
            >
              <HolderOutlined />
              {displayIndex}
            </span>
          </Tooltip>
        );
      },
    },
    {
      title: 'Profile',
      dataIndex: 'profileName',
      key: 'profileName',
      fixed: isMobile ? undefined : 'left',
      width: isMobile ? 130 : 180,
      ellipsis: true,
      render: (_, record) => {
        const profile = record.profileName?.trim() || '';
        const seller =
          config.showOwnerColumn && record.ownerFullName?.trim()
            ? record.ownerFullName.trim()
            : '';
        const shown = [profile || (seller ? '—' : ''), seller].filter(Boolean).join(' · ');
        return (
          <span style={tableCellRow}>
            <span style={{ display: 'inline-flex', lineHeight: 0, flexShrink: 0 }}>
              <PlatformAvatar platformCode={record.platformCode} platforms={platforms} size={16} />
            </span>
            <span style={{ minWidth: 0, flex: 1 }}>
              {copyableEllipsis(profile || seller || undefined, shown || undefined)}
            </span>
          </span>
        );
      },
    },
    {
      title: 'Shop',
      dataIndex: 'name',
      key: 'name',
      fixed: isMobile ? undefined : 'left',
      width: isMobile ? 170 : 220,
      ellipsis: true,
      render: (_, record) => {
        const idHint = [record.externalShopId, record.shopCode].filter(Boolean).join(' · ');
        return (
          <span style={tableCellRow} title={idHint || undefined}>
            {shopTypeTag(record.shopType)}
            <span style={{ minWidth: 0, flex: 1 }}>{copyableEllipsis(record.name)}</span>
            <ShopScoreBadges scores={record} />
            {shopDeliBadge(record.deli)}
          </span>
        );
      },
    },
    {
      title: 'Danh mục',
      key: 'catalog',
      width: 220,
      render: (_, record) => catalogCell(record),
    },
    {
      title: 'Listing',
      key: 'publishLimit',
      width: 100,
      align: 'right',
      render: (_, record) => {
        const pub = record.publishLimitPublished;
        const total = record.publishLimitTotal;
        const text =
          pub != null && total != null
            ? `${pub.toLocaleString('en-US')}/${total.toLocaleString('en-US')}`
            : pub != null
              ? pub.toLocaleString('en-US')
              : '—';
        return (
          <Tooltip title="published / limit từ TikTok Shop (publish_limit)">
            <Typography.Text style={{ fontVariantNumeric: 'tabular-nums' }}>{text}</Typography.Text>
          </Tooltip>
        );
      },
    },
    {
      title: 'Online',
      key: 'todayOnline',
      width: 88,
      align: 'right',
      render: (_, record) => (
        <Tooltip title="Tổng thời gian online hôm nay (ngày VN) — tab seller-us">
          <Typography.Text style={{ fontVariantNumeric: 'tabular-nums' }}>
            {formatTodayOnline(record.todayOnlineSeconds)}
          </Typography.Text>
        </Tooltip>
      ),
    },
    {
      title: 'Bank',
      key: 'hasReceivingBank',
      width: 140,
      render: (_, record) => {
        const status = record.bankMatchStatus as ShopBankMatchStatus | null | undefined;
        const linked = !!record.receivingBankAccountId;

        if (!status) {
          return (
            <Tag
              color={linked ? 'green' : 'orange'}
              style={{ margin: 0, cursor: linked ? 'pointer' : 'default' }}
              title={linked ? 'Xem thông tin thẻ' : undefined}
              onClick={(e) => {
                e.stopPropagation();
                if (linked) openViewCard(record);
              }}
            >
              {linked ? 'Đã gắn' : 'Chưa gắn'}
            </Tag>
          );
        }

        const label =
          status === 'Matched' && record.receivingBankLast4
            ? `Khớp ****${record.receivingBankLast4}`
            : (SHOP_BANK_MATCH_LABELS[status] ?? status);

        const tooltipParts = [
          record.settlementBankLast4
            ? `TTS ****${record.settlementBankLast4}`
            : 'TTS —',
          record.receivingBankLast4
            ? `Hệ thống ****${record.receivingBankLast4}`
            : 'Hệ thống —',
        ];

        return (
          <Tooltip title={tooltipParts.join(' · ')}>
            <Tag
              color={SHOP_BANK_MATCH_COLORS[status] ?? 'default'}
              style={{ margin: 0, cursor: linked ? 'pointer' : 'default' }}
              onClick={(e) => {
                e.stopPropagation();
                if (linked) openViewCard(record);
              }}
            >
              {label}
            </Tag>
          </Tooltip>
        );
      },
    },
    {
      title: '4Seller',
      key: 'fourSellerConnectionStatus',
      width: 120,
      render: (_, record) => {
        const status = resolveFourSellerStatus(record);
        const label = fourSellerStatusLabel[status] ?? status;
        return (
          <Tag color={fourSellerStatusColors[status] ?? 'default'} style={{ margin: 0 }}>
            {label}
          </Tag>
        );
      },
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (status: string) => (
        <Tag color={SHOP_STATUS_COLORS[status] ?? 'default'} style={{ margin: 0 }}>
          {SHOP_STATUS_LABELS[status] ?? status}
        </Tag>
      ),
    },
    {
      title: 'Auto publish',
      key: 'autoPublish',
      width: 180,
      render: (_, record) => {
        if (!systemAutoPublishEnabled && record.autoPublishEnabled) {
          return (
            <Tag color="default" style={{ margin: 0, fontSize: 11 }}>
              System off
            </Tag>
          );
        }
        if (!record.autoPublishEnabled) {
          return (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              Tắt
            </Typography.Text>
          );
        }
        const sec = effectiveAutoPublishIntervalSeconds(
          record.autoPublishIntervalSeconds,
          autoPublishDefaultInterval,
        );
        const minAge = effectiveAutoPublishMinDraftAgeMinutes(
          record.autoPublishMinDraftAgeMinutes,
          autoPublishDefaultMinDraftAge,
        );
        return (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'flex-start' }}>
            <Tag color="purple" style={{ margin: 0, fontSize: 11 }}>
              ON · {sec}s · chờ {minAge}p
            </Tag>
            {systemAutoPublishEnabled ? (
              <ShopAutoPublishSchedulerBadge
                status={record.autoPublishSchedulerStatus}
                nextPickAt={record.autoPublishNextPickAt}
                eligibleDraftCount={record.autoPublishEligibleDraftCount}
              />
            ) : null}
          </div>
        );
      },
    },
    {
      title: 'Ngày thêm',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 140,
      render: (createdAt: string) => (
        <Typography.Text style={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
          {formatDateTimeVn(createdAt)}
        </Typography.Text>
      ),
    },
    {
      title: '',
      key: 'actions',
      fixed: isMobile ? undefined : 'right',
      width: canManage
        ? canDelete
          ? isMobile
            ? 152
            : 172
          : isMobile
            ? 128
            : 144
        : config.enableViewModal
          ? 40
          : 0,
      align: 'right',
      render: (_, record) => (
        <Space size={0} onClick={(e) => e.stopPropagation()}>
          {config.enableViewModal ? (
            <Button
              type="text"
              size="small"
              icon={<EyeOutlined />}
              title="Xem chi tiết"
              onClick={() => openView(record)}
            />
          ) : null}
          {canManage ? (
            (() => {
              const complete = isShopCatalogAudienceComplete(record);
              const catCount = shopCatalogCategoryCount(record);
              return (
                <Badge
                  count={complete ? catCount : 0}
                  size="small"
                  offset={[-2, 2]}
                  className="shop-catalog-btn-badge"
                >
                  <Button
                    type="text"
                    size="small"
                    icon={<AppstoreOutlined />}
                    className={
                      complete
                        ? 'shop-catalog-btn is-complete'
                        : 'shop-catalog-btn is-incomplete'
                    }
                    title={
                      complete
                        ? `Danh mục — đã đủ (${catCount} category)`
                        : 'Danh mục — chưa đủ hồ sơ khách hàng'
                    }
                    onClick={() => openCatalog(record)}
                  />
                </Badge>
              );
            })()
          ) : null}
          {canManage ? (
            <Tooltip
              title={
                !systemAutoPublishEnabled
                  ? 'Auto publish hệ thống đang tắt (Settings)'
                  : record.autoPublishEnabled
                    ? `Auto publish: Bật · ${effectiveAutoPublishIntervalSeconds(
                        record.autoPublishIntervalSeconds,
                        autoPublishDefaultInterval,
                      )}s`
                    : 'Cấu hình auto publish'
              }
            >
              <Button
                type="text"
                size="small"
                icon={<ThunderboltOutlined />}
                className={
                  record.autoPublishEnabled && systemAutoPublishEnabled
                    ? 'shop-auto-publish-btn is-on'
                    : 'shop-auto-publish-btn'
                }
                title="Cấu hình auto publish"
                onClick={() => openAutoPublish(record)}
              />
            </Tooltip>
          ) : null}
          {canManage ? (
            <Button
              type="text"
              size="small"
              icon={<EditOutlined />}
              title="Sửa"
              onClick={() => void openEdit(record)}
            />
          ) : null}
          {canDelete && deleteShop ? (
            <Popconfirm title="Xóa shop này?" onConfirm={() => deleteShop.mutate(record.id)}>
              <Button type="text" size="small" danger icon={<DeleteOutlined />} title="Xóa" />
            </Popconfirm>
          ) : null}
        </Space>
      ),
    },
  ];

  const gatedColumns = columns.filter((col) => {
    const key = String(col.key);
    if (key === 'publishLimit' && !config.showActiveListingCount) {
      return false;
    }
    if (key === 'catalog' && !config.showCatalogColumn) return false;
    if (key === 'hasReceivingBank' && !config.showReceivingBankColumn) return false;
    return true;
  });

  const visibleColumns = !isMobile
    ? gatedColumns
    : gatedColumns
        .filter((col) => MOBILE_SHOP_COLUMN_KEYS.has(String(col.key)))
        .map((col) =>
          col.key === 'index' || col.key === 'profileName' || col.key === 'name'
            ? { ...col, fixed: undefined }
            : col,
        );

  return (
    <div>
      <PageHeader
        title={isMobile ? 'Shop' : 'Quản lý Shop'}
        extra={
          <Space wrap size={8}>
            <Tooltip title="Xuất Excel theo bộ lọc hiện tại">
              <Button
                icon={<FileExcelOutlined />}
                disabled={filteredShops.length === 0}
                onClick={() => {
                  try {
                    exportShopsExcel(filteredShops);
                    message.success(`Đã xuất ${filteredShops.length} shop`);
                  } catch {
                    message.error('Xuất Excel thất bại');
                  }
                }}
              >
                {isMobile ? null : 'Xuất Excel'}
              </Button>
            </Tooltip>
            {canImport ? (
              <Tooltip title="Import shop từ file Excel">
                <Button icon={<UploadOutlined />} onClick={openImport}>
                  {isMobile ? null : 'Import shop'}
                </Button>
              </Tooltip>
            ) : null}
            {canCreate ? (
              <Tooltip title="Thêm shop">
                <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
                  {isMobile ? null : 'Thêm shop'}
                </Button>
              </Tooltip>
            ) : null}
          </Space>
        }
      />

      {mappingHealthQuery?.data && mappingHealthQuery.data.unlinkedOrderCount > 0 ? (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12, fontSize: 12 }}
          message={`${mappingHealthQuery.data.unlinkedOrderCount} đơn chưa gắn shop (ShopId trống)`}
          description={
            <div style={{ fontSize: 11, lineHeight: 1.45 }}>
              <div style={{ marginBottom: 4 }}>
                Thường do shop chuyển seller / reconnect 4Seller (id nội bộ đổi). Đồng bộ shop
                4Seller để relink id, rồi đổi owner shop + Đồng bộ ownership. Chưa đổi owner thì
                đơn vẫn thuộc seller cũ và có thể thiếu enrich giá TikTok.
              </div>
              {(mappingHealthQuery.data.groups ?? []).slice(0, 8).map((g) => (
                <div key={`${g.fourSellerShopId ?? 'none'}-${g.ownerId ?? ''}`}>
                  4Seller #{g.fourSellerShopId ?? '—'} · {g.orderCount} đơn ·{' '}
                  {g.ownerName || g.ownerId || '—'}
                  {g.hasLocalShop ? ' · đã có shop local' : ' · chưa có shop local'}
                </div>
              ))}
            </div>
          }
          action={
            reassignAllOwnedData && canManage ? (
              <Button size="small" loading={reassignAllOwnedData.isPending} onClick={handleReassignAllOwnedData}>
                Đồng bộ ownership
              </Button>
            ) : undefined
          }
        />
      ) : null}

      <div className="admin-filters">
        <Input
          className="admin-filters-search"
          allowClear
          placeholder={
            config.showSellerFilter
              ? 'Tìm shop, seller, email, proxy, profile, ID...'
              : 'Tìm shop, email, proxy, profile, ID...'
          }
          value={filters.search}
          onChange={(e) => updateFilters({ search: e.target.value })}
        />
        <div className="admin-filters-grid">
          {config.showSellerFilter ? (
            <Select
              allowClear
              showSearch
              placeholder="Seller"
              style={{ width: 200 }}
              options={sellerFilterOptions}
              value={filters.ownerId}
              optionFilterProp="label"
              onChange={(value) => updateFilters({ ownerId: value })}
            />
          ) : null}
          <Select
            allowClear
            showSearch
            placeholder="Platform"
            style={{ width: 120 }}
            options={platformOptions}
            loading={platformsLoading}
            value={filters.platformId}
            optionFilterProp="label"
            onChange={(value) => updateFilters({ platformId: value })}
          />
          <Select
            allowClear
            showSearch
            placeholder="Region"
            style={{ width: 90 }}
            options={regionOptions}
            value={filters.region}
            optionFilterProp="label"
            onChange={(value) => updateFilters({ region: value })}
          />
          <Select
            allowClear
            placeholder="PER/LLC"
            style={{ width: 100 }}
            options={SHOP_TYPES.map((t) => ({ label: t.label, value: t.value }))}
            value={filters.shopType}
            onChange={(value) => updateFilters({ shopType: value })}
          />
          <Select
            allowClear
            showSearch
            placeholder="Danh mục"
            style={{ width: 180 }}
            options={categoryFilterOptions}
            value={filters.categoryName}
            optionFilterProp="label"
            onChange={(value) => updateFilters({ categoryName: value })}
          />
          {config.showReceivingBankColumn ? (
            <Select
              allowClear
              placeholder="Bank"
              style={{ width: 150 }}
              options={SHOP_BANK_MATCH_STATUSES.map((s) => ({
                label: SHOP_BANK_MATCH_LABELS[s],
                value: s,
              }))}
              value={filters.bankMatch}
              onChange={(value) => updateFilters({ bankMatch: value })}
            />
          ) : null}
          <Select
            placeholder="Sắp xếp"
            style={{ width: 160 }}
            options={SHOP_LIST_SORT_OPTIONS}
            value={filters.sortBy}
            onChange={(value: ShopListSortBy) => updateFilters({ sortBy: value })}
          />
          <Tooltip title={filters.sortDir === 'asc' ? 'Tăng dần' : 'Giảm dần'}>
            <Button
              aria-label={filters.sortDir === 'asc' ? 'Đang tăng dần' : 'Đang giảm dần'}
              icon={
                filters.sortDir === 'asc' ? <SortAscendingOutlined /> : <SortDescendingOutlined />
              }
              onClick={() =>
                updateFilters({ sortDir: filters.sortDir === 'asc' ? 'desc' : 'asc' })
              }
            />
          </Tooltip>
        </div>
        <div className="admin-filters-actions">
          {hasActiveFilters && (
            <Button icon={<ReloadOutlined />} onClick={resetFilters}>
              Xóa bộ lọc
            </Button>
          )}
          <Button
            icon={<ReloadOutlined />}
            onClick={() => void refetch()}
            loading={isFetching}
          >
            Tải lại
          </Button>
        </div>
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 12,
          marginBottom: 8,
          flexWrap: 'wrap',
        }}
      >
        <Tabs
          className="admin-status-tabs"
          size="small"
          activeKey={filters.status ?? ''}
          onChange={(key) => updateFilters({ status: key || undefined })}
          items={[
            { key: '', label: 'Tất cả', count: shopStatusCounts.all },
            ...SHOP_STATUSES.map((status) => ({
              key: status,
              label: SHOP_STATUS_LABELS[status] ?? status,
              count: shopStatusCounts.byStatus.get(status) ?? 0,
            })),
          ].map((tab) => ({
            key: tab.key,
            label: (
              <span>
                {tab.label}{' '}
                <Badge
                  count={tab.count}
                  overflowCount={99999}
                  style={{ marginLeft: 4 }}
                />
              </span>
            ),
          }))}
          style={{ marginBottom: 0, flex: 1, minWidth: 0 }}
        />
      </div>

      <div className="admin-table-wrap">
      <Table
        rowKey="id"
        columns={visibleColumns}
        dataSource={filteredShops}
        loading={isLoading || !!reorderShops?.isPending}
        size="small"
        tableLayout="fixed"
        scroll={{
          x: isMobile
            ? 480
            : 850 +
              (config.showCatalogColumn ? 220 : 0) +
              (config.showActiveListingCount ? 100 : 0) +
              (config.showReceivingBankColumn ? 140 : 0),
        }}
        styles={{ body: { cell: { whiteSpace: isMobile ? 'normal' : 'nowrap' } } }}
        onRow={(record) => ({
          onClick: () => {
            if (config.enableViewModal) openView(record);
          },
          style: { cursor: config.enableViewModal ? 'pointer' : undefined },
          onDragOver: (e: DragEvent<HTMLTableRowElement>) => {
            if (!canDragSort || !dragSourceIdRef.current) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            if (dragOverId !== record.id) setDragOverId(record.id);
          },
          onDrop: (e: DragEvent<HTMLTableRowElement>) => {
            e.preventDefault();
            handleDropReorder(record.id);
          },
          onDragLeave: () => {
            if (dragOverId === record.id) setDragOverId(null);
          },
        })}
        rowClassName={(record) => {
          const classes: string[] = [];
          if (record.status === 'Suspended') classes.push('shop-row-suspended');
          if (record.status === 'Deactivated') classes.push('shop-row-deactivated');
          if (draggingId === record.id) classes.push('shop-row-dragging');
          if (dragOverId === record.id && draggingId !== record.id) classes.push('shop-row-drag-over');
          return classes.join(' ');
        }}
        pagination={tablePagination(filteredShops.length, {
          pageSize: pagination.pageSize,
          current: pagination.current,
          showTotal: (total) =>
            `${total} shop${canReorder && hasActiveFilters ? ' (kéo-thả chỉ đổi chỗ các dòng đang thấy)' : ''}`,
          onChange: (page, pageSize) => setPagination({ current: page, pageSize }),
        })}
      />
      </div>

      <Modal
        title={
          viewingCard
            ? `Thẻ · ${viewingCard.accountHolderName || viewingCard.name}`
            : 'Thông tin thẻ'
        }
        open={!!viewingCardId}
        onCancel={closeViewCard}
        className="shop-view-modal"
        destroyOnHidden
        centered={!isMobile}
        width={isMobile ? '100%' : 520}
        styles={{
          body: {
            paddingTop: 8,
            ...(isMobile ? { maxHeight: '70vh', overflowY: 'auto' as const } : {}),
          },
        }}
        footer={<Button onClick={closeViewCard}>Đóng</Button>}
      >
        <Spin spinning={bankAccountLoading || bankAccountFetching}>
          {viewingCard ? (
            <Descriptions
              className="shop-view-descriptions"
              bordered
              size="small"
              column={1}
              styles={
                isMobile
                  ? { label: { width: 96, padding: '6px 8px' }, content: { padding: '6px 8px' } }
                  : undefined
              }
            >
              <Descriptions.Item label="Nguồn thẻ">
                {copyableEllipsis(viewingCard.sourceName)}
              </Descriptions.Item>
              <Descriptions.Item label="Tài khoản nguồn">
                {copyableEllipsis(viewingCard.sourceAccountName)}
              </Descriptions.Item>
              <Descriptions.Item label="Account currency">
                {viewingCard.currency || '—'}
              </Descriptions.Item>
              <Descriptions.Item label="Bank name">
                {copyableEllipsis(viewingCard.bankName)}
              </Descriptions.Item>
              <Descriptions.Item label="Bank location">
                {copyableEllipsis(viewingCard.bankLocation)}
              </Descriptions.Item>
              <Descriptions.Item label="Account holder's name">
                {copyableEllipsis(viewingCard.accountHolderName)}
              </Descriptions.Item>
              <Descriptions.Item label="Account type">
                {copyableEllipsis(viewingCard.accountType)}
              </Descriptions.Item>
              <Descriptions.Item label="SWIFT / BIC">
                {copyableEllipsis(viewingCard.swiftBic)}
              </Descriptions.Item>
              <Descriptions.Item label="Routing number">
                {copyableEllipsis(viewingCard.routingNumber)}
              </Descriptions.Item>
              <Descriptions.Item label="Bank account number">
                {copyableEllipsis(viewingCard.accountNumber)}
              </Descriptions.Item>
              <Descriptions.Item label="Bank address">
                {copyableEllipsis(viewingCard.bankAddress)}
              </Descriptions.Item>
            </Descriptions>
          ) : !bankAccountLoading && !bankAccountFetching ? (
            <Typography.Text type="secondary">Không tải được chi tiết thẻ.</Typography.Text>
          ) : (
            <div style={{ minHeight: 80 }} />
          )}
        </Spin>
      </Modal>

      <Modal
        title={
          viewingShop ? (
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
              <Avatar src={viewingShop.avatarUrl} icon={<ShopOutlined />} size={28} />
              <span style={{ ...ellipsisStyle, fontWeight: 600 }}>{viewingShop.name}</span>
            </span>
          ) : (
            'Chi tiết shop'
          )
        }
        open={!!viewingShopId}
        onCancel={() => setViewingShopId(null)}
        className="shop-view-modal"
        destroyOnHidden
        centered={!isMobile}
        width={isMobile ? '100%' : 720}
        styles={{
          body: {
            paddingTop: 8,
            maxHeight: isMobile ? '70vh' : 'calc(100vh - 160px)',
            overflowY: 'auto',
          },
        }}
        footer={
          <Space>
            <Button onClick={() => setViewingShopId(null)}>Đóng</Button>
            {canManage && viewingShop ? (
              <Button
                type="primary"
                icon={<EditOutlined />}
                onClick={() => {
                  const row = viewingShop;
                  setViewingShopId(null);
                  void openEdit(row);
                }}
              >
                Sửa
              </Button>
            ) : null}
          </Space>
        }
      >
        <Spin spinning={viewingShopLoading || viewingShopFetching}>
          {viewingShop ? (
            <div>
              <div
                style={{
                  display: 'flex',
                  flexWrap: 'wrap',
                  alignItems: 'center',
                  gap: 8,
                  marginBottom: 12,
                }}
              >
                {viewingShop.shopType ? (
                  <Tag
                    color={viewingShop.shopType === 'Llc' ? 'blue' : 'default'}
                    style={{ marginInlineEnd: 0 }}
                  >
                    {SHOP_TYPE_LABELS[viewingShop.shopType] ?? viewingShop.shopType}
                  </Tag>
                ) : null}
                {shopCatalogCategories(viewingShop).length > 0 ? (
                  shopCatalogCategories(viewingShop).map((c) => (
                    <Tag key={c.id} style={{ marginInlineEnd: 0 }} title={c.name}>
                      {categoryLeafName(c.name)}
                    </Tag>
                  ))
                ) : (
                  <Tag color="orange" style={{ marginInlineEnd: 0 }}>
                    Chưa chọn danh mục
                  </Tag>
                )}
                {viewingShop.bodySizeCode ? (
                  <Tag style={{ marginInlineEnd: 0 }}>{formatBodySizeLabel(viewingShop.bodySizeCode)}</Tag>
                ) : null}
                {normalizeStyleCodes(viewingShop.styleCodes).map((code) => (
                  <Tag key={code} style={{ marginInlineEnd: 0 }}>
                    {formatStyleLabel(code)}
                  </Tag>
                ))}
                {catalogProductFilterFlags(viewingShop).map((flag) => (
                  <Tag key={flag.key} color={flag.color} style={{ marginInlineEnd: 0 }}>
                    {flag.label}
                  </Tag>
                ))}
                <Tag color={SHOP_STATUS_COLORS[viewingShop.status] ?? 'default'} style={{ marginInlineEnd: 0 }}>
                  {SHOP_STATUS_LABELS[viewingShop.status] ?? viewingShop.status}
                </Tag>
                {(() => {
                  const status = resolveFourSellerStatus(viewingShop);
                  return (
                    <Tag
                      color={fourSellerStatusColors[status] ?? 'default'}
                      style={{ marginInlineEnd: 0 }}
                    >
                      {fourSellerStatusLabel[status] ?? status}
                    </Tag>
                  );
                })()}
                {viewingShop.profileName ? (
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {viewingShop.profileName}
                  </Typography.Text>
                ) : null}
              </div>

              <div className="shop-view-block">
                <div className="shop-view-section-title">Thông tin</div>
                <Descriptions
                  className="shop-view-descriptions"
                  bordered
                  size="small"
                  column={isMobile ? 1 : 2}
                  styles={{
                    label: isMobile
                      ? { width: 88, padding: '6px 8px', fontSize: 12 }
                      : { width: 100 },
                    ...(isMobile ? { content: { padding: '6px 8px' } } : {}),
                  }}
                >
                  {config.showOwnerColumn && viewingShop.ownerFullName ? (
                    <Descriptions.Item label="Seller">
                      {copyableEllipsis(
                        viewingShop.ownerUserName
                          ? `${viewingShop.ownerFullName} (${viewingShop.ownerUserName})`
                          : viewingShop.ownerFullName,
                      )}
                    </Descriptions.Item>
                  ) : null}
                  <Descriptions.Item label="Platform">
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, maxWidth: '100%' }}>
                      <PlatformAvatar platformCode={viewingShop.platformCode} platforms={platforms} />
                      <span style={{ minWidth: 0, flex: 1 }}>
                        {copyableEllipsis(
                          viewingShop.region
                            ? `${viewingShop.platformName} · ${viewingShop.region}`
                            : viewingShop.platformName,
                        )}
                      </span>
                    </span>
                  </Descriptions.Item>
                  <Descriptions.Item label="Danh mục" span={2}>
                    {shopCatalogCategories(viewingShop).length > 0 ? (
                      <span style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                        {shopCatalogCategories(viewingShop).map((c) => (
                          <Tag key={c.id} style={{ marginInlineEnd: 0 }} title={c.name}>
                            {categoryLeafName(c.name)}
                          </Tag>
                        ))}
                        {viewingShop.bodySizeCode ? (
                          <Tag style={{ marginInlineEnd: 0 }}>
                            {formatBodySizeLabel(viewingShop.bodySizeCode)}
                          </Tag>
                        ) : null}
                        {normalizeStyleCodes(viewingShop.styleCodes).map((code) => (
                          <Tag key={code} style={{ marginInlineEnd: 0 }}>
                            {formatStyleLabel(code)}
                          </Tag>
                        ))}
                        {catalogProductFilterFlags(viewingShop).map((flag) => (
                          <Tag key={flag.key} color={flag.color} style={{ marginInlineEnd: 0 }}>
                            {flag.label}
                          </Tag>
                        ))}
                      </span>
                    ) : (
                      'Chưa chọn'
                    )}
                  </Descriptions.Item>
                  <Descriptions.Item label="Tuổi">
                    {formatShopAgeLabel(viewingShop.ageMin, viewingShop.ageMax, ageRanges)}
                  </Descriptions.Item>
                  <Descriptions.Item label="Giá (USD)">
                    {viewingShop.priceMinUsd != null || viewingShop.priceMaxUsd != null
                      ? `${viewingShop.priceMinUsd ?? '—'}–${viewingShop.priceMaxUsd ?? '—'}`
                      : '—'}
                  </Descriptions.Item>
                  <Descriptions.Item label="Proxy" span={2}>
                    {copyableEllipsis(viewingShop.proxy)}
                  </Descriptions.Item>
                  <Descriptions.Item label="Phone">
                    {copyableEllipsis(viewingShop.newPhoneNumber)}
                  </Descriptions.Item>
                  <Descriptions.Item label="Day Live">
                    {copyableEllipsis(viewingShop.dayLive)}
                  </Descriptions.Item>
                  <Descriptions.Item label="DELI (hạn TT)">
                    {viewingShop.deli != null ? `${viewingShop.deli} ngày` : '—'}
                  </Descriptions.Item>
                  {config.showActiveListingCount ? (
                    <Descriptions.Item label="Listing">
                      {viewingShop.publishLimitPublished != null &&
                      viewingShop.publishLimitTotal != null
                        ? `${viewingShop.publishLimitPublished.toLocaleString('en-US')}/${viewingShop.publishLimitTotal.toLocaleString('en-US')}`
                        : viewingShop.publishLimitPublished != null
                          ? viewingShop.publishLimitPublished.toLocaleString('en-US')
                          : '—'}
                    </Descriptions.Item>
                  ) : null}
                  <Descriptions.Item label="Ngày tạo">
                    {formatDateTimeVn(viewingShop.createdAt)}
                  </Descriptions.Item>
                  <Descriptions.Item label="URL" span={2}>
                    {(() => {
                      const url =
                        viewingShop.shopUrl?.trim() ||
                        buildTikTokShopStoreUrl(viewingShop.name, viewingShop.externalShopId) ||
                        null;
                      if (!url) return '—';
                      return (
                        <Typography.Link
                          href={url}
                          target="_blank"
                          rel="noopener noreferrer"
                          style={{ wordBreak: 'break-all' }}
                        >
                          {url}
                        </Typography.Link>
                      );
                    })()}
                  </Descriptions.Item>
                </Descriptions>
              </div>

              {(() => {
                const hasTikTokProfile =
                  viewingShop.shopCode ||
                  viewingShop.tikTokShopStatus != null ||
                  viewingShop.timezone ||
                  viewingShop.tikTokCreatedAt ||
                  viewingShop.verifiedDocuments != null ||
                  viewingShop.sellerType != null ||
                  viewingShop.companyCertNum ||
                  viewingShop.companyAddress ||
                  viewingShop.ownerPerson ||
                  viewingShop.accountName ||
                  viewingShop.publishLimitPublished != null ||
                  viewingShop.publishLimitTotal != null ||
                  (viewingShop.logisticsServices?.length ?? 0) > 0 ||
                  (viewingShop.productCategories?.length ?? 0) > 0 ||
                  viewingShop.tikTokProfileSyncedAt ||
                  viewingShop.isBlocked ||
                  viewingShop.isBlocking ||
                  viewingShop.settlementBankAccountNumber;

                if (!hasTikTokProfile) return null;

                const companyAddr = formatShopAddress(viewingShop.companyAddress);
                const ownerAddr = formatShopAddress(viewingShop.ownerPerson?.address);
                const descStyles = {
                  label: isMobile
                    ? { width: 88, padding: '6px 8px', fontSize: 12 }
                    : { width: 100 },
                  ...(isMobile ? { content: { padding: '6px 8px' } } : {}),
                } as const;

                return (
                  <div className="shop-view-block">
                    <div className="shop-view-section-title">
                      TikTok profile
                      {viewingShop.tikTokProfileSyncedAt ? (
                        <Typography.Text type="secondary" style={{ marginLeft: 8, fontSize: 12, fontWeight: 400 }}>
                          sync {formatDateTimeVn(viewingShop.tikTokProfileSyncedAt)}
                        </Typography.Text>
                      ) : null}
                    </div>
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginBottom: 10 }}>
                      {viewingShop.region ? <Tag style={{ marginInlineEnd: 0 }}>{viewingShop.region}</Tag> : null}
                      {viewingShop.sellerType != null ? (
                        <Tag color="cyan" style={{ marginInlineEnd: 0 }}>
                          {formatTikTokSellerType(viewingShop.sellerType)}
                        </Tag>
                      ) : null}
                      {viewingShop.tikTokShopStatus != null ? (
                        <Tag
                          color={TIKTOK_SHOP_STATUS_COLORS[viewingShop.tikTokShopStatus]}
                          style={{ marginInlineEnd: 0 }}
                        >
                          {formatTikTokShopStatus(viewingShop.tikTokShopStatus)}
                        </Tag>
                      ) : null}
                      {viewingShop.isBlocked ? (
                        <Tag color="error" style={{ marginInlineEnd: 0 }}>
                          Bị chặn
                        </Tag>
                      ) : null}
                      {viewingShop.isBlocking ? (
                        <Tag color="warning" style={{ marginInlineEnd: 0 }}>
                          Đang chặn người khác
                        </Tag>
                      ) : null}
                    </div>
                    <Descriptions
                      className="shop-view-descriptions"
                      bordered
                      size="small"
                      column={isMobile ? 1 : 2}
                      styles={descStyles}
                    >
                      {viewingShop.shopCode ? (
                        <Descriptions.Item label="Shop code">
                          {copyableEllipsis(viewingShop.shopCode)}
                        </Descriptions.Item>
                      ) : null}
                      {viewingShop.tikTokCreatedAt ? (
                        <Descriptions.Item label="Tạo (TTS)">
                          {formatDateTimeVn(viewingShop.tikTokCreatedAt)}
                          {viewingShop.dayLive ? (
                            <Typography.Text type="secondary"> ({viewingShop.dayLive})</Typography.Text>
                          ) : null}
                        </Descriptions.Item>
                      ) : null}
                      {viewingShop.verifiedDocuments != null ? (
                        <Descriptions.Item label="Docs">
                          {viewingShop.verifiedDocuments} xác minh
                        </Descriptions.Item>
                      ) : null}
                      {viewingShop.timezone ? (
                        <Descriptions.Item label="TZ">
                          <Tooltip title={viewingShop.timezone}>
                            {formatTimezoneShort(viewingShop.timezone)}
                          </Tooltip>
                        </Descriptions.Item>
                      ) : null}
                      {viewingShop.llcName ? (
                        <Descriptions.Item label="Company" span={2}>
                          {copyableEllipsis(viewingShop.llcName)}
                        </Descriptions.Item>
                      ) : null}
                      {viewingShop.companyCertNum ? (
                        <Descriptions.Item label="EIN / Cert" span={2}>
                          <Tooltip
                            title={
                              viewingShop.companyCertType != null
                                ? `cert_type=${viewingShop.companyCertType}`
                                : undefined
                            }
                          >
                            {copyableEllipsis(viewingShop.companyCertNum)}
                          </Tooltip>
                        </Descriptions.Item>
                      ) : null}
                      {companyAddr ? (
                        <Descriptions.Item label="Địa chỉ" span={2}>
                          {copyableEllipsis(companyAddr)}
                        </Descriptions.Item>
                      ) : null}
                      {viewingShop.ownerPerson?.fullName ? (
                        <Descriptions.Item label="Owner" span={2}>
                          <span style={{ display: 'inline-flex', flexDirection: 'column', gap: 2 }}>
                            {copyableEllipsis(viewingShop.ownerPerson.fullName)}
                            {viewingShop.ownerPerson.birthday ? (
                              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                                {viewingShop.ownerPerson.birthday}
                                {viewingShop.ownerPerson.nationality
                                  ? ` · ${viewingShop.ownerPerson.nationality}`
                                  : ''}
                              </Typography.Text>
                            ) : null}
                          </span>
                        </Descriptions.Item>
                      ) : null}
                      {ownerAddr ? (
                        <Descriptions.Item label="Addr owner" span={2}>
                          {copyableEllipsis(ownerAddr)}
                        </Descriptions.Item>
                      ) : null}
                      {viewingShop.accountName ? (
                        <Descriptions.Item label="Tài khoản" span={2}>
                          {copyableEllipsis(viewingShop.accountName)}
                        </Descriptions.Item>
                      ) : null}
                      {viewingShop.email ? (
                        <Descriptions.Item label="Email" span={2}>
                          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, maxWidth: '100%' }}>
                            <span style={{ minWidth: 0, flex: 1 }}>{copyableEllipsis(viewingShop.email)}</span>
                            {viewingShop.emailVerified ? (
                              <Tooltip title="Đã xác minh">
                                <CheckCircleOutlined style={{ color: 'var(--ant-color-success)' }} />
                              </Tooltip>
                            ) : null}
                          </span>
                        </Descriptions.Item>
                      ) : null}
                      {viewingShop.newPhoneNumber || viewingShop.settlementBankAccountNumber ? (
                        <>
                          <Descriptions.Item label="Mobile">
                            {viewingShop.newPhoneNumber ? (
                              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, maxWidth: '100%' }}>
                                <span style={{ minWidth: 0, flex: 1 }}>
                                  {copyableEllipsis(viewingShop.newPhoneNumber)}
                                </span>
                                {viewingShop.phoneVerified ? (
                                  <Tooltip title="Đã xác minh">
                                    <CheckCircleOutlined style={{ color: 'var(--ant-color-success)' }} />
                                  </Tooltip>
                                ) : null}
                              </span>
                            ) : (
                              '—'
                            )}
                          </Descriptions.Item>
                          <Descriptions.Item label="Bank">
                            {viewingShop.settlementBankAccountNumber ? (
                              <Tooltip
                                title={
                                  [
                                    viewingShop.settlementPaymentMethod,
                                    viewingShop.settlementBeneficiaryName,
                                    viewingShop.settlementAccountType,
                                    viewingShop.settlementAccountStatus != null
                                      ? `status=${viewingShop.settlementAccountStatus}`
                                      : null,
                                  ]
                                    .filter(Boolean)
                                    .join(' · ') || 'Số TK settlement TikTok (masked)'
                                }
                              >
                                <span>
                                  {copyableEllipsis(
                                    viewingShop.settlementBankAccountNumber,
                                    viewingShop.settlementBankAccountNumber.replace(
                                      /[•●◦﹡＊✱⁎∗]/g,
                                      '*',
                                    ),
                                    { mono: true },
                                  )}
                                </span>
                              </Tooltip>
                            ) : (
                              '—'
                            )}
                          </Descriptions.Item>
                        </>
                      ) : null}
                    </Descriptions>
                    {(viewingShop.logisticsServices?.length ?? 0) > 0 ? (
                      <div style={{ marginTop: 12 }}>
                        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                          Shipping ({viewingShop.logisticsServices!.length})
                        </Typography.Text>
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, marginTop: 6 }}>
                          {viewingShop.logisticsServices!.map((svc, i) => {
                            const label = [svc.serviceName, svc.deliveryOptionName]
                              .filter(Boolean)
                              .join(' · ');
                            return (
                              <Tag
                                key={`${label}-${i}`}
                                color={svc.isDefault ? 'blue' : undefined}
                                style={{ marginInlineEnd: 0 }}
                              >
                                {label || '—'}
                              </Tag>
                            );
                          })}
                        </div>
                      </div>
                    ) : null}
                    {(viewingShop.productCategories?.length ?? 0) > 0 ? (
                      <div style={{ marginTop: 12 }}>
                        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                          Categories ({viewingShop.productCategories!.length})
                        </Typography.Text>
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, marginTop: 6 }}>
                          {viewingShop.productCategories!.map((cat) => (
                            <Tag key={cat.value || cat.label} title={cat.value} style={{ marginInlineEnd: 0 }}>
                              {cat.label || cat.value}
                            </Tag>
                          ))}
                        </div>
                      </div>
                    ) : null}
                  </div>
                );
              })()}

              {(() => {
                const scoreRows: { label: string; value?: number | null }[] = [
                  { label: 'Shop Performance', value: viewingShop.shopPerformanceScore },
                  { label: 'AHR score', value: viewingShop.ahrScore },
                  { label: 'Product satisfaction', value: viewingShop.productSatisfactionScore },
                  { label: 'Fulfillment', value: viewingShop.fulfillmentScore },
                  { label: 'Customer service', value: viewingShop.customerServiceScore },
                ];
                if (scoreRows.every((row) => row.value == null)) return null;

                return (
                  <div className="shop-view-block">
                    <div className="shop-view-section-title">
                      Điểm Health Center
                      {viewingShop.tikTokShopScoreSyncedAt ? (
                        <Typography.Text
                          type="secondary"
                          style={{ marginLeft: 8, fontSize: 12, fontWeight: 400 }}
                        >
                          sync {formatDateTimeVn(viewingShop.tikTokShopScoreSyncedAt)}
                        </Typography.Text>
                      ) : null}
                    </div>
                    <Descriptions
                      className="shop-view-descriptions"
                      bordered
                      size="small"
                      column={isMobile ? 1 : 2}
                      styles={{
                        label: isMobile
                          ? { width: 88, padding: '6px 8px', fontSize: 12 }
                          : { width: 140 },
                        ...(isMobile ? { content: { padding: '6px 8px' } } : {}),
                      }}
                    >
                      {scoreRows.map((row) => (
                        <Descriptions.Item key={row.label} label={row.label}>
                          {row.value ?? '—'}
                        </Descriptions.Item>
                      ))}
                    </Descriptions>
                  </div>
                );
              })()}

              <div className="shop-view-block">
                <div className="shop-view-section-title">Tài khoản</div>
                <Descriptions
                  className="shop-view-descriptions"
                  bordered
                  size="small"
                  column={isMobile ? 1 : 2}
                  styles={{
                    label: isMobile
                      ? { width: 88, padding: '6px 8px', fontSize: 12 }
                      : { width: 100 },
                    ...(isMobile ? { content: { padding: '6px 8px' } } : {}),
                  }}
                >
                  <Descriptions.Item label="Email" span={2}>
                    {copyableEllipsis(viewingShop.email)}
                  </Descriptions.Item>
                  <Descriptions.Item label="Password">
                    {maskedCopyCell(viewingShop.tiktokPassword, 'mật khẩu')}
                  </Descriptions.Item>
                  <Descriptions.Item label="2FA">
                    {twoFactorCopyCell(viewingShop.twoFactorAuth)}
                  </Descriptions.Item>
                </Descriptions>
              </div>

              <div className="shop-view-block">
                <div className="shop-view-section-title">Thanh toán</div>
                <Descriptions
                  className="shop-view-descriptions"
                  bordered
                  size="small"
                  column={isMobile ? 1 : 2}
                  styles={{
                    label: isMobile
                      ? { width: 88, padding: '6px 8px', fontSize: 12 }
                      : { width: 100 },
                    ...(isMobile ? { content: { padding: '6px 8px' } } : {}),
                  }}
                >
                  <Descriptions.Item label="Thẻ" span={2}>
                    {(() => {
                      const label = receivingBankLabel(viewingShop);
                      if (!label && !viewingShop.receivingBankAccountId) return '—';
                      return (
                        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, maxWidth: '100%' }}>
                          <span style={{ minWidth: 0, flex: 1 }}>
                            {label ? copyableEllipsis(label) : 'Đã gắn'}
                          </span>
                          {viewingShop.receivingBankAccountCurrency ? (
                            <Tag style={{ marginInlineEnd: 0, flexShrink: 0 }}>
                              {viewingShop.receivingBankAccountCurrency}
                            </Tag>
                          ) : null}
                        </span>
                      );
                    })()}
                  </Descriptions.Item>
                  <Descriptions.Item label="Số TK">
                    {(() => {
                      const full =
                        viewingShop.receivingBankAccountNumber || viewingShop.newBankNumber || undefined;
                      if (!full) return '—';
                      return copyableEllipsis(full, maskAccountNumber(full));
                    })()}
                  </Descriptions.Item>
                  <Descriptions.Item label="Tên TT">
                    {copyableEllipsis(viewingShop.paymentName)}
                  </Descriptions.Item>
                </Descriptions>
              </div>

              <div className="shop-view-block">
                <div className="shop-view-section-title">ID</div>
                <Descriptions
                  className="shop-view-descriptions"
                  bordered
                  size="small"
                  column={isMobile ? 1 : 2}
                  styles={{
                    label: isMobile
                      ? { width: 88, padding: '6px 8px', fontSize: 12 }
                      : { width: 100 },
                    ...(isMobile ? { content: { padding: '6px 8px' } } : {}),
                  }}
                >
                  <Descriptions.Item label="External">
                    {copyableEllipsis(viewingShop.externalShopId)}
                  </Descriptions.Item>
                  <Descriptions.Item label="Shop code">
                    {copyableEllipsis(viewingShop.shopCode)}
                  </Descriptions.Item>
                  <Descriptions.Item label="4Seller">
                    {copyableEllipsis(viewingShop.fourSellerShopId)}
                  </Descriptions.Item>
                  {viewingShop.updatedAt ? (
                    <Descriptions.Item label="Cập nhật" span={2}>
                      {formatDateTimeVn(viewingShop.updatedAt)}
                    </Descriptions.Item>
                  ) : null}
                </Descriptions>
              </div>
            </div>
          ) : (
            <div style={{ minHeight: 120 }} />
          )}
        </Spin>
      </Modal>

      <Modal
        title={
          catalogShop
            ? `Danh mục · ${catalogShop.name}`
            : 'Danh mục shop'
        }
        open={!!catalogShop}
        onCancel={closeCatalog}
        onOk={() => void handleCatalogSubmit()}
        confirmLoading={updateCatalog.isPending}
        destroyOnHidden
        centered={!isMobile}
        width={isMobile ? '100%' : 800}
        className="shop-edit-modal shop-catalog-modal"
        okText="Lưu"
        styles={{
          body: {
            paddingTop: 8,
            paddingBottom: 4,
            maxHeight: isMobile ? '70vh' : 'calc(100vh - 140px)',
            overflowY: 'auto',
          },
        }}
      >
        <Form
          form={catalogForm}
          layout="vertical"
          size="small"
          requiredMark={false}
          initialValues={{
            styleCodes: [],
            minRating: 0,
            minSoldCount: 0,
            maxVariantCount: 0,
            preferSheinClub: false,
          }}
        >
          <Form.Item
            name="categories"
            hidden
            rules={[
              {
                validator: async (_, value) => {
                  if (!Array.isArray(value) || value.length === 0) {
                    throw new Error('Chọn category');
                  }
                },
              },
            ]}
          />
          <div className="shop-catalog-grid">
            <Form.Item
              className="shop-form-span-2"
              label={
                <span className="shop-catalog-category-label">
                  <span>Category</span>
                  <Tooltip title="Site theo region của shop — category phải cùng site mới lưu được">
                    <Tag className="shop-catalog-site-badge">{catalogSite}</Tag>
                  </Tooltip>
                  <span className="shop-catalog-category-count">
                    {selectedCatalogCategories.length}/{CATALOG_CATEGORY_MAX}
                  </span>
                </span>
              }
              required
            >
              <div
                role="button"
                tabIndex={0}
                className="shop-category-picker-trigger"
                onClick={openCategoryPicker}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openCategoryPicker();
                  }
                }}
              >
                <span className="shop-category-picker-tags">
                  {selectedCatalogCategories.length > 0 ? (
                    selectedCatalogCategories.map((c) => (
                      <Tag
                        key={c.id}
                        closable
                        onClick={(e) => e.stopPropagation()}
                        onClose={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          const next = selectedCatalogCategories.filter((x) => x.id !== c.id);
                          catalogForm.setFieldsValue({ categories: next });
                          setCategoryPath(
                            next.length ? next.map((x) => [x.id]) : undefined,
                          );
                        }}
                        style={{ marginInlineEnd: 0 }}
                        title={c.name}
                      >
                        {categoryLeafName(c.name)}
                      </Tag>
                    ))
                  ) : (
                    <span className="shop-category-picker-placeholder">
                      Chọn category — tìm kiếm, AI hoặc duyệt cây
                    </span>
                  )}
                </span>
                <AppstoreOutlined className="shop-category-picker-icon" />
              </div>
            </Form.Item>
            <Form.Item name="ageRangeCode" label="Khoảng tuổi" className="shop-form-span-2">
              <Select
                className="shop-age-range-select"
                allowClear
                showSearch
                optionFilterProp="searchText"
                listItemHeight={64}
                listHeight={360}
                loading={ageRangesLoading}
                placeholder="Chọn khoảng tuổi"
                options={ageRanges.map((r) => ({
                  label: r.label,
                  value: r.code,
                  painPoint: r.painPoint,
                  productGroups: r.productGroups,
                  searchText: `${r.label} ${r.painPoint} ${r.productGroups} ${r.desire}`,
                }))}
                labelRender={(props) => {
                  const range = ageRanges.find((r) => r.code === props.value);
                  if (!range) return props.label;
                  const full = range.painPoint
                    ? `${range.label}  ${range.painPoint}`
                    : range.label;
                  return (
                    <span className="shop-age-range-selected" title={full}>
                      <span className="shop-age-range-selected-age">{range.label}</span>
                      {range.painPoint ? (
                        <span className="shop-age-range-selected-pain">{range.painPoint}</span>
                      ) : null}
                    </span>
                  );
                }}
                optionRender={(option) => {
                  const data = option.data as {
                    label?: ReactNode;
                    painPoint?: string;
                    productGroups?: string;
                  };
                  return (
                    <div className="shop-age-range-option">
                      <div className="shop-age-range-option-label">
                        <span className="shop-age-range-option-age">{data.label}</span>
                        {data.painPoint ? (
                          <span className="shop-age-range-option-pain">{data.painPoint}</span>
                        ) : null}
                      </div>
                      {data.productGroups ? (
                        <div className="shop-age-range-option-meta">{data.productGroups}</div>
                      ) : null}
                    </div>
                  );
                }}
                onChange={(code) => {
                  if (code) setClearCustomAge(false);
                }}
              />
            </Form.Item>
            {selectedAgeRange ? (
              <div className="shop-catalog-insight shop-form-span-2">
                {(
                  [
                    { label: 'Pain point', value: selectedAgeRange.painPoint },
                    { label: 'Muốn', value: selectedAgeRange.desire },
                    { label: 'Nên đánh', value: selectedAgeRange.productGroups },
                  ] as const
                ).map((item) => (
                  <Tooltip key={item.label} title={item.value || undefined}>
                    <span className="shop-catalog-insight-item">
                      <span className="shop-catalog-insight-label">{item.label}</span>
                      <span className="shop-catalog-insight-value">{item.value || '—'}</span>
                    </span>
                  </Tooltip>
                ))}
              </div>
            ) : catalogShop &&
              (catalogShop.ageMin != null || catalogShop.ageMax != null) &&
              !findAgeRangeByBounds(catalogShop.ageMin, catalogShop.ageMax, ageRanges) &&
              !ageRangeCodeValue &&
              !clearCustomAge ? (
              <div className="shop-catalog-custom-age shop-form-span-2">
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  Đang dùng khoảng tùy chỉnh{' '}
                  {formatShopAgeLabel(catalogShop.ageMin, catalogShop.ageMax)}
                </Typography.Text>
                <Button
                  type="link"
                  size="small"
                  style={{ paddingInline: 0 }}
                  onClick={() => setClearCustomAge(true)}
                >
                  Gỡ tuổi
                </Button>
              </div>
            ) : null}
            <Form.Item name="bodySizeCode" label="Body / size">
              <Select
                allowClear
                showSearch
                optionFilterProp="searchText"
                listItemHeight={52}
                listHeight={320}
                placeholder="Chọn nhóm body/size"
                options={BODY_SIZE_OPTIONS.map((o) => ({
                  label: o.label,
                  value: o.code,
                  meaning: o.meaning,
                  searchText: `${o.label} ${o.meaning}`,
                }))}
                optionRender={(option) => {
                  const data = option.data as {
                    label?: ReactNode;
                    meaning?: string;
                  };
                  return (
                    <div className="shop-age-range-option">
                      <div className="shop-age-range-option-label">
                        <span className="shop-age-range-option-age">{data.label}</span>
                      </div>
                      {data.meaning ? (
                        <div className="shop-age-range-option-meta">{data.meaning}</div>
                      ) : null}
                    </div>
                  );
                }}
              />
            </Form.Item>
            <Form.Item name="styleCodes" label="Style">
              <Select
                mode="multiple"
                allowClear
                showSearch
                maxTagCount="responsive"
                optionFilterProp="searchText"
                listItemHeight={56}
                listHeight={360}
                placeholder="Chọn style (có thể nhiều)"
                maxCount={STYLE_OPTIONS.length}
                options={STYLE_OPTIONS.map((o) => ({
                  label: o.label,
                  value: o.code,
                  meaning: o.meaning,
                  examples: o.examples,
                  searchText: `${o.label} ${o.meaning} ${o.examples}`,
                }))}
                tagRender={(props) => {
                  const { label, value, closable, onClose } = props;
                  const opt = STYLE_OPTIONS.find((o) => o.code === value);
                  const tip = opt ? (
                    <div className="shop-catalog-tag-tip">
                      <div>{opt.meaning}</div>
                      {opt.examples ? (
                        <div className="shop-catalog-tag-tip-meta">{opt.examples}</div>
                      ) : null}
                    </div>
                  ) : undefined;
                  return (
                    <Tooltip title={tip}>
                      <span
                        className="shop-catalog-style-tag-wrap"
                        onMouseDown={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                        }}
                      >
                        <Tag closable={closable} onClose={onClose} style={{ marginInlineEnd: 0 }}>
                          {label}
                        </Tag>
                      </span>
                    </Tooltip>
                  );
                }}
                optionRender={(option) => {
                  const data = option.data as {
                    label?: ReactNode;
                    meaning?: string;
                  };
                  return (
                    <div className="shop-age-range-option">
                      <div className="shop-age-range-option-label">
                        <span className="shop-age-range-option-age">{data.label}</span>
                      </div>
                      {data.meaning ? (
                        <div className="shop-age-range-option-meta">{data.meaning}</div>
                      ) : null}
                    </div>
                  );
                }}
              />
            </Form.Item>
          </div>
          <ShopFormSection title="Bộ lọc sản phẩm">
            <div className="shop-catalog-filters">
              <Form.Item name="priceMinUsd" label="Giá từ">
                <InputNumber
                  style={{ width: '100%' }}
                  min={0}
                  placeholder={`${DEFAULT_PRICE_MIN_USD}`}
                />
              </Form.Item>
              <Form.Item name="priceMaxUsd" label="Giá đến">
                <InputNumber
                  style={{ width: '100%' }}
                  min={0}
                  placeholder={`${DEFAULT_PRICE_MAX_USD}`}
                />
              </Form.Item>
              <Form.Item name="minRating" label="Rating ≥">
                <InputNumber
                  style={{ width: '100%' }}
                  min={0}
                  max={5}
                  step={0.1}
                  placeholder="0"
                />
              </Form.Item>
              <Form.Item name="minSoldCount" label="Sold ≥">
                <InputNumber
                  style={{ width: '100%' }}
                  min={0}
                  step={1}
                  precision={0}
                  placeholder="0"
                />
              </Form.Item>
              <Form.Item name="maxVariantCount" label="Variants <">
                <InputNumber
                  style={{ width: '100%' }}
                  min={0}
                  max={500}
                  step={1}
                  precision={0}
                  placeholder="0"
                />
              </Form.Item>
            </div>
            <div className="shop-product-filter-flags">
              <Form.Item name="preferQuickShip" valuePropName="checked" noStyle>
                <Checkbox>QuickShip</Checkbox>
              </Form.Item>
              <Form.Item name="preferLocal" valuePropName="checked" noStyle>
                <Checkbox>Local</Checkbox>
              </Form.Item>
              <Form.Item name="preferUsWarehouse" valuePropName="checked" noStyle>
                <Checkbox>US Warehouse</Checkbox>
              </Form.Item>
              <Form.Item name="preferSheinClub" valuePropName="checked" noStyle>
                <Checkbox>Shein Club</Checkbox>
              </Form.Item>
            </div>
          </ShopFormSection>
        </Form>
      </Modal>

      <Modal
        title={
          <span className="shop-category-picker-title">
            <span>Chọn category</span>
            <Tag className="shop-catalog-site-badge">{catalogSite}</Tag>
            <span className="shop-catalog-category-count">
              {pickerCategories.length}/{CATALOG_CATEGORY_MAX}
            </span>
          </span>
        }
        open={categoryPickerOpen}
        onCancel={closeCategoryPicker}
        onOk={applyCategoryPicker}
        destroyOnHidden
        centered={!isMobile}
        width={isMobile ? '100%' : 920}
        className="shop-edit-modal shop-category-picker-modal"
        okText="Xong"
        cancelText="Hủy"
        styles={{
          body: {
            paddingTop: 0,
            paddingBottom: 0,
            maxHeight: isMobile ? '70vh' : 'calc(100vh - 160px)',
            overflow: 'hidden',
            display: 'flex',
            flexDirection: 'column',
          },
        }}
      >
        <div className="shop-category-picker">
          <div className="shop-category-picker-toolbar">
            <Input
              allowClear
              size="middle"
              prefix={<SearchOutlined />}
              placeholder="Tìm tên hoặc ID category…"
              value={categorySearch}
              onChange={(e) => handleCategorySearchChange(e.target.value)}
              onPressEnter={(e) => {
                e.preventDefault();
                void applyAiCategory();
              }}
            />
            <Button
              type="default"
              icon={<ThunderboltOutlined />}
              loading={aiPending}
              onClick={() => void applyAiCategory()}
              title="AI gợi ý category từ từ khóa"
              className="shop-ai-action-btn"
            >
              AI
            </Button>
          </div>

          {pickerCategories.length > 0 ? (
            <div className="shop-category-picker-selected">
              <div className="shop-category-picker-selected-tags">
                {pickerCategories.map((c) => (
                  <Tag
                    key={c.id}
                    closable
                    onClose={() =>
                      applyPickerSelection(
                        (() => {
                          const next = (pickerPath ?? []).filter((p) => p[p.length - 1] !== c.id);
                          return next.length ? next : undefined;
                        })(),
                        pickerCategories.filter((x) => x.id !== c.id),
                      )
                    }
                    style={{ marginInlineEnd: 0 }}
                    title={c.name}
                  >
                    {categoryLeafName(c.name)}
                  </Tag>
                ))}
              </div>
            </div>
          ) : null}

          <div className="shop-category-picker-body">
            {showSearchList ? (
              <Spin spinning={searchLoading || aiPending}>
                {displayedHits.length === 0 && !searchLoading && !aiPending ? (
                  <Empty
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                    description="Không có category khớp"
                  />
                ) : (
                  <div className="shop-category-search-list">
                    {displayedHits.map((hit) => {
                      const checked = pickerCategories.some((c) => c.id === hit.id);
                      const fromAi = aiHits.some((a) => a.id === hit.id);
                      return (
                        <div
                          key={hit.id}
                          role="button"
                          tabIndex={0}
                          className={`shop-category-search-row${checked ? ' is-checked' : ''}`}
                          onClick={() => togglePickerHit(hit)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter' || e.key === ' ') {
                              e.preventDefault();
                              togglePickerHit(hit);
                            }
                          }}
                        >
                          <Checkbox
                            checked={checked}
                            onClick={(e) => e.preventDefault()}
                          />
                          <span className="shop-category-search-copy">
                            <span className="shop-category-search-name">{hit.name}</span>
                            {hit.pathLabel ? (
                              <span className="shop-category-search-path">{hit.pathLabel}</span>
                            ) : null}
                          </span>
                          <span className="shop-category-search-meta">
                            {fromAi ? (
                              <Tag color="purple" style={{ marginInlineEnd: 0 }}>
                                AI
                              </Tag>
                            ) : null}
                            <span
                              className={`shop-category-search-kind${hit.isLeaf ? ' is-leaf' : ''}`}
                            >
                              {hit.isLeaf ? 'Lá' : 'Nhóm'}
                            </span>
                          </span>
                        </div>
                      );
                    })}
                  </div>
                )}
              </Spin>
            ) : (
              <CategoryCascader
                key={`${catalogShop?.id ?? ''}:${catalogSite}:picker`}
                variant="panel"
                multiple
                site={catalogSite}
                value={pickerPath}
                placeholder="Chọn một hoặc nhiều category TikTok"
                onChange={(path, selected) => {
                  const items = Array.isArray(selected) ? selected : selected ? [selected] : [];
                  applyPickerSelection(
                    Array.isArray(path) && path.length > 0 && Array.isArray(path[0])
                      ? (path as string[][])
                      : undefined,
                    items.map((s) => ({
                      id: s.id,
                      name: s.name,
                    })),
                  );
                }}
              />
            )}
          </div>
        </div>
      </Modal>

      <Modal
        title="Import shop từ file Excel"
        open={importOpen}
        onCancel={closeImport}
        destroyOnHidden
        centered={!isMobile}
        width={isMobile ? '100%' : 920}
        className="shop-edit-modal shop-import-modal"
        maskClosable={!importSubmitting}
        styles={{
          body: {
            paddingTop: 8,
            maxHeight: isMobile ? '70vh' : 'calc(100vh - 160px)',
            overflowY: 'auto',
          },
        }}
        footer={
          <Space wrap>
            <Button onClick={closeImport} disabled={importSubmitting}>
              Hủy
            </Button>
            <Button
              type="primary"
              loading={importSubmitting}
              disabled={pendingImportRows(importRows).length === 0}
              onClick={() => void handleImportSubmit()}
            >
              {importProgress && importSubmitting
                ? `Import (${importProgress.done}/${importProgress.total})`
                : 'Import'}
            </Button>
          </Space>
        }
      >
        <Form form={importForm} layout="vertical" size="small" requiredMark={false}>
          <div className="shop-form-grid">
            <Form.Item
              name="platformId"
              label="Nền tảng"
              rules={[{ required: true, message: 'Chọn nền tảng' }]}
            >
              <Select
                showSearch
                placeholder="Chọn nền tảng"
                options={platformOptions}
                loading={platformsLoading}
                optionFilterProp="label"
                disabled={importSubmitting}
              />
            </Form.Item>
            <Form.Item
              name="region"
              label="Region"
              rules={[{ required: true, message: 'Chọn region' }]}
            >
              <Select
                showSearch
                placeholder="Region"
                options={SHOP_REGIONS.map((r) => ({ label: r.label, value: r.value }))}
                optionFilterProp="label"
                disabled={importSubmitting}
              />
            </Form.Item>
          </div>
        </Form>

        <div className="shop-import-hint">
          <div className="shop-import-hint-title">Định dạng file chuẩn</div>
          <ul>
            <li>
              File <b>.xlsx</b> / <b>.xls</b>, hàng đầu là header, mỗi shop một dòng.
            </li>
            <li>
              Cột: <b>{SHOP_IMPORT_COLUMN_LABELS.externalShopId}</b> ·{' '}
              <b>{SHOP_IMPORT_COLUMN_LABELS.name}</b> · <b>{SHOP_IMPORT_COLUMN_LABELS.profileName}</b> ·{' '}
              <b>{SHOP_IMPORT_COLUMN_LABELS.company}</b> ·{' '}
              <b>{SHOP_IMPORT_COLUMN_LABELS.sellerUserName}</b> (không phân biệt hoa thường, có thể
              thiếu dấu).
            </li>
            <li>
              <b>Tên shop</b> bắt buộc. <b>External shop ID</b> có thể để trống — khi đó ghép theo
              tên shop.
            </li>
            <li>
              <b>Seller username</b> là username seller trên hệ thống; bắt buộc với shop thêm mới,
              shop đã có thì để trống sẽ giữ seller hiện tại.
            </li>
            <li>
              Có <b>Company</b> → loại <b>LLC</b> (lưu tên công ty); để trống → loại <b>PER</b>.
            </li>
            <li>
              Ghép theo External shop ID trước, sau đó tới tên shop: đã có thì cập nhật, chưa có thì
              thêm mới (trạng thái <b>Active</b>). Ô trống trong file giữ nguyên dữ liệu cũ.
            </li>
            <li>
              <b>Nền tảng</b> và <b>Region</b> chọn ở trên áp dụng cho mọi dòng, kể cả shop được cập
              nhật.
            </li>
          </ul>
        </div>

        <div className="shop-import-upload">
          <Upload.Dragger
            accept=".xlsx,.xls"
            maxCount={1}
            fileList={importFileList}
            disabled={importSubmitting || importParsing}
            beforeUpload={(file) => {
              setImportFile(file);
              setImportFileList([{ uid: file.uid, name: file.name, status: 'done' }]);
              setImportRows([]);
              setImportProgress(null);
              return false;
            }}
            onRemove={() => {
              resetImportState();
              return true;
            }}
          >
            <p className="ant-upload-drag-icon">
              <InboxOutlined />
            </p>
            <p className="ant-upload-text">Chọn hoặc kéo file Excel danh sách shop</p>
            <p className="ant-upload-hint">Định dạng .xlsx / .xls</p>
          </Upload.Dragger>
          <div className="shop-import-upload-actions">
            <Button
              loading={importParsing || sellersLoading}
              disabled={!importFile || importSubmitting || sellersLoading}
              onClick={() => void handleImportProcess()}
            >
              Xử lý
            </Button>
          </div>
        </div>

        {importRows.length > 0 ? (
          <Table<ShopImportPreviewRow>
            className="shop-import-preview"
            size="small"
            rowKey={(r) => r.rowIndex}
            dataSource={importRows}
            pagination={{
              pageSize: 20,
              size: 'small',
              showSizeChanger: false,
              showTotal: (total) => `${total} dòng`,
            }}
            scroll={{ x: 760 }}
            rowClassName={(r) => (r.action === 'skip' ? 'shop-import-row-error' : '')}
            columns={[
              { title: '#', dataIndex: 'rowIndex', width: 48, align: 'center' },
              {
                title: 'External shop ID',
                dataIndex: 'externalShopId',
                width: 150,
                ellipsis: true,
                render: (v: string) => v || '—',
              },
              {
                title: 'Tên shop',
                dataIndex: 'name',
                width: 170,
                ellipsis: true,
                render: (v: string, row) => (
                  <span>
                    {v || '—'}
                    {row.matchedBy === 'name' ? (
                      <Tooltip title="Ghép theo tên shop (không có External shop ID khớp)">
                        <Tag color="gold" className="shop-import-tag">
                          tên
                        </Tag>
                      </Tooltip>
                    ) : null}
                  </span>
                ),
              },
              {
                title: 'Profile',
                dataIndex: 'profileName',
                width: 130,
                ellipsis: true,
                render: (v: string) => v || '—',
              },
              {
                title: 'Loại',
                dataIndex: 'shopType',
                width: 110,
                render: (_: string, row) => (
                  <span>
                    {shopTypeTag(row.shopType)}
                    {row.company ? (
                      <Tooltip title={row.company}>
                        <span className="shop-import-company">{row.company}</span>
                      </Tooltip>
                    ) : null}
                  </span>
                ),
              },
              {
                title: 'Seller',
                dataIndex: 'sellerUserName',
                width: 150,
                ellipsis: true,
                render: (v: string, row) => (
                  <span>
                    {row.ownerLabel || v || '— (giữ seller cũ)'}
                    {row.ownerChanged ? (
                      <Tooltip
                        title={`Đang thuộc «${row.currentOwnerLabel ?? '—'}» — import sẽ chuyển giao shop`}
                      >
                        <Tag color="volcano" className="shop-import-tag">
                          đổi seller
                        </Tag>
                      </Tooltip>
                    ) : null}
                  </span>
                ),
              },
              {
                title: 'Thao tác',
                dataIndex: 'action',
                width: 110,
                fixed: isMobile ? undefined : 'right',
                render: (action: ShopImportAction, row) => {
                  if (action === 'skip') {
                    return (
                      <Tooltip title={row.error}>
                        <Tag color="error" className="shop-import-tag">
                          Lỗi
                        </Tag>
                      </Tooltip>
                    );
                  }
                  if (action === 'done') {
                    return (
                      <Tag color="success" className="shop-import-tag">
                        Đã xong
                      </Tag>
                    );
                  }
                  if (action === 'update') {
                    return (
                      <Tooltip title={`Cập nhật shop «${row.matchedShopName ?? ''}»`}>
                        <Tag color="blue" className="shop-import-tag">
                          Cập nhật
                        </Tag>
                      </Tooltip>
                    );
                  }
                  return (
                    <Tag color="green" className="shop-import-tag">
                      Thêm mới
                    </Tag>
                  );
                },
              },
            ]}
          />
        ) : null}
      </Modal>

      <Modal
        title={editingShop ? 'Sửa shop' : canCreate ? 'Thêm shop' : 'Sửa shop'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={handleSubmit}
        confirmLoading={!!createShop?.isPending || updateShop.isPending || !!deps.setAcquisitionCost?.isPending}
        okButtonProps={{ disabled: editLoading }}
        destroyOnHidden
        centered={!isMobile}
        width={isMobile ? '100%' : 880}
        className="shop-edit-modal"
        styles={{
          body: {
            maxHeight: 'calc(100vh - 140px)',
            overflowY: 'auto',
          },
        }}
        footer={(_, { OkBtn, CancelBtn }) => (
          <Space style={{ width: '100%', justifyContent: 'space-between' }} wrap>
            <span>
              {editingShop && reassignOwnedData && canManage ? (
                <Button
                  onClick={handleReassignOwnedData}
                  loading={reassignOwnedData.isPending}
                  disabled={editLoading}
                >
                  Đồng bộ ownership
                </Button>
              ) : null}
            </span>
            <Space>
              <CancelBtn />
              <OkBtn />
            </Space>
          </Space>
        )}
      >
        <Spin spinning={editLoading}>
        <Form form={form} layout="vertical" size="small" requiredMark={false}>
          <ShopFormSection title="Thông tin" first>
            <div className="shop-form-grid">
              <Form.Item name="name" label="Tên shop" rules={[{ required: true, message: 'Nhập tên shop' }]}>
                <Input />
              </Form.Item>
              <Form.Item name="profileName" label="Profile">
                <Input placeholder="Profile trình duyệt" />
              </Form.Item>
              {config.requireOwnerOnForm ? (
                <Form.Item
                  name="ownerId"
                  label="Seller"
                  rules={[{ required: true, message: 'Chọn seller' }]}
                >
                  <Select
                    showSearch
                    placeholder="Chọn seller"
                    options={sellerOptions}
                    loading={sellersLoading}
                    optionFilterProp="label"
                  />
                </Form.Item>
              ) : (
                <Form.Item name="shopType" label="Loại">
                  <Select
                    allowClear
                    placeholder="PER / LLC"
                    options={SHOP_TYPES.map((t) => ({ label: t.label, value: t.value }))}
                  />
                </Form.Item>
              )}

              <Form.Item
                name="platformId"
                label="Nền tảng"
                rules={
                  config.lockPlatformAndRegion
                    ? undefined
                    : [{ required: true, message: 'Chọn nền tảng' }]
                }
              >
                <Select
                  showSearch
                  disabled={config.lockPlatformAndRegion}
                  placeholder="Chọn nền tảng"
                  options={platformOptions}
                  loading={platformsLoading}
                  optionFilterProp="label"
                />
              </Form.Item>
              <Form.Item
                name="region"
                label="Region"
                rules={
                  config.lockPlatformAndRegion
                    ? undefined
                    : [{ required: true, message: 'Chọn region' }]
                }
              >
                <Select
                  showSearch
                  disabled={config.lockPlatformAndRegion}
                  placeholder="Region"
                  options={SHOP_REGIONS.map((r) => ({ label: r.label, value: r.value }))}
                  optionFilterProp="label"
                />
              </Form.Item>
              <Form.Item name="status" label="Trạng thái" rules={[{ required: true }]}>
                <Select
                  options={SHOP_STATUSES.map((s) => ({
                    label: SHOP_STATUS_LABELS[s] ?? s,
                    value: s,
                  }))}
                />
              </Form.Item>

              {config.requireOwnerOnForm ? (
                <Form.Item name="shopType" label="Loại">
                  <Select
                    allowClear
                    placeholder="PER / LLC"
                    options={SHOP_TYPES.map((t) => ({ label: t.label, value: t.value }))}
                  />
                </Form.Item>
              ) : null}
              <Form.Item name="llcName" label="Company">
                <Input placeholder="Tên công ty / LLC" />
              </Form.Item>

              <Form.Item
                name="proxy"
                label="Proxy"
                validateTrigger={['onChange', 'onBlur']}
                className="shop-form-span-2"
                rules={[
                  {
                    warningOnly: true,
                    validator: async (_, value) => {
                      const proxy = normalizeProxy(value);
                      if (!proxy) return;
                      const conflict = shops.find(
                        (s) =>
                          normalizeProxy(s.proxy) === proxy &&
                          s.id !== editingShop?.id,
                      );
                      if (conflict) {
                        throw new Error(
                          `Proxy đã thêm cho shop "${conflict.name}"${
                            conflict.ownerFullName ? ` (${conflict.ownerFullName})` : ''
                          }`,
                        );
                      }
                    },
                  },
                ]}
              >
                <Input placeholder="IP:Port" />
              </Form.Item>
              <Form.Item name="newPhoneNumber" label="Phone">
                <Input />
              </Form.Item>

              <Form.Item name="dayLive" label="Day Live">
                <Input placeholder="VD: 954" />
              </Form.Item>
              <Form.Item
                name="deli"
                label="DELI"
                extra="Hạn thanh toán (ngày). Extension ghi đè từ TTS bill_finish_period_in_days khi sync profile."
              >
                <InputNumber style={{ width: '100%' }} min={0} placeholder="Ngày" />
              </Form.Item>
              {canSetAcquisition ? (
                <Form.Item
                  name="acquisitionCost"
                  label="Giá mua"
                  extra="USD, ghi vào sổ cái. 0 = xóa giá mua."
                >
                  <InputNumber style={{ width: '100%' }} min={0} precision={2} placeholder="0" />
                </Form.Item>
              ) : null}
              <Form.Item name="shopUrl" label="Shop URL">
                <Input placeholder="https://..." />
              </Form.Item>
              <Form.Item name="avatarUrl" label="Avatar URL" className="shop-form-span-3" style={{ marginBottom: 0 }}>
                <Input placeholder="https://..." />
              </Form.Item>
            </div>
          </ShopFormSection>

          <ShopFormSection title="Tài khoản">
            <div className="shop-form-grid">
              <Form.Item
                name="email"
                label="Email"
                rules={[{ type: 'email', message: 'Email không hợp lệ' }]}
              >
                <Input />
              </Form.Item>
              <Form.Item name="tiktokPassword" label="Password">
                <Input.Password />
              </Form.Item>
              <Form.Item name="twoFactorAuth" label="2FA" style={{ marginBottom: 0 }}>
                <CopyOnHoverInput />
              </Form.Item>
            </div>
          </ShopFormSection>

          <ShopFormSection title="Thanh toán">
            <div className="shop-form-grid">
              {config.enableReceivingBank ? (
                <Form.Item
                  name="receivingBankAccountId"
                  label="Thẻ nhận tiền"
                  className="shop-form-span-2"
                  style={{ marginBottom: 0 }}
                >
                  <Select
                    allowClear
                    showSearch
                    placeholder="Chọn thẻ nhận tiền"
                    options={receivingBankOptions}
                    loading={receivingBanksLoading}
                    optionFilterProp="label"
                    optionRender={(option) =>
                      renderReceivingBankOption(
                        option.data.label,
                        (option.data as { used?: boolean }).used,
                      )
                    }
                  />
                </Form.Item>
              ) : null}
              {config.enableManualBankNumber ? (
                <Form.Item
                  name="newBankNumber"
                  label="Số TK"
                  style={{ marginBottom: 0 }}
                >
                  <Input placeholder="Số tài khoản" />
                </Form.Item>
              ) : null}
              <Form.Item
                name="paymentName"
                label="Tên thanh toán"
                className={
                  config.enableReceivingBank && !config.enableManualBankNumber
                    ? undefined
                    : 'shop-form-span-2'
                }
                style={{ marginBottom: 0 }}
              >
                <Input placeholder="Tên nhận thanh toán" />
              </Form.Item>
            </div>
          </ShopFormSection>

          <ShopFormSection title="ID">
            <div className="shop-form-grid">
              <Form.Item name="externalShopId" label="External ID" style={{ marginBottom: 0 }}>
                <Input placeholder="Có thể nhập sau" />
              </Form.Item>
              <Form.Item name="fourSellerShopId" label="4Seller ID" style={{ marginBottom: 0 }}>
                <Input />
              </Form.Item>
              {editingShop ? (
                <Form.Item label="Ngày tạo" style={{ marginBottom: 0 }}>
                  <Input readOnly value={formatDateTimeVn(editingShop.createdAt)} />
                </Form.Item>
              ) : null}
            </div>
          </ShopFormSection>
        </Form>
        </Spin>
      </Modal>

      <Modal
        title={
          autoPublishShop ? (
            <div className="shop-auto-publish-modal-head">
              <span className="shop-auto-publish-modal-kicker">Auto publish</span>
              <span className="shop-auto-publish-modal-name">{autoPublishShop.name}</span>
            </div>
          ) : (
            'Auto publish'
          )
        }
        open={!!autoPublishShop}
        onCancel={closeAutoPublish}
        onOk={() => void handleAutoPublishSubmit()}
        okText="Lưu"
        cancelText="Hủy"
        confirmLoading={updateAutoPublish.isPending}
        destroyOnHidden
        centered={!isMobile}
        width={isMobile ? '100%' : 440}
        className="shop-auto-publish-modal"
      >
        {autoPublishShop ? (
          <Form form={autoPublishForm} layout="vertical" size="small" requiredMark={false}>
            <ShopAutoPublishModalBody
              shop={autoPublishShop}
              canManage={canManage}
              systemAutoPublishEnabled={systemAutoPublishEnabled}
              defaultIntervalSeconds={autoPublishDefaultInterval}
              defaultMinDraftAgeMinutes={autoPublishDefaultMinDraftAge}
            />
          </Form>
        ) : null}
      </Modal>
    </div>
  );
};
