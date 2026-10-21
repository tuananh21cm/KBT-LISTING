import type { ListingRatingTier } from './listingRatingSettings';

const STAR_PATH =
  'M12 2.5l2.9 6.1 6.7.6-5.1 4.5 1.5 6.5L12 16.8 5.9 20.2l1.5-6.5-5.1-4.5 6.7-.6L12 2.5z';

function StarSvg({ className }: { className?: string }) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      width={14}
      height={14}
      aria-hidden
      focusable="false"
    >
      <path d={STAR_PATH} />
    </svg>
  );
}

/** Five SVG stars with partial fill — geometrically centered vs adjacent rating text. */
export function ListingStars({
  rating,
  tier,
}: {
  rating: number;
  tier?: ListingRatingTier | null;
}) {
  return (
    <span
      className={tier ? `listing-stars listing-stars--${tier}` : 'listing-stars'}
      aria-hidden
    >
      {Array.from({ length: 5 }, (_, i) => {
        const fill = Math.max(0, Math.min(1, rating - i));
        return (
          <span key={i} className="listing-star">
            <StarSvg className="listing-star__empty" />
            {fill > 0 ? (
              <span className="listing-star__fill" style={{ width: `${fill * 100}%` }}>
                <StarSvg />
              </span>
            ) : null}
          </span>
        );
      })}
    </span>
  );
}
