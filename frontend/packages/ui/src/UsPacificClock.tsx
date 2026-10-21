import { useEffect, useState } from 'react';
import { dayjs, DATETIME_FORMAT_SECONDS } from '@drop/utils/datetime';

/** 4Seller / US order create-date wall-clock (see docs/architecture.md). */
export const US_PACIFIC_TZ = 'America/Los_Angeles';

/**
 * Live Pacific clock for sider footer — shared by admin & seller portals.
 * Matches 4Seller create-date business day (America/Los_Angeles).
 */
export const UsPacificClock = () => {
  const [now, setNow] = useState(() => dayjs());

  useEffect(() => {
    const timerId = window.setInterval(() => setNow(dayjs()), 1_000);
    return () => window.clearInterval(timerId);
  }, []);

  const time = now.tz(US_PACIFIC_TZ).format(DATETIME_FORMAT_SECONDS);

  return (
    <div
      className="admin-sider-us-clock"
      title={`Giờ Mỹ (Pacific): ${time}`}
      aria-label={`Giờ Mỹ, múi giờ Pacific: ${time}`}
    >
      <span className="admin-sider-us-clock-content">
        <svg className="admin-sider-us-clock-flag" viewBox="0 0 19 12" aria-hidden="true">
          <rect width="19" height="12" rx="1" fill="#fff" />
          <path
            d="M0 0h19v1H0zm0 2h19v1H0zm0 2h19v1H0zm0 2h19v1H0zm0 2h19v1H0zm0 2h19v1H0z"
            fill="#b22234"
          />
          <path d="M0 0h8v7H0z" fill="#3c3b6e" />
          <path
            d="M1.2 1.2h.6v.6h-.6zm2 0h.6v.6h-.6zm2 0h.6v.6h-.6zm2 0h.6v.6h-.6zm-5 2h.6v.6h-.6zm2 0h.6v.6h-.6zm2 0h.6v.6h-.6zm-5 2h.6v.6h-.6zm2 0h.6v.6h-.6zm2 0h.6v.6h-.6zm2 0h.6v.6h-.6z"
            fill="#fff"
          />
        </svg>
        <span className="admin-sider-us-clock-time">{time}</span>
      </span>
    </div>
  );
};
