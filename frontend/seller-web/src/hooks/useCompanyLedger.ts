import { useQuery } from '@tanstack/react-query';
import type { CompanyLedgerQuery } from '@drop/types/companyLedger';
import { companyLedgerApi } from '../services/companyLedgerApi';
import { shopApi } from '../services/shopApi';

const EMPTY_LIST = { items: [], summary: { count: 0, byCurrency: [] } };

export const useCompanyLedger = (query: CompanyLedgerQuery) =>
  useQuery({
    queryKey: ['companyLedger', 'seller', query],
    queryFn: async () => {
      const response = await companyLedgerApi.list(query);
      if (!response.success) throw new Error(response.message);
      return response.data ?? EMPTY_LIST;
    },
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });

export const useCompanyLedgerCategories = () =>
  useQuery({
    queryKey: ['companyLedgerCategories', 'seller'],
    queryFn: async () => {
      const response = await companyLedgerApi.getCategories();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    staleTime: 10 * 60_000,
  });

export const useCompanyLedgerShops = () =>
  useQuery({
    queryKey: ['companyLedgerShops', 'seller'],
    queryFn: async () => {
      const response = await shopApi.list();
      if (!response.success) throw new Error(response.message);
      return (response.data ?? []).map((shop) => ({
        id: shop.id,
        name: shop.name,
        profileName: shop.profileName,
        status: shop.status,
      }));
    },
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
