import { useEffect, useMemo, useState } from 'react';
import { Alert, Form, Input, Select, Typography } from 'antd';
import { Modal } from '@drop/ui/AppModal';
import type { OrderListItem } from '@drop/types/order';
import { useOrdersDeps } from './OrdersDepsContext';

type MarkShipModalProps = {
  order: OrderListItem | null;
  open: boolean;
  onClose: () => void;
  /** Prefill tracking from supplier carrier link (push-track / Mark Shipped). */
  initialTrackingNo?: string | null;
  /** SHEIN carrier name when it does not exact-match 4Seller shipping services. */
  suggestedCarrier?: string | null;
  /** Báo order id đang Confirm Mark Ship — để OrdersPage hiện spinner trên menu. */
  onPendingOrderIdChange?: (orderId: string | null) => void;
};

export const MarkShipModal = ({
  order,
  open,
  onClose,
  initialTrackingNo,
  suggestedCarrier,
  onPendingOrderIdChange,
}: MarkShipModalProps) => {
  const deps = useOrdersDeps();
  const [shippingService, setShippingService] = useState<string | undefined>();
  const [trackingNo, setTrackingNo] = useState('');

  const {
    data: services = [],
    isLoading: servicesLoading,
    isError: servicesError,
    error: servicesErrorObj,
  } = deps.useShippingServices(order?.ownerId, order?.platform, open && !!order);

  const markShip = deps.useMarkShipOrder();

  useEffect(() => {
    if (!open) {
      setShippingService(undefined);
      setTrackingNo('');
      return;
    }
    setShippingService(undefined);
    setTrackingNo((initialTrackingNo ?? '').trim());
  }, [open, order?.id, initialTrackingNo]);

  useEffect(() => {
    if (!onPendingOrderIdChange) return;
    if (open && markShip.isPending && order?.id) {
      onPendingOrderIdChange(order.id);
    } else {
      onPendingOrderIdChange(null);
    }
    return () => onPendingOrderIdChange(null);
  }, [open, markShip.isPending, order?.id, onPendingOrderIdChange]);

  const options = useMemo(
    () =>
      services.map((s) => ({
        value: s.carrier,
        label: s.carrier,
      })),
    [services],
  );

  const canSubmit = !!shippingService?.trim() && !!trackingNo.trim() && !!order;
  const carrierHint = suggestedCarrier?.trim();

  const handleOk = async () => {
    if (!order || !canSubmit) return;
    try {
      const response = await markShip.mutateAsync({
        id: order.id,
        data: {
          shippingService: shippingService!.trim(),
          trackingNo: trackingNo.trim(),
        },
      });
      if (response.success) {
        onClose();
      }
    } catch {
      // Mutation onError toasts + invalidates persisted autoMarkShipError; keep modal open.
    }
  };

  return (
    <Modal
      title="Mark as Shipped"
      open={open}
      onCancel={onClose}
      onOk={handleOk}
      okText="Confirm"
      cancelText="Cancel"
      confirmLoading={markShip.isPending}
      okButtonProps={{ disabled: !canSubmit }}
      destroyOnClose
    >
      <Form layout="vertical" style={{ marginTop: 8 }}>
        {carrierHint ? (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 12 }}
            title={
              <Typography.Text>
                SHEIN: <strong>{carrierHint}</strong> — không khớp Shipping Service 4Seller. Chọn
                service thủ công.
              </Typography.Text>
            }
          />
        ) : null}
        <Form.Item label="Shipping Service" required>
          <Select
            showSearch
            allowClear
            placeholder="Select shipping service"
            optionFilterProp="label"
            loading={servicesLoading}
            value={shippingService}
            onChange={setShippingService}
            options={options}
            status={servicesError ? 'error' : undefined}
            notFoundContent={
              servicesError
                ? (servicesErrorObj as Error)?.message || 'Không tải được shipping service'
                : undefined
            }
          />
        </Form.Item>
        <Form.Item label="Tracking No." required>
          <Input
            placeholder="Enter tracking number"
            value={trackingNo}
            onChange={(e) => setTrackingNo(e.target.value)}
            onPressEnter={() => {
              if (canSubmit && !markShip.isPending) void handleOk();
            }}
          />
        </Form.Item>
      </Form>
    </Modal>
  );
};
