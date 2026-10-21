import { describe, expect, it } from 'vitest';

import {
  applySheinShippingAddress,
  extractBreakdownFromDetail,
  normalizeSheinOrder,
  stubSheinOrderPayload,
} from './sheinOrders';

/** Billing ≠ Shipping — phone/zip so khớp phải lấy Shipping. */
const listItemBillingDiffersShipping = {
  billno: 'GSU18G24X001XC3',
  currency_code: 'USD',
  shippingaddr_info: {
    shipping_firstname: 'Ship',
    shipping_lastname: 'To',
    shipping_telephone: '5551112222',
    shipping_postcode: '90210',
    shipping_address_1: '1 Shipping Lane',
    shipping_city: 'Beverly Hills',
    shipping_province: 'CA',
    shipping_country_code: 'US',
  },
  billaddr_info: {
    email: 'buyer@example.com',
    // Intentionally different — must NEVER be used for phone/zip match.
    billing_telephone: '5559998888',
    billing_postcode: '10001',
    telephone: '5559998888',
    postcode: '10001',
    shipping_telephone: '5559998888',
    shipping_postcode: '10001',
  },
};

describe('Shipping phone/zip cho Sync Order match', () => {
  it('normalizeSheinOrder lấy phone/zip từ shippingaddr_info, bỏ qua billaddr', () => {
    const payload = normalizeSheinOrder(listItemBillingDiffersShipping);
    expect(payload.buyerPhone).toBe('5551112222');
    expect(payload.postalCode).toBe('90210');
    expect(payload.buyerName).toBe('Ship To');
    expect(payload.address).toContain('Shipping Lane');
    expect(payload.accountEmail).toBe('buyer@example.com');
    expect(payload.buyerPhone).not.toBe('5559998888');
    expect(payload.postalCode).not.toBe('10001');
  });

  it('applySheinShippingAddress overwrite ghi đè phone/zip list sai bằng Shipping detail', () => {
    const target = stubSheinOrderPayload('GSU18G24X001XC3');
    // List từng mang số Billing (hoặc stale).
    target.buyerPhone = '5559998888';
    target.postalCode = '10001';
    target.buyerName = 'Bill To';
    target.address = '9 Billing Rd';

    applySheinShippingAddress(
      target,
      listItemBillingDiffersShipping.shippingaddr_info,
      { overwrite: true },
    );

    expect(target.buyerPhone).toBe('5551112222');
    expect(target.postalCode).toBe('90210');
    expect(target.buyerName).toBe('Ship To');
    expect(target.address).toContain('Shipping Lane');
  });

  it('extractBreakdownFromDetail overwrite Shipping dù list đã có phone/zip', () => {
    const target = stubSheinOrderPayload('GSU18G24X001XC3');
    target.buyerPhone = '5559998888';
    target.postalCode = '10001';

    extractBreakdownFromDetail(
      {
        order: {
          billno: 'GSU18G24X001XC3',
          shippingaddr_info: listItemBillingDiffersShipping.shippingaddr_info,
          billaddr_info: listItemBillingDiffersShipping.billaddr_info,
          totalPrice: { amount: 20 },
        },
      },
      target,
    );

    expect(target.buyerPhone).toBe('5551112222');
    expect(target.postalCode).toBe('90210');
    expect(target.accountEmail).toBe('buyer@example.com');
  });

  it('get_order_info detail: đọc shipping_* phẳng trên root (không có shippingaddr_info)', () => {
    const target = stubSheinOrderPayload('GSUFLAT');
    target.buyerPhone = '5559998888';
    target.postalCode = '10001';
    target.buyerName = 'Bill Name';

    extractBreakdownFromDetail(
      {
        order: {
          billno: 'GSUFLAT',
          // Billing flat — phải bỏ qua
          telephone: '5559998888',
          postcode: '10001',
          firstname: 'Bill',
          lastname: 'Name',
          // Shipping flat (thật trên get_order_info)
          shipping_telephone: '5551112222',
          shipping_postcode: '90210',
          shipping_firstname: 'Ship',
          shipping_lastname: 'To',
          shipping_address_1: '1 Shipping Lane',
          shipping_city: 'Beverly Hills',
          shipping_province: 'CA',
          shipping_country_code: 'US',
          billaddr_info: { email: 'buyer@example.com' },
          totalPrice: { amount: 20 },
        },
      },
      target,
    );

    expect(target.buyerPhone).toBe('5551112222');
    expect(target.postalCode).toBe('90210');
    expect(target.buyerName).toBe('Ship To');
    expect(target.address).toContain('Shipping Lane');
    expect(target.accountEmail).toBe('buyer@example.com');
  });


  it('không đọc alias telephone/phone trên shippingaddr — chỉ shipping_telephone/postcode/name', () => {
    const payload = normalizeSheinOrder({
      billno: 'GSUALIAS',
      shippingaddr_info: {
        telephone: '5550000000',
        phone: '5550000000',
        postcode: '00000',
        firstname: 'Wrong',
        lastname: 'Name',
        shipping_telephone: '5551112222',
        shipping_postcode: '90210',
        shipping_firstname: 'Ship',
        shipping_lastname: 'To',
      },
      billaddr_info: { email: 'a@b.com', shipping_telephone: '5559998888' },
    });
    expect(payload.buyerPhone).toBe('5551112222');
    expect(payload.postalCode).toBe('90210');
    expect(payload.buyerName).toBe('Ship To');
  });

  it('không fallback sang billaddr khi shippingaddr trống', () => {
    const payload = normalizeSheinOrder({
      billno: 'GSUEMPTYSHIP',
      billaddr_info: {
        email: 'x@y.com',
        shipping_telephone: '5550001111',
        shipping_postcode: '30301',
      },
    });
    expect(payload.buyerPhone).toBe('');
    expect(payload.postalCode).toBe('');
    expect(payload.accountEmail).toBe('x@y.com');
  });
});
