import { getListingRatingTier, type ListingRatingTier } from '../shared/listingRatingSettings';

export function formatDraftPrice(low?: number | null, high?: number | null): string | null {
  const lows = low != null && Number.isFinite(Number(low)) ? Number(low) : null;
  const highs = high != null && Number.isFinite(Number(high)) ? Number(high) : null;
  const a = lows != null && lows > 0 ? lows : highs != null && highs > 0 ? highs : null;
  const b = highs != null && highs > 0 ? highs : lows != null && lows > 0 ? lows : null;
  if (a == null || b == null) return null;
  const fmt = (n: number) =>
    n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  if (a === b) return `$${fmt(a)}`;
  return `$${fmt(Math.min(a, b))} – $${fmt(Math.max(a, b))}`;
}

const DRAFT_STAR_PATH =
  'M12 2.5l2.9 6.1 6.7.6-5.1 4.5 1.5 6.5L12 16.8 5.9 20.2l1.5-6.5-5.1-4.5 6.7-.6L12 2.5z';

function DraftStarSvg({ className }: { className?: string }) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      width={14}
      height={14}
      aria-hidden
      focusable="false"
    >
      <path d={DRAFT_STAR_PATH} />
    </svg>
  );
}

function DraftStars({
  rating,
  tier,
}: {
  rating: number;
  tier: ListingRatingTier;
}) {
  const clamped = Math.max(0, Math.min(5, rating));
  return (
    <span className={`draft-stars draft-stars--${tier}`} aria-hidden="true">
      {Array.from({ length: 5 }, (_, i) => {
        const fill = Math.max(0, Math.min(1, clamped - i));
        return (
          <span key={i} className="draft-star">
            <DraftStarSvg className="draft-star__empty" />
            {fill > 0 ? (
              <span className="draft-star__fill" style={{ width: `${fill * 100}%` }}>
                <DraftStarSvg />
              </span>
            ) : null}
          </span>
        );
      })}
    </span>
  );
}

export function DraftRatingBlock({
  rating,
  reviewCount,
}: {
  rating?: number | null;
  reviewCount?: number | null;
}) {
  const ratingNum =
    rating != null && Number.isFinite(Number(rating)) ? Number(rating) : null;
  const ratingTier = ratingNum != null ? getListingRatingTier(ratingNum) : null;
  if (ratingNum == null && reviewCount == null) return null;
  return (
    <span
      className={ratingTier ? `draft-rating draft-rating--${ratingTier}` : 'draft-rating'}
      title={
        ratingNum != null
          ? `${ratingNum.toFixed(2)} sao` +
            (reviewCount != null ? ` · ${reviewCount.toLocaleString('vi-VN')} đánh giá` : '')
          : reviewCount != null
            ? `${reviewCount.toLocaleString('vi-VN')} đánh giá`
            : undefined
      }
    >
      {ratingNum != null && ratingTier != null && (
        <DraftStars rating={ratingNum} tier={ratingTier} />
      )}
      {ratingNum != null && (
        <span className="draft-rating-value">{ratingNum.toFixed(1)}</span>
      )}
      {reviewCount != null && (
        <span className="draft-review-count">({reviewCount.toLocaleString('vi-VN')})</span>
      )}
    </span>
  );
}
