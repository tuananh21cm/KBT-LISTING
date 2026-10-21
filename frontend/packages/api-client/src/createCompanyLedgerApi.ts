import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  CompanyLedgerCategory,
  CompanyLedgerList,
  CompanyLedgerQuery,
  CompanyLedgerEntry,
  SaveCompanyLedgerEntryRequest,
} from '@drop/types/companyLedger';

export type CompanyLedgerApiBasePath = '/company-ledger' | '/admin/company-ledger';

export type CompanyLedgerApi = {
  list: (query?: CompanyLedgerQuery) => Promise<ApiResponse<CompanyLedgerList>>;
  getCategories: () => Promise<ApiResponse<CompanyLedgerCategory[]>>;
  create?: (data: SaveCompanyLedgerEntryRequest) => Promise<ApiResponse<CompanyLedgerEntry>>;
  update?: (
    id: string,
    data: SaveCompanyLedgerEntryRequest,
  ) => Promise<ApiResponse<CompanyLedgerEntry>>;
  delete?: (id: string) => Promise<ApiResponse<null>>;
};

/**
 * Company income/expense ledger.
 * Admin: `/admin/company-ledger` (CRUD). Seller: `/company-ledger` (read-only, assigned rows).
 */
export function createCompanyLedgerApi(
  http: AxiosInstance,
  opts: { basePath: CompanyLedgerApiBasePath },
): CompanyLedgerApi {
  const { basePath } = opts;
  const isAdmin = basePath === '/admin/company-ledger';

  const api: CompanyLedgerApi = {
    list: async (query) => {
      const response = await http.get<ApiResponse<CompanyLedgerList>>(basePath, { params: query });
      return response.data;
    },
    getCategories: async () => {
      const response = await http.get<ApiResponse<CompanyLedgerCategory[]>>(`${basePath}/categories`);
      return response.data;
    },
  };

  if (isAdmin) {
    api.create = async (data) => {
      const response = await http.post<ApiResponse<CompanyLedgerEntry>>(basePath, data);
      return response.data;
    };
    api.update = async (id, data) => {
      const response = await http.put<ApiResponse<CompanyLedgerEntry>>(`${basePath}/${id}`, data);
      return response.data;
    };
    api.delete = async (id) => {
      const response = await http.delete<ApiResponse<null>>(`${basePath}/${id}`);
      return response.data;
    };
  }

  return api;
}
