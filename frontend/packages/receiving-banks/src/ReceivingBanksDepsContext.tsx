import { createContext, useContext, type ReactNode } from 'react';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import type { ApiResponse } from '@drop/types/api';
import type { PlatformOption } from '@drop/types/platform';
import type {
  CreateReceivingBankTransactionRequest,
  ReceivingBankAccountOption,
  ReceivingBankTransaction,
  ReceivingBankTransactionQuery,
  SetReceivingBankConfirmationStatusRequest,
  UpdateReceivingBankTransactionRequest,
} from '@drop/types/receivingBank';

/** Slim shop shape for the filter / form dropdown. */
export type ReceivingBanksShopOption = {
  id: string;
  name: string;
  profileName?: string | null;
  /** Hạn thanh toán (DELI), ngày. */
  deli?: number | null;
  platformCode?: string | null;
  /** Active | Inactive | Suspended */
  status?: string | null;
  receivingBankAccountId?: string | null;
  /** Full account number when list already joins the card (seller). */
  receivingBankAccountNumber?: string | null;
  newBankNumber?: string | null;
  /** Admin — cascade shop list when seller filter is set. */
  ownerId?: string | null;
};

/** Admin seller dropdown (id + display name). */
export type ReceivingBanksSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
  email?: string | null;
};

/** First4....Last4 from account number digits. */
export const maskReceivingBankAccountMiddle = (number?: string | null) => {
  if (!number?.trim()) return null;
  const digits = number.replace(/\D/g, '');
  if (digits.length === 0) return null;
  if (digits.length <= 8) return digits;
  return `${digits.slice(0, 4)}....${digits.slice(-4)}`;
};

/** Dropdown label: Profile - Tên shop - 8Days (0039....9549) */
export const formatReceivingBanksShopLabel = (
  shop: Pick<
    ReceivingBanksShopOption,
    'name' | 'profileName' | 'deli' | 'receivingBankAccountNumber' | 'newBankNumber'
  > & { accountNumber?: string | null },
) => {
  const masked = maskReceivingBankAccountMiddle(
    shop.accountNumber || shop.receivingBankAccountNumber || shop.newBankNumber,
  );
  const base = [
    shop.profileName?.trim() || null,
    shop.name?.trim() || null,
    shop.deli != null ? `${shop.deli}Days` : null,
  ]
    .filter(Boolean)
    .join(' - ');
  if (!base && !masked) return '—';
  if (!masked) return base || '—';
  return base ? `${base} (${masked})` : `(${masked})`;
};

/**
 * Hooks/data the shared Transactions page needs, without importing anything app-specific.
 * Each portal (admin/seller) provides its own implementation.
 */
export type ReceivingBanksDeps = {
  useTransactions: (
    query: ReceivingBankTransactionQuery,
  ) => Pick<UseQueryResult<ReceivingBankTransaction[]>, 'data' | 'isLoading' | 'isFetching'> & {
    refetch: () => Promise<unknown>;
  };
  useAccountOptions: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<ReceivingBankAccountOption[]>, 'data' | 'isLoading'>;
  useShopOptions: () => Pick<UseQueryResult<ReceivingBanksShopOption[]>, 'data' | 'isLoading'>;
  /** Admin — seller filter dropdown. */
  useSellers?: () => Pick<UseQueryResult<ReceivingBanksSellerOption[]>, 'data' | 'isLoading'>;
  /** Platform catalog for shop option avatars. */
  usePlatformOptions: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<PlatformOption[]>, 'data' | 'isLoading'>;
  /** Manual create (admin Confirmed / seller Pending). */
  useCreateTransaction?: () => Pick<
    UseMutationResult<
      ApiResponse<ReceivingBankTransaction>,
      unknown,
      CreateReceivingBankTransactionRequest
    >,
    'mutateAsync' | 'isPending'
  >;
  useUpdateTransaction?: () => Pick<
    UseMutationResult<
      ApiResponse<ReceivingBankTransaction>,
      unknown,
      { id: string; data: UpdateReceivingBankTransactionRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useDeleteTransaction?: () => Pick<
    UseMutationResult<ApiResponse<null>, unknown, string>,
    'mutate' | 'isPending'
  >;
  /** Admin only — set Pending / Confirmed / Rejected. */
  useSetConfirmationStatus?: () => Pick<
    UseMutationResult<
      ApiResponse<ReceivingBankTransaction>,
      unknown,
      { id: string; confirmationStatus: SetReceivingBankConfirmationStatusRequest['confirmationStatus'] }
    >,
    'mutate' | 'isPending'
  >;
  /** Admin only — Pending/Rejected → Confirmed. */
  useConfirmTransaction?: () => Pick<
    UseMutationResult<ApiResponse<ReceivingBankTransaction>, unknown, string>,
    'mutate' | 'isPending'
  >;
  /** Admin only — Manual/TikTokPayout → Rejected. */
  useRejectTransaction?: () => Pick<
    UseMutationResult<ApiResponse<ReceivingBankTransaction>, unknown, string>,
    'mutate' | 'isPending'
  >;
  hasPermission: (permission: string) => boolean;
};

const ReceivingBanksDepsContext = createContext<ReceivingBanksDeps | null>(null);

export const ReceivingBanksDepsProvider = ({
  value,
  children,
}: {
  value: ReceivingBanksDeps;
  children: ReactNode;
}) => (
  <ReceivingBanksDepsContext.Provider value={value}>{children}</ReceivingBanksDepsContext.Provider>
);

export const useReceivingBanksDeps = (): ReceivingBanksDeps => {
  const deps = useContext(ReceivingBanksDepsContext);
  if (!deps) {
    throw new Error('useReceivingBanksDeps must be used within an <ReceivingBanksDepsProvider>');
  }
  return deps;
};
