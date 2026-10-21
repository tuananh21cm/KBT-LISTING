/** Setting keys for Telegram notification toggles (must match backend TelegramSettingKeys).
 * BotToken is configured in appsettings.json (Telegram section). Chat ids are in Settings.
 *
 * Routing:
 * - ordersChatId: đơn mới + tổng hợp đơn ngày + tổng hợp Seller
 * - cancelRefundChatId (ops): hủy/refund + USPS + track kho + tổng hợp fulfil + thông báo message
 *   + digest đơn N giờ chưa track (cùng giờ tổng hợp xử lý đơn)
 */
export const TELEGRAM_SETTING_KEYS = {
  notifyNewOrder: 'telegram.notify_new_order',
  notifyCancelRefund: 'telegram.notify_cancel_refund',
  ordersChatId: 'telegram.orders_chat_id',
  cancelRefundChatId: 'telegram.cancel_refund_chat_id',
  dailySummaryEnabled: 'telegram.daily_summary.enabled',
  dailySummaryHour: 'telegram.daily_summary.hour',
  dailySummaryLastSentDate: 'telegram.daily_summary.last_sent_date',
  sellerSummaryEnabled: 'telegram.seller_summary.enabled',
  sellerSummaryHour: 'telegram.seller_summary.hour',
  sellerSummaryLastSentDate: 'telegram.seller_summary.last_sent_date',
  fulfilSummaryEnabled: 'telegram.fulfil_summary.enabled',
  fulfilSummaryHour: 'telegram.fulfil_summary.hour',
  fulfilSummaryLastSentDate: 'telegram.fulfil_summary.last_sent_date',
  notifyUsps: 'telegram.notify_usps',
  notifyWarehouseTrack: 'telegram.notify_warehouse_track',
  notifyOrderMessage: 'telegram.notify_order_message',
  notifyStaleNoTracking: 'telegram.notify_stale_no_tracking',
  notifyStaleNoTrackingHours: 'telegram.notify_stale_no_tracking.hours',
} as const;

export const TELEGRAM_STALE_NO_TRACKING_DEFAULTS = {
  hours: 72,
  min: 1,
  max: 720,
} as const;
