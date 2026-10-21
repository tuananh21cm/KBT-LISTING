export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
}

/** Persisted 4Seller cookie login status. */
export type CookieLoginStatus = 'none' | 'unchecked' | 'logged_in' | 'expired';

export interface CurrentUser {
  id: string;
  username: string;
  email: string;
  fullName: string;
  roles: string[];
  permissions: string[];
  theme?: 'light' | 'dark';
  hasFourSellerCookie?: boolean;
  fourSellerCookieLoginStatus?: CookieLoginStatus;
}

export interface UpdateThemeRequest {
  theme: 'light' | 'dark';
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export interface UpdateFourSellerCookieRequest {
  fourSellerCookie?: string | null;
}

export interface FourSellerLoginCheckRequest {
  fourSellerCookie?: string | null;
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

export interface RefreshTokenRequest {
  refreshToken: string;
}
