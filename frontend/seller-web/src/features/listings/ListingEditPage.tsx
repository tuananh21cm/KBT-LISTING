import {
  SELLER_LISTINGS_CONFIG,
  ListingsDepsProvider,
  ListingEditPage as SharedListingEditPage,
} from '@drop/listings';
import { useSellerListingsDeps } from './useSellerListingsDeps';

/** Seller Listing Edit shell — wires portal deps into shared `@drop/listings` page. */
export const ListingEditPage = () => {
  const deps = useSellerListingsDeps();
  return (
    <ListingsDepsProvider value={deps}>
      <SharedListingEditPage config={SELLER_LISTINGS_CONFIG} />
    </ListingsDepsProvider>
  );
};
