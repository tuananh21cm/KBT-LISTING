import { useQuery } from '@tanstack/react-query';
import { supplierSellerApi } from '../services/supplierSellerApi';
import type { SupplierSellerListParams } from '../types/supplierSeller';

export const SUPPLIER_LISTS_DEFAULT_PAGE_SIZE = 20;

export const useSupplierSellers = (params: SupplierSellerListParams) =>
  useQuery({
    queryKey: ['supplierSellers', params],
    queryFn: async () => {
      const response = await supplierSellerApi.list(params);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được danh sách NCC.');
      }
      return response.data;
    },
    placeholderData: (previous) => previous,
    refetchOnWindowFocus: false,
  });

export const useSupplierSeller = (
  id: string | undefined,
  params: SupplierSellerListParams,
  enabled = true,
) =>
  useQuery({
    queryKey: ['supplierSeller', id, params],
    queryFn: async () => {
      const response = await supplierSellerApi.getById(id!, params);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được shop NCC.');
      }
      return response.data;
    },
    enabled: enabled && !!id,
    refetchOnWindowFocus: false,
  });
