export type OrderStatusKey =
  | 'all'
  | 'pendingProcess'
  | 'purchased'
  | 'awaitingCollection'
  | 'inTransit'
  | 'delivered'
  | 'canceled'
  | 'refund';

export interface OrderStatusMeta {
  key: OrderStatusKey;
  label: string;
  /** Query param for /orders list filter (platformOrderStatus / workflow tab) */
  platformOrderStatus?: string;
  tone?: 'default' | 'warn' | 'danger' | 'success';
  /** Include in pie chart (exclude "all") */
  inPie: boolean;
}

export const ORDER_STATUS_META: OrderStatusMeta[] = [
  { key: 'all', label: 'Tất cả', tone: 'default', inPie: false },
  {
    key: 'pendingProcess',
    label: 'Chờ xử lý',
    platformOrderStatus: 'PENDING_PROCESS',
    tone: 'warn',
    inPie: true,
  },
  {
    key: 'purchased',
    label: 'Đã mua',
    platformOrderStatus: 'PURCHASED',
    tone: 'warn',
    inPie: true,
  },
  {
    key: 'awaitingCollection',
    label: 'Chờ lấy',
    platformOrderStatus: 'AWAITING_COLLECTION_GROUP',
    tone: 'default',
    inPie: true,
  },
  {
    key: 'inTransit',
    label: 'Đang giao',
    platformOrderStatus: 'IN_TRANSIT',
    tone: 'default',
    inPie: true,
  },
  {
    key: 'delivered',
    label: 'Đã giao',
    platformOrderStatus: 'DELIVERED',
    tone: 'success',
    inPie: true,
  },
  {
    key: 'canceled',
    label: 'Đã hủy',
    platformOrderStatus: 'CANCELLED',
    tone: 'danger',
    inPie: true,
  },
  {
    key: 'refund',
    label: 'Refund',
    platformOrderStatus: 'REFUND',
    tone: 'danger',
    inPie: true,
  },
];
