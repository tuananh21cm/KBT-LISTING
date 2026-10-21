import {
  ADMIN_LISTINGS_CONFIG,
  ListingsDepsProvider,
  ListingsPage as SharedListingsPage,
} from '@drop/listings';
import { useAdminListingsDeps } from './useAdminListingsDeps';

/** Admin Listings shell — wires portal deps into shared `@drop/listings` page. */
export const ListingsPage = () => {
  const deps = useAdminListingsDeps();
  return (
    <ListingsDepsProvider value={deps}>
      <SharedListingsPage config={ADMIN_LISTINGS_CONFIG} />
    </ListingsDepsProvider>
  );
};
