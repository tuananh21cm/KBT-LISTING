import type { PricingFormulaInput } from '@drop/types/pricingTemplate';
import type { ListingAppliedPricing } from '@drop/types/listing';
import { templateToFormulaInput } from './pricingBatchUtils';

const STORAGE_PREFIX = 'listing-applied-pricing:';

export type ListingAppliedPricingSnapshot = {
  listingId: string;
  appliedAt: string;
  formula: PricingFormulaInput;
  templateId?: string;
};

function storageKey(listingId: string): string {
  return `${STORAGE_PREFIX}${listingId}`;
}

function isValidFormula(value: unknown): value is PricingFormulaInput {
  if (!value || typeof value !== 'object') return false;
  const f = value as Record<string, unknown>;
  return (
    typeof f.supplierShipping === 'number' &&
    typeof f.tax === 'number' &&
    typeof f.discountPercent === 'number' &&
    typeof f.fastSalePercent === 'number' &&
    typeof f.adsFee === 'number' &&
    typeof f.tikTokShipping === 'number' &&
    typeof f.platformFeePercent === 'number' &&
    (f.targetMode === 'margin' || f.targetMode === 'profit')
  );
}

/** Convert API / DB appliedPricing row into calculator formula input. */
export function appliedPricingToFormula(
  pricing: ListingAppliedPricing | null | undefined,
): PricingFormulaInput | null {
  if (!pricing) return null;
  if (!isValidFormula(pricing)) return null;
  return templateToFormulaInput(pricing);
}

/** Build API payload from formula state (flat shape matches backend DTO). */
export function buildListingAppliedPricingPayload(
  formula: PricingFormulaInput,
  templateId?: string,
  appliedAt?: string,
): ListingAppliedPricing {
  const normalized = templateToFormulaInput(formula);
  return {
    ...normalized,
    templateId: templateId || undefined,
    appliedAt: appliedAt || new Date().toISOString(),
  };
}

export function snapshotFromAppliedPricing(
  listingId: string,
  pricing: ListingAppliedPricing,
): ListingAppliedPricingSnapshot | null {
  const formula = appliedPricingToFormula(pricing);
  if (!formula) return null;
  return {
    listingId,
    appliedAt: pricing.appliedAt || new Date().toISOString(),
    formula,
    templateId: pricing.templateId || undefined,
  };
}

export function loadListingAppliedPricing(
  listingId: string,
): ListingAppliedPricingSnapshot | null {
  if (!listingId || typeof localStorage === 'undefined') return null;
  try {
    const raw = localStorage.getItem(storageKey(listingId));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as ListingAppliedPricingSnapshot;
    if (!parsed?.listingId || parsed.listingId !== listingId || !isValidFormula(parsed.formula)) {
      return null;
    }
    return {
      listingId: parsed.listingId,
      appliedAt: parsed.appliedAt || new Date().toISOString(),
      formula: templateToFormulaInput(parsed.formula),
      templateId: parsed.templateId,
    };
  } catch {
    return null;
  }
}

export function saveListingAppliedPricing(
  listingId: string,
  formula: PricingFormulaInput,
  templateId?: string,
): void {
  if (!listingId || typeof localStorage === 'undefined') return;
  const snapshot: ListingAppliedPricingSnapshot = {
    listingId,
    appliedAt: new Date().toISOString(),
    formula: templateToFormulaInput(formula),
    templateId,
  };
  try {
    localStorage.setItem(storageKey(listingId), JSON.stringify(snapshot));
  } catch {
    // Quota / private mode — ignore.
  }
}
