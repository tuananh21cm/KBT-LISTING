import { audienceModifier, normalizeAudienceKeyword } from './audienceSearchQuery';
import { collectAttrFacets, type SheinStyleAttr } from './sheinStyleAttrs';
import {
  passesCoverage,
  phraseHits,
  phraseTitleScore,
  resolvePhrases,
  titleTokens,
  type KeywordMatchPhrase,
} from './keywordMatchPhrases';

export const DEFAULT_SEARCH_LIMIT = 30;
/**
 * Trần cứng số trang gom mỗi keyword. Gom đúng tối đa từng này trang (mỗi trang ~100 SP),
 * lọc catalog (checkbox user) rồi chấm điểm TOÀN BỘ phần còn lại + sắp xếp rồi cắt `limit`.
 * Tiêu đề chỉ là trọng số điểm — không loại cứng. Không quét thêm trang chỉ để đủ `limit`.
 */
export const MAX_SEARCH_PAGES = 4;
/** Ngưỡng pool tối thiểu để cache dùng lại được (khi chưa quét đủ trần trang). */
export const MIN_SEARCH_POOL = 200;
/** Chờ tab Shein load — giữ cho tương thích; luồng search hiện không hard-reload trang. */
export const SEARCH_PAGE_TIMEOUT_MS = 45_000;
/**
 * Nhịp giữa hai trang, tính từ lúc trang trước đã paint xong (`PAGE_SETTLE_MS`) đến lúc
 * bấm sang trang sau. Người thật xem hết một trang ~120 card mất nhiều giây, nên đây là
 * chặn dưới. Jitter rộng (gần bằng base) để chuỗi request không cách nhau quanh một hằng số —
 * nhịp đều tăm tắp là tín hiệu bot rẻ nhất để Shein bắt.
 */
export const SEARCH_PAGE_DELAY_MS = 2600;
export const SEARCH_PAGE_DELAY_JITTER_MS = 2400;
/** Càng vào sâu càng chậm — người thật lướt tới trang 4 không còn bấm nhanh như trang 1. */
export const SEARCH_PAGE_DEPTH_STEP_MS = 500;
/** Chờ JSON SPA sau `tabs.update` mọi trang (Enter URL — không reload trình duyệt). */
export const SPA_PAGE_CAPTURE_TIMEOUT_MS = 30_000;
/**
 * Chờ JSON sau khi bấm phân trang SPA: chỉ một BFF call, không dựng lại document,
 * nên ngắn hơn nhánh Enter URL — bấm trượt thì rơi sang điều hướng sớm thay vì treo 30s.
 */
export const SPA_SOFT_PAGE_CAPTURE_TIMEOUT_MS = 15_000;
/**
 * Nghỉ sau khi đã hớt JSON một trang, trước khi gom/sang trang tiếp.
 * JSON về **trước** lúc SPA vẽ xong ~120 card kèm ảnh, nên nhịp này phải đủ dài để mắt
 * thấy list thật sự đổi — dưới ~1s là không cảm nhận được. Đây là số duy nhất cần sửa
 * nếu muốn xem trang lâu hơn / nhanh hơn.
 */
export const PAGE_SETTLE_MS = 1200;
/** Chờ content script Shein sẵn sàng sau điều hướng tab. */
export const CONTENT_SCRIPT_READY_MS = 8_000;
/** TTL + trần LRU cache pool theo (câu tìm + bộ lọc URL). */
export const SEARCH_POOL_CACHE_TTL_MS = 6 * 60 * 60 * 1000;
export const SEARCH_POOL_CACHE_MAX = 12;

/**
 * Nhịp ngẫu nhiên giữa hai trang — tránh chuỗi request cách nhau đúng một hằng số.
 * `page` = trang **vừa gom xong**, dùng để cộng thêm theo độ sâu.
 */
export function searchPageDelayMs(page = 1): number {
  const depth = Math.max(0, Math.floor(Number(page) || 1) - 1);
  return (
    SEARCH_PAGE_DELAY_MS +
    depth * SEARCH_PAGE_DEPTH_STEP_MS +
    Math.floor(Math.random() * (SEARCH_PAGE_DELAY_JITTER_MS + 1))
  );
}

/**
 * Nghỉ trước khi **leo sang cách lấy trang mạnh tay hơn** (bấm phân trang → điều hướng URL
 * → forge XHR). Không có nhịp này thì một trang xử lý trượt sẽ bắn 2–3 request BFF cách nhau
 * dưới một giây — dồn cục như vậy nguy hiểm hơn nhiều so với nhịp giữa hai trang thành công,
 * vì đúng lúc Shein đã thấy request trước có gì đó bất thường.
 */
export const FALLBACK_ESCALATE_DELAY_MS = 1800;
export const FALLBACK_ESCALATE_JITTER_MS = 1200;

/** Nhịp ngẫu nhiên trước mỗi bước leo thang trong cùng một trang. */
export function fallbackEscalateDelayMs(): number {
  return (
    FALLBACK_ESCALATE_DELAY_MS + Math.floor(Math.random() * (FALLBACK_ESCALATE_JITTER_MS + 1))
  );
}
export const SEARCH_SCORE_WEIGHTS = {
  title: 0.4,
  proven: 0.35,
  rating: 0.15,
  price: 0.1,
} as const;
export const ADD_PRODUCT_DELAY_MS = 2000;
export const ADD_NAV_TIMEOUT_MS = 45_000;

export interface SheinSearchProduct {
  goodsId: string;
  name: string;
  image?: string | null;
  price?: number | null;
  rating?: number | null;
  reviewCount?: number | null;
  soldCount?: number | null;
  variantCount?: number | null;
  goodsUrlName?: string | null;
  isLocal?: boolean;
  isQuickShip?: boolean;
  isSheinClub?: boolean;
  isUsWarehouse?: boolean;
  /** Shein `cate_name` — hỗ trợ khớp title, không dùng làm must. */
  cateName?: string | null;
  /** Điểm tổng 0–100: khớp tiêu đề 40% + Wilson 35% + sao 15% + giá 10%. */
  score?: number | null;
  /** Khớp tiêu đề = trọng số cụm important khớp / tổng (0–100). */
  scoreTitle?: number | null;
  /** Nhiều đánh giá tốt (Wilson) — tuyệt đối, không percentile. */
  scoreProven?: number | null;
  /** Alias scoreProven (tương thích UI cũ). */
  scoreSold?: number | null;
  scoreRating?: number | null;
  scorePrice?: number | null;
  url: string;
  keyword: string;
}

export interface SheinSearchCapture {
  keywords: string;
  page: number;
  total: number;
  products: Omit<SheinSearchProduct, 'url' | 'keyword'>[];
  facets: Array<{ attrId: string; attrValue: string }>;
  at: number;
}

/**
 * Pool SP thô đã gom cho một (câu tìm + bộ lọc URL), trước khi áp lọc client + chấm điểm.
 * Đổi `limit` / rating / sold / Local / variants **không** cần gom lại — chúng áp sau pool.
 */
export interface SearchPoolCacheEntry {
  query: string;
  products: Omit<SheinSearchProduct, 'url' | 'keyword'>[];
  /** `num` Shein báo về (0 = không biết). */
  total: number;
  pagesFetched: number;
  /** Đã quét tới hết trang — pool này là tối đa Shein có cho bộ lọc đó. */
  exhausted: boolean;
  at: number;
}

/** Pool đủ lớn để chọn ra `limit` SP sau khi lọc, hoặc đã gom hết trần trang / hết SP Shein. */
export function searchPoolCacheUsable(
  entry: SearchPoolCacheEntry | null | undefined,
  limit: number,
  now = Date.now(),
): entry is SearchPoolCacheEntry {
  if (!entry || !Array.isArray(entry.products) || entry.products.length === 0) return false;
  if (now - Number(entry.at || 0) > SEARCH_POOL_CACHE_TTL_MS) return false;
  if (entry.exhausted) return true;
  // Đã gom đủ trần trang theo chính sách → không quét thêm được nữa, dùng lại luôn.
  if (entry.pagesFetched >= MAX_SEARCH_PAGES) return true;
  if (entry.total > 0 && entry.products.length >= entry.total) return true;
  const want = Math.max(MIN_SEARCH_POOL, Math.max(1, Math.floor(limit)) * 6);
  return entry.products.length >= want;
}

export interface ProductSearchKeywordInput {
  keyword: string;
  limit: number;
  mustPhrases?: KeywordMatchPhrase[] | null;
  shouldPhrases?: KeywordMatchPhrase[] | null;
  minDistinctiveHits?: number | null;
}

export interface ProductSearchCatalog {
  categories?: { id: string; name: string }[];
  categoryId?: string;
  categoryName?: string;
  ageMin?: number | null;
  ageMax?: number | null;
  bodySizeCode?: string | null;
  styleCodes?: string[];
  priceMinUsd?: number | null;
  priceMaxUsd?: number | null;
  preferQuickShip?: boolean;
  /** Sort by — gắn warmup URL + BFF; Recommend = không gửi `sort`. */
  sheinSort?: string | null;
  preferLocal?: boolean;
  preferUsWarehouse?: boolean;
  preferSheinClub?: boolean;
  minRating?: number;
  minSoldCount?: number;
  maxVariantCount?: number;
}

export interface ProductSearchAddProgress {
  status: 'idle' | 'adding' | 'done';
  total: number;
  success: number;
  failed: number;
  currentUrl?: string;
  message?: string;
}

export interface ProductSearchState {
  status: 'idle' | 'searching' | 'done' | 'error' | 'cancelled';
  shopLocalId?: string;
  shopFsId?: number;
  message?: string;
  currentKeyword?: string;
  currentPage?: number;
  products: SheinSearchProduct[];
  keywordCounts: Record<string, number>;
  add: ProductSearchAddProgress;
  startedAt?: number;
  updatedAt: number;
}

export function emptyProductSearch(): ProductSearchState {
  return {
    status: 'idle',
    products: [],
    keywordCounts: {},
    add: { status: 'idle', total: 0, success: 0, failed: 0 },
    updatedAt: Date.now(),
  };
}

export function countByKeyword(products: SheinSearchProduct[]): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const p of products) {
    const key = p.keyword || '';
    counts[key] = (counts[key] ?? 0) + 1;
  }
  return counts;
}

export function productFingerprint(products: Array<{ goodsId?: string | null }>): string {
  return products
    .map((p) => String(p.goodsId || '').trim())
    .filter(Boolean)
    .slice(0, 8)
    .join(',');
}

/**
 * Lọc SAU khi đã có JSON Shein — không gắn lên URL/BFF.
 * Rating / sold / Local / Club / variants không có query Shein ổn định (gắn URL → 403).
 * Giá + QuickShip đã gửi trên URL; lọc lại ở đây vì Shein đôi khi trả SP ngoài band.
 * Field catalog thiếu (`null`/`undefined`) → bỏ qua tiêu chí đó, không loại
 * (JSON forge XHR đôi khi mỏng hơn JSON do chính SPA gọi).
 */
export function passesClientFilters(
  product: Omit<SheinSearchProduct, 'url' | 'keyword'>,
  catalog: ProductSearchCatalog,
): boolean {
  const minPrice = Number(catalog.priceMinUsd);
  if (Number.isFinite(minPrice) && minPrice > 0 && product.price != null && product.price < minPrice) {
    return false;
  }
  const maxPrice = Number(catalog.priceMaxUsd);
  if (Number.isFinite(maxPrice) && maxPrice > 0 && product.price != null && product.price > maxPrice) {
    return false;
  }
  const minRating = Number(catalog.minRating) || 0;
  if (minRating > 0 && product.rating != null && product.rating < minRating) return false;
  const minSold = Number(catalog.minSoldCount) || 0;
  if (minSold > 0 && product.soldCount != null && product.soldCount < minSold) return false;
  const maxVar = Number(catalog.maxVariantCount) || 0;
  if (maxVar > 0 && product.variantCount != null && product.variantCount >= maxVar) return false;
  if (catalog.preferQuickShip && product.isQuickShip === false) return false;
  if (catalog.preferLocal && product.isLocal === false) return false;
  if (catalog.preferSheinClub && product.isSheinClub === false) return false;
  return true;
}

const TITLE_STOPWORDS = new Set([
  'plus',
  'size',
  'petite',
  'tall',
  'curvy',
  'curve',
  'women',
  'womens',
  'woman',
  'for',
  'the',
  'a',
  'an',
  'and',
  'with',
  'in',
  'of',
  'to',
]);

const TITLE_SYNONYMS: Record<string, readonly string[]> = {
  denim: ['jean', 'jeans', 'denims'],
  jean: ['denim', 'jeans', 'denims'],
  jeans: ['denim', 'jean', 'denims'],
  denims: ['denim', 'jean', 'jeans'],
  dress: ['dresses'],
  dresses: ['dress'],
  skirt: ['skirts'],
  skirts: ['skirt'],
  pant: ['pants', 'trousers'],
  pants: ['pant', 'trousers'],
  trousers: ['pant', 'pants'],
};

function splitTokens(normalized: string): string[] {
  return normalized.split(/\s+/).filter(Boolean);
}

/** Token cốt của keyword (bỏ plus/size/women/…). */
export function coreTitleTokens(keyword?: string | null): string[] {
  const norm = normalizeAudienceKeyword(keyword);
  if (!norm) return [];
  return splitTokens(norm).filter((t) => !TITLE_STOPWORDS.has(t));
}

function titleTokenSet(product: {
  name?: string | null;
  goodsUrlName?: string | null;
  cateName?: string | null;
}): Set<string> {
  const hay = normalizeAudienceKeyword(
    `${product.name || ''} ${product.goodsUrlName || ''} ${product.cateName || ''}`,
  );
  const out = new Set<string>();
  for (const t of splitTokens(hay)) {
    out.add(t);
    for (const part of t.split('-')) {
      if (part) out.add(part);
    }
  }
  return out;
}

function tokenHitsTitle(titleTokens: Set<string>, token: string): boolean {
  if (titleTokens.has(token)) return true;
  for (const syn of TITLE_SYNONYMS[token] ?? []) {
    if (titleTokens.has(syn)) return true;
  }
  return false;
}

export function countMatchedTitleTokens(
  product: { name?: string | null; goodsUrlName?: string | null; cateName?: string | null },
  keyword?: string | null,
): { matched: number; cores: number } {
  const cores = coreTitleTokens(keyword);
  if (cores.length === 0) return { matched: 0, cores: 0 };
  const tokens = titleTokenSet(product);
  let matched = 0;
  for (const c of cores) {
    if (tokenHitsTitle(tokens, c)) matched += 1;
  }
  return { matched, cores: cores.length };
}

/**
 * Cổng tiêu đề theo độ phủ: loại SP (nếu có) + ≥ ceil(N/2) trên tối đa 2 từ nhu cầu.
 * Không tách được cụm → giữ cổng cũ (ít nhất 1 token cốt).
 * Extension Search **không** dùng làm cổng cứng — title chỉ còn trọng số điểm.
 */
export function passesTitleGate(
  product: { name?: string | null; goodsUrlName?: string | null; cateName?: string | null },
  keyword?: string | null,
  mustPhrases?: KeywordMatchPhrase[] | null,
): boolean {
  const set = resolvePhrases(keyword, mustPhrases);
  if (set.must.length > 0) return passesCoverage(product, set);
  const { matched, cores } = countMatchedTitleTokens(product, keyword);
  if (cores === 0) return true;
  return matched >= 1;
}

/** 0–1: trọng số cụm important khớp; fallback % token cốt khi không có cụm. */
export function titleRelevanceScore(
  product: { name?: string | null; goodsUrlName?: string | null; cateName?: string | null },
  keyword?: string | null,
  phrases?: { must?: KeywordMatchPhrase[] | null; should?: KeywordMatchPhrase[] | null },
): number {
  const resolved = resolvePhrases(keyword, phrases?.must, phrases?.should);
  if (resolved.must.length > 0 || resolved.should.length > 0) {
    return phraseTitleScore(product, resolved.must, resolved.should);
  }
  const { matched, cores } = countMatchedTitleTokens(product, keyword);
  if (cores === 0) return 0;
  return matched / cores;
}

export type SearchRankable = {
  price?: number | null;
  rating?: number | null;
  reviewCount?: number | null;
  soldCount?: number | null;
  goodsId?: string | null;
  name?: string | null;
  goodsUrlName?: string | null;
  cateName?: string | null;
};

export type SearchScoreOpts = {
  keyword?: string | null;
  priceMinUsd?: number | null;
  priceMaxUsd?: number | null;
  mustPhrases?: KeywordMatchPhrase[] | null;
  shouldPhrases?: KeywordMatchPhrase[] | null;
  /** Body/size của shop — prefix câu tìm + bonus điểm tiêu đề. */
  bodySizeCode?: string | null;
};

/** Phần điểm tiêu đề dành cho cụm body/size (0.2 → 8% tổng điểm). */
export const BODY_SIZE_TITLE_WEIGHT = 0.2;

const BODY_SIZE_TITLE_ALTS: Record<string, string[]> = {
  'plus size': ['plus size', 'curve', 'curvy'],
  curve: ['curve', 'curvy'],
};

/** Cụm body/size để dò trên tiêu đề; `null` khi shop không chọn body/size. */
export function bodySizeTitlePhrase(bodySizeCode?: string | null): KeywordMatchPhrase | null {
  const phrase = audienceModifier(bodySizeCode);
  if (!phrase) return null;
  return { phrase, anyOf: BODY_SIZE_TITLE_ALTS[phrase] ?? [phrase] };
}

/**
 * Điểm tiêu đề có tính tệp body/size: nhường `BODY_SIZE_TITLE_WEIGHT` cho cụm body/size.
 * Chỉ nâng hạng SP ghi đúng tệp (`plus size`, `petite`…) chứ không loại SP thiếu cụm —
 * JSON search của Shein không trả size nên thiếu cụm không có nghĩa là sai tệp.
 */
function titleScoreWithBodySize(
  product: SearchRankable,
  keyword: string | null,
  phrases: { must?: KeywordMatchPhrase[] | null; should?: KeywordMatchPhrase[] | null },
  bodyPhrase: KeywordMatchPhrase | null,
): number {
  const base = titleRelevanceScore(product, keyword, phrases);
  if (!bodyPhrase) return base;
  const tokens = titleTokens(product.name, product.goodsUrlName, product.cateName);
  const hit = phraseHits(tokens, bodyPhrase) ? 1 : 0;
  return base * (1 - BODY_SIZE_TITLE_WEIGHT) + hit * BODY_SIZE_TITLE_WEIGHT;
}

function finiteNums(values: Array<number | null | undefined>): number[] {
  return values.filter((v): v is number => v != null && Number.isFinite(v));
}

function clamp01(n: number): number {
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(1, n));
}

/** Shein rating thường 0–5; nếu >5 coi như thang 100. */
export function ratingToPositiveRate(rating: number | null | undefined): number {
  if (rating == null || !Number.isFinite(rating) || rating <= 0) return 0;
  const stars = rating > 5 ? rating / 20 : rating;
  return clamp01(stars / 5);
}

/** Ưu tiên số đánh giá; thiếu thì dùng sold (cùng tín hiệu “đã bán được”). */
export function reviewVolumeOf(p: SearchRankable): number {
  const reviews = p.reviewCount;
  if (reviews != null && Number.isFinite(reviews) && reviews > 0) return reviews;
  const sold = p.soldCount;
  if (sold != null && Number.isFinite(sold) && sold > 0) return sold;
  return 0;
}

/**
 * Cận dưới Wilson 95%: 5★ với ít review vẫn thấp hơn 4.6★ với nhiều review.
 * Đây là “thị trường đã chứng minh” — nhiều đánh giá tốt mới tin được.
 */
export function wilsonLowerBound(positiveRate: number, n: number, z = 1.96): number {
  if (n <= 0 || !Number.isFinite(n) || !Number.isFinite(positiveRate)) return 0;
  const p = clamp01(positiveRate);
  const z2 = z * z;
  const denom = 1 + z2 / n;
  const centre = p + z2 / (2 * n);
  const inner = (p * (1 - p) + z2 / (4 * n)) / n;
  const margin = z * Math.sqrt(Math.max(0, inner));
  return clamp01((centre - margin) / denom);
}

/** 0–1: nhiều đánh giá × rating tốt. Thiếu rating thì chưa chứng minh “tốt”. */
export function marketProofScore(p: SearchRankable): number {
  if (p.rating == null || !Number.isFinite(p.rating)) return 0;
  return wilsonLowerBound(ratingToPositiveRate(p.rating), reviewVolumeOf(p));
}

function toScore100(n: number): number {
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(100, Math.round(n * 100)));
}

function medianOf(nums: number[]): number | null {
  if (nums.length === 0) return null;
  const s = [...nums].sort((a, b) => a - b);
  const mid = Math.floor(s.length / 2);
  return s.length % 2 === 1 ? s[mid]! : (s[mid - 1]! + s[mid]!) / 2;
}

function priceCheapScore(
  price: number | null | undefined,
  bandMin: number | null,
  bandMax: number | null,
  median: number | null,
): number {
  if (price == null || !Number.isFinite(price) || price <= 0) return 0;
  if (bandMin != null && bandMax != null && bandMax > bandMin) {
    return clamp01((bandMax - price) / (bandMax - bandMin));
  }
  if (median != null && median > 0) {
    return clamp01(median / (median + price));
  }
  return 0.5;
}

export type ScoredSearchProduct<T extends SearchRankable> = T & {
  score: number;
  scoreTitle: number;
  scoreProven: number;
  scoreSold: number;
  scoreRating: number;
  scorePrice: number;
};

/**
 * Chấm tuyệt đối trên pool đã lọc (trước khi cắt limit), luôn sort điểm giảm dần.
 * Sort by Shein chỉ ảnh hưởng request BFF — không giữ thứ tự Shein trên list.
 * 40% khớp tiêu đề (trọng số cụm important, đồng nghĩa denim=jean) + 35% Wilson + 15% sao + 10% giá thấp.
 * Có body/size shop thì 20% của phần tiêu đề dành cho cụm đó.
 * Thiếu một chiều cả pool → bỏ trọng số đó, chuẩn hóa lại tổng 1.
 */
export function scoreSearchProducts<T extends SearchRankable>(
  products: T[],
  opts?: SearchScoreOpts,
): ScoredSearchProduct<T>[] {
  if (products.length === 0) return [];
  const keyword = opts?.keyword ?? null;
  const phrases = resolvePhrases(keyword, opts?.mustPhrases, opts?.shouldPhrases);
  const bodyPhrase = bodySizeTitlePhrase(opts?.bodySizeCode);
  const cores = coreTitleTokens(keyword);
  const minRaw = Number(opts?.priceMinUsd);
  const maxRaw = Number(opts?.priceMaxUsd);
  const bandMin = Number.isFinite(minRaw) && minRaw > 0 ? minRaw : null;
  const bandMax = Number.isFinite(maxRaw) && maxRaw > 0 ? maxRaw : null;
  const prices = finiteNums(products.map((p) => p.price));
  const medianPrice = medianOf(prices);
  const provenRaw = products.map((p) => marketProofScore(p));
  const ratings = finiteNums(products.map((p) => p.rating));
  const titleUsed =
    phrases.must.length > 0 || phrases.should.length > 0 || cores.length > 0 || !!bodyPhrase;
  const provenUsed = provenRaw.some((v) => v > 0);
  const ratingUsed = ratings.length > 0;
  const priceUsed = prices.length > 0;

  let wTitle = titleUsed ? SEARCH_SCORE_WEIGHTS.title : 0;
  let wProven = provenUsed ? SEARCH_SCORE_WEIGHTS.proven : 0;
  let wRating = ratingUsed ? SEARCH_SCORE_WEIGHTS.rating : 0;
  let wPrice = priceUsed ? SEARCH_SCORE_WEIGHTS.price : 0;
  const wSum = wTitle + wProven + wRating + wPrice;
  if (wSum > 0) {
    wTitle /= wSum;
    wProven /= wSum;
    wRating /= wSum;
    wPrice /= wSum;
  }

  return products
    .map((p, index) => {
      const title = titleUsed ? titleScoreWithBodySize(p, keyword, phrases, bodyPhrase) : 0;
      const proven = provenRaw[index] ?? 0;
      const rating = ratingToPositiveRate(p.rating);
      const cheap = priceCheapScore(p.price, bandMin, bandMax, medianPrice);
      const total = wTitle * title + wProven * proven + wRating * rating + wPrice * cheap;
      const proven100 = toScore100(proven);
      return {
        ...p,
        score: wSum > 0 ? toScore100(total) : 0,
        scoreTitle: toScore100(title),
        scoreProven: proven100,
        scoreSold: proven100,
        scoreRating: toScore100(rating),
        scorePrice: toScore100(cheap),
        _proven: proven,
        _volume: reviewVolumeOf(p),
        _index: index,
      };
    })
    .sort((a, b) => {
      if (b.score !== a.score) return b.score - a.score;
      if (b._proven !== a._proven) return b._proven - a._proven;
      if (b._volume !== a._volume) return b._volume - a._volume;
      const rateA = a.rating ?? -1;
      const rateB = b.rating ?? -1;
      if (rateB !== rateA) return rateB - rateA;
      const priceA = a.price ?? Number.POSITIVE_INFINITY;
      const priceB = b.price ?? Number.POSITIVE_INFINITY;
      if (priceA !== priceB) return priceA - priceB;
      return a._index - b._index;
    })
    .map((row) => {
      const { _index, _proven, _volume, ...rest } = row;
      void _index;
      void _proven;
      void _volume;
      return rest as ScoredSearchProduct<T>;
    });
}

type ScoredListItem = {
  score?: number | null;
  scoreProven?: number | null;
  reviewCount?: number | null;
  soldCount?: number | null;
  rating?: number | null;
  price?: number | null;
};

/** Xếp list Search theo điểm (cache cũ / Thêm sản phẩm cùng thứ tự UI). */
export function sortBySearchScore<T extends ScoredListItem>(products: T[]): T[] {
  return [...products].sort((a, b) => {
    const sa = a.score ?? -1;
    const sb = b.score ?? -1;
    if (sb !== sa) return sb - sa;
    const pa = a.scoreProven ?? -1;
    const pb = b.scoreProven ?? -1;
    if (pb !== pa) return pb - pa;
    const va = reviewVolumeOf(a);
    const vb = reviewVolumeOf(b);
    if (vb !== va) return vb - va;
    const ra = a.rating ?? -1;
    const rb = b.rating ?? -1;
    if (rb !== ra) return rb - ra;
    const priceA = a.price ?? Number.POSITIVE_INFINITY;
    const priceB = b.price ?? Number.POSITIVE_INFINITY;
    if (priceA !== priceB) return priceA - priceB;
    return 0;
  });
}

/** @deprecated dùng scoreSearchProducts — giữ alias cho chỗ gọi cũ. */
export function rankSearchProducts<T extends SearchRankable>(
  products: T[],
  opts?: SearchScoreOpts,
): T[] {
  return scoreSearchProducts(products, opts);
}

function toNum(v: unknown): number | null {
  if (typeof v === 'number' && Number.isFinite(v)) return v;
  if (typeof v === 'string' && v.trim()) {
    const n = parseFloat(v.replace(/[$,\s]/g, '').replace(/,/g, ''));
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

function toInt(v: unknown): number | null {
  const n = toNum(v);
  return n == null ? null : Math.floor(n);
}

function soldLabelText(raw: unknown): string | null {
  if (raw == null || raw === '') return null;
  if (typeof raw === 'number' && Number.isFinite(raw)) return String(raw);
  if (typeof raw === 'string') {
    const t = raw.trim();
    return t && t !== '[object Object]' ? t : null;
  }
  if (Array.isArray(raw)) {
    for (const item of raw) {
      const t = soldLabelText(item);
      if (t) return t;
    }
    return null;
  }
  if (typeof raw === 'object') {
    const obj = raw as Record<string, unknown>;
    for (const key of ['labelLang', 'label_lang', 'label', 'text', 'value', 'desc']) {
      const t = soldLabelText(obj[key]);
      if (t) return t;
    }
  }
  return null;
}

/** `"300+ sold"` / `{ labelLang: "1.5k+ sold" }` → số sàn (300, 1500). */
function parseSoldLabel(raw: unknown): number | null {
  if (raw == null || raw === '') return null;
  if (typeof raw === 'number' && Number.isFinite(raw)) return Math.max(0, Math.floor(raw));
  const s = soldLabelText(raw);
  if (!s) return null;
  const norm = s.toLowerCase().replace(/,/g, '');
  const m = norm.match(/(\d+(?:\.\d+)?)\s*([km])?\s*\+?/i);
  if (!m) {
    const n = parseInt(norm, 10);
    return Number.isFinite(n) ? n : null;
  }
  let val = parseFloat(m[1]);
  if (!Number.isFinite(val)) return null;
  const unit = (m[2] || '').toLowerCase();
  if (unit === 'k') val *= 1000;
  if (unit === 'm') val *= 1_000_000;
  return Math.max(0, Math.floor(val));
}

function soldCountOf(row: Record<string, unknown>): number | null {
  return (
    parseSoldLabel(row.salesLabel) ??
    parseSoldLabel(row.sales_label) ??
    parseSoldLabel(row.salesVolume) ??
    parseSoldLabel(row.sales_volume) ??
    parseSoldLabel(row.sales) ??
    parseSoldLabel(row.soldCount) ??
    parseSoldLabel(row.sold_count) ??
    parseSoldLabel(row.soldNum) ??
    parseSoldLabel(row.sold_num) ??
    parseSoldLabel(row.unit_sell) ??
    parseSoldLabel(row.saleNum)
  );
}

function pickPrice(row: Record<string, unknown>): number | null {
  const sale = row.salePrice || row.sale_price || row.retailPrice || row.retail_price || row;
  if (sale && typeof sale === 'object') {
    const obj = sale as Record<string, unknown>;
    const amount = toNum(obj.usdAmount) ?? toNum(obj.amount) ?? toNum(obj.salePrice) ?? toNum(obj.price);
    if (amount != null) return amount;
  }
  return toNum(row.salePrice) ?? toNum(row.sale_price) ?? toNum(row.price);
}

function pickImage(row: Record<string, unknown>): string | null {
  const img =
    row.goods_img ||
    row.goodsImg ||
    row.goods_thumb ||
    row.goodsThumb ||
    (Array.isArray(row.detail_image) ? row.detail_image[0] : null) ||
    (Array.isArray(row.detailImage) ? row.detailImage[0] : null);
  if (!img) return null;
  const s = String(img);
  if (s.indexOf('http') === 0) return s;
  if (s.indexOf('//') === 0) return `https:${s}`;
  return s;
}

function variantCountOf(row: Record<string, unknown>): number | null {
  const related = row.relatedColor || row.related_color || row.relatedGoods || row.skuList;
  if (Array.isArray(related) && related.length) return related.length;
  const n =
    toNum(row.relatedColorCount) ??
    toNum(row.related_color_count) ??
    toNum(row.colorCount) ??
    toNum(row.skuCount);
  return n != null ? Math.max(0, Math.floor(n)) : null;
}

function flagBool(v: unknown): boolean | undefined {
  if (v === true || v === 1 || v === '1') return true;
  if (v === false || v === 0 || v === '0') return false;
  if (typeof v === 'string' && /^(true|yes|local|quickship|club)$/i.test(v.trim())) return true;
  if (typeof v === 'string' && /^(false|no)$/i.test(v.trim())) return false;
  return undefined;
}

/**
 * Local US trên list Search: `productMaterial.titleLabelList[].labelType === "local_stock"`.
 * Có `titleLabelList` mà không có `local_stock` → false. Không có material/list → undefined.
 */
function parseSearchLocal(row: Record<string, unknown>): boolean | undefined {
  const material = row.productMaterial ?? row.product_material;
  if (!material || typeof material !== 'object') return undefined;
  const list =
    (material as Record<string, unknown>).titleLabelList ??
    (material as Record<string, unknown>).title_label_list;
  if (!Array.isArray(list)) return undefined;
  for (const item of list) {
    if (!item || typeof item !== 'object') continue;
    const type = String((item as Record<string, unknown>).labelType ?? '').trim().toLowerCase();
    if (type === 'local_stock') return true;
  }
  return false;
}

function slimProduct(row: unknown): Omit<SheinSearchProduct, 'url' | 'keyword'> | null {
  if (!row || typeof row !== 'object') return null;
  const obj = row as Record<string, unknown>;
  const goodsId = obj.goods_id != null ? String(obj.goods_id) : obj.goodsId != null ? String(obj.goodsId) : '';
  if (!goodsId) return null;
  const name = String(obj.goods_name || obj.goodsName || obj.goods_url_name || obj.goodsUrlName || '').trim();
  const slug = String(obj.goods_url_name || obj.goodsUrlName || '').trim();
  const cateName = String(
    obj.cate_name || obj.cateName || obj.category_name || obj.categoryName || '',
  ).trim();
  const sold = soldCountOf(obj);
  const rating =
    toNum(obj.comment_rank_average) ??
    toNum(obj.commentRankAverage) ??
    toNum(obj.commentRank) ??
    toNum(obj.grade) ??
    toNum(obj.goodsScore) ??
    toNum(obj.score);
  const reviews = toInt(obj.comment_num) ?? toInt(obj.commentNum) ?? toInt(obj.comment_count);
  const isLocal = parseSearchLocal(obj);
  const isQuickShip =
    flagBool(obj.isSupportQuickShip) ?? flagBool(obj.is_support_quick_ship) ?? flagBool(obj.quickship);
  const isSheinClub = flagBool(obj.sheinClub) ?? flagBool(obj.isSheinClub) ?? flagBool(obj.premiumFlag);
  const isUsWarehouse =
    flagBool(obj.isUsWarehouse) ??
    flagBool(obj.is_us_warehouse) ??
    flagBool(obj.usWarehouse) ??
    flagBool(obj.shipFromUs) ??
    flagBool(obj.is_us_stock);
  return {
    goodsId,
    name: name || goodsId,
    image: pickImage(obj),
    price: pickPrice(obj),
    rating,
    reviewCount: reviews,
    soldCount: sold,
    variantCount: variantCountOf(obj),
    goodsUrlName: slug || null,
    cateName: cateName || null,
    isLocal,
    isQuickShip,
    isSheinClub,
    isUsWarehouse,
  };
}

function extractProductsArray(json: unknown): unknown[] {
  if (!json || typeof json !== 'object') return [];
  const root = json as Record<string, unknown>;
  const info = (root.info || root.data || root) as Record<string, unknown>;
  const list = info.products || info.goods || info.list || info.productList || root.products || root.goods;
  return Array.isArray(list) ? list : [];
}

function extractTotal(json: unknown, fallbackLen: number): number {
  if (!json || typeof json !== 'object') return fallbackLen;
  const root = json as Record<string, unknown>;
  const info = (root.info || root.data || root) as Record<string, unknown>;
  return toInt(info.num) ?? toInt(info.total) ?? toInt(info.totalCount) ?? toInt(root.num) ?? toInt(root.total) ?? fallbackLen;
}

function keywordsFromUrlOrJson(url: string, json: unknown): string {
  try {
    const u = new URL(url);
    const fromQuery = u.searchParams.get('keywords') || u.searchParams.get('keyword');
    if (fromQuery) return fromQuery;
  } catch {
    /* ignore */
  }
  if (json && typeof json === 'object') {
    const root = json as Record<string, unknown>;
    const info = (root.info || root.data || root) as Record<string, unknown>;
    const k = info.keywords || info.keyword || root.keywords || root.keyword;
    if (k != null) return String(k).trim();
  }
  return '';
}

function pageFromUrlOrJson(url: string, json: unknown): number {
  if (json && typeof json === 'object') {
    const root = json as Record<string, unknown>;
    const info = (root.info || root.data || root) as Record<string, unknown>;
    const p = toInt(info.page) ?? toInt(info.pageNum) ?? toInt(info.pageNo) ?? toInt(root.page);
    if (p != null && p > 0) return p;
  }
  try {
    const n = parseInt(new URL(url).searchParams.get('page') || '1', 10);
    return Number.isFinite(n) && n > 0 ? n : 1;
  } catch {
    return 1;
  }
}

export function parseSheinSearchJson(url: string, json: unknown): SheinSearchCapture | null {
  if (!json || typeof json !== 'object') return null;
  const rows = extractProductsArray(json);
  const products: Omit<SheinSearchProduct, 'url' | 'keyword'>[] = [];
  const seen = new Set<string>();
  for (const row of rows) {
    const slim = slimProduct(row);
    if (!slim || seen.has(slim.goodsId)) continue;
    seen.add(slim.goodsId);
    products.push(slim);
  }
  return {
    keywords: keywordsFromUrlOrJson(url, json),
    page: pageFromUrlOrJson(url, json),
    total: extractTotal(json, products.length),
    products,
    facets: collectAttrFacets(json),
    at: Date.now(),
  };
}

export interface SavedKeywordRow {
  keyword: string;
  picUrl?: string | null;
  leadTagTexts?: string[] | null;
  searchVolume?: number;
  rationale?: string | null;
  fit?: string | null;
  /** Shop research: `listNow` (List ngay) or `upcoming` (Đón đầu). */
  researchLane?: 'listNow' | 'upcoming' | null;
  mustPhrases?: KeywordMatchPhrase[] | null;
  shouldPhrases?: KeywordMatchPhrase[] | null;
  minDistinctiveHits?: number | null;
}

export type LearnedStyleAttrs = SheinStyleAttr[];
