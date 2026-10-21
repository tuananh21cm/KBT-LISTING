/**
 * Sort helper shared by Shops list ("Theo tên Profile") and dashboard shop heatmap.
 *
 * Splits a profile name into prefix + trailing integer:
 * `KBT_SHOP_041` / `KBT-SHOP 41` → prefix `kbtshop` + index `41`.
 * Separators (`_`, `-`, `.`, whitespace) are stripped from the prefix so
 * alternate spellings of the same series compare equal; leading zeros on the
 * index are ignored via numeric parse.
 */

export type ProfileIndexParts = {
  isEmpty: boolean;
  prefix: string;
  index: number | null;
};

export const splitProfileIndex = (profileName?: string | null): ProfileIndexParts => {
  const name = (profileName ?? '').trim();
  const match = /^(.*?)(\d+)$/.exec(name);
  const rawPrefix = match?.[1] ?? name;
  return {
    isEmpty: !name,
    prefix: rawPrefix.toLowerCase().replace(/[\s_\-.]+/g, ''),
    index: match ? Number(match[2]) : null,
  };
};

/**
 * Compare two profile names (ascending when `dir === 1`).
 * Empty names always sink to the end, regardless of direction.
 */
export const compareProfileNames = (
  a?: string | null,
  b?: string | null,
  dir: 1 | -1 = 1,
): number => {
  const left = splitProfileIndex(a);
  const right = splitProfileIndex(b);
  if (left.isEmpty !== right.isEmpty) return left.isEmpty ? 1 : -1;
  const byPrefix = left.prefix.localeCompare(right.prefix, 'vi');
  if (byPrefix !== 0) return dir * byPrefix;
  if (left.index !== right.index) {
    if (left.index === null) return 1;
    if (right.index === null) return -1;
    return dir * (left.index - right.index);
  }
  return 0;
};
