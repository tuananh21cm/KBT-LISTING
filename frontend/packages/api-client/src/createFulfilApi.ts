import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  CreateFulfilAccountRequest,
  CreateFulfilCardRequest,
  CreateFulfilTransactionRequest,
  FulfilAccount,
  FulfilAccountOption,
  FulfilCard,
  FulfilCardOption,
  FulfilSheinSession,
  FulfilSpendingQuery,
  FulfilSpendingSummary,
  FulfilTransaction,
  FulfilTransactionListPage,
  FulfilTransactionQuery,
  SaveFulfilSheinSessionRequest,
  SaveFulfilSheinSessionResult,
  TestFulfilSheinSessionResult,
  UpdateFulfilAccountRequest,
  UpdateFulfilCardRequest,
  UpdateFulfilTransactionRequest,
} from '@drop/types/fulfil';

export type FulfilApiBasePath = '/fulfil' | '/admin/fulfil';

export type FulfilApi = {
  getAccountOptions: () => Promise<ApiResponse<FulfilAccountOption[]>>;
  getCardOptions: (accountId?: string) => Promise<ApiResponse<FulfilCardOption[]>>;
  getTransactions: (
    query?: FulfilTransactionQuery,
  ) => Promise<ApiResponse<FulfilTransactionListPage>>;
  createTransaction: (
    data: CreateFulfilTransactionRequest,
  ) => Promise<ApiResponse<FulfilTransaction>>;
  updateTransaction: (
    id: string,
    data: UpdateFulfilTransactionRequest,
  ) => Promise<ApiResponse<FulfilTransaction>>;
  deleteTransaction: (id: string) => Promise<ApiResponse<null>>;

  /** Admin only */
  getAccounts?: () => Promise<ApiResponse<FulfilAccount[]>>;
  createAccount?: (data: CreateFulfilAccountRequest) => Promise<ApiResponse<FulfilAccount>>;
  updateAccount?: (
    id: string,
    data: UpdateFulfilAccountRequest,
  ) => Promise<ApiResponse<FulfilAccount>>;
  deleteAccount?: (id: string) => Promise<ApiResponse<null>>;
  setActiveCard?: (id: string, cardId: string | null) => Promise<ApiResponse<FulfilAccount>>;
  /** Cookie + header thô — endpoint riêng, chỉ gọi khi người dùng bấm xem. */
  getSheinSession?: (id: string) => Promise<ApiResponse<FulfilSheinSession>>;
  saveSheinSession?: (
    id: string,
    data: SaveFulfilSheinSessionRequest,
  ) => Promise<ApiResponse<SaveFulfilSheinSessionResult>>;
  testSheinSession?: (id: string) => Promise<ApiResponse<TestFulfilSheinSessionResult>>;
  getCards?: (accountId?: string) => Promise<ApiResponse<FulfilCard[]>>;
  createCard?: (data: CreateFulfilCardRequest) => Promise<ApiResponse<FulfilCard>>;
  updateCard?: (id: string, data: UpdateFulfilCardRequest) => Promise<ApiResponse<FulfilCard>>;
  deleteCard?: (id: string) => Promise<ApiResponse<null>>;
  getSpendingStats?: (query?: FulfilSpendingQuery) => Promise<ApiResponse<FulfilSpendingSummary>>;
  bulkDeleteTransactions?: (ids: string[]) => Promise<ApiResponse<{ deleted: number }>>;
};

/**
 * Shared fulfil API client.
 * Admin: `/admin/fulfil` (+ accounts/cards CRUD, stats, bulk-delete).
 * Seller: `/fulfil` (owner-scoped transactions + options).
 */
export function createFulfilApi(
  http: AxiosInstance,
  opts: { basePath: FulfilApiBasePath },
): FulfilApi {
  const { basePath } = opts;
  const isAdmin = basePath === '/admin/fulfil';

  const api: FulfilApi = {
    getAccountOptions: async () => {
      const response = await http.get<ApiResponse<FulfilAccountOption[]>>(
        `${basePath}/accounts/options`,
      );
      return response.data;
    },

    getCardOptions: async (accountId) => {
      const response = await http.get<ApiResponse<FulfilCardOption[]>>(
        `${basePath}/cards/options`,
        { params: accountId ? { accountId } : undefined },
      );
      return response.data;
    },

    getTransactions: async (query = {}) => {
      const response = await http.get<ApiResponse<FulfilTransactionListPage>>(
        `${basePath}/transactions`,
        { params: query },
      );
      return response.data;
    },

    createTransaction: async (data) => {
      const response = await http.post<ApiResponse<FulfilTransaction>>(
        `${basePath}/transactions`,
        data,
      );
      return response.data;
    },

    updateTransaction: async (id, data) => {
      const response = await http.put<ApiResponse<FulfilTransaction>>(
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
    api.getAccounts = async () => {
      const response = await http.get<ApiResponse<FulfilAccount[]>>(`${basePath}/accounts`);
      return response.data;
    };

    api.createAccount = async (data) => {
      const response = await http.post<ApiResponse<FulfilAccount>>(`${basePath}/accounts`, data);
      return response.data;
    };

    api.updateAccount = async (id, data) => {
      const response = await http.put<ApiResponse<FulfilAccount>>(
        `${basePath}/accounts/${id}`,
        data,
      );
      return response.data;
    };

    api.deleteAccount = async (id) => {
      const response = await http.delete<ApiResponse<null>>(`${basePath}/accounts/${id}`);
      return response.data;
    };

    api.setActiveCard = async (id, cardId) => {
      const response = await http.put<ApiResponse<FulfilAccount>>(
        `${basePath}/accounts/${id}/active-card`,
        { cardId },
      );
      return response.data;
    };

    api.getSheinSession = async (id) => {
      const response = await http.get<ApiResponse<FulfilSheinSession>>(
        `${basePath}/accounts/${id}/shein-session`,
      );
      return response.data;
    };

    api.saveSheinSession = async (id, data) => {
      const response = await http.put<ApiResponse<SaveFulfilSheinSessionResult>>(
        `${basePath}/accounts/${id}/shein-session`,
        data,
      );
      return response.data;
    };

    api.testSheinSession = async (id) => {
      const response = await http.post<ApiResponse<TestFulfilSheinSessionResult>>(
        `${basePath}/accounts/${id}/shein-session/test`,
      );
      return response.data;
    };

    api.getCards = async (accountId) => {
      const response = await http.get<ApiResponse<FulfilCard[]>>(`${basePath}/cards`, {
        params: accountId ? { accountId } : undefined,
      });
      return response.data;
    };

    api.createCard = async (data) => {
      const response = await http.post<ApiResponse<FulfilCard>>(`${basePath}/cards`, data);
      return response.data;
    };

    api.updateCard = async (id, data) => {
      const response = await http.put<ApiResponse<FulfilCard>>(`${basePath}/cards/${id}`, data);
      return response.data;
    };

    api.deleteCard = async (id) => {
      const response = await http.delete<ApiResponse<null>>(`${basePath}/cards/${id}`);
      return response.data;
    };

    api.getSpendingStats = async (query = {}) => {
      const response = await http.get<ApiResponse<FulfilSpendingSummary>>(
        `${basePath}/transactions/stats`,
        { params: query },
      );
      return response.data;
    };

    api.bulkDeleteTransactions = async (ids) => {
      const response = await http.post<ApiResponse<{ deleted: number }>>(
        `${basePath}/transactions/bulk-delete`,
        { ids },
      );
      return response.data;
    };
  }

  return api;
}
