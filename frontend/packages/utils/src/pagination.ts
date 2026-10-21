import type { TablePaginationConfig } from 'antd';

/**
 * Show pager only when total exceeds one page (and is non-empty).
 * Empty lists and single-page results hide pagination UI.
 */
export const needsPagination = (total: number, pageSize: number): boolean =>
  total > pageSize && pageSize > 0;

/**
 * Ant Design Table `pagination` prop: `false` hides the pager.
 * Typed as TablePaginationConfig so literal props like `size: 'small'` stay SizeType.
 */
export const tablePagination = (
  total: number,
  config: TablePaginationConfig,
): TablePaginationConfig | false => {
  const pageSize = config.pageSize ?? 10;
  return needsPagination(total, pageSize) ? config : false;
};
