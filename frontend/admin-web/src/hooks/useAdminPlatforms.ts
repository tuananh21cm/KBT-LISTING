import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { adminPlatformApi } from '../services/adminPlatformApi';
import type { CreatePlatformRequest, UpdatePlatformRequest } from '../types/platform';

export const useAdminPlatforms = () =>
  useQuery({
    queryKey: ['adminPlatforms'],
    queryFn: async () => {
      const response = await adminPlatformApi.getAll();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
  });

export const usePlatformOptions = (enabled = true) =>
  useQuery({
    queryKey: ['platformOptions'],
    queryFn: async () => {
      const response = await adminPlatformApi.getOptions();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useCreateAdminPlatform = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreatePlatformRequest) => adminPlatformApi.create(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo nền tảng thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminPlatforms'] });
        queryClient.invalidateQueries({ queryKey: ['platformOptions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateAdminPlatform = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdatePlatformRequest }) =>
      adminPlatformApi.update(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật nền tảng thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminPlatforms'] });
        queryClient.invalidateQueries({ queryKey: ['platformOptions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteAdminPlatform = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => adminPlatformApi.delete(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa nền tảng thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminPlatforms'] });
        queryClient.invalidateQueries({ queryKey: ['platformOptions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};
