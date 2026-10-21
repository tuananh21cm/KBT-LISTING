import { useEffect, useMemo, useRef, useState } from 'react';
import { Button, Checkbox, Empty, Modal, Popover, Segmented, Tag, Tooltip, Typography } from 'antd';
import {
  CaretDownOutlined,
  CaretUpOutlined,
  DeleteOutlined,
  ReloadOutlined,
  ScissorOutlined,
} from '@ant-design/icons';
import type { ListingDelistItem } from '@drop/types/listing';
import { PageContentSpin, useIsMobile } from '@drop/ui';
import { useShopTasksDeps } from './ShopTasksDepsContext';

const { Text } = Typography;

const SUGGESTION_LIMIT = 50;
const THUMB_SIZE = 40;
/** Tránh `?? []` tạo reference mới mỗi render → effect phụ thuộc `selectableIds` lặp vô hạn. */
const EMPTY_DELIST_ITEMS: ListingDelistItem[] = [];

type VerdictFilter = 'all' | 'delist' | 'review';

const scoreTone = (score: number): 'error' | 'warning' =>
  score >= 70 ? 'error' : 'warning';

const isListingActive = (item: ListingDelistItem): boolean =>
  item.siteStatus?.toLowerCase() === 'active';

const formatCompact = (value: number): string => {
  if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1)}M`;
  if (value >= 1_000) return `${(value / 1_000).toFixed(1)}k`;
  return value.toLocaleString('en-US');
};

const formatMoney = (value: number, currency: string): string => {
  try {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: currency || 'USD',
      minimumFractionDigits: 0,
      maximumFractionDigits: value < 100 ? 2 : 0,
    }).format(value);
  } catch {
    return `${value.toLocaleString('en-US')} ${currency}`;
  }
};

const formatCtr = (ctr?: number | null): string => {
  if (ctr == null || !Number.isFinite(ctr)) return '—';
  const pct = ctr * 100;
  if (pct === 0) return '0%';
  if (Math.abs(pct) >= 1) return `${Math.round(pct * 10) / 10}%`;
  return `${pct.toFixed(2)}%`;
};

const parseDelta = (
  ratio?: number | null,
): { dir: 'up' | 'down' | 'flat'; label: string } | null => {
  if (ratio == null || !Number.isFinite(ratio)) return null;
  const percent = ratio * 100;
  const rounded = Math.abs(percent) >= 100 ? Math.round(percent) : Math.round(percent * 10) / 10;
  if (rounded === 0) return { dir: 'flat', label: '0%' };
  return {
    dir: rounded > 0 ? 'up' : 'down',
    label: `${Math.abs(rounded).toLocaleString('en-US')}%`,
  };
};

const DelistMetaDelta = ({ ratio }: { ratio?: number | null }) => {
  const delta = parseDelta(ratio);
  if (!delta) return null;
  return (
    <span className={`shop-delist-meta-delta is-${delta.dir}`}>
      {delta.dir === 'up' ? <CaretUpOutlined /> : null}
      {delta.dir === 'down' ? <CaretDownOutlined /> : null}
      <span>{delta.label}</span>
    </span>
  );
};

const DelistMetaMetric = ({
  main,
  ratio,
}: {
  main: string;
  ratio?: number | null;
}) => (
  <span className="shop-delist-meta-metric">
    <span>{main}</span>
    <DelistMetaDelta ratio={ratio} />
  </span>
);

const DelistProductMeta = ({
  item,
  currency,
}: {
  item: ListingDelistItem;
  currency: string;
}) => (
  <div className="shop-delist-meta">
    <span className="shop-delist-meta-id" title={item.tikTokProductId}>
      {item.tikTokProductId}
    </span>
    <span className="shop-delist-meta-sep">·</span>
    <DelistMetaMetric main={formatMoney(item.gmv, currency)} ratio={item.gmvRatio} />
    <span className="shop-delist-meta-sep">·</span>
    <DelistMetaMetric main={`${item.orders} đơn`} ratio={item.ordersRatio} />
    <span className="shop-delist-meta-sep">·</span>
    <DelistMetaMetric
      main={`${formatCompact(item.impressions)} view`}
      ratio={item.impressionsRatio}
    />
    <span className="shop-delist-meta-sep">·</span>
    <span>CTR {formatCtr(item.ctr)}</span>
    <span className="shop-delist-meta-sep">·</span>
    <span>ATC {formatCompact(item.addToCartUnits)}</span>
    {item.daysListed != null ? (
      <>
        <span className="shop-delist-meta-sep">·</span>
        <span>{item.daysListed}d</span>
      </>
    ) : null}
  </div>
);

/** URL ảnh đầu khi `mainImage` pipe-separated (cùng convention listings). */
const firstImage = (raw?: string | null): string | null => {
  if (!raw) return null;
  return (
    raw
      .split('|')
      .map((part) => part.trim())
      .find(Boolean) || null
  );
};

const DelistProductThumb = ({ src }: { src?: string | null }) => {
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
        if (entries.some((entry) => entry.isIntersecting)) {
          setInView(true);
          io.disconnect();
        }
      },
      { rootMargin: '240px 0px', threshold: 0.01 },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [src, inView]);

  if (!src) {
    return <span className="shop-delist-thumb is-empty" aria-hidden />;
  }

  return (
    <Popover
      content={
        previewReady ? (
          <img
            className="shop-delist-thumb-preview"
            src={src}
            alt=""
            referrerPolicy="no-referrer"
            decoding="async"
          />
        ) : (
          <span className="shop-delist-thumb is-empty" aria-hidden />
        )
      }
      trigger="hover"
      placement="right"
      mouseEnterDelay={0.2}
      arrow={false}
      styles={{ container: { padding: 2 } }}
      onOpenChange={(open) => {
        if (open) setPreviewReady(true);
      }}
    >
      <span className="shop-delist-thumb">
        <img
          ref={imgRef}
          src={inView ? src : undefined}
          alt=""
          width={THUMB_SIZE}
          height={THUMB_SIZE}
          referrerPolicy="no-referrer"
          loading="lazy"
          decoding="async"
          onError={(event) => {
            event.currentTarget.style.visibility = 'hidden';
          }}
        />
      </span>
    </Popover>
  );
};

export type ShopDelistListingsModalProps = {
  open: boolean;
  shopId: string | null;
  shopName?: string | null;
  canManage: boolean;
  onClose: () => void;
  /** Sau Deactivate/Delete — board refetch để cập nhật chip Cần hủy / Cần xem xét. */
  onChanged?: () => void;
};

/**
 * Modal đề xuất hủy list theo shop — mở từ chip «Cần hủy / Cần xem xét» trên board optimize.
 * Deactivate (= Hủy list) + Delete từng dòng / hàng loạt khi đã mirror listing.
 */
export const ShopDelistListingsModal = (props: ShopDelistListingsModalProps) => {
  const deps = useShopTasksDeps();
  const isMobile = useIsMobile();
  const ready =
    deps.useListingDelistSuggestions != null &&
    deps.useDeactivateListing != null &&
    deps.useDeleteListing != null;

  if (!ready) {
    return (
      <Modal
        open={props.open}
        onCancel={props.onClose}
        footer={null}
        destroyOnHidden
        width={isMobile ? '100%' : 480}
        title="Tối ưu listing"
      >
        <Text type="secondary" style={{ fontSize: 12 }}>
          Portal chưa gắn API đề xuất hủy list.
        </Text>
      </Modal>
    );
  }

  return <ShopDelistListingsModalBody {...props} />;
};

const ShopDelistListingsModalBody = ({
  open,
  shopId,
  shopName,
  canManage,
  onClose,
  onChanged,
}: ShopDelistListingsModalProps) => {
  const deps = useShopTasksDeps();
  const isMobile = useIsMobile();
  const useSuggestions = deps.useListingDelistSuggestions!;
  const deactivate = deps.useDeactivateListing!();
  const deleteListing = deps.useDeleteListing!();

  const [selectedIds, setSelectedIds] = useState<Set<string>>(() => new Set());
  const [bulkBusy, setBulkBusy] = useState(false);
  const [verdictFilter, setVerdictFilter] = useState<VerdictFilter>('all');

  const query = useSuggestions(
    { shopId: shopId ?? undefined, limit: SUGGESTION_LIMIT },
    { enabled: open && Boolean(shopId) },
  );

  const data = query.data;
  const items = data?.items ?? EMPTY_DELIST_ITEMS;
  const scoredNothing =
    !query.isLoading &&
    !query.isError &&
    items.length === 0 &&
    (data?.warnings.length ?? 0) === 0;

  const delistInList = useMemo(
    () => items.filter((item) => item.verdict === 'delist').length,
    [items],
  );
  const reviewInList = useMemo(
    () => items.filter((item) => item.verdict === 'review').length,
    [items],
  );

  const filteredItems = useMemo(() => {
    if (verdictFilter === 'all') return items;
    return items.filter((item) => item.verdict === verdictFilter);
  }, [items, verdictFilter]);

  const selectableItems = useMemo(
    () =>
      filteredItems.filter((item): item is ListingDelistItem & { listingId: string } =>
        Boolean(item.listingId),
      ),
    [filteredItems],
  );
  const selectableIds = useMemo(
    () => selectableItems.map((item) => item.listingId),
    [selectableItems],
  );

  useEffect(() => {
    if (!open) {
      setSelectedIds((prev) => (prev.size === 0 ? prev : new Set()));
      setVerdictFilter((prev) => (prev === 'all' ? prev : 'all'));
      return;
    }
    setSelectedIds((prev) => {
      if (prev.size === 0) return prev;
      const allowed = new Set(selectableIds);
      const next = new Set([...prev].filter((id) => allowed.has(id)));
      return next.size === prev.size ? prev : next;
    });
  }, [open, shopId, selectableIds]);

  useEffect(() => {
    setVerdictFilter((prev) => (prev === 'all' ? prev : 'all'));
    setSelectedIds((prev) => (prev.size === 0 ? prev : new Set()));
  }, [shopId]);

  const selectedCount = selectedIds.size;
  const allSelected = selectableIds.length > 0 && selectableIds.every((id) => selectedIds.has(id));
  const someSelected = selectableIds.some((id) => selectedIds.has(id));

  const selectedActiveCount = selectableItems.filter(
    (item) => selectedIds.has(item.listingId) && isListingActive(item),
  ).length;

  const filterEmpty = items.length > 0 && filteredItems.length === 0;

  const summaryLine = useMemo(() => {
    if (!data) return null;
    return (
      <>
        <span className="shop-delist-summary-delist">Hủy {data.delistCount}</span>
        <span className="shop-delist-summary-sep"> · </span>
        <span className="shop-delist-summary-review">Xem xét {data.reviewCount}</span>
        {data.publishLimitTotal != null ? (
          <>
            <span className="shop-delist-summary-sep"> · </span>
            <span>
              Slot {data.publishLimitPublished ?? 0}/{data.publishLimitTotal} · trống{' '}
              {data.remainingSlots}
            </span>
          </>
        ) : null}
      </>
    );
  }, [data]);

  const refreshAfterAction = async () => {
    setSelectedIds(new Set());
    await query.refetch();
    onChanged?.();
  };

  const toggleOne = (listingId: string, checked: boolean) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (checked) next.add(listingId);
      else next.delete(listingId);
      return next;
    });
  };

  const toggleAll = (checked: boolean) => {
    setSelectedIds(checked ? new Set(selectableIds) : new Set());
  };

  const confirmDeactivate = (item: ListingDelistItem) => {
    const listingId = item.listingId;
    if (!listingId) return;
    Modal.confirm({
      title: 'Hủy list sản phẩm này?',
      content: `${item.productName ?? item.tikTokProductId} chuyển sang inactive trên TikTok; bản mirror vẫn giữ để list lại sau.`,
      okText: 'Hủy list',
      okButtonProps: { danger: true },
      cancelText: 'Thôi',
      onOk: async () => {
        await deactivate.mutateAsync(listingId);
        await refreshAfterAction();
      },
    });
  };

  const confirmDelete = (item: ListingDelistItem) => {
    const listingId = item.listingId;
    if (!listingId) return;
    Modal.confirm({
      title: 'Xóa listing?',
      content: `${item.productName ?? item.tikTokProductId} sẽ bị xóa trên 4Seller và local. Không hoàn tác được.`,
      okText: 'Xóa',
      okButtonProps: { danger: true },
      cancelText: 'Thôi',
      onOk: async () => {
        await deleteListing.mutateAsync(listingId);
        await refreshAfterAction();
      },
    });
  };

  const confirmBulkDeactivate = () => {
    const targets = selectableItems.filter(
      (item) => selectedIds.has(item.listingId) && isListingActive(item),
    );
    if (targets.length === 0) return;
    Modal.confirm({
      title: `Hủy list ${targets.length} sản phẩm?`,
      content: 'Các listing đang active sẽ chuyển inactive trên TikTok; bản mirror vẫn giữ.',
      okText: 'Hủy list',
      okButtonProps: { danger: true },
      cancelText: 'Thôi',
      onOk: async () => {
        setBulkBusy(true);
        try {
          for (const item of targets) {
            await deactivate.mutateAsync(item.listingId);
          }
          await refreshAfterAction();
        } finally {
          setBulkBusy(false);
        }
      },
    });
  };

  const confirmBulkDelete = () => {
    const targets = selectableItems.filter((item) => selectedIds.has(item.listingId));
    if (targets.length === 0) return;
    Modal.confirm({
      title: `Xóa ${targets.length} listing?`,
      content: 'Sẽ xóa trên 4Seller và local. Không hoàn tác được.',
      okText: 'Xóa',
      okButtonProps: { danger: true },
      cancelText: 'Thôi',
      onOk: async () => {
        setBulkBusy(true);
        try {
          for (const item of targets) {
            await deleteListing.mutateAsync(item.listingId);
          }
          await refreshAfterAction();
        } finally {
          setBulkBusy(false);
        }
      },
    });
  };

  const rowBusy = deactivate.isPending || deleteListing.isPending || bulkBusy;

  const renderActions = (item: ListingDelistItem) => {
    if (!canManage) return null;

    if (item.listingId == null) {
      return (
        <Tooltip title="Chưa mirror listing về hệ thống">
          <span className="shop-delist-hint">—</span>
        </Tooltip>
      );
    }

    const active = isListingActive(item);
    const deactivating = deactivate.isPending && deactivate.variables === item.listingId;
    const deleting = deleteListing.isPending && deleteListing.variables === item.listingId;

    return (
      <div className="shop-delist-actions">
        {active ? (
          <Tooltip title="Hủy list">
            <Button
              type="text"
              size="small"
              danger
              icon={<ScissorOutlined />}
              loading={deactivating}
              disabled={rowBusy && !deactivating}
              onClick={() => confirmDeactivate(item)}
              aria-label="Hủy list"
            />
          </Tooltip>
        ) : (
          <Tooltip title={`Không active (${item.siteStatus ?? '—'})`}>
            <span className="shop-delist-hint">—</span>
          </Tooltip>
        )}
        <Tooltip title="Xóa">
          <Button
            type="text"
            size="small"
            danger
            icon={<DeleteOutlined />}
            loading={deleting}
            disabled={rowBusy && !deleting}
            onClick={() => confirmDelete(item)}
            aria-label="Xóa"
          />
        </Tooltip>
      </div>
    );
  };

  return (
    <Modal
      open={open}
      onCancel={onClose}
      footer={null}
      destroyOnHidden
      width={isMobile ? '100%' : 960}
      className={isMobile ? 'shop-delist-modal is-mobile' : 'shop-delist-modal'}
      title={
        <div className="shop-delist-modal-title">
          <div className="shop-delist-modal-heading">
            <span className="shop-delist-modal-name">Tối ưu listing</span>
            {shopName ? (
              <Text type="secondary" className="shop-delist-modal-shop">
                · {shopName}
              </Text>
            ) : null}
          </div>
          <Button
            type="text"
            size="small"
            icon={<ReloadOutlined />}
            loading={query.isFetching}
            disabled={bulkBusy}
            onClick={() => void query.refetch()}
            aria-label="Tính lại"
          />
        </div>
      }
    >
      <PageContentSpin spinning={query.isLoading || bulkBusy}>
        {summaryLine ? <div className="shop-delist-summary">{summaryLine}</div> : null}

        {query.isError ? (
          <Text type="danger" className="shop-delist-warning">
            {query.error instanceof Error ? query.error.message : 'Không tải được đề xuất.'}
          </Text>
        ) : null}

        {data?.warnings[0] ? (
          <Text type="secondary" className="shop-delist-warning" title={data.warnings.join('\n')}>
            {data.warnings[0]}
            {data.warnings.length > 1 ? ` (+${data.warnings.length - 1})` : ''}
          </Text>
        ) : null}

        {scoredNothing ? (
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description={
              <span className="shop-delist-hint">Không có listing nào đủ yếu để đề xuất hủy.</span>
            }
          />
        ) : null}

        {items.length > 0 ? (
          <>
            <div className="shop-delist-toolbar">
              <div className="shop-delist-toolbar-left">
                {canManage ? (
                  <Checkbox
                    checked={allSelected}
                    indeterminate={!allSelected && someSelected}
                    disabled={selectableIds.length === 0 || bulkBusy}
                    onChange={(event) => toggleAll(event.target.checked)}
                    aria-label="Chọn tất cả listing đã link trong bộ lọc"
                  />
                ) : null}
                <Segmented
                  size="small"
                  className={`shop-delist-verdict-filter is-${verdictFilter}`}
                  value={verdictFilter}
                  disabled={bulkBusy}
                  onChange={(value) => {
                    setVerdictFilter(value as VerdictFilter);
                    setSelectedIds(new Set());
                  }}
                  options={[
                    { label: `Tất cả ${items.length}`, value: 'all' },
                    {
                      label: (
                        <span className="shop-delist-filter-delist">
                          Cần hủy {delistInList}
                        </span>
                      ),
                      value: 'delist',
                      disabled: delistInList === 0,
                    },
                    {
                      label: (
                        <span className="shop-delist-filter-review">
                          Xem xét {reviewInList}
                        </span>
                      ),
                      value: 'review',
                      disabled: reviewInList === 0,
                    },
                  ]}
                />
                <Text type="secondary" className="shop-delist-toolbar-meta">
                  {selectedCount > 0
                    ? `Đã chọn ${selectedCount}/${selectableIds.length}`
                    : `${filteredItems.length} đề xuất`}
                </Text>
              </div>
              {canManage && selectedCount > 0 ? (
                <div className="shop-delist-toolbar-actions">
                  <Button
                    size="small"
                    danger
                    icon={<ScissorOutlined />}
                    disabled={selectedActiveCount === 0 || bulkBusy}
                    loading={bulkBusy}
                    onClick={confirmBulkDeactivate}
                  >
                    Hủy list
                    {selectedActiveCount > 0 ? ` ${selectedActiveCount}` : ''}
                  </Button>
                  <Button
                    size="small"
                    danger
                    icon={<DeleteOutlined />}
                    disabled={bulkBusy}
                    loading={bulkBusy}
                    onClick={confirmBulkDelete}
                  >
                    Xóa {selectedCount}
                  </Button>
                </div>
              ) : null}
            </div>

            {filterEmpty ? (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description={
                  <span className="shop-delist-hint">
                    {verdictFilter === 'delist'
                      ? 'Không có listing Cần hủy trong danh sách này.'
                      : 'Không có listing Xem xét trong danh sách này.'}
                  </span>
                }
              />
            ) : (
              <div className="shop-delist-table-wrap">
                <table className="shop-delist-table">
                  <thead>
                    <tr>
                      {canManage ? <th scope="col" className="shop-delist-check" /> : null}
                      <th scope="col">Sản phẩm</th>
                      <th scope="col" className="shop-delist-score-col">
                        Điểm
                      </th>
                      <th scope="col">Lý do</th>
                      {canManage ? (
                        <th scope="col" className="shop-delist-action" aria-label="Hành động" />
                      ) : null}
                    </tr>
                  </thead>
                  <tbody>
                    {filteredItems.map((item) => {
                      const listingId = item.listingId ?? null;
                      const checked = listingId != null && selectedIds.has(listingId);
                      const primaryReason = item.reasons[0] ?? '—';
                      const extraReasons = item.reasons.length > 1 ? item.reasons.slice(1) : [];
                      return (
                        <tr
                          key={item.tikTokProductId}
                          className={checked ? 'is-selected' : undefined}
                        >
                          {canManage ? (
                            <td className="shop-delist-check">
                              {listingId == null ? (
                                <Tooltip title="Chưa mirror — không chọn được">
                                  <Checkbox disabled />
                                </Tooltip>
                              ) : (
                                <Checkbox
                                  checked={checked}
                                  disabled={bulkBusy}
                                  onChange={(event) => toggleOne(listingId, event.target.checked)}
                                  aria-label={`Chọn ${item.productName ?? item.tikTokProductId}`}
                                />
                              )}
                            </td>
                          ) : null}
                          <td>
                            <div className="shop-delist-product">
                              <DelistProductThumb src={firstImage(item.mainImage)} />
                              <div className="shop-delist-product-body">
                                <div className="shop-delist-name">
                                  {item.productName ?? '—'}
                                </div>
                                <DelistProductMeta
                                  item={item}
                                  currency={data?.currency || 'USD'}
                                />
                              </div>
                            </div>
                          </td>
                          <td className="shop-delist-score-col">
                            <Tooltip
                              title={item.verdict === 'delist' ? 'Nên hủy' : 'Cần xem xét'}
                            >
                              <Tag color={scoreTone(item.score)} className="app-tag-compact">
                                {item.score}
                              </Tag>
                            </Tooltip>
                          </td>
                          <td>
                            <Tooltip
                              title={
                                extraReasons.length > 0 ? (
                                  <ul className="shop-delist-reason-tip">
                                    {item.reasons.map((reason) => (
                                      <li key={reason}>{reason}</li>
                                    ))}
                                  </ul>
                                ) : (
                                  primaryReason
                                )
                              }
                            >
                              <span className="shop-delist-reason">
                                {primaryReason}
                                {extraReasons.length > 0 ? ` +${extraReasons.length}` : ''}
                              </span>
                            </Tooltip>
                          </td>
                          {canManage ? (
                            <td className="shop-delist-action">{renderActions(item)}</td>
                          ) : null}
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </>
        ) : null}
      </PageContentSpin>
    </Modal>
  );
};
