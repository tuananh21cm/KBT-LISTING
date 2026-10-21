import { useQuery } from '@tanstack/react-query';
import { adminShopApi } from '../services/adminShopApi';
import type { ShopFinanceQuery } from '@drop/types/shopFinance';

const EMPTY_FINANCE = {
  summary: {
    shopCount: 0,
    shopsWithoutBankCount: 0,
    onHoldAmount: 0,
    onHoldInTransitAmount: 0,
    onHoldAwaitingSettlementAmount: 0,
    onHoldReserveAmount: 0,
    moneyReceived: 0,
    pendingConfirmationAmount: 0,
    acquisitionCostTotal: 0,
  },
  items: [],
};

export const useAdminShopFinance = (params: ShopFinanceQuery) =>
  useQuery({
    queryKey: ['adminShopFinance', params],
    queryFn: async () => {
      const response = await adminShopApi.getFinance(params);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? EMPTY_FINANCE;
    },
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
