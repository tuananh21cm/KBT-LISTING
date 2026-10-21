import { useEffect, useState } from 'react';
import { Alert, Empty, Image, Radio, Space, Spin, Typography } from 'antd';
import { Modal } from '@drop/ui';
import { useListingsDeps } from './ListingsDepsContext';
import type {
  ListingListItem,
  ListingMoveSource,
  ListingRecentCategory,
} from '@drop/types/listing';
import './listings.css';
const { Text } = Typography;

type BindCategoryModalProps = {
  listing: ListingListItem | null;
  open: boolean;
  onClose: () => void;
  onSuccess: (listingId: string) => void;
};

export const BindCategoryModal = ({
  listing,
  open,
  onClose,
  onSuccess,
}: BindCategoryModalProps) => {
  const deps = useListingsDeps();
  const bindMutation = deps.useBindListingCategory();
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [moveSource, setMoveSource] = useState<ListingMoveSource | null>(null);
  const [categories, setCategories] = useState<ListingRecentCategory[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);

  useEffect(() => {
    if (!open || !listing) {
      return;
    }

    let cancelled = false;
    setLoading(true);
    setLoadError(null);
    setMoveSource(null);
    setCategories([]);
    setSelectedId(null);

    const load = async () => {
      try {
        const moveRes = await deps.getMoveSource(listing.id);
        if (!moveRes.success || !moveRes.data) {
          throw new Error(moveRes.message || 'Không tải được thông tin sản phẩm.');
        }
        if (cancelled) return;
        setMoveSource(moveRes.data);

        const site = (listing.site?.trim() || 'US').toUpperCase();
        const shopId =
          moveRes.data.targetShopId > 0 ? moveRes.data.targetShopId : undefined;
        const catRes = await deps.getRecentCategories(listing.id, {
          shopId,
          site,
        });
        if (!catRes.success) {
          throw new Error(catRes.message || 'Không tải được danh sách category.');
        }
        if (cancelled) return;
        setCategories(catRes.data ?? []);
      } catch (err) {
        if (!cancelled) {
          setLoadError(err instanceof Error ? err.message : 'Tải dữ liệu thất bại.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    void load();
    return () => {
      cancelled = true;
    };
  }, [open, listing]);

  const handleOk = async () => {
    if (!listing || selectedId == null) return;
    const chosen = categories.find((c) => c.id === selectedId);
    if (!chosen) return;

    try {
      await bindMutation.mutateAsync({
        id: listing.id,
        data: {
          recentCategoryId: chosen.id,
          categoryId: chosen.categoryId || undefined,
          categoryName: chosen.categoryName || undefined,
          nodePathId: chosen.nodePathId || undefined,
        },
      });
      onClose();
      onSuccess(listing.id);
    } catch {
      /* toast from hook */
    }
  };

  const title =
    moveSource?.listingName?.trim() ||
    listing?.productName?.trim() ||
    'Chọn category';
  const thumb =
    moveSource?.listingUrl?.trim() || listing?.mainImage?.trim() || undefined;
  const sourceHint = [
    moveSource?.sourcePlatform,
    moveSource?.sourceCategoryName,
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <Modal
      title="Chọn category cho sản phẩm"
      open={open}
      onCancel={onClose}
      onOk={() => void handleOk()}
      okText="Gắn category & mở Edit"
      cancelText="Hủy"
      okButtonProps={{
        disabled: selectedId == null || loading || !!loadError,
        loading: bindMutation.isPending,
      }}
      width={640}
      destroyOnClose
    >
      {loading ? (
        <div style={{ textAlign: 'center', padding: 32 }}>
          <Spin description="Đang tải sản phẩm & category…">
            <div style={{ minHeight: 48 }} />
          </Spin>
        </div>
      ) : loadError ? (
        <Alert type="error" showIcon title={loadError} />
      ) : (
        <Space orientation="vertical" size={16} style={{ width: '100%' }}>
          <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
            {thumb ? (
              <Image
                src={thumb}
                width={72}
                height={72}
                style={{ objectFit: 'cover', borderRadius: 6, flexShrink: 0 }}
                preview={false}
              />
            ) : null}
            <div style={{ minWidth: 0 }}>
              <Text strong style={{ display: 'block' }}>
                {title}
              </Text>
              {sourceHint ? (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  Nguồn: {sourceHint}
                </Text>
              ) : (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  Listing chưa có giá / variants — chọn category TikTok gần đây để
                  tiếp tục.
                </Text>
              )}
              {moveSource?.targetShopName ? (
                <div>
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    Shop: {moveSource.targetShopName}
                  </Text>
                </div>
              ) : null}
            </div>
          </div>

          {categories.length === 0 ? (
            <Empty description="Chưa có category gần đây cho shop này." />
          ) : (
            <Radio.Group
              value={selectedId}
              onChange={(e) => setSelectedId(e.target.value as number)}
              style={{ width: '100%' }}
            >
              <Space orientation="vertical" size={8} style={{ width: '100%' }}>
                {categories.map((cat, index) => (
                  <Radio
                    key={`${cat.id}-${cat.categoryId ?? index}`}
                    value={cat.id}
                    style={{
                      display: 'flex',
                      alignItems: 'flex-start',
                      whiteSpace: 'normal',
                      height: 'auto',
                      padding: '8px 10px',
                      border: '1px solid var(--listing-border)',
                      borderRadius: 6,
                      width: '100%',
                      background: 'var(--listing-surface)',
                    }}
                  >
                    <div>
                      <Text strong>{cat.categoryName}</Text>
                      {cat.nodePath ? (
                        <div>
                          <Text type="secondary" style={{ fontSize: 12 }}>
                            {cat.nodePath}
                          </Text>
                        </div>
                      ) : null}
                    </div>
                  </Radio>
                ))}
              </Space>
            </Radio.Group>
          )}
        </Space>
      )}
    </Modal>
  );
};
