import {
  ADMIN_LISTINGS_CONFIG,
  ListingsDepsProvider,
  ListingAnalyticsPage as SharedListingAnalyticsPage,
} from '@drop/listings';
import { useAdminListingsDeps } from './useAdminListingsDeps';

/** Admin Listing analytics shell — wires portal deps into shared `@drop/listings` page. */
export const ListingAnalyticsPage = () => {
  const deps = useAdminListingsDeps();
  return (
    <ListingsDepsProvider value={deps}>
      <SharedListingAnalyticsPage config={ADMIN_LISTINGS_CONFIG} />
    </ListingsDepsProvider>
  );
};
