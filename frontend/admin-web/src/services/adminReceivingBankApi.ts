import api from './api';
import { createReceivingBankApi } from '@drop/api-client';
import type { ApiResponse } from '../types/api';
import type {
  CreateReceivingBankAccountRequest,
  CreateReceivingSourceAccountRequest,
  ReceivingBankAccount,
  ReceivingBankAccountListItem,
  ReceivingBankIncomingQuery,
  ReceivingBankIncomingSummary,
  ReceivingCardSource,
  ReceivingSourceAccount,
  ReceivingSourceAccountOption,
  UpdateReceivingBankAccountRequest,
  UpdateReceivingSourceAccountRequest,
} from '../types/receivingBank';

/** Shared txn/options client — same factory seller uses (`/receiving-bank-accounts`). */
const txnApi = createReceivingBankApi(api, { basePath: '/admin/receiving-bank-accounts' });

export const adminReceivingBankApi = {
  getSources: async (): Promise<ApiResponse<ReceivingCardSource[]>> => {
    const response = await api.get<ApiResponse<ReceivingCardSource[]>>(
      '/admin/receiving-bank-accounts/sources',
    );
    return response.data;
  },

  getSourceAccounts: async (
    sourceId?: string,
  ): Promise<ApiResponse<ReceivingSourceAccount[]>> => {
    const response = await api.get<ApiResponse<ReceivingSourceAccount[]>>(
      '/admin/receiving-bank-accounts/source-accounts',
      { params: sourceId ? { sourceId } : undefined },
    );
    return response.data;
  },

  getSourceAccountOptions: async (
    sourceId?: string,
  ): Promise<ApiResponse<ReceivingSourceAccountOption[]>> => {
    const response = await api.get<ApiResponse<ReceivingSourceAccountOption[]>>(
      '/admin/receiving-bank-accounts/source-accounts/options',
      { params: sourceId ? { sourceId } : undefined },
    );
    return response.data;
  },

  createSourceAccount: async (
    data: CreateReceivingSourceAccountRequest,
  ): Promise<ApiResponse<ReceivingSourceAccount>> => {
    const response = await api.post<ApiResponse<ReceivingSourceAccount>>(
      '/admin/receiving-bank-accounts/source-accounts',
      data,
    );
    return response.data;
  },

  updateSourceAccount: async (
    id: string,
    data: UpdateReceivingSourceAccountRequest,
  ): Promise<ApiResponse<ReceivingSourceAccount>> => {
    const response = await api.put<ApiResponse<ReceivingSourceAccount>>(
      `/admin/receiving-bank-accounts/source-accounts/${id}`,
      data,
    );
    return response.data;
  },

  deleteSourceAccount: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(
      `/admin/receiving-bank-accounts/source-accounts/${id}`,
    );
    return response.data;
  },

  getAll: async (): Promise<ApiResponse<ReceivingBankAccountListItem[]>> => {
    const response = await api.get<ApiResponse<ReceivingBankAccountListItem[]>>(
      '/admin/receiving-bank-accounts',
    );
    return response.data;
  },

  getById: async (id: string): Promise<ApiResponse<ReceivingBankAccount>> => {
    const response = await api.get<ApiResponse<ReceivingBankAccount>>(
      `/admin/receiving-bank-accounts/${id}`,
    );
    return response.data;
  },

  getOptions: txnApi.getOptions,

  create: async (
    data: CreateReceivingBankAccountRequest,
  ): Promise<ApiResponse<ReceivingBankAccount>> => {
    const response = await api.post<ApiResponse<ReceivingBankAccount>>(
      '/admin/receiving-bank-accounts',
      data,
    );
    return response.data;
  },

  update: async (
    id: string,
    data: UpdateReceivingBankAccountRequest,
  ): Promise<ApiResponse<ReceivingBankAccount>> => {
    const response = await api.put<ApiResponse<ReceivingBankAccount>>(
      `/admin/receiving-bank-accounts/${id}`,
      data,
    );
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<null>> => {
    const response = await api.delete<ApiResponse<null>>(
      `/admin/receiving-bank-accounts/${id}`,
    );
    return response.data;
  },

  getTransactions: txnApi.getTransactions,

  getTransactionStats: async (
    query: ReceivingBankIncomingQuery = {},
  ): Promise<ApiResponse<ReceivingBankIncomingSummary>> => {
    const response = await api.get<ApiResponse<ReceivingBankIncomingSummary>>(
      '/admin/receiving-bank-accounts/transactions/stats',
      { params: query },
    );
    return response.data;
  },

  createTransaction: txnApi.createTransaction,
  updateTransaction: txnApi.updateTransaction,
  deleteTransaction: txnApi.deleteTransaction,
  confirmTransaction: txnApi.confirmTransaction!,
  rejectTransaction: txnApi.rejectTransaction!,
  setConfirmationStatus: txnApi.setConfirmationStatus!,
};
