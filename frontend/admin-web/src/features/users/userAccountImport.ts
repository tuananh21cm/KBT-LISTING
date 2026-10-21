import * as XLSX from 'xlsx';

/** Một dòng tài khoản đã map từ file Excel. */
export interface ImportUserRow {
  /** Số dòng trong file Excel (1-based, tính cả hàng tiêu đề). */
  rowIndex: number;
  userName: string;
  password: string;
  fullName: string;
  email: string;
  /** Lỗi parse/validate — dòng bị bỏ qua khi gửi lên server. */
  error?: string;
}

/** Domain gắn vào email tự sinh từ tên đăng nhập khi file thiếu cột Email. */
const GENERATED_EMAIL_DOMAIN = 'kbt.global';

/** Mô tả định dạng file hiển thị trong modal import. */
export const IMPORT_FORMAT_HINT =
  'Dòng đầu là tiêu đề, mỗi dòng sau là một tài khoản với các cột: Tên đăng nhập, Mật khẩu, Họ tên, Email (tuỳ chọn). ' +
  'Thiếu Email thì hệ thống tự sinh `{tên đăng nhập}@kbt.global`. ' +
  'Mật khẩu lấy đúng theo file — tối thiểu 8 ký tự, gồm chữ hoa, chữ thường và số (không bắt buộc ký tự đặc biệt).';

const HEADER_ALIASES: Record<keyof Omit<ImportUserRow, 'rowIndex' | 'error'>, string[]> = {
  userName: ['tên đăng nhập', 'ten dang nhap', 'tendangnhap', 'username', 'user name', 'tài khoản', 'tai khoan'],
  password: ['mật khẩu', 'mat khau', 'matkhau', 'password', 'pass'],
  fullName: ['họ tên', 'ho ten', 'hoten', 'họ và tên', 'ho va ten', 'full name', 'fullname', 'tên', 'name'],
  email: ['email', 'e-mail', 'mail', 'địa chỉ email', 'dia chi email'],
};

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Sinh email từ tên đăng nhập khi file không có email.
 * Local-part giữ chữ/số/`.`/`_`/`+`/`-`; ký tự khác bị bỏ để luôn hợp lệ.
 */
export const emailFromUserName = (userName: string): string => {
  const local = userName.trim().replace(/[^a-zA-Z0-9._+-]/g, '');
  return local ? `${local}@${GENERATED_EMAIL_DOMAIN}` : '';
};

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

/** Khớp cùng bộ rule với `PasswordValidationRules` ở backend để báo lỗi ngay trên preview. */
const passwordProblem = (password: string): string | null => {
  if (!password) return 'Thiếu Mật khẩu';
  if (password.length < 8) return 'Mật khẩu phải có ít nhất 8 ký tự';
  if (!/[A-Z]/.test(password)) return 'Mật khẩu phải có ít nhất một chữ hoa';
  if (!/[a-z]/.test(password)) return 'Mật khẩu phải có ít nhất một chữ thường';
  if (!/[0-9]/.test(password)) return 'Mật khẩu phải có ít nhất một chữ số';
  return null;
};

const resolveColumnMap = (headers: unknown[]): Map<string, number> | null => {
  const normalized = headers.map(normalizeHeader);
  const map = new Map<string, number>();

  for (const [field, aliases] of Object.entries(HEADER_ALIASES)) {
    const idx = normalized.findIndex((h) => aliases.includes(h));
    if (idx >= 0) map.set(field, idx);
  }

  if (!map.has('userName') || !map.has('password')) return null;
  return map;
};

/**
 * Parse file `.xlsx` / `.xls` danh sách tài khoản thành các dòng đã map.
 * Throw khi sheet không có cột Tên đăng nhập / Mật khẩu.
 */
export function parseUserAccountFile(buffer: ArrayBuffer): ImportUserRow[] {
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
    throw new Error('Không nhận diện được tiêu đề. Cần có cột Tên đăng nhập và Mật khẩu.');
  }

  const get = (row: (string | number | null | undefined)[], field: string): string => {
    const idx = columnMap.get(field);
    if (idx == null) return '';
    return cellText(row[idx]);
  };

  const rows: ImportUserRow[] = [];
  const seenUserNames = new Set<string>();
  const seenEmails = new Set<string>();

  for (let i = 1; i < matrix.length; i++) {
    const raw = matrix[i] ?? [];
    if (raw.every((c) => cellText(c) === '')) continue;

    const userName = get(raw, 'userName');
    const password = get(raw, 'password');
    const fullName = get(raw, 'fullName');
    const rawEmail = get(raw, 'email');
    // Thiếu email → sinh từ tên đăng nhập; mật khẩu luôn lấy đúng từ file (không liên quan email).
    const email = rawEmail || (userName ? emailFromUserName(userName) : '');

    const problems: string[] = [];
    if (!userName) problems.push('Thiếu Tên đăng nhập');
    if (!fullName) problems.push('Thiếu Họ tên');
    if (!email) {
      problems.push('Không tạo được Email từ Tên đăng nhập');
    } else if (!EMAIL_PATTERN.test(email)) {
      problems.push('Email không hợp lệ');
    }
    const passwordError = passwordProblem(password);
    if (passwordError) problems.push(passwordError);

    // Trùng trong cùng file → giữ dòng đầu, chặn dòng sau để không ghi đè hai lần.
    const userNameKey = userName.toLowerCase();
    if (userName) {
      if (seenUserNames.has(userNameKey)) {
        problems.push('Tên đăng nhập bị lặp trong file');
      } else {
        seenUserNames.add(userNameKey);
      }
    }
    const emailKey = email.toLowerCase();
    if (email) {
      if (seenEmails.has(emailKey)) {
        problems.push('Email bị lặp trong file');
      } else {
        seenEmails.add(emailKey);
      }
    }

    rows.push({
      rowIndex: i + 1,
      userName,
      password,
      fullName,
      email,
      error: problems.length > 0 ? problems.join('; ') : undefined,
    });
  }

  if (rows.length === 0) {
    throw new Error('Không có dòng dữ liệu nào trong file.');
  }

  return rows;
}
