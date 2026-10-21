import api from './api';
import type { ApiResponse } from '../types/api';
import type {
  Category,
  CategoryPath,
  CategoryTreeNode,
  RecommendCategoryRequest,
  RecommendCategoryResult,
} from '../types/category';

export const categoryApi = {
  getList: async (params?: {
    site?: string;
    search?: string;
    activeOnly?: boolean;
    take?: number;
  }): Promise<ApiResponse<Category[]>> => {
    const response = await api.get<ApiResponse<Category[]>>('/categories', { params });
    return response.data;
  },

  getTree: async (params?: {
    site?: string;
    activeOnly?: boolean;
  }): Promise<ApiResponse<CategoryTreeNode[]>> => {
    const response = await api.get<ApiResponse<CategoryTreeNode[]>>('/categories/tree', {
      params,
    });
    return response.data;
  },

  getChildren: async (params: {
    site: string;
    parentId?: string;
    activeOnly?: boolean;
  }): Promise<ApiResponse<CategoryTreeNode[]>> => {
    const response = await api.get<ApiResponse<CategoryTreeNode[]>>('/categories/children', {
      params,
    });
    return response.data;
  },

  getPath: async (params: {
    site: string;
    externalId: string;
    activeOnly?: boolean;
  }): Promise<ApiResponse<CategoryPath>> => {
    const response = await api.get<ApiResponse<CategoryPath>>('/categories/path', {
      params,
    });
    return response.data;
  },

  recommend: async (data: RecommendCategoryRequest): Promise<ApiResponse<RecommendCategoryResult>> => {
    const response = await api.post<ApiResponse<RecommendCategoryResult>>(
      '/categories/recommend',
      data,
    );
    return response.data;
  },
};
