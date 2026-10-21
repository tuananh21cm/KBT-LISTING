import { Card, Skeleton } from 'antd';
import type { ReactNode } from 'react';

/** Placeholder card while heavy listing-edit sections wait for deferred mount. */
export function DeferredSection({
  title,
  minHeight = 240,
  rows = 6,
}: {
  title?: ReactNode;
  minHeight?: number;
  rows?: number;
}) {
  return (
    <Card title={title} size="small" style={{ marginBottom: 16, minHeight }}>
      <Skeleton active paragraph={{ rows }} title={false} />
    </Card>
  );
}
