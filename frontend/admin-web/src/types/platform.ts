export type { PlatformOption } from '@drop/types/platform';

export interface Platform {
  id: string;
  code: string;
  name: string;
  description?: string;
  avatarUrl?: string;
  isActive: boolean;
  shopCount: number;
  createdAt: string;
  updatedAt?: string;
}

export interface CreatePlatformRequest {
  code: string;
  name: string;
  description?: string;
  avatarUrl?: string;
  isActive: boolean;
}

export interface UpdatePlatformRequest {
  code: string;
  name: string;
  description?: string;
  avatarUrl?: string;
  isActive: boolean;
}
