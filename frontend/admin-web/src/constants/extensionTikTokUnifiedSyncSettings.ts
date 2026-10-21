/** Setting keys for Sync All / Sync Fast step toggles
 * (must match backend ExtensionTikTokUnifiedSyncSettingKeys). */
export const EXTENSION_TIKTOK_UNIFIED_SYNC_SETTING_KEYS = {
  stepInfo: 'extension.tiktok_unified_sync.step_info',
  stepFinance: 'extension.tiktok_unified_sync.step_finance',
  stepKeywords: 'extension.tiktok_unified_sync.step_keywords',
  stepAnalytics: 'extension.tiktok_unified_sync.step_analytics',
  stepHealthCenter: 'extension.tiktok_unified_sync.step_health_center',
  stepShopScore: 'extension.tiktok_unified_sync.step_shop_score',
} as const;

export const EXTENSION_TIKTOK_UNIFIED_SYNC_STEP_LABELS: Record<
  keyof typeof EXTENSION_TIKTOK_UNIFIED_SYNC_SETTING_KEYS,
  string
> = {
  stepInfo: 'Info',
  stepFinance: 'Finance',
  stepKeywords: 'Keywords',
  stepAnalytics: 'Analytics',
  stepHealthCenter: 'Health Center',
  stepShopScore: 'Shop Score',
};
