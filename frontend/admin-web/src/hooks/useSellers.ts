import { useQuery } from '@tanstack/react-query';
import { adminUserApi } from '../services/adminUserApi';

export const useSellers = (enabled = true) =>
  useQuery({
    queryKey: ['sellers'],
    queryFn: async () => {
      const response = await adminUserApi.getSellers();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });
