import type { PricingFormulaInput, PricingTargetMode } from '@drop/types/pricingTemplate';

export const DEFAULT_PRICING_FORMULA: PricingFormulaInput = {
  supplierShipping: 3.99,
  tax: 1,
  discountPercent: 15,
  fastSalePercent: 15,
  adsFee: 3,
  tikTokShipping: 5.99,
  platformFeePercent: 6,
  targetMode: 'margin',
  marginPercent: 50,
  profitAmount: 10,
};

export interface SalePriceBreakdown {
  cost: number;
  totalImport: number;
  promoRate: number;
  promoPercent: number;
  targetProfit: number;
  salePrice: number | null;
  error?: string;
}

/** Round to 2 decimal places (USD). */
export function roundMoney(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

export function resolvePromoPercent(discountPercent: number, fastSalePercent: number): number {
  return Math.max(discountPercent || 0, fastSalePercent || 0);
}

/**
 * SalePrice S =
 *   (TotalImport + AdsFee - TikTokShipping + TargetProfit)
 *   / ((1 - PromoRate) * (1 - PlatformFee%/100))
 *
 * where PromoRate = max(Discount%, FastSale%) / 100
 * and TargetProfit = margin% * TotalImport  OR  fixed ProfitUSD
 */
export function calculateSalePrice(
  cost: number | null | undefined,
  input: PricingFormulaInput,
): SalePriceBreakdown {
  const safeCost = cost == null || Number.isNaN(Number(cost)) ? NaN : Number(cost);
  if (!(safeCost > 0)) {
    return {
      cost: safeCost || 0,
      totalImport: 0,
      promoRate: 0,
      promoPercent: 0,
      targetProfit: 0,
      salePrice: null,
      error: 'Thiếu costPrice',
    };
  }

  const totalImport = safeCost + (input.supplierShipping || 0) + (input.tax || 0);
  const promoPercent = resolvePromoPercent(input.discountPercent, input.fastSalePercent);
  const promoRate = promoPercent / 100;
  const platformRate = (input.platformFeePercent || 0) / 100;

  if (promoRate >= 1 || platformRate >= 1) {
    return {
      cost: safeCost,
      totalImport: roundMoney(totalImport),
      promoRate,
      promoPercent,
      targetProfit: 0,
      salePrice: null,
      error: 'Promo / phí sàn không hợp lệ',
    };
  }

  const mode: PricingTargetMode = input.targetMode === 'profit' ? 'profit' : 'margin';
  const targetProfit =
    mode === 'margin'
      ? ((input.marginPercent || 0) / 100) * totalImport
      : input.profitAmount || 0;

  const numerator = totalImport + (input.adsFee || 0) - (input.tikTokShipping || 0) + targetProfit;
  const denominator = (1 - promoRate) * (1 - platformRate);

  if (denominator <= 0) {
    return {
      cost: safeCost,
      totalImport: roundMoney(totalImport),
      promoRate,
      promoPercent,
      targetProfit: roundMoney(targetProfit),
      salePrice: null,
      error: 'Công thức không hợp lệ',
    };
  }

  const salePrice = roundMoney(numerator / denominator);
  if (!(salePrice > 0)) {
    return {
      cost: safeCost,
      totalImport: roundMoney(totalImport),
      promoRate,
      promoPercent,
      targetProfit: roundMoney(targetProfit),
      salePrice: null,
      error: 'Giá bán không hợp lệ',
    };
  }

  return {
    cost: safeCost,
    totalImport: roundMoney(totalImport),
    promoRate,
    promoPercent,
    targetProfit: roundMoney(targetProfit),
    salePrice,
  };
}

/** Cost + supplier shipping + tax (tổng chi phí nhập 1 đơn). */
export function resolveTotalImport(
  cost: number | null | undefined,
  input: Pick<PricingFormulaInput, 'supplierShipping' | 'tax'>,
): number | null {
  const safeCost = cost == null || Number.isNaN(Number(cost)) ? NaN : Number(cost);
  if (Number.isNaN(safeCost) || safeCost < 0) return null;
  return roundMoney(safeCost + (input.supplierShipping || 0) + (input.tax || 0));
}

/** SKU + shipping nhập + tax + ads — tổng chi phí tiền mặt trước phí sàn %. */
export function resolveTotalCost(
  cost: number | null | undefined,
  input: Pick<PricingFormulaInput, 'supplierShipping' | 'tax' | 'adsFee'>,
): number | null {
  const totalImport = resolveTotalImport(cost, input);
  if (totalImport == null) return null;
  return roundMoney(totalImport + (input.adsFee || 0));
}

/**
 * Seller net after promo, platform fee, import costs, and ads.
 * Uses the listing's sale price when set; otherwise falls back to formula-derived sale price.
 */
export function calculateNetReceive(
  salePrice: number | null | undefined,
  cost: number | null | undefined,
  input: PricingFormulaInput,
): number | null {
  const safeSale = salePrice == null || Number.isNaN(Number(salePrice)) ? NaN : Number(salePrice);
  const safeCost = cost == null || Number.isNaN(Number(cost)) ? NaN : Number(cost);
  if (!(safeSale > 0) || Number.isNaN(safeCost) || safeCost < 0) return null;

  const totalImport = resolveTotalImport(safeCost, input) ?? 0;
  const promoRate = resolvePromoPercent(input.discountPercent, input.fastSalePercent) / 100;
  const platformRate = (input.platformFeePercent || 0) / 100;

  const netPrice = roundMoney(safeSale * (1 - promoRate));
  return roundMoney(
    netPrice * (1 - platformRate) +
      (input.tikTokShipping || 0) -
      totalImport -
      (input.adsFee || 0),
  );
}

/** Profit (thực nhận) thấp hơn tổng cost → 1 success khó bù 1 refund. */
export function isProfitBelowTotalCost(
  netReceive: number | null | undefined,
  totalCost: number | null | undefined,
): boolean {
  return (
    netReceive != null &&
    totalCost != null &&
    totalCost > 0 &&
    netReceive < totalCost
  );
}

export function collectVariationProfits(
  variations: Array<{ costPrice?: number | null; salePrice?: number | null }>,
  formula: PricingFormulaInput,
): number[] {
  return variations
    .map((v) => {
      const cost = v.costPrice;
      let sale = v.salePrice;
      const saleNum = sale == null ? NaN : Number(sale);
      if (Number.isNaN(saleNum) || saleNum <= 0) {
        sale = calculateSalePrice(cost, formula).salePrice;
      }
      return calculateNetReceive(sale, cost, formula);
    })
    .filter((p): p is number => p != null);
}

export function formatProfitLabel(profits: number[]): string | null {
  if (profits.length === 0) return null;
  const min = Math.min(...profits);
  const max = Math.max(...profits);
  const fmt = (n: number) => `${n >= 0 ? '+' : ''}$${n.toFixed(2)}`;
  if (min === max) return fmt(min);
  return `${fmt(min)} – ${fmt(max)}`;
}

export function templateToFormulaInput(
  t: Partial<PricingFormulaInput> & {
    targetMode?: string;
  },
): PricingFormulaInput {
  return {
    supplierShipping: t.supplierShipping ?? DEFAULT_PRICING_FORMULA.supplierShipping,
    tax: t.tax ?? DEFAULT_PRICING_FORMULA.tax,
    discountPercent: t.discountPercent ?? DEFAULT_PRICING_FORMULA.discountPercent,
    fastSalePercent: t.fastSalePercent ?? DEFAULT_PRICING_FORMULA.fastSalePercent,
    adsFee: t.adsFee ?? DEFAULT_PRICING_FORMULA.adsFee,
    tikTokShipping: t.tikTokShipping ?? DEFAULT_PRICING_FORMULA.tikTokShipping,
    platformFeePercent: t.platformFeePercent ?? DEFAULT_PRICING_FORMULA.platformFeePercent,
    targetMode: t.targetMode === 'profit' ? 'profit' : 'margin',
    marginPercent: t.marginPercent ?? DEFAULT_PRICING_FORMULA.marginPercent,
    profitAmount: t.profitAmount ?? DEFAULT_PRICING_FORMULA.profitAmount,
  };
}

/** Apply formula to rows that are missing a positive salePrice (keeps existing prices). */
export function fillMissingSalePrices<
  T extends { costPrice?: number | null; salePrice?: number | null },
>(rows: T[], input: PricingFormulaInput): { rows: T[]; filled: number } {
  let filled = 0;
  const next = rows.map((row) => {
    const sale = row.salePrice == null ? NaN : Number(row.salePrice);
    if (!Number.isNaN(sale) && sale > 0) return row;
    const result = calculateSalePrice(row.costPrice, input);
    if (result.salePrice == null) return row;
    filled += 1;
    return { ...row, salePrice: result.salePrice };
  });
  return { rows: next, filled };
}

/** Apply default pricing template to variation rows (sets salePrice when cost is valid). */
export function applyPricingTemplateToRows<T extends { costPrice?: number; salePrice: number }>(
  rows: T[],
  input: PricingFormulaInput,
): T[] {
  return rows.map((row) => {
    const result = calculateSalePrice(row.costPrice, input);
    if (result.salePrice == null) return row;
    return { ...row, salePrice: result.salePrice };
  });
}

export function getDefaultPricingTemplate<T extends { isDefault?: boolean }>(templates: T[]): T | undefined {
  return templates.find((t) => t.isDefault) ?? templates[0];
}
