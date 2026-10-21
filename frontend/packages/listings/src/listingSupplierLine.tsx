import type { ReactNode } from 'react';

/** Nguồn meta shop NCC — `ListingListItem` và `ListingAnalyticsRow` đều map vào shape này. */
export type ListingSupplierMetaSource = {
  storeTitle?: string | null;
  storeUrl?: string | null;
  shopRank?: number | null;
  storeAttention?: number | null;
  shippingDays?: number | null;
  shippingDayPercent?: number | null;
  isLocalProduct?: boolean | null;
  isSupportQuickShip?: boolean | null;
};

/** SLA shop NCC: `80.34% ≤ 4`. */
export function formatShippingSla(
  days: number | null | undefined,
  percent: number | null | undefined,
): string {
  const hasDays = days != null && Number.isFinite(days);
  const hasPercent = percent != null && Number.isFinite(percent);
  if (hasPercent && hasDays) return `${percent}% ≤ ${days}`;
  if (hasPercent) return `${percent}%`;
  if (hasDays) return `≤ ${days}`;
  return '';
}

export function formatShopRank(rank?: number | null): string {
  if (rank == null || !Number.isFinite(Number(rank))) return '';
  const n = Number(rank);
  return Number.isInteger(n) ? String(n) : n.toFixed(2).replace(/\.?0+$/, '');
}

export function formatStoreAttention(count?: number | null): string {
  if (count == null || !Number.isFinite(Number(count)) || Number(count) < 0) return '';
  const n = Math.floor(Number(count));
  if (n >= 1_000_000) {
    const v = n / 1_000_000;
    return `${v >= 10 ? Math.round(v) : v.toFixed(1).replace(/\.0$/, '')}M`;
  }
  if (n >= 1000) {
    const v = n / 1000;
    return `${v >= 10 ? Math.round(v) : v.toFixed(1).replace(/\.0$/, '')}k`;
  }
  return n.toLocaleString('vi-VN');
}

function ListingStoreIcon() {
  return (
    <svg className="listing-stat-icon" viewBox="0 0 16 16" width="12" height="12" aria-hidden>
      <path
        fill="currentColor"
        d="M2 3.5A1.5 1.5 0 0 1 3.5 2h9A1.5 1.5 0 0 1 14 3.5V6h.25a.75.75 0 0 1 0 1.5H14V13a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 13V7.5h-.25a.75.75 0 0 1 0-1.5H2V3.5ZM3.5 3a.5.5 0 0 0-.5.5V6h10V3.5a.5.5 0 0 0-.5-.5h-9ZM3 8v5h3.5V9.75h3V13H13V8H3Z"
      />
    </svg>
  );
}

function ListingRankIcon() {
  return (
    <svg className="listing-stat-icon" viewBox="0 0 16 16" width="12" height="12" aria-hidden>
      <path
        fill="currentColor"
        d="M8 1.25A4.25 4.25 0 0 0 5.4 8.9L4.15 14.2a.4.4 0 0 0 .58.45L8 12.85l3.27 1.8a.4.4 0 0 0 .58-.45L10.6 8.9A4.25 4.25 0 0 0 8 1.25Zm0 1.5a2.75 2.75 0 1 1 0 5.5 2.75 2.75 0 0 1 0-5.5Z"
      />
    </svg>
  );
}

function ListingFollowIcon() {
  return (
    <svg className="listing-stat-icon" viewBox="0 0 16 16" width="12" height="12" aria-hidden>
      <path
        fill="currentColor"
        d="M6.25 7.75a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Zm5.1.35a2.15 2.15 0 1 0-2.05-3.45 3.35 3.35 0 0 1 .55 3.45ZM1.5 13.15C1.5 11.1 3.45 9.6 6.25 9.6s4.75 1.5 4.75 3.55V14h-9.5v-.85Zm9.35-.05c0-1.05-.4-1.95-1.1-2.65 1.55.2 3.25 1 3.25 2.7V14h-2.15v-.9Z"
      />
    </svg>
  );
}

export function ListingShipFlags({ source }: { source: ListingSupplierMetaSource }) {
  if (source.isLocalProduct !== true && source.isSupportQuickShip !== true) return null;
  return (
    <span className="listing-supplier-flags">
      {source.isLocalProduct === true && (
        <span className="listing-ship-badge listing-ship-badge--local" title="Hàng local US">
          Local
        </span>
      )}
      {source.isSupportQuickShip === true && (
        <span
          className="listing-ship-badge listing-ship-badge--quick"
          title="Ít nhất một SKU mall_stock hỗ trợ ship nhanh"
        >
          Nhanh
        </span>
      )}
    </span>
  );
}

export function ListingSupplierLine({
  source,
  trailing,
}: {
  source: ListingSupplierMetaSource;
  trailing?: ReactNode;
}) {
  const shopRank = formatShopRank(source.shopRank);
  const attention = formatStoreAttention(source.storeAttention);
  const shippingSla = formatShippingSla(source.shippingDays, source.shippingDayPercent);
  const hasStore = !!source.storeTitle || !!source.storeUrl;
  const hasShopMeta = !!shopRank || !!attention;
  const hasFlags = source.isLocalProduct === true || source.isSupportQuickShip === true;
  if (!hasStore && !hasShopMeta && !shippingSla && !hasFlags && !trailing) return null;

  const storeInner = (
    <>
      <ListingStoreIcon />
      {source.storeTitle ? (
        <span className="listing-store-title">{source.storeTitle}</span>
      ) : null}
    </>
  );

  return (
    <div className="listing-supplier-shop">
      {hasStore || hasShopMeta ? (
        <span className="listing-store-group">
          {source.storeUrl ? (
            <a
              className="listing-store"
              href={source.storeUrl}
              target="_blank"
              rel="noreferrer"
              title={source.storeTitle || source.storeUrl}
              onClick={(e) => e.stopPropagation()}
            >
              {storeInner}
            </a>
          ) : hasStore ? (
            <span className="listing-store" title={source.storeTitle || undefined}>
              {storeInner}
            </span>
          ) : null}
          {hasShopMeta ? (
            <span className="listing-supplier-stats">
              {shopRank ? (
                <span className="listing-stat listing-stat--rank" title={`Rate shop NCC: ${shopRank}`}>
                  <ListingRankIcon />
                  <span>{shopRank}</span>
                </span>
              ) : null}
              {attention ? (
                <span className="listing-stat listing-stat--follow" title={`Follow shop NCC: ${attention}`}>
                  <ListingFollowIcon />
                  <span>{attention}</span>
                </span>
              ) : null}
            </span>
          ) : null}
        </span>
      ) : null}
      {shippingSla ? (
        <span className="listing-ship-sla" title="SLA giao hàng shop NCC">
          {shippingSla}
        </span>
      ) : null}
      <ListingShipFlags source={source} />
      {trailing ? <span className="listing-card-mobile-extra">{trailing}</span> : null}
    </div>
  );
}

export function hasListingSupplierMeta(source: ListingSupplierMetaSource): boolean {
  const shopRank = formatShopRank(source.shopRank);
  const attention = formatStoreAttention(source.storeAttention);
  const shippingSla = formatShippingSla(source.shippingDays, source.shippingDayPercent);
  const hasStore = !!source.storeTitle || !!source.storeUrl;
  const hasShopMeta = !!shopRank || !!attention;
  const hasFlags = source.isLocalProduct === true || source.isSupportQuickShip === true;
  return hasStore || hasShopMeta || !!shippingSla || hasFlags;
}
