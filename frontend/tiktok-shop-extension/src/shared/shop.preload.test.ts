import { describe, expect, it } from 'vitest';
import { parseProductPreload } from './shop';

describe('parseProductPreload', () => {
  it('parse publish_limit snake_case', () => {
    const result = parseProductPreload({
      code: 0,
      data: {
        publish_limit: { total_limit: 200, published_num: 156 },
      },
    });
    expect(result.publishLimit).toEqual({ totalLimit: 200, publishedNum: 156 });
  });

  it('chấp nhận published_num thiếu — mặc định 0', () => {
    const result = parseProductPreload({
      code: 0,
      data: {
        publish_limit: { total_limit: 100 },
      },
    });
    expect(result.publishLimit).toEqual({ totalLimit: 100, publishedNum: 0 });
  });

  it('parse chuỗi số từ TikTok', () => {
    const result = parseProductPreload({
      code: 0,
      data: {
        publish_limit: { total_limit: '200', published_num: '12' },
      },
    });
    expect(result.publishLimit).toEqual({ totalLimit: 200, publishedNum: 12 });
  });

  it('null khi thiếu total_limit', () => {
    const result = parseProductPreload({
      code: 0,
      data: {
        publish_limit: { published_num: 5 },
      },
    });
    expect(result.publishLimit).toBeNull();
    expect(result.publishLimitReason).toContain('total_limit');
  });

  it('payload không có `code` vẫn parse (TTS có version trả thẳng data)', () => {
    const result = parseProductPreload({
      data: {
        publish_limit: { total_limit: 300, published_num: 12 },
      },
    });
    expect(result.publishLimit).toEqual({ totalLimit: 300, publishedNum: 12 });
    expect(result.publishLimitReason).toBeNull();
  });

  it('code ≠ 0 → nêu code + message TikTok', () => {
    const result = parseProductPreload({
      code: 12100001,
      message: 'permission denied',
      data: { publish_limit: { total_limit: 100 } },
    });
    expect(result.publishLimit).toBeNull();
    expect(result.publishLimitReason).toContain('12100001');
    expect(result.publishLimitReason).toContain('permission denied');
  });

  it('thiếu publish_limit → liệt kê key data để biết TTS đổi schema', () => {
    const result = parseProductPreload({
      code: 0,
      data: {
        warehouse_logistics_services: [
          {
            warehouse_id: 'w1',
            logistics_services: [{ service_name: 'USPS', delivery_option_name: 'Standard' }],
          },
        ],
      },
    });
    expect(result.logisticsServices).toHaveLength(1);
    expect(result.publishLimit).toBeNull();
    expect(result.publishLimitReason).toContain('warehouse_logistics_services');
  });

  it('parse publish_limit từ response thật KBT seller 7495500257100007435', () => {
    const result = parseProductPreload({
      code: 0,
      data: {
        publish_limit: { total_limit: 100, published_num: 100, interval: 0 },
      },
    });
    expect(result.publishLimit).toEqual({ totalLimit: 100, publishedNum: 100 });
  });
});
