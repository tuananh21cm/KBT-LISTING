/** Điểm Health Center của shop, map sẵn để POST lên Kinhbac. */
export interface ShopScoreSnapshot {
  /** `account/ahr/score` → `data.score`. */
  ahrScore: number | null;
  /** Shop Performance Score tổng — `overview.score`. */
  performanceScore: number | null;
  /** Nhóm `020100`. */
  productSatisfactionScore: number | null;
  /** Nhóm `020200`. */
  fulfillmentScore: number | null;
  /** Nhóm `020300`. */
  customerServiceScore: number | null;
}

type ScoreGroupKey = Exclude<keyof ShopScoreSnapshot, 'ahrScore' | 'performanceScore'>;

/** `metricGroups[].code` — bền hơn tên hiển thị vì TikTok đổi label theo locale. */
const METRIC_GROUP_BY_CODE: Record<string, ScoreGroupKey> = {
  '020100': 'productSatisfactionScore',
  '020200': 'fulfillmentScore',
  '020300': 'customerServiceScore',
};

/** Fallback khi payload thiếu `code`. */
const METRIC_GROUP_BY_NAME: Record<string, ScoreGroupKey> = {
  ses_product: 'productSatisfactionScore',
  ses_fulfillment: 'fulfillmentScore',
  ses_service: 'customerServiceScore',
};

/** Snapshot rỗng — mọi điểm null. */
export function emptyShopScoreSnapshot(): ShopScoreSnapshot {
  return {
    ahrScore: null,
    performanceScore: null,
    productSatisfactionScore: null,
    fulfillmentScore: null,
    customerServiceScore: null,
  };
}

/** True khi snapshot có ít nhất một điểm — backend từ chối payload rỗng. */
export function hasAnyShopScore(snapshot: ShopScoreSnapshot): boolean {
  return (
    snapshot.ahrScore != null ||
    snapshot.performanceScore != null ||
    snapshot.productSatisfactionScore != null ||
    snapshot.fulfillmentScore != null ||
    snapshot.customerServiceScore != null
  );
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

/** TikTok trả `overview.score` là number nhưng `metricGroups[].score` là string. */
function readNumber(value: unknown): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'string') {
    const trimmed = value.trim();
    if (!trimmed) return null;
    const parsed = Number(trimmed);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

/** Map `data.score` từ `/api/v1/account/ahr/score`. */
export function mapAhrScore(raw: unknown): number | null {
  const data = asRecord(asRecord(raw)?.data);
  if (!data) return null;
  const score = readNumber(data.score);
  return score == null ? null : Math.round(score);
}

/** Map điểm tổng + 3 nhóm từ `/api/v1/seller/experience-score/score-overview`. */
export function mapSesScoreOverview(raw: unknown): Partial<ShopScoreSnapshot> {
  const data = asRecord(asRecord(raw)?.data);
  if (!data) return {};

  const result: Partial<ShopScoreSnapshot> = {};

  const overviewScore = readNumber(asRecord(data.overview)?.score);
  if (overviewScore != null) result.performanceScore = overviewScore;

  const groups = asRecord(data.metric)?.metricGroups;
  if (!Array.isArray(groups)) return result;

  for (const row of groups) {
    const group = asRecord(row);
    if (!group) continue;

    const code = typeof group.code === 'string' ? group.code.trim() : '';
    const groupName = typeof group.groupName === 'string' ? group.groupName.trim() : '';
    const key = METRIC_GROUP_BY_CODE[code] ?? METRIC_GROUP_BY_NAME[groupName];
    if (!key) continue;

    const score = readNumber(group.score);
    if (score != null) result[key] = score;
  }

  return result;
}
