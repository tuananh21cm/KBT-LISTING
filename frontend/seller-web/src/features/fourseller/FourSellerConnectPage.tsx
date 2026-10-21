import {
  SELLER_FOURSELLER_CONFIG,
  FourSellerDepsProvider,
  FourSellerConnectPage as SharedFourSellerConnectPage,
  type FourSellerDeps,
} from '@drop/fourseller';
import { useAuthStore } from '../../stores/authStore';
import { useShops } from '../../hooks/useShops';
import { usePlatformOptions } from '../../hooks/usePlatforms';
import {
  useFourSellerAuthRedirect,
  useFourSellerCheckConnection,
  useFourSellerSyncShops,
} from '../../hooks/useFourSeller';

function useSellerFourSellerDeps(): FourSellerDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useShopsList: useShops,
    usePlatformOptions,
    useAuthRedirect: useFourSellerAuthRedirect,
    useCheckConnection: useFourSellerCheckConnection,
    useSyncShops: useFourSellerSyncShops,
    hasPermission,
  };
}

/** Seller 4Seller connect shell — wires portal deps into shared `@drop/fourseller` page. */
export const FourSellerConnectPage = () => {
  const deps = useSellerFourSellerDeps();
  return (
    <FourSellerDepsProvider value={deps}>
      <SharedFourSellerConnectPage config={SELLER_FOURSELLER_CONFIG} />
    </FourSellerDepsProvider>
  );
};
