import { Tooltip } from 'antd';
import type { ListingAnalyticsRow } from '@drop/types/listing';
import {
  getListingRatingTier,
  LISTING_RATING_DEFAULTS,
  ListingStars,
  type ListingRatingTier,
} from '@drop/ui';
import { firstImage } from './listingImage';
import { ProductThumb } from './ProductThumb';
import {
  hasListingSupplierMeta,
  ListingSupplierLine,
  type ListingSupplierMetaSource,
} from './listingSupplierLine';

const THUMB_SIZE = 52;

const copyText = async (text: string) => {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // Bảng analytics không toast — tooltip đã nói "click để copy".
  }
};

const sellerLabel = (fullName?: string | null, userName?: string | null): string =>
  fullName?.trim() || userName?.trim() || '';

/** Cuối dòng supplier (bên phải): seller (admin) + profile shop. */
const supplierTailLabel = (row: ListingAnalyticsRow, showOwnerName: boolean): string => {
  const parts = [
    showOwnerName ? sellerLabel(row.ownerFullName, row.ownerUserName) : null,
    row.shopProfileName?.trim() || null,
  ].filter(Boolean) as string[];
  return parts.join(' · ');
};

const supplierSource = (row: ListingAnalyticsRow): ListingSupplierMetaSource => ({
  storeTitle: row.storeTitle,
  storeUrl: row.storeUrl,
  shopRank: row.shopRank,
  storeAttention: row.storeAttention,
  shippingDays: row.shippingDays,
  shippingDayPercent: row.shippingDayPercent,
  isLocalProduct: row.isLocalProduct,
  isSupportQuickShip: row.isSupportQuickShip,
});

export type ListingAnalyticsProductCellProps = {
  row: ListingAnalyticsRow;
  /** STT toàn trang (1-based), khớp Listings `(pageCurrent - 1) * pageSize + i + 1`. */
  index: number;
  showOwnerName?: boolean;
  ratingSuccessMin?: number;
  ratingWarningMin?: number;
};

/**
 * Ô Product trong bảng analytics — cùng layout `listing-card-product` / `listing-card-lines`
 * như `ListingsPage` (thumb popover + 2 dòng meta).
 */
export function ListingAnalyticsProductCell({
  row,
  index,
  showOwnerName = false,
  ratingSuccessMin = LISTING_RATING_DEFAULTS.successMin,
  ratingWarningMin = LISTING_RATING_DEFAULTS.warningMin,
}: ListingAnalyticsProductCellProps) {
  const title = row.productName?.trim() || row.tikTokProductId;
  const img = firstImage(row.mainImage);
  const tooltip = `${title} · TT: ${row.tikTokProductId}`;
  const tailLabel = supplierTailLabel(row, showOwnerName);

  const ratingNum =
    row.averageRating != null && Number.isFinite(Number(row.averageRating))
      ? Number(row.averageRating)
      : null;
  const ratingTier: ListingRatingTier | null =
    ratingNum != null ? getListingRatingTier(ratingNum, ratingSuccessMin, ratingWarningMin) : null;

  const reviewLine =
    ratingNum != null || row.reviewCount != null ? (
      <div
        className={ratingTier ? `listing-rating listing-rating--${ratingTier}` : 'listing-rating'}
        title={
          ratingNum != null
            ? `${ratingNum.toFixed(2)}` +
              (row.reviewCount != null ? ` (${row.reviewCount.toLocaleString()})` : '')
            : row.reviewCount != null
              ? `(${row.reviewCount.toLocaleString()})`
              : undefined
        }
      >
        {ratingNum != null && <ListingStars rating={ratingNum} tier={ratingTier} />}
        {ratingNum != null && <span className="listing-rating-value">{ratingNum.toFixed(2)}</span>}
        {row.reviewCount != null && (
          <span className="listing-review-count">({row.reviewCount.toLocaleString()})</span>
        )}
      </div>
    ) : null;

  const supplier = supplierSource(row);
  const showSupplier = hasListingSupplierMeta(supplier);

  const titleNode = row.listingId ? (
    <a
      href={`/listings/${row.listingId}/edit`}
      target="_blank"
      rel="noopener noreferrer"
      title={`${tooltip} · Mở listing`}
      className="listing-card-line listing-card-line--title"
    >
      {title}
    </a>
  ) : (
    <div className="listing-card-line listing-card-line--title" title={tooltip}>
      {title}
    </div>
  );

  const originBadge =
    row.addedFrom === 'recommended' ? (
      <span
        className="listing-ship-badge listing-ship-badge--recommended"
        title="Thêm từ lọc sản phẩm extension"
      >
        Đề xuất
      </span>
    ) : null;

  const titleLine = originBadge ? (
    <div className="listing-card-title-row">
      {titleNode}
      {originBadge}
    </div>
  ) : (
    titleNode
  );

  const identityLine = (
    <Tooltip title="Click để copy TikTok product id">
      <span
        role="button"
        tabIndex={0}
        className="listing-analytics-tt-id"
        onClick={() => void copyText(row.tikTokProductId)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            void copyText(row.tikTokProductId);
          }
        }}
      >
        {row.tikTokProductId}
      </span>
    </Tooltip>
  );

  const tailLine = tailLabel ? (
    <span className="listing-analytics-scope-inline">{tailLabel}</span>
  ) : null;

  return (
    <div className="listing-card-product">
      <div className="listing-thumb-wrap">
        <span className="listing-thumb-index" aria-hidden="true">
          {index}
        </span>
        <ProductThumb src={img} size={THUMB_SIZE} />
      </div>
      <div className="listing-card-lines">
        {titleLine}
        {reviewLine ? (
          <div className="listing-card-line listing-card-line--row">{reviewLine}</div>
        ) : null}
        {showSupplier ? (
          <div className="listing-card-line listing-card-line--row">
            <ListingSupplierLine source={supplier} trailing={tailLine} />
          </div>
        ) : (
          <div className="listing-card-line listing-card-line--row listing-analytics-identity-row">
            {identityLine}
            {tailLine ? <span className="listing-analytics-scope-sep">·</span> : null}
            {tailLine}
          </div>
        )}
      </div>
    </div>
  );
}
