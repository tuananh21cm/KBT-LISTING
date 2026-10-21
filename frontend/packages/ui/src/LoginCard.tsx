import type { ReactNode } from 'react';
import { Card, Spin, Typography } from 'antd';
import { BRAND_LOGO_URL, BRAND_NAME } from './brand';
import { useIsMobile } from './useIsMobile';

const { Title, Text } = Typography;

export type LoginCardProps = {
  /** Portal title under logo (defaults to BRAND_NAME). */
  title?: string;
  subtitle: string;
  /** Shown while auth hydrate. */
  loading?: boolean;
  /** Optional session warning (wrong portal role). */
  sessionWarn?: ReactNode;
  children: ReactNode;
  logoUrl?: string;
};

/** Shared login chrome — same gradient/card/logo sizing for admin & seller. */
export const LoginCard = ({
  title = BRAND_NAME,
  subtitle,
  loading = false,
  sessionWarn,
  children,
  logoUrl = BRAND_LOGO_URL,
}: LoginCardProps) => {
  const isMobile = useIsMobile();

  if (loading) {
    return (
      <div className="login-page login-page--loading">
        <Spin size="large" />
      </div>
    );
  }

  return (
    <div className="login-page">
      <Card className="login-page-card" styles={{ body: { padding: isMobile ? 16 : 20 } }}>
        <div className="login-page-brand">
          <img
            src={logoUrl}
            alt={title}
            className="login-page-logo"
            width={160}
            height={60}
            decoding="async"
          />
          <Title level={4} className="login-page-title">
            {title}
          </Title>
          <Text type="secondary">{subtitle}</Text>
        </div>
        {sessionWarn}
        {children}
      </Card>
    </div>
  );
};
