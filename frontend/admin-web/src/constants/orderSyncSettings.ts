/** Setting keys for the two background order sync loops (must match backend OrderSyncSettingKeys). */
export const ORDER_SYNC_SETTING_KEYS = {
  newEnabled: 'fourseller.order_sync.new.enabled',
  newIntervalMinutes: 'fourseller.order_sync.new.interval_minutes',
  newLookbackDays: 'fourseller.order_sync.new.lookback_days',
  newBatchSize: 'fourseller.order_sync.new.batch_size',
  statusEnabled: 'fourseller.order_sync.status.enabled',
  statusIntervalHours: 'fourseller.order_sync.status.interval_hours',
  statusLookbackDays: 'fourseller.order_sync.status.lookback_days',
  statusBatchSize: 'fourseller.order_sync.status.batch_size',
} as const;

/** Bounds for the light new-orders loop (must match backend OrderSyncSettingKeys). */
export const ORDER_SYNC_NEW_BOUNDS = {
  intervalMinutes: { min: 1, max: 1440, default: 5 },
  lookbackDays: { min: 1, max: 7, default: 2 },
  batchSize: { min: 1, max: 100, default: 15 },
} as const;

/** Bounds for the heavier status loop (must match backend OrderSyncSettingKeys). */
export const ORDER_SYNC_STATUS_BOUNDS = {
  intervalHours: { min: 1, max: 168, default: 12 },
  lookbackDays: { min: 1, max: 60, default: 30 },
  batchSize: { min: 1, max: 100, default: 10 },
} as const;
