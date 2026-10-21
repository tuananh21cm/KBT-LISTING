import type {
  ListingAppliedPricing,
  ListingDetail,
  UpdateListingPayload,
} from '@drop/types/listing';
import { NO_BRAND_ID, NO_BRAND_NAME } from '@drop/types/listing';
import { normalizeDimensionUnit, normalizeWeightUnit } from './shippingUnits';
import type { ListingFormValues } from './listingFormUtils';
import {
  createEmptyVariationRow,
  formatVariantLabel,
  normalizeVariantAttrs,
  normalizeVariations,
  parseVariantAttributesJson,
  repairSwappedColorSizeAttrs,
  SIZE_PRESETS,
  upgradeHttpImageUrls,
  upgradeHttpPipeJoinedUrls,
  upgradeHttpToHttpsUrl,
  variationAttrsToJson,
} from './listingFormUtils';
import type {
  ListingAttributeRow,
  ListingCertificationRow,
  ListingImageRow,
  ListingVariationRow,
} from './productFormTypes';

/**
 * Default Specific for listings that have none — 4Seller rejects an empty productAttributes on
 * push, so we seed Brand → No brand (mirrors 4Seller's default) so the listing can be published.
 */
export const DEFAULT_SPECIFICS: ListingAttributeRow[] = [
  { attrId: 'Brand', attrName: 'Brand', attrValue: NO_BRAND_NAME, isRequired: true },
];

/** True when the Specifics row is the seeded Brand attribute. */
export function isBrandAttribute(
  attr: { attrId?: string | null; attrName?: string | null } | null | undefined,
): boolean {
  if (!attr) return false;
  const id = String(attr.attrId ?? '').trim().toLowerCase();
  const name = String(attr.attrName ?? '').trim().toLowerCase();
  return id === 'brand' || name === 'brand';
}

/**
 * True when Specifics are empty or only Brand (/cert echo) — eligible for background Auto map.
 */
export function needsAutoMapSpecifics(
  attrs: ListingAttributeRow[] | null | undefined,
): boolean {
  const list = attrs ?? [];
  if (list.length === 0) return true;
  return list.every(
    (a) => isBrandAttribute(a) || isCertificationAttribute(a.attrName),
  );
}

function withDefaultSpecifics(rows: ListingAttributeRow[]): ListingAttributeRow[] {
  return rows.length > 0 ? rows : DEFAULT_SPECIFICS.map((r) => ({ ...r }));
}

/** Trim + drop empty strings for 4Seller searchTerms / productHighlights. */
export function normalizeStringList(values?: string[] | null): string[] {
  return (values ?? [])
    .map((s) => String(s ?? '').trim())
    .filter(Boolean);
}

/**
 * Certification / compliance fields (Dangerous Goods, CA Prop 65…) are surfaced in the dedicated
 * "Product Certification" card. 4Seller/TikTok also echoes them inside productAttributes, which would
 * duplicate them in the "Specifics" card — filter them out of the attribute list.
 */
export function isCertificationAttribute(name?: string | null): boolean {
  if (!name) return false;
  const n = name.trim().toLowerCase();
  if (!n) return false;
  return (
    n.includes('prop 65') ||
    n.includes('prop65') ||
    n.includes('hazardous') ||
    n.includes('dangerous goods') ||
    n.includes('carcinogen') ||
    n.includes('reproductive') ||
    n.includes('repro. chems') ||
    n.includes('repro chems')
  );
}

/**
 * Attributes TikTok refuses on publish whatever value they carry (【Unpublishable】), so Auto
 * Specifics / Auto Publish never create them and they are dropped from the push payload.
 */
/** Formerly blocked Region of origin / CPSIA; those are mandatory on many categories and are seeded. */
export function isPublishBlockedAttribute(_name?: string | null): boolean {
  return false;
}

/** Local certType ↔ TikTok productAttributes Yes/No (fashion default = No). */
export const DEFAULT_PRODUCT_CERTIFICATIONS: ListingCertificationRow[] = [
  { certType: 'Compliance', name: 'No', fileUrls: '[]' },
  { certType: 'ProductCert', name: 'No', fileUrls: '[]' },
  { certType: 'ResponsiblePerson', name: 'No', fileUrls: '[]' },
];

function normalizeYesNo(value?: string | null): 'Yes' | 'No' {
  return value?.trim().toLowerCase() === 'yes' ? 'Yes' : 'No';
}

function attrValueForCertType(
  attrs: { attrId?: string; attrName?: string | null; attrValue?: string | null }[],
  certType: string,
): string | undefined {
  const matchers: Record<string, (n: string) => boolean> = {
    Compliance: (n) => n.includes('dangerous goods') || n.includes('hazardous'),
    ProductCert: (n) =>
      n.includes('repro. chems') ||
      n.includes('repro chems') ||
      (n.includes('prop 65') && n.includes('repro')),
    ResponsiblePerson: (n) => n.includes('carcinogen'),
  };
  const ids: Record<string, string> = {
    Compliance: '101619',
    ProductCert: '101395',
    ResponsiblePerson: '101400',
  };
  const matcher = matchers[certType];
  const id = ids[certType];
  const hit = attrs.find((a) => {
    if (id && a.attrId === id) return true;
    const n = a.attrName?.trim().toLowerCase() ?? '';
    return matcher ? matcher(n) : false;
  });
  return hit?.attrValue?.trim() || undefined;
}

/**
 * Always three Product Certification rows (Compliance / ProductCert / ResponsiblePerson), default No.
 * Prefers listing.certifications, then Yes/No echoed in productAttributes.
 */
export function withDefaultCertifications(
  listingCerts: ListingCertificationRow[] | undefined,
  attributes?: { attrId?: string; attrName?: string | null; attrValue?: string | null }[],
): ListingCertificationRow[] {
  const byType = new Map(
    (listingCerts ?? []).map((c) => [c.certType, c] as const),
  );
  return DEFAULT_PRODUCT_CERTIFICATIONS.map((def) => {
    const existing = byType.get(def.certType);
    const fromAttr = attributes ? attrValueForCertType(attributes, def.certType) : undefined;
    const name = normalizeYesNo(existing?.name || fromAttr || def.name);
    return {
      ...existing,
      certType: def.certType,
      name,
      fileUrls: existing?.fileUrls ?? def.fileUrls ?? '[]',
    };
  });
}

function colorFromVariation(v: {
  variantAttributes?: string | null;
  title?: string | null;
  attrs?: Record<string, string>;
}): string | undefined {
  if (v.attrs?.Color?.trim()) return v.attrs.Color.trim();
  const attrs = parseVariantAttributesJson(v.variantAttributes ?? undefined);
  if (attrs.Color?.trim() && !attrs.Color.includes('/')) return attrs.Color.trim();
  if (v.title?.includes('/')) {
    const parts = v.title.split('/').map((s) => s.trim()).filter(Boolean);
    const firstIsSize =
      SIZE_PRESETS.some((s) => s.toLowerCase() === parts[0]?.toLowerCase()) ||
      /^(XXS|XS|S|M|L|XL|XXL|\d+XL)$/i.test(parts[0] ?? '');
    // formatVariantLabel → Size/Color; legacy → Color/Size
    if (parts.length >= 2) return firstIsSize ? parts[1] : parts[0];
    return parts[0];
  }
  return attrs.Color?.trim() || undefined;
}

/** Mirror backend ResolveListingProductType — trust API productType when set. */
export function inferListingProductType(
  listing: Pick<ListingDetail, 'productType' | 'variationCount'>,
  variationRowCount: number,
): 'SingleSku' | 'MultiSku' {
  const count = listing.variationCount ?? 0;
  if (count > 1 || variationRowCount > 1) {
    return 'MultiSku';
  }
  if (listing.productType === 'MultiSku' || listing.productType === 'SingleSku') {
    return listing.productType;
  }
  return 'SingleSku';
}

export function mirrorListingToForm(listing: ListingDetail): ListingFormValues {
  let mainSpecification = 'Color';
  const variations: ListingVariationRow[] = (listing.variations ?? []).map((sku, index) => {
    const attrs = parseVariantAttributesJson(sku.variantAttributes ?? undefined);
    let mainSpec = Object.keys(attrs)[0] ?? 'Color';
    let specValue =
      Object.keys(attrs).length > 0
        ? formatVariantLabel(attrs)
        : sku.title || '';
    if (sku.variantAttributes) {
      try {
        const parsed = repairSwappedColorSizeAttrs(
          normalizeVariantAttrs(
            JSON.parse(sku.variantAttributes) as Record<string, unknown>,
          ),
        );
        mainSpec = Object.keys(parsed)[0] ?? mainSpec;
        const color = parsed.Color;
        const size = parsed.Size;
        if (color && size) {
          specValue = formatVariantLabel(parsed);
        } else {
          specValue = Object.values(parsed).filter(Boolean).join('/') || specValue;
        }
      } catch {
        /* keep */
      }
    } else if (sku.title?.includes('/')) {
      specValue = sku.title;
      mainSpec = 'Color';
    }
    if (index === 0) mainSpecification = mainSpec;
    return {
      id: sku.id,
      fourSellerSkuId: sku.fourSellerSkuId ?? undefined,
      sortOrder: sku.sortOrder ?? index,
      mainSpec,
      specValue,
      attrs: Object.keys(attrs).length ? attrs : undefined,
      sellerSku: sku.sellerSku ?? '',
      costPrice: sku.initialPrice ?? undefined,
      salePrice: sku.salePrice ?? 0,
      originalPrice: sku.originalPrice ?? sku.salePrice ?? undefined,
      quantity: sku.quantity ?? 0,
      barcode: sku.barcode ?? '',
      isDefault: sku.isDefault ?? index === 0,
    };
  });

  const variationById = new Map((listing.variations ?? []).map((v) => [v.id, v]));

  let images: ListingImageRow[] =
    (listing.images ?? []).length > 0
      ? (listing.images ?? []).map((img, index) => {
          let colorValue = img.colorValue?.trim() || undefined;
          if (!colorValue && img.listingSkuId) {
            const v = variationById.get(img.listingSkuId);
            colorValue = v ? colorFromVariation(v) : undefined;
          }
          return {
            mediaType: img.mediaType,
            url: upgradeHttpToHttpsUrl(img.url),
            sortOrder: img.sortOrder ?? index,
            listingSkuId: img.listingSkuId ?? undefined,
            tikTokImageId: img.tikTokImageId ?? undefined,
            colorValue,
          };
        })
      : listing.mainImage
        ? listing.mainImage
            .split('|')
            .map((s) => s.trim())
            .filter(Boolean)
            .map((url, i) => ({
              mediaType: i === 0 ? 'Main' : 'Detail',
              url: upgradeHttpToHttpsUrl(url),
              sortOrder: i,
            }))
        : [{ mediaType: 'Main', url: '', sortOrder: 0 }];

  // Fallback: bind all variation.mainImage URLs (pipe-separated, ≤ 9) per color.
  const urlsByColor = new Map<string, Set<string>>();
  for (const img of images) {
    const c = img.colorValue?.trim().toLowerCase();
    if (!c || !String(img.url ?? '').trim()) continue;
    if (!urlsByColor.has(c)) urlsByColor.set(c, new Set());
    urlsByColor.get(c)!.add(img.url.trim());
  }
  let nextSort =
    images.length === 0 ? 0 : Math.max(...images.map((img) => img.sortOrder ?? 0)) + 1;
  for (const v of listing.variations ?? []) {
    const color = colorFromVariation(v);
    if (!color?.trim()) continue;
    const key = color.trim().toLowerCase();
    const existing = urlsByColor.get(key) ?? new Set<string>();
    const parts = (v.mainImage ?? '')
      .split('|')
      .map((s) => s.trim())
      .filter(Boolean);
    for (const url of parts) {
      const upgraded = upgradeHttpToHttpsUrl(url);
      if (existing.size >= 9 || existing.has(upgraded)) continue;
      images = [
        ...images,
        {
          mediaType: 'Detail',
          url: upgraded,
          sortOrder: nextSort++,
          listingSkuId: v.id,
          colorValue: color.trim(),
        },
      ];
      existing.add(upgraded);
    }
    urlsByColor.set(key, existing);
  }

  // Keep shared product gallery lean (≤ 9) like Product Create / import; Variant Images stay.
  const SHARED_PRODUCT_IMAGE_MAX = 9;
  const isSharedRow = (img: { mediaType?: string; colorValue?: string; listingSkuId?: string }) =>
    !img.colorValue?.trim() &&
    !img.listingSkuId &&
    (!img.mediaType || img.mediaType === 'Main' || img.mediaType === 'Detail');
  const isVariantRow = (img: { colorValue?: string; listingSkuId?: string }) =>
    Boolean(img.colorValue?.trim() || img.listingSkuId);
  const variantBound = images.filter((img) => isVariantRow(img));
  const extras = images.filter((img) => !isSharedRow(img) && !isVariantRow(img));
  const sharedWithUrl = upgradeHttpImageUrls(
    images
      .filter((img) => isSharedRow(img) && String(img.url ?? '').trim())
      .slice(0, SHARED_PRODUCT_IMAGE_MAX)
      .map((img, i) => ({
        ...img,
        mediaType: i === 0 ? 'Main' : 'Detail',
        sortOrder: i,
        colorValue: undefined,
        listingSkuId: undefined,
      })),
  );
  const shared =
    sharedWithUrl.length > 0
      ? sharedWithUrl
      : [{ mediaType: 'Main', url: '', sortOrder: 0 }];
  images = [
    ...shared,
    ...variantBound.map((img, i) => ({
      ...img,
      mediaType: 'Detail',
      sortOrder: shared.length + i,
    })),
    ...extras.map((img, i) => ({
      ...img,
      sortOrder: shared.length + variantBound.length + i,
    })),
  ];

  return {
    sourceId: '',
    externalProductId: '',
    title: listing.productName ?? '',
    status: 'Active',
    productName: listing.productName ?? '',
    categoryId: listing.categoryId ?? undefined,
    categoryName: listing.categoryName ?? undefined,
    // Admin listing edit luôn publish No brand — không bind brand từ RawPayload.
    brandId: NO_BRAND_ID,
    brandName: NO_BRAND_NAME,
    productType: inferListingProductType(listing, variations.length),
    mainSpecification,
    descriptionHtml: listing.descriptionHtml ?? '',
    searchTerms: normalizeStringList(listing.searchTerms),
    productHighlights: normalizeStringList(listing.productHighlights),
    warehouseId: listing.warehouseId ?? undefined,
    packageWeight: listing.packageWeight ?? undefined,
    packageLength: listing.packageLength ?? undefined,
    packageWidth: listing.packageWidth ?? undefined,
    packageHeight: listing.packageHeight ?? undefined,
    weightUnit: normalizeWeightUnit(listing.weightUnit),
    dimensionUnit: normalizeDimensionUnit(listing.dimensionUnit),
    sizeChartId: listing.sizeChartId ?? undefined,
    erpVideoId: listing.erpVideoId ?? undefined,
    safetyWarning: listing.safetyWarning ?? undefined,
    listingSourceUrl: listing.listingSourceUrl ?? listing.sourceUrl ?? undefined,
    fourSellerGroupId: listing.fourSellerGroupId ?? undefined,
    fourSellerGroupName: listing.fourSellerGroupName ?? undefined,
    appliedRules: listing.appliedRules ?? undefined,
    refListingId: listing.refListingId ?? undefined,
    refShopId: listing.refShopId ?? undefined,
    shopId: listing.shopId ?? undefined,
    siteId: listing.siteId ?? listing.site ?? undefined,
    listingStatus: listing.pageType === 'draft' ? 'Draft' : 'Published',
    publishStatus: listing.publishStatus ?? undefined,
    fourSellerListingId: String(listing.fourSellerListingId),
    tikTokProductId: listing.tikTokProductId ?? undefined,
    variations: variations.length > 0 ? variations : [createEmptyVariationRow(0)],
    images,
    attributes: withDefaultSpecifics(
      (listing.attributes ?? [])
        .filter(
          (a) =>
            !isCertificationAttribute(a.attrName) &&
            !isPublishBlockedAttribute(a.attrName),
        )
        .map((a) => ({
          attrId: a.attrId,
          attrName: a.attrName ?? undefined,
          attrValue: a.attrValue ?? undefined,
          valueId: a.valueId ?? undefined,
          values: a.values?.length
            ? a.values.map((v) => ({
                valueId: v.valueId ?? undefined,
                valueName: v.valueName ?? undefined,
              }))
            : undefined,
          valueType: a.valueType ?? undefined,
          isRequired: a.isRequired ?? false,
        })),
    ),
    certifications: withDefaultCertifications(
      (listing.certifications ?? []).map((c) => ({
        certType: c.certType,
        externalId: c.externalId ?? undefined,
        name: c.name ?? undefined,
        phone: c.phone ?? undefined,
        email: c.email ?? undefined,
        address: c.address ?? undefined,
        fileUrls: c.fileUrls ?? undefined,
      })),
      listing.attributes,
    ),
  };
}

function resolveListingSkuIdForColor(
  color: string | undefined,
  variations: ListingVariationRow[],
  previous?: ListingDetail | null,
): string | undefined {
  if (!color?.trim()) return undefined;
  const c = color.trim().toLowerCase();
  const match = variations.find((v) => {
    const attrs = v.attrs ?? parseVariantAttributesJson(
      variationAttrsToJson(v, 'Color'),
    );
    return attrs.Color?.trim().toLowerCase() === c;
  });
  if (!match) return undefined;
  const prev =
    previous?.variations?.find(
      (v) =>
        v.sellerSku &&
        match.sellerSku &&
        v.sellerSku.toLowerCase() === match.sellerSku.toLowerCase(),
    ) ??
    previous?.variations?.find((v) => {
      const attrs = parseVariantAttributesJson(v.variantAttributes ?? undefined);
      return attrs.Color?.trim().toLowerCase() === c;
    });
  return prev?.id;
}

export function formToMirrorListingPayload(
  values: ListingFormValues,
  previous?: ListingDetail | null,
  options?: {
    appliedPricing?: ListingAppliedPricing | null;
    descriptionRewritten?: boolean;
    descriptionImagesInserted?: boolean;
  },
): UpdateListingPayload {
  const variations = normalizeVariations(values);
  const previousBySku = new Map(
    (previous?.variations ?? []).map((v) => [v.sellerSku?.toLowerCase() ?? '', v]),
  );
  const previousById = new Map((previous?.variations ?? []).map((v) => [v.id, v]));

  /** Prefer form value when non-empty; otherwise keep original listing value (hidden fields). */
  const keep = <T,>(formVal: T | null | undefined, original: T | null | undefined): T | undefined => {
    if (formVal === null || formVal === undefined) return original ?? undefined;
    if (typeof formVal === 'string' && !formVal.trim()) return original ?? undefined;
    return formVal;
  };

  return {
    productName: values.productName,
    descriptionHtml: values.descriptionHtml,
    searchTerms: normalizeStringList(values.searchTerms),
    productHighlights: normalizeStringList(values.productHighlights),
    categoryId: values.categoryId,
    categoryName: values.categoryName,
    brandId: NO_BRAND_ID,
    brandName: NO_BRAND_NAME,
    productType: keep(values.productType, previous?.productType),
    warehouseId: values.warehouseId?.trim() || undefined,
    sourceUrl: keep(values.listingSourceUrl, previous?.listingSourceUrl ?? previous?.sourceUrl),
    listingSourceUrl: keep(
      values.listingSourceUrl,
      previous?.listingSourceUrl ?? previous?.sourceUrl,
    ),
    packageWeight: values.packageWeight ?? previous?.packageWeight,
    packageLength: values.packageLength ?? previous?.packageLength,
    packageWidth: values.packageWidth ?? previous?.packageWidth,
    packageHeight: values.packageHeight ?? previous?.packageHeight,
    weightUnit: normalizeWeightUnit(keep(values.weightUnit, previous?.weightUnit)),
    dimensionUnit: normalizeDimensionUnit(keep(values.dimensionUnit, previous?.dimensionUnit)),
    sizeChartId: (() => {
      const hasSizeChart = (values.images ?? []).some(
        (img) =>
          String(img.mediaType ?? '').toLowerCase() === 'sizechart' &&
          String(img.url ?? '').trim(),
      );
      // Explicit clear when Size Chart thumb removed — do not keep() stale id.
      if (!hasSizeChart) return '';
      return keep(values.sizeChartId, previous?.sizeChartId) ?? '';
    })(),
    erpVideoId: (() => {
      const hasVideo = (values.images ?? []).some(
        (img) =>
          String(img.mediaType ?? '').toLowerCase() === 'video' &&
          String(img.url ?? '').trim(),
      );
      if (!hasVideo) return '';
      return keep(values.erpVideoId, previous?.erpVideoId) ?? '';
    })(),
    safetyWarning: keep(values.safetyWarning, previous?.safetyWarning),
    siteId: keep(values.siteId, previous?.siteId ?? previous?.site),
    fourSellerGroupId: keep(values.fourSellerGroupId, previous?.fourSellerGroupId),
    fourSellerGroupName: keep(values.fourSellerGroupName, previous?.fourSellerGroupName),
    appliedRules: keep(values.appliedRules, previous?.appliedRules),
    // Only send when provided — omit leaves existing DB jsonb unchanged.
    ...(options?.appliedPricing
      ? { appliedPricing: options.appliedPricing }
      : {}),
    descriptionRewritten: options?.descriptionRewritten ?? false,
    descriptionImagesInserted: options?.descriptionImagesInserted ?? false,
    refListingId: keep(values.refListingId, previous?.refListingId),
    refShopId: keep(values.refShopId, previous?.refShopId),
    shopId: keep(values.shopId, previous?.shopId),
    variations: variations.map((v, i) => {
      const attrsJson = variationAttrsToJson(v, values.mainSpecification || 'Color');
      const prev =
        (v.id ? previousById.get(v.id) : undefined) ??
        (v.sellerSku?.trim()
          ? previousBySku.get(v.sellerSku.trim().toLowerCase())
          : undefined);
      const color = v.attrs?.Color ?? parseVariantAttributesJson(attrsJson).Color;
      const colorUrls = (values.images ?? [])
        .filter(
          (img) =>
            img.colorValue?.trim().toLowerCase() === color?.trim().toLowerCase() &&
            String(img.url ?? '').trim(),
        )
        .map((img) => upgradeHttpToHttpsUrl(img.url));
      // Keep pipe-joined original when form did not provide variant images.
      const mainImage =
        colorUrls.length > 0
          ? colorUrls.join('|')
          : upgradeHttpPipeJoinedUrls(prev?.mainImage) || undefined;
      const parsedAttrs = parseVariantAttributesJson(attrsJson);
      const attrsLookCorrupt = Boolean(parsedAttrs.Color?.includes('/'));
      const variantAttributes =
        attrsLookCorrupt && prev?.variantAttributes
          ? prev.variantAttributes
          : attrsJson || prev?.variantAttributes || undefined;
      return {
        id: v.id || prev?.id,
        fourSellerSkuId: v.fourSellerSkuId ?? prev?.fourSellerSkuId,
        sellerSku: v.sellerSku || prev?.sellerSku || undefined,
        salePrice: v.salePrice,
        initialPrice: v.costPrice ?? prev?.initialPrice,
        originalPrice: v.originalPrice ?? v.salePrice,
        quantity: v.quantity,
        barcode: keep(v.barcode, prev?.barcode),
        title: v.specValue || formatVariantLabel(v.attrs ?? {}) || prev?.title || undefined,
        variantAttributes,
        mainImage,
        sortOrder: v.sortOrder ?? i,
        isDefault: v.isDefault,
      };
    }),
    images: upgradeHttpImageUrls(
      (values.images ?? [])
        .filter((img) => String(img.url ?? '').trim())
        .map((img, i) => ({
          mediaType: img.mediaType,
          url: upgradeHttpToHttpsUrl(img.url),
          sortOrder: img.sortOrder ?? i,
          listingSkuId:
            img.listingSkuId ||
            resolveListingSkuIdForColor(img.colorValue, variations, previous),
          tikTokImageId: img.tikTokImageId,
        })),
    ),
    attributes: (values.attributes ?? [])
      .filter((a) => !isPublishBlockedAttribute(a.attrName))
      // Drop empty Specifics (esp. empty Washing Instruction from older auto-map) —
      // TikTok rejects them as 【Unpublishable】Product attributes are unavailable.
      .filter(
        (a) =>
          isBrandAttribute(a) ||
          String(a.attrValue ?? '').trim().length > 0 ||
          (a.values?.some((v) => String(v.valueName ?? '').trim()) ?? false),
      )
      .map((a) => ({
        attrId: a.attrId || a.attrName || crypto.randomUUID(),
        attrName: a.attrName,
        attrValue: a.attrValue,
        valueId: a.valueId,
        values: a.values?.length
          ? a.values
              .filter((v) => String(v.valueName ?? '').trim())
              .map((v) => ({
                valueId: v.valueId,
                valueName: v.valueName,
              }))
          : undefined,
        valueType: a.valueType,
        isRequired: a.isRequired,
      })),
    certifications: withDefaultCertifications(
      (values.certifications ?? []).map((c) => ({
        certType: c.certType,
        externalId: c.externalId,
        name: c.name,
        phone: c.phone,
        email: c.email,
        address: c.address,
        fileUrls: c.fileUrls,
      })),
    ),
  };
}
