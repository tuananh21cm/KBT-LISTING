import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { adminSettingApi } from '../services/adminSettingApi';
import type { CreateSettingRequest, UpdateSettingRequest } from '../types/setting';

export const useAdminSettings = (options?: { enabled?: boolean }) =>
  useQuery({
    queryKey: ['adminSettings'],
    enabled: options?.enabled ?? true,
    queryFn: async () => {
      const response = await adminSettingApi.getAll();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
  });

export const useCreateAdminSetting = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateSettingRequest) => adminSettingApi.create(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo cấu hình thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminSettings'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateAdminSetting = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateSettingRequest }) =>
      adminSettingApi.update(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật cấu hình thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminSettings'] });
        queryClient.invalidateQueries({ queryKey: ['listingRewriteConfig'] });
        void queryClient.invalidateQueries({ queryKey: ['adminAutoPublishSystemConfig'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteAdminSetting = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => adminSettingApi.delete(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa cấu hình thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminSettings'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};
