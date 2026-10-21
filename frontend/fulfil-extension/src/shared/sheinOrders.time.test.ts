import { describe, expect, it } from 'vitest';
import { coerceSheinTimeToIso, extractSheinOrderedAt } from './sheinOrders';

/**
 * Golden: unix tuyệt đối UTC → ISO Z. Không lấy wall Eastern rồi +7
 * (03:05 ET + 7 = 10:05 sai; đúng là 07:05Z → 14:05 VN).
 */
describe('coerceSheinTimeToIso — unix UTC', () => {
  it('golden addTime 1784531136 → 2026-07-20T07:05:36.000Z', () => {
    expect(coerceSheinTimeToIso(1784531136)).toBe('2026-07-20T07:05:36.000Z');
    expect(coerceSheinTimeToIso('1784531136')).toBe('2026-07-20T07:05:36.000Z');
  });

  it('extractSheinOrderedAt ưu tiên addTime', () => {
    expect(extractSheinOrderedAt({ addTime: 1784531136, pay_time: 1784531200 })).toBe(
      '2026-07-20T07:05:36.000Z',
    );
  });

  it('reject 0 / âm — không ra epoch 1970 (parity backend FromUnix)', () => {
    expect(coerceSheinTimeToIso(0)).toBeNull();
    expect(coerceSheinTimeToIso('0')).toBeNull();
    expect(coerceSheinTimeToIso(-1)).toBeNull();
  });

  it('reject ngoài cửa sổ 2000–2100', () => {
    expect(coerceSheinTimeToIso(100)).toBeNull();
    expect(coerceSheinTimeToIso(9_999_999_999)).toBeNull();
  });

  it('millisecond payload (≥ 1e12) cùng instant', () => {
    expect(coerceSheinTimeToIso(1784531136000)).toBe('2026-07-20T07:05:36.000Z');
  });
});
