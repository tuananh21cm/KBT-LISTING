import { lazy, Suspense, useMemo } from 'react';
import { Button, Card, Form, Select, Skeleton, Space, Typography, message } from 'antd';
import {
  PictureOutlined,
  ThunderboltOutlined,
  UndoOutlined,
} from '@ant-design/icons';
import type { FormInstance } from 'antd/es/form';
import type { ListingImageRow } from './productFormTypes';
import type { ProductImagePickerGroup } from './AddProductImageModal';
import { DeferredSection } from './DeferredSection';
import { useDeferredMount } from './useDeferredMount';

const HtmlEditor = lazy(() =>
  import('./HtmlEditor').then((m) => ({ default: m.HtmlEditor })),
);

const { Text } = Typography;

/** Full-bleed row; width/height attrs stay integers for TikTok API. */
const DESC_IMG_CENTER =
  'width:100%;max-width:100%;height:auto;display:block;margin:8px auto;';
/** TikTok requires positive integer width/height on description <img> (≤4000). */
const DESC_IMG_WIDTH = 800;
const DESC_IMG_HEIGHT = 800;
const DESC_HTML_MAX = 10000;
/**
 * TikTok Shop / 4Seller: 1 image per row only. Cap Default (Main) gallery inserts
 * so the description stays short (max 3 images after sections).
 */
const MAX_DESC_INSERT_IMAGES = 3;

function isSharedProductImage(img?: {
  mediaType?: string;
  colorValue?: string;
  listingSkuId?: string;
}) {
  if (!img) return false;
  if (img.colorValue?.trim()) return false;
  if (img.listingSkuId) return false;
  return (
    !img.mediaType || img.mediaType === 'Main' || img.mediaType === 'Detail'
  );
}

function isVariantColorImage(
  img?: { mediaType?: string; colorValue?: string },
  color?: string,
) {
  if (!img?.colorValue?.trim()) return false;
  if (
    color &&
    img.colorValue.trim().toLowerCase() !== color.trim().toLowerCase()
  ) {
    return false;
  }
  return (
    !img.mediaType || img.mediaType === 'Main' || img.mediaType === 'Detail'
  );
}

function escapeAttr(url: string): string {
  return url.trim().replace(/"/g, '&quot;');
}

/** Single full-width description image — one per row (TikTok-safe; no table). */
function imgHtml(url: string): string {
  return `<img src="${escapeAttr(url)}" alt="" width="${DESC_IMG_WIDTH}" height="${DESC_IMG_HEIGHT}" style="${DESC_IMG_CENTER}" />`;
}

/**
 * Default (Main/Detail) gallery URLs in sortOrder, capped for description insert.
 * Size Chart and variant-color images are not used here.
 */
function collectDescriptionInsertUrls(images: ListingImageRow[]): string[] {
  const mains: string[] = [];
  const seen = new Set<string>();
  for (const img of images
    .filter(
      (i) => isSharedProductImage(i) && String(i.url ?? '').trim(),
    )
    .slice()
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))) {
    const url = String(img.url).trim();
    if (seen.has(url)) continue;
    seen.add(url);
    mains.push(url);
    if (mains.length >= MAX_DESC_INSERT_IMAGES) break;
  }
  return mains;
}

const TEXT_BLOCK_TAGS = new Set([
  'P',
  'H1',
  'H2',
  'H3',
  'H4',
  'UL',
  'OL',
  'BLOCKQUOTE',
]);

function hasText(el: Element): boolean {
  return (el.textContent || '').replace(/\u00a0/g, ' ').trim().length > 0;
}

function isMeaningfulTextBlock(el: Element): boolean {
  if (TEXT_BLOCK_TAGS.has(el.tagName)) return hasText(el);
  // contenteditable / paste often uses bare <div> as a paragraph
  if (el.tagName === 'DIV') {
    if (el.querySelector('p, h1, h2, h3, h4, ul, ol, blockquote')) return false;
    if (el.querySelector('img, table')) return false;
    return hasText(el);
  }
  return false;
}

const HEADING_TAGS = new Set(['H1', 'H2', 'H3', 'H4', 'H5', 'H6']);

/** Left-aligned plain bullet lines from AI rewrite (`<p>- …</p>`), not nested lists. */
function isBulletParagraph(el: Element): boolean {
  if (el.tagName !== 'P') return false;
  const t = (el.textContent || '').replace(/\u00a0/g, ' ').trim();
  return /^[-*•·–—]\s+\S/.test(t);
}

/** Trailing SEO hashtag line from AI rewrite — never an Insert Images anchor. */
function isHashtagParagraph(el: Element): boolean {
  if (el.tagName !== 'P') return false;
  const t = (el.textContent || '').replace(/\u00a0/g, ' ').trim();
  if (!t.includes('#')) return false;
  const tags = t.match(/#[a-z0-9]+/gi) ?? [];
  if (tags.length < 2) return false;
  const tagChars = tags.reduce((n, tag) => n + tag.length, 0);
  return tagChars * 2 >= t.replace(/\s+/g, '').length;
}

/**
 * Insertion anchors, skipping headings: AI rewrites emit `<h3>Section</h3><ul>…</ul>`,
 * and anchoring on the heading would drop the images between a section title and the
 * list it introduces. Anchoring on the content block puts them at the section's end.
 * Plain-text TTS bullets are one `<p>- …</p>` each — only the last of a consecutive
 * run is an anchor so images still land after each section, not between every line.
 * The trailing hashtag line is skipped so images never fall after the SEO tag wall.
 */
function findImageAnchorBlocks(root: Element): Element[] {
  const blocks = findTextBlocks(root).filter(
    (el) => !HEADING_TAGS.has(el.tagName) && !isHashtagParagraph(el),
  );
  const anchors: Element[] = [];
  for (let i = 0; i < blocks.length; i++) {
    const el = blocks[i]!;
    if (isBulletParagraph(el)) {
      const next = blocks[i + 1];
      if (next && isBulletParagraph(next)) continue;
    }
    anchors.push(el);
  }
  return anchors;
}

/** Top-level text blocks; unwrap a single wrapper <div> if needed. */
function findTextBlocks(root: Element): Element[] {
  const direct = Array.from(root.children).filter(isMeaningfulTextBlock);
  if (direct.length > 0) return direct;

  const blocks: Element[] = [];
  const walk = (parent: Element) => {
    for (const child of Array.from(parent.children)) {
      if (isMeaningfulTextBlock(child)) {
        blocks.push(child);
      } else if (
        child.tagName === 'DIV' &&
        child.querySelector('p, h1, h2, h3, ul, ol, blockquote, div')
      ) {
        walk(child);
      }
    }
  };
  walk(root);
  return blocks;
}

/**
 * Drop every <img> in description HTML (and empty wrapper tables/divs/p).
 * Used before re-insert so "Insert Images" always replaces old images.
 */
function stripAllDescriptionImages(html: string): string {
  if (!html.trim()) return html;
  const doc = new DOMParser().parseFromString(
    `<div id="__desc_root__">${html}</div>`,
    'text/html',
  );
  const root = doc.getElementById('__desc_root__');
  if (!root) return html;

  for (const img of Array.from(root.querySelectorAll('img'))) {
    const parent = img.parentElement;
    img.remove();
    // Clean empty <td> / lone row table left behind.
    if (parent?.tagName === 'TD') {
      const tr = parent.parentElement;
      parent.remove();
      if (tr?.tagName === 'TR' && tr.children.length === 0) {
        const table = tr.closest('table');
        tr.remove();
        if (table && !table.querySelector('img')) table.remove();
      }
    } else if (
      parent &&
      (parent.tagName === 'DIV' || parent.tagName === 'P') &&
      !(parent.textContent || '').trim() &&
      !parent.querySelector('img')
    ) {
      parent.remove();
    }
  }

  // Remove empty tables.
  for (const table of Array.from(root.querySelectorAll('table'))) {
    if (!table.querySelector('img') && !(table.textContent || '').trim()) {
      table.remove();
    }
  }

  return root.innerHTML;
}

/**
 * Insert up to 3 Default gallery images — one <img> per row after successive
 * content sections (headings skipped). Extra images (more images than sections)
 * go after the last section, still one per line. Strips existing images first.
 * Size Chart / variant-color images are not inserted (manual HtmlEditor only).
 */
function insertImagesByParagraphs(html: string, urls: string[]): string {
  const cleaned = stripAllDescriptionImages(html);
  if (urls.length === 0) return cleaned;

  const doc = new DOMParser().parseFromString(
    `<div id="__desc_root__">${cleaned}</div>`,
    'text/html',
  );
  const root = doc.getElementById('__desc_root__');
  if (!root) {
    const fallback = urls.map((url) => imgHtml(url)).join('');
    return `${cleaned.trimEnd()}${cleaned.trim() ? '\n' : ''}${fallback}`;
  }

  const blocks = findImageAnchorBlocks(root);
  if (blocks.length === 0) {
    for (const url of urls) {
      const wrap = doc.createElement('div');
      wrap.innerHTML = imgHtml(url);
      while (wrap.firstChild) root.appendChild(wrap.firstChild);
    }
    return root.innerHTML;
  }

  // 1 image after §1, §2, …; leftovers after the last section (reverse insert keeps order).
  const insertions: { after: Element; url: string }[] = [];
  for (let i = 0; i < urls.length; i++) {
    const block = blocks[Math.min(i, blocks.length - 1)]!;
    insertions.push({ after: block, url: urls[i]! });
  }

  for (let i = insertions.length - 1; i >= 0; i--) {
    const item = insertions[i]!;
    const wrap = doc.createElement('div');
    wrap.innerHTML = imgHtml(item.url);
    const node = wrap.firstChild;
    if (node) item.after.parentNode?.insertBefore(node, item.after.nextSibling);
  }

  return root.innerHTML;
}

function buildDescriptionImageGroups(
  images: ListingImageRow[],
  selectedColors: string[],
): ProductImagePickerGroup[] {
  const groups: ProductImagePickerGroup[] = [];
  const defaultImgs = images.filter(
    (img) => isSharedProductImage(img) && String(img.url ?? '').trim(),
  );
  if (defaultImgs.length > 0) {
    groups.push({
      label: 'Default',
      images: defaultImgs.map((img, i) => ({
        id: `default-${i}-${img.url}`,
        url: String(img.url).trim(),
      })),
    });
  }
  for (const color of selectedColors) {
    const colorImgs = images.filter(
      (img) =>
        isVariantColorImage(img, color) && String(img.url ?? '').trim(),
    );
    if (colorImgs.length === 0) continue;
    groups.push({
      label: color,
      images: colorImgs.map((img, i) => ({
        id: `color-${color}-${i}-${img.url}`,
        url: String(img.url).trim(),
      })),
    });
  }
  return groups;
}

export type ListingDescriptionCardProps = {
  form: FormInstance;
  selectedColors: string[];
  rewritePending: boolean;
  onAiRewrite: () => void;
  /** True once an AI rewrite has been applied and the previous text is recoverable. */
  canUndoRewrite?: boolean;
  onUndoRewrite?: () => void;
  /** Fired after Insert Images successfully updates descriptionHtml. */
  onImagesInserted?: () => void;
  /** Checklist: AI Rewrite đã áp dụng (Publish gate). */
  rewriteDone?: boolean;
  /** When true, Rewrite is disabled (already done and repeat is not allowed). */
  rewriteLocked?: boolean;
  /** Optional: Insert Images đã chạy sau Rewrite (không chặn Publish). */
  imagesInsertedDone?: boolean;
  /** Parent already deferred media — still delay editor chunk one tick. */
  enabled?: boolean;
  /** Tooltip for the Rewrite button (title-mode aware). */
  rewriteTitleTooltip?: string;
};

/**
 * Description card with deferred + lazy HtmlEditor so first paint stays light.
 * Owns its own `images` watch so typing in the editor does not need parent re-renders for image groups.
 */
export function ListingDescriptionCard({
  form,
  selectedColors,
  rewritePending,
  onAiRewrite,
  canUndoRewrite = false,
  onUndoRewrite,
  onImagesInserted,
  rewriteDone = false,
  rewriteLocked,
  imagesInsertedDone = false,
  enabled = true,
  rewriteTitleTooltip,
}: ListingDescriptionCardProps) {
  const editorReady = useDeferredMount(enabled, 0);
  const rewriteButtonLocked = rewriteLocked ?? rewriteDone;
  const watchedImages =
    (Form.useWatch('images', form) as ListingImageRow[] | undefined) ?? [];

  const productImageGroups = useMemo(
    () => buildDescriptionImageGroups(watchedImages, selectedColors),
    [watchedImages, selectedColors],
  );

  const applyDescriptionHtml = (next: string, current: string) => {
    if (next.length > DESC_HTML_MAX) {
      message.error(
        `Chèn ảnh sẽ vượt giới hạn ${DESC_HTML_MAX} ký tự (còn trống ${Math.max(0, DESC_HTML_MAX - current.length)}).`,
      );
      return false;
    }
    form.setFieldsValue({ descriptionHtml: next });
    return true;
  };

  /** Insert ≤3 Default images: 1 per row after successive sections. */
  const insertGalleryImagesByParagraphs = () => {
    const mains = collectDescriptionInsertUrls(watchedImages);
    if (mains.length === 0) {
      message.warning('Chưa có ảnh Default (Main) để chèn.');
      return;
    }

    const current = String(form.getFieldValue('descriptionHtml') ?? '');
    const next = insertImagesByParagraphs(current, mains);
    if (!applyDescriptionHtml(next, current)) return;

    onImagesInserted?.();
    message.success(
      `Đã chèn ${mains.length} ảnh Default (1 ảnh/dòng sau các section).`,
    );
  };

  if (!editorReady) {
    return <DeferredSection title="Description" minHeight={760} rows={14} />;
  }

  return (
    <Card size="small" style={{ marginBottom: 16 }} className="fs-field-card">
      <div style={{ marginBottom: 0 }}>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 8,
            marginBottom: 4,
          }}
        >
          <Text strong>
            Description <span style={{ color: '#ff4d4f' }}>*</span>
          </Text>
          <Space size={8} wrap>
            <Button
              type="default"
              icon={<ThunderboltOutlined />}
              loading={rewritePending}
              disabled={rewriteButtonLocked}
              onClick={onAiRewrite}
              title={
                rewriteTitleTooltip
                  ?? (rewriteButtonLocked
                    ? 'Đã Rewrite — dùng Hoàn tác nếu muốn viết lại'
                    : 'AI viết lại tiêu đề, mô tả, search terms và highlights')
              }
              style={{
                flexShrink: 0,
                color: rewriteDone ? '#389e0d' : '#722ed1',
                borderColor: rewriteDone ? '#b7eb8f' : '#d3adf7',
                background: rewriteDone ? '#f6ffed' : '#f9f0ff',
              }}
            >
              {rewriteDone ? '1. Rewrite ✓' : '1. Rewrite'}
            </Button>
            {canUndoRewrite && onUndoRewrite ? (
              <Button
                type="default"
                icon={<UndoOutlined />}
                onClick={onUndoRewrite}
                title="Khôi phục tiêu đề, mô tả, search terms và highlights trước khi AI viết lại"
                style={{ flexShrink: 0 }}
              >
                Hoàn tác
              </Button>
            ) : null}
            <Button
              type="default"
              icon={<PictureOutlined />}
              disabled={!rewriteDone}
              onClick={insertGalleryImagesByParagraphs}
              title={
                !rewriteDone
                  ? 'Cần 1. Rewrite trước khi chèn ảnh (chèn ảnh là tùy chọn)'
                  : imagesInsertedDone
                    ? 'Chèn lại ảnh vào mô tả (xóa ảnh cũ rồi chèn mới). Không bắt buộc.'
                    : `Tùy chọn: xóa ảnh cũ rồi chèn tối đa ${MAX_DESC_INSERT_IMAGES} ảnh từ Default (Main), 1 ảnh/dòng sau từng section. Có thể bỏ qua.`
              }
              style={{
                flexShrink: 0,
                color: imagesInsertedDone
                  ? '#389e0d'
                  : !rewriteDone
                    ? undefined
                    : '#08979c',
                borderColor: imagesInsertedDone
                  ? '#b7eb8f'
                  : !rewriteDone
                    ? undefined
                    : '#87e8de',
                background: imagesInsertedDone
                  ? '#f6ffed'
                  : !rewriteDone
                    ? undefined
                    : '#e6fffb',
              }}
            >
              {imagesInsertedDone ? '2. Images ✓' : '2. Images'}
            </Button>
          </Space>
        </div>
        {/* Suspense MUST wrap Form.Item (not sit inside it): Form.Item injects
            value/onChange into its direct child — a Suspense boundary would swallow
            them and HtmlEditor would never receive the description value. */}
        <Suspense
          fallback={<Skeleton active paragraph={{ rows: 8 }} title={false} />}
        >
          <Form.Item
            name="descriptionHtml"
            rules={[
              {
                validator: async (_, value?: string) => {
                  const html = value ?? '';
                  if (html.length > DESC_HTML_MAX) {
                    throw new Error(`Tối đa ${DESC_HTML_MAX} ký tự`);
                  }
                  const text = html
                    .replace(/<[^>]*>/g, ' ')
                    .replace(/&nbsp;/g, ' ')
                    .trim();
                  if (!text) {
                    throw new Error('Can not be empty');
                  }
                },
              },
            ]}
            style={{ marginBottom: 0 }}
          >
            <HtmlEditor
              maxLength={DESC_HTML_MAX}
              minHeight={720}
              maxHeight={720}
              placeholder="Nhập mô tả sản phẩm…"
              productImageGroups={productImageGroups}
            />
          </Form.Item>
        </Suspense>
        <Form.Item
          name="searchTerms"
          label="Search terms"
          style={{ marginTop: 16, marginBottom: 12 }}
        >
          <Select
            mode="tags"
            tokenSeparators={[',']}
            placeholder="Nhập search terms, Enter hoặc dấu phẩy để thêm"
            allowClear
            open={false}
          />
        </Form.Item>
        <Form.Item
          name="productHighlights"
          label="Product highlights"
          style={{ marginBottom: 0 }}
        >
          <Select
            mode="tags"
            tokenSeparators={[',']}
            placeholder="Nhập product highlights, Enter hoặc dấu phẩy để thêm"
            allowClear
            open={false}
          />
        </Form.Item>
      </div>
    </Card>
  );
}
