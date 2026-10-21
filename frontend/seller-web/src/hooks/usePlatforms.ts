import { useQuery } from '@tanstack/react-query';
import { platformApi } from '../services/platformApi';

export const usePlatformOptions = (enabled = true) =>
  useQuery({
    queryKey: ['platformOptions'],
    queryFn: async () => {
      const response = await platformApi.getOptions();
      if (!response.success) {
        throw new Error(response.message || 'Không tải được danh sách nền tảng.');
      }
      return response.data ?? [];
    },
    enabled,
  });
