/**
 * Builds a SHEIN / paste sourcing query from a TikTok trending keyword plus shop body/size.
 * Keep in sync with AudienceSearchQuery.cs (backend).
 */

const PLUS = 'plus';
const PETITE = 'petite';
const TALL = 'tall';
const CURVY = 'curvy';

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

export function buildAudienceSearchQuery(keyword?: string | null, bodySizeCode?: string | null): string {
  const query = normalizeAudienceKeyword(keyword);
  if (!query) return '';
  const modifier = modifierFor(bodySizeCode);
  if (!modifier) return query;
  if (hasConflictingSize(query, bodySizeCode) || hasModifier(query, bodySizeCode)) return query;
  return `${modifier} ${query}`;
}

/**
 * Organic SHEIN US pdsearch — same formula as WhatToSellTodayService.BuildSheinSearchUrl.
 * Không `src_identifier` / `src_tab_page_id`: SHEIN đối chiếu chúng với log lượt xem list
 * của phiên đang mở, nên link mình tự dựng luôn khai sai và ăn `/risk/action/limit`.
 */
export function buildSheinSearchUrl(keywords: string): string {
  const kw = keywords.trim();
  const encoded = encodeURIComponent(kw);
  const ici = `s1\`RecentSearch\`${encoded}\`_fb\`d0\`PageOthers`;
  return (
    `https://us.shein.com/pdsearch/${encoded}/` +
    `?ici=${ici}` +
    `&search_source=1` +
    `&search_type=all` +
    `&source=historyWord` +
    `&src_module=search`
  );
}

export function isSameSearchQuery(keyword?: string | null, searchQuery?: string | null): boolean {
  return normalizeAudienceKeyword(keyword) === normalizeAudienceKeyword(searchQuery);
}

function modifierFor(bodySizeCode?: string | null): string | null {
  switch ((bodySizeCode ?? '').trim().toLowerCase()) {
    case PLUS:
      return 'plus size';
    case PETITE:
      return 'petite';
    case TALL:
      return 'tall';
    case CURVY:
      return 'curvy';
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
      return hasToken(query, 'curvy');
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
