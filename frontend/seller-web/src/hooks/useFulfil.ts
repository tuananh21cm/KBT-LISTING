import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { fulfilApi } from '../services/fulfilApi';
import { orderApi } from '../services/orderApi';
import type {
  CreateFulfilTransactionRequest,
  FulfilTransactionQuery,
  UpdateFulfilTransactionRequest,
} from '@drop/types/fulfil';

export const useFulfilTransactions = (query: FulfilTransactionQuery = {}, enabled = true) =>
  useQuery({
    queryKey: ['fulfilTransactions', query],
    queryFn: async () => {
      const response = await fulfilApi.getTransactions(query);
      if (!response.success) throw new Error(response.message);
      return (
        response.data ?? {
          items: [],
          total: 0,
          pageCurrent: query.pageCurrent ?? 1,
          pageSize: query.pageSize ?? 100,
          summary: {
            orderCount: 0,
            cardAmount: 0,
            costAmount: 0,
            buyTrackAmount: 0,
            thankCardAmount: 0,
            couponAmount: 0,
            walletAmount: 0,
            membershipAmount: 0,
            pointsAmount: 0,
            profitAmount: 0,
          },
        }
      );
    },
    enabled,
    placeholderData: keepPreviousData,
  });

export const useFulfilAccountOptions = (enabled = true) =>
  useQuery({
    queryKey: ['fulfilAccountOptions'],
    queryFn: async () => {
      const response = await fulfilApi.getAccountOptions();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useFulfilCardOptions = (accountId?: string, enabled = true) =>
  useQuery({
    queryKey: ['fulfilCardOptions', accountId ?? 'all'],
    queryFn: async () => {
      const response = await fulfilApi.getCardOptions(accountId);
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useOrderPreview = (id: string | null, enabled: boolean) =>
  useQuery({
    queryKey: ['orderPreview', id],
    queryFn: async () => {
      if (!id) return null;
      const response = await orderApi.getPreview(id);
      if (!response.success) throw new Error(response.message);
      return response.data;
    },
    enabled: enabled && !!id,
    staleTime: 60_000,
  });

export const useCreateFulfilTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateFulfilTransactionRequest) => fulfilApi.createTransaction(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
      } else message.error(response.message || 'Tạo giao dịch thất bại.');
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateFulfilTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateFulfilTransactionRequest }) =>
      fulfilApi.updateTransaction(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
      } else message.error(response.message || 'Cập nhật giao dịch thất bại.');
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteFulfilTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => fulfilApi.deleteTransaction(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
      } else message.error(response.message || 'Xóa giao dịch thất bại.');
    },
    onError: (error: Error) => message.error(error.message),
  });
};
