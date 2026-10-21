import type { ListingFormValues } from './listingFormUtils';
import type { ListingImageRow } from './productFormTypes';

const TYPED_MEDIA = ['Video', 'SizeChart'] as const;

/** Stable JSON for deep equality (sort object keys recursively). */
export function stableStringify(value: unknown): string {
  return JSON.stringify(value, (_, v) => {
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      return Object.fromEntries(
        Object.keys(v as object)
          .sort()
          .map((k) => [k, (v as Record<string, unknown>)[k]]),
      );
    }
    return v;
  });
}

/** Null / empty-string / empty-array — treated as "remote didn't provide this field". */
function isEmptyish(v: unknown): boolean {
  if (v == null) return true;
  if (typeof v === 'string') return v.trim() === '';
  if (Array.isArray(v)) return v.length === 0;
  return false;
}

export function deepEqual(a: unknown, b: unknown): boolean {
  if (Object.is(a, b)) return true;
  if (a == null || b == null) return a === b;
  if (typeof a !== typeof b) return false;
  // Primitives / dates already handled by Object.is above when equal.
  if (typeof a !== 'object') return false;
  return stableStringify(a) === stableStringify(b);
}

function hasTypedMediaUrl(images: ListingImageRow[] | undefined, mediaType: string): boolean {
  return Boolean(typedMediaUrl(images, mediaType));
}

function typedMediaUrl(
  images: ListingImageRow[] | undefined,
  mediaType: string,
): string {
  const hit = (images ?? []).find(
    (img) =>
      img?.mediaType === mediaType &&
      !img.colorValue?.trim() &&
      String(img.url ?? '').trim(),
  );
  return hit?.url?.trim() ?? '';
}

/**
 * Fill missing Video / SizeChart rows from `source` into `target` images.
 * Used when browser draft was saved before hydrate knew about erpVideoUrl, or when
 * three-way merge kept a draft images[] that omitted typed media the server now has.
 */
export function ensureTypedMediaFromSource(
  target: ListingFormValues,
  source: ListingFormValues | null | undefined,
): ListingFormValues {
  if (!source?.images?.length) return target;
  const list = [...(target.images ?? [])];
  let changed = false;

  for (const mediaType of TYPED_MEDIA) {
    if (hasTypedMediaUrl(list, mediaType)) continue;
    const hit = source.images.find(
      (img) =>
        img?.mediaType === mediaType &&
        !img.colorValue?.trim() &&
        String(img.url ?? '').trim(),
    );
    if (!hit) continue;
    const idx = list.findIndex((img) => img.mediaType === mediaType && !img.colorValue?.trim());
    const existingRow = idx >= 0 ? list[idx] : undefined;
    if (existingRow) {
      list[idx] = { ...existingRow, ...hit, mediaType, url: hit.url.trim() };
    } else {
      list.push({
        mediaType,
        url: hit.url.trim(),
        sortOrder: list.length,
      });
    }
    changed = true;
  }

  return changed ? { ...target, images: list } : target;
}

/**
 * After sync, Size Chart / Video must mirror 4Seller exactly — including removal when
 * remote no longer has them (local draft or stale DB must not keep typed media).
 */
export function reconcileTypedMediaWithRemote(
  target: ListingFormValues,
  remote: ListingFormValues,
): ListingFormValues {
  let images = [...(target.images ?? [])];
  let changed = false;

  for (const mediaType of TYPED_MEDIA) {
    const remoteUrl = typedMediaUrl(remote.images, mediaType);
    const idx = images.findIndex(
      (img) => img.mediaType === mediaType && !img.colorValue?.trim(),
    );

    if (remoteUrl) {
      const existingRow = idx >= 0 ? images[idx] : undefined;
      if (existingRow) {
        const existing = String(existingRow.url ?? '').trim();
        if (existing !== remoteUrl) {
          images[idx] = { ...existingRow, mediaType, url: remoteUrl };
          changed = true;
        }
      } else {
        images.push({
          mediaType,
          url: remoteUrl,
          sortOrder: images.length,
        });
        changed = true;
      }
    } else if (idx >= 0) {
      images = images.filter((_, i) => i !== idx);
      changed = true;
    }
  }

  let next: ListingFormValues = changed ? { ...target, images } : target;

  const hasRemoteSizeChart = Boolean(typedMediaUrl(remote.images, 'SizeChart'));
  const hasRemoteVideo = Boolean(typedMediaUrl(remote.images, 'Video'));
  const nextSizeChartId = hasRemoteSizeChart
    ? (remote.sizeChartId ?? '').trim()
    : '';
  const nextErpVideoId = hasRemoteVideo ? (remote.erpVideoId ?? '').trim() : '';

  if (
    (next.sizeChartId ?? '').trim() !== nextSizeChartId ||
    (next.erpVideoId ?? '').trim() !== nextErpVideoId
  ) {
    next = {
      ...next,
      sizeChartId: nextSizeChartId || undefined,
      erpVideoId: nextErpVideoId || undefined,
    };
    changed = true;
  }

  return changed ? next : target;
}

/**
 * Three-way merge for listing edit form:
 * - If remote field changed vs baseline and user did not edit that field → take remote.
 * - If user edited the field → keep current.
 * - Arrays (variations/images/attributes/certifications) treated as whole fields.
 * - Backfill missing Video/SizeChart from remote, then reconcile typed media to remote
 *   (remote removal wins over local draft / stale rows).
 */
export function mergeListingFormRemoteDiff(
  baseline: ListingFormValues,
  remote: ListingFormValues,
  current: ListingFormValues,
): ListingFormValues {
  const keys = new Set([
    ...Object.keys(baseline),
    ...Object.keys(remote),
    ...Object.keys(current),
  ]) as Set<keyof ListingFormValues>;

  const next: ListingFormValues = { ...current };

  for (const key of keys) {
    const baseVal = baseline[key];
    const remoteVal = remote[key];
    const currentVal = current[key];

    if (deepEqual(remoteVal, baseVal)) {
      continue;
    }

    // Remote changed. Keep user edit if they diverged from baseline.
    if (!deepEqual(currentVal, baseVal)) {
      continue;
    }

    // Background sync = enrich, not erase: a partial 4Seller detail (e.g. per-id
    // /detail omitting description) must not wipe a field the local snapshot has.
    if (isEmptyish(remoteVal) && !isEmptyish(baseVal)) {
      continue;
    }

    (next as unknown as Record<string, unknown>)[key as string] = remoteVal;
  }

  return reconcileTypedMediaWithRemote(ensureTypedMediaFromSource(next, remote), remote);
}
