import { useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Form,
  Input,
  message,  Popconfirm,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
  Upload,
} from 'antd';
import { Modal } from '../../components/AppModal';
import {
  PlusOutlined,
  EditOutlined,
  DeleteOutlined,
  InboxOutlined,
  KeyOutlined,
  ReloadOutlined,
  SafetyCertificateOutlined,
  UploadOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { UploadFile } from 'antd/es/upload/interface';
import { formatDateTimeVn } from '../../utils/datetime';
import {
  useAdminUsers,
  useCheckAdminUserFourSellerLogin,
  useCheckAdminUsersFourSellerLogin,
  useCreateAdminUser,
  useDeleteAdminUser,
  useImportAdminUsers,
  useResetAdminUserPassword,
  useRoleOptions,
  useUpdateAdminUser,
} from '../../hooks/useAdminUsers';
import { useAuthStore } from '../../stores/authStore';
import { useIsMobile } from '../../hooks/useIsMobile';
import { PageHeader } from '../../components/PageHeader';
import type { AdminUser, CookieLoginStatus } from '../../types/user';
import { PASSWORD_HINT, passwordFormRules } from '../../utils/passwordRules';
import { tablePagination } from '../../utils/pagination';
import { IMPORT_FORMAT_HINT, parseUserAccountFile, type ImportUserRow } from './userAccountImport';
import './users.css';

const { Text } = Typography;

/** Role gán sẵn cho mọi dòng import — nhân sự nhập hàng loạt hầu hết là Seller. */
const DEFAULT_IMPORT_ROLE = 'Seller';

/** Một dòng trên bảng preview: dữ liệu từ file + role do người dùng chọn + lỗi server trả về. */
interface ImportPreviewRow extends ImportUserRow {
  role?: string;
  serverError?: string;
}

interface UserFormValues {
  userName?: string;
  email: string;
  fullName: string;
  password?: string;
  roles: string[];
  isActive: boolean;
  fourSellerCookie?: string;
  fourSellerProxy?: string;
  fulfilPin?: string;
}

const COOKIE_HINT =
  'Dán session cookie từ trình duyệt đã đăng nhập 4Seller (cần có userToken). Khi sửa: để trống nếu không đổi.';

const PIN_HINT =
  'Dùng để đăng nhập nhanh extension KBT Fulfil (chỉ role Fulfil/Admin). PIN phải duy nhất; xoá trắng khi sửa = gỡ PIN.';

const MOBILE_USER_COLUMN_KEYS = new Set([
  'fullName',
  'roles',
  'pin',
  'cookieLoginStatus',
  'fourSellerProxy',
  'isActive',
  'actions',
]);

const cookieStatusTag = (status: CookieLoginStatus, compact?: boolean) => {
  const tag = (() => {
    switch (status) {
      case 'logged_in':
        return <Tag color="success">{compact ? 'OK' : 'Đã login'}</Tag>;
      case 'expired':
        return <Tag color="error">{compact ? 'Hết' : 'Hết hạn'}</Tag>;
      case 'unchecked':
        return <Tag color="processing">{compact ? '?' : 'Chưa kiểm tra'}</Tag>;
      default:
        return <Tag>{compact ? '—' : 'Chưa có'}</Tag>;
    }
  })();

  if (status === 'expired') {
    return (
      <Tooltip title="Chỉ đánh dấu hết hạn khi is-login xác nhận chắc chắn. Lỗi mạng/proxy/567 không đổi trạng thái — chạy Check login hoặc đợi background re-check.">
        {tag}
      </Tooltip>
    );
  }

  return tag;
};

const resolveCookieStatus = (user: AdminUser): CookieLoginStatus => {
  if (!user.hasFourSellerCookie) return 'none';
  const status = user.fourSellerCookieLoginStatus;
  if (status === 'logged_in' || status === 'expired' || status === 'unchecked') {
    return status;
  }
  return 'unchecked';
};

const shortenProxy = (proxy: string) =>
  proxy.length > 6 ? `${proxy.slice(0, 3)}...${proxy.slice(-3)}` : proxy;

export const UsersPage = () => {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canManage = hasPermission('admin.users.manage');
  const canView = hasPermission('admin.users.view');
  const isMobile = useIsMobile();

  const { data: users = [], isLoading, isFetching, refetch } = useAdminUsers();
  const { data: roles = [], isLoading: rolesLoading, isError: rolesError } = useRoleOptions(canManage);
  const createUser = useCreateAdminUser();
  const updateUser = useUpdateAdminUser();
  const deleteUser = useDeleteAdminUser();
  const resetPassword = useResetAdminUserPassword();
  const importUsers = useImportAdminUsers();
  const checkLoginOne = useCheckAdminUserFourSellerLogin();
  const checkLoginBulk = useCheckAdminUsersFourSellerLogin();

  const [modalOpen, setModalOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [importFileList, setImportFileList] = useState<UploadFile[]>([]);
  const [importParsing, setImportParsing] = useState(false);
  const [importRows, setImportRows] = useState<ImportPreviewRow[]>([]);
  const [resetModalOpen, setResetModalOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [resetUserId, setResetUserId] = useState<string | null>(null);
  const [form] = Form.useForm<UserFormValues>();
  const [resetForm] = Form.useForm<{ newPassword: string }>();
  const draftCookie = Form.useWatch('fourSellerCookie', form);
  const [pagination, setPagination] = useState({ current: 1, pageSize: 50 });
  const [checkingUserId, setCheckingUserId] = useState<string | null>(null);

  const roleOptions = roles.map((role) => ({ label: role.name, value: role.name }));
  const canCheckCookie = Boolean(draftCookie?.trim()) || Boolean(editingUser?.hasFourSellerCookie);

  /** `Seller` khi role này tồn tại, nếu không thì role đầu tiên để combobox không rỗng. */
  const defaultImportRole = useMemo(() => {
    const preferred = roles.find((r) => r.name.toLowerCase() === DEFAULT_IMPORT_ROLE.toLowerCase());
    return preferred?.name ?? roles[0]?.name;
  }, [roles]);

  const existingByUserName = useMemo(
    () => new Map(users.map((u) => [u.userName.toLowerCase(), u])),
    [users],
  );
  const existingByEmail = useMemo(
    () =>
      new Map(
        users.filter((u) => u.email).map((u) => [u.email!.toLowerCase(), u] as const),
      ),
    [users],
  );

  const resolveImportRole = (row: ImportPreviewRow) => row.role ?? defaultImportRole;
  const importableRows = importRows.filter((row) => !row.error && resolveImportRole(row));

  const usersWithCookieCount = useMemo(
    () => users.filter((u) => u.hasFourSellerCookie).length,
    [users],
  );

  const openCreate = () => {
    setEditingUser(null);
    form.resetFields();
    form.setFieldsValue({
      isActive: true,
      roles: [],
      fourSellerCookie: undefined,
      fourSellerProxy: undefined,
      fulfilPin: undefined,
    });
    setModalOpen(true);
  };

  const openEdit = (user: AdminUser) => {
    setEditingUser(user);
    form.setFieldsValue({
      email: user.email ?? '',
      fullName: user.fullName,
      roles: user.roles,
      isActive: user.isActive,
      fourSellerCookie: undefined,
      fourSellerProxy: user.fourSellerProxy ?? undefined,
      fulfilPin: undefined,
    });
    setModalOpen(true);
  };

  const openResetPassword = (user: AdminUser) => {
    setResetUserId(user.id);
    resetForm.resetFields();
    setResetModalOpen(true);
  };

  const handleCheckOne = (user: AdminUser) => {
    if (!user.hasFourSellerCookie) return;
    setCheckingUserId(user.id);
    checkLoginOne.mutate(
      { id: user.id },
      {
        onSettled: () => setCheckingUserId(null),
      },
    );
  };

  const handleCheckAll = () => {
    checkLoginBulk.mutate(undefined);
  };

  const copyProxy = async (proxy: string) => {
    try {
      await navigator.clipboard.writeText(proxy);
      message.success('Đã copy proxy');
    } catch {
      message.error('Không thể copy proxy');
    }
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    const cookie = values.fourSellerCookie?.trim();
    const proxy = values.fourSellerProxy?.trim() ?? '';
    const pinTouched = form.isFieldTouched('fulfilPin');
    const pinRaw = values.fulfilPin?.trim() ?? '';
    if (editingUser) {
      await updateUser.mutateAsync({
        id: editingUser.id,
        data: {
          email: values.email,
          fullName: values.fullName,
          isActive: values.isActive,
          roles: values.roles,
          ...(cookie ? { fourSellerCookie: cookie } : {}),
          // Prefilled with the current value: send always so clearing the field clears the proxy.
          fourSellerProxy: proxy,
          ...(pinTouched ? { fulfilPin: pinRaw } : {}),
        },
      });
    } else {
      await createUser.mutateAsync({
        userName: values.userName!,
        email: values.email,
        fullName: values.fullName,
        password: values.password!,
        roles: values.roles,
        ...(cookie ? { fourSellerCookie: cookie } : {}),
        ...(proxy ? { fourSellerProxy: proxy } : {}),
        ...(pinRaw ? { fulfilPin: pinRaw } : {}),
      });
    }
    setModalOpen(false);
  };

  const handleResetPassword = async () => {
    const values = await resetForm.validateFields();
    if (resetUserId) {
      await resetPassword.mutateAsync({ id: resetUserId, data: values });
      setResetModalOpen(false);
    }
  };

  const resetImportState = () => {
    setImportFileList([]);
    setImportRows([]);
    setImportParsing(false);
  };

  const openImport = () => {
    resetImportState();
    setImportOpen(true);
  };

  const closeImport = () => {
    if (importUsers.isPending) return;
    setImportOpen(false);
    resetImportState();
  };

  const handleImportFile = async (file: File) => {
    setImportParsing(true);
    try {
      const parsed = parseUserAccountFile(await file.arrayBuffer());
      setImportRows(parsed.map((row) => ({ ...row, role: defaultImportRole })));
    } catch (error) {
      setImportRows([]);
      message.error(error instanceof Error ? error.message : 'Không đọc được file Excel.');
    } finally {
      setImportParsing(false);
    }
  };

  const handleImportConfirm = async () => {
    if (importableRows.length === 0) {
      message.warning('Không có dòng hợp lệ nào để import.');
      return;
    }

    let result;
    try {
      result = await importUsers.mutateAsync({
        items: importableRows.map((row) => ({
          userName: row.userName,
          email: row.email,
          fullName: row.fullName,
          password: row.password,
          roles: [resolveImportRole(row)!],
        })),
      });
    } catch {
      // Hook đã toast lỗi — giữ nguyên preview để người dùng thử lại.
      return;
    }

    if (result.failed.length === 0) {
      setImportOpen(false);
      resetImportState();
      return;
    }

    // Giữ modal mở với các dòng chưa vào được; `index` là vị trí trong mảng vừa gửi.
    const errorByRowIndex = new Map(
      result.failed
        .map((f) => [importableRows[f.index]?.rowIndex, f.error] as const)
        .filter((pair): pair is readonly [number, string] => pair[0] != null),
    );
    setImportRows((rows) =>
      rows
        .map((row) => ({ ...row, serverError: errorByRowIndex.get(row.rowIndex) }))
        .filter((row) => row.error || row.serverError),
    );
  };

  const onModalCheckLogin = () => {
    const draft = (form.getFieldValue('fourSellerCookie') as string | undefined)?.trim();
    if (!editingUser) return;
    if (!draft && !editingUser.hasFourSellerCookie) return;
    checkLoginOne.mutate({
      id: editingUser.id,
      data: draft ? { fourSellerCookie: draft } : {},
    });
  };

  const columns: ColumnsType<AdminUser> = [
    {
      title: '#',
      key: 'index',
      width: 56,
      align: 'center',
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: 'Họ tên',
      dataIndex: 'fullName',
      key: 'fullName',
      width: isMobile ? 180 : undefined,
      ellipsis: true,
      render: (name: string, record) =>
        isMobile ? (
          <div style={{ minWidth: 0 }}>
            <div style={{ fontWeight: 500, lineHeight: 1.35 }}>{name}</div>
            <Text type="secondary" style={{ fontSize: 11 }}>
              {record.userName}
            </Text>
          </div>
        ) : (
          name
        ),
    },
    { title: 'Username', dataIndex: 'userName', key: 'userName' },
    { title: 'Tổ', dataIndex: 'teamOwnerUserName', key: 'teamOwnerUserName', render: (v) => v || '—' },
    { title: 'Email', dataIndex: 'email', key: 'email', render: (v) => v || '—' },
    {
      title: 'Vai trò',
      dataIndex: 'roles',
      key: 'roles',
      width: isMobile ? 104 : undefined,
      ellipsis: isMobile,
      render: (userRoles: string[]) =>
        isMobile ? (
          <Tooltip title={userRoles.join(', ')}>
            <span>
              {userRoles.slice(0, 1).map((r) => (
                <Tag key={r} style={{ marginInlineEnd: 0 }}>
                  {r}
                </Tag>
              ))}
              {userRoles.length > 1 ? (
                <Text type="secondary" style={{ fontSize: 11, marginLeft: 4 }}>
                  +{userRoles.length - 1}
                </Text>
              ) : null}
            </span>
          </Tooltip>
        ) : (
          userRoles.map((r) => <Tag key={r}>{r}</Tag>)
        ),
    },
    {
      title: (
        <Tooltip title="Mã PIN extension Fulfil">
          <span>PIN</span>
        </Tooltip>
      ),
      key: 'pin',
      width: isMobile ? 48 : 64,
      align: 'center',
      render: (_, record) =>
        record.hasFulfilPin ? <Tag color="blue">{isMobile ? '✓' : 'Có'}</Tag> : '—',
    },
    {
      title: (
        <Tooltip title="4Seller Cookie">
          <span>{isMobile ? '4S' : '4Seller'}</span>
        </Tooltip>
      ),
      key: 'cookieLoginStatus',
      width: isMobile ? 64 : 140,
      render: (_, record) => cookieStatusTag(resolveCookieStatus(record), isMobile),
    },
    {
      title: 'Proxy',
      dataIndex: 'fourSellerProxy',
      key: 'fourSellerProxy',
      width: isMobile ? 118 : 110,
      render: (proxy?: string | null) =>
        proxy ? (
          <Tooltip title="Nhấn để sao chép proxy">
            <Text
              code
              className="users-proxy-code"
              role="button"
              tabIndex={0}
              style={{ cursor: 'pointer' }}
              onClick={() => void copyProxy(proxy)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  void copyProxy(proxy);
                }
              }}
            >
              {shortenProxy(proxy)}
            </Text>
          </Tooltip>
        ) : (
          '—'
        ),
    },
    {
      title: isMobile ? 'TT' : 'Trạng thái',
      dataIndex: 'isActive',
      key: 'isActive',
      width: isMobile ? 72 : undefined,
      render: (active: boolean) => (
        <Tag color={active ? 'green' : 'default'}>
          {isMobile ? (active ? 'ON' : 'OFF') : active ? 'Hoạt động' : 'Vô hiệu'}
        </Tag>
      ),
    },
    {
      title: 'Ngày tạo',
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (v: string) => formatDateTimeVn(v),
    },
    ...(canManage || canView
      ? [
          {
            title: isMobile ? '' : 'Thao tác',
            key: 'actions',
            width: isMobile ? (canManage ? 136 : 48) : canManage ? 180 : 56,
            align: isMobile ? ('right' as const) : undefined,
            render: (_: unknown, record: AdminUser) => (
              <Space size={isMobile ? 0 : undefined}>
                {(canView || canManage) && (
                  <Tooltip title={record.hasFourSellerCookie ? 'Check Cookie 4Seller' : 'Chưa có cookie'}>
                    <Button
                      type="text"
                      icon={<SafetyCertificateOutlined />}
                      disabled={!record.hasFourSellerCookie}
                      loading={checkingUserId === record.id}
                      onClick={() => handleCheckOne(record)}
                    />
                  </Tooltip>
                )}
                {canManage && (
                  <>
                    <Button type="text" icon={<EditOutlined />} onClick={() => openEdit(record)} />
                    <Button type="text" icon={<KeyOutlined />} onClick={() => openResetPassword(record)} />
                    <Popconfirm
                      title="Xóa hẳn người dùng này?"
                      description="Không thể hoàn tác. User còn shop/đơn/listing sẽ bị chặn."
                      onConfirm={() => deleteUser.mutate(record.id)}
                    >
                      <Button type="text" danger icon={<DeleteOutlined />} />
                    </Popconfirm>
                  </>
                )}
              </Space>
            ),
          } as ColumnsType<AdminUser>[number],
        ]
      : []),
  ];

  const visibleColumns = isMobile
    ? columns.filter((col) => MOBILE_USER_COLUMN_KEYS.has(String(col.key)))
    : columns;

  return (
    <div>
      <PageHeader
        title={isMobile ? 'Người dùng' : 'Quản lý Người dùng'}
        extra={
          canManage ? (
            <Space size={8}>
              <Tooltip title="Import tài khoản từ file Excel">
                <Button icon={<UploadOutlined />} onClick={openImport} aria-label="Import file">
                  {isMobile ? null : 'Import file'}
                </Button>
              </Tooltip>
              <Tooltip title="Thêm người dùng">
                <Button type="primary" icon={<PlusOutlined />} onClick={openCreate} aria-label="Thêm người dùng">
                  {isMobile ? null : 'Thêm người dùng'}
                </Button>
              </Tooltip>
            </Space>
          ) : undefined
        }
      />

      <div className="admin-filters">
        <div className="admin-filters-actions" style={{ marginLeft: 0 }}>
          {(canView || canManage) && (
            <Tooltip title="Check login 4Seller">
              <Button
                icon={<SafetyCertificateOutlined />}
                onClick={handleCheckAll}
                loading={checkLoginBulk.isPending}
                disabled={usersWithCookieCount === 0 || checkLoginOne.isPending}
                aria-label="Check login"
              >
                {isMobile ? null : 'Check login'}
              </Button>
            </Tooltip>
          )}
          <Button
            icon={<ReloadOutlined />}
            onClick={() => void refetch()}
            loading={isFetching}
            aria-label="Tải lại"
          >
            {isMobile ? null : 'Tải lại'}
          </Button>
        </div>
      </div>
      <Table
        className="admin-table-wrap"
        rowKey="id"
        columns={visibleColumns}
        dataSource={users}
        loading={isLoading || isFetching || checkLoginBulk.isPending}
        size="small"
        scroll={{ x: isMobile ? 722 : 1024 }}
        pagination={tablePagination(users.length, {
          size: 'small',
          simple: isMobile,
          pageSize: pagination.pageSize,
          current: pagination.current,
          showSizeChanger: !isMobile,
          onChange: (page, pageSize) => setPagination({ current: page, pageSize }),
        })}
      />

      <Modal
        className="users-edit-modal"
        title={editingUser ? 'Sửa người dùng' : 'Thêm người dùng'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={handleSubmit}
        confirmLoading={createUser.isPending || updateUser.isPending}
        destroyOnHidden
        width={isMobile ? '100%' : 560}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          {!editingUser && (
            <>
              <Form.Item
                name="userName"
                label="Tên đăng nhập"
                rules={[{ required: true, message: 'Nhập tên đăng nhập' }]}
              >
                <Input />
              </Form.Item>
              <Form.Item
                name="password"
                label="Mật khẩu"
                rules={passwordFormRules()}
                extra={PASSWORD_HINT}
              >
                <Input.Password />
              </Form.Item>
            </>
          )}
          <Form.Item name="fullName" label="Họ tên" rules={[{ required: true, message: 'Nhập họ tên' }]}>
            <Input />
          </Form.Item>
          <Form.Item
            name="email"
            label="Email"
            rules={[{ required: true, type: 'email', message: 'Nhập email hợp lệ' }]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="roles" label="Vai trò" rules={[{ required: true, message: 'Chọn ít nhất một vai trò' }]}>
            <Select
              mode="multiple"
              options={roleOptions}
              placeholder="Chọn vai trò"
              loading={rolesLoading}
              disabled={rolesError}
            />
          </Form.Item>
          <Form.Item
            name="fulfilPin"
            label="Mã PIN extension (4 số)"
            extra={
              editingUser?.hasFulfilPin
                ? `${PIN_HINT} Tài khoản này đã có PIN.`
                : PIN_HINT
            }
            rules={[
              {
                validator: async (_, value) => {
                  const v = typeof value === 'string' ? value.trim() : '';
                  if (!v) return;
                  if (!/^\d{4}$/.test(v)) {
                    throw new Error('Mã PIN phải gồm đúng 4 chữ số');
                  }
                },
              },
            ]}
          >
            <Input
              maxLength={4}
              inputMode="numeric"
              placeholder={editingUser ? 'Để trống nếu không đổi' : '4 chữ số'}
              autoComplete="off"
              allowClear
            />
          </Form.Item>
          <Form.Item
            name="fourSellerCookie"
            label="4Seller Cookie"
            extra={
              editingUser?.hasFourSellerCookie
                ? `${COOKIE_HINT} Tài khoản này đã có cookie.`
                : COOKIE_HINT
            }
          >
            <Input.TextArea
              rows={3}
              placeholder={editingUser ? 'Để trống nếu không đổi' : 'Dán cookie 4Seller'}
              autoComplete="off"
            />
          </Form.Item>
          <Form.Item
            name="fourSellerProxy"
            label="Proxy 4Seller"
            extra="Proxy residential riêng cho seller (sticky), tránh EdgeOne chặn IP (567). Định dạng: ip:port:user:pass (mặc định HTTP CONNECT); thêm https:// nếu proxy HTTPS; socks5:// cho SOCKS5. Để trống = dùng pool/direct."
            rules={[{ max: 300, message: 'Tối đa 300 ký tự' }]}
          >
            <Input placeholder="vd: 74.0.103.54:50100:kbiproxy:abc123" autoComplete="off" allowClear />
          </Form.Item>
          {editingUser && (
            <Form.Item label=" " colon={false}>
              <Button
                icon={<SafetyCertificateOutlined />}
                onClick={onModalCheckLogin}
                loading={checkLoginOne.isPending}
                disabled={!canCheckCookie || checkLoginBulk.isPending}
              >
                Check login
              </Button>
            </Form.Item>
          )}
          {editingUser && (
            <Form.Item name="isActive" label="Hoạt động" valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
        </Form>
      </Modal>

      <Modal
        className="users-edit-modal"
        title="Đặt lại mật khẩu"
        open={resetModalOpen}
        onCancel={() => setResetModalOpen(false)}
        onOk={handleResetPassword}
        confirmLoading={resetPassword.isPending}
        destroyOnHidden
        width={isMobile ? '100%' : undefined}
      >
        <Form form={resetForm} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            name="newPassword"
            label="Mật khẩu mới"
            rules={passwordFormRules('Mật khẩu mới')}
            extra={PASSWORD_HINT}
          >
            <Input.Password />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        className="users-edit-modal"
        title="Import tài khoản từ file"
        open={importOpen}
        onCancel={closeImport}
        destroyOnHidden
        width={isMobile ? '100%' : 900}
        footer={[
          <Button key="cancel" onClick={closeImport} disabled={importUsers.isPending}>
            Huỷ
          </Button>,
          <Button
            key="confirm"
            type="primary"
            loading={importUsers.isPending}
            disabled={importableRows.length === 0 || importParsing}
            onClick={() => void handleImportConfirm()}
          >
            {`Xác nhận (${importableRows.length})`}
          </Button>,
        ]}
      >
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          message="Định dạng file"
          description={
            <>
              <div>{IMPORT_FORMAT_HINT}</div>
              <div style={{ marginTop: 4 }}>
                Tài khoản đã tồn tại (khớp theo Tên đăng nhập hoặc Email) sẽ được cập nhật họ tên,
                email, vai trò và mật khẩu mới; chưa có thì tạo mới.
              </div>
            </>
          }
        />

        <Upload.Dragger
          accept=".xlsx,.xls"
          maxCount={1}
          fileList={importFileList}
          disabled={importParsing || importUsers.isPending}
          beforeUpload={(file) => {
            setImportFileList([{ uid: file.uid, name: file.name, status: 'done' }]);
            void handleImportFile(file);
            return false;
          }}
          onRemove={() => {
            resetImportState();
            return true;
          }}
        >
          <p className="ant-upload-drag-icon">
            <InboxOutlined />
          </p>
          <p className="ant-upload-text">Chọn hoặc kéo file danh sách tài khoản vào đây</p>
          <p className="ant-upload-hint">Định dạng .xlsx / .xls</p>
        </Upload.Dragger>

        {importRows.length > 0 && (
          <Table<ImportPreviewRow>
            style={{ marginTop: 16 }}
            rowKey="rowIndex"
            size="small"
            loading={importParsing}
            dataSource={importRows}
            scroll={{ x: isMobile ? 640 : undefined, y: 320 }}
            pagination={false}
            columns={[
              { title: 'Dòng', dataIndex: 'rowIndex', key: 'rowIndex', width: 60 },
              { title: 'Tên đăng nhập', dataIndex: 'userName', key: 'userName', width: 140, ellipsis: true },
              { title: 'Email', dataIndex: 'email', key: 'email', width: 180, ellipsis: true },
              { title: 'Họ tên', dataIndex: 'fullName', key: 'fullName', width: 150, ellipsis: true },
              {
                title: 'Mật khẩu',
                dataIndex: 'password',
                key: 'password',
                width: 90,
                render: (password: string) => (password ? '••••••••' : '—'),
              },
              {
                title: 'Thao tác',
                key: 'action',
                width: 100,
                render: (_, row) => {
                  if (row.error) return <Tag>Bỏ qua</Tag>;
                  const exists =
                    existingByUserName.has(row.userName.toLowerCase()) ||
                    existingByEmail.has(row.email.toLowerCase());
                  return exists ? <Tag color="blue">Cập nhật</Tag> : <Tag color="green">Thêm mới</Tag>;
                },
              },
              {
                title: 'Vai trò',
                key: 'role',
                width: 160,
                render: (_, row) => (
                  <Select
                    size="small"
                    style={{ width: '100%' }}
                    value={resolveImportRole(row)}
                    options={roleOptions}
                    loading={rolesLoading}
                    disabled={rolesError || importUsers.isPending}
                    onChange={(role) =>
                      setImportRows((rows) =>
                        rows.map((r) => (r.rowIndex === row.rowIndex ? { ...r, role } : r)),
                      )
                    }
                  />
                ),
              },
              {
                title: 'Lỗi',
                key: 'error',
                width: 220,
                render: (_, row) =>
                  row.error || row.serverError ? (
                    <Text type="danger" style={{ fontSize: 12 }}>
                      {row.error ?? row.serverError}
                    </Text>
                  ) : (
                    '—'
                  ),
              },
            ]}
          />
        )}
      </Modal>
    </div>
  );
};
