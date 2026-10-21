import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  ProductDiscountListPage,
  ProductDiscountListParams,
  ProductDiscountSyncJobStatus,
  ProductDiscountSyncRequest,
} from '@drop/types/productDiscount';

export type PromotionsApiBasePath =
  | '/promotions/product-discounts'
  | '/admin/promotions/product-discounts'
  | '/promotions/flash-deals'
  | '/admin/promotions/flash-deals';

export type FlashDealsApiBasePath = '/promotions/flash-deals' | '/admin/promotions/flash-deals';

export type PromotionsApi = {
  list: (params?: ProductDiscountListParams) => Promise<ApiResponse<ProductDiscountListPage>>;
  sync: (data?: ProductDiscountSyncRequest) => Promise<ApiResponse<ProductDiscountSyncJobStatus>>;
  syncStatus: (jobId: string) => Promise<ApiResponse<ProductDiscountSyncJobStatus>>;
  deactivate: (id: string) => Promise<ApiResponse<unknown>>;
};

/**
 * Shared promotion activity API client (Product Discount or Flash Deal).
 * Admin: `/admin/promotions/product-discounts` | `/admin/promotions/flash-deals`
 * Seller: `/promotions/product-discounts` | `/promotions/flash-deals`
 */
export function createPromotionsApi(
  http: AxiosInstance,
  opts: { basePath: PromotionsApiBasePath },
): PromotionsApi {
  const { basePath } = opts;

  return {
    list: async (params) => {
      const response = await http.get<ApiResponse<ProductDiscountListPage>>(basePath, { params });
      return response.data;
    },
    sync: async (data = {}) => {
      const response = await http.post<ApiResponse<ProductDiscountSyncJobStatus>>(
        `${basePath}/sync`,
        data,
      );
      return response.data;
    },
    syncStatus: async (jobId) => {
      const response = await http.get<ApiResponse<ProductDiscountSyncJobStatus>>(
        `${basePath}/sync/status/${jobId}`,
      );
      return response.data;
    },
    deactivate: async (id) => {
      const response = await http.post<ApiResponse<unknown>>(`${basePath}/${id}/deactivate`);
      return response.data;
    },
  };
}

/** Alias for Flash Deal endpoints — same client shape as Product Discount. */
export function createFlashDealsApi(
  http: AxiosInstance,
  opts: { basePath: FlashDealsApiBasePath },
): PromotionsApi {
  return createPromotionsApi(http, opts);
}
