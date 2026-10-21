import { useState } from 'react';
import {
  Button,
  Descriptions,
  Form,
  Input,
  Popconfirm,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tabs,
  Tag,
  Tooltip,
  Typography,
  Upload,
  message,
} from 'antd';
import { Modal } from '../../components/AppModal';
import {
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  EyeOutlined,
  InboxOutlined,
  PlusOutlined,
  ReloadOutlined,
  UploadOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { UploadFile } from 'antd/es/upload/interface';
import type { CSSProperties, MouseEvent } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { PageHeader } from '../../components/PageHeader';
import { useIsMobile } from '../../hooks/useIsMobile';
import {
  useCreateReceivingBankAccount,
  useCreateReceivingSourceAccount,
  useDeleteReceivingBankAccount,
  useDeleteReceivingSourceAccount,
  useReceivingBankAccount,
  useReceivingBankAccounts,
  useReceivingCardSources,
  useReceivingSourceAccountOptions,
  useReceivingSourceAccounts,
  useUpdateReceivingBankAccount,
  useUpdateReceivingSourceAccount,
} from '../../hooks/useAdminReceivingBanks';
import { adminReceivingBankApi } from '../../services/adminReceivingBankApi';
import { useAuthStore } from '../../stores/authStore';
import {
  RECEIVING_ACCOUNT_CURRENCIES,
  RECEIVING_ACCOUNT_TYPES,
  type ReceivingBankAccount,
  type ReceivingBankAccountListItem,
  type ReceivingSourceAccount,
} from '../../types/receivingBank';
import { tablePagination } from '../../utils/pagination';
import {
  normalizeAccountNumber,
  parsePingPongGlobalAccountFile,
  type ImportCardRow,
} from './pingpongImport';
import '@drop/receiving-banks/receivingBanks.css';

const { Text } = Typography;

const ellipsisStyle: CSSProperties = {
  display: 'block',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  cursor: 'pointer',
};

const copyToClipboard = async (text: string, successMessage = 'Đã copy') => {
  await navigator.clipboard.writeText(text);
  message.success(successMessage);
};

const copyableEllipsis = (value?: string | null, successMessage = 'Đã copy') => {
  if (!value) return '—';

  const handleCopy = (e: MouseEvent) => {
    e.stopPropagation();
    void copyToClipboard(value, successMessage);
  };

  return (
    <Tooltip title={value}>
      <span
        role="button"
        tabIndex={0}
        onClick={handleCopy}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            handleCopy(e as unknown as MouseEvent);
          }
        }}
        style={ellipsisStyle}
      >
        {value}
      </span>
    </Tooltip>
  );
};

interface CardFormValues {
  sourceId: string;
  sourceAccountId?: string;
  currency: string;
  bankName: string;
  bankLocation?: string;
  accountHolderName: string;
  accountType?: string;
  swiftBic?: string;
  routingNumber?: string;
  accountNumber: string;
  bankAddress?: string;
  isActive: boolean;
}

interface SourceAccountFormValues {
  sourceId: string;
  name: string;
  externalAccountId?: string;
  username?: string;
  note?: string;
  isActive: boolean;
}

interface ImportFormValues {
  sourceId: string;
  sourceAccountId?: string;
  currency: string;
  accountType?: string;
}

type ImportPreviewRow = ImportCardRow & {
  action: 'create' | 'update' | 'skip';
  existingId?: string;
  /** All DB cards sharing sourceId + accountNumber (duplicates). */
  existingIds?: string[];
};

const copyableField = (value?: string | null) => {
  if (!value) return '—';
  return (
    <Text copyable={{ text: value }} style={{ wordBreak: 'break-all' }}>
      {value}
    </Text>
  );
};

const buildCopyText = (row: ReceivingBankAccount) =>
  [
    `Nguồn thẻ: ${row.sourceName}`,
    row.sourceAccountName ? `Tài khoản nguồn: ${row.sourceAccountName}` : null,
    `Account currency: ${row.currency}`,
    `Bank name: ${row.bankName}`,
    row.bankLocation ? `Bank location: ${row.bankLocation}` : null,
    `Account holder's name: ${row.accountHolderName}`,
    row.accountType ? `Account type: ${row.accountType}` : null,
    row.swiftBic ? `SWIFT / BIC: ${row.swiftBic}` : null,
    row.routingNumber ? `Routing number: ${row.routingNumber}` : null,
    `Bank account number: ${row.accountNumber}`,
    row.bankAddress ? `Bank address: ${row.bankAddress}` : null,
  ]
    .filter(Boolean)
    .join('\n');

const fetchAccountDetail = async (id: string): Promise<ReceivingBankAccount> => {
  const response = await adminReceivingBankApi.getById(id);
  if (!response.success || !response.data) {
    throw new Error(response.message || 'Không tải được chi tiết thẻ.');
  }
  return response.data;
};

export const ReceivingBanksPage = () => {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canManage = hasPermission('admin.shops.manage');
  const isMobile = useIsMobile();
  const queryClient = useQueryClient();

  const [activeTab, setActiveTab] = useState('cards');
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<ReceivingBankAccount | null>(null);
  const [editLoading, setEditLoading] = useState(false);
  const [viewingId, setViewingId] = useState<string | null>(null);
  const [form] = Form.useForm<CardFormValues>();
  const selectedSourceId = Form.useWatch('sourceId', form);

  const [saModalOpen, setSaModalOpen] = useState(false);
  const [editingSa, setEditingSa] = useState<ReceivingSourceAccount | null>(null);
  const [saForm] = Form.useForm<SourceAccountFormValues>();
  const [pagination, setPagination] = useState({ current: 1, pageSize: 100 });
  const [saPagination, setSaPagination] = useState({ current: 1, pageSize: 100 });

  const [importOpen, setImportOpen] = useState(false);
  const [importForm] = Form.useForm<ImportFormValues>();
  const importSourceId = Form.useWatch('sourceId', importForm);
  const [importFileList, setImportFileList] = useState<UploadFile[]>([]);
  const [importFile, setImportFile] = useState<File | null>(null);
  const [importRows, setImportRows] = useState<ImportPreviewRow[]>([]);
  const [importParsing, setImportParsing] = useState(false);
  const [importSubmitting, setImportSubmitting] = useState(false);
  const [importProgress, setImportProgress] = useState<{ done: number; total: number } | null>(
    null,
  );

  // Sources are a tiny seeded set (cached); needed for create/edit forms.
  const { data: sources = [] } = useReceivingCardSources();
  const {
    data: accounts = [],
    isLoading,
    isFetching,
    refetch,
  } = useReceivingBankAccounts(activeTab === 'cards');
  const {
    data: sourceAccounts = [],
    isLoading: sourceAccountsLoading,
    isFetching: sourceAccountsFetching,
    refetch: refetchSourceAccounts,
  } = useReceivingSourceAccounts(undefined, activeTab === 'source-accounts');

  const {
    data: viewing,
    isLoading: viewingLoading,
    isFetching: viewingFetching,
  } = useReceivingBankAccount(viewingId);

  const createAccount = useCreateReceivingBankAccount();
  const updateAccount = useUpdateReceivingBankAccount();
  const deleteAccount = useDeleteReceivingBankAccount();
  const createSourceAccount = useCreateReceivingSourceAccount();
  const updateSourceAccount = useUpdateReceivingSourceAccount();
  const deleteSourceAccount = useDeleteReceivingSourceAccount();

  const selectedSource = sources.find((s) => s.id === selectedSourceId);
  const needsSourceAccount = !!selectedSource?.supportsAccounts;

  const importSelectedSource = sources.find((s) => s.id === importSourceId);
  const importNeedsSourceAccount = !!importSelectedSource?.supportsAccounts;

  const { data: sourceAccountOptions = [], isLoading: sourceAccountOptionsLoading } =
    useReceivingSourceAccountOptions(selectedSourceId, needsSourceAccount && modalOpen);

  const { data: importSourceAccountOptions = [], isLoading: importSourceAccountOptionsLoading } =
    useReceivingSourceAccountOptions(
      importSourceId,
      importNeedsSourceAccount && importOpen,
    );

  const currencyOptions = RECEIVING_ACCOUNT_CURRENCIES.map((c) => ({ label: c, value: c }));
  const accountTypeOptions = RECEIVING_ACCOUNT_TYPES.map((t) => ({
    label: t.label,
    value: t.value,
  }));
  const sourceOptions = sources.map((s) => ({ value: s.id, label: s.name }));
  const sourcesWithAccounts = sources
    .filter((s) => s.supportsAccounts)
    .map((s) => ({ value: s.id, label: s.name }));

  const modalWidth = isMobile ? '100%' : 720;
  const modalBodyStyles = isMobile
    ? { body: { paddingTop: 8, maxHeight: '70vh', overflowY: 'auto' as const } }
    : { body: { paddingTop: 4 } };

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    const defaultSource = sources.find((s) => s.code === 'PINGPONGX') ?? sources[0];
    form.setFieldsValue({
      currency: 'USD',
      accountType: 'Checking',
      isActive: true,
      sourceId: defaultSource?.id,
    });
    setModalOpen(true);
  };

  const fillEditForm = (row: ReceivingBankAccount) => {
    setEditing(row);
    form.setFieldsValue({
      sourceId: row.sourceId,
      sourceAccountId: row.sourceAccountId ?? undefined,
      currency: row.currency,
      bankName: row.bankName,
      bankLocation: row.bankLocation ?? undefined,
      accountHolderName: row.accountHolderName,
      accountType: row.accountType ?? undefined,
      swiftBic: row.swiftBic ?? undefined,
      routingNumber: row.routingNumber ?? undefined,
      accountNumber: row.accountNumber,
      bankAddress: row.bankAddress ?? undefined,
      isActive: row.isActive,
    });
    setModalOpen(true);
  };

  const openEdit = async (id: string) => {
    setEditLoading(true);
    try {
      const detail = await queryClient.fetchQuery({
        queryKey: ['adminReceivingBank', id],
        queryFn: () => fetchAccountDetail(id),
      });
      fillEditForm(detail);
    } catch (error) {
      message.error(error instanceof Error ? error.message : 'Không tải được chi tiết thẻ.');
    } finally {
      setEditLoading(false);
    }
  };

  const copyAccountInfo = async (row: ReceivingBankAccount) => {
    await navigator.clipboard.writeText(buildCopyText(row));
    message.success('Đã copy thông tin thẻ nhận tiền');
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    const payload = {
      sourceId: values.sourceId,
      sourceAccountId: needsSourceAccount ? values.sourceAccountId || null : null,
      currency: values.currency,
      bankName: values.bankName,
      bankLocation: values.bankLocation || null,
      accountHolderName: values.accountHolderName,
      accountType: values.accountType || null,
      swiftBic: values.swiftBic || null,
      routingNumber: values.routingNumber || null,
      accountNumber: values.accountNumber,
      bankAddress: values.bankAddress || null,
      isActive: values.isActive,
    };
    if (editing) {
      await updateAccount.mutateAsync({ id: editing.id, data: payload });
    } else {
      await createAccount.mutateAsync(payload);
    }
    setModalOpen(false);
  };

  const openCreateSa = () => {
    setEditingSa(null);
    saForm.resetFields();
    const defaultSource = sourcesWithAccounts[0];
    saForm.setFieldsValue({
      isActive: true,
      sourceId: defaultSource?.value,
    });
    setSaModalOpen(true);
  };

  const openEditSa = (row: ReceivingSourceAccount) => {
    setEditingSa(row);
    saForm.setFieldsValue({
      sourceId: row.sourceId,
      name: row.name,
      externalAccountId: row.externalAccountId ?? undefined,
      username: row.username ?? undefined,
      note: row.note ?? undefined,
      isActive: row.isActive,
    });
    setSaModalOpen(true);
  };

  const handleSaSubmit = async () => {
    const values = await saForm.validateFields();
    if (editingSa) {
      await updateSourceAccount.mutateAsync({
        id: editingSa.id,
        data: {
          name: values.name,
          externalAccountId: values.externalAccountId || null,
          username: values.username || null,
          note: values.note || null,
          isActive: values.isActive,
        },
      });
    } else {
      await createSourceAccount.mutateAsync({
        sourceId: values.sourceId,
        name: values.name,
        externalAccountId: values.externalAccountId || null,
        username: values.username || null,
        note: values.note || null,
        isActive: values.isActive,
      });
    }
    setSaModalOpen(false);
  };

  const resetImportState = () => {
    setImportFileList([]);
    setImportFile(null);
    setImportRows([]);
    setImportParsing(false);
    setImportSubmitting(false);
    setImportProgress(null);
  };

  const openImport = () => {
    resetImportState();
    importForm.resetFields();
    const defaultSource = sources.find((s) => s.code === 'PINGPONGX') ?? sources[0];
    importForm.setFieldsValue({
      currency: 'USD',
      accountType: 'Checking',
      sourceId: defaultSource?.id,
    });
    setImportOpen(true);
  };

  const closeImport = () => {
    if (importSubmitting) return;
    setImportOpen(false);
    resetImportState();
  };

  const fetchAccountsSnapshot = async (): Promise<ReceivingBankAccountListItem[]> => {
    const response = await adminReceivingBankApi.getAll();
    if (!response.success) {
      throw new Error(response.message || 'Không tải được danh sách thẻ.');
    }
    return response.data ?? [];
  };

  /** Preview actions: DB match by sourceId+accountNumber; trùng trong file → Cập nhật sau dòng đầu. */
  const buildImportPreview = (
    parsed: ImportCardRow[],
    sourceId: string,
    existingAccounts: ReceivingBankAccountListItem[],
  ): ImportPreviewRow[] => {
    const existingByNumber = new Map<string, string[]>();
    for (const a of existingAccounts) {
      if (a.sourceId !== sourceId) continue;
      const key = normalizeAccountNumber(a.accountNumber);
      const list = existingByNumber.get(key) ?? [];
      list.push(a.id);
      existingByNumber.set(key, list);
    }
    const seenInFile = new Set<string>();

    return parsed.map((row) => {
      if (row.error) {
        return { ...row, action: 'skip' as const };
      }
      const key = normalizeAccountNumber(row.accountNumber);
      const existingIds = existingByNumber.get(key);

      if (existingIds && existingIds.length > 0) {
        seenInFile.add(key);
        return {
          ...row,
          action: 'update' as const,
          existingId: existingIds[0],
          existingIds,
        };
      }
      if (seenInFile.has(key)) {
        // Trùng trong file (chưa có trên DB): dòng sau sẽ update bản tạo từ dòng đầu.
        return { ...row, action: 'update' as const };
      }
      seenInFile.add(key);
      return { ...row, action: 'create' as const };
    });
  };

  const stripPreviewMeta = (rows: ImportPreviewRow[]): ImportCardRow[] =>
    rows.map((r) => ({
      rowIndex: r.rowIndex,
      accountNumber: r.accountNumber,
      bankName: r.bankName,
      bankLocation: r.bankLocation,
      accountHolderName: r.accountHolderName,
      accountType: r.accountType,
      swiftBic: r.swiftBic,
      routingNumber: r.routingNumber,
      bankAddress: r.bankAddress,
      currency: r.currency,
      isActive: r.isActive,
      error: r.error,
    }));

  const handleImportProcess = async () => {
    try {
      const fields: (keyof ImportFormValues)[] = ['sourceId', 'currency'];
      if (importNeedsSourceAccount) fields.push('sourceAccountId');
      await importForm.validateFields(fields);
    } catch {
      return;
    }
    if (!importFile) {
      message.warning('Chọn file Excel PingPongX trước.');
      return;
    }

    setImportParsing(true);
    try {
      const [buffer, existingAccounts] = await Promise.all([
        importFile.arrayBuffer(),
        fetchAccountsSnapshot(),
      ]);
      const parsed = parsePingPongGlobalAccountFile(buffer);
      const sourceId = importForm.getFieldValue('sourceId') as string;
      const preview = buildImportPreview(parsed, sourceId, existingAccounts);
      setImportRows(preview);
      // Keep react-query cache in sync for subsequent sourceId changes.
      queryClient.setQueryData(['adminReceivingBanks'], existingAccounts);

      const valid = preview.filter((r) => r.action !== 'skip').length;
      const skipped = preview.length - valid;
      const toCreate = preview.filter((r) => r.action === 'create').length;
      const toUpdate = preview.filter((r) => r.action === 'update').length;
      message.success(
        skipped > 0
          ? `Đã xử lý ${preview.length} dòng: ${toCreate} mới, ${toUpdate} cập nhật, ${skipped} lỗi.`
          : `Đã xử lý ${preview.length} dòng: ${toCreate} mới, ${toUpdate} cập nhật.`,
      );
    } catch (error) {
      setImportRows([]);
      message.error(error instanceof Error ? error.message : 'Không đọc được file Excel.');
    } finally {
      setImportParsing(false);
    }
  };

  const handleImportSubmit = async () => {
    let values: ImportFormValues;
    try {
      values = await importForm.validateFields();
    } catch {
      return;
    }

    const validRows = importRows.filter((r) => r.action !== 'skip' && !r.error);
    if (validRows.length === 0) {
      message.warning('Không có dòng hợp lệ để thêm. Hãy Xử lý file trước.');
      return;
    }

    const source = sources.find((s) => s.id === values.sourceId);
    const needsSa = !!source?.supportsAccounts;
    if (needsSa && !values.sourceAccountId) {
      message.warning('Chọn tài khoản nguồn trước khi thêm.');
      return;
    }

    setImportSubmitting(true);
    setImportProgress({ done: 0, total: validRows.length });

    let created = 0;
    let updated = 0;
    let updatedCards = 0;
    let failed = 0;
    let firstError: string | null = null;

    try {
      // Fresh snapshot — tránh tạo trùng khi list cache rỗng/stale lúc preview.
      // Gom mọi id trùng sourceId+accountNumber (DB có thể đã có duplicate).
      const existingAccounts = await fetchAccountsSnapshot();
      const known = new Map<string, string[]>();
      for (const a of existingAccounts) {
        if (a.sourceId !== values.sourceId) continue;
        const key = normalizeAccountNumber(a.accountNumber);
        const list = known.get(key) ?? [];
        list.push(a.id);
        known.set(key, list);
      }

      for (let i = 0; i < validRows.length; i++) {
        const row = validRows[i]!;
        const accountNumber = normalizeAccountNumber(row.accountNumber);
        const payload = {
          sourceId: values.sourceId,
          sourceAccountId: needsSa ? values.sourceAccountId || null : null,
          currency: values.currency || row.currency || 'USD',
          bankName: row.bankName,
          bankLocation: row.bankLocation || null,
          accountHolderName: row.accountHolderName,
          accountType: row.accountType || values.accountType || null,
          swiftBic: row.swiftBic || null,
          routingNumber: row.routingNumber || null,
          accountNumber,
          bankAddress: row.bankAddress || null,
          isActive: row.isActive,
        };

        try {
          const existingIds = known.get(accountNumber) ?? [];
          if (existingIds.length > 0) {
            for (const existingId of existingIds) {
              const response = await adminReceivingBankApi.update(existingId, payload);
              if (!response.success) {
                throw new Error(response.message || 'Cập nhật thất bại');
              }
            }
            known.set(accountNumber, existingIds);
            updated += 1;
            updatedCards += existingIds.length;
          } else {
            const response = await adminReceivingBankApi.create(payload);
            if (!response.success || !response.data) {
              throw new Error(response.message || 'Tạo thất bại');
            }
            known.set(accountNumber, [response.data.id]);
            created += 1;
          }
        } catch (error) {
          failed += 1;
          if (!firstError) {
            firstError =
              error instanceof Error ? error.message : 'Lỗi không xác định';
          }
        }

        setImportProgress({ done: i + 1, total: validRows.length });
      }

      await queryClient.invalidateQueries({ queryKey: ['adminReceivingBanks'] });
      await queryClient.invalidateQueries({ queryKey: ['receivingBankOptions'] });

      setImportOpen(false);
      resetImportState();

      if (failed === 0) {
        message.success(
          updatedCards > updated
            ? `Import xong: ${created} mới, ${updated} cập nhật (${updatedCards} thẻ, có bản trùng số TK).`
            : `Import xong: ${created} mới, ${updated} cập nhật.`,
        );
      } else {
        message.warning(
          `Import xong: ${created} mới, ${updated} cập nhật, ${failed} lỗi${
            firstError ? ` (lỗi đầu: ${firstError})` : ''
          }.`,
        );
      }
    } catch (error) {
      message.error(
        error instanceof Error ? error.message : 'Import thất bại — thử lại.',
      );
    } finally {
      setImportSubmitting(false);
      setImportProgress(null);
    }
  };

  const cardColumns: ColumnsType<ReceivingBankAccountListItem> = [
    {
      title: '#',
      key: 'index',
      width: 40,
      align: 'center',
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: 'Nguồn',
      key: 'source',
      width: 180,
      ellipsis: true,
      render: (_, row) => {
        const label = row.sourceAccountName
          ? `${row.sourceName} · ${row.sourceAccountName}`
          : row.sourceName || '';
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 0 }}>
            <Text
              ellipsis={{ tooltip: label || undefined }}
              style={{ maxWidth: 120, fontWeight: 500, flex: 1, minWidth: 0 }}
            >
              {label || '—'}
            </Text>
            <Tag style={{ margin: 0, flexShrink: 0 }}>{row.currency}</Tag>
          </div>
        );
      },
    },
    {
      title: 'Tài khoản',
      key: 'account',
      ellipsis: true,
      render: (_, row) => {
        const label = `${row.accountHolderName} · ${row.accountNumber}`;
        return (
          <Tooltip title={label}>
            <span
              role="button"
              tabIndex={0}
              onClick={(e) => {
                e.stopPropagation();
                void copyToClipboard(row.accountNumber, 'Đã copy số TK');
              }}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  e.stopPropagation();
                  void copyToClipboard(row.accountNumber, 'Đã copy số TK');
                }
              }}
              style={ellipsisStyle}
            >
              {label}
            </span>
          </Tooltip>
        );
      },
    },
    {
      title: 'Shop',
      key: 'shops',
      width: 200,
      ellipsis: true,
      render: (_, row) => {
        const shops = row.shops ?? [];
        if (shops.length === 0) return '—';
        const labels = shops.map((s) =>
          s.profileName ? `${s.profileName} · ${s.name}` : s.name,
        );
        const text = labels.join(', ');
        return (
          <Text ellipsis={{ tooltip: text }} style={{ maxWidth: 188 }}>
            {text}
          </Text>
        );
      },
    },
    {
      title: 'Gắn shop',
      key: 'linked',
      width: 100,
      render: (_, row) => {
        const linked = (row.shopCount ?? 0) > 0 || (row.shops?.length ?? 0) > 0;
        return (
          <Tag color={linked ? 'blue' : 'default'} style={{ margin: 0 }}>
            {linked ? 'Đã gắn' : 'Chưa gắn'}
          </Tag>
        );
      },
    },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 96,
      render: (active: boolean) => (
        <Tag color={active ? 'green' : 'default'} style={{ margin: 0 }}>
          {active ? 'On' : 'Off'}
        </Tag>
      ),
    },
    {
      title: '',
      key: 'actions',
      width: canManage ? 88 : 40,
      align: 'right',
      render: (_, record) => (
        <Space size={0} onClick={(e) => e.stopPropagation()}>
          <Button
            type="text"
            size="small"
            icon={<EyeOutlined />}
            title="Xem nhanh"
            onClick={() => setViewingId(record.id)}
          />
          {canManage ? (
            <>
              <Button
                type="text"
                size="small"
                icon={<EditOutlined />}
                title="Sửa"
                loading={editLoading}
                onClick={() => void openEdit(record.id)}
              />
              <Popconfirm
                title="Xóa thẻ nhận tiền này?"
                disabled={record.shopCount > 0}
                onConfirm={() => deleteAccount.mutate(record.id)}
              >
                <Button
                  type="text"
                  size="small"
                  danger
                  icon={<DeleteOutlined />}
                  title="Xóa"
                  disabled={record.shopCount > 0}
                />
              </Popconfirm>
            </>
          ) : null}
        </Space>
      ),
    },
  ];

  const saColumns: ColumnsType<ReceivingSourceAccount> = [
    {
      title: '#',
      key: 'index',
      width: 40,
      align: 'center',
      render: (_, __, index) => (saPagination.current - 1) * saPagination.pageSize + index + 1,
    },
    {
      title: 'Nguồn',
      dataIndex: 'sourceName',
      key: 'sourceName',
      width: 110,
      ellipsis: true,
    },
    {
      title: 'Tài khoản',
      key: 'account',
      ellipsis: true,
      render: (_, row) => {
        const label = row.username ? `${row.name} · ${row.username}` : row.name;
        return copyableEllipsis(label);
      },
    },
    {
      title: 'External ID',
      dataIndex: 'externalAccountId',
      key: 'externalAccountId',
      width: 140,
      ellipsis: true,
      render: (v?: string) => copyableEllipsis(v),
    },
    {
      title: 'Thẻ',
      dataIndex: 'cardCount',
      key: 'cardCount',
      width: 56,
      align: 'center',
    },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 96,
      render: (active: boolean) => (
        <Tag color={active ? 'green' : 'default'} style={{ margin: 0 }}>
          {active ? 'On' : 'Off'}
        </Tag>
      ),
    },
    {
      title: '',
      key: 'actions',
      width: canManage ? 72 : 0,
      align: 'right',
      render: (_, record) =>
        canManage ? (
          <Space size={0}>
            <Button
              type="text"
              size="small"
              icon={<EditOutlined />}
              title="Sửa"
              onClick={() => openEditSa(record)}
            />
            <Popconfirm
              title="Xóa tài khoản nguồn này?"
              disabled={record.cardCount > 0}
              onConfirm={() => deleteSourceAccount.mutate(record.id)}
            >
              <Button
                type="text"
                size="small"
                danger
                icon={<DeleteOutlined />}
                title="Xóa"
                disabled={record.cardCount > 0}
              />
            </Popconfirm>
          </Space>
        ) : null,
    },
  ];

  return (
    <>
      <PageHeader
        title="Thẻ nhận tiền"
        extra={
          <Space wrap>
            <Button
              icon={<ReloadOutlined />}
              loading={
                activeTab === 'cards' ? isFetching : sourceAccountsFetching
              }
              onClick={() => {
                if (activeTab === 'cards') {
                  void refetch();
                } else {
                  void refetchSourceAccounts();
                }
              }}
            >
              Làm mới
            </Button>
            {canManage ? (
              activeTab === 'cards' ? (
                <Space wrap>
                  <Button icon={<UploadOutlined />} onClick={openImport}>
                    Import file
                  </Button>
                  <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
                    Thêm thẻ
                  </Button>
                </Space>
              ) : (
                <Button type="primary" icon={<PlusOutlined />} onClick={openCreateSa}>
                  Thêm tài khoản nguồn
                </Button>
              )
            ) : null}
          </Space>
        }
      />

      <Tabs
        activeKey={activeTab}
        onChange={setActiveTab}
        items={[
          {
            key: 'cards',
            label: 'Thẻ nhận tiền',
            children: (
              <Table
                rowKey="id"
                size="small"
                loading={isLoading}
                columns={cardColumns}
                dataSource={accounts}
                tableLayout="fixed"
                pagination={tablePagination(accounts.length, {
                  ...pagination,
                  size: 'small',
                  showSizeChanger: !isMobile,
                  onChange: (current, pageSize) => setPagination({ current, pageSize }),
                })}
              />
            ),
          },
          {
            key: 'source-accounts',
            label: 'Tài khoản nguồn',
            children: (
              <Table
                rowKey="id"
                size="small"
                loading={sourceAccountsLoading}
                columns={saColumns}
                dataSource={sourceAccounts}
                tableLayout="fixed"
                pagination={tablePagination(sourceAccounts.length, {
                  ...saPagination,
                  size: 'small',
                  showSizeChanger: !isMobile,
                  onChange: (current, pageSize) => setSaPagination({ current, pageSize }),
                })}
              />
            ),
          },
        ]}
      />

      <Modal
        title={
          viewing
            ? `Account details · ${viewing.accountHolderName || viewing.accountNumber}`
            : 'Account details'
        }
        open={!!viewingId}
        onCancel={() => setViewingId(null)}
        className="rb-view-modal"
        destroyOnHidden
        centered={!isMobile}
        footer={
          <Space wrap>
            <Button onClick={() => setViewingId(null)}>Đóng</Button>
            <Button
              icon={<CopyOutlined />}
              onClick={() => viewing && void copyAccountInfo(viewing)}
              disabled={!viewing}
            >
              Copy all
            </Button>
            {canManage && viewing ? (
              <Button
                type="primary"
                icon={<EditOutlined />}
                onClick={() => {
                  const row = viewing;
                  setViewingId(null);
                  fillEditForm(row);
                }}
              >
                Sửa
              </Button>
            ) : null}
          </Space>
        }
        width={isMobile ? '100%' : 520}
        styles={{
          body: {
            paddingTop: 8,
            ...(isMobile ? { maxHeight: '70vh', overflowY: 'auto' as const } : {}),
          },
        }}
      >
        {viewingLoading || viewingFetching ? (
          <div style={{ textAlign: 'center', padding: 24 }}>
            <Spin />
          </div>
        ) : viewing ? (
          <Descriptions
            className="rb-view-descriptions"
            bordered
            size="small"
            column={1}
            styles={{
              label: isMobile
                ? { width: 108, padding: '6px 8px', fontSize: 12 }
                : { width: 140 },
              ...(isMobile ? { content: { padding: '6px 8px', fontSize: 12 } } : {}),
            }}
          >
            <Descriptions.Item label="Nguồn thẻ">{viewing.sourceName}</Descriptions.Item>
            <Descriptions.Item label="Tài khoản nguồn">
              {viewing.sourceAccountName || '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Account currency">{viewing.currency}</Descriptions.Item>
            <Descriptions.Item label="Bank name">{viewing.bankName}</Descriptions.Item>
            <Descriptions.Item label="Bank location">
              {viewing.bankLocation || '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Account holder's name">
              {viewing.accountHolderName}
            </Descriptions.Item>
            <Descriptions.Item label="Account type">
              {viewing.accountType || '—'}
            </Descriptions.Item>
            <Descriptions.Item label="SWIFT / BIC">
              {copyableField(viewing.swiftBic)}
            </Descriptions.Item>
            <Descriptions.Item label="Routing number">
              {copyableField(viewing.routingNumber)}
            </Descriptions.Item>
            <Descriptions.Item label="Bank account number">
              {copyableField(viewing.accountNumber)}
            </Descriptions.Item>
            <Descriptions.Item label="Bank address">
              {viewing.bankAddress || '—'}
            </Descriptions.Item>
          </Descriptions>
        ) : null}
      </Modal>

      <Modal
        title={editing ? 'Sửa thẻ nhận tiền' : 'Thêm thẻ nhận tiền'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={() => void handleSubmit()}
        confirmLoading={createAccount.isPending || updateAccount.isPending}
        destroyOnHidden
        centered={!isMobile}
        width={modalWidth}
        styles={modalBodyStyles}
        className="rb-edit-modal"
      >
        <Form
          form={form}
          layout="vertical"
          size="small"
          style={{ marginTop: 4 }}
          onValuesChange={(changed) => {
            if ('sourceId' in changed) {
              form.setFieldValue('sourceAccountId', undefined);
            }
          }}
        >
          <div className="rb-form-grid">
            <Form.Item
              name="sourceId"
              label="Nguồn thẻ"
              rules={[{ required: true, message: 'Chọn nguồn thẻ' }]}
            >
              <Select
                options={sourceOptions}
                placeholder="PingPongX / WorldFirst / Khác"
              />
            </Form.Item>
            {needsSourceAccount ? (
              <Form.Item
                name="sourceAccountId"
                label="Tài khoản nguồn"
                rules={[{ required: true, message: 'Chọn tài khoản nguồn' }]}
              >
                <Select
                  showSearch
                  optionFilterProp="label"
                  loading={sourceAccountOptionsLoading}
                  placeholder="Chọn tài khoản PingPongX / WorldFirst"
                  options={sourceAccountOptions.map((a) => ({
                    value: a.id,
                    label: a.username ? `${a.name} (${a.username})` : a.name,
                  }))}
                />
              </Form.Item>
            ) : (
              <Form.Item
                name="currency"
                label="Account currency"
                rules={[{ required: true, message: 'Chọn loại tiền' }]}
              >
                <Select options={currencyOptions} />
              </Form.Item>
            )}
            {needsSourceAccount ? (
              <>
                <Form.Item
                  name="currency"
                  label="Account currency"
                  rules={[{ required: true, message: 'Chọn loại tiền' }]}
                >
                  <Select options={currencyOptions} />
                </Form.Item>
                <Form.Item name="accountType" label="Account type">
                  <Select allowClear options={accountTypeOptions} />
                </Form.Item>
              </>
            ) : null}
            <Form.Item
              name="bankName"
              label="Bank name"
              rules={[{ required: true, message: 'Nhập tên ngân hàng' }]}
            >
              <Input placeholder="Deutsche Bank Trust Company Americas" />
            </Form.Item>
            <Form.Item name="bankLocation" label="Bank location">
              <Input placeholder="United States" />
            </Form.Item>
            <Form.Item
              name="accountHolderName"
              label="Account holder's name"
              rules={[{ required: true, message: 'Nhập tên chủ tài khoản' }]}
              className={needsSourceAccount ? 'rb-form-grid-full' : undefined}
            >
              <Input placeholder="NGUYEN DAC BAY" />
            </Form.Item>
            {!needsSourceAccount ? (
              <Form.Item name="accountType" label="Account type">
                <Select allowClear options={accountTypeOptions} />
              </Form.Item>
            ) : null}
            <Form.Item name="swiftBic" label="SWIFT / BIC">
              <Input placeholder="BKTRUS33XXX" />
            </Form.Item>
            <Form.Item name="routingNumber" label="Routing number">
              <Input placeholder="021001033" />
            </Form.Item>
            <Form.Item
              name="accountNumber"
              label="Bank account number"
              rules={[{ required: true, message: 'Nhập số tài khoản' }]}
            >
              <Input placeholder="00395823000149549" />
            </Form.Item>
            <Form.Item name="bankAddress" label="Bank address">
              <Input placeholder="One Columbus Circle" />
            </Form.Item>
            <Form.Item
              name="isActive"
              label="Hoạt động"
              valuePropName="checked"
              className="rb-form-grid-full rb-form-item-inline"
            >
              <Switch />
            </Form.Item>
          </div>
        </Form>
      </Modal>

      <Modal
        title={editingSa ? 'Sửa tài khoản nguồn' : 'Thêm tài khoản nguồn'}
        open={saModalOpen}
        onCancel={() => setSaModalOpen(false)}
        onOk={() => void handleSaSubmit()}
        confirmLoading={createSourceAccount.isPending || updateSourceAccount.isPending}
        destroyOnHidden
        centered={!isMobile}
        width={isMobile ? '100%' : 480}
        styles={
          isMobile
            ? { body: { paddingTop: 8, maxHeight: '70vh', overflowY: 'auto' as const } }
            : undefined
        }
        className="rb-edit-modal"
      >
        <Form form={saForm} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item
            name="sourceId"
            label="Nguồn thẻ"
            rules={[{ required: true, message: 'Chọn nguồn' }]}
          >
            <Select
              options={sourcesWithAccounts}
              disabled={!!editingSa}
              placeholder="PingPongX / WorldFirst"
            />
          </Form.Item>
          <Form.Item
            name="name"
            label="Tên tài khoản"
            rules={[{ required: true, message: 'Nhập tên tài khoản' }]}
          >
            <Input placeholder="VD: PingPong — Bay US" />
          </Form.Item>
          <Form.Item name="username" label="Username / Email">
            <Input placeholder="login@example.com" />
          </Form.Item>
          <Form.Item name="externalAccountId" label="External account ID">
            <Input placeholder="ID trên PingPongX / WorldFirst" />
          </Form.Item>
          <Form.Item name="note" label="Ghi chú">
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="isActive" label="Hoạt động" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="Import thẻ từ file PingPongX"
        open={importOpen}
        onCancel={closeImport}
        className="rb-edit-modal rb-import-modal"
        destroyOnHidden
        centered={!isMobile}
        width={isMobile ? '100%' : 960}
        styles={{
          body: {
            paddingTop: 8,
            maxHeight: isMobile ? '70vh' : '75vh',
            overflowY: 'auto',
          },
        }}
        footer={
          <Space wrap>
            <Button onClick={closeImport} disabled={importSubmitting}>
              Hủy
            </Button>
            <Button
              type="primary"
              loading={importSubmitting}
              disabled={importRows.filter((r) => r.action !== 'skip').length === 0}
              onClick={() => void handleImportSubmit()}
            >
              {importProgress
                ? `Thêm (${importProgress.done}/${importProgress.total})`
                : 'Thêm'}
            </Button>
          </Space>
        }
      >
        <Form
          form={importForm}
          layout="vertical"
          size="small"
          style={{ marginTop: 4 }}
          onValuesChange={(changed) => {
            if ('sourceId' in changed) {
              importForm.setFieldValue('sourceAccountId', undefined);
              if (importRows.length > 0) {
                const sourceId = changed.sourceId as string | undefined;
                if (sourceId) {
                  const cached =
                    queryClient.getQueryData<ReceivingBankAccountListItem[]>([
                      'adminReceivingBanks',
                    ]) ?? accounts;
                  setImportRows((prev) =>
                    buildImportPreview(stripPreviewMeta(prev), sourceId, cached),
                  );
                }
              }
            }
          }}
        >
          <div className="rb-form-grid">
            <Form.Item
              name="sourceId"
              label="Nguồn thẻ"
              rules={[{ required: true, message: 'Chọn nguồn thẻ' }]}
            >
              <Select
                options={sourceOptions}
                placeholder="PingPongX / WorldFirst / Khác"
                disabled={importSubmitting}
              />
            </Form.Item>
            {importNeedsSourceAccount ? (
              <Form.Item
                name="sourceAccountId"
                label="Tài khoản nguồn"
                rules={[{ required: true, message: 'Chọn tài khoản nguồn' }]}
              >
                <Select
                  showSearch
                  optionFilterProp="label"
                  loading={importSourceAccountOptionsLoading}
                  placeholder="Chọn tài khoản PingPongX / WorldFirst"
                  disabled={importSubmitting}
                  options={importSourceAccountOptions.map((a) => ({
                    value: a.id,
                    label: a.username ? `${a.name} (${a.username})` : a.name,
                  }))}
                />
              </Form.Item>
            ) : (
              <Form.Item
                name="currency"
                label="Account currency"
                rules={[{ required: true, message: 'Chọn loại tiền' }]}
              >
                <Select options={currencyOptions} disabled={importSubmitting} />
              </Form.Item>
            )}
            {importNeedsSourceAccount ? (
              <>
                <Form.Item
                  name="currency"
                  label="Account currency"
                  rules={[{ required: true, message: 'Chọn loại tiền' }]}
                >
                  <Select options={currencyOptions} disabled={importSubmitting} />
                </Form.Item>
                <Form.Item
                  name="accountType"
                  label="Account type"
                  extra="Ưu tiên theo cột Account type trong file; chọn ở đây chỉ khi file thiếu."
                >
                  <Select allowClear options={accountTypeOptions} disabled={importSubmitting} />
                </Form.Item>
              </>
            ) : (
              <Form.Item
                name="accountType"
                label="Account type"
                className="rb-form-grid-full"
                extra="Ưu tiên theo cột Account type trong file; chọn ở đây chỉ khi file thiếu."
              >
                <Select allowClear options={accountTypeOptions} disabled={importSubmitting} />
              </Form.Item>
            )}
          </div>
        </Form>

        <div className="rb-import-upload">
          <Upload.Dragger
            accept=".xlsx,.xls"
            maxCount={1}
            fileList={importFileList}
            disabled={importSubmitting || importParsing}
            beforeUpload={(file) => {
              setImportFile(file);
              setImportFileList([
                {
                  uid: file.uid,
                  name: file.name,
                  status: 'done',
                },
              ]);
              setImportRows([]);
              return false;
            }}
            onRemove={() => {
              setImportFile(null);
              setImportFileList([]);
              setImportRows([]);
              return true;
            }}
          >
            <p className="ant-upload-drag-icon">
              <InboxOutlined />
            </p>
            <p className="ant-upload-text">Chọn hoặc kéo file Excel PingPongX Global Account</p>
            <p className="ant-upload-hint">Định dạng .xlsx / .xls — export Global Account</p>
          </Upload.Dragger>
          <div className="rb-import-upload-actions">
            <Button
              type="default"
              loading={importParsing}
              disabled={!importFile || importSubmitting}
              onClick={() => void handleImportProcess()}
            >
              Xử lý
            </Button>
          </div>
        </div>

        {importRows.length > 0 ? (
          <Table<ImportPreviewRow>
            className="rb-import-preview"
            size="small"
            rowKey={(r) => `${r.rowIndex}-${r.accountNumber}`}
            dataSource={importRows}
            pagination={{
              pageSize: 20,
              size: 'small',
              showSizeChanger: false,
              showTotal: (total) => `${total} dòng`,
            }}
            scroll={{ x: 900 }}
            rowClassName={(r) => (r.error ? 'rb-import-row-error' : '')}
            columns={[
              {
                title: '#',
                dataIndex: 'rowIndex',
                width: 48,
                align: 'center',
              },
              {
                title: 'Account number',
                dataIndex: 'accountNumber',
                width: 160,
                ellipsis: true,
                render: (v: string) => v || '—',
              },
              {
                title: 'Holder',
                dataIndex: 'accountHolderName',
                width: 140,
                ellipsis: true,
                render: (v: string) => v || '—',
              },
              {
                title: 'Type',
                dataIndex: 'accountType',
                width: 90,
                ellipsis: true,
                render: (v?: string) => v || '—',
              },
              {
                title: 'Bank name',
                dataIndex: 'bankName',
                width: 180,
                ellipsis: true,
                render: (v: string) => v || '—',
              },
              {
                title: 'Location',
                dataIndex: 'bankLocation',
                width: 110,
                ellipsis: true,
                render: (v?: string) => v || '—',
              },
              {
                title: 'SWIFT',
                dataIndex: 'swiftBic',
                width: 110,
                ellipsis: true,
                render: (v?: string) => v || '—',
              },
              {
                title: 'Routing',
                dataIndex: 'routingNumber',
                width: 100,
                ellipsis: true,
                render: (v?: string) => v || '—',
              },
              {
                title: 'Address',
                dataIndex: 'bankAddress',
                ellipsis: true,
                render: (v?: string) => v || '—',
              },
              {
                title: 'Status',
                dataIndex: 'isActive',
                width: 72,
                render: (active: boolean, row) =>
                  row.error ? (
                    <Tooltip title={row.error}>
                      <Tag color="error" style={{ margin: 0 }}>
                        Lỗi
                      </Tag>
                    </Tooltip>
                  ) : (
                    <Tag color={active ? 'green' : 'default'} style={{ margin: 0 }}>
                      {active ? 'On' : 'Off'}
                    </Tag>
                  ),
              },
              {
                title: 'Thao tác',
                dataIndex: 'action',
                width: 120,
                fixed: 'right',
                render: (action: ImportPreviewRow['action'], row: ImportPreviewRow) => {
                  if (action === 'skip') return <Tag style={{ margin: 0 }}>Bỏ qua</Tag>;
                  if (action === 'update') {
                    const n = row.existingIds?.length ?? 0;
                    return (
                      <Tag color="blue" style={{ margin: 0 }}>
                        {n > 1 ? `Cập nhật ×${n}` : 'Cập nhật'}
                      </Tag>
                    );
                  }
                  return (
                    <Tag color="green" style={{ margin: 0 }}>
                      Mới
                    </Tag>
                  );
                },
              },
            ]}
          />
        ) : null}
      </Modal>
    </>
  );
};
