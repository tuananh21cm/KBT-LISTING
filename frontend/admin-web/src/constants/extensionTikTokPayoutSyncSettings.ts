/** Setting keys for tiktok-shop-extension payout Auto Sync + Telegram
 * (must match backend ExtensionTikTokPayoutSyncSettingKeys). */
export const EXTENSION_TIKTOK_PAYOUT_SYNC_SETTING_KEYS = {
  enabled: 'extension.tiktok_payout_sync.enabled',
  intervalValue: 'extension.tiktok_payout_sync.interval_value',
  intervalUnit: 'extension.tiktok_payout_sync.interval_unit',
  maxDate: 'extension.tiktok_payout_sync.max_date',
  notifyNewPayment: 'extension.tiktok_payout_sync.notify_new_payment',
  telegramChatId: 'extension.tiktok_payout_sync.telegram_chat_id',
} as const;

/** Interval unit values. */
export const EXTENSION_TIKTOK_PAYOUT_SYNC_UNITS = {
  seconds: 'seconds',
  minutes: 'minutes',
} as const;
