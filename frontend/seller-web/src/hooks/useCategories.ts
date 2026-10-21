import { useMutation, useQuery } from '@tanstack/react-query';
import { message } from 'antd';
import { categoryApi } from '../services/categoryApi';
import type { RecommendCategoryRequest } from '../types/category';

export const useCategoryTree = (site: string, enabled = true) =>
  useQuery({
    queryKey: ['categoryTree', site],
    queryFn: async () => {
      const response = await categoryApi.getTree({ site, activeOnly: true });
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled: enabled && !!site,
  });

export const useRecommendCategory = () =>
  useMutation({
    mutationFn: (data: RecommendCategoryRequest) => categoryApi.recommend(data),
    onError: (error: Error) => message.error(error.message),
  });
