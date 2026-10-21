/** Setting keys for background Product Discount sync (must match backend ProductDiscountSyncSettingKeys). */
export const PRODUCT_DISCOUNT_SYNC_SETTING_KEYS = {
  enabled: 'fourseller.product_discount_sync.enabled',
  intervalHours: 'fourseller.product_discount_sync.interval_hours',
} as const;

/** Setting keys for background Flash Deal sync (must match backend FlashDealSyncSettingKeys). */
export const FLASH_DEAL_SYNC_SETTING_KEYS = {
  enabled: 'fourseller.flash_deal_sync.enabled',
  intervalHours: 'fourseller.flash_deal_sync.interval_hours',
} as const;
