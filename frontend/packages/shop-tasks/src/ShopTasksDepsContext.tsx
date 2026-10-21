import { createContext, useContext, type ReactNode } from 'react';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import type {
  ListingDelistSuggestionParams,
  ListingDelistSuggestions,
} from '@drop/types/listing';
import type {
  MarkShopHealthRecordHandledPayload,
  SaveShopTaskDefinitionPayload,
  ShopTaskBoard,
  ShopTaskBoardParams,
  ShopTaskDefinition,
} from '@drop/types/shopTask';

/** Chủ shop cho dropdown lọc (chỉ portal admin dùng). */
export type ShopTasksOwnerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

export type ShopTasksDeps = {
  /** Bảng nhiệm vụ theo ngày — mỗi portal wire hook React Query riêng. */
  useTaskBoard: (params: ShopTaskBoardParams) => UseQueryResult<ShopTaskBoard | null | undefined>;
  /** Danh sách seller cho dropdown lọc (chỉ admin cung cấp). */
  useOwners?: () => UseQueryResult<ShopTasksOwnerOption[] | null | undefined>;
  /** CRUD nhiệm vụ truy cập URL — chỉ admin cung cấp. */
  useTaskDefinitions?: (options?: {
    enabled?: boolean;
  }) => UseQueryResult<ShopTaskDefinition[] | null | undefined>;
  useSaveTaskDefinition?: () => UseMutationResult<
    unknown,
    unknown,
    { id?: string; payload: SaveShopTaskDefinitionPayload }
  >;
  useDeleteTaskDefinition?: () => UseMutationResult<unknown, unknown, string>;
  useReorderTaskDefinitions?: () => UseMutationResult<unknown, unknown, string[]>;
  /** Đánh dấu health record đã xử lý. */
  useMarkHealthRecordHandled?: () => UseMutationResult<
    unknown,
    unknown,
    MarkShopHealthRecordHandledPayload
  >;
  /** Đề xuất hủy list theo shop — lazy khi mở modal Listing cần tối ưu. */
  useListingDelistSuggestions?: (
    params: ListingDelistSuggestionParams,
    options?: { enabled?: boolean },
  ) => Pick<
    UseQueryResult<ListingDelistSuggestions>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'error'
  > & {
    refetch: () => Promise<unknown>;
  };
  useDeactivateListing?: () => Pick<
    UseMutationResult<unknown, unknown, string>,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  useDeleteListing?: () => Pick<
    UseMutationResult<unknown, unknown, string>,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  hasPermission: (permission: string) => boolean;
  /** Điều hướng nội bộ (react-router `useNavigate` của từng app). */
  navigate: (path: string) => void;
};

const ShopTasksDepsContext = createContext<ShopTasksDeps | null>(null);

export const ShopTasksDepsProvider = ({
  value,
  children,
}: {
  value: ShopTasksDeps;
  children: ReactNode;
}) => <ShopTasksDepsContext.Provider value={value}>{children}</ShopTasksDepsContext.Provider>;

export const useShopTasksDeps = (): ShopTasksDeps => {
  const deps = useContext(ShopTasksDepsContext);
  if (!deps) {
    throw new Error('useShopTasksDeps phải nằm trong <ShopTasksDepsProvider>.');
  }
  return deps;
};
