/** Định dạng số dùng chung cho Listing analytics (bảng, funnel, đề xuất hủy list). */

export const formatInt = (value?: number | null): string =>
  value == null ? '—' : Math.round(value).toLocaleString('en-US');

export const formatMoney = (value: number, currency: string): string => {
  try {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: currency || 'USD',
      minimumFractionDigits: 0,
      maximumFractionDigits: value < 100 ? 2 : 0,
    }).format(value);
  } catch {
    return `${value.toLocaleString('en-US')} ${currency}`;
  }
};

/** Fraction → percent 2 chữ số thập phân (0.0234 → `2.34%`). */
export const formatRate = (value?: number | null): string =>
  value == null ? '—' : `${(value * 100).toFixed(2)}%`;

/** Fraction → percent với số chữ số thập phân co theo độ lớn (funnel chip). */
export const formatConversion = (fraction: number): string => {
  if (!Number.isFinite(fraction)) return '—';
  const pct = fraction * 100;
  if (pct === 0) return '0%';
  if (Math.abs(pct) >= 100) return `${Math.round(pct).toLocaleString('en-US')}%`;
  if (Math.abs(pct) >= 1) return `${Math.round(pct * 10) / 10}%`;
  const maxDecimals = Math.abs(pct) < 0.1 ? 3 : 2;
  return `${pct.toLocaleString('en-US', {
    minimumFractionDigits: 0,
    maximumFractionDigits: maxDecimals,
  })}%`;
};

/** 12400 → `12.4K`; giữ dòng meta ngắn khi cột hẹp. */
export const formatCompact = (value?: number | null): string => {
  if (value == null) return '—';
  const abs = Math.abs(value);
  if (abs >= 1_000_000) return `${(value / 1_000_000).toFixed(1).replace(/\.0$/, '')}M`;
  if (abs >= 1_000) return `${(value / 1_000).toFixed(1).replace(/\.0$/, '')}K`;
  return Math.round(value).toLocaleString('en-US');
};
