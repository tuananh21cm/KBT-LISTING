import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  ProductOpportunityBulkDeleteResult,
  ProductOpportunityListPage,
  ProductOpportunityListParams,
  ProductOpportunitySavedResult,
  SavedKeywordList,
  ShopKeywordResearch,
} from '@drop/types/productOpportunity';

export type ProductOpportunityApiBasePath =
  | '/product-opportunities'
  | '/admin/product-opportunities';

export type ProductOpportunityApi = {
  list: (
    params?: ProductOpportunityListParams,
  ) => Promise<ApiResponse<ProductOpportunityListPage>>;
  bulkDelete: (
    ids: string[],
    shopId?: string,
  ) => Promise<ApiResponse<ProductOpportunityBulkDeleteResult>>;
  setSaved: (
    id: string,
    saved: boolean,
    shopId?: string,
  ) => Promise<ApiResponse<ProductOpportunitySavedResult>>;
  setSavedKeyword: (
    shopId: string,
    keyword: string,
    saved: boolean,
  ) => Promise<ApiResponse<ProductOpportunitySavedResult>>;
  listSavedKeywords: (shopId: string) => Promise<ApiResponse<SavedKeywordList>>;
  getShopResearch: (shopId: string) => Promise<ApiResponse<ShopKeywordResearch | null>>;
  shopResearch: (shopId: string) => Promise<ApiResponse<ShopKeywordResearch>>;
};

/**
 * Shared Product opportunities API client.
 * Admin: `/admin/product-opportunities` (all shops).
 * Seller: `/product-opportunities` (JWT owner-scoped).
 */
export function createProductOpportunityApi(
  http: AxiosInstance,
  opts: { basePath: ProductOpportunityApiBasePath },
): ProductOpportunityApi {
  const { basePath } = opts;

  return {
    list: async (params) => {
      const response = await http.get<ApiResponse<ProductOpportunityListPage>>(basePath, {
        params,
      });
      return response.data;
    },
    bulkDelete: async (ids, shopId) => {
      const response = await http.post<ApiResponse<ProductOpportunityBulkDeleteResult>>(
        `${basePath}/bulk-delete`,
        { ids, shopId: shopId || undefined },
      );
      return response.data;
    },
    setSaved: async (id, saved, shopId) => {
      const response = await http.put<ApiResponse<ProductOpportunitySavedResult>>(
        `${basePath}/${id}/saved`,
        { saved, shopId: shopId || undefined },
      );
      return response.data;
    },
    setSavedKeyword: async (shopId, keyword, saved) => {
      const response = await http.put<ApiResponse<ProductOpportunitySavedResult>>(
        `${basePath}/saved-keywords`,
        { shopId, keyword, saved },
      );
      return response.data;
    },
    listSavedKeywords: async (shopId) => {
      const response = await http.get<ApiResponse<SavedKeywordList>>(
        `${basePath}/saved-keywords`,
        { params: { shopId } },
      );
      return response.data;
    },
    getShopResearch: async (shopId) => {
      const response = await http.get<ApiResponse<ShopKeywordResearch | null>>(
        `${basePath}/shop-research`,
        { params: { shopId } },
      );
      return response.data;
    },
    shopResearch: async (shopId) => {
      const response = await http.post<ApiResponse<ShopKeywordResearch>>(
        `${basePath}/shop-research`,
        { shopId },
      );
      return response.data;
    },
  };
}
