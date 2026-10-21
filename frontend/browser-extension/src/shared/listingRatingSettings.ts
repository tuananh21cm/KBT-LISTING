/** Ngưỡng màu sao nguồn — khớp admin-web `listingRatingSettings` / Settings `listing.rating.*`. */
export const LISTING_RATING_DEFAULTS = {
  /** ≥ this → xanh (success). */
  successMin: 4.4,
  /** ≥ this và &lt; successMin → vàng; dưới → đỏ. */
  warningMin: 4.0,
} as const;

export type ListingRatingTier = 'success' | 'warning' | 'danger';

export function getListingRatingTier(
  rating: number,
  successMin: number = LISTING_RATING_DEFAULTS.successMin,
  warningMin: number = LISTING_RATING_DEFAULTS.warningMin,
): ListingRatingTier {
  if (rating >= successMin) return 'success';
  if (rating >= warningMin) return 'warning';
  return 'danger';
}
