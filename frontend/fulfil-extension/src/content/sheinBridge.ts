// Isolated-world content script — document_start trên *.shein.com.
// Nhận postMessage từ capture.js (MAIN) sớm hơn content.js (document_idle),
// đồng thời flush sessionStorage bridge nếu body được ghi trước khi listener sẵn sàng.

const SHEIN_CAPTURE_KEY = 'kb_fulfil_shein_capture';
const SHEIN_CAPTURE_MSG = 'kb-fulfil-shein-capture';
const BRIDGE_KEY = 'kb_fulfil_shein_capture_bridge';

interface SheinCaptureState {
  headers: Record<string, string>;
  listUrl?: string;
  detailUrl?: string;
  listMethod?: string;
  listBody?: string;
  detailMethod?: string;
  detailBody?: string;
  accountEmail?: string;
  accountId?: string;
  at: number;
}

interface SheinCapturePayload {
  source: typeof SHEIN_CAPTURE_MSG;
  url: string;
  kind: 'list' | 'detail' | 'other';
  method?: string;
  headers: Record<string, string>;
  body?: string;
  accountEmail?: string;
  accountId?: string;
  at: number;
}

function isPayload(data: unknown): data is SheinCapturePayload {
  return (
    !!data &&
    typeof data === 'object' &&
    (data as { source?: unknown }).source === SHEIN_CAPTURE_MSG
  );
}

function isSheinListApiUrl(url: string): boolean {
  if (/\/user\/orders?\b/i.test(url)) return false;
  return (
    /\/bff-api\/order\/(?:list|order_list|get_order_list|query_list)\b/i.test(url) ||
    /\/(?:bff-api\/)?api\/orders?(?:\/base)?\/(?:list|order_list|get_order_list|query_list)\b/i.test(
      url,
    )
  );
}

function mergeState(
  prev: SheinCaptureState | undefined,
  data: SheinCapturePayload | SheinCaptureState,
): SheinCaptureState {
  const base: SheinCaptureState = prev ?? { headers: {}, at: 0 };
  const kind = 'kind' in data ? data.kind : undefined;
  const method = 'method' in data ? data.method : undefined;
  const body = 'body' in data && typeof data.body === 'string' ? data.body : undefined;
  const url = 'url' in data && typeof data.url === 'string' ? data.url : undefined;
  const listFromPayload =
    kind === 'list' && url && isSheinListApiUrl(url)
      ? url
      : 'listUrl' in data && data.listUrl && isSheinListApiUrl(data.listUrl)
        ? data.listUrl
        : undefined;
  const prevList =
    base.listUrl && isSheinListApiUrl(base.listUrl) ? base.listUrl : undefined;

  return {
    headers: { ...base.headers, ...(data.headers || {}) },
    listUrl: listFromPayload || prevList,
    detailUrl:
      kind === 'detail' && url ? url : ('detailUrl' in data && data.detailUrl) || base.detailUrl,
    listMethod:
      kind === 'list' && method
        ? method
        : ('listMethod' in data && data.listMethod) || base.listMethod,
    listBody:
      kind === 'list' && body
        ? body
        : ('listBody' in data && data.listBody) || base.listBody,
    detailMethod:
      kind === 'detail' && method
        ? method
        : ('detailMethod' in data && data.detailMethod) || base.detailMethod,
    detailBody:
      kind === 'detail' && body
        ? body
        : ('detailBody' in data && data.detailBody) || base.detailBody,
    accountEmail:
      ('accountEmail' in data && data.accountEmail) || base.accountEmail,
    accountId: ('accountId' in data && data.accountId) || base.accountId,
    at: typeof data.at === 'number' ? data.at : base.at,
  };
}

async function persist(next: SheinCaptureState): Promise<void> {
  try {
    if (!chrome?.runtime?.id) return;
    const raw = await chrome.storage.local.get(SHEIN_CAPTURE_KEY);
    const prev = raw[SHEIN_CAPTURE_KEY] as SheinCaptureState | undefined;
    // mergeState với prev dùng shape state (không có kind) — gộp thủ công
    const pickListUrl = (...candidates: Array<string | undefined>) =>
      candidates.find((u) => !!u && isSheinListApiUrl(u));
    const merged: SheinCaptureState = {
      headers: { ...(prev?.headers || {}), ...(next.headers || {}) },
      listUrl: pickListUrl(next.listUrl, prev?.listUrl),
      detailUrl: next.detailUrl || prev?.detailUrl,
      listMethod: next.listMethod || prev?.listMethod,
      listBody: next.listBody || prev?.listBody,
      detailMethod: next.detailMethod || prev?.detailMethod,
      detailBody: next.detailBody || prev?.detailBody,
      accountEmail: next.accountEmail || prev?.accountEmail,
      accountId: next.accountId || prev?.accountId,
      at: Math.max(next.at || 0, prev?.at || 0),
    };
    await chrome.storage.local.set({ [SHEIN_CAPTURE_KEY]: merged });
  } catch {
    // ignore (extension reload / context invalidated)
  }
}

async function flushBridge(): Promise<boolean> {
  try {
    const raw = sessionStorage.getItem(BRIDGE_KEY);
    if (!raw) return false;
    const bridge = JSON.parse(raw) as SheinCaptureState;
    if (!bridge || typeof bridge !== 'object') return false;
    await persist(bridge);
    return !!(bridge.listBody && String(bridge.listBody).length > 0);
  } catch {
    return false;
  }
}

window.addEventListener('message', (event) => {
  if (event.source !== window) return;
  if (!isPayload(event.data)) return;
  void persist(mergeState(undefined, event.data));
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type !== 'FLUSH_SHEIN_CAPTURE') return false;
  void flushBridge()
    .then((hasBody) => sendResponse({ ok: true, hasBody }))
    .catch(() => sendResponse({ ok: false, hasBody: false }));
  return true;
});

void flushBridge();
setTimeout(() => void flushBridge(), 500);
setTimeout(() => void flushBridge(), 1500);
setTimeout(() => void flushBridge(), 4000);
