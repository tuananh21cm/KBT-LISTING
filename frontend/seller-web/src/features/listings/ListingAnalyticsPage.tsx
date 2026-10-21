import {
  SELLER_LISTINGS_CONFIG,
  ListingsDepsProvider,
  ListingAnalyticsPage as SharedListingAnalyticsPage,
} from '@drop/listings';
import { useSellerListingsDeps } from './useSellerListingsDeps';

/** Seller Listing analytics shell — wires portal deps into shared `@drop/listings` page. */
export const ListingAnalyticsPage = () => {
  const deps = useSellerListingsDeps();
  return (
    <ListingsDepsProvider value={deps}>
      <SharedListingAnalyticsPage config={SELLER_LISTINGS_CONFIG} />
    </ListingsDepsProvider>
  );
};
