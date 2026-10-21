import type { ShopKeywordResearch, ShopKeywordResearchItem } from '@drop/types/productOpportunity';

export const patchShopResearchSaved = (
  data: ShopKeywordResearch,
  id: string,
  saved: boolean,
): ShopKeywordResearch => ({
  ...data,
  listNow: data.listNow.map((row) => (row.id === id ? { ...row, isSaved: saved } : row)),
  upcoming: data.upcoming.map((row) => (row.id === id ? { ...row, isSaved: saved } : row)),
});

const sameKeyword = (row: ShopKeywordResearchItem, id?: string, keyword?: string) => {
  if (id && row.id === id) return true;
  const key = keyword?.trim().toLowerCase();
  return !!key && row.keyword.trim().toLowerCase() === key;
};

/** Drop a keyword from cached «Shop này bán gì» lists (audience-suggest unsave / bulk-delete). */
export const removeShopResearchKeywords = (
  data: ShopKeywordResearch,
  keyword: string,
  id?: string,
): ShopKeywordResearch => ({
  ...data,
  listNow: data.listNow.filter((row) => !sameKeyword(row, id, keyword)),
  upcoming: data.upcoming.filter((row) => !sameKeyword(row, id, keyword)),
});
