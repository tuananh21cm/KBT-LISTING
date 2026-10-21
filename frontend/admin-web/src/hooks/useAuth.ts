import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { useNavigate } from 'react-router-dom';
import { authApi } from '../services/authApi';
import { useAuthStore } from '../stores/authStore';
import { useThemeStore } from '../stores/themeStore';
import type { LoginRequest } from '../types/auth';
import { shouldRefreshAccessToken } from '../utils/authToken';

export const useCurrentUser = () => {
  const accessToken = useAuthStore((s) => s.accessToken);
  const setAuth = useAuthStore((s) => s.setAuth);
  const setUser = useAuthStore((s) => s.setUser);
  const hydrateFromUser = useThemeStore((s) => s.hydrateFromUser);

  return useQuery({
    queryKey: ['currentUser'],
    queryFn: async () => {
      // Only rotate refresh token when access JWT is near expiry.
      // Eager refresh on every tab open revoked the parent tab's token (Edit opens _blank).
      const { refreshToken: storedRefreshToken, expiresAt } = useAuthStore.getState();
      if (storedRefreshToken && shouldRefreshAccessToken(expiresAt)) {
        try {
          const refreshResponse = await authApi.refresh({ refreshToken: storedRefreshToken });
          if (refreshResponse.success && refreshResponse.data) {
            setAuth(refreshResponse.data);
          }
        } catch {
          // Continue with existing token; 401 flow handled by api interceptor.
        }
      }

      const response = await authApi.getCurrentUser();
      if (!response.success) {
        throw new Error(response.message);
      }
      if (!response.data) {
        throw new Error('Không lấy được thông tin người dùng.');
      }
      setUser(response.data);
      hydrateFromUser(response.data.theme);
      return response.data;
    },
    enabled: !!accessToken,
    retry: false,
  });
};

export const useLogin = () => {
  const navigate = useNavigate();
  const setAuth = useAuthStore((s) => s.setAuth);
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: LoginRequest) => authApi.login(data),
    onSuccess: async (response) => {
      if (!response.success || !response.data) {
        message.error(response.message || 'Đăng nhập thất bại.');
        return;
      }
      setAuth(response.data);
      await queryClient.invalidateQueries({ queryKey: ['currentUser'] });
      message.success(response.message || 'Đăng nhập thành công.');
      navigate('/', { replace: true });
    },
    onError: (error: unknown) => {
      const axiosError = error as { response?: { data?: { message?: string } } };
      const msg = axiosError.response?.data?.message ?? 'Tên đăng nhập hoặc mật khẩu không đúng.';
      message.error(msg);
    },
  });
};

export const useLogout = () => {
  const navigate = useNavigate();
  const refreshToken = useAuthStore((s) => s.refreshToken);
  const clearAuth = useAuthStore((s) => s.clearAuth);
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      if (refreshToken) {
        await authApi.logout({ refreshToken });
      }
    },
    onSettled: () => {
      clearAuth();
      queryClient.clear();
      navigate('/login', { replace: true });
      message.success('Đăng xuất thành công.');
    },
  });
};
