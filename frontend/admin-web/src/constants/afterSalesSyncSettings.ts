/**
 * Setting keys for the 4Seller after-sales (return / refund) sync.
 * Must match backend `AfterSalesSyncSettingKeys`.
 *
 * `trigger` asks 4Seller to pull return cases from TikTok Shop; `pull` reads them back into our
 * orders. They run on separate clocks, hence separate on/off + interval pairs.
 */
export const AFTER_SALES_SYNC_SETTING_KEYS = {
  triggerEnabled: 'fourseller.after_sales.trigger.enabled',
  triggerIntervalMinutes: 'fourseller.after_sales.trigger.interval_minutes',
  pullEnabled: 'fourseller.after_sales.pull.enabled',
  pullIntervalMinutes: 'fourseller.after_sales.pull.interval_minutes',
  notifyEnabled: 'fourseller.after_sales.notify_enabled',
  telegramChatId: 'fourseller.after_sales.telegram_chat_id',
} as const;

/** Interval bounds enforced by the backend orchestrator. */
export const AFTER_SALES_SYNC_INTERVAL_BOUNDS = {
  min: 1,
  max: 1440,
} as const;
