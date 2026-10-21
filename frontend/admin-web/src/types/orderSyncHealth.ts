/** One branch of `GET /health` → `orderSync.newOrders` / `orderSync.status`. */
export interface OrderSyncHealthBranch {
  enabled: boolean | null;
  intervalMinutes: number | null;
  lastStartedVietnam: string | null;
  lastFinishedVietnam: string | null;
  lastSummary: string | null;
  isRunning: boolean;
  nextDueVietnam: string | null;
  secondsOverdue: number | null;
}

export interface OrderSyncHealthResponse {
  status: string;
  vietnamTime: string;
  timezone: string;
  orderSync: {
    newOrders: OrderSyncHealthBranch;
    status: OrderSyncHealthBranch;
  };
}
