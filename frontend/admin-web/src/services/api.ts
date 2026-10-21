import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { API_BASE_URL } from '../config/apiBaseUrl';
import type { ApiResponse } from '../types/api';
import type { AuthResponse, CurrentUser } from '../types/auth';
import { AUTH_STORAGE_KEY, useAuthStore } from '../stores/authStore';
import { readPersistedAuth } from '../utils/authToken';

interface RetryConfig extends InternalAxiosRequestConfig {
  _retry?: boolean;
}

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
});

let isRefreshing = false;
let refreshQueue: Array<(token: string) => void> = [];

const processQueue = (token: string) => {
  refreshQueue.forEach((callback) => callback(token));
  refreshQueue = [];
};

/** If another tab already rotated tokens, adopt them instead of logging out. */
const adoptPersistedAuthIfNewer = (staleRefreshToken: string): string | null => {
  const persisted = readPersistedAuth(AUTH_STORAGE_KEY);
  if (
    !persisted?.accessToken ||
    !persisted.refreshToken ||
    persisted.refreshToken === staleRefreshToken
  ) {
    return null;
  }

  useAuthStore.setState({
    accessToken: persisted.accessToken,
    refreshToken: persisted.refreshToken,
    expiresAt: persisted.expiresAt ?? null,
    user: (persisted.user as CurrentUser | null | undefined) ?? useAuthStore.getState().user,
  });
  return persisted.accessToken;
};

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken;
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ApiResponse<unknown>>) => {
    const originalRequest = error.config as RetryConfig | undefined;
    if (!originalRequest || error.response?.status !== 401 || originalRequest._retry) {
      return Promise.reject(error);
    }

    const isAuthEndpoint = originalRequest.url?.includes('/auth/login') ||
      originalRequest.url?.includes('/auth/refresh');

    if (isAuthEndpoint) {
      return Promise.reject(error);
    }

    const refreshToken = useAuthStore.getState().refreshToken;
    if (!refreshToken) {
      useAuthStore.getState().clearAuth();
      return Promise.reject(error);
    }

    if (isRefreshing) {
      return new Promise((resolve) => {
        refreshQueue.push((token: string) => {
          originalRequest.headers.Authorization = `Bearer ${token}`;
          resolve(api(originalRequest));
        });
      });
    }

    originalRequest._retry = true;
    isRefreshing = true;

    try {
      const response = await axios.post<ApiResponse<AuthResponse>>(`${API_BASE_URL}/auth/refresh`, {
        refreshToken,
      });

      const auth = response.data.data;
      if (!auth) {
        throw new Error('Refresh token response is empty.');
      }

      useAuthStore.getState().setAuth(auth);
      processQueue(auth.accessToken);
      originalRequest.headers.Authorization = `Bearer ${auth.accessToken}`;
      return api(originalRequest);
    } catch (refreshError) {
      const adopted = adoptPersistedAuthIfNewer(refreshToken);
      if (adopted) {
        processQueue(adopted);
        originalRequest.headers.Authorization = `Bearer ${adopted}`;
        return api(originalRequest);
      }

      useAuthStore.getState().clearAuth();
      refreshQueue = [];
      return Promise.reject(refreshError);
    } finally {
      isRefreshing = false;
    }
  },
);

export default api;
