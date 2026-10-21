import { lazy, Suspense } from 'react';
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { ShopTasksIndexRedirect } from '@drop/shop-tasks';
import { PageBootSpin } from '../components/PageSpin';
import { ProtectedRoute } from '../components/ProtectedRoute';
import { LoginPage } from '../features/auth/LoginPage';
import { PlatformsPage } from '../features/platforms/PlatformsPage';
import { ProductSourcesPage } from '../features/product-sources/ProductSourcesPage';
import { ProductOpportunitiesPage } from '../features/product-opportunities/ProductOpportunitiesPage';
import { ProductDiscountPage } from '../features/promotions/ProductDiscountPage';
import { FlashDealPage } from '../features/promotions/FlashDealPage';
import { RolesPage } from '../features/roles/RolesPage';
import { SettingsPage } from '../features/settings/SettingsPage';
import { ShopsPage } from '../features/shops/ShopsPage';
import { ShopFinancePage } from '../features/shop-finance/ShopFinancePage';
import { CompanyLedgerPage } from '../features/company-ledger/CompanyLedgerPage';
import { ReceivingBanksPage } from '../features/receiving-banks/ReceivingBanksPage';
import { ReceivingBankTransactionsPage } from '../features/receiving-banks/ReceivingBankTransactionsPage';
import { FourSellerConnectPage } from '../features/fourseller/FourSellerConnectPage';
import { CategoriesPage } from '../features/categories/CategoriesPage';
import { ProductScrapePage } from '../features/product-scrape/ProductScrapePage';
import { UsersPage } from '../features/users/UsersPage';
import { OrdersPage } from '../features/orders/OrdersPage';
import { ListingsPage } from '../features/listings/ListingsPage';
import { SupplierListsPage } from '../features/supplier-lists/SupplierListsPage';
import { SupplierSellerDetailPage } from '../features/supplier-lists/SupplierSellerDetailPage';
import { FulfilAccountsPage } from '../features/fulfil/FulfilAccountsPage';
import { FulfilCardsPage } from '../features/fulfil/FulfilCardsPage';
import { FulfilTransactionsPage } from '../features/fulfil/FulfilTransactionsPage';
import { SupplierPurchasesPage } from '../features/fulfil/SupplierPurchasesPage';
import { AdminLayout } from '../layouts/AdminLayout';
import {
  DashboardHomeRedirect,
  HomeRedirect,
} from '../features/dashboard/DashboardHomeRedirect';
import { OrdersStatsPage } from '../features/stats/OrdersStatsPage';
import { RevenueStatsPage } from '../features/stats/RevenueStatsPage';
import { SellerPerformanceStatsPage } from '../features/stats/SellerPerformanceStatsPage';
import { ChartsStatsPage } from '../features/stats/ChartsStatsPage';
import { CardSpendingStatsPage } from '../features/stats/CardSpendingStatsPage';
import { ReceivingBankStatsPage } from '../features/stats/ReceivingBankStatsPage';

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
            <AdminLayout />
          </ProtectedRoute>
        }
      >
        <Route index element={<HomeRedirect />} />
        <Route path="dashboard" element={<DashboardHomeRedirect />} />
        <Route path="stats/orders" element={<OrdersStatsPage />} />
        <Route path="stats/revenue" element={<RevenueStatsPage />} />
        <Route path="stats/seller-performance" element={<SellerPerformanceStatsPage />} />
        <Route path="stats/charts" element={<ChartsStatsPage />} />
        <Route path="stats/card-spending" element={<CardSpendingStatsPage />} />
        <Route path="stats/receiving-banks" element={<ReceivingBankStatsPage />} />
        <Route path="shops" element={<Outlet />}>
          <Route index element={<ShopsPage />} />
          <Route path="finance" element={<ShopFinancePage />} />
        </Route>
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
        <Route path="receiving-banks" element={<ReceivingBanksPage />} />
        <Route path="receiving-banks/transactions" element={<ReceivingBankTransactionsPage />} />
        <Route path="fourseller" element={<FourSellerConnectPage />} />
        <Route path="orders" element={<OrdersPage />} />
        <Route path="orders/pending" element={<OrdersPage />} />
        <Route path="orders/usps" element={<OrdersPage />} />
        <Route path="orders/thank-card" element={<OrdersPage />} />
        <Route path="orders/refund" element={<OrdersPage />} />
        <Route path="fulfil/accounts" element={<FulfilAccountsPage />} />
        <Route path="fulfil/cards" element={<FulfilCardsPage />} />
        <Route path="fulfil/transactions" element={<FulfilTransactionsPage />} />
        <Route path="fulfil/supplier-purchases" element={<SupplierPurchasesPage />} />
        <Route path="fulfil/transactions/accounts" element={<Navigate to="/fulfil/transactions" replace />} />
        <Route path="fulfil/transactions/cards" element={<Navigate to="/fulfil/transactions" replace />} />
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
        <Route path="platforms" element={<PlatformsPage />} />
        <Route path="users" element={<UsersPage />} />
        <Route path="roles" element={<RolesPage />} />
        <Route path="product-scrape" element={<ProductScrapePage />} />
        <Route path="product-sources" element={<ProductSourcesPage />} />
        <Route path="product-opportunities" element={<ProductOpportunitiesPage />} />
        <Route path="promotions/product-discount" element={<ProductDiscountPage />} />
        <Route path="promotions/flash-deal" element={<FlashDealPage />} />
        <Route path="categories" element={<CategoriesPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="system/ledger" element={<CompanyLedgerPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  </BrowserRouter>
);
