import { sheinSearchSortQuery } from './sheinSearchSort';

function encodeSheinQuery(pairs: Array<[string, string]>): string {
  return pairs
    .filter(([, value]) => value.length > 0)
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    .join('&');
}

/**
 * Tham số **không bao giờ** được tự sinh: SHEIN đối chiếu chúng với log page-view của
 * chính phiên đang mở, nên URL do mình dựng luôn khai sai và ăn `/risk/action/limit`.
 * - `src_tab_page_id=page_search{ts}`: id lượt xem list, phải do SPA phát ra trước đó.
 * - `src_identifier=st=…\`sc=…\`sr=…\`ps=…`: vị trí click trong list đó.
 * Kiểm chứng: dán URL kèm 2 tham số này vào thanh địa chỉ → risk ngay; xóa đi → vào bình thường.
 * `ici` / `src_module` / `source` giữ lại vì không mang id hay timestamp nào để đối chiếu.
 */
export const SHEIN_SESSION_SCOPED_PARAMS = [
  'src_identifier',
  'src_identifier_pre_search',
  'src_tab_page_id',
] as const;

/**
 * Builds a us.shein.com pdsearch URL shaped like an organic RecentSearch /
 * historyWord click. Omits sort/price/attr/QuickShip — those trip SHEIN
 * `/risk/action/limit` — cùng mọi tham số ở `SHEIN_SESSION_SCOPED_PARAMS`.
 * Keep in sync with WhatToSellTodayService.BuildSheinSearchUrl.
 */
export function buildSheinSearchUrl(keywords: string): string {
  const kw = keywords.trim();
  if (!kw) return '';
  const encoded = encodeURIComponent(kw);
  // SHEIN uses literal backticks as separators in `ici`; only the keyword is %-encoded.
  const ici = `s1\`RecentSearch\`${encoded}\`_fb\`d0\`PageOthers`;
  return (
    `https://us.shein.com/pdsearch/${encoded}/` +
    `?ici=${ici}` +
    `&search_source=1` +
    `&search_type=all` +
    `&source=historyWord` +
    `&src_module=search`
  );
}

export interface SheinProductSearchParams {
  /** Câu đang tìm (`searchQuery`) — pdsearch và BFF phải cùng chuỗi. */
  keyword: string;
  page?: number;
}

/**
 * Bộ lọc được phép gắn lên pdsearch / BFF `get_products_by_keywords`.
 * Chỉ khi có giá trị thật. Body/size đã gộp vào `keyword` (prefix), không phải query param.
 * Không gồm rating, sold, Local, Club, variants.
 */
export interface SheinUrlSearchFilters {
  priceMinUsd?: number | null;
  priceMaxUsd?: number | null;
  attrIds?: string[];
  attrValues?: string[];
  preferQuickShip?: boolean;
}

export interface SheinFilteredSearchParams extends SheinProductSearchParams, SheinUrlSearchFilters {
  /** Sort by — gắn `sort`/`sourceStatus`/`source=sort` khi không Recommend. */
  sheinSort?: string | null;
}

export function roundSheinMinPrice(n: number): number {
  return Math.max(0, Math.floor(n));
}

export function roundSheinMaxPrice(n: number): number {
  return Math.max(0, Math.ceil(n));
}

/**
 * pdsearch EditSearch/`insideFilter` (hoặc `source=sort` khi Sort by ≠ Recommend).
 * Path/`ici` đều là câu đang tìm (`searchQuery`, có thể đã prefix body/size) — trùng
 * `keywords` mà BFF nhận, nên response SPA hớt được dùng lại.
 * Chỉ gắn filter khi có giá trị thật:
 * không Style → không `attr_*`; không giá → không `min_price`/`max_price`/`price_input`;
 * không QuickShip → không `tag_ids`; Recommend → không `sort`. Không `cloudTag` / `RecentSearch`.
 * Luôn có `page` (kể cả `page=1`) và `sourceStatus` — bám đúng URL Shein thật
 * để BFF nhận cùng bộ param. Không gắn `child_cat_id`.
 * **Không** `src_identifier` / `src_tab_page_id` — xem `SHEIN_SESSION_SCOPED_PARAMS`.
 */
export function buildSheinFilteredSearchUrl(params: SheinFilteredSearchParams): string {
  const kw = params.keyword.trim();
  if (!kw) return '';
  const page = Math.max(1, Math.floor(params.page ?? 1));
  const attrIdsRaw = (params.attrIds ?? []).map((id) => String(id || '').trim()).filter(Boolean);
  const attrValuesRaw = (params.attrValues ?? []).map((v) => String(v || '').trim()).filter(Boolean);
  const attrCount = Math.min(attrIdsRaw.length, attrValuesRaw.length);
  const attrIds = attrIdsRaw.slice(0, attrCount);
  const attrValues = attrValuesRaw.slice(0, attrCount);
  const hasAttrs = attrCount > 0;
  const minRaw = Number(params.priceMinUsd);
  const maxRaw = Number(params.priceMaxUsd);
  const hasMin = Number.isFinite(minRaw) && minRaw > 0;
  const hasMax = Number.isFinite(maxRaw) && maxRaw > 0;
  const attrIdJoined = attrIds.join('-');
  const sortQuery = sheinSearchSortQuery(params.sheinSort);

  const pairs: Array<[string, string]> = [];
  if (hasAttrs) pairs.push(['attr_values', attrValues.join('-')]);
  pairs.push(['ici', `s1\`EditSearch\`${kw}\`_fb\`d0\`PageSearchResult`]);
  pairs.push(['search_source', '1']);
  pairs.push(['search_type', 'all']);
  pairs.push(['source', sortQuery.source]);
  pairs.push(['src_module', 'search']);
  if (hasMin) pairs.push(['min_price', String(roundSheinMinPrice(minRaw))]);
  if (hasMax) pairs.push(['max_price', String(roundSheinMaxPrice(maxRaw))]);
  if (hasMin || hasMax) pairs.push(['price_input', 'slide']);
  pairs.push(['sourceStatus', sortQuery.sourceStatus]);
  if (sortQuery.sort) pairs.push(['sort', sortQuery.sort]);
  pairs.push(['page', String(page)]);
  pairs.push(['force_suggest', '1']);
  if (hasAttrs) {
    pairs.push(['attr_ids', attrIdJoined]);
    pairs.push(['attr_node_ids', attrIdJoined]);
  }
  if (params.preferQuickShip) {
    pairs.push(['tag_ids', 'quickship']);
    pairs.push(['tag_type', '12']);
  }

  return `https://us.shein.com/pdsearch/${encodeURIComponent(kw)}/?${encodeSheinQuery(pairs)}`;
}

const WARMUP_FILTER_KEYS = [
  'tag_ids',
  'tag_type',
  'quickship',
  'min_price',
  'max_price',
  'attr_ids',
  'attr_values',
  'attr_node_ids',
  'sort',
  'sourceStatus',
  'source',
  // `page` phải nằm trong khóa: warmup lại sang trang N cùng bộ lọc mà thiếu nó sẽ bị
  // hiểu là "cùng URL" rồi chỉ reload trang cũ, không bao giờ tới trang N.
  'page',
] as const;

function sheinUrlFilterKey(url: string | null | undefined, keys: readonly string[]): string {
  if (!url) return '';
  try {
    const parsed = new URL(url);
    const path = decodeURI(parsed.pathname).replace(/\/+$/, '').toLowerCase();
    const filters = keys.map((key) => `${key}=${parsed.searchParams.get(key) ?? ''}`).join('&');
    return `${parsed.origin}|${path}|${filters}`;
  } catch {
    return String(url);
  }
}

/**
 * Path + bộ lọc URL (QS/giá/style/trang) — chỉ những param đổi nội dung list.
 * Dùng để biết có được `tabs.reload` không — reload khi chỉ path khớp sẽ giữ query cũ.
 */
export function sheinWarmupFilterKey(url?: string | null): string {
  return sheinUrlFilterKey(url, WARMUP_FILTER_KEYS);
}

const POOL_CACHE_FILTER_KEYS = WARMUP_FILTER_KEYS.filter((key) => key !== 'page');

/**
 * Khóa cache pool: giống `sheinWarmupFilterKey` nhưng **bỏ `page`** — mọi trang của cùng
 * câu tìm + cùng bộ lọc URL thuộc về một pool. Lọc rating/sold/Local/variants không nằm
 * trong khóa vì chúng áp sau JSON nên pool cũ vẫn dùng lại được.
 */
export function sheinSearchPoolKey(url?: string | null): string {
  return sheinUrlFilterKey(url, POOL_CACHE_FILTER_KEYS);
}

export function isSheinRiskUrl(url?: string | null): boolean {
  if (!url) return false;
  try {
    const parsed = new URL(url);
    return /\/risk\//i.test(parsed.pathname);
  } catch {
    return /\/risk\/action\/limit/i.test(url);
  }
}

export function isSheinPdsearchUrl(url?: string | null): boolean {
  if (!url) return false;
  try {
    return /\/pdsearch\//i.test(new URL(url).pathname);
  } catch {
    return /\/pdsearch\//i.test(url);
  }
}

export function isSheinHttpUrl(url?: string | null): boolean {
  if (!url) return false;
  try {
    const host = new URL(url).hostname.toLowerCase();
    return host === 'shein.com' || host.endsWith('.shein.com');
  } catch {
    return false;
  }
}

export interface SheinPdpUrlOpts {
  keyword?: string | null;
}

/**
 * PDP giống click card từ pdsearch: slug từ `goods_url_name` (không slugify name)
 * + query list (`mallCode` / `pageListType=4` / `detailBusinessFrom=0-2` / `imgRatio`).
 * Có keyword thì thêm `src_module=search`; **không** `src_identifier` / `src_tab_page_id`
 * (xem `SHEIN_SESSION_SCOPED_PARAMS`) — PDP mở bằng tab mới cũng không có lượt xem list
 * nào để Shein đối chiếu, khai vào là ăn risk y như pdsearch.
 */
export function buildSheinPdpUrl(
  goodsId: string,
  goodsUrlName?: string | null,
  opts?: SheinPdpUrlOpts,
): string {
  const id = String(goodsId || '').trim();
  if (!id) return '';
  const slug = (goodsUrlName || '').trim().replace(/^\/+|\/+$/g, '');
  const path = `https://us.shein.com/${slug}-p-${id}.html`;
  const parts: string[] = [];
  if ((opts?.keyword || '').trim()) parts.push('src_module=search');
  parts.push('mallCode=1');
  parts.push('pageListType=4');
  parts.push('detailBusinessFrom=0-2');
  parts.push('imgRatio=3-4');
  return `${path}?${parts.join('&')}`;
}
