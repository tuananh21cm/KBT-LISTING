import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { Grid, Menu, type MenuProps } from 'antd';
import {
  AppstoreOutlined,
  CheckSquareOutlined,
  LinkOutlined,
  ShopOutlined,
  UserOutlined,
  SettingOutlined,
  UnorderedListOutlined,
  TagsOutlined,
} from '@ant-design/icons';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { AppShell, NAV_LABEL, NAV_ROUTE_LABEL, NAV_ROUTE_PARENT, UsPacificClock } from '@drop/ui';
import { ProfileDropdown } from '../components/ProfileDropdown';
import { ThemeToggle } from '../components/ThemeToggle';
import { BRAND_FAVICON_URL, BRAND_LOGO_URL, BRAND_NAME } from '../constants/brand';
import { useCurrentUser } from '../hooks/useAuth';
import { useAuthStore } from '../stores/authStore';
import { useThemeStore } from '../stores/themeStore';
import { siderBgForMode } from '../theme/antdTheme';

const { useBreakpoint } = Grid;

type MenuItem = NonNullable<MenuProps['items']>[number];

const routeLabels = NAV_ROUTE_LABEL;

/** Parent group for breadcrumb when leaf sits under a submenu. */
const routeParents = NAV_ROUTE_PARENT;

type MenuItemDef = {
  key?: string;
  icon?: ReactNode;
  label?: ReactNode;
  type?: 'group';
  visible?: boolean;
  children?: Array<MenuItemDef | null>;
};

const leafMenuKeys = (items: MenuItem[]): string[] =>
  items.flatMap((item) => {
    if (!item || typeof item !== 'object') return [];
    // Groups (`type: 'group'`) often have no key — still walk children
    if ('children' in item && Array.isArray(item.children) && item.children.length > 0) {
      return leafMenuKeys(item.children as MenuItem[]);
    }
    if (!('key' in item) || item.key == null) return [];
    return [String(item.key)];
  });

/** Hide items by permission; drop empty groups/submenus. */
const resolveMenuItems = (
  defs: Array<MenuItemDef | null>,
  canSee: (visible?: boolean) => boolean,
): MenuItem[] => {
  const result: MenuItem[] = [];
  for (const def of defs) {
    if (!def) continue;
    if (def.children) {
      const children = resolveMenuItems(def.children, canSee);
      if (children.length === 0) continue;
      if (def.visible !== undefined && !canSee(def.visible)) continue;
      const { visible: _, children: __, ...rest } = def;
      result.push({ ...rest, children } as MenuItem);
      continue;
    }
    if (!canSee(def.visible)) continue;
    const { visible: _, ...item } = def;
    result.push(item as MenuItem);
  }
  return result;
};

export const AdminLayout = () => {
  const [collapsed, setCollapsed] = useState(false);
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const screens = useBreakpoint();
  const isMobile = screens.md === false;
  const navigate = useNavigate();
  const location = useLocation();
  const themeMode = useThemeStore((s) => s.mode);
  const siderBg = siderBgForMode(themeMode);
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const user = useAuthStore((s) => s.user);

  useCurrentUser();

  useEffect(() => {
    setMobileNavOpen(false);
  }, [location.pathname]);

  /** Đưa viewport về đầu trang khi đổi menu — tránh spinner/nội dung mới nằm ngoài vùng nhìn thấy. */
  useEffect(() => {
    window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
  }, [location.pathname]);

  const menuItems = useMemo(() => {
    const isAdmin = user?.roles.includes('Admin') ?? false;
    const canSee = (visible?: boolean) => visible === undefined || visible || isAdmin;
    const canSeeProductOpportunities =
      hasPermission('admin.product_opportunities.view') || isAdmin;
    const canListings =
      hasPermission('admin.listings.view') || canSeeProductOpportunities || isAdmin;

    // Thứ tự: listings → nhiệm vụ ngày → kênh bán → danh mục → hệ thống
    // Product scrape tạm ẩn menu (route vẫn có) — listing dùng mirror 4Seller
    // Tạm ẩn menu (route vẫn có): Dashboard, Đơn hàng, Promotions, Fulfillments, Thống kê,
    // Tài chính shop, Lịch sử tiền về, Thẻ nhận tiền, Nguồn hàng, Phân quyền, Sổ cái hệ thống,
    // Supplier Lists
    const defs: Array<MenuItemDef | null> = [
      canListings
        ? {
            key: 'listings',
            icon: <UnorderedListOutlined />,
            label: NAV_LABEL.listings,
            children: [
              {
                key: '/listings/draft',
                label: NAV_LABEL.listingsDraft,
                visible: hasPermission('admin.listings.view'),
              },
              {
                key: '/listings/published',
                label: NAV_LABEL.listingsPublished,
                visible: hasPermission('admin.listings.view'),
              },
              {
                key: '/product-opportunities',
                label: NAV_LABEL.productOpportunities,
                visible: hasPermission('admin.product_opportunities.view'),
              },
            ],
          }
        : null,
      {
        key: 'shop-tasks',
        icon: <CheckSquareOutlined />,
        label: NAV_LABEL.shopTasks,
        visible:
          hasPermission('admin.shop_tasks.view') || hasPermission('admin.listings.view'),
        children: [
          {
            key: '/shop-tasks/daily',
            label: NAV_LABEL.shopTasksDaily,
            visible: hasPermission('admin.shop_tasks.view'),
          },
          {
            key: '/shop-tasks/optimize',
            label: NAV_LABEL.shopTasksOptimize,
            visible: hasPermission('admin.shop_tasks.view'),
          },
          {
            key: '/listings/analytics',
            label: NAV_LABEL.listingsAnalytics,
            visible: hasPermission('admin.listings.view'),
          },
          {
            key: '/shop-tasks/settings',
            label: NAV_LABEL.shopTasksSettings,
            visible: hasPermission('admin.shop_tasks.view'),
          },
        ],
      },
      {
        key: 'channels',
        icon: <ShopOutlined />,
        label: NAV_LABEL.channels,
        children: [
          {
            key: '/shops',
            icon: <ShopOutlined />,
            label: NAV_LABEL.shops,
            visible: hasPermission('admin.shops.view'),
          },
          {
            key: '/fourseller',
            icon: <LinkOutlined />,
            label: NAV_LABEL.fourseller,
            visible: hasPermission('admin.shops.view'),
          },
          {
            key: '/platforms',
            icon: <AppstoreOutlined />,
            label: NAV_LABEL.platforms,
            visible: hasPermission('admin.platforms.view'),
          },
        ],
      },
      {
        key: 'catalog',
        icon: <TagsOutlined />,
        label: NAV_LABEL.catalog,
        children: [
          {
            key: '/categories',
            icon: <TagsOutlined />,
            label: NAV_LABEL.categories,
            visible: hasPermission('admin.categories.view'),
          },
        ],
      },
      {
        key: 'system',
        icon: <SettingOutlined />,
        label: NAV_LABEL.system,
        children: [
          {
            key: '/users',
            icon: <UserOutlined />,
            label: NAV_LABEL.users,
            visible: hasPermission('admin.users.view'),
          },
          {
            key: '/settings',
            icon: <SettingOutlined />,
            label: NAV_LABEL.settings,
            visible: hasPermission('admin.settings.view'),
          },
        ],
      },
    ];

    return resolveMenuItems(defs, canSee);
  }, [hasPermission, user?.roles]);

  const routeSelectedKey = (() => {
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
    if (location.pathname === '/orders') {
      return '/orders';
    }

    const matchLongest = (keys: string[]) =>
      [...keys]
        .sort((a, b) => b.length - a.length)
        .find((key) => location.pathname === key || location.pathname.startsWith(`${key}/`));

    const leaves = leafMenuKeys(menuItems).filter((key) => key !== '/');
    // Route tạm ẩn khỏi sidebar vẫn vào được bằng URL → lấy nhãn từ bảng route cho breadcrumb.
    const routeKeys = Object.keys(routeLabels).filter((key) => key !== '/');
    return matchLongest(leaves) ?? matchLongest(routeKeys) ?? '';
  })();

  const selectedKey = routeSelectedKey;

  const submenuKeysForPath = (pathname: string): string[] => {
    // /orders… only — not /stats/orders
    if (pathname === '/orders' || pathname.startsWith('/orders/')) return ['orders'];
    if (pathname.startsWith('/shop-tasks') || pathname.startsWith('/listings/analytics')) {
      return ['shop-tasks'];
    }
    if (pathname.startsWith('/promotions')) return ['promotions'];
    if (pathname.startsWith('/fulfil')) return ['fulfil'];
    if (pathname.startsWith('/stats')) return ['stats'];
    if (
      pathname.startsWith('/shops') ||
      pathname.startsWith('/receiving-banks') ||
      pathname.startsWith('/platforms') ||
      pathname.startsWith('/fourseller')
    ) {
      return ['channels'];
    }
    if (pathname.startsWith('/categories') || pathname.startsWith('/product-sources')) {
      return ['catalog'];
    }
    if (
      pathname.startsWith('/users') ||
      pathname.startsWith('/roles') ||
      pathname.startsWith('/settings') ||
      pathname.startsWith('/system/ledger')
    ) {
      return ['system'];
    }
    // Listings (Draft/Published) — mặc định mở rộng, gồm cả route listings và trang không khớp submenu khác.
    return ['listings'];
  };

  // Accordion: mở theo route; khi sider collapsed dùng popup — không giữ openKeys.
  const ROOT_SUBMENU_KEYS = [
    'shop-tasks',
    'orders',
    'listings',
    'promotions',
    'fulfil',
    'channels',
    'stats',
    'catalog',
    'system',
  ];

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
      // Mở submenu mới → thu gọn các root khác (accordion)
      setOpenKeys([latestOpenKey]);
      return;
    }
    setOpenKeys(keys);
  };

  const breadcrumbLeaf = (() => {
    if (location.pathname.match(/^\/listings\/[^/]+\/edit$/)) {
      return NAV_LABEL.listingEdit;
    }
    if (location.pathname.match(/^\/supplier-lists\/[^/]+$/)) {
      return NAV_LABEL.supplierListDetail;
    }
    return routeLabels[selectedKey] ?? 'Admin';
  })();

  const breadcrumbParent = (() => {
    if (location.pathname.match(/^\/listings\/[^/]+\/edit$/)) return NAV_LABEL.listings;
    if (location.pathname.match(/^\/supplier-lists\/[^/]+$/)) return NAV_LABEL.supplierLists;
    if (selectedKey && routeParents[selectedKey]) return routeParents[selectedKey];
    return null;
  })();

  const breadcrumbItems = (() => {
    const trail: { title: string }[] = [];
    if (!isMobile) trail.push({ title: 'Admin' });
    if (breadcrumbParent) trail.push({ title: breadcrumbParent });
    trail.push({ title: breadcrumbLeaf });
    return trail;
  })();

  const contentFullBleed = false;

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
      contentFullBleed={contentFullBleed}
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
            if (isMobile) setMobileNavOpen(false);
          }}
        />
      )}
    >
      <Outlet />
    </AppShell>
  );
};
