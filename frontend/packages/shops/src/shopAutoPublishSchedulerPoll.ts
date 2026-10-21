import { useMemo } from 'react';
import { useQuery, useQueryClient, type QueryKey } from '@tanstack/react-query';
import type { ShopAutoPublishSchedulerStatusItem, ShopListItem } from '@drop/types/shop';

export const SHOP_SCHEDULER_IN_FLIGHT_STATUSES = ['processing', 'waiting'] as const;

export type ShopSchedulerInFlightStatus = (typeof SHOP_SCHEDULER_IN_FLIGHT_STATUSES)[number];

export const isShopSchedulerInFlight = (
  status?: string | null,
): status is ShopSchedulerInFlightStatus =>
  SHOP_SCHEDULER_IN_FLIGHT_STATUSES.includes(status as ShopSchedulerInFlightStatus);

export function patchShopListSchedulerStatus(
  shops: ShopListItem[],
  patches: ShopAutoPublishSchedulerStatusItem[],
): ShopListItem[] {
  if (patches.length === 0) {
    return shops;
  }

  const map = new Map(patches.map((patch) => [patch.id, patch]));
  return shops.map((shop) => {
    const patch = map.get(shop.id);
    if (!patch) {
      return shop;
    }
    return {
      ...shop,
      autoPublishSchedulerStatus: patch.autoPublishSchedulerStatus ?? null,
      autoPublishNextPickAt: patch.autoPublishNextPickAt ?? null,
      autoPublishEligibleDraftCount: patch.autoPublishEligibleDraftCount ?? null,
    };
  });
}

const countInFlight = (shops: ShopListItem[]) =>
  shops.filter(
    (shop) =>
      shop.autoPublishEnabled && isShopSchedulerInFlight(shop.autoPublishSchedulerStatus),
  ).length;

/** Poll lightweight scheduler status and patch the list cache in place (no full-list refetch spinner). */
export function useShopAutoPublishSchedulerPoll(options: {
  listQueryKey: QueryKey;
  shops: ShopListItem[];
  fetchStatuses: (ids: string[]) => Promise<ShopAutoPublishSchedulerStatusItem[]>;
  pollQueryKeyPrefix: readonly unknown[];
  enabled?: boolean;
  intervalMs?: number;
}) {
  const {
    listQueryKey,
    shops,
    fetchStatuses,
    pollQueryKeyPrefix,
    enabled = true,
    intervalMs = 12_000,
  } = options;
  const queryClient = useQueryClient();

  const watchIds = useMemo(
    () =>
      shops
        .filter(
          (shop) =>
            shop.autoPublishEnabled && isShopSchedulerInFlight(shop.autoPublishSchedulerStatus),
        )
        .map((shop) => shop.id),
    [shops],
  );

  const pollEnabled = enabled && watchIds.length > 0;
  const watchIdsKey = watchIds.join(',');

  useQuery({
    queryKey: [...pollQueryKeyPrefix, 'autoPublishSchedulerStatus', watchIdsKey],
    enabled: pollEnabled,
    refetchInterval: pollEnabled ? intervalMs : false,
    staleTime: 0,
    queryFn: async () => {
      const patches = await fetchStatuses(watchIds);
      let shouldRefreshList = false;

      queryClient.setQueryData<ShopListItem[]>(listQueryKey, (current) => {
        if (!current) {
          return current;
        }

        const beforeCount = countInFlight(current);
        const next = patchShopListSchedulerStatus(current, patches);
        const afterCount = countInFlight(next);

        if (beforeCount > 0 && afterCount === 0) {
          shouldRefreshList = true;
        }

        return next;
      });

      if (shouldRefreshList) {
        void queryClient.invalidateQueries({
          queryKey: listQueryKey,
          refetchType: 'active',
        });
      }

      return patches;
    },
  });
}
