import * as XLSX from 'xlsx';

/** One mapped row from a PingPong virtual card export. */
export interface ImportFulfilCardRow {
  rowIndex: number;
  cardNumber: string;
  cardBrand: string;
  cvv?: string;
  expiryMonth?: number;
  expiryYear?: number;
  currency?: string;
  holderName?: string;
  /** `Account Name` từ file — dùng để khớp tài khoản nguồn, không lưu vào thẻ. */
  accountName?: string;
  isActive: boolean;
  /** Parse/validation error — row skipped on submit. */
  error?: string;
}

const HEADER_ALIASES: Record<string, string[]> = {
  cardNumber: ['card number', 'card no', 'card num', 'cardnumber'],
  cvv: ['cvv', 'cvc', 'cvv2'],
  expiry: ['expiration date', 'expiry date', 'expiration', 'expiry', 'valid thru'],
  currency: ['currency'],
  status: ['card status', 'status'],
  accountName: ['account name', 'account'],
  remark: ['remark', 'remarks', 'note'],
};

/** Giá trị placeholder PingPong dùng cho ô trống. */
const PLACEHOLDERS = new Set(['-', '--', '/', 'n/a', 'na', 'null']);

const normalizeHeader = (value: unknown): string =>
  String(value ?? '')
    .trim()
    .toLowerCase()
    .replace(/\s+/g, ' ');

const cellText = (value: unknown): string => {
  if (value == null) return '';
  if (typeof value === 'number' && Number.isFinite(value)) return String(value);
  return String(value).trim();
};

/** Bỏ placeholder `-` / `/` PingPong ghi khi ô trống. */
const meaningfulText = (value: string): string | undefined => {
  const trimmed = value.trim();
  if (!trimmed || PLACEHOLDERS.has(trimmed.toLowerCase())) return undefined;
  return trimmed;
};

/** Normalize số thẻ để so trùng — chỉ digits, khớp `NormalizeCardNumber` backend. */
export const normalizeCardNumber = (value: string): string => value.replace(/\D/g, '');

/** Backend chỉ nhận Visa / Master; file PingPong không có cột brand nên suy từ BIN. */
const resolveBrand = (cardNumber: string): string | null => {
  if (cardNumber.startsWith('4')) return 'Visa';
  if (cardNumber.startsWith('5') || cardNumber.startsWith('2')) return 'Master';
  return null;
};

const INACTIVE_STATUSES = [
  'cancel',
  'cancelled',
  'canceled',
  'frozen',
  'freeze',
  'closed',
  'close',
  'expired',
  'inactive',
  'disabled',
  'locked',
  'void',
  'terminated',
  'deleted',
];

const parseStatusActive = (raw: string): boolean => {
  const value = meaningfulText(raw)?.toLowerCase();
  if (!value) return true;
  return !INACTIVE_STATUSES.some((s) => value.includes(s));
};

/** `MM/YY`, `MM/YYYY`, `YYYY-MM`, `YYYY-MM-DD` → tháng + năm 4 số. */
const parseExpiry = (raw: string): { month?: number; year?: number; error?: string } => {
  const value = meaningfulText(raw);
  if (!value) return {};

  const parts = value.split(/[^\d]+/).filter(Boolean);
  if (parts.length < 2) return { error: 'Expiration Date không đọc được' };

  let monthText: string;
  let yearText: string;
  if (parts[0]!.length === 4) {
    yearText = parts[0]!;
    monthText = parts[1]!;
  } else {
    monthText = parts[0]!;
    yearText = parts[parts.length - 1]!;
  }

  const month = Number(monthText);
  let year = Number(yearText);
  if (yearText.length <= 2) year += 2000;

  if (!Number.isInteger(month) || month < 1 || month > 12) {
    return { error: 'Expiration Date không đọc được' };
  }
  if (!Number.isInteger(year) || year < 2000 || year > 2100) {
    return { error: 'Expiration Date không đọc được' };
  }
  return { month, year };
};

/** CVV Visa/Master luôn 3 số — Excel số hoá làm mất số 0 đầu. */
const normalizeCvv = (raw: string): string | undefined => {
  const value = meaningfulText(raw);
  if (!value) return undefined;
  if (/^\d{1,2}$/.test(value)) return value.padStart(3, '0');
  return value;
};

const resolveColumnMap = (headers: unknown[]): Map<string, number> | null => {
  const normalized = headers.map(normalizeHeader);
  const map = new Map<string, number>();

  for (const [field, aliases] of Object.entries(HEADER_ALIASES)) {
    const idx = normalized.findIndex((h) => aliases.includes(h));
    if (idx >= 0) map.set(field, idx);
  }

  if (!map.has('cardNumber')) return null;
  return map;
};

/**
 * Parse file `.xlsx` / `.xls` export thẻ ảo PingPong thành các dòng đã map.
 * Throw nếu sheet không phải format thẻ PingPong.
 */
export function parsePingPongCardFile(buffer: ArrayBuffer): ImportFulfilCardRow[] {
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
    throw new Error('Không nhận diện được format thẻ PingPong. Cần cột Card Number.');
  }

  const get = (row: (string | number | null | undefined)[], field: string): string => {
    const idx = columnMap.get(field);
    if (idx == null) return '';
    return cellText(row[idx]);
  };

  const rows: ImportFulfilCardRow[] = [];

  for (let i = 1; i < matrix.length; i++) {
    const raw = matrix[i] ?? [];
    if (raw.every((c) => cellText(c) === '')) continue;

    const cardNumberRaw = get(raw, 'cardNumber');
    const cardNumber = normalizeCardNumber(cardNumberRaw);
    const expiry = parseExpiry(get(raw, 'expiry'));
    const brand = cardNumber ? resolveBrand(cardNumber) : null;

    const problems: string[] = [];
    if (!cardNumber) {
      problems.push('Thiếu Card Number');
    } else if (/e[+-]/i.test(cardNumberRaw)) {
      problems.push('Card Number bị scientific notation (xuất lại dạng Text)');
    } else if (cardNumber.length < 12 || cardNumber.length > 19) {
      problems.push(`Card Number ${cardNumber.length} số (cần 12–19)`);
    } else if (!brand) {
      problems.push('Không xác định được loại thẻ (chỉ hỗ trợ Visa / Master)');
    }
    if (expiry.error) problems.push(expiry.error);

    rows.push({
      rowIndex: i + 1,
      cardNumber,
      cardBrand: brand ?? '',
      cvv: normalizeCvv(get(raw, 'cvv')),
      expiryMonth: expiry.month,
      expiryYear: expiry.year,
      currency: meaningfulText(get(raw, 'currency'))?.toUpperCase(),
      holderName: meaningfulText(get(raw, 'remark')),
      accountName: meaningfulText(get(raw, 'accountName')),
      isActive: parseStatusActive(get(raw, 'status')),
      error: problems.length > 0 ? problems.join('; ') : undefined,
    });
  }

  if (rows.length === 0) {
    throw new Error('Không có dòng dữ liệu hợp lệ trong file.');
  }

  return rows;
}
