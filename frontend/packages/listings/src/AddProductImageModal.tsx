import { useEffect, useMemo, useState } from 'react';
import { Button, Empty, Image, Input, Spin, Tabs, Upload, message } from 'antd';
import { Modal } from '@drop/ui';
import {
  AppstoreOutlined,
  FolderOpenOutlined,
  LinkOutlined,
  PlusOutlined,
  DeleteOutlined,
} from '@ant-design/icons';
import type { UploadProps } from 'antd';
import { useListingsDeps } from './ListingsDepsContext';
import type { UserFile } from './localTypes';

export type AddMediaKind = 'image' | 'video';

/** Product / variant color images shown as the first picker tab (insert into description). */
export interface ProductImagePickerGroup {
  label: string;
  images: { id: string; url: string }[];
}

interface AddProductImageModalProps {
  open: boolean;
  title?: string;
  productId?: string;
  kind?: AddMediaKind;
  accept?: string;
  maxSizeMb?: number;
  multiple?: boolean;
  uploadHint?: string;
  /** When set (image kind), first tab lists these product/variant images. */
  productImageGroups?: ProductImagePickerGroup[];
  onCancel: () => void;
  onAdd: (urls: string[]) => void;
}

function formatFileSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function matchesKind(file: UserFile, kind: AddMediaKind): boolean {
  const type = (file.contentType || '').toLowerCase();
  const name = (file.originalFileName || '').toLowerCase();
  if (kind === 'video') {
    return type.startsWith('video/') || /\.(mp4|webm|mov|m4v|avi|mkv)$/i.test(name);
  }
  if (type.startsWith('image/')) return true;
  if (/\.(jpe?g|png|gif|webp|bmp|svg|heic)$/i.test(name)) return true;
  if (!type || type === 'application/octet-stream') {
    return !name.match(/\.(mp4|webm|mov|pdf)$/i);
  }
  return false;
}

function hasPublicUrl(file: UserFile): boolean {
  return !!file.publicUrl?.trim();
}

export function AddProductImageModal({
  open,
  title = 'Thêm ảnh sản phẩm',
  productId,
  kind = 'image',
  accept = 'image/jpeg,image/jpg,image/png,image/webp,image/gif',
  maxSizeMb = 5,
  multiple = true,
  productImageGroups,
  onCancel,
  onAdd,
}: AddProductImageModalProps) {
  const deps = useListingsDeps();
  const uploadFile = deps.useUploadFile();
  const deleteFile = deps.useDeleteFile();
  const {
    data: filesData,
    isLoading: filesLoading,
    isFetching: filesFetching,
    isError: filesError,
    error: filesQueryError,
    refetch: refetchFiles,
  } = deps.useFiles({ enabled: open });
  const files = useMemo<UserFile[]>(
    () => (Array.isArray(filesData) ? filesData : (filesData?.items ?? [])),
    [filesData],
  );
  const showProductTab = kind === 'image' && (productImageGroups?.length ?? 0) > 0;
  const defaultTab = showProductTab ? 'product' : 'library';
  const [activeTab, setActiveTab] = useState(defaultTab);
  const [urlDraft, setUrlDraft] = useState('');
  const [uploading, setUploading] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());

  useEffect(() => {
    if (!open) {
      setActiveTab(defaultTab);
      setUrlDraft('');
      setUploading(false);
      setSelectedIds(new Set());
      return;
    }
    setActiveTab(defaultTab);
    void refetchFiles();
  }, [open, refetchFiles, defaultTab]);

  const libraryFiles = useMemo(() => files.filter((f) => matchesKind(f, kind)), [files, kind]);

  const productImagesFlat = useMemo(
    () => (productImageGroups ?? []).flatMap((g) => g.images),
    [productImageGroups],
  );

  const missingCdnCount = useMemo(
    () => libraryFiles.filter((f) => !hasPublicUrl(f)).length,
    [libraryFiles],
  );

  const commitUrl = () => {
    const url = urlDraft.trim();
    if (!url) {
      message.warning(kind === 'video' ? 'Nhập URL video' : 'Nhập URL ảnh');
      return;
    }
    if (!/^https?:\/\//i.test(url)) {
      message.warning('URL phải bắt đầu bằng http:// hoặc https://');
      return;
    }
    onAdd([url]);
    onCancel();
  };

  const commitLibrary = () => {
    const urls = libraryFiles
      .filter((f) => selectedIds.has(f.id) && hasPublicUrl(f))
      .map((f) => f.publicUrl!.trim());
    if (urls.length === 0) {
      message.warning(
        missingCdnCount > 0
          ? 'File chưa có CDN URL. Cấu hình bunny.cdn_hostname trong Settings rồi upload lại.'
          : kind === 'video'
            ? 'Chọn ít nhất 1 video'
            : 'Chọn ít nhất 1 ảnh',
      );
      return;
    }
    onAdd(urls);
    onCancel();
  };

  const commitProduct = () => {
    const urls = productImagesFlat
      .filter((img) => selectedIds.has(img.id))
      .map((img) => img.url.trim())
      .filter(Boolean);
    if (urls.length === 0) {
      message.warning('Chọn ít nhất 1 ảnh');
      return;
    }
    onAdd(urls);
    onCancel();
  };

  const toggleSelectedId = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
        return next;
      }
      if (!multiple) return new Set([id]);
      next.add(id);
      return next;
    });
  };

  const toggleLibraryFile = (file: UserFile) => {
    if (!hasPublicUrl(file)) {
      message.warning(
        'File này chưa có public CDN URL. Kiểm tra setting bunny.cdn_hostname (vd: xxx.b-cdn.net).',
      );
      return;
    }
    toggleSelectedId(file.id);
  };

  const handleDeleteLibraryFile = async (file: UserFile) => {
    try {
      const res = await deleteFile.mutateAsync(file.id);
      if (!res.success) return;
      setSelectedIds((prev) => {
        if (!prev.has(file.id)) return prev;
        const next = new Set(prev);
        next.delete(file.id);
        return next;
      });
    } catch {
      // error toasted by hook
    }
  };

  const uploadProps: UploadProps = {
    name: 'file',
    multiple: true,
    accept,
    showUploadList: false,
    disabled: uploading,
    beforeUpload: async (file: File) => {
      const isVideo = kind === 'video';
      if (isVideo && !file.type.startsWith('video/')) {
        message.error(`${file.name}: chỉ hỗ trợ file video`);
        return false;
      }
      if (!isVideo && !file.type.startsWith('image/')) {
        message.error(`${file.name}: chỉ hỗ trợ file ảnh`);
        return false;
      }
      if (file.size > maxSizeMb * 1024 * 1024) {
        message.error(`${file.name}: tối đa ${maxSizeMb} MB`);
        return false;
      }

      setUploading(true);
      try {
        const res = await uploadFile.mutateAsync({
          file,
          linkedEntityType: 'Product',
          linkedEntityId: productId,
          metadata: JSON.stringify({ purpose: isVideo ? 'product-video' : 'product-image' }),
        });
        if (!res.success || !res.data) {
          message.error(res.message || `Upload ${file.name} thất bại`);
          return false;
        }
        if (res.data.isDuplicate) {
          message.info(res.message || 'File đã tồn tại — dùng lại URL CDN.');
        } else {
          message.success(res.message || 'Upload thành công.');
        }
        if (!res.data.publicUrl?.trim()) {
          message.warning(
            'Upload OK nhưng chưa có CDN URL. Vào Settings → bunny.cdn_hostname (vd: your-zone.b-cdn.net).',
          );
        } else {
          setSelectedIds((prev) => {
            if (!multiple) return new Set([res.data!.id]);
            const next = new Set(prev);
            next.add(res.data!.id);
            return next;
          });
        }
        await refetchFiles();
      } catch (err) {
        message.error(err instanceof Error ? err.message : `Upload ${file.name} thất bại`);
      } finally {
        setUploading(false);
      }
      return false;
    },
  };

  const footer =
    activeTab === 'library' || activeTab === 'product'
      ? [
          <Button key="cancel" onClick={onCancel}>
            Hủy
          </Button>,
          <Button
            key="ok"
            type="primary"
            disabled={selectedIds.size === 0}
            onClick={activeTab === 'product' ? commitProduct : commitLibrary}
          >
            Thêm{selectedIds.size > 0 ? ` (${selectedIds.size})` : ''}
          </Button>,
        ]
      : [
          <Button key="cancel" onClick={onCancel}>
            Hủy
          </Button>,
          <Button key="ok" type="primary" onClick={commitUrl}>
            Thêm
          </Button>,
        ];

  const productTab = showProductTab
    ? {
        key: 'product',
        label: (
          <span>
            <AppstoreOutlined /> Ảnh sản phẩm
          </span>
        ),
        children: (
          <div className="fs-file-library">
            {productImagesFlat.length === 0 ? (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Chưa có ảnh Default / Variant — thêm ảnh ở phần Product Image trước"
              />
            ) : (
              <div className="fs-product-picker-groups">
                {(productImageGroups ?? []).map((group) =>
                  group.images.length === 0 ? null : (
                    <div key={group.label} className="fs-product-picker-group">
                      <p className="fs-product-picker-group-label">{group.label}</p>
                      <div className="fs-file-library-grid">
                        {group.images.map((img, idx) => {
                          const selected = selectedIds.has(img.id);
                          return (
                            <button
                              key={img.id}
                              type="button"
                              className={[
                                'fs-file-library-item',
                                selected ? 'is-selected' : '',
                              ]
                                .filter(Boolean)
                                .join(' ')}
                              onClick={() => toggleSelectedId(img.id)}
                              title={group.label}
                            >
                              <img src={img.url} alt={group.label} />
                              <span className="fs-product-picker-index">{idx + 1}</span>
                            </button>
                          );
                        })}
                      </div>
                    </div>
                  ),
                )}
              </div>
            )}
          </div>
        ),
      }
    : null;

  return (
    <Modal
      title={title}
      open={open}
      onCancel={onCancel}
      destroyOnHidden
      width={880}
      footer={footer}
    >
      <Tabs
        activeKey={activeTab}
        onChange={(key) => {
          setActiveTab(key);
          setSelectedIds(new Set());
        }}
        items={[
          ...(productTab ? [productTab] : []),
          {
            key: 'library',
            label: (
              <span>
                <FolderOpenOutlined /> Thư viện
              </span>
            ),
            children: (
              <div className="fs-file-library">
                <div className="fs-file-library-toolbar">
                  <p className="fs-file-library-hint">Upload file</p>
                  <Upload {...uploadProps}>
                    <Button type="primary" icon={<PlusOutlined />} loading={uploading}>
                      Upload
                    </Button>
                  </Upload>
                </div>
                {filesLoading || filesFetching ? (
                  <div className="fs-file-library-loading">
                    <Spin />
                  </div>
                ) : filesError ? (
                  <Empty
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                    description={
                      filesQueryError instanceof Error
                        ? filesQueryError.message
                        : 'Không tải được thư viện file'
                    }
                  >
                    <Button onClick={() => void refetchFiles()}>Thử lại</Button>
                  </Empty>
                ) : libraryFiles.length === 0 ? (
                  <Empty
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                    description={
                      kind === 'video'
                        ? 'Chưa có video — bấm Upload để thêm'
                        : 'Chưa có ảnh — bấm Upload để thêm'
                    }
                  />
                ) : (
                  <>
                    {missingCdnCount > 0 && (
                      <p className="fs-file-library-warn">
                        {missingCdnCount} file chưa có CDN URL (cấu hình{' '}
                        <code>bunny.cdn_hostname</code> trong Settings). Không chọn được các file này.
                      </p>
                    )}
                    <div className="fs-file-library-grid">
                      {libraryFiles.map((file) => {
                        const selected = selectedIds.has(file.id);
                        const canSelect = hasPublicUrl(file);
                        return (
                          <button
                            key={file.id}
                            type="button"
                            className={[
                              'fs-file-library-item',
                              selected ? 'is-selected' : '',
                              !canSelect ? 'is-disabled' : '',
                            ]
                              .filter(Boolean)
                              .join(' ')}
                            onClick={() => toggleLibraryFile(file)}
                            title={
                              canSelect
                                ? file.originalFileName
                                : `${file.originalFileName} — thiếu CDN URL`
                            }
                          >
                            {canSelect ? (
                              kind === 'video' ? (
                                <video src={file.publicUrl} muted preload="metadata" />
                              ) : (
                                <img src={file.publicUrl} alt={file.originalFileName} />
                              )
                            ) : (
                              <span className="fs-file-library-placeholder">No CDN</span>
                            )}
                            <span
                              role="button"
                              tabIndex={0}
                              className="fs-file-library-delete"
                              title="Xóa file"
                              onClick={(e) => {
                                e.stopPropagation();
                                void handleDeleteLibraryFile(file);
                              }}
                              onKeyDown={(e) => {
                                if (e.key === 'Enter' || e.key === ' ') {
                                  e.stopPropagation();
                                  e.preventDefault();
                                  void handleDeleteLibraryFile(file);
                                }
                              }}
                            >
                              <DeleteOutlined />
                            </span>
                            <span className="fs-file-library-meta">
                              <span className="name">{file.originalFileName}</span>
                              <span className="size">{formatFileSize(file.fileSizeBytes)}</span>
                            </span>
                          </button>
                        );
                      })}
                    </div>
                  </>
                )}
              </div>
            ),
          },
          {
            key: 'url',
            label: (
              <span>
                <LinkOutlined /> Từ URL
              </span>
            ),
            children: (
              <div className="fs-add-image-url">
                <Input
                  autoFocus
                  placeholder="https://..."
                  value={urlDraft}
                  onChange={(e) => setUrlDraft(e.target.value)}
                  onPressEnter={commitUrl}
                />
                {urlDraft.trim() && kind === 'image' && (
                  <div className="fs-add-image-preview">
                    <Image
                      src={urlDraft.trim()}
                      alt="Preview"
                      style={{ maxHeight: 180, objectFit: 'contain' }}
                      fallback="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='120' height='120'%3E%3Crect fill='%23f5f5f5' width='120' height='120'/%3E%3Ctext x='50%25' y='50%25' fill='%23999' text-anchor='middle' dy='.3em' font-size='12'%3EInvalid%3C/text%3E%3C/svg%3E"
                    />
                  </div>
                )}
                {urlDraft.trim() && kind === 'video' && (
                  <div className="fs-add-image-preview">
                    <video
                      src={urlDraft.trim()}
                      controls
                      style={{ maxHeight: 180, maxWidth: '100%' }}
                    />
                  </div>
                )}
              </div>
            ),
          },
        ]}
      />
    </Modal>
  );
}
