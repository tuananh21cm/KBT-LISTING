import {
  ADMIN_FOURSELLER_CONFIG,
  FourSellerDepsProvider,
  FourSellerConnectPage as SharedFourSellerConnectPage,
  type FourSellerDeps,
} from '@drop/fourseller';
import { useAuthStore } from '../../stores/authStore';
import { useAdminShops } from '../../hooks/useAdminShops';
import { usePlatformOptions } from '../../hooks/useAdminPlatforms';
import { useSellers } from '../../hooks/useSellers';
import {
  useFourSellerAuthRedirect,
  useFourSellerCheckConnection,
  useFourSellerSyncShops,
  useKbtCrmSync,
} from '../../hooks/useFourSeller';

function useAdminFourSellerDeps(): FourSellerDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useShopsList: useAdminShops,
    usePlatformOptions,
    useSellers,
    useAuthRedirect: useFourSellerAuthRedirect,
    useCheckConnection: useFourSellerCheckConnection,
    useSyncShops: useFourSellerSyncShops,
    useSyncFromCrm: useKbtCrmSync,
    hasPermission,
  };
}

/** Admin 4Seller connect shell — wires portal deps into shared `@drop/fourseller` page. */
export const FourSellerConnectPage = () => {
  const deps = useAdminFourSellerDeps();
  return (
    <FourSellerDepsProvider value={deps}>
      <SharedFourSellerConnectPage config={ADMIN_FOURSELLER_CONFIG} />
    </FourSellerDepsProvider>
  );
};
