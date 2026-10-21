/** Persisted + UI status for 4Seller cookie login. */
export type CookieLoginStatus = 'none' | 'unchecked' | 'logged_in' | 'expired';

export interface AdminUser {
  id: string;
  userName: string;
  fullName: string;
  email?: string;
  isActive: boolean;
  roles: string[];
  createdAt: string;
  updatedAt?: string;
  hasFourSellerCookie: boolean;
  /** Persisted: none | unchecked | logged_in | expired */
  fourSellerCookieLoginStatus: CookieLoginStatus;
  /** Per-seller sticky egress proxy for 4Seller (null when not set). */
  fourSellerProxy?: string | null;
  /** Whether a Fulfil extension PIN is configured (value never returned). */
  hasFulfilPin: boolean;
}

export interface CreateAdminUserRequest {
  userName: string;
  email: string;
  fullName: string;
  password: string;
  roles: string[];
  fourSellerCookie?: string;
  fourSellerProxy?: string;
  /** Optional 4-digit Fulfil extension PIN (unique system-wide). */
  fulfilPin?: string;
}

export interface UpdateAdminUserRequest {
  email: string;
  fullName: string;
  isActive: boolean;
  roles: string[];
  /** null/undefined = keep; empty string = clear; non-empty = replace */
  fourSellerCookie?: string | null;
  /** null/undefined = keep; empty string = clear; non-empty = replace */
  fourSellerProxy?: string | null;
  /** null/undefined = keep; empty string = clear; 4 digits = replace */
  fulfilPin?: string | null;
}

export interface ImportAdminUserItem {
  userName: string;
  email: string;
  fullName: string;
  password: string;
  roles: string[];
}

export interface ImportAdminUsersRequest {
  items: ImportAdminUserItem[];
}

export interface ImportAdminUserError {
  /** Vị trí dòng trong mảng `items` đã gửi (bắt đầu từ 0). */
  index: number;
  userName: string;
  error: string;
}

export interface ImportAdminUsersResult {
  created: number;
  updated: number;
  failed: ImportAdminUserError[];
}

export interface ResetPasswordRequest {
  newPassword: string;
}

export interface RoleOption {
  id: string;
  name: string;
}

export interface FourSellerLoginCheckRequest {
  fourSellerCookie?: string;
}

export interface FourSellerLoginCheckResponse {
  isLoggedIn: boolean;
  message: string;
  hasFourSellerCookie?: boolean;
  fourSellerCookieLoginStatus?: CookieLoginStatus;
  cookieCleared?: boolean;
  /** True when EdgeOne blocked the request by IP (567) — session unverifiable, not expired. */
  blocked?: boolean;
  /** True when session could not be verified (proxy/timeout/5xx) — do not treat as expired. */
  unverified?: boolean;
}

export interface AdminFourSellerLoginCheckRequest {
  userIds?: string[];
}

export interface AdminFourSellerLoginCheckItem {
  userId: string;
  userName: string;
  fullName: string;
  hasCookie: boolean;
  isLoggedIn: boolean;
  fourSellerCookieLoginStatus?: CookieLoginStatus;
  cookieCleared?: boolean;
  blocked?: boolean;
  unverified?: boolean;
  message: string;
}
