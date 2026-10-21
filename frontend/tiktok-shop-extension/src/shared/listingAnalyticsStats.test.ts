import { describe, expect, it } from 'vitest';
import {
  ATC_BUYER_KEYS,
  ATC_UNIT_KEYS,
  CLICK_KEYS,
  CTR_KEYS,
  CURRENCY_KEYS,
  GMV_KEYS,
  IMPRESSION_KEYS,
  ORDER_KEYS,
  analyticsDeltaRatio,
  analyticsInt,
  analyticsMoney,
  analyticsRate,
  analyticsRatio,
  compactAnalyticsStats,
  describeRowKeys,
  findProductId,
  firstDefined,
  nestedRecords,
  parseAnalyticsNumber,
  pickAnalyticsHasMore,
  pickAnalyticsList,
  pickAnalyticsStatsPair,
  pickAnalyticsTotal,
  readMetric,
} from './listingAnalyticsStats';

const money = (amount: string) => ({
  amount_formatted: `$${amount}`,
  amount_delimited: amount,
  amount,
  currency_code: 'USD',
  currency_symbol: '$',
});

/** Một dòng cắt gọn từ response thật của `POST /api/v3/insights/seller/ttp/product/list`. */
const realRow = {
  meta: {
    product_id: '1732502214127227455',
    product_name: "Women's Orange Red Summer Casual Tropical Halter Backless Sequin Tie-Up Top",
    product_image: 'https://p16-oec-general-useast5.ttcdn-us.com/tos/fe05905f.jpeg?t=555f072d',
    product_status: 1,
    gmv_range: 2,
    share_link: 'aweme://ec/pdp?requestParams=%7B%22product_id%22%3A%5B%221732502214127227455%22%5D%7D',
  },
  stats_v3: {
    total: {
      product_impression_pv: '7080',
      product_impression_uv: '5458',
      product_click_pv: '470',
      product_click_uv: '427',
      product_ctr_pv: '0.0664',
      product_ctr_uv: '0.0782',
      add_to_cart_cnt_pv: '67',
      add_to_cart_users_uv: '55',
      gmv: money('254.44'),
      orders: '7',
      sku_orders: '7',
      items_sold: '7',
      aov_sku: money('36.35'),
    },
    total_prev: {
      product_impression_pv: '4039',
      product_click_pv: '208',
      product_ctr_pv: '0.0515',
      add_to_cart_cnt_pv: '35',
      add_to_cart_users_uv: '28',
      gmv: money('146.06'),
      orders: '4',
      sku_orders: '4',
    },
    // Tỷ trọng trong tổng shop — KHÔNG phải % thay đổi.
    total_contribution: {
      product_impression_pv: '0.0968',
      gmv: '0.2259',
      sku_orders: '0.2059',
    },
  },
};

describe('parseAnalyticsNumber', () => {
  it('doc number va string tien te', () => {
    expect(parseAnalyticsNumber(1234.56)).toBe(1234.56);
    expect(parseAnalyticsNumber('$1,234.56')).toBe(1234.56);
    expect(parseAnalyticsNumber('12.3K')).toBe(12_300);
    expect(parseAnalyticsNumber('1.2M')).toBe(1_200_000);
  });

  it('string phan tram tra ve phan so', () => {
    expect(parseAnalyticsNumber('4.5%')).toBeCloseTo(0.045, 10);
    expect(parseAnalyticsNumber('-12.5%')).toBeCloseTo(-0.125, 10);
  });

  it('bo qua gia tri rong / khong so', () => {
    expect(parseAnalyticsNumber('-')).toBeUndefined();
    expect(parseAnalyticsNumber('--')).toBeUndefined();
    expect(parseAnalyticsNumber('')).toBeUndefined();
    expect(parseAnalyticsNumber(null)).toBeUndefined();
    expect(parseAnalyticsNumber(Number.NaN)).toBeUndefined();
  });

  it('doc value long trong object metric', () => {
    expect(parseAnalyticsNumber({ value: '2,000' })).toBe(2000);
    expect(parseAnalyticsNumber({ amount: { value: 5 } })).toBe(5);
  });
});

describe('chuan hoa metric', () => {
  it('gmv khong am, 2 chu so thap phan', () => {
    expect(analyticsMoney('$1,234.567')).toBe(1234.57);
    expect(analyticsMoney(-10)).toBe(0);
    expect(analyticsMoney(undefined)).toBeUndefined();
  });

  it('dem lam tron ve nguyen', () => {
    expect(analyticsInt('12.3K')).toBe(12_300);
    expect(analyticsInt(3.6)).toBe(4);
    expect(analyticsInt(-5)).toBe(0);
  });

  it('ctr luon la phan so', () => {
    expect(analyticsRate(0.0234)).toBeCloseTo(0.0234, 10);
    expect(analyticsRate('2.34%')).toBeCloseTo(0.0234, 10);
    // TikTok co luc gui percent tho (2.34 = 2.34%).
    expect(analyticsRate(2.34)).toBeCloseTo(0.0234, 10);
    expect(analyticsRate(-1)).toBeUndefined();
  });

  it('ratio giu dau am', () => {
    expect(analyticsRatio('-12.5%')).toBeCloseTo(-0.125, 10);
    expect(analyticsRatio(0.12)).toBeCloseTo(0.12, 10);
    expect(analyticsRatio(undefined)).toBeUndefined();
  });
});

describe('pickAnalyticsStatsPair', () => {
  it('uu tien stats_v3.total va doc kem total_prev', () => {
    const pair = pickAnalyticsStatsPair({
      stats_v3: { total: { gmv: 10 }, total_prev: { gmv: 8 }, daily: [{ gmv: 1 }] },
    });
    expect(pair.total).toEqual({ gmv: 10 });
    expect(pair.previous).toEqual({ gmv: 8 });
  });

  it('fallback ve node stats khi khong co total', () => {
    expect(pickAnalyticsStatsPair({ stats: { gmv: 7 } })).toEqual({
      total: { gmv: 7 },
      previous: null,
    });
    expect(pickAnalyticsStatsPair({ other: 1 })).toEqual({ total: null, previous: null });
  });
});

describe('readMetric', () => {
  it('doc dang scalar kem ratio key anh em', () => {
    const stats = { gmv: '1,000', gmv_chain_ratio: '10%' };
    const metric = readMetric(stats, GMV_KEYS);
    expect(analyticsMoney(metric.value)).toBe(1000);
    expect(analyticsRatio(metric.ratio)).toBeCloseTo(0.1, 10);
  });

  it('doc dang object { value, chain_ratio }', () => {
    const stats = { sku_orders: { value: 42, chain_ratio: -0.2 } };
    const metric = readMetric(stats, ORDER_KEYS);
    expect(analyticsInt(metric.value)).toBe(42);
    expect(analyticsRatio(metric.ratio)).toBeCloseTo(-0.2, 10);
  });

  it('tra rong khi khong co key nao khop', () => {
    expect(readMetric({ gmv: 1 }, ATC_UNIT_KEYS)).toEqual({});
  });

  it('doc alias impressions / clicks / ctr', () => {
    const stats = {
      product_impressions: 5000,
      product_page_views: 250,
      ctr: '5%',
    };
    expect(analyticsInt(readMetric(stats, IMPRESSION_KEYS).value)).toBe(5000);
    expect(analyticsInt(readMetric(stats, CLICK_KEYS).value)).toBe(250);
    expect(analyticsRate(readMetric(stats, CTR_KEYS).value)).toBeCloseTo(0.05, 10);
  });
});

describe('findProductId', () => {
  it('doc product_id o cap ngoai cung', () => {
    expect(findProductId({ product_id: '1729384756102938', gmv: 10 })).toBe('1729384756102938');
    expect(findProductId({ productId: 1729384756102938 })).toBe('1729384756102938');
  });

  it('doc product_id long trong wrapper bat ky ten gi', () => {
    expect(findProductId({ product_info: { product_id: '1729384756102938' } })).toBe(
      '1729384756102938',
    );
    expect(findProductId({ item: { basic: { item_id: '1729384756102938' } } })).toBe(
      '1729384756102938',
    );
  });

  it('uu tien product_id hon id chung cua node khac', () => {
    const row = {
      id: 'category-987654',
      shop: { id: '55555555' },
      detail: { product_id: '1729384756102938' },
    };
    expect(findProductId(row)).toBe('1729384756102938');
  });

  it('doc dang dimension key/value', () => {
    const row = {
      dimensions: [
        { key: 'shop_id', value: '7000000000' },
        { key: 'product_id', value: '1729384756102938' },
      ],
    };
    expect(findProductId(row)).toBe('1729384756102938');
  });

  it('doc id bi boc trong { value }', () => {
    expect(findProductId({ product_id: { value: '1729384756102938' } })).toBe('1729384756102938');
  });

  it('bo qua id nam trong stats_v3', () => {
    expect(findProductId({ stats_v3: { total: { product_id: '1729384756102938' } } })).toBeUndefined();
  });

  it('bo qua gia tri khong giong id', () => {
    expect(findProductId({ product_id: '-' })).toBeUndefined();
    expect(findProductId({ product_id: 'n/a' })).toBeUndefined();
    expect(findProductId({ gmv: 10 })).toBeUndefined();
  });
});

describe('nestedRecords / describeRowKeys', () => {
  it('gom moi record long tru node stats', () => {
    const row = { a: 1, product: { name: 'x', extra: { b: 2 } }, stats_v3: { total: { gmv: 1 } } };
    const sources = nestedRecords(row, 2);

    expect(sources[0]).toBe(row);
    expect(sources.some((s) => s.name === 'x')).toBe(true);
    expect(sources.some((s) => s.b === 2)).toBe(true);
    expect(sources.some((s) => s.gmv === 1)).toBe(false);
  });

  it('liet ke duong dan key de bao loi doc duoc', () => {
    const keys = describeRowKeys({ product: { title: 'x' }, gmv: 1 });

    expect(keys).toContain('product.title');
    expect(keys).toContain('gmv');
  });

  it('doc key khi dong bi boc trong mang', () => {
    expect(describeRowKeys([{ product_id: '1' }])).toEqual(['product_id']);
  });
});

describe('phan trang', () => {
  it('tim danh sach product o cac dang field khac nhau', () => {
    expect(pickAnalyticsList({ list: [1, 2] })).toEqual({ found: true, list: [1, 2] });
    expect(pickAnalyticsList({ data: { product_list: [1] } })).toEqual({ found: true, list: [1] });
    expect(pickAnalyticsList([1])).toEqual({ found: true, list: [1] });
    expect(pickAnalyticsList({ nope: 1 })).toEqual({ found: false, list: [] });
  });

  it('doc total tu data hoac pagination', () => {
    expect(pickAnalyticsTotal(null, { total: 250 })).toBe(250);
    expect(pickAnalyticsTotal(null, { pagination: { total: '1,200' } })).toBe(1200);
    expect(pickAnalyticsTotal({ total: 5 }, null)).toBe(5);
    expect(pickAnalyticsTotal(null, {})).toBe(0);
  });

  it('doc total va has_more tu list_control.next_pagination', () => {
    const data = {
      items: [],
      list_control: {
        rules: [{ direction: 2, field: 'gmv' }],
        next_pagination: { has_more: true, next_page: 1, total_page: 19, total: 182 },
      },
    };

    expect(pickAnalyticsTotal(null, data)).toBe(182);
    expect(pickAnalyticsHasMore(data)).toBe(true);
  });

  it('has_more = undefined khi payload khong noi gi', () => {
    expect(pickAnalyticsHasMore({ items: [] })).toBeUndefined();
    expect(
      pickAnalyticsHasMore({ list_control: { next_pagination: { has_more: false } } }),
    ).toBe(false);
  });
});

describe('payload that cua Compass product/list', () => {
  it('lay duoc danh sach o data.items', () => {
    const picked = pickAnalyticsList({ items: [realRow] });
    expect(picked.found).toBe(true);
    expect(picked.list).toHaveLength(1);
  });

  it('doc product_id trong meta', () => {
    expect(findProductId(realRow)).toBe('1732502214127227455');
  });

  it('doc ten / anh trong meta qua nestedRecords', () => {
    const sources = nestedRecords(realRow, 2);
    const meta = sources.find((source) => source.product_name != null);
    expect(meta?.product_name).toContain('Halter Backless Sequin');
    expect(meta?.product_image).toContain('ttcdn-us.com');
  });

  it('doc du 7 metric tu stats_v3.total', () => {
    const { total, previous } = pickAnalyticsStatsPair(realRow);
    expect(total).not.toBeNull();
    expect(previous).not.toBeNull();
    const stats = total!;

    expect(analyticsMoney(readMetric(stats, GMV_KEYS).value)).toBe(254.44);
    expect(analyticsInt(readMetric(stats, ORDER_KEYS).value)).toBe(7);
    expect(analyticsInt(readMetric(stats, IMPRESSION_KEYS).value)).toBe(7080);
    expect(analyticsInt(readMetric(stats, CLICK_KEYS).value)).toBe(470);
    expect(analyticsRate(readMetric(stats, CTR_KEYS).value)).toBeCloseTo(0.0664, 10);
    expect(analyticsInt(readMetric(stats, ATC_UNIT_KEYS).value)).toBe(67);
    expect(analyticsInt(readMetric(stats, ATC_BUYER_KEYS).value)).toBe(55);
  });

  it('doc currency tu object gmv', () => {
    const stats = pickAnalyticsStatsPair(realRow).total!;
    const gmvNode = firstDefined(stats, GMV_KEYS) as Record<string, unknown>;
    expect(firstDefined(gmvNode, CURRENCY_KEYS)).toBe('USD');
  });

  it('tinh % thay doi tu total_prev, khong dung total_contribution', () => {
    const { total, previous } = pickAnalyticsStatsPair(realRow);

    const gmvNow = analyticsMoney(readMetric(total!, GMV_KEYS).value);
    const gmvPrev = analyticsMoney(readMetric(previous!, GMV_KEYS).value);
    // (254.44 - 146.06) / 146.06 = +74.2%, khac han total_contribution 0.2259.
    expect(analyticsDeltaRatio(gmvNow, gmvPrev)).toBeCloseTo(0.742024, 5);

    const impressionsNow = analyticsInt(readMetric(total!, IMPRESSION_KEYS).value);
    const impressionsPrev = analyticsInt(readMetric(previous!, IMPRESSION_KEYS).value);
    expect(analyticsDeltaRatio(impressionsNow, impressionsPrev)).toBeCloseTo(0.752909, 5);

    // Payload khong co ratio theo tung metric → readMetric khong duoc bat total_contribution.
    expect(readMetric(total!, GMV_KEYS).ratio).toBeUndefined();
  });

  it('nen stats: money object rut con amount', () => {
    const compact = compactAnalyticsStats(pickAnalyticsStatsPair(realRow).total!);

    expect(compact.gmv).toBe('254.44');
    expect(compact.aov_sku).toBe('36.35');
    expect(compact.product_impression_pv).toBe('7080');
    expect(JSON.stringify(compact).length).toBeLessThan(
      JSON.stringify(pickAnalyticsStatsPair(realRow).total).length,
    );
  });
});

describe('analyticsDeltaRatio', () => {
  it('tinh phan tram thay doi so ky truoc', () => {
    expect(analyticsDeltaRatio(150, 100)).toBeCloseTo(0.5, 10);
    expect(analyticsDeltaRatio(50, 100)).toBeCloseTo(-0.5, 10);
  });

  it('ky truoc bang 0: 0 khi ca hai bang 0, bo trong khi tang tu 0', () => {
    expect(analyticsDeltaRatio(0, 0)).toBe(0);
    expect(analyticsDeltaRatio(10, 0)).toBeUndefined();
  });

  it('thieu so lieu thi khong doan', () => {
    expect(analyticsDeltaRatio(undefined, 100)).toBeUndefined();
    expect(analyticsDeltaRatio(100, undefined)).toBeUndefined();
  });
});

describe('readMetric khop ten da chuan hoa', () => {
  it('camelCase va snake_case dung chung alias', () => {
    expect(analyticsInt(readMetric({ productImpressionPv: '900' }, IMPRESSION_KEYS).value)).toBe(900);
    expect(analyticsInt(readMetric({ 'product-click-pv': '12' }, CLICK_KEYS).value)).toBe(12);
  });

  it('khong nham alias gan giong nhau', () => {
    // `add_to_cart_cnt_pv` la units, `add_to_cart_users_uv` la so nguoi.
    const stats = { add_to_cart_cnt_pv: '9', add_to_cart_users_uv: '5' };
    expect(analyticsInt(readMetric(stats, ATC_UNIT_KEYS).value)).toBe(9);
    expect(analyticsInt(readMetric(stats, ATC_BUYER_KEYS).value)).toBe(5);
  });
});
