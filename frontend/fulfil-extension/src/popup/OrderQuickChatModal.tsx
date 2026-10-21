import { useEffect, useRef, useState } from 'react';
import type {
  CreateIssueTagResponse,
  DeleteIssueTagResponse,
  ListIssueTagsResponse,
  UpdateIssueTagResponse,
} from '../shared/messages';
import type {
  FulfilIssueTag,
  FulfilOrder,
  FulfilOrderItem,
} from '../shared/types';
import { resolveBuyUrl } from '../shared/buyUrl';
import { reloadExtension, send, isStaleWorkerError } from './messaging';

const TAG_LABEL_MAX = 50;
const TAG_PERSIST_DEBOUNCE_MS = 280;

/** Naive API datetime (no Z/offset) = UTC — append Z. */
const NAIVE_UTC_DATETIME_RE =
  /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?$/;

function parseApiInstantMs(iso: string): number {
  const t = iso.trim();
  if (NAIVE_UTC_DATETIME_RE.test(t)) {
    const normalized = (t.includes('T') ? t : t.replace(' ', 'T')) + 'Z';
    return Date.parse(normalized);
  }
  return Date.parse(t);
}

function formatChatDate(iso?: string | null): string {
  if (!iso) return '—';
  const ms = parseApiInstantMs(iso);
  if (!Number.isFinite(ms)) return '—';
  return new Date(ms).toLocaleString('vi-VN', {
    timeZone: 'Asia/Ho_Chi_Minh',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function formatBuyerShipTo(parts: {
  address?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
}): string {
  const street = (parts.address || '').trim();
  const city = (parts.city || '').trim();
  const state = (parts.state || '').trim();
  const zip = (parts.postalCode || '').trim();
  const locality = [city, state, zip].filter(Boolean).join(', ');
  if (street && locality) {
    const streetLower = street.toLowerCase();
    if (
      (city && streetLower.includes(city.toLowerCase())) ||
      (zip && streetLower.includes(zip.toLowerCase()))
    ) {
      return street;
    }
    return `${street}, ${locality}`;
  }
  return street || locality || '—';
}

async function copyText(text: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    /* ignore */
  }
}

function Spinner({ dark }: { dark?: boolean }) {
  return <span className={`spinner${dark ? ' spinner--dark' : ''}`} aria-hidden />;
}

function TagsIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" aria-hidden focusable="false">
      <path
        fill="currentColor"
        d="M21.41 11.58l-9-9C12.05 2.22 11.55 2 11 2H4c-1.1 0-2 .9-2 2v7c0 .55.22 1.05.59 1.42l9 9c.36.36.86.58 1.41.58s1.05-.22 1.41-.59l7-7c.37-.36.59-.86.59-1.41s-.23-1.06-.59-1.42zM5.5 7C4.67 7 4 6.33 4 5.5S4.67 4 5.5 4 7 4.67 7 5.5 6.33 7 5.5 7z"
      />
    </svg>
  );
}

function SettingsIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden focusable="false">
      <path
        fill="currentColor"
        d="M19.14 12.94c.04-.31.06-.63.06-.94s-.02-.63-.06-.94l2.03-1.58a.49.49 0 0 0 .12-.61l-1.92-3.32a.49.49 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54a.48.48 0 0 0-.48-.41h-3.84a.48.48 0 0 0-.48.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96a.49.49 0 0 0-.59.22L2.74 8.87a.49.49 0 0 0 .12.61l2.03 1.58c-.04.31-.06.63-.06.94s.02.63.06.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32c.12.22.37.3.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.48-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32a.49.49 0 0 0-.12-.61l-2.03-1.58zM12 15.6A3.6 3.6 0 1 1 12 8.4a3.6 3.6 0 0 1 0 7.2z"
      />
    </svg>
  );
}

function EditIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden focusable="false">
      <path
        fill="currentColor"
        d="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04a1.003 1.003 0 0 0 0-1.41l-2.34-2.34a1.003 1.003 0 0 0-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z"
      />
    </svg>
  );
}

function DeleteIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden focusable="false">
      <path
        fill="currentColor"
        d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"
      />
    </svg>
  );
}

function ArrowLeftIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden focusable="false">
      <path fill="currentColor" d="M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z" />
    </svg>
  );
}

function CheckSmallIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden focusable="false">
      <path fill="currentColor" d="M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z" />
    </svg>
  );
}

function CheckCircleIcon({ filled }: { filled?: boolean }) {
  if (filled) {
    return (
      <svg width="16" height="16" viewBox="0 0 24 24" aria-hidden focusable="false">
        <path
          fill="currentColor"
          d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"
        />
      </svg>
    );
  }
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" aria-hidden focusable="false">
      <path
        fill="currentColor"
        d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm-1.29-5.29L7.4 11.4l1.41-1.41 2.12 2.12 4.95-4.95 1.41 1.41-6.58 6.54z"
      />
    </svg>
  );
}

/** Card context đơn (ảnh SP, buyer, shop) hiển thị trên modal tag lỗi. */
function OrderChatContextCard({
  order,
  onOpenProduct,
}: {
  order: FulfilOrder;
  onOpenProduct?: (orderId: string, item: FulfilOrderItem) => void;
}) {
  const firstItem = order.lineItems?.[0] ?? null;
  const orderIdLabel = order.platformOrderId?.trim() || order.id.slice(0, 8);
  const productTitle = firstItem?.title?.trim() || 'Đơn hàng';
  const productUrl =
    (firstItem ? resolveBuyUrl(firstItem) : null) ||
    firstItem?.listingUrl?.trim() ||
    firstItem?.sourceUrl?.trim() ||
    null;
  const imageUrl = firstItem?.sourceImgUrl?.trim() || firstItem?.imgUrl?.trim() || null;
  const sellerSku = firstItem?.sellerSku ?? null;
  const variantAttr = firstItem?.variantAttr ?? null;
  const quantity = firstItem?.quantity ?? null;
  const metaLine = [
    sellerSku ? `SKU: ${sellerSku}` : null,
    variantAttr || null,
    quantity != null && quantity > 1 ? `×${quantity}` : null,
  ]
    .filter(Boolean)
    .join(' · ');

  const buyerName = order.buyerName ?? null;
  const buyerPhone = order.buyerPhone ?? null;
  const shipTo = formatBuyerShipTo({
    address: order.address,
    city: order.city,
    state: order.state,
    postalCode: order.postalCode,
  });
  const shopName = order.shopName ?? order.shopProfileName ?? null;
  const placedAt = order.orderCreateTime ?? order.orderPaidTime;

  const openProduct = () => {
    if (firstItem && onOpenProduct) {
      onOpenProduct(order.id, firstItem);
      return;
    }
    if (productUrl) {
      void send({ type: 'OPEN_PRODUCT_URL', url: productUrl });
    }
  };

  const thumb = imageUrl ? (
    <img
      src={imageUrl}
      alt=""
      width={52}
      height={52}
      className="msg-chat-product-thumb-img"
      referrerPolicy="no-referrer"
      loading="lazy"
      decoding="async"
    />
  ) : (
    <div className="msg-chat-product-thumb-empty" />
  );

  return (
    <div className="msg-chat-order-stack">
      <div className="msg-chat-order-box">
        <div className="msg-chat-order-box-head">
          <button
            type="button"
            className="msg-chat-order-box-id"
            title="Bấm để copy mã đơn"
            onClick={() => void copyText(orderIdLabel)}
          >
            {orderIdLabel}
          </button>
          {firstItem || productUrl ? (
            <button type="button" className="msg-chat-order-open" onClick={openProduct}>
              Mở SP
            </button>
          ) : null}
        </div>
        <div className="msg-chat-product-block">
          {productUrl ? (
            <a
              href={productUrl}
              target="_blank"
              rel="noreferrer"
              className="msg-chat-product-thumb-link"
              onClick={(ev) => {
                if (firstItem && onOpenProduct) {
                  ev.preventDefault();
                  onOpenProduct(order.id, firstItem);
                }
              }}
            >
              {thumb}
            </a>
          ) : (
            thumb
          )}
          <div className="msg-chat-product-lines">
            <div className="msg-chat-product-line msg-chat-product-line--title" title={productTitle}>
              {productTitle}
            </div>
            <div className="msg-chat-product-line msg-chat-product-line--row">
              {metaLine ? (
                <span className="msg-chat-product-meta" title={metaLine}>
                  {metaLine}
                </span>
              ) : null}
            </div>
          </div>
        </div>
      </div>

      <div className="msg-chat-order-box">
        <div className="msg-chat-buyer-compact">
          <div className="msg-chat-buyer-row msg-chat-buyer-row--primary">
            <span className="msg-chat-buyer-name" title={buyerName || undefined}>
              {buyerName || '—'}
            </span>
            {buyerPhone ? (
              <button
                type="button"
                className="msg-chat-buyer-phone"
                title={`${buyerPhone} — bấm để copy`}
                onClick={() => void copyText(buyerPhone)}
              >
                {buyerPhone}
              </button>
            ) : null}
          </div>
          <div className="msg-chat-buyer-row msg-chat-buyer-address" title={shipTo}>
            {shipTo}
          </div>
          <div className="msg-chat-buyer-row msg-chat-buyer-meta">
            {shopName ? (
              <span className="msg-chat-buyer-shop" title={shopName}>
                {shopName}
              </span>
            ) : null}
            <span>
              <span className="msg-chat-buyer-meta-label">Đặt</span> {formatChatDate(placedAt)}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}

export type OrderQuickChatModalProps = {
  order: FulfilOrder | null;
  open: boolean;
  savingTags: boolean;
  suggest?: string | null;
  onClose: () => void;
  onOpenProduct?: (orderId: string, item: FulfilOrderItem) => void;
  onSaveTags: (payload: {
    tagIds?: string[];
    status?: 'Processing' | 'Done';
  }) => void | Promise<void>;
};

/**
 * Modal tag lỗi nhanh cho đơn (issue tags + trạng thái xử lý). Không còn chat/thread.
 */
export function OrderQuickChatModal({
  order,
  open,
  savingTags,
  suggest,
  onClose,
  onOpenProduct,
  onSaveTags,
}: OrderQuickChatModalProps) {
  const [library, setLibrary] = useState<FulfilIssueTag[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [status, setStatus] = useState<'Processing' | 'Done'>('Processing');
  const [newLabel, setNewLabel] = useState('');
  const [loadingLib, setLoadingLib] = useState(false);
  const [tagPickerOpen, setTagPickerOpen] = useState(false);
  const [tagManageOpen, setTagManageOpen] = useState(false);
  const [creatingTag, setCreatingTag] = useState(false);
  const [editingTagId, setEditingTagId] = useState<string | null>(null);
  const [editingLabel, setEditingLabel] = useState('');
  const [savingEdit, setSavingEdit] = useState(false);
  const [deletingTagId, setDeletingTagId] = useState<string | null>(null);
  const [tagPersisting, setTagPersisting] = useState(false);

  const [chatError, setChatError] = useState<string | null>(null);

  const pickerRef = useRef<HTMLDivElement>(null);
  const openOrderIdRef = useRef<string | null>(null);
  openOrderIdRef.current = open && order ? order.id : null;
  const onSaveTagsRef = useRef(onSaveTags);
  onSaveTagsRef.current = onSaveTags;
  const statusRef = useRef(status);
  statusRef.current = status;
  const pendingTagIdsRef = useRef<string[] | null>(null);
  const tagPersistTimerRef = useRef<number | null>(null);
  const tagPersistInFlightRef = useRef(false);

  const flushTagPersist = async () => {
    if (tagPersistInFlightRef.current) return;
    const ids = pendingTagIdsRef.current;
    const orderId = openOrderIdRef.current;
    if (!ids || !orderId) return;
    pendingTagIdsRef.current = null;
    tagPersistInFlightRef.current = true;
    setTagPersisting(true);
    try {
      await onSaveTagsRef.current({ tagIds: ids });
    } finally {
      tagPersistInFlightRef.current = false;
      if (pendingTagIdsRef.current) {
        void flushTagPersist();
      } else {
        setTagPersisting(false);
      }
    }
  };

  const schedulePersistTags = (nextIds: string[], delayMs = TAG_PERSIST_DEBOUNCE_MS) => {
    setSelectedIds(nextIds);
    if (nextIds.length > 0 && statusRef.current === 'Done') {
      setStatus('Processing');
      statusRef.current = 'Processing';
    }
    pendingTagIdsRef.current = nextIds;
    if (tagPersistTimerRef.current) window.clearTimeout(tagPersistTimerRef.current);
    tagPersistTimerRef.current = window.setTimeout(() => {
      tagPersistTimerRef.current = null;
      void flushTagPersist();
    }, delayMs);
  };

  const closeTagPickerUi = () => {
    setTagPickerOpen(false);
    setTagManageOpen(false);
    setEditingTagId(null);
  };

  useEffect(() => {
    if (!open || !order) return;
    if (tagPersistTimerRef.current) {
      window.clearTimeout(tagPersistTimerRef.current);
      tagPersistTimerRef.current = null;
    }
    pendingTagIdsRef.current = null;
    setSelectedIds((order.issueTags ?? []).map((t) => t.id));
    setStatus((order.issueStatus || '').toLowerCase() === 'done' ? 'Done' : 'Processing');
    setNewLabel('');
    setChatError(null);
    setTagPickerOpen(false);
    setTagManageOpen(false);
    setEditingTagId(null);
    setEditingLabel('');
    setTagPersisting(false);
    setLoadingLib(true);
    void send({ type: 'LIST_ISSUE_TAGS' })
      .then((res) => {
        const r = res as ListIssueTagsResponse;
        if (openOrderIdRef.current !== order.id) return;
        if (!r.ok) {
          setChatError(r.message || 'Không tải được nhãn.');
          return;
        }
        if (r.tags) setLibrary(r.tags);
      })
      .catch((err) => {
        if (openOrderIdRef.current !== order.id) return;
        setChatError(err instanceof Error ? err.message : 'Không tải được nhãn.');
      })
      .finally(() => setLoadingLib(false));

    const hint = suggest?.trim();
    if (!(order.issueTags?.length) && hint) {
      setNewLabel(hint.slice(0, TAG_LABEL_MAX));
      setTagPickerOpen(true);
      setTagManageOpen(true);
    }

    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, order?.id, suggest]);

  useEffect(() => {
    if (!tagPickerOpen) return;
    const onDoc = (ev: MouseEvent) => {
      const el = pickerRef.current;
      if (!el) return;
      if (ev.target instanceof Node && !el.contains(ev.target)) {
        setTagPickerOpen(false);
        setTagManageOpen(false);
        setEditingTagId(null);
      }
    };
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [tagPickerOpen]);

  useEffect(() => {
    return () => {
      if (tagPersistTimerRef.current) window.clearTimeout(tagPersistTimerRef.current);
    };
  }, []);

  if (!open || !order) return null;

  const orderLabel = order.platformOrderId || order.id.slice(0, 8);
  const sellerLabel = order.ownerName?.trim() || null;
  const busy = savingTags || creatingTag || tagPersisting;
  const tagsBusy = savingTags || tagPersisting;
  const selectedTags = library.filter((t) => selectedIds.includes(t.id));
  // Tags on order but not yet in library (race) — still show.
  const orphanSelected = (order.issueTags ?? []).filter(
    (t) => selectedIds.includes(t.id) && !selectedTags.some((x) => x.id === t.id),
  );
  const displaySelected = [...selectedTags, ...orphanSelected];

  const toggleTag = (id: string, fromPicker = false) => {
    if (tagsBusy) return;
    // Chọn xong → đóng panel ngay; chip hiện trên toolbar; gửi API ngầm.
    if (fromPicker || tagPickerOpen) closeTagPickerUi();
    const next = selectedIds.includes(id)
      ? selectedIds.filter((x) => x !== id)
      : [...selectedIds, id];
    schedulePersistTags(next, fromPicker || tagPickerOpen ? 0 : TAG_PERSIST_DEBOUNCE_MS);
  };

  const toggleIssueDone = () => {
    if (savingTags) return;
    const next: 'Processing' | 'Done' = status === 'Done' ? 'Processing' : 'Done';
    setStatus(next);
    statusRef.current = next;
    void onSaveTags({ status: next });
  };

  const addCustom = async () => {
    const label = newLabel.trim();
    if (!label || creatingTag) return;
    setCreatingTag(true);
    try {
      const res = (await send({ type: 'CREATE_ISSUE_TAG', label })) as CreateIssueTagResponse;
      if (res.ok && res.tag) {
        setLibrary((prev) =>
          prev.some((t) => t.id === res.tag!.id) ? prev : [...prev, res.tag!],
        );
        setNewLabel('');
      }
    } finally {
      setCreatingTag(false);
    }
  };

  const startEditTag = (tag: FulfilIssueTag) => {
    setEditingTagId(tag.id);
    setEditingLabel(tag.label);
  };

  const cancelEditTag = () => {
    setEditingTagId(null);
    setEditingLabel('');
  };

  const saveEditTag = async () => {
    if (!editingTagId) return;
    const label = editingLabel.trim();
    if (!label || savingEdit) return;
    setSavingEdit(true);
    try {
      const res = (await send({
        type: 'UPDATE_ISSUE_TAG',
        tagId: editingTagId,
        label,
      })) as UpdateIssueTagResponse;
      if (res.ok && res.tag) {
        setLibrary((prev) => prev.map((t) => (t.id === res.tag!.id ? res.tag! : t)));
        cancelEditTag();
      }
    } finally {
      setSavingEdit(false);
    }
  };

  const removeLibraryTag = async (tag: FulfilIssueTag) => {
    if (tag.isSystem || deletingTagId) return;
    const ok = window.confirm(`Xóa nhãn «${tag.label}»?\nNhãn sẽ gỡ khỏi mọi đơn đang gắn.`);
    if (!ok) return;
    setDeletingTagId(tag.id);
    try {
      const res = (await send({
        type: 'DELETE_ISSUE_TAG',
        tagId: tag.id,
      })) as DeleteIssueTagResponse;
      if (res.ok) {
        setLibrary((prev) => prev.filter((t) => t.id !== tag.id));
        if (selectedIds.includes(tag.id)) {
          schedulePersistTags(selectedIds.filter((id) => id !== tag.id));
        }
      }
    } finally {
      setDeletingTagId(null);
    }
  };

  const openTagManage = () => {
    setEditingTagId(null);
    setEditingLabel('');
    setTagManageOpen(true);
  };

  const backToTagPicker = () => {
    setTagManageOpen(false);
    setEditingTagId(null);
    setEditingLabel('');
  };

  const requestClose = () => {
    if (busy) return;
    onClose();
  };

  return (
    <div className="msg-modal-backdrop" role="presentation" onClick={requestClose}>
      <div
        className="msg-modal msg-modal--chat"
        role="dialog"
        aria-modal="true"
        aria-labelledby="msg-modal-title"
        onClick={(ev) => ev.stopPropagation()}
      >
        <header className="msg-chat-header">
          <div className="msg-chat-header-main">
            <div id="msg-modal-title" className="msg-chat-header-title">
              Tag lỗi
            </div>
            <div className="msg-chat-header-meta">
              <span className="msg-chat-order-id" title={orderLabel}>
                {orderLabel}
              </span>
              {sellerLabel ? (
                <>
                  <span className="msg-chat-dot" aria-hidden>
                    ·
                  </span>
                  <span className="msg-chat-seller" title={sellerLabel}>
                    {sellerLabel}
                  </span>
                </>
              ) : null}
            </div>
          </div>
          <button
            type="button"
            className="msg-chat-close"
            title="Đóng"
            aria-label="Đóng"
            disabled={busy}
            onClick={requestClose}
          >
            <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden focusable="false">
              <path
                fill="currentColor"
                d="M18.3 5.71a1 1 0 0 0-1.41 0L12 10.59 7.11 5.7A1 1 0 0 0 5.7 7.11L10.59 12l-4.9 4.89a1 1 0 1 0 1.41 1.42L12 13.41l4.89 4.9a1 1 0 0 0 1.42-1.41L13.41 12l4.9-4.89a1 1 0 0 0-.01-1.4z"
              />
            </svg>
          </button>
        </header>

        <OrderChatContextCard
          order={order}
          onOpenProduct={onOpenProduct}
        />

        {chatError ? (
          <div className="msg-chat-error">
            <span>{chatError}</span>
            {isStaleWorkerError(chatError) ? (
              <button
                type="button"
                className="msg-chat-error-reload"
                onClick={() => reloadExtension()}
              >
                Reload extension
              </button>
            ) : null}
          </div>
        ) : null}

        <div className="msg-chat-footer">
          <div className="msg-chat-composer">
            <div className="msg-chat-issue-box">
              <div className="msg-chat-issue-toolbar">
                <div className="msg-chat-presets msg-chat-presets--selected" role="list">
                  {displaySelected.map((tag) => (
                    <button
                      key={tag.id}
                      type="button"
                      role="listitem"
                      className="msg-chat-preset-chip is-active"
                      title="Gỡ khỏi đơn"
                      disabled={tagsBusy}
                      onClick={() => toggleTag(tag.id)}
                    >
                      {tag.label}
                    </button>
                  ))}
                </div>
                <div className="msg-chat-issue-toolbar-actions" ref={pickerRef}>
                  <button
                    type="button"
                    className={`msg-chat-issue-status${
                      status === 'Done' ? ' is-done' : ' is-processing'
                    }`}
                    disabled={tagsBusy}
                    title={
                      status === 'Done'
                        ? 'Đã xong — bấm để mở lại Đang xử lý'
                        : 'Đang xử lý — bấm để đánh dấu Đã xong'
                    }
                    aria-label={status === 'Done' ? 'Đã xong' : 'Đang xử lý'}
                    onClick={toggleIssueDone}
                  >
                    {tagsBusy ? <Spinner dark /> : <CheckCircleIcon filled={status === 'Done'} />}
                  </button>
                  <button
                    type="button"
                    className={`msg-chat-issue-tags${selectedIds.length > 0 ? ' is-active' : ''}`}
                    title="Chọn nhãn"
                    aria-label="Chọn nhãn"
                    aria-expanded={tagPickerOpen}
                    disabled={tagsBusy}
                    onClick={() => {
                      if (tagsBusy) return;
                      setTagPickerOpen((v) => {
                        if (v) {
                          setTagManageOpen(false);
                          setEditingTagId(null);
                        }
                        return !v;
                      });
                    }}
                  >
                    {tagsBusy ? <Spinner dark /> : <TagsIcon />}
                  </button>
                  {tagPickerOpen ? (
                    <div
                      className="msg-chat-tag-popover"
                      role="dialog"
                      aria-label={tagManageOpen ? 'Quản lý nhãn' : 'Chọn nhãn'}
                    >
                      {tagManageOpen ? (
                        <div className="msg-chat-tag-manager">
                          <div className="msg-chat-tag-picker-head">
                            <button
                              type="button"
                              className="msg-chat-tag-back"
                              onClick={backToTagPicker}
                            >
                              <ArrowLeftIcon />
                              Quay lại chọn nhãn
                            </button>
                          </div>
                          <div className="msg-chat-tag-manager-list">
                            {loadingLib ? <span className="muted">Đang tải…</span> : null}
                            {library.map((tag) => {
                              const editing = editingTagId === tag.id;
                              return (
                                <div key={tag.id} className="msg-chat-tag-manager-row">
                                  {editing ? (
                                    <>
                                      <input
                                        className="msg-chat-tag-edit-input"
                                        value={editingLabel}
                                        maxLength={TAG_LABEL_MAX}
                                        autoFocus
                                        disabled={savingEdit}
                                        onChange={(ev) =>
                                          setEditingLabel(ev.target.value.slice(0, TAG_LABEL_MAX))
                                        }
                                        onKeyDown={(ev) => {
                                          if (ev.key === 'Enter') {
                                            ev.preventDefault();
                                            void saveEditTag();
                                          }
                                          if (ev.key === 'Escape') cancelEditTag();
                                        }}
                                      />
                                      <button
                                        type="button"
                                        className="msg-chat-tag-icon-btn is-primary"
                                        title="Lưu"
                                        aria-label="Lưu nhãn"
                                        disabled={savingEdit || !editingLabel.trim()}
                                        onClick={() => void saveEditTag()}
                                      >
                                        {savingEdit ? <Spinner dark /> : <CheckSmallIcon />}
                                      </button>
                                      <button
                                        type="button"
                                        className="msg-chat-tag-icon-btn"
                                        title="Hủy"
                                        aria-label="Hủy sửa"
                                        disabled={savingEdit}
                                        onClick={cancelEditTag}
                                      >
                                        ×
                                      </button>
                                    </>
                                  ) : (
                                    <>
                                      <span
                                        className="msg-chat-tag-manager-label"
                                        title={tag.label}
                                      >
                                        {tag.label}
                                        {tag.isSystem ? (
                                          <span className="msg-chat-tag-manager-system">
                                            hệ thống
                                          </span>
                                        ) : null}
                                      </span>
                                      <div className="msg-chat-tag-manager-actions">
                                        <button
                                          type="button"
                                          className="msg-chat-tag-icon-btn"
                                          title="Sửa nhãn"
                                          aria-label="Sửa nhãn"
                                          onClick={() => startEditTag(tag)}
                                        >
                                          <EditIcon />
                                        </button>
                                        {!tag.isSystem ? (
                                          <button
                                            type="button"
                                            className="msg-chat-tag-icon-btn is-danger"
                                            title="Xóa nhãn"
                                            aria-label="Xóa nhãn"
                                            disabled={deletingTagId === tag.id}
                                            onClick={() => void removeLibraryTag(tag)}
                                          >
                                            {deletingTagId === tag.id ? (
                                              <Spinner dark />
                                            ) : (
                                              <DeleteIcon />
                                            )}
                                          </button>
                                        ) : null}
                                      </div>
                                    </>
                                  )}
                                </div>
                              );
                            })}
                          </div>
                          <div className="msg-chat-tag-add-row">
                            <input
                              className="msg-chat-tag-add-input"
                              maxLength={TAG_LABEL_MAX}
                              placeholder="Thêm nhãn mới…"
                              value={newLabel}
                              disabled={busy}
                              onChange={(ev) =>
                                setNewLabel(ev.target.value.slice(0, TAG_LABEL_MAX))
                              }
                              onKeyDown={(ev) => {
                                if (ev.key === 'Enter') {
                                  ev.preventDefault();
                                  void addCustom();
                                }
                              }}
                            />
                            <button
                              type="button"
                              className="btn btn-primary msg-chat-tag-add-btn"
                              disabled={busy || !newLabel.trim()}
                              onClick={() => void addCustom()}
                            >
                              {creatingTag ? <Spinner /> : null}
                              Thêm
                            </button>
                          </div>
                        </div>
                      ) : (
                        <div className="msg-chat-tag-picker">
                          <div className="msg-chat-tag-picker-head">
                            <span className="msg-chat-tag-picker-hint">Bấm để gắn / gỡ</span>
                            <button
                              type="button"
                              className="msg-chat-tag-icon-btn"
                              title="Quản lý nhãn"
                              aria-label="Quản lý nhãn"
                              onClick={openTagManage}
                            >
                              <SettingsIcon />
                            </button>
                          </div>
                          <div className="msg-chat-presets msg-chat-presets--picker" role="list">
                            {loadingLib ? <span className="muted">Đang tải…</span> : null}
                            {!loadingLib && library.length === 0 ? (
                              <span className="muted">Chưa có nhãn — mở Quản lý để thêm.</span>
                            ) : null}
                            {library.map((tag) => {
                              const active = selectedIds.includes(tag.id);
                              return (
                                <button
                                  key={tag.id}
                                  type="button"
                                  role="listitem"
                                  className={`msg-chat-preset-chip${active ? ' is-active' : ''}`}
                                  disabled={tagsBusy}
                                  onClick={() => toggleTag(tag.id, true)}
                                >
                                  {tag.label}
                                </button>
                              );
                            })}
                          </div>
                        </div>
                      )}
                    </div>
                  ) : null}
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
