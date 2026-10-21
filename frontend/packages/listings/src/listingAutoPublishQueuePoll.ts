import { useMemo } from 'react';
import { useQuery, useQueryClient, type QueryKey } from '@tanstack/react-query';
import type {
  ListingAutoPublishQueueStatusItem,
  ListingListItem,
  ListingListPage,
  ListingListParams,
} from '@drop/types/listing';

export const AUTO_PUBLISH_IN_FLIGHT_STATUSES = ['queue', 'waiting', 'processing'] as const;

export type AutoPublishInFlightStatus = (typeof AUTO_PUBLISH_IN_FLIGHT_STATUSES)[number];

export const isAutoPublishInFlight = (status?: string | null): status is AutoPublishInFlightStatus =>
  AUTO_PUBLISH_IN_FLIGHT_STATUSES.includes(status as AutoPublishInFlightStatus);

export function patchListingPageAutoPublishStatus(
  page: ListingListPage,
  patches: ListingAutoPublishQueueStatusItem[],
): ListingListPage {
  if (patches.length === 0) {
    return page;
  }

  const map = new Map(patches.map((patch) => [patch.id, patch]));
  return {
    ...page,
    items: page.items.map((item) => {
      const patch = map.get(item.id);
      if (!patch) {
        return item;
      }
      return {
        ...item,
        autoPublishQueueStatus: patch.autoPublishQueueStatus ?? null,
        autoPublishNextPickAt: patch.autoPublishNextPickAt ?? null,
        lastAutoPublishRunStatus: patch.lastAutoPublishRunStatus ?? null,
        autoPublishSkipped: patch.autoPublishSkipped ?? item.autoPublishSkipped,
        autoPublishSkipReason: patch.autoPublishSkipReason ?? null,
        lastAutoPublishRunId: patch.lastAutoPublishRunId ?? null,
      };
    }),
  };
}

/** Clear skip flags then apply a fresh queue-status poll row (no full-list refetch). */
export function applyAutoPublishRetryToListPage(
  page: ListingListPage | undefined,
  listingId: string,
  patches: ListingAutoPublishQueueStatusItem[],
): ListingListPage | undefined {
  if (!page?.items?.length) {
    return page;
  }

  const cleared: ListingListPage = {
    ...page,
    items: page.items.map((item) =>
      item.id === listingId
        ? {
            ...item,
            autoPublishSkipped: false,
            autoPublishSkipReason: null,
            autoPublishSkippedAt: null,
          }
        : item,
    ),
  };

  return patches.length > 0 ? patchListingPageAutoPublishStatus(cleared, patches) : cleared;
}

const countInFlight = (items: ListingListItem[]) =>
  items.filter((item) => isAutoPublishInFlight(item.autoPublishQueueStatus)).length;

/** Poll lightweight queue status and patch the list cache in place (no full-list refetch spinner). */
export function useListingAutoPublishQueuePoll(options: {
  listQueryKey: QueryKey;
  listParams: ListingListParams;
  items: ListingListItem[];
  fetchStatuses: (ids: string[]) => Promise<ListingAutoPublishQueueStatusItem[]>;
  pollQueryKeyPrefix: readonly unknown[];
  enabled?: boolean;
  intervalMs?: number;
}) {
  const {
    listQueryKey,
    listParams,
    items,
    fetchStatuses,
    pollQueryKeyPrefix,
    enabled = true,
    intervalMs = 12_000,
  } = options;
  const queryClient = useQueryClient();

  const watchIds = useMemo(
    () =>
      items
        .filter((item) => isAutoPublishInFlight(item.autoPublishQueueStatus))
        .map((item) => item.id),
    [items],
  );

  const pollEnabled = enabled && listParams.pageType === 'draft' && watchIds.length > 0;
  const watchIdsKey = watchIds.join(',');

  useQuery({
    queryKey: [...pollQueryKeyPrefix, 'autoPublishQueueStatus', watchIdsKey],
    enabled: pollEnabled,
    refetchInterval: pollEnabled ? intervalMs : false,
    staleTime: 0,
    queryFn: async () => {
      const patches = await fetchStatuses(watchIds);
      let shouldRefreshList = false;

      queryClient.setQueryData<ListingListPage>(listQueryKey, (current) => {
        if (!current) {
          return current;
        }

        const beforeCount = countInFlight(current.items);
        const next = patchListingPageAutoPublishStatus(current, patches);
        const afterCount = countInFlight(next.items);

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
