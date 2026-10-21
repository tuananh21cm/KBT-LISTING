export interface Category {
  id: string;
  externalId: string;
  name: string;
  parentExternalId?: string;
  site: string;
  isLeaf: boolean;
  level: number;
  path: string;
  pathLabel?: string;
  isActive: boolean;
}

export interface CategoryTreeNode {
  value: string;
  label: string;
  isLeaf: boolean;
  children?: CategoryTreeNode[];
}

export interface CategoryPath {
  pathIds: string[];
  options: CategoryTreeNode[];
}

export interface RecommendCategoryRequest {
  site: string;
  title: string;
  fourSellerShopId?: number;
}

export interface RecommendCategoryResult {
  categoryId: string;
  categoryName: string;
  nodePath?: string;
  pathIds: string[];
  isLeaf: boolean;
  site: string;
  /** False khi category không có trong catalog đã sync, bị vô hiệu hoá, hoặc không phải leaf. */
  isValidated?: boolean;
  validationWarning?: string | null;
  categoryPath?: string | null;
  /** AI trả về leaf này nhưng Settings blocked-remap đã thay bằng categoryId. */
  replacedFromCategoryId?: string | null;
  replacedFromCategoryName?: string | null;
  replacementReason?: string | null;
}
