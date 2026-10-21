/** Query for GET /shops/finance and GET /admin/shops/finance. */
export interface ShopFinanceQuery {
  /** Admin only — filter shops by owner. */
  ownerUserId?: string;
  /** Inclusive UTC start for ledger tiền đã về. Omit = all-time. */
  from?: string;
  /** Inclusive UTC end for ledger tiền đã về. Omit = all-time. */
  to?: string;
  /** Shop ops status. Default Active. Pass `all` for every status. */
  status?: string;
}

/** KPI totals. On hold is a TTS snapshot; moneyReceived follows from/to. */
export interface ShopFinanceSummary {
  shopCount: number;
  shopsWithoutBankCount: number;
  onHoldAmount: number;
  onHoldInTransitAmount: number;
  onHoldAwaitingSettlementAmount: number;
  onHoldReserveAmount: number;
  moneyReceived: number;
  pendingConfirmationAmount: number;
  /** Sum of shop purchase costs (all-time, not the date window). */
  acquisitionCostTotal: number;
}

/** Per-shop finance row. */
export interface ShopFinanceItem {
  shopId: string;
  name: string;
  profileName?: string | null;
  avatarUrl?: string | null;
  status: string;
  deli?: number | null;
  onHoldAmount?: number | null;
  onHoldInTransitAmount?: number | null;
  onHoldAwaitingSettlementAmount?: number | null;
  onHoldReserveAmount?: number | null;
  moneyReceived: number;
  pendingConfirmationAmount: number;
  receivingBankAccountId?: string | null;
  tikTokProfileSyncedAt?: string | null;
  ownerId: string;
  ownerFullName?: string | null;
  ownerUserName?: string | null;
  /** Shop purchase cost from the company ledger. Null when unset. */
  acquisitionCost?: number | null;
  acquisitionOccurredAt?: string | null;
}

/** Body for PUT /admin/shops/{id}/acquisition-cost. Null or 0 deletes the row. */
export interface SetShopAcquisitionCostRequest {
  amount?: number | null;
  occurredAt?: string | null;
  note?: string | null;
}

/** Shop finance overview payload. */
export interface ShopFinance {
  summary: ShopFinanceSummary;
  items: ShopFinanceItem[];
}
