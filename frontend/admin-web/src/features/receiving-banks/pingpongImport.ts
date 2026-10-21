import * as XLSX from 'xlsx';

/** One mapped row from a PingPongX Global Account export. */
export interface ImportCardRow {
  rowIndex: number;
  accountNumber: string;
  bankName: string;
  bankLocation?: string;
  accountHolderName: string;
  accountType?: string;
  swiftBic?: string;
  routingNumber?: string;
  bankAddress?: string;
  currency?: string;
  isActive: boolean;
  /** Parse/validation error — row skipped on submit. */
  error?: string;
}

const HEADER_ALIASES: Record<string, string[]> = {
  accountNumber: ['bank account number', 'bank account no', 'account number'],
  iban: ['iban'],
  bankName: ['bank name'],
  bankLocation: ['bank location'],
  accountHolderName: ["account holder's name", 'account holder name', 'account holder'],
  accountType: ['account type'],
  swiftBic: ['swift code/bic', 'swift/bic', 'swift code', 'swift', 'bic'],
  routingNumber: ['9-digit routing number', 'routing number', 'routing no'],
  bankAddress: ['bank address'],
  city: ['city'],
  zipCode: ['zip code', 'zipcode', 'postal code'],
  currency: ['currency'],
  status: ['status'],
};

const normalizeHeader = (value: unknown): string =>
  String(value ?? '')
    .trim()
    .toLowerCase()
    .replace(/\s+/g, ' ');

const cellText = (value: unknown): string => {
  if (value == null) return '';
  if (typeof value === 'number' && Number.isFinite(value)) {
    // Long account numbers as IEEE floats lose precision; prefer integer-like string.
    if (Number.isInteger(value) && Math.abs(value) < Number.MAX_SAFE_INTEGER) {
      return String(value);
    }
    // Still stringify — caller should prefer text-formatted Excel cells.
    return String(value);
  }
  return String(value).trim();
};

/** Normalize account number for duplicate matching (trim + collapse spaces). */
export const normalizeAccountNumber = (value: string): string =>
  value.trim().replace(/\s+/g, '');

const resolveColumnMap = (headers: unknown[]): Map<string, number> | null => {
  const normalized = headers.map(normalizeHeader);
  const map = new Map<string, number>();

  for (const [field, aliases] of Object.entries(HEADER_ALIASES)) {
    const idx = normalized.findIndex((h) => aliases.includes(h));
    if (idx >= 0) map.set(field, idx);
  }

  const hasAccount =
    map.has('accountNumber') || map.has('iban');
  if (!hasAccount || !map.has('bankName') || !map.has('accountHolderName')) {
    return null;
  }
  return map;
};

const joinAddress = (address: string, city: string, zip: string): string | undefined => {
  const parts = [address, city, zip].filter(Boolean);
  return parts.length > 0 ? parts.join(', ') : undefined;
};

const parseStatusActive = (raw: string): boolean => {
  if (!raw) return true;
  const v = raw.toLowerCase();
  if (v === 'inactive' || v === 'disabled' || v === 'off' || v === 'closed') return false;
  return true;
};

/**
 * Parse a PingPongX Global Account `.xlsx` / `.xls` ArrayBuffer into mapped rows.
 * Throws if the sheet is not recognizable PingPongX format.
 */
export function parsePingPongGlobalAccountFile(buffer: ArrayBuffer): ImportCardRow[] {
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
      'Không nhận diện được format PingPongX Global Account. Cần cột Bank Account number, Bank Name, Account holder\'s name.',
    );
  }

  const get = (row: (string | number | null | undefined)[], field: string): string => {
    const idx = columnMap.get(field);
    if (idx == null) return '';
    return cellText(row[idx]);
  };

  const rows: ImportCardRow[] = [];

  for (let i = 1; i < matrix.length; i++) {
    const raw = matrix[i] ?? [];
    // Skip fully empty rows
    if (raw.every((c) => cellText(c) === '')) continue;

    const accountNumberRaw = get(raw, 'accountNumber') || get(raw, 'iban');
    const accountNumber = normalizeAccountNumber(accountNumberRaw);
    const bankName = get(raw, 'bankName');
    const accountHolderName = get(raw, 'accountHolderName');
    const bankLocation = get(raw, 'bankLocation') || undefined;
    const accountType = get(raw, 'accountType') || undefined;
    const swiftBic = get(raw, 'swiftBic') || undefined;
    const routingNumber = get(raw, 'routingNumber') || undefined;
    const currency = get(raw, 'currency') || undefined;
    const bankAddress = joinAddress(
      get(raw, 'bankAddress'),
      get(raw, 'city'),
      get(raw, 'zipCode'),
    );
    const isActive = parseStatusActive(get(raw, 'status'));

    const missing: string[] = [];
    const problems: string[] = [];
    if (!accountNumber) missing.push('Bank Account number');
    if (!bankName) missing.push('Bank Name');
    if (!accountHolderName) missing.push("Account holder's name");
    if (accountNumber && /e[+-]/i.test(accountNumberRaw)) {
      problems.push('Account number bị scientific notation (xuất lại dạng Text)');
    }

    const errorParts = [
      missing.length > 0 ? `Thiếu: ${missing.join(', ')}` : null,
      ...problems,
    ].filter(Boolean);

    rows.push({
      rowIndex: i + 1,
      accountNumber,
      bankName,
      bankLocation,
      accountHolderName,
      accountType,
      swiftBic,
      routingNumber,
      bankAddress,
      currency,
      isActive,
      error: errorParts.length > 0 ? errorParts.join('; ') : undefined,
    });
  }

  if (rows.length === 0) {
    throw new Error('Không có dòng dữ liệu hợp lệ trong file.');
  }

  return rows;
}
