import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { adminProductSourceApi } from '../services/adminProductSourceApi';
import type { CreateProductSourceRequest, UpdateProductSourceRequest } from '../types/productSource';

export const useAdminProductSources = () =>
  useQuery({
    queryKey: ['adminProductSources'],
    queryFn: async () => {
      const response = await adminProductSourceApi.getAll();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
  });

export const useProductSourceOptions = (enabled = true) =>
  useQuery({
    queryKey: ['productSourceOptions'],
    queryFn: async () => {
      const response = await adminProductSourceApi.getOptions();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled,
  });

export const useCreateAdminProductSource = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateProductSourceRequest) => adminProductSourceApi.create(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo nguồn hàng thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminProductSources'] });
        queryClient.invalidateQueries({ queryKey: ['productSourceOptions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateAdminProductSource = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateProductSourceRequest }) =>
      adminProductSourceApi.update(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật nguồn hàng thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminProductSources'] });
        queryClient.invalidateQueries({ queryKey: ['productSourceOptions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteAdminProductSource = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => adminProductSourceApi.delete(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa nguồn hàng thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminProductSources'] });
        queryClient.invalidateQueries({ queryKey: ['productSourceOptions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};
