import api from './api';
import type { ApiResponse } from '../types/api';
import type {
  CreateShippingTemplateRequest,
  ShippingTemplate,
  UpdateShippingTemplateRequest,
} from '../types/shippingTemplate';

export const shippingTemplateApi = {
  getAll: async (): Promise<ApiResponse<ShippingTemplate[]>> => {
    const response = await api.get<ApiResponse<ShippingTemplate[]>>('/shipping-templates');
    return response.data;
  },

  create: async (
    data: CreateShippingTemplateRequest,
  ): Promise<ApiResponse<ShippingTemplate>> => {
    const response = await api.post<ApiResponse<ShippingTemplate>>('/shipping-templates', data);
    return response.data;
  },

  update: async (
    id: string,
    data: UpdateShippingTemplateRequest,
  ): Promise<ApiResponse<ShippingTemplate>> => {
    const response = await api.put<ApiResponse<ShippingTemplate>>(
      `/shipping-templates/${id}`,
      data,
    );
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(`/shipping-templates/${id}`);
    return response.data;
  },
};
