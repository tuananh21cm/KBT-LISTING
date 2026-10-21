import { createContext, useContext, type ReactNode } from 'react';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import type { ApiResponse } from '@drop/types/api';
import type { OrderHoverPreview } from '@drop/types/order';
import type {
  CreateFulfilTransactionRequest,
  FulfilAccountOption,
  FulfilCardOption,
  FulfilTransaction,
  FulfilTransactionListPage,
  FulfilTransactionQuery,
  UpdateFulfilTransactionRequest,
} from '@drop/types/fulfil';

/** Slim seller option for admin filter. */
export type FulfilSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
  email?: string | null;
};

/** Slim shop option for filter dropdown. */
export type FulfilShopOption = {
  id: string;
  name: string;
  profileName?: string | null;
  status?: string | null;
  /** Admin — cascade shop list when seller filter is set. */
  ownerId?: string | null;
};

/** Slim user option for paid-by dropdown. */
export type FulfilUserOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

/** Đơn sàn tìm được khi chọn đơn đích cho thao tác chuyển đơn. */
export type FulfilOrderLookupItem = {
  id: string;
  platformOrderId?: string | null;
  fourSellerOrderId?: number | null;
  buyerName?: string | null;
  postalCode?: string | null;
};

/**
 * Hooks/data the shared Transactions page needs, without importing anything app-specific.
 */
export type FulfilDeps = {
  useTransactions: (
    query: FulfilTransactionQuery,
    enabled?: boolean,
  ) => Pick<UseQueryResult<FulfilTransactionListPage>, 'data' | 'isLoading' | 'isFetching'> & {
    refetch: () => Promise<unknown>;
  };
  useAccountOptions: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<FulfilAccountOption[]>, 'data' | 'isLoading'>;
  useCardOptions: (
    accountId?: string,
    enabled?: boolean,
  ) => Pick<UseQueryResult<FulfilCardOption[]>, 'data' | 'isLoading'>;
  useCreateTransaction?: () => Pick<
    UseMutationResult<
      ApiResponse<FulfilTransaction>,
      unknown,
      CreateFulfilTransactionRequest
    >,
    'mutateAsync' | 'isPending'
  >;
  useUpdateTransaction?: () => Pick<
    UseMutationResult<
      ApiResponse<FulfilTransaction>,
      unknown,
      { id: string; data: UpdateFulfilTransactionRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useDeleteTransaction?: () => Pick<
    UseMutationResult<ApiResponse<null>, unknown, string>,
    'mutate' | 'isPending'
  >;
  useBulkDeleteTransactions?: () => Pick<
    UseMutationResult<ApiResponse<{ deleted: number }>, unknown, string[]>,
    'mutateAsync' | 'isPending'
  >;
  /** Shop filter (admin + seller). */
  useShopOptions: () => Pick<UseQueryResult<FulfilShopOption[]>, 'data' | 'isLoading'>;
  /** Admin — seller filter. */
  useSellers?: () => Pick<UseQueryResult<FulfilSellerOption[]>, 'data' | 'isLoading'>;
  /** Admin — paid-by users (enabled when manage). */
  useUsers?: (enabled?: boolean) => Pick<UseQueryResult<FulfilUserOption[]>, 'data' | 'isLoading'>;
  /** Admin — tra đơn sàn cho hộp thoại chuyển đơn (chỉ chạy khi gõ đủ ký tự). */
  useOrderLookup?: (
    search: string,
  ) => Pick<UseQueryResult<FulfilOrderLookupItem[]>, 'data' | 'isFetching'>;
  /**
   * Admin — chuyển dòng Cập nhật phí sang đơn khác bằng cách gắn lại **lần mua NCC**: giá mua và
   * dòng chi của cả đơn cũ lẫn đơn mới được dựng lại trong cùng một transaction.
   */
  useReassignPurchaseOrder?: () => Pick<
    UseMutationResult<
      ApiResponse<unknown>,
      unknown,
      { purchaseId: string; orderId: string }
    >,
    'mutateAsync' | 'isPending'
  >;
  /** Order hover/modal preview. */
  useOrderPreview: (
    id: string | null,
    enabled: boolean,
  ) => Pick<
    UseQueryResult<OrderHoverPreview | null>,
    'data' | 'isFetching' | 'isError'
  >;
  hasPermission: (permission: string) => boolean;
  currentUserId?: string | null;
};

const FulfilDepsContext = createContext<FulfilDeps | null>(null);

export const FulfilDepsProvider = ({
  value,
  children,
}: {
  value: FulfilDeps;
  children: ReactNode;
}) => <FulfilDepsContext.Provider value={value}>{children}</FulfilDepsContext.Provider>;

export const useFulfilDeps = (): FulfilDeps => {
  const deps = useContext(FulfilDepsContext);
  if (!deps) {
    throw new Error('useFulfilDeps must be used within an <FulfilDepsProvider>');
  }
  return deps;
};
