/**
 * Nhãn hiển thị trạng thái SHEIN thuần của billno (không liên quan đơn TikTok/4Seller).
 *
 * Giữ nguyên mã thô trong DB (`SupplierStatusRaw`):
 * - detail: `subOrderStatus[].packageState` (common_delivered / shipped / processing / canceled / …)
 * - list: trường `status` (3 / 5 / 6 / 7 / 13 / 16) — khác `orderStatus` (=10 gần như mọi đơn)
 *
 * Survey raw payload: status 3↔processing, 5↔shipped, 6↔common_delivered, 13↔refunded, 16↔hủy/ghost.
 */

export type SheinSupplierStatusTag = {
  /** Mã thô SHEIN đang hiển thị */
  raw: string;
  /** Nhãn tiếng Việt */
  label: string;
  /** Nhãn ngắn trên bảng */
  shortLabel: string;
  color: string;
};

/** packageState + list.status → text. Không gộp về group nội bộ. */
const RAW_LABELS: Record<string, { label: string; shortLabel: string; color: string }> = {
  // packageState
  processing: { label: 'Đang xử lý', shortLabel: 'Xử lý', color: 'default' },
  packing: { label: 'Đang đóng gói', shortLabel: 'Đóng gói', color: 'default' },
  preparing: { label: 'Đang chuẩn bị', shortLabel: 'Chuẩn bị', color: 'default' },
  shipped: { label: 'Đã gửi', shortLabel: 'Gửi', color: 'blue' },
  in_transit: { label: 'Đang vận chuyển', shortLabel: 'VC', color: 'blue' },
  dispatched: { label: 'Đã xuất kho', shortLabel: 'Xuất kho', color: 'blue' },
  delivered: { label: 'Đã giao', shortLabel: 'Giao', color: 'green' },
  common_delivered: { label: 'Đã giao', shortLabel: 'Giao', color: 'green' },
  received: { label: 'Đã nhận', shortLabel: 'Nhận', color: 'green' },
  signed: { label: 'Đã ký nhận', shortLabel: 'Ký nhận', color: 'green' },
  canceled: { label: 'Đã hủy', shortLabel: 'Hủy', color: 'red' },
  cancelled: { label: 'Đã hủy', shortLabel: 'Hủy', color: 'red' },
  refunded: { label: 'Đã hoàn', shortLabel: 'Hoàn', color: 'magenta' },
  // list/detail `status` (không phải orderStatus)
  '3': { label: 'Đang xử lý', shortLabel: 'Xử lý', color: 'default' },
  '7': { label: 'Đang xử lý', shortLabel: 'Xử lý', color: 'default' },
  '5': { label: 'Đã gửi', shortLabel: 'Gửi', color: 'blue' },
  '6': { label: 'Đã giao', shortLabel: 'Giao', color: 'green' },
  '13': { label: 'Đã hoàn', shortLabel: 'Hoàn', color: 'magenta' },
  '14': { label: 'Đã hoàn', shortLabel: 'Hoàn', color: 'magenta' },
  '16': { label: 'Đã hủy', shortLabel: 'Hủy', color: 'red' },
};

/** Mã canonical để hiển thị — ưu tiên raw (packageState/status), không dùng group nội bộ. */
export function resolveSheinStatusRaw(
  status?: string | null,
  statusRaw?: string | null,
): string | null {
  const raw = (statusRaw || '').trim();
  if (raw) return raw;
  const group = (status || '').trim();
  return group || null;
}

export function sheinSupplierStatusTag(
  status?: string | null,
  statusRaw?: string | null,
): SheinSupplierStatusTag | null {
  const raw = resolveSheinStatusRaw(status, statusRaw);
  if (!raw) return null;
  const key = raw.toLowerCase();
  const mapped = RAW_LABELS[key];
  if (mapped) {
    return { raw, label: mapped.label, shortLabel: mapped.shortLabel, color: mapped.color };
  }
  // Chưa biết mã — hiện đúng chuỗi SHEIN, không bịa group.
  return { raw, label: raw, shortLabel: raw, color: 'default' };
}

export function sheinSupplierStatusTooltip(
  status?: string | null,
  statusRaw?: string | null,
): string | undefined {
  const tag = sheinSupplierStatusTag(status, statusRaw);
  if (!tag) return undefined;
  if (tag.label.toLowerCase() === tag.raw.toLowerCase()) return undefined;
  return `SHEIN: ${tag.raw}`;
}
