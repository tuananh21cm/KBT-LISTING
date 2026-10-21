import axios from 'axios';
import { API_BASE_URL } from '../config/apiBaseUrl';
import api from './api';
import type { ApiResponse } from '../types/api';
import type { AuthResponse, CurrentUser, LoginRequest, RefreshTokenRequest, UpdateThemeRequest } from '../types/auth';
import type {
  FourSellerLoginCheckRequest,
  FourSellerLoginCheckResponse,
} from '../types/user';

export const authApi = {
  login: async (data: LoginRequest): Promise<ApiResponse<AuthResponse>> => {
    const response = await api.post<ApiResponse<AuthResponse>>('/auth/login', data);
    return response.data;
  },

  logout: async (data: RefreshTokenRequest): Promise<ApiResponse<null>> => {
    const response = await api.post<ApiResponse<null>>('/auth/logout', data);
    return response.data;
  },

  refresh: async (data: RefreshTokenRequest): Promise<ApiResponse<AuthResponse>> => {
    const response = await axios.post<ApiResponse<AuthResponse>>(`${API_BASE_URL}/auth/refresh`, data);
    return response.data;
  },

  getCurrentUser: async (): Promise<ApiResponse<CurrentUser>> => {
    const response = await api.get<ApiResponse<CurrentUser>>('/auth/me');
    return response.data;
  },

  updateTheme: async (
    data: UpdateThemeRequest,
  ): Promise<ApiResponse<{ theme: 'light' | 'dark' }>> => {
    const response = await api.put<ApiResponse<{ theme: 'light' | 'dark' }>>('/auth/theme', data);
    return response.data;
  },

  checkFourSellerLogin: async (
    data?: FourSellerLoginCheckRequest,
  ): Promise<ApiResponse<FourSellerLoginCheckResponse>> => {
    const response = await api.post<ApiResponse<FourSellerLoginCheckResponse>>(
      '/auth/four-seller-cookie/check-login',
      data ?? {},
    );
    return response.data;
  },
};
