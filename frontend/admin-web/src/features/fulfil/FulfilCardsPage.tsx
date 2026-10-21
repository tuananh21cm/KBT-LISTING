import { useMemo, useState } from 'react';
import {
  Button,
  Descriptions,
  Empty,
  Form,
  Input,
  InputNumber,
  Pagination,
  Popconfirm,
  Select,
  Space,
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
import type { UploadFile } from 'antd/es/upload/interface';
import type { CSSProperties, MouseEvent } from 'react';
import { useQueryClient } from '@tanstack/react-query';
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
  useCreateFulfilCard,
  useDeleteFulfilCard,
  useFulfilAccountOptions,
  useFulfilCards,
  useUpdateFulfilCard,
} from '../../hooks/useAdminFulfil';
import {
  useCreateReceivingSourceAccount,
  useDeleteReceivingSourceAccount,
  useReceivingCardSources,
  useReceivingSourceAccountOptions,
  useReceivingSourceAccounts,
  useUpdateReceivingSourceAccount,
} from '../../hooks/useAdminReceivingBanks';
import { useAdminUsers } from '../../hooks/useAdminUsers';
import { adminFulfilApi } from '../../services/adminFulfilApi';
import { useAuthStore } from '../../stores/authStore';
import { needsPagination } from '../../utils/pagination';
import { FULFIL_CARD_BRANDS, type FulfilCard } from '../../types/fulfil';
import {
  RECEIVING_ACCOUNT_CURRENCIES,
  type ReceivingSourceAccount,
} from '../../types/receivingBank';
import {
  normalizeCardNumber,
  parsePingPongCardFile,
  type ImportFulfilCardRow,
} from './pingpongCardImport';
import '@drop/fulfil/fulfil.css';

const { Text } = Typography;

const PAGE_SIZE_OPTIONS = [10, 20, 50];
const CARD_STATUS_OPTIONS = [
  { value: 'active', label: 'Hoạt động' },
  { value: 'inactive', label: 'Vô hiệu' },
] as const;
/** Bộ lọc / gợi ý "Cấp cho" chỉ lấy user đi đơn hoặc admin. */
const ASSIGNEE_FILTER_ROLES = new Set(['Admin', 'Fulfil']);
const CARD_COL =
  'minmax(180px, 1.8fr) minmax(72px, 0.55fr) minmax(72px, 0.5fr) minmax(120px, 1fr) minmax(110px, 0.9fr) minmax(48px, 0.4fr)';
const CARD_HEADERS = ['Nguồn', 'Loại', 'Hết hạn', 'Tài khoản', 'Cấp cho', 'Đơn'];
const SA_COL = 'minmax(180px, 1.5fr) minmax(140px, 1.2fr) minmax(56px, 0.45fr) minmax(88px, 0.6fr)';
const SA_HEADERS = ['Tài khoản', 'External ID', 'Thẻ', 'Trạng thái'];
const MONO: CSSProperties = {
  fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
  fontSize: 12,
};

type CardStatusFilter = (typeof CARD_STATUS_OPTIONS)[number]['value'];

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
  cardNumber: string;
  cardBrand: string;
  currency: string;
  expiryMonth?: number | null;
  expiryYear?: number | null;
  holderName?: string;
  cvv?: string;
  sourceId: string;
  sourceAccountId?: string;
  accountIds: string[];
  assigneeUserIds: string[];
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
}

type ImportPreviewRow = ImportFulfilCardRow & {
  action: 'create' | 'update' | 'skip';
  /** Mọi thẻ trên DB cùng số thẻ (DB chưa có unique index). */
  existingIds?: string[];
};

const formatExpiry = (month?: number | null, year?: number | null) => {
  if (!month || !year) return '—';
  return `${String(month).padStart(2, '0')}/${String(year).slice(-2)}`;
};

const copyableField = (value?: string | null) => {
  if (!value) return '—';
  return (
    <Text copyable={{ text: value }} style={{ wordBreak: 'break-all' }}>
      {value}
    </Text>
  );
};

const buildCardCopyText = (row: FulfilCard) => {
  const lines = [
    `Nguồn thẻ: ${row.sourceName}`,
    row.sourceAccountName ? `Tài khoản nguồn: ${row.sourceAccountName}` : null,
    `Loại: ${row.cardBrand}`,
    `Currency: ${row.currency}`,
    `Số thẻ: ${row.cardNumber}`,
    row.expiryMonth && row.expiryYear
      ? `Hết hạn: ${formatExpiry(row.expiryMonth, row.expiryYear)}`
      : null,
    row.holderName ? `Tên trên thẻ: ${row.holderName}` : null,
    row.cvv ? `CVV: ${row.cvv}` : null,
    row.accountNames?.length ? `Tài khoản: ${row.accountNames.join(', ')}` : null,
    row.assigneeUserNames?.length ? `Cấp cho: ${row.assigneeUserNames.join(', ')}` : null,
  ];
  return lines.filter(Boolean).join('\n');
};

export const FulfilCardsPage = () => {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canManage = hasPermission('admin.fulfil.manage');
  const canManageSourceAccounts =
    canManage || hasPermission('admin.shops.manage');
  const isMobile = useIsMobile();
  const queryClient = useQueryClient();

  const { data: cards = [], isLoading, isFetching, refetch } = useFulfilCards();
  const {
    data: sourceAccounts = [],
    isLoading: sourceAccountsLoading,
    isFetching: sourceAccountsFetching,
    refetch: refetchSourceAccounts,
  } = useReceivingSourceAccounts();
  const { data: accountOptions = [] } = useFulfilAccountOptions(true);
  const { data: sources = [] } = useReceivingCardSources();
  const { data: users = [] } = useAdminUsers(canManage);
  const createCard = useCreateFulfilCard();
  const updateCard = useUpdateFulfilCard();
  const deleteCard = useDeleteFulfilCard();
  const createSourceAccount = useCreateReceivingSourceAccount();
  const updateSourceAccount = useUpdateReceivingSourceAccount();
  const deleteSourceAccount = useDeleteReceivingSourceAccount();

  const [activeTab, setActiveTab] = useState('cards');
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<FulfilCard | null>(null);
  const [viewing, setViewing] = useState<FulfilCard | null>(null);
  const [form] = Form.useForm<CardFormValues>();
  const selectedSourceId = Form.useWatch('sourceId', form);
  const [pagination, setPagination] = useState({ current: 1, pageSize: 50 });
  const [saPagination, setSaPagination] = useState({ current: 1, pageSize: 50 });
  const [cardSearch, setCardSearch] = useState('');
  const [cardSearchApplied, setCardSearchApplied] = useState('');
  const [cardSourceId, setCardSourceId] = useState<string | undefined>();
  const [cardStatus, setCardStatus] = useState<CardStatusFilter | undefined>();
  const [cardAccountId, setCardAccountId] = useState<string | undefined>();
  const [cardAssigneeUserId, setCardAssigneeUserId] = useState<string | undefined>();

  const [saModalOpen, setSaModalOpen] = useState(false);
  const [editingSa, setEditingSa] = useState<ReceivingSourceAccount | null>(null);
  const [saForm] = Form.useForm<SourceAccountFormValues>();

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

  const selectedSource = useMemo(
    () => sources.find((s) => s.id === selectedSourceId),
    [sources, selectedSourceId],
  );
  const needsSourceAccount = !!selectedSource?.supportsAccounts;

  const { data: sourceAccountOptions = [], isLoading: sourceAccountOptionsLoading } =
    useReceivingSourceAccountOptions(selectedSourceId, needsSourceAccount);

  const importSelectedSource = useMemo(
    () => sources.find((s) => s.id === importSourceId),
    [sources, importSourceId],
  );
  const importNeedsSourceAccount = !!importSelectedSource?.supportsAccounts;

  const { data: importSourceAccountOptions = [], isLoading: importSourceAccountOptionsLoading } =
    useReceivingSourceAccountOptions(importSourceId, importOpen && importNeedsSourceAccount);

  const userOptions = useMemo(
    () => users.map((u) => ({ value: u.id, label: u.fullName?.trim() || u.userName })),
    [users],
  );
  const assigneeFilterOptions = useMemo(
    () =>
      users
        .filter((u) => u.roles.some((r) => ASSIGNEE_FILTER_ROLES.has(r)))
        .map((u) => ({ value: u.id, label: u.fullName?.trim() || u.userName })),
    [users],
  );
  const accountSelectOptions = useMemo(
    () => accountOptions.map((a) => ({ value: a.id, label: a.name })),
    [accountOptions],
  );
  const sourceOptions = useMemo(
    () => sources.map((s) => ({ value: s.id, label: s.name })),
    [sources],
  );
  const resetCardPage = () => setPagination((p) => ({ ...p, current: 1 }));

  const filteredCards = useMemo(() => {
    const q = cardSearchApplied.trim().toLowerCase();
    const digits = q.replace(/\D/g, '');
    return cards.filter((c) => {
      if (cardSourceId && c.sourceId !== cardSourceId) return false;
      if (cardStatus === 'active' && !c.isActive) return false;
      if (cardStatus === 'inactive' && c.isActive) return false;
      if (cardAccountId && !c.accountIds?.includes(cardAccountId)) return false;
      if (cardAssigneeUserId && !c.assigneeUserIds?.includes(cardAssigneeUserId)) return false;
      if (!q) return true;
      const haystack = [
        c.cardNumberMasked,
        c.cardNumber,
        c.name,
        c.holderName,
        c.sourceName,
        c.sourceAccountName,
        c.cardBrand,
        c.currency,
        ...(c.accountNames ?? []),
        ...(c.assigneeUserNames ?? []),
      ]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
      if (haystack.includes(q)) return true;
      if (digits.length >= 4) {
        const cardDigits = normalizeCardNumber(c.cardNumber);
        if (cardDigits.includes(digits)) return true;
      }
      return false;
    });
  }, [
    cards,
    cardSearchApplied,
    cardSourceId,
    cardStatus,
    cardAccountId,
    cardAssigneeUserId,
  ]);

  const cardPage = useMemo(() => {
    const start = (pagination.current - 1) * pagination.pageSize;
    return filteredCards.slice(start, start + pagination.pageSize);
  }, [filteredCards, pagination]);
  const saPage = useMemo(() => {
    const start = (saPagination.current - 1) * saPagination.pageSize;
    return sourceAccounts.slice(start, start + saPagination.pageSize);
  }, [sourceAccounts, saPagination]);
  const currencyOptions = useMemo(
    () => RECEIVING_ACCOUNT_CURRENCIES.map((c) => ({ label: c, value: c })),
    [],
  );
  const sourcesWithAccounts = useMemo(
    () => sources.filter((s) => s.supportsAccounts).map((s) => ({ value: s.id, label: s.name })),
    [sources],
  );
  const hasCardFilters =
    !!cardSearchApplied.trim() ||
    !!cardSourceId ||
    !!cardStatus ||
    !!cardAccountId ||
    !!cardAssigneeUserId;

  const modalWidth = isMobile ? '100%' : 520;
  const modalBodyStyles = {
    body: {
      paddingTop: 8,
      ...(isMobile ? { maxHeight: '70vh', overflowY: 'auto' as const } : {}),
    },
  };

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({
      cardBrand: 'Visa',
      currency: 'USD',
      isActive: true,
      accountIds: [],
      assigneeUserIds: [],
      sourceAccountId: undefined,
    });
    setModalOpen(true);
  };

  const openView = (row: FulfilCard) => {
    setViewing(row);
  };

  const copyCardInfo = async (row: FulfilCard) => {
    await navigator.clipboard.writeText(buildCardCopyText(row));
    message.success('Đã copy thông tin thẻ');
  };

  const openEdit = (row: FulfilCard) => {
    setEditing(row);
    form.setFieldsValue({
      cardNumber: row.cardNumber,
      cardBrand: row.cardBrand,
      currency: row.currency || 'USD',
      expiryMonth: row.expiryMonth,
      expiryYear: row.expiryYear,
      holderName: row.holderName ?? undefined,
      cvv: row.cvv ?? undefined,
      sourceId: row.sourceId,
      sourceAccountId: row.sourceAccountId ?? undefined,
      accountIds: row.accountIds ?? [],
      assigneeUserIds: row.assigneeUserIds ?? [],
      isActive: row.isActive,
    });
    setModalOpen(true);
  };

  const buildDisplayName = (sourceId: string, sourceAccountId?: string | null) => {
    const source = sources.find((s) => s.id === sourceId);
    if (!source) return 'Thẻ thanh toán';
    if (sourceAccountId) {
      const account = sourceAccountOptions.find((a) => a.id === sourceAccountId);
      if (account?.name) return account.name;
    }
    return source.name;
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    const sourceAccountId = needsSourceAccount ? values.sourceAccountId || null : null;
    const payload = {
      name: buildDisplayName(values.sourceId, sourceAccountId),
      cardNumber: values.cardNumber,
      cardBrand: values.cardBrand,
      currency: values.currency,
      expiryMonth: values.expiryMonth ?? null,
      expiryYear: values.expiryYear ?? null,
      holderName: values.holderName || null,
      cvv: values.cvv || null,
      sourceId: values.sourceId,
      sourceAccountId,
      accountIds: values.accountIds ?? [],
      assigneeUserIds: values.assigneeUserIds ?? [],
      isActive: values.isActive,
    };
    if (editing) {
      await updateCard.mutateAsync({ id: editing.id, data: payload });
    } else {
      await createCard.mutateAsync(payload);
    }
    setModalOpen(false);
  };

  const resetImportState = () => {
    setImportFile(null);
    setImportFileList([]);
    setImportRows([]);
    setImportParsing(false);
    setImportSubmitting(false);
    setImportProgress(null);
  };

  const openImport = () => {
    resetImportState();
    importForm.resetFields();
    const defaultSource = sources.find((s) => s.code === 'PINGPONGX') ?? sources[0];
    importForm.setFieldsValue({ currency: 'USD', sourceId: defaultSource?.id });
    setImportOpen(true);
  };

  const closeImport = () => {
    if (importSubmitting) return;
    setImportOpen(false);
    resetImportState();
  };

  const fetchCardsSnapshot = async (): Promise<FulfilCard[]> => {
    const response = await adminFulfilApi.getCards();
    if (!response.success) {
      throw new Error(response.message || 'Không tải được danh sách thẻ.');
    }
    return response.data ?? [];
  };

  /** Khớp `Account Name` trong file với tài khoản nguồn; không khớp thì dùng lựa chọn trên form. */
  const resolveImportSourceAccountId = (accountName?: string, fallbackId?: string) => {
    if (accountName) {
      const key = accountName.trim().toLowerCase();
      const matched = importSourceAccountOptions.find(
        (a) =>
          a.name.trim().toLowerCase() === key || a.username?.trim().toLowerCase() === key,
      );
      if (matched) return matched.id;
    }
    return fallbackId;
  };

  const buildImportDisplayName = (sourceId: string, sourceAccountId?: string | null) => {
    const source = sources.find((s) => s.id === sourceId);
    if (!source) return 'Thẻ thanh toán';
    if (sourceAccountId) {
      const account = importSourceAccountOptions.find((a) => a.id === sourceAccountId);
      if (account?.name) return account.name;
    }
    return source.name;
  };

  /** Preview: khớp DB theo số thẻ; trùng trong file → dòng sau cập nhật bản do dòng đầu tạo. */
  const buildImportPreview = (
    parsed: ImportFulfilCardRow[],
    existingCards: FulfilCard[],
  ): ImportPreviewRow[] => {
    const existingByNumber = new Map<string, string[]>();
    for (const card of existingCards) {
      const key = normalizeCardNumber(card.cardNumber ?? '');
      if (!key) continue;
      const list = existingByNumber.get(key) ?? [];
      list.push(card.id);
      existingByNumber.set(key, list);
    }
    const seenInFile = new Set<string>();

    return parsed.map((row) => {
      if (row.error) {
        return { ...row, action: 'skip' as const };
      }
      const existingIds = existingByNumber.get(row.cardNumber);
      if (existingIds && existingIds.length > 0) {
        seenInFile.add(row.cardNumber);
        return { ...row, action: 'update' as const, existingIds };
      }
      if (seenInFile.has(row.cardNumber)) {
        return { ...row, action: 'update' as const };
      }
      seenInFile.add(row.cardNumber);
      return { ...row, action: 'create' as const };
    });
  };

  const handleImportProcess = async () => {
    try {
      await importForm.validateFields(['sourceId', 'currency']);
    } catch {
      return;
    }
    if (!importFile) {
      message.warning('Chọn file Excel thẻ PingPong trước.');
      return;
    }

    setImportParsing(true);
    try {
      const [buffer, existingCards] = await Promise.all([
        importFile.arrayBuffer(),
        fetchCardsSnapshot(),
      ]);
      const preview = buildImportPreview(parsePingPongCardFile(buffer), existingCards);
      setImportRows(preview);

      const toCreate = preview.filter((r) => r.action === 'create').length;
      const toUpdate = preview.filter((r) => r.action === 'update').length;
      const skipped = preview.filter((r) => r.action === 'skip').length;
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

    const needsSa = !!sources.find((s) => s.id === values.sourceId)?.supportsAccounts;
    if (needsSa) {
      const unresolved = validRows.filter(
        (r) => !resolveImportSourceAccountId(r.accountName, values.sourceAccountId),
      );
      if (unresolved.length > 0) {
        message.warning(
          `${unresolved.length} dòng không khớp tài khoản nguồn theo Account Name — chọn tài khoản nguồn dùng chung trước khi thêm.`,
        );
        return;
      }
    }

    setImportSubmitting(true);
    setImportProgress({ done: 0, total: validRows.length });

    let created = 0;
    let updated = 0;
    let updatedCards = 0;
    let failed = 0;
    let firstError: string | null = null;

    try {
      // Snapshot mới — tránh tạo trùng khi preview đã cũ.
      // Gom mọi thẻ cùng số thẻ (DB chưa có unique index nên có thể đã trùng).
      const existingCards = await fetchCardsSnapshot();
      const known = new Map<string, FulfilCard[]>();
      for (const card of existingCards) {
        const key = normalizeCardNumber(card.cardNumber ?? '');
        if (!key) continue;
        const list = known.get(key) ?? [];
        list.push(card);
        known.set(key, list);
      }

      for (let i = 0; i < validRows.length; i++) {
        const row = validRows[i]!;
        const sourceAccountId = needsSa
          ? resolveImportSourceAccountId(row.accountName, values.sourceAccountId) ?? null
          : null;

        try {
          const matches = known.get(row.cardNumber) ?? [];
          if (matches.length > 0) {
            for (const card of matches) {
              const response = await adminFulfilApi.updateCard(card.id, {
                // Server đặt lại name theo nguồn / tài khoản nguồn.
                name: card.name,
                cardNumber: row.cardNumber,
                cardBrand: row.cardBrand,
                currency: row.currency || values.currency || 'USD',
                expiryMonth: row.expiryMonth ?? card.expiryMonth ?? null,
                expiryYear: row.expiryYear ?? card.expiryYear ?? null,
                holderName: row.holderName ?? card.holderName ?? null,
                cvv: row.cvv ?? card.cvv ?? null,
                sourceId: values.sourceId,
                sourceAccountId,
                // Giữ liên kết tài khoản fulfil / người được cấp — file không có thông tin này.
                accountIds: card.accountIds ?? [],
                assigneeUserIds: card.assigneeUserIds ?? [],
                isActive: row.isActive,
              });
              if (!response.success) {
                throw new Error(response.message || 'Cập nhật thất bại');
              }
            }
            updated += 1;
            updatedCards += matches.length;
          } else {
            const response = await adminFulfilApi.createCard({
              name: buildImportDisplayName(values.sourceId, sourceAccountId),
              cardNumber: row.cardNumber,
              cardBrand: row.cardBrand,
              currency: row.currency || values.currency || 'USD',
              expiryMonth: row.expiryMonth ?? null,
              expiryYear: row.expiryYear ?? null,
              holderName: row.holderName ?? null,
              cvv: row.cvv ?? null,
              sourceId: values.sourceId,
              sourceAccountId,
              accountIds: [],
              assigneeUserIds: [],
              isActive: row.isActive,
            });
            if (!response.success || !response.data) {
              throw new Error(response.message || 'Tạo thất bại');
            }
            known.set(row.cardNumber, [response.data]);
            created += 1;
          }
        } catch (error) {
          failed += 1;
          if (!firstError) {
            firstError = error instanceof Error ? error.message : 'Lỗi không xác định';
          }
        }

        setImportProgress({ done: i + 1, total: validRows.length });
      }

      await queryClient.invalidateQueries({ queryKey: ['fulfilCards'] });
      await queryClient.invalidateQueries({ queryKey: ['fulfilCardOptions'] });
      await queryClient.invalidateQueries({ queryKey: ['fulfilAccounts'] });

      setImportOpen(false);
      resetImportState();

      if (failed === 0) {
        message.success(
          updatedCards > updated
            ? `Import xong: ${created} mới, ${updated} cập nhật (${updatedCards} thẻ, có bản trùng số thẻ).`
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
      message.error(error instanceof Error ? error.message : 'Import thất bại — thử lại.');
    } finally {
      setImportSubmitting(false);
      setImportProgress(null);
    }
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

  const renderActions = (record: FulfilCard) => (
    <Space size={0} className="fulfil-txn-actions" onClick={(e) => e.stopPropagation()}>
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
          <Popconfirm title="Xóa thẻ này?" onConfirm={() => deleteCard.mutate(record.id)}>
            <Button type="text" size="small" danger icon={<DeleteOutlined />} title="Xóa" />
          </Popconfirm>
        </>
      ) : null}
    </Space>
  );

  const renderCardItem = (row: FulfilCard, index: number) => {
    const sourceLabel = row.sourceAccountName
      ? `${row.sourceName} · ${row.sourceAccountName}`
      : row.sourceName || row.name || '';
    const accounts = row.accountNames?.length ? row.accountNames.join(', ') : '';
    const assignees = row.assigneeUserNames?.length ? row.assigneeUserNames.join(', ') : '';
    const expiry = formatExpiry(row.expiryMonth, row.expiryYear);
    const statusLabel = row.isActive ? 'Hoạt động' : 'Vô hiệu';

    const head = (
      <ListCardHead mobile={isMobile}>
        <div className="app-list-card-head-meta">
          {!isMobile ? (
            <span style={{ color: 'var(--app-text-tertiary)', fontVariantNumeric: 'tabular-nums', width: 20 }}>
              {index}
            </span>
          ) : null}
          <Text strong style={MONO}>
            {row.cardNumberMasked}
          </Text>
          <StatusDot color={row.isActive ? '#52c41a' : '#8c8c8c'} label={statusLabel} />
          <span
            style={{
              color: 'var(--app-text-tertiary)',
              textTransform: 'uppercase',
              letterSpacing: '0.02em',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
          >
            {row.cardBrand}
            {row.currency ? ` · ${row.currency}` : ''}
          </span>
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
            <ListLine isTitle>{sourceLabel || '—'}</ListLine>
            <ListLine>{[accounts, assignees].filter(Boolean).join(' · ') || '—'}</ListLine>
          </div>
        </ListCard>
      );
    }

    return (
      <ListCard key={row.id}>
        {head}
        <ListCardBody columns={CARD_COL} style={{ minWidth: 900 }}>
          <ListCol>
            <ListLine primary title={sourceLabel || undefined}>
              {sourceLabel || '—'}
            </ListLine>
            <ListLine>{row.sourceAccountName || '—'}</ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary>{row.cardBrand}</ListLine>
            <ListLine>{row.currency || 'USD'}</ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary>{expiry}</ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary title={accounts || undefined}>
              {accounts || '—'}
            </ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary title={assignees || undefined}>
              {assignees || '—'}
            </ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary>{row.orderCount ?? 0}</ListLine>
          </ListCol>
        </ListCardBody>
      </ListCard>
    );
  };

  const renderSaActions = (record: ReceivingSourceAccount) =>
    canManageSourceAccounts ? (
      <Space size={0} className="fulfil-txn-actions" onClick={(e) => e.stopPropagation()}>
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
    ) : null;

  const renderSaItem = (row: ReceivingSourceAccount, index: number) => {
    const sub = [row.username?.trim() || null, row.externalAccountId?.trim() || null]
      .filter(Boolean)
      .join(' · ');
    const statusLabel = row.isActive ? 'Hoạt động' : 'Vô hiệu';
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
          {row.sourceName ? (
            <span
              style={{
                color: 'var(--app-text-tertiary)',
                textTransform: 'uppercase',
                letterSpacing: '0.02em',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
              }}
            >
              {row.sourceName}
            </span>
          ) : null}
        </div>
        <div className="app-list-card-head-actions">{renderSaActions(row)}</div>
      </ListCardHead>
    );

    if (isMobile) {
      return (
        <ListCard key={row.id} mobile>
          {head}
          <div style={{ padding: '6px 10px' }}>
            <ListLine>{sub || '—'}</ListLine>
            <ListLine>{row.cardCount} thẻ</ListLine>
          </div>
        </ListCard>
      );
    }

    return (
      <ListCard key={row.id}>
        {head}
        <ListCardBody columns={SA_COL} style={{ minWidth: 720 }}>
          <ListCol>
            <ListLine primary title={row.name}>
              {row.name}
            </ListLine>
            <ListLine>{row.username || '—'}</ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary>{copyableEllipsis(row.externalAccountId)}</ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary>{row.cardCount}</ListLine>
          </ListCol>
          <ListCol>
            <ListLine primary>{statusLabel}</ListLine>
          </ListCol>
        </ListCardBody>
      </ListCard>
    );
  };

  return (
    <div className={isMobile ? 'fulfil-cards-page is-mobile' : 'fulfil-cards-page'}>
      <PageHeader
        title="Thẻ thanh toán"
        extra={
          <Space size={4}>
            <Button
              size="small"
              icon={<ReloadOutlined />}
              onClick={() => {
                void refetch();
                void refetchSourceAccounts();
              }}
              loading={isFetching || sourceAccountsFetching}
            >
              {isMobile ? null : 'Làm mới'}
            </Button>
            {activeTab === 'cards' ? (
              canManage ? (
                <>
                  <Button size="small" icon={<UploadOutlined />} onClick={openImport}>
                    {isMobile ? null : 'Import file'}
                  </Button>
                  <Button type="primary" size="small" icon={<PlusOutlined />} onClick={openCreate}>
                    {isMobile ? null : 'Thêm thẻ'}
                  </Button>
                </>
              ) : null
            ) : canManageSourceAccounts ? (
              <Button type="primary" size="small" icon={<PlusOutlined />} onClick={openCreateSa}>
                {isMobile ? null : 'Thêm tài khoản nguồn'}
              </Button>
            ) : null}
          </Space>
        }
      />

      <Tabs
        size="small"
        activeKey={activeTab}
        onChange={setActiveTab}
        items={[
          {
            key: 'cards',
            label: 'Thẻ thanh toán',
            children: (
              <>
                <div className="admin-filters">
                  <Input.Search
                    className="admin-filters-search"
                    allowClear
                    placeholder="Tìm số thẻ / nguồn / tài khoản / người được cấp…"
                    style={{ width: isMobile ? '100%' : 300 }}
                    value={cardSearch}
                    onChange={(e) => setCardSearch(e.target.value)}
                    onSearch={(v) => {
                      setCardSearchApplied(v.trim());
                      resetCardPage();
                    }}
                  />
                  <div className="admin-filters-grid">
                    <Select
                      size="small"
                      allowClear
                      showSearch
                      optionFilterProp="label"
                      placeholder="Nguồn"
                      style={{ width: isMobile ? '100%' : 150 }}
                      options={sourceOptions}
                      value={cardSourceId}
                      onChange={(v) => {
                        setCardSourceId(v);
                        resetCardPage();
                      }}
                    />
                    <Select
                      size="small"
                      allowClear
                      placeholder="Trạng thái"
                      style={{ width: isMobile ? '100%' : 120 }}
                      options={[...CARD_STATUS_OPTIONS]}
                      value={cardStatus}
                      onChange={(v) => {
                        setCardStatus(v);
                        resetCardPage();
                      }}
                    />
                    <Select
                      size="small"
                      allowClear
                      showSearch
                      optionFilterProp="label"
                      placeholder="Tài khoản mua hàng"
                      style={{ width: isMobile ? '100%' : 180 }}
                      options={accountSelectOptions}
                      value={cardAccountId}
                      onChange={(v) => {
                        setCardAccountId(v);
                        resetCardPage();
                      }}
                    />
                    {canManage ? (
                      <Select
                        size="small"
                        allowClear
                        showSearch
                        optionFilterProp="label"
                        placeholder="Cấp cho"
                        style={{ width: isMobile ? '100%' : 160 }}
                        options={assigneeFilterOptions}
                        value={cardAssigneeUserId}
                        onChange={(v) => {
                          setCardAssigneeUserId(v);
                          resetCardPage();
                        }}
                      />
                    ) : null}
                  </div>
                </div>
                <PageContentSpin spinning={isLoading} tip="Đang tải thẻ…">
                  <div className="admin-list-scroll" style={{ overflowX: isMobile ? 'visible' : 'auto' }}>
                    {!isMobile ? (
                      <ListHeader columns={CARD_COL} style={{ minWidth: 900 }}>
                        {CARD_HEADERS.map((label) => (
                          <div key={label}>{label}</div>
                        ))}
                      </ListHeader>
                    ) : null}
                    <div className="admin-list-body" style={{ minWidth: isMobile ? 0 : 900, minHeight: 160 }}>
                      {cardPage.length === 0 && !isLoading ? (
                        <Empty
                          description={
                            hasCardFilters ? 'Không có thẻ khớp bộ lọc' : 'Chưa có thẻ thanh toán'
                          }
                          style={{ margin: '48px 0' }}
                        />
                      ) : (
                        cardPage.map((row, i) =>
                          renderCardItem(row, (pagination.current - 1) * pagination.pageSize + i + 1),
                        )
                      )}
                    </div>
                  </div>
                </PageContentSpin>
                {needsPagination(filteredCards.length, pagination.pageSize) ? (
                  <ListPagination mobile={isMobile}>
                    <Pagination
                      size="small"
                      current={pagination.current}
                      pageSize={pagination.pageSize}
                      total={filteredCards.length}
                      showSizeChanger={!isMobile}
                      simple={isMobile}
                      pageSizeOptions={PAGE_SIZE_OPTIONS.map(String)}
                      showTotal={isMobile ? undefined : (t) => `${t} thẻ`}
                      onChange={(current, pageSize) => setPagination({ current, pageSize })}
                    />
                  </ListPagination>
                ) : null}
              </>
            ),
          },
          {
            key: 'source-accounts',
            label: 'Tài khoản nguồn',
            children: (
              <>
                <PageContentSpin spinning={sourceAccountsLoading} tip="Đang tải tài khoản nguồn…">
                  <div className="admin-list-scroll" style={{ overflowX: isMobile ? 'visible' : 'auto' }}>
                    {!isMobile ? (
                      <ListHeader columns={SA_COL} style={{ minWidth: 720 }}>
                        {SA_HEADERS.map((label) => (
                          <div key={label}>{label}</div>
                        ))}
                      </ListHeader>
                    ) : null}
                    <div className="admin-list-body" style={{ minWidth: isMobile ? 0 : 720, minHeight: 160 }}>
                      {saPage.length === 0 && !sourceAccountsLoading ? (
                        <Empty description="Chưa có tài khoản nguồn" style={{ margin: '48px 0' }} />
                      ) : (
                        saPage.map((row, i) =>
                          renderSaItem(row, (saPagination.current - 1) * saPagination.pageSize + i + 1),
                        )
                      )}
                    </div>
                  </div>
                </PageContentSpin>
                {needsPagination(sourceAccounts.length, saPagination.pageSize) ? (
                  <ListPagination mobile={isMobile}>
                    <Pagination
                      size="small"
                      current={saPagination.current}
                      pageSize={saPagination.pageSize}
                      total={sourceAccounts.length}
                      showSizeChanger={!isMobile}
                      simple={isMobile}
                      pageSizeOptions={PAGE_SIZE_OPTIONS.map(String)}
                      showTotal={isMobile ? undefined : (t) => `${t} tài khoản`}
                      onChange={(current, pageSize) => setSaPagination({ current, pageSize })}
                    />
                  </ListPagination>
                ) : null}
              </>
            ),
          },
        ]}
      />

      <Modal
        title={viewing ? `Thẻ · ${viewing.sourceAccountName || viewing.sourceName || viewing.name}` : 'Xem thẻ'}
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
              onClick={() => viewing && void copyCardInfo(viewing)}
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
            <Descriptions.Item label="Nguồn thẻ">{viewing.sourceName || '—'}</Descriptions.Item>
            <Descriptions.Item label="Tài khoản nguồn">
              {viewing.sourceAccountName || '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Currency">{viewing.currency || 'USD'}</Descriptions.Item>
            <Descriptions.Item label="Loại">
              <Tag style={{ marginInlineEnd: 0 }}>{viewing.cardBrand}</Tag>
            </Descriptions.Item>
            <Descriptions.Item label="Số thẻ">{copyableField(viewing.cardNumber)}</Descriptions.Item>
            <Descriptions.Item label="Hết hạn">
              {formatExpiry(viewing.expiryMonth, viewing.expiryYear)}
            </Descriptions.Item>
            <Descriptions.Item label="Tên trên thẻ">
              {copyableField(viewing.holderName)}
            </Descriptions.Item>
            <Descriptions.Item label="CVV">{copyableField(viewing.cvv)}</Descriptions.Item>
            <Descriptions.Item label="Tài khoản">
              {viewing.accountNames?.length ? viewing.accountNames.join(', ') : '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Cấp cho">
              {viewing.assigneeUserNames?.length ? viewing.assigneeUserNames.join(', ') : '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Đơn">{viewing.orderCount}</Descriptions.Item>
            <Descriptions.Item label="Trạng thái">
              <Tag color={viewing.isActive ? 'green' : 'default'} style={{ marginInlineEnd: 0 }}>
                {viewing.isActive ? 'Hoạt động' : 'Vô hiệu'}
              </Tag>
            </Descriptions.Item>
          </Descriptions>
        ) : null}
      </Modal>

      <Modal
        title={editing ? 'Sửa thẻ thanh toán' : 'Thêm thẻ thanh toán'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={() => void handleSubmit()}
        okText="Lưu"
        cancelText="Hủy"
        confirmLoading={createCard.isPending || updateCard.isPending}
        destroyOnClose
        width={modalWidth}
        centered={!isMobile}
        styles={modalBodyStyles}
        className="fulfil-edit-modal"
      >
        <Form
          form={form}
          layout="vertical"
          requiredMark={false}
          size="small"
          onValuesChange={(changed) => {
            if ('sourceId' in changed) {
              form.setFieldValue('sourceAccountId', undefined);
            }
          }}
        >
          <Form.Item
            name="sourceId"
            label="Nguồn thẻ"
            rules={[{ required: true, message: 'Chọn nguồn thẻ' }]}
          >
            <Select options={sourceOptions} placeholder="PingPongX / WorldFirst / Khác" />
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
          ) : null}
          <Form.Item
            name="currency"
            label="Account currency"
            rules={[{ required: true, message: 'Chọn loại tiền' }]}
          >
            <Select options={currencyOptions} />
          </Form.Item>
          <div className="fulfil-form-grid">
            <Form.Item
              name="cardNumber"
              label="Số thẻ"
              rules={[{ required: true, message: 'Nhập số thẻ' }]}
            >
              <Input placeholder="4111111111111111" />
            </Form.Item>
            <Form.Item name="cardBrand" label="Loại thẻ" rules={[{ required: true }]}>
              <Select options={FULFIL_CARD_BRANDS.map((b) => ({ value: b, label: b }))} />
            </Form.Item>
            <Form.Item name="expiryMonth" label="Tháng hết hạn">
              <InputNumber min={1} max={12} style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item name="expiryYear" label="Năm hết hạn">
              <InputNumber min={2000} max={2100} style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item name="holderName" label="Tên trên thẻ">
              <Input />
            </Form.Item>
            <Form.Item name="cvv" label="CVV">
              <Input.Password autoComplete="off" />
            </Form.Item>
          </div>
          <Form.Item
            name="accountIds"
            label="Tài khoản fulfil"
            extra={isMobile ? undefined : 'Một thẻ có thể gắn nhiều tài khoản mua hàng'}
          >
            <Select
              mode="multiple"
              allowClear
              showSearch
              optionFilterProp="label"
              options={accountSelectOptions}
              placeholder="Chọn một hoặc nhiều tài khoản"
            />
          </Form.Item>
          <Form.Item
            name="assigneeUserIds"
            label="Cấp cho"
            extra={isMobile ? undefined : 'Một thẻ có thể cấp cho nhiều người'}
          >
            <Select
              mode="multiple"
              allowClear
              showSearch
              optionFilterProp="label"
              options={userOptions}
              placeholder="Chọn một hoặc nhiều người"
            />
          </Form.Item>
          <Form.Item name="isActive" label="Hoạt động" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={editingSa ? 'Sửa tài khoản nguồn' : 'Thêm tài khoản nguồn'}
        open={saModalOpen}
        onCancel={() => setSaModalOpen(false)}
        onOk={() => void handleSaSubmit()}
        okText="Lưu"
        cancelText="Hủy"
        confirmLoading={createSourceAccount.isPending || updateSourceAccount.isPending}
        destroyOnClose
        width={isMobile ? '100%' : 480}
        centered={!isMobile}
        styles={modalBodyStyles}
        className="fulfil-edit-modal"
      >
        <Form form={saForm} layout="vertical" requiredMark={false} size="small">
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
        title="Import thẻ thanh toán từ file PingPong"
        open={importOpen}
        onCancel={closeImport}
        className="fulfil-edit-modal fulfil-import-modal"
        destroyOnClose
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
              {importProgress ? `Thêm (${importProgress.done}/${importProgress.total})` : 'Thêm'}
            </Button>
          </Space>
        }
      >
        <Form
          form={importForm}
          layout="vertical"
          requiredMark={false}
          size="small"
          style={{ marginTop: 4 }}
          onValuesChange={(changed) => {
            if ('sourceId' in changed) {
              importForm.setFieldValue('sourceAccountId', undefined);
            }
          }}
        >
          <div className="fulfil-form-grid">
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
            <Form.Item
              name="currency"
              label="Currency"
              rules={[{ required: true, message: 'Chọn loại tiền' }]}
              extra="Ưu tiên cột Currency trong file; chọn ở đây khi file thiếu."
            >
              <Select options={currencyOptions} disabled={importSubmitting} />
            </Form.Item>
            {importNeedsSourceAccount ? (
              <Form.Item
                name="sourceAccountId"
                label="Tài khoản nguồn"
                className="fulfil-form-grid-full"
                extra="Khớp theo cột Account Name trong file; chọn ở đây cho các dòng không khớp."
              >
                <Select
                  allowClear
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
            ) : null}
          </div>
        </Form>

        <div className="fulfil-import-upload">
          <Upload.Dragger
            accept=".xlsx,.xls"
            maxCount={1}
            fileList={importFileList}
            disabled={importSubmitting || importParsing}
            beforeUpload={(file) => {
              setImportFile(file);
              setImportFileList([{ uid: file.uid, name: file.name, status: 'done' }]);
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
            <p className="ant-upload-text">Chọn hoặc kéo file Excel thẻ ảo PingPong</p>
            <p className="ant-upload-hint">
              Định dạng .xlsx / .xls — cần cột Card Number, CVV, Expiration Date, Card Status
            </p>
          </Upload.Dragger>
          <div className="fulfil-import-upload-actions">
            <Button
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
            className="fulfil-import-preview"
            size="small"
            rowKey={(r) => `${r.rowIndex}-${r.cardNumber}`}
            dataSource={importRows}
            pagination={{
              pageSize: 20,
              size: 'small',
              showSizeChanger: false,
              showTotal: (total) => `${total} dòng`,
            }}
            scroll={{ x: 860 }}
            rowClassName={(r) => (r.error ? 'fulfil-import-row-error' : '')}
            columns={[
              { title: '#', dataIndex: 'rowIndex', width: 48, align: 'center' },
              {
                title: 'Số thẻ',
                dataIndex: 'cardNumber',
                width: 170,
                ellipsis: true,
                render: (v: string) => <span style={MONO}>{v || '—'}</span>,
              },
              {
                title: 'Loại',
                dataIndex: 'cardBrand',
                width: 80,
                render: (v: string) => v || '—',
              },
              {
                title: 'Hết hạn',
                key: 'expiry',
                width: 88,
                render: (_, row) => formatExpiry(row.expiryMonth, row.expiryYear),
              },
              { title: 'CVV', dataIndex: 'cvv', width: 70, render: (v?: string) => v || '—' },
              {
                title: 'Currency',
                dataIndex: 'currency',
                width: 90,
                render: (v?: string) => v || '—',
              },
              {
                title: 'Tên trên thẻ',
                dataIndex: 'holderName',
                ellipsis: true,
                render: (v?: string) => v || '—',
              },
              {
                title: 'Account Name',
                dataIndex: 'accountName',
                width: 150,
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
                render: (action: ImportPreviewRow['action'], row) => {
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
    </div>
  );
};
