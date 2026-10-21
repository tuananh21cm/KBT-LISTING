import type { CSSProperties } from 'react';
import { Button, Tooltip } from 'antd';
import { MoonOutlined, SunOutlined } from '@ant-design/icons';

/** Hit-area + icon size shared by header actions (bell / chat / theme). */
export const HEADER_ACTION_BTN_STYLE: CSSProperties = {
  width: 28,
  height: 28,
  padding: 0,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
};

export const HEADER_ACTION_ICON_STYLE: CSSProperties = {
  fontSize: 16,
};

export type ThemeToggleButtonProps = {
  mode: 'light' | 'dark';
  onToggle: () => void;
  disabled?: boolean;
};

/** Presentational theme toggle — apps wire persist (API / local store). */
export const ThemeToggleButton = ({ mode, onToggle, disabled }: ThemeToggleButtonProps) => {
  const isDark = mode === 'dark';
  const label = isDark ? 'Chế độ sáng' : 'Chế độ tối';

  return (
    <Tooltip title={label}>
      <Button
        type="text"
        size="small"
        aria-label={label}
        disabled={disabled}
        onClick={onToggle}
        style={HEADER_ACTION_BTN_STYLE}
        icon={
          isDark ? (
            <SunOutlined style={HEADER_ACTION_ICON_STYLE} />
          ) : (
            <MoonOutlined style={HEADER_ACTION_ICON_STYLE} />
          )
        }
      />
    </Tooltip>
  );
};
