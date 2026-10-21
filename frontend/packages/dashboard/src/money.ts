export const money = (value: number) =>
  value.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 2 });

/** Fewer decimals once the amount is large — keeps mobile / dense tables readable. */
export const moneyCompact = (value: number) =>
  value.toLocaleString('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: Math.abs(value) >= 100 ? 0 : 2,
  });

/** Compact $ label for dense heatmap cells / chips (e.g. `$1.2k`). */
export const shortMoney = (value: number) => {
  const abs = Math.abs(value);
  if (abs >= 10_000) return `$${Math.round(value / 1000)}k`;
  if (abs >= 1000) return `$${(value / 1000).toFixed(1)}k`;
  return `$${Math.round(value)}`;
};
