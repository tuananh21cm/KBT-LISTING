import type { PricingTemplate } from '../../types/pricingTemplate';

const STORAGE_PREFIX = 'pricing-templates-cache:';

export type PricingTemplatesCacheSnapshot = {
  userId: string;
  savedAt: string;
  templates: PricingTemplate[];
};

function storageKey(userId: string): string {
  return `${STORAGE_PREFIX}${userId}`;
}

function isValidTemplate(value: unknown): value is PricingTemplate {
  if (!value || typeof value !== 'object') return false;
  const t = value as Record<string, unknown>;
  return (
    typeof t.id === 'string' &&
    typeof t.name === 'string' &&
    typeof t.supplierShipping === 'number' &&
    typeof t.tax === 'number' &&
    typeof t.discountPercent === 'number' &&
    typeof t.fastSalePercent === 'number' &&
    typeof t.adsFee === 'number' &&
    typeof t.tikTokShipping === 'number' &&
    typeof t.platformFeePercent === 'number' &&
    (t.targetMode === 'margin' || t.targetMode === 'profit') &&
    typeof t.marginPercent === 'number' &&
    typeof t.profitAmount === 'number' &&
    typeof t.isDefault === 'boolean'
  );
}

/** Load seller-scoped pricing templates from localStorage (key includes userId). */
export function loadPricingTemplatesCache(
  userId: string,
): PricingTemplatesCacheSnapshot | null {
  if (!userId || typeof localStorage === 'undefined') return null;
  try {
    const raw = localStorage.getItem(storageKey(userId));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as PricingTemplatesCacheSnapshot;
    if (
      !parsed?.userId ||
      parsed.userId !== userId ||
      !Array.isArray(parsed.templates) ||
      !parsed.templates.every(isValidTemplate)
    ) {
      return null;
    }
    return {
      userId: parsed.userId,
      savedAt: parsed.savedAt || new Date().toISOString(),
      templates: parsed.templates,
    };
  } catch {
    return null;
  }
}

/** Persist seller-scoped pricing templates for instant Batch modal open. */
export function savePricingTemplatesCache(userId: string, templates: PricingTemplate[]): void {
  if (!userId || typeof localStorage === 'undefined') return;
  const snapshot: PricingTemplatesCacheSnapshot = {
    userId,
    savedAt: new Date().toISOString(),
    templates,
  };
  try {
    localStorage.setItem(storageKey(userId), JSON.stringify(snapshot));
  } catch {
    // Quota / private mode — ignore.
  }
}

/** Clear cached templates for a seller (e.g. after logout). */
export function clearPricingTemplatesCache(userId: string): void {
  if (!userId || typeof localStorage === 'undefined') return;
  try {
    localStorage.removeItem(storageKey(userId));
  } catch {
    /* ignore */
  }
}
