import type { ShopTaskShop } from '@drop/types/shopTask';

export const ownerLabel = (fullName?: string | null, userName?: string | null): string =>
  fullName?.trim() || userName?.trim() || '—';

export const shopLabel = (shopName: string, profileName?: string | null): string => {
  const profile = profileName?.trim();
  const name = shopName?.trim() || 'Shop';
  return profile ? `${profile} (${name})` : name;
};

/** Tiêu đề shop — admin thêm tên seller ở đầu. */
export const shopTitleLabel = (row: ShopTaskShop, showOwner: boolean): string => {
  const label = shopLabel(row.shopName, row.profileName);
  if (!showOwner) return label;
  const owner = ownerLabel(row.ownerFullName, row.ownerUserName);
  return owner === '—' ? label : `${owner} · ${label}`;
};
