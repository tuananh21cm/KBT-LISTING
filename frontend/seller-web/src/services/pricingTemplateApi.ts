import api from './api';
import type { ApiResponse } from '../types/api';
import type {
  CreatePricingTemplateRequest,
  PricingTemplate,
  UpdatePricingTemplateRequest,
} from '../types/pricingTemplate';

export const pricingTemplateApi = {
  getAll: async (): Promise<ApiResponse<PricingTemplate[]>> => {
    const response = await api.get<ApiResponse<PricingTemplate[]>>('/pricing-templates');
    return response.data;
  },

  create: async (
    data: CreatePricingTemplateRequest,
  ): Promise<ApiResponse<PricingTemplate>> => {
    const response = await api.post<ApiResponse<PricingTemplate>>('/pricing-templates', data);
    return response.data;
  },

  update: async (
    id: string,
    data: UpdatePricingTemplateRequest,
  ): Promise<ApiResponse<PricingTemplate>> => {
    const response = await api.put<ApiResponse<PricingTemplate>>(
      `/pricing-templates/${id}`,
      data,
    );
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(`/pricing-templates/${id}`);
    return response.data;
  },
};
