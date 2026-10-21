import api from './api';
import type { ApiResponse } from '../types/api';
import type {
  SupplierPurchase,
  SupplierPurchaseListPage,
  SupplierPurchaseQuery,
} from '../types/supplierPurchase';

/** Hàng đợi đơn NCC — `/api/v1/admin/supplier-purchases`. */
export const adminSupplierPurchaseApi = {
  list: async (query: SupplierPurchaseQuery = {}): Promise<ApiResponse<SupplierPurchaseListPage>> => {
    const response = await api.get<ApiResponse<SupplierPurchaseListPage>>('/admin/supplier-purchases', {
      params: query,
    });
    return response.data;
  },

  /**
   * `fulfilAccountId` chỉ gửi khi lần mua chưa quy kết được tài khoản — backend từ chối khi nó
   * khác tài khoản đã lưu, nên không thể đổi quy kết tiền qua đường này.
   */
  attach: async (
    id: string,
    orderId: string,
    fulfilAccountId?: string,
  ): Promise<ApiResponse<SupplierPurchase>> => {
    const response = await api.post<ApiResponse<SupplierPurchase>>(
      `/admin/supplier-purchases/${id}/attach`,
      { orderId, fulfilAccountId },
    );
    return response.data;
  },

  detach: async (id: string): Promise<ApiResponse<SupplierPurchase>> => {
    const response = await api.post<ApiResponse<SupplierPurchase>>(
      `/admin/supplier-purchases/${id}/detach`,
    );
    return response.data;
  },

  ignore: async (id: string, ignored: boolean): Promise<ApiResponse<SupplierPurchase>> => {
    const response = await api.post<ApiResponse<SupplierPurchase>>(
      `/admin/supplier-purchases/${id}/ignore`,
      null,
      { params: { ignored } },
    );
    return response.data;
  },

  delete: async (id: string): Promise<ApiResponse<object>> => {
    const response = await api.delete<ApiResponse<object>>(`/admin/supplier-purchases/${id}`);
    return response.data;
  },

  bulkDelete: async (ids: string[]): Promise<ApiResponse<{ deleted: number }>> => {
    const response = await api.post<ApiResponse<{ deleted: number }>>(
      '/admin/supplier-purchases/bulk-delete',
      { ids },
    );
    return response.data;
  },
};

export default adminSupplierPurchaseApi;
