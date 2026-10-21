import { useState } from 'react';
import { Button, Empty, Modal, Tag, Tooltip, Typography } from 'antd';
import {
  CaretDownOutlined,
  CaretUpOutlined,
  ReloadOutlined,
  ScissorOutlined,
} from '@ant-design/icons';
import type {
  ListingDelistItem,
  ListingDelistSuggestionParams,
  ListingDelistSuggestions as ListingDelistSuggestionsData,
} from '@drop/types/listing';
import { useListingsDeps } from './ListingsDepsContext';
import { formatCompact, formatConversion, formatMoney } from './listingAnalyticsFormat';

const { Text } = Typography;

/** Số dòng hỏi server — đủ để nhả hết slot cần thiết mà vẫn nhẹ. */
const SUGGESTION_LIMIT = 12;

const PILLARS = [
  { key: 'sales', label: 'Không bán được', weight: 40 },
  { key: 'funnel', label: 'Funnel dưới trung vị', weight: 25 },
  { key: 'reach', label: 'Ít hiển thị', weight: 15 },
  { key: 'trend', label: 'Đang giảm', weight: 10 },
  { key: 'keyword', label: 'Lệch keyword shop', weight: 10 },
] as const;

const scoreTone = (score: number): 'error' | 'warning' =>
  score >= 70 ? 'error' : 'warning';

const ScoreTooltip = ({ item }: { item: ListingDelistItem }) => (
  <div className="listing-delist-score-tip">
    {PILLARS.map((pillar) => (
      <div key={pillar.key} className="listing-delist-score-tip-row">
        <span>{pillar.label}</span>
        <span>
          {item.breakdown[pillar.key]}/{pillar.weight}
        </span>
      </div>
    ))}
    {item.breakdown.risingRelief > 0 ? (
      <div className="listing-delist-score-tip-row is-relief">
        <span>Trả lại vì đang tăng</span>
        <span>−{item.breakdown.risingRelief}</span>
      </div>
    ) : null}
    <div className="listing-delist-score-tip-row is-total">
      <span>Chuẩn hoá trên phần đo được</span>
      <span>
        {item.breakdown.available}/100 → {item.score}
      </span>
    </div>
  </div>
);

const MetaLine = ({ item, currency }: { item: ListingDelistItem; currency: string }) => (
  <div className="listing-delist-meta">
    <span>{formatMoney(item.gmv, currency)}</span>
    <span>·</span>
    <span>{item.orders} đơn</span>
    <span>·</span>
    <span>{formatCompact(item.impressions)} hiển thị</span>
    <span>·</span>
    <span>CTR {item.ctr == null ? '—' : formatConversion(item.ctr)}</span>
    <span>·</span>
    <span>đỉnh {item.peakOrders} đơn (7 ngày) · {item.daysTracked} lần sync</span>
    {item.daysListed != null ? (
      <>
        <span>·</span>
        <span>list {item.daysListed} ngày</span>
      </>
    ) : null}
  </div>
);

const SummaryChips = ({ data }: { data: ListingDelistSuggestionsData }) => (
  <div className="listing-delist-chips">
    <Tag color="error" className="app-tag-compact">
      Hủy {data.delistCount}
    </Tag>
    <Tag color="warning" className="app-tag-compact">
      Xem xét {data.reviewCount}
    </Tag>
    {data.publishLimitTotal ? (
      <Tag className="app-tag-compact">
        Slot {data.publishLimitPublished ?? 0}/{data.publishLimitTotal} · trống{' '}
        {data.remainingSlots}
      </Tag>
    ) : null}
    <Tooltip title="Đang bán tốt được loại; mới list dưới 7 ngày hoặc chưa có snapshot thì chưa chấm">
      <Tag className="app-tag-compact">
        Bỏ qua {data.skippedWinners} đang bán · {data.skippedTooNew} mới list
      </Tag>
    </Tooltip>
  </div>
);

export type ListingDelistSuggestionsProps = {
  /** Cùng scope với bảng analytics (ngày / shop / seller). */
  params: ListingDelistSuggestionParams;
  /** Cho phép bấm Hủy list (quyền manage của portal). */
  canManage: boolean;
  /** Mở sẵn panel khi vào từ nhiệm vụ «Hủy list rác để lấy slot». */
  defaultOpen?: boolean;
};

/**
 * «Đề xuất hủy list»: xếp hạng listing yếu nhất của shop theo công thức
 * <c>ListingDelistScoring</c> (không bán được 40 · funnel 25 · hiển thị 15 · xu hướng 10 ·
 * keyword 10, trả lại tối đa 20 khi đang tăng) rồi gợi ý keyword list thay vào.
 * Chỉ fetch khi panel mở.
 */
export function ListingDelistSuggestions({
  params,
  canManage,
  defaultOpen = false,
}: ListingDelistSuggestionsProps) {
  const deps = useListingsDeps();
  const [open, setOpen] = useState(defaultOpen);

  const query = deps.useListingDelistSuggestions(
    { ...params, limit: SUGGESTION_LIMIT },
    { enabled: open },
  );
  const deactivate = deps.useDeactivateListing();

  const data = query.data;
  const items = data?.items ?? [];
  // "Không có gì để hủy" chỉ đúng khi thật sự đã chấm xong. Lúc lỗi hoặc lúc server trả warning
  // (chưa chọn shop, chưa có snapshot…) thì warning/lỗi mới là câu trả lời.
  const scoredNothing =
    !query.isLoading && !query.isError && items.length === 0 && data?.warnings.length === 0;

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
        await query.refetch();
      },
    });
  };

  return (
    <div className={`listing-delist${open ? ' is-open' : ''}`}>
      <Button
        type="link"
        size="small"
        className="listing-delist-toggle"
        icon={<ScissorOutlined />}
        onClick={() => setOpen((value) => !value)}
      >
        Đề xuất hủy list để lấy slot
        {open ? <CaretUpOutlined /> : <CaretDownOutlined />}
      </Button>

      {open ? (
        <div className="listing-delist-panel">
          <div className="listing-delist-panel-head">
            <Text className="listing-delist-headline">
              {query.isLoading
                ? 'Đang chấm điểm listing…'
                : (data?.headline ?? 'Chấm điểm listing yếu theo chỉ số của shop.')}
            </Text>
            <Button
              type="text"
              size="small"
              icon={<ReloadOutlined />}
              loading={query.isFetching}
              onClick={() => void query.refetch()}
              aria-label="Tính lại"
              title="Tính lại"
            />
          </div>

          {data ? <SummaryChips data={data} /> : null}

          {query.isError ? (
            <Text type="danger" className="listing-delist-warning">
              {query.error instanceof Error ? query.error.message : 'Không tải được đề xuất.'}
            </Text>
          ) : null}

          {data?.warnings.map((warning) => (
            <Text key={warning} type="secondary" className="listing-delist-warning">
              {warning}
            </Text>
          ))}

          {scoredNothing ? (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={
                <span className="listing-delist-hint">
                  Không có listing nào đủ yếu để đề xuất hủy.
                </span>
              }
            />
          ) : null}

          {items.length > 0 ? (
            <div className="listing-delist-table-wrap">
              <table className="listing-delist-table">
                <thead>
                  <tr>
                    <th scope="col">#</th>
                    <th scope="col">Sản phẩm</th>
                    <th scope="col">Điểm</th>
                    <th scope="col">Lý do</th>
                    {canManage ? <th scope="col" aria-label="Hành động" /> : null}
                  </tr>
                </thead>
                <tbody>
                  {items.map((item, index) => (
                    <tr key={item.tikTokProductId}>
                      <td className="listing-delist-rank">{index + 1}</td>
                      <td>
                        <div className="listing-delist-name">{item.productName ?? '—'}</div>
                        <MetaLine item={item} currency={data?.currency ?? 'USD'} />
                      </td>
                      <td>
                        <Tooltip title={<ScoreTooltip item={item} />}>
                          <Tag color={scoreTone(item.score)} className="app-tag-compact">
                            {item.score}
                          </Tag>
                        </Tooltip>
                        <div className="listing-delist-verdict">
                          {item.verdict === 'delist' ? 'Hủy' : 'Xem xét'}
                        </div>
                      </td>
                      <td>
                        <ul className="listing-delist-reasons">
                          {item.reasons.map((reason) => (
                            <li key={reason}>{reason}</li>
                          ))}
                        </ul>
                      </td>
                      {canManage ? (
                        <td className="listing-delist-action">
                          {item.listingId == null ? (
                            <Tooltip title="Chưa mirror listing về hệ thống — sync Listings rồi thao tác">
                              <span className="listing-delist-hint">chưa link</span>
                            </Tooltip>
                          ) : item.siteStatus?.toLowerCase() === 'active' ? (
                            <Button
                              type="text"
                              size="small"
                              danger
                              loading={
                                deactivate.isPending && deactivate.variables === item.listingId
                              }
                              onClick={() => confirmDeactivate(item)}
                            >
                              Hủy list
                            </Button>
                          ) : (
                            <Tooltip title="Chỉ hủy được listing đang active trên shop">
                              <span className="listing-delist-hint">
                                {item.siteStatus ?? 'không active'}
                              </span>
                            </Tooltip>
                          )}
                        </td>
                      ) : null}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}

          {data && data.keywords.length > 0 ? (
            <div className="listing-delist-keywords">
              <div className="listing-delist-keywords-label">List thay vào</div>
              <div className="listing-delist-keywords-list">
                {data.keywords.map((keyword) => (
                  <Tooltip
                    key={`${keyword.lane}-${keyword.keyword}`}
                    title={`Volume ${formatCompact(keyword.searchVolume)}${
                      keyword.score == null ? '' : ` · điểm ngách ${keyword.score}`
                    }`}
                  >
                    <Tag
                      color={keyword.lane === 'listNow' ? 'green' : 'blue'}
                      className="app-tag-compact"
                    >
                      {keyword.keyword}
                    </Tag>
                  </Tooltip>
                ))}
              </div>
            </div>
          ) : null}

          {data?.snapshotDate ? (
            <Text type="secondary" className="listing-delist-footnote">
              Snapshot {data.snapshotDate}
              {data.rangeStart && data.rangeEnd
                ? ` · Compass ${data.rangeStart}→${data.rangeEnd} (cửa sổ ${data.benchmarks.windowDays} ngày)`
                : ''}
              · trung vị shop CTR {formatConversion(data.benchmarks.medianCtr)} · thêm giỏ{' '}
              {formatConversion(data.benchmarks.medianAtcRate)} · chốt đơn{' '}
              {formatConversion(data.benchmarks.medianOrderRate)} · đỉnh đơn = MAX trong{' '}
              {data.benchmarks.trendWindowDays} lần sync gần nhất (mỗi lần đã là tổng{' '}
              {data.benchmarks.windowDays} ngày Compass, không cộng SUM) · bỏ qua khi mới list dưới{' '}
              {data.benchmarks.minListedDays} ngày (theo ngày list 4Seller/TikTok).
            </Text>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
