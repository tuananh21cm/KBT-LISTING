import { describe, expect, it } from 'vitest';
import {
  extractBreakdownFromDetail,
  defaultMissingBreakdownToZero,
  isRichSheinBreakdown,
  normalizeSheinOrder,
  stripSheinListCostBreakdown,
  stubSheinOrderPayload,
} from './sheinOrders';
import detailGsu18j2480004pr from './fixtures/GSU18J2480004PR.detail.json';
import detailGsu18v24000m10p from './fixtures/GSU18V24000M10P.detail.json';

/**
 * Fixture kiểu GSU18B24Y00N7PJ: ship FREE ($3.99 gạch) bị SHEIN gộp vào
 * CẢ `couponPrice` lẫn `prime_discount` — parser phải tách excess/2.
 */
const freeShipOrderInflatedApi = {
  billno: 'GSU18B24Y00N7PJ',
  currency_code: 'USD',
  retailTotallPrice: { amount: 17.29, usdAmount: 17.29 },
  newSubTotalPrice: { amount: 13.39, usdAmount: 13.39 },
  shippingPrice: { amount: 0, usdAmount: 0 },
  /** Trap thật: API couponPrice = coupon UI + ship FREE. */
  couponPrice: { amount: 7.81, usdAmount: 7.81 },
  totalDiscountNew: { amount: 7.81, usdAmount: 7.81 },
  /** Trap: Total Savings = club UI + ship FREE. */
  prime_discount_amount_vo_total: { amount: 4.66, usdAmount: 4.66 },
  total_tax: { amount: 0.53, usdAmount: 0.53 },
  totalPrice: { amount: 9.43, usdAmount: 9.43 },
};

const freeShipOrderWithLabels = {
  ...freeShipOrderInflatedApi,
  priceDetailList: [
    {
      name: 'Total Savings with SHEIN CLUB Exclusive Discount',
      amount: 4.66,
    },
    {
      name: 'Discount (Coupon Code:newonly30)',
      amount: 3.82,
    },
    { name: 'Subtotal', amount: 13.39 },
    { name: 'Retail Price', amount: 17.29 },
  ],
};

/**
 * Fixture kiểu GSU18F244001WYM: có hàng "SHEIN Points" riêng và API `couponPrice`
 * đã gộp luôn số điểm (4.10 + 0.66) — parser phải tách lại thành 3 khoản.
 */
const pointsOrderApi = {
  billno: 'GSU18F244001WYM',
  currency_code: 'USD',
  retailTotallPrice: { amount: 27.78, usdAmount: 27.78 },
  newSubTotalPrice: { amount: 23.34, usdAmount: 23.34 },
  shippingPrice: { amount: 2.62, usdAmount: 2.62 },
  couponPrice: { amount: 4.76, usdAmount: 4.76 },
  pointPrice: { amount: 0.66, usdAmount: 0.66 },
  prime_discount_amount_vo_total: { amount: 0.58, usdAmount: 0.58 },
  total_tax: { amount: 2.0, usdAmount: 2.0 },
  totalPrice: { amount: 22.62, usdAmount: 22.62 },
};

const pointsOrderWithLabels = {
  ...pointsOrderApi,
  priceDetailList: [
    { name: 'Retail Price', amount: 27.78 },
    { name: 'Subtotal', amount: 23.34 },
    { name: 'Discount (Coupon Code:usht0622zs02i9xt)', amount: 4.1 },
    { name: 'SHEIN Points (Points:66)', amount: 0.66 },
    { name: 'Total Savings with SHEIN CLUB Exclusive Discount', amount: 0.58 },
  ],
};

describe('SHEIN Sync Order price breakdown (SHEIN Points)', () => {
  it('extractBreakdownFromDetail: tách điểm khỏi couponPrice đã gộp', () => {
    const target = stubSheinOrderPayload('GSU18F244001WYM');
    extractBreakdownFromDetail({ order: pointsOrderApi }, target);

    expect(target.retailPrice).toBe(27.78);
    expect(target.subtotal).toBe(23.34);
    expect(target.shippingFee).toBe(2.62);
    expect(target.couponPrice).toBe(4.1);
    expect(target.pointsDiscount).toBe(0.66);
    expect(target.clubDiscount).toBe(0.58);
    expect(target.tax).toBe(2.0);
    expect(target.total).toBe(22.62);
  });

  it('extractBreakdownFromDetail: label SHEIN Points giữ nguyên 3 khoản', () => {
    const target = stubSheinOrderPayload('GSU18F244001WYM');
    extractBreakdownFromDetail({ order: pointsOrderWithLabels }, target);

    expect(target.couponPrice).toBe(4.1);
    expect(target.pointsDiscount).toBe(0.66);
    expect(target.clubDiscount).toBe(0.58);
    expect(target.total).toBe(22.62);
  });

  it('normalizeSheinOrder: identity coupon + club + points', () => {
    const p = normalizeSheinOrder(pointsOrderApi);

    expect(p.subtotal).toBe(23.34);
    expect(p.couponPrice).toBe(4.1);
    expect(p.pointsDiscount).toBe(0.66);
    expect(p.clubDiscount).toBe(0.58);
    expect(p.total).toBe(22.62);
  });
});

describe('SHEIN Sync Order price breakdown (FREE shipping)', () => {
  it('extractBreakdownFromDetail: tách ship FREE khi API coupon/club đều phình', () => {
    const target = stubSheinOrderPayload('GSU18B24Y00N7PJ');
    extractBreakdownFromDetail({ order: freeShipOrderInflatedApi }, target);

    expect(target.retailPrice).toBe(17.29);
    expect(target.subtotal).toBe(13.39);
    expect(target.shippingFee).toBe(0);
    expect(target.couponPrice).toBe(3.82);
    expect(target.clubDiscount).toBe(0.67);
    expect(target.tax).toBe(0.53);
    expect(target.total).toBe(9.43);
  });

  it('extractBreakdownFromDetail: label Coupon Code ghi đè + reconcile', () => {
    const target = stubSheinOrderPayload('GSU18B24Y00N7PJ');
    extractBreakdownFromDetail({ order: freeShipOrderWithLabels }, target);

    expect(target.subtotal).toBe(13.39);
    expect(target.couponPrice).toBe(3.82);
    expect(target.clubDiscount).toBe(0.67);
    expect(target.total).toBe(9.43);
  });

  it('normalizeSheinOrder: reconcile coupon+club từ identity', () => {
    const p = normalizeSheinOrder(freeShipOrderInflatedApi);

    expect(p.subtotal).toBe(13.39);
    expect(p.couponPrice).toBe(3.82);
    expect(p.clubDiscount).toBe(0.67);
    expect(p.tax).toBe(0.53);
    expect(p.total).toBe(9.43);
    expect(p.retailPrice).toBe(17.29);
  });

  it('extractBreakdownFromDetail: sparse chỉ có total — không zero-fill / không gán Giá nhập = total', () => {
    const target = stubSheinOrderPayload('GSU18B24Y00N7PJ');
    extractBreakdownFromDetail(
      {
        order: {
          billno: 'GSU18B24Y00N7PJ',
          totalPrice: { amount: 9.43, usdAmount: 9.43 },
          taxPrice: { amount: 0, usdAmount: 0 },
          couponPrice: { amount: 0, usdAmount: 0 },
          shippingPrice: { amount: 0, usdAmount: 0 },
        },
      },
      target,
    );

    expect(target.total).toBe(9.43);
    expect(target.subtotal).toBeNull();
    expect(target.retailPrice).toBeNull();
    expect(target.tax).toBeNull();
    expect(target.couponPrice).toBeNull();
    expect(target.clubDiscount).toBeNull();
    expect(target.pointsDiscount).toBeNull();
    expect(target.walletPrice).toBeNull();
    expect(target.retailDeliveryFee).toBeNull();
  });
});

/**
 * Fixtures thật GSU18V24000M10P / GSU18J2480004PR (get_order_info):
 * - totalPrice = $0 (chỉ phần thẻ) + usedWalletPrice trả hết
 * - thiếu wallet khiến reconcile cũ đổ số ví sang coupon/membership
 */
describe('SHEIN Sync Order wallet + retail delivery (real payloads)', () => {
  it('GSU18V24000M10P: wallet 12.08, retail delivery 0.31, coupon không chứa ví', () => {
    const target = stubSheinOrderPayload('GSU18V24000M10P');
    extractBreakdownFromDetail(detailGsu18v24000m10p, target);

    expect(target.subtotal).toBe(7.56);
    expect(target.shippingFee).toBe(3.99);
    expect(target.retailDeliveryFee).toBe(0.31);
    expect(target.walletPrice).toBe(12.08);
    expect(target.couponPrice).toBe(0.76);
    expect(target.clubDiscount).toBe(0);
    expect(target.tax).toBe(0.98);
    expect(target.total).toBe(0);
  });

  it('GSU18J2480004PR: wallet 17.55 không gộp vào coupon', () => {
    const target = stubSheinOrderPayload('GSU18J2480004PR');
    extractBreakdownFromDetail(detailGsu18j2480004pr, target);

    expect(target.subtotal).toBe(18.47);
    expect(target.walletPrice).toBe(17.55);
    expect(target.couponPrice).toBe(0);
    expect(target.clubDiscount).toBe(0.92);
    expect(target.retailDeliveryFee).toBe(0);
    expect(target.total).toBe(0);
  });
});

describe('SHEIN Sync Order rich breakdown (sub ≈ total)', () => {
  it('isRichSheinBreakdown: subtotal === total không detailCostProven — không rich', () => {
    const target = stubSheinOrderPayload('GSUFLATSUB');
    target.subtotal = 12.5;
    target.total = 12.5;
    target.shippingFee = 0;
    expect(isRichSheinBreakdown(target)).toBe(false);
  });

  it('isRichSheinBreakdown: subtotal === total + detailCostProven — rich', () => {
    const target = stubSheinOrderPayload('GSUFLATSUB');
    target.subtotal = 12.5;
    target.total = 12.5;
    target.shippingFee = 0;
    target.detailCostProven = true;
    expect(isRichSheinBreakdown(target)).toBe(true);
  });

  it('isRichSheinBreakdown: chỉ có total — không rich', () => {
    const target = stubSheinOrderPayload('GSUSPARSE');
    target.total = 12.5;
    expect(isRichSheinBreakdown(target)).toBe(false);
  });

  it('extractBreakdownFromDetail: sub≈total không coupon/tax → vẫn có subtotal để POST cost', () => {
    const target = stubSheinOrderPayload('GSUFLATSUB');
    extractBreakdownFromDetail(
      {
        order: {
          billno: 'GSUFLATSUB',
          newSubTotalPrice: { amount: 12.5, usdAmount: 12.5 },
          shippingPrice: { amount: 0, usdAmount: 0 },
          totalPrice: { amount: 12.5, usdAmount: 12.5 },
          priceDetailList: [{ name: 'Subtotal', amount: 12.5 }],
        },
      },
      target,
    );

    expect(target.subtotal).toBe(12.5);
    expect(target.total).toBe(12.5);
    expect(target.detailCostProven).toBe(true);
    expect(isRichSheinBreakdown(target)).toBe(true);
  });

  it('defaultMissingBreakdownToZero: sub+total không discountInspected — không chốt coupon/wallet = 0', () => {
    const target = stubSheinOrderPayload('GSUFLATNODISC');
    target.subtotal = 12.5;
    target.total = 12.5;
    target.detailCostProven = true;
    target.detailDiscountInspected = false;
    defaultMissingBreakdownToZero(target);
    expect(target.couponPrice).toBeNull();
    expect(target.walletPrice).toBeNull();
  });
});

describe('stripSheinListCostBreakdown', () => {
  it('xóa coupon/subtotal/wallet, giữ total + shippingFee', () => {
    const order = stubSheinOrderPayload('GSULIST1');
    order.total = 20;
    order.shippingFee = 3;
    order.subtotal = 25;
    order.couponPrice = 5;
    order.walletPrice = 2;
    order.tax = 1;
    order.clubDiscount = 0.5;
    const stripped = stripSheinListCostBreakdown(order);
    expect(stripped.total).toBe(20);
    expect(stripped.shippingFee).toBe(3);
    expect(stripped.subtotal).toBeNull();
    expect(stripped.couponPrice).toBeNull();
    expect(stripped.walletPrice).toBeNull();
    expect(stripped.tax).toBeNull();
    expect(stripped.clubDiscount).toBeNull();
    // Không mutate bản gốc
    expect(order.couponPrice).toBe(5);
  });
});
