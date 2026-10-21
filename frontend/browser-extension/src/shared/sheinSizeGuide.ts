/** Normalized Shein size guide — local-only, for size-chart image generation. */

export interface SheinSizeGuideMeasureHint {
  name: string;
  description: string;
  sort: number;
}

export interface SheinSizeGuideDimension {
  id: string;
  name: string;
  nameEn: string;
  sort: number;
}

export interface SheinSizeGuideRow {
  size: string;
  sizeId: string;
  usSize: string | null;
  measurements: Record<string, string>;
}

export interface SheinSizeGuideChartRow {
  size: string;
  sizeId: string;
  usSize: string | null;
  bust: string | null;
  waist: string | null;
  hip: string | null;
  length: string | null;
}

export interface SheinSizeGuide {
  goodsId: string;
  unit: 'inch' | 'cm';
  dimensions: SheinSizeGuideDimension[];
  rows: SheinSizeGuideRow[];
  chartRows: SheinSizeGuideChartRow[];
  productMeasureGuide: SheinSizeGuideMeasureHint[];
  bodyMeasureGuide: SheinSizeGuideMeasureHint[];
  measureNote?: string | null;
  productMeasureImageUrl?: string | null;
  bodyMeasureImageUrl?: string | null;
  capturedAt: string;
}

export const SHEIN_SIZE_GUIDE_STORAGE_KEY = 'kbt_shein_size_guide_cache';
export const SHEIN_SIZE_GUIDE_MSG = 'kbt-shein-size-guide-capture';
export const SHEIN_SIZE_GUIDE_FETCH_MSG = 'kbt-shein-size-guide-fetch';
export const SHEIN_SIZE_GUIDE_FETCH_RESULT_MSG = 'kbt-shein-size-guide-fetch-result';

const SIZE_GUIDE_API_PATH = '/bff-api/products-api/get_size_guide_info';

interface BodySizeInch {
  bust: string | null;
  waist: string | null;
  hip: string | null;
}

function trimText(v: unknown): string | null {
  if (typeof v === 'number' && Number.isFinite(v)) return String(v);
  if (typeof v !== 'string') return null;
  const t = v.trim();
  return t || null;
}

function toSort(v: unknown): number {
  if (typeof v === 'number' && Number.isFinite(v)) return Math.floor(v);
  if (typeof v === 'string' && v.trim()) {
    const n = parseInt(v, 10);
    if (Number.isFinite(n)) return n;
  }
  return 0;
}

function toAbsoluteUrl(v: unknown): string | null {
  const t = trimText(v);
  if (!t) return null;
  if (t.startsWith('//')) return `https:${t}`;
  return t;
}

function parseMeasureHints(
  list: unknown,
  descriptionKey: 'description' | 'desc',
): SheinSizeGuideMeasureHint[] {
  if (!Array.isArray(list)) return [];
  const out: SheinSizeGuideMeasureHint[] = [];
  for (const item of list) {
    if (!item || typeof item !== 'object') continue;
    const row = item as Record<string, unknown>;
    const name = trimText(row.name);
    const description = trimText(row[descriptionKey]);
    if (!name && !description) continue;
    out.push({
      name: name ?? '',
      description: description ?? '',
      sort: toSort(row.sort),
    });
  }
  out.sort((a, b) => a.sort - b.sort || a.name.localeCompare(b.name));
  return out;
}

function parseDimensions(sizeInfo: Record<string, unknown> | null): SheinSizeGuideDimension[] {
  const raw = sizeInfo?.dimensionAttrInfo;
  if (!Array.isArray(raw)) return [];
  const out: SheinSizeGuideDimension[] = [];
  for (const item of raw) {
    if (!item || typeof item !== 'object') continue;
    const row = item as Record<string, unknown>;
    const name = trimText(row.attr_name)?.replace(/\s+$/, '') ?? trimText(row.attr_name_en);
    const nameEn = trimText(row.attr_name_en) ?? name;
    if (!name) continue;
    out.push({
      id: trimText(row.attr_id) ?? '',
      name,
      nameEn: nameEn ?? name,
      sort: toSort(row.sort),
    });
  }
  out.sort((a, b) => a.sort - b.sort || a.name.localeCompare(b.name));
  return out;
}

function buildUsSizeMap(info: Record<string, unknown>): Map<string, string> {
  const map = new Map<string, string>();
  const mlsa = info.multiLevelSaleAttribute;
  if (!mlsa || typeof mlsa !== 'object') return map;
  const attrs = (mlsa as Record<string, unknown>).skc_sale_attr;
  if (!Array.isArray(attrs)) return map;

  for (const attr of attrs) {
    if (!attr || typeof attr !== 'object') continue;
    const attrRow = attr as Record<string, unknown>;
    if (String(attrRow.isSize ?? '') !== '1') continue;
    const list = attrRow.attr_value_list;
    if (!Array.isArray(list)) continue;

    for (const item of list) {
      if (!item || typeof item !== 'object') continue;
      const sizeRow = item as Record<string, unknown>;
      const sizeName = trimText(sizeRow.attr_value_name);
      if (!sizeName) continue;

      let usSize: string | null = null;
      if (trimText(sizeRow.country_code) === 'US') {
        usSize = trimText(sizeRow.attr_local_size_value);
      }
      const localSizes = sizeRow.attr_value_local_size;
      if (!usSize && Array.isArray(localSizes)) {
        for (const loc of localSizes) {
          if (!loc || typeof loc !== 'object') continue;
          const locRow = loc as Record<string, unknown>;
          if (trimText(locRow.country_code) === 'US') {
            usSize = trimText(locRow.attr_local_size_value);
            break;
          }
        }
      }
      if (!usSize) usSize = trimText(sizeRow.attr_local_size_value);
      if (usSize) map.set(sizeName, usSize);
    }
  }
  return map;
}

function lookupMeasurement(row: Record<string, unknown>, dim: SheinSizeGuideDimension): string | null {
  const candidates = [dim.name, `${dim.name} `, dim.nameEn, `${dim.nameEn} `];
  for (const key of candidates) {
    const val = trimText(row[key]);
    if (val) return val;
  }
  return null;
}

function parseRowsFromArray(
  raw: unknown,
  dimensions: SheinSizeGuideDimension[],
  usSizeMap: Map<string, string>,
): SheinSizeGuideRow[] {
  if (!Array.isArray(raw)) return [];
  const out: SheinSizeGuideRow[] = [];

  for (const item of raw) {
    if (!item || typeof item !== 'object') continue;
    const row = item as Record<string, unknown>;
    const size = trimText(row.attr_value_name) ?? trimText(row.attr_value_name_en);
    if (!size) continue;

    const measurements: Record<string, string> = {};
    for (const dim of dimensions) {
      const val = lookupMeasurement(row, dim);
      if (val) measurements[dim.name] = val;
    }

    out.push({
      size,
      sizeId: trimText(row.attr_value_id) ?? '',
      usSize: usSizeMap.get(size) ?? null,
      measurements,
    });
  }
  return out;
}

/** Strip inch units; keep numeric ranges like `30.3-36.2`. */
export function normalizeInchValue(raw: string | null | undefined): string | null {
  const t = trimText(raw);
  if (!t) return null;
  let s = t.replace(/\s*(in|inch|inches)\b/gi, '').trim();
  s = s.replace(/\s+/g, ' ');
  return s || null;
}

/** Dual length `24.8/13.6 in` → `24.8-13.6` (both segments as fit range). */
export function normalizeLengthDual(raw: string | null | undefined): string | null {
  const t = normalizeInchValue(raw);
  if (!t) return null;
  if (!t.includes('/')) return t;

  const parts = t.split('/').map((p) => p.trim()).filter(Boolean);
  if (parts.length < 2) return t;

  const nums: number[] = [];
  for (const part of parts) {
    const n = parseFloat(part.replace(/[^\d.+-]/g, ''));
    if (Number.isFinite(n)) nums.push(n);
  }
  if (nums.length >= 2) {
    const lo = Math.min(...nums);
    const hi = Math.max(...nums);
    return `${lo}-${hi}`;
  }
  return t;
}

function mapMeasurementColumn(dimName: string): 'bust' | 'waist' | 'hip' | 'length' | null {
  const n = dimName.trim().toLowerCase();
  if (
    n.includes('sleeve') ||
    n.includes('shoulder') ||
    n.includes('inseam') ||
    n.includes('armhole')
  ) {
    return null;
  }
  if (n.includes('bust') || n.includes('chest')) return 'bust';
  if (n.includes('waist')) return 'waist';
  if (n.includes('hip')) return 'hip';
  if (n.includes('length')) return 'length';
  return null;
}

function parseBodySizeInchMap(basicAttribute: Record<string, unknown> | null): Map<string, BodySizeInch> {
  const map = new Map<string, BodySizeInch>();
  const raw = basicAttribute?.base_code_info_inch;
  if (!Array.isArray(raw)) return map;

  for (const item of raw) {
    if (!item || typeof item !== 'object') continue;
    const row = item as Record<string, unknown>;
    const size = trimText(row.size);
    if (!size) continue;
    map.set(size, {
      bust: normalizeInchValue(trimText(row.Bust)),
      waist: normalizeInchValue(trimText(row['Waist Size'])),
      hip: normalizeInchValue(trimText(row['Hip Size'])),
    });
  }
  return map;
}

/** Build prompt-ready chart rows from inch product rows + body hip chart. */
export function buildChartRows(
  rows: SheinSizeGuideRow[],
  dimensions: SheinSizeGuideDimension[],
  bodyMap: Map<string, BodySizeInch>,
  unit: 'inch' | 'cm',
): SheinSizeGuideChartRow[] {
  return rows.map((row) => {
    const chart: SheinSizeGuideChartRow = {
      size: row.size,
      sizeId: row.sizeId,
      usSize: row.usSize,
      bust: null,
      waist: null,
      hip: null,
      length: null,
    };

    for (const dim of dimensions) {
      const raw = row.measurements[dim.name];
      if (!raw) continue;
      const col = mapMeasurementColumn(dim.name);
      if (col === 'bust') {
        chart.bust = unit === 'inch' ? normalizeInchValue(raw) : raw;
      } else if (col === 'waist') {
        chart.waist = unit === 'inch' ? normalizeInchValue(raw) : raw;
      } else if (col === 'hip') {
        chart.hip = unit === 'inch' ? normalizeInchValue(raw) : raw;
      } else if (col === 'length') {
        chart.length = unit === 'inch' ? normalizeLengthDual(raw) : raw;
      }
    }

    const body = bodyMap.get(row.size);
    if (body?.hip && !chart.hip) {
      chart.hip = body.hip;
    }

    return chart;
  });
}

function parseMeasureNote(info: Record<string, unknown>): string | null {
  const tips = info.sizeGuideLanguageTips;
  if (!tips || typeof tips !== 'object') return null;
  return trimText((tips as Record<string, unknown>).manualMeasurementNote);
}

/** Parse Shein `get_size_guide_info` JSON into normalized size guide. */
export function parseSheinSizeGuideResponse(
  json: unknown,
  goodsId?: string | null,
): SheinSizeGuide | null {
  if (!json || typeof json !== 'object') return null;
  const root = json as Record<string, unknown>;
  if (String(root.code ?? '') !== '0') return null;

  const info = root.info;
  if (!info || typeof info !== 'object') return null;
  const infoObj = info as Record<string, unknown>;

  const sizeInfo =
    infoObj.sizeInfo && typeof infoObj.sizeInfo === 'object'
      ? (infoObj.sizeInfo as Record<string, unknown>)
      : null;
  const sizeTemplate =
    infoObj.sizeTemplate && typeof infoObj.sizeTemplate === 'object'
      ? (infoObj.sizeTemplate as Record<string, unknown>)
      : null;
  const basicAttribute =
    sizeInfo?.basicAttribute && typeof sizeInfo.basicAttribute === 'object'
      ? (sizeInfo.basicAttribute as Record<string, unknown>)
      : null;

  const dimensions = parseDimensions(sizeInfo);
  const usSizeMap = buildUsSizeMap(infoObj);
  const bodyMap = parseBodySizeInchMap(basicAttribute);

  const inchRaw = sizeInfo?.sizeInfoInch;
  const hasInch = Array.isArray(inchRaw) && inchRaw.length > 0;
  const unit: 'inch' | 'cm' = hasInch ? 'inch' : 'cm';
  const rows = hasInch
    ? parseRowsFromArray(inchRaw, dimensions, usSizeMap)
    : parseRowsFromArray(sizeInfo?.sizeInfo, dimensions, usSizeMap);

  const chartRows =
    unit === 'inch' && rows.length > 0
      ? buildChartRows(rows, dimensions, bodyMap, 'inch')
      : [];

  const productMeasureGuide = parseMeasureHints(sizeTemplate?.description_multi, 'description');
  const bodyMeasureGuide = parseMeasureHints(basicAttribute?.attribute_info, 'desc');
  const measureNote = parseMeasureNote(infoObj);

  const productMeasureImageUrl = toAbsoluteUrl(sizeTemplate?.image_url);
  const bodyMeasureImageUrl = toAbsoluteUrl(basicAttribute?.image_url);

  if (
    chartRows.length === 0 &&
    rows.length === 0 &&
    productMeasureGuide.length === 0 &&
    bodyMeasureGuide.length === 0
  ) {
    return null;
  }

  const gid = trimText(goodsId) ?? '';
  return {
    goodsId: gid,
    unit,
    dimensions,
    rows,
    chartRows,
    productMeasureGuide,
    bodyMeasureGuide,
    measureNote,
    productMeasureImageUrl,
    bodyMeasureImageUrl,
    capturedAt: new Date().toISOString(),
  };
}

/** Build size-guide API URL for the current Shein origin. */
export function buildSheinSizeGuideApiUrl(origin: string, goodsId: string): string {
  const base = origin.replace(/\/$/, '');
  const params = new URLSearchParams({
    _ver: '1.1.8',
    _lang: 'en',
    goodsId: String(goodsId),
  });
  return `${base}${SIZE_GUIDE_API_PATH}?${params.toString()}`;
}

/** True when object looks like normalized size guide from cache. */
export function isSheinSizeGuide(value: unknown): value is SheinSizeGuide {
  if (!value || typeof value !== 'object') return false;
  const o = value as Record<string, unknown>;
  const hasChart = Array.isArray(o.chartRows) && o.chartRows.length > 0;
  const hasRows = Array.isArray(o.rows) && o.rows.length > 0;
  return typeof o.goodsId === 'string' && (hasChart || hasRows);
}
