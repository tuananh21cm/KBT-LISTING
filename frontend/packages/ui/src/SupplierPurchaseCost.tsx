import { type ReactNode } from 'react';
import { Tag, Typography, message } from 'antd';
import './supplierPurchaseCost.css';

const { Text } = Typography;

const formatAmount = (amount: number) =>
  Number(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** Pass-through DB money — null = "—"; 0 vẫn hiện. */
export const formatSupplierMoney = (amount: number | null | undefined, currency = 'USD') =>
  amount == null ? '—' : `${formatAmount(amount)} ${currency}`;

/** Giảm giá: thiếu → "—"; có giá trị → prefix `-`. */
export const formatSupplierDiscountMoney = (
  amount: number | null | undefined,
  currency = 'USD',
) => (amount == null ? '—' : `-${formatAmount(Math.abs(amount))} ${currency}`);

export const formatSupplierUsd = (amount: number | null | undefined) =>
  amount == null ? '—' : `$${formatAmount(amount)}`;

/**
 * TrueSpend = thẻ (supplierTotal) + ví (supplierWalletPrice) + club (supplierMembershipDiscount).
 * Club cộng vào vì giảm membership chỉ có nhờ đã mua SHEIN Club.
 * Null khi cả ba thiếu.
 */
export const settledTrueSpend = (values: {
  supplierTotal?: number | null;
  supplierWalletPrice?: number | null;
  supplierMembershipDiscount?: number | null;
}): number | null => {
  const card = values.supplierTotal;
  const wallet = values.supplierWalletPrice;
  const club = values.supplierMembershipDiscount;
  if (card == null && wallet == null && club == null) return null;
  return (card ?? 0) + (wallet ?? 0) + (club ?? 0);
};

/** Số trừ trên thẻ (totalPrice hóa đơn) — khớp cột Thẻ sổ cái / sao kê. */
export const cardPaymentAmount = (values: {
  supplierTotal?: number | null;
}): number | null => values.supplierTotal ?? null;

export const sumChargedSupplierTotals = (
  links: Array<{ supplierTotal?: number | null; isCharged?: boolean }>,
): number | null => {
  let total: number | null = null;
  for (const link of links) {
    if (link.isCharged === false) continue;
    if (link.supplierTotal == null) continue;
    total = (total ?? 0) + link.supplierTotal;
  }
  return total;
};

export const isSupplierCostDetailComplete = (values: {
  supplierSubtotal?: number | null;
  supplierTotal?: number | null;
  costDetailComplete?: boolean;
}) =>
  values.costDetailComplete === true ||
  (values.supplierSubtotal != null && values.supplierTotal != null);

/** Các cột Supplier* dùng chung giữa tổng đơn và từng billno. */
export type SupplierCostValues = {
  supplierRetailPrice?: number | null;
  supplierSubtotal?: number | null;
  supplierShippingFee?: number | null;
  supplierRetailDeliveryFee?: number | null;
  supplierOnTimeGuarantee?: number | null;
  supplierCouponPrice?: number | null;
  supplierMembershipDiscount?: number | null;
  supplierPointsDiscount?: number | null;
  supplierWalletPrice?: number | null;
  supplierTax?: number | null;
  supplierTotal?: number | null;
  costDetailComplete?: boolean;
};

export type SupplierTrackingValues = {
  trackingNo?: string | null;
  carrier?: string | null;
  trackingKind?: string | null;
  /** Order.ShippingService khi link chưa có tên carrier. */
  fallbackCarrier?: string | null;
};

export type SupplierPurchaseLink = SupplierCostValues &
  SupplierTrackingValues & {
    supplierOrderNo: string;
    supplierOrderUrl?: string | null;
    supplierStatus?: string | null;
    supplierUnpaid?: boolean;
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
  discount?: boolean;
  struck?: boolean;
}) => (
  <div className="supplier-purchase-field">
    <span className="supplier-purchase-field-label">{label}</span>
    <span
      className={[
        strong ? 'supplier-purchase-field-strong' : 'supplier-purchase-field-value',
        discount ? 'supplier-purchase-discount' : '',
        struck ? 'supplier-purchase-struck' : '',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      {children}
    </span>
  </div>
);

const copyTrackingNo = async (trackingNo: string) => {
  await navigator.clipboard.writeText(trackingNo);
  message.success('Đã copy mã vận đơn');
};

/** Carrier/Kho tag + mã track — cùng ngữ nghĩa tag ở bảng Lịch sử mua hàng. */
export const SupplierTrackingRow = ({
  trackingNo,
  carrier,
  trackingKind,
  fallbackCarrier,
}: SupplierTrackingValues) => {
  const no = trackingNo?.trim();
  const kind = (trackingKind || '').toLowerCase();
  let name = carrier?.trim() || '';
  if (!name) {
    const fb = fallbackCarrier?.trim() || '';
    const lower = fb.toLowerCase();
    if (
      fb &&
      !lower.includes('seller shipping') &&
      !lower.includes('tiktok shipping')
    ) {
      name = fb;
    }
  }
  if (!no && !name) return null;

  const isCarrier = kind === 'carrier' || Boolean(name);
  const isWarehouse = kind === 'warehouse';

  let tagLabel: string | null = null;
  let tagColor: string | undefined;
  let tagTitle: string | undefined;
  if (isCarrier && (name || no)) {
    tagLabel = name || 'Track';
    tagColor = 'blue';
    tagTitle = no ? `Copy mã: ${no}` : name;
  } else if (isWarehouse && no) {
    tagLabel = 'Kho';
    tagTitle = `Kho SHEIN · ${no}`;
  } else if (no) {
    tagLabel = 'Track';
    tagTitle = `Copy mã: ${no}`;
  }

  return (
    <div className="supplier-purchase-track">
      {tagLabel ? (
        <Tag
          color={tagColor}
          className="supplier-purchase-track-carrier"
          title={tagTitle}
          style={no ? { cursor: 'pointer' } : undefined}
          onClick={
            no
              ? (e) => {
                  e.stopPropagation();
                  void copyTrackingNo(no);
                }
              : undefined
          }
        >
          {tagLabel}
        </Tag>
      ) : null}
      {no ? (
        <Text
          className="supplier-purchase-track-no"
          ellipsis={{ tooltip: `Copy: ${no}` }}
          onClick={(e) => {
            e.stopPropagation();
            void copyTrackingNo(no);
          }}
        >
          {no}
        </Text>
      ) : null}
    </div>
  );
};

/**
 * Breakdown NCC — chỉ hiện dòng > 0 (đơn giản, đồng bộ Orders/Fulfil).
 * Ví = thanh toán (số dương), không style giảm giá.
 * Chi thật/Cost nằm ở header section — không lặp Thẻ / Cost trong grid.
 */
export const SupplierCostBreakdown = ({
  values,
  currency = 'USD',
}: {
  values: SupplierCostValues;
  currency?: string;
}) => {
  const hasPositive = (v: number | null | undefined) => v != null && v > 0;
  const rows: Array<{
    key: string;
    label: string;
    content: ReactNode;
    strong?: boolean;
    discount?: boolean;
    struck?: boolean;
  }> = [];

  if (hasPositive(values.supplierRetailPrice)) {
    rows.push({
      key: 'retail',
      label: 'Retail',
      content: formatSupplierMoney(values.supplierRetailPrice, currency),
      struck: true,
    });
  }
  if (hasPositive(values.supplierSubtotal)) {
    rows.push({
      key: 'subtotal',
      label: 'Subtotal',
      content: formatSupplierMoney(values.supplierSubtotal, currency),
    });
  }
  if (hasPositive(values.supplierShippingFee)) {
    rows.push({
      key: 'ship',
      label: 'Ship',
      content: formatSupplierMoney(values.supplierShippingFee, currency),
    });
  }
  if (hasPositive(values.supplierRetailDeliveryFee)) {
    rows.push({
      key: 'retailDelivery',
      label: 'RDF',
      content: formatSupplierMoney(values.supplierRetailDeliveryFee, currency),
    });
  }
  if (hasPositive(values.supplierOnTimeGuarantee)) {
    rows.push({
      key: 'ontime',
      label: 'On-Time',
      content: formatSupplierMoney(values.supplierOnTimeGuarantee, currency),
    });
  }
  if (hasPositive(values.supplierCouponPrice)) {
    rows.push({
      key: 'coupon',
      label: 'Coupon',
      content: formatSupplierDiscountMoney(values.supplierCouponPrice, currency),
      discount: true,
    });
  }
  if (hasPositive(values.supplierMembershipDiscount)) {
    rows.push({
      key: 'membership',
      label: 'Club',
      content: formatSupplierDiscountMoney(values.supplierMembershipDiscount, currency),
      discount: true,
    });
  }
  if (hasPositive(values.supplierPointsDiscount)) {
    rows.push({
      key: 'points',
      label: 'Points',
      content: formatSupplierDiscountMoney(values.supplierPointsDiscount, currency),
      discount: true,
    });
  }
  if (hasPositive(values.supplierWalletPrice)) {
    rows.push({
      key: 'wallet',
      label: 'Wallet',
      content: formatSupplierMoney(values.supplierWalletPrice, currency),
    });
  }
  if (hasPositive(values.supplierTax)) {
    rows.push({
      key: 'tax',
      label: 'Tax',
      content: formatSupplierMoney(values.supplierTax, currency),
    });
  }

  if (rows.length === 0) {
    return (
      <Text type="secondary" style={{ fontSize: 11 }}>
        Chưa có breakdown giá
      </Text>
    );
  }

  return (
    <div className="supplier-purchase-grid">
      {rows.map((row) => (
        <Field
          key={row.key}
          label={row.label}
          strong={row.strong}
          discount={row.discount}
          struck={row.struck}
        >
          {row.content}
        </Field>
      ))}
    </div>
  );
};

/**
 * Khối “Mua hàng” đồng bộ Orders drawer ↔ Fulfil hover:
 * header TrueSpend (Orders) hoặc thẻ (Fulfil) + breakdown + tracking + optional multi-billno.
 */
export const SupplierPurchaseSection = ({
  headerTotal,
  values,
  currency = 'USD',
  tracking,
  multiLinks,
  multiCount,
  incomplete,
  title = 'Mua hàng',
  className,
  showTracking = true,
  headerAmountMode = 'trueSpend',
}: {
  /** TrueSpend hoặc tiền thẻ tùy headerAmountMode. */
  headerTotal?: number | null;
  values: SupplierCostValues;
  currency?: string;
  tracking?: SupplierTrackingValues | null;
  /** Từng billno khi Joint Shipment — header mỗi link theo headerAmountMode. */
  multiLinks?: SupplierPurchaseLink[];
  multiCount?: number;
  incomplete?: boolean;
  title?: string;
  className?: string;
  /** Fulfil hover preview ẩn track — bảng đã có tag carrier/kho. */
  showTracking?: boolean;
  /** Orders drawer = TrueSpend; Fulfil hover = tiền thẻ (supplierTotal). */
  headerAmountMode?: 'trueSpend' | 'card';
}) => {
  const links = multiLinks ?? [];
  const isMulti = links.length > 1;
  const count = multiCount ?? links.length;
  const linkHeaderAmount = (link: SupplierCostValues) =>
    headerAmountMode === 'card' ? cardPaymentAmount(link) : settledTrueSpend(link);

  return (
    <section className={['supplier-purchase-section', className].filter(Boolean).join(' ')}>
      <div className="supplier-purchase-section-head">
        <Text strong className="supplier-purchase-section-title">
          {title}
        </Text>
        <Text strong className="supplier-purchase-cost">
          {formatSupplierUsd(headerTotal)}
        </Text>
      </div>
      {isMulti || incomplete ? (
        <div className="supplier-purchase-section-meta">
          {isMulti ? (
            <Tag color="purple" className="supplier-purchase-multi-tag">
              ×{count} đơn NCC
            </Tag>
          ) : null}
          {incomplete ? (
            <Tag color="warning" className="supplier-purchase-multi-tag">
              Thiếu order detail
            </Tag>
          ) : null}
        </div>
      ) : null}
      <SupplierCostBreakdown values={values} currency={currency} />
      {showTracking && !isMulti && tracking ? <SupplierTrackingRow {...tracking} /> : null}
      {isMulti ? (
        <div className="supplier-purchase-links">
          <div className="supplier-purchase-multi-note">
            Tổng trên là cộng gộp {count} đơn NCC. Từng đơn dưới đây khớp 1-1 với trang đơn của NCC:
          </div>
          {links.map((link) => (
            <div key={link.supplierOrderNo} className="supplier-purchase-link">
              <div className="supplier-purchase-link-head">
                <span className="supplier-purchase-link-no">
                  {link.supplierOrderNo}
                  {!isSupplierCostDetailComplete(link) ? (
                    <Tag
                      color="warning"
                      style={{ marginLeft: 6, fontSize: 10, lineHeight: '16px' }}
                    >
                      Thiếu detail
                    </Tag>
                  ) : null}
                </span>
                <span className="supplier-purchase-cost">
                  {formatSupplierMoney(linkHeaderAmount(link), currency)}
                </span>
              </div>
              {showTracking ? (
                <SupplierTrackingRow
                  trackingNo={link.trackingNo}
                  carrier={link.carrier}
                  trackingKind={link.trackingKind}
                  fallbackCarrier={link.fallbackCarrier ?? tracking?.fallbackCarrier}
                />
              ) : null}
              <SupplierCostBreakdown
                values={link}
                currency={currency}
              />
            </div>
          ))}
        </div>
      ) : null}
    </section>
  );
};
