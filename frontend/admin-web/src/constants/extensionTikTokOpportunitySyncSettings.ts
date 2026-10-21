/** Setting keys for tiktok-shop-extension Product opportunities Auto Sync
 * (must match backend ExtensionTikTokOpportunitySyncSettingKeys). */
export const EXTENSION_TIKTOK_OPPORTUNITY_SYNC_SETTING_KEYS = {
  enabled: 'extension.tiktok_opportunity_sync.enabled',
  intervalValue: 'extension.tiktok_opportunity_sync.interval_value',
  intervalUnit: 'extension.tiktok_opportunity_sync.interval_unit',
} as const;

/** Interval unit values. */
export const EXTENSION_TIKTOK_OPPORTUNITY_SYNC_UNITS = {
  seconds: 'seconds',
  minutes: 'minutes',
} as const;
