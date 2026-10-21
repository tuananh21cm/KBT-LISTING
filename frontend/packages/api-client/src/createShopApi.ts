import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  CreateShopRequest,
  ReorderShopsRequest,
  Shop,
  ShopDetail,
  ShopListItem,
  ShopOption,
  UpdateAdminShopRequest,
  UpdateSellerShopRequest,
  UpdateShopCatalogProfileRequest,
  ShopCatalogProfile,
  AgeRange,
  AutoPublishSystemConfig,
  UpdateShopAutoPublishRequest,
  ShopAutoPublish,
  ShopAutoPublishSchedulerStatusBatch,
  ShopOwnershipTransferResult,
  ShopMappingHealth,
} from '@drop/types/shop';
import type { ShopFinance, ShopFinanceQuery, SetShopAcquisitionCostRequest } from '@drop/types/shopFinance';
import type { CompanyLedgerEntry } from '@drop/types/companyLedger';

export type ShopApiBasePath = '/shops' | '/admin/shops';

export type ShopApi = {
  list: () => Promise<ApiResponse<ShopListItem[]>>;
  getById: (id: string) => Promise<ApiResponse<ShopDetail>>;
  update: (
    id: string,
    data: UpdateAdminShopRequest | UpdateSellerShopRequest,
  ) => Promise<ApiResponse<ShopDetail>>;
  updateCatalogProfile: (
    id: string,
    data: UpdateShopCatalogProfileRequest,
  ) => Promise<ApiResponse<ShopCatalogProfile>>;
  updateAutoPublish: (
    id: string,
    data: UpdateShopAutoPublishRequest,
  ) => Promise<ApiResponse<ShopAutoPublish>>;
  /** Seeded audience age brackets for catalog Select. */
  listAgeRanges: () => Promise<ApiResponse<AgeRange[]>>;
  /** On hold snapshot + ledger tiền đã về (admin + seller). */
  getFinance: (params?: ShopFinanceQuery) => Promise<ApiResponse<ShopFinance>>;
  /** Admin only — upsert the unique shop purchase-cost ledger row. */
  setAcquisitionCost?: (
    id: string,
    data: SetShopAcquisitionCostRequest,
  ) => Promise<ApiResponse<CompanyLedgerEntry | null>>;
  /** Admin only. */
  getOptions?: () => Promise<ApiResponse<ShopOption[]>>;
  /** Admin only — gate shop auto publish switch. */
  getAutoPublishSystemConfig?: () => Promise<ApiResponse<AutoPublishSystemConfig>>;
  /** Lightweight scheduler badges for background list poll. */
  autoPublishSchedulerStatus: (ids: string[]) => Promise<ApiResponse<ShopAutoPublishSchedulerStatusBatch>>;
  create?: (data: CreateShopRequest) => Promise<ApiResponse<ShopDetail>>;
  reorder?: (data: ReorderShopsRequest) => Promise<ApiResponse<null>>;
  delete?: (id: string) => Promise<ApiResponse<null>>;
  /** Admin only — backfill Orders/Listings OwnerId onto current shop owner. */
  reassignOwnedData?: (id: string) => Promise<ApiResponse<ShopOwnershipTransferResult>>;
  /** Admin only — backfill ownership for all mismatched shops. */
  reassignAllOwnedData?: () => Promise<ApiResponse<ShopOwnershipTransferResult>>;
  /** Admin only — orders with null ShopId grouped by 4Seller shop id. */
  getMappingHealth?: () => Promise<ApiResponse<ShopMappingHealth>>;
};

/**
 * Shared shop API client.
 * Admin: `/admin/shops` (+ options / create / reorder / delete + finance).
 * Seller: `/shops` (list / get / update / finance).
 */
export function createShopApi(
  http: AxiosInstance,
  opts: { basePath: ShopApiBasePath },
): ShopApi {
  const { basePath } = opts;
  const isAdmin = basePath === '/admin/shops';
  const ageRangesPath = isAdmin ? '/admin/age-ranges' : '/age-ranges';

  const api: ShopApi = {
    list: async () => {
      const response = await http.get<ApiResponse<ShopListItem[]>>(basePath);
      return response.data;
    },

    getById: async (id: string) => {
      const response = await http.get<ApiResponse<ShopDetail>>(`${basePath}/${id}`);
      return response.data;
    },

    update: async (id, data) => {
      const response = await http.put<ApiResponse<ShopDetail>>(`${basePath}/${id}`, data);
      return response.data;
    },

    updateCatalogProfile: async (id, data) => {
      const response = await http.put<ApiResponse<ShopCatalogProfile>>(
        `${basePath}/${id}/catalog-profile`,
        data,
      );
      return response.data;
    },

    updateAutoPublish: async (id, data) => {
      const response = await http.put<ApiResponse<ShopAutoPublish>>(
        `${basePath}/${id}/auto-publish`,
        data,
      );
      return response.data;
    },

    listAgeRanges: async () => {
      const response = await http.get<ApiResponse<AgeRange[]>>(ageRangesPath);
      return response.data;
    },

    getFinance: async (params) => {
      const response = await http.get<ApiResponse<ShopFinance>>(`${basePath}/finance`, {
        params,
      });
      return response.data;
    },

    getAutoPublishSystemConfig: async () => {
      const response = await http.get<ApiResponse<AutoPublishSystemConfig>>(
        `${basePath}/auto-publish/system-config`,
      );
      return response.data;
    },

    autoPublishSchedulerStatus: async (ids) => {
      const response = await http.get<ApiResponse<ShopAutoPublishSchedulerStatusBatch>>(
        `${basePath}/auto-publish-scheduler-status`,
        {
          params: { ids },
          paramsSerializer: {
            indexes: null,
          },
        },
      );
      return response.data;
    },
  };

  if (isAdmin) {
    api.getOptions = async () => {
      const response = await http.get<ApiResponse<ShopOption[]>>(`${basePath}/options`);
      return response.data;
    };

    api.setAcquisitionCost = async (id, data) => {
      const response = await http.put<ApiResponse<CompanyLedgerEntry | null>>(
        `${basePath}/${id}/acquisition-cost`,
        data,
      );
      return response.data;
    };

    api.create = async (data: CreateShopRequest) => {
      const response = await http.post<ApiResponse<ShopDetail>>(basePath, data);
      return response.data;
    };

    api.reorder = async (data: ReorderShopsRequest) => {
      const response = await http.put<ApiResponse<null>>(`${basePath}/reorder`, data);
      return response.data;
    };

    api.delete = async (id: string) => {
      const response = await http.delete<ApiResponse<null>>(`${basePath}/${id}`);
      return response.data;
    };

    api.reassignOwnedData = async (id: string) => {
      const response = await http.post<ApiResponse<ShopOwnershipTransferResult>>(
        `${basePath}/${id}/reassign-owned-data`,
      );
      return response.data;
    };

    api.reassignAllOwnedData = async () => {
      const response = await http.post<ApiResponse<ShopOwnershipTransferResult>>(
        `${basePath}/reassign-all-owned-data`,
      );
      return response.data;
    };

    api.getMappingHealth = async () => {
      const response = await http.get<ApiResponse<ShopMappingHealth>>(
        `${basePath}/mapping-health`,
      );
      return response.data;
    };
  }

  return api;
}

/** Seller list may return fuller `Shop` rows — compatible with ShopListItem. */
export type { Shop };
