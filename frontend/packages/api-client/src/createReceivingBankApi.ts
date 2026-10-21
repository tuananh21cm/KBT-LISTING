import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  CreateReceivingBankTransactionRequest,
  ReceivingBankAccountOption,
  ReceivingBankTransaction,
  ReceivingBankTransactionQuery,
  SetReceivingBankConfirmationStatusRequest,
  UpdateReceivingBankTransactionRequest,
} from '@drop/types/receivingBank';

export type ReceivingBankApiBasePath =
  | '/receiving-bank-accounts'
  | '/admin/receiving-bank-accounts';

export type ReceivingBankApi = {
  getOptions: () => Promise<ApiResponse<ReceivingBankAccountOption[]>>;
  getTransactions: (
    query?: ReceivingBankTransactionQuery,
  ) => Promise<ApiResponse<ReceivingBankTransaction[]>>;
  createTransaction: (
    data: CreateReceivingBankTransactionRequest,
  ) => Promise<ApiResponse<ReceivingBankTransaction>>;
  updateTransaction: (
    id: string,
    data: UpdateReceivingBankTransactionRequest,
  ) => Promise<ApiResponse<ReceivingBankTransaction>>;
  deleteTransaction: (id: string) => Promise<ApiResponse<null>>;
  /** Admin only — set Pending / Confirmed / Rejected. */
  setConfirmationStatus?: (
    id: string,
    data: SetReceivingBankConfirmationStatusRequest,
  ) => Promise<ApiResponse<ReceivingBankTransaction>>;
  /** Admin only — Pending/Rejected → Confirmed (Đã duyệt). */
  confirmTransaction?: (id: string) => Promise<ApiResponse<ReceivingBankTransaction>>;
  /** Admin only — Manual/TikTokPayout → Rejected (Không duyệt, không cộng tổng). */
  rejectTransaction?: (id: string) => Promise<ApiResponse<ReceivingBankTransaction>>;
};

/**
 * Shared receiving-bank API client (transactions + options).
 * Admin: `/admin/receiving-bank-accounts` (+ confirm).
 * Seller: `/receiving-bank-accounts` (owner-scoped; create Pending Credit).
 */
export function createReceivingBankApi(
  http: AxiosInstance,
  opts: { basePath: ReceivingBankApiBasePath },
): ReceivingBankApi {
  const { basePath } = opts;
  const isAdmin = basePath === '/admin/receiving-bank-accounts';

  const api: ReceivingBankApi = {
    getOptions: async () => {
      const response = await http.get<ApiResponse<ReceivingBankAccountOption[]>>(
        `${basePath}/options`,
      );
      return response.data;
    },

    getTransactions: async (query = {}) => {
      const response = await http.get<ApiResponse<ReceivingBankTransaction[]>>(
        `${basePath}/transactions`,
        { params: query },
      );
      return response.data;
    },

    createTransaction: async (data) => {
      const response = await http.post<ApiResponse<ReceivingBankTransaction>>(
        `${basePath}/transactions`,
        data,
      );
      return response.data;
    },

    updateTransaction: async (id, data) => {
      const response = await http.put<ApiResponse<ReceivingBankTransaction>>(
        `${basePath}/transactions/${id}`,
        data,
      );
      return response.data;
    },

    deleteTransaction: async (id) => {
      const response = await http.delete<ApiResponse<null>>(`${basePath}/transactions/${id}`);
      return response.data;
    },
  };

  if (isAdmin) {
    api.setConfirmationStatus = async (id, data) => {
      const response = await http.post<ApiResponse<ReceivingBankTransaction>>(
        `${basePath}/transactions/${id}/confirmation-status`,
        data,
      );
      return response.data;
    };
    api.confirmTransaction = async (id) => {
      const response = await http.post<ApiResponse<ReceivingBankTransaction>>(
        `${basePath}/transactions/${id}/confirm`,
      );
      return response.data;
    };
    api.rejectTransaction = async (id) => {
      const response = await http.post<ApiResponse<ReceivingBankTransaction>>(
        `${basePath}/transactions/${id}/reject`,
      );
      return response.data;
    };
  }

  return api;
}
