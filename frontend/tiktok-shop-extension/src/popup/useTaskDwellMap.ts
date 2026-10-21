import { useEffect, useState } from 'react';
import { STORAGE_KEYS } from '../shared/config';
import type { TaskDwellMap } from '../shared/taskDwell';

/** Đồng bộ map dwell từ chrome.storage — không phụ thuộc GET_STATE (cập nhật ngay khi background ghi). */
export function useTaskDwellMap(): TaskDwellMap {
  const [map, setMap] = useState<TaskDwellMap>({});

  useEffect(() => {
    const read = async () => {
      try {
        const raw = await chrome.storage.local.get(STORAGE_KEYS.taskDwells);
        const next = (raw[STORAGE_KEYS.taskDwells] as TaskDwellMap | undefined) ?? {};
        setMap(next && typeof next === 'object' ? next : {});
      } catch {
        setMap({});
      }
    };

    void read();

    const onChanged = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local' || !changes[STORAGE_KEYS.taskDwells]) return;
      const next = changes[STORAGE_KEYS.taskDwells].newValue as TaskDwellMap | undefined;
      setMap(next && typeof next === 'object' ? next : {});
    };

    chrome.storage.onChanged.addListener(onChanged);
    return () => chrome.storage.onChanged.removeListener(onChanged);
  }, []);

  return map;
}
