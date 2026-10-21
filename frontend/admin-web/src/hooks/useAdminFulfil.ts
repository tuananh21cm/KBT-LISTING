import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { useState } from 'react';
import { adminFulfilApi } from '../services/adminFulfilApi';
import type {
  CreateFulfilAccountRequest,
  CreateFulfilCardRequest,
  CreateFulfilTransactionRequest,
  FulfilSpendingQuery,
  FulfilTransactionQuery,
  SaveFulfilSheinSessionRequest,
  UpdateFulfilAccountRequest,
  UpdateFulfilCardRequest,
  UpdateFulfilTransactionRequest,
} from '../types/fulfil';
import { getMutationErrorMessage } from '../utils/apiError';

/** Concurrent probes per wave when bulk-testing SHEIN sessions from the accounts list. */
const BULK_TEST_SESSION_CONCURRENCY = 10;

type BulkTestSessionCounts = {
  ok: number;
  sessionInvalid: number;
  proxyFailed: number;
  otherFail: number;
  requestError: number;
};

export const useFulfilAccounts = () =>
  useQuery({
    queryKey: ['fulfilAccounts'],
    queryFn: async () => {
      const response = await adminFulfilApi.getAccounts();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
  });

export const useFulfilAccountOptions = (enabled = true) =>
  useQuery({
    queryKey: ['fulfilAccountOptions'],
    queryFn: async () => {
      const response = await adminFulfilApi.getAccountOptions();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useCreateFulfilAccount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateFulfilAccountRequest) => adminFulfilApi.createAccount(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo tài khoản fulfil thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilAccountOptions'] });
      } else message.error(response.message || 'Tạo tài khoản thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useUpdateFulfilAccount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateFulfilAccountRequest }) =>
      adminFulfilApi.updateAccount(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật tài khoản fulfil thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilAccountOptions'] });
      } else message.error(response.message || 'Cập nhật tài khoản thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useDeleteFulfilAccount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminFulfilApi.deleteAccount(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa tài khoản fulfil thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilAccountOptions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilCards'] });
      } else message.error(response.message || 'Xóa tài khoản thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useSetActiveFulfilCard = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ accountId, cardId }: { accountId: string; cardId: string | null }) =>
      adminFulfilApi.setActiveCard(accountId, cardId),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật thẻ active thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilAccountOptions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilCards'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilCardOptions'] });
      } else message.error(response.message || 'Cập nhật thẻ active thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

/**
 * Cookie + header thô của một tài khoản. `enabled` mặc định false: cookie ~16 KB nên chỉ
 * fetch khi người dùng thực sự bấm xem, không kèm theo mỗi lần mở modal.
 */
export const useFulfilSheinSession = (accountId?: string, enabled = false) =>
  useQuery({
    queryKey: ['fulfilSheinSession', accountId],
    queryFn: async () => {
      const response = await adminFulfilApi.getSheinSession(accountId!);
      if (!response.success) throw new Error(response.message);
      return response.data;
    },
    enabled: enabled && !!accountId,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });

export const useSaveFulfilSheinSession = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: SaveFulfilSheinSessionRequest }) =>
      adminFulfilApi.saveSheinSession(id, data),
    onSuccess: (response, variables) => {
      if (response.success) {
        message.success(response.message || 'Đã lưu session SHEIN.');
        queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilSheinSession', variables.id] });
      } else message.error(response.message || 'Lưu session SHEIN thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useTestFulfilSheinSession = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminFulfilApi.testSheinSession(id),
    onSuccess: (response, id) => {
      if (response.success && response.data?.ok) {
        message.success(response.message || 'Session còn dùng được.');
      } else if (response.data?.proxyFailed) {
        message.warning(
          response.data.error ||
            'Proxy lỗi — cookie vẫn giữ, không cần bắt lại session.',
        );
      } else {
        message.warning(response.data?.error || response.message || 'Session không dùng được.');
      }
      // Test ghi lastCheckedAt/lastError lên account → làm mới cả list và session detail.
      queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });
      queryClient.invalidateQueries({ queryKey: ['fulfilSheinSession', id] });
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

/**
 * Probe nhiều tài khoản sẵn session: gọi endpoint test đơn lẻ, tối đa 10 request/lần.
 * Một toast tổng hợp + invalidate list một lần (không spam từng TK).
 */
export const useBulkTestFulfilSheinSessions = () => {
  const queryClient = useQueryClient();
  const [progress, setProgress] = useState({ done: 0, total: 0 });

  const mutation = useMutation({
    mutationFn: async (ids: string[]) => {
      const counts: BulkTestSessionCounts = {
        ok: 0,
        sessionInvalid: 0,
        proxyFailed: 0,
        otherFail: 0,
        requestError: 0,
      };
      setProgress({ done: 0, total: ids.length });

      for (let i = 0; i < ids.length; i += BULK_TEST_SESSION_CONCURRENCY) {
        const chunk = ids.slice(i, i + BULK_TEST_SESSION_CONCURRENCY);
        const results = await Promise.all(
          chunk.map(async (id) => {
            try {
              return await adminFulfilApi.testSheinSession(id);
            } catch {
              return null;
            }
          }),
        );

        for (const response of results) {
          if (!response) {
            counts.requestError += 1;
          } else if (response.success && response.data?.ok) {
            counts.ok += 1;
          } else if (response.data?.proxyFailed) {
            counts.proxyFailed += 1;
          } else if (response.data?.sessionInvalid) {
            counts.sessionInvalid += 1;
          } else {
            counts.otherFail += 1;
          }
        }

        setProgress({
          done: Math.min(i + BULK_TEST_SESSION_CONCURRENCY, ids.length),
          total: ids.length,
        });
      }

      return counts;
    },
    onSuccess: (counts, ids) => {
      const total = ids.length;
      const parts = [
        `${counts.ok} OK`,
        counts.sessionInvalid > 0 ? `${counts.sessionInvalid} session lỗi` : null,
        counts.proxyFailed > 0 ? `${counts.proxyFailed} proxy` : null,
        counts.otherFail > 0 ? `${counts.otherFail} lỗi` : null,
        counts.requestError > 0 ? `${counts.requestError} request lỗi` : null,
      ].filter(Boolean);

      const summary = `Kiểm tra ${total} TK: ${parts.join(' · ')}`;
      const allOk =
        counts.ok === total &&
        counts.sessionInvalid === 0 &&
        counts.proxyFailed === 0 &&
        counts.otherFail === 0 &&
        counts.requestError === 0;

      if (allOk) message.success(summary);
      else message.warning(summary);

      void queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
    onSettled: () => setProgress({ done: 0, total: 0 }),
  });

  return { ...mutation, progress };
};

export const useFulfilCards = (accountId?: string) =>
  useQuery({
    queryKey: ['fulfilCards', accountId ?? 'all'],
    queryFn: async () => {
      const response = await adminFulfilApi.getCards(accountId);
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
  });

export const useFulfilCardOptions = (accountId?: string, enabled = true) =>
  useQuery({
    queryKey: ['fulfilCardOptions', accountId ?? 'all'],
    queryFn: async () => {
      const response = await adminFulfilApi.getCardOptions(accountId);
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useCreateFulfilCard = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateFulfilCardRequest) => adminFulfilApi.createCard(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo thẻ thanh toán thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilCards'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilCardOptions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });
      } else message.error(response.message || 'Tạo thẻ thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useUpdateFulfilCard = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateFulfilCardRequest }) =>
      adminFulfilApi.updateCard(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật thẻ thanh toán thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilCards'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilCardOptions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });
      } else message.error(response.message || 'Cập nhật thẻ thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useDeleteFulfilCard = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminFulfilApi.deleteCard(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa thẻ thanh toán thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilCards'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilCardOptions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });
      } else message.error(response.message || 'Xóa thẻ thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useFulfilTransactions = (query: FulfilTransactionQuery = {}, enabled = true) =>
  useQuery({
    queryKey: ['fulfilTransactions', query],
    queryFn: async () => {
      const response = await adminFulfilApi.getTransactions(query);
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

export const useFulfilSpendingStats = (query: FulfilSpendingQuery = {}) =>
  useQuery({
    queryKey: ['fulfilSpendingStats', query],
    queryFn: async () => {
      const response = await adminFulfilApi.getSpendingStats(query);
      if (!response.success) throw new Error(response.message);
      return response.data;
    },
    placeholderData: keepPreviousData,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });

export const useCreateFulfilTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateFulfilTransactionRequest) => adminFulfilApi.createTransaction(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilSpendingStats'] });
      } else message.error(response.message || 'Tạo giao dịch thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useUpdateFulfilTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateFulfilTransactionRequest }) =>
      adminFulfilApi.updateTransaction(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilSpendingStats'] });
      } else message.error(response.message || 'Cập nhật giao dịch thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useDeleteFulfilTransaction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminFulfilApi.deleteTransaction(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa giao dịch thành công.');
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilSpendingStats'] });
      } else message.error(response.message || 'Xóa giao dịch thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useBulkDeleteFulfilTransactions = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (ids: string[]) => adminFulfilApi.bulkDeleteTransactions(ids),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã xóa giao dịch.');
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilSpendingStats'] });
      } else message.error(response.message || 'Xóa giao dịch thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};
