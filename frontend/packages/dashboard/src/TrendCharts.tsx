import { Column, Line } from '@ant-design/plots';
import { chartTheme } from './chartTheme';

export interface OrdersTrendPoint {
  date: string;
  metric: string;
  value: number;
  total: number;
}

/** Build stacked ok / canceled / refund points for {@link OrdersTrendChart}. */
export const buildOrdersTrendData = (
  days: {
    date: string;
    orders: number;
    ordersOk?: number;
    ordersCanceled?: number;
    ordersRefund?: number;
  }[],
  formatDate: (isoDate: string) => string,
  okLabel = 'Đơn hàng',
  canceledLabel = 'Đã hủy',
  refundLabel = 'Refund',
): OrdersTrendPoint[] =>
  days.flatMap((d) => {
    const total = d.orders;
    const ok = Math.min(d.ordersOk ?? 0, total);
    const hasSplit = d.ordersCanceled != null || d.ordersRefund != null;
    const canceled = hasSplit
      ? Math.max(0, Number(d.ordersCanceled) || 0)
      : Math.max(0, total - ok);
    const refund = hasSplit ? Math.max(0, Number(d.ordersRefund) || 0) : 0;
    return [
      { date: formatDate(d.date), metric: okLabel, value: ok, total },
      { date: formatDate(d.date), metric: canceledLabel, value: canceled, total },
      { date: formatDate(d.date), metric: refundLabel, value: refund, total },
    ];
  });

/** Stacked orders-ok vs canceled vs refund column chart — dashboard trend row. */
export const OrdersTrendChart = ({
  data,
  height = 220,
}: {
  data: OrdersTrendPoint[];
  height?: number;
}) => (
  <Column
    height={height}
    data={data}
    xField="date"
    yField="value"
    colorField="metric"
    stack
    scale={{ color: { range: ['#52c41a', '#ff7875', '#eb2f96'] } }}
    style={{ maxWidth: 28, radiusTopLeft: 3, radiusTopRight: 3 }}
    axis={chartTheme.axisSm}
    legend={{
      color: {
        position: 'top',
        layout: { justifyContent: 'flex-end' },
        ...chartTheme.legendColor,
      },
    }}
    tooltip={{
      shared: true,
      title: (d: { date?: string; total?: number }) => {
        const date = d?.date ?? '';
        const total = Number(d?.total || 0);
        return total > 0 ? `${date} · Tất cả ${total.toLocaleString('en-US')}` : date;
      },
      items: [
        (d: { metric?: string; value?: number }) => ({
          name: d.metric ?? '',
          value: `${Number(d.value || 0).toLocaleString('en-US')} đơn`,
        }),
      ],
    }}
    interaction={{
      elementHighlight: { background: true },
      tooltip: { shared: true },
    }}
  />
);

export interface SellerShopStatusPoint {
  seller: string;
  metric: string;
  value: number;
  total: number;
  operationalShops: number;
}

/** Max sellers on the dashboard “shops per seller” stacked column. */
export const SELLER_SHOP_CHART_TOP = 12;

/**
 * Build stacked Active / Deactivated / Suspended points for {@link SellerShopStatusChart}.
 * Sorted by total shops desc; top N sellers with ≥1 shop.
 */
export const buildSellerShopStatusData = (
  sellers: {
    displayName?: string | null;
    userId: string;
    activeShopCount?: number | null;
    deactivatedShopCount?: number | null;
    suspendedShopCount?: number | null;
    shopCount?: number | null;
    operationalShopCount?: number | null;
    participatingShopCount?: number | null;
  }[],
  topN = SELLER_SHOP_CHART_TOP,
  activeLabel = 'Active',
  deactivatedLabel = 'Deactivated',
  suspendedLabel = 'Suspended',
): SellerShopStatusPoint[] => {
  const ranked = sellers
    .map((s) => {
      const active = Math.max(0, Number(s.activeShopCount) || 0);
      const deactivated = Math.max(0, Number(s.deactivatedShopCount) || 0);
      const suspended = Math.max(0, Number(s.suspendedShopCount) || 0);
      const total =
        Number(s.shopCount) > 0
          ? Number(s.shopCount)
          : active + deactivated + suspended;
      return {
        seller: (s.displayName || s.userId).trim() || s.userId,
        active,
        deactivated,
        suspended,
        total,
        operationalShops:
          Number(s.operationalShopCount ?? s.participatingShopCount) || 0,
      };
    })
    .filter((s) => s.total > 0)
    .sort((a, b) => b.total - a.total || a.seller.localeCompare(b.seller))
    .slice(0, topN);

  return ranked.flatMap((s) => [
    {
      seller: s.seller,
      metric: activeLabel,
      value: s.active,
      total: s.total,
      operationalShops: s.operationalShops,
    },
    {
      seller: s.seller,
      metric: deactivatedLabel,
      value: s.deactivated,
      total: s.total,
      operationalShops: s.operationalShops,
    },
    {
      seller: s.seller,
      metric: suspendedLabel,
      value: s.suspended,
      total: s.total,
      operationalShops: s.operationalShops,
    },
  ]);
};

/** Stacked Active / Deactivated / Suspended shops per seller — mirrors OrdersTrendChart. */
export const SellerShopStatusChart = ({
  data,
  height = 320,
}: {
  data: SellerShopStatusPoint[];
  height?: number;
}) => (
  <Column
    height={height}
    data={data}
    xField="seller"
    yField="value"
    colorField="metric"
    stack
    scale={{ color: { range: ['#52c41a', '#8c8c8c', '#ff4d4f'] } }}
    style={{ maxWidth: 36, radiusTopLeft: 4, radiusTopRight: 4 }}
    marginBottom={28}
    axis={{
      ...chartTheme.axis,
      x: {
        ...chartTheme.axis.x,
        title: false,
        labelAutoRotate: false,
        labelAutoEllipsis: true,
        labelAutoHide: true,
      },
      y: {
        ...chartTheme.axis.y,
        title: false,
        labelFormatter: (v: number) => String(Math.round(Number(v) || 0)),
        gridStrokeOpacity: 1,
      },
    }}
    legend={{
      color: {
        position: 'top',
        layout: { justifyContent: 'flex-end' },
        ...chartTheme.legendColor,
      },
    }}
    tooltip={{
      shared: true,
      title: (d: {
        seller?: string;
        total?: number;
        operationalShops?: number;
      }) => {
        const seller = d?.seller ?? '';
        const total = Number(d?.total || 0);
        const operational = Number(d?.operationalShops || 0);
        if (total <= 0) return seller;
        const parts = [`${seller} · Tất cả ${total.toLocaleString('en-US')}`];
        if (operational > 0) {
          parts.push(`vận hành ${operational.toLocaleString('en-US')}`);
        }
        return parts.join(' · ');
      },
      items: [
        (d: { metric?: string; value?: number }) => ({
          name: d.metric ?? '',
          value: `${Number(d.value || 0).toLocaleString('en-US')} shop`,
        }),
      ],
    }}
    interaction={{
      elementHighlight: { background: true },
      tooltip: { shared: true },
    }}
  />
);

/** Revenue line chart — dashboard trend row. */
export const RevenueTrendChart = ({
  data,
  height = 220,
}: {
  data: { date: string; value: number }[];
  height?: number;
}) => (
  <Line
    height={height}
    data={data}
    xField="date"
    yField="value"
    style={{ lineWidth: 2, stroke: '#52c41a' }}
    axis={chartTheme.axisSm}
    point={{ size: 3, shape: 'circle', style: { fill: '#52c41a' } }}
  />
);

/** Listed-count column chart — dashboard trend row. */
export const ListedTrendChart = ({
  data,
  height = 220,
}: {
  data: { date: string; listed: number }[];
  height?: number;
}) => (
  <Column
    height={height}
    data={data}
    xField="date"
    yField="listed"
    style={{ maxWidth: 28, fill: '#13c2c2' }}
    axis={chartTheme.axisSm}
  />
);
