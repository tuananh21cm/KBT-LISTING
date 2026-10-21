/** Setting keys for tiktok-shop-extension auto-sync + Telegram alerts
 * (must match backend ExtensionTikTokShopSyncSettingKeys). */
export const EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS = {
  enabled: 'extension.tiktok_shop_sync.enabled',
  intervalValue: 'extension.tiktok_shop_sync.interval_value',
  intervalUnit: 'extension.tiktok_shop_sync.interval_unit',
  notifyEmailChange: 'extension.tiktok_shop_sync.notify_email_change',
  notifyMobileChange: 'extension.tiktok_shop_sync.notify_mobile_change',
  notifyBankChange: 'extension.tiktok_shop_sync.notify_bank_change',
  notifyBankLast4Mismatch: 'extension.tiktok_shop_sync.notify_bank_last4_mismatch',
  notifyStatusChange: 'extension.tiktok_shop_sync.notify_status_change',
  telegramChatId: 'extension.tiktok_shop_sync.telegram_chat_id',
} as const;

/** Interval unit values. */
export const EXTENSION_TIKTOK_SHOP_SYNC_UNITS = {
  seconds: 'seconds',
  minutes: 'minutes',
} as const;
