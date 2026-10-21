import { describe, expect, it } from 'vitest';
import { extractSheinStatus } from './sheinOrders';

describe('extractSheinStatus — list status beats Return&Refund chrome', () => {
  it('status 5 + Return&Refund button → shipped, not refunded', () => {
    const status = extractSheinStatus({
      status: '5',
      orderStatus: 10,
      orderStatusTitle: 'Shipped',
      isCommonOrderCanRefund: 0,
      return_and_refund_info: {
        button_text: 'Return&Refund',
        popup_title: 'Please select the refund type',
      },
      orderGoodsList: [
        { goods_id: '1', refund_type: 0, goods_status: '5' },
      ],
    });

    expect(status.sheinStatusGroup).toBe('shipped');
  });

  it('status 13 → refunded', () => {
    const status = extractSheinStatus({
      status: '13',
      orderStatus: 11,
    });

    expect(status.sheinStatusGroup).toBe('refunded');
  });
});
