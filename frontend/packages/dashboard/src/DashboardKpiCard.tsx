import type { ReactNode } from 'react';
import { Card, Col, Row } from 'antd';
import { useNavigate } from 'react-router-dom';
import { isDarkTheme } from './theme';

export type DashboardKpiTone = 'blue' | 'green' | 'cyan' | 'orange' | 'red' | 'purple';

const KPI_TONES_LIGHT: Record<DashboardKpiTone, { iconBg: string; iconColor: string }> = {
  blue: { iconBg: '#e6f4ff', iconColor: '#1677ff' },
  green: { iconBg: '#f6ffed', iconColor: '#52c41a' },
  cyan: { iconBg: '#e6fffb', iconColor: '#13c2c2' },
  orange: { iconBg: '#fff7e6', iconColor: '#fa8c16' },
  red: { iconBg: '#fff1f0', iconColor: '#f5222d' },
  purple: { iconBg: '#f9f0ff', iconColor: '#722ed1' },
};

const KPI_TONES_DARK: Record<DashboardKpiTone, { iconBg: string; iconColor: string }> = {
  blue: { iconBg: 'rgba(22, 119, 255, 0.2)', iconColor: '#69b1ff' },
  green: { iconBg: 'rgba(82, 196, 26, 0.2)', iconColor: '#95de64' },
  cyan: { iconBg: 'rgba(19, 194, 194, 0.2)', iconColor: '#5cdbd3' },
  orange: { iconBg: 'rgba(250, 140, 22, 0.2)', iconColor: '#ffc069' },
  red: { iconBg: 'rgba(245, 34, 45, 0.2)', iconColor: '#ff7875' },
  purple: { iconBg: 'rgba(114, 46, 209, 0.2)', iconColor: '#b37feb' },
};

export const dashboardKpiTone = (tone: DashboardKpiTone) =>
  (isDarkTheme() ? KPI_TONES_DARK : KPI_TONES_LIGHT)[tone];

export interface DashboardKpiCardProps {
  label: ReactNode;
  value: ReactNode;
  suffix?: ReactNode;
  /** Small lower text on the same line as the value (e.g. margin % after Profit). */
  sub?: ReactNode;
  icon: ReactNode;
  tone?: DashboardKpiTone;
  /** Client route to navigate to on click (uses react-router `useNavigate`). */
  to?: string;
  variant?: 'primary' | 'compact';
}

/** Single KPI tile — icon chip + label + big value. Used in the dashboard KPI strip. */
export const DashboardKpiCard = ({
  label,
  value,
  suffix,
  sub,
  icon,
  tone = 'blue',
  to,
  variant = 'primary',
}: DashboardKpiCardProps) => {
  const navigate = useNavigate();
  const colors = dashboardKpiTone(tone);

  return (
    <Card
      size="small"
      className={`dashboard-kpi-card is-${variant}${to ? ' is-link' : ''}`}
      onClick={to ? () => navigate(to) : undefined}
    >
      <div className="dashboard-kpi-head">
        <span className="dashboard-kpi-label">{label}</span>
        <span
          className="dashboard-kpi-icon"
          style={{ background: colors.iconBg, color: colors.iconColor }}
        >
          {icon}
        </span>
      </div>
      <p className="dashboard-kpi-value">
        <span className="dashboard-kpi-amount">
          <span className="dashboard-kpi-amount-main">{value}</span>
          {sub != null ? <span className="dashboard-kpi-sub">{sub}</span> : null}
        </span>
        {suffix != null ? <span className="dashboard-kpi-suffix">{suffix}</span> : null}
      </p>
    </Card>
  );
};

export interface DashboardKpiItem extends DashboardKpiCardProps {
  key: string;
  /** Ant `Col` sizing; defaults match the 4–6 card dashboard strip. */
  colProps?: { xs?: number; sm?: number; flex?: string };
}

/** Row of KPI cards — same responsive grid used by both admin and seller dashboards. */
export const DashboardKpiStrip = ({ items }: { items: DashboardKpiItem[] }) => (
  <Row gutter={[10, 10]}>
    {items.map(({ key, colProps, ...card }) => (
      <Col key={key} xs={colProps?.xs ?? 12} sm={colProps?.sm ?? 8} flex={colProps?.flex ?? '1 1 160px'}>
        <DashboardKpiCard {...card} />
      </Col>
    ))}
  </Row>
);
