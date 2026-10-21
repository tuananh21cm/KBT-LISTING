/** Setting keys for the two background listing sync loops (must match backend ListingSyncSettingKeys). */
export const LISTING_SYNC_SETTING_KEYS = {
  draftEnabled: 'fourseller.listing_sync.draft.enabled',
  draftIntervalMinutes: 'fourseller.listing_sync.draft.interval_minutes',
  publishedEnabled: 'fourseller.listing_sync.published.enabled',
  publishedIntervalMinutes: 'fourseller.listing_sync.published.interval_minutes',
  publishedLookbackDays: 'fourseller.listing_sync.published.lookback_days',
} as const;
