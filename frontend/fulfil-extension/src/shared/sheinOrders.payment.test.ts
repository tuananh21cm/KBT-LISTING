import { describe, expect, it } from 'vitest';
import { extractSheinPaidAt } from './sheinOrders';

/**
 * Fixture lấy nguyên văn từ payload gốc thật, thu qua cờ `capture_raw_payload` rồi đọc lại bằng
 * endpoint chẩn đoán. Khảo sát 50 billno đã thu: cả 50 đơn thật đều có `pay_time` > 0, chỉ đơn
 * ghost GSU18J247000TWB có `pay_time` = 0 — đó là cơ sở để coi khoá này là tín hiệu thanh toán.
 */

/**
 * Đơn ghost GSU18J247000TWB của đơn TikTok 577494030896631910: tạo 26/07 08:59:53, hạn trả tiền
 * đúng 4 giờ sau, không ai trả nên SHEIN tự huỷ. `addTime` vẫn bình thường.
 */
const unpaidExpiredOrder = {
  billno: 'GSU18J247000TWB',
  addTime: 1785056393,
  pay_time: 0,
  order_expire_time: 1785070793,
  delivery_time: 0,
};

/** Cùng đơn ghost nhìn từ `get_order_info`: detail dùng `paymentTime` và tính bằng millisecond. */
const unpaidExpiredDetail = {
  billno: 'GSU18J247000TWB',
  addTime: 1785056393,
  paymentTime: 0,
};

/** Đơn đã trả tiền thật (GSU18F24400W3W3, đã giao): list dùng giây. */
const paidListItem = {
  billno: 'GSU18F24400W3W3',
  addTime: 1784598512,
  pay_time: 1784598661,
};

/** Cùng đơn đó nhìn từ detail: cùng mốc nhưng tính bằng millisecond. */
const paidDetail = {
  billno: 'GSU18F24400W3W3',
  paymentTime: 1784598661000,
};

describe('extractSheinPaidAt — đơn chưa thanh toán', () => {
  it('nhận ra pay_time = 0 là chưa trả tiền, không phải epoch 1970', () => {
    const paid = extractSheinPaidAt(unpaidExpiredOrder);

    expect(paid.paidAt).toBeNull();
    expect(paid.isUnpaid).toBe(true);
  });

  it('nhận ra paymentTime = 0 ở payload detail', () => {
    expect(extractSheinPaidAt(unpaidExpiredDetail).isUnpaid).toBe(true);
  });

  it('không lấy addTime làm mốc trả tiền', () => {
    expect(extractSheinPaidAt(unpaidExpiredOrder).paidAt).not.toBe('2026-07-26T08:59:53.000Z');
  });
});

describe('extractSheinPaidAt — đơn đã thanh toán', () => {
  it('đọc pay_time giây từ list', () => {
    const paid = extractSheinPaidAt(paidListItem);

    expect(paid.paidAt).toBe('2026-07-21T01:51:01.000Z');
    expect(paid.isUnpaid).toBe(false);
  });

  it('đọc paymentTime millisecond từ detail ra cùng mốc', () => {
    expect(extractSheinPaidAt(paidDetail).paidAt).toBe('2026-07-21T01:51:01.000Z');
  });

  it('mốc trả tiền muộn hơn mốc tạo đơn — hai sự kiện khác nhau', () => {
    const paid = extractSheinPaidAt(paidListItem);

    expect(new Date(paid.paidAt!).getTime()).toBeGreaterThan(1784598512 * 1000);
  });
});

describe('extractSheinPaidAt — payload không kết luận được', () => {
  it('thiếu hẳn khoá thanh toán thì trả null, không dám kết luận chưa trả', () => {
    // Payload rút gọn (list stub) không được phép biến đơn đã trả thành đơn ghost.
    const paid = extractSheinPaidAt({ billno: 'GSU-STUB', addTime: 1785056393 });

    expect(paid.paidAt).toBeNull();
    expect(paid.isUnpaid).toBeNull();
  });

  it('chuỗi rỗng cũng không phải bằng chứng chưa trả', () => {
    expect(extractSheinPaidAt({ pay_time: '' }).isUnpaid).toBeNull();
  });

  it('pay_time = "0" dạng chuỗi vẫn là chưa trả', () => {
    expect(extractSheinPaidAt({ pay_time: '0' }).isUnpaid).toBe(true);
  });
});
