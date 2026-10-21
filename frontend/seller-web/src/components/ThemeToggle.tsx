import { useState } from 'react';
import { message } from 'antd';
import { ThemeToggleButton } from '@drop/ui';
import type { ThemeMode } from '@drop/theme';
import { authApi } from '../services/authApi';
import { useAuthStore } from '../stores/authStore';
import { useThemeStore } from '../stores/themeStore';

/**
 * Seller theme toggle — localStorage (`seller-theme`) always;
 * when logged in, also persists via `PUT /auth/theme` so preference survives logout / devices.
 */
export const ThemeToggle = () => {
  const mode = useThemeStore((s) => s.mode);
  const setMode = useThemeStore((s) => s.setMode);
  const accessToken = useAuthStore((s) => s.accessToken);
  const user = useAuthStore((s) => s.user);
  const setUser = useAuthStore((s) => s.setUser);
  const [saving, setSaving] = useState(false);

  const isDark = mode === 'dark';
  const nextMode: ThemeMode = isDark ? 'light' : 'dark';

  const handleToggle = async () => {
    const previous = mode;
    setMode(nextMode);

    // Logged out (e.g. LoginPage): local cache only — still applied after logout.
    if (!accessToken) return;

    setSaving(true);
    try {
      const response = await authApi.updateTheme({ theme: nextMode });
      if (!response.success) {
        throw new Error(response.message || 'Cập nhật theme thất bại.');
      }
      const confirmed: ThemeMode = response.data?.theme === 'dark' ? 'dark' : 'light';
      if (confirmed !== nextMode) {
        setMode(confirmed);
      }
      if (user) {
        setUser({ ...user, theme: confirmed });
      }
    } catch (error: unknown) {
      setMode(previous);
      const axiosError = error as { response?: { data?: { message?: string } }; message?: string };
      message.error(
        axiosError.response?.data?.message ?? axiosError.message ?? 'Không lưu được chế độ giao diện.',
      );
    } finally {
      setSaving(false);
    }
  };

  return (
    <ThemeToggleButton mode={mode} disabled={saving} onToggle={() => void handleToggle()} />
  );
};
