/**
 * Setting keys cho vòng quét chi tiết đơn SHEIN chạy trên API server
 * (phải khớp backend `SheinDetailSyncSettingKeys`).
 *
 * Khác `extension.shein_sync.*`: vòng này không phân trang `order/list`, chỉ gọi
 * `get_order_info` cho billno đã có trong sổ mà còn thiếu tracking thật / chưa Mark Ship,
 * dùng cookie + header extension đã lưu trên tài khoản mua hàng.
 */
export const SHEIN_DETAIL_SYNC_SETTING_KEYS = {
  enabled: 'shein.order_detail_sync.enabled',
  intervalMinutes: 'shein.order_detail_sync.interval_minutes',
  maxBillnos: 'shein.order_detail_sync.max_billnos',
  maxMarkShipPerPass: 'shein.order_detail_sync.max_mark_ship_per_pass',
  notifyCarrierTracking: 'shein.order_detail_sync.notify_carrier_tracking',
  telegramChatId: 'shein.order_detail_sync.telegram_chat_id',
} as const;

/** Chu kỳ quét (phút) — clamp phải khớp backend. */
export const SHEIN_DETAIL_SYNC_INTERVAL = {
  min: 1,
  max: 180,
  default: 10,
} as const;

/** Trần billno lấy chi tiết mỗi lượt. */
export const SHEIN_DETAIL_SYNC_MAX_BILLNOS = {
  min: 5,
  max: 200,
  default: 40,
} as const;

/** Trần lần gọi 4Seller Mark Ship mỗi lượt. */
export const SHEIN_DETAIL_SYNC_MAX_MARK_SHIP = {
  min: 1,
  max: 50,
  default: 10,
} as const;
