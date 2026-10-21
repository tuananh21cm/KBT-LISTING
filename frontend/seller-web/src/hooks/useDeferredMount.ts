import { useEffect, useState } from 'react';

/**
 * Mounts heavy content right after the first paint (deterministic, one frame).
 *
 * Intentionally does NOT use requestIdleCallback: on a heavy page the main thread
 * stays busy (mount + background 4Seller sync) so idle callbacks get starved and
 * deferred sections appear seconds late. rAF + a 0ms macrotask fires as soon as
 * the initial shell has painted, so e.g. Variations show up ~1 frame after Product Info.
 */
export function useDeferredMount(enabled = true, delayMs = 0): boolean {
  const [ready, setReady] = useState(!enabled);

  useEffect(() => {
    if (!enabled) {
      setReady(true);
      return;
    }

    setReady(false);
    let cancelled = false;
    let raf = 0;
    let timeoutId: ReturnType<typeof setTimeout> | undefined;

    const finish = () => {
      if (!cancelled) setReady(true);
    };

    // rAF → runs just before the next paint; the 0ms timeout then fires right
    // after that paint, so the shell is visible before we mount heavy content.
    raf = requestAnimationFrame(() => {
      timeoutId = setTimeout(finish, delayMs);
    });

    return () => {
      cancelled = true;
      cancelAnimationFrame(raf);
      if (timeoutId != null) clearTimeout(timeoutId);
    };
  }, [enabled, delayMs]);

  return ready;
}
