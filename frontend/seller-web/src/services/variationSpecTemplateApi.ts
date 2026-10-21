import api from './api';
import type { ApiResponse } from '../types/api';
import type { VariationSpecTemplate } from '../types/variationSpecTemplate';

export const variationSpecTemplateApi = {
  getOptions: async (specType?: string): Promise<ApiResponse<VariationSpecTemplate[]>> => {
    const response = await api.get<ApiResponse<VariationSpecTemplate[]>>(
      '/variation-spec-templates/options',
      { params: specType ? { specType } : undefined },
    );
    return response.data;
  },
};
