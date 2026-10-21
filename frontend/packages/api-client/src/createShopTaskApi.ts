import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  MarkShopHealthRecordHandledPayload,
  ReorderShopTaskDefinitionsRequest,
  SaveShopTaskDefinitionPayload,
  ShopTaskBoard,
  ShopTaskBoardParams,
  ShopTaskDefinition,
} from '@drop/types/shopTask';

export type ShopTaskApiBasePath = '/shop-tasks' | '/admin/shop-tasks';

export type ShopTaskApi = {
  /** Bảng nhiệm vụ theo shop trong một ngày VN. */
  board: (params?: ShopTaskBoardParams) => Promise<ApiResponse<ShopTaskBoard>>;
  /** Danh sách nhiệm vụ truy cập URL — chỉ portal admin gọi được. */
  listDefinitions: () => Promise<ApiResponse<ShopTaskDefinition[]>>;
  createDefinition: (
    payload: SaveShopTaskDefinitionPayload,
  ) => Promise<ApiResponse<ShopTaskDefinition>>;
  updateDefinition: (
    id: string,
    payload: SaveShopTaskDefinitionPayload,
  ) => Promise<ApiResponse<ShopTaskDefinition>>;
  deleteDefinition: (id: string) => Promise<ApiResponse<null>>;
  /** Sắp xếp lại thứ tự nhiệm vụ truy cập URL — chỉ portal admin. */
  reorderDefinitions: (payload: ReorderShopTaskDefinitionsRequest) => Promise<ApiResponse<null>>;
  /** Đánh dấu violation/warning Health Center đã xử lý. */
  markHealthRecordHandled: (
    payload: MarkShopHealthRecordHandledPayload,
  ) => Promise<ApiResponse<null>>;
};

/**
 * Shared API client cho nhiệm vụ hệ thống.
 * Admin: `/admin/shop-tasks` (mọi shop + CRUD nhiệm vụ).
 * Seller: `/shop-tasks` (JWT owner-scoped, chỉ đọc bảng).
 */
export function createShopTaskApi(
  http: AxiosInstance,
  opts: { basePath: ShopTaskApiBasePath },
): ShopTaskApi {
  const { basePath } = opts;

  return {
    board: async (params) => {
      const response = await http.get<ApiResponse<ShopTaskBoard>>(`${basePath}/board`, { params });
      return response.data;
    },
    listDefinitions: async () => {
      const response = await http.get<ApiResponse<ShopTaskDefinition[]>>(`${basePath}/definitions`);
      return response.data;
    },
    createDefinition: async (payload) => {
      const response = await http.post<ApiResponse<ShopTaskDefinition>>(
        `${basePath}/definitions`,
        payload,
      );
      return response.data;
    },
    updateDefinition: async (id, payload) => {
      const response = await http.put<ApiResponse<ShopTaskDefinition>>(
        `${basePath}/definitions/${id}`,
        payload,
      );
      return response.data;
    },
    deleteDefinition: async (id) => {
      const response = await http.delete<ApiResponse<null>>(`${basePath}/definitions/${id}`);
      return response.data;
    },
    reorderDefinitions: async (payload) => {
      const response = await http.put<ApiResponse<null>>(
        `${basePath}/definitions/reorder`,
        payload,
      );
      return response.data;
    },
    markHealthRecordHandled: async (payload) => {
      const response = await http.post<ApiResponse<null>>(
        `${basePath}/health-records/handled`,
        payload,
      );
      return response.data;
    },
  };
}
