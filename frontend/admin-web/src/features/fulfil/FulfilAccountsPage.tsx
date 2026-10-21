import { useEffect, useMemo, useState } from 'react';
import {
  Button,
  Descriptions,
  Empty,
  Form,
  Input,
  Pagination,
  Popconfirm,
  Select,
  Space,
  Switch,
  Tag,
  Typography,
  message,
} from 'antd';
import { Modal } from '../../components/AppModal';
import {
  ApiOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  EyeOutlined,
  KeyOutlined,
  PlusOutlined,
  ReloadOutlined,
} from '@ant-design/icons';
import {
  ListCard,
  ListCardBody,
  ListCardHead,
  ListCol,
  ListHeader,
  ListLine,
  ListPagination,
  PageContentSpin,
  StatusDot,
} from '@drop/ui';
import { PageHeader } from '../../components/PageHeader';
import { useIsMobile } from '../../hooks/useIsMobile';
import {
  useCreateFulfilAccount,
  useDeleteFulfilAccount,
  useBulkTestFulfilSheinSessions,
  useFulfilAccounts,
  useFulfilCards,
  useFulfilSheinSession,
  useSaveFulfilSheinSession,
  useSetActiveFulfilCard,
  useTestFulfilSheinSession,
  useUpdateFulfilAccount,
} from '../../hooks/useAdminFulfil';
import { useAdminUsers } from '../../hooks/useAdminUsers';
import { useAuthStore } from '../../stores/authStore';
import type { FulfilAccount } from '../../types/fulfil';
import { formatFulfilCardOptionLabel } from '../../types/fulfil';
import { needsPagination } from '../../utils/pagination';
import '@drop/fulfil/fulfil.css';

const { Text } = Typography;

const PAGE_SIZE_OPTIONS = [10, 20, 50];
const ACC_COL =
  'minmax(150px, 1.3fr) minmax(150px, 1.2fr) minmax(110px, 0.9fr) minmax(130px, 1.1fr) minmax(96px, 0.7fr) minmax(44px, 0.35fr) minmax(130px, 1fr) minmax(120px, 0.9fr)';
const ACC_HEADERS = [
  'Tên',
  'Email',
  'Cấp cho',
  'Nguồn thẻ',
  'Số thẻ',
  'Thẻ',
  'Proxy',
  'Session',
];
const LIST_MIN_WIDTH = 1140;
const MONO = {
  fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
  fontSize: 12,
} as const;

const copyableField = (value?: string | null) => {
  if (!value) return '—';
  return (
    <Text copyable={{ text: value }} style={{ wordBreak: 'break-all' }}>
      {value}
    </Text>
  );
};

const buildAccountCopyText = (row: FulfilAccount) => {
  const lines = [
    `Tên: ${row.name}`,
    row.username ? `Username: ${row.username}` : null,
    row.password ? `Password: ${row.password}` : null,
    row.email ? `Email: ${row.email}` : null,
    row.passMail ? `Pass mail: ${row.passMail}` : null,
    row.twoFactorAuth ? `2FA: ${row.twoFactorAuth}` : null,
    row.activeCardNumberMasked || row.activeCardSourceName || row.activeCardName
      ? `Thẻ đang dùng: ${
          [
            row.activeCardSourceAccountName
              ? `${row.activeCardSourceName} · ${row.activeCardSourceAccountName}`
              : row.activeCardSourceName,
            row.activeCardNumberMasked,
            row.activeCardCurrency,
          ]
            .filter(Boolean)
            .join(' · ') ||
          [row.activeCardName, row.activeCardNumberMasked].filter(Boolean).join(' · ')
        }`
      : null,
  ];
  return lines.filter(Boolean).join('\n');
};

interface AccountFormValues {
  name: string;
  username?: string;
  password?: string;
  email?: string;
  passMail?: string;
  twoFactorAuth?: string;
  assigneeUserIds: string[];
  isActive: boolean;
  activeCardId?: string;
  sheinProxy?: string;
}

interface SessionFormValues {
  origin?: string;
  userAgent?: string;
  cookie?: string;
  headersText?: string;
}

/** `name: value` từng dòng — dạng DevTools "Copy request headers" mà PUT session nhận. */
const headersToText = (headers: Record<string, string>) =>
  Object.entries(headers)
    .filter(([name]) => name.toLowerCase() !== 'user-agent')
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([name, value]) => `${name}: ${value}`)
    .join('\n');
const formatSessionTime = (iso?: string | null) => {
  if (!iso) return null;
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? null : d.toLocaleString('vi-VN');
};

/** Khớp `SheinDetailSyncSettingKeys.MaxSessionAge` — chỉ cảnh báo UI, không khoá sweep. */
const SHEIN_SESSION_WARN_AGE_MS = 12 * 60 * 60 * 1000;

const isSessionAged = (capturedAt?: string | null) => {
  if (!capturedAt) return false;
  const t = new Date(capturedAt).getTime();
  return !Number.isNaN(t) && Date.now() - t > SHEIN_SESSION_WARN_AGE_MS;
};

/** Session SHEIN: Lỗi = SHEIN từ chối cookie; Cũ = bắt lâu nhưng vẫn thử dùng. */
const sheinSessionState = (row: FulfilAccount) => {
  if (row.sheinSessionLastError) {
    return { color: 'red' as const, label: 'Lỗi', detail: row.sheinSessionLastError };
  }
  if (row.sheinSessionReady) {
    const at = formatSessionTime(row.sheinSessionCapturedAt);
    if (isSessionAged(row.sheinSessionCapturedAt)) {
      return {
        color: 'orange' as const,
        label: 'Cũ',
        detail: at
          ? `Bắt lúc ${at} — đã quá 12 giờ, vẫn dùng đến khi SHEIN từ chối. Nên mở extension bắt lại khi rảnh.`
          : 'Session đã bắt lâu hơn 12 giờ — vẫn dùng đến khi SHEIN từ chối.',
      };
    }
    return {
      color: 'green' as const,
      label: 'Sẵn sàng',
      detail: at ? `Bắt lúc ${at}` : 'Đủ header + cookie',
    };
  }
  if (row.sheinHeaderCount > 0) {
    const missing = row.sheinMissingHeaders?.length
      ? `Thiếu ${row.sheinMissingHeaders.join(', ')}`
      : 'Thiếu cookie';
    return { color: 'orange' as const, label: 'Thiếu', detail: missing };
  }
  return { color: 'default' as const, label: 'Chưa có', detail: 'Chưa nhận session từ extension' };
};

export const FulfilAccountsPage = () => {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canManage = hasPermission('admin.fulfil.manage');
  const isMobile = useIsMobile();

  const { data: accounts = [], isLoading, isFetching, refetch } = useFulfilAccounts();
  const { data: users = [] } = useAdminUsers(canManage);
  const createAccount = useCreateFulfilAccount();
  const updateAccount = useUpdateFulfilAccount();
  const deleteAccount = useDeleteFulfilAccount();
  const setActiveCard = useSetActiveFulfilCard();

  const saveSession = useSaveFulfilSheinSession();
  const testSession = useTestFulfilSheinSession();
  const bulkTestSessions = useBulkTestFulfilSheinSessions();

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<FulfilAccount | null>(null);
  const [viewing, setViewing] = useState<FulfilAccount | null>(null);
  const [form] = Form.useForm<AccountFormValues>();
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10 });

  // Cookie ~16 KB nên chỉ fetch khi người dùng bấm xem, không kèm mỗi lần mở modal.
  const [sessionRevealed, setSessionRevealed] = useState(false);
  const [sessionEditing, setSessionEditing] = useState<FulfilAccount | null>(null);
  const [sessionForm] = Form.useForm<SessionFormValues>();
  const { data: sessionDetail, isFetching: sessionFetching } = useFulfilSheinSession(
    viewing?.id ?? sessionEditing?.id,
    sessionRevealed || !!sessionEditing,
  );
  const accountPage = useMemo(() => {
    const start = (pagination.current - 1) * pagination.pageSize;
    return accounts.slice(start, start + pagination.pageSize);
  }, [accounts, pagination]);

  const editingAccountId = editing?.id;
  const { data: accountCards = [] } = useFulfilCards(editingAccountId);

  const userOptions = useMemo(
    () =>
      users.map((u) => ({
        value: u.id,
        label: u.fullName?.trim() || u.userName,
      })),
    [users],
  );

  const cardOptions = useMemo(
    () =>
      accountCards.map((c) => ({
        value: c.id,
        label: formatFulfilCardOptionLabel(
          {
            name: c.name,
            cardNumberMasked: c.cardNumberMasked,
            currency: c.currency,
            sourceName: c.sourceName,
            sourceAccountName: c.sourceAccountName,
          },
          { preferredSuffix: false },
        ),
      })),
    [accountCards],
  );

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ isActive: true, assigneeUserIds: [] });
    setModalOpen(true);
  };

  const handleBulkTestSessions = () => {
    const eligible = accounts.filter((a) => a.sheinSessionReady);
    if (eligible.length === 0) {
      message.info('Không có tài khoản nào sẵn session để kiểm tra.');
      return;
    }
    Modal.confirm({
      title: `Kiểm tra session ${eligible.length} tài khoản?`,
      content: 'Chạy tối đa 10 tài khoản cùng lúc. Chỉ các tài khoản đã sẵn session SHEIN.',
      okText: 'Kiểm tra',
      cancelText: 'Hủy',
      onOk: () => bulkTestSessions.mutateAsync(eligible.map((a) => a.id)),
    });
  };

  const openView = (row: FulfilAccount) => {
    setSessionRevealed(false);
    setViewing(row);
  };

  const openSessionEdit = (row: FulfilAccount) => {
    setViewing(null);
    setSessionRevealed(false);
    sessionForm.resetFields();
    setSessionEditing(row);
  };

  // Session tải xong (lazy) → nạp vào form dán tay để người dùng sửa trên nền hiện tại,
  // thay vì phải dán lại toàn bộ khối header chỉ để đổi một dòng.
  useEffect(() => {
    if (!sessionEditing || !sessionDetail || sessionDetail.accountId !== sessionEditing.id) return;
    sessionForm.setFieldsValue({
      origin: sessionDetail.origin ?? undefined,
      userAgent: sessionDetail.userAgent ?? undefined,
      cookie: sessionDetail.cookie ?? undefined,
      headersText: headersToText(sessionDetail.headers ?? {}),
    });
  }, [sessionEditing, sessionDetail, sessionForm]);

  const handleSessionSubmit = async () => {
    if (!sessionEditing) return;
    const values = await sessionForm.validateFields();
    await saveSession.mutateAsync({
      id: sessionEditing.id,
      data: {
        origin: values.origin?.trim() || null,
        cookie: values.cookie?.trim() || null,
        headersText: values.headersText?.trim() || null,
        userAgent: values.userAgent?.trim() || null,
      },
    });
    setSessionEditing(null);
  };

  const copyAccountInfo = async (row: FulfilAccount) => {
    await navigator.clipboard.writeText(buildAccountCopyText(row));
    message.success('Đã copy thông tin tài khoản');
  };

  const openEdit = (row: FulfilAccount) => {
    setEditing(row);
    form.setFieldsValue({
      name: row.name,
      username: row.username ?? undefined,
      password: row.password ?? undefined,
      email: row.email ?? undefined,
      passMail: row.passMail ?? undefined,
      twoFactorAuth: row.twoFactorAuth ?? undefined,
      assigneeUserIds: row.assigneeUserIds ?? [],
      isActive: row.isActive,
      activeCardId: row.activeCardId ?? undefined,
      sheinProxy: row.sheinProxy ?? undefined,
    });
    setModalOpen(true);
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    if (editing) {
      await updateAccount.mutateAsync({
        id: editing.id,
        data: {
          name: values.name,
          username: values.username || null,
          password: values.password || null,
          email: values.email || null,
          passMail: values.passMail || null,
          twoFactorAuth: values.twoFactorAuth || null,
          assigneeUserIds: values.assigneeUserIds ?? [],
          isActive: values.isActive,
          sheinProxy: values.sheinProxy?.trim() || null,
        },
      });
      const nextActive = values.activeCardId || null;
      if (nextActive !== (editing.activeCardId || null)) {
        await setActiveCard.mutateAsync({ accountId: editing.id, cardId: nextActive });
      }
    } else {
      await createAccount.mutateAsync({
        name: values.name,
        username: values.username || null,
        password: values.password || null,
        email: values.email || null,
        passMail: values.passMail || null,
        twoFactorAuth: values.twoFactorAuth || null,
        assigneeUserIds: values.assigneeUserIds ?? [],
        isActive: values.isActive,
        sheinProxy: values.sheinProxy?.trim() || null,
      });
    }
    setModalOpen(false);
  };

  const renderActions = (record: FulfilAccount) => (
    <Space size={0} onClick={(e) => e.stopPropagation()}>
      <Button
        type="text"
        size="small"
        icon={<EyeOutlined />}
        title="Xem nhanh"
        onClick={() => openView(record)}
      />
      {canManage ? (
        <>
          <Button
            type="text"
            size="small"
            icon={<EditOutlined />}
            title="Sửa"
            onClick={() => openEdit(record)}
          />
          <Popconfirm
            title="Xóa tài khoản này?"
            onConfirm={() => deleteAccount.mutateAsync(record.id)}
          >
            <Button type="text" size="small" danger icon={<DeleteOutlined />} title="Xóa" />
          </Popconfirm>
        </>
      ) : null}
    </Space>
  );

  const renderAccountItem = (row: FulfilAccount, index: number) => {
    const session = sheinSessionState(row);
    const assignees = row.assigneeUserNames?.length ? row.assigneeUserNames.join(', ') : '';
    const cardSource = row.activeCardSourceAccountName
      ? `${row.activeCardSourceName} · ${row.activeCardSourceAccountName}`
      : row.activeCardSourceName || row.activeCardName || '';
    const masked = row.activeCardNumberMasked || '';
    const statusLabel = row.isActive ? 'Hoạt động' : 'Vô hiệu';
    const secondary = [row.username, row.email].filter(Boolean).join(' · ');
    const sessionMeta = row.sheinHeaderCount
      ? `${row.sheinHeaderCount} header${row.sheinHasCookie ? ' · cookie' : ' · chưa cookie'}`
      : '';

    const head = (
      <ListCardHead mobile={isMobile}>
        <div className="app-list-card-head-meta">
          {!isMobile ? (
            <span style={{ color: 'var(--app-text-tertiary)', fontVariantNumeric: 'tabular-nums', width: 20 }}>
              {index}
            </span>
          ) : null}
          <Text strong style={{ fontSize: 12 }}>
            {row.name}
          </Text>
          <StatusDot color={row.isActive ? '#52c41a' : '#8c8c8c'} label={statusLabel} />
          <Tag color={session.color} className="app-tag-compact" title={session.detail}>
            {session.label}
          </Tag>
        </div>
        <div className="app-list-card-head-actions">{renderActions(row)}</div>
      </ListCardHead>
    );

    if (isMobile) {
      return (
        <ListCard
          key={row.id}
          mobile
          role="button"
          tabIndex={0}
          onClick={() => openView(row)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              openView(row);
            }
          }}
          style={{ cursor: 'pointer' }}
        >
          {head}
          <div style={{ padding: '6px 10px' }}>
            <ListLine>{secondary || '—'}</ListLine>
            <ListLine>
              {[assignees || 'Chưa cấp', cardSource, masked].filter(Boolean).join(' · ')}
            </ListLine>
            <ListLine>
              {[row.sheinProxy || 'Không proxy', sessionMeta].filter(Boolean).join(' · ')}
            </ListLine>
          </div>
        </ListCard>
      );
    }

    return (
      <ListCard key={row.id}>
        {head}
        <ListCardBody columns={ACC_COL} style={{ minWidth: LIST_MIN_WIDTH }}>
          <ListCol>
            <ListLine primary>{row.name}</ListLine>
            <ListLine>{row.username || '—'}</ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary title={row.email || undefined}>
              {row.email || '—'}
            </ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary title={assignees || undefined}>
              {assignees || '—'}
            </ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary title={cardSource || undefined}>
              {cardSource || '—'}
            </ListLine>
            <ListLine>{row.activeCardCurrency || '—'}</ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary style={MONO}>
              {masked || '—'}
            </ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary>{row.cardCount ?? 0}</ListLine>
          </ListCol>
          <ListCol>
            {row.sheinProxy ? (
              <ListLine primary style={MONO} title={row.sheinProxy}>
                <Text copyable={{ text: row.sheinProxy }} style={MONO}>
                  {row.sheinProxy}
                </Text>
              </ListLine>
            ) : (
              <ListLine primary>—</ListLine>
            )}
          </ListCol>
          <ListCol>
            <ListLine primary title={session.detail}>
              {session.label}
            </ListLine>
            <ListLine>{sessionMeta || '—'}</ListLine>
          </ListCol>
        </ListCardBody>
      </ListCard>
    );
  };

  const modalWidth = isMobile ? '100%' : 520;
  const modalBodyStyles = isMobile
    ? { body: { paddingTop: 8, maxHeight: '70vh', overflowY: 'auto' as const } }
    : undefined;

  return (
    <div className="fulfil-accounts-page">
      <PageHeader
        title="Tài khoản mua hàng"
        extra={
          <Space size={4}>
            <Button
              size="small"
              icon={<ReloadOutlined />}
              onClick={() => void refetch()}
              loading={isFetching}
            >
              {isMobile ? null : 'Làm mới'}
            </Button>
            {canManage ? (
              <>
                <Button
                  size="small"
                  icon={<ApiOutlined />}
                  title="Kiểm tra session"
                  aria-label="Kiểm tra session"
                  loading={bulkTestSessions.isPending}
                  disabled={bulkTestSessions.isPending}
                  onClick={handleBulkTestSessions}
                >
                  {isMobile
                    ? null
                    : bulkTestSessions.isPending && bulkTestSessions.progress.total > 0
                      ? `Kiểm tra ${bulkTestSessions.progress.done}/${bulkTestSessions.progress.total}`
                      : 'Kiểm tra session'}
                </Button>
                <Button type="primary" size="small" icon={<PlusOutlined />} onClick={openCreate}>
                  {isMobile ? null : 'Thêm tài khoản'}
                </Button>
              </>
            ) : null}
          </Space>
        }
      />

      <PageContentSpin spinning={isLoading} tip="Đang tải tài khoản…">
        <div className="admin-list-scroll" style={{ overflowX: isMobile ? 'visible' : 'auto' }}>
          {!isMobile ? (
            <ListHeader columns={ACC_COL} style={{ minWidth: LIST_MIN_WIDTH }}>
              {ACC_HEADERS.map((label) => (
                <div key={label}>{label}</div>
              ))}
            </ListHeader>
          ) : null}
          <div
            className="admin-list-body"
            style={{ minWidth: isMobile ? 0 : LIST_MIN_WIDTH, minHeight: 160 }}
          >
            {accountPage.length === 0 && !isLoading ? (
              <Empty description="Chưa có tài khoản mua hàng" style={{ margin: '48px 0' }} />
            ) : (
              accountPage.map((row, i) =>
                renderAccountItem(row, (pagination.current - 1) * pagination.pageSize + i + 1),
              )
            )}
          </div>
        </div>
      </PageContentSpin>
      {needsPagination(accounts.length, pagination.pageSize) ? (
        <ListPagination mobile={isMobile}>
          <Pagination
            size="small"
            current={pagination.current}
            pageSize={pagination.pageSize}
            total={accounts.length}
            showSizeChanger={!isMobile}
            simple={isMobile}
            pageSizeOptions={PAGE_SIZE_OPTIONS.map(String)}
            showTotal={isMobile ? undefined : (t) => `${t} tài khoản`}
            onChange={(current, pageSize) => setPagination({ current, pageSize })}
          />
        </ListPagination>
      ) : null}

      <Modal
        title={viewing ? `Tài khoản · ${viewing.name}` : 'Xem tài khoản'}
        open={!!viewing}
        onCancel={() => setViewing(null)}
        className="fulfil-view-modal"
        footer={
          <div
            style={{
              display: 'flex',
              flexDirection: isMobile ? 'column-reverse' : 'row',
              flexWrap: 'wrap',
              gap: 8,
              justifyContent: isMobile ? 'stretch' : 'flex-end',
            }}
          >
            <Button block={isMobile} onClick={() => setViewing(null)}>
              Đóng
            </Button>
            <Button
              block={isMobile}
              icon={<CopyOutlined />}
              onClick={() => viewing && void copyAccountInfo(viewing)}
              disabled={!viewing}
            >
              Copy tất cả
            </Button>
            {canManage && viewing ? (
              <Button
                block={isMobile}
                type="primary"
                icon={<EditOutlined />}
                onClick={() => {
                  const row = viewing;
                  setViewing(null);
                  openEdit(row);
                }}
              >
                Sửa
              </Button>
            ) : null}
          </div>
        }
        width={modalWidth}
        centered={!isMobile}
        styles={modalBodyStyles}
        destroyOnClose
      >
        {viewing ? (
          <Descriptions
            className="fulfil-view-descriptions"
            bordered
            size="small"
            column={1}
            styles={
              isMobile
                ? { label: { width: 96, padding: '6px 8px' }, content: { padding: '6px 8px' } }
                : undefined
            }
          >
            <Descriptions.Item label="Tên">{copyableField(viewing.name)}</Descriptions.Item>
            <Descriptions.Item label="Username">{copyableField(viewing.username)}</Descriptions.Item>
            <Descriptions.Item label="Password">{copyableField(viewing.password)}</Descriptions.Item>
            <Descriptions.Item label="Email">{copyableField(viewing.email)}</Descriptions.Item>
            <Descriptions.Item label="Pass mail">{copyableField(viewing.passMail)}</Descriptions.Item>
            <Descriptions.Item label="2FA">{copyableField(viewing.twoFactorAuth)}</Descriptions.Item>
            <Descriptions.Item label="Cấp cho">
              {viewing.assigneeUserNames?.length ? viewing.assigneeUserNames.join(', ') : '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Thẻ dùng">
              {viewing.activeCardNumberMasked || viewing.activeCardSourceName || viewing.activeCardName
                ? copyableField(
                    [
                      viewing.activeCardSourceAccountName
                        ? `${viewing.activeCardSourceName} · ${viewing.activeCardSourceAccountName}`
                        : viewing.activeCardSourceName,
                      viewing.activeCardNumberMasked,
                      viewing.activeCardCurrency,
                    ]
                      .filter(Boolean)
                      .join(' · ') ||
                      [viewing.activeCardName, viewing.activeCardNumberMasked]
                        .filter(Boolean)
                        .join(' · '),
                  )
                : '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Số thẻ">{viewing.cardCount}</Descriptions.Item>
            <Descriptions.Item label="Proxy SHEIN">
              {viewing.sheinProxy ? (
                <Text copyable={{ text: viewing.sheinProxy }} style={MONO}>
                  {viewing.sheinProxy}
                </Text>
              ) : (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  Không proxy — backend gọi SHEIN từ IP server
                </Text>
              )}
            </Descriptions.Item>
            <Descriptions.Item label="Storefront">
              {viewing.sheinOrigin ? (
                <Text style={MONO}>{viewing.sheinOrigin}</Text>
              ) : (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  —
                </Text>
              )}
            </Descriptions.Item>
            <Descriptions.Item label="Session SHEIN">
              {(() => {
                const state = sheinSessionState(viewing);
                const captured = formatSessionTime(viewing.sheinSessionCapturedAt);
                const checked = formatSessionTime(viewing.sheinSessionLastCheckedAt);
                return (
                  <Space direction="vertical" size={4} style={{ width: '100%' }}>
                    <Space size={4} wrap>
                      <Tag color={state.color} style={{ marginInlineEnd: 0 }}>
                        {state.label}
                      </Tag>
                      <Text type="secondary" style={{ fontSize: 12 }}>
                        {viewing.sheinHeaderCount} header
                        {viewing.sheinHasCookie ? ' · có cookie' : ' · chưa có cookie'}
                      </Text>
                    </Space>
                    <Text type="secondary" style={{ fontSize: 12 }}>
                      {state.detail}
                    </Text>
                    {captured ? (
                      <Text type="secondary" style={{ fontSize: 12 }}>
                        Bắt lúc: {captured}
                      </Text>
                    ) : null}
                    {checked ? (
                      <Text type="secondary" style={{ fontSize: 12 }}>
                        Backend dùng lần cuối: {checked}
                      </Text>
                    ) : null}
                    <Space size={4} wrap>
                      <Button
                        type="text"
                        size="small"
                        icon={<KeyOutlined />}
                        loading={sessionFetching && sessionRevealed}
                        onClick={() => setSessionRevealed((v) => !v)}
                      >
                        {sessionRevealed ? 'Ẩn cookie/header' : 'Xem cookie/header'}
                      </Button>
                      {canManage ? (
                        <>
                          <Button
                            type="text"
                            size="small"
                            icon={<ApiOutlined />}
                            loading={testSession.isPending}
                            onClick={() => void testSession.mutateAsync(viewing.id)}
                          >
                            Test session
                          </Button>
                          <Button
                            type="text"
                            size="small"
                            icon={<EditOutlined />}
                            onClick={() => openSessionEdit(viewing)}
                          >
                            Sửa session
                          </Button>
                        </>
                      ) : null}
                    </Space>
                    {sessionRevealed && sessionDetail?.accountId === viewing.id ? (
                      <Space direction="vertical" size={2} style={{ width: '100%' }}>
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          Cookie
                        </Text>
                        {sessionDetail.cookie ? (
                          <Text
                            copyable={{ text: sessionDetail.cookie }}
                            style={{ ...MONO, wordBreak: 'break-all' }}
                          >
                            {sessionDetail.cookie}
                          </Text>
                        ) : (
                          <Text type="secondary" style={{ fontSize: 12 }}>
                            Chưa có cookie
                          </Text>
                        )}
                        <Text type="secondary" style={{ fontSize: 12, marginTop: 4 }}>
                          Header
                        </Text>
                        {Object.keys(sessionDetail.headers ?? {}).length === 0 ? (
                          <Text type="secondary" style={{ fontSize: 12 }}>
                            Chưa có header
                          </Text>
                        ) : (
                          Object.entries(sessionDetail.headers)
                            .sort(([a], [b]) => a.localeCompare(b))
                            .map(([name, value]) => (
                              <Text
                                key={name}
                                copyable={{ text: value }}
                                style={{ ...MONO, wordBreak: 'break-all' }}
                              >
                                {name}: {value}
                              </Text>
                            ))
                        )}
                      </Space>
                    ) : null}
                  </Space>
                );
              })()}
            </Descriptions.Item>
            <Descriptions.Item label="Trạng thái">
              <Tag color={viewing.isActive ? 'green' : 'default'} style={{ marginInlineEnd: 0 }}>
                {viewing.isActive ? 'Hoạt động' : 'Vô hiệu'}
              </Tag>
            </Descriptions.Item>
          </Descriptions>
        ) : null}
      </Modal>

      <Modal
        title={editing ? 'Sửa tài khoản' : 'Thêm tài khoản'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={() => void handleSubmit()}
        okText="Lưu"
        cancelText="Hủy"
        confirmLoading={createAccount.isPending || updateAccount.isPending || setActiveCard.isPending}
        destroyOnClose
        width={modalWidth}
        centered={!isMobile}
        styles={modalBodyStyles}
        className="fulfil-edit-modal"
      >
        <Form form={form} layout="vertical" requiredMark={false} size="small">
          <Form.Item name="name" label="Tên" rules={[{ required: true, message: 'Nhập tên' }]}>
            <Input placeholder="Tên tài khoản" />
          </Form.Item>
          <div className="fulfil-form-grid">
            <Form.Item name="username" label="Username">
              <Input autoComplete="off" />
            </Form.Item>
            <Form.Item name="password" label="Password">
              <Input.Password autoComplete="new-password" />
            </Form.Item>
            <Form.Item name="email" label="Email">
              <Input />
            </Form.Item>
            <Form.Item name="passMail" label="Pass mail">
              <Input.Password autoComplete="new-password" />
            </Form.Item>
          </div>
          <Form.Item name="twoFactorAuth" label="2FA">
            <Input.Password autoComplete="off" />
          </Form.Item>
          <Form.Item name="assigneeUserIds" label="Cấp cho">
            <Select
              mode="multiple"
              allowClear
              showSearch
              optionFilterProp="label"
              options={userOptions}
              placeholder="Người được cấp tài khoản"
            />
          </Form.Item>
          {editing ? (
            <Form.Item name="activeCardId" label="Thẻ đang dùng">
              <Select
                allowClear
                showSearch
                optionFilterProp="label"
                options={cardOptions}
                placeholder="Chọn thẻ ưu tiên"
              />
            </Form.Item>
          ) : null}
          <Form.Item
            name="sheinProxy"
            label="Proxy SHEIN"
            extra="Backend dùng proxy này khi tự gọi SHEIN. Phải cùng đường ra với trình duyệt chạy extension — cf_clearance bị khoá theo IP + User-Agent."
          >
            <Input placeholder="host:port hoặc host:port:user:pass" style={MONO} />
          </Form.Item>
          <Form.Item name="isActive" label="Hoạt động" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={sessionEditing ? `Session SHEIN · ${sessionEditing.name}` : 'Session SHEIN'}
        open={!!sessionEditing}
        onCancel={() => setSessionEditing(null)}
        onOk={() => void handleSessionSubmit()}
        okText="Lưu session"
        cancelText="Hủy"
        confirmLoading={saveSession.isPending}
        destroyOnClose
        width={isMobile ? '100%' : 640}
        centered={!isMobile}
        styles={modalBodyStyles}
      >
        <Form form={sessionForm} layout="vertical" requiredMark={false} size="small">
          <Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 8 }}>
            Bình thường extension tự đẩy session lên khi Auto Sync. Dán tay khi cần ghi đè hoặc
            khi extension chưa nối được. Để trống cả cookie và header là xoá session.
          </Text>
          <Form.Item
            name="cookie"
            label="Cookie"
            extra="Chuỗi header cookie đầy đủ (a=1; b=2), gồm cả cookie HttpOnly như AT, sessionID_shein, cf_clearance."
          >
            <Input.TextArea rows={4} style={MONO} placeholder="a=1; b=2; cf_clearance=…" />
          </Form.Item>
          <Form.Item
            name="headersText"
            label="Header"
            extra="Mỗi dòng một header dạng name: value (dán từ DevTools → Copy request headers). Bắt buộc có armortoken, smdeviceid, x-csrf-token, x-gw-auth."
          >
            <Input.TextArea rows={7} style={MONO} placeholder={'armortoken: …\nx-csrf-token: …'} />
          </Form.Item>
          <Form.Item
            name="userAgent"
            label="User-Agent"
            extra="UA của đúng trình duyệt đã bắt cookie. Lệch UA là một lý do SHEIN trả trang xác minh."
          >
            <Input style={MONO} placeholder="Mozilla/5.0 …" />
          </Form.Item>
          <Form.Item name="origin" label="Storefront">
            <Input style={MONO} placeholder="https://us.shein.com" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};
