import { CheckCircleOutlined, CloseCircleFilled, WarningFilled } from '@ant-design/icons';
import { Button, Empty, Modal, Tabs, Tag, Typography } from 'antd';
import type { ShopHealthAction, ShopHealthRecordBrief, ShopHealthSummary } from '@drop/types/shopTask';
import { useIsMobile } from '@drop/ui';
import { formatDateVn, formatDateTimeVn, parseUtcInstant } from '@drop/utils/datetime';
import type { HealthTabKey } from './ShopHealthIndicators';

const { Text } = Typography;

const DATE_ONLY = 'DD/MM/YYYY';

const formatRecordTime = (value: string): string =>
  formatDateTimeVn(value, 'DD/MM/YYYY HH:mm');

const hasActionTime = (value?: string | null): boolean => {
  if (!value) return false;
  const parsed = parseUtcInstant(value);
  return parsed.isValid() && parsed.valueOf() > 0;
};

const formatActionPeriod = (action: ShopHealthAction): string | null => {
  const hasStart = hasActionTime(action.actionStartTime);
  const hasEnd = hasActionTime(action.actionEndTime);

  if (hasStart && hasEnd) {
    return `${formatDateVn(action.actionStartTime, DATE_ONLY)} – ${formatDateVn(action.actionEndTime, DATE_ONLY)}`;
  }
  if (hasStart) {
    return `Từ ${formatDateVn(action.actionStartTime, DATE_ONLY)}`;
  }
  if (hasEnd) {
    return `Đến ${formatDateVn(action.actionEndTime, DATE_ONLY)}`;
  }
  return null;
};

const mergeRecords = (
  summary: ShopHealthSummary | null,
  recordType: HealthTabKey,
): ShopHealthRecordBrief[] => {
  if (!summary) return [];

  const byId = new Map<string, ShopHealthRecordBrief>();
  for (const record of [...summary.pendingRecent, ...summary.handledRecent]) {
    if (record.recordType !== recordType) continue;
    byId.set(record.recordId, record);
  }

  return [...byId.values()].sort(
    (a, b) => parseUtcInstant(b.createTime).valueOf() - parseUtcInstant(a.createTime).valueOf(),
  );
};

const HealthMessage = ({
  record,
  tone,
  markingRecordId,
  onMarkHandled,
}: {
  record: ShopHealthRecordBrief;
  tone: HealthTabKey;
  markingRecordId?: string | null;
  onMarkHandled?: (recordId: string) => void;
}) => {
  const isHandled = Boolean(record.handledAt);

  return (
    <article className={`shop-health-message is-${tone}${isHandled ? ' is-handled' : ''}`}>
      <header className="shop-health-message-head">
        <div className="shop-health-message-title-row">
          <Text className="shop-health-message-reason">{record.reason || '—'}</Text>
          {isHandled ? (
            <Tag color="success" className="app-tag-compact shop-health-handled-tag">
              Đã xong
            </Tag>
          ) : null}
        </div>
        <Text type="secondary" className="shop-health-message-time">
          {formatRecordTime(record.createTime)}
        </Text>
      </header>
      {record.categoryType ? (
        <Text type="secondary" className="shop-health-message-category">
          {record.categoryType}
        </Text>
      ) : null}
      {record.detail ? (
        <Text type="secondary" className="shop-health-message-detail">
          {record.detail}
        </Text>
      ) : null}
      {record.actions.length > 0 ? (
        <div className="shop-health-message-actions">
          {record.actions.map((action) => {
            const period = formatActionPeriod(action);
            return (
              <div
                key={`${action.title}-${action.actionStartTime ?? ''}-${action.actionEndTime ?? ''}`}
                className="shop-health-action-item"
              >
                <Tag className="app-tag-compact">{action.title}</Tag>
                {period ? (
                  <Text type="secondary" className="shop-health-action-period">
                    {period}
                  </Text>
                ) : null}
              </div>
            );
          })}
        </div>
      ) : null}
      {!isHandled && onMarkHandled ? (
        <div className="shop-health-message-footer">
          <Button
            type="link"
            size="small"
            icon={<CheckCircleOutlined />}
            loading={markingRecordId === record.recordId}
            onClick={() => onMarkHandled(record.recordId)}
          >
            Đã xử lý
          </Button>
        </div>
      ) : null}
    </article>
  );
};

const HealthMessageList = ({
  records,
  tone,
  emptyText,
  markingRecordId,
  onMarkHandled,
}: {
  records: ShopHealthRecordBrief[];
  tone: HealthTabKey;
  emptyText: string;
  markingRecordId?: string | null;
  onMarkHandled?: (recordId: string) => void;
}) => {
  if (records.length === 0) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={emptyText} />;
  }

  return (
    <div className="shop-health-modal-list">
      {records.map((record) => (
        <HealthMessage
          key={record.recordId}
          record={record}
          tone={tone}
          markingRecordId={markingRecordId}
          onMarkHandled={onMarkHandled}
        />
      ))}
    </div>
  );
};

export type ShopHealthRecordsModalProps = {
  open: boolean;
  shopName?: string;
  summary: ShopHealthSummary | null;
  initialTab: HealthTabKey;
  markingRecordId?: string | null;
  onMarkHandled?: (recordId: string) => void;
  onClose: () => void;
};

export const ShopHealthRecordsModal = ({
  open,
  shopName,
  summary,
  initialTab,
  markingRecordId,
  onMarkHandled,
  onClose,
}: ShopHealthRecordsModalProps) => {
  const isMobile = useIsMobile();
  const violationRecords = mergeRecords(summary, 'violation');
  const warningRecords = mergeRecords(summary, 'warning');

  return (
    <Modal
      title={shopName ? `TikTok Health Center — ${shopName}` : 'TikTok Health Center'}
      open={open}
      onCancel={onClose}
      footer={null}
      width={isMobile ? '100%' : 640}
      centered={!isMobile}
      destroyOnHidden
      className={isMobile ? 'shop-health-modal is-mobile' : 'shop-health-modal'}
      mask={{ closable: true }}
      styles={isMobile ? { body: { maxHeight: '70vh', overflowY: 'auto' } } : undefined}
    >
      <Tabs
        size="small"
        defaultActiveKey={initialTab}
        key={initialTab}
        items={[
          {
            key: 'violation',
            label: (
              <span className="shop-health-tab-label">
                <CloseCircleFilled className="shop-health-tab-icon is-violation" />
                Violation records
                {violationRecords.length > 0 ? (
                  <span className="shop-health-tab-count">{violationRecords.length}</span>
                ) : null}
              </span>
            ),
            children: (
              <HealthMessageList
                records={violationRecords}
                tone="violation"
                markingRecordId={markingRecordId}
                onMarkHandled={onMarkHandled}
                emptyText="Không có violation."
              />
            ),
          },
          {
            key: 'warning',
            label: (
              <span className="shop-health-tab-label">
                <WarningFilled className="shop-health-tab-icon is-warning" />
                Warning records
                {warningRecords.length > 0 ? (
                  <span className="shop-health-tab-count">{warningRecords.length}</span>
                ) : null}
              </span>
            ),
            children: (
              <HealthMessageList
                records={warningRecords}
                tone="warning"
                markingRecordId={markingRecordId}
                onMarkHandled={onMarkHandled}
                emptyText="Không có warning."
              />
            ),
          },
        ]}
      />
    </Modal>
  );
};
