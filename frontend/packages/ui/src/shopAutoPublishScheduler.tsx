import { useEffect, useState } from 'react';
import { Tag, Tooltip } from 'antd';
import type { CSSProperties } from 'react';
import {
  LISTING_AUTO_PUBLISH_QUEUE_LABELS,
  LISTING_AUTO_PUBLISH_QUEUE_TOOLTIPS,
} from './listingAutoPublishSettings';

export const SHOP_AUTO_PUBLISH_SCHEDULER_TOOLTIPS = {
  interval:
    'Chờ interval shop — thời gian nghỉ riêng trước khi scheduler được pick listing kế tiếp',
  queueBacklog: (count: number) =>
    `Còn ${count} draft eligible trong hàng FIFO sau listing kế tiếp`,
  countdownReady: LISTING_AUTO_PUBLISH_QUEUE_TOOLTIPS.processingReady,
} as const;

/** Format seconds remaining until nextPickAt as M:SS or "Sắp xử lý". */
export const formatAutoPublishCountdown = (
  nextPickAt: string | null | undefined,
  nowMs: number = Date.now(),
): string => {
  if (!nextPickAt) {
    return 'Sắp xử lý';
  }
  const remainingMs = new Date(nextPickAt).getTime() - nowMs;
  if (remainingMs <= 0) {
    return 'Sắp xử lý';
  }
  const totalSec = Math.ceil(remainingMs / 1000);
  const minutes = Math.floor(totalSec / 60);
  const seconds = totalSec % 60;
  if (minutes > 0) {
    return `${minutes}:${String(seconds).padStart(2, '0')}`;
  }
  return `${seconds}s`;
};

const BADGE_STYLE: CSSProperties = {
  margin: 0,
  fontSize: 10,
  lineHeight: '16px',
  padding: '0 4px',
};

export type ShopAutoPublishSchedulerBadgeProps = {
  status?: 'processing' | 'waiting' | null;
  nextPickAt?: string | null;
  eligibleDraftCount?: number | null;
};

export const ShopAutoPublishSchedulerBadge = ({
  status,
  nextPickAt,
  eligibleDraftCount,
}: ShopAutoPublishSchedulerBadgeProps) => {
  const [nowMs, setNowMs] = useState(() => Date.now());

  useEffect(() => {
    if (status !== 'waiting') {
      return;
    }
    const id = window.setInterval(() => setNowMs(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [status, nextPickAt]);

  if (!status) {
    return null;
  }

  const queueExtra =
    eligibleDraftCount != null && eligibleDraftCount > 1 ? eligibleDraftCount - 1 : 0;

  if (status === 'processing') {
    return (
      <Tooltip title={LISTING_AUTO_PUBLISH_QUEUE_TOOLTIPS.processing}>
        <Tag color="processing" style={BADGE_STYLE}>
          {LISTING_AUTO_PUBLISH_QUEUE_LABELS.processing}
        </Tag>
      </Tooltip>
    );
  }

  const countdown = formatAutoPublishCountdown(nextPickAt, nowMs);
  const countdownReady = countdown === 'Sắp xử lý';

  return (
    <span style={{ display: 'inline-flex', flexWrap: 'wrap', gap: 4, alignItems: 'center' }}>
      <Tooltip
        title={
          countdownReady
            ? SHOP_AUTO_PUBLISH_SCHEDULER_TOOLTIPS.countdownReady
            : SHOP_AUTO_PUBLISH_SCHEDULER_TOOLTIPS.interval
        }
      >
        <Tag color="gold" style={{ ...BADGE_STYLE, fontVariantNumeric: 'tabular-nums' }}>
          {countdownReady ? LISTING_AUTO_PUBLISH_QUEUE_LABELS.processingReady : `Wait ${countdown}`}
        </Tag>
      </Tooltip>
      {queueExtra > 0 ? (
        <Tooltip title={SHOP_AUTO_PUBLISH_SCHEDULER_TOOLTIPS.queueBacklog(queueExtra)}>
          <Tag color="blue" style={BADGE_STYLE}>
            {LISTING_AUTO_PUBLISH_QUEUE_LABELS.queue} +{queueExtra}
          </Tag>
        </Tooltip>
      ) : null}
    </span>
  );
};
