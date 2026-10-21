/** Setting keys for listing source-rating color thresholds (must match backend ListingRatingSettingKeys). */
export const LISTING_RATING_SETTING_KEYS = {
  successMin: 'listing.rating.success_min',
  warningMin: 'listing.rating.warning_min',
} as const;

export const LISTING_RATING_DEFAULTS = {
  /** ≥ this → green (success). */
  successMin: 4.4,
  /** ≥ this and &lt; successMin → yellow; below → red. */
  warningMin: 4.0,
} as const;

export type ListingRatingTier = 'success' | 'warning' | 'danger';

export function parseListingRatingThreshold(
  raw: string | undefined,
  fallback: number,
): number {
  if (raw == null || raw.trim() === '') return fallback;
  const n = Number(raw);
  return Number.isFinite(n) ? n : fallback;
}

export function getListingRatingTier(
  rating: number,
  successMin: number = LISTING_RATING_DEFAULTS.successMin,
  warningMin: number = LISTING_RATING_DEFAULTS.warningMin,
): ListingRatingTier {
  if (rating >= successMin) return 'success';
  if (rating >= warningMin) return 'warning';
  return 'danger';
}
