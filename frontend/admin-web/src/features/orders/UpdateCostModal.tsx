import { useEffect, useMemo, useState } from 'react';
import { DatePicker, Form, Input, InputNumber, Select, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { Modal } from '../../components/AppModal';
import { useUpdateOrderCost } from '../../hooks/useAdminOrders';
import { useFulfilAccountOptions, useFulfilCardOptions } from '../../hooks/useAdminFulfil';
import { useFulfilUserOptions } from '../../hooks/useAdminUsers';
import { useAuthStore } from '../../stores/authStore';
import type { OrderListItem } from '../../types/order';
import { formatFulfilCardOptionLabel } from '../../types/fulfil';
import { APP_TZ, DATETIME_FORMAT } from '../../utils/datetime';
import '@drop/orders/orders.css';

const { Text } = Typography;

type UpdateCostModalProps = {
  order: OrderListItem | null;
  open: boolean;
  onClose: () => void;
};

type CostFormValues = {
  supplierOrderNo?: string | null;
  supplierRetailPrice?: number | null;
  supplierSubtotal?: number | null;
  supplierShippingFee?: number | null;
  supplierOnTimeGuarantee?: number | null;
  supplierCouponPrice?: number | null;
  supplierMembershipDiscount?: number | null;
  supplierPointsDiscount?: number | null;
  supplierTax?: number | null;
  supplierTotal?: number | null;
  fulfilAccountId?: string;
  fulfilCardId?: string;
  paidByUserId?: string;
  fulfilPurchasedAt?: Dayjs | null;
};

type SupplierFeeName = keyof Pick<
  CostFormValues,
  | 'supplierRetailPrice'
  | 'supplierSubtotal'
  | 'supplierShippingFee'
  | 'supplierOnTimeGuarantee'
  | 'supplierCouponPrice'
  | 'supplierMembershipDiscount'
  | 'supplierPointsDiscount'
  | 'supplierTax'
  | 'supplierTotal'
>;

const SUPPLIER_FEE_FIELDS: { name: SupplierFeeName; label: string; placeholder: string }[] = [
  { name: 'supplierSubtotal', label: 'Subtotal', placeholder: '12.79' },
  { name: 'supplierShippingFee', label: 'Ship', placeholder: '3.99' },
  { name: 'supplierCouponPrice', label: 'Coupon', placeholder: '1.92' },
  { name: 'supplierMembershipDiscount', label: 'Club', placeholder: '0.66' },
  { name: 'supplierPointsDiscount', label: 'Points', placeholder: '0.66' },
  { name: 'supplierTax', label: 'Tax', placeholder: '1.19' },
  { name: 'supplierTotal', label: 'Thẻ (totalPrice)', placeholder: '16.05' },
  { name: 'supplierRetailPrice', label: 'Retail (tuỳ chọn)', placeholder: '21.89' },
  { name: 'supplierOnTimeGuarantee', label: 'On-Time (tuỳ chọn)', placeholder: '0.00' },
];

const formatAmount = (amount: number) =>
  Number(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export const UpdateCostModal = ({ order, open, onClose }: UpdateCostModalProps) => {
  const [form] = Form.useForm<CostFormValues>();
  const updateCost = useUpdateOrderCost();
  const currentUser = useAuthStore((s) => s.user);

  const [accountId, setAccountId] = useState<string | undefined>();
  // Local preview synced via onValuesChange (keeps footer in sync without Form.useWatch).
  const [supplierTotalPreview, setSupplierTotalPreview] = useState<number | null>(null);
  const { data: accountOptions = [] } = useFulfilAccountOptions(open);
  const { data: cardOptions = [] } = useFulfilCardOptions(accountId, open && !!accountId);
  // Người mua hàng chỉ có thể là Admin / Fulfil — cùng danh sách với bộ lọc Lịch sử mua hàng.
  const { data: users = [] } = useFulfilUserOptions(open);

  const userOptions = useMemo(() => {
    const options = users.map((u) => ({ value: u.id, label: u.fullName?.trim() || u.userName }));
    // Đơn cũ có thể gắn người ngoài Admin/Fulfil (hoặc user đã khóa) — giữ option để Select
    // không phơi GUID và không âm thầm đổi người mua khi lưu lại.
    const current = order?.paidByUserId;
    if (!current || options.some((o) => o.value === current)) return options;
    return [...options, { value: current, label: order?.paidByUserName?.trim() || 'Người dùng cũ' }];
  }, [users, order?.paidByUserId, order?.paidByUserName]);

  useEffect(() => {
    if (!open || !order) {
      setAccountId(undefined);
      setSupplierTotalPreview(null);
      return;
    }
    const nextAccountId = order.fulfilAccountId ?? undefined;
    setAccountId(nextAccountId);
    setSupplierTotalPreview(order.supplierTotal ?? null);
    form.setFieldsValue({
      supplierOrderNo: order.supplierOrderNo ?? null,
      supplierRetailPrice: order.supplierRetailPrice ?? null,
      supplierSubtotal: order.supplierSubtotal ?? null,
      supplierShippingFee: order.supplierShippingFee ?? null,
      supplierOnTimeGuarantee: order.supplierOnTimeGuarantee ?? null,
      supplierCouponPrice: order.supplierCouponPrice ?? null,
      supplierMembershipDiscount: order.supplierMembershipDiscount ?? null,
      supplierPointsDiscount: order.supplierPointsDiscount ?? null,
      supplierTax: order.supplierTax ?? null,
      supplierTotal: order.supplierTotal ?? null,
      fulfilAccountId: nextAccountId,
      fulfilCardId: order.fulfilCardId ?? undefined,
      paidByUserId: order.paidByUserId ?? currentUser?.id,
      fulfilPurchasedAt: order.fulfilPurchasedAt ? dayjs(order.fulfilPurchasedAt).tz(APP_TZ) : dayjs().tz(APP_TZ),
    });
  }, [open, order, form, currentUser?.id]);

  useEffect(() => {
    if (!open || !order || order.fulfilCardId || !order.fulfilAccountId || accountOptions.length === 0) {
      return;
    }
    const currentCard = form.getFieldValue('fulfilCardId') as string | undefined;
    if (currentCard) return;
    const activeCardId = accountOptions.find((a) => a.id === order.fulfilAccountId)?.activeCardId;
    if (activeCardId) {
      form.setFieldValue('fulfilCardId', activeCardId);
    }
  }, [open, order, accountOptions, form]);

  const handleOk = async () => {
    if (!order) return;
    const values = await form.validateFields();
    const response = await updateCost.mutateAsync({
      id: order.id,
      data: {
        supplierOrderNo: values.supplierOrderNo?.trim() || null,
        supplierRetailPrice: values.supplierRetailPrice ?? null,
        supplierSubtotal: values.supplierSubtotal ?? null,
        supplierShippingFee: values.supplierShippingFee ?? null,
        supplierOnTimeGuarantee: values.supplierOnTimeGuarantee ?? null,
        supplierCouponPrice: values.supplierCouponPrice ?? null,
        supplierMembershipDiscount: values.supplierMembershipDiscount ?? null,
        supplierPointsDiscount: values.supplierPointsDiscount ?? null,
        supplierTax: values.supplierTax ?? null,
        supplierTotal: values.supplierTotal ?? null,
        fulfilAccountId: values.fulfilAccountId || null,
        fulfilCardId: values.fulfilCardId || null,
        paidByUserId: values.paidByUserId || null,
        fulfilPurchasedAt: values.fulfilPurchasedAt?.toISOString() ?? null,
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
          <div>Cập nhật phí NCC</div>
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
      confirmLoading={updateCost.isPending}
      forceRender
      width={520}
      styles={{ body: { paddingTop: 12 } }}
    >
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        onValuesChange={(changed) => {
          if ('supplierTotal' in changed) {
            setSupplierTotalPreview(changed.supplierTotal ?? null);
          }
          if ('fulfilAccountId' in changed) {
            setAccountId(changed.fulfilAccountId);
            form.setFieldValue('fulfilCardId', undefined);
            const selected = accountOptions.find((a) => a.id === changed.fulfilAccountId);
            if (selected?.activeCardId) {
              form.setFieldValue('fulfilCardId', selected.activeCardId);
            }
          }
        }}
      >
        <Form.Item name="supplierOrderNo" label="Mã đơn NCC" style={{ marginBottom: 12 }}>
          <Input placeholder="Mã đơn trên SHEIN / Temu / …" allowClear />
        </Form.Item>

        <div
          style={{
            display: 'grid',
            gridTemplateColumns: '1fr 1fr',
            gap: '4px 16px',
          }}
        >
          {SUPPLIER_FEE_FIELDS.map((field) => (
            <Form.Item
              key={field.name}
              label={field.label}
              name={field.name}
              rules={[{ type: 'number', min: 0, message: '≥ 0' }]}
              style={{ marginBottom: 12 }}
            >
              <InputNumber
                style={{ width: '100%' }}
                min={0}
                step={0.01}
                precision={2}
                placeholder={field.placeholder}
                prefix="$"
                controls={false}
              />
            </Form.Item>
          ))}
        </div>

        <Form.Item name="fulfilAccountId" label="Tài khoản mua hàng" style={{ marginBottom: 12 }}>
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Chọn tài khoản fulfil"
            options={accountOptions.map((a) => ({ value: a.id, label: a.name }))}
          />
        </Form.Item>

        <Form.Item
          name="fulfilCardId"
          label="Thẻ thanh toán"
          style={{ marginBottom: 12 }}
          dependencies={['fulfilAccountId']}
          extra="Mặc định = thẻ đang dùng của tài khoản; có thể đổi hoặc bỏ trống"
        >
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={accountId ? 'Thẻ đang dùng / không gắn thẻ' : 'Chọn tài khoản trước'}
            disabled={!accountId}
            options={cardOptions.map((c) => ({
              value: c.id,
              label: formatFulfilCardOptionLabel(c),
            }))}
          />
        </Form.Item>

        <Form.Item
          name="paidByUserId"
          label="Người mua hàng"
          extra="Fulfil đã bấm Đã mua — dùng cho thống kê tốc độ fulfil"
          style={{ marginBottom: 12 }}
        >
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Chọn người mua hàng"
            options={userOptions}
          />
        </Form.Item>

        <Form.Item
          name="fulfilPurchasedAt"
          label="Ngày fulfil mua hàng"
          extra="Mặc định lúc lưu lần đầu; có thể chỉnh lại cho đúng thực tế"
          style={{ marginBottom: 12 }}
        >
          <DatePicker
            showTime
            style={{ width: '100%' }}
            format={DATETIME_FORMAT}
            placeholder="Chọn ngày mua"
          />
        </Form.Item>
      </Form>

      <div
        style={{
          display: 'flex',
          alignItems: 'baseline',
          justifyContent: 'space-between',
          gap: 12,
          marginTop: 4,
          padding: '10px 12px',
          background: 'var(--order-surface-muted)',
          borderRadius: 6,
          border: '1px solid var(--order-border)',
        }}
      >
        <div>
          <Text strong style={{ fontSize: 13 }}>
            Tổng thanh toán NCC
          </Text>
          <div style={{ fontSize: 11, color: 'var(--order-text-tertiary)', marginTop: 2 }}>
            Số tiền ghi sổ cái (SupplierTotal)
          </div>
        </div>
        <Text
          strong
          style={{
            fontSize: 18,
            color:
              supplierTotalPreview == null
                ? 'var(--order-text-quaternary)'
                : 'var(--order-cost)',
            fontVariantNumeric: 'tabular-nums',
            whiteSpace: 'nowrap',
          }}
        >
          {supplierTotalPreview == null ? '—' : `$${formatAmount(supplierTotalPreview)}`}
        </Text>
      </div>
    </Modal>
  );
};
