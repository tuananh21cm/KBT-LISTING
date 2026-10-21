import { Link } from 'react-router-dom';

export type DashboardStatusTone = 'default' | 'warn' | 'danger' | 'success';

export interface DashboardStatusItemProps {
  label: string;
  value: number;
  tone?: DashboardStatusTone;
  /** Client route to link to (uses react-router `Link`); omit for a static tile. */
  to?: string;
}

/** One tile in the "Tồn kho & trạng thái" / listings / shops status grid. */
export const DashboardStatusItem = ({
  label,
  value,
  tone = 'default',
  to,
}: DashboardStatusItemProps) => {
  const valueClass =
    tone === 'warn'
      ? 'dashboard-status-value is-warn'
      : tone === 'danger'
        ? 'dashboard-status-value is-danger'
        : tone === 'success'
          ? 'dashboard-status-value is-success'
          : 'dashboard-status-value';

  const content = (
    <>
      <span className="dashboard-status-label">{label}</span>
      <span className={valueClass}>{value.toLocaleString('en-US')}</span>
    </>
  );

  if (to) {
    return (
      <Link to={to} className="dashboard-status-item is-link">
        {content}
      </Link>
    );
  }

  return <div className="dashboard-status-item">{content}</div>;
};
