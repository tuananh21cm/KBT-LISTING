import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { invalidateOrdersCaches } from '@drop/api-client';
import { adminReceivingBankApi } from '../services/adminReceivingBankApi';
import type {
  CreateReceivingBankAccountRequest,
  CreateReceivingBankTransactionRequest,
  CreateReceivingSourceAccountRequest,
  ReceivingBankIncomingQuery,
  ReceivingBankTransactionQuery,
  UpdateReceivingBankAccountRequest,
  UpdateReceivingBankTransactionRequest,
  UpdateReceivingSourceAccountRequest,
} from '../types/receivingBank';

export const useReceivingCardSources = () =>
  useQuery({
    queryKey: ['receivingCardSources'],
    queryFn: async () => {
      const response = await adminReceivingBankApi.getSources();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useReceivingSourceAccounts = (sourceId?: string, enabled = true) =>
  useQuery({
    queryKey: ['receivingSourceAccounts', sourceId ?? 'all'],
    queryFn: async () => {
      const response = await adminReceivingBankApi.getSourceAccounts(sourceId);
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
  });

export const useReceivingSourceAccountOptions = (sourceId?: string, enabled = true) =>
  useQuery({
    queryKey: ['receivingSourceAccountOptions', sourceId ?? 'all'],
    queryFn: async () => {
      const response = await adminReceivingBankApi.getSourceAccountOptions(sourceId);
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled: enabled && !!sourceId,
  });

export const useCreateReceivingSourceAccount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateReceivingSourceAccountRequest) =>
      adminReceivingBankApi.createSourceAccount(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo tài khoản nguồn thành công.');
        queryClient.invalidateQueries({ queryKey: ['receivingSourceAccounts'] });
        queryClient.invalidateQueries({ queryKey: ['receivingSourceAccountOptions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilCards'] });
        queryClient.invalidateQueries({ queryKey: ['adminReceivingBanks'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateReceivingSourceAccount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateReceivingSourceAccountRequest }) =>
      adminReceivingBankApi.updateSourceAccount(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật tài khoản nguồn thành công.');
        queryClient.invalidateQueries({ queryKey: ['receivingSourceAccounts'] });
        queryClient.invalidateQueries({ queryKey: ['receivingSourceAccountOptions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilCards'] });
        queryClient.invalidateQueries({ queryKey: ['adminReceivingBanks'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteReceivingSourceAccount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminReceivingBankApi.deleteSourceAccount(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa tài khoản nguồn thành công.');
        queryClient.invalidateQueries({ queryKey: ['receivingSourceAccounts'] });
        queryClient.invalidateQueries({ queryKey: ['receivingSourceAccountOptions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilCards'] });
        queryClient.invalidateQueries({ queryKey: ['adminReceivingBanks'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useReceivingBankAccounts = (enabled = true) =>
  useQuery({
    queryKey: ['adminReceivingBanks'],
    queryFn: async () => {
      const response = await adminReceivingBankApi.getAll();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
  });

export const useReceivingBankAccount = (id?: string | null, enabled = true) =>
  useQuery({
    queryKey: ['adminReceivingBank', id],
    queryFn: async () => {
      const response = await adminReceivingBankApi.getById(id!);
      if (!response.success) throw new Error(response.message);
      return response.data;
    },
    enabled: enabled && !!id,
  });

export const useReceivingBankAccountOptions = (enabled = true) =>
  useQuery({
    queryKey: ['receivingBankOptions'],
    queryFn: async () => {
      const response = await adminReceivingBankApi.getOptions();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
  });

export const useCreateReceivingBankAccount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateReceivingBankAccountRequest) =>
      adminReceivingBankApi.create(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo thẻ nhận tiền thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminReceivingBanks'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankOptions'] });
        queryClient.invalidateQueries({ queryKey: ['adminReceivingBank'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateReceivingBankAccount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateReceivingBankAccountRequest }) =>
      adminReceivingBankApi.update(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật thẻ nhận tiền thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminReceivingBanks'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankOptions'] });
        queryClient.invalidateQueries({ queryKey: ['adminReceivingBank'] });
        queryClient.invalidateQueries({ queryKey: ['adminShops'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteReceivingBankAccount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminReceivingBankApi.delete(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa thẻ nhận tiền thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminReceivingBanks'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankOptions'] });
        queryClient.invalidateQueries({ queryKey: ['adminReceivingBank'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useReceivingBankTransactions = (
  query: ReceivingBankTransactionQuery = {},
  enabled = true,
) =>
  useQuery({
    queryKey: ['receivingBankTransactions', query],
    queryFn: async () => {
      const response = await adminReceivingBankApi.getTransactions(query);
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
  });

export const useReceivingBankIncomingStats = (query: ReceivingBankIncomingQuery = {}) =>
  useQuery({
    queryKey: ['receivingBankIncomingStats', query],
    queryFn: async () => {
      const response = await adminReceivingBankApi.getTransactionStats(query);
      if (!response.success) throw new Error(response.message);
      return response.data;
    },
  });

export const useCreateReceivingBankTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateReceivingBankTransactionRequest) =>
      adminReceivingBankApi.createTransaction(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['receivingBankTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankIncomingStats'] });
        invalidateOrdersCaches(queryClient, 'admin');
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
      adminReceivingBankApi.updateTransaction(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['receivingBankTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankIncomingStats'] });
        invalidateOrdersCaches(queryClient, 'admin');
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
    mutationFn: (id: string) => adminReceivingBankApi.deleteTransaction(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['receivingBankTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankIncomingStats'] });
        invalidateOrdersCaches(queryClient, 'admin');
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useSetReceivingBankConfirmationStatus = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      confirmationStatus,
    }: {
      id: string;
      confirmationStatus: 'Pending' | 'Confirmed' | 'Rejected';
    }) => adminReceivingBankApi.setConfirmationStatus(id, { confirmationStatus }),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật trạng thái.');
        queryClient.invalidateQueries({ queryKey: ['receivingBankTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankIncomingStats'] });
        queryClient.invalidateQueries({ queryKey: ['adminShopFinance'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useConfirmReceivingBankTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminReceivingBankApi.confirmTransaction(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã duyệt giao dịch.');
        queryClient.invalidateQueries({ queryKey: ['receivingBankTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankIncomingStats'] });
        queryClient.invalidateQueries({ queryKey: ['adminShopFinance'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useRejectReceivingBankTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminReceivingBankApi.rejectTransaction(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã chuyển sang Không duyệt.');
        queryClient.invalidateQueries({ queryKey: ['receivingBankTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankIncomingStats'] });
        queryClient.invalidateQueries({ queryKey: ['adminShopFinance'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};
