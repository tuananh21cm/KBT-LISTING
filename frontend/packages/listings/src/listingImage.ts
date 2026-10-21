/** Lấy URL ảnh đầu tiên khi `mainImage` là chuỗi pipe-separated. */
export function firstImage(raw?: string | null): string | null {
  if (!raw) return null;
  return raw.split('|').map((s) => s.trim()).find(Boolean) || null;
}
