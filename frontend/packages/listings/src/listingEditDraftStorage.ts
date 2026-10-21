import type { ListingFormValues } from './listingFormUtils';

const STORAGE_PREFIX = 'listing-edit-draft:';

export type ListingEditDraftSnapshot = {
  listingId: string;
  savedAt: string;
  formValues: ListingFormValues;
  hazardous?: string;
  prop65Repro?: string;
  prop65Carcinogen?: string;
  variantImageOn?: boolean;
};

function storageKey(listingId: string): string {
  return `${STORAGE_PREFIX}${listingId}`;
}

export function loadListingEditDraft(listingId: string): ListingEditDraftSnapshot | null {
  if (!listingId || typeof localStorage === 'undefined') return null;
  try {
    const raw = localStorage.getItem(storageKey(listingId));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as ListingEditDraftSnapshot;
    if (!parsed?.listingId || parsed.listingId !== listingId || !parsed.formValues) return null;
    return parsed;
  } catch {
    return null;
  }
}

export function saveListingEditDraft(
  listingId: string,
  formValues: ListingFormValues,
  extras?: Pick<
    ListingEditDraftSnapshot,
    'hazardous' | 'prop65Repro' | 'prop65Carcinogen' | 'variantImageOn'
  >,
): void {
  if (!listingId || typeof localStorage === 'undefined') return;
  const snapshot: ListingEditDraftSnapshot = {
    listingId,
    savedAt: new Date().toISOString(),
    formValues,
    ...extras,
  };
  try {
    localStorage.setItem(storageKey(listingId), JSON.stringify(snapshot));
  } catch {
    // Quota / private mode — ignore; DB mirror remains source of truth.
  }
}

export function clearListingEditDraft(listingId: string): void {
  if (!listingId || typeof localStorage === 'undefined') return;
  try {
    localStorage.removeItem(storageKey(listingId));
  } catch {
    /* ignore */
  }
}
