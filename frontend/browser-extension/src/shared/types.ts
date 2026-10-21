/** Trạng thái đăng nhập kinhbac lưu trong storage đến khi Đăng xuất hoặc gỡ extension. */
export interface AuthState {
  accessToken: string;
  refreshToken: string;
  /** Thời điểm access token hết hạn (ISO). */
  expiresAt: string;
  /** Thời điểm lưu phiên sau đăng nhập thành công (ISO) — audit, không dùng cắt hạn. */
  savedAt: string;
  user: {
    id: string;
    username: string;
    fullName?: string;
    /** Theme tài khoản (`GET /auth/me`). */
    theme?: 'light' | 'dark';
  } | null;
}

/** Nguồn sản phẩm (từ /product-sources/options). */
export interface ProductSourceOption {
  id: string;
  code: string;
  name: string;
  logoUrl?: string;
  /** Host patterns phân tách bằng dấu phẩy/xuống dòng. */
  domains?: string;
}

/** Platform bán hàng (từ /platforms/options). */
export interface PlatformOption {
  id: string;
  code: string;
  name: string;
  avatarUrl?: string;
}

/** Trạng thái cookie 4Seller. */
export interface FourSellerStatus {
  hasCookie: boolean;
  loginStatus: 'none' | 'unchecked' | 'logged_in' | 'expired' | string;
  /** Kết quả is-login lần gần nhất. */
  isLoggedIn: boolean;
  message?: string;
  checkedAt?: string;
}

/** Một dòng draft / published hiển thị trong popup. */
export interface DraftItem {
  id: string;
  name: string;
  image?: string;
  shopName?: string;
  platform?: string;
  pageType?: string;
  siteStatus?: string | null;
  sourceUrl?: string;
  /** 4Seller shop id — unmark đúng shop khi xóa draft. */
  fourSellerShopId?: number;
  createTime?: string;
  /** Giá nhập thấp (cost / InitialPrice) từ listing draft local. */
  lowPrice?: number | null;
  /** Giá nhập cao (cost / InitialPrice) từ listing draft local. */
  highPrice?: number | null;
  /** Icon nguồn sản phẩm (LogoUrl khớp domain). */
  sourceLogoUrl?: string;
  sourceName?: string;
  /** Số đánh giá nguồn (Shein comment_num). */
  reviewCount?: number | null;
  /** Điểm TB 0–5 (Shein comment_rank_average). */
  averageRating?: number | null;
  storeLogo?: string | null;
  storeTitle?: string | null;
  storeUrl?: string | null;
  shopRank?: number | null;
  storeAttention?: number | null;
  shippingDays?: number | null;
  shippingDayPercent?: number | null;
  sourceStock?: number | null;
  isLocalProduct?: boolean | null;
  isSupportQuickShip?: boolean | null;
}

/** Shop option cho KBT Craper (local Active + 4Seller id). */
export interface FsShopOption {
  /** 4Seller shop id (postHtml shopIdList). */
  id: number;
  /** Local shop Guid. */
  localId?: string;
  platform?: string;
  /** Browser profile name gắn shop (Shop.ProfileName). */
  profileName?: string;
  /** Tên shop (Shop.Name). */
  shopName?: string;
  /** Region / marketplace (vd. US). */
  marketplaceId?: string;
  platformAvatarUrl?: string;
  /** TTS-synced product categories. */
  productCategories?: { label: string; value: string }[];
  categories?: { id: string; name: string }[];
  categoryId?: string;
  categoryName?: string;
  ageMin?: number | null;
  ageMax?: number | null;
  priceMinUsd?: number | null;
  priceMaxUsd?: number | null;
  preferQuickShip?: boolean;
  preferLocal?: boolean;
  preferUsWarehouse?: boolean;
  preferSheinClub?: boolean;
  minRating?: number;
  minSoldCount?: number;
  maxVariantCount?: number;
  bodySizeCode?: string | null;
  styleCodes?: string[];
  /** Listing siteStatus=active. */
  activeCount?: number;
}
