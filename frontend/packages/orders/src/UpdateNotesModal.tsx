import { useEffect } from 'react';
import { Form, Input, Typography } from 'antd';
import { Modal } from '@drop/ui/AppModal';
import type { OrderListItem } from '@drop/types/order';
import { useOrdersDeps } from './OrdersDepsContext';

const { Text } = Typography;

type UpdateNotesModalProps = {
  order: OrderListItem | null;
  open: boolean;
  onClose: () => void;
};

type NotesFormValues = {
  buyerMemo?: string;
  sellerMemo?: string;
  internalMemo?: string;
};

const NOTE_FIELDS: {
  name: keyof NotesFormValues;
  label: string;
  placeholder: string;
}[] = [
  { name: 'buyerMemo', label: 'Buyer Notes', placeholder: 'Ghi chú cho buyer…' },
  { name: 'sellerMemo', label: 'Seller Notes', placeholder: 'Ghi chú seller…' },
  { name: 'internalMemo', label: 'Internal Notes', placeholder: 'Ghi chú nội bộ…' },
];

export const UpdateNotesModal = ({ order, open, onClose }: UpdateNotesModalProps) => {
  const deps = useOrdersDeps();
  const [form] = Form.useForm<NotesFormValues>();
  const updateNotes = deps.useUpdateOrderNotes();

  useEffect(() => {
    if (!open || !order) return;
    form.setFieldsValue({
      buyerMemo: order.buyerMemo ?? '',
      sellerMemo: order.sellerMemo ?? '',
      internalMemo: order.internalMemo ?? '',
    });
  }, [open, order, form]);

  const handleOk = async () => {
    if (!order) return;
    const values = await form.validateFields();
    const response = await updateNotes.mutateAsync({
      id: order.id,
      data: {
        buyerMemo: values.buyerMemo?.trim() || null,
        sellerMemo: values.sellerMemo?.trim() || null,
        internalMemo: values.internalMemo?.trim() || null,
      },
    });
    if (response.success) {
      onClose();
    }
  };

  const orderLabel = order?.platformOrderId || String(order?.fourSellerOrderId || '');

  return (
    <Modal
      title={
        <div style={{ lineHeight: 1.35 }}>
          <div>Ghi chú</div>
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
      confirmLoading={updateNotes.isPending}
      forceRender
      width={480}
      styles={{ body: { paddingTop: 12, paddingBottom: 8 } }}
    >
      <Form form={form} layout="vertical" requiredMark={false}>
        {NOTE_FIELDS.map((field, index) => {
          const isLast = index === NOTE_FIELDS.length - 1;
          return (
            <Form.Item
              key={field.name}
              label={field.label}
              name={field.name}
              rules={[{ max: 2000, message: 'Tối đa 2000 ký tự' }]}
              // showCount sits below the textarea; keep room so footer does not cover it
              style={{ marginBottom: isLast ? 28 : 24 }}
            >
              <Input.TextArea
                rows={3}
                maxLength={2000}
                showCount
                placeholder={field.placeholder}
              />
            </Form.Item>
          );
        })}
      </Form>
    </Modal>
  );
};
