import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import type { ShopTaskBoardParams } from '@drop/types/shopTask';
import { shopTaskApi } from '../services/shopTaskApi';

const BOARD_KEY = 'shopTaskBoard';

export const useShopTaskBoard = (params: ShopTaskBoardParams) =>
  useQuery({
    queryKey: [BOARD_KEY, params],
    queryFn: async () => {
      const response = await shopTaskApi.board(params);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    // Trạng thái chỉ đổi sau mỗi lượt Sync của extension.
    staleTime: 30_000,
  });

export const useMarkShopHealthRecordHandled = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: { shopId: string; recordId: string }) => {
      const response = await shopTaskApi.markHealthRecordHandled(payload);
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