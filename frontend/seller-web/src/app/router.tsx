import { lazy, Suspense } from 'react';
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { ShopTasksIndexRedirect } from '@drop/shop-tasks';
import { PageBootSpin } from '../components/PageSpin';
import { ProtectedRoute } from '../components/ProtectedRoute';
import { LoginPage } from '../features/auth/LoginPage';
import { ChangePasswordPage } from '../features/auth/ChangePasswordPage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { OrdersPage } from '../features/orders/OrdersPage';
import { ProfilePage } from '../features/profile/ProfilePage';
import { ShopsPage } from '../features/shops/ShopsPage';
import { ProductOpportunitiesPage } from '../features/product-opportunities/ProductOpportunitiesPage';
import { ProductDiscountPage } from '../features/promotions/ProductDiscountPage';
import { FlashDealPage } from '../features/promotions/FlashDealPage';
import { ShopFinancePage } from '../features/shop-finance/ShopFinancePage';
import { CompanyLedgerPage } from '../features/company-ledger/CompanyLedgerPage';
import { ReceivingBankTransactionsPage } from '../features/receiving-banks/ReceivingBankTransactionsPage';
import { FulfilTransactionsPage } from '../features/fulfil/FulfilTransactionsPage';
import { FilesPage } from '../features/files/FilesPage';
import { FourSellerConnectPage } from '../features/fourseller/FourSellerConnectPage';
import { ListingsPage } from '../features/listings/ListingsPage';
import { SupplierListsPage } from '../features/supplier-lists/SupplierListsPage';
import { SupplierSellerDetailPage } from '../features/supplier-lists/SupplierSellerDetailPage';
import { OrdersStatsPage } from '../features/stats/OrdersStatsPage';
import { RevenueStatsPage } from '../features/stats/RevenueStatsPage';
import { SellerPerformanceStatsPage } from '../features/stats/SellerPerformanceStatsPage';
import { MainLayout } from '../layouts/MainLayout';

/** Heavy edit form — code-split so list/dashboard chunks stay lean. */
const ListingEditPage = lazy(() =>
  import('../features/listings/ListingEditPage').then((m) => ({ default: m.ListingEditPage })),
);

const ListingAnalyticsPage = lazy(() =>
  import('../features/listings/ListingAnalyticsPage').then((m) => ({
    default: m.ListingAnalyticsPage,
  })),
);

const ShopTasksDailyPage = lazy(() =>
  import('../features/shop-tasks/daily/ShopTasksDailyPage').then((m) => ({
    default: m.ShopTasksDailyPage,
  })),
);
const ShopTasksOptimizePage = lazy(() =>
  import('../features/shop-tasks/optimize/ShopTasksOptimizePage').then((m) => ({
    default: m.ShopTasksOptimizePage,
  })),
);
const ShopTasksSettingsPage = lazy(() =>
  import('../features/shop-tasks/settings/ShopTasksSettingsPage').then((m) => ({
    default: m.ShopTasksSettingsPage,
  })),
);

export const AppRouter = () => (
  // RR7 mặc định bọc navigate/setSearchParams trong startTransition; sider clock 1s +
  // Suspense lazy làm URL/filter đổi mà UI/query chậm hoặc không kịp cập nhật.
  <BrowserRouter useTransitions={false}>
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        element={
          <ProtectedRoute>
            <MainLayout />
          </ProtectedRoute>
        }
      >
        <Route index element={<Navigate to="/shop-tasks/daily" replace />} />
        <Route path="dashboard" element={<DashboardPage />} />
        <Route path="orders" element={<OrdersPage />} />
        <Route path="orders/pending" element={<OrdersPage />} />
        <Route path="orders/usps" element={<OrdersPage />} />
        <Route path="orders/thank-card" element={<OrdersPage />} />
        <Route path="orders/refund" element={<OrdersPage />} />
        <Route path="stats/orders" element={<OrdersStatsPage />} />
        <Route path="stats/revenue" element={<RevenueStatsPage />} />
        <Route path="stats/seller-performance" element={<SellerPerformanceStatsPage />} />
        <Route path="shops" element={<Outlet />}>
          <Route index element={<ShopsPage />} />
          <Route path="finance" element={<ShopFinancePage />} />
        </Route>
        <Route path="ledger" element={<CompanyLedgerPage />} />
        <Route path="shop-tasks" element={<Outlet />}>
          <Route index element={<ShopTasksIndexRedirect />} />
          <Route
            path="daily"
            element={
              <Suspense fallback={<PageBootSpin tip="Đang tải trang…" />}>
                <ShopTasksDailyPage />
              </Suspense>
            }
          />
          <Route
            path="optimize"
            element={
              <Suspense fallback={<PageBootSpin tip="Đang tải trang…" />}>
                <ShopTasksOptimizePage />
              </Suspense>
            }
          />
          <Route
            path="settings"
            element={
              <Suspense fallback={<PageBootSpin tip="Đang tải trang…" />}>
                <ShopTasksSettingsPage />
              </Suspense>
            }
          />
        </Route>
        <Route path="product-opportunities" element={<ProductOpportunitiesPage />} />
        <Route path="promotions/product-discount" element={<ProductDiscountPage />} />
        <Route path="promotions/flash-deal" element={<FlashDealPage />} />
        <Route path="receiving-banks/transactions" element={<ReceivingBankTransactionsPage />} />
        <Route path="fulfil/transactions" element={<FulfilTransactionsPage />} />
        <Route path="fourseller" element={<FourSellerConnectPage />} />
        <Route path="listings" element={<Navigate to="/listings/draft" replace />} />
        <Route path="listings/draft" element={<ListingsPage />} />
        <Route path="listings/published" element={<ListingsPage />} />
        <Route
          path="listings/analytics"
          element={
            <Suspense fallback={<PageBootSpin tip="Đang tải trang…" />}>
              <ListingAnalyticsPage />
            </Suspense>
          }
        />
        <Route path="supplier-lists" element={<SupplierListsPage />} />
        <Route path="supplier-lists/:id" element={<SupplierSellerDetailPage />} />
        <Route
          path="listings/:listingId/edit"
          element={
            <Suspense fallback={<PageBootSpin tip="Đang tải trang…" />}>
              <ListingEditPage />
            </Suspense>
          }
        />
        <Route path="files" element={<FilesPage />} />
        <Route path="profile" element={<ProfilePage />} />
        <Route path="change-password" element={<ChangePasswordPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  </BrowserRouter>
);
