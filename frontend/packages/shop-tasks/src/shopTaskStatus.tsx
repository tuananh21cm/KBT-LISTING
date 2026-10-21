import {
  CheckCircleFilled,
  ClockCircleOutlined,
  CloseCircleFilled,
  MinusCircleOutlined,
  WarningFilled,
} from '@ant-design/icons';
import type { ReactNode } from 'react';
import type { ShopTaskItem, ShopTaskStatus } from '@drop/types/shopTask';

type StatusMeta = {
  label: string;
  /** Tone dùng cho Tag của Ant. */
  tone: 'success' | 'processing' | 'error' | 'warning' | 'default';
  icon: ReactNode;
};

const STATUS_META: Record<ShopTaskStatus, StatusMeta> = {
  done: { label: 'Xong', tone: 'success', icon: <CheckCircleFilled /> },
  pending: { label: 'Chưa làm', tone: 'default', icon: <ClockCircleOutlined /> },
  attention: { label: 'Cần xử lý', tone: 'warning', icon: <WarningFilled /> },
  failed: { label: 'Thất bại', tone: 'error', icon: <CloseCircleFilled /> },
  blocked: { label: 'Thiếu dữ liệu', tone: 'default', icon: <MinusCircleOutlined /> },
};

export const statusMeta = (status: ShopTaskStatus): StatusMeta =>
  STATUS_META[status] ?? STATUS_META.blocked;

/**
 * Tone chip `delist_cleanup`: theo số cần hủy / cần xem xét trên shop,
 * không theo slot còn trống (status backend vẫn `done` khi còn slot).
 */
export const delistChipTone = (
  delistCount?: number | null,
  reviewCount?: number | null,
): StatusMeta['tone'] => {
  if ((delistCount ?? 0) > 0) return 'error';
  if ((reviewCount ?? 0) > 0) return 'warning';
  if (delistCount != null || reviewCount != null) return 'success';
  return 'default';
};

/** Nhãn chip tối ưu listing — cùng wording summary «Cần hủy» / «Cần xem xét». */
export const delistChipLabel = (
  delistCount?: number | null,
  reviewCount?: number | null,
): string => {
  const parts: string[] = [];
  if (delistCount != null) parts.push(`Cần hủy ${delistCount}`);
  if (reviewCount != null) parts.push(`Cần xem xét ${reviewCount}`);
  if (parts.length === 0) return 'Cần hủy / Cần xem xét';
  return parts.join(' · ');
};

/** Nhiệm vụ hàng ngày thật sự — không gồm `listing_fill` (theo dõi qua cột Slot). */
export const isDailyMissionTask = (task: ShopTaskItem): boolean =>
  task.group === 'daily' && task.key !== 'listing_fill';

/** Nhãn ngắn cho chip trên bảng — nhiệm vụ URL lấy tên admin đặt. */
export const taskShortLabel = (task: ShopTaskItem): string => {
  switch (task.key) {
    case 'sync_extension':
      return 'Đồng bộ';
    case 'listing_fill':
      return 'Đủ listing';
    case 'delist_cleanup':
      return delistChipLabel(task.current, null);
    default:
      return task.title;
  }
};
