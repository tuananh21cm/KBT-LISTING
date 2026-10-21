import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConfigProvider } from 'antd';
import viVN from 'antd/locale/vi_VN';
import 'dayjs/locale/vi';
import { useIsMobile } from '@drop/ui';
import { darkTheme, lightTheme } from '@drop/theme';
import { dayjs } from '../utils/datetime';
import { useThemeStore } from '../stores/themeStore';

dayjs.locale('vi');

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
});

interface AppProvidersProps {
  children: React.ReactNode;
}

/** Mobile: empty Tooltip trigger so first tap runs the action (no hover-tooltip steal). */
const AntdProvider = ({ children }: AppProvidersProps) => {
  const isMobile = useIsMobile();
  const mode = useThemeStore((s) => s.mode);
  const themeConfig = mode === 'dark' ? darkTheme : lightTheme;

  return (
    <ConfigProvider
      theme={themeConfig}
      locale={viVN}
      componentSize="small"
      tooltip={isMobile ? { trigger: [] } : undefined}
    >
      {children}
    </ConfigProvider>
  );
};

export const AppProviders = ({ children }: AppProvidersProps) => (
  <QueryClientProvider client={queryClient}>
    <AntdProvider>{children}</AntdProvider>
  </QueryClientProvider>
);
