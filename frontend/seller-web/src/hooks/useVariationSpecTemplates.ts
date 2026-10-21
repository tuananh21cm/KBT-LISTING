import { useQuery } from '@tanstack/react-query';
import { variationSpecTemplateApi } from '../services/variationSpecTemplateApi';

export const useVariationSpecTemplateOptions = (enabled = true) =>
  useQuery({
    queryKey: ['variationSpecTemplateOptions'],
    queryFn: async () => {
      const response = await variationSpecTemplateApi.getOptions();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
  });
