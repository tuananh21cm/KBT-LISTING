import {
  SELLER_LISTINGS_CONFIG,
  ListingsDepsProvider,
  ListingsPage as SharedListingsPage,
} from '@drop/listings';
import { useSellerListingsDeps } from './useSellerListingsDeps';

/** Seller Listings shell — wires portal deps into shared `@drop/listings` page. */
export const ListingsPage = () => {
  const deps = useSellerListingsDeps();
  return (
    <ListingsDepsProvider value={deps}>
      <SharedListingsPage config={SELLER_LISTINGS_CONFIG} />
    </ListingsDepsProvider>
  );
};
