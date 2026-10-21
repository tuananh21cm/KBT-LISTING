import api from './api';
import type { ApiResponse } from '../types/api';
import type { UserSummary } from '../types/auth';
import type {
  AdminFourSellerLoginCheckItem,
  AdminFourSellerLoginCheckRequest,
  AdminUser,
  CreateAdminUserRequest,
  FourSellerLoginCheckRequest,
  FourSellerLoginCheckResponse,
  ImportAdminUsersRequest,
  ImportAdminUsersResult,
  ResetPasswordRequest,
  RoleOption,
  UpdateAdminUserRequest,
} from '../types/user';

export const adminUserApi = {
  getAll: async (): Promise<ApiResponse<AdminUser[]>> => {
    const response = await api.get<ApiResponse<AdminUser[]>>('/admin/users');
    return response.data;
  },

  getById: async (id: string): Promise<ApiResponse<AdminUser>> => {
    const response = await api.get<ApiResponse<AdminUser>>(`/admin/users/${id}`);
    return response.data;
  },

  create: async (data: CreateAdminUserRequest): Promise<ApiResponse<AdminUser>> => {
    const response = await api.post<ApiResponse<AdminUser>>('/admin/users', data);
    return response.data;
  },

  update: async (id: string, data: UpdateAdminUserRequest): Promise<ApiResponse<AdminUser>> => {
    const response = await api.put<ApiResponse<AdminUser>>(`/admin/users/${id}`, data);
    return response.data;
  },

  import: async (data: ImportAdminUsersRequest): Promise<ApiResponse<ImportAdminUsersResult>> => {
    const response = await api.post<ApiResponse<ImportAdminUsersResult>>('/admin/users/import', data);
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(`/admin/users/${id}`);
    return response.data;
  },

  resetPassword: async (id: string, data: ResetPasswordRequest): Promise<ApiResponse<null>> => {
    const response = await api.put<ApiResponse<null>>(`/admin/users/${id}/reset-password`, data);
    return response.data;
  },

  getSellers: async (): Promise<ApiResponse<UserSummary[]>> => {
    const response = await api.get<ApiResponse<UserSummary[]>>('/admin/users/sellers');
    return response.data;
  },

  /** `roles` = tên role cách nhau bằng dấu phẩy (OR); bỏ trống = mọi user đang hoạt động. */
  getOptions: async (roles?: string): Promise<ApiResponse<UserSummary[]>> => {
    const response = await api.get<ApiResponse<UserSummary[]>>('/admin/users/options', {
      params: roles ? { roles } : undefined,
    });
    return response.data;
  },

  getRoleOptions: async (): Promise<ApiResponse<RoleOption[]>> => {
    const response = await api.get<ApiResponse<RoleOption[]>>('/admin/users/role-options');
    return response.data;
  },

  checkFourSellerLogin: async (
    id: string,
    data?: FourSellerLoginCheckRequest,
  ): Promise<ApiResponse<FourSellerLoginCheckResponse>> => {
    const response = await api.post<ApiResponse<FourSellerLoginCheckResponse>>(
      `/admin/users/${id}/four-seller-cookie/check-login`,
      data ?? {},
    );
    return response.data;
  },

  checkFourSellerLogins: async (
    data?: AdminFourSellerLoginCheckRequest,
  ): Promise<ApiResponse<AdminFourSellerLoginCheckItem[]>> => {
    const response = await api.post<ApiResponse<AdminFourSellerLoginCheckItem[]>>(
      '/admin/users/four-seller-cookie/check-login',
      data ?? {},
    );
    return response.data;
  },
};
