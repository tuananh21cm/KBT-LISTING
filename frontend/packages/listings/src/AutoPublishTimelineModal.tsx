import { memo, useMemo } from 'react';
import {
  Button,
  Modal,
  Space,
  Spin,
  Tag,
  Timeline,
  Tooltip,
  Typography,
} from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { formatDateTimeVn } from '@drop/utils/datetime';
import { listingAutoPublishStepLabel } from '@drop/ui';
import type { ListingAutoPublishRun } from '@drop/types/listing';

const { Text } = Typography;

const runStatusColor = (status: string): string => {
  const key = status.trim().toLowerCase();
  if (key === 'succeeded' || key === 'success') return 'green';
  if (key === 'failed' || key === 'fail') return 'red';
  if (key === 'cancelled' || key === 'canceled') return 'orange';
  return 'blue';
};

const isRetryableRunStatus = (status?: string | null): boolean => {
  if (!status) return false;
  const key = status.trim().toLowerCase();
  return key === 'failed' || key === 'fail' || key === 'cancelled' || key === 'canceled';
};

const stepTimelineColor = (step: {
  success: boolean;
  skipped: boolean;
  inProgress?: boolean;
}): 'blue' | 'green' | 'red' | 'gray' => {
  if (step.inProgress) return 'blue';
  if (step.skipped) return 'gray';
  return step.success ? 'green' : 'red';
};

export type AutoPublishTimelineModalProps = {
  open: boolean;
  listingId: string | null;
  listingTitle?: string | null;
  autoPublishSkipped?: boolean;
  autoPublishSkipReason?: string | null;
  runs?: ListingAutoPublishRun[];
  /** Chỉ true khi chưa có data (lần tải đầu) — không dùng cho background refetch. */
  loading?: boolean;
  /** Refetch nền khi đã có data; không thay toàn bộ nội dung bằng Spin. */
  refreshing?: boolean;
  retrying?: boolean;
  canRetry?: boolean;
  onClose: () => void;
  onRetry?: () => void;
};

export const AutoPublishTimelineModal = memo(function AutoPublishTimelineModal({
  open,
  listingId,
  listingTitle,
  autoPublishSkipped,
  autoPublishSkipReason,
  runs = [],
  loading = false,
  refreshing = false,
  retrying = false,
  canRetry = false,
  onClose,
  onRetry,
}: AutoPublishTimelineModalProps) {
  const latestRun = runs[0];
  const retryEligible =
    canRetry && !!onRetry && (autoPublishSkipped || isRetryableRunStatus(latestRun?.status));

  const retryButton = retryEligible ? (
    <Button
      size="small"
      color="orange"
      variant="solid"
      icon={<ReloadOutlined />}
      loading={retrying}
      onClick={() => onRetry!()}
    >
      Retry auto publish
    </Button>
  ) : null;

  const timelineItems = useMemo(() => {
    if (!latestRun?.steps?.length) return [];
    return latestRun.steps.map((step, index) => {
      const label = listingAutoPublishStepLabel(step.code, step.label);
      const duration =
        step.inProgress
          ? 'đang chạy…'
          : step.durationMs > 0
            ? `${(step.durationMs / 1000).toFixed(1)}s`
            : null;
      const subLines =
        step.subSteps?.map((sub) => {
          const subLabel = listingAutoPublishStepLabel(sub.code);
          const mark = sub.skipped ? 'skip' : sub.success ? 'ok' : 'fail';
          const detail = sub.error || sub.note;
          return `${subLabel}: ${mark}${detail ? ` — ${detail}` : ''}`;
        }) ?? [];

      return {
        key: `${latestRun.id}:${index}:${step.code}`,
        color: stepTimelineColor(step),
        children: (
          <div>
            <Text strong>{label}</Text>
            {duration ? (
              <Text type="secondary" style={{ marginLeft: 8, fontSize: 12 }}>
                {duration}
              </Text>
            ) : null}
            {step.error ? (
              <div style={{ marginTop: 4 }}>
                <Text type="danger" style={{ fontSize: 12 }}>{step.error}</Text>
              </div>
            ) : null}
            {subLines.length > 0 ? (
              <ul style={{ margin: '4px 0 0', paddingLeft: 16, fontSize: 12 }}>
                {subLines.map((line) => (
                  <li key={line}>{line}</li>
                ))}
              </ul>
            ) : null}
          </div>
        ),
      };
    });
  }, [latestRun]);

  // Spin chỉ khi chưa có data; refetch nền giữ timeline và hiện Spin nhỏ.
  const showInitialSpin = loading && !latestRun;

  return (
    <Modal
      title={
        listingTitle?.trim()
          ? `Auto publish · ${listingTitle.trim()}`
          : 'Auto publish log'
      }
      open={open}
      onCancel={onClose}
      footer={null}
      width={560}
    >
      {autoPublishSkipped ? (
        <Space style={{ marginBottom: 12 }} wrap>
          <Tag color="orange">Skipped</Tag>
          {autoPublishSkipReason ? (
            <Tooltip title={autoPublishSkipReason}>
              <Text type="secondary" style={{ fontSize: 12 }}>
                {autoPublishSkipReason}
              </Text>
            </Tooltip>
          ) : null}
          {retryButton}
        </Space>
      ) : null}

      {showInitialSpin ? (
        <div style={{ textAlign: 'center', padding: 24 }}>
          <Spin />
        </div>
      ) : !latestRun ? (
        <Text type="secondary">Chưa có log auto publish.</Text>
      ) : (
        <div>
          <Space style={{ marginBottom: 12 }} wrap size={8}>
            <Tag color={runStatusColor(latestRun.status)}>{latestRun.status}</Tag>
            <Text type="secondary" style={{ fontSize: 12 }}>
              {formatDateTimeVn(latestRun.startedAt)}
              {latestRun.finishedAt ? ` → ${formatDateTimeVn(latestRun.finishedAt)}` : ''}
            </Text>
            {latestRun.durationMs > 0 ? (
              <Text type="secondary" style={{ fontSize: 12 }}>
                {(latestRun.durationMs / 1000).toFixed(1)}s
              </Text>
            ) : null}
            {refreshing ? <Spin size="small" /> : null}
          </Space>
          {latestRun.errorMessage ? (
            <Text type="danger" style={{ display: 'block', marginBottom: 12, fontSize: 12 }}>
              {latestRun.failedStepCode ? `${latestRun.failedStepCode}: ` : ''}
              {latestRun.errorMessage}
            </Text>
          ) : null}
          {!autoPublishSkipped && retryButton ? (
            <div style={{ marginBottom: 12 }}>{retryButton}</div>
          ) : null}
          {timelineItems.length > 0 ? (
            <Timeline items={timelineItems} />
          ) : (
            <Text type="secondary">
              {latestRun.status.trim().toLowerCase() === 'running'
                ? 'Đang chạy — chờ bước đầu ghi vào log…'
                : 'Run không có step chi tiết.'}
            </Text>
          )}
          {runs.length > 1 ? (
            <Text type="secondary" style={{ fontSize: 12, marginTop: 8, display: 'block' }}>
              Hiển thị run gần nhất ({runs.length} run trong lịch sử).
            </Text>
          ) : null}
        </div>
      )}
      {listingId ? (
        <Text type="secondary" style={{ fontSize: 11, marginTop: 12, display: 'block' }}>
          Listing ID: {listingId}
        </Text>
      ) : null}
    </Modal>
  );
});
