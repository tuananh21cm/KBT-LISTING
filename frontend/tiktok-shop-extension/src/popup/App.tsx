import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { BRAND_NAME } from '../shared/config';
import type { LoginResponse, StateResponse } from '../shared/messages';
import {
  formatBusinessTypeLabel,
  formatCreateTimeVn,
  formatLogisticsService,
  formatPublishLimit,
  formatSellerType,
  formatShopAddress,
  formatShopEntityType,
  formatShopStatus,
  formatUsdAmount,
  resolveDisplayShopStatus,
  shopStatusBadgeClass,
} from '../shared/shop';
import { applyDocumentTheme, normalizeTheme, type ThemeMode } from '../shared/theme';
import type {
  SellerUsSessionStatus,
  SellerUsTabSnapshot,
  ShopDailyTaskPlan,
  TikTokShopInfo,
  UnifiedSyncState,
  UnifiedSyncStepState,
  UnifiedSyncStepStatus,
} from '../shared/types';
import { formatOnlineDuration } from '../shared/onlineSession';
import { send } from './messaging';
import { useOnlineTodayCounter } from './useOnlineTodayCounter';
import { useTaskDwellMap } from './useTaskDwellMap';
import { useTaskNavigation } from './useTaskNavigation';
import { useSellerUsTabSnapshot } from './useSellerUsTabSnapshot';
import { dwellRemainingSeconds, formatDwellCountdown } from '../shared/taskDwell';
import { isTaskNavigating, resolveNavigatingTaskId } from '../shared/taskPageState';
import {
  formatDailyTaskSyncBlockMessage,
  formatDailyTaskSyncUnlockHint,
  getDailyTaskProgress,
} from '../shared/dailyTaskProgress';
import { matchesTaskUrl } from '../shared/taskUrlMatch';

const LOGO_URL = chrome.runtime.getURL('icons/icon-128.png');
const BOOT_TIMEOUT_MS = 8000;
const SELLER_US_URL = 'https://seller-us.tiktok.com/';

function Spinner({ dark }: { dark?: boolean }) {
  return <span className={`spinner${dark ? ' spinner--dark' : ''}`} aria-hidden />;
}

function formatStepTime(at?: number): string {
  if (!at) return '';
  return new Date(at).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

function stepStatusLabel(status: UnifiedSyncStepStatus): string {
  switch (status) {
    case 'pending':
      return 'Chờ';
    case 'running':
      return 'Đang chạy';
    case 'completed':
      return 'OK';
    case 'failed':
      return 'Lỗi';
    case 'skipped':
      return 'Bỏ qua';
    default:
      return status;
  }
}

function UnifiedSyncChecklist({ sync }: { sync: UnifiedSyncState | null | undefined }) {
  if (!sync || sync.phase === 'idle') return null;

  const rows: { key: keyof UnifiedSyncState; label: string; step: UnifiedSyncStepState }[] = [
    { key: 'profile', label: 'Info', step: sync.profile },
    { key: 'payout', label: 'Finance', step: sync.payout },
    { key: 'opportunity', label: 'Keywords', step: sync.opportunity },
    { key: 'analytics', label: 'Analytics', step: sync.analytics },
    {
      key: 'healthCenter',
      label: 'Health Center',
      step: sync.healthCenter ?? { status: 'pending' },
    },
    {
      key: 'shopScore',
      label: 'Shop Score',
      step: sync.shopScore ?? { status: 'pending' },
    },
    // State cũ trong storage chưa có bước này — giữ fallback để side panel không vỡ sau khi nâng cấp.
    { key: 'tasks', label: 'Nhiệm vụ', step: sync.tasks ?? { status: 'pending' } },
  ];

  const visibleRows =
    sync.mode === 'fast'
      ? rows.filter(({ key }) => key === 'payout' || key === 'healthCenter' || key === 'shopScore')
      : rows;

  return (
    <ul className="unified-sync-checklist" aria-live="polite">
      {visibleRows.map(({ key, label, step }) => (
        <li key={key} className={`unified-sync-item unified-sync-item--${step.status}`}>
          <span className="unified-sync-label">{label}</span>
          <span className="unified-sync-status">{stepStatusLabel(step.status)}</span>
          {step.at ? <span className="unified-sync-time">{formatStepTime(step.at)}</span> : null}
          {step.message ? <span className="unified-sync-msg">{step.message}</span> : null}
        </li>
      ))}
    </ul>
  );
}

/**
 * Nhiệm vụ truy cập URL của hôm nay.
 *
 * Bấm một dòng chỉ **điều hướng** tab seller-us tới trang đó; nhiệm vụ được tính hoàn thành khi
 * trang thực sự mở, y hệt như khi người dùng tự bấm menu trong Seller Center. Checkbox **Auto**
 * (opt-in) để background tự mở lần lượt rồi Sync All — vẫn chỉ navigate + quan sát, không thao tác
 * thay trên trang TikTok.
 */
function DailyTaskList({
  plan,
  busyTaskId,
  taskNavigation,
  taskDwells,
  tabSnapshot,
  autoEnabled,
  onAutoChange,
  onOpen,
}: {
  plan: ShopDailyTaskPlan | null | undefined;
  busyTaskId: string | null;
  taskNavigation: ReturnType<typeof useTaskNavigation>;
  taskDwells: ReturnType<typeof useTaskDwellMap>;
  tabSnapshot: SellerUsTabSnapshot | null;
  autoEnabled: boolean;
  onAutoChange: (enabled: boolean) => void;
  onOpen: (task: { id: string; url: string }) => void;
}) {
  const [, tick] = useState(0);

  const hasActiveDwell = Object.keys(taskDwells).length > 0;
  const hasDurationTasks =
    (plan?.items ?? []).some((task) => !task.done && (task.viewDurationSeconds ?? 0) > 0);

  useEffect(() => {
    if (!hasActiveDwell && !hasDurationTasks) return;
    const timer = window.setInterval(() => tick((n) => n + 1), 1000);
    return () => window.clearInterval(timer);
  }, [hasActiveDwell, hasDurationTasks]);

  if (!plan || plan.items.length === 0) return null;

  const navigatingTaskId = resolveNavigatingTaskId(plan, taskNavigation, tabSnapshot);
  const doneCount = plan.items.filter((task) => task.done).length;
  const allDone = doneCount === plan.items.length;
  const syncUnlockHint = formatDailyTaskSyncUnlockHint(plan);

  return (
    <div className="daily-tasks">
      <div className="daily-tasks-head">
        <span>Nhiệm vụ hôm nay</span>
        <div className="daily-tasks-head-right">
          <label
            className="daily-tasks-auto"
            title={
              autoEnabled
                ? 'Đang mở lại lần lượt toàn bộ nhiệm vụ — bỏ chọn để dừng. Sync đang chạy thì không hủy giữa chừng.'
                : 'Mỗi lần bật: mở lại toàn bộ nhiệm vụ hôm nay, rồi Sync All'
            }
          >
            <input
              type="checkbox"
              checked={autoEnabled}
              onChange={(e) => onAutoChange(e.target.checked)}
            />
            Auto
          </label>
          <span className={`daily-tasks-count${allDone ? ' is-done' : ''}`}>
            {doneCount}/{plan.items.length}
          </span>
        </div>
      </div>
      <ul className="daily-tasks-list">
        {plan.items.map((task) => {
          const durationSec = Math.max(0, Math.floor(task.viewDurationSeconds ?? 0));
          const dwell = taskDwells[task.id];
          const dwellRemaining = dwell ? dwellRemainingSeconds(dwell.endsAt) : 0;
          const isWatching = dwellRemaining > 0;
          const isNavigating =
            !isWatching &&
            (busyTaskId === task.id || isTaskNavigating(task.id, navigatingTaskId, false));
          const showDurationHint = !task.done && durationSec > 0 && !isWatching && !isNavigating;
          return (
            <li key={task.id} className={`daily-task${task.done ? ' is-done' : ''}`}>
              <button
                type="button"
                className={`daily-task-btn${isNavigating ? ' is-loading' : ''}${isWatching ? ' is-watching' : ''}`}
                onClick={() => onOpen(task)}
                disabled={isNavigating}
                title={
                  isNavigating
                    ? 'Đang tải trang nhiệm vụ…'
                    : isWatching
                      ? `Đang xem trang — còn ${formatDwellCountdown(dwellRemaining)}`
                      : task.done
                        ? `Đã xong — mở lại ${task.url}`
                        : durationSec > 0
                          ? `Mở ${task.url} — giữ tab ${durationSec}s sau khi load`
                          : `Mở ${task.url}`
                }
                aria-busy={isNavigating || isWatching}
              >
                <span className="daily-task-dot" aria-hidden />
                <span className="daily-task-name">{task.name}</span>
                {isWatching ? (
                  <span className="daily-task-countdown is-live" aria-live="polite">
                    {formatDwellCountdown(dwellRemaining)}
                  </span>
                ) : showDurationHint ? (
                  <span className="daily-task-countdown is-hint">{durationSec}s</span>
                ) : null}
                {isNavigating ? <Spinner dark /> : null}
              </button>
            </li>
          );
        })}
      </ul>
      {syncUnlockHint ? <p className="daily-tasks-hint">{syncUnlockHint}</p> : null}
      {autoEnabled ? (
        <p className="daily-tasks-hint">
          Auto đang chạy — mở lại toàn bộ nhiệm vụ rồi Sync All. Giữ tab seller-us.
        </p>
      ) : allDone ? null : (
        <p className="daily-tasks-hint">
          Bấm để mở trang, hoặc tự vào bằng menu TikTok — giữ tab đủ thời gian xem (nếu có) để
          nhiệm vụ tự đánh dấu.
        </p>
      )}
    </div>
  );
}

function ThemeToggle({
  mode,
  disabled,
  onToggle,
}: {
  mode: ThemeMode;
  disabled?: boolean;
  onToggle: () => void;
}) {
  const isDark = mode === 'dark';
  const label = isDark ? 'Chế độ sáng' : 'Chế độ tối';
  return (
    <button
      type="button"
      className="theme-toggle"
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onToggle}
    >
      {isDark ? (
        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden>
          <path
            fill="currentColor"
            d="M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10Zm0-5a1 1 0 0 1 1 1v1a1 1 0 1 1-2 0V3a1 1 0 0 1 1-1Zm0 17a1 1 0 0 1 1 1v1a1 1 0 1 1-2 0v-1a1 1 0 0 1 1-1ZM3 11a1 1 0 1 0 0 2h1a1 1 0 1 0 0-2H3Zm17 0a1 1 0 1 0 0 2h1a1 1 0 1 0 0-2h-1ZM5.64 5.64a1 1 0 0 1 1.41 0l.71.71a1 1 0 1 1-1.41 1.41l-.71-.71a1 1 0 0 1 0-1.41Zm12.02 12.02a1 1 0 0 1 1.41 0l.71.71a1 1 0 0 1-1.41 1.41l-.71-.71a1 1 0 0 1 0-1.41ZM18.36 5.64a1 1 0 0 1 0 1.41l-.71.71a1 1 0 1 1-1.41-1.41l.71-.71a1 1 0 0 1 1.41 0ZM6.34 17.66a1 1 0 0 1 0 1.41l-.71.71a1 1 0 1 1-1.41-1.41l.71-.71a1 1 0 0 1 1.41 0Z"
          />
        </svg>
      ) : (
        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden>
          <path
            fill="currentColor"
            d="M12.1 2a1 1 0 0 1 .9 1.45A7 7 0 1 0 19.55 13a1 1 0 0 1 1.9-.45A9 9 0 1 1 12.1 2Z"
          />
        </svg>
      )}
    </button>
  );
}

function VerifiedMark({ value }: { value?: boolean | null }) {
  if (value !== true) return null;
  return (
    <span className="badge badge-ok badge-icon" title="Đã xác minh" aria-label="Đã xác minh">
      <svg width="12" height="12" viewBox="0 0 24 24" aria-hidden focusable="false">
        <path
          fill="currentColor"
          d="M9.55 17.6 4.9 12.95l1.4-1.4 3.25 3.25 8.15-8.15 1.4 1.4L9.55 17.6Z"
        />
      </svg>
    </span>
  );
}

function sellerUsBadge(status: SellerUsSessionStatus): { label: string; tone: 'ok' | 'warn' | 'err' } {
  switch (status) {
    case 'logged_in':
      return { label: 'Đã đăng nhập seller-us', tone: 'ok' };
    case 'tab_open':
      return { label: 'Đã mở seller-us', tone: 'warn' };
    case 'no_capture':
      return { label: 'Chưa bắt session', tone: 'warn' };
    default:
      return { label: 'Chưa mở seller-us', tone: 'err' };
  }
}

function OnlineTodayBadge() {
  const snap = useOnlineTodayCounter();
  const label = formatOnlineDuration(snap.seconds, { live: snap.isLive });
  const tone = snap.isLive ? 'ok' : snap.seconds > 0 ? 'warn' : 'err';
  return (
    <span
      className={`badge badge-${tone} online-today-badge${snap.isLive ? ' is-live' : ''}`}
      title="Tổng thời gian tab seller-us hôm nay (ngày VN)"
      aria-live={snap.isLive ? 'polite' : undefined}
    >
      <span className="online-today-label">Online hôm nay</span>
      <span className="online-today-value">{label}</span>
    </span>
  );
}

function LoginForm({
  theme,
  themeSaving,
  onToggleTheme,
  onLoggedIn,
}: {
  theme: ThemeMode;
  themeSaving?: boolean;
  onToggleTheme: () => void;
  onLoggedIn: (s: StateResponse) => void;
}) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError(null);
    try {
      const res = await send<LoginResponse>({ type: 'LOGIN', username, password });
      if (!res.ok) throw new Error(res.message || 'Đăng nhập thất bại.');
      onLoggedIn(res);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-card">
      <div className="login-theme">
        <ThemeToggle mode={theme} disabled={themeSaving} onToggle={onToggleTheme} />
      </div>
      <div className="login-brand">
        <img className="login-logo" src={LOGO_URL} alt={BRAND_NAME} />
        <h1 className="login-title">{BRAND_NAME}</h1>
        <p className="login-sub">Đăng nhập bằng tài khoản Seller</p>
      </div>
      <form className="login-form" onSubmit={submit}>
        <div className="field">
          <label htmlFor="kb-username">Tên đăng nhập</label>
          <input
            id="kb-username"
            placeholder="Nhập tên đăng nhập"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            autoComplete="username"
            autoFocus
          />
        </div>
        <div className="field">
          <label htmlFor="kb-password">Mật khẩu</label>
          <input
            id="kb-password"
            type="password"
            placeholder="Nhập mật khẩu"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
          />
        </div>
        {error && <div className="error">{error}</div>}
        <button
          className="btn btn-primary"
          type="submit"
          disabled={loading || !username || !password}
        >
          {loading ? (
            <>
              <Spinner />
              Đang đăng nhập...
            </>
          ) : (
            'Đăng nhập'
          )}
        </button>
      </form>
    </div>
  );
}

function sameText(a?: string | null, b?: string | null): boolean {
  return (a || '').trim().toLowerCase() === (b || '').trim().toLowerCase();
}

function ShopInfoCard({ shop }: { shop: TikTokShopInfo | null }) {
  if (!shop) {
    return (
      <section className="card">
        <div className="card-title">Thông tin shop</div>
        <div className="hint">
          Mở{' '}
          <a className="link" href={SELLER_US_URL} target="_blank" rel="noreferrer">
            seller-us.tiktok.com
          </a>{' '}
          rồi bấm <strong>Info</strong> để lấy và đẩy profile lên Kinhbac.
        </div>
      </section>
    );
  }

  const bizLabel = formatBusinessTypeLabel(shop.businessType);
  const entityBadgeTitle = [
    shop.shopTypeReason,
    bizLabel ? `business_type: ${bizLabel}` : null,
    shop.payEntityType != null ? `pay entity_type=${shop.payEntityType}` : null,
  ]
    .filter(Boolean)
    .join(' · ');
  const showCompany = !!shop.companyName && !sameText(shop.companyName, shop.shopName);
  const showAccountName =
    !!shop.accountName &&
    !sameText(shop.accountName, shop.shopName) &&
    !sameText(shop.accountName, shop.companyName) &&
    !sameText(shop.accountName, shop.owner?.fullName);
  const companyAddressText = formatShopAddress(shop.companyAddress);
  const ownerAddressText = formatShopAddress(shop.owner?.address);
  const showOwner =
    !!shop.owner?.fullName &&
    !sameText(shop.owner.fullName, shop.shopName) &&
    !sameText(shop.owner.fullName, shop.companyName);
  const showCompanyBlock = !!(
    showCompany ||
    shop.companyCertNum ||
    companyAddressText ||
    showOwner ||
    ownerAddressText
  );
  const showContact = !!(
    shop.accountEmail ||
    shop.accountMobile ||
    showAccountName ||
    shop.bankAccountNumber
  );
  const bankTooltip = [
    shop.settlementPaymentMethod,
    shop.settlementBeneficiaryName,
    shop.settlementAccountType,
    shop.settlementAccountStatus != null ? `status=${shop.settlementAccountStatus}` : null,
  ]
    .filter(Boolean)
    .join(' · ');
  const riskFlags = [
    shop.isBlocked === true ? 'Bị chặn (is_blocked)' : null,
    shop.isBlocking === true ? 'Đang chặn người khác (is_blocking)' : null,
  ].filter(Boolean) as string[];
  const created = formatCreateTimeVn(shop.createTimeMs);
  const listing = formatPublishLimit(shop.publishLimit);
  const docs =
    shop.verifiedDocuments != null && Number.isFinite(shop.verifiedDocuments)
      ? String(shop.verifiedDocuments)
      : null;
  const hasOnHold = shop.onHoldAmount != null && Number.isFinite(shop.onHoldAmount);
  const hasStats = !!(created || listing !== '—' || docs || shop.timezone || shop.deli != null || hasOnHold);
  const catCount = shop.productCategories?.length ?? 0;
  const shipCount = shop.logisticsServices?.length ?? 0;
  const displayStatus = resolveDisplayShopStatus(shop.shopStatus, shop.sellerStatus);

  return (
    <section className="card shop-card">
      <div className="shop-card-head">
        {shop.logoUrl ? (
          <img className="shop-avatar" src={shop.logoUrl} alt="" />
        ) : (
          <div className="shop-avatar shop-avatar--fallback" aria-hidden>
            {(shop.shopName || '?').slice(0, 1).toUpperCase()}
          </div>
        )}
        <div className="shop-card-title-block">
          <div className="shop-name" title={shop.shopName}>
            {shop.shopName}
          </div>
          <div className="shop-meta-row">
            {shop.shopType ? (
              <span
                className={`badge ${shop.shopType === 'Llc' ? 'badge-ok' : 'badge-warn'}`}
                title={entityBadgeTitle || undefined}
              >
                {formatShopEntityType(shop.shopType)}
              </span>
            ) : null}
            {shop.region ? <span className="badge badge-ok">{shop.region}</span> : null}
            {shop.deli != null ? (
              <span className="badge badge-ok" title="bill_finish_period_in_days (Finance On hold)">
                DELI {shop.deli} {shop.deli === 1 ? 'day' : 'days'}
              </span>
            ) : null}
            {shop.sellerType != null ? (
              <span
                className="badge badge-ok"
                title="seller.seller_type — POP/Local vs Cross-border (không phải PER/LLC)"
              >
                {formatSellerType(shop.sellerType).replace(/\s*\(\d+\)$/, '')}
              </span>
            ) : null}
            <span
              className={shopStatusBadgeClass(displayStatus)}
              title="worse of seller.shop_status / seller.status"
            >
              {formatShopStatus(displayStatus)}
            </span>
          </div>
          <div className="shop-id-row" title={`${shop.sellerId}${shop.shopCode ? ` · ${shop.shopCode}` : ''}`}>
            <span className="shop-id">{shop.sellerId}</span>
            {shop.shopCode ? (
              <>
                <span className="shop-id-sep" aria-hidden>
                  ·
                </span>
                <span className="shop-code">{shop.shopCode}</span>
              </>
            ) : null}
          </div>
        </div>
      </div>

      {riskFlags.length > 0 ? (
        <div className="shop-risk-row">
          {riskFlags.map((label) => (
            <span key={label} className="badge badge-err">
              {label}
            </span>
          ))}
        </div>
      ) : null}

      {hasStats ? (
        <div className="shop-stat-grid">
          {created ? (
            <div
              className="shop-stat"
              title={shop.createTimeMs != null ? String(shop.createTimeMs) : undefined}
            >
              <span className="shop-stat-label">Tạo</span>
              <span className="shop-stat-value">{created}</span>
            </div>
          ) : null}
          {listing !== '—' ? (
            <div
              className="shop-stat"
              title={
                shop.publishLimit
                  ? `published ${shop.publishLimit.publishedNum} / total ${shop.publishLimit.totalLimit}`
                  : undefined
              }
            >
              <span className="shop-stat-label">Listing</span>
              <span className="shop-stat-value">{listing}</span>
            </div>
          ) : null}
          {docs ? (
            <div className="shop-stat">
              <span className="shop-stat-label">Docs</span>
              <span className="shop-stat-value">{docs} xác minh</span>
            </div>
          ) : null}
          {shop.timezone ? (
            <div className="shop-stat">
              <span className="shop-stat-label">TZ</span>
              <span className="shop-stat-value" title={shop.timezone}>
                {shop.timezone.replace(/^America\//, '')}
              </span>
            </div>
          ) : null}
          {hasOnHold ? (
            <div
              className="shop-stat shop-stat--span"
              title="TikTok Finance On hold (statement/stat/info)"
            >
              <span className="shop-stat-label">On hold</span>
              <span className="shop-stat-value">{formatUsdAmount(shop.onHoldAmount)}</span>
              <span className="shop-stat-sub">
                GH {formatUsdAmount(shop.onHoldInTransitAmount)} · TT{' '}
                {formatUsdAmount(shop.onHoldAwaitingSettlementAmount)} · Rsv{' '}
                {formatUsdAmount(shop.onHoldReserveAmount)}
              </span>
            </div>
          ) : null}
        </div>
      ) : null}

      {showCompanyBlock ? (
        <dl className="shop-dl shop-dl--compact">
          {showCompany ? (
            <div className="shop-dl-row">
              <dt>Company</dt>
              <dd title={shop.companyName}>{shop.companyName}</dd>
            </div>
          ) : null}
          {shop.companyCertNum ? (
            <div className="shop-dl-row">
              <dt>EIN / Cert</dt>
              <dd
                title={
                  shop.companyCertType != null
                    ? `cert_type=${shop.companyCertType}`
                    : undefined
                }
              >
                {shop.companyCertNum}
              </dd>
            </div>
          ) : null}
          {companyAddressText ? (
            <div className="shop-dl-row shop-dl-row--wrap">
              <dt>Địa chỉ</dt>
              <dd title={companyAddressText}>{companyAddressText}</dd>
            </div>
          ) : null}
          {showOwner ? (
            <div className="shop-dl-row">
              <dt>Owner</dt>
              <dd
                title={[shop.owner?.fullName, shop.owner?.birthday, shop.owner?.nationality]
                  .filter(Boolean)
                  .join(' · ')}
              >
                <span className="shop-dl-stack">
                  <span>{shop.owner!.fullName}</span>
                  {shop.owner?.birthday ? (
                    <span className="meta muted">{shop.owner.birthday}</span>
                  ) : null}
                </span>
              </dd>
            </div>
          ) : null}
          {ownerAddressText ? (
            <div className="shop-dl-row shop-dl-row--wrap">
              <dt>Addr owner</dt>
              <dd title={ownerAddressText}>{ownerAddressText}</dd>
            </div>
          ) : null}
        </dl>
      ) : null}

      {showContact ? (
        <dl className="shop-dl shop-dl--compact">
          {showAccountName ? (
            <div className="shop-dl-row">
              <dt>Tài khoản</dt>
              <dd title={shop.accountName || undefined}>{shop.accountName}</dd>
            </div>
          ) : null}
          {shop.accountEmail ? (
            <div className="shop-dl-row">
              <dt>Email</dt>
              <dd title={shop.accountEmail}>
                <span className="shop-dl-stack">
                  <span>{shop.accountEmail}</span>
                  <VerifiedMark value={shop.emailVerified} />
                </span>
              </dd>
            </div>
          ) : null}
          {shop.accountMobile ? (
            <div className="shop-dl-row">
              <dt>Mobile</dt>
              <dd title={shop.accountMobile}>
                <span className="shop-dl-stack">
                  <span>{shop.accountMobile}</span>
                  <VerifiedMark value={shop.phoneVerified} />
                </span>
              </dd>
            </div>
          ) : null}
          {shop.bankAccountNumber ? (
            <div className="shop-dl-row">
              <dt>Bank</dt>
              <dd className="mono-mask" title={bankTooltip || shop.bankAccountNumber}>
                {shop.bankAccountNumber.replace(/[•●◦﹡＊✱⁎∗]/g, '*')}
              </dd>
            </div>
          ) : null}
        </dl>
      ) : null}

      {shipCount > 0 ? (
        <div className="shop-section">
          <div className="shop-section-head">
            <span className="shop-section-title">Shipping</span>
            <span className="meta muted">{shipCount}</span>
          </div>
          <div className="shop-cat-list shop-cat-list--shipping">
            {shop.logisticsServices!.map((svc) => (
              <span
                key={`${svc.serviceName}|${svc.deliveryOptionName}`}
                className={`shop-cat-chip${svc.isDefault ? ' is-default' : ''}`}
                title={
                  svc.isDefault
                    ? `${formatLogisticsService(svc)} (default)`
                    : formatLogisticsService(svc)
                }
              >
                {formatLogisticsService(svc)}
              </span>
            ))}
          </div>
        </div>
      ) : null}

      {catCount > 0 ? (
        <div className="shop-section">
          <div className="shop-section-head">
            <span className="shop-section-title">Categories</span>
            <span className="meta muted">{catCount}</span>
          </div>
          <div className="shop-cat-list" title={`${catCount} categories`}>
            {shop.productCategories!.map((cat) => (
              <span key={cat.value} className="shop-cat-chip" title={cat.value}>
                {cat.label}
              </span>
            ))}
          </div>
        </div>
      ) : null}
    </section>
  );
}

export function App() {
  const taskDwells = useTaskDwellMap();
  const taskNavigation = useTaskNavigation();
  const tabSnapshot = useSellerUsTabSnapshot();
  const [state, setState] = useState<StateResponse | null>(null);
  const [bootError, setBootError] = useState<string | null>(null);
  const [theme, setTheme] = useState<ThemeMode>('light');
  const [themeSaving, setThemeSaving] = useState(false);
  const [actionMsg, setActionMsg] = useState<{ ok: boolean; message: string } | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  /** Epoch khi bắt đầu Làm mới / Info — dùng để tắt spinner sớm khi shopInfo mới về. */
  const [refreshStartedAt, setRefreshStartedAt] = useState<number | null>(null);
  const [syncingMode, setSyncingMode] = useState<'all' | 'fast' | null>(null);
  const [openingTaskId, setOpeningTaskId] = useState<string | null>(null);
  const [openingBusy, setOpeningBusy] = useState(false);
  /** Seller đã thử tải nhiệm vụ — tránh gọi lại vòng lặp khi shop chưa có nhiệm vụ nào. */
  const taskFetchedForSeller = useRef<string | null>(null);

  const hydrate = useCallback((s: StateResponse) => {
    setState(s);
    const next = normalizeTheme(s.theme);
    setTheme(next);
    applyDocumentTheme(next);
    setBootError(null);
  }, []);

  const refresh = useCallback(async () => {
    try {
      const timeout = new Promise<never>((_, reject) => {
        window.setTimeout(() => reject(new Error('Timeout tải trạng thái.')), BOOT_TIMEOUT_MS);
      });
      const s = await Promise.race([send<StateResponse>({ type: 'GET_STATE' }), timeout]);
      hydrate(s);
    } catch (err) {
      setBootError((err as Error).message);
      setState(null);
    }
  }, [hydrate]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  /** Đã login → tự Làm mới shop nếu chưa có (cần tab seller-us). */
  useEffect(() => {
    if (!state?.authenticated) return;
    if (state.shopInfo) return;
    let cancelled = false;
    const startedAt = Date.now();
    void (async () => {
      setRefreshing(true);
      setRefreshStartedAt(startedAt);
      try {
        const res = await send<StateResponse & { ok: boolean; message?: string }>({
          type: 'REFRESH_SHOP',
        });
        if (cancelled) return;
        hydrate(res);
        if (res.message) {
          setActionMsg({ ok: !!res.ok, message: res.message });
        }
      } catch {
        // ignore — user bấm Info / mở seller-us
      } finally {
        if (!cancelled) {
          setRefreshing(false);
          setRefreshStartedAt(null);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [state?.authenticated, state?.shopInfo, hydrate]);

  /** Content script / webRequest cập nhật storage → panel sync. */
  useEffect(() => {
    if (!state?.authenticated) return;
    const onChanged = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local') return;
      if (
        !changes.kb_tts_shop_info &&
        !changes.kb_tts_capture &&
        !changes.kb_tts_unified_sync &&
        !changes.kb_tts_daily_tasks &&
        !changes.kb_tts_task_navigation &&
        !changes.kb_tts_task_dwells &&
        !changes.kb_tts_auto_daily_tasks
      ) {
        return;
      }
      void refresh();
    };
    chrome.storage.onChanged.addListener(onChanged);
    return () => chrome.storage.onChanged.removeListener(onChanged);
  }, [state?.authenticated, refresh]);

  /**
   * Mở panel mà chưa có nhiệm vụ của shop hiện tại thì tải một lần — cache chỉ được ghi lúc bấm
   * Sync nên nếu không tải ở đây, seller mở panel sẽ không thấy việc phải làm.
   */
  useEffect(() => {
    const sellerId = state?.shopInfo?.sellerId?.trim();
    if (!state?.authenticated || !sellerId) return;
    if (state.dailyTasks?.sellerId === sellerId) return;
    if (taskFetchedForSeller.current === sellerId) return;

    taskFetchedForSeller.current = sellerId;
    void (async () => {
      try {
        const res = await send<StateResponse & { ok: boolean }>({ type: 'REFRESH_TASKS' });
        hydrate(res);
      } catch {
        // Không có nhiệm vụ cũng không sao — panel vẫn dùng được bình thường.
      }
    })();
  }, [state?.authenticated, state?.shopInfo?.sellerId, state?.dailyTasks?.sellerId, hydrate]);

  /** Đã có shop mới trong lúc Làm mới → tắt spinner, không đợi request ngầm. */
  useEffect(() => {
    if (!refreshing || refreshStartedAt == null) return;
    const shop = state?.shopInfo;
    if (!shop) return;
    const fetchedAt = shop.fetchedAt ?? 0;
    if (fetchedAt < refreshStartedAt - 500) return;
    setRefreshing(false);
    setRefreshStartedAt(null);
    if (!syncingMode) {
      setActionMsg({ ok: true, message: `Đã lấy shop «${shop.shopName}».` });
    }
  }, [refreshing, refreshStartedAt, state?.shopInfo, syncingMode]);

  const toggleTheme = async () => {
    const next: ThemeMode = theme === 'dark' ? 'light' : 'dark';
    setThemeSaving(true);
    try {
      applyDocumentTheme(next);
      setTheme(next);
      const res = await send<{ ok: boolean; theme: ThemeMode }>({ type: 'SET_THEME', theme: next });
      if (res.ok) {
        setTheme(normalizeTheme(res.theme));
        applyDocumentTheme(normalizeTheme(res.theme));
      }
    } finally {
      setThemeSaving(false);
    }
  };

  const logout = async () => {
    await send({ type: 'LOGOUT' });
    setActionMsg(null);
    await refresh();
  };

  const onOpenTask = async (task: { id: string; url: string }) => {
    setOpeningTaskId(task.id);
    setOpeningBusy(true);
    setActionMsg(null);
    try {
      const res = await send<StateResponse & { ok: boolean; message?: string }>({
        type: 'OPEN_TASK_URL',
        url: task.url,
        taskId: task.id,
      });
      hydrate(res);
      if (!res.ok) {
        setOpeningTaskId(null);
        if (res.message) setActionMsg({ ok: false, message: res.message });
      }
    } catch (err) {
      setOpeningTaskId(null);
      setActionMsg({ ok: false, message: (err as Error).message });
    } finally {
      setOpeningBusy(false);
    }
  };

  const onAutoDailyTasksChange = async (enabled: boolean) => {
    setActionMsg(null);
    try {
      const res = await send<StateResponse & { ok: boolean; message?: string }>({
        type: 'SET_AUTO_DAILY_TASKS',
        enabled,
      });
      hydrate(res);
      if (res.message) setActionMsg({ ok: !!res.ok, message: res.message });
    } catch (err) {
      setActionMsg({ ok: false, message: (err as Error).message });
    }
  };

  useEffect(() => {
    if (!openingTaskId || openingBusy) return;
    const dwell = taskDwells[openingTaskId];
    if (dwell && dwellRemainingSeconds(dwell.endsAt) > 0) {
      setOpeningTaskId(null);
      return;
    }
    const item = state?.dailyTasks?.items.find((task) => task.id === openingTaskId);
    if (item?.done) {
      setOpeningTaskId(null);
      return;
    }
    if (taskNavigation?.taskId === openingTaskId) return;
    if (resolveNavigatingTaskId(state?.dailyTasks, taskNavigation, tabSnapshot) === openingTaskId) {
      return;
    }
    // Đã hết điều hướng mà tab không còn ở URL nhiệm vụ (rời trang giữa chừng).
    if (
      item &&
      tabSnapshot?.status === 'complete' &&
      !matchesTaskUrl(tabSnapshot.url, item.url, item.urlMatchPattern)
    ) {
      setOpeningTaskId(null);
    }
  }, [openingTaskId, openingBusy, taskNavigation, taskDwells, tabSnapshot, state?.dailyTasks]);

  const onSync = async (mode: 'all' | 'fast') => {
    setSyncingMode(mode);
    setActionMsg(null);
    try {
      const res = await send<StateResponse & { ok: boolean; message?: string }>({
        type: mode === 'fast' ? 'SYNC_FAST' : 'SYNC_ALL',
      });
      hydrate(res);
      setActionMsg({
        ok: !!res.ok,
        message:
          res.message ||
          (res.ok ? (mode === 'fast' ? 'Đã Sync fast.' : 'Đã Sync all.') : 'Sync thất bại.'),
      });
    } catch (err) {
      setActionMsg({ ok: false, message: (err as Error).message });
    } finally {
      setSyncingMode(null);
    }
  };

  if (!state) {
    return (
      <div className="app loading">
        {bootError ? (
          <div className="boot-error">
            <div className="error">{bootError}</div>
            <button type="button" className="btn btn-primary" onClick={() => void refresh()}>
              Thử lại
            </button>
          </div>
        ) : (
          <>
            <Spinner dark />
            Đang tải...
          </>
        )}
      </div>
    );
  }

  if (!state.authenticated) {
    return (
      <div className="app app--login">
        <LoginForm
          theme={theme}
          themeSaving={themeSaving}
          onToggleTheme={() => void toggleTheme()}
          onLoggedIn={hydrate}
        />
      </div>
    );
  }

  const badge = sellerUsBadge(state.sellerUsStatus);
  const userLabel = state.user?.fullName || state.user?.username;
  const taskProgress = getDailyTaskProgress(state.dailyTasks);
  const syncBlockedByTasks = !taskProgress.syncUnlocked;
  const syncBusy = syncingMode !== null || refreshing || state.unifiedSync?.phase === 'running';

  return (
    <div className="app app--main">
      <header className="header">
        <div className="header-row">
          <div className="brand">
            <img className="brand-logo" src={LOGO_URL} alt={BRAND_NAME} />
            <div className="title">
              <div className="title-top">
                <span className="title-brand">{BRAND_NAME}</span>
                <span className="header-actions">
                  <ThemeToggle
                    mode={theme}
                    disabled={themeSaving}
                    onToggle={() => void toggleTheme()}
                  />
                  <button type="button" className="link header-logout" onClick={() => void logout()}>
                    Đăng xuất
                  </button>
                </span>
              </div>
              <div className="title-sub-row">
                <span className="title-sub">
                  TikTok Shop US
                  {userLabel ? <span className="title-seller">{` · ${userLabel}`}</span> : null}
                </span>
              </div>
            </div>
          </div>
        </div>
      </header>

      <div className="main">
        <section className="card source-bar">
          <div className="source-bar-row">
            <span className={`badge badge-${badge.tone}`}>{badge.label}</span>
            <OnlineTodayBadge />
            <div className="source-bar-sync-actions">
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={() => void onSync('fast')}
                disabled={syncBusy}
                title="Đồng bộ Finance, Health Center và Shop Score"
              >
                {syncingMode === 'fast' ? <Spinner dark /> : null}
                Sync fast
              </button>
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => void onSync('all')}
                disabled={syncBusy || syncBlockedByTasks}
                title={
                  syncBlockedByTasks
                    ? formatDailyTaskSyncBlockMessage(state.dailyTasks)
                    : 'Đồng bộ Info, Finance, Keywords, Analytics, Health Center, Shop Score và tải nhiệm vụ hệ thống'
                }
              >
                {syncingMode === 'all' ? <Spinner /> : null}
                Sync all
              </button>
            </div>
          </div>
          <DailyTaskList
            plan={state.dailyTasks}
            busyTaskId={openingTaskId ?? state.taskLoadingTaskId}
            taskNavigation={taskNavigation}
            taskDwells={taskDwells}
            tabSnapshot={tabSnapshot}
            autoEnabled={!!state.autoDailyTasks}
            onAutoChange={(enabled) => void onAutoDailyTasksChange(enabled)}
            onOpen={(task) => void onOpenTask(task)}
          />
          <UnifiedSyncChecklist sync={state.unifiedSync} />
          {refreshing ? (
            <div className="sync-progress sync-progress--busy" role="status" aria-live="polite">
              <Spinner dark />
              <span>Đang lấy dữ liệu TikTok Shop…</span>
            </div>
          ) : null}
          {actionMsg ? (
            <div className={`result ${actionMsg.ok ? 'ok' : 'err'}`}>{actionMsg.message}</div>
          ) : null}
          {!state.sellerUsTabOpen ? (
            <div className="hint">
              Chưa có tab seller-us —{' '}
              <button
                type="button"
                className="link"
                onClick={() => void send({ type: 'OPEN_SELLER_US' }).then(() => refresh())}
              >
                Mở seller-us.tiktok.com
              </button>
            </div>
          ) : null}
        </section>

        <ShopInfoCard shop={state.shopInfo} />
      </div>
    </div>
  );
}
