/** Setting keys for the fulfil-extension SHEIN auto-sync (must match backend ExtensionSheinSyncSettingKeys). */
export const EXTENSION_SHEIN_SYNC_SETTING_KEYS = {
  enabled: 'extension.shein_sync.enabled',
  intervalValue: 'extension.shein_sync.interval_value',
  intervalUnit: 'extension.shein_sync.interval_unit',
  /** new = pool fulfil; all = mọi đơn không huỷ/hoàn trong cửa sổ Lookback. */
  updateMode: 'extension.shein_sync.update_mode',
  concurrentThreads: 'extension.shein_sync.concurrent_threads',
  refreshLookbackDays: 'extension.shein_sync.refresh_lookback_days',
  /** @deprecated Always realtime (extension) — UI no longer exposes. */
  headerSource: 'extension.shein_sync.header_source',
} as const;

/** Clamp for SHEIN detail-fetch concurrency (must match backend). */
export const EXTENSION_SHEIN_SYNC_CONCURRENT_THREADS = {
  min: 1,
  max: 20,
  default: 10,
} as const;

/** List lookback (days) — cửa sổ `orderedAt` khi phân trang order/list. */
export const EXTENSION_SHEIN_SYNC_REFRESH_LOOKBACK = {
  min: 1,
  max: 60,
  default: 14,
} as const;

/** Interval unit values. */
export const EXTENSION_SHEIN_SYNC_UNITS = {
  seconds: 'seconds',
  minutes: 'minutes',
} as const;

/** Sync Order update mode values. */
export const EXTENSION_SHEIN_SYNC_UPDATE_MODES = {
  new: 'new',
  all: 'all',
} as const;
