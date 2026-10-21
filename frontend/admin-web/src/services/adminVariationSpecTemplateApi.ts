import api from './api';
import type { ApiResponse } from '../types/api';
import type {
  CreateVariationSpecTemplateRequest,
  ReorderVariationSpecTemplatesRequest,
  UpdateVariationSpecTemplateRequest,
  VariationSpecTemplate,
} from '../types/variationSpecTemplate';

export const adminVariationSpecTemplateApi = {
  getAll: async (specType?: string): Promise<ApiResponse<VariationSpecTemplate[]>> => {
    const response = await api.get<ApiResponse<VariationSpecTemplate[]>>(
      '/admin/variation-spec-templates',
      { params: specType ? { specType } : undefined },
    );
    return response.data;
  },

  getOptions: async (specType?: string): Promise<ApiResponse<VariationSpecTemplate[]>> => {
    const response = await api.get<ApiResponse<VariationSpecTemplate[]>>(
      '/admin/variation-spec-templates/options',
      { params: specType ? { specType } : undefined },
    );
    return response.data;
  },

  create: async (
    data: CreateVariationSpecTemplateRequest,
  ): Promise<ApiResponse<VariationSpecTemplate>> => {
    const response = await api.post<ApiResponse<VariationSpecTemplate>>(
      '/admin/variation-spec-templates',
      data,
    );
    return response.data;
  },

  update: async (
    id: string,
    data: UpdateVariationSpecTemplateRequest,
  ): Promise<ApiResponse<VariationSpecTemplate>> => {
    const response = await api.put<ApiResponse<VariationSpecTemplate>>(
      `/admin/variation-spec-templates/${id}`,
      data,
    );
    return response.data;
  },

  reorder: async (data: ReorderVariationSpecTemplatesRequest): Promise<ApiResponse<null>> => {
    const response = await api.put<ApiResponse<null>>('/admin/variation-spec-templates/reorder', data);
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(`/admin/variation-spec-templates/${id}`);
    return response.data;
  },
};
