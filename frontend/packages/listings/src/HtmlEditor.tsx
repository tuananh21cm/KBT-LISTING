import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { MouseEvent as ReactMouseEvent } from 'react';
import { Button, Dropdown, InputNumber, Popover, Space, Tooltip } from 'antd';
import type { MenuProps } from 'antd';
import {
  AlignCenterOutlined,
  AlignLeftOutlined,
  AlignRightOutlined,
  BoldOutlined,
  ColumnWidthOutlined,
  CompressOutlined,
  ExpandOutlined,
  FontSizeOutlined,
  ItalicOutlined,
  OrderedListOutlined,
  PictureOutlined,
  UnderlineOutlined,
  UnorderedListOutlined,
} from '@ant-design/icons';
import {
  AddProductImageModal,
  type ProductImagePickerGroup,
} from './AddProductImageModal';

export interface HtmlEditorProps {
  value?: string;
  onChange?: (html: string) => void;
  maxLength?: number;
  placeholder?: string;
  minHeight?: number;
  /** Visible viewport cap (overrides CSS default). Defaults to max(minHeight, 480). */
  maxHeight?: number;
  disabled?: boolean;
  /** Used by image picker (Bunny library / upload). */
  productId?: string;
  /** Listing product / variant color images — first tab in insert-image modal. */
  productImageGroups?: ProductImagePickerGroup[];
}

type ImageAlign = 'left' | 'center' | 'right';

function normalizeEmpty(html: string): string {
  const el = document.createElement('div');
  el.innerHTML = html;
  const text = (el.textContent || el.innerText || '').replace(/\u00a0/g, ' ').trim();
  if (!text) return '';
  return html;
}

function findImageFromNode(node: Node | null, root: HTMLElement): HTMLImageElement | null {
  let cur: Node | null = node;
  while (cur && cur !== root) {
    if (cur instanceof HTMLImageElement) return cur;
    cur = cur.parentNode;
  }
  return null;
}

function applyImageAlign(img: HTMLImageElement, align: ImageAlign) {
  img.style.float = 'none';
  img.style.display = 'block';
  img.removeAttribute('align');

  if (align === 'left') {
    img.style.float = 'left';
    img.style.display = 'inline';
    img.style.margin = '0 12px 8px 0';
  } else if (align === 'right') {
    img.style.float = 'right';
    img.style.display = 'inline';
    img.style.margin = '0 0 8px 12px';
  } else {
    img.style.display = 'block';
    img.style.margin = '8px auto';
  }
}

function applyImageWidth(img: HTMLImageElement, width: string) {
  img.style.width = width;
  img.style.height = 'auto';
  img.style.maxWidth = '100%';
  img.removeAttribute('width');
  img.removeAttribute('height');
}

export function HtmlEditor({
  value = '',
  onChange,
  maxLength = 10000,
  placeholder = 'Nhập mô tả sản phẩm…',
  minHeight = 220,
  maxHeight,
  disabled = false,
  productId,
  productImageGroups,
}: HtmlEditorProps) {
  const resolvedMaxHeight = maxHeight ?? Math.max(minHeight, 480);
  const editorRef = useRef<HTMLDivElement>(null);
  const wrapRef = useRef<HTMLDivElement>(null);
  const savedRange = useRef<Range | null>(null);
  const selectedImgRef = useRef<HTMLImageElement | null>(null);
  const resizeState = useRef<{ startX: number; startW: number } | null>(null);

  const [fullscreen, setFullscreen] = useState(false);
  const [imageModalOpen, setImageModalOpen] = useState(false);
  const [charCount, setCharCount] = useState(() => (value || '').length);
  const [hasSelectedImg, setHasSelectedImg] = useState(false);
  const [customWidth, setCustomWidth] = useState<number | null>(null);
  const [handlePos, setHandlePos] = useState<{ left: number; top: number } | null>(null);

  const emitChange = useCallback(() => {
    const el = editorRef.current;
    if (!el) return;
    const html = normalizeEmpty(el.innerHTML);
    setCharCount(html.length);
    if (html.length > maxLength) return;
    onChange?.(html);
  }, [maxLength, onChange]);

  const updateHandlePos = useCallback(() => {
    const img = selectedImgRef.current;
    const wrap = wrapRef.current;
    if (!img || !wrap) {
      setHandlePos(null);
      return;
    }
    const ir = img.getBoundingClientRect();
    const wr = wrap.getBoundingClientRect();
    setHandlePos({
      left: ir.right - wr.left - 6,
      top: ir.bottom - wr.top - 6,
    });
  }, []);

  const clearImageSelection = useCallback(() => {
    selectedImgRef.current?.classList.remove('is-selected');
    selectedImgRef.current = null;
    setHasSelectedImg(false);
    setCustomWidth(null);
    setHandlePos(null);
  }, []);

  const selectImage = useCallback(
    (img: HTMLImageElement) => {
      if (selectedImgRef.current && selectedImgRef.current !== img) {
        selectedImgRef.current.classList.remove('is-selected');
      }
      img.classList.add('is-selected');
      selectedImgRef.current = img;
      setHasSelectedImg(true);
      const raw = img.style.width || '';
      const parsed = Number.parseInt(raw, 10);
      setCustomWidth(Number.isFinite(parsed) ? parsed : Math.round(img.getBoundingClientRect().width));
      requestAnimationFrame(updateHandlePos);
    },
    [updateHandlePos],
  );

  useEffect(() => {
    const el = editorRef.current;
    if (!el) return;
    const next = value || '';
    if (document.activeElement === el) return;
    if (el.innerHTML !== next) {
      el.innerHTML = next;
      clearImageSelection();
    }
    setCharCount(next.length);
  }, [value, clearImageSelection]);

  useEffect(() => {
    if (!fullscreen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setFullscreen(false);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [fullscreen]);

  useLayoutEffect(() => {
    if (!hasSelectedImg) return;
    updateHandlePos();
    const editor = editorRef.current;
    const onScrollOrResize = () => updateHandlePos();
    editor?.addEventListener('scroll', onScrollOrResize);
    window.addEventListener('resize', onScrollOrResize);
    return () => {
      editor?.removeEventListener('scroll', onScrollOrResize);
      window.removeEventListener('resize', onScrollOrResize);
    };
  }, [hasSelectedImg, updateHandlePos, fullscreen]);

  useEffect(() => {
    const onMouseMove = (e: MouseEvent) => {
      const state = resizeState.current;
      const img = selectedImgRef.current;
      if (!state || !img) return;
      const delta = e.clientX - state.startX;
      const next = Math.max(40, Math.round(state.startW + delta));
      applyImageWidth(img, `${next}px`);
      setCustomWidth(next);
      updateHandlePos();
    };
    const onMouseUp = () => {
      if (!resizeState.current) return;
      resizeState.current = null;
      emitChange();
      updateHandlePos();
    };
    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
    return () => {
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    };
  }, [emitChange, updateHandlePos]);

  const rememberSelection = () => {
    const sel = window.getSelection();
    if (sel && sel.rangeCount > 0) {
      const range = sel.getRangeAt(0);
      if (editorRef.current?.contains(range.commonAncestorContainer)) {
        savedRange.current = range.cloneRange();
      }
    }
  };

  const restoreSelection = () => {
    const el = editorRef.current;
    if (!el) return;
    el.focus();
    const sel = window.getSelection();
    if (!sel) return;
    sel.removeAllRanges();
    if (savedRange.current) {
      sel.addRange(savedRange.current);
    } else {
      const range = document.createRange();
      range.selectNodeContents(el);
      range.collapse(false);
      sel.addRange(range);
    }
  };

  const run = (command: string, commandValue?: string) => {
    if (disabled) return;
    editorRef.current?.focus();
    document.execCommand(command, false, commandValue);
    emitChange();
  };

  const alignContent = (align: ImageAlign) => {
    if (disabled) return;
    const img = selectedImgRef.current;
    if (img) {
      applyImageAlign(img, align);
      emitChange();
      requestAnimationFrame(updateHandlePos);
      return;
    }
    if (align === 'left') run('justifyLeft');
    else if (align === 'center') run('justifyCenter');
    else run('justifyRight');
  };

  const setSelectedImageWidth = (width: string) => {
    const img = selectedImgRef.current;
    if (!img || disabled) return;
    applyImageWidth(img, width);
    const parsed = Number.parseInt(width, 10);
    setCustomWidth(Number.isFinite(parsed) ? parsed : null);
    emitChange();
    requestAnimationFrame(updateHandlePos);
  };

  const openImagePicker = () => {
    if (disabled) return;
    rememberSelection();
    setImageModalOpen(true);
  };

  const insertImages = (urls: string[]) => {
    if (urls.length === 0) return;
    restoreSelection();
    clearImageSelection();
    for (const url of urls) {
      const safe = url.trim();
      if (!safe) continue;
      // TikTok description requires positive integer width/height on <img>.
      const imgHtml = `<img src="${safe.replace(/"/g, '&quot;')}" alt="" width="800" height="800" style="width:100%;max-width:100%;height:auto;display:block;margin:8px auto;" />`;
      document.execCommand('insertHTML', false, imgHtml);
    }
    emitChange();
  };

  const onEditorMouseDown = (e: ReactMouseEvent<HTMLDivElement>) => {
    const root = editorRef.current;
    if (!root || disabled) return;
    const target = e.target as HTMLElement;
    const img = findImageFromNode(target, root);
    if (img) {
      e.preventDefault();
      selectImage(img);
      return;
    }
    clearImageSelection();
  };

  const startResize = (e: ReactMouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    const img = selectedImgRef.current;
    if (!img) return;
    resizeState.current = {
      startX: e.clientX,
      startW: img.getBoundingClientRect().width,
    };
  };

  const headingItems: MenuProps['items'] = [
    { key: 'p', label: 'Paragraph', onClick: () => run('formatBlock', 'p') },
    { key: 'h1', label: 'Heading 1', onClick: () => run('formatBlock', 'h1') },
    { key: 'h2', label: 'Heading 2', onClick: () => run('formatBlock', 'h2') },
    { key: 'h3', label: 'Heading 3', onClick: () => run('formatBlock', 'h3') },
  ];

  const alignItems: MenuProps['items'] = [
    {
      key: 'left',
      label: (
        <Space>
          <AlignLeftOutlined /> Left
        </Space>
      ),
      onClick: () => alignContent('left'),
    },
    {
      key: 'center',
      label: (
        <Space>
          <AlignCenterOutlined /> Center
        </Space>
      ),
      onClick: () => alignContent('center'),
    },
    {
      key: 'right',
      label: (
        <Space>
          <AlignRightOutlined /> Right
        </Space>
      ),
      onClick: () => alignContent('right'),
    },
  ];

  const sizePopover = (
    <div className="fs-img-size-popover">
      <div className="fs-img-size-presets">
        {(['25%', '50%', '75%', '100%'] as const).map((w) => (
          <Button key={w} size="small" onClick={() => setSelectedImageWidth(w)}>
            {w}
          </Button>
        ))}
      </div>
      <Space.Compact size="small" className="fs-img-size-input">
        <InputNumber
          size="small"
          min={40}
          max={2000}
          value={customWidth ?? undefined}
          placeholder="Width"
          onChange={(v) => setCustomWidth(typeof v === 'number' ? v : null)}
          onPressEnter={() => {
            if (customWidth != null) setSelectedImageWidth(`${customWidth}px`);
          }}
          onBlur={() => {
            if (customWidth != null) setSelectedImageWidth(`${customWidth}px`);
          }}
        />
        <Button size="small" disabled tabIndex={-1}>
          px
        </Button>
      </Space.Compact>
      <div className="fs-img-size-hint">
        Click ảnh → Align (trái/giữa/phải) · nút kích thước · kéo góc dưới-phải
      </div>
    </div>
  );

  return (
    <>
      <div
        className={`fs-desc-box${disabled ? ' is-disabled' : ''}${fullscreen ? ' is-fullscreen' : ''}`}
      >
        <div className="fs-html-toolbar">
          <div className="fs-html-toolbar-left">
            <Tooltip title="Bullet list">
              <Button
                type="text"
                size="small"
                icon={<UnorderedListOutlined />}
                disabled={disabled}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => run('insertUnorderedList')}
              />
            </Tooltip>
            <Tooltip title="Numbered list">
              <Button
                type="text"
                size="small"
                icon={<OrderedListOutlined />}
                disabled={disabled}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => run('insertOrderedList')}
              />
            </Tooltip>
            <Dropdown menu={{ items: headingItems }} trigger={['click']} disabled={disabled}>
              <Button
                type="text"
                size="small"
                disabled={disabled}
                onMouseDown={(e) => e.preventDefault()}
              >
                <FontSizeOutlined /> H
              </Button>
            </Dropdown>
            <Tooltip title="Bold">
              <Button
                type="text"
                size="small"
                icon={<BoldOutlined />}
                disabled={disabled}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => run('bold')}
              />
            </Tooltip>
            <Tooltip title="Italic">
              <Button
                type="text"
                size="small"
                icon={<ItalicOutlined />}
                disabled={disabled}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => run('italic')}
              />
            </Tooltip>
            <Tooltip title="Underline">
              <Button
                type="text"
                size="small"
                icon={<UnderlineOutlined />}
                disabled={disabled}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => run('underline')}
              />
            </Tooltip>
            <Dropdown menu={{ items: alignItems }} trigger={['click']} disabled={disabled}>
              <Button
                type="text"
                size="small"
                icon={<AlignLeftOutlined />}
                disabled={disabled}
                onMouseDown={(e) => e.preventDefault()}
                title={hasSelectedImg ? 'Căn ảnh' : 'Căn chữ'}
              />
            </Dropdown>
            <Tooltip title="Chèn ảnh">
              <Button
                type="text"
                size="small"
                icon={<PictureOutlined />}
                disabled={disabled}
                onMouseDown={(e) => e.preventDefault()}
                onClick={openImagePicker}
              />
            </Tooltip>
            <Popover
              content={sizePopover}
              title="Kích thước ảnh"
              trigger="click"
              placement="bottomLeft"
            >
              <Tooltip title={hasSelectedImg ? 'Đổi kích thước ảnh' : 'Chọn ảnh trước'}>
                <Button
                  type="text"
                  size="small"
                  icon={<ColumnWidthOutlined />}
                  disabled={disabled || !hasSelectedImg}
                  onMouseDown={(e) => e.preventDefault()}
                />
              </Tooltip>
            </Popover>
            <Tooltip title={fullscreen ? 'Exit fullscreen' : 'Fullscreen'}>
              <Button
                type="text"
                size="small"
                icon={fullscreen ? <CompressOutlined /> : <ExpandOutlined />}
                disabled={disabled}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => setFullscreen((v) => !v)}
              />
            </Tooltip>
          </div>
          <span className="fs-html-toolbar-count">
            {charCount} / {maxLength}
          </span>
        </div>
        <div ref={wrapRef} className="fs-html-editor-wrap">
          <div
            ref={editorRef}
            className="fs-html-editor-body"
            contentEditable={!disabled}
            suppressContentEditableWarning
            data-placeholder={placeholder}
            style={{
              minHeight: fullscreen ? 'calc(100vh - 120px)' : minHeight,
              maxHeight: fullscreen ? 'none' : resolvedMaxHeight,
            }}
            onInput={emitChange}
            onBlur={emitChange}
            onMouseDown={onEditorMouseDown}
          />
          {hasSelectedImg && !disabled && handlePos && (
            <span
              className="fs-img-resize-handle"
              title="Kéo để đổi kích thước"
              style={{ left: handlePos.left, top: handlePos.top }}
              onMouseDown={startResize}
            />
          )}
        </div>
      </div>

      <AddProductImageModal
        open={imageModalOpen}
        title="Chèn ảnh vào mô tả"
        productId={productId}
        productImageGroups={productImageGroups}
        onCancel={() => setImageModalOpen(false)}
        onAdd={insertImages}
      />
    </>
  );
}
