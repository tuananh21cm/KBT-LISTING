import { useEffect, useState } from 'react';
import { STORAGE_KEYS } from '../shared/config';
import type { SellerUsTabSnapshot } from '../shared/types';

/** Snapshot tab seller-us: URL + loading/complete — spinner theo trạng thái trang thật. */
export function useSellerUsTabSnapshot(): SellerUsTabSnapshot | null {
  const [snapshot, setSnapshot] = useState<SellerUsTabSnapshot | null>(null);

  useEffect(() => {
    const read = async () => {
      try {
        const raw = await chrome.storage.local.get(STORAGE_KEYS.sellerUsTab);
        setSnapshot((raw[STORAGE_KEYS.sellerUsTab] as SellerUsTabSnapshot | undefined) ?? null);
      } catch {
        setSnapshot(null);
      }
    };

    void read();

    const onChanged = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local' || !changes[STORAGE_KEYS.sellerUsTab]) return;
      setSnapshot(
        (changes[STORAGE_KEYS.sellerUsTab].newValue as SellerUsTabSnapshot | undefined) ?? null,
      );
    };

    chrome.storage.onChanged.addListener(onChanged);
    return () => chrome.storage.onChanged.removeListener(onChanged);
  }, []);

  return snapshot;
}
