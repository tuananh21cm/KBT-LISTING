import {
  useEffect,
  useMemo,
  useState,
  type CSSProperties,
  type MouseEvent,
  type ReactNode,
} from 'react';
import {
  Button,
  Card,
  Col,
  Form,
  Input,
  InputNumber,
  message,
  Popconfirm,
  Radio,
  Row,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  TimePicker,
  Tooltip,
  Typography,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import customParseFormat from 'dayjs/plugin/customParseFormat';
import { Modal } from '../../components/AppModal';
import {
  PlusOutlined,
  EditOutlined,
  DeleteOutlined,
  ReloadOutlined,
  InfoCircleOutlined,
  SendOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import {
  EXTENSION_SHEIN_SYNC_SETTING_KEYS,
  EXTENSION_SHEIN_SYNC_UNITS,
  EXTENSION_SHEIN_SYNC_UPDATE_MODES,
  EXTENSION_SHEIN_SYNC_CONCURRENT_THREADS,
  EXTENSION_SHEIN_SYNC_REFRESH_LOOKBACK,
} from '../../constants/extensionSheinSyncSettings';
import {
  SHEIN_DETAIL_SYNC_INTERVAL,
  SHEIN_DETAIL_SYNC_MAX_BILLNOS,
  SHEIN_DETAIL_SYNC_MAX_MARK_SHIP,
  SHEIN_DETAIL_SYNC_SETTING_KEYS,
} from '../../constants/sheinDetailSyncSettings';
import {
  EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS,
  EXTENSION_TIKTOK_SHOP_SYNC_UNITS,
} from '../../constants/extensionTikTokShopSyncSettings';
import {
  EXTENSION_TIKTOK_PAYOUT_SYNC_SETTING_KEYS,
  EXTENSION_TIKTOK_PAYOUT_SYNC_UNITS,
} from '../../constants/extensionTikTokPayoutSyncSettings';
import {
  EXTENSION_TIKTOK_OPPORTUNITY_SYNC_SETTING_KEYS,
  EXTENSION_TIKTOK_OPPORTUNITY_SYNC_UNITS,
} from '../../constants/extensionTikTokOpportunitySyncSettings';
import {
  EXTENSION_TIKTOK_UNIFIED_SYNC_SETTING_KEYS,
  EXTENSION_TIKTOK_UNIFIED_SYNC_STEP_LABELS,
} from '../../constants/extensionTikTokUnifiedSyncSettings';
import { FOURSELLER_PROXY_SETTING_KEYS } from '../../constants/fourSellerProxySettings';
import {
  LISTING_RATING_DEFAULTS,
  LISTING_RATING_SETTING_KEYS,
} from '../../constants/listingRatingSettings';
import {
  LISTING_REWRITE_TITLE_MODES,
  LISTING_REWRITE_TITLE_SETTING_KEYS,
  parseListingRewriteAllowRepeat,
  parseListingRewriteTitleMode,
} from '../../constants/listingRewriteSettings';
import {
  LISTING_SPECIFICS_SETTING_KEYS,
  parseListingSpecificsAllowRepeat,
} from '../../constants/listingSpecificsSettings';
import {
  LISTING_SIZE_CHART_SETTING_KEYS,
  parseListingSizeChartAutoGenerate,
} from '../../constants/listingSizeChartSettings';
import {
  LISTING_AUTO_PUBLISH_SETTING_KEYS,
  LISTING_AUTO_PUBLISH_DEFAULTS,
  LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES,
  LISTING_AUTO_PUBLISH_CONCURRENCY_HINT,
  parseListingAutoPublishEnabled,
} from '../../constants/listingAutoPublishSettings';
import { LISTING_SYNC_SETTING_KEYS } from '../../constants/listingSyncSettings';
import {
  FLASH_DEAL_SYNC_SETTING_KEYS,
  PRODUCT_DISCOUNT_SYNC_SETTING_KEYS,
} from '../../constants/productDiscountSyncSettings';
import {
  ORDER_HANDLING_DEFAULTS,
  ORDER_HANDLING_SETTING_KEYS,
} from '../../constants/orderHandlingSettings';
import {
  USPS_SCAN_AUTO_CHUNK_SIZE,
  USPS_SCAN_AUTO_CUTOFF_MARGIN_MINUTES,
  USPS_SCAN_AUTO_DEFAULTS,
  USPS_SCAN_AUTO_DELAY_HOURS,
  USPS_SCAN_AUTO_INTERVAL_MINUTES,
  USPS_SCAN_AUTO_SETTING_KEYS,
  parseUspsScanAutoEnabled,
  parseUspsScanClock,
  parseUspsScanFlagOn,
  parseUspsScanNumber,
} from '../../constants/uspsScanAutoSettings';
import { describeUspsScanWindow, etRangeToVnLabel } from './uspsScanWindow';
import {
  ORDER_SYNC_NEW_BOUNDS,
  ORDER_SYNC_SETTING_KEYS,
  ORDER_SYNC_STATUS_BOUNDS,
} from '../../constants/orderSyncSettings';
import {
  AFTER_SALES_SYNC_INTERVAL_BOUNDS,
  AFTER_SALES_SYNC_SETTING_KEYS,
} from '../../constants/afterSalesSyncSettings';
import {
  TELEGRAM_SETTING_KEYS,
  TELEGRAM_STALE_NO_TRACKING_DEFAULTS,
} from '../../constants/telegramSettings';
import {
  useAdminSettings,
  useCreateAdminSetting,
  useDeleteAdminSetting,
  useUpdateAdminSetting,
} from '../../hooks/useAdminSettings';
import { useOrderSyncHealth } from '../../hooks/useOrderSyncHealth';
import { adminSettingApi } from '../../services/adminSettingApi';
import { useAuthStore } from '../../stores/authStore';
import { useIsMobile } from '../../hooks/useIsMobile';
import { PageHeader } from '../../components/PageHeader';
import type { OrderSyncHealthBranch } from '../../types/orderSyncHealth';
import type { Setting, SettingType } from '../../types/setting';
import { SETTING_TYPE_OPTIONS } from '../../types/setting';
import { tablePagination } from '../../utils/pagination';
import './settings.css';

dayjs.extend(customParseFormat);

const SUMMARY_TIME_FORMAT = 'HH:mm';

/** Accepts `HH:mm` / `H:mm` or legacy bare hour `0–23`. */
const parseSummaryClock = (value: string | undefined): Dayjs | undefined => {
  if (value == null || value === '') return undefined;
  const trimmed = value.trim();
  const parsed = dayjs(trimmed, [SUMMARY_TIME_FORMAT, 'H:mm'], true);
  if (parsed.isValid()) return parsed;
  if (/^\d{1,2}$/.test(trimmed)) {
    const hour = Number(trimmed);
    if (hour >= 0 && hour <= 23) {
      return dayjs().hour(hour).minute(0).second(0).millisecond(0);
    }
  }
  return undefined;
};

const { Text } = Typography;

interface SettingFormValues {
  group: string;
  key: string;
  label: string;
  value: string;
  type: SettingType;
  description?: string;
}

const TYPE_COLORS: Record<SettingType, string> = {
  text: 'blue',
  number: 'orange',
  boolean: 'green',
  password: 'red',
};

const MOBILE_SETTING_COLUMN_KEYS = new Set(['label', 'value', 'actions']);

/** Clamps an after-sales interval to the bounds the backend enforces; null = not a usable number. */
const clampAfterSalesInterval = (value: number | string | null): string | null => {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return null;
  const { min, max } = AFTER_SALES_SYNC_INTERVAL_BOUNDS;
  return String(Math.min(max, Math.max(min, Math.floor(parsed))));
};

/** Clamps a number setting to min/max; null = not a usable number. */
const clampIntSetting = (
  value: number | string | null,
  min: number,
  max: number,
): string | null => {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return null;
  return String(Math.min(max, Math.max(min, Math.floor(parsed))));
};

/** Tạm ẩn card settings đơn hàng / Telegram / SHEIN / USPS / phí / promotions / payout / keywords. */
const SHOW_TEMP_HIDDEN_SETTING_CARDS = false;

const ellipsisStyle: CSSProperties = {
  display: 'block',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
};

const settingRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
  width: '100%',
};

const copyToClipboard = async (text: string, successMessage = 'Đã copy') => {
  await navigator.clipboard.writeText(text);
  message.success(successMessage);
};

const copyableValueCell = (setting: Setting) => {
  if (!setting.value) return '—';

  const handleCopy = (e: MouseEvent) => {
    e.stopPropagation();
    void copyToClipboard(setting.value, `Đã copy ${setting.label}`);
  };

  const display =
    setting.type === 'password'
      ? '••••••••'
      : setting.type === 'boolean'
        ? setting.value === 'true'
          ? 'true'
          : 'false'
        : setting.value;

  return (
    <Tooltip title="Click để copy">
      <span
        role="button"
        tabIndex={0}
        onClick={handleCopy}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            void copyToClipboard(setting.value, `Đã copy ${setting.label}`);
          }
        }}
        style={{ ...ellipsisStyle, cursor: 'pointer', userSelect: 'none' }}
      >
        {display}
      </span>
    </Tooltip>
  );
};

const SettingRow = ({
  label,
  control,
  description,
}: {
  label: ReactNode;
  control: ReactNode;
  description?: ReactNode;
}) => (
  <div style={{ width: '100%' }}>
    <div style={settingRowStyle}>
      <span style={{ minWidth: 0, flex: 1, lineHeight: 1.35, wordBreak: 'break-word' }}>
        {label}
      </span>
      <div style={{ flexShrink: 0 }}>{control}</div>
    </div>
    {description ? (
      <Text
        type="secondary"
        style={{ display: 'block', fontSize: 11, lineHeight: 1.4, marginTop: 4 }}
      >
        {description}
      </Text>
    ) : null}
  </div>
);

const formatHealthClock = (iso: string | null | undefined): string => {
  if (!iso) return '—';
  const parsed = dayjs(iso);
  return parsed.isValid() ? parsed.format('DD/MM HH:mm:ss') : '—';
};

const formatOverdue = (seconds: number | null | undefined): string | null => {
  if (seconds == null || seconds <= 0) return null;
  if (seconds < 60) return `trễ ${seconds}s`;
  if (seconds < 3600) return `trễ ${Math.floor(seconds / 60)} phút`;
  return `trễ ${(seconds / 3600).toFixed(1)} giờ`;
};

/** Compact runtime line from GET /health for the two order-sync cards. */
const OrderSyncRuntimeStatus = ({
  branch,
  isLoading,
  isError,
}: {
  branch: OrderSyncHealthBranch | undefined;
  isLoading: boolean;
  isError: boolean;
}) => {
  if (isLoading && !branch) {
    return (
      <Text type="secondary" style={{ fontSize: 11, lineHeight: 1.4 }}>
        Đang tải trạng thái job…
      </Text>
    );
  }

  if (isError || !branch) {
    return (
      <Text type="danger" style={{ fontSize: 11, lineHeight: 1.4 }}>
        Không đọc được /health — job có thể đã dừng (kiểm tra keep-alive IIS).
      </Text>
    );
  }

  const overdue = formatOverdue(branch.secondsOverdue);
  const stateLabel = branch.isRunning
    ? 'Đang chạy'
    : overdue
      ? overdue
      : branch.enabled === false
        ? 'Đã tắt'
        : 'Chờ chu kỳ';

  return (
    <Text
      type={overdue || branch.lastSummary?.startsWith('ERROR:') ? 'danger' : 'secondary'}
      style={{ fontSize: 11, lineHeight: 1.45, display: 'block' }}
    >
      Job: {stateLabel}
      {' · '}
      xong {formatHealthClock(branch.lastFinishedVietnam)}
      {' · '}
      tới {formatHealthClock(branch.nextDueVietnam)}
      {branch.lastSummary ? (
        <>
          <br />
          {branch.lastSummary}
        </>
      ) : null}
    </Text>
  );
};

export const SettingsPage = () => {
  const isMobile = useIsMobile();
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canManage = hasPermission('admin.settings.manage');

  const { data: settings = [], isLoading, isFetching, refetch } = useAdminSettings();
  const {
    data: orderSyncHealth,
    isLoading: orderSyncHealthLoading,
    isError: orderSyncHealthError,
    refetch: refetchOrderSyncHealth,
  } = useOrderSyncHealth();
  const createSetting = useCreateAdminSetting();
  const updateSetting = useUpdateAdminSetting();
  const deleteSetting = useDeleteAdminSetting();

  const [modalOpen, setModalOpen] = useState(false);
  const [editingSetting, setEditingSetting] = useState<Setting | null>(null);
  const [groupFilter, setGroupFilter] = useState<string | undefined>();
  const [form] = Form.useForm<SettingFormValues>();
  const [pagination, setPagination] = useState({ current: 1, pageSize: 15 });
  const selectedType = Form.useWatch('type', form);
  const [testAddTrackLoading, setTestAddTrackLoading] = useState(false);

  const sendAddTrackTest = async () => {
    if (!canManage) return;
    setTestAddTrackLoading(true);
    try {
      const res = await adminSettingApi.testSheinDetailAddTrackNotify();
      if (res.success) {
        message.success(res.message || 'Đã gửi tin TEST Add track.');
      } else {
        message.error(res.message || 'Không gửi được tin TEST.');
      }
    } catch {
      message.error('Không gửi được tin TEST Add track.');
    } finally {
      setTestAddTrackLoading(false);
    }
  };

  const groupOptions = useMemo(() => {
    const groups = [...new Set(settings.map((s) => s.group))].sort();
    return groups.map((g) => ({ value: g, label: g }));
  }, [settings]);

  const filteredSettings = useMemo(
    () => (groupFilter ? settings.filter((s) => s.group === groupFilter) : settings),
    [settings, groupFilter],
  );

  const orderSyncNewEnabled = useMemo(
    () => settings.find((s) => s.key === ORDER_SYNC_SETTING_KEYS.newEnabled),
    [settings],
  );
  const orderSyncNewInterval = useMemo(
    () => settings.find((s) => s.key === ORDER_SYNC_SETTING_KEYS.newIntervalMinutes),
    [settings],
  );
  const orderSyncNewLookback = useMemo(
    () => settings.find((s) => s.key === ORDER_SYNC_SETTING_KEYS.newLookbackDays),
    [settings],
  );
  const orderSyncNewBatchSize = useMemo(
    () => settings.find((s) => s.key === ORDER_SYNC_SETTING_KEYS.newBatchSize),
    [settings],
  );
  const orderSyncStatusEnabled = useMemo(
    () => settings.find((s) => s.key === ORDER_SYNC_SETTING_KEYS.statusEnabled),
    [settings],
  );
  const orderSyncStatusInterval = useMemo(
    () => settings.find((s) => s.key === ORDER_SYNC_SETTING_KEYS.statusIntervalHours),
    [settings],
  );
  const orderSyncStatusLookback = useMemo(
    () => settings.find((s) => s.key === ORDER_SYNC_SETTING_KEYS.statusLookbackDays),
    [settings],
  );
  const orderSyncStatusBatchSize = useMemo(
    () => settings.find((s) => s.key === ORDER_SYNC_SETTING_KEYS.statusBatchSize),
    [settings],
  );

  const afterSalesTriggerEnabled = useMemo(
    () => settings.find((s) => s.key === AFTER_SALES_SYNC_SETTING_KEYS.triggerEnabled),
    [settings],
  );
  const afterSalesTriggerInterval = useMemo(
    () => settings.find((s) => s.key === AFTER_SALES_SYNC_SETTING_KEYS.triggerIntervalMinutes),
    [settings],
  );
  const afterSalesPullEnabled = useMemo(
    () => settings.find((s) => s.key === AFTER_SALES_SYNC_SETTING_KEYS.pullEnabled),
    [settings],
  );
  const afterSalesPullInterval = useMemo(
    () => settings.find((s) => s.key === AFTER_SALES_SYNC_SETTING_KEYS.pullIntervalMinutes),
    [settings],
  );
  const afterSalesNotifyEnabled = useMemo(
    () => settings.find((s) => s.key === AFTER_SALES_SYNC_SETTING_KEYS.notifyEnabled),
    [settings],
  );
  const afterSalesChatId = useMemo(
    () => settings.find((s) => s.key === AFTER_SALES_SYNC_SETTING_KEYS.telegramChatId),
    [settings],
  );

  const listingSyncDraftEnabled = useMemo(
    () => settings.find((s) => s.key === LISTING_SYNC_SETTING_KEYS.draftEnabled),
    [settings],
  );
  const listingSyncDraftInterval = useMemo(
    () => settings.find((s) => s.key === LISTING_SYNC_SETTING_KEYS.draftIntervalMinutes),
    [settings],
  );
  const listingSyncPublishedEnabled = useMemo(
    () => settings.find((s) => s.key === LISTING_SYNC_SETTING_KEYS.publishedEnabled),
    [settings],
  );
  const listingSyncPublishedInterval = useMemo(
    () => settings.find((s) => s.key === LISTING_SYNC_SETTING_KEYS.publishedIntervalMinutes),
    [settings],
  );
  const listingSyncPublishedLookback = useMemo(
    () => settings.find((s) => s.key === LISTING_SYNC_SETTING_KEYS.publishedLookbackDays),
    [settings],
  );

  const productDiscountSyncEnabled = useMemo(
    () => settings.find((s) => s.key === PRODUCT_DISCOUNT_SYNC_SETTING_KEYS.enabled),
    [settings],
  );
  const productDiscountSyncIntervalHours = useMemo(
    () => settings.find((s) => s.key === PRODUCT_DISCOUNT_SYNC_SETTING_KEYS.intervalHours),
    [settings],
  );
  const flashDealSyncEnabled = useMemo(
    () => settings.find((s) => s.key === FLASH_DEAL_SYNC_SETTING_KEYS.enabled),
    [settings],
  );
  const flashDealSyncIntervalHours = useMemo(
    () => settings.find((s) => s.key === FLASH_DEAL_SYNC_SETTING_KEYS.intervalHours),
    [settings],
  );

  const listingRatingSuccessMin = useMemo(
    () => settings.find((s) => s.key === LISTING_RATING_SETTING_KEYS.successMin),
    [settings],
  );
  const listingRatingWarningMin = useMemo(
    () => settings.find((s) => s.key === LISTING_RATING_SETTING_KEYS.warningMin),
    [settings],
  );
  const listingRewriteTitleMode = useMemo(
    () => settings.find((s) => s.key === LISTING_REWRITE_TITLE_SETTING_KEYS.titleMode),
    [settings],
  );
  const listingRewriteAllowRepeat = useMemo(
    () => settings.find((s) => s.key === LISTING_REWRITE_TITLE_SETTING_KEYS.allowRepeat),
    [settings],
  );
  const listingSpecificsAllowRepeat = useMemo(
    () => settings.find((s) => s.key === LISTING_SPECIFICS_SETTING_KEYS.allowRepeat),
    [settings],
  );
  const listingSizeChartAutoGenerate = useMemo(
    () => settings.find((s) => s.key === LISTING_SIZE_CHART_SETTING_KEYS.autoGenerateEnabled),
    [settings],
  );
  const listingAutoPublishEnabled = useMemo(
    () => settings.find((s) => s.key === LISTING_AUTO_PUBLISH_SETTING_KEYS.enabled),
    [settings],
  );
  const listingAutoPublishMaxPublishes = useMemo(
    () => settings.find((s) => s.key === LISTING_AUTO_PUBLISH_SETTING_KEYS.maxConcurrentPublishes),
    [settings],
  );
  const listingAutoPublishDefaultInterval = useMemo(
    () => settings.find((s) => s.key === LISTING_AUTO_PUBLISH_SETTING_KEYS.defaultIntervalSeconds),
    [settings],
  );
  const listingAutoPublishDefaultMinDraftAge = useMemo(
    () => settings.find((s) => s.key === LISTING_AUTO_PUBLISH_SETTING_KEYS.defaultMinDraftAgeMinutes),
    [settings],
  );
  const listingAutoPublishLogRetention = useMemo(
    () => settings.find((s) => s.key === LISTING_AUTO_PUBLISH_SETTING_KEYS.logRetentionDays),
    [settings],
  );

  const buyTrackFee = useMemo(
    () => settings.find((s) => s.key === ORDER_HANDLING_SETTING_KEYS.buyTrack),
    [settings],
  );
  const thankCardFee = useMemo(
    () => settings.find((s) => s.key === ORDER_HANDLING_SETTING_KEYS.thankCard),
    [settings],
  );
  const uspsScanAutoEnabled = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.autoEnabled),
    [settings],
  );
  const uspsScanAutoDelayHours = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.delayHours),
    [settings],
  );
  const uspsScanAutoChunkSize = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.chunkSize),
    [settings],
  );
  const uspsScanAutoIntervalMinutes = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.intervalMinutes),
    [settings],
  );
  const uspsScanWindowEnabled = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.windowEnabled),
    [settings],
  );
  const uspsScanWeekdayStart = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.weekdayStart),
    [settings],
  );
  const uspsScanWeekdayEnd = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.weekdayEnd),
    [settings],
  );
  const uspsScanSaturdayEnabled = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.saturdayEnabled),
    [settings],
  );
  const uspsScanSaturdayStart = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.saturdayStart),
    [settings],
  );
  const uspsScanSaturdayEnd = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.saturdayEnd),
    [settings],
  );
  const uspsScanCutoffMarginMinutes = useMemo(
    () => settings.find((s) => s.key === USPS_SCAN_AUTO_SETTING_KEYS.cutoffMarginMinutes),
    [settings],
  );

  /** Window status is wall-clock derived, so re-render every minute to keep it honest. */
  const [uspsWindowTick, setUspsWindowTick] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setUspsWindowTick(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  const uspsScanWindowConfig = useMemo(
    () => ({
      enabled: parseUspsScanFlagOn(
        uspsScanWindowEnabled?.value,
        USPS_SCAN_AUTO_DEFAULTS.windowEnabled,
      ),
      weekdayStart: parseUspsScanClock(
        uspsScanWeekdayStart?.value,
        USPS_SCAN_AUTO_DEFAULTS.weekdayStart,
      ),
      weekdayEnd: parseUspsScanClock(
        uspsScanWeekdayEnd?.value,
        USPS_SCAN_AUTO_DEFAULTS.weekdayEnd,
      ),
      saturdayEnabled: parseUspsScanFlagOn(
        uspsScanSaturdayEnabled?.value,
        USPS_SCAN_AUTO_DEFAULTS.saturdayEnabled,
      ),
      saturdayStart: parseUspsScanClock(
        uspsScanSaturdayStart?.value,
        USPS_SCAN_AUTO_DEFAULTS.saturdayStart,
      ),
      saturdayEnd: parseUspsScanClock(
        uspsScanSaturdayEnd?.value,
        USPS_SCAN_AUTO_DEFAULTS.saturdayEnd,
      ),
      cutoffMarginMinutes: parseUspsScanNumber(
        uspsScanCutoffMarginMinutes?.value,
        USPS_SCAN_AUTO_CUTOFF_MARGIN_MINUTES,
      ),
    }),
    [
      uspsScanWindowEnabled,
      uspsScanWeekdayStart,
      uspsScanWeekdayEnd,
      uspsScanSaturdayEnabled,
      uspsScanSaturdayStart,
      uspsScanSaturdayEnd,
      uspsScanCutoffMarginMinutes,
    ],
  );

  const uspsScanWindowStatus = useMemo(
    () => describeUspsScanWindow(uspsScanWindowConfig, dayjs(uspsWindowTick)),
    [uspsScanWindowConfig, uspsWindowTick],
  );
  const uspsScanWeekdayVnLabel = useMemo(
    () =>
      etRangeToVnLabel(
        uspsScanWindowConfig.weekdayStart,
        uspsScanWindowConfig.weekdayEnd,
        dayjs(uspsWindowTick),
      ),
    [uspsScanWindowConfig, uspsWindowTick],
  );
  const uspsScanSaturdayVnLabel = useMemo(
    () =>
      etRangeToVnLabel(
        uspsScanWindowConfig.saturdayStart,
        uspsScanWindowConfig.saturdayEnd,
        dayjs(uspsWindowTick),
      ),
    [uspsScanWindowConfig, uspsWindowTick],
  );

  const telegramNotifyNewOrder = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.notifyNewOrder),
    [settings],
  );
  const telegramNotifyCancelRefund = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.notifyCancelRefund),
    [settings],
  );
  const telegramOrdersChatId = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.ordersChatId),
    [settings],
  );
  const telegramCancelRefundChatId = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.cancelRefundChatId),
    [settings],
  );
  const telegramDailyEnabled = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.dailySummaryEnabled),
    [settings],
  );
  const telegramDailyHour = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.dailySummaryHour),
    [settings],
  );
  const telegramSellerEnabled = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.sellerSummaryEnabled),
    [settings],
  );
  const telegramSellerHour = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.sellerSummaryHour),
    [settings],
  );
  const telegramFulfilEnabled = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.fulfilSummaryEnabled),
    [settings],
  );
  const telegramFulfilHour = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.fulfilSummaryHour),
    [settings],
  );
  const telegramNotifyUsps = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.notifyUsps),
    [settings],
  );
  const telegramNotifyWarehouseTrack = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.notifyWarehouseTrack),
    [settings],
  );
  const telegramNotifyOrderMessage = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.notifyOrderMessage),
    [settings],
  );
  const telegramNotifyStaleNoTracking = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.notifyStaleNoTracking),
    [settings],
  );
  const telegramStaleNoTrackingHours = useMemo(
    () => settings.find((s) => s.key === TELEGRAM_SETTING_KEYS.notifyStaleNoTrackingHours),
    [settings],
  );

  const sheinSyncEnabled = useMemo(
    () => settings.find((s) => s.key === EXTENSION_SHEIN_SYNC_SETTING_KEYS.enabled),
    [settings],
  );
  const sheinSyncIntervalValue = useMemo(
    () => settings.find((s) => s.key === EXTENSION_SHEIN_SYNC_SETTING_KEYS.intervalValue),
    [settings],
  );
  const sheinSyncIntervalUnit = useMemo(
    () => settings.find((s) => s.key === EXTENSION_SHEIN_SYNC_SETTING_KEYS.intervalUnit),
    [settings],
  );
  const sheinSyncConcurrentThreads = useMemo(
    () => settings.find((s) => s.key === EXTENSION_SHEIN_SYNC_SETTING_KEYS.concurrentThreads),
    [settings],
  );
  const sheinSyncUpdateMode = useMemo(
    () => settings.find((s) => s.key === EXTENSION_SHEIN_SYNC_SETTING_KEYS.updateMode),
    [settings],
  );
  const sheinSyncRefreshLookback = useMemo(
    () => settings.find((s) => s.key === EXTENSION_SHEIN_SYNC_SETTING_KEYS.refreshLookbackDays),
    [settings],
  );

  const sheinDetailSyncEnabled = useMemo(
    () => settings.find((s) => s.key === SHEIN_DETAIL_SYNC_SETTING_KEYS.enabled),
    [settings],
  );
  const sheinDetailSyncInterval = useMemo(
    () => settings.find((s) => s.key === SHEIN_DETAIL_SYNC_SETTING_KEYS.intervalMinutes),
    [settings],
  );
  const sheinDetailSyncMaxBillnos = useMemo(
    () => settings.find((s) => s.key === SHEIN_DETAIL_SYNC_SETTING_KEYS.maxBillnos),
    [settings],
  );
  const sheinDetailSyncMaxMarkShip = useMemo(
    () => settings.find((s) => s.key === SHEIN_DETAIL_SYNC_SETTING_KEYS.maxMarkShipPerPass),
    [settings],
  );
  const sheinDetailSyncNotify = useMemo(
    () => settings.find((s) => s.key === SHEIN_DETAIL_SYNC_SETTING_KEYS.notifyCarrierTracking),
    [settings],
  );
  const sheinDetailSyncChatId = useMemo(
    () => settings.find((s) => s.key === SHEIN_DETAIL_SYNC_SETTING_KEYS.telegramChatId),
    [settings],
  );

  const tiktokShopSyncEnabled = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS.enabled),
    [settings],
  );
  const tiktokShopSyncIntervalValue = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS.intervalValue),
    [settings],
  );
  const tiktokShopSyncIntervalUnit = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS.intervalUnit),
    [settings],
  );
  const tiktokShopNotifyEmail = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS.notifyEmailChange),
    [settings],
  );
  const tiktokShopNotifyMobile = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS.notifyMobileChange),
    [settings],
  );
  const tiktokShopNotifyBank = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS.notifyBankChange),
    [settings],
  );
  const tiktokShopNotifyBankLast4 = useMemo(
    () =>
      settings.find((s) => s.key === EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS.notifyBankLast4Mismatch),
    [settings],
  );
  const tiktokShopNotifyStatusChange = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS.notifyStatusChange),
    [settings],
  );
  const tiktokShopTelegramChatId = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_SHOP_SYNC_SETTING_KEYS.telegramChatId),
    [settings],
  );

  const tiktokPayoutSyncEnabled = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_PAYOUT_SYNC_SETTING_KEYS.enabled),
    [settings],
  );
  const tiktokPayoutSyncIntervalValue = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_PAYOUT_SYNC_SETTING_KEYS.intervalValue),
    [settings],
  );
  const tiktokPayoutSyncIntervalUnit = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_PAYOUT_SYNC_SETTING_KEYS.intervalUnit),
    [settings],
  );
  const tiktokPayoutSyncMaxDate = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_PAYOUT_SYNC_SETTING_KEYS.maxDate),
    [settings],
  );
  const tiktokPayoutNotifyNew = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_PAYOUT_SYNC_SETTING_KEYS.notifyNewPayment),
    [settings],
  );
  const tiktokPayoutTelegramChatId = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_PAYOUT_SYNC_SETTING_KEYS.telegramChatId),
    [settings],
  );

  const tiktokOpportunitySyncEnabled = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_OPPORTUNITY_SYNC_SETTING_KEYS.enabled),
    [settings],
  );
  const tiktokOpportunitySyncIntervalValue = useMemo(
    () =>
      settings.find((s) => s.key === EXTENSION_TIKTOK_OPPORTUNITY_SYNC_SETTING_KEYS.intervalValue),
    [settings],
  );
  const tiktokOpportunitySyncIntervalUnit = useMemo(
    () =>
      settings.find((s) => s.key === EXTENSION_TIKTOK_OPPORTUNITY_SYNC_SETTING_KEYS.intervalUnit),
    [settings],
  );

  const unifiedSyncStepInfo = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_UNIFIED_SYNC_SETTING_KEYS.stepInfo),
    [settings],
  );
  const unifiedSyncStepFinance = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_UNIFIED_SYNC_SETTING_KEYS.stepFinance),
    [settings],
  );
  const unifiedSyncStepKeywords = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_UNIFIED_SYNC_SETTING_KEYS.stepKeywords),
    [settings],
  );
  const unifiedSyncStepAnalytics = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_UNIFIED_SYNC_SETTING_KEYS.stepAnalytics),
    [settings],
  );
  const unifiedSyncStepHealthCenter = useMemo(
    () =>
      settings.find((s) => s.key === EXTENSION_TIKTOK_UNIFIED_SYNC_SETTING_KEYS.stepHealthCenter),
    [settings],
  );
  const unifiedSyncStepShopScore = useMemo(
    () => settings.find((s) => s.key === EXTENSION_TIKTOK_UNIFIED_SYNC_SETTING_KEYS.stepShopScore),
    [settings],
  );

  const unifiedSyncSteps = useMemo(
    () =>
      [
        { key: 'stepInfo' as const, setting: unifiedSyncStepInfo },
        { key: 'stepFinance' as const, setting: unifiedSyncStepFinance },
        { key: 'stepKeywords' as const, setting: unifiedSyncStepKeywords },
        { key: 'stepAnalytics' as const, setting: unifiedSyncStepAnalytics },
        { key: 'stepHealthCenter' as const, setting: unifiedSyncStepHealthCenter },
        { key: 'stepShopScore' as const, setting: unifiedSyncStepShopScore },
      ] as const,
    [
      unifiedSyncStepInfo,
      unifiedSyncStepFinance,
      unifiedSyncStepKeywords,
      unifiedSyncStepAnalytics,
      unifiedSyncStepHealthCenter,
      unifiedSyncStepShopScore,
    ],
  );

  const proxyEnabled = useMemo(
    () => settings.find((s) => s.key === FOURSELLER_PROXY_SETTING_KEYS.enabled),
    [settings],
  );
  const proxyPool = useMemo(
    () => settings.find((s) => s.key === FOURSELLER_PROXY_SETTING_KEYS.pool),
    [settings],
  );
  const proxyCooldown = useMemo(
    () => settings.find((s) => s.key === FOURSELLER_PROXY_SETTING_KEYS.cooldownMinutes),
    [settings],
  );
  const proxyValidateTtl = useMemo(
    () => settings.find((s) => s.key === FOURSELLER_PROXY_SETTING_KEYS.validateTtlMinutes),
    [settings],
  );

  const numberWidth = isMobile ? 88 : 120;
  const switchProps = {
    loading: updateSetting.isPending,
    checkedChildren: isMobile ? undefined : 'Bật',
    unCheckedChildren: isMobile ? undefined : 'Tắt',
  };

  const saveSettingValue = async (setting: Setting | undefined, value: string) => {
    if (!setting || !canManage) return;
    await updateSetting.mutateAsync({
      id: setting.id,
      data: {
        group: setting.group,
        key: setting.key,
        label: setting.label,
        value,
        type: setting.type,
        description: setting.description,
      },
    });
  };

  /**
   * InputNumber lưu ngay khi blur/step, kẹp trong [min, max] để giá trị gửi lên luôn nằm
   * trong khoảng backend clamp (tránh lưu 999 rồi backend âm thầm hạ xuống max).
   */
  const clampedNumberControl = (
    setting: Setting | undefined,
    range: { min: number; max: number; default: number },
    width = isMobile ? 72 : 88,
  ) => {
    const commit = (raw: number) => {
      if (!Number.isFinite(raw)) return;
      const next = String(Math.min(range.max, Math.max(range.min, Math.floor(raw))));
      if (next === setting?.value) return;
      void saveSettingValue(setting, next);
    };
    return (
      <InputNumber
        min={range.min}
        max={range.max}
        precision={0}
        style={{ width }}
        value={setting?.value ? Number(setting.value) : range.default}
        disabled={!canManage || !setting || updateSetting.isPending}
        onBlur={(e) => commit(Number(e.target.value))}
        onStep={(val) => val != null && commit(val)}
      />
    );
  };

  /**
   * `HH:mm` clock picker for settings holding a US Eastern wall-clock string.
   * `clock` is the already-normalized value so the picker never disagrees with the VN label.
   */
  const clockControl = (setting: Setting | undefined, clock: string, extraDisabled = false) => (
    <TimePicker
      format={SUMMARY_TIME_FORMAT}
      needConfirm={false}
      allowClear={false}
      showNow={false}
      minuteStep={15}
      style={{ width: isMobile ? 76 : 88 }}
      value={parseSummaryClock(clock)}
      disabled={!canManage || !setting || extraDisabled || updateSetting.isPending}
      onChange={(t) => {
        if (!t) return;
        const next = t.format(SUMMARY_TIME_FORMAT);
        if (next === setting?.value) return;
        void saveSettingValue(setting, next);
      }}
    />
  );

  const openCreate = () => {
    setEditingSetting(null);
    form.resetFields();
    form.setFieldsValue({ type: 'text', value: '' });
    setModalOpen(true);
  };

  const openEdit = (setting: Setting) => {
    setEditingSetting(setting);
    form.setFieldsValue({
      group: setting.group,
      key: setting.key,
      label: setting.label,
      value: setting.value,
      type: setting.type,
      description: setting.description,
    });
    setModalOpen(true);
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    const group = Array.isArray(values.group) ? values.group[0] : values.group;
    const payload = {
      ...values,
      group,
      value: values.type === 'boolean' ? (values.value === 'true' ? 'true' : 'false') : String(values.value ?? ''),
    };

    if (editingSetting) {
      await updateSetting.mutateAsync({ id: editingSetting.id, data: payload });
    } else {
      await createSetting.mutateAsync(payload);
    }
    setModalOpen(false);
  };

  const renderValueField = () => {
    switch (selectedType) {
      case 'number':
        return (
          <Form.Item
            name="value"
            label="Giá trị"
            getValueFromEvent={(val: number | null) => (val == null ? '' : String(val))}
            getValueProps={(val) => ({
              value: val === '' || val == null ? undefined : Number(val),
            })}
          >
            <InputNumber style={{ width: '100%' }} placeholder="Nhập số" />
          </Form.Item>
        );
      case 'boolean':
        return (
          <Form.Item
            name="value"
            label="Giá trị"
            valuePropName="checked"
            getValueProps={(val) => ({ checked: val === 'true' })}
          >
            <Switch
              checkedChildren="true"
              unCheckedChildren="false"
              onChange={(checked) => form.setFieldValue('value', checked ? 'true' : 'false')}
            />
          </Form.Item>
        );
      case 'password':
        return (
          <Form.Item name="value" label="Giá trị">
            <Input.Password placeholder="Nhập giá trị bí mật" />
          </Form.Item>
        );
      default:
        return (
          <Form.Item name="value" label="Giá trị">
            <Input placeholder="Nhập giá trị" />
          </Form.Item>
        );
    }
  };

  const columns: ColumnsType<Setting> = [
    {
      title: '#',
      key: 'index',
      width: 56,
      align: 'center',
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: 'Nhóm',
      dataIndex: 'group',
      key: 'group',
      width: 140,
    },
    { title: 'Key', dataIndex: 'key', key: 'key', width: 180, ellipsis: true },
    {
      title: isMobile ? 'Cấu hình' : 'Tên hiển thị',
      dataIndex: 'label',
      key: 'label',
      width: isMobile ? undefined : 160,
      ellipsis: true,
      render: (label: string, record) =>
        isMobile ? (
          <div style={{ minWidth: 0 }}>
            <div style={{ fontWeight: 500, lineHeight: 1.35 }}>{label}</div>
            <Text type="secondary" style={{ fontSize: 11 }}>
              {record.group}
            </Text>
          </div>
        ) : (
          label
        ),
    },
    {
      title: 'Giá trị',
      dataIndex: 'value',
      key: 'value',
      ellipsis: true,
      width: isMobile ? 100 : undefined,
      render: (_, record) => copyableValueCell(record),
    },
    {
      title: 'Loại',
      dataIndex: 'type',
      key: 'type',
      width: 100,
      render: (type: SettingType) => <Tag color={TYPE_COLORS[type]}>{type}</Tag>,
    },
    {
      title: 'Mô tả',
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
      render: (v) => v || '—',
    },
    {
      title: isMobile ? '' : 'Thao tác',
      key: 'actions',
      width: isMobile ? 72 : 100,
      align: isMobile ? 'right' : undefined,
      render: (_, record) =>
        canManage ? (
          <Space size={isMobile ? 0 : undefined}>
            <Button type="text" icon={<EditOutlined />} onClick={() => openEdit(record)} aria-label="Sửa" />
            <Popconfirm title="Xóa cấu hình này?" onConfirm={() => deleteSetting.mutate(record.id)}>
              <Button type="text" danger icon={<DeleteOutlined />} aria-label="Xóa" />
            </Popconfirm>
          </Space>
        ) : null,
    },
  ];

  const visibleColumns = isMobile
    ? columns.filter((col) => MOBILE_SETTING_COLUMN_KEYS.has(String(col.key)))
    : columns;

  return (
    <div>
      <PageHeader
        title={isMobile ? 'Cài đặt' : 'Cài đặt hệ thống'}
        extra={
          <Space wrap size={isMobile ? 4 : undefined}>
            <Button
              icon={<ReloadOutlined />}
              onClick={() => {
                void refetch();
                void refetchOrderSyncHealth();
              }}
              loading={isFetching}
              aria-label="Tải lại"
            >
              {isMobile ? null : 'Tải lại'}
            </Button>
            {canManage ? (
              <Tooltip title="Thêm cấu hình">
                <Button type="primary" icon={<PlusOutlined />} onClick={openCreate} aria-label="Thêm cấu hình">
                  {isMobile ? null : 'Thêm cấu hình'}
                </Button>
              </Tooltip>
            ) : null}
          </Space>
        }
      />

      <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Đơn mới' : 'Đồng bộ đơn hàng mới'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <SettingRow
                label={isMobile ? 'Tự động' : 'Tự động lấy đơn hàng mới'}
                control={
                  <Tooltip title="Job nhẹ: mỗi tài khoản 4Seller chạy một truy vấn lấy mọi trạng thái trong cửa sổ lookback ngắn">
                    <Switch
                      checked={orderSyncNewEnabled?.value === 'true'}
                      disabled={!canManage || !orderSyncNewEnabled || updateSetting.isPending}
                      {...switchProps}
                      onChange={(checked) =>
                        void saveSettingValue(orderSyncNewEnabled, checked ? 'true' : 'false')
                      }
                    />
                  </Tooltip>
                }
              />
              <SettingRow
                label={isMobile ? 'Chu kỳ (phút)' : 'Chu kỳ lấy đơn mới (phút)'}
                control={
                  <InputNumber
                    min={ORDER_SYNC_NEW_BOUNDS.intervalMinutes.min}
                    max={ORDER_SYNC_NEW_BOUNDS.intervalMinutes.max}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      orderSyncNewInterval?.value
                        ? Number(orderSyncNewInterval.value)
                        : undefined
                    }
                    disabled={!canManage || !orderSyncNewInterval || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = clampIntSetting(
                        Number(e.target.value),
                        ORDER_SYNC_NEW_BOUNDS.intervalMinutes.min,
                        ORDER_SYNC_NEW_BOUNDS.intervalMinutes.max,
                      );
                      if (next === null || next === orderSyncNewInterval?.value) return;
                      void saveSettingValue(orderSyncNewInterval, next);
                    }}
                    onStep={(val) => {
                      const next = clampIntSetting(
                        val,
                        ORDER_SYNC_NEW_BOUNDS.intervalMinutes.min,
                        ORDER_SYNC_NEW_BOUNDS.intervalMinutes.max,
                      );
                      if (next === null) return;
                      void saveSettingValue(orderSyncNewInterval, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Lookback (ngày)' : 'Lookback đơn mới (ngày VN)'}
                control={
                  <InputNumber
                    min={ORDER_SYNC_NEW_BOUNDS.lookbackDays.min}
                    max={ORDER_SYNC_NEW_BOUNDS.lookbackDays.max}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      orderSyncNewLookback?.value
                        ? Number(orderSyncNewLookback.value)
                        : undefined
                    }
                    disabled={!canManage || !orderSyncNewLookback || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = clampIntSetting(
                        Number(e.target.value),
                        ORDER_SYNC_NEW_BOUNDS.lookbackDays.min,
                        ORDER_SYNC_NEW_BOUNDS.lookbackDays.max,
                      );
                      if (next === null || next === orderSyncNewLookback?.value) return;
                      void saveSettingValue(orderSyncNewLookback, next);
                    }}
                    onStep={(val) => {
                      const next = clampIntSetting(
                        val,
                        ORDER_SYNC_NEW_BOUNDS.lookbackDays.min,
                        ORDER_SYNC_NEW_BOUNDS.lookbackDays.max,
                      );
                      if (next === null) return;
                      void saveSettingValue(orderSyncNewLookback, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Lô tài khoản' : 'Số tài khoản mỗi lô'}
                control={
                  <Tooltip title="Mỗi tài khoản 4Seller kéo đơn của tất cả shop thuộc cookie đó trong một request. Các lô chạy tuần tự đến hết danh sách">
                    <InputNumber
                      min={ORDER_SYNC_NEW_BOUNDS.batchSize.min}
                      max={ORDER_SYNC_NEW_BOUNDS.batchSize.max}
                      precision={0}
                      style={{ width: numberWidth }}
                      value={
                        orderSyncNewBatchSize?.value
                          ? Number(orderSyncNewBatchSize.value)
                          : undefined
                      }
                      disabled={!canManage || !orderSyncNewBatchSize || updateSetting.isPending}
                      onBlur={(e) => {
                        const next = clampIntSetting(
                          Number(e.target.value),
                          ORDER_SYNC_NEW_BOUNDS.batchSize.min,
                          ORDER_SYNC_NEW_BOUNDS.batchSize.max,
                        );
                        if (next === null || next === orderSyncNewBatchSize?.value) return;
                        void saveSettingValue(orderSyncNewBatchSize, next);
                      }}
                      onStep={(val) => {
                        const next = clampIntSetting(
                          val,
                          ORDER_SYNC_NEW_BOUNDS.batchSize.min,
                          ORDER_SYNC_NEW_BOUNDS.batchSize.max,
                        );
                        if (next === null) return;
                        void saveSettingValue(orderSyncNewBatchSize, next);
                      }}
                    />
                  </Tooltip>
                }
              />
              {(!orderSyncNewEnabled ||
                !orderSyncNewInterval ||
                !orderSyncNewLookback ||
                !orderSyncNewBatchSize) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
              <OrderSyncRuntimeStatus
                branch={orderSyncHealth?.orderSync.newOrders}
                isLoading={orderSyncHealthLoading}
                isError={orderSyncHealthError}
              />
            </Space>
          </Card>
        </Col>
        ) : null}

        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Trạng thái đơn' : 'Đồng bộ trạng thái đơn hàng'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <SettingRow
                label={isMobile ? 'Tự động' : 'Tự động cập nhật trạng thái'}
                control={
                  <Tooltip title="Job nặng: mỗi tài khoản 4Seller chạy một truy vấn lấy mọi trạng thái trong cửa sổ lookback dài. Giữ quyền ghi trọn lượt nên luồng đơn mới tạm nghỉ đến khi xong">
                    <Switch
                      checked={orderSyncStatusEnabled?.value === 'true'}
                      disabled={!canManage || !orderSyncStatusEnabled || updateSetting.isPending}
                      {...switchProps}
                      onChange={(checked) =>
                        void saveSettingValue(orderSyncStatusEnabled, checked ? 'true' : 'false')
                      }
                    />
                  </Tooltip>
                }
              />
              <SettingRow
                label={isMobile ? 'Chu kỳ (giờ)' : 'Chu kỳ cập nhật (giờ)'}
                control={
                  <InputNumber
                    min={ORDER_SYNC_STATUS_BOUNDS.intervalHours.min}
                    max={ORDER_SYNC_STATUS_BOUNDS.intervalHours.max}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      orderSyncStatusInterval?.value
                        ? Number(orderSyncStatusInterval.value)
                        : undefined
                    }
                    disabled={!canManage || !orderSyncStatusInterval || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = clampIntSetting(
                        Number(e.target.value),
                        ORDER_SYNC_STATUS_BOUNDS.intervalHours.min,
                        ORDER_SYNC_STATUS_BOUNDS.intervalHours.max,
                      );
                      if (next === null || next === orderSyncStatusInterval?.value) return;
                      void saveSettingValue(orderSyncStatusInterval, next);
                    }}
                    onStep={(val) => {
                      const next = clampIntSetting(
                        val,
                        ORDER_SYNC_STATUS_BOUNDS.intervalHours.min,
                        ORDER_SYNC_STATUS_BOUNDS.intervalHours.max,
                      );
                      if (next === null) return;
                      void saveSettingValue(orderSyncStatusInterval, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Lookback (ngày)' : 'Lookback trạng thái (ngày VN)'}
                control={
                  <InputNumber
                    min={ORDER_SYNC_STATUS_BOUNDS.lookbackDays.min}
                    max={ORDER_SYNC_STATUS_BOUNDS.lookbackDays.max}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      orderSyncStatusLookback?.value
                        ? Number(orderSyncStatusLookback.value)
                        : undefined
                    }
                    disabled={!canManage || !orderSyncStatusLookback || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = clampIntSetting(
                        Number(e.target.value),
                        ORDER_SYNC_STATUS_BOUNDS.lookbackDays.min,
                        ORDER_SYNC_STATUS_BOUNDS.lookbackDays.max,
                      );
                      if (next === null || next === orderSyncStatusLookback?.value) return;
                      void saveSettingValue(orderSyncStatusLookback, next);
                    }}
                    onStep={(val) => {
                      const next = clampIntSetting(
                        val,
                        ORDER_SYNC_STATUS_BOUNDS.lookbackDays.min,
                        ORDER_SYNC_STATUS_BOUNDS.lookbackDays.max,
                      );
                      if (next === null) return;
                      void saveSettingValue(orderSyncStatusLookback, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Lô tài khoản' : 'Số tài khoản mỗi lô'}
                control={
                  <Tooltip title="Mỗi tài khoản 4Seller kéo đơn của tất cả shop thuộc cookie đó trong một request. Các lô chạy tuần tự đến hết danh sách">
                    <InputNumber
                      min={ORDER_SYNC_STATUS_BOUNDS.batchSize.min}
                      max={ORDER_SYNC_STATUS_BOUNDS.batchSize.max}
                      precision={0}
                      style={{ width: numberWidth }}
                      value={
                        orderSyncStatusBatchSize?.value
                          ? Number(orderSyncStatusBatchSize.value)
                          : undefined
                      }
                      disabled={!canManage || !orderSyncStatusBatchSize || updateSetting.isPending}
                      onBlur={(e) => {
                        const next = clampIntSetting(
                          Number(e.target.value),
                          ORDER_SYNC_STATUS_BOUNDS.batchSize.min,
                          ORDER_SYNC_STATUS_BOUNDS.batchSize.max,
                        );
                        if (next === null || next === orderSyncStatusBatchSize?.value) return;
                        void saveSettingValue(orderSyncStatusBatchSize, next);
                      }}
                      onStep={(val) => {
                        const next = clampIntSetting(
                          val,
                          ORDER_SYNC_STATUS_BOUNDS.batchSize.min,
                          ORDER_SYNC_STATUS_BOUNDS.batchSize.max,
                        );
                        if (next === null) return;
                        void saveSettingValue(orderSyncStatusBatchSize, next);
                      }}
                    />
                  </Tooltip>
                }
              />
              {(!orderSyncStatusEnabled ||
                !orderSyncStatusInterval ||
                !orderSyncStatusLookback ||
                !orderSyncStatusBatchSize) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
              <OrderSyncRuntimeStatus
                branch={orderSyncHealth?.orderSync.status}
                isLoading={orderSyncHealthLoading}
                isError={orderSyncHealthError}
              />
            </Space>
          </Card>
        </Col>
        ) : null}

        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Đơn sau bán' : 'Đồng bộ đơn sau bán (4Seller)'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <SettingRow
                label={isMobile ? 'Kéo từ TikTok' : 'Sync đơn sau bán từ TikTok Shop'}
                control={
                  <Tooltip title="Yêu cầu 4Seller kéo yêu cầu hoàn/trả mới từ TikTok Shop về kho dữ liệu của 4Seller">
                    <Switch
                      checked={afterSalesTriggerEnabled?.value === 'true'}
                      disabled={!canManage || !afterSalesTriggerEnabled || updateSetting.isPending}
                      {...switchProps}
                      onChange={(checked) =>
                        void saveSettingValue(afterSalesTriggerEnabled, checked ? 'true' : 'false')
                      }
                    />
                  </Tooltip>
                }
              />
              <SettingRow
                label={isMobile ? 'Chu kỳ (phút)' : 'Chu kỳ kéo từ TikTok (phút)'}
                control={
                  <InputNumber
                    min={AFTER_SALES_SYNC_INTERVAL_BOUNDS.min}
                    max={AFTER_SALES_SYNC_INTERVAL_BOUNDS.max}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      afterSalesTriggerInterval?.value
                        ? Number(afterSalesTriggerInterval.value)
                        : undefined
                    }
                    disabled={!canManage || !afterSalesTriggerInterval || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = clampAfterSalesInterval(Number(e.target.value));
                      if (next === null || next === afterSalesTriggerInterval?.value) return;
                      void saveSettingValue(afterSalesTriggerInterval, next);
                    }}
                    onStep={(val) => {
                      const next = clampAfterSalesInterval(val);
                      if (next === null) return;
                      void saveSettingValue(afterSalesTriggerInterval, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Kéo từ 4Seller' : 'Sync đơn refund cần xử lý'}
                control={
                  <Tooltip title="Kéo danh sách hoàn/trả từ 4Seller để chuẩn trạng thái REFUND cho đơn. Đây là luồng duy nhất đặt hoặc gỡ REFUND">
                    <Switch
                      checked={afterSalesPullEnabled?.value === 'true'}
                      disabled={!canManage || !afterSalesPullEnabled || updateSetting.isPending}
                      {...switchProps}
                      onChange={(checked) =>
                        void saveSettingValue(afterSalesPullEnabled, checked ? 'true' : 'false')
                      }
                    />
                  </Tooltip>
                }
              />
              <SettingRow
                label={isMobile ? 'Chu kỳ (phút)' : 'Chu kỳ kéo từ 4Seller (phút)'}
                control={
                  <InputNumber
                    min={AFTER_SALES_SYNC_INTERVAL_BOUNDS.min}
                    max={AFTER_SALES_SYNC_INTERVAL_BOUNDS.max}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      afterSalesPullInterval?.value
                        ? Number(afterSalesPullInterval.value)
                        : undefined
                    }
                    disabled={!canManage || !afterSalesPullInterval || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = clampAfterSalesInterval(Number(e.target.value));
                      if (next === null || next === afterSalesPullInterval?.value) return;
                      void saveSettingValue(afterSalesPullInterval, next);
                    }}
                    onStep={(val) => {
                      const next = clampAfterSalesInterval(val);
                      if (next === null) return;
                      void saveSettingValue(afterSalesPullInterval, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Thông báo' : 'Thông báo đơn cần xử lý'}
                control={
                  <Tooltip title="Gửi Telegram cho đơn thực sự đổi trạng thái hoàn/trả trong lượt sync. Lượt chạy đầu tiên chỉ đồng bộ, không gửi">
                    <Switch
                      checked={afterSalesNotifyEnabled?.value === 'true'}
                      disabled={!canManage || !afterSalesNotifyEnabled || updateSetting.isPending}
                      {...switchProps}
                      onChange={(checked) =>
                        void saveSettingValue(afterSalesNotifyEnabled, checked ? 'true' : 'false')
                      }
                    />
                  </Tooltip>
                }
              />
              <SettingRow
                label={isMobile ? 'Chat sau bán' : 'Chat ID nhóm xử lý sau bán'}
                control={
                  <Input
                    style={{ width: isMobile ? 140 : 180 }}
                    placeholder="-1004462111161"
                    key={`after-sales-chat:${afterSalesChatId?.id}:${afterSalesChatId?.value ?? ''}`}
                    defaultValue={afterSalesChatId?.value ?? ''}
                    disabled={!canManage || !afterSalesChatId || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = e.target.value.trim();
                      if (next === (afterSalesChatId?.value ?? '')) return;
                      void saveSettingValue(afterSalesChatId, next);
                    }}
                  />
                }
              />
              {(!afterSalesTriggerEnabled ||
                !afterSalesTriggerInterval ||
                !afterSalesPullEnabled ||
                !afterSalesPullInterval ||
                !afterSalesNotifyEnabled ||
                !afterSalesChatId) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>
        ) : null}

        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Đồng bộ Draft' : 'Đồng bộ Draft 4Seller'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <SettingRow
                label={isMobile ? 'Tự động' : 'Tự động cập nhật Draft'}
                control={
                  <Switch
                    checked={listingSyncDraftEnabled?.value === 'true'}
                    disabled={!canManage || !listingSyncDraftEnabled || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(listingSyncDraftEnabled, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Chu kỳ (phút)' : 'Chu kỳ cập nhật (phút)'}
                control={
                  <InputNumber
                    min={1}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      listingSyncDraftInterval?.value
                        ? Number(listingSyncDraftInterval.value)
                        : undefined
                    }
                    disabled={!canManage || !listingSyncDraftInterval || updateSetting.isPending}
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 1) return;
                      const next = String(Math.floor(parsed));
                      if (next === listingSyncDraftInterval?.value) return;
                      void saveSettingValue(listingSyncDraftInterval, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 1) return;
                      void saveSettingValue(listingSyncDraftInterval, String(Math.floor(val)));
                    }}
                  />
                }
              />
              {(!listingSyncDraftEnabled || !listingSyncDraftInterval) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>

        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Đồng bộ Published' : 'Đồng bộ Published 4Seller'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <SettingRow
                label={isMobile ? 'Tự động' : 'Tự động cập nhật Published'}
                control={
                  <Switch
                    checked={listingSyncPublishedEnabled?.value === 'true'}
                    disabled={!canManage || !listingSyncPublishedEnabled || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(listingSyncPublishedEnabled, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Chu kỳ (phút)' : 'Chu kỳ cập nhật (phút)'}
                control={
                  <InputNumber
                    min={1}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      listingSyncPublishedInterval?.value
                        ? Number(listingSyncPublishedInterval.value)
                        : undefined
                    }
                    disabled={!canManage || !listingSyncPublishedInterval || updateSetting.isPending}
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 1) return;
                      const next = String(Math.floor(parsed));
                      if (next === listingSyncPublishedInterval?.value) return;
                      void saveSettingValue(listingSyncPublishedInterval, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 1) return;
                      void saveSettingValue(listingSyncPublishedInterval, String(Math.floor(val)));
                    }}
                  />
                }
              />
              <SettingRow
                label={
                  <span>
                    {isMobile ? 'Lookback (ngày)' : 'Lookback (ngày)'}{' '}
                    <Tooltip title="Quét lại trạng thái các listing tạo trong N ngày gần nhất (createTime 4Seller). Listing cũ hơn cửa sổ này chỉ được refresh qua reconcile theo id khi đang Reviewing/Pending.">
                      <InfoCircleOutlined style={{ color: 'var(--app-text-tertiary)' }} />
                    </Tooltip>
                  </span>
                }
                control={
                  <InputNumber
                    min={1}
                    max={30}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      listingSyncPublishedLookback?.value
                        ? Number(listingSyncPublishedLookback.value)
                        : undefined
                    }
                    disabled={!canManage || !listingSyncPublishedLookback || updateSetting.isPending}
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 1) return;
                      const next = String(Math.min(30, Math.floor(parsed)));
                      if (next === listingSyncPublishedLookback?.value) return;
                      void saveSettingValue(listingSyncPublishedLookback, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 1) return;
                      void saveSettingValue(
                        listingSyncPublishedLookback,
                        String(Math.min(30, Math.floor(val))),
                      );
                    }}
                  />
                }
              />
              {(!listingSyncPublishedEnabled ||
                !listingSyncPublishedInterval ||
                !listingSyncPublishedLookback) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>

        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Đồng bộ Promotions' : 'Đồng bộ Product Discount / Flash Deal'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <SettingRow
                label={isMobile ? 'PD tự động' : 'Tự động Product Discount'}
                control={
                  <Switch
                    checked={productDiscountSyncEnabled?.value === 'true'}
                    disabled={
                      !canManage || !productDiscountSyncEnabled || updateSetting.isPending
                    }
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(
                        productDiscountSyncEnabled,
                        checked ? 'true' : 'false',
                      )
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'PD chu kỳ (giờ)' : 'PD — đồng bộ sau mỗi (giờ)'}
                control={
                  <InputNumber
                    min={1}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      productDiscountSyncIntervalHours?.value
                        ? Number(productDiscountSyncIntervalHours.value)
                        : undefined
                    }
                    disabled={
                      !canManage ||
                      !productDiscountSyncIntervalHours ||
                      updateSetting.isPending
                    }
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 1) return;
                      const next = String(Math.floor(parsed));
                      if (next === productDiscountSyncIntervalHours?.value) return;
                      void saveSettingValue(productDiscountSyncIntervalHours, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 1) return;
                      void saveSettingValue(
                        productDiscountSyncIntervalHours,
                        String(Math.floor(val)),
                      );
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Flash tự động' : 'Tự động Flash Deal'}
                control={
                  <Switch
                    checked={flashDealSyncEnabled?.value === 'true'}
                    disabled={!canManage || !flashDealSyncEnabled || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(flashDealSyncEnabled, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Flash chu kỳ (giờ)' : 'Flash — đồng bộ sau mỗi (giờ)'}
                control={
                  <InputNumber
                    min={1}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      flashDealSyncIntervalHours?.value
                        ? Number(flashDealSyncIntervalHours.value)
                        : undefined
                    }
                    disabled={
                      !canManage || !flashDealSyncIntervalHours || updateSetting.isPending
                    }
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 1) return;
                      const next = String(Math.floor(parsed));
                      if (next === flashDealSyncIntervalHours?.value) return;
                      void saveSettingValue(flashDealSyncIntervalHours, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 1) return;
                      void saveSettingValue(
                        flashDealSyncIntervalHours,
                        String(Math.floor(val)),
                      );
                    }}
                  />
                }
              />
              <Text type="secondary" style={{ fontSize: 11 }}>
                Mỗi loại độc lập; chu kỳ tính từ lúc lần đồng bộ tự động của loại đó hoàn tất.
              </Text>
              {(!productDiscountSyncEnabled ||
                !productDiscountSyncIntervalHours ||
                !flashDealSyncEnabled ||
                !flashDealSyncIntervalHours) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>
        ) : null}

        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Auto publish' : 'Auto publish listing'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 12 : 14} style={{ width: '100%' }}>
              <SettingRow
                label="Bật toàn hệ thống"
                control={
                  <Switch
                    checked={parseListingAutoPublishEnabled(listingAutoPublishEnabled?.value)}
                    disabled={
                      !canManage || !listingAutoPublishEnabled || updateSetting.isPending
                    }
                    {...switchProps}
                    onChange={(checked) => {
                      if (!checked) {
                        message.info('Đã tắt auto publish — đang hủy các lượt chạy…');
                      }
                      void saveSettingValue(
                        listingAutoPublishEnabled,
                        checked ? 'true' : 'false',
                      );
                    }}
                  />
                }
              />
              <SettingRow
                label="Publish song song"
                description={LISTING_AUTO_PUBLISH_CONCURRENCY_HINT}
                control={
                  <InputNumber
                    min={1}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      listingAutoPublishMaxPublishes?.value
                        ? Number(listingAutoPublishMaxPublishes.value)
                        : LISTING_AUTO_PUBLISH_DEFAULTS.maxConcurrentPublishes
                    }
                    disabled={
                      !canManage || !listingAutoPublishMaxPublishes || updateSetting.isPending
                    }
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 1) return;
                      const next = String(Math.floor(parsed));
                      if (next === listingAutoPublishMaxPublishes?.value) return;
                      void saveSettingValue(listingAutoPublishMaxPublishes, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 1) return;
                      void saveSettingValue(
                        listingAutoPublishMaxPublishes,
                        String(Math.floor(val)),
                      );
                    }}
                  />
                }
              />
              <SettingRow
                label="Chu kỳ mặc định (giây)"
                description="Dùng khi shop chưa cấu hình interval riêng."
                control={
                  <InputNumber
                    min={1}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      listingAutoPublishDefaultInterval?.value
                        ? Number(listingAutoPublishDefaultInterval.value)
                        : LISTING_AUTO_PUBLISH_DEFAULTS.defaultIntervalSeconds
                    }
                    disabled={
                      !canManage || !listingAutoPublishDefaultInterval || updateSetting.isPending
                    }
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 1) return;
                      const next = String(Math.floor(parsed));
                      if (next === listingAutoPublishDefaultInterval?.value) return;
                      void saveSettingValue(listingAutoPublishDefaultInterval, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 1) return;
                      void saveSettingValue(
                        listingAutoPublishDefaultInterval,
                        String(Math.floor(val)),
                      );
                    }}
                  />
                }
              />
              <SettingRow
                label="Chờ draft (phút)"
                description="Dùng khi shop chưa cấu hình thời gian chờ riêng."
                control={
                  <InputNumber
                    min={LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      listingAutoPublishDefaultMinDraftAge?.value
                        ? Number(listingAutoPublishDefaultMinDraftAge.value)
                        : LISTING_AUTO_PUBLISH_DEFAULTS.defaultMinDraftAgeMinutes
                    }
                    disabled={
                      !canManage || !listingAutoPublishDefaultMinDraftAge || updateSetting.isPending
                    }
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (
                        !Number.isFinite(parsed)
                        || parsed < LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES
                      ) {
                        return;
                      }
                      const next = String(Math.floor(parsed));
                      if (next === listingAutoPublishDefaultMinDraftAge?.value) return;
                      void saveSettingValue(listingAutoPublishDefaultMinDraftAge, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < LISTING_AUTO_PUBLISH_MIN_DRAFT_AGE_MINUTES) return;
                      void saveSettingValue(
                        listingAutoPublishDefaultMinDraftAge,
                        String(Math.floor(val)),
                      );
                    }}
                  />
                }
              />
              <SettingRow
                label="Giữ log (ngày)"
                control={
                  <InputNumber
                    min={1}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      listingAutoPublishLogRetention?.value
                        ? Number(listingAutoPublishLogRetention.value)
                        : LISTING_AUTO_PUBLISH_DEFAULTS.logRetentionDays
                    }
                    disabled={
                      !canManage || !listingAutoPublishLogRetention || updateSetting.isPending
                    }
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 1) return;
                      const next = String(Math.floor(parsed));
                      if (next === listingAutoPublishLogRetention?.value) return;
                      void saveSettingValue(listingAutoPublishLogRetention, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 1) return;
                      void saveSettingValue(
                        listingAutoPublishLogRetention,
                        String(Math.floor(val)),
                      );
                    }}
                  />
                }
              />
              {(!listingAutoPublishEnabled ||
                !listingAutoPublishMaxPublishes ||
                !listingAutoPublishDefaultInterval ||
                !listingAutoPublishDefaultMinDraftAge ||
                !listingAutoPublishLogRetention) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>

        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Phí xử lý' : 'Phí xử lý đơn'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <Text type="secondary" style={{ fontSize: isMobile ? 12 : 13 }}>
                BuyTrack cộng khi đánh dấu USPS; ThankCard khi đánh dấu mua thank card (track kho
                Shein).
              </Text>
              <SettingRow
                label={
                  <Space size={4}>
                    BuyTrack (USD)
                    <Tooltip title="Phí mua scan track USPS — cộng vào chi phí mua hàng">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={
                  <InputNumber
                    min={0}
                    step={0.1}
                    precision={2}
                    style={{ width: numberWidth }}
                    value={
                      buyTrackFee?.value != null && buyTrackFee.value !== ''
                        ? Number(buyTrackFee.value)
                        : ORDER_HANDLING_DEFAULTS.buyTrack
                    }
                    disabled={!canManage || !buyTrackFee || updateSetting.isPending}
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 0) return;
                      const next = String(Math.round(parsed * 100) / 100);
                      if (next === buyTrackFee?.value) return;
                      void saveSettingValue(buyTrackFee, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 0) return;
                      void saveSettingValue(
                        buyTrackFee,
                        String(Math.round(val * 100) / 100),
                      );
                    }}
                  />
                }
              />
              <SettingRow
                label={
                  <Space size={4}>
                    ThankCard (USD)
                    <Tooltip title="Phí mua thank card gửi khách — cộng vào chi phí mua hàng">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={
                  <InputNumber
                    min={0}
                    step={0.1}
                    precision={2}
                    style={{ width: numberWidth }}
                    value={
                      thankCardFee?.value != null && thankCardFee.value !== ''
                        ? Number(thankCardFee.value)
                        : ORDER_HANDLING_DEFAULTS.thankCard
                    }
                    disabled={!canManage || !thankCardFee || updateSetting.isPending}
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 0) return;
                      const next = String(Math.round(parsed * 100) / 100);
                      if (next === thankCardFee?.value) return;
                      void saveSettingValue(thankCardFee, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 0) return;
                      void saveSettingValue(
                        thankCardFee,
                        String(Math.round(val * 100) / 100),
                      );
                    }}
                  />
                }
              />
              {(!buyTrackFee || !thankCardFee) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>
        ) : null}

        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'USPS auto' : 'USPS Track Scan tự động'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <Text type="secondary" style={{ fontSize: 12 }}>
                Nhân viên bưu điện Mỹ chỉ scan trong giờ hành chính, nên mỗi chu kỳ chỉ đẩy khi
                khung giờ US đang mở.
              </Text>
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'Bật' : 'Tự động đẩy'}
                    <Tooltip title="Bật BackgroundService đẩy track lên PrintPackShip">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={
                  <Switch
                    {...switchProps}
                    checked={parseUspsScanAutoEnabled(
                      uspsScanAutoEnabled?.value ?? String(USPS_SCAN_AUTO_DEFAULTS.autoEnabled),
                    )}
                    disabled={!canManage || !uspsScanAutoEnabled || updateSetting.isPending}
                    onChange={(checked) => {
                      void saveSettingValue(uspsScanAutoEnabled, checked ? 'true' : 'false');
                    }}
                  />
                }
              />
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'Giờ US' : 'Theo giờ làm việc US'}
                    <Tooltip title="Chỉ đẩy trong giờ hành chính bưu điện Mỹ (neo America/New_York, tự khớp DST). Tắt = đẩy suốt ngày đêm">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={
                  <Switch
                    {...switchProps}
                    checked={uspsScanWindowConfig.enabled}
                    disabled={!canManage || !uspsScanWindowEnabled || updateSetting.isPending}
                    onChange={(checked) => {
                      void saveSettingValue(uspsScanWindowEnabled, checked ? 'true' : 'false');
                    }}
                  />
                }
              />
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'T2–T6 (ET)' : 'T2–T6 mở/đóng (giờ ET)'}
                    <Tooltip title="Giờ Mỹ Miền Đông. Bưu điện làm 8h–17h các ngày trong tuần; giờ VN tương ứng hiển thị bên dưới và tự lệch 1 tiếng khi Mỹ đổi DST">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={
                  <Space size={4}>
                    {clockControl(
                      uspsScanWeekdayStart,
                      uspsScanWindowConfig.weekdayStart,
                      !uspsScanWindowConfig.enabled,
                    )}
                    {clockControl(
                      uspsScanWeekdayEnd,
                      uspsScanWindowConfig.weekdayEnd,
                      !uspsScanWindowConfig.enabled,
                    )}
                  </Space>
                }
              />
              {uspsScanWeekdayVnLabel && (
                <Text type="secondary" style={{ fontSize: 11 }}>
                  T2–T6 giờ VN: {uspsScanWeekdayVnLabel}
                </Text>
              )}
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'Thứ 7' : 'Đẩy cả sáng thứ 7'}
                    <Tooltip title="Bưu điện Mỹ làm sáng thứ 7. Chủ nhật luôn nghỉ, không đẩy">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={
                  <Switch
                    {...switchProps}
                    checked={uspsScanWindowConfig.saturdayEnabled}
                    disabled={
                      !canManage ||
                      !uspsScanSaturdayEnabled ||
                      !uspsScanWindowConfig.enabled ||
                      updateSetting.isPending
                    }
                    onChange={(checked) => {
                      void saveSettingValue(uspsScanSaturdayEnabled, checked ? 'true' : 'false');
                    }}
                  />
                }
              />
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'T7 (ET)' : 'Thứ 7 mở/đóng (giờ ET)'}
                    <Tooltip title="Giờ Mỹ Miền Đông cho sáng thứ 7 (mặc định 8h–12h)">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={
                  <Space size={4}>
                    {clockControl(
                      uspsScanSaturdayStart,
                      uspsScanWindowConfig.saturdayStart,
                      !uspsScanWindowConfig.enabled || !uspsScanWindowConfig.saturdayEnabled,
                    )}
                    {clockControl(
                      uspsScanSaturdayEnd,
                      uspsScanWindowConfig.saturdayEnd,
                      !uspsScanWindowConfig.enabled || !uspsScanWindowConfig.saturdayEnabled,
                    )}
                  </Space>
                }
              />
              {uspsScanWindowConfig.saturdayEnabled && uspsScanSaturdayVnLabel && (
                <Text type="secondary" style={{ fontSize: 11 }}>
                  Thứ 7 giờ VN: {uspsScanSaturdayVnLabel}
                </Text>
              )}
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'Đệm (phút)' : 'Đệm trước giờ đóng (phút)'}
                    <Tooltip title="Ngừng đẩy N phút trước giờ đóng để lô cuối kịp được scan; cũng cắt các gói còn lại nếu chu kỳ chạy quá giờ">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={clampedNumberControl(
                  uspsScanCutoffMarginMinutes,
                  USPS_SCAN_AUTO_CUTOFF_MARGIN_MINUTES,
                )}
              />
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'Tuổi mã (h)' : 'Tuổi tối thiểu (giờ)'}
                    <Tooltip title="Chặn track vừa thêm: chỉ đẩy sau N giờ kể từ lúc lần đầu thêm mã VC (TrackingAddedAt), không tính từ ngày tạo đơn. Thời điểm đẩy vẫn do khung giờ US quyết định">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={clampedNumberControl(uspsScanAutoDelayHours, USPS_SCAN_AUTO_DELAY_HOURS)}
              />
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'Mỗi lần gửi' : 'Số mã mỗi lần gửi'}
                    <Tooltip title="Mỗi chu kỳ lấy hết track đủ điều kiện. N>0: gửi từng gói N mã. N=0: gửi tất cả lên cùng một lần">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={clampedNumberControl(uspsScanAutoChunkSize, USPS_SCAN_AUTO_CHUNK_SIZE)}
              />
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'Chờ (phút)' : 'Khoảng chờ (phút)'}
                    <Tooltip title="Sau khi gửi hết các gói mới đếm N phút rồi quét lại">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={clampedNumberControl(
                  uspsScanAutoIntervalMinutes,
                  USPS_SCAN_AUTO_INTERVAL_MINUTES,
                )}
              />
              <Text
                type={uspsScanWindowStatus.isOpen ? 'success' : 'secondary'}
                style={{ fontSize: 12 }}
              >
                {uspsScanWindowStatus.text}
              </Text>
              {(!uspsScanAutoEnabled ||
                !uspsScanAutoDelayHours ||
                !uspsScanAutoChunkSize ||
                !uspsScanAutoIntervalMinutes ||
                !uspsScanWindowEnabled ||
                !uspsScanWeekdayStart ||
                !uspsScanWeekdayEnd ||
                !uspsScanSaturdayEnabled ||
                !uspsScanSaturdayStart ||
                !uspsScanSaturdayEnd ||
                !uspsScanCutoffMarginMinutes) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>
        ) : null}

        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Cảnh báo sao' : 'Cảnh báo rating listing'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <Text type="secondary" style={{ fontSize: isMobile ? 12 : 13 }}>
                Màu ngôi sao trên ListingCard: ≥ tốt → xanh · ≥ cảnh báo → vàng · dưới → đỏ.
              </Text>
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'Ngưỡng xanh' : 'Ngưỡng sao tốt (xanh)'}
                    <Tooltip title="Rating ≥ ngưỡng này → ngôi sao màu success (xanh)">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={
                  <InputNumber
                    min={0}
                    max={5}
                    step={0.1}
                    precision={1}
                    style={{ width: numberWidth }}
                    value={
                      listingRatingSuccessMin?.value
                        ? Number(listingRatingSuccessMin.value)
                        : LISTING_RATING_DEFAULTS.successMin
                    }
                    disabled={!canManage || !listingRatingSuccessMin || updateSetting.isPending}
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 0 || parsed > 5) return;
                      const next = String(Math.round(parsed * 10) / 10);
                      if (next === listingRatingSuccessMin?.value) return;
                      void saveSettingValue(listingRatingSuccessMin, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 0 || val > 5) return;
                      void saveSettingValue(
                        listingRatingSuccessMin,
                        String(Math.round(val * 10) / 10),
                      );
                    }}
                  />
                }
              />
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'Ngưỡng vàng/đỏ' : 'Ngưỡng cảnh báo (vàng/đỏ)'}
                    <Tooltip title="≥ ngưỡng này và < ngưỡng tốt → vàng; dưới ngưỡng này → đỏ">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={
                  <InputNumber
                    min={0}
                    max={5}
                    step={0.1}
                    precision={1}
                    style={{ width: numberWidth }}
                    value={
                      listingRatingWarningMin?.value
                        ? Number(listingRatingWarningMin.value)
                        : LISTING_RATING_DEFAULTS.warningMin
                    }
                    disabled={!canManage || !listingRatingWarningMin || updateSetting.isPending}
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 0 || parsed > 5) return;
                      const next = String(Math.round(parsed * 10) / 10);
                      if (next === listingRatingWarningMin?.value) return;
                      void saveSettingValue(listingRatingWarningMin, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 0 || val > 5) return;
                      void saveSettingValue(
                        listingRatingWarningMin,
                        String(Math.round(val * 10) / 10),
                      );
                    }}
                  />
                }
              />
              {(!listingRatingSuccessMin || !listingRatingWarningMin) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>

        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Rewrite / Specifics' : 'Rewrite & Auto Specifics'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <Text type="secondary" style={{ fontSize: isMobile ? 12 : 13 }}>
                Áp dụng khi bấm Rewrite / Auto Specifics trên trang sửa listing (admin và seller).
              </Text>
              <Radio.Group
                value={parseListingRewriteTitleMode(listingRewriteTitleMode?.value)}
                disabled={!canManage || !listingRewriteTitleMode || updateSetting.isPending}
                onChange={(e) =>
                  void saveSettingValue(listingRewriteTitleMode, String(e.target.value))
                }
              >
                <Space direction="vertical" size={8}>
                  <Radio value={LISTING_REWRITE_TITLE_MODES.stripBrands}>
                    {isMobile
                      ? 'Xóa thương hiệu (quá ngắn → SEO Women …)'
                      : 'Chỉ xóa thương hiệu (giữ tiêu đề gốc; quá ngắn thì tạo SEO Women …)'}
                  </Radio>
                  <Radio value={LISTING_REWRITE_TITLE_MODES.rewrite}>
                    {isMobile
                      ? 'Machine-first SEO'
                      : 'Machine-first SEO (150–250, không bịa thuộc tính)'}
                  </Radio>
                </Space>
              </Radio.Group>
              <SettingRow
                label={isMobile ? 'Rewrite lại' : 'Cho phép Rewrite lại'}
                control={
                  <Switch
                    checked={parseListingRewriteAllowRepeat(listingRewriteAllowRepeat?.value)}
                    disabled={!canManage || !listingRewriteAllowRepeat || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(
                        listingRewriteAllowRepeat,
                        checked ? 'true' : 'false',
                      )
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Specifics lại' : 'Cho phép tạo lại Specifics'}
                control={
                  <Switch
                    checked={parseListingSpecificsAllowRepeat(listingSpecificsAllowRepeat?.value)}
                    disabled={!canManage || !listingSpecificsAllowRepeat || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(
                        listingSpecificsAllowRepeat,
                        checked ? 'true' : 'false',
                      )
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Size Chart AI' : 'Tạo ảnh Size Chart tự động'}
                control={
                  <Switch
                    checked={parseListingSizeChartAutoGenerate(listingSizeChartAutoGenerate?.value)}
                    disabled={
                      !canManage || !listingSizeChartAutoGenerate || updateSetting.isPending
                    }
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(
                        listingSizeChartAutoGenerate,
                        checked ? 'true' : 'false',
                      )
                    }
                  />
                }
              />
              {(!listingRewriteTitleMode ||
                !listingRewriteAllowRepeat ||
                !listingSpecificsAllowRepeat ||
                !listingSizeChartAutoGenerate) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>

        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={24} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Telegram' : 'Thông báo Telegram'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <SettingRow
                label={isMobile ? 'Đơn mới' : 'Khi có đơn hàng mới'}
                control={
                  <Switch
                    checked={telegramNotifyNewOrder?.value === 'true'}
                    disabled={!canManage || !telegramNotifyNewOrder || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(telegramNotifyNewOrder, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Hủy/Refund' : 'Khi có đơn hủy/refund'}
                control={
                  <Switch
                    checked={telegramNotifyCancelRefund?.value === 'true'}
                    disabled={!canManage || !telegramNotifyCancelRefund || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(telegramNotifyCancelRefund, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'USPS' : 'Thông báo USPS cần xử lý'}
                control={
                  <Switch
                    checked={telegramNotifyUsps?.value === 'true'}
                    disabled={!canManage || !telegramNotifyUsps || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(telegramNotifyUsps, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Track kho' : 'Thông báo track kho Shein'}
                control={
                  <Switch
                    checked={telegramNotifyWarehouseTrack?.value === 'true'}
                    disabled={!canManage || !telegramNotifyWarehouseTrack || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(telegramNotifyWarehouseTrack, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Message' : 'Thông báo xử lý đơn (message)'}
                control={
                  <Switch
                    checked={telegramNotifyOrderMessage?.value === 'true'}
                    disabled={!canManage || !telegramNotifyOrderMessage || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(telegramNotifyOrderMessage, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Chat báo đơn' : 'Chat ID báo đơn / summary'}
                control={
                  <Input
                    style={{ width: isMobile ? 140 : 180 }}
                    placeholder="-1004406324922"
                    key={`orders-chat:${telegramOrdersChatId?.id}:${telegramOrdersChatId?.value ?? ''}`}
                    defaultValue={telegramOrdersChatId?.value ?? ''}
                    disabled={!canManage || !telegramOrdersChatId || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = e.target.value.trim();
                      if (next === (telegramOrdersChatId?.value ?? '')) return;
                      void saveSettingValue(telegramOrdersChatId, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Chat xử lý' : 'Chat ID xử lý đơn'}
                control={
                  <Input
                    style={{ width: isMobile ? 140 : 180 }}
                    placeholder="-100…"
                    key={`cancel-chat:${telegramCancelRefundChatId?.id}:${telegramCancelRefundChatId?.value ?? ''}`}
                    defaultValue={telegramCancelRefundChatId?.value ?? ''}
                    disabled={!canManage || !telegramCancelRefundChatId || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = e.target.value.trim();
                      if (next === (telegramCancelRefundChatId?.value ?? '')) return;
                      void saveSettingValue(telegramCancelRefundChatId, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Tổng hợp đơn' : 'Tổng hợp đơn trong ngày'}
                control={
                  <Switch
                    checked={telegramDailyEnabled?.value === 'true'}
                    disabled={!canManage || !telegramDailyEnabled || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(telegramDailyEnabled, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Giờ tổng hợp đơn' : 'Giờ đẩy tổng hợp đơn (HH:mm, GMT+7)'}
                control={
                  <TimePicker
                    format={SUMMARY_TIME_FORMAT}
                    needConfirm={false}
                    allowClear={false}
                    showNow={false}
                    style={{ width: isMobile ? 100 : 120 }}
                    value={parseSummaryClock(telegramDailyHour?.value)}
                    disabled={!canManage || !telegramDailyHour || updateSetting.isPending}
                    onChange={(t) => {
                      if (!t) return;
                      const next = t.format(SUMMARY_TIME_FORMAT);
                      if (next === telegramDailyHour?.value) return;
                      void saveSettingValue(telegramDailyHour, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Tổng hợp Seller' : 'Tổng hợp Seller trong ngày'}
                control={
                  <Switch
                    checked={telegramSellerEnabled?.value === 'true'}
                    disabled={!canManage || !telegramSellerEnabled || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(telegramSellerEnabled, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Giờ tổng hợp Seller' : 'Giờ đẩy tổng hợp Seller (HH:mm, GMT+7)'}
                control={
                  <TimePicker
                    format={SUMMARY_TIME_FORMAT}
                    needConfirm={false}
                    allowClear={false}
                    showNow={false}
                    style={{ width: isMobile ? 100 : 120 }}
                    value={parseSummaryClock(telegramSellerHour?.value)}
                    disabled={!canManage || !telegramSellerHour || updateSetting.isPending}
                    onChange={(t) => {
                      if (!t) return;
                      const next = t.format(SUMMARY_TIME_FORMAT);
                      if (next === telegramSellerHour?.value) return;
                      void saveSettingValue(telegramSellerHour, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Xử lý đơn' : 'Tổng hợp xử lý đơn hàng'}
                control={
                  <Switch
                    checked={telegramFulfilEnabled?.value === 'true'}
                    disabled={!canManage || !telegramFulfilEnabled || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(telegramFulfilEnabled, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Giờ xử lý đơn' : 'Giờ đẩy tổng hợp xử lý đơn (HH:mm, GMT+7)'}
                control={
                  <TimePicker
                    format={SUMMARY_TIME_FORMAT}
                    needConfirm={false}
                    allowClear={false}
                    showNow={false}
                    style={{ width: isMobile ? 100 : 120 }}
                    value={parseSummaryClock(telegramFulfilHour?.value)}
                    disabled={!canManage || !telegramFulfilHour || updateSetting.isPending}
                    onChange={(t) => {
                      if (!t) return;
                      const next = t.format(SUMMARY_TIME_FORMAT);
                      if (next === telegramFulfilHour?.value) return;
                      void saveSettingValue(telegramFulfilHour, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={
                  <Space size={4}>
                    {isMobile ? 'Đơn chưa track' : 'Đơn hàng N giờ chưa track'}
                    <Tooltip title="Danh sách tab Đã mua (không gồm hoàn/hủy) quá N giờ chưa có tracking thật. Gửi 1 lần/ngày vào nhóm xử lý đơn, cùng giờ đẩy tổng hợp xử lý đơn.">
                      <InfoCircleOutlined className="settings-info-icon" />
                    </Tooltip>
                  </Space>
                }
                control={
                  <Switch
                    checked={telegramNotifyStaleNoTracking?.value === 'true'}
                    disabled={
                      !canManage || !telegramNotifyStaleNoTracking || updateSetting.isPending
                    }
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(
                        telegramNotifyStaleNoTracking,
                        checked ? 'true' : 'false',
                      )
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Số giờ' : 'Số giờ từ lúc mua hàng'}
                control={
                  <InputNumber
                    min={TELEGRAM_STALE_NO_TRACKING_DEFAULTS.min}
                    max={TELEGRAM_STALE_NO_TRACKING_DEFAULTS.max}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      telegramStaleNoTrackingHours?.value
                        ? Number(telegramStaleNoTrackingHours.value)
                        : TELEGRAM_STALE_NO_TRACKING_DEFAULTS.hours
                    }
                    disabled={
                      !canManage || !telegramStaleNoTrackingHours || updateSetting.isPending
                    }
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < TELEGRAM_STALE_NO_TRACKING_DEFAULTS.min)
                        return;
                      const next = String(
                        Math.min(
                          TELEGRAM_STALE_NO_TRACKING_DEFAULTS.max,
                          Math.max(
                            TELEGRAM_STALE_NO_TRACKING_DEFAULTS.min,
                            Math.floor(parsed),
                          ),
                        ),
                      );
                      if (next === telegramStaleNoTrackingHours?.value) return;
                      void saveSettingValue(telegramStaleNoTrackingHours, next);
                    }}
                    onStep={(val) => {
                      if (val == null) return;
                      const next = String(
                        Math.min(
                          TELEGRAM_STALE_NO_TRACKING_DEFAULTS.max,
                          Math.max(
                            TELEGRAM_STALE_NO_TRACKING_DEFAULTS.min,
                            Math.floor(val),
                          ),
                        ),
                      );
                      void saveSettingValue(telegramStaleNoTrackingHours, next);
                    }}
                  />
                }
              />
            </Space>
          </Card>
        </Col>
        ) : null}

        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={isMobile ? 'Proxy 4Seller' : 'Proxy 4Seller (chống chặn IP 567)'}
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={isMobile ? 10 : 12} style={{ width: '100%' }}>
              <SettingRow
                label={isMobile ? 'Bật pool' : 'Bật pool dự phòng'}
                control={
                  <Switch
                    checked={proxyEnabled?.value === 'true'}
                    disabled={!canManage || !proxyEnabled || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(proxyEnabled, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Cooldown (phút)' : 'Cooldown khi bị chặn (phút)'}
                control={
                  <InputNumber
                    min={1}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      proxyCooldown?.value ? Number(proxyCooldown.value) : undefined
                    }
                    disabled={!canManage || !proxyCooldown || updateSetting.isPending}
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 1) return;
                      const next = String(Math.floor(parsed));
                      if (next === proxyCooldown?.value) return;
                      void saveSettingValue(proxyCooldown, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 1) return;
                      void saveSettingValue(proxyCooldown, String(Math.floor(val)));
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'TTL is-login' : 'TTL cache is-login (phút)'}
                control={
                  <InputNumber
                    min={1}
                    precision={0}
                    style={{ width: numberWidth }}
                    value={
                      proxyValidateTtl?.value ? Number(proxyValidateTtl.value) : undefined
                    }
                    disabled={!canManage || !proxyValidateTtl || updateSetting.isPending}
                    onBlur={(e) => {
                      const parsed = Number(e.target.value);
                      if (!Number.isFinite(parsed) || parsed < 1) return;
                      const next = String(Math.floor(parsed));
                      if (next === proxyValidateTtl?.value) return;
                      void saveSettingValue(proxyValidateTtl, next);
                    }}
                    onStep={(val) => {
                      if (val == null || val < 1) return;
                      void saveSettingValue(proxyValidateTtl, String(Math.floor(val)));
                    }}
                  />
                }
              />
              <div>
                <Text style={{ fontSize: 12 }} type="secondary">
                  Pool dự phòng (chỉ khi bật toggle trên): mỗi dòng 1 proxy ip:port:user:pass
                  (mặc định HTTP CONNECT; thêm https:// cho HTTPS proxy; prefix socks5:// nếu cần). Proxy gắn trên từng tài khoản (trang
                  Người dùng) luôn được dùng khi Check login / sync — không cần bật toggle này. Proxy sticky không bao giờ
                  fallback sang IP trực tiếp (tránh nhảy IP làm chết cookie).
                </Text>
                <Input.TextArea
                  key={`${proxyPool?.id ?? 'pool'}:${proxyPool?.value ?? ''}`}
                  rows={4}
                  defaultValue={proxyPool?.value ?? ''}
                  disabled={!canManage || !proxyPool || updateSetting.isPending}
                  placeholder={'74.0.103.54:50100:kbiproxy:abc123\n74.0.103.55:50100:kbiproxy:abc123'}
                  style={{ marginTop: 6 }}
                  onBlur={(e) => {
                    const next = e.target.value;
                    if (next === (proxyPool?.value ?? '')) return;
                    void saveSettingValue(proxyPool, next);
                  }}
                />
              </div>
              {(!proxyEnabled || !proxyPool || !proxyCooldown || !proxyValidateTtl) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>

        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={
              <Space size={6}>
                <span>{isMobile ? 'Đồng bộ SHEIN' : 'Đồng bộ đơn SHEIN (Extension)'}</span>
                <Tooltip title="Extension tự chạy Sync Order theo chu kỳ khi panel đang mở trên tab SHEIN.">
                  <InfoCircleOutlined className="settings-info-icon" />
                </Tooltip>
              </Space>
            }
            extra={
              <Tooltip
                title={
                  sheinSyncEnabled?.value === 'true'
                    ? 'Tắt tự động đồng bộ'
                    : 'Bật tự động đồng bộ'
                }
              >
                <Switch
                  size="small"
                  checked={sheinSyncEnabled?.value === 'true'}
                  disabled={!canManage || !sheinSyncEnabled || updateSetting.isPending}
                  loading={updateSetting.isPending}
                  onChange={(checked) =>
                    void saveSettingValue(sheinSyncEnabled, checked ? 'true' : 'false')
                  }
                />
              </Tooltip>
            }
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={8} style={{ width: '100%' }}>
              <SettingRow
                label="Chu kỳ"
                control={
                  <Space.Compact>
                    <InputNumber
                      min={1}
                      precision={0}
                      style={{ width: isMobile ? 72 : 88 }}
                      value={
                        sheinSyncIntervalValue?.value
                          ? Number(sheinSyncIntervalValue.value)
                          : undefined
                      }
                      disabled={
                        !canManage || !sheinSyncIntervalValue || updateSetting.isPending
                      }
                      onBlur={(e) => {
                        const parsed = Number(e.target.value);
                        if (!Number.isFinite(parsed) || parsed < 1) return;
                        const next = String(Math.floor(parsed));
                        if (next === sheinSyncIntervalValue?.value) return;
                        void saveSettingValue(sheinSyncIntervalValue, next);
                      }}
                      onStep={(val) => {
                        if (val == null || val < 1) return;
                        void saveSettingValue(
                          sheinSyncIntervalValue,
                          String(Math.floor(val)),
                        );
                      }}
                    />
                    <Select
                      style={{ width: 84 }}
                      value={sheinSyncIntervalUnit?.value ?? EXTENSION_SHEIN_SYNC_UNITS.seconds}
                      disabled={
                        !canManage || !sheinSyncIntervalUnit || updateSetting.isPending
                      }
                      options={[
                        { value: EXTENSION_SHEIN_SYNC_UNITS.seconds, label: 'Giây' },
                        { value: EXTENSION_SHEIN_SYNC_UNITS.minutes, label: 'Phút' },
                      ]}
                      onChange={(val) => void saveSettingValue(sheinSyncIntervalUnit, val)}
                    />
                  </Space.Compact>
                }
              />
              <SettingRow
                label="Lookback"
                control={
                  <Tooltip title="Cửa sổ ngày (orderedAt list SHEIN + pool TikTok khi chế độ Cập nhật tất cả).">
                    <Space.Compact style={{ width: isMobile ? 120 : 140 }}>
                      <InputNumber
                        min={EXTENSION_SHEIN_SYNC_REFRESH_LOOKBACK.min}
                        max={EXTENSION_SHEIN_SYNC_REFRESH_LOOKBACK.max}
                        precision={0}
                        style={{ width: '100%' }}
                        value={
                          sheinSyncRefreshLookback?.value
                            ? Number(sheinSyncRefreshLookback.value)
                            : EXTENSION_SHEIN_SYNC_REFRESH_LOOKBACK.default
                        }
                        disabled={
                          !canManage || !sheinSyncRefreshLookback || updateSetting.isPending
                        }
                        onBlur={(e) => {
                          const parsed = Number(e.target.value);
                          if (!Number.isFinite(parsed)) return;
                          const next = String(
                            Math.min(
                              EXTENSION_SHEIN_SYNC_REFRESH_LOOKBACK.max,
                              Math.max(
                                EXTENSION_SHEIN_SYNC_REFRESH_LOOKBACK.min,
                                Math.floor(parsed),
                              ),
                            ),
                          );
                          if (next === sheinSyncRefreshLookback?.value) return;
                          void saveSettingValue(sheinSyncRefreshLookback, next);
                        }}
                        onStep={(val) => {
                          if (val == null) return;
                          const next = String(
                            Math.min(
                              EXTENSION_SHEIN_SYNC_REFRESH_LOOKBACK.max,
                              Math.max(
                                EXTENSION_SHEIN_SYNC_REFRESH_LOOKBACK.min,
                                Math.floor(val),
                              ),
                            ),
                          );
                          void saveSettingValue(sheinSyncRefreshLookback, next);
                        }}
                      />
                      <Button disabled tabIndex={-1}>
                        ngày
                      </Button>
                    </Space.Compact>
                  </Tooltip>
                }
              />
              <SettingRow
                label="Chế độ cập nhật"
                control={
                  <Tooltip
                    title={
                      'Chỉ áp dụng cho nút Sync Order trên extension — Auto Sync luôn chạy như "Chỉ đơn mới" ' +
                      '(list nông ~20 đơn / 1 trang, cứ 10 chu kỳ full Lookback). ' +
                      'Chỉ đơn mới = kéo list SHEIN trong Lookback + gắn đơn mới / cập nhật tracking·status·cost khi đổi trên pool fulfil. ' +
                      'Cập nhật tất cả = kéo hết list SHEIN, khớp lại mọi đơn trong cửa sổ lead bằng phone+zip+tên, ' +
                      'thu hồi mã NCC đang gắn sai và ghi đè giá/tracking/status bằng dữ liệu SHEIN mới.'
                    }
                  >
                    <Select
                      style={{ width: isMobile ? 160 : 220 }}
                      value={sheinSyncUpdateMode?.value ?? EXTENSION_SHEIN_SYNC_UPDATE_MODES.new}
                      disabled={!canManage || !sheinSyncUpdateMode || updateSetting.isPending}
                      options={[
                        {
                          value: EXTENSION_SHEIN_SYNC_UPDATE_MODES.new,
                          label: 'Chỉ đơn mới (fulfil)',
                        },
                        {
                          value: EXTENSION_SHEIN_SYNC_UPDATE_MODES.all,
                          label: 'Cập nhật tất cả (Lookback)',
                        },
                      ]}
                      onChange={(val) => void saveSettingValue(sheinSyncUpdateMode, val)}
                    />
                  </Tooltip>
                }
              />
              <SettingRow
                label="Số luồng"
                control={
                  <Tooltip title="Số request get_order_info chạy song song khi Sync Order (1–20)">
                    <InputNumber
                      min={EXTENSION_SHEIN_SYNC_CONCURRENT_THREADS.min}
                      max={EXTENSION_SHEIN_SYNC_CONCURRENT_THREADS.max}
                      precision={0}
                      style={{ width: isMobile ? 72 : 88 }}
                      value={
                        sheinSyncConcurrentThreads?.value
                          ? Number(sheinSyncConcurrentThreads.value)
                          : undefined
                      }
                      disabled={
                        !canManage || !sheinSyncConcurrentThreads || updateSetting.isPending
                      }
                      onBlur={(e) => {
                        const parsed = Number(e.target.value);
                        if (!Number.isFinite(parsed)) return;
                        const next = String(
                          Math.min(
                            EXTENSION_SHEIN_SYNC_CONCURRENT_THREADS.max,
                            Math.max(
                              EXTENSION_SHEIN_SYNC_CONCURRENT_THREADS.min,
                              Math.floor(parsed),
                            ),
                          ),
                        );
                        if (next === sheinSyncConcurrentThreads?.value) return;
                        void saveSettingValue(sheinSyncConcurrentThreads, next);
                      }}
                      onStep={(val) => {
                        if (val == null) return;
                        const next = String(
                          Math.min(
                            EXTENSION_SHEIN_SYNC_CONCURRENT_THREADS.max,
                            Math.max(
                              EXTENSION_SHEIN_SYNC_CONCURRENT_THREADS.min,
                              Math.floor(val),
                            ),
                          ),
                        );
                        void saveSettingValue(sheinSyncConcurrentThreads, next);
                      }}
                    />
                  </Tooltip>
                }
              />
              {(!sheinSyncEnabled ||
                !sheinSyncIntervalValue ||
                !sheinSyncIntervalUnit ||
                !sheinSyncUpdateMode ||
                !sheinSyncConcurrentThreads ||
                !sheinSyncRefreshLookback) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>
        ) : null}

        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={
              <Space size={6}>
                <span>{isMobile ? 'Chi tiết SHEIN' : 'Auto Sync Order detail SHEIN'}</span>
                <Tooltip
                  title={
                    'Job nền trên API server: mỗi chu kỳ snapshot toàn bộ đơn còn thiếu mã vận đơn thật hoặc chưa Mark Ship, ' +
                    'gọi get_order_info theo lô Billno/lượt (cookie + header extension trên tài khoản mua hàng), ' +
                    'Mark Ship khi có mã carrier, rồi mới nghỉ chu kỳ. Không cần mở trình duyệt hay extension.'
                  }
                >
                  <InfoCircleOutlined className="settings-info-icon" />
                </Tooltip>
              </Space>
            }
            extra={
              <Tooltip
                title={
                  sheinDetailSyncEnabled?.value === 'true'
                    ? 'Tắt quét chi tiết phía backend'
                    : 'Bật quét chi tiết phía backend'
                }
              >
                <Switch
                  size="small"
                  checked={sheinDetailSyncEnabled?.value === 'true'}
                  disabled={!canManage || !sheinDetailSyncEnabled || updateSetting.isPending}
                  loading={updateSetting.isPending}
                  onChange={(checked) =>
                    void saveSettingValue(sheinDetailSyncEnabled, checked ? 'true' : 'false')
                  }
                />
              </Tooltip>
            }
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={8} style={{ width: '100%' }}>
              <SettingRow
                label="Chu kỳ (phút)"
                control={
                  <Tooltip
                    title={`Nghỉ sau khi quét hết danh sách ứng viên trước khi snapshot chu kỳ mới (${SHEIN_DETAIL_SYNC_INTERVAL.min}–${SHEIN_DETAIL_SYNC_INTERVAL.max} phút)`}
                  >
                    {clampedNumberControl(sheinDetailSyncInterval, SHEIN_DETAIL_SYNC_INTERVAL)}
                  </Tooltip>
                }
              />
              <SettingRow
                label="Billno / lượt"
                control={
                  <Tooltip
                    title={`Kích thước mỗi lô trong một chu kỳ (${SHEIN_DETAIL_SYNC_MAX_BILLNOS.min}–${SHEIN_DETAIL_SYNC_MAX_BILLNOS.max}). Chu kỳ vẫn xử lý hết snapshot; lô chỉ giới hạn song song/rate`}
                  >
                    {clampedNumberControl(
                      sheinDetailSyncMaxBillnos,
                      SHEIN_DETAIL_SYNC_MAX_BILLNOS,
                    )}
                  </Tooltip>
                }
              />
              <SettingRow
                label="Mark Ship / lượt"
                control={
                  <Tooltip
                    title={`Trần lần gọi 4Seller Mark Ship mỗi lô Billno (${SHEIN_DETAIL_SYNC_MAX_MARK_SHIP.min}–${SHEIN_DETAIL_SYNC_MAX_MARK_SHIP.max}). Reset mỗi lô; job nền không bị Cloudflare cắt request`}
                  >
                    {clampedNumberControl(
                      sheinDetailSyncMaxMarkShip,
                      SHEIN_DETAIL_SYNC_MAX_MARK_SHIP,
                    )}
                  </Tooltip>
                }
              />
              <SettingRow
                label={isMobile ? 'Báo Add track' : 'Thông báo Add track'}
                control={
                  <Tooltip title="Gửi Telegram khi lần đầu có mã vận đơn thật cho một đơn, kèm kết quả Mark Ship (thành công/thất bại). Gửi sau khi Mark Ship đã chạy">
                    <Switch
                      checked={sheinDetailSyncNotify?.value === 'true'}
                      disabled={!canManage || !sheinDetailSyncNotify || updateSetting.isPending}
                      {...switchProps}
                      onChange={(checked) =>
                        void saveSettingValue(sheinDetailSyncNotify, checked ? 'true' : 'false')
                      }
                    />
                  </Tooltip>
                }
              />
              <SettingRow
                label={isMobile ? 'Chat Add track' : 'Chat ID Add track'}
                control={
                  <Space size={6} wrap={false}>
                    <Input
                      style={{ width: isMobile ? 120 : 160 }}
                      placeholder="-1004462111161"
                      key={`detail-track-chat:${sheinDetailSyncChatId?.id}:${sheinDetailSyncChatId?.value ?? ''}`}
                      defaultValue={sheinDetailSyncChatId?.value ?? ''}
                      disabled={!canManage || !sheinDetailSyncChatId || updateSetting.isPending}
                      onBlur={(e) => {
                        const next = e.target.value.trim();
                        if (next === (sheinDetailSyncChatId?.value ?? '')) return;
                        void saveSettingValue(sheinDetailSyncChatId, next);
                      }}
                    />
                    <Tooltip title="Gửi tin TEST dạng Add track vào Chat ID (không cần đơn thật)">
                      <Button
                        type="default"
                        size="small"
                        icon={<SendOutlined />}
                        loading={testAddTrackLoading}
                        disabled={!canManage || !sheinDetailSyncChatId || testAddTrackLoading}
                        onClick={() => void sendAddTrackTest()}
                      >
                        {isMobile ? 'Thử' : 'Gửi thử'}
                      </Button>
                    </Tooltip>
                  </Space>
                }
              />
              <Text type="secondary" style={{ fontSize: 12 }}>
                Cần tài khoản mua hàng có session SHEIN sẵn sàng và proxy cùng đường ra với
                trình duyệt chạy extension.
              </Text>
              {(!sheinDetailSyncEnabled ||
                !sheinDetailSyncInterval ||
                !sheinDetailSyncMaxBillnos ||
                !sheinDetailSyncMaxMarkShip ||
                !sheinDetailSyncNotify ||
                !sheinDetailSyncChatId) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>
        ) : null}

        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={
              <Space size={6}>
                <span>{isMobile ? 'Đồng bộ TTS' : 'Đồng bộ TikTok Shop (Extension)'}</span>
                <Tooltip title="Extension Auto Sync scrape seller-us + PUT profile theo chu kỳ (chrome.alarms). Cảnh báo Telegram khi email/mobile/bank đổi.">
                  <InfoCircleOutlined className="settings-info-icon" />
                </Tooltip>
              </Space>
            }
            extra={
              <Tooltip
                title={
                  tiktokShopSyncEnabled?.value === 'true'
                    ? 'Tắt tự động đồng bộ'
                    : 'Bật tự động đồng bộ'
                }
              >
                <Switch
                  size="small"
                  checked={tiktokShopSyncEnabled?.value === 'true'}
                  disabled={!canManage || !tiktokShopSyncEnabled || updateSetting.isPending}
                  loading={updateSetting.isPending}
                  onChange={(checked) =>
                    void saveSettingValue(tiktokShopSyncEnabled, checked ? 'true' : 'false')
                  }
                />
              </Tooltip>
            }
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={8} style={{ width: '100%' }}>
              <SettingRow
                label="Chu kỳ"
                control={
                  <Space.Compact>
                    <InputNumber
                      min={1}
                      precision={0}
                      style={{ width: isMobile ? 72 : 88 }}
                      value={
                        tiktokShopSyncIntervalValue?.value
                          ? Number(tiktokShopSyncIntervalValue.value)
                          : undefined
                      }
                      disabled={
                        !canManage || !tiktokShopSyncIntervalValue || updateSetting.isPending
                      }
                      onBlur={(e) => {
                        const parsed = Number(e.target.value);
                        if (!Number.isFinite(parsed) || parsed < 1) return;
                        const next = String(Math.floor(parsed));
                        if (next === tiktokShopSyncIntervalValue?.value) return;
                        void saveSettingValue(tiktokShopSyncIntervalValue, next);
                      }}
                      onStep={(val) => {
                        if (val == null || val < 1) return;
                        void saveSettingValue(
                          tiktokShopSyncIntervalValue,
                          String(Math.floor(val)),
                        );
                      }}
                    />
                    <Select
                      style={{ width: 84 }}
                      value={
                        tiktokShopSyncIntervalUnit?.value ??
                        EXTENSION_TIKTOK_SHOP_SYNC_UNITS.seconds
                      }
                      disabled={
                        !canManage || !tiktokShopSyncIntervalUnit || updateSetting.isPending
                      }
                      options={[
                        { value: EXTENSION_TIKTOK_SHOP_SYNC_UNITS.seconds, label: 'Giây' },
                        { value: EXTENSION_TIKTOK_SHOP_SYNC_UNITS.minutes, label: 'Phút' },
                      ]}
                      onChange={(val) => void saveSettingValue(tiktokShopSyncIntervalUnit, val)}
                    />
                  </Space.Compact>
                }
              />
              <SettingRow
                label={isMobile ? 'Đổi email' : 'Cảnh báo đổi email'}
                control={
                  <Switch
                    checked={tiktokShopNotifyEmail?.value === 'true'}
                    disabled={!canManage || !tiktokShopNotifyEmail || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(tiktokShopNotifyEmail, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Đổi mobile' : 'Cảnh báo đổi mobile'}
                control={
                  <Switch
                    checked={tiktokShopNotifyMobile?.value === 'true'}
                    disabled={!canManage || !tiktokShopNotifyMobile || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(tiktokShopNotifyMobile, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Đổi bank' : 'Cảnh báo đổi bank'}
                control={
                  <Switch
                    checked={tiktokShopNotifyBank?.value === 'true'}
                    disabled={!canManage || !tiktokShopNotifyBank || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(tiktokShopNotifyBank, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Bank ≠ hệ thống' : 'Bank không khớp hệ thống'}
                control={
                  <Tooltip title="Đuôi 4 số settlement bank không khớp ReceivingBankAccounts nào">
                    <Switch
                      checked={tiktokShopNotifyBankLast4?.value === 'true'}
                      disabled={
                        !canManage || !tiktokShopNotifyBankLast4 || updateSetting.isPending
                      }
                      {...switchProps}
                      onChange={(checked) =>
                        void saveSettingValue(
                          tiktokShopNotifyBankLast4,
                          checked ? 'true' : 'false',
                        )
                      }
                    />
                  </Tooltip>
                }
              />
              <SettingRow
                label={isMobile ? 'Đổi trạng thái' : 'Cảnh báo đổi trạng thái shop'}
                control={
                  <Tooltip title="Ops Status đổi (Active / Deactivated / Suspended) — từ TTS sync hoặc admin/seller chỉnh tay">
                    <Switch
                      checked={tiktokShopNotifyStatusChange?.value === 'true'}
                      disabled={
                        !canManage || !tiktokShopNotifyStatusChange || updateSetting.isPending
                      }
                      {...switchProps}
                      onChange={(checked) =>
                        void saveSettingValue(
                          tiktokShopNotifyStatusChange,
                          checked ? 'true' : 'false',
                        )
                      }
                    />
                  </Tooltip>
                }
              />
              <SettingRow
                label={isMobile ? 'Chat ID' : 'Chat ID cảnh báo'}
                control={
                  <Input
                    style={{ width: isMobile ? 140 : 180 }}
                    placeholder="-1004331688585"
                    key={`tts-chat:${tiktokShopTelegramChatId?.id}:${tiktokShopTelegramChatId?.value ?? ''}`}
                    defaultValue={tiktokShopTelegramChatId?.value ?? ''}
                    disabled={!canManage || !tiktokShopTelegramChatId || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = e.target.value.trim();
                      if (next === (tiktokShopTelegramChatId?.value ?? '')) return;
                      void saveSettingValue(tiktokShopTelegramChatId, next);
                    }}
                  />
                }
              />
              {(!tiktokShopSyncEnabled ||
                !tiktokShopSyncIntervalValue ||
                !tiktokShopSyncIntervalUnit ||
                !tiktokShopNotifyEmail ||
                !tiktokShopNotifyMobile ||
                !tiktokShopNotifyBank ||
                !tiktokShopNotifyBankLast4 ||
                !tiktokShopNotifyStatusChange ||
                !tiktokShopTelegramChatId) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>

        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={
              <Space size={6}>
                <span>{isMobile ? 'Payout TTS' : 'Đồng bộ payout TTS (Extension)'}</span>
                <Tooltip title="Extension Sync paid / Auto Sync scrape payment list seller-us → ledger ReceivingBankTransaction (TikTokPayout) + Telegram khi có thanh toán mới.">
                  <InfoCircleOutlined className="settings-info-icon" />
                </Tooltip>
              </Space>
            }
            extra={
              <Tooltip
                title={
                  tiktokPayoutSyncEnabled?.value === 'true'
                    ? 'Tắt tự động đồng bộ payout'
                    : 'Bật tự động đồng bộ payout'
                }
              >
                <Switch
                  size="small"
                  checked={tiktokPayoutSyncEnabled?.value === 'true'}
                  disabled={!canManage || !tiktokPayoutSyncEnabled || updateSetting.isPending}
                  loading={updateSetting.isPending}
                  onChange={(checked) =>
                    void saveSettingValue(tiktokPayoutSyncEnabled, checked ? 'true' : 'false')
                  }
                />
              </Tooltip>
            }
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={8} style={{ width: '100%' }}>
              <SettingRow
                label="Chu kỳ"
                control={
                  <Space.Compact>
                    <InputNumber
                      min={1}
                      precision={0}
                      style={{ width: isMobile ? 72 : 88 }}
                      value={
                        tiktokPayoutSyncIntervalValue?.value
                          ? Number(tiktokPayoutSyncIntervalValue.value)
                          : undefined
                      }
                      disabled={
                        !canManage || !tiktokPayoutSyncIntervalValue || updateSetting.isPending
                      }
                      onBlur={(e) => {
                        const parsed = Number(e.target.value);
                        if (!Number.isFinite(parsed) || parsed < 1) return;
                        const next = String(Math.floor(parsed));
                        if (next === tiktokPayoutSyncIntervalValue?.value) return;
                        void saveSettingValue(tiktokPayoutSyncIntervalValue, next);
                      }}
                      onStep={(val) => {
                        if (val == null || val < 1) return;
                        void saveSettingValue(
                          tiktokPayoutSyncIntervalValue,
                          String(Math.floor(val)),
                        );
                      }}
                    />
                    <Select
                      style={{ width: 84 }}
                      value={
                        tiktokPayoutSyncIntervalUnit?.value ??
                        EXTENSION_TIKTOK_PAYOUT_SYNC_UNITS.seconds
                      }
                      disabled={
                        !canManage || !tiktokPayoutSyncIntervalUnit || updateSetting.isPending
                      }
                      options={[
                        { value: EXTENSION_TIKTOK_PAYOUT_SYNC_UNITS.seconds, label: 'Giây' },
                        { value: EXTENSION_TIKTOK_PAYOUT_SYNC_UNITS.minutes, label: 'Phút' },
                      ]}
                      onChange={(val) => void saveSettingValue(tiktokPayoutSyncIntervalUnit, val)}
                    />
                  </Space.Compact>
                }
              />
              <SettingRow
                label={isMobile ? 'Max date' : 'Ngày tối thiểu'}
                control={
                  <Input
                    style={{ width: isMobile ? 140 : 160 }}
                    placeholder="2026-07-01"
                    key={`tts-payout-max:${tiktokPayoutSyncMaxDate?.id}:${tiktokPayoutSyncMaxDate?.value ?? ''}`}
                    defaultValue={tiktokPayoutSyncMaxDate?.value ?? ''}
                    disabled={!canManage || !tiktokPayoutSyncMaxDate || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = e.target.value.trim();
                      if (next === (tiktokPayoutSyncMaxDate?.value ?? '')) return;
                      void saveSettingValue(tiktokPayoutSyncMaxDate, next);
                    }}
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'TG thanh toán' : 'Thông báo thanh toán mới'}
                control={
                  <Switch
                    checked={tiktokPayoutNotifyNew?.value === 'true'}
                    disabled={!canManage || !tiktokPayoutNotifyNew || updateSetting.isPending}
                    {...switchProps}
                    onChange={(checked) =>
                      void saveSettingValue(tiktokPayoutNotifyNew, checked ? 'true' : 'false')
                    }
                  />
                }
              />
              <SettingRow
                label={isMobile ? 'Chat ID' : 'Chat ID cảnh báo'}
                control={
                  <Input
                    style={{ width: isMobile ? 140 : 180 }}
                    placeholder="-1004331688585"
                    key={`tts-payout-chat:${tiktokPayoutTelegramChatId?.id}:${tiktokPayoutTelegramChatId?.value ?? ''}`}
                    defaultValue={tiktokPayoutTelegramChatId?.value ?? ''}
                    disabled={!canManage || !tiktokPayoutTelegramChatId || updateSetting.isPending}
                    onBlur={(e) => {
                      const next = e.target.value.trim();
                      if (next === (tiktokPayoutTelegramChatId?.value ?? '')) return;
                      void saveSettingValue(tiktokPayoutTelegramChatId, next);
                    }}
                  />
                }
              />
              {(!tiktokPayoutSyncEnabled ||
                !tiktokPayoutSyncIntervalValue ||
                !tiktokPayoutSyncIntervalUnit ||
                !tiktokPayoutSyncMaxDate ||
                !tiktokPayoutNotifyNew ||
                !tiktokPayoutTelegramChatId) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>
        ) : null}

        {SHOW_TEMP_HIDDEN_SETTING_CARDS ? (
        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={
              <Space size={6}>
                <span>{isMobile ? 'Keywords TTS' : 'Đồng bộ keywords TTS (Extension)'}</span>
                <Tooltip title="Extension Đồng bộ keywords / Auto Sync scrape trending keywords seller-us theo niche catalog của shop → snapshot Product opportunities.">
                  <InfoCircleOutlined className="settings-info-icon" />
                </Tooltip>
              </Space>
            }
            extra={
              <Tooltip
                title={
                  tiktokOpportunitySyncEnabled?.value === 'true'
                    ? 'Tắt tự động đồng bộ keywords'
                    : 'Bật tự động đồng bộ keywords'
                }
              >
                <Switch
                  size="small"
                  checked={tiktokOpportunitySyncEnabled?.value === 'true'}
                  disabled={!canManage || !tiktokOpportunitySyncEnabled || updateSetting.isPending}
                  loading={updateSetting.isPending}
                  onChange={(checked) =>
                    void saveSettingValue(tiktokOpportunitySyncEnabled, checked ? 'true' : 'false')
                  }
                />
              </Tooltip>
            }
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={8} style={{ width: '100%' }}>
              <SettingRow
                label="Chu kỳ"
                control={
                  <Space.Compact>
                    <InputNumber
                      min={1}
                      precision={0}
                      style={{ width: isMobile ? 72 : 88 }}
                      value={
                        tiktokOpportunitySyncIntervalValue?.value
                          ? Number(tiktokOpportunitySyncIntervalValue.value)
                          : undefined
                      }
                      disabled={
                        !canManage ||
                        !tiktokOpportunitySyncIntervalValue ||
                        updateSetting.isPending
                      }
                      onBlur={(e) => {
                        const parsed = Number(e.target.value);
                        if (!Number.isFinite(parsed) || parsed < 1) return;
                        const next = String(Math.floor(parsed));
                        if (next === tiktokOpportunitySyncIntervalValue?.value) return;
                        void saveSettingValue(tiktokOpportunitySyncIntervalValue, next);
                      }}
                      onStep={(val) => {
                        if (val == null || val < 1) return;
                        void saveSettingValue(
                          tiktokOpportunitySyncIntervalValue,
                          String(Math.floor(val)),
                        );
                      }}
                    />
                    <Select
                      style={{ width: 84 }}
                      value={
                        tiktokOpportunitySyncIntervalUnit?.value ??
                        EXTENSION_TIKTOK_OPPORTUNITY_SYNC_UNITS.minutes
                      }
                      disabled={
                        !canManage ||
                        !tiktokOpportunitySyncIntervalUnit ||
                        updateSetting.isPending
                      }
                      options={[
                        { value: EXTENSION_TIKTOK_OPPORTUNITY_SYNC_UNITS.seconds, label: 'Giây' },
                        { value: EXTENSION_TIKTOK_OPPORTUNITY_SYNC_UNITS.minutes, label: 'Phút' },
                      ]}
                      onChange={(val) =>
                        void saveSettingValue(tiktokOpportunitySyncIntervalUnit, val)
                      }
                    />
                  </Space.Compact>
                }
              />
              {(!tiktokOpportunitySyncEnabled ||
                !tiktokOpportunitySyncIntervalValue ||
                !tiktokOpportunitySyncIntervalUnit) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>
        ) : null}

        <Col xs={24} md={12} xl={8}>
          <Card
            size="small"
            title={
              <Space size={6}>
                <span>{isMobile ? 'Sync All TTS' : 'Sync All TTS (Extension)'}</span>
                <Tooltip title="Bật/tắt từng bước khi bấm Sync All hoặc Sync Fast trên extension. Không ảnh hưởng Auto Sync theo chu kỳ. Thiếu seed → coi như bật. Tắt Info/Finance/Keywords/Analytics khiến nhiệm vụ «Đồng bộ» trên board không đủ 4 log completed trong ngày.">
                  <InfoCircleOutlined className="settings-info-icon" />
                </Tooltip>
              </Space>
            }
            style={{ height: '100%', borderRadius: 6 }}
          >
            <Space direction="vertical" size={8} style={{ width: '100%' }}>
              {unifiedSyncSteps.map(({ key, setting }) => (
                <SettingRow
                  key={key}
                  label={EXTENSION_TIKTOK_UNIFIED_SYNC_STEP_LABELS[key]}
                  control={
                    <Switch
                      /* Thiếu seed: API fail-open = bật — UI phải cùng trạng thái, không hiện Tắt giả. */
                      checked={!setting || setting.value === 'true'}
                      disabled={!canManage || !setting || updateSetting.isPending}
                      {...switchProps}
                      onChange={(checked) =>
                        void saveSettingValue(setting, checked ? 'true' : 'false')
                      }
                    />
                  }
                />
              ))}
              {unifiedSyncSteps.some(({ setting }) => !setting) && (
                <Text type="warning" style={{ fontSize: isMobile ? 12 : undefined }}>
                  {isMobile
                    ? 'Thiếu seed Settings — restart API rồi tải lại.'
                    : 'Chưa có seed Settings. Restart API để chạy DataSeeder, rồi Tải lại trang này.'}
                </Text>
              )}
            </Space>
          </Card>
        </Col>
      </Row>

      <Space wrap style={{ marginBottom: 16, width: isMobile ? '100%' : undefined }}>
        <Select
          allowClear
          showSearch
          placeholder="Lọc theo nhóm"
          style={{ width: isMobile ? '100%' : 240 }}
          options={groupOptions}
          value={groupFilter}
          optionFilterProp="label"
          onChange={(value) => setGroupFilter(value)}
        />
      </Space>

      <Table
        rowKey="id"
        columns={visibleColumns}
        dataSource={filteredSettings}
        loading={isLoading || isFetching}
        size="small"
        scroll={isMobile ? undefined : { x: 960 }}
        pagination={tablePagination(filteredSettings.length, {
          size: 'small',
          simple: isMobile,
          pageSize: pagination.pageSize,
          current: pagination.current,
          showSizeChanger: !isMobile,
          onChange: (page, pageSize) => setPagination({ current: page, pageSize }),
        })}
      />

      <Modal
        className="settings-edit-modal"
        title={editingSetting ? 'Sửa cấu hình' : 'Thêm cấu hình'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={handleSubmit}
        confirmLoading={createSetting.isPending || updateSetting.isPending}
        destroyOnHidden
        width={isMobile ? '100%' : 560}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            name="group"
            label="Nhóm cấu hình"
            rules={[{ required: true, message: 'Nhập nhóm cấu hình' }]}
          >
            <Select
              mode="tags"
              maxCount={1}
              placeholder="VD: Bunny Config"
              options={groupOptions}
            />
          </Form.Item>
          <Form.Item
            name="key"
            label="Key"
            rules={[
              { required: true, message: 'Nhập key' },
              { pattern: /^[A-Za-z0-9_.-]+$/, message: 'Key chỉ dùng chữ, số, dấu chấm, gạch ngang và gạch dưới' },
            ]}
          >
            <Input placeholder="VD: bunny.access_key" disabled={!!editingSetting} />
          </Form.Item>
          <Form.Item
            name="label"
            label="Tên hiển thị"
            rules={[{ required: true, message: 'Nhập tên hiển thị' }]}
          >
            <Input placeholder="VD: Access Key" />
          </Form.Item>
          <Form.Item
            name="type"
            label="Loại giá trị"
            rules={[{ required: true, message: 'Chọn loại giá trị' }]}
          >
            <Select
              options={SETTING_TYPE_OPTIONS}
              onChange={() => form.setFieldValue('value', '')}
            />
          </Form.Item>
          {renderValueField()}
          <Form.Item name="description" label="Mô tả">
            <Input.TextArea rows={2} placeholder="Mô tả ngắn (tùy chọn)" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};
