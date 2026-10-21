import { useEffect, useState } from 'react';
import { STORAGE_KEYS } from '../shared/config';
import {
  computeTodayOnlineSeconds,
  type LocalOnlineSession,
  type LocalOnlineToday,
  type TodayOnlineSnapshot,
} from '../shared/onlineSession';

function emptySnapshot(nowMs = Date.now()): TodayOnlineSnapshot {
  return computeTodayOnlineSeconds(null, null, nowMs);
}

/**
 * Đọc `kb_tts_online_today` + `kb_tts_online_session`, tick 1s khi đang live.
 */
export function useOnlineTodayCounter(): TodayOnlineSnapshot {
  const [today, setToday] = useState<LocalOnlineToday | null>(null);
  const [session, setSession] = useState<LocalOnlineSession | null>(null);
  const [nowMs, setNowMs] = useState(() => Date.now());

  useEffect(() => {
    const read = async () => {
      try {
        const raw = await chrome.storage.local.get([
          STORAGE_KEYS.onlineToday,
          STORAGE_KEYS.onlineSession,
        ]);
        setToday((raw[STORAGE_KEYS.onlineToday] as LocalOnlineToday | undefined) ?? null);
        setSession((raw[STORAGE_KEYS.onlineSession] as LocalOnlineSession | undefined) ?? null);
        setNowMs(Date.now());
      } catch {
        setToday(null);
        setSession(null);
      }
    };

    void read();

    const onChanged = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local') return;
      if (changes[STORAGE_KEYS.onlineToday]) {
        setToday(
          (changes[STORAGE_KEYS.onlineToday].newValue as LocalOnlineToday | undefined) ?? null,
        );
      }
      if (changes[STORAGE_KEYS.onlineSession]) {
        setSession(
          (changes[STORAGE_KEYS.onlineSession].newValue as LocalOnlineSession | undefined) ?? null,
        );
      }
      if (changes[STORAGE_KEYS.onlineToday] || changes[STORAGE_KEYS.onlineSession]) {
        setNowMs(Date.now());
      }
    };

    chrome.storage.onChanged.addListener(onChanged);
    return () => chrome.storage.onChanged.removeListener(onChanged);
  }, []);

  const snap = computeTodayOnlineSeconds(today, session, nowMs);

  useEffect(() => {
    if (!snap.isLive) return;
    const timer = window.setInterval(() => setNowMs(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [snap.isLive]);

  return snap.externalShopId || snap.seconds > 0 || snap.isLive ? snap : emptySnapshot(nowMs);
}
