import { describe, expect, it } from 'vitest';
import { findBestMatchingTask, matchesTaskUrl, normalizeTaskUrl } from './taskUrlMatch';

const TARGET = 'https://seller-us.tiktok.com/product/manage';

describe('normalizeTaskUrl', () => {
  it('bỏ query, fragment và dấu / cuối', () => {
    expect(normalizeTaskUrl('https://seller-us.tiktok.com/product/manage/?tab=1#x')).toBe(
      'https://seller-us.tiktok.com/product/manage',
    );
  });

  it('trả rỗng khi không parse được', () => {
    expect(normalizeTaskUrl('seller-us.tiktok.com/product')).toBe('');
    expect(normalizeTaskUrl('')).toBe('');
    expect(normalizeTaskUrl(null)).toBe('');
  });
});

describe('matchesTaskUrl', () => {
  it('khớp khi trùng URL đích', () => {
    expect(matchesTaskUrl(TARGET, TARGET)).toBe(true);
  });

  it('chấp nhận query thừa TikTok tự thêm', () => {
    expect(matchesTaskUrl(`${TARGET}?shop_region=US&tab=live`, TARGET)).toBe(true);
  });

  it('chấp nhận đi sâu hơn trong cùng nhánh path', () => {
    expect(matchesTaskUrl(`${TARGET}/list`, TARGET)).toBe(true);
  });

  it('không khớp khi sang path khác', () => {
    expect(matchesTaskUrl('https://seller-us.tiktok.com/homepage', TARGET)).toBe(false);
  });

  it('không khớp khi đổi host — redirect ra ngoài không tính là xong', () => {
    expect(matchesTaskUrl('https://www.tiktok.com/product/manage', TARGET)).toBe(false);
  });

  it('dùng pattern riêng khi backend gửi kèm', () => {
    const pattern = 'https://seller-us.tiktok.com/compass';
    expect(matchesTaskUrl('https://seller-us.tiktok.com/compass/overview', TARGET, pattern)).toBe(
      true,
    );
    expect(matchesTaskUrl(TARGET, TARGET, pattern)).toBe(false);
  });

  it('bỏ qua pattern rỗng và quay về URL đích', () => {
    expect(matchesTaskUrl(TARGET, TARGET, '   ')).toBe(true);
  });

  it('không phân biệt hoa thường', () => {
    expect(matchesTaskUrl('https://Seller-US.tiktok.com/Product/Manage', TARGET)).toBe(true);
  });

  it('không khớp khi thiếu dữ liệu', () => {
    expect(matchesTaskUrl(undefined, TARGET)).toBe(false);
    expect(matchesTaskUrl(TARGET, undefined)).toBe(false);
  });

  // So prefix theo chuỗi nên path chỉ trùng tiền tố cũng tính là khớp — ghi lại để ai đọc sau
  // biết đây là hành vi cố ý (giống hệt backend), không phải lỗ hổng mới.
  it('so theo prefix chuỗi nên /product/manage-v2 vẫn khớp', () => {
    expect(matchesTaskUrl('https://seller-us.tiktok.com/product/manage-v2', TARGET)).toBe(true);
  });
});

describe('findBestMatchingTask', () => {
  it('chọn nhiệm vụ path dài hơn khi URL khớp nhiều prefix', () => {
    const items = [
      {
        id: 'host',
        url: 'https://seller-us.tiktok.com',
        urlMatchPattern: 'https://seller-us.tiktok.com',
        done: false,
      },
      {
        id: 'product',
        url: TARGET,
        urlMatchPattern: TARGET,
        done: false,
      },
    ];
    expect(findBestMatchingTask(items, `${TARGET}?tab=1`)?.id).toBe('product');
  });

  it('bỏ nhiệm vụ đã done', () => {
    const items = [
      { id: 'a', url: TARGET, urlMatchPattern: TARGET, done: true },
      { id: 'b', url: TARGET, urlMatchPattern: TARGET, done: false },
    ];
    expect(findBestMatchingTask(items, TARGET)?.id).toBe('b');
  });
});
