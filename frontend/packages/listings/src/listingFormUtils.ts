import type {
  CreateAdminListingRequest,
  UpdateAdminListingRequest,
} from './localTypes';
import { normalizeDimensionUnit, normalizeWeightUnit } from './shippingUnits';
import type {
  ListingVariationRow,
  ProductEditFormValues,
} from './productFormTypes';

export interface ListingFormValues extends ProductEditFormValues {
  listingStatus: string;
  publishStatus?: string;
  syncStatus?: string;
  fourSellerListingId?: string;
  tikTokProductId?: string;
}

export function normalizeVariations(
  values: Pick<ListingFormValues, 'productType' | 'mainSpecification' | 'variations'>,
): ListingVariationRow[] {
  const isMulti = values.productType === 'MultiSku';
  if (isMulti) return values.variations ?? [];
  const first = values.variations?.[0];
  if (first) {
    return [{ ...first, isDefault: true, sortOrder: 0 }];
  }
  return [
    {
      sortOrder: 0,
      mainSpec: values.mainSpecification || 'Color',
      specValue: 'Default',
      sellerSku: '',
      salePrice: 0,
      quantity: 0,
      isDefault: true,
    },
  ];
}

export function formToListingPayload(
  values: ListingFormValues,
): Omit<UpdateAdminListingRequest, never> & Pick<CreateAdminListingRequest, never> {
  const variations = normalizeVariations(values);

  return {
    listingStatus: values.listingStatus,
    publishStatus: values.publishStatus,
    syncStatus: values.syncStatus,
    fourSellerListingId: values.fourSellerListingId,
    tikTokProductId: values.tikTokProductId,
    productName: values.productName,
    categoryId: values.categoryId,
    categoryName: values.categoryName,
    brandId: values.brandId,
    brandName: values.brandName,
    productType: values.productType,
    descriptionHtml: values.descriptionHtml,
    warehouseId: values.warehouseId,
    packageWeight: values.packageWeight,
    packageLength: values.packageLength,
    packageWidth: values.packageWidth,
    packageHeight: values.packageHeight,
    weightUnit: normalizeWeightUnit(values.weightUnit),
    dimensionUnit: normalizeDimensionUnit(values.dimensionUnit),
    sizeChartId: values.sizeChartId,
    safetyWarning: values.safetyWarning,
    listingSourceUrl: values.listingSourceUrl,
    fourSellerGroupId: values.fourSellerGroupId,
    fourSellerGroupName: values.fourSellerGroupName,
    appliedRules: values.appliedRules,
    refListingId: values.refListingId,
    refShopId: values.refShopId,
    siteId: values.siteId,
    skus: variations.map((v, i) => ({
      sortOrder: v.sortOrder ?? i,
      sellerSku: v.sellerSku,
      salePrice: v.salePrice,
      originalPrice: v.originalPrice,
      quantity: v.quantity,
      barcode: v.barcode,
      variantAttributes: variationAttrsToJson(v, values.mainSpecification || 'Color'),
      isDefault: v.isDefault,
    })),
    images: (values.images ?? []).map((img, i) => ({
      mediaType: img.mediaType,
      url: img.url,
      sortOrder: img.sortOrder ?? i,
      listingSkuId: img.listingSkuId,
      tikTokImageId: img.tikTokImageId,
    })),
    attributes: (values.attributes ?? []).map((a) => ({
      attrId: a.attrId || a.attrName || crypto.randomUUID(),
      attrName: a.attrName,
      attrValue: a.attrValue,
      valueId: a.valueId,
      values: a.values,
      valueType: a.valueType,
      isRequired: a.isRequired,
    })),
    certifications: (values.certifications ?? []).map((c) => ({
      certType: c.certType,
      externalId: c.externalId,
      name: c.name,
      phone: c.phone,
      email: c.email,
      address: c.address,
      fileUrls: c.fileUrls,
    })),
  };
}

export function createEmptyVariationRow(
  index: number,
  mainSpec = 'Color',
): ListingVariationRow {
  return {
    sortOrder: index,
    mainSpec,
    specValue: '',
    attrs: {},
    sellerSku: '',
    costPrice: 0,
    salePrice: 0,
    originalPrice: 0,
    quantity: 0,
    barcode: '',
    isDefault: index === 0,
  };
}

export const COLOR_PRESETS = [
  'White',
  'Pink',
  'Red',
  'Wine',
  'Blue',
  'Gold',
  'Green',
];

export const SIZE_PRESETS = [
  'XXS',
  'XS',
  'S',
  'M',
  'L',
  'XL',
  'XXL',
  '3XL',
  '4XL',
  '5XL',
];

export const NUMBER_OF_ITEMS_PRESETS = ['1', '2', '3', '4', '5', '6', '8', '10', '12'];

export type VariationSpecKey = 'Color' | 'Size' | 'Number of Items';

/** Axis order used everywhere (4Seller golden shape: Color→option1, Size→option2, Items→option3). */
export const VARIATION_SPEC_KEYS: VariationSpecKey[] = ['Color', 'Size', 'Number of Items'];

/** Size-like tokens including one-size / free-size / XS-M ranges (TikTok / Shein). */
export function looksLikeSizeToken(token: string | undefined | null): boolean {
  const t = String(token ?? '').trim();
  if (!t) return false;
  if (SIZE_PRESETS.some((s) => s.toLowerCase() === t.toLowerCase())) return true;
  if (/^(XXS|XS|S|M|L|XL|XXL|\d+XL|OS)$/i.test(t)) return true;
  const compact = t.replace(/[\s-]/g, '');
  if (/^(onesize|freesize)$/i.test(compact)) return true;
  if (/^(XXS|XS|S|M|L|XL|XXL|\d+XL)-(XXS|XS|S|M|L|XL|XXL|\d+XL)$/i.test(t)) return true;
  return false;
}

/** Fix Color↔Size swap when Color holds a size token (e.g. one-size) and Size holds a color. */
export function repairSwappedColorSizeAttrs(
  attrs: Record<string, string>,
): Record<string, string> {
  const color = attrs.Color;
  const size = attrs.Size;
  if (!color || !size) return attrs;
  if (looksLikeSizeToken(color) && !looksLikeSizeToken(size)) {
    return { ...attrs, Color: size, Size: color };
  }
  return attrs;
}

export function formatVariantLabel(attrs: Record<string, string>): string {
  const order: VariationSpecKey[] = ['Size', 'Color', 'Number of Items'];
  const parts = order.filter((k) => attrs[k]).map((k) => attrs[k]);
  if (parts.length > 0) return parts.join('/');
  return Object.values(attrs).filter(Boolean).join('/') || '—';
}

/** Coerce attr values to string so JSON numbers (e.g. Size: 47) don't diverge from "47". */
export function normalizeVariantAttrs(
  raw: Record<string, unknown> | null | undefined,
): Record<string, string> {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const out: Record<string, string> = {};
  for (const [k, v] of Object.entries(raw)) {
    if (v == null) continue;
    const s = String(v).trim();
    if (s) out[k] = s;
  }
  return out;
}

export function parseVariantAttributesJson(raw?: string): Record<string, string> {
  if (!raw) return {};
  try {
    const obj = JSON.parse(raw) as Record<string, unknown>;
    if (obj && typeof obj === 'object' && !Array.isArray(obj)) {
      return repairSwappedColorSizeAttrs(normalizeVariantAttrs(obj));
    }
  } catch {
    /* plain string fallback */
  }
  return repairSwappedColorSizeAttrs({ Color: raw });
}

export type VariationSpecSnapshot = {
  colors: string[];
  sizes: string[];
  itemCounts: string[];
};

type VariationAxis = { key: VariationSpecKey; values: string[] };

function buildVariationAxes(
  colors: string[],
  sizes: string[],
  itemCounts: string[],
): VariationAxis[] {
  const axes: VariationAxis[] = [];
  if (sizes.length > 0) axes.push({ key: 'Size', values: sizes.map(String) });
  if (colors.length > 0) axes.push({ key: 'Color', values: colors.map(String) });
  if (itemCounts.length > 0) {
    axes.push({ key: 'Number of Items', values: itemCounts.map(String) });
  }
  return axes;
}

function cartesianCombos(axes: VariationAxis[]): Record<string, string>[] {
  let combos: Record<string, string>[] = [{}];
  for (const axis of axes) {
    const next: Record<string, string>[] = [];
    for (const base of combos) {
      for (const value of axis.values) {
        next.push({ ...base, [axis.key]: value });
      }
    }
    combos = next;
  }
  return combos;
}

function comboKeyForAxes(
  attrs: Record<string, string>,
  axes: VariationAxis[],
): string {
  return axes
    .map((axis) => `${axis.key}=${String(attrs[axis.key] ?? '').trim()}`)
    .join('|');
}

function rowMatchesAxes(
  attrs: Record<string, string>,
  axes: VariationAxis[],
): boolean {
  return axes.every((axis) =>
    axis.values.includes(String(attrs[axis.key] ?? '').trim()),
  );
}

function pickAxisAttrs(
  attrs: Record<string, string>,
  axes: VariationAxis[],
): Record<string, string> {
  const out: Record<string, string> = {};
  for (const axis of axes) {
    const v = String(attrs[axis.key] ?? '').trim();
    if (v) out[axis.key] = v;
  }
  return out;
}

function addedValueSet(prev: string[], next: string[]): Set<string> {
  const prevSet = new Set(prev.map(String));
  return new Set(next.map(String).filter((v) => !prevSet.has(v)));
}

function isBlankVariationTable(rows: ListingVariationRow[]): boolean {
  if (rows.length === 0) return true;
  return rows.every((row) => {
    const attrs = resolveVariationRowAttrs(row);
    const hasAttr = Object.values(attrs).some((v) => String(v ?? '').trim());
    const spec = String(row.specValue ?? '').trim();
    return !hasAttr && (!spec || spec === 'Default' || spec === '—');
  });
}

/**
 * Rebuild the variant table from selected Color × Size × Number of Items chips.
 * Incremental when `previous` is passed: keep holes the user deleted, only add the
 * slice for newly ticked values, and fill the remaining cartesian when an axis is
 * dropped or the table is still blank.
 */
export function rebuildVariationsFromSpecs(
  colors: string[],
  sizes: string[],
  itemCounts: string[],
  existing: ListingVariationRow[],
  previous?: VariationSpecSnapshot,
): ListingVariationRow[] {
  const axes = buildVariationAxes(colors, sizes, itemCounts);
  if (axes.length === 0) {
    return [createEmptyVariationRow(0)];
  }

  const prev =
    previous &&
    previous.colors.length + previous.sizes.length + previous.itemCounts.length > 0
      ? previous
      : extractSelectedSpecs(existing);
  const prevAxes = buildVariationAxes(prev.colors, prev.sizes, prev.itemCounts);
  const fullCombos = cartesianCombos(axes);
  const axisDropped = prevAxes.some(
    (axis) => !axes.some((next) => next.key === axis.key),
  );
  const newAxes = axes.filter(
    (axis) => !prevAxes.some((p) => p.key === axis.key),
  );

  let combos: Record<string, string>[];
  if (isBlankVariationTable(existing) || axisDropped) {
    combos = fullCombos;
  } else if (newAxes.length > 0) {
    const seen = new Set<string>();
    for (const row of existing) {
      const attrs = resolveVariationRowAttrs(row);
      if (!rowMatchesAxes(attrs, prevAxes)) continue;
      let slice = [pickAxisAttrs(attrs, prevAxes)];
      for (const axis of newAxes) {
        slice = slice.flatMap((base) =>
          axis.values.map((value) => ({ ...base, [axis.key]: value })),
        );
      }
      for (const combo of slice) {
        const key = comboKeyForAxes(combo, axes);
        if (seen.has(key)) continue;
        seen.add(key);
      }
    }
    combos =
      seen.size > 0
        ? fullCombos.filter((combo) => seen.has(comboKeyForAxes(combo, axes)))
        : fullCombos;
  } else {
    const addedColors = addedValueSet(prev.colors, colors);
    const addedSizes = addedValueSet(prev.sizes, sizes);
    const addedItems = addedValueSet(prev.itemCounts, itemCounts);
    const usesAdded = (attrs: Record<string, string>) =>
      (attrs.Color != null && addedColors.has(attrs.Color)) ||
      (attrs.Size != null && addedSizes.has(attrs.Size)) ||
      (attrs['Number of Items'] != null &&
        addedItems.has(attrs['Number of Items']));

    const keepKeys = new Set<string>();
    for (const row of existing) {
      const attrs = resolveVariationRowAttrs(row);
      if (!rowMatchesAxes(attrs, axes)) continue;
      keepKeys.add(comboKeyForAxes(attrs, axes));
    }
    for (const combo of fullCombos) {
      const key = comboKeyForAxes(combo, axes);
      if (keepKeys.has(key)) continue;
      if (usesAdded(combo)) keepKeys.add(key);
    }
    combos =
      keepKeys.size > 0
        ? fullCombos.filter((combo) => keepKeys.has(comboKeyForAxes(combo, axes)))
        : fullCombos;
  }

  const sameCombo = (a: Record<string, string>, b: Record<string, string>) => {
    const keysA = Object.keys(a).filter((k) => String(a[k] ?? '').trim());
    const keysB = Object.keys(b).filter((k) => String(b[k] ?? '').trim());
    return (
      keysA.length === keysB.length &&
      keysA.every((k) => String(a[k]).trim() === String(b[k] ?? '').trim())
    );
  };
  const axesMatch = (a: Record<string, string>, b: Record<string, string>) =>
    axes.every((axis) => a[axis.key] === b[axis.key]);

  /**
   * `exact` gates 4Seller identity: only a full attrs match may carry `id` / `fourSellerSkuId`
   * forward. A partial (axis-subset) match still seeds SKU code, price and stock so dropping an
   * axis does not wipe the user's data, but 4Seller then treats the row as a new SKU instead of
   * renaming a live one into a different sale-prop combo.
   */
  const findPrev = (
    attrs: Record<string, string>,
  ): { row: ListingVariationRow; exact: boolean } | undefined => {
    const label = formatVariantLabel(attrs);

    const byAttrs = existing.find(
      (r) => r.attrs && Object.keys(r.attrs).length > 0 && sameCombo(r.attrs, attrs),
    );
    if (byAttrs) return { row: byAttrs, exact: true };

    const exactLabel = existing.find((r) => r.specValue === label);
    if (exactLabel) return { row: exactLabel, exact: true };

    const byAxes = existing.find(
      (r) => r.attrs && Object.keys(r.attrs).length > 0 && axesMatch(r.attrs, attrs),
    );
    if (byAxes) return { row: byAxes, exact: false };

    // Legacy labels without attrs may be Color/Size instead of Size/Color.
    if (label.includes('/')) {
      const b = new Set(label.split('/').map((s) => s.trim().toLowerCase()));
      const setMatches = existing.filter((r) => {
        if (!r.specValue?.includes('/')) return false;
        if (r.attrs && Object.keys(r.attrs).length > 0) return false;
        const a = new Set(r.specValue.split('/').map((s) => s.trim().toLowerCase()));
        return a.size === b.size && [...a].every((x) => b.has(x));
      });
      if (setMatches.length === 1 && setMatches[0]) {
        return { row: setMatches[0], exact: false };
      }
    }
    return undefined;
  };

  const mainSpec = axes[0]?.key ?? 'Color';

  return combos.map((attrs, i) => {
    const label = formatVariantLabel(attrs);
    const match = findPrev(attrs);
    const prevRow = match?.row;
    const keepIdentity = match?.exact ?? false;
    return {
      id: keepIdentity ? prevRow?.id : undefined,
      fourSellerSkuId: keepIdentity ? prevRow?.fourSellerSkuId : undefined,
      sortOrder: i,
      mainSpec,
      specValue: label,
      attrs,
      // A partial match keeps its SKU code: distinct combos always resolve to distinct `prev`
      // rows, so no duplicate can arise, and dropping an axis would otherwise force a full
      // Batch SKU re-run. Only `id` / `fourSellerSkuId` stay strict, because reusing those
      // renames a live 4Seller SKU into a different sale-prop combo.
      sellerSku: prevRow?.sellerSku ?? '',
      costPrice: prevRow?.costPrice ?? 0,
      salePrice: prevRow?.salePrice ?? 0,
      originalPrice: prevRow?.originalPrice,
      quantity: prevRow?.quantity ?? 0,
      barcode: keepIdentity ? (prevRow?.barcode ?? '') : '',
      isDefault: i === 0 ? true : (keepIdentity ? (prevRow?.isDefault ?? false) : false),
    };
  });
}

/** Row attrs, falling back to parsing the label when the form store lost `attrs`. */
export function resolveVariationRowAttrs(
  row: ListingVariationRow,
  fallbackMain = 'Color',
): Record<string, string> {
  if (row.attrs && Object.keys(row.attrs).length > 0) {
    return repairSwappedColorSizeAttrs(normalizeVariantAttrs(row.attrs));
  }
  return parseVariantAttributesJson(variationAttrsToJson(row, fallbackMain));
}

export interface VariationMatrixCheck {
  /** Blocks Save Draft / Publish — 4Seller would reject or mis-assign the SKU. */
  error: string | null;
  /** Reserved; sparse Color×Size matrices are intentional and no longer warned. */
  warning: string | null;
}

/**
 * Guards the invariants the 4Seller push relies on: every row resolves to a combo built
 * only from currently selected chip values, combos are unique, and every row carries a
 * unique SKU code (the backend falls back to it when matching against the previous
 * payload, and 4Seller rejects a SKU with no MSKU).
 */
export function validateVariationMatrix(
  rows: ListingVariationRow[],
  specs: { colors: string[]; sizes: string[]; itemCounts: string[] },
  options?: { requirePricing?: boolean; mainSpecification?: string },
): VariationMatrixCheck {
  const axes: { key: VariationSpecKey; values: string[] }[] = [];
  if (specs.sizes.length > 0) axes.push({ key: 'Size', values: specs.sizes.map(String) });
  if (specs.colors.length > 0) axes.push({ key: 'Color', values: specs.colors.map(String) });
  if (specs.itemCounts.length > 0) {
    axes.push({ key: 'Number of Items', values: specs.itemCounts.map(String) });
  }
  if (axes.length === 0) return { error: null, warning: null };

  const fail = (error: string): VariationMatrixCheck => ({ error, warning: null });
  const fallbackMain = options?.mainSpecification || 'Color';
  const seenCombos = new Map<string, number>();
  const seenSkus = new Map<string, number>();

  for (const [index, row] of rows.entries()) {
    const attrs = resolveVariationRowAttrs(row, fallbackMain);
    const label = row.specValue?.trim() || formatVariantLabel(attrs);

    for (const axis of axes) {
      const value = String(attrs[axis.key] ?? '').trim();
      if (!value) {
        return fail(
          `Variant "${label}" thiếu ${axis.key} — bấm lại một chip ${axis.key} để dựng lại bảng variant.`,
        );
      }
      if (!axis.values.includes(value)) {
        return fail(
          `Variant "${label}" có ${axis.key} = "${value}" không còn được chọn — bấm lại một chip ${axis.key} để dựng lại bảng variant.`,
        );
      }
    }

    const comboKey = axes.map((axis) => `${axis.key}=${attrs[axis.key]}`).join('|');
    const dupeAt = seenCombos.get(comboKey);
    if (dupeAt !== undefined) {
      return fail(
        `Variant "${label}" bị trùng (dòng ${dupeAt + 1} và ${index + 1}) — 4Seller từ chối hai SKU cùng thuộc tính.`,
      );
    }
    seenCombos.set(comboKey, index);

    const sku = String(row.sellerSku ?? '').trim().toLowerCase();
    // Required by every push mode, Save Draft included: the backend only writes `sellerSku` when
    // non-empty, so a blank one reaches 4Seller as a SKU with no MSKU and is rejected there.
    if (!sku) {
      return fail(
        `Variant "${label}" chưa có SKU — dùng Batch SKU để sinh mã cho các variant mới.`,
      );
    }
    const skuAt = seenSkus.get(sku);
    if (skuAt !== undefined) {
      return fail(
        `SKU "${row.sellerSku}" bị trùng ở dòng ${skuAt + 1} và ${index + 1} — dùng Batch SKU để sinh lại mã duy nhất.`,
      );
    }
    seenSkus.set(sku, index);

    if (options?.requirePricing) {
      if (!(Number(row.salePrice) > 0)) {
        return fail(`Variant "${label}" chưa có giá bán — nhập giá hoặc dùng Batch giá bán.`);
      }
      if (row.quantity == null) {
        return fail(`Variant "${label}" chưa có tồn kho — nhập QTY hoặc dùng Batch QTY.`);
      }
    }
  }

  return { error: null, warning: null };
}

export function variationAttrsToJson(
  row: ListingVariationRow,
  fallbackMain = 'Color',
): string {
  if (row.attrs && Object.keys(row.attrs).length > 0) {
    return JSON.stringify(row.attrs);
  }

  const raw = (row.specValue || '').trim();
  if (raw.includes('/')) {
    const parts = raw.split('/').map((s) => s.trim()).filter(Boolean);
    const attrs: Record<string, string> = {};
    // formatVariantLabel order is Size / Color / Number of Items — also repair Color/Size titles.
    const [first = '', second = '', third] = parts;
    if (parts.length >= 2) {
      if (looksLikeSizeToken(first) && !looksLikeSizeToken(second)) {
        attrs.Size = first;
        attrs.Color = second;
      } else if (looksLikeSizeToken(second) && !looksLikeSizeToken(first)) {
        attrs.Color = first;
        attrs.Size = second;
      } else if (looksLikeSizeToken(first)) {
        attrs.Size = first;
        attrs.Color = second;
      } else {
        attrs.Color = first;
        attrs.Size = second;
      }
      if (third) attrs['Number of Items'] = third;
    } else if (parts.length === 1) {
      attrs[looksLikeSizeToken(first) ? 'Size' : fallbackMain || 'Color'] = first;
    }
    return JSON.stringify(repairSwappedColorSizeAttrs(attrs));
  }

  return JSON.stringify({
    [row.mainSpec || fallbackMain]: row.specValue || 'Default',
  });
}

export function extractSelectedSpecs(rows: ListingVariationRow[]): {
  colors: string[];
  sizes: string[];
  itemCounts: string[];
} {
  const colors = new Set<string>();
  const sizes = new Set<string>();
  const itemCounts = new Set<string>();

  for (const row of rows) {
    const attrs = repairSwappedColorSizeAttrs(
      normalizeVariantAttrs(
        row.attrs && Object.keys(row.attrs).length > 0
          ? row.attrs
          : parseVariantAttributesJson(
              row.specValue?.startsWith('{') ? row.specValue : undefined,
            ),
      ),
    );

    if (attrs.Color) colors.add(attrs.Color);
    if (attrs.Size) sizes.add(attrs.Size);
    if (attrs['Number of Items']) itemCounts.add(attrs['Number of Items']);

    // Legacy "Color/Size" or "Size/Color" labels without attrs
    if (!attrs.Color && !attrs.Size && row.specValue?.includes('/')) {
      const parts = row.specValue.split('/');
      const left = parts[0]?.trim();
      const right = parts[1]?.trim();
      if (left && right) {
        if (looksLikeSizeToken(left) && !looksLikeSizeToken(right)) {
          sizes.add(left);
          colors.add(right);
        } else if (looksLikeSizeToken(right) && !looksLikeSizeToken(left)) {
          colors.add(left);
          sizes.add(right);
        } else if (looksLikeSizeToken(left)) {
          sizes.add(left);
          colors.add(right);
        } else {
          colors.add(left);
          sizes.add(right);
        }
        const third = parts[2]?.trim();
        if (third && /^\d+$/.test(third)) itemCounts.add(third);
      }
    } else if (!attrs.Color && !attrs.Size && row.specValue) {
      if ((row.mainSpec || 'Color') === 'Size') sizes.add(row.specValue);
      else if (row.mainSpec === 'Number of Items') itemCounts.add(row.specValue);
      else if (row.specValue !== 'Default' && row.specValue !== '') colors.add(row.specValue);
    }
  }

  return {
    colors: [...colors],
    sizes: [...sizes],
    itemCounts: [...itemCounts],
  };
}

export function validateProductImages(
  images: { mediaType: string; url?: string }[],
): string | null {
  const productImages = images.filter(
    (i) => i.mediaType === 'Main' || i.mediaType === 'Detail',
  );
  if (productImages.length > 9) {
    return 'Tối đa 9 ảnh sản phẩm (Main + Detail).';
  }
  const mainCount = productImages.filter((i) => i.mediaType === 'Main').length;
  if (mainCount !== 1) {
    return 'Cần đúng 1 ảnh Main.';
  }
  if (productImages.some((i) => !i.url?.trim())) {
    return 'Mỗi ảnh sản phẩm cần có URL.';
  }
  return null;
}

/**
 * 4Seller rejects explicit http:// image URLs (Invalid Para). Upgrade scheme only —
 * leaves https:// and protocol-relative //… unchanged.
 */
export function upgradeHttpToHttpsUrl(url?: string | null): string {
  const trimmed = String(url ?? '').trim();
  if (!trimmed) return '';
  if (trimmed.toLowerCase().startsWith('http://')) {
    return `https://${trimmed.slice(7)}`;
  }
  return trimmed;
}

/** Upgrade every http:// image URL in a gallery list (idempotent). */
export function upgradeHttpImageUrls<T extends { url?: string | null }>(
  list: T[],
): T[] {
  let changed = false;
  const next = list.map((img) => {
    const url = String(img.url ?? '').trim();
    if (!url) return img;
    const upgraded = upgradeHttpToHttpsUrl(url);
    if (upgraded === url) return img;
    changed = true;
    return { ...img, url: upgraded };
  });
  return changed ? next : list;
}

/** Upgrade http:// segments inside a pipe-joined URL list (variation.mainImage). */
export function upgradeHttpPipeJoinedUrls(raw?: string | null): string {
  const trimmed = String(raw ?? '').trim();
  if (!trimmed) return '';
  return trimmed
    .split('|')
    .map((part) => upgradeHttpToHttpsUrl(part))
    .filter(Boolean)
    .join('|');
}

/**
 * Whether edit-page silent `?refresh=true` is still needed.
 * Scheduled sync (page + batch-detail, no per-id) can leave listings without RawPayload
 * or with page-only thin media — those still need on-demand `/detail`.
 */
export function listingNeedsFourSellerRefresh(listing: {
  hasRawPayload?: boolean;
  mainImage?: string | null;
  images?: { url?: string | null }[] | null;
  variations?: { mainImage?: string | null }[] | null;
}): boolean {
  if (!listing.hasRawPayload) return true;

  if (listing.mainImage?.trim()) return false;
  if ((listing.images ?? []).some((img) => Boolean(String(img.url ?? '').trim()))) {
    return false;
  }
  if (
    (listing.variations ?? []).some((v) => Boolean(String(v.mainImage ?? '').trim()))
  ) {
    return false;
  }
  return true;
}

/** Parity 4Seller warning_box: keep 【Publish Failed】 / 【Unpublishable】 prefixes. */
export function formatPublishFailedMessage(error: string): string {
  const trimmed = error.trim();
  if (
    /^【\s*Publish\s*Failed\s*】/i.test(trimmed) ||
    /^\[Publish Failed\]/i.test(trimmed) ||
    /^【\s*Unpublishable\s*】/i.test(trimmed) ||
    /^\[Unpublishable\]/i.test(trimmed)
  ) {
    return trimmed;
  }
  if (/product attributes are unavailable/i.test(trimmed) || /^unpublishable\b/i.test(trimmed)) {
    return `【Unpublishable】${trimmed.replace(/^【\s*Unpublishable\s*】\s*/i, '')}`;
  }
  return `【Publish Failed】${trimmed}`;
}

/** System-generated when 4Seller is blocked but returns no failedMessage. */
export function isSystemPublishBlockedFallback(error: string): boolean {
  const trimmed = error.trim();
  if (!trimmed) return false;
  return (
    /4Seller vẫn ở draft \(/i.test(trimmed) ||
    (/publishStatus=/i.test(trimmed) && /không trả lý do chi tiết/i.test(trimmed))
  );
}

export type SyncErrorDisplay = {
  title: string;
  body: string;
  isFromFourSeller: boolean;
};

/** Short title + full body for syncError alerts (list + edit). */
export function formatSyncErrorDisplay(error: string): SyncErrorDisplay {
  const formatted = formatPublishFailedMessage(error);
  // Display upgraded https:// so Alert links match gallery after http→https rewrite.
  const trimmed = upgradeHttpUrlsInDisplayText(formatted.trim());

  if (isSystemPublishBlockedFallback(trimmed)) {
    return {
      title: 'Không thể publish',
      body: trimmed,
      isFromFourSeller: false,
    };
  }

  if (/^【\s*Unpublishable\s*】/i.test(trimmed) || /^\[Unpublishable\]/i.test(trimmed)) {
    return {
      title: 'Không thể publish (Unpublishable)',
      body: trimmed,
      isFromFourSeller: true,
    };
  }

  if (
    /^【\s*Publish\s*Failed\s*】/i.test(trimmed) ||
    /^\[Publish Failed\]/i.test(trimmed)
  ) {
    return {
      title: 'Publish thất bại',
      body: trimmed,
      isFromFourSeller: true,
    };
  }

  return {
    title: 'Lỗi đồng bộ / push',
    body: trimmed,
    isFromFourSeller: false,
  };
}

function upgradeHttpUrlsInDisplayText(text: string): string {
  if (!text || !/http:\/\//i.test(text)) return text;
  return text.replace(/http:\/\//gi, 'https://');
}

/** Short Alert title for syncError — body carries the full 4Seller text. */
export function publishFailedAlertTitle(formattedError: string): string {
  return formatSyncErrorDisplay(formattedError).title;
}

/** Parse `Specifics.<attrName>` tokens from a 4Seller publish/sync error. */
export function parseSyncErrorMissingSpecifics(error?: string | null): string[] {
  const trimmed = String(error ?? '').trim();
  if (!trimmed) return [];
  const found = new Set<string>();
  const re = /Specifics\.([^;]+)/gi;
  let match: RegExpExecArray | null;
  while ((match = re.exec(trimmed)) !== null) {
    const name = match[1]?.trim();
    if (name) found.add(name);
  }
  return [...found];
}
