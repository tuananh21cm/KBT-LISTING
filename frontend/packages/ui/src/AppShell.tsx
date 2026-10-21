import type { CSSProperties, ReactNode } from 'react';
import { Breadcrumb, Drawer, Grid, Layout, theme } from 'antd';
import { MenuFoldOutlined, MenuUnfoldOutlined } from '@ant-design/icons';
import {
  HEADER_HEIGHT,
  SIDER_COLLAPSED_WIDTH,
  SIDER_WIDTH,
} from '@drop/theme/layout';

const { Header, Sider, Content } = Layout;
const { useBreakpoint } = Grid;

export type AppShellBreadcrumbItem = { title: ReactNode };

export type AppShellProps = {
  brandName: string;
  brandLogoUrl: string;
  brandFaviconUrl: string;
  /** Factory so desktop sider + mobile drawer each get their own Menu instance. */
  renderMenu: () => ReactNode;
  breadcrumbItems: AppShellBreadcrumbItem[];
  headerExtra?: ReactNode;
  /** Optional footer under sider menu (e.g. US Pacific clock) — desktop expanded only. */
  siderFooter?: ReactNode;
  /** Full-bleed content (no card radius / tighter padding) for special edit shells. */
  contentFullBleed?: boolean;
  siderBg: string;
  collapsed: boolean;
  onCollapsedChange: (collapsed: boolean) => void;
  mobileNavOpen: boolean;
  onMobileNavOpenChange: (open: boolean) => void;
  children: ReactNode;
};

/**
 * Shared portal chrome — identical sider/header/content for admin & seller.
 * Apps only supply menu items, breadcrumbs, and header extras (profile / theme).
 */
export const AppShell = ({
  brandName,
  brandLogoUrl,
  brandFaviconUrl,
  renderMenu,
  breadcrumbItems,
  headerExtra,
  siderFooter,
  contentFullBleed = false,
  siderBg,
  collapsed,
  onCollapsedChange,
  mobileNavOpen,
  onMobileNavOpenChange,
  children,
}: AppShellProps) => {
  const { token } = theme.useToken();
  const screens = useBreakpoint();
  const isMobile = screens.md === false;

  const brandRow = (compact: boolean) => (
    <div
      style={{
        height: compact ? 40 : 48,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: compact ? 0 : '0 16px',
        overflow: 'hidden',
      }}
    >
      <img
        src={compact ? brandFaviconUrl : brandLogoUrl}
        alt={brandName}
        width={compact ? 24 : 120}
        height={compact ? 24 : 32}
        style={{
          width: compact ? 24 : '100%',
          maxWidth: compact ? 24 : 120,
          height: compact ? 24 : 32,
          objectFit: 'contain',
          flexShrink: 0,
        }}
        decoding="async"
      />
    </div>
  );

  return (
    <Layout style={{ minHeight: '100vh' }}>
      {!isMobile && (
        <Sider
          trigger={null}
          collapsible
          collapsed={collapsed}
          width={SIDER_WIDTH}
          collapsedWidth={SIDER_COLLAPSED_WIDTH}
          style={{
            position: 'fixed',
            left: 0,
            top: 0,
            bottom: 0,
            zIndex: 100,
            display: 'flex',
            flexDirection: 'column',
          }}
        >
          {brandRow(collapsed)}
          <div className="admin-sider-menu-scroll">{renderMenu()}</div>
          {!collapsed && siderFooter ? siderFooter : null}
        </Sider>
      )}

      {isMobile ? (
        <Drawer
          className="admin-nav-drawer"
          placement="left"
          open={mobileNavOpen}
          onClose={() => onMobileNavOpenChange(false)}
          size={260}
          styles={{
            body: { padding: 0, background: siderBg, overflowY: 'auto' },
            header: {
              background: siderBg,
              borderBottom: '1px solid rgba(255,255,255,0.12)',
            },
          }}
          title={
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                width: '100%',
              }}
            >
              <img
                src={brandLogoUrl}
                alt={brandName}
                width={120}
                height={32}
                style={{ width: 120, height: 32, objectFit: 'contain' }}
                decoding="async"
              />
            </span>
          }
          closeIcon={<MenuFoldOutlined style={{ color: 'rgba(255,255,255,0.85)' }} />}
        >
          {renderMenu()}
        </Drawer>
      ) : null}

      <Layout
        style={{
          marginLeft: isMobile ? 0 : collapsed ? SIDER_COLLAPSED_WIDTH : SIDER_WIDTH,
          transition: 'margin-left 0.2s',
          minWidth: 0,
        }}
      >
        <Header
          style={{
            height: HEADER_HEIGHT,
            lineHeight: `${HEADER_HEIGHT}px`,
            padding: isMobile ? '0 12px' : '0 16px',
            background: token.colorBgContainer,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            borderBottom: `1px solid ${token.colorBorderSecondary}`,
            position: 'sticky',
            top: 0,
            zIndex: 99,
            gap: 8,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, minWidth: 0, flex: 1 }}>
            {isMobile ? (
              <MenuUnfoldOutlined
                onClick={() => onMobileNavOpenChange(true)}
                style={{ fontSize: 18, cursor: 'pointer', flexShrink: 0 }}
              />
            ) : collapsed ? (
              <MenuUnfoldOutlined
                onClick={() => onCollapsedChange(false)}
                style={{ fontSize: 16, cursor: 'pointer' }}
              />
            ) : (
              <MenuFoldOutlined
                onClick={() => onCollapsedChange(true)}
                style={{ fontSize: 16, cursor: 'pointer' }}
              />
            )}
            <Breadcrumb items={breadcrumbItems} style={{ minWidth: 0, overflow: 'hidden' }} />
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexShrink: 0 }}>
            {headerExtra}
          </div>
        </Header>
        <Content style={{ margin: contentFullBleed ? 0 : isMobile ? 8 : 16 }}>
          <div
            className="admin-page-shell"
            style={
              {
                padding: contentFullBleed ? (isMobile ? 12 : 24) : isMobile ? 12 : 20,
                background: token.colorBgContainer,
                borderRadius: contentFullBleed ? 0 : 6,
                minHeight: 'calc(100vh - 96px)',
                minWidth: 0,
                overflowX: 'hidden',
              } satisfies CSSProperties
            }
          >
            {children}
          </div>
        </Content>
      </Layout>
    </Layout>
  );
};
