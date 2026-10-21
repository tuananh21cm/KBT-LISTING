import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { useNavigate } from 'react-router-dom';
import { authApi } from '../services/authApi';
import { useAuthStore } from '../stores/authStore';
import { useThemeStore } from '../stores/themeStore';
import type {
  ChangePasswordRequest,
  FourSellerLoginCheckRequest,
  LoginRequest,
  UpdateFourSellerCookieRequest,
} from '../types/auth';
import { shouldRefreshAccessToken } from '../utils/authToken';

export const useCurrentUser = () => {
  const accessToken = useAuthStore((s) => s.accessToken);
  const setAuth = useAuthStore((s) => s.setAuth);
  const setUser = useAuthStore((s) => s.setUser);
  const hydrateFromUser = useThemeStore((s) => s.hydrateFromUser);

  return useQuery({
    queryKey: ['currentUser'],
    queryFn: async () => {
      // Only rotate when access JWT is near expiry (avoid unnecessary revoke on every load).
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

export const useChangePassword = () => {
  return useMutation({
    mutationFn: (data: ChangePasswordRequest) => authApi.changePassword(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đổi mật khẩu thành công.');
      } else {
        message.error(response.message || 'Đổi mật khẩu thất bại.');
      }
    },
    onError: (error: Error) => {
      message.error(error.message || 'Đổi mật khẩu thất bại.');
    },
  });
};

export const useUpdateFourSellerCookie = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: UpdateFourSellerCookieRequest) => authApi.updateFourSellerCookie(data),
    onSuccess: (response) => {
      if (!response.success) {
        message.error(response.message || 'Cập nhật cookie 4Seller thất bại.');
        return;
      }
      const hasCookie = response.data?.hasFourSellerCookie ?? false;
      const loginStatus = response.data?.fourSellerCookieLoginStatus;
      const current = useAuthStore.getState().user;
      if (current) {
        useAuthStore.getState().setUser({
          ...current,
          hasFourSellerCookie: hasCookie,
          ...(loginStatus ? { fourSellerCookieLoginStatus: loginStatus } : {}),
        });
      }
      queryClient.invalidateQueries({ queryKey: ['currentUser'] });
      message.success(response.message || 'Đã cập nhật cookie 4Seller.');
    },
    onError: (error: unknown) => {
      const axiosError = error as { response?: { data?: { message?: string } }; message?: string };
      message.error(
        axiosError.response?.data?.message ?? axiosError.message ?? 'Cập nhật cookie 4Seller thất bại.',
      );
    },
  });
};

export const useCheckFourSellerLogin = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data?: FourSellerLoginCheckRequest) => {
      const response = await authApi.checkFourSellerLogin(data);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không kiểm tra được trạng thái đăng nhập 4Seller.');
      }
      return response.data;
    },
    onSuccess: (data) => {
      const current = useAuthStore.getState().user;
      if (current) {
        useAuthStore.getState().setUser({
          ...current,
          ...(typeof data.hasFourSellerCookie === 'boolean'
            ? { hasFourSellerCookie: data.hasFourSellerCookie }
            : {}),
          ...(data.fourSellerCookieLoginStatus
            ? { fourSellerCookieLoginStatus: data.fourSellerCookieLoginStatus }
            : {}),
        });
      }
      void queryClient.invalidateQueries({ queryKey: ['currentUser'] });
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
    onError: (error: unknown) => {
      const axiosError = error as { response?: { data?: { message?: string } }; message?: string };
      message.error(
        axiosError.response?.data?.message ??
          axiosError.message ??
          'Không kiểm tra được trạng thái đăng nhập 4Seller.',
      );
    },
  });
};
