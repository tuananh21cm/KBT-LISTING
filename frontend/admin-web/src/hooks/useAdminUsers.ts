import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { adminUserApi } from '../services/adminUserApi';
import { getMutationErrorMessage } from '../utils/apiError';
import type {
  AdminFourSellerLoginCheckRequest,
  CreateAdminUserRequest,
  FourSellerLoginCheckRequest,
  ImportAdminUsersRequest,
  ResetPasswordRequest,
  UpdateAdminUserRequest,
} from '../types/user';

export const useAdminUsers = (enabled = true) =>
  useQuery({
    queryKey: ['adminUsers'],
    queryFn: async () => {
      const response = await adminUserApi.getAll();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled,
  });

/**
 * Active users as id/name options for filter dropdowns (no roles in payload).
 * `roles` = tên role cách nhau bằng dấu phẩy (OR); bỏ trống = mọi user đang hoạt động.
 */
export const useAdminUserOptions = (enabled = true, roles?: string) =>
  useQuery({
    queryKey: ['adminUserOptions', roles ?? null],
    queryFn: async () => {
      const response = await adminUserApi.getOptions(roles);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

/** Role được phép đi đơn — chỉ những người này mới là "người fulfil" của một đơn. */
const FULFIL_ROLES = 'Admin,Fulfil';

/** Người fulfil (role Admin / Fulfil) cho dropdown gán + lọc người mua hàng. */
export const useFulfilUserOptions = (enabled = true) =>
  useAdminUserOptions(enabled, FULFIL_ROLES);

export const useCreateAdminUser = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateAdminUserRequest) => adminUserApi.create(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo người dùng thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminUsers'] });
        queryClient.invalidateQueries({ queryKey: ['adminUserOptions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useUpdateAdminUser = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateAdminUserRequest }) =>
      adminUserApi.update(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật người dùng thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminUsers'] });
        queryClient.invalidateQueries({ queryKey: ['adminUserOptions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

/**
 * Import hàng loạt: backend upsert theo tên đăng nhập hoặc email.
 * Trả kết quả cho modal tự hiển thị lỗi từng dòng — chỉ toast phần tổng kết.
 */
export const useImportAdminUsers = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data: ImportAdminUsersRequest) => {
      const response = await adminUserApi.import(data);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Import tài khoản thất bại.');
      }
      return response.data;
    },
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['adminUsers'] });
      queryClient.invalidateQueries({ queryKey: ['adminUserOptions'] });
      const summary = `Thêm mới ${result.created}, cập nhật ${result.updated}`;
      if (result.failed.length > 0) {
        message.warning(`${summary}, ${result.failed.length} dòng lỗi.`);
      } else {
        message.success(`${summary}.`);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useDeleteAdminUser = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => adminUserApi.delete(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa người dùng thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminUsers'] });
        queryClient.invalidateQueries({ queryKey: ['adminUserOptions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useResetAdminUserPassword = () =>
  useMutation({
    mutationFn: ({ id, data }: { id: string; data: ResetPasswordRequest }) =>
      adminUserApi.resetPassword(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đặt lại mật khẩu thành công.');
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });

export const useCheckAdminUserFourSellerLogin = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      data,
    }: {
      id: string;
      data?: FourSellerLoginCheckRequest;
    }) => {
      const response = await adminUserApi.checkFourSellerLogin(id, data);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không kiểm tra được trạng thái đăng nhập 4Seller.');
      }
      return { ...response.data, message: response.data.message || response.message };
    },
    onSuccess: (data) => {
      void queryClient.invalidateQueries({ queryKey: ['adminUsers'] });
      if (data.isLoggedIn) {
        message.success(data.message || 'Cookie 4Seller hợp lệ.');
      } else if (data.blocked) {
        message.warning(data.message || '4Seller chặn IP (567) — không đổi trạng thái cookie.');
      } else if (data.unverified) {
        message.warning(
          data.message || 'Không xác minh được phiên 4Seller (mạng/proxy) — không đổi trạng thái cookie.',
        );
      } else {
        message.warning(data.message || 'Cookie 4Seller không hợp lệ hoặc đã hết hạn.');
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useCheckAdminUsersFourSellerLogin = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data?: AdminFourSellerLoginCheckRequest) => {
      const response = await adminUserApi.checkFourSellerLogins(data);
      if (!response.success) {
        throw new Error(response.message || 'Không kiểm tra được trạng thái đăng nhập 4Seller.');
      }
      return {
        items: response.data ?? [],
        message: response.message,
      };
    },
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: ['adminUsers'] });
      if (result.items.length === 0) {
        message.info(result.message || 'Không có người dùng nào có cookie 4Seller để kiểm tra.');
        return;
      }
      const loggedIn = result.items.filter((i) => i.isLoggedIn).length;
      const blocked = result.items.filter((i) => i.blocked).length;
      const unverified = result.items.filter((i) => i.unverified).length;
      const expired = result.items.length - loggedIn - blocked - unverified;
      if (blocked + unverified + expired === 0) {
        message.success(result.message || `Tất cả ${loggedIn} cookie đang login.`);
      } else {
        message.warning(
          result.message ||
            `Đã kiểm tra ${result.items.length}: ${loggedIn} login, ${expired} hết hạn` +
              (blocked > 0 ? `, ${blocked} chặn IP` : '') +
              (unverified > 0 ? `, ${unverified} chưa xác minh` : '') +
              '.',
        );
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useRoleOptions = (enabled = true) =>
  useQuery({
    queryKey: ['roleOptions'],
    queryFn: async () => {
      const response = await adminUserApi.getRoleOptions();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled,
  });
