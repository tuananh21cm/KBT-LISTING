import { describe, expect, it } from 'vitest';
import { extractSheinMilestones } from './sheinOrders';

/**
 * Fixture dựng từ payload gốc thật, thu qua cờ `capture_raw_payload` rồi đọc lại bằng
 * endpoint chẩn đoán — không phải dữ liệu tự nghĩ ra. Mọi mốc thời gian dưới đây là số Unix
 * nguyên văn SHEIN trả về.
 *
 * Hai trường dễ nhầm nhất, đã đối chiếu trên 156 đơn có payload gốc:
 * - `delivery_time` = lúc rời kho. 55/57 đơn còn ở trạng thái `shipped` vẫn có trường này.
 * - `signed_time`   = lúc giao thành công. Vắng ở toàn bộ 57 đơn `shipped`, đủ ở 99/99 đơn
 *   `common_delivered`, và luôn muộn hơn `delivery_time` trung bình 5,9 ngày.
 */

/** Đơn GSU18G2440023Y8 — đã giao. Rời kho 25/07, ký nhận 31/07, cách nhau gần 6 ngày. */
const deliveredOrder = {
  billno: 'GSU18G2440023Y8',
  addTime: 1784855671,
  pay_time: '1784855675',
  delivery_time: 1785002706,
  status: 6,
  orderStatusTitle: 'Shipped',
  track_info: {
    status: 6,
    time: '1785509449',
    status_desc: 'Have been delivered',
  },
  subOrderStatus: [{ packageState: 'common_delivered' }],
  order_package_info_list: [
    {
      packageNo: 'C26072401360190',
      status: 54,
      deliveryTime: 1785002706,
      signed_time: 1785509449,
    },
  ],
};

/**
 * Đơn GSU18V24T001X9L — mới rời kho, chưa giao. Bẫy ở đây: `track_info.time` vẫn có giá trị
 * (sự kiện vận chuyển gần nhất) nên nếu đọc mà không xét `status_desc` sẽ báo đã giao nhầm.
 */
const shippedOrder = {
  billno: 'GSU18V24T001X9L',
  addTime: 1785207005,
  delivery_time: 1785393394,
  status: 3,
  track_info: {
    status: 3,
    time: 1785635100,
    status_desc: 'Shipped',
  },
  subOrderStatus: [{ packageState: 'shipped' }],
  order_package_info_list: [
    {
      packageNo: 'C26072701832249',
      status: 16,
      deliveryTime: 1785393394,
      signed_time: '',
    },
  ],
};

describe('extractSheinMilestones — đơn đã giao', () => {
  const m = extractSheinMilestones(deliveredOrder);

  it('lấy signed_time làm mốc giao, không lấy delivery_time', () => {
    expect(m.deliveredAt).toBe('2026-07-31T14:50:49.000Z');
  });

  it('lấy delivery_time làm mốc rời kho', () => {
    expect(m.shippedAt).toBe('2026-07-25T18:05:06.000Z');
  });

  it('mốc giao muộn hơn mốc rời kho', () => {
    expect(Date.parse(m.deliveredAt!)).toBeGreaterThan(Date.parse(m.shippedAt!));
  });

  it('không tự suy mốc gán mã vận đơn — SHEIN không phát ra clock này', () => {
    expect(m.trackingAt).toBeNull();
  });
});

describe('extractSheinMilestones — đơn mới rời kho', () => {
  const m = extractSheinMilestones(shippedOrder);

  it('có mốc rời kho', () => {
    expect(m.shippedAt).toBe('2026-07-30T06:36:34.000Z');
  });

  it('không đóng dấu đã giao dù track_info.time có giá trị', () => {
    expect(m.deliveredAt).toBeNull();
  });
});

describe('extractSheinMilestones — đơn nhiều gói', () => {
  /** Hai gói rời kho khác ngày, chỉ một gói được ký nhận. */
  const partiallyDelivered = {
    billno: 'MULTI-1',
    order_package_info_list: [
      { packageNo: 'C1', deliveryTime: 1785002706, signed_time: 1785509449 },
      { packageNo: 'C2', deliveryTime: 1785089106, signed_time: '' },
    ],
  };

  it('chưa báo đã giao khi còn gói chưa ký nhận', () => {
    expect(extractSheinMilestones(partiallyDelivered).deliveredAt).toBeNull();
  });

  it('mốc rời kho tính theo gói sớm nhất', () => {
    expect(extractSheinMilestones(partiallyDelivered).shippedAt).toBe('2026-07-25T18:05:06.000Z');
  });

  it('giao xong tính theo gói ký nhận muộn nhất', () => {
    const allDelivered = {
      billno: 'MULTI-2',
      order_package_info_list: [
        { packageNo: 'C1', deliveryTime: 1785002706, signed_time: 1785509449 },
        { packageNo: 'C2', deliveryTime: 1785089106, signed_time: 1785595849 },
      ],
    };
    expect(extractSheinMilestones(allDelivered).deliveredAt).toBe('2026-08-01T14:50:49.000Z');
  });
});
