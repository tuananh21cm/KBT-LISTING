import { useEffect, useState } from 'react';
import { STORAGE_KEYS } from '../shared/config';
import type { TaskNavigationState } from '../shared/types';

/** Spinner nhiệm vụ: đọc trực tiếp storage (không chờ GET_STATE). */
export function useTaskNavigation(): TaskNavigationState | null {
  const [nav, setNav] = useState<TaskNavigationState | null>(null);

  useEffect(() => {
    const read = async () => {
      try {
        const raw = await chrome.storage.local.get(STORAGE_KEYS.taskNavigation);
        setNav((raw[STORAGE_KEYS.taskNavigation] as TaskNavigationState | undefined) ?? null);
      } catch {
        setNav(null);
      }
    };

    void read();

    const onChanged = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local' || !changes[STORAGE_KEYS.taskNavigation]) return;
      setNav(
        (changes[STORAGE_KEYS.taskNavigation].newValue as TaskNavigationState | undefined) ??
          null,
      );
    };

    chrome.storage.onChanged.addListener(onChanged);
    return () => chrome.storage.onChanged.removeListener(onChanged);
  }, []);

  return nav;
}
