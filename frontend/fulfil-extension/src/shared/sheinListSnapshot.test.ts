import { describe, expect, it } from 'vitest';
import {
  buildSheinListSnapshot,
  filterSheinListOrdersChanged,
  sheinListOrderFingerprint,
  sheinListSnapshotHasChanges,
  type SheinOrderPayload,
} from './sheinOrders';

function order( partial: Partial<SheinOrderPayload> & { billno: string }): SheinOrderPayload {
  return {
    accountEmail: null,
    buyerName: '',
    buyerPhone: '',
    address: '',
    city: '',
    state: '',
    postalCode: '',
    countryCode: '',
    trackingNo: '',
    carrier: '',
    isJointShipment: null,
    relationBillno: null,
    confluenceBatchNo: null,
    orderType: null,
    isMembershipOrVirtual: false,
    currency: 'USD',
    orderedAt: null,
    sheinPaidAt: null,
    sheinUnpaid: null,
    orderUrl: null,
    retailPrice: null,
    subtotal: null,
    shippingFee: null,
    retailDeliveryFee: null,
    onTimeGuarantee: null,
    couponPrice: null,
    clubDiscount: null,
    pointsDiscount: null,
    walletPrice: null,
    tax: null,
    total: null,
    productIds: [],
    sheinStatus: null,
    sheinStatusGroup: null,
    sheinStatusAt: null,
    sheinShippedAt: null,
    sheinTrackingAt: null,
    sheinDeliveredAt: null,
    sheinCanceledAt: null,
    sheinRefundedAt: null,
    ...partial,
  };
}

describe('sheinListSnapshot', () => {
  it('detects first snapshot as change', () => {
    const orders = [order({ billno: 'A1', sheinStatusGroup: 'processing' })];
    expect(sheinListSnapshotHasChanges(null, orders)).toBe(true);
    expect(sheinListSnapshotHasChanges(undefined, orders)).toBe(true);
  });

  it('skips when fingerprints match', () => {
    const orders = [
      order({ billno: 'A1', sheinStatusGroup: 'shipped', trackingNo: 'T1' }),
      order({ billno: 'B2', sheinStatusGroup: 'processing' }),
    ];
    const snap = buildSheinListSnapshot(orders);
    expect(sheinListSnapshotHasChanges(snap, orders)).toBe(false);
  });

  it('detects new billno', () => {
    const prev = buildSheinListSnapshot([
      order({ billno: 'A1', sheinStatusGroup: 'processing' }),
    ]);
    const next = [
      order({ billno: 'A1', sheinStatusGroup: 'processing' }),
      order({ billno: 'B2', sheinStatusGroup: 'processing' }),
    ];
    expect(sheinListSnapshotHasChanges(prev, next)).toBe(true);
  });

  it('detects status/tracking change', () => {
    const base = order({ billno: 'A1', sheinStatusGroup: 'processing' });
    const prev = buildSheinListSnapshot([base]);
    expect(
      sheinListSnapshotHasChanges(prev, [
        order({ billno: 'A1', sheinStatusGroup: 'shipped', trackingNo: 'TN' }),
      ]),
    ).toBe(true);
  });

  it('ignores billnos that aged out of the list window', () => {
    const prev = buildSheinListSnapshot([
      order({ billno: 'OLD', sheinStatusGroup: 'delivered' }),
      order({ billno: 'A1', sheinStatusGroup: 'processing' }),
    ]);
    const next = [order({ billno: 'A1', sheinStatusGroup: 'processing' })];
    expect(sheinListSnapshotHasChanges(prev, next)).toBe(false);
  });

  it('normalizes billno case in fingerprint key', () => {
    const a = sheinListOrderFingerprint(order({ billno: 'ab12' }));
    const b = sheinListOrderFingerprint(order({ billno: 'AB12' }));
    expect(a).toBe(b);
    expect(Object.keys(buildSheinListSnapshot([order({ billno: 'ab12' })]))).toEqual(['AB12']);
  });

  it('filters only new or changed orders for Auto POST', () => {
    const prev = buildSheinListSnapshot([
      order({ billno: 'A1', sheinStatusGroup: 'processing' }),
      order({ billno: 'B2', sheinStatusGroup: 'shipped', trackingNo: 'T1' }),
    ]);
    const next = [
      order({ billno: 'A1', sheinStatusGroup: 'processing' }),
      order({ billno: 'B2', sheinStatusGroup: 'shipped', trackingNo: 'T1' }),
      order({ billno: 'C3', sheinStatusGroup: 'processing' }),
    ];
    const changed = filterSheinListOrdersChanged(prev, next);
    expect(changed.map((o) => o.billno)).toEqual(['C3']);
  });

  it('keeps posting billnos still waiting on Auto Mark Ship', () => {
    // Mã vận đơn đã cấp xong nên fingerprint đứng yên; nếu chỉ nhìn snapshot thì backend không bao
    // giờ nhận thêm lượt POST nào để đẩy nốt mã lên sàn.
    const shipped = order({ billno: 'A1', sheinStatusGroup: 'shipped', trackingNo: 'T1' });
    const prev = buildSheinListSnapshot([shipped]);

    expect(filterSheinListOrdersChanged(prev, [shipped])).toEqual([]);
    expect(
      filterSheinListOrdersChanged(prev, [shipped], new Set(['A1'])).map((o) => o.billno),
    ).toEqual(['A1']);
  });
});
