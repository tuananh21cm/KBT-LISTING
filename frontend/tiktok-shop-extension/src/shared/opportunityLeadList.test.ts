import { describe, expect, it } from 'vitest';
import {
  buildLeadListRequestBodies,
  describeLeadListShape,
  isEmptyLeadPayload,
  pickLeadList,
  pickLeadTotal,
} from './opportunityLeadList';

describe('pickLeadList', () => {
  it('doc leads[] / lead_list[] / list long 2 cap', () => {
    expect(pickLeadList({ leads: [{ lead_id: '1' }] })).toEqual({
      found: true,
      list: [{ lead_id: '1' }],
    });
    expect(pickLeadList({ lead_list: [] })).toEqual({ found: true, list: [] });
    expect(pickLeadList({ data: { list: [{ lead_id: '2' }] } }).list).toEqual([{ lead_id: '2' }]);
  });

  it('null hoac lead_list:null la danh sach rong — khong phai loi schema', () => {
    expect(pickLeadList(null)).toEqual({ found: true, list: [] });
    expect(pickLeadList({ lead_list: null, total_product_count: 0 })).toEqual({
      found: true,
      list: [],
    });
    expect(pickLeadList({ leads: null })).toEqual({ found: true, list: [] });
  });

  it('doc lead_infos va mang lead nam o key la', () => {
    expect(pickLeadList({ lead_infos: [{ lead_id: 'a' }] }).list).toEqual([{ lead_id: 'a' }]);
    expect(
      pickLeadList({ payload: { result: { rows: [{ lead_id: 'b', lead_name: 'dress' }] } } }).list,
    ).toEqual([{ lead_id: 'b', lead_name: 'dress' }]);
  });

  it('parse JSON string double-encoded', () => {
    const inner = JSON.stringify({ leads: [{ lead_id: 'x' }] });
    expect(pickLeadList(inner).list).toEqual([{ lead_id: 'x' }]);
  });

  it('khong nham tag_codes[] string la danh sach lead', () => {
    expect(pickLeadList({ tag_codes: ['hot_search'], extra: true }).found).toBe(false);
  });
});

describe('isEmptyLeadPayload / describeLeadListShape', () => {
  it('{} va chi meta + list null la rong', () => {
    expect(isEmptyLeadPayload({})).toBe(true);
    expect(isEmptyLeadPayload({ total_product_count: 0, lead_list: null })).toBe(true);
    expect(isEmptyLeadPayload({ status: 'CATEGORY_NOT_SUPPORTED' })).toBe(false);
  });

  it('mo ta key de debug toast', () => {
    expect(describeLeadListShape(null)).toBe('data=null');
    expect(describeLeadListShape({ lead_infos: [], total: 0 })).toBe('lead_infos[], total:number');
  });
});

describe('pickLeadTotal', () => {
  it('doc total / total_count / pagination', () => {
    expect(pickLeadTotal(null, { total: 12 })).toBe(12);
    expect(pickLeadTotal({ total_product_count: 3 }, {})).toBe(3);
    expect(pickLeadTotal(null, { pagination: { total_count: '40' } })).toBe(40);
  });
});

describe('buildLeadListRequestBodies', () => {
  it('khong AND tag_code hot_search+low_competition', () => {
    const [primary, fallback] = buildLeadListRequestBodies([{ cate_id: 900624, level: 4 }], 1, 100);
    expect(primary).toBeDefined();
    expect(fallback).toBeDefined();
    expect(primary).not.toContain('tag_code');
    expect(primary).toContain('trending_keywords');
    expect(fallback).not.toContain('tab_code_filter');
    const parsed = JSON.parse(primary!);
    expect(parsed.category_infos[0]).toMatchObject({ cate_id: 900624, cateId: 900624, level: 4 });
  });
});
