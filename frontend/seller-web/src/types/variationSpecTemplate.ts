export type VariationSpecType = 'Color' | 'Size' | 'Number of Items';

export interface VariationSpecTemplate {
  id: string;
  specType: VariationSpecType | string;
  value: string;
  sortOrder: number;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string;
}
