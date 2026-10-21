/**
 * URL không tính vào «mạng đang tải trang» — telemetry / log của TikTok vẫn chạy sau khi UI xong.
 * Giữ đồng bộ với `public/pageIdleHook.js` (MAIN world, không import được module này).
 */
const IGNORED_NETWORK_URL =
  /slardar|\/monitor\/|\/tea\/|\/mcs\/|hotpot|\/web\/report|\/logs?\/|collect\b|beacon|\/gen_204|csp_report|sentry|starling|\/ping\/|heartbeat|\/metric/i;

export function shouldTrackSellerPageRequest(url: string): boolean {
  const raw = (url || '').trim();
  if (!raw) return false;
  if (raw.startsWith('chrome-extension:') || raw.startsWith('data:') || raw.startsWith('blob:')) {
    return false;
  }
  return !IGNORED_NETWORK_URL.test(raw);
}
