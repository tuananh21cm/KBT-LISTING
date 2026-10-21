import { useQuery } from '@tanstack/react-query';
import { adminSupplierSellerApi } from '../services/adminSupplierSellerApi';
import type { SupplierSellerListParams } from '../types/supplierSeller';

export const ADMIN_SUPPLIER_LISTS_DEFAULT_PAGE_SIZE = 20;

export const useAdminSupplierSellers = (params: SupplierSellerListParams) =>
  useQuery({
    queryKey: ['adminSupplierSellers', params],
    queryFn: async () => {
      const response = await adminSupplierSellerApi.list(params);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được danh sách NCC.');
      }
      return response.data;
    },
    placeholderData: (previous) => previous,
    refetchOnWindowFocus: false,
  });

export const useAdminSupplierSeller = (
  id: string | undefined,
  params: SupplierSellerListParams,
  enabled = true,
) =>
  useQuery({
    queryKey: ['adminSupplierSeller', id, params],
    queryFn: async () => {
      const response = await adminSupplierSellerApi.getById(id!, params);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được shop NCC.');
      }
      return response.data;
    },
    enabled: enabled && !!id,
    refetchOnWindowFocus: false,
  });
