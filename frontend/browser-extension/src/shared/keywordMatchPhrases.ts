/**
 * Split a keyword into content tokens. Gate uses at most 2 demand modifiers + garment
 * (coverage ceil(N/2)), not AND-all. Leftover modifiers score only.
 * Keep in sync with backend KeywordMatchPhrases.cs.
 */

import { normalizeAudienceKeyword } from './audienceSearchQuery';

export interface KeywordMatchPhrase {
  phrase: string;
  anyOf: string[];
}

export interface KeywordMatchPhraseSet {
  must: KeywordMatchPhrase[];
  should: KeywordMatchPhrase[];
  minDistinctiveHits: number;
}

const STOPWORDS = new Set([
  'plus',
  'size',
  'petite',
  'tall',
  'curvy',
  'curve',
  'women',
  'womens',
  'woman',
  "woman's",
  'for',
  'the',
  'a',
  'an',
  'and',
  'with',
  'in',
  'of',
  'to',
  'at',
  'on',
  'ladies',
  'lady',
  'girl',
  'girls',
]);

const GENERIC_SHOULD = new Set([
  'casual',
  'elegant',
  'chic',
  'cute',
  'sexy',
  'nice',
  'trendy',
  'holiday',
  'party',
  'beach',
  'vacation',
  'school',
  'christmas',
  'summer',
  'spring',
  'fall',
  'autumn',
  'winter',
  'new',
  'fashion',
  'comfortable',
  'loose',
  'fitted',
  'slim',
  'oversized',
  'plus',
  'size',
  'women',
  'womens',
]);

const GARMENT_NOUNS = new Set([
  'dress',
  'dresses',
  'gown',
  'sundress',
  'top',
  'tops',
  'shirt',
  'shirts',
  'tee',
  't-shirt',
  'tshirt',
  'blouse',
  'blouses',
  'pant',
  'pants',
  'trouser',
  'trousers',
  'jean',
  'jeans',
  'skirt',
  'skirts',
  'shorts',
  'set',
  'sets',
  'sweater',
  'sweaters',
  'jumper',
  'jumpers',
  'jacket',
  'jackets',
  'coat',
  'coats',
  'blazer',
  'blazers',
  'vest',
  'vests',
  'waistcoat',
  'waistcoats',
  'romper',
  'rompers',
  'jumpsuit',
  'jumpsuits',
  'bodysuit',
  'bodysuits',
  'hoodie',
  'hoodies',
  'cardigan',
  'cardigans',
  'legging',
  'leggings',
  'overall',
  'overalls',
  'dungaree',
  'dungarees',
  'pinafore',
  'pinafores',
  'cami',
  'camisole',
  'tank',
  'kimono',
  'poncho',
  'trench',
  'parka',
  'swimsuit',
  'bikini',
  'coverup',
  'cover-up',
]);

const LENGTH_TOKENS = new Set([
  'midi',
  'mini',
  'maxi',
  'knee-length',
  'ankle',
  'tea-length',
  'midi-length',
  'mid-length',
  'floor-length',
  'full-length',
]);

const MATERIAL_TOKENS = new Set([
  'denim',
  'linen',
  'satin',
  'silk',
  'mesh',
  'leather',
  'lace',
  'knit',
  'velvet',
  'corduroy',
  'chiffon',
  'fleece',
  'wool',
  'sequin',
  'sequined',
  'crochet',
  'tweed',
  'gauze',
  'organza',
  'terry',
]);

const PRINT_TOKENS = new Set([
  'floral',
  'plaid',
  'leopard',
  'zebra',
  'snake',
  'camo',
  'camouflage',
  'stripe',
  'striped',
  'gingham',
  'paisley',
  'skeleton',
  'skull',
  'x-ray',
  'xray',
  'polka',
  'checkered',
  'tie-dye',
  'tiedye',
  'cow',
  'marble',
  'geometric',
  'graphic',
  'animal',
]);

const SILHOUETTE_TOKENS = new Set([
  'off-shoulder',
  'strapless',
  'halter',
  'wrap',
  'a-line',
  'aline',
  'bodycon',
  'empire',
  'peplum',
  'one-shoulder',
  'bardot',
  'sweetheart',
  'cowl',
  'square-neck',
  'v-neck',
  'turtleneck',
  'collared',
  'asymmetric',
  'asymmetrical',
  'mermaid',
  'sheath',
  'babydoll',
  'boat',
  'scoop',
]);

const HARDWARE_TOKENS = new Set([
  'button',
  'buttons',
  'placket',
  'pocket',
  'pockets',
  'zipper',
  'zip',
  'buckle',
  'belt',
  'belts',
  'hook',
  'stud',
  'rivet',
  'snap',
  'snaps',
]);

const STYLE_TOKENS = new Set([
  'retro',
  'vintage',
  'boho',
  'western',
  'preppy',
  'grunge',
  'y2k',
  'gothic',
  'romantic',
  'modest',
  'cottage',
  'cottagecore',
]);

const SYNONYM_GROUPS: string[][] = [
  ['denim', 'jean', 'jeans', 'denims'],
  ['dress', 'dresses', 'gown', 'sundress'],
  [
    'vest dress',
    'pinafore',
    'pinafore dress',
    'jumper dress',
    'overall dress',
    'dungaree',
    'dungaree dress',
    'dungarees',
  ],
  ['vest', 'vests', 'waistcoat', 'waistcoats'],
  ['skirt', 'skirts'],
  ['pant', 'pants', 'trouser', 'trousers'],
  ['a-line', 'aline', 'a line'],
  ['retro', 'vintage', 'retro-style', 'retro style'],
  ['floral', 'floral print', 'flower print', 'florals'],
  ['sleeveless', 'sleeve-less', 'no sleeve', 'no sleeves', 'without sleeves'],
  ['off-shoulder', 'off shoulder', 'off the shoulder', 'off-the-shoulder', 'bardot'],
  ['ruffled', 'ruffle', 'ruffles'],
  ['midi', 'midi length', 'mid-length', 'mid length'],
];

/** Content words of a keyword (stopwords dropped). */
export function contentTokens(keyword?: string | null): string[] {
  const norm = normalizeAudienceKeyword(keyword);
  if (!norm) return [];
  return norm.split(/\s+/).filter((t) => t && !STOPWORDS.has(t));
}

const MAX_DEMAND_DISTINCTIVE = 2;

/** ceil(N/2) distinctive hits required; 0 when N is 0. */
export function minDistinctiveHitsFor(distinctiveCount: number): number {
  if (distinctiveCount <= 0) return 0;
  return Math.ceil(distinctiveCount / 2);
}

export function isGarmentToken(token?: string | null): boolean {
  return GARMENT_NOUNS.has(String(token || '').trim().toLowerCase());
}

export function fromKeyword(
  keyword?: string | null,
  _mustTokens?: string[] | null,
): KeywordMatchPhraseSet {
  const tokens = contentTokens(keyword);
  if (tokens.length === 0) return { must: [], should: [], minDistinctiveHits: 0 };

  const garment = [...tokens].reverse().find(isGarment) ?? null;
  const demand = pickDemandDistinctive(tokens);
  const mustSet = new Set(demand.map((t) => t.toLowerCase()));
  if (garment) mustSet.add(garment.toLowerCase());
  const mustRaw = uniqueKeepOrder(tokens.filter((t) => mustSet.has(t.toLowerCase())));
  const shouldRaw = uniqueKeepOrder(
    tokens.filter((t) => !GENERIC_SHOULD.has(t) && !mustSet.has(t.toLowerCase())),
  );
  return {
    must: mustRaw.map(toPhrase),
    should: shouldRaw.map(toPhrase),
    minDistinctiveHits: minDistinctiveHitsFor(demand.length),
  };
}

export function sanitizeMustTokens(keyword?: string | null, raw?: string[] | null): string[] {
  return sanitizeMustTokensFromTokens(contentTokens(keyword), raw);
}

export function normalizePhrases(raw?: KeywordMatchPhrase[] | null): KeywordMatchPhrase[] | undefined {
  if (!Array.isArray(raw)) return undefined;
  return raw
    .map((p) => ({
      phrase: String(p?.phrase || '').trim(),
      anyOf: Array.isArray(p?.anyOf)
        ? p.anyOf.map((x) => String(x || '').trim()).filter(Boolean)
        : [],
    }))
    .filter((p) => p.phrase.length > 0)
    .map((p) => ({
      phrase: p.phrase,
      anyOf: p.anyOf.length > 0 ? p.anyOf : alternativesFor(p.phrase),
    }));
}

export function resolvePhrases(
  keyword?: string | null,
  mustFromApi?: KeywordMatchPhrase[] | null,
  shouldFromApi?: KeywordMatchPhrase[] | null,
  minDistinctiveHitsFromApi?: number | null,
): KeywordMatchPhraseSet {
  if (keyword?.trim()) return fromKeyword(keyword);
  const must = normalizePhrases(mustFromApi) ?? [];
  const should = normalizePhrases(shouldFromApi) ?? [];
  const n = must.filter((p) => !isGarment(p.phrase)).length;
  const stamped = Number(minDistinctiveHitsFromApi);
  return {
    must,
    should,
    minDistinctiveHits: Number.isFinite(stamped)
      ? Math.max(0, Math.floor(stamped))
      : minDistinctiveHitsFor(n),
  };
}

export function titleTokens(name?: string | null, slug?: string | null, cateName?: string | null): Set<string> {
  const hay = normalizeAudienceKeyword(`${name || ''} ${slug || ''} ${cateName || ''}`);
  const out = new Set<string>();
  for (const token of hay.split(/\s+/).filter(Boolean)) {
    out.add(token);
    if (!token.includes('-')) continue;
    for (const part of token.split('-')) {
      if (part) out.add(part);
    }
  }
  return out;
}

export function phraseHits(title: Set<string>, phrase: KeywordMatchPhrase): boolean {
  for (const alt of phrase.anyOf ?? []) {
    const parts = matchParts(alt);
    if (parts.length === 0) continue;
    if (parts.every((p) => tokenInTitle(title, p))) return true;
  }
  return false;
}

export function passesCoverage(
  product: { name?: string | null; goodsUrlName?: string | null; cateName?: string | null },
  set: KeywordMatchPhraseSet,
): boolean {
  if (set.must.length === 0) return true;
  const title = titleTokens(product.name, product.goodsUrlName, product.cateName);
  const garments = set.must.filter((p) => isGarment(p.phrase));
  if (garments.length > 0 && !garments.some((p) => phraseHits(title, p))) return false;
  const distinctiveHits = set.must.filter((p) => !isGarment(p.phrase) && phraseHits(title, p)).length;
  return distinctiveHits >= set.minDistinctiveHits;
}

/** Alias of passesCoverage. */
export function passesMust(
  product: { name?: string | null; goodsUrlName?: string | null; cateName?: string | null },
  set: KeywordMatchPhraseSet,
): boolean {
  return passesCoverage(product, set);
}

/** 0–1 = matched weight / important weight. Empty → 0. */
export function phraseTitleScore(
  product: { name?: string | null; goodsUrlName?: string | null; cateName?: string | null },
  must: KeywordMatchPhrase[],
  should: KeywordMatchPhrase[] = [],
): number {
  const phrases = concatPhrases(must, should);
  if (phrases.length === 0) return 0;
  const title = titleTokens(product.name, product.goodsUrlName, product.cateName);
  let total = 0;
  let hit = 0;
  for (const phrase of phrases) {
    const weight = phraseWeight(phrase.phrase);
    total += weight;
    if (phraseHits(title, phrase)) hit += weight;
  }
  return total <= 0 ? 0 : hit / total;
}

function toPhrase(phrase: string): KeywordMatchPhrase {
  return { phrase, anyOf: alternativesFor(phrase) };
}

function alternativesFor(phrase: string): string[] {
  const set = new Set<string>([phrase]);
  const lower = phrase.toLowerCase();
  for (const group of SYNONYM_GROUPS) {
    if (!group.some((m) => m.toLowerCase() === lower)) continue;
    for (const m of group) set.add(m);
  }
  const rest = [...set].filter((x) => x.toLowerCase() !== lower).sort((a, b) => a.localeCompare(b));
  return [phrase, ...rest];
}

function sanitizeMustTokensFromTokens(tokens: string[], raw?: string[] | null): string[] {
  if (!raw || tokens.length === 0) return [];
  const allowed = allowedMustKeys(tokens);
  const result: string[] = [];
  const seen = new Set<string>();
  for (const item of raw) {
    const norm = normalizeAudienceKeyword(item);
    if (!norm) continue;
    const canonical = allowed.get(norm);
    if (!canonical) continue;
    const key = canonical.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    result.push(canonical);
  }
  return result;
}

function isGarment(token: string): boolean {
  return GARMENT_NOUNS.has(token);
}

function isLength(token: string): boolean {
  return LENGTH_TOKENS.has(token);
}

function isDistinctive(token: string): boolean {
  return !GENERIC_SHOULD.has(token) && !isGarment(token) && !HARDWARE_TOKENS.has(token) && !STYLE_TOKENS.has(token);
}

function isHeadline(token: string): boolean {
  return (
    isDistinctive(token) &&
    !isLength(token) &&
    !MATERIAL_TOKENS.has(token) &&
    !PRINT_TOKENS.has(token) &&
    !SILHOUETTE_TOKENS.has(token)
  );
}

function pickDemandDistinctive(tokens: string[]): string[] {
  const buckets = [
    tokens.filter((t) => MATERIAL_TOKENS.has(t)),
    tokens.filter((t) => PRINT_TOKENS.has(t)),
    tokens.filter((t) => SILHOUETTE_TOKENS.has(t)),
    tokens.filter(isHeadline),
    tokens.filter(isLength),
  ];
  const picked: string[] = [];
  const seen = new Set<string>();
  for (const bucket of buckets) {
    for (const token of bucket) {
      if (picked.length >= MAX_DEMAND_DISTINCTIVE) return picked;
      const key = token.toLowerCase();
      if (seen.has(key)) continue;
      seen.add(key);
      picked.push(token);
    }
  }
  return picked;
}

function uniqueKeepOrder(tokens: string[]): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const t of tokens) {
    const key = t.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(t);
  }
  return out;
}

function phraseWeight(phrase: string): number {
  const token = (phrase || '').trim();
  if (!token) return 0;
  if (isGarment(token)) return 2;
  if (isLength(token)) return 1.5;
  if (HARDWARE_TOKENS.has(token) || STYLE_TOKENS.has(token)) return 1;
  if (MATERIAL_TOKENS.has(token) || PRINT_TOKENS.has(token) || SILHOUETTE_TOKENS.has(token)) return 2;
  return 2;
}

function allowedMustKeys(tokens: string[]): Map<string, string> {
  const map = new Map<string, string>();
  const add = (alias: string, canonical: string) => {
    const key = alias.toLowerCase();
    if (!key || !canonical || map.has(key)) return;
    map.set(key, canonical);
  };
  const keys: string[] = [];
  for (let i = 0; i < tokens.length; i++) {
    keys.push(tokens[i]!);
    add(tokens[i]!, tokens[i]!);
    if (i + 1 >= tokens.length) continue;
    const pair = `${tokens[i]} ${tokens[i + 1]}`;
    keys.push(pair);
    add(pair, pair);
  }
  for (const key of keys) {
    const lower = key.toLowerCase();
    for (const group of SYNONYM_GROUPS) {
      if (!group.some((m) => m.toLowerCase() === lower)) continue;
      for (const member of group) add(member, key);
    }
  }
  return map;
}

function concatPhrases(must: KeywordMatchPhrase[], should: KeywordMatchPhrase[]): KeywordMatchPhrase[] {
  const list: KeywordMatchPhrase[] = [];
  const seen = new Set<string>();
  for (const p of [...must, ...should]) {
    const key = (p.phrase || '').trim().toLowerCase();
    if (!key || seen.has(key)) continue;
    seen.add(key);
    list.push(p);
  }
  return list;
}

function matchParts(phrase: string): string[] {
  return normalizeAudienceKeyword(phrase)
    .replace(/-/g, ' ')
    .replace(/ {2,}/g, ' ')
    .split(/\s+/)
    .filter(Boolean);
}

function tokenInTitle(title: Set<string>, token: string): boolean {
  if (title.has(token)) return true;
  const lower = token.toLowerCase();
  for (const group of SYNONYM_GROUPS) {
    if (!group.some((m) => m.toLowerCase() === lower)) continue;
    for (const m of group) {
      const parts = matchParts(m);
      if (parts.length === 1 && title.has(parts[0]!)) return true;
    }
  }
  return false;
}
