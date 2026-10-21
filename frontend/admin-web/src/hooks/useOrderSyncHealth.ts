import { useQuery } from '@tanstack/react-query';
import { healthApi } from '../services/healthApi';

/** Polls `/health` so Settings can show auto-sync lastFinished / overdue without a separate admin API. */
export const useOrderSyncHealth = (options?: { enabled?: boolean; refetchIntervalMs?: number }) =>
  useQuery({
    queryKey: ['orderSyncHealth'],
    enabled: options?.enabled ?? true,
    refetchInterval: options?.refetchIntervalMs ?? 30_000,
    staleTime: 15_000,
    queryFn: () => healthApi.get(),
  });
