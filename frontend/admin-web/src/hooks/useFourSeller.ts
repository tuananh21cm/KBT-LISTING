import { createElement } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import axios from 'axios';
import { message } from 'antd';
import { Modal } from '@drop/ui';
import { invalidateOrdersCaches } from '@drop/api-client';
import { fourSellerApi } from '../services/fourSellerApi';
import { adminKbtCrmApi } from '../services/adminKbtCrmApi';
import type { FourSellerShopCheckResponse, FourSellerSyncResponse } from '../types/fourSeller';

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
    msg.includes('đang login')
  ) {
    message.error(msg);
    return;
  }
  message.error(msg);
};

const formatSyncSuccess = (data: FourSellerSyncResponse, apiMessage?: string) => {
  const failures = data.failedAccounts ?? [];
  const relinked = data.ordersRelinkedCount ?? 0;
  const idRelinked = data.relinkedCount ?? 0;
  const unmatched = (data.unmatchedShops ?? []).filter(
    (s) =>
      s.reason === 'no_local_shop' ||
      s.reason === 'ambiguous_external_id' ||
      s.reason === 'fourseller_id_taken',
  );

  const unmatchedHint =
    unmatched.length > 0
      ? unmatched
          .slice(0, 5)
          .map(
            (s) =>
              `#${s.fourSellerShopId} ${s.platformShopName || s.shopName || s.sellingPartnerId || ''} (${s.reason})`,
          )
          .join(' · ')
      : '';

  if (failures.length > 0) {
    const detail = failures
      .map((f) => `${f.ownerName?.trim() || f.ownerUserId}: ${f.message}`)
      .join(' | ');
    message.warning(
      `${apiMessage || `Đồng bộ xong có lỗi: cập nhật ${data.updatedCount}, relink id ${idRelinked}, gắn lại ${relinked} đơn.`} — ${detail}`,
      8,
    );
  } else {
    message.success(
      apiMessage ||
        `Đồng bộ xong: ${data.syncedAccounts ?? 0} tài khoản, cập nhật ${data.updatedCount}, relink id ${idRelinked}, gắn lại ${relinked} đơn, bỏ qua ${data.skippedCount} (4Seller ${data.fourSellerShopCount}).`,
    );
  }

  if (unmatchedHint) {
    Modal.warning({
      title: `${unmatched.length} shop 4Seller chưa map local`,
      content: unmatchedHint,
      okText: 'Đã hiểu',
    });
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
        queryClient.invalidateQueries({ queryKey: ['adminShops'] });
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
      try {
        const response = await fourSellerApi.syncShops();
        if (!response.success || !response.data) {
          throw new Error(response.message || 'Không đồng bộ được danh sách shop 4Seller.');
        }
        return response;
      } catch (error) {
        throw new Error(
          getApiErrorMessage(error, 'Không đồng bộ được danh sách shop 4Seller.'),
        );
      }
    },
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: ['adminShops'] });
      queryClient.invalidateQueries({ queryKey: ['adminShopMappingHealth'] });
      queryClient.invalidateQueries({ queryKey: ['fourSellerShops'] });
      invalidateOrdersCaches(queryClient, 'admin');
      if (response.data) {
        formatSyncSuccess(response.data, response.message);
      }
    },
    onError: handleFourSellerError,
  });
};

export const useFourSellerShopList = (enabled = true) =>
  useQuery({
    queryKey: ['fourSellerShops'],
    queryFn: async () => {
      if (!fourSellerApi.getShopList) {
        throw new Error('Danh sách shop 4Seller chỉ khả dụng trên admin.');
      }
      const response = await fourSellerApi.getShopList();
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được danh sách shop 4Seller.');
      }
      return response.data;
    },
    enabled,
    staleTime: 60_000,
  });

/** Pull teams / users / shops from KBT CRM (replaces the Excel import). */
export const useKbtCrmSync = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const response = await adminKbtCrmApi.sync();
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không đồng bộ được từ CRM.');
      }
      return response;
    },
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: ['adminShops'] });
      queryClient.invalidateQueries({ queryKey: ['adminUsers'] });
      queryClient.invalidateQueries({ queryKey: ['sellers'] });
      const skipped = response.data?.skipped ?? [];
      if (skipped.length > 0) {
        Modal.info({
          title: response.message,
          width: 640,
          content: createElement(
            'ul',
            { style: { maxHeight: 360, overflow: 'auto', paddingLeft: 20 } },
            skipped.map((line) => createElement('li', { key: line }, line)),
          ),
        });
      } else {
        message.success(response.message || 'Đã đồng bộ từ CRM.');
      }
    },
    onError: (error: unknown) => {
      message.error(getApiErrorMessage(error, 'Không đồng bộ được từ CRM.'));
    },
  });
};
