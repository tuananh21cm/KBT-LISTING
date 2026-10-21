export const LISTING_SIZE_CHART_SETTING_KEYS = {
  autoGenerateEnabled: 'listing.size_chart.auto_generate_enabled',
} as const;

export const parseListingSizeChartAutoGenerate = (value: string | undefined): boolean =>
  value?.trim().toLowerCase() === 'true' || value?.trim() === '1';
