import type { ShippingTemplate } from '../../types/shippingTemplate';

const STORAGE_PREFIX = 'shipping-templates-cache:';

export type ShippingTemplatesCacheSnapshot = {
  userId: string;
  savedAt: string;
  templates: ShippingTemplate[];
};

function storageKey(userId: string): string {
  return `${STORAGE_PREFIX}${userId}`;
}

function isValidTemplate(value: unknown): value is ShippingTemplate {
  if (!value || typeof value !== 'object') return false;
  const t = value as Record<string, unknown>;
  return (
    typeof t.id === 'string' &&
    typeof t.name === 'string' &&
    typeof t.packageWeight === 'number' &&
    typeof t.packageLength === 'number' &&
    typeof t.packageWidth === 'number' &&
    typeof t.packageHeight === 'number' &&
    typeof t.weightUnit === 'string' &&
    typeof t.dimensionUnit === 'string' &&
    typeof t.isDefault === 'boolean'
  );
}

/** Load user-scoped shipping templates from localStorage (key includes userId). */
export function loadShippingTemplatesCache(
  userId: string,
): ShippingTemplatesCacheSnapshot | null {
  if (!userId || typeof localStorage === 'undefined') return null;
  try {
    const raw = localStorage.getItem(storageKey(userId));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as ShippingTemplatesCacheSnapshot;
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

/** Persist user-scoped shipping templates for instant select / manage modal. */
export function saveShippingTemplatesCache(userId: string, templates: ShippingTemplate[]): void {
  if (!userId || typeof localStorage === 'undefined') return;
  const snapshot: ShippingTemplatesCacheSnapshot = {
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

/** Clear cached shipping templates for a user (e.g. after logout). */
export function clearShippingTemplatesCache(userId: string): void {
  if (!userId || typeof localStorage === 'undefined') return;
  try {
    localStorage.removeItem(storageKey(userId));
  } catch {
    /* ignore */
  }
}
