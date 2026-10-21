import { Grid } from 'antd';

/** True below Ant Design `md` breakpoint (~768px). */
export const useIsMobile = () => {
  const screens = Grid.useBreakpoint();
  // `md` is undefined on first paint before measure — treat as desktop to avoid flash of mobile chrome.
  return screens.md === false;
};
