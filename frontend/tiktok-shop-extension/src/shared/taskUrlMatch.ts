/**
 * So khớp URL đang mở với nhiệm vụ truy cập URL.
 *
 * Bản sao đúng luật của `ShopTaskStatusRules.MatchesTargetUrl` phía backend: chuẩn hoá về
 * `scheme://host + path` (bỏ query, fragment và dấu `/` cuối) rồi so prefix không phân biệt hoa
 * thường. Extension chỉ dùng để **lọc** xem có đáng gọi API báo về hay không — quyền quyết định
 * vẫn ở backend, nên hai bên lệch nhau cũng không ghi nhầm trạng thái.
 */

/** Chuẩn hoá URL về `scheme://host/path`, bỏ query/fragment và `/` cuối. Không parse được → ''. */
export function normalizeTaskUrl(value: string | null | undefined): string {
  const raw = (value ?? '').trim();
  if (!raw) return '';
  try {
    const url = new URL(raw);
    return `${url.protocol}//${url.host}${url.pathname}`.replace(/\/+$/, '');
  } catch {
    return '';
  }
}

/**
 * Trang hiện tại đã tới đúng nơi nhiệm vụ yêu cầu chưa.
 *
 * `pattern` là prefix backend gửi kèm (mặc định = origin + path của URL đích), nên một nhiệm vụ
 * trỏ tới trang danh sách vẫn tính là xong khi TikTok thêm query lọc hoặc điều hướng sâu hơn
 * trong cùng nhánh path. Đổi host hoặc sang path khác thì không tính.
 */
export function matchesTaskUrl(
  currentUrl: string | null | undefined,
  targetUrl: string | null | undefined,
  pattern?: string | null,
): boolean {
  const expected = (pattern ?? '').trim()
    ? (pattern ?? '').trim().replace(/\/+$/, '')
    : normalizeTaskUrl(targetUrl);
  if (!expected) return false;

  const actual = normalizeTaskUrl(currentUrl);
  if (!actual) return false;

  return actual.toLowerCase().startsWith(expected.toLowerCase());
}

/** Độ dài prefix so khớp — pattern dài hơn = nhiệm vụ cụ thể hơn. */
export function taskUrlMatchLength(
  targetUrl: string | null | undefined,
  pattern?: string | null,
): number {
  const expected = (pattern ?? '').trim()
    ? (pattern ?? '').trim().replace(/\/+$/, '')
    : normalizeTaskUrl(targetUrl);
  return expected.length;
}

/**
 * Khi nhiều nhiệm vụ cùng khớp prefix (vd. host rộng vs path cụ thể), chọn pattern dài nhất
 * để spinner / dwell không dính lên dòng đứng trước trong list.
 */
export function findBestMatchingTask<
  T extends { done?: boolean; url: string; urlMatchPattern?: string },
>(items: readonly T[] | null | undefined, currentUrl: string): T | undefined {
  let best: T | undefined;
  let bestLen = -1;
  for (const item of items ?? []) {
    if (item.done) continue;
    if (!matchesTaskUrl(currentUrl, item.url, item.urlMatchPattern)) continue;
    const len = taskUrlMatchLength(item.url, item.urlMatchPattern);
    if (len > bestLen) {
      best = item;
      bestLen = len;
    }
  }
  return best;
}
