export const LISTING_AUTO_PUBLISH_SETTING_KEYS = {
  enabled: 'listing.auto_publish.enabled',
  schedulerIntervalMinutes: 'listing.auto_publish.scheduler_interval_minutes',
  maxConcurrentPublishes: 'listing.auto_publish.max_concurrent_publishes',
  defaultIntervalSeconds: 'listing.auto_publish.default_interval_seconds',
  defaultMinDraftAgeMinutes: 'listing.auto_publish.default_min_draft_age_minutes',
  logRetentionDays: 'listing.auto_publish.log_retention_days',
} as const;

/** Minimum minutes a draft must exist before auto publish can pick it. */
export const LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES = 5;

export const LISTING_AUTO_PUBLISH_DEFAULTS = {
  schedulerIntervalMinutes: 5,
  maxConcurrentPublishes: 10,
  defaultIntervalSeconds: 120,
  defaultMinDraftAgeMinutes: LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES,
  logRetentionDays: 30,
} as const;

export const parseListingAutoPublishEnabled = (value: string | undefined): boolean =>
  value?.trim().toLowerCase() === 'true' || value?.trim() === '1';

export const LISTING_AUTO_PUBLISH_CONCURRENCY_HINT =
  'Số shop publish đồng thời tối đa trên VPS — mỗi shop vẫn tuân interval riêng';

export const LISTING_AUTO_PUBLISH_STEP_LABELS: Record<string, string> = {
  load_full: 'Load dữ liệu',
  load_from_db: 'Load DB',
  sync_4seller: 'Sync 4Seller',
  category: 'Danh mục',
  category_ai_request: 'AI category — request',
  category_ai_response: 'AI category — response',
  category_ai_failed: 'AI category lỗi',
  category_ai_invalid: 'AI category không hợp lệ',
  category_confirmed: 'Category khớp AI',
  category_assigned: 'Gán category từ AI',
  category_overridden: 'Ghi đè category',
  category_kept: 'Giữ category hiện tại',
  category_attributes_reset: 'Xoá Specifics của category cũ',
  commerce: 'Giá / SKU / tồn',
  specifics: 'Specifics',
  no_key_value_pairs: 'Không có Key: Value',
  required_defaults: 'Điền mặc định attribute bắt buộc',
  rewrite: 'Mô tả AI',
  media: 'Media',
  validate: 'Kiểm tra',
  shipping_template: 'Shipping template',
  warehouse: 'Warehouse',
  package_weight: 'Cân nặng',
  package_dims: 'Kích thước gói',
  size_chart: 'Size chart',
  raw_payload: 'Raw payload',
  persist_local: 'Lưu local',
  publish: 'Publish',
};

export const listingAutoPublishStepLabel = (code: string, label?: string | null): string =>
  label?.trim() || LISTING_AUTO_PUBLISH_STEP_LABELS[code] || code;

export const LISTING_AUTO_PUBLISH_QUEUE_LABELS: Record<string, string> = {
  queue: 'Queue',
  waiting: 'Waiting',
  processingReady: 'Processing',
  processing: 'Publishing',
  failed: 'Failed',
};

export const LISTING_AUTO_PUBLISH_QUEUE_TOOLTIPS: Record<string, string> = {
  queue: 'Trong hàng đợi auto publish — chờ các listing trước cùng shop xong (FIFO)',
  waiting: 'Sắp auto publish — listing kế tiếp của shop (chờ scheduler/interval)',
  processingReady: 'Interval đã hết — scheduler đang/bắt đầu xử lý pick listing kế tiếp',
  processing: 'Auto publish đang đăng listing — bấm xem tiến trình',
  retry: 'Auto publish lại — đưa listing về hàng đợi scheduler',
};
