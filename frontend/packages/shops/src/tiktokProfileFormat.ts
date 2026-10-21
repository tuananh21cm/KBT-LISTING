import type { ShopAddress } from '@drop/types/shop';
import { TIKTOK_SELLER_TYPE_LABELS, TIKTOK_SHOP_STATUS_LABELS } from '@drop/types/shop';

/** Format company / owner address for ShopsPage (matches extension). */
export function formatShopAddress(addr?: ShopAddress | null): string | null {
  if (!addr) return null;
  const line1 = [addr.lineOne, addr.lineTwo].filter(Boolean).join(', ');
  const regionBits = [addr.city, addr.state, addr.postCode].filter(Boolean).join(', ');
  const tail = [regionBits, addr.countryCode || addr.country].filter(Boolean).join(' · ');
  const out = [line1, tail].filter(Boolean).join(' — ');
  return out || null;
}

export function formatTikTokShopStatus(status?: number | null): string {
  if (status == null || !Number.isFinite(status)) return '—';
  return TIKTOK_SHOP_STATUS_LABELS[status] ?? `Status ${status}`;
}

export function formatTikTokSellerType(sellerType?: number | null): string {
  if (sellerType == null || !Number.isFinite(Number(sellerType))) return '—';
  return TIKTOK_SELLER_TYPE_LABELS[Number(sellerType)] ?? `Type ${sellerType}`;
}

export function formatTimezoneShort(timezone?: string | null): string {
  if (!timezone?.trim()) return '—';
  const t = timezone.trim();
  const slash = t.lastIndexOf('/');
  return slash >= 0 ? t.slice(slash + 1) : t;
}

/** Public TikTok Shop storefront URL from name + seller / external shop id. */
export function buildTikTokShopStoreUrl(
  shopName?: string | null,
  sellerId?: string | null,
): string | null {
  const id = sellerId?.trim();
  if (!id) return null;
  const slug = slugifyShopName(shopName) || id;
  return `https://www.tiktok.com/shop/store/${slug}/${id}`;
}

function slugifyShopName(name?: string | null): string {
  if (!name?.trim()) return '';
  return name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}
