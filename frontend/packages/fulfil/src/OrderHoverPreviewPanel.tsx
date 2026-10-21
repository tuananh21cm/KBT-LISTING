import { useState, type CSSProperties, type ReactNode } from 'react';
import { Avatar, message, Popover, Spin, Typography } from 'antd';
import { ShoppingOutlined } from '@ant-design/icons';
import { Modal, useIsMobile } from '@drop/ui';
import {
  SupplierPurchaseSection,
  cardPaymentAmount,
  sumChargedSupplierTotals,
  isSupplierCostDetailComplete,
  formatSupplierUsd,
  type SupplierPurchaseLink,
} from '@drop/ui/SupplierPurchaseCost';
import '@drop/ui/supplierPurchaseCost.css';
import type { OrderHoverPreview } from '@drop/types/order';
import { useFulfilDeps } from './FulfilDepsContext';

const { Text } = Typography;

const copyDisplayId = async (value: string) => {
  if (!value) return;
  await navigator.clipboard.writeText(value);
  message.success('Đã copy mã đơn');
};

const formatAmount = (amount: number) =>
  Number(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/**
 * Pass-through DB money — null/undefined = chưa có detail (hiện "—"),
 * 0 thật vẫn hiện "0.00 USD".
 */
const formatDbMoney = (amount: number | null | undefined, currency = 'USD') =>
  amount == null ? '—' : `${formatAmount(amount)} ${currency}`;

/** Giảm giá: prefix `-` (DB lưu dương) — khớp OrdersPage / SupplierPurchaseCost. */
const formatDbDiscountMoney = (amount: number | null | undefined, currency = 'USD') =>
  amount == null ? '—' : `-${formatAmount(Math.abs(amount))} ${currency}`;

const moneyUsd = formatSupplierUsd;

const formatShippingMethod = (value?: string | null) => {
  if (!value) return null;
  const lower = value.toLowerCase();
  if (lower.includes('seller shipping')) return 'Seller Shipping';
  if (lower.includes('tiktok shipping')) return 'Tiktok Shipping';
  return value;
};

const Field = ({
  label,
  children,
  strong,
  discount,
  struck,
}: {
  label: string;
  children: ReactNode;
  strong?: boolean;
  /** Số tiền giảm — màu #c44a01. */
  discount?: boolean;
  /** Giá gốc trước giảm — gạch ngang. */
  struck?: boolean;
}) => (
  <div className="fulfil-order-preview-field">
    <span className="fulfil-order-preview-field-label">{label}</span>
    <span
      className={[
        strong ? 'fulfil-order-preview-field-strong' : 'fulfil-order-preview-field-value',
        discount ? 'fulfil-order-preview-discount' : '',
        struck ? 'fulfil-order-preview-struck' : '',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      {children}
    </span>
  </div>
);

const PreviewBody = ({
  data,
  loading,
  error,
  focusSupplierOrderNo,
  fullWidth,
}: {
  data: OrderHoverPreview | null | undefined;
  loading: boolean;
  error: boolean;
  /** Chỉ hiện breakdown của billno này (dòng sổ cái của một lần quẹt thẻ). */
  focusSupplierOrderNo?: string | null;
  /** Đặt trong modal: bỏ khung cố định của popover, để modal lo bề ngang và cuộn. */
  fullWidth?: boolean;
}) => {
  if (loading && !data) {
    return (
      <div className="fulfil-order-preview-loading">
        <Spin size="small" />
      </div>
    );
  }

  if (error || !data) {
    return (
      <Text type="secondary" style={{ fontSize: 12 }}>
        Không tải được đơn hàng
      </Text>
    );
  }

  const hidden = Math.max(0, data.itemCount - data.items.length);
  const currency = (data.currency || 'USD').trim().toUpperCase() || 'USD';
  const shipLabel =
    formatShippingMethod(data.shippingMethod) || formatShippingMethod(data.shippingService);
  const revenueTitle = shipLabel ? `Doanh thu (${shipLabel})` : 'Doanh thu';
  // Số NCC gộp: các số bên dưới là TỔNG của nhiều billno, không phải một đơn SHEIN.
  const supplierLinks = data.supplierLinks ?? [];
  const supplierCount = data.supplierOrderCount ?? supplierLinks.length;
  // Mở từ một dòng tiền → chỉ số của billno đó, khớp 1-1 trang đơn SHEIN.
  const focusKey = focusSupplierOrderNo?.trim().toLowerCase();
  const focusLink = focusKey
    ? supplierLinks.find((l) => l.supplierOrderNo.trim().toLowerCase() === focusKey)
    : undefined;
  const isMultiSupplier = !focusLink && supplierCount > 1 && supplierLinks.length > 1;
  const singleLink = !focusLink && !isMultiSupplier && supplierLinks.length === 1
    ? supplierLinks[0]
    : undefined;
  const costValues = focusLink ?? singleLink ?? data;
  const costComplete = focusLink
    ? isSupplierCostDetailComplete(focusLink)
    : supplierLinks.length > 0
      ? supplierLinks.every((l) => isSupplierCostDetailComplete(l))
      : data.supplierSubtotal != null && data.costAmount != null;
  // Header Fulfil = tiền thẻ (supplierTotal), khớp cột Thẻ / totalPrice hóa đơn — không cộng ví/club.
  const chargedLinks = supplierLinks.filter((l) => l.isCharged !== false);
  const linksForCardSum = chargedLinks.length > 0 ? chargedLinks : supplierLinks;
  const headerTotal = focusLink
    ? cardPaymentAmount(focusLink)
    : singleLink
      ? cardPaymentAmount(singleLink)
      : sumChargedSupplierTotals(linksForCardSum);
  const hasTikTokRevenue =
    !!data.pricingDetailSyncedAt &&
    ((Number(data.sellAmount) || 0) !== 0 ||
      (data.productAmountBeforeDiscount != null && data.productAmountBeforeDiscount > 0));
  const purchaseLinks: SupplierPurchaseLink[] | undefined = isMultiSupplier
    ? supplierLinks.map((link) => ({
        ...link,
        fallbackCarrier: data.shippingService,
      }))
    : undefined;

  return (
    <div className={`fulfil-order-preview${fullWidth ? ' fulfil-order-preview-full' : ''}`}>
      <div className="fulfil-order-preview-items">
        {data.items.length === 0 ? (
          <Text type="secondary" style={{ fontSize: 12 }}>
            Không có sản phẩm
          </Text>
        ) : (
          data.items.map((item, idx) => {
            const img = item.imgUrl || undefined;
            const meta = [item.variantAttr, item.sellerSku].filter(Boolean).join(' · ');
            return (
              <div key={`${item.sellerSku ?? 'i'}-${idx}`} className="fulfil-order-preview-item">
                <Avatar
                  shape="square"
                  size={36}
                  src={img}
                  icon={<ShoppingOutlined />}
                  className="fulfil-order-preview-thumb"
                />
                <div className="fulfil-order-preview-item-meta">
                  <Text ellipsis={{ tooltip: item.title || undefined }} className="fulfil-order-preview-title">
                    {item.title || '—'}
                  </Text>
                  <div className="fulfil-order-preview-sub-row">
                    {meta ? (
                      <Text
                        type="secondary"
                        ellipsis={{ tooltip: meta }}
                        className="fulfil-order-preview-sub fulfil-order-preview-sub-meta"
                      >
                        {meta}
                      </Text>
                    ) : null}
                    <Text type="secondary" className="fulfil-order-preview-sub fulfil-order-preview-sub-qty">
                      ×{item.quantity} · {moneyUsd(item.itemPrice)}
                    </Text>
                  </div>
                </div>
              </div>
            );
          })
        )}
        {hidden > 0 ? (
          <Text type="secondary" style={{ fontSize: 11 }}>
            +{hidden} sản phẩm khác
          </Text>
        ) : null}
      </div>

      <div className="fulfil-order-preview-sections">
        <SupplierPurchaseSection
          className="fulfil-order-preview-section"
          headerTotal={headerTotal}
          values={costValues}
          currency={currency}
          incomplete={!costComplete}
          multiLinks={purchaseLinks}
          multiCount={supplierCount}
          showTracking={false}
          headerAmountMode="card"
        />

        {hasTikTokRevenue ? (
          <section className="fulfil-order-preview-section">
            <div className="fulfil-order-preview-section-head">
              <Text strong className="fulfil-order-preview-section-title">
                {revenueTitle}
              </Text>
              <Text strong className="fulfil-order-preview-sell">
                {moneyUsd(data.sellAmount)}
              </Text>
            </div>
            <div className="fulfil-order-preview-grid">
              {data.productAmountBeforeDiscount != null && data.productAmountBeforeDiscount > 0 ? (
                <Field label="Giá trước giảm">
                  {formatDbMoney(data.productAmountBeforeDiscount, currency)}
                </Field>
              ) : null}
              {data.productPlatformDiscount != null && data.productPlatformDiscount > 0 ? (
                <Field label="Nền tảng giảm" discount>
                  {formatDbDiscountMoney(data.productPlatformDiscount, currency)}
                </Field>
              ) : null}
              {data.productSellerDiscount != null && data.productSellerDiscount > 0 ? (
                <Field label="Seller giảm" discount>
                  {formatDbDiscountMoney(data.productSellerDiscount, currency)}
                </Field>
              ) : null}
              {data.shippingFeeBeforeDiscount != null && data.shippingFeeBeforeDiscount > 0 ? (
                <Field label="Ship trước giảm">
                  {formatDbMoney(data.shippingFeeBeforeDiscount, currency)}
                </Field>
              ) : null}
              {data.shippingFeeSellerDiscount != null && data.shippingFeeSellerDiscount > 0 ? (
                <Field label="Ship seller giảm" discount>
                  {formatDbDiscountMoney(data.shippingFeeSellerDiscount, currency)}
                </Field>
              ) : null}
              {data.platformTotalTax != null && data.platformTotalTax > 0 ? (
                <Field label="Thuế">{formatDbMoney(data.platformTotalTax, currency)}</Field>
              ) : null}
              {data.platformOtherFee != null && data.platformOtherFee > 0 ? (
                <Field label="Phí khác">{formatDbMoney(data.platformOtherFee, currency)}</Field>
              ) : null}
              {data.totalPaidAmount != null && data.totalPaidAmount > 0 ? (
                <Field label="Khách trả" strong>
                  {formatDbMoney(data.totalPaidAmount, currency)}
                </Field>
              ) : null}
            </div>
          </section>
        ) : data.pricingDetailSyncedAt && (Number(data.sellAmount) || 0) === 0 ? (
          <Text type="secondary" style={{ fontSize: 12 }}>
            4Seller không trả số tiền TikTok cho đơn này
          </Text>
        ) : null}
      </div>
    </div>
  );
};

/**
 * Bản modal của preview — dành cho mobile, nơi không hover được nên phải mở bằng nút hành động.
 * Chỉ fetch khi `open` để không tốn request cho mọi dòng trong bảng.
 */
export const OrderPreviewModal = ({
  orderId,
  focusSupplierOrderNo,
  open,
  title,
  onClose,
}: {
  orderId?: string | null;
  focusSupplierOrderNo?: string | null;
  open: boolean;
  title?: ReactNode;
  onClose: () => void;
}) => {
  const { useOrderPreview } = useFulfilDeps();
  const { data, isFetching, isError } = useOrderPreview(orderId ?? null, open);

  return (
    <Modal
      open={open}
      onCancel={onClose}
      footer={null}
      maskClosable
      width="100%"
      title={title ?? 'Chi tiết đơn hàng'}
      styles={{ body: { paddingTop: 4, maxHeight: '72vh', overflowY: 'auto' } }}
    >
      <PreviewBody
        data={data}
        loading={isFetching}
        error={isError}
        focusSupplierOrderNo={focusSupplierOrderNo}
        fullWidth
      />
    </Modal>
  );
};

export const OrderIdWithHoverPreview = ({
  displayId,
  orderId,
  maxWidth,
  style,
  children,
  focusSupplierOrderNo,
}: {
  displayId: string;
  orderId?: string | null;
  maxWidth?: number;
  style?: CSSProperties;
  /** Custom trigger; default = mã đơn (desktop: hover preview + click copy; mobile: click mở modal). */
  children?: ReactNode;
  /**
   * Billno mà dòng tiền này trả cho — preview chỉ hiện breakdown của nó thay vì tổng gộp.
   * Chỉ truyền khi giao dịch thật sự gắn với một billno cụ thể.
   */
  focusSupplierOrderNo?: string | null;
}) => {
  const isMobile = useIsMobile();
  const { useOrderPreview } = useFulfilDeps();
  const [hoverOpen, setHoverOpen] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  // Desktop: fetch khi hover; mobile dùng OrderPreviewModal (tự fetch khi open).
  const { data, isFetching, isError } = useOrderPreview(
    orderId ?? null,
    !isMobile && hoverOpen,
  );

  const openMobilePreview = (e: { stopPropagation: () => void }) => {
    e.stopPropagation();
    if (!orderId) return;
    setModalOpen(true);
  };

  // Không gắn Tooltip ellipsis khi đã có Popover — hai lớp hover chồng nhau làm panel nhảy/flicker.
  const defaultTrigger = (
    <Text
      className="fulfil-link"
      ellipsis
      title={isMobile || orderId ? undefined : `Click để copy: ${displayId}`}
      style={{
        maxWidth,
        fontVariantNumeric: 'tabular-nums',
        cursor: 'pointer',
        ...style,
      }}
      onClick={(e) => {
        if (isMobile && orderId) {
          openMobilePreview(e);
          return;
        }
        void copyDisplayId(displayId);
      }}
    >
      {displayId}
    </Text>
  );

  const trigger = children ?? defaultTrigger;

  if (!orderId) {
    return <>{trigger}</>;
  }

  // Mobile: không hover — tap mã đơn mở cùng panel với nút "Xem đơn SHEIN".
  if (isMobile) {
    return (
      <>
        <span
          className="fulfil-order-preview-trigger"
          onClick={children ? openMobilePreview : undefined}
        >
          {trigger}
        </span>
        <OrderPreviewModal
          open={modalOpen}
          orderId={orderId}
          focusSupplierOrderNo={focusSupplierOrderNo}
          title={focusSupplierOrderNo || displayId}
          onClose={() => setModalOpen(false)}
        />
      </>
    );
  }

  return (
    <Popover
      open={hoverOpen}
      onOpenChange={setHoverOpen}
      trigger="hover"
      placement="rightTop"
      // Không flip/shift theo Y khi nội dung load xong đổi chiều cao — tránh panel nhảy lên xuống.
      autoAdjustOverflow={{ adjustX: 1, adjustY: 0 }}
      mouseEnterDelay={0.35}
      mouseLeaveDelay={0.25}
      arrow={false}
      destroyOnHidden
      getPopupContainer={() => document.body}
      styles={{ container: { padding: 0 } }}
      content={
        <PreviewBody
          data={data}
          loading={isFetching}
          error={isError}
          focusSupplierOrderNo={focusSupplierOrderNo}
        />
      }
    >
      <span className="fulfil-order-preview-trigger">{trigger}</span>
    </Popover>
  );
};
