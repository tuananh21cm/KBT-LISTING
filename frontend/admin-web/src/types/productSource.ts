export interface ProductSource {
  id: string;
  code: string;
  name: string;
  logoUrl?: string;
  domains?: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface ProductSourceOption {
  id: string;
  code: string;
  name: string;
  logoUrl?: string;
  domains?: string;
}

export interface CreateProductSourceRequest {
  code: string;
  name: string;
  logoUrl?: string;
  domains?: string;
  description?: string;
  isActive: boolean;
}

export interface UpdateProductSourceRequest {
  code: string;
  name: string;
  logoUrl?: string;
  domains?: string;
  description?: string;
  isActive: boolean;
}
