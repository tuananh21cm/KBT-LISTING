import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { adminRoleApi } from '../services/adminRoleApi';
import type {
  CreateRoleRequest,
  SetRolePermissionsRequest,
  UpdateRoleRequest,
} from '../types/role';
import { getMutationErrorMessage } from '../utils/apiError';

export const useAdminRoles = () =>
  useQuery({
    queryKey: ['adminRoles'],
    queryFn: async () => {
      const response = await adminRoleApi.getAll();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
  });

export const useAdminRole = (roleId: string | null) =>
  useQuery({
    queryKey: ['adminRole', roleId],
    queryFn: async () => {
      if (!roleId) return null;
      const response = await adminRoleApi.getById(roleId);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    enabled: !!roleId,
  });

export const useAdminPermissions = () =>
  useQuery({
    queryKey: ['adminPermissions'],
    queryFn: async () => {
      const response = await adminRoleApi.getPermissions();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
  });

export const useCreateAdminRole = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateRoleRequest) => adminRoleApi.create(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo vai trò thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminRoles'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useUpdateAdminRole = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateRoleRequest }) =>
      adminRoleApi.update(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật vai trò thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminRoles'] });
        queryClient.invalidateQueries({ queryKey: ['adminRole'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useDeleteAdminRole = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => adminRoleApi.delete(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa vai trò thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminRoles'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useSetRolePermissions = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: SetRolePermissionsRequest }) =>
      adminRoleApi.setPermissions(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật phân quyền thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminRoles'] });
        queryClient.invalidateQueries({ queryKey: ['adminRole'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};
