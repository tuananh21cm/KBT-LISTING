import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { adminVariationSpecTemplateApi } from '../services/adminVariationSpecTemplateApi';
import type {
  CreateVariationSpecTemplateRequest,
  ReorderVariationSpecTemplatesRequest,
  UpdateVariationSpecTemplateRequest,
} from '../types/variationSpecTemplate';

export const variationSpecTemplateKeys = {
  all: ['variationSpecTemplates'] as const,
  options: ['variationSpecTemplateOptions'] as const,
  byType: (specType?: string) => ['variationSpecTemplates', specType ?? 'all'] as const,
};

export const useVariationSpecTemplates = (specType?: string, enabled = true) =>
  useQuery({
    queryKey: variationSpecTemplateKeys.byType(specType),
    queryFn: async () => {
      const response = await adminVariationSpecTemplateApi.getAll(specType);
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
  });

export const useVariationSpecTemplateOptions = (enabled = true) =>
  useQuery({
    queryKey: variationSpecTemplateKeys.options,
    queryFn: async () => {
      const response = await adminVariationSpecTemplateApi.getOptions();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
  });

export const useCreateVariationSpecTemplate = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateVariationSpecTemplateRequest) =>
      adminVariationSpecTemplateApi.create(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo template thành công.');
        void queryClient.invalidateQueries({ queryKey: variationSpecTemplateKeys.all });
        void queryClient.invalidateQueries({ queryKey: variationSpecTemplateKeys.options });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateVariationSpecTemplate = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateVariationSpecTemplateRequest }) =>
      adminVariationSpecTemplateApi.update(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật template thành công.');
        void queryClient.invalidateQueries({ queryKey: variationSpecTemplateKeys.all });
        void queryClient.invalidateQueries({ queryKey: variationSpecTemplateKeys.options });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteVariationSpecTemplate = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminVariationSpecTemplateApi.delete(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa template thành công.');
        void queryClient.invalidateQueries({ queryKey: variationSpecTemplateKeys.all });
        void queryClient.invalidateQueries({ queryKey: variationSpecTemplateKeys.options });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useReorderVariationSpecTemplates = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: ReorderVariationSpecTemplatesRequest) =>
      adminVariationSpecTemplateApi.reorder(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Sắp xếp thành công.');
        void queryClient.invalidateQueries({ queryKey: variationSpecTemplateKeys.all });
        void queryClient.invalidateQueries({ queryKey: variationSpecTemplateKeys.options });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};
