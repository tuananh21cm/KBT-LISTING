/** Một hình thức phạt từ action_informations. */
export interface HealthActionInfo {
  title: string;
  actionStartTime?: number;
  actionEndTime?: number;
}

/** Một health record đã map sẵn để POST lên Kinhbac. */
export interface SyncHealthRecordItem {
  recordId: string;
  recordType: 'violation' | 'warning';
  reason: string;
  detail: string;
  categoryType: string;
  actions: HealthActionInfo[];
  recordStatus: number;
  createTime: number;
}

const HTML_TAG_RE = /<[^>]+>/g;

/** Bỏ tag HTML — giữ text cho DB nhẹ. */
export function stripHealthHtml(html: string | undefined | null): string {
  if (!html) return '';
  return html
    .replace(/\r\n/g, '\n')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(HTML_TAG_RE, '')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

function readUnixSeconds(value: unknown): number | undefined {
  const n = Number(value ?? 0);
  return Number.isFinite(n) && n > 0 ? n : undefined;
}

function readActionInformations(raw: unknown): HealthActionInfo[] {
  if (!raw || typeof raw !== 'object') return [];
  const enforcement = (raw as { enforcement_expression?: unknown }).enforcement_expression;
  if (!enforcement || typeof enforcement !== 'object') return [];
  const actions = (enforcement as { action_informations?: unknown }).action_informations;
  if (!Array.isArray(actions)) return [];

  const result: HealthActionInfo[] = [];
  for (const row of actions) {
    if (!row || typeof row !== 'object') continue;
    const item = row as {
      action_title?: unknown;
      action_start_time?: unknown;
      action_end_time?: unknown;
    };
    const title = item.action_title;
    if (typeof title !== 'string' || !title.trim()) continue;

    const action: HealthActionInfo = { title: title.trim() };
    const start = readUnixSeconds(item.action_start_time);
    const end = readUnixSeconds(item.action_end_time);
    if (start !== undefined) action.actionStartTime = start;
    if (end !== undefined) action.actionEndTime = end;
    result.push(action);
  }
  return result;
}

/** Map một dòng TikTok Growth Center sang payload sync. */
export function mapHealthRecord(
  raw: unknown,
  recordType: 'violation' | 'warning',
): SyncHealthRecordItem | null {
  if (!raw || typeof raw !== 'object') return null;
  const row = raw as Record<string, unknown>;
  const recordId = String(row.record_id ?? '').trim();
  if (!recordId) return null;

  const staticExpr = row.static_expression;
  const expr =
    staticExpr && typeof staticExpr === 'object'
      ? (staticExpr as Record<string, unknown>)
      : {};

  const state = row.state;
  const recordStatus =
    state && typeof state === 'object'
      ? Number((state as { record_status?: unknown }).record_status ?? 0)
      : 0;

  const createTime = Number(row.create_time ?? 0);

  return {
    recordId,
    recordType,
    reason: typeof expr.reason === 'string' ? expr.reason.trim() : '',
    detail: stripHealthHtml(typeof expr.detail === 'string' ? expr.detail : ''),
    categoryType: typeof expr.type === 'string' ? expr.type.trim() : '',
    actions: readActionInformations(staticExpr),
    recordStatus: Number.isFinite(recordStatus) ? recordStatus : 0,
    createTime: Number.isFinite(createTime) ? createTime : 0,
  };
}
