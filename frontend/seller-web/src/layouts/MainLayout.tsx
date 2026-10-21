import { useEffect, useMemo, useState } from 'react';
import { Menu } from 'antd';
import {
  CheckSquareOutlined,
  LinkOutlined,
  ShopOutlined,
  UnorderedListOutlined,
} from '@ant-design/icons';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import {
  AppShell,
  NAV_LABEL,
  NAV_ROUTE_LABEL,
  NAV_ROUTE_PARENT,
  UsPacificClock,
} from '@drop/ui';
import { siderBgForMode } from '@drop/theme';
import { ProfileDropdown } from '../components/ProfileDropdown';
import { ThemeToggle } from '../components/ThemeToggle';
import { BRAND_FAVICON_URL, BRAND_LOGO_URL, BRAND_NAME } from '../constants/brand';
import { useCurrentUser } from '../hooks/useAuth';
import { useAuthStore } from '../stores/authStore';
import { useThemeStore } from '../stores/themeStore';

const ROOT_SUBMENU_KEYS = ['shop-tasks', 'listings', 'channels'];

const leafMenuKeys = (items: Array<{ key?: string; children?: Array<{ key?: string }> }>): string[] => {
  const keys: string[] = [];
  for (const item of items) {
    if (item.children?.length) {
      for (const child of item.children) {
        if (typeof child.key === 'string') keys.push(child.key);
      }
    } else if (typeof item.key === 'string') {
      keys.push(item.key);
    }
  }
  return keys;
};

const submenuKeysForPath = (pathname: string): string[] => {
  // /orders… only — not /stats/orders
  if (pathname === '/orders' || pathname.startsWith('/orders/')) return [];
  if (pathname.startsWith('/shop-tasks') || pathname.startsWith('/listings/analytics')) {
    return ['shop-tasks'];
  }
  if (pathname.startsWith('/promotions') || pathname.startsWith('/stats')) return [];
  if (
    pathname.startsWith('/shops') ||
    pathname.startsWith('/receiving-banks') ||
    pathname.startsWith('/fourseller') ||
    pathname.startsWith('/fulfil') ||
    pathname.startsWith('/ledger')
  ) {
    return ['channels'];
  }
  // Listings (Draft/Published/Trending) — mặc định mở rộng, gồm cả route listings.
  if (pathname.startsWith('/listings') || pathname.startsWith('/product-opportunities')) {
    return ['listings'];
  }
  return ['listings'];
};

export const MainLayout = () => {
  const [collapsed, setCollapsed] = useState(false);
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  const themeMode = useThemeStore((s) => s.mode);
  const siderBg = siderBgForMode(themeMode);
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canSeeListings = hasPermission('seller.products.view');
  const canSeeProductOpportunities = hasPermission('seller.product_opportunities.view');
  const canSeeShopTasks = hasPermission('seller.shop_tasks.view');

  useCurrentUser();

  useEffect(() => {
    setMobileNavOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
  }, [location.pathname]);

  // Thứ tự mirror admin: listings → nhiệm vụ ngày → kênh bán
  // Tạm ẩn menu (route vẫn có): Dashboard, Đơn hàng, Promotions, Thống kê,
  // Tài chính shop, Lịch sử tiền về, Lịch sử mua hàng, Sổ cái, Supplier Lists, Files, Profile
  const menuItems = useMemo(() => {
    const items = [
      canSeeListings || canSeeProductOpportunities
        ? {
            key: 'listings',
            icon: <UnorderedListOutlined />,
            label: NAV_LABEL.listings,
            children: [
              ...(canSeeListings
                ? [
                    { key: '/listings/draft', label: NAV_LABEL.listingsDraft },
                    { key: '/listings/published', label: NAV_LABEL.listingsPublished },
                  ]
                : []),
              ...(canSeeProductOpportunities
                ? [
                    {
                      key: '/product-opportunities',
                      label: NAV_LABEL.productOpportunities,
                    },
                  ]
                : []),
            ],
          }
        : null,
      canSeeShopTasks || canSeeListings
        ? {
            key: 'shop-tasks',
            icon: <CheckSquareOutlined />,
            label: NAV_LABEL.shopTasks,
            children: [
              ...(canSeeShopTasks
                ? [
                    { key: '/shop-tasks/daily', label: NAV_LABEL.shopTasksDaily },
                    { key: '/shop-tasks/optimize', label: NAV_LABEL.shopTasksOptimize },
                  ]
                : []),
              ...(canSeeListings
                ? [{ key: '/listings/analytics', label: NAV_LABEL.listingsAnalytics }]
                : []),
            ],
          }
        : null,
      {
        key: 'channels',
        icon: <ShopOutlined />,
        label: NAV_LABEL.channels,
        children: [
          { key: '/shops', icon: <ShopOutlined />, label: NAV_LABEL.shops },
          { key: '/fourseller', icon: <LinkOutlined />, label: NAV_LABEL.fourseller },
        ],
      },
    ];
    return items.filter(Boolean) as NonNullable<(typeof items)[number]>[];
  }, [canSeeListings, canSeeProductOpportunities, canSeeShopTasks]);

  const selectedKey = (() => {
    if (location.pathname === '/dashboard') {
      return '/dashboard';
    }
    // Edit page: keep Listings submenu open but don't force Draft/Published selection.
    if (location.pathname.match(/^\/listings\/[^/]+\/edit$/)) {
      return '';
    }
    if (location.pathname === '/listings/published' || location.pathname.startsWith('/listings/published/')) {
      return '/listings/published';
    }
    if (location.pathname.startsWith('/listings/analytics')) {
      return '/listings/analytics';
    }
    if (
      location.pathname === '/listings/draft' ||
      location.pathname === '/listings' ||
      location.pathname.startsWith('/listings/draft/')
    ) {
      return '/listings/draft';
    }
    if (location.pathname === '/orders/thank-card' || location.pathname.startsWith('/orders/thank-card/')) {
      return '/orders/thank-card';
    }
    if (location.pathname === '/orders/usps' || location.pathname.startsWith('/orders/usps/')) {
      return '/orders/usps';
    }
    if (location.pathname === '/orders/refund' || location.pathname.startsWith('/orders/refund/')) {
      return '/orders/refund';
    }
    if (location.pathname === '/orders/pending' || location.pathname.startsWith('/orders/pending/')) {
      return '/orders/pending';
    }
    if (location.pathname === '/orders') return '/orders';

    const matchLongest = (keys: string[]) =>
      [...keys]
        .sort((a, b) => b.length - a.length)
        .find((key) => location.pathname === key || location.pathname.startsWith(`${key}/`));

    const leaves = leafMenuKeys(menuItems).filter((key) => key !== '/');
    // Route tạm ẩn khỏi sidebar vẫn vào được bằng URL → lấy nhãn từ bảng route cho breadcrumb.
    const routeKeys = Object.keys(NAV_ROUTE_LABEL).filter((key) => key !== '/');
    return matchLongest(leaves) ?? matchLongest(routeKeys) ?? '';
  })();

  // Accordion: mở theo route; collapsed = popup mode.
  const [openKeys, setOpenKeys] = useState<string[]>(() =>
    collapsed ? [] : submenuKeysForPath(location.pathname),
  );

  useEffect(() => {
    if (collapsed) {
      setOpenKeys([]);
      return;
    }
    setOpenKeys(submenuKeysForPath(location.pathname));
  }, [location.pathname, collapsed]);

  const handleOpenChange = (keys: string[]) => {
    if (collapsed) {
      setOpenKeys(keys);
      return;
    }
    const latestOpenKey = keys.find((key) => !openKeys.includes(key));
    if (latestOpenKey && ROOT_SUBMENU_KEYS.includes(latestOpenKey)) {
      setOpenKeys([latestOpenKey]);
      return;
    }
    setOpenKeys(keys);
  };

  const breadcrumbTitle = (() => {
    if (location.pathname.match(/^\/listings\/[^/]+\/edit$/)) return NAV_LABEL.listingEdit;
    if (location.pathname.match(/^\/supplier-lists\/[^/]+$/)) return NAV_LABEL.supplierListDetail;
    return NAV_ROUTE_LABEL[selectedKey] ?? NAV_ROUTE_LABEL[location.pathname] ?? 'Trang';
  })();

  const breadcrumbParent =
    location.pathname.match(/^\/listings\/[^/]+\/edit$/)
      ? NAV_LABEL.listings
      : location.pathname.match(/^\/supplier-lists\/[^/]+$/)
        ? NAV_LABEL.supplierLists
        : selectedKey === '/fulfil/transactions'
          ? NAV_LABEL.channels
          : (NAV_ROUTE_PARENT[selectedKey] ?? null);
  const breadcrumbItems = [
    { title: 'Seller' },
    ...(breadcrumbParent ? [{ title: breadcrumbParent }] : []),
    { title: breadcrumbTitle },
  ];

  return (
    <AppShell
      brandName={BRAND_NAME}
      brandLogoUrl={BRAND_LOGO_URL}
      brandFaviconUrl={BRAND_FAVICON_URL}
      siderBg={siderBg}
      collapsed={collapsed}
      onCollapsedChange={setCollapsed}
      mobileNavOpen={mobileNavOpen}
      onMobileNavOpenChange={setMobileNavOpen}
      breadcrumbItems={breadcrumbItems}
      headerExtra={
        <>
          <ThemeToggle />
          <ProfileDropdown />
        </>
      }
      siderFooter={<UsPacificClock />}
      renderMenu={() => (
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={selectedKey ? [selectedKey] : []}
          openKeys={openKeys}
          onOpenChange={handleOpenChange}
          items={menuItems}
          onClick={({ key }) => {
            if (!key.startsWith('/')) return;
            navigate(key);
            setMobileNavOpen(false);
          }}
        />
      )}
    >
      <Outlet />
    </AppShell>
  );
};
