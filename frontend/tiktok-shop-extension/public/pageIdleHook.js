/**
 * MAIN world: đếm fetch/XHR của trang seller-us (isolated content không thấy fetch của page).
 * Ignore list phải khớp `src/shared/sellerPageIdle.ts`.
 */
(() => {
  const w = window;
  if (w.__kbTtsPageIdleHook) return;
  w.__kbTtsPageIdleHook = true;

  const IGNORED =
    /slardar|\/monitor\/|\/tea\/|\/mcs\/|hotpot|\/web\/report|\/logs?\/|collect\b|beacon|\/gen_204|csp_report|sentry|starling|\/ping\/|heartbeat|\/metric/i;

  let inflight = 0;

  function trackable(url) {
    const raw = String(url || '').trim();
    if (!raw) return false;
    if (raw.indexOf('chrome-extension:') === 0 || raw.indexOf('data:') === 0 || raw.indexOf('blob:') === 0) {
      return false;
    }
    // Request do `sellerProductBridge.js` tự gọi không phải mạng của trang — đếm vào thì
    // logic page-ready của nhiệm vụ tưởng trang còn đang tải.
    if (w.__kbTtsForgedUrls && w.__kbTtsForgedUrls[raw]) return false;
    return !IGNORED.test(raw);
  }

  function resolveUrl(url) {
    try {
      return new URL(String(url || ''), location.href).href;
    } catch {
      return String(url || '');
    }
  }

  function post() {
    try {
      w.postMessage({ source: 'kb-tts-net', inflight }, '*');
    } catch {
      /* ignore */
    }
  }

  function bump(url, delta) {
    if (!trackable(url)) return;
    inflight = Math.max(0, inflight + delta);
    post();
  }

  const origFetch = w.fetch.bind(w);
  w.fetch = function kbTtsFetch(input, init) {
    let url = '';
    try {
      if (typeof input === 'string') url = input;
      else if (input && typeof input.url === 'string') url = input.url;
      else if (input instanceof URL) url = input.href;
    } catch {
      url = '';
    }
    url = resolveUrl(url);
    bump(url, 1);
    return origFetch(input, init).finally(() => bump(url, -1));
  };

  const origOpen = XMLHttpRequest.prototype.open;
  XMLHttpRequest.prototype.open = function kbTtsXhrOpen(method, url) {
    this.__kbTtsUrl = resolveUrl(url);
    return origOpen.apply(this, arguments);
  };

  const origSend = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.send = function kbTtsXhrSend() {
    const url = this.__kbTtsUrl || '';
    bump(url, 1);
    this.addEventListener('loadend', () => bump(url, -1), { once: true });
    return origSend.apply(this, arguments);
  };

  w.addEventListener('message', (event) => {
    if (event.source !== w) return;
    const data = event.data;
    if (!data || data.source !== 'kb-tts-net-ask') return;
    post();
  });

  post();
})();
