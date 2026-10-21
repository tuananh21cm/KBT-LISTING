import type { CSSProperties, ReactNode } from 'react';
import { Typography } from 'antd';

const { Title } = Typography;

const headerStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
  marginBottom: 12,
  minHeight: 32,
  flexWrap: 'nowrap',
};

const titleStyle: CSSProperties = {
  margin: 0,
  minWidth: 0,
  lineHeight: '32px',
  fontSize: 16,
  fontWeight: 600,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
};

interface PageHeaderProps {
  title: ReactNode;
  extra?: ReactNode;
  leading?: ReactNode;
  description?: ReactNode;
  style?: CSSProperties;
}

/** Shared page title row — keeps font size / height stable across admin pages. */
export const PageHeader = ({ title, extra, leading, description, style }: PageHeaderProps) => (
  <div style={description ? { marginBottom: 12 } : undefined}>
    <div
      className="admin-page-header"
      style={{ ...headerStyle, marginBottom: description ? 8 : 12, ...style }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0, flex: '1 1 auto' }}>
        {leading}
        <Title level={5} style={titleStyle}>
          {title}
        </Title>
      </div>
      {extra ? (
        <div className="admin-page-header-extra" style={{ flexShrink: 0 }}>
          {extra}
        </div>
      ) : null}
    </div>
    {description}
  </div>
);
