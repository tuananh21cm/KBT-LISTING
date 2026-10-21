import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  FourSellerAuthRedirectResponse,
  FourSellerShopCheckResponse,
  FourSellerShopRecord,
  FourSellerSyncResponse,
} from '@drop/types/fourSeller';

export type FourSellerApiBasePath = '/fourseller' | '/admin/fourseller';

export type FourSellerApi = {
  getAuthRedirectUrl: (shopId: string) => Promise<ApiResponse<FourSellerAuthRedirectResponse>>;
  checkConnection: (shopId: string) => Promise<ApiResponse<FourSellerShopCheckResponse>>;
  syncShops: () => Promise<ApiResponse<FourSellerSyncResponse>>;
  /** Admin only — live 4Seller shop list for crawl shopIdList. */
  getShopList?: () => Promise<ApiResponse<FourSellerShopRecord[]>>;
};

/**
 * Shared 4Seller shop-connect API client.
 * Admin: `/admin/fourseller` (+ getShopList).
 * Seller: `/fourseller` (auth / check / sync only).
 */
export function createFourSellerApi(
  http: AxiosInstance,
  opts: { basePath: FourSellerApiBasePath },
): FourSellerApi {
  const { basePath } = opts;
  const isAdmin = basePath === '/admin/fourseller';

  const api: FourSellerApi = {
    getAuthRedirectUrl: async (shopId) => {
      const response = await http.post<ApiResponse<FourSellerAuthRedirectResponse>>(
        `${basePath}/shops/${shopId}/auth-redirect-url`,
      );
      return response.data;
    },

    checkConnection: async (shopId) => {
      const response = await http.post<ApiResponse<FourSellerShopCheckResponse>>(
        `${basePath}/shops/${shopId}/check-connection`,
      );
      return response.data;
    },

    syncShops: async () => {
      const response = await http.post<ApiResponse<FourSellerSyncResponse>>(
        `${basePath}/shops/sync`,
      );
      return response.data;
    },
  };

  if (isAdmin) {
    api.getShopList = async () => {
      const response = await http.get<ApiResponse<FourSellerShopRecord[]>>(
        `${basePath}/shops`,
      );
      return response.data;
    };
  }

  return api;
}
