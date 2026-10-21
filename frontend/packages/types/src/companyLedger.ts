/** Query for GET /admin/company-ledger and GET /company-ledger. */
export interface CompanyLedgerQuery {
  from?: string;
  to?: string;
  direction?: string;
  categoryCode?: string;
  /** Admin only. Ignored when ownerUnassigned is set. */
  ownerUserId?: string;
  ownerUnassigned?: boolean;
  shopId?: string;
  shopUnassigned?: boolean;
  search?: string;
}

export interface CompanyLedgerCategory {
  code: string;
  name: string;
  defaultDirection: string;
}

export interface CompanyLedgerEntry {
  id: string;
  amount: number;
  currency: string;
  direction: string;
  categoryCode: string;
  title: string;
  note?: string | null;
  occurredAt: string;
  shopId?: string | null;
  shopName?: string | null;
  ownerUserId?: string | null;
  ownerFullName?: string | null;
  ownerUserName?: string | null;
  source: string;
  createdByUserId: string;
  createdAt: string;
}

export interface CompanyLedgerCurrencyTotal {
  currency: string;
  creditTotal: number;
  debitTotal: number;
  net: number;
}

export interface CompanyLedgerSummary {
  count: number;
  byCurrency: CompanyLedgerCurrencyTotal[];
}

export interface CompanyLedgerList {
  items: CompanyLedgerEntry[];
  summary: CompanyLedgerSummary;
}

export interface SaveCompanyLedgerEntryRequest {
  direction: string;
  categoryCode: string;
  amount: number;
  currency: string;
  title: string;
  note?: string | null;
  occurredAt: string;
  shopId?: string | null;
  ownerUserId?: string | null;
}

export const COMPANY_LEDGER_DIRECTIONS = {
  Debit: 'Chi',
  Credit: 'Thu',
} as const;
