import { describe, expect, it } from 'vitest';
import {
  emptyShopScoreSnapshot,
  hasAnyShopScore,
  mapAhrScore,
  mapSesScoreOverview,
} from './shopScores';

const SES_RESPONSE = {
  code: 0,
  message: 'success',
  data: {
    overview: { date: '1787356800', score: 3.9, rank: 75, label: 'Normal' },
    metric: {
      metricGroups: [
        { code: '020100', name: 'Product satisfaction', score: '3.8', groupName: 'ses_product' },
        {
          code: '020200',
          name: 'Fulfillment and logistics',
          score: '3.6',
          groupName: 'ses_fulfillment',
        },
        { code: '020300', name: 'Customer service', score: '4.4', groupName: 'ses_service' },
      ],
    },
  },
};

describe('mapAhrScore', () => {
  it('đọc data.score', () => {
    expect(mapAhrScore({ code: 0, message: '', data: { score: 190 } })).toBe(190);
  });

  it('chấp nhận score dạng chuỗi', () => {
    expect(mapAhrScore({ data: { score: '190' } })).toBe(190);
  });

  it('trả null khi payload hỏng', () => {
    expect(mapAhrScore(null)).toBeNull();
    expect(mapAhrScore({})).toBeNull();
    expect(mapAhrScore({ data: {} })).toBeNull();
    expect(mapAhrScore({ data: { score: 'n/a' } })).toBeNull();
  });
});

describe('mapSesScoreOverview', () => {
  it('map điểm tổng và 3 nhóm theo code', () => {
    expect(mapSesScoreOverview(SES_RESPONSE)).toEqual({
      performanceScore: 3.9,
      productSatisfactionScore: 3.8,
      fulfillmentScore: 3.6,
      customerServiceScore: 4.4,
    });
  });

  it('fallback groupName khi thiếu code', () => {
    const raw = {
      data: {
        overview: { score: 4 },
        metric: { metricGroups: [{ score: '4.4', groupName: 'ses_service' }] },
      },
    };

    expect(mapSesScoreOverview(raw)).toEqual({
      performanceScore: 4,
      customerServiceScore: 4.4,
    });
  });

  it('bỏ qua nhóm lạ và payload thiếu metricGroups', () => {
    expect(mapSesScoreOverview({ data: { overview: { score: 3.2 } } })).toEqual({
      performanceScore: 3.2,
    });
    expect(
      mapSesScoreOverview({
        data: { metric: { metricGroups: [{ code: '999999', score: '5' }] } },
      }),
    ).toEqual({});
    expect(mapSesScoreOverview(null)).toEqual({});
  });
});

describe('hasAnyShopScore', () => {
  it('false khi snapshot rỗng, true khi có một điểm', () => {
    const empty = emptyShopScoreSnapshot();
    expect(hasAnyShopScore(empty)).toBe(false);
    expect(hasAnyShopScore({ ...empty, ahrScore: 0 })).toBe(true);
    expect(hasAnyShopScore({ ...empty, performanceScore: 3.9 })).toBe(true);
  });
});
