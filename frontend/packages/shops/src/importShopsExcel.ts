import * as XLSX from 'xlsx';

/** Backend `NormalizeTikTokShopType` only persists these two values. */
const SHOP_TYPE_LLC = 'Llc';
const SHOP_TYPE_PER = 'Per';

/** One mapped row from a quick shop-import sheet. */
export interface ShopImportRow {
  /** 1-based sheet row (header = 1) for preview / error messages. */
  rowIndex: number;
  externalShopId: string;
  name: string;
  profileName: string;
  company: string;
  sellerUserName: string;
  /** Parse/validation error — row skipped on submit. */
  error?: string;
}

const HEADER_ALIASES: Record<keyof Omit<ShopImportRow, 'rowIndex' | 'error'>, string[]> = {
  externalShopId: [
    'id shop',
    'shop id',
    'id',
    'external shop id',
    'external id',
    'externalshopid',
    'id shop tiktok',
  ],
  name: ['ten shop', 'shop name', 'name', 'ten'],
  profileName: ['profile', 'profile name', 'ten profile', 'profilename'],
  company: ['company', 'llc', 'ten llc', 'llc name', 'company name', 'ten cong ty'],
  sellerUserName: ['seller username', 'username', 'seller', 'user name', 'tai khoan seller'],
};

/** Lowercase, strip diacritics, collapse spaces — header match is accent-insensitive. */
const normalizeHeader = (value: unknown): string =>
  String(value ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/gi, 'd')
    .trim()
    .toLowerCase()
    .replace(/\s+/g, ' ');

const cellText = (value: unknown): string => {
  if (value == null) return '';
  if (typeof value === 'number' && Number.isFinite(value)) {
    // Long TikTok shop ids as IEEE floats lose precision; prefer integer-like string.
    if (Number.isInteger(value) && Math.abs(value) < Number.MAX_SAFE_INTEGER) {
      return String(value);
    }
    return String(value);
  }
  return String(value).trim();
};

/** Match key for external shop id / shop name (trim + collapse spaces + lowercase). */
export const normalizeShopMatchKey = (value?: string | null): string =>
  (value ?? '').trim().replace(/\s+/g, ' ').toLowerCase();

/** Company filled → LLC shop, otherwise personal. */
export const resolveShopTypeFromCompany = (company?: string | null): string =>
  company && company.trim() ? SHOP_TYPE_LLC : SHOP_TYPE_PER;

const resolveColumnMap = (headers: unknown[]): Map<string, number> | null => {
  const normalized = headers.map(normalizeHeader);
  const map = new Map<string, number>();

  for (const [field, aliases] of Object.entries(HEADER_ALIASES)) {
    const idx = normalized.findIndex((h) => aliases.includes(h));
    if (idx >= 0) map.set(field, idx);
  }

  // Shop name is the only column every row must carry; without it nothing can be matched.
  if (!map.has('name')) return null;
  return map;
};

/**
 * Parse a quick shop-import `.xlsx` / `.xls` ArrayBuffer into mapped rows.
 * Throws if the sheet has no recognizable header.
 */
export function parseShopImportFile(buffer: ArrayBuffer): ShopImportRow[] {
  const workbook = XLSX.read(buffer, { type: 'array' });
  const sheetName = workbook.SheetNames[0];
  if (!sheetName) {
    throw new Error('File Excel không có sheet nào.');
  }

  const sheet = workbook.Sheets[sheetName];
  if (!sheet) {
    throw new Error('Không đọc được sheet Excel.');
  }

  const matrix = XLSX.utils.sheet_to_json<(string | number | null | undefined)[]>(sheet, {
    header: 1,
    defval: '',
    raw: false,
  });

  if (matrix.length < 2) {
    throw new Error('File Excel trống hoặc thiếu dữ liệu.');
  }

  const columnMap = resolveColumnMap(matrix[0] ?? []);
  if (!columnMap) {
    throw new Error(
      'Không nhận diện được header. Cần tối thiểu cột Tên shop; '
        + 'kèm External shop ID, Profile, Company, Seller username.',
    );
  }

  const get = (row: (string | number | null | undefined)[], field: string): string => {
    const idx = columnMap.get(field);
    if (idx == null) return '';
    return cellText(row[idx]);
  };

  const rows: ShopImportRow[] = [];

  for (let i = 1; i < matrix.length; i++) {
    const raw = matrix[i] ?? [];
    if (raw.every((c) => cellText(c) === '')) continue;

    const externalShopIdRaw = get(raw, 'externalShopId');
    const externalShopId = externalShopIdRaw.trim();
    const name = get(raw, 'name');
    const profileName = get(raw, 'profileName');
    const company = get(raw, 'company');
    const sellerUserName = get(raw, 'sellerUserName');

    const problems: string[] = [];
    if (!name) problems.push('Thiếu Tên shop');
    if (externalShopId && /e[+-]/i.test(externalShopIdRaw)) {
      problems.push('External shop ID bị scientific notation (xuất lại dạng Text)');
    }

    rows.push({
      rowIndex: i + 1,
      externalShopId,
      name,
      profileName,
      company,
      sellerUserName,
      error: problems.length > 0 ? problems.join('; ') : undefined,
    });
  }

  if (rows.length === 0) {
    throw new Error('Không có dòng dữ liệu hợp lệ trong file.');
  }

  return rows;
}

/** Canonical header labels shown in the import modal hint. */
export const SHOP_IMPORT_COLUMN_LABELS = {
  externalShopId: 'External shop ID',
  name: 'Tên shop',
  profileName: 'Profile',
  company: 'Company',
  sellerUserName: 'Seller username',
} as const;
