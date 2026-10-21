export type FunnelSnapshot = {
  impressions: number;
  clicks: number;
  addToCartUnits: number;
  orders: number;
};

export type FunnelRates = {
  ctr: number | null;
  atcRate: number | null;
  orderFromAtc: number | null;
  cvr: number | null;
};

export type CustomFunnelRates = {
  ctr: number;
  atcRate: number;
  orderFromAtc: number;
};

export type FunnelProjection = {
  clicks: number;
  addToCartUnits: number;
  orders: number;
  cvr: number;
};

export function resolveFunnelRates(snapshot: FunnelSnapshot): FunnelRates {
  const { impressions, clicks, addToCartUnits, orders } = snapshot;
  const ctr = impressions > 0 ? clicks / impressions : null;
  const atcRate = clicks > 0 ? addToCartUnits / clicks : null;
  const orderFromAtc = addToCartUnits > 0 ? orders / addToCartUnits : null;
  const cvr = impressions > 0 ? orders / impressions : null;
  return { ctr, atcRate, orderFromAtc, cvr };
}

export function defaultCustomRates(rates: FunnelRates): CustomFunnelRates {
  return {
    ctr: rates.ctr ?? 0,
    atcRate: rates.atcRate ?? 0,
    orderFromAtc: rates.orderFromAtc ?? 0,
  };
}

/** Tỷ lệ dạng fraction (0.042 = 4.2%). */
export function projectFunnelFromRates(
  impressions: number,
  rates: CustomFunnelRates,
): FunnelProjection | null {
  if (impressions <= 0) return null;

  const ctr = Math.max(0, rates.ctr);
  const atcRate = Math.max(0, rates.atcRate);
  const orderFromAtc = Math.max(0, rates.orderFromAtc);

  const clicks = impressions * ctr;
  const addToCartUnits = clicks * atcRate;
  const orders = addToCartUnits * orderFromAtc;
  const cvr = orders / impressions;

  return { clicks, addToCartUnits, orders, cvr };
}

export function snapshotProjection(snapshot: FunnelSnapshot): FunnelProjection | null {
  const rates = resolveFunnelRates(snapshot);
  if (rates.ctr == null) return null;
  return projectFunnelFromRates(snapshot.impressions, defaultCustomRates(rates));
}
