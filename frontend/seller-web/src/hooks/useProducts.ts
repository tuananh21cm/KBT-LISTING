import { useQuery } from '@tanstack/react-query';
import api from '../services/api';
import type { ApiResponse } from '../types/api';

export type ProductSourceOption = {
  id: string;
  code: string;
  name: string;
  logoUrl?: string;
  domains?: string;
};

/** Active product-source options for listing edit (extension / source picker). */
export const useProductSourceOptions = (enabled = true) =>
  useQuery({
    queryKey: ['productSourceOptions'],
    queryFn: async () => {
      const response = await api.get<ApiResponse<ProductSourceOption[]>>('/product-sources/options');
      if (!response.data.success) {
        throw new Error(response.data.message);
      }
      return response.data.data ?? [];
    },
    enabled,
  });
