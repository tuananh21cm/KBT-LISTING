/** Setting keys for listing Rewrite title handling (must match backend ListingRewriteSettingKeys). */
export const LISTING_REWRITE_TITLE_SETTING_KEYS = {
  titleMode: 'listing.rewrite.title_mode',
  allowRepeat: 'listing.rewrite.allow_repeat',
} as const;

export const LISTING_REWRITE_TITLE_MODES = {
  stripBrands: 'strip_brands',
  rewrite: 'rewrite',
} as const;

export type ListingRewriteTitleMode =
  (typeof LISTING_REWRITE_TITLE_MODES)[keyof typeof LISTING_REWRITE_TITLE_MODES];

export const LISTING_REWRITE_TITLE_MODE_DEFAULT: ListingRewriteTitleMode =
  LISTING_REWRITE_TITLE_MODES.stripBrands;

export function parseListingRewriteTitleMode(
  raw: string | undefined,
): ListingRewriteTitleMode {
  const value = (raw ?? '').trim().toLowerCase();
  return value === LISTING_REWRITE_TITLE_MODES.rewrite
    ? LISTING_REWRITE_TITLE_MODES.rewrite
    : LISTING_REWRITE_TITLE_MODE_DEFAULT;
}

export function parseListingRewriteAllowRepeat(
  raw: string | undefined,
): boolean {
  return (raw ?? '').trim().toLowerCase() === 'true';
}

export function listingRewriteTitleModeLabel(
  mode: ListingRewriteTitleMode,
): string {
  return mode === LISTING_REWRITE_TITLE_MODES.rewrite
    ? 'Machine-first SEO'
    : 'Xóa thương hiệu';
}

export function listingRewriteTitleTooltip(
  mode: ListingRewriteTitleMode | null,
  rewritten: boolean,
  allowRepeat = false,
): string {
  if (rewritten && !allowRepeat) {
    return 'Đã Rewrite — dùng Hoàn tác nếu muốn viết lại tiêu đề, mô tả, search terms và highlights';
  }
  if (rewritten && allowRepeat) {
    return 'Đã Rewrite — bấm lại để viết lại tiêu đề, mô tả, search terms và highlights (tốn token). Hoàn tác khôi phục bản trước.';
  }
  if (mode === LISTING_REWRITE_TITLE_MODES.stripBrands) {
    return 'AI xóa từ thương hiệu, giữ tiêu đề gốc; nếu còn dưới 40 ký tự thì tạo tiêu đề SEO mới (Women …, nhồi keyword đồng bộ mô tả). Cùng lúc điền Search terms và Product highlights.';
  }
  if (mode === LISTING_REWRITE_TITLE_MODES.rewrite) {
    return 'AI viết lại tiêu đề Machine-first SEO 150–250 ký tự từ title gốc + synonym cùng món (không bịa thuộc tính) + mô tả, Search terms và Product highlights';
  }
  return 'AI viết lại mô tả, search terms, highlights và xử lý tiêu đề theo cài đặt hệ thống';
}
