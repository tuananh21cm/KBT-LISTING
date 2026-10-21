import type { ListingVariationRow } from './productFormTypes';
import type { VariationSpecKey } from './listingFormUtils';

const ALPHANUM = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';

export type SkuRuleSegment =
  | 'none'
  | 'externalProductId'
  | 'variantAttributes'
  | 'randomCode'
  | 'custom'
  | 'index';

export type SkuBatchMode = 'rules' | 'random';

export interface SkuBatchRuleConfig {
  prefix: SkuRuleSegment;
  prefixCustom?: string;
  middle: SkuRuleSegment;
  middleCustom?: string;
  suffix: SkuRuleSegment;
  suffixCustom?: string;
}

export const SKU_RULE_SEGMENT_OPTIONS: { value: SkuRuleSegment; label: string }[] = [
  { value: 'none', label: 'Không điền' },
  { value: 'externalProductId', label: 'External Product ID' },
  { value: 'variantAttributes', label: 'Thuộc tính variant' },
  { value: 'randomCode', label: 'Mã ngẫu nhiên' },
  { value: 'index', label: 'STT variant' },
  { value: 'custom', label: 'Tùy chỉnh' },
];

export const DEFAULT_SKU_RULE_CONFIG: SkuBatchRuleConfig = {
  prefix: 'none',
  middle: 'variantAttributes',
  suffix: 'none',
};

export function generateRandomAlphanumeric(length: number): string {
  const bytes = crypto.getRandomValues(new Uint8Array(length));
  return Array.from(bytes, (b) => ALPHANUM[b % ALPHANUM.length]).join('');
}

/** 9-digit numeric prefix (4seller style), from external product id or random. */
export function normalizeNumericSkuPrefix(externalProductId?: string): string {
  const digits = (externalProductId ?? '').replace(/\D/g, '');
  if (digits.length >= 9) return digits.slice(0, 9);
  if (digits.length > 0) return digits.padStart(9, '0');
  return String(Math.floor(100000000 + Math.random() * 900000000));
}

/** Format like 4seller: `102918554-I9chw29tlxsv` */
export function generateFourSellerStyleSku(numericPrefix: string): string {
  return `${numericPrefix}-${generateRandomAlphanumeric(12)}`;
}

/** Color-Size style label for SKU rules (4seller uses hyphen). */
export function buildVariantAttributeSku(row: ListingVariationRow): string {
  const attrs = row.attrs ?? {};
  const order: VariationSpecKey[] = ['Color', 'Size', 'Number of Items'];
  const parts = order
    .filter((k) => attrs[k])
    .map((k) => (attrs[k] ?? '').replace(/\s+/g, ''));
  if (parts.length > 0) return parts.join('-');
  return (row.specValue || 'Default').replace(/\//g, '-').replace(/\s+/g, '');
}

function resolveRuleSegment(
  segment: SkuRuleSegment,
  custom: string | undefined,
  row: ListingVariationRow,
  index: number,
  externalProductId: string | undefined,
  _numericPrefix: string,
): string | null {
  switch (segment) {
    case 'none':
      return null;
    case 'externalProductId':
      return externalProductId?.trim() || null;
    case 'variantAttributes':
      return buildVariantAttributeSku(row);
    case 'randomCode':
      return generateRandomAlphanumeric(12);
    case 'custom':
      return custom?.trim() || null;
    case 'index':
      return String(index + 1);
    default:
      return null;
  }
}

export function buildSkuByRules(
  row: ListingVariationRow,
  index: number,
  config: SkuBatchRuleConfig,
  externalProductId?: string,
  numericPrefix?: string,
): string {
  const prefix = numericPrefix ?? normalizeNumericSkuPrefix(externalProductId);
  const segments = [
    resolveRuleSegment(config.prefix, config.prefixCustom, row, index, externalProductId, prefix),
    resolveRuleSegment(config.middle, config.middleCustom, row, index, externalProductId, prefix),
    resolveRuleSegment(config.suffix, config.suffixCustom, row, index, externalProductId, prefix),
  ].filter((s): s is string => !!s);

  if (segments.length === 0) {
    return generateFourSellerStyleSku(prefix);
  }

  return segments.join('-');
}

export function generateBatchSkus(
  rows: ListingVariationRow[],
  mode: SkuBatchMode,
  config: SkuBatchRuleConfig,
  externalProductId?: string,
): string[] {
  const numericPrefix = normalizeNumericSkuPrefix(externalProductId);
  const used = new Set<string>();

  return rows.map((row, index) => {
    let sku =
      mode === 'random'
        ? generateFourSellerStyleSku(numericPrefix)
        : buildSkuByRules(row, index, config, externalProductId, numericPrefix);

    let attempt = 0;
    while (used.has(sku) && attempt < 100) {
      const extra = mode === 'random' ? generateRandomAlphanumeric(4) : String(attempt + 2);
      sku = `${sku}-${extra}`;
      attempt++;
    }
    used.add(sku);
    return sku;
  });
}
