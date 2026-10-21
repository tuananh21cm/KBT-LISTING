import api from './api';
import type { ApiResponse } from '../types/api';
import type { CheckDuplicateRequest, UserFile } from '../types/file';

export const fileApi = {
  getAll: async (): Promise<ApiResponse<UserFile[]>> => {
    const response = await api.get<ApiResponse<UserFile[]>>('/files');
    return response.data;
  },

  getById: async (id: string): Promise<ApiResponse<UserFile>> => {
    const response = await api.get<ApiResponse<UserFile>>(`/files/${id}`);
    return response.data;
  },

  upload: async (
    file: File,
    options?: { linkedEntityType?: string; linkedEntityId?: string; metadata?: string },
  ): Promise<ApiResponse<UserFile>> => {
    const formData = new FormData();
    formData.append('file', file);
    if (options?.linkedEntityType) {
      formData.append('linkedEntityType', options.linkedEntityType);
    }
    if (options?.linkedEntityId) {
      formData.append('linkedEntityId', options.linkedEntityId);
    }
    if (options?.metadata) {
      formData.append('metadata', options.metadata);
    }

    const response = await api.post<ApiResponse<UserFile>>('/files/upload', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
    return response.data;
  },

  checkDuplicate: async (data: CheckDuplicateRequest): Promise<ApiResponse<UserFile | null>> => {
    const response = await api.post<ApiResponse<UserFile | null>>('/files/check-duplicate', data);
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(`/files/${id}`);
    return response.data;
  },
};
