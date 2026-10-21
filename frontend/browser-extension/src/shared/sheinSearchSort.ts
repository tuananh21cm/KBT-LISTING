/**
 * Shein Sort by trên pdsearch URL + BFF `get_products_by_keywords`.
 * Đối chiếu URL Shein thật cho cả 6 mức: `sourceStatus=1` **luôn luôn**;
 * Recommend = `source=insideFilter` không `sort`, các mức khác = `source=sort` + `sort`.
 */
export const SHEIN_SEARCH_SORT_DEFAULT = 'recommend' as const;

/** `param` = `sort` trên URL Shein thật (Recommend không có `sort`). */
export const SHEIN_SEARCH_SORT_OPTIONS = [
  { code: 'recommend', label: 'Recommend', param: null },
  { code: 'most_popular', label: 'Most Popular', param: '8' },
  { code: 'new_arrivals', label: 'New Arrivals', param: '9' },
  { code: 'top_rated', label: 'Top Rated', param: '7' },
  { code: 'price_low', label: 'Price Low to High', param: '10' },
  { code: 'price_high', label: 'Price High to Low', param: '11' },
] as const;

/** Shein gửi `sourceStatus=1` cho mọi mức Sort by, kể cả Recommend. */
export const SHEIN_SEARCH_SOURCE_STATUS = '1' as const;

export type SheinSearchSort = (typeof SHEIN_SEARCH_SORT_OPTIONS)[number]['code'];

const SORT_CODES = new Set<string>(SHEIN_SEARCH_SORT_OPTIONS.map((o) => o.code));

export function normalizeSheinSearchSort(raw: unknown): SheinSearchSort {
  const code = String(raw || '').trim();
  return SORT_CODES.has(code) ? (code as SheinSearchSort) : SHEIN_SEARCH_SORT_DEFAULT;
}

/** `sort` trên URL Shein; `null` = Recommend (bỏ hẳn param). */
export function sheinSearchSortParam(code?: string | null): string | null {
  const normalized = normalizeSheinSearchSort(code);
  const opt = SHEIN_SEARCH_SORT_OPTIONS.find((o) => o.code === normalized);
  return opt?.param ?? null;
}

export function sheinSearchSortLabel(code?: string | null): string {
  const normalized = normalizeSheinSearchSort(code);
  return SHEIN_SEARCH_SORT_OPTIONS.find((o) => o.code === normalized)?.label ?? 'Recommend';
}

/** Query Sort by cho pdsearch + BFF. Chỉ `source`/`sort` đổi theo mức, `sourceStatus` cố định. */
export function sheinSearchSortQuery(code?: string | null): {
  sort: string | null;
  source: 'sort' | 'insideFilter';
  sourceStatus: typeof SHEIN_SEARCH_SOURCE_STATUS;
} {
  const sort = sheinSearchSortParam(code);
  return {
    sort,
    source: sort ? 'sort' : 'insideFilter',
    sourceStatus: SHEIN_SEARCH_SOURCE_STATUS,
  };
}
