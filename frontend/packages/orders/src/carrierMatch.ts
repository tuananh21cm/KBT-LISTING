/**
 * Mirrors backend OrderAutoMarkShipPolicy carrier matching:
 * normalize punctuation/casing/whitespace, exact alphanumeric identity only.
 */
export const normalizeCarrier = (value?: string | null): string => {
  if (!value?.trim()) return '';
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]/g, '');
};

/** Safe exact match after normalization — no fuzzy matching. */
export const isExactCarrierMatch = (
  supplierCarrier?: string | null,
  fourSellerCarrier?: string | null,
): boolean => {
  const left = normalizeCarrier(supplierCarrier);
  return left.length > 0 && left === normalizeCarrier(fourSellerCarrier);
};

export const findExactCarrierMatch = <T extends { carrier?: string | null }>(
  supplierCarrier: string | null | undefined,
  services: T[],
): T | undefined => {
  const matches = services.filter((s) => isExactCarrierMatch(supplierCarrier, s.carrier));
  if (matches.length === 0) return undefined;
  const exact = matches.find(
    (s) => (s.carrier ?? '').trim().toLowerCase() === (supplierCarrier ?? '').trim().toLowerCase(),
  );
  return exact ?? matches[0];
};
