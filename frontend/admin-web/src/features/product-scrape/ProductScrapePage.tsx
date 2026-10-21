import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  Image,
  Input,  Popconfirm,
  Progress,
  Select,
  Space,
  Table,
  Tooltip,
  Typography,
  message,
} from 'antd';
import { Modal } from '../../components/AppModal';
import {
  CloudDownloadOutlined,
  DeleteOutlined,
  ReloadOutlined,
  SendOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useQueryClient } from '@tanstack/react-query';
import {
  pollScheduleProgress,
  sourceUrlKey,
  useDeleteMoveDrafts,
  useMoveDrafts,
  useSaveToPlatform,
  useSubmitCrawlUrls,
} from '../../hooks/useProductScrape';
import { useFourSellerShopList } from '../../hooks/useFourSeller';
import { useAdminUsers } from '../../hooks/useAdminUsers';
import { useAuthStore } from '../../stores/authStore';
import { PageHeader } from '../../components/PageHeader';
import type {
  FourSellerMoveDraftRecord,
  FourSellerScheduleProgress,
} from '../../types/productScrape';
import { tablePagination } from '../../utils/pagination';

const { Text, Link } = Typography;
const { TextArea } = Input;

type OwnerFilter = 'mine' | 'all' | string;

const parseUrls = (raw: string) => {
  // Split glued "https://...https://..." pastes, then strip query/hash (match Postman clean URL).
  const expanded = raw.replace(/(https?:\/\/)/gi, '\n$1');
  const seen = new Set<string>();
  const urls: string[] = [];
  for (const part of expanded.split(/[\n,\s]+/)) {
    const trimmed = part.trim();
    if (!/^https?:\/\//i.test(trimmed)) continue;
    try {
      const parsed = new URL(trimmed);
      const clean = `${parsed.origin}${parsed.pathname}`;
      const key = clean.toLowerCase();
      if (seen.has(key)) continue;
      seen.add(key);
      urls.push(clean);
    } catch {
      /* skip invalid */
    }
  }
  return urls;
};

const formatPriceRange = (row: FourSellerMoveDraftRecord) => {
  const currency = row.currency || 'USD';
  if (row.lowPrice == null && row.highPrice == null) return '—';
  if (row.lowPrice != null && row.highPrice != null && row.lowPrice !== row.highPrice) {
    return `${row.lowPrice} – ${row.highPrice} ${currency}`;
  }
  return `${row.lowPrice ?? row.highPrice} ${currency}`;
};

function stringEqualsIgnoreCase(a?: string | null, b?: string | null) {
  return (a ?? '').trim().toLowerCase() === (b ?? '').trim().toLowerCase();
}

export const ProductScrapePage = () => {
  const queryClient = useQueryClient();
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const user = useAuthStore((s) => s.user);
  const canManage = hasPermission('admin.products.manage');
  const isAdmin = user?.roles.includes('Admin') ?? false;

  const { data: fourSellerShops = [], isLoading: shopsLoading, error: shopsError } =
    useFourSellerShopList(true);
  const { data: adminUsers = [] } = useAdminUsers(isAdmin);

  const connectedShops = useMemo(
    () => fourSellerShops.filter((s) => stringEqualsIgnoreCase(s.statusDesc, 'Connected')),
    [fourSellerShops],
  );

  const shopOptions = useMemo(
    () =>
      connectedShops.map((s) => ({
        label: `${s.platformShopName || s.shopName} (${s.marketplaceId || '—'}) · #${s.id}`,
        value: s.id,
      })),
    [connectedShops],
  );

  const ownerFilterOptions = useMemo(() => {
    const opts: { label: string; value: string }[] = [
      { label: 'Của tôi', value: 'mine' },
      { label: 'Tất cả', value: 'all' },
    ];
    for (const u of adminUsers) {
      opts.push({ label: u.fullName || u.userName, value: u.id });
    }
    return opts;
  }, [adminUsers]);

  const [urlText, setUrlText] = useState('');
  const [search, setSearch] = useState('');
  const [searchApplied, setSearchApplied] = useState<string | null>(null);
  const [ownerFilter, setOwnerFilter] = useState<OwnerFilter>('mine');
  const [pagination, setPagination] = useState({ current: 1, pageSize: 50 });
  const [selectedDraftIds, setSelectedDraftIds] = useState<number[]>([]);
  const [selectedShopIds, setSelectedShopIds] = useState<number[]>([]);
  const [modalOpen, setModalOpen] = useState(false);
  const [modalDraftIds, setModalDraftIds] = useState<number[]>([]);
  const [modalTitle, setModalTitle] = useState('Đẩy lên shop');
  const [scheduleCode, setScheduleCode] = useState<string | null>(null);
  const [scheduleProgress, setScheduleProgress] = useState<FourSellerScheduleProgress | null>(null);
  const [isPollingSchedule, setIsPollingSchedule] = useState(false);
  const [isScrapingList, setIsScrapingList] = useState(false);
  const [lastScrapedKeys, setLastScrapedKeys] = useState<Set<string>>(new Set());

  const scheduleAbortRef = useRef<AbortController | null>(null);

  const draftRequest = useMemo(() => {
    if (!isAdmin || ownerFilter === 'mine') {
      return {
        pageCurrent: pagination.current,
        pageSize: pagination.pageSize,
        searchValue: searchApplied,
        mineOnly: true,
        createdById: null as string | null,
      };
    }
    if (ownerFilter === 'all') {
      return {
        pageCurrent: pagination.current,
        pageSize: pagination.pageSize,
        searchValue: searchApplied,
        mineOnly: false,
        createdById: null as string | null,
      };
    }
    return {
      pageCurrent: pagination.current,
      pageSize: pagination.pageSize,
      searchValue: searchApplied,
      mineOnly: false,
      createdById: ownerFilter,
    };
  }, [pagination.current, pagination.pageSize, searchApplied, ownerFilter, isAdmin]);

  const draftQuery = useMoveDrafts(draftRequest);
  const submitCrawl = useSubmitCrawlUrls();
  const saveToPlatform = useSaveToPlatform();
  const deleteDrafts = useDeleteMoveDrafts();

  useEffect(
    () => () => {
      scheduleAbortRef.current?.abort();
    },
    [],
  );

  const handleDeleteDrafts = async (ids: number[]) => {
    if (ids.length === 0) return;
    await deleteDrafts.mutateAsync(ids);
    setSelectedDraftIds((prev) => prev.filter((id) => !ids.includes(id)));
  };

  const handleScrape = async () => {
    const urls = parseUrls(urlText);
    if (urls.length === 0) {
      message.warning('Nhập ít nhất một URL hợp lệ (http/https).');
      return;
    }

    setIsScrapingList(true);
    setSearchApplied(null);
    setSearch('');
    setOwnerFilter('mine');
    setPagination((p) => ({ ...p, current: 1 }));
    setLastScrapedKeys(new Set(urls.map(sourceUrlKey)));

    try {
      const result = await submitCrawl.mutateAsync(urls);

      if (result.fourSellerResponse) {
        Modal.info({
          title: 'Response từ 4Seller (crawl/urls)',
          width: 720,
          content: (
            <div>
              <Text type="secondary" style={{ display: 'block', marginBottom: 8 }}>
                URL đã gửi (canonical):
              </Text>
              <Typography.Paragraph
                copyable
                style={{
                  marginBottom: 12,
                  fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
                  fontSize: 12,
                }}
              >
                {urls.join('\n')}
              </Typography.Paragraph>
              <Text type="secondary" style={{ display: 'block', marginBottom: 8 }}>
                Body response:
              </Text>
              <Typography.Paragraph
                copyable
                style={{
                  marginBottom: 0,
                  maxHeight: 280,
                  overflow: 'auto',
                  whiteSpace: 'pre-wrap',
                  fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
                  fontSize: 12,
                }}
              >
                {result.fourSellerResponse}
              </Typography.Paragraph>
            </div>
          ),
        });
      }

      // HTTP 200 / success = done — no draft-pool poll. User Refresh if drafts appear later.
      message.success(`Đã gửi ${urls.length} URL lên 4Seller.`);
      await queryClient.invalidateQueries({ queryKey: ['productScrapeDrafts'] });
      await draftQuery.refetch();
    } catch {
      // submit errors already toasted by useSubmitCrawlUrls.onError
    } finally {
      setIsScrapingList(false);
    }
  };

  const openSaveModal = (draftIds: number[], title?: string) => {
    if (draftIds.length === 0) {
      message.warning('Chọn ít nhất một sản phẩm.');
      return;
    }
    setModalDraftIds(draftIds);
    setModalTitle(
      title ??
        (draftIds.length === 1
          ? 'Đẩy sản phẩm lên shop'
          : `Đẩy ${draftIds.length} sản phẩm lên shop`),
    );
    setScheduleCode(null);
    setScheduleProgress(null);
    setSelectedShopIds((prev) =>
      prev.length ? prev : shopOptions.slice(0, 1).map((o) => o.value),
    );
    setModalOpen(true);
  };

  const handleConfirmSave = async () => {
    if (modalDraftIds.length === 0) {
      message.warning('Không có sản phẩm để đẩy.');
      return;
    }
    if (selectedShopIds.length === 0) {
      message.warning('Chọn ít nhất một shop 4Seller (Connected).');
      return;
    }

    scheduleAbortRef.current?.abort();
    const abort = new AbortController();
    scheduleAbortRef.current = abort;

    try {
      const result = await saveToPlatform.mutateAsync({
        moveDraftId: modalDraftIds,
        shopIdList: selectedShopIds,
      });
      setScheduleCode(result.scheduleCode);
      setScheduleProgress(null);
      setIsPollingSchedule(true);

      const progress = await pollScheduleProgress(result.scheduleCode, {
        intervalMs: 2000,
        timeoutMs: 120_000,
        signal: abort.signal,
        onProgress: setScheduleProgress,
      });

      setScheduleProgress(progress);
      if (progress.total > 0 && progress.succeed + progress.failed >= progress.total) {
        if (progress.failed > 0) {
          message.warning(
            `Hoàn tất: ${progress.succeed} thành công, ${progress.failed} thất bại.`,
          );
        } else {
          message.success(`Hoàn tất: ${progress.succeed}/${progress.total} thành công.`);
          setSelectedDraftIds((ids) => ids.filter((id) => !modalDraftIds.includes(id)));
          setModalOpen(false);
        }
        void draftQuery.refetch();
      } else {
        message.info('Hết thời gian chờ schedule. Có thể thử lại.');
      }
    } catch (error) {
      if (error instanceof Error && error.message.includes('hủy')) {
        return;
      }
    } finally {
      setIsPollingSchedule(false);
    }
  };

  const progressPercent = useMemo(() => {
    if (!scheduleProgress || scheduleProgress.total <= 0) return 0;
    return Math.min(
      100,
      Math.round(
        ((scheduleProgress.succeed + scheduleProgress.failed) / scheduleProgress.total) * 100,
      ),
    );
  }, [scheduleProgress]);

  const columns: ColumnsType<FourSellerMoveDraftRecord> = [
    {
      title: 'Ảnh',
      dataIndex: 'image',
      key: 'image',
      width: 72,
      render: (url?: string | null) =>
        url ? <Image src={url} width={48} height={48} style={{ objectFit: 'cover' }} /> : '—',
    },
    {
      title: 'Tên sản phẩm',
      dataIndex: 'productName',
      key: 'productName',
      ellipsis: true,
      render: (name?: string | null, row?: FourSellerMoveDraftRecord) => {
        const isNew =
          row?.sourceUrl && lastScrapedKeys.size > 0
            ? lastScrapedKeys.has(sourceUrlKey(row.sourceUrl))
            : false;
        return (
          <Space size={4}>
            <span>{name || '—'}</span>
            {isNew ? <Text type="success">(mới)</Text> : null}
          </Space>
        );
      },
    },
    {
      title: 'Source URL',
      dataIndex: 'sourceUrl',
      key: 'sourceUrl',
      ellipsis: true,
      width: 220,
      render: (url?: string | null) =>
        url ? (
          <Link href={url} target="_blank" rel="noreferrer">
            {url}
          </Link>
        ) : (
          '—'
        ),
    },
    {
      title: 'Variants',
      dataIndex: 'variantCount',
      key: 'variantCount',
      width: 90,
      align: 'center',
    },
    {
      title: 'Giá',
      key: 'price',
      width: 160,
      render: (_, row) => formatPriceRange(row),
    },
    {
      title: 'Creator',
      dataIndex: 'creator',
      key: 'creator',
      width: 120,
      ellipsis: true,
      render: (v?: string | null) => v || '—',
    },
    ...(isAdmin && ownerFilter !== 'mine'
      ? ([
          {
            title: 'Người scrape',
            dataIndex: 'ownedByName',
            key: 'ownedByName',
            width: 140,
            ellipsis: true,
            render: (v?: string | null) => v || '—',
          },
        ] as ColumnsType<FourSellerMoveDraftRecord>)
      : []),
    {
      title: 'Create time',
      dataIndex: 'createTime',
      key: 'createTime',
      width: 160,
      render: (v?: string | null) => v || '—',
    },
    ...(canManage
      ? ([
          {
            title: 'Act',
            key: 'act',
            width: 180,
            fixed: 'right',
            render: (_: unknown, row: FourSellerMoveDraftRecord) => (
              <Space size={0}>
                <Tooltip title="Đẩy lên shop 4Seller">
                  <Button
                    type="link"
                    size="small"
                    icon={<SendOutlined />}
                    disabled={isPollingSchedule || deleteDrafts.isPending || isScrapingList}
                    onClick={() =>
                      openSaveModal(
                        [row.id],
                        row.productName
                          ? `Đẩy lên shop · ${row.productName}`
                          : 'Đẩy sản phẩm lên shop',
                      )
                    }
                  >
                    Đẩy shop
                  </Button>
                </Tooltip>
                <Popconfirm
                  title="Xóa draft này?"
                  description="Xóa trên 4Seller, không thể hoàn tác."
                  okText="Xóa"
                  cancelText="Hủy"
                  okButtonProps={{ danger: true, loading: deleteDrafts.isPending }}
                  onConfirm={() => void handleDeleteDrafts([row.id])}
                >
                  <Button
                    type="link"
                    size="small"
                    danger
                    icon={<DeleteOutlined />}
                    disabled={isPollingSchedule || deleteDrafts.isPending || isScrapingList}
                  >
                    Xóa
                  </Button>
                </Popconfirm>
              </Space>
            ),
          },
        ] as ColumnsType<FourSellerMoveDraftRecord>)
      : []),
  ];

  const shopsErrorMessage =
    shopsError instanceof Error
      ? shopsError.message
      : shopsError
        ? 'Không tải được shop 4Seller.'
        : null;

  const listLoading = draftQuery.isLoading || draftQuery.isFetching || isScrapingList;

  return (
    <div>
      <PageHeader
        title="Scrape sản phẩm (4Seller)"
        description={
          <Text type="secondary">
            Scrape gửi URL lên 4Seller rồi reload danh sách. Tải lại lấy trực tiếp draft từ 4Seller
            (cookie tài khoản đang đăng nhập). Admin có thể xem Tất cả (local) / lọc theo người.
          </Text>
        }
      />

      {(connectedShops.length === 0 || shopsErrorMessage) && (
        <Alert
          style={{ marginTop: 16 }}
          type="warning"
          showIcon
          title={shopsErrorMessage ? 'Không tải shop 4Seller' : 'Chưa có shop Connected trên 4Seller'}
          description={
            shopsErrorMessage ??
            'Cần shop Connected trước khi Đẩy shop. Nút Scrape vẫn chạy nếu cookie hợp lệ.'
          }
        />
      )}

      <Card style={{ marginTop: 16 }} size="small" title="1. Scrape — lấy danh sách của bạn">
        <Text type="secondary" style={{ display: 'block', marginBottom: 8 }}>
          Gửi URL lên 4Seller (HTTP 200 = thành công) rồi tải lại danh sách draft. Draft mới có thể
          xuất hiện sau vài giây — bấm Tải lại nếu chưa thấy.
        </Text>
        <TextArea
          rows={5}
          value={urlText}
          onChange={(e) => setUrlText(e.target.value)}
          placeholder={'Mỗi dòng một URL, ví dụ:\nhttps://us.shein.com/...html'}
          disabled={!canManage || isScrapingList}
        />
        <Space style={{ marginTop: 12 }} wrap>
          {canManage && (
            <Button
              type="primary"
              icon={<CloudDownloadOutlined />}
              loading={isScrapingList}
              onClick={() => void handleScrape()}
            >
              Scrape
            </Button>
          )}
        </Space>
      </Card>

      <Card
        style={{ marginTop: 16 }}
        size="small"
        title={`2. Danh sách sản phẩm (${draftQuery.data?.total ?? 0})`}
      >
        <div className="admin-filters" style={{ marginBottom: 12 }}>
          <Input.Search
            className="admin-filters-search"
            allowClear
            placeholder="Tìm draft"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onSearch={(v) => {
              setSearchApplied(v.trim() || null);
              setPagination((p) => ({ ...p, current: 1 }));
            }}
          />
          <div className="admin-filters-grid">
            {isAdmin && (
              <Select
                value={ownerFilter}
                options={ownerFilterOptions}
                style={{ width: 180 }}
                onChange={(v) => {
                  setOwnerFilter(v);
                  setPagination((p) => ({ ...p, current: 1 }));
                  setSelectedDraftIds([]);
                }}
                optionFilterProp="label"
                showSearch
              />
            )}
          </div>
          <div className="admin-filters-actions">
            {canManage && selectedDraftIds.length > 0 && (
              <>
                <Button
                  type="primary"
                  icon={<SendOutlined />}
                  disabled={
                    isPollingSchedule ||
                    connectedShops.length === 0 ||
                    deleteDrafts.isPending ||
                    isScrapingList
                  }
                  onClick={() => openSaveModal(selectedDraftIds)}
                >
                  Đẩy shop ({selectedDraftIds.length})
                </Button>
                <Popconfirm
                  title={`Xóa ${selectedDraftIds.length} draft đã chọn?`}
                  description="Xóa trên 4Seller, không thể hoàn tác."
                  okText="Xóa"
                  cancelText="Hủy"
                  okButtonProps={{ danger: true, loading: deleteDrafts.isPending }}
                  onConfirm={() => void handleDeleteDrafts(selectedDraftIds)}
                >
                  <Button
                    danger
                    icon={<DeleteOutlined />}
                    disabled={isPollingSchedule || deleteDrafts.isPending || isScrapingList}
                    loading={deleteDrafts.isPending}
                  >
                    Xóa ({selectedDraftIds.length})
                  </Button>
                </Popconfirm>
              </>
            )}
            <Button
              icon={<ReloadOutlined />}
              loading={draftQuery.isFetching && !isScrapingList}
              disabled={isScrapingList}
              onClick={() => void draftQuery.refetch()}
            >
              Tải lại
            </Button>
          </div>
        </div>
        <Table
          size="small"
          rowKey="id"
          loading={listLoading}
          columns={columns}
          dataSource={draftQuery.data?.records ?? []}
          rowSelection={
            canManage
              ? {
                  selectedRowKeys: selectedDraftIds,
                  onChange: (keys) => setSelectedDraftIds(keys.map((k) => Number(k))),
                }
              : undefined
          }
          pagination={tablePagination(draftQuery.data?.total ?? 0, {
            current: pagination.current,
            pageSize: pagination.pageSize,
            total: draftQuery.data?.total ?? 0,
            showSizeChanger: true,
            pageSizeOptions: ['20', '50', '100'],
            onChange: (current, pageSize) => setPagination({ current, pageSize }),
          })}
          scroll={{ x: 1100 }}
        />
      </Card>

      <Modal
        title={modalTitle}
        open={modalOpen}
        onCancel={() => {
          if (isPollingSchedule) {
            scheduleAbortRef.current?.abort();
            setIsPollingSchedule(false);
          }
          setModalOpen(false);
        }}
        okText="Save to platform"
        okButtonProps={{
          icon: <SendOutlined />,
          loading: saveToPlatform.isPending || isPollingSchedule,
          disabled: selectedShopIds.length === 0 || modalDraftIds.length === 0,
        }}
        onOk={() => void handleConfirmSave()}
        destroyOnClose
        width={560}
      >
        <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
          Chọn shop Connected → save-to-platform → theo dõi schedule.
        </Text>
        <Select
          mode="multiple"
          allowClear
          placeholder="Chọn shop 4Seller"
          style={{ width: '100%' }}
          options={shopOptions}
          value={selectedShopIds}
          onChange={setSelectedShopIds}
          loading={shopsLoading}
          disabled={saveToPlatform.isPending || isPollingSchedule}
          optionFilterProp="label"
        />
        <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
          {modalDraftIds.length} draft · {selectedShopIds.length} shop
        </Text>

        {(scheduleCode || scheduleProgress) && (
          <div style={{ marginTop: 16 }}>
            {scheduleCode && (
              <Text type="secondary" style={{ display: 'block', marginBottom: 8 }}>
                Schedule: {scheduleCode}
              </Text>
            )}
            <Progress
              percent={progressPercent}
              status={
                scheduleProgress && scheduleProgress.failed > 0
                  ? 'exception'
                  : isPollingSchedule
                    ? 'active'
                    : 'normal'
              }
              format={() =>
                scheduleProgress
                  ? `${scheduleProgress.succeed + scheduleProgress.failed}/${scheduleProgress.total}`
                  : '…'
              }
            />
            {scheduleProgress && scheduleProgress.failedList.length > 0 && (
              <Alert
                style={{ marginTop: 12 }}
                type="error"
                showIcon
                title="Failed list"
                description={
                  <pre style={{ margin: 0, whiteSpace: 'pre-wrap' }}>
                    {JSON.stringify(scheduleProgress.failedList, null, 2)}
                  </pre>
                }
              />
            )}
          </div>
        )}
      </Modal>
    </div>
  );
};
