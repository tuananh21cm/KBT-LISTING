import {
  ADMIN_LISTINGS_CONFIG,
  ListingsDepsProvider,
  ListingEditPage as SharedListingEditPage,
} from '@drop/listings';
import { useAdminListingsDeps } from './useAdminListingsDeps';

/** Admin Listing Edit shell — wires portal deps into shared `@drop/listings` page. */
export const ListingEditPage = () => {
  const deps = useAdminListingsDeps();
  return (
    <ListingsDepsProvider value={deps}>
      <SharedListingEditPage config={ADMIN_LISTINGS_CONFIG} />
    </ListingsDepsProvider>
  );
};
