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

export interface CreateVariationSpecTemplateRequest {
  specType: VariationSpecType | string;
  value: string;
  sortOrder?: number;
  isActive?: boolean;
}

export interface UpdateVariationSpecTemplateRequest {
  value: string;
  sortOrder: number;
  isActive: boolean;
}

export interface ReorderVariationSpecTemplatesRequest {
  specType: VariationSpecType | string;
  orderedIds: string[];
}
