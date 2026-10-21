/**
 * Một lần mua hàng ở nhà cung cấp (SHEIN/Temu…) hoặc một khoản phí nhập tay.
 *
 * Khác `OrderSupplierLink` cũ (luôn nằm bên trong một đơn sàn), dòng ở đây có thể **chưa** thuộc
 * đơn nào — đó chính là hàng đợi trang này tồn tại để xử lý.
 */
export interface SupplierPurchase {
  id: string;
  orderId: string | null;
  platformOrderId: string | null;
  fourSellerOrderId: number | null;
  shopName: string | null;
  supplierOrderNo: string | null;
  supplierOrderUrl: string | null;
  /** `shein` | `manual` */
  source: string;
  /** `matched` | `unmatched` | `ignored` */
  matchState: string;
  skipReason: string | null;
  skipEvidenceJson: string | null;
  isCharged: boolean;
  supplierTotal: number | null;
  supplierSubtotal: number | null;
  currency: string;
  orderedAt: string | null;
  supplierPaidAt: string | null;
  supplierUnpaid: boolean;
  isMembershipOrVirtual: boolean;
  supplierStatus: string | null;
  trackingNo: string | null;
  warehouseTrackingNo?: string | null;
  warehouseTrackingSeenAt?: string | null;
  /** `none` | `warehouse` | `carrier` */
  trackingKind: string;
  /** Tên hãng VC từ NCC (USPS…), khi đã nhận diện. */
  carrier?: string | null;
  receiverName: string | null;
  receiverPhone: string | null;
  receiverPostalCode: string | null;
  fulfilAccountId: string | null;
  fulfilAccountName: string | null;
  /** Người mua hàng hiệu lực (lần mua, fallback đơn sàn). */
  paidByUserId?: string | null;
  /** Tên người mua hàng — cột Fulfill. */
  paidByUserName?: string | null;
  firstSeenAt: string;
  lastSeenAt: string;
  lastRunId: string | null;
  matchedAt: string | null;
  /** Người xử lý tự chọn đơn — đồng bộ SHEIN không tự chuyển lần mua này sang đơn khác nữa. */
  manualLinkedAt?: string | null;
}

export interface SupplierPurchaseQuery {
  matchState?: string;
  skipReason?: string;
  fulfilAccountId?: string;
  orderId?: string;
  paidByUserId?: string;
  from?: string;
  to?: string;
  keyword?: string;
  pageCurrent?: number;
  pageSize?: number;
}

export interface SupplierPurchaseListPage {
  items: SupplierPurchase[];
  total: number;
  pageCurrent: number;
  pageSize: number;
  /** Số dòng chưa gắn — badge tổng, không phụ thuộc bộ lọc đang xem. */
  unmatchedCount: number;
  ignoredCount: number;
}

/** Trạng thái khớp — trùng `Shared.Constants.SupplierPurchaseMatchStates`. */
export const SUPPLIER_PURCHASE_MATCH_STATES = {
  matched: 'Đã gắn',
  unmatched: 'Chưa gắn',
  ignored: 'Đã bỏ qua',
} as const;

/**
 * Nhãn tiếng Việt cho mã lý do bỏ qua (`Shared.Constants.SheinSkipReasons`).
 *
 * Trước đây extension dò chuỗi tiếng Việt trong `Notes` để đếm lý do, nên sáu trong tám nhánh biến
 * mất khỏi thống kê. Mã ổn định là hợp đồng, chuỗi hiển thị chỉ nằm ở đây.
 */
export const SUPPLIER_PURCHASE_SKIP_REASONS: Record<string, string> = {
  membership_virtual: 'SHEIN Club / Saver / hàng ảo',
  unpaid_ghost: 'Chưa thanh toán (SHEIN tự huỷ)',
  terminal_unlinked: 'Đã huỷ/hoàn trên SHEIN (đã gỡ / không gắn)',
  missing_address: 'Payload thiếu điện thoại hoặc mã bưu chính',
  address_conflict: 'Điện thoại/mã bưu chính mâu thuẫn với đơn đã gắn',
  ambiguous_match: 'Nhiều đơn cùng khớp, không phân giải được',
  sibling_linked: 'Đơn cùng khách đã gắn mã khác',
  no_local_match: 'Không đơn nào trong pool khớp điện thoại + mã bưu chính',
  duplicate_billno: 'Mã đang thuộc một đơn khác',
  ledger_deleted: 'Đã khóa (dữ liệu cũ — xoá sổ cái trước đây; xoá mới sẽ xóa hẳn lần mua)',
  operator_ignored: 'Người xử lý bấm bỏ qua',
  purchased_before_order: 'Mua trước khi đơn sàn được đặt',
  manual_detach: 'Người xử lý tự gỡ khỏi đơn',
  rebalanced_to_sibling: 'Đã chuyển sang đơn cùng khách còn trống',
  manual_link_locked: 'Giữ theo đơn người xử lý đã gắn tay',
  platform_canceled: 'Đơn sàn đã hủy/hoàn — mua sau khi hủy',
  moved_off_canceled_order: 'Đã chuyển khỏi đơn đã hủy sang đơn còn hiệu lực',
};
