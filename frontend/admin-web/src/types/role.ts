export interface Role {
  id: string;
  name: string;
  description?: string;
  isSystem: boolean;
  permissionCount: number;
  createdAt: string;
}

export interface RoleDetail extends Role {
  permissionIds: string[];
}

export interface Permission {
  id: string;
  code: string;
  name: string;
  module: string;
  application: string;
  description?: string;
}

export interface CreateRoleRequest {
  name: string;
  description?: string;
}

export interface UpdateRoleRequest {
  name: string;
  description?: string;
}

export interface SetRolePermissionsRequest {
  permissionIds: string[];
}
