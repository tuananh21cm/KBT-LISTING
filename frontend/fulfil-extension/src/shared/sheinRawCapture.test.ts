import { afterEach, describe, expect, it } from 'vitest';

import {
  captureSheinRaw,
  extractBreakdownFromDetail,
  isSheinRawCaptureEnabled,
  normalizeSheinOrder,
  setSheinRawCaptureEnabled,
  SHEIN_RAW_CAPTURE_MAX_CHARS,
  SHEIN_RAW_TRUNCATED_SUFFIX,
  stubSheinOrderPayload,
} from './sheinOrders';

afterEach(() => {
  setSheinRawCaptureEnabled(false);
});

describe('cờ capture JSON gốc SHEIN', () => {
  it('mặc định tắt để sync thường ngày không phình payload', () => {
    expect(isSheinRawCaptureEnabled()).toBe(false);
    expect(captureSheinRaw({ billno: 'GSU1' })).toBeNull();
  });

  it('bật thì serialize nguyên vẹn', () => {
    setSheinRawCaptureEnabled(true);
    expect(captureSheinRaw({ billno: 'GSU1', n: 1 })).toBe('{"billno":"GSU1","n":1}');
  });

  it('không nuốt giá trị falsy hợp lệ nhưng bỏ qua null', () => {
    setSheinRawCaptureEnabled(true);
    expect(captureSheinRaw(null)).toBeNull();
    expect(captureSheinRaw(undefined)).toBeNull();
    expect(captureSheinRaw(0)).toBe('0');
  });

  it('trả null thay vì ném khi gặp vòng lặp tham chiếu', () => {
    setSheinRawCaptureEnabled(true);
    const cyclic: Record<string, unknown> = { billno: 'GSU1' };
    cyclic.self = cyclic;
    expect(captureSheinRaw(cyclic)).toBeNull();
  });

  it('cắt ở trần và đánh dấu rõ là bản cắt', () => {
    setSheinRawCaptureEnabled(true);
    const raw = captureSheinRaw({ pad: 'x'.repeat(SHEIN_RAW_CAPTURE_MAX_CHARS + 5000) });
    expect(raw).not.toBeNull();
    expect(raw!.endsWith(SHEIN_RAW_TRUNCATED_SUFFIX)).toBe(true);
    expect(raw!.length).toBe(SHEIN_RAW_CAPTURE_MAX_CHARS + SHEIN_RAW_TRUNCATED_SUFFIX.length);
  });
});

describe('gắn raw vào payload đồng bộ', () => {
  const listItem = {
    billno: 'GSU18G2440023Y8',
    currency_code: 'USD',
    shippingaddr_info: { shipping_telephone: '5551234567', shipping_postcode: '10001' },
  };

  it('normalizeSheinOrder bỏ trống raw khi tắt', () => {
    expect(normalizeSheinOrder(listItem).rawListItem).toBeNull();
  });

  it('normalizeSheinOrder giữ nguyên item gốc khi bật', () => {
    setSheinRawCaptureEnabled(true);
    const payload = normalizeSheinOrder(listItem);
    expect(payload.rawListItem).toBe(JSON.stringify(listItem));
    expect(JSON.parse(payload.rawListItem!).billno).toBe('GSU18G2440023Y8');
  });

  it('extractBreakdownFromDetail giữ nguyên response detail khi bật', () => {
    setSheinRawCaptureEnabled(true);
    const target = stubSheinOrderPayload('GSU18G2440023Y8');
    const detail = { order: { billno: 'GSU18G2440023Y8', totalPrice: { amount: 12.34 } } };
    extractBreakdownFromDetail(detail, target);
    expect(target.rawDetail).toBe(JSON.stringify(detail));
  });

  it('extractBreakdownFromDetail không gắn gì khi tắt', () => {
    const target = stubSheinOrderPayload('GSU18G2440023Y8');
    extractBreakdownFromDetail({ order: { billno: 'GSU18G2440023Y8' } }, target);
    expect(target.rawDetail).toBeUndefined();
  });
});
