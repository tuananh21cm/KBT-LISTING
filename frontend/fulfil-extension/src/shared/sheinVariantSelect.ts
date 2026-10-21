/**
 * Khớp / chọn size trên PDP SHEIN theo nhãn variant đơn.
 * Pure — dùng chung content script + unit test.
 */

export function normLabel(s: string): string {
  return s.replace(/\s+/g, '').toLowerCase();
}

/** Size token chuẩn (xl ≠ xxl, s ≠ xs). */
export function isSizeToken(n: string): boolean {
  return /^(?:xxs|xs|s|m|l|xl|xxl|[2-5]xl|onesize|freesize|\d{1,2}(?:\.\d)?)$/i.test(n);
}

/**
 * Lấy token size chuẩn từ nhãn SHEIN / variant.
 * Ví dụ: "12 (XL)" → xl, "XXL" → xxl, "Apricot, XXL" → xxl.
 */
export function canonicalSizeToken(raw: string): string | null {
  const s = raw.trim();
  if (!s) return null;

  const paren = s.match(/\(\s*([^)]+?)\s*\)/);
  if (paren?.[1]) {
    const inner = normLabel(paren[1]);
    if (isSizeToken(inner)) return inner.toLowerCase();
  }

  const n = normLabel(s);
  if (isSizeToken(n)) return n.toLowerCase();

  // Ưu tiên token dài (xxl trước xl) để không cắt nhầm từ "xxl".
  // Letter sizes: không bắt `s`/`m`/`l` đơn lẻ trong chuỗi dài (dễ false-positive).
  const m = n.match(/(?:[2-5]xl|xxl|xxs|onesize|freesize|xl|xs|\d{1,2}(?:\.\d)?)/i);
  if (!m?.[0]) return null;
  const tok = m[0].toLowerCase();
  return isSizeToken(tok) ? tok : null;
}

/** So khớp size: exact token — không dùng includes (tránh XL khớp XXL). */
export function sizesMatch(a: string, b: string): boolean {
  const ta = canonicalSizeToken(a);
  const tb = canonicalSizeToken(b);
  if (ta && tb) return ta === tb;
  return normLabel(a) === normLabel(b);
}

export interface SizeOptionFields {
  attrValueName?: string | null;
  ariaLabel?: string | null;
  text?: string | null;
  disabled?: boolean;
  soldOut?: boolean;
}

/**
 * Các khoá có thể khớp của một option size SHEIN.
 * Ví dụ "12 (XL)" / data-attr_value_name=XL → ["xl", "12", "12(xl)"].
 * "8/10 (L)" → ["l", "8", "10", "8/10", "8/10(l)"].
 */
export function sizeOptionKeys(opt: SizeOptionFields): string[] {
  const keys = new Set<string>();
  const push = (raw?: string | null) => {
    const s = raw?.trim();
    if (!s) return;
    const n = normLabel(s);
    if (n) keys.add(n);

    const tok = canonicalSizeToken(s);
    if (tok) keys.add(tok);

    const paren = s.match(/\(\s*([^)]+?)\s*\)/);
    if (paren?.[1]) {
      const inner = normLabel(paren[1]);
      if (inner) keys.add(inner);
      const innerTok = canonicalSizeToken(paren[1]);
      if (innerTok) keys.add(innerTok);
    }

    // Phần trước ngoặc: "12" hoặc "8/10"
    const beforeParen = s.replace(/\s*\([^)]*\)\s*$/, '').trim();
    if (beforeParen) {
      const bn = normLabel(beforeParen);
      if (bn) keys.add(bn);
      for (const part of beforeParen.split(/[\/|,;-]+/)) {
        const p = normLabel(part);
        if (p) keys.add(p);
      }
    }

    // data-attr_value_name thường là S/M/L/XL
    if (isSizeToken(n)) keys.add(n.toLowerCase());
  };

  push(opt.attrValueName);
  push(opt.ariaLabel);
  push(opt.text);
  return [...keys];
}

/** Want keys từ variantAttr / sizeLabel (vd. "Size:XL", "Black, XXL", "12"). */
export function sizeWantKeys(want: string): string[] {
  const s = want.trim();
  if (!s) return [];
  const keys = new Set<string>();

  const sizeEq = s.match(/size\s*[=:]\s*([^,/|;]+)/i);
  if (sizeEq?.[1]) {
    for (const k of sizeOptionKeys({ text: sizeEq[1].trim() })) keys.add(k);
  }

  // Bỏ prefix color nếu dạng "Color:Black / Size:XL" hoặc "Black, XXL"
  const withoutColor = s
    .replace(/colou?r\s*[=:][^,/|;]*/gi, '')
    .replace(/^[,;/|\s]+|[,;/|\s]+$/g, '')
    .trim();

  const parts = (withoutColor || s)
    .split(/[,|/]/)
    .map((p) => p.trim())
    .filter(Boolean);

  for (const part of parts) {
    // Bỏ qua phần rõ ràng là màu (không có size token và dài)
    const tok = canonicalSizeToken(part);
    if (tok) {
      keys.add(tok);
      for (const k of sizeOptionKeys({ text: part })) keys.add(k);
      continue;
    }
    if (isSizeToken(normLabel(part)) || /^\d{1,2}(?:\.\d)?$/.test(normLabel(part))) {
      for (const k of sizeOptionKeys({ text: part })) keys.add(k);
    }
  }

  // Fallback toàn chuỗi
  if (keys.size === 0) {
    for (const k of sizeOptionKeys({ text: s })) keys.add(k);
  }

  return [...keys];
}

function isLetterSizeToken(t: string): boolean {
  return /^(?:xxs|xs|s|m|l|xl|xxl|[2-5]xl|onesize|freesize)$/i.test(t);
}

function isNumericSizeToken(t: string): boolean {
  return /^\d{1,2}(?:\.\d)?$/.test(t);
}

/**
 * Chọn index option size khớp `want`.
 * Ưu tiên token chữ (XL ≠ XXL), sau đó khớp số. Bỏ disabled/soldOut.
 * Trả về -1 nếu không khớp.
 */
export function pickSizeOptionIndex(
  options: SizeOptionFields[],
  want: string,
): number {
  const wantKeys = sizeWantKeys(want);
  if (wantKeys.length === 0 || options.length === 0) return -1;

  const wantLetter = wantKeys.filter(isLetterSizeToken);
  const wantNumeric = wantKeys.filter(isNumericSizeToken);

  const eligible = options.map((opt, index) => ({ opt, index })).filter(({ opt }) => {
    if (opt.disabled || opt.soldOut) return false;
    return sizeOptionKeys(opt).length > 0;
  });

  // 1) Exact letter token
  if (wantLetter.length > 0) {
    for (const { opt, index } of eligible) {
      const keys = sizeOptionKeys(opt);
      if (wantLetter.some((w) => keys.includes(w))) return index;
    }
  }

  // 2) Exact numeric
  if (wantNumeric.length > 0) {
    for (const { opt, index } of eligible) {
      const keys = sizeOptionKeys(opt);
      if (wantNumeric.some((w) => keys.includes(w))) return index;
    }
  }

  // 3) Any remaining want key (normalized full label)
  const rest = wantKeys.filter((k) => !isLetterSizeToken(k) && !isNumericSizeToken(k));
  if (rest.length > 0) {
    for (const { opt, index } of eligible) {
      const keys = sizeOptionKeys(opt);
      if (rest.some((w) => keys.includes(w))) return index;
    }
  }

  return -1;
}
