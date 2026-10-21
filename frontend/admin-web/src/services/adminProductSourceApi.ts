import api from './api';
import type { ApiResponse } from '../types/api';
import type {
  CreateProductSourceRequest,
  ProductSource,
  ProductSourceOption,
  UpdateProductSourceRequest,
} from '../types/productSource';

export const adminProductSourceApi = {
  getAll: async (): Promise<ApiResponse<ProductSource[]>> => {
    const response = await api.get<ApiResponse<ProductSource[]>>('/admin/product-sources');
    return response.data;
  },

  getById: async (id: string): Promise<ApiResponse<ProductSource>> => {
    const response = await api.get<ApiResponse<ProductSource>>(`/admin/product-sources/${id}`);
    return response.data;
  },

  getOptions: async (): Promise<ApiResponse<ProductSourceOption[]>> => {
    const response = await api.get<ApiResponse<ProductSourceOption[]>>('/admin/product-sources/options');
    return response.data;
  },

  create: async (data: CreateProductSourceRequest): Promise<ApiResponse<ProductSource>> => {
    const response = await api.post<ApiResponse<ProductSource>>('/admin/product-sources', data);
    return response.data;
  },

  update: async (id: string, data: UpdateProductSourceRequest): Promise<ApiResponse<ProductSource>> => {
    const response = await api.put<ApiResponse<ProductSource>>(`/admin/product-sources/${id}`, data);
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(`/admin/product-sources/${id}`);
    return response.data;
  },
};
