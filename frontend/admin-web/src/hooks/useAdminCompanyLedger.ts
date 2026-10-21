import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { CompanyLedgerQuery, SaveCompanyLedgerEntryRequest } from '@drop/types/companyLedger';
import { adminCompanyLedgerApi } from '../services/adminCompanyLedgerApi';
import { adminShopApi } from '../services/adminShopApi';

const EMPTY_LIST = { items: [], summary: { count: 0, byCurrency: [] } };

export const useAdminCompanyLedger = (query: CompanyLedgerQuery) =>
  useQuery({
    queryKey: ['companyLedger', 'admin', query],
    queryFn: async () => {
      const response = await adminCompanyLedgerApi.list(query);
      if (!response.success) throw new Error(response.message);
      return response.data ?? EMPTY_LIST;
    },
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });

export const useAdminCompanyLedgerCategories = () =>
  useQuery({
    queryKey: ['companyLedgerCategories', 'admin'],
    queryFn: async () => {
      const response = await adminCompanyLedgerApi.getCategories();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    staleTime: 10 * 60_000,
  });

export const useAdminCompanyLedgerShops = () =>
  useQuery({
    queryKey: ['companyLedgerShops', 'admin', 'status'],
    queryFn: async () => {
      const response = await adminShopApi.getOptions!();
      if (!response.success) throw new Error(response.message);
      return (response.data ?? []).map((shop) => ({
        id: shop.id,
        name: shop.name,
        profileName: shop.profileName,
        ownerId: shop.ownerId,
        status: shop.status,
      }));
    },
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });

const invalidateLedger = (queryClient: ReturnType<typeof useQueryClient>) => {
  void queryClient.invalidateQueries({ queryKey: ['companyLedger'] });
  void queryClient.invalidateQueries({ queryKey: ['adminShopFinance'] });
};

export const useSaveCompanyLedgerEntry = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: SaveCompanyLedgerEntryRequest }) => {
      const response = id
        ? await adminCompanyLedgerApi.update!(id, body)
        : await adminCompanyLedgerApi.create!(body);
      if (!response.success) throw new Error(response.message);
      return response.data;
    },
    onSuccess: () => invalidateLedger(queryClient),
  });
};

export const useDeleteCompanyLedgerEntry = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const response = await adminCompanyLedgerApi.delete!(id);
      if (!response.success) throw new Error(response.message);
    },
    onSuccess: () => invalidateLedger(queryClient),
  });
};
