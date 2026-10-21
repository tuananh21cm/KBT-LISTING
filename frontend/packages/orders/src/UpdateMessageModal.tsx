import { useEffect, useMemo, useState } from 'react';
import { Button, Form, Input, Segmented, Space, Tag, Typography } from 'antd';
import { Modal } from '@drop/ui/AppModal';
import type { IssueTag, OrderListItem } from '@drop/types/order';
import { useOrdersDeps } from './OrdersDepsContext';
import { isDoneIssueStatus, isProcessingIssueStatus } from './orderMessagePresets';

const { Text } = Typography;

type UpdateMessageModalProps = {
  order: OrderListItem | null;
  open: boolean;
  onClose: () => void;
};

export const UpdateMessageModal = ({ order, open, onClose }: UpdateMessageModalProps) => {
  const deps = useOrdersDeps();
  const libraryQuery = deps.useIssueTagLibrary?.(open) ?? { data: [] as IssueTag[], isLoading: false };
  const setTags = deps.useSetOrderIssueTags?.();
  const setStatus = deps.useSetOrderIssueStatus?.();
  const createTag = deps.useCreateIssueTag?.();
  const updateMessage = deps.useUpdateOrderMessage();

  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [status, setLocalStatus] = useState<'Processing' | 'Done'>('Processing');
  const [newLabel, setNewLabel] = useState('');
  const [saving, setSaving] = useState(false);

  const library = libraryQuery.data ?? [];

  useEffect(() => {
    if (!open || !order) return;
    setSelectedIds((order.issueTags ?? []).map((t) => t.id));
    setLocalStatus(isDoneIssueStatus(order.issueStatus) ? 'Done' : 'Processing');
    setNewLabel('');
  }, [open, order]);

  const selectedSet = useMemo(() => new Set(selectedIds), [selectedIds]);

  const toggle = (id: string) => {
    setSelectedIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  };

  const handleAddCustom = async () => {
    const label = newLabel.trim();
    if (!label || !createTag) return;
    const res = await createTag.mutateAsync({ label });
    if (res.success && res.data) {
      setSelectedIds((prev) => (prev.includes(res.data!.id) ? prev : [...prev, res.data!.id]));
      setNewLabel('');
    }
  };

  const handleOk = async () => {
    if (!order) return;
    setSaving(true);
    try {
      if (setTags) {
        const tagRes = await setTags.mutateAsync({ id: order.id, data: { tagIds: selectedIds } });
        if (!tagRes.success) return;
        if (selectedIds.length > 0 && setStatus) {
          await setStatus.mutateAsync({ id: order.id, data: { status } });
        }
        onClose();
        return;
      }
      // Fallback legacy PUT message
      const labels = library
        .filter((t) => selectedIds.includes(t.id))
        .map((t) => t.label);
      const response = await updateMessage.mutateAsync({
        id: order.id,
        data: { message: labels.join(', ') || null },
      });
      if (response.success) onClose();
    } finally {
      setSaving(false);
    }
  };

  const orderLabel = order?.platformOrderId || String(order?.fourSellerOrderId || '');
  const pending =
    saving ||
    !!setTags?.isPending ||
    !!setStatus?.isPending ||
    !!createTag?.isPending ||
    updateMessage.isPending;

  return (
    <Modal
      title={
        <div style={{ lineHeight: 1.35 }}>
          <div>Tag lỗi sản phẩm</div>
          {orderLabel ? (
            <Text
              type="secondary"
              style={{
                fontSize: 12,
                fontWeight: 400,
                fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
              }}
            >
              {orderLabel}
            </Text>
          ) : null}
        </div>
      }
      open={open}
      onCancel={onClose}
      onOk={() => void handleOk()}
      okText="Lưu"
      cancelText="Hủy"
      confirmLoading={pending}
      forceRender
      width={460}
      styles={{ body: { paddingTop: 12, paddingBottom: 8 } }}
    >
      <Form layout="vertical" requiredMark={false}>
        <Form.Item label="Tag" style={{ marginBottom: 12 }}>
          <Space size={[6, 6]} wrap>
            {library.map((tag) => {
              const active = selectedSet.has(tag.id);
              return (
                <Tag
                  key={tag.id}
                  color="error"
                  variant="solid"
                  style={{
                    marginInlineEnd: 0,
                    cursor: 'pointer',
                    fontWeight: 600,
                    opacity: active ? 1 : 0.55,
                    outline: active ? '2px solid rgba(0,0,0,0.25)' : '2px solid transparent',
                    outlineOffset: 1,
                  }}
                  onClick={() => toggle(tag.id)}
                >
                  {tag.label}
                </Tag>
              );
            })}
          </Space>
        </Form.Item>
        <Form.Item label="Thêm tag" style={{ marginBottom: 12 }}>
          <Space.Compact style={{ width: '100%' }}>
            <Input
              value={newLabel}
              onChange={(e) => setNewLabel(e.target.value)}
              maxLength={50}
              placeholder="Nhãn mới…"
              onPressEnter={() => void handleAddCustom()}
            />
            <Button
              disabled={!newLabel.trim() || !createTag}
              loading={!!createTag?.isPending}
              onClick={() => void handleAddCustom()}
            >
              Thêm
            </Button>
          </Space.Compact>
        </Form.Item>
        <Form.Item
          label="Trạng thái xử lý"
          style={{ marginBottom: 8 }}
          extra={
            isProcessingIssueStatus(status)
              ? 'Đang xử lý → hiện ở tab Hết hàng (kể cả chưa gắn tag).'
              : 'Đã xong → ra khỏi tab Hết hàng.'
          }
        >
          <Segmented
            block
            value={status}
            onChange={(v) => setLocalStatus(v as 'Processing' | 'Done')}
            options={[
              { label: 'Đang xử lý', value: 'Processing' },
              { label: 'Đã xong', value: 'Done' },
            ]}
          />
        </Form.Item>
      </Form>
    </Modal>
  );
};
