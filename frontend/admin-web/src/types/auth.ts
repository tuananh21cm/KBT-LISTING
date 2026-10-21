export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
}

export interface CurrentUser {
  id: string;
  username: string;
  email: string;
  fullName: string;
  roles: string[];
  permissions: string[];
  theme?: 'light' | 'dark';
  hasFourSellerCookie?: boolean;
  fourSellerCookieLoginStatus?: string;
}

export interface UpdateThemeRequest {
  theme: 'light' | 'dark';
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface RefreshTokenRequest {
  refreshToken: string;
}

export interface UserSummary {
  id: string;
  userName: string;
  fullName: string;
  email?: string;
  isActive: boolean;
}
