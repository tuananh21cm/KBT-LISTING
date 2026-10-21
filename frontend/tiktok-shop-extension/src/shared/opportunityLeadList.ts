/**
 * Đọc payload `POST seller lead/list` (Product Opportunities / trending keywords).
 * TikTok đổi tên field và trả `null` thay vì `[]` khi filter không khớp — parser phải
 * tách khỏi content script để test được.
 *
 * Chỉ `src/content/index.ts` được import file này (cùng lý do với listingAnalyticsStats:
 * content.js classic, không được để Rollup tách chunk).
 */

export type LeadListPick = { found: boolean; list: unknown[] };

export type LeadListCategoryInfo = {
  cate_id: number;
  cateId?: number;
  level: number;
};

const LIST_KEYS = [
  'data',
  'leads',
  'lead_list',
  'leadList',
  'LeadList',
  'lead_infos',
  'leadInfos',
  'lead_info_list',
  'leadInfoList',
  'keyword_list',
  'keywordList',
  'keywords',
  'list',
  'items',
  'records',
  'rows',
  'result',
  'results',
];

const TOTAL_KEYS = [
  'total_product_count',
  'totalProductCount',
  'total_count',
  'totalCount',
  'total',
  'count',
];

const META_KEY = /^(total|page|has_more|next|pagination|list_control|cursor|count|size|code|status|success|ok|msg|message)/i;

function record(value: unknown): Record<string, any> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, any>)
    : null;
}

function unwrapJson(value: unknown, depth = 0): unknown {
  if (typeof value !== 'string' || depth > 2) return value;
  const t = value.trim();
  if (!t || (t[0] !== '{' && t[0] !== '[')) return value;
  try {
    return unwrapJson(JSON.parse(t), depth + 1);
  } catch {
    return value;
  }
}

function looksLikeLeadRow(value: unknown): boolean {
  const rec = record(value);
  if (!rec) return false;
  const inner = record(rec.lead);
  const id = rec.lead_id ?? rec.leadId ?? inner?.lead_id ?? inner?.leadId;
  if (id != null && String(id).trim()) return true;
  const name = rec.lead_name ?? rec.leadName ?? inner?.lead_name ?? inner?.leadName;
  return name != null && String(name).trim().length > 0;
}

function parseTotalNumber(raw: unknown): number {
  if (typeof raw === 'number' && Number.isFinite(raw)) return Math.max(0, Math.floor(raw));
  if (typeof raw !== 'string') return 0;
  const digits = raw.replace(/[^\d]/g, '');
  if (!digits) return 0;
  const n = Number(digits);
  return Number.isFinite(n) ? n : 0;
}

/**
 * Tìm mảng lead. `null` / key list hiện diện nhưng null = danh sách rỗng (found),
 * không phải lỗi schema — TikTok hay trả vậy khi category/tag không có keyword.
 */
export function pickLeadList(data: unknown, depth = 0): LeadListPick {
  const value = unwrapJson(data);
  if (value == null) return { found: true, list: [] };
  if (Array.isArray(value)) {
    if (value.length === 0 || looksLikeLeadRow(value[0]) || record(value[0])) {
      return { found: true, list: value };
    }
    return { found: false, list: [] };
  }
  const rec = record(value);
  if (!rec || depth > 5) return { found: false, list: [] };

  for (const key of LIST_KEYS) {
    if (Array.isArray(rec[key])) return { found: true, list: rec[key] as unknown[] };
  }

  for (const key of LIST_KEYS) {
    const inner = rec[key];
    if (!inner || typeof inner !== 'object' || Array.isArray(inner)) continue;
    const nested = pickLeadList(inner, depth + 1);
    if (nested.found) return nested;
  }

  for (const key of LIST_KEYS) {
    if (Object.prototype.hasOwnProperty.call(rec, key) && rec[key] == null) {
      return { found: true, list: [] };
    }
  }

  if (depth < 4) {
    for (const inner of Object.values(rec)) {
      if (Array.isArray(inner) && inner.length > 0 && looksLikeLeadRow(inner[0])) {
        return { found: true, list: inner };
      }
      if (inner && typeof inner === 'object' && !Array.isArray(inner)) {
        const nested = pickLeadList(inner, depth + 1);
        if (nested.found && nested.list.length > 0) return nested;
      }
    }
  }

  return { found: false, list: [] };
}

export function pickLeadTotal(envelope: Record<string, any> | null, data: unknown): number {
  const dataRec = record(unwrapJson(data));
  const pagination =
    record(dataRec?.pagination) ??
    record(dataRec?.page_info) ??
    record(dataRec?.pageInfo) ??
    record(dataRec?.list_control) ??
    record(dataRec?.listControl);
  const sources = [envelope, dataRec, pagination];
  for (const src of sources) {
    if (!src) continue;
    for (const key of TOTAL_KEYS) {
      const n = parseTotalNumber(src[key]);
      if (n > 0) return n;
    }
  }
  return 0;
}

/** Mô tả shape `data` để hiện trong toast khi không parse được. */
export function describeLeadListShape(data: unknown): string {
  const value = unwrapJson(data);
  if (value == null) return 'data=null';
  if (Array.isArray(value)) return `data[] len=${value.length}`;
  if (typeof value !== 'object') return `data:${typeof value}`;
  const rec = record(value);
  if (!rec) return 'data=?';
  const keys = Object.keys(rec).slice(0, 12);
  if (keys.length === 0) return 'data={}';
  return keys
    .map((key) => {
      const v = rec[key];
      if (v == null) return `${key}=null`;
      if (Array.isArray(v)) return `${key}[]`;
      if (typeof v === 'object') return `${key}{}`;
      return `${key}:${typeof v}`;
    })
    .join(', ');
}

/**
 * Payload không chứa mảng (null / {} / chỉ meta + list null) — coi là 0 keyword của group,
 * không abort cả sync.
 */
export function isEmptyLeadPayload(data: unknown): boolean {
  const value = unwrapJson(data);
  if (value == null) return true;
  if (Array.isArray(value)) return value.length === 0;
  const rec = record(value);
  if (!rec) return true;
  const keys = Object.keys(rec);
  if (keys.length === 0) return true;
  for (const [key, v] of Object.entries(rec)) {
    if (v == null || v === '') continue;
    if (Array.isArray(v)) {
      if (v.length === 0) continue;
      return false;
    }
    if (typeof v === 'object') {
      if (!isEmptyLeadPayload(v)) return false;
      continue;
    }
    if (typeof v === 'number' || typeof v === 'boolean') {
      if (META_KEY.test(key)) continue;
      return false;
    }
    if (typeof v === 'string' && /^(ok|success|true|false)$/i.test(v)) continue;
    if (typeof v === 'string' && /^(total|page|has_more|next|count|size|code|msg|message)$/i.test(key)) {
      continue;
    }
    return false;
  }
  return true;
}

/** Body scrape: không AND tag_code (UI lọc tag sau). Fallback tối giản khi schema/filter lạ. */
export function buildLeadListRequestBodies(
  categoryInfos: LeadListCategoryInfo[],
  page: number,
  pageSize: number,
): string[] {
  const infos = categoryInfos.map((c) => ({
    cate_id: c.cate_id,
    cateId: c.cateId ?? c.cate_id,
    level: c.level,
  }));
  return [
    JSON.stringify({
      opportunity_type: 2,
      tab_code_filter: ['trending_keywords'],
      use_like: false,
      category_infos: infos,
      page_number: page,
      page_size: pageSize,
      traffic_source: 'seller_organic',
    }),
    JSON.stringify({
      opportunity_type: 2,
      category_infos: infos,
      page_number: page,
      page_size: pageSize,
    }),
  ];
}
