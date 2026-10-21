import { useEffect } from 'react';
import { Form, InputNumber, Select, Typography } from 'antd';
import { Modal } from '@drop/ui/AppModal';
import type { OrderListItem } from '@drop/types/order';
import { useOrdersDeps } from './OrdersDepsContext';
import './orders.css';

const { Text } = Typography;

type UpdateSettlementModalProps = {
  order: OrderListItem | null;
  open: boolean;
  onClose: () => void;
};

type SettlementFormValues = {
  isSettlementPaid: boolean;
};

const SETTLEMENT_OPTIONS = [
  { value: false, label: 'Chưa thanh toán' },
  { value: true, label: 'Đã thanh toán' },
];

const resolveReceivedAmount = (order: OrderListItem | null): number => {
  if (!order) return 0;
  if (order.sellerNetAmount != null) return order.sellerNetAmount;
  const paid = order.totalPaidAmount ?? 0;
  return paid !== 0 ? paid : (order.amount ?? 0);
};

const formatAmount = (amount: number) =>
  Number(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export const UpdateSettlementModal = ({ order, open, onClose }: UpdateSettlementModalProps) => {
  const deps = useOrdersDeps();
  const [form] = Form.useForm<SettlementFormValues>();
  const updateSettlement = deps.useUpdateOrderSettlement();
  const receivedAmount = resolveReceivedAmount(order);

  useEffect(() => {
    if (!open || !order) return;
    form.setFieldsValue({
      isSettlementPaid: order.isSettlementPaid ?? false,
    });
  }, [open, order, form]);

  const handleOk = async () => {
    if (!order) return;
    const values = await form.validateFields();
    const response = await updateSettlement.mutateAsync({
      id: order.id,
      data: { isSettlementPaid: values.isSettlementPaid ?? false },
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
          <div>Thanh toán đơn hàng</div>
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
      confirmLoading={updateSettlement.isPending}
      forceRender
      width={440}
      styles={{ body: { paddingTop: 12 } }}
    >
      <Form form={form} layout="vertical" requiredMark={false}>
        <Form.Item
          name="isSettlementPaid"
          label="Trạng thái thanh toán"
          extra="Đã thanh toán ghi tiền về vào lịch sử thẻ nhận tiền của shop. Chưa thanh toán xóa giao dịch đã tạo."
          rules={[{ required: true, message: 'Chọn trạng thái' }]}
          style={{ marginBottom: 12 }}
        >
          <Select options={SETTLEMENT_OPTIONS} />
        </Form.Item>

        <Form.Item label="Số tiền nhận về (Rev)" style={{ marginBottom: 0 }}>
          <InputNumber
            style={{ width: '100%' }}
            value={receivedAmount}
            readOnly
            disabled
            controls={false}
            prefix="$"
            precision={2}
          />
        </Form.Item>
      </Form>

      {order?.shopName ? (
        <Text type="secondary" style={{ display: 'block', marginTop: 12, fontSize: 12 }}>
          Shop: {order.shopName}
          {order.isSettlementPaid
            ? ' · hiện đang Đã thanh toán'
            : ' · hiện đang Chưa thanh toán'}
        </Text>
      ) : (
        <Text type="secondary" style={{ display: 'block', marginTop: 12, fontSize: 12 }}>
          Đơn chưa gắn shop — cần gắn shop (và thẻ nhận tiền) trước khi đánh dấu Đã thanh toán.
        </Text>
      )}

      <div
        style={{
          display: 'flex',
          alignItems: 'baseline',
          justifyContent: 'space-between',
          gap: 12,
          marginTop: 16,
          padding: '10px 12px',
          background: 'var(--order-settlement-bg)',
          borderRadius: 6,
          border: '1px solid var(--order-settlement-border)',
        }}
      >
        <Text strong style={{ fontSize: 13 }}>
          Số ghi sổ cái
        </Text>
        <Text
          strong
          style={{
            fontSize: 18,
            color: 'var(--order-settlement-fg)',
            fontVariantNumeric: 'tabular-nums',
            whiteSpace: 'nowrap',
          }}
        >
          ${formatAmount(receivedAmount)}
        </Text>
      </div>
    </Modal>
  );
};
