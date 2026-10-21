import * as XLSX from 'xlsx';
import type { ShopListItem } from '@drop/types/shop';
import {
  SHOP_BANK_MATCH_LABELS,
  SHOP_STATUS_LABELS,
  SHOP_TYPE_LABELS,
} from '@drop/types/shop';
import type { ShopBankMatchStatus } from '@drop/types/shop';
import { formatDateTimeVn } from '@drop/utils/datetime';

const formatListingLimit = (shop: ShopListItem): string => {
  const published = shop.publishLimitPublished ?? shop.activeListingCount;
  const total = shop.publishLimitTotal;
  if (published == null && total == null) return '';
  return `${published ?? 0}/${total ?? '—'}`;
};

const formatSeller = (shop: ShopListItem): string => {
  const name = shop.ownerFullName?.trim();
  const user = shop.ownerUserName?.trim();
  if (name && user && name !== user) return `${name} (${user})`;
  return name || user || '';
};

/** Filename stamp in Vietnam local time: shops-YYYYMMDD-HHmm.xlsx */
const buildExportFileName = (now = new Date()): string => {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).formatToParts(now);
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((p) => p.type === type)?.value ?? '00';
  return `shops-${get('year')}${get('month')}${get('day')}-${get('hour')}${get('minute')}.xlsx`;
};

const formatBankMatchLabel = (shop: ShopListItem): string => {
  const status = shop.bankMatchStatus as ShopBankMatchStatus | null | undefined;
  if (!status) return shop.receivingBankAccountId ? 'Đã gắn' : 'Chưa gắn';
  return SHOP_BANK_MATCH_LABELS[status] ?? status;
};

export type ShopExcelRow = {
  STT: number;
  'Tên shop': string;
  'Loại shop': string;
  'Tên LLC': string;
  'Shop code': string;
  'External shop ID': string;
  Profile: string;
  Seller: string;
  'Trạng thái': string;
  Email: string;
  Password: string;
  '2FA': string;
  SĐT: string;
  'Ngày live': string;
  DELI: string | number;
  Proxy: string;
  Listing: string;
  'Bank TTS': string;
  'Bank hệ thống': string;
  'Khớp bank': string;
  '4Seller status': string;
  '4Seller ID': string;
  'Ngày tạo': string;
};

/** Map filtered shop rows to Excel column objects. */
export const mapShopsToExcelRows = (shops: ShopListItem[]): ShopExcelRow[] =>
  shops.map((shop, index) => {
    const shopTypeKey = shop.shopType?.trim() ?? '';
    const shopTypeLabel = (shopTypeKey && SHOP_TYPE_LABELS[shopTypeKey]) || shopTypeKey;
    const statusKey = shop.status?.trim() ?? '';
    const statusLabel = (statusKey && SHOP_STATUS_LABELS[statusKey]) || statusKey;

    return {
      STT: index + 1,
      'Tên shop': shop.name ?? '',
      'Loại shop': shopTypeLabel,
      'Tên LLC': shop.llcName ?? '',
      'Shop code': shop.shopCode ?? '',
      'External shop ID': shop.externalShopId ?? '',
      Profile: shop.profileName ?? '',
      Seller: formatSeller(shop),
      'Trạng thái': statusLabel,
      Email: shop.email ?? '',
      Password: shop.tiktokPassword ?? '',
      '2FA': shop.twoFactorAuth ?? '',
      SĐT: shop.newPhoneNumber ?? '',
      'Ngày live': shop.dayLive ?? '',
      DELI: shop.deli ?? '',
      Proxy: shop.proxy ?? '',
      Listing: formatListingLimit(shop),
      'Bank TTS': shop.settlementBankLast4 ? `****${shop.settlementBankLast4}` : '',
      'Bank hệ thống': shop.receivingBankLast4 ? `****${shop.receivingBankLast4}` : '',
      'Khớp bank': formatBankMatchLabel(shop),
      '4Seller status': shop.fourSellerConnectionStatus ?? '',
      '4Seller ID': shop.fourSellerShopId ?? '',
      'Ngày tạo': shop.createdAt ? formatDateTimeVn(shop.createdAt) : '',
    };
  });

/** Download Excel for the given shops (typically filteredShops). */
export const exportShopsExcel = (shops: ShopListItem[]): void => {
  if (shops.length === 0) return;

  const rows = mapShopsToExcelRows(shops);
  const sheet = XLSX.utils.json_to_sheet(rows);
  const book = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(book, sheet, 'Shops');
  XLSX.writeFile(book, buildExportFileName());
};
