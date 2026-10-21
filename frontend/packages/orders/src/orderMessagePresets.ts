import type { IssueTag } from '@drop/types/order';

/** Fallback labels when library chưa load — sync với seed backend. */
export const SYSTEM_ISSUE_TAG_LABELS = [
  'Hết size',
  'Giá cao',
  'Hết hàng',
  'Sai màu',
  'Shop không ship',
  'Outstock',
] as const;

export type OrderIssueStatus = 'None' | 'Processing' | 'Done';

export function issueTagBadgeColor(_tag?: Pick<IssueTag, 'label' | 'isSystem'> | string): 'error' {
  return 'error';
}

export function orderMessageBadgeColor(_text: string): 'error' {
  return 'error';
}

export function isProcessingIssueStatus(status?: string | null): boolean {
  return (status || '').toLowerCase() === 'processing';
}

export function isDoneIssueStatus(status?: string | null): boolean {
  return (status || '').toLowerCase() === 'done';
}

/** @deprecated Prefer order.issueTags — kept for card prefix fallback. */
export const ORDER_MESSAGE_PRESETS = SYSTEM_ISSUE_TAG_LABELS.map((label) => ({
  label,
  color: 'error' as const,
}));
