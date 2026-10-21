/**
 * Builds a SHEIN sourcing query from a TikTok trending keyword plus shop body/size.
 * Extension Search prefixes `curve` (shop code vẫn `curvy`). BE / Trending keywords
 * vẫn dùng `curvy` — không đổi AudienceSearchQuery.cs trong lần này.
 */

const PLUS = 'plus';
const PETITE = 'petite';
const TALL = 'tall';
const CURVY = 'curvy';
const CURVE = 'curve';

export function normalizeAudienceKeyword(raw?: string | null): string {
  if (!raw?.trim()) return '';
  const cleaned = raw.trim().toLowerCase().replace(/[\n\r]/g, ' ').replace(/\//g, ' ');
  let out = '';
  let prevSpace = false;
  for (const ch of cleaned) {
    if (/[a-z0-9]/.test(ch)) {
      out += ch;
      prevSpace = false;
    } else if (ch === "'" || ch === '-') {
      if (!out || prevSpace) continue;
      out += ch;
      prevSpace = false;
    } else if (/\s/.test(ch)) {
      if (!prevSpace && out) {
        out += ' ';
        prevSpace = true;
      }
    }
  }
  return out.replace(/^[-']+|[-']+$/g, '').trim();
}

export function buildAudienceSearchQuery(
  keyword?: string | null,
  bodySizeCode?: string | null,
): string {
  const query = normalizeAudienceKeyword(keyword);
  if (!query) return '';
  const modifier = audienceModifier(bodySizeCode);
  if (!modifier) return query;
  if (hasConflictingSize(query, bodySizeCode) || hasModifier(query, bodySizeCode)) return query;
  return `${modifier} ${query}`;
}

/** Cụm prefix câu tìm Shein (`plus` → `plus size`, `curvy` → `curve`). */
export function audienceModifier(bodySizeCode?: string | null): string | null {
  switch ((bodySizeCode ?? '').trim().toLowerCase()) {
    case PLUS:
      return 'plus size';
    case PETITE:
      return 'petite';
    case TALL:
      return 'tall';
    case CURVY:
    case CURVE:
      return 'curve';
    default:
      return null;
  }
}

function hasModifier(query: string, bodySizeCode?: string | null): boolean {
  switch ((bodySizeCode ?? '').trim().toLowerCase()) {
    case PLUS:
      return hasPlusSize(query);
    case PETITE:
      return hasToken(query, 'petite');
    case TALL:
      return hasToken(query, 'tall');
    case CURVY:
    case CURVE:
      return hasToken(query, 'curve') || hasToken(query, 'curvy');
    default:
      return false;
  }
}

function hasConflictingSize(query: string, bodySizeCode?: string | null): boolean {
  switch ((bodySizeCode ?? '').trim().toLowerCase()) {
    case PLUS:
      return hasToken(query, 'petite') || hasToken(query, 'tall');
    case PETITE:
      return hasPlusSize(query) || hasToken(query, 'tall');
    case TALL:
      return hasPlusSize(query) || hasToken(query, 'petite');
    case CURVY:
    case CURVE:
      return hasToken(query, 'petite');
    default:
      return false;
  }
}

function hasPlusSize(query: string): boolean {
  return spaced(query).includes('plus size');
}

function hasToken(query: string, token: string): boolean {
  return spaced(query)
    .split(' ')
    .filter(Boolean)
    .some((part) => part === token);
}

function spaced(query: string): string {
  return query.replace(/-/g, ' ').replace(/ {2,}/g, ' ');
}
