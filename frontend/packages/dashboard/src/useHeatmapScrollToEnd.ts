import { useEffect, useRef } from 'react';

/** Ant Design `lg` breakpoint — treat below as mobile/tablet for heatmap UX. */
const MOBILE_MQ = '(max-width: 991px)';

/**
 * On narrow viewports, pin horizontal heatmap scroll to the end (latest days/hours).
 * Re-runs when `revision` changes (e.g. cell count / identity) or the scroll area resizes.
 */
export const useHeatmapScrollToEnd = (revision: string | number) => {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;

    const scrollToEndIfMobile = () => {
      if (typeof window === 'undefined' || !window.matchMedia(MOBILE_MQ).matches) return;
      el.scrollLeft = el.scrollWidth;
    };

    scrollToEndIfMobile();
    const frame = requestAnimationFrame(scrollToEndIfMobile);

    const ro = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(scrollToEndIfMobile) : null;
    ro?.observe(el);
    window.addEventListener('resize', scrollToEndIfMobile);

    return () => {
      cancelAnimationFrame(frame);
      ro?.disconnect();
      window.removeEventListener('resize', scrollToEndIfMobile);
    };
  }, [revision]);

  return ref;
};
