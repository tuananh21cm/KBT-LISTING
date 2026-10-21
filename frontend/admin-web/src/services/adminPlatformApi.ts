import api from './api';
import type { ApiResponse } from '../types/api';
import type {
  CreatePlatformRequest,
  Platform,
  PlatformOption,
  UpdatePlatformRequest,
} from '../types/platform';

export const adminPlatformApi = {
  getAll: async (): Promise<ApiResponse<Platform[]>> => {
    const response = await api.get<ApiResponse<Platform[]>>('/admin/platforms');
    return response.data;
  },

  getById: async (id: string): Promise<ApiResponse<Platform>> => {
    const response = await api.get<ApiResponse<Platform>>(`/admin/platforms/${id}`);
    return response.data;
  },

  getOptions: async (): Promise<ApiResponse<PlatformOption[]>> => {
    const response = await api.get<ApiResponse<PlatformOption[]>>('/admin/platforms/options');
    return response.data;
  },

  create: async (data: CreatePlatformRequest): Promise<ApiResponse<Platform>> => {
    const response = await api.post<ApiResponse<Platform>>('/admin/platforms', data);
    return response.data;
  },

  update: async (id: string, data: UpdatePlatformRequest): Promise<ApiResponse<Platform>> => {
    const response = await api.put<ApiResponse<Platform>>(`/admin/platforms/${id}`, data);
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(`/admin/platforms/${id}`);
    return response.data;
  },
};
