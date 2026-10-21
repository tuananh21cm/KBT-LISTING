import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { receivingBankApi } from '../services/receivingBankApi';
import type {
  CreateReceivingBankTransactionRequest,
  ReceivingBankTransactionQuery,
  UpdateReceivingBankTransactionRequest,
} from '@drop/types/receivingBank';

export const useReceivingBankTransactions = (
  query: ReceivingBankTransactionQuery = {},
  enabled = true,
) =>
  useQuery({
    queryKey: ['receivingBankTransactions', query],
    queryFn: async () => {
      const response = await receivingBankApi.getTransactions(query);
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
  });

export const useReceivingBankAccountOptions = (enabled = true) =>
  useQuery({
    queryKey: ['receivingBankOptions'],
    queryFn: async () => {
      const response = await receivingBankApi.getOptions();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useCreateReceivingBankTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateReceivingBankTransactionRequest) =>
      receivingBankApi.createTransaction(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã gửi giao dịch — chờ Admin xác nhận.');
        queryClient.invalidateQueries({ queryKey: ['receivingBankTransactions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateReceivingBankTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateReceivingBankTransactionRequest }) =>
      receivingBankApi.updateTransaction(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['receivingBankTransactions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteReceivingBankTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => receivingBankApi.deleteTransaction(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['receivingBankTransactions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};
