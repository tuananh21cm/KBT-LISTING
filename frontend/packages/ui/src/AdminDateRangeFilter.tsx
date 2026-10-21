import { DatePicker, Segmented, Tooltip } from 'antd';
import type { RangePickerProps } from 'antd/es/date-picker';
import { type Dayjs } from 'dayjs';
import type { ReactNode } from 'react';
import {
  DATE_FORMAT,
  DATE_FORMAT_SHORT,
  disabledFutureVnDate,
  endOfVnDay,
  startOfVnDay,
} from '@drop/utils/datetime';
import { useIsMobile } from './useIsMobile';

const { RangePicker } = DatePicker;

export type DateRangePresetOption = {
  value: string;
  label: string;
};

export type AdminDateRangeFilterProps = {
  value: [Dayjs | null, Dayjs | null] | null;
  onChange: (range: [Dayjs, Dayjs] | null) => void;
  disabled?: boolean;
  allowClear?: boolean;
  disabledDate?: (date: Dayjs) => boolean;
  size?: 'small' | 'middle' | 'large';
  desktopWidth?: number;
  /** Desktop: antd presets panel. Ignored on mobile (use presetOptions). */
  presets?: RangePickerProps['presets'];
  /** Mobile: Segmented full-width below date row. */
  presetOptions?: DateRangePresetOption[];
  presetValue?: string;
  onPresetChange?: (value: string) => void;
  /** grid = admin-filters-span wrapper; toolbar = dashboard-date-row; inline = bare RangePicker */
  variant?: 'grid' | 'toolbar' | 'inline';
  placeholder?: [string, string];
  /** When disabled, show these placeholders instead (e.g. Orders search-all-time). */
  disabledPlaceholder?: [string, string];
  presetSpanClassName?: string;
  presetTooltip?: string;
  className?: string;
  /** grid: hiển thị giữa RangePicker và preset Segmented (vd. nút Tìm kiếm). */
  betweenRangeAndPreset?: ReactNode;
};

const clampRange = (
  from: Dayjs,
  to: Dayjs,
): [Dayjs, Dayjs] => [
  startOfVnDay(from.isAfter(to, 'day') ? to : from),
  endOfVnDay(to.isBefore(from, 'day') ? from : to),
];

export const AdminDateRangeFilter = ({
  value,
  onChange,
  disabled = false,
  allowClear = false,
  disabledDate = disabledFutureVnDate,
  size = 'small',
  desktopWidth = 240,
  presets,
  presetOptions,
  presetValue,
  onPresetChange,
  variant = 'grid',
  placeholder = ['Ngày bắt đầu', 'Ngày kết thúc'],
  disabledPlaceholder,
  presetSpanClassName = 'admin-filters-span',
  presetTooltip,
  className,
  betweenRangeAndPreset,
}: AdminDateRangeFilterProps) => {
  const isMobile = useIsMobile();
  const dateRowClass =
    variant === 'toolbar' ? 'dashboard-date-row' : 'admin-filters-date-row';

  const handleFromChange = (from: Dayjs | null) => {
    if (!from || disabled) return;
    const to = value?.[1] ?? from;
    onChange(clampRange(from, to));
  };

  const handleToChange = (to: Dayjs | null) => {
    if (!to || disabled) return;
    const from = value?.[0] ?? to;
    onChange(clampRange(from, to));
  };

  const handleRangeChange = (next: [Dayjs | null, Dayjs | null] | null) => {
    if (disabled) return;
    if (!next?.[0] || !next[1]) {
      if (allowClear) onChange(null);
      return;
    }
    onChange(clampRange(next[0], next[1]));
  };

  const presetSegmented =
    presetOptions && presetOptions.length > 0 && onPresetChange ? (() => {
      const segmented = (
        <Segmented
          size={size}
          block={variant !== 'toolbar' || isMobile}
          disabled={disabled}
          value={presetValue}
          options={presetOptions}
          onChange={(v) => onPresetChange(String(v))}
        />
      );
      const wrapped = presetTooltip ? (
        <Tooltip title={disabled ? presetTooltip : undefined}>{segmented}</Tooltip>
      ) : (
        segmented
      );
      if (variant === 'grid') {
        return <div className={presetSpanClassName}>{wrapped}</div>;
      }
      return wrapped;
    })() : null;

  if (isMobile) {
    const mobilePlaceholders = disabled && disabledPlaceholder ? disabledPlaceholder : placeholder;
    return (
      <>
        <div className={[dateRowClass, className].filter(Boolean).join(' ')}>
          <DatePicker
            size={size}
            placeholder={mobilePlaceholders[0] || 'Từ ngày'}
            value={value?.[0] ?? null}
            format={DATE_FORMAT_SHORT}
            inputReadOnly
            disabled={disabled}
            disabledDate={disabledDate}
            placement="bottomLeft"
            style={{ width: '100%' }}
            onChange={handleFromChange}
          />
          <DatePicker
            size={size}
            placeholder={mobilePlaceholders[1] || 'Đến ngày'}
            value={value?.[1] ?? null}
            format={DATE_FORMAT_SHORT}
            inputReadOnly
            disabled={disabled}
            disabledDate={disabledDate}
            placement="bottomRight"
            style={{ width: '100%' }}
            onChange={handleToChange}
          />
        </div>
        {betweenRangeAndPreset ? (
          <div className="admin-filters-between">{betweenRangeAndPreset}</div>
        ) : null}
        {presetSegmented}
      </>
    );
  }

  const rangePicker = (
    <RangePicker
      size={size}
      value={value}
      disabled={disabled}
      allowClear={allowClear}
      disabledDate={disabledDate}
      presets={presets}
      placeholder={
        disabled && disabledPlaceholder ? disabledPlaceholder : placeholder
      }
      format={DATE_FORMAT}
      style={{ width: desktopWidth }}
      onChange={handleRangeChange}
    />
  );

  if (variant === 'inline') {
    return (
      <>
        <span className={className}>{rangePicker}</span>
        {presetSegmented}
      </>
    );
  }

  if (variant === 'toolbar') {
    return (
      <>
        {rangePicker}
        {presetSegmented}
      </>
    );
  }

  return (
    <>
      <span className={['admin-filters-span', className].filter(Boolean).join(' ')}>
        {rangePicker}
      </span>
      {betweenRangeAndPreset ? (
        <div className="admin-filters-between">{betweenRangeAndPreset}</div>
      ) : null}
      {presetSegmented}
    </>
  );
};
