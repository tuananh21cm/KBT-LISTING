import api from './api';
import type { ApiResponse } from '../types/api';
import type { PlatformOption } from '../types/platform';

export const platformApi = {
  getOptions: async (): Promise<ApiResponse<PlatformOption[]>> => {
    const response = await api.get<ApiResponse<PlatformOption[]>>('/platforms/options');
    return response.data;
  },
};
