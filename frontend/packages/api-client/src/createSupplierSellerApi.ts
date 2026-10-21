import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  SupplierSellerDetail,
  SupplierSellerListPage,
  SupplierSellerListParams,
} from '@drop/types/supplierSeller';

export type SupplierSellerApiBasePath = '/supplier-sellers' | '/admin/supplier-sellers';

export type SupplierSellerApi = {
  list: (params?: SupplierSellerListParams) => Promise<ApiResponse<SupplierSellerListPage>>;
  getById: (
    id: string,
    params?: SupplierSellerListParams,
  ) => Promise<ApiResponse<SupplierSellerDetail>>;
};

export function createSupplierSellerApi(
  http: AxiosInstance,
  opts: { basePath: SupplierSellerApiBasePath },
): SupplierSellerApi {
  const { basePath } = opts;

  return {
    list: async (params) => {
      const response = await http.get<ApiResponse<SupplierSellerListPage>>(basePath, { params });
      return response.data;
    },
    getById: async (id, params) => {
      const response = await http.get<ApiResponse<SupplierSellerDetail>>(`${basePath}/${id}`, {
        params,
      });
      return response.data;
    },
  };
}
