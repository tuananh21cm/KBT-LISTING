/** Query for GET /product-opportunities and GET /admin/product-opportunities. */
export interface ProductOpportunityListParams {
  page?: number;
  pageCurrent?: number;
  pageSize?: number;
  shopId?: string;
  /** Admin only — ignored on seller API. */
  ownerId?: string;
  q?: string;
  /** `volume` (default) | `online` | `order` | `gmv` | `name` | `synced` */
  sort?: string;
  /** `desc` (default for volume/online/order/gmv) | `asc` */
  sortDir?: string;
  /** When true, only rows whose keyword is saved for the selected shop (or a linked shop in scope). Filtering by shop also includes bookmarks that have no TikTok snapshot. */
  saved?: boolean;
  /** Filter rows that have any of these `leadTagTexts` (OR). CSV or array. */
  leadTag?: string | string[];
}

export interface ProductOpportunityCategory {
  cateId: string;
  cateName: string;
  level?: number | null;
}

export interface ProductOpportunityListItem {
  id: string;
  shopId: string;
  shopName: string;
  shopProfileName?: string | null;
  ownerId: string;
  ownerFullName?: string | null;
  ownerUserName?: string | null;
  leadId: string;
  leadName: string;
  picUrl?: string | null;
  picUrls?: string[] | null;
  searchVolume: number;
  onlineProducts?: number | null;
  onlineProductsRaw?: string | null;
  searchVolumeRingRatio?: string | null;
  onlineProductsRingRatio?: string | null;
  orderCount?: number | null;
  orderRaw?: string | null;
  orderRingRatio?: string | null;
  gmv?: number | null;
  gmvRaw?: string | null;
  gmvRingRatio?: string | null;
  categories?: ProductOpportunityCategory[] | null;
  leadTagTexts?: string[] | null;
  level1CateId?: string | null;
  level1CateName?: string | null;
  level2CateId?: string | null;
  level2CateName?: string | null;
  level3CateId?: string | null;
  level3CateName?: string | null;
  leadSourceTag?: string | null;
  leadClusterSourceTag?: string | null;
  opportunityType?: number | null;
  shopSyncedAt?: string | null;
  /** Shops in the current filter that link this keyword. */
  shopCount?: number | null;
  /** Saved for this row's shop — later used to generate AI search URLs. */
  isSaved?: boolean;
}

export interface ProductOpportunityListPage {
  items: ProductOpportunityListItem[];
  total: number;
  pageCurrent: number;
  pageSize: number;
  lastSyncedAt?: string | null;
  /** Distinct lead_tag_text values in the current shop/owner/q/saved scope. */
  leadTagOptions?: string[] | null;
}

export interface ProductOpportunityBulkDeleteResult {
  deleted: number;
}

export interface ProductOpportunitySavedResult {
  saved: boolean;
}

/** Body for POST …/product-opportunities/shop-research */
export interface ShopKeywordResearchRequest {
  shopId: string;
}

/** How «Shop này bán gì» picked keywords. */
export type ShopKeywordResearchSource = 'snapshot' | 'audienceSuggest';

/** One required / preferred match phrase plus synonym alternatives. */
export interface KeywordMatchPhrase {
  phrase: string;
  /** Hitting any of these on the Shein title/slug/cate counts. */
  anyOf: string[];
}

/** Positive score contributions for the drawer tooltip (0–100 parts). */
export interface ShopKeywordScoreBreakdown {
  /** Niche fit contribution (0–45). */
  audience: number;
  /** Season / weather contribution (0–5). */
  season: number;
  /** Demand (search volume vs the shop snapshot) contribution (0–30). */
  demand: number;
  /** Recent search growth (volume ring) contribution (0–15). Absent on legacy payloads. */
  growth?: number | null;
  /** Competition (online products sweet-spot) contribution (0–5). */
  competition: number;
}

/** One AI-picked snapshot keyword, or an audience-suggested search phrase. */
export interface ShopKeywordResearchItem {
  id: string;
  keyword: string;
  searchVolume: number;
  onlineProducts?: number | null;
  onlineProductsRaw?: string | null;
  searchVolumeRingRatio?: string | null;
  leadTagTexts?: string[] | null;
  picUrl?: string | null;
  picUrls?: string[] | null;
  rationale?: string | null;
  fit?: string | null;
  isSaved?: boolean;
  /** 1-based rank within the lane (`listNow` / `upcoming`). */
  rank?: number | null;
  /** Absolute 0–100 display score (snapshot). Null for audienceSuggest / legacy. */
  score?: number | null;
  /** Positive contributions for the score tooltip. */
  scoreBreakdown?: ShopKeywordScoreBreakdown | null;
  /** Sourcing query with shop body/size (SHEIN / paste). */
  searchQuery?: string | null;
  /** Organic SHEIN US pdsearch URL for `searchQuery`. */
  sheinUrl?: string | null;
  /**
   * Important Shein title phrases (at most 2 demand modifiers + primary garment). `anyOf` = canonical + synonyms.
   * Coverage gate: garment + `minDistinctiveHits` distinctive hits — not AND-all.
   */
  mustPhrases?: KeywordMatchPhrase[] | null;
  /** Hardware / style / extra garments; title score uses weighted coverage. */
  shouldPhrases?: KeywordMatchPhrase[] | null;
  /** Minimum distinctive hits (`ceil(N/2)`). Stamped with mustPhrases. */
  minDistinctiveHits?: number | null;
  /** AI-chosen required tokens persisted with the analysis. Empty → last content token. */
  mustTokens?: string[] | null;
}

/** AI «Shop này bán gì» result. */
export interface ShopKeywordResearch {
  /** `snapshot` when grounded on TikTok trending keywords; `audienceSuggest` when invented (empty or unusable snapshot). */
  source?: ShopKeywordResearchSource | string | null;
  analysisPoints: string[];
  warnings?: string[] | null;
  categories: string[];
  ageMin?: number | null;
  ageMax?: number | null;
  bodySizeCode?: string | null;
  styleCodes?: string[] | null;
  publishLimitPublished?: number | null;
  publishLimitTotal?: number | null;
  remainingSlots: number;
  snapshotCount: number;
  droppedGeneric: number;
  candidatesScored: number;
  season: string;
  weatherNote: string;
  upcomingEvents: string[];
  asOfDate: string;
  lastSyncedAt?: string | null;
  /** When this analysis was last written (ISO UTC). */
  analyzedAt?: string | null;
  /** True when the shop keyword snapshot is newer than this analysis. */
  snapshotChanged?: boolean;
  listNow: ShopKeywordResearchItem[];
  upcoming: ShopKeywordResearchItem[];
}

/** Body for PUT …/product-opportunities/saved-keywords */
export interface SetSavedKeywordRequest {
  shopId: string;
  keyword: string;
  saved: boolean;
}

/** GET …/product-opportunities/saved-keywords?shopId= */
export interface SavedKeywordItem {
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

export interface SavedKeywordList {
  items: SavedKeywordItem[];
}
