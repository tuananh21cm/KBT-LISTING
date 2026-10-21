export type PricingTargetMode = 'margin' | 'profit';

export interface PricingTemplate {
  id: string;
  name: string;
  supplierShipping: number;
  tax: number;
  discountPercent: number;
  fastSalePercent: number;
  adsFee: number;
  tikTokShipping: number;
  platformFeePercent: number;
  targetMode: PricingTargetMode;
  marginPercent: number;
  profitAmount: number;
  isDefault: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreatePricingTemplateRequest {
  name: string;
  supplierShipping: number;
  tax: number;
  discountPercent: number;
  fastSalePercent: number;
  adsFee: number;
  tikTokShipping: number;
  platformFeePercent: number;
  targetMode: PricingTargetMode;
  marginPercent: number;
  profitAmount: number;
  isDefault: boolean;
}

export type UpdatePricingTemplateRequest = CreatePricingTemplateRequest;

/** Calculator inputs (template fee fields, excludes name / isDefault). */
export type PricingFormulaInput = Omit<CreatePricingTemplateRequest, 'name' | 'isDefault'>;
