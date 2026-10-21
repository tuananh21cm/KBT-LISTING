import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import type {
  SaveShopTaskDefinitionPayload,
  ShopTaskBoardParams,
} from '@drop/types/shopTask';
import { adminShopTaskApi } from '../services/adminShopTaskApi';

const BOARD_KEY = 'adminShopTaskBoard';
const DEFINITIONS_KEY = 'adminShopTaskDefinitions';

export const useAdminShopTaskBoard = (params: ShopTaskBoardParams) =>
  useQuery({
    queryKey: [BOARD_KEY, params],
    queryFn: async () => {
      const response = await adminShopTaskApi.board(params);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    // Trạng thái đổi theo lượt Sync của seller, không cần realtime.
    staleTime: 30_000,
  });

export const useAdminShopTaskDefinitions = (options?: { enabled?: boolean }) =>
  useQuery({
    queryKey: [DEFINITIONS_KEY],
    enabled: options?.enabled ?? true,
    queryFn: async () => {
      const response = await adminShopTaskApi.listDefinitions();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
  });

export const useSaveAdminShopTaskDefinition = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      payload,
    }: {
      id?: string;
      payload: SaveShopTaskDefinitionPayload;
    }) => {
      const response = id
        ? await adminShopTaskApi.updateDefinition(id, payload)
        : await adminShopTaskApi.createDefinition(payload);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [DEFINITIONS_KEY] });
      queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteAdminShopTaskDefinition = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const response = await adminShopTaskApi.deleteDefinition(id);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [DEFINITIONS_KEY] });
      queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useReorderAdminShopTaskDefinitions = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (orderedIds: string[]) => {
      const response = await adminShopTaskApi.reorderDefinitions({ orderedIds });
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [DEFINITIONS_KEY] });
      queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useMarkAdminHealthRecordHandled = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: { shopId: string; recordId: string }) => {
      const response = await adminShopTaskApi.markHealthRecordHandled(payload);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
      message.success('Đã đánh dấu xử lý.');
    },
    onError: (error: Error) => message.error(error.message),
  });
};
