import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { invalidateOrdersCaches } from '@drop/api-client';
import { adminOrderApi } from '../services/adminOrderApi';
import { adminSupplierPurchaseApi } from '../services/adminSupplierPurchaseApi';
import { getMutationErrorMessage } from '../utils/apiError';
import type { SupplierPurchaseQuery } from '../types/supplierPurchase';

/**
 * Gắn/gỡ một lần mua đổi giá mua và sổ cái của đơn, nên phải làm mới cả ba nhóm truy vấn — bằng
 * không danh sách đơn và sổ cái vẫn hiển thị con số trước thao tác.
 */
const invalidateAll = (queryClient: ReturnType<typeof useQueryClient>) => {
  queryClient.invalidateQueries({ queryKey: ['supplierPurchases'] });
  invalidateOrdersCaches(queryClient, 'admin');
  queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
  queryClient.invalidateQueries({ queryKey: ['fulfilSpendingStats'] });
};

export const useSupplierPurchases = (query: SupplierPurchaseQuery = {}) =>
  useQuery({
    queryKey: ['supplierPurchases', query],
    queryFn: async () => {
      const response = await adminSupplierPurchaseApi.list(query);
      if (!response.success) throw new Error(response.message);
      return (
        response.data ?? {
          items: [],
          total: 0,
          pageCurrent: query.pageCurrent ?? 1,
          pageSize: query.pageSize ?? 50,
          unmatchedCount: 0,
          ignoredCount: 0,
        }
      );
    },
    placeholderData: keepPreviousData,
  });

/**
 * Tra đơn sàn cho hộp thoại gắn tay.
 *
 * Tách khỏi `useAdminOrders` để không dùng chung cache với trang Đơn hàng: hai chỗ khác nhau về
 * khoảng ngày và cỡ trang, dùng chung sẽ làm trang kia mất bộ lọc đang xem. Chỉ chạy khi đã gõ đủ
 * ba ký tự — tải trang đầu của toàn bộ đơn thì vô nghĩa với việc tìm một đơn cụ thể.
 */
export const useOrderLookup = (search: string) => {
  const keyword = search.trim();
  return useQuery({
    queryKey: ['supplierPurchaseOrderLookup', keyword],
    queryFn: async () => {
      const response = await adminOrderApi.list({ search: keyword, pageCurrent: 1, pageSize: 20 });
      if (!response.success) throw new Error(response.message);
      return response.data?.items ?? [];
    },
    enabled: keyword.length >= 3,
    staleTime: 30_000,
  });
};

export const useAttachSupplierPurchase = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      orderId,
      fulfilAccountId,
    }: {
      id: string;
      orderId: string;
      fulfilAccountId?: string;
    }) => adminSupplierPurchaseApi.attach(id, orderId, fulfilAccountId),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã gắn đơn NCC vào đơn hàng.');
        invalidateAll(queryClient);
      } else message.error(response.message || 'Gắn đơn NCC thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

/**
 * Chuyển một dòng sổ cái Cập nhật phí sang đơn khác.
 *
 * Cùng endpoint với nút Gắn ở hàng đợi Đơn NCC — chuyển đơn cho dòng tiền **là** gắn lại lần mua
 * NCC: sửa mỗi `orderId` của dòng tiền sẽ để giá mua ở lại đơn cũ còn tiền sang đơn mới.
 */
export const useReassignPurchaseOrder = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ purchaseId, orderId }: { purchaseId: string; orderId: string }) =>
      adminSupplierPurchaseApi.attach(purchaseId, orderId),
    onSuccess: (response) => {
      if (response.success) {
        message.success('Đã chuyển giao dịch sang đơn mới.');
        invalidateAll(queryClient);
      } else message.error(response.message || 'Chuyển đơn thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useDetachSupplierPurchase = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminSupplierPurchaseApi.detach(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã gỡ đơn NCC khỏi đơn hàng.');
        invalidateAll(queryClient);
      } else message.error(response.message || 'Gỡ đơn NCC thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useIgnoreSupplierPurchase = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ignored }: { id: string; ignored: boolean }) =>
      adminSupplierPurchaseApi.ignore(id, ignored),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật.');
        invalidateAll(queryClient);
      } else message.error(response.message || 'Cập nhật thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useDeleteSupplierPurchase = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminSupplierPurchaseApi.delete(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã xóa lần mua NCC.');
        invalidateAll(queryClient);
      } else message.error(response.message || 'Xóa thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useBulkDeleteSupplierPurchases = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (ids: string[]) => adminSupplierPurchaseApi.bulkDelete(ids),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã xóa lần mua NCC.');
        invalidateAll(queryClient);
      } else message.error(response.message || 'Xóa thất bại.');
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};
