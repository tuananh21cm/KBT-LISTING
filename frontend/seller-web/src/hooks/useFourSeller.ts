import { useMutation, useQueryClient } from '@tanstack/react-query';
import axios from 'axios';
import { message } from 'antd';
import { authApi } from '../services/authApi';
import { fourSellerApi } from '../services/fourSellerApi';
import type { FourSellerShopCheckResponse } from '../types/fourSeller';

const getApiErrorMessage = (error: unknown, fallback: string) => {
  if (axios.isAxiosError<{ message?: string }>(error)) {
    return error.response?.data?.message || error.message || fallback;
  }
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return fallback;
};

const handleFourSellerError = (error: unknown) => {
  const msg = getApiErrorMessage(error, 'Thao tác 4Seller thất bại.');
  if (
    msg.includes('cookie 4Seller') ||
    msg.includes('Cookie 4Seller') ||
    msg.includes('4seller.cookie') ||
    msg.includes('Chưa đăng nhập 4Seller')
  ) {
    message.error(
      msg.includes('Hồ sơ')
        ? msg
        : 'Chưa cấu hình hoặc hết hạn cookie 4Seller. Vào Hồ sơ cá nhân để thêm/cập nhật session cookie.',
    );
    return;
  }
  message.error(msg);
};

/** Ensures current user has a valid 4Seller session before outbound sync calls. */
const ensureFourSellerLoggedIn = async () => {
  const loginResponse = await authApi.checkFourSellerLogin();
  if (!loginResponse.success || !loginResponse.data) {
    throw new Error(
      loginResponse.message || 'Không kiểm tra được trạng thái đăng nhập 4Seller.',
    );
  }
  if (!loginResponse.data.isLoggedIn) {
    throw new Error(
      loginResponse.data.message ||
        'Chưa đăng nhập 4Seller. Cập nhật cookie tại Hồ sơ cá nhân.',
    );
  }
};

export const useFourSellerAuthRedirect = () =>
  useMutation({
    mutationFn: async (shopId: string) => {
      const response = await fourSellerApi.getAuthRedirectUrl(shopId);
      if (!response.success || !response.data?.authorizeUrl) {
        throw new Error(response.message || 'Không tạo được link xác thực 4Seller.');
      }
      return response.data.authorizeUrl;
    },
    onError: handleFourSellerError,
  });

export const useFourSellerCheckConnection = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (shopId: string): Promise<FourSellerShopCheckResponse> => {
      const response = await fourSellerApi.checkConnection(shopId);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không kiểm tra được trạng thái kết nối 4Seller.');
      }
      return response.data;
    },
    onSuccess: (data) => {
      if (data.shopSynced) {
        queryClient.invalidateQueries({ queryKey: ['shops'] });
        message.success('Đã cập nhật trạng thái và ngày kết nối 4Seller.');
      }
    },
    onError: handleFourSellerError,
  });
};

export const useFourSellerSyncShops = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      await ensureFourSellerLoggedIn();
      try {
        const response = await fourSellerApi.syncShops();
        if (!response.success || !response.data) {
          throw new Error(response.message || 'Không đồng bộ được danh sách shop 4Seller.');
        }
        return response.data;
      } catch (error) {
        throw new Error(
          getApiErrorMessage(error, 'Không đồng bộ được danh sách shop 4Seller.'),
        );
      }
    },
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['shops'] });
      message.success(
        `Đồng bộ xong: cập nhật ${data.updatedCount}, bỏ qua ${data.skippedCount} (4Seller ${data.fourSellerShopCount}).`,
      );
    },
    onError: handleFourSellerError,
  });
};
