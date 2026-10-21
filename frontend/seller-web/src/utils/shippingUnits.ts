/** Map API/4Seller unit names (e.g. KILOGRAM) to short Select values (kg, lb, cm, inch). */

export function normalizeWeightUnit(value?: string | null): string {
  const unit = (value ?? 'lb').trim().toLowerCase();
  if (['kg', 'kgs', 'kilogram', 'kilograms'].includes(unit)) return 'kg';
  if (['lb', 'lbs', 'pound', 'pounds'].includes(unit)) return 'lb';
  return 'lb';
}

export function normalizeDimensionUnit(value?: string | null): string {
  const unit = (value ?? 'inch').trim().toLowerCase();
  if (['cm', 'centimeter', 'centimeters', 'centimetre', 'centimetres'].includes(unit)) return 'cm';
  if (['inch', 'inches', 'in'].includes(unit)) return 'inch';
  return 'inch';
}

export const WEIGHT_UNIT_OPTIONS = [
  { label: 'lb', value: 'lb' },
  { label: 'kg', value: 'kg' },
] as const;

export const DIMENSION_UNIT_OPTIONS = [
  { label: 'inch', value: 'inch' },
  { label: 'cm', value: 'cm' },
] as const;
