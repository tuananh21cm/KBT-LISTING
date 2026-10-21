import api from './api';
import type { ApiResponse } from '../types/api';
import type {
  Category,
  CategoryPath,
  CategorySyncResult,
  CategoryTreeNode,
  RecommendCategoryRequest,
  RecommendCategoryResult,
  SyncCategoriesRequest,
} from '../types/category';

export const adminCategoryApi = {
  getList: async (params?: {
    site?: string;
    search?: string;
    activeOnly?: boolean;
    take?: number;
  }): Promise<ApiResponse<Category[]>> => {
    const response = await api.get<ApiResponse<Category[]>>('/admin/categories', { params });
    return response.data;
  },

  getTree: async (params?: {
    site?: string;
    activeOnly?: boolean;
  }): Promise<ApiResponse<CategoryTreeNode[]>> => {
    const response = await api.get<ApiResponse<CategoryTreeNode[]>>('/admin/categories/tree', {
      params,
    });
    return response.data;
  },

  getChildren: async (params: {
    site: string;
    parentId?: string;
    activeOnly?: boolean;
  }): Promise<ApiResponse<CategoryTreeNode[]>> => {
    const response = await api.get<ApiResponse<CategoryTreeNode[]>>(
      '/admin/categories/children',
      { params },
    );
    return response.data;
  },

  getPath: async (params: {
    site: string;
    externalId: string;
    activeOnly?: boolean;
  }): Promise<ApiResponse<CategoryPath>> => {
    const response = await api.get<ApiResponse<CategoryPath>>('/admin/categories/path', {
      params,
    });
    return response.data;
  },

  sync: async (data: SyncCategoriesRequest): Promise<ApiResponse<CategorySyncResult>> => {
    const response = await api.post<ApiResponse<CategorySyncResult>>('/admin/categories/sync', data);
    return response.data;
  },

  recommend: async (data: RecommendCategoryRequest): Promise<ApiResponse<RecommendCategoryResult>> => {
    const response = await api.post<ApiResponse<RecommendCategoryResult>>(
      '/admin/categories/recommend',
      data,
    );
    return response.data;
  },
};
