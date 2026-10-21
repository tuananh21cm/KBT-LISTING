import type { MouseEvent, ReactNode } from 'react';
import { FileTextOutlined } from '@ant-design/icons';
import { Badge } from 'antd';
import type { ShopHealthSummary } from '@drop/types/shopTask';

export type HealthTabKey = 'violation' | 'warning';

export type ShopHealthIndicatorsProps = {
  summary?: ShopHealthSummary | null;
  onOpen: (tab: HealthTabKey, event: MouseEvent) => void;
  /** `icon` cạnh tên shop; `avatar` gắn badge lên góc ảnh shop. */
  variant?: 'icon' | 'avatar';
  children?: ReactNode;
};
const stopRowClick = (event: MouseEvent) => {
  event.stopPropagation();
  event.preventDefault();
};

export const ShopHealthIndicators = ({
  summary,
  onOpen,
  variant = 'icon',
  children,
}: ShopHealthIndicatorsProps) => {
  if (!summary) {
    return variant === 'avatar' && children ? <>{children}</> : null;
  }

  const { activeViolationCount, activeWarningCount, handledViolationCount, handledWarningCount } =
    summary;
  const pendingTotal = activeViolationCount + activeWarningCount;
  const recordTotal = pendingTotal + handledViolationCount + handledWarningCount;
  if (recordTotal <= 0) return null;

  const initialTab: HealthTabKey =
    activeViolationCount > 0 || handledViolationCount > 0 ? 'violation' : 'warning';
  const tone = pendingTotal > 0 ? 'danger' : 'record';
  const iconColor =
    tone === 'danger'
      ? 'var(--ant-color-error, #ff4d4f)'
      : 'var(--ant-color-text-secondary, rgba(0, 0, 0, 0.45))';
  const label =
    pendingTotal > 0
      ? `Health Center: ${pendingTotal} cần xử lý${recordTotal > pendingTotal ? `, ${recordTotal} record` : ''}`
      : `Health Center: ${recordTotal} record`;

  const badgeProps = {
    count: pendingTotal > 0 ? pendingTotal : undefined,
    dot: pendingTotal === 0,
    overflowCount: 99,
    size: 'small' as const,
    classNames: { indicator: 'shop-health-indicator-badge' },
  };

  if (variant === 'avatar' && children) {
    return (
      <span
        className="shop-health-indicators shop-health-indicators--avatar"
        onClick={stopRowClick}
        onMouseDown={stopRowClick}
      >
        <button
          type="button"
          className={`shop-health-indicator is-${tone} is-avatar`}
          aria-label={label}
          title={label}
          onClick={(event) => onOpen(initialTab, event)}
          onMouseDown={stopRowClick}
        >
          <Badge {...badgeProps} offset={[-2, 2]}>
            {children}
          </Badge>
        </button>
      </span>
    );
  }

  return (
    <span
      className="shop-health-indicators"
      onClick={stopRowClick}
      onMouseDown={stopRowClick}
    >
      <button
        type="button"
        className={`shop-health-indicator is-${tone}`}
        aria-label={label}
        title={label}
        onClick={(event) => onOpen(initialTab, event)}
        onMouseDown={stopRowClick}
      >
        <Badge {...badgeProps} offset={[4, -2]}>
          <span className="shop-health-indicator-icon" style={{ color: iconColor }} aria-hidden="true">
            <FileTextOutlined />
          </span>
        </Badge>
      </button>
    </span>
  );
};
