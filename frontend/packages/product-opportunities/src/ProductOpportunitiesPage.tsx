import { useEffect, useMemo, useState } from 'react';
import type { Key } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Avatar, Button, Input, Popconfirm, Popover, Result, Select, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { BulbOutlined, CaretDownOutlined, CaretUpOutlined, DeleteOutlined, LoadingOutlined, ReloadOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { ProductOpportunityListItem } from '@drop/types/productOpportunity';
import { PageContentSpin, PageHeader, useIsMobile } from '@drop/ui';
import { formatDateTimeVn } from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import type { ProductOpportunitiesPortalConfig } from './portalConfig';
import { useProductOpportunitiesDeps } from './ProductOpportunitiesDepsContext';
import { ShopKeywordResearchDrawer } from './ShopKeywordResearchDrawer';
import {
  buildAudienceSearchQuery,
  buildSheinSearchUrl,
  isSameSearchQuery,
} from './audienceSearchQuery';
import { SheinSearchLink, copySourcingQuery } from './KeywordSourcing';
import './productOpportunities.css';

const { Text } = Typography;

const formatVolume = (value: number) => value.toLocaleString('en-US');

/** Group thousands like Volume (`305800.00` → `305,800`); keep meaningful decimals (`4.02`). */
const formatRingNumber = (amount: string): string => {
  const n = Number(amount);
  if (!Number.isFinite(n)) return amount;
  const fraction = amount.includes('.') ? amount.split('.')[1] ?? '' : '';
  const decimals = fraction.replace(/0+$/, '').length;
  return n.toLocaleString('en-US', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
};

const uniqueNames = (names: Array<string | null | undefined>): string[] => {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const name of names) {
    const t = name?.trim();
    if (!t || seen.has(t)) continue;
    seen.add(t);
    out.push(t);
  }
  return out;
};

const allCategoryNames = (row: ProductOpportunityListItem): string[] => {
  const fromArr = uniqueNames((row.categories ?? []).map((c) => c.cateName));
  if (fromArr.length > 0) return fromArr;
  return uniqueNames([row.level1CateName, row.level2CateName, row.level3CateName]);
};

/** Leaf category only (deepest / last in path). */
const displayCategoryName = (row: ProductOpportunityListItem): string | null => {
  const cats = row.categories ?? [];
  const withLevel = cats.filter((c) => (c.level ?? 0) > 0 && c.cateName?.trim());
  if (withLevel.length > 0) {
    const deepest = withLevel.reduce((a, b) => ((b.level ?? 0) > (a.level ?? 0) ? b : a));
    const name = deepest.cateName.trim();
    return name || null;
  }
  const all = allCategoryNames(row);
  return all[all.length - 1] ?? null;
};

const rowPicUrls = (row: ProductOpportunityListItem): string[] => {
  const fromArr = (row.picUrls ?? []).map((u) => u.trim()).filter(Boolean);
  if (fromArr.length > 0) return fromArr;
  const first = row.picUrl?.trim();
  return first ? [first] : [];
};

type RingDir = 'up' | 'down' | 'flat';

const parseRing = (
  ring?: string | null,
): { dir: RingDir; label: string } | null => {
  const raw = ring?.trim() ?? '';
  if (!raw) return null;
  const compact = raw.replace(/,/g, '').replace(/−/g, '-').replace(/\s/g, '');
  const match = compact.match(/^([+-])?(\d+(?:\.\d+)?)(%?)$/);
  if (!match) {
    if (raw.startsWith('-') || raw.startsWith('−')) {
      return { dir: 'down', label: raw.replace(/^[+\-−]\s*/, '') };
    }
    if (raw.startsWith('+')) {
      return { dir: 'up', label: raw.replace(/^\+\s*/, '') };
    }
    return { dir: 'flat', label: raw };
  }
  const sign = match[1];
  const amount = match[2];
  if (!amount) return { dir: 'flat', label: raw };
  const suffix = match[3] ?? '';
  const n = Number(amount);
  const label = `${formatRingNumber(amount)}${suffix}`;
  if (sign === '-' || n < 0) return { dir: n === 0 ? 'flat' : 'down', label };
  if (n === 0) return { dir: 'flat', label };
  return { dir: 'up', label };
};

const MetricCell = ({
  main,
  ring,
}: {
  main: string;
  ring?: string | null;
}) => {
  const parsed = parseRing(ring);
  return (
    <div>
      <div>{main}</div>
      {parsed ? (
        <div className={`product-opportunities-meta product-opportunities-ring-${parsed.dir}`}>
          {parsed.dir === 'up' ? <CaretUpOutlined /> : null}
          {parsed.dir === 'down' ? <CaretDownOutlined /> : null}
          <span>{parsed.label}</span>
        </div>
      ) : null}
    </div>
  );
};

const KeywordName = ({
  name,
  searchQuery,
  sheinUrl,
}: {
  name: string;
  searchQuery?: string | null;
  sheinUrl?: string | null;
}) => {
  const text = name.trim();
  if (!text) {
    return <span className="product-opportunities-keyword-text">—</span>;
  }
  const query = (searchQuery ?? text).trim();
  const showSourcing = query.length > 0 && !isSameSearchQuery(text, query);
  return (
    <div className="product-opportunities-keyword-copy">
      <div className="product-opportunities-keyword-line-text">
        <Tooltip title="Copy">
          <span
            className="product-opportunities-keyword-text"
            role="button"
            tabIndex={0}
            onClick={(e) => {
              e.stopPropagation();
              void copySourcingQuery(query, text);
            }}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                e.stopPropagation();
                void copySourcingQuery(query, text);
              }
            }}
          >
            {name}
          </span>
        </Tooltip>
        <SheinSearchLink url={sheinUrl} />
      </div>
      {showSourcing ? (
        <Tooltip title="Copy">
          <span
            className="product-opportunities-sourcing-text"
            role="button"
            tabIndex={0}
            onClick={(e) => {
              e.stopPropagation();
              void copySourcingQuery(query, text);
            }}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                e.stopPropagation();
                void copySourcingQuery(query, text);
              }
            }}
          >
            Tìm: {query}
          </span>
        </Tooltip>
      ) : null}
    </div>
  );
};

/** Bookmark matching TikTok Seller Center `arco-icon-subscribe-fill`. */
const SubscribeIcon = ({ filled }: { filled: boolean }) => (
  <svg viewBox="0 0 48 48" width="12" height="12" aria-hidden>
    {filled ? (
      <path
        fill="currentColor"
        d="M10 6c0-1.1.9-2 2-2h24a2 2 0 0 1 2 2v36.14c0 1.3-1.47 2.04-2.51 1.25L24.51 35.01a.8.8 0 0 0-1.02 0L12.51 43.39C11.47 44.18 10 43.44 10 42.14V6Z"
      />
    ) : (
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth="3.5"
        strokeLinejoin="round"
        d="M12 4.5h24A1.5 1.5 0 0 1 37.5 6v35.9c0 .9-1.02 1.42-1.74.87L24.35 34.7a.7.7 0 0 0-.7 0L12.24 42.77c-.72.55-1.74.03-1.74-.87V6A1.5 1.5 0 0 1 12 4.5Z"
      />
    )}
  </svg>
);

const shopLabel = (name: string, profileName?: string | null) =>
  profileName?.trim() ? `${profileName} (${name})` : name;

const sellerLabel = (fullName?: string | null, userName?: string | null) =>
  fullName?.trim() || userName?.trim() || '—';

const THUMB_SIZE = 40;
const PREVIEW_MAX = 320;
const SORTABLE = new Set(['volume', 'online']);
const SORT_DIRECTIONS: Array<'descend' | 'ascend'> = ['descend', 'ascend'];
const MOBILE_COLUMN_KEYS = new Set(['keyword', 'volume', 'online']);

type SortKey = 'volume' | 'online';

const parseSort = (raw: string | null): SortKey =>
  raw && SORTABLE.has(raw) ? (raw as SortKey) : 'volume';

const parseSortDir = (raw: string | null): 'asc' | 'desc' => (raw === 'asc' ? 'asc' : 'desc');

const parseLeadTags = (raw: string | null): string[] => {
  if (!raw?.trim()) return [];
  const seen = new Set<string>();
  const out: string[] = [];
  for (const part of raw.split(',')) {
    const t = part.trim();
    const key = t.toLowerCase();
    if (!t || seen.has(key)) continue;
    seen.add(key);
    out.push(t);
    if (out.length >= 8) break;
  }
  return out;
};

const antdSortOrder = (active: string, key: string, dir: 'asc' | 'desc') =>
  active === key ? (dir === 'asc' ? ('ascend' as const) : ('descend' as const)) : undefined;

function KeywordThumbOne({ src }: { src: string }) {
  const [previewReady, setPreviewReady] = useState(false);

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
              maxWidth: PREVIEW_MAX,
              maxHeight: PREVIEW_MAX,
              objectFit: 'contain',
              borderRadius: 4,
            }}
          />
        ) : (
          <div
            style={{
              width: THUMB_SIZE,
              height: THUMB_SIZE,
              background: 'var(--app-surface-subtle)',
              borderRadius: 4,
            }}
          />
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
      <span className="product-opportunities-thumb-trigger">
        <Avatar src={src} shape="square" size={THUMB_SIZE} alt="" />
      </span>
    </Popover>
  );
}

function KeywordThumb({
  srcs,
  fallback,
}: {
  srcs: string[];
  fallback: string;
}) {
  if (srcs.length === 0) {
    return (
      <Avatar shape="square" size={THUMB_SIZE} alt="">
        {fallback}
      </Avatar>
    );
  }

  return (
    <div className="product-opportunities-thumb-list">
      {srcs.map((src, i) => (
        <KeywordThumbOne key={`${i}:${src}`} src={src} />
      ))}
    </div>
  );
}

export type SharedProductOpportunitiesPageProps = {
  config: ProductOpportunitiesPortalConfig;
};

export const ProductOpportunitiesPage = ({ config }: SharedProductOpportunitiesPageProps) => {
  const [searchParams, setSearchParams] = useSearchParams();
  const isMobile = useIsMobile();
  const deps = useProductOpportunitiesDeps();
  const canView = deps.hasPermission(config.viewPermission);

  const q = searchParams.get('q')?.trim() || undefined;
  const shopId = searchParams.get('shopId')?.trim() || undefined;
  const ownerId = config.showSellerFilter
    ? searchParams.get('ownerId')?.trim() || undefined
    : undefined;
  const savedOnly = searchParams.get('saved') === '1';
  const leadTags = parseLeadTags(searchParams.get('leadTag'));
  const leadTagKey = leadTags.join(',');
  const sort = parseSort(searchParams.get('sort'));
  const sortDir = parseSortDir(searchParams.get('sortDir'));
  const pageCurrent = Math.max(1, Number(searchParams.get('page') ?? '1') || 1);
  const pageSize = Math.max(
    1,
    Number(searchParams.get('pageSize') ?? String(deps.defaultPageSize)) || deps.defaultPageSize,
  );

  const [searchInput, setSearchInput] = useState(q ?? '');
  const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([]);
  const [researchOpen, setResearchOpen] = useState(false);

  const patchParams = (patch: Record<string, string | undefined>, replace = true) => {
    const next = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(patch)) {
      if (value == null || value === '') next.delete(key);
      else next.set(key, value);
    }
    setSearchParams(next, { replace });
  };

  useEffect(() => {
    setSearchInput(q ?? '');
  }, [q]);

  useEffect(() => {
    setSelectedRowKeys([]);
  }, [shopId, ownerId, q, pageCurrent, pageSize, sort, sortDir, savedOnly, leadTagKey]);

  useEffect(() => {
    if (isMobile) setSelectedRowKeys([]);
  }, [isMobile]);

  const sellersQuery = deps.useSellers?.(config.showSellerFilter);
  const shopsQuery = deps.useShopOptions(true);
  const listQuery = deps.useList({
    page: pageCurrent,
    pageSize,
    q,
    shopId,
    ownerId,
    sort,
    sortDir,
    saved: savedOnly || undefined,
    leadTag: leadTagKey || undefined,
  });

  const shopOptions = useMemo(() => {
    const rows = shopsQuery.data ?? [];
    if (!ownerId) return rows;
    return rows.filter((s) => s.ownerId === ownerId);
  }, [shopsQuery.data, ownerId]);

  const selectedBodySize = useMemo(() => {
    if (!shopId) return null;
    return shopOptions.find((s) => s.id === shopId)?.bodySizeCode ?? null;
  }, [shopId, shopOptions]);

  const leadTagSelectOptions = useMemo(() => {
    const fromApi = listQuery.data?.leadTagOptions ?? [];
    const seen = new Set(fromApi.map((t) => t.toLowerCase()));
    const extra = leadTags.filter((t) => !seen.has(t.toLowerCase()));
    return [...fromApi, ...extra].map((t) => ({ value: t, label: t }));
  }, [listQuery.data?.leadTagOptions, leadTags]);

  const items = listQuery.data?.items ?? [];
  const total = listQuery.data?.total ?? 0;
  const lastSyncedAt = listQuery.data?.lastSyncedAt;
  const canBulkDelete = config.canBulkDelete && canView;
  const showBulkSelect = canBulkDelete && !isMobile;
  const bulkDeleteMutation = deps.useBulkDelete();
  const setSavedMutation = deps.useSetSaved();

  const handleBulkDelete = async () => {
    const ids = selectedRowKeys.map(String).filter(Boolean);
    if (ids.length === 0) return;
    try {
      await bulkDeleteMutation.mutateAsync({ ids, shopId });
      setSelectedRowKeys([]);
    } catch {
      /* toast from mutation onError */
    }
  };

  const columns: ColumnsType<ProductOpportunityListItem> = [
    {
      title: 'Keyword',
      key: 'keyword',
      width: isMobile ? undefined : 300,
      render: (_, row) => {
        const tags = uniqueNames(row.leadTagTexts ?? []).filter((t) => t !== '[object Object]');
        const saved = !!row.isSaved;
        const savingThis =
          setSavedMutation.isPending && setSavedMutation.variables?.id === row.id;
        const category = displayCategoryName(row);
        const profile = row.shopProfileName?.trim() || '';
        const shopName = row.shopName?.trim() || '';
        const shopDistinct =
          shopName && (!profile || profile.toLowerCase() !== shopName.toLowerCase())
            ? shopName
            : '';
        const shopMeta =
          (row.shopCount ?? 1) > 1 ? `${row.shopCount} shop` : shopDistinct;
        const owner = config.showOwnerColumn
          ? sellerLabel(row.ownerFullName, row.ownerUserName)
          : null;
        const keywordMeta = uniqueNames([category, profile, shopMeta, owner]).join(' · ');
        const searchQuery = buildAudienceSearchQuery(row.leadName, selectedBodySize);
        const sheinUrl = searchQuery ? buildSheinSearchUrl(searchQuery) : undefined;
        return (
          <div className="product-opportunities-keyword-cell">
            <div className="product-opportunities-keyword-line">
              <Tooltip title={saved ? 'Bỏ lưu keyword' : 'Lưu keyword — dùng sau để AI tạo URL search'}>
                <button
                  type="button"
                  className={`product-opportunities-subscribe${saved ? ' is-saved' : ''}`}
                  aria-label={saved ? 'Bỏ lưu keyword' : 'Lưu keyword'}
                  aria-pressed={saved}
                  disabled={savingThis}
                  onClick={(e) => {
                    e.stopPropagation();
                    void setSavedMutation
                      .mutateAsync({
                        id: row.id,
                        saved: !saved,
                        shopId: shopId ?? row.shopId,
                        keyword: row.leadName,
                      })
                      .catch(() => undefined);
                  }}
                >
                  {savingThis ? <LoadingOutlined /> : <SubscribeIcon filled={saved} />}
                </button>
              </Tooltip>
              <KeywordName
                name={row.leadName}
                searchQuery={searchQuery}
                sheinUrl={sheinUrl}
              />
            </div>
            {tags.length > 0 ? (
              <div className="product-opportunities-keyword-tags">
                {tags.map((tag) => (
                  <Tag key={tag} className="app-tag-compact" bordered={false}>
                    {tag}
                  </Tag>
                ))}
              </div>
            ) : null}
            {isMobile && keywordMeta ? (
              <div className="product-opportunities-keyword-meta">{keywordMeta}</div>
            ) : null}
          </div>
        );
      },
    },
    {
      title: 'Ảnh',
      key: 'image',
      width: 176,
      render: (_, row) => (
        <KeywordThumb
          srcs={rowPicUrls(row)}
          fallback={row.leadName.slice(0, 1).toUpperCase()}
        />
      ),
    },
    {
      title: 'Volume',
      dataIndex: 'searchVolume',
      key: 'volume',
      width: 88,
      align: 'right',
      sorter: true,
      sortOrder: antdSortOrder(sort, 'volume', sortDir),
      sortDirections: SORT_DIRECTIONS,
      showSorterTooltip: false,
      render: (value: number, row) => (
        <MetricCell main={formatVolume(value)} ring={row.searchVolumeRingRatio} />
      ),
    },
    {
      title: 'Product',
      key: 'online',
      width: 96,
      align: 'right',
      sorter: true,
      sortOrder: antdSortOrder(sort, 'online', sortDir),
      sortDirections: SORT_DIRECTIONS,
      showSorterTooltip: false,
      render: (_, row) => (
        <MetricCell
          main={
            row.onlineProducts != null
              ? formatVolume(row.onlineProducts)
              : (row.onlineProductsRaw ?? '—')
          }
          ring={row.onlineProductsRingRatio}
        />
      ),
    },
    {
      title: 'Danh mục',
      key: 'category',
      ellipsis: true,
      render: (_, row) => {
        const text = displayCategoryName(row);
        if (!text) return '—';
        const full = allCategoryNames(row).join(' · ');
        return full && full !== text ? (
          <Tooltip title={full}>
            <span>{text}</span>
          </Tooltip>
        ) : (
          text
        );
      },
    },
    {
      title: 'Shop',
      key: 'shop',
      width: 280,
      render: (_, row) => {
        const count = row.shopCount ?? 1;
        const label = shopLabel(row.shopName, row.shopProfileName);
        const line1 = count > 1 ? `${count} shop · ${label}` : label;
        const owner = config.showOwnerColumn
          ? sellerLabel(row.ownerFullName, row.ownerUserName)
          : '';
        return (
          <div className="product-opportunities-shop-cell">
            <Tooltip title={line1}>
              <div className="product-opportunities-shop-name">{line1}</div>
            </Tooltip>
            {owner ? (
              <div className="product-opportunities-shop-owner">{owner}</div>
            ) : null}
          </div>
        );
      },
    },
    {
      title: 'Sync',
      key: 'synced',
      width: 120,
      render: (_, row) => (
        <span className="product-opportunities-meta">{formatDateTimeVn(row.shopSyncedAt)}</span>
      ),
    },
  ];

  const visibleColumns = isMobile
    ? columns.filter((col) => MOBILE_COLUMN_KEYS.has(String(col.key)))
    : columns;

  const handleTableChange: TableProps<ProductOpportunityListItem>['onChange'] = (
    _pagination,
    _filters,
    sorter,
    extra,
  ) => {
    if (extra.action !== 'sort') return;
    const current = Array.isArray(sorter) ? sorter[0] : sorter;
    const key = String(current?.columnKey ?? '');
    if (!SORTABLE.has(key) || !current?.order) {
      patchParams({ sort: undefined, sortDir: undefined, page: undefined });
      return;
    }
    patchParams({
      sort: key === 'volume' ? undefined : key,
      sortDir: current.order === 'ascend' ? 'asc' : undefined,
      page: undefined,
    });
  };

  const extra = (
    <Space size={4}>
      <Tooltip title={shopId ? undefined : 'Chọn shop'}>
        <Button
          size="small"
          icon={<BulbOutlined />}
          disabled={!shopId}
          onClick={() => setResearchOpen(true)}
        >
          Shop này bán gì
        </Button>
      </Tooltip>
      {showBulkSelect && selectedRowKeys.length > 0 ? (
        <Popconfirm
          title={`Xóa ${selectedRowKeys.length} keyword đã chọn?`}
          description="Keyword đã xóa sẽ mất khỏi snapshot đến lần đồng bộ kế tiếp."
          okText="Xóa"
          cancelText="Hủy"
          okButtonProps={{ danger: true, loading: bulkDeleteMutation.isPending }}
          onConfirm={() => void handleBulkDelete()}
        >
          <Button
            danger
            size="small"
            icon={<DeleteOutlined />}
            disabled={bulkDeleteMutation.isPending}
            loading={bulkDeleteMutation.isPending}
            aria-label={`Xóa ${selectedRowKeys.length}`}
          >
            Xóa ({selectedRowKeys.length})
          </Button>
        </Popconfirm>
      ) : null}
      <Button
        size="small"
        icon={<ReloadOutlined />}
        loading={listQuery.isFetching}
        onClick={() => void listQuery.refetch()}
      >
        Tải lại
      </Button>
    </Space>
  );

  if (!canView) {
    return (
      <Result
        status="403"
        title="Không có quyền"
        subTitle="Tài khoản không được xem Product opportunities."
      />
    );
  }

  return (
    <div className={`product-opportunities-page${isMobile ? ' is-mobile' : ''}`}>
      <PageHeader
        title="Product opportunities"
        extra={extra}
        description={
          lastSyncedAt ? (
            <Text type="secondary" style={{ fontSize: 12 }}>
              Đồng bộ gần nhất: {formatDateTimeVn(lastSyncedAt)}
            </Text>
          ) : total > 0 ? (
            <Text type="secondary" style={{ fontSize: 12 }}>
              Chưa có snapshot TikTok — đang hiện keyword đã lưu (Shop này bán gì / extension).
            </Text>
          ) : (
            <Text type="secondary" style={{ fontSize: 12 }}>
              Chưa có snapshot — đồng bộ keywords trên extension TikTok Shop.
            </Text>
          )
        }
      />
      <div className="admin-filters">
        <Input.Search
          className="admin-filters-search"
          size="small"
          allowClear
          placeholder="Tìm keyword"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          onSearch={(value) => patchParams({ q: value.trim() || undefined, page: undefined })}
        />
        <div className="admin-filters-grid">
          {config.showSellerFilter ? (
            <Select
              size="small"
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Seller"
              style={{ width: isMobile ? '100%' : 150 }}
              value={ownerId}
              loading={sellersQuery?.isLoading}
              options={(sellersQuery?.data ?? []).map((s) => ({
                value: s.id,
                label: sellerLabel(s.fullName, s.userName),
              }))}
              onChange={(value) =>
                patchParams({ ownerId: value, shopId: undefined, page: undefined })
              }
            />
          ) : null}
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Shop"
            style={{ width: isMobile ? '100%' : 220 }}
            popupMatchSelectWidth={isMobile ? true : 280}
            value={shopId}
            loading={shopsQuery.isLoading}
            options={shopOptions.map((s) => ({
              value: s.id,
              label: shopLabel(s.name, s.profileName),
            }))}
            onChange={(value) => patchParams({ shopId: value, page: undefined })}
          />
          <Select
            size="small"
            allowClear
            showSearch
            mode="multiple"
            maxTagCount="responsive"
            optionFilterProp="label"
            placeholder="Nhãn"
            style={{ width: isMobile ? '100%' : 220 }}
            value={leadTags}
            options={leadTagSelectOptions}
            onChange={(value) => {
              const next = Array.isArray(value) ? value : [];
              patchParams({
                leadTag: next.length > 0 ? next.join(',') : undefined,
                page: undefined,
              });
            }}
          />
          <Select
            size="small"
            allowClear
            placeholder="Đã lưu"
            style={{ width: isMobile ? '100%' : 110 }}
            value={savedOnly ? '1' : undefined}
            options={[{ value: '1', label: 'Đã lưu' }]}
            onChange={(value) =>
              patchParams({ saved: value === '1' ? '1' : undefined, page: undefined })
            }
          />
        </div>
      </div>
      <PageContentSpin spinning={listQuery.isLoading}>
        <Table
          size="small"
          rowKey="id"
          columns={visibleColumns}
          dataSource={items}
          tableLayout={isMobile ? 'fixed' : undefined}
          scroll={{ x: isMobile ? 360 : 1300 }}
          rowSelection={
            showBulkSelect
              ? {
                  selectedRowKeys,
                  onChange: (keys) => setSelectedRowKeys(keys),
                  columnWidth: 36,
                  getCheckboxProps: () => ({
                    onClick: (e) => e.stopPropagation(),
                  }),
                }
              : undefined
          }
          onChange={handleTableChange}
          pagination={tablePagination(total, {
            current: pageCurrent,
            pageSize,
            total,
            size: 'small',
            simple: isMobile,
            showSizeChanger: !isMobile,
            pageSizeOptions: ['20', '50', '100'],
            onChange: (current, nextSize) =>
              patchParams({
                page: String(current),
                pageSize: String(nextSize),
              }),
          })}
        />
      </PageContentSpin>
      <ShopKeywordResearchDrawer
        open={researchOpen}
        shopId={shopId}
        isMobile={isMobile}
        hasSnapshotKeywords={total > 0}
        onClose={() => setResearchOpen(false)}
      />
    </div>
  );
};
