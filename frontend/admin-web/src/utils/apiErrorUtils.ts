import axios from 'axios';
import type { ApiResponse } from '../types/api';

export interface ApiErrorDetails {
  message: string;
  errors: string[];
}

export function getApiErrorDetails(error: unknown, fallback = 'Thao tác thất bại.'): ApiErrorDetails {
  if (axios.isAxiosError<ApiResponse<unknown>>(error)) {
    const data = error.response?.data;
    return {
      message: data?.message?.trim() || error.message || fallback,
      errors: (data?.errors ?? []).filter((e) => String(e).trim()),
    };
  }

  if (error instanceof Error && error.message.trim()) {
    return { message: error.message.trim(), errors: [] };
  }

  return { message: fallback, errors: [] };
}
