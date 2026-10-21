import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { adminCategoryApi } from '../services/adminCategoryApi';
import type { RecommendCategoryRequest, SyncCategoriesRequest } from '../types/category';
import { getMutationErrorMessage } from '../utils/apiError';

export const useAdminCategories = (site: string, search?: string, enabled = true) =>
  useQuery({
    queryKey: ['adminCategories', site, search ?? ''],
    queryFn: async () => {
      const response = await adminCategoryApi.getList({
        site,
        search: search || undefined,
        activeOnly: true,
      });
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled,
  });

export const useCategoryTree = (site: string, enabled = true) =>
  useQuery({
    queryKey: ['adminCategoryTree', site],
    queryFn: async () => {
      const response = await adminCategoryApi.getTree({ site, activeOnly: true });
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled: enabled && !!site,
  });

export const useSyncCategories = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: SyncCategoriesRequest) => adminCategoryApi.sync(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đồng bộ category thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminCategories'] });
        queryClient.invalidateQueries({ queryKey: ['adminCategoryTree'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useRecommendCategory = () =>
  useMutation({
    mutationFn: (data: RecommendCategoryRequest) => adminCategoryApi.recommend(data),
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
