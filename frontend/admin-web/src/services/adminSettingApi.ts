import api from './api';
import type { ApiResponse } from '../types/api';
import type { CreateSettingRequest, Setting, UpdateSettingRequest } from '../types/setting';

export const adminSettingApi = {
  getAll: async (): Promise<ApiResponse<Setting[]>> => {
    const response = await api.get<ApiResponse<Setting[]>>('/admin/settings');
    return response.data;
  },

  getById: async (id: string): Promise<ApiResponse<Setting>> => {
    const response = await api.get<ApiResponse<Setting>>(`/admin/settings/${id}`);
    return response.data;
  },

  create: async (data: CreateSettingRequest): Promise<ApiResponse<Setting>> => {
    const response = await api.post<ApiResponse<Setting>>('/admin/settings', data);
    return response.data;
  },

  update: async (id: string, data: UpdateSettingRequest): Promise<ApiResponse<Setting>> => {
    const response = await api.put<ApiResponse<Setting>>(`/admin/settings/${id}`, data);
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(`/admin/settings/${id}`);
    return response.data;
  },

  /** Gửi tin TEST Add track vào Chat ID cấu hình (không stamp đơn). */
  testSheinDetailAddTrackNotify: async (): Promise<ApiResponse<null>> => {
    const response = await api.post<ApiResponse<null>>(
      '/admin/settings/shein-detail-sync/test-add-track-notify',
    );
    return response.data;
  },
};
