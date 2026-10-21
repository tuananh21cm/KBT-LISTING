/**
 * SHEIN Style filter ids (attr group 101) — danh sách chuẩn dùng chung shop UI + extension.
 * Lấy từ URL pdsearch khi tick sidebar Style (`attr_ids` khớp `attr_values` theo vị trí).
 * Code mới vẫn học thêm được từ facet (`learnStyleAttrsFromFacets`).
 */

export interface SheinStyleAttr {
  /** Shop catalog style code (casual, vintage, …). */
  code: string;
  /** SHEIN attr_ids token, e.g. 101_167. */
  attrId: string;
  /** SHEIN attr_values token, e.g. Casual. */
  attrValue: string;
}

export const SHEIN_STYLE_ATTR_SEED: readonly SheinStyleAttr[] = [
  { code: 'elegant', attrId: '101_257', attrValue: 'Elegant' },
  { code: 'casual', attrId: '101_167', attrValue: 'Casual' },
  { code: 'vintage', attrId: '101_730', attrValue: 'Vintage' },
  { code: 'sexy', attrId: '101_580', attrValue: 'Sexy' },
  { code: 'boho', attrId: '101_725', attrValue: 'Boho' },
  { code: 'cute', attrId: '101_231', attrValue: 'Cute' },
  { code: 'glamorous', attrId: '101_493', attrValue: 'Glamorous' },
  { code: 'vacation', attrId: '101_2489', attrValue: 'Vacation' },
  { code: 'modest', attrId: '101_1547', attrValue: 'Modest' },
  { code: 'party', attrId: '101_2491', attrValue: 'Party' },
];

/** Nhãn chip extension — cùng code/label với `STYLE_OPTIONS` trên shop UI. */
export const STYLE_OPTION_LABELS: Record<string, string> = {
  elegant: 'Elegant',
  casual: 'Casual',
  vintage: 'Vintage',
  sexy: 'Sexy',
  boho: 'Boho',
  cute: 'Cute',
  glamorous: 'Glamorous',
  vacation: 'Vacation',
  modest: 'Modest',
  party: 'Party',
};

/** Lựa chọn Body/size trên Search — Regular/Midsize không prefix, coi như không chọn. */
export const BODY_SIZE_OPTIONS: readonly { code: string; label: string }[] = [
  { code: 'plus', label: 'Plus Size' },
  { code: 'curvy', label: 'Curve' },
  { code: 'petite', label: 'Petite' },
  { code: 'tall', label: 'Tall' },
];

/** Code shop còn trong 4 option Search; `regular`/`midsize`/lạ → không chọn. */
export function normalizeSearchBodySizeCode(raw?: string | null): string | null {
  const code = String(raw ?? '')
    .trim()
    .toLowerCase();
  if (!code) return null;
  return BODY_SIZE_OPTIONS.some((o) => o.code === code) ? code : null;
}

export const STYLE_CHIP_OPTIONS: readonly { code: string; label: string }[] = Object.entries(
  STYLE_OPTION_LABELS,
).map(([code, label]) => ({ code, label }));

function norm(value: string): string {
  return value.trim().toLowerCase().replace(/-/g, ' ').replace(/\s+/g, ' ');
}

/** Canonical shop style code — trim/lower + whitelist chip. */
export function normalizeStyleCode(raw?: string | null): string | null {
  const code = String(raw ?? '')
    .trim()
    .toLowerCase();
  if (!code || !(code in STYLE_OPTION_LABELS)) return null;
  return code;
}

/** Dedupe + whitelist — chip UI và URL attr_* dùng cùng nguồn. */
export function normalizeStyleCodes(codes: string[] | null | undefined): string[] {
  const out: string[] = [];
  const seen = new Set<string>();
  for (const raw of codes ?? []) {
    const code = normalizeStyleCode(raw);
    if (!code || seen.has(code)) continue;
    seen.add(code);
    out.push(code);
  }
  return out;
}

export function mergeStyleAttrMaps(
  cached: SheinStyleAttr[] | null | undefined,
): Map<string, SheinStyleAttr> {
  const map = new Map<string, SheinStyleAttr>();
  for (const row of SHEIN_STYLE_ATTR_SEED) {
    map.set(row.code, row);
  }
  if (Array.isArray(cached)) {
    for (const row of cached) {
      const code = String(row?.code ?? '').trim().toLowerCase();
      const attrId = String(row?.attrId ?? '').trim();
      const attrValue = String(row?.attrValue ?? '').trim();
      if (!code || !attrId || !attrValue) continue;
      map.set(code, { code, attrId, attrValue });
    }
  }
  return map;
}

export function resolveStyleAttrs(
  styleCodes: string[] | null | undefined,
  cached?: SheinStyleAttr[] | null,
): { attrIds: string[]; attrValues: string[] } {
  const map = mergeStyleAttrMaps(cached);
  const attrIds: string[] = [];
  const attrValues: string[] = [];
  const seen = new Set<string>();
  for (const raw of normalizeStyleCodes(styleCodes)) {
    if (seen.has(raw)) continue;
    seen.add(raw);
    const hit = map.get(raw);
    if (!hit) continue;
    attrIds.push(hit.attrId);
    attrValues.push(hit.attrValue);
  }
  return { attrIds, attrValues };
}

/** Match SHEIN filter facets (attr_id + attr_value) onto catalog style codes. */
export function learnStyleAttrsFromFacets(
  facets: Array<{ attrId?: string | null; attrValue?: string | null }>,
  cached?: SheinStyleAttr[] | null,
): SheinStyleAttr[] {
  const map = mergeStyleAttrMaps(cached);
  const labelToCode = new Map<string, string>();
  for (const [code, label] of Object.entries(STYLE_OPTION_LABELS)) {
    labelToCode.set(norm(label), code);
    labelToCode.set(norm(code), code);
  }
  for (const facet of facets) {
    const attrId = String(facet.attrId ?? '').trim();
    const attrValue = String(facet.attrValue ?? '').trim();
    if (!attrId || !attrValue) continue;
    const code = labelToCode.get(norm(attrValue));
    if (!code) continue;
    map.set(code, { code, attrId, attrValue });
  }
  return [...map.values()];
}

export function collectAttrFacets(root: unknown): Array<{ attrId: string; attrValue: string }> {
  const out: Array<{ attrId: string; attrValue: string }> = [];
  const seen = new Set<string>();
  const walk = (node: unknown, depth: number) => {
    if (!node || depth > 8) return;
    if (Array.isArray(node)) {
      for (const item of node) walk(item, depth + 1);
      return;
    }
    if (typeof node !== 'object') return;
    const obj = node as Record<string, unknown>;
    const attrId = firstString(obj, [
      'attr_id',
      'attrId',
      'attr_value_id',
      'attrValueId',
      'id',
    ]);
    const attrValue = firstString(obj, [
      'attr_value',
      'attrValue',
      'attr_value_en',
      'attrValueEn',
      'name',
      'value',
      'label',
    ]);
    if (attrId && attrValue && /^\d+_\d+$/.test(attrId)) {
      const key = `${attrId}|${attrValue}`;
      if (!seen.has(key)) {
        seen.add(key);
        out.push({ attrId, attrValue });
      }
    }
    for (const child of Object.values(obj)) {
      if (child && typeof child === 'object') walk(child, depth + 1);
    }
  };
  walk(root, 0);
  return out;
}

function firstString(obj: Record<string, unknown>, keys: string[]): string | null {
  for (const key of keys) {
    const v = obj[key];
    if (typeof v === 'string' && v.trim()) return v.trim();
    if (typeof v === 'number' && Number.isFinite(v)) return String(v);
  }
  return null;
}
