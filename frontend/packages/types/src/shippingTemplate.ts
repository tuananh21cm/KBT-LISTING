export interface ShippingTemplate {
  id: string;
  name: string;
  packageWeight: number;
  packageLength: number;
  packageWidth: number;
  packageHeight: number;
  weightUnit: string;
  dimensionUnit: string;
  isDefault: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateShippingTemplateRequest {
  name: string;
  packageWeight: number;
  packageLength: number;
  packageWidth: number;
  packageHeight: number;
  weightUnit: string;
  dimensionUnit: string;
  isDefault?: boolean;
}

export type UpdateShippingTemplateRequest = CreateShippingTemplateRequest;

/** Prefer explicit default, else first template. */
export function getDefaultShippingTemplate<T extends { isDefault?: boolean }>(
  templates: T[],
): T | undefined {
  return templates.find((t) => t.isDefault) ?? templates[0];
}
