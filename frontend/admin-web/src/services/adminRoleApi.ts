import api from './api';
import type { ApiResponse } from '../types/api';
import type {
  CreateRoleRequest,
  Permission,
  Role,
  RoleDetail,
  SetRolePermissionsRequest,
  UpdateRoleRequest,
} from '../types/role';

export const adminRoleApi = {
  getAll: async (): Promise<ApiResponse<Role[]>> => {
    const response = await api.get<ApiResponse<Role[]>>('/admin/roles');
    return response.data;
  },

  getById: async (id: string): Promise<ApiResponse<RoleDetail>> => {
    const response = await api.get<ApiResponse<RoleDetail>>(`/admin/roles/${id}`);
    return response.data;
  },

  create: async (data: CreateRoleRequest): Promise<ApiResponse<Role>> => {
    const response = await api.post<ApiResponse<Role>>('/admin/roles', data);
    return response.data;
  },

  update: async (id: string, data: UpdateRoleRequest): Promise<ApiResponse<Role>> => {
    const response = await api.put<ApiResponse<Role>>(`/admin/roles/${id}`, data);
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(`/admin/roles/${id}`);
    return response.data;
  },

  setPermissions: async (id: string, data: SetRolePermissionsRequest): Promise<ApiResponse<RoleDetail>> => {
    const response = await api.put<ApiResponse<RoleDetail>>(`/admin/roles/${id}/permissions`, data);
    return response.data;
  },

  getPermissions: async (): Promise<ApiResponse<Permission[]>> => {
    const response = await api.get<ApiResponse<Permission[]>>('/admin/permissions');
    return response.data;
  },
};
