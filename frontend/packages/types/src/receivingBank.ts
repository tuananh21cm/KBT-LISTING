export interface ReceivingCardSource {
  id: string;
  code: string;
  name: string;
  description?: string;
  supportsAccounts: boolean;
  isActive: boolean;
}

export interface ReceivingSourceAccount {
  id: string;
  sourceId: string;
  sourceName: string;
  sourceCode?: string;
  name: string;
  externalAccountId?: string;
  username?: string;
  note?: string;
  isActive: boolean;
  cardCount: number;
  createdAt?: string;
  updatedAt?: string;
}

export interface ReceivingSourceAccountOption {
  id: string;
  sourceId: string;
  name: string;
  username?: string;
  isActive: boolean;
}

export interface CreateReceivingSourceAccountRequest {
  sourceId: string;
  name: string;
  externalAccountId?: string | null;
  username?: string | null;
  note?: string | null;
  isActive: boolean;
}

export type UpdateReceivingSourceAccountRequest = Omit<
  CreateReceivingSourceAccountRequest,
  'sourceId'
>;

/** Slim catalog row for `/receiving-banks` table. Full bank fields via GET by id. */
export interface ReceivingBankAccountListItem {
  id: string;
  sourceId: string;
  sourceName: string;
  sourceAccountId?: string;
  sourceAccountName?: string;
  currency: string;
  accountHolderName: string;
  accountNumber: string;
  isActive: boolean;
  shopCount: number;
  shops?: ReceivingBankLinkedShop[];
}

export interface ReceivingBankAccount {
  id: string;
  name: string;
  sourceId: string;
  sourceCode: string;
  sourceName: string;
  sourceAccountId?: string;
  sourceAccountName?: string;
  currency: string;
  bankName: string;
  bankLocation?: string;
  accountHolderName: string;
  accountType?: string;
  swiftBic?: string;
  routingNumber?: string;
  accountNumber: string;
  bankAddress?: string;
  isActive: boolean;
  shopCount: number;
  shops?: ReceivingBankLinkedShop[];
  createdAt: string;
  updatedAt?: string;
}

export interface ReceivingBankLinkedShop {
  id: string;
  name: string;
  profileName?: string | null;
}

export interface ReceivingBankAccountOption {
  id: string;
  name: string;
  sourceId: string;
  sourceName: string;
  sourceAccountId?: string;
  sourceAccountName?: string;
  currency: string;
  accountHolderName: string;
  accountNumber: string;
  bankName: string;
  isActive: boolean;
}

export interface CreateReceivingBankAccountRequest {
  sourceId: string;
  sourceAccountId?: string | null;
  currency: string;
  bankName: string;
  bankLocation?: string | null;
  accountHolderName: string;
  accountType?: string | null;
  swiftBic?: string | null;
  routingNumber?: string | null;
  accountNumber: string;
  bankAddress?: string | null;
  isActive: boolean;
}

export type UpdateReceivingBankAccountRequest = CreateReceivingBankAccountRequest;

/** Order line inside a synced TikTok payout. */
export interface TikTokPayoutOrderDetail {
  tradeOrderId: string;
  settlementAmount?: number | null;
  earningAmount?: number | null;
  fees?: number | null;
  settlementTime?: string | null;
}

export interface ReceivingBankTransaction {
  id: string;
  receivingBankAccountId: string;
  receivingBankAccountName?: string;
  receivingBankAccountNumber?: string;
  receivingBankAccountNumberMasked?: string;
  receivingBankAccountCurrency?: string;
  sourceName?: string;
  sourceAccountName?: string;
  accountHolderName?: string;
  shopId?: string | null;
  shopName?: string;
  shopProfileName?: string;
  shopOwnerId?: string | null;
  shopOwnerName?: string;
  orderId?: string | null;
  platformOrderId?: string;
  fourSellerOrderId?: number | null;
  amount: number;
  direction: 'Debit' | 'Credit' | string;
  currency: string;
  note?: string | null;
  /** Payout / transfer id from the platform that sent the money. */
  externalTransactionId?: string | null;
  source: 'SettlementPaid' | 'Manual' | 'TikTokPayout' | string;
  /** Order lines inside a TikTok payout (Source=TikTokPayout). */
  payoutOrders?: TikTokPayoutOrderDetail[] | null;
  confirmationStatus: 'Pending' | 'Confirmed' | 'Rejected' | string;
  createdByUserId: string;
  createdByUserName?: string;
  occurredAt: string;
  createdAt: string;
  updatedAt?: string;
}

export interface SetReceivingBankConfirmationStatusRequest {
  confirmationStatus: 'Pending' | 'Confirmed' | 'Rejected';
}

export const RECEIVING_BANK_CONFIRMATION = {
  Pending: 'Pending',
  Confirmed: 'Confirmed',
  Rejected: 'Rejected',
} as const;

export function receivingBankConfirmationLabel(status?: string): string {
  if (status === RECEIVING_BANK_CONFIRMATION.Pending) return 'Chờ duyệt';
  if (status === RECEIVING_BANK_CONFIRMATION.Rejected) return 'Không duyệt';
  return 'Đã duyệt';
}

export function receivingBankConfirmationColor(status?: string): 'orange' | 'red' | 'green' {
  if (status === RECEIVING_BANK_CONFIRMATION.Pending) return 'orange';
  if (status === RECEIVING_BANK_CONFIRMATION.Rejected) return 'red';
  return 'green';
}

/** Last 4 digit characters, or null when fewer than 4. */
export function receivingBankLast4(value?: string | null): string | null {
  const digits = (value ?? '').replace(/\D/g, '');
  return digits.length >= 4 ? digits.slice(-4) : null;
}

/** TikTok `bank_card_mask_code` stored in payout notes as `card ********4529`. */
export function payoutCardMaskFromNote(note?: string | null): string | null {
  if (!note) return null;
  const match = note.match(/\bcard\s+([*\d]+)/i);
  return match?.[1] ?? null;
}

/** True when TTS payout mask last-4 does not match the shop receiving-bank tail. */
export function isReceivingBankPayoutCardMismatch(
  row: Pick<
    ReceivingBankTransaction,
    'source' | 'note' | 'receivingBankAccountNumber' | 'receivingBankAccountNumberMasked'
  >,
): boolean {
  if (row.source !== 'TikTokPayout') return false;
  const maskTail = receivingBankLast4(payoutCardMaskFromNote(row.note));
  if (!maskTail) return false;
  const accountTail = receivingBankLast4(
    row.receivingBankAccountNumber || row.receivingBankAccountNumberMasked,
  );
  if (!accountTail) return true;
  return maskTail !== accountTail;
}

/** Table/dropdown label: WorldFirst · Huy Nguyễn · 0039****9549 · USD */
export const formatReceivingBankTxnCardLabel = (
  row: Pick<
    ReceivingBankTransaction,
    | 'sourceName'
    | 'sourceAccountName'
    | 'accountHolderName'
    | 'receivingBankAccountName'
    | 'receivingBankAccountNumberMasked'
    | 'receivingBankAccountNumber'
    | 'receivingBankAccountCurrency'
  >,
) => {
  const source =
    row.sourceName && row.sourceAccountName
      ? `${row.sourceName} · ${row.sourceAccountName}`
      : row.sourceAccountName || row.sourceName || row.accountHolderName || row.receivingBankAccountName;
  const masked = row.receivingBankAccountNumberMasked || row.receivingBankAccountNumber;
  return [source, masked].filter(Boolean).join(' · ') || '—';
};

const maskAccountNumber = (number?: string | null) => {
  if (!number) return '';
  const digits = number.replace(/\D/g, '');
  if (digits.length <= 8) return digits;
  return `${digits.slice(0, 4)}****${digits.slice(-4)}`;
};

/** Dropdown option label matching fulfil card style. */
export const formatReceivingBankOptionLabel = (
  opt: Partial<
    Pick<
      ReceivingBankAccountOption,
      'sourceName' | 'sourceAccountName' | 'accountHolderName' | 'name' | 'accountNumber' | 'currency'
    >
  >,
) => {
  const source =
    opt.sourceName && opt.sourceAccountName
      ? `${opt.sourceName} · ${opt.sourceAccountName}`
      : opt.sourceAccountName || opt.sourceName || opt.accountHolderName || opt.name;
  const parts = [source, maskAccountNumber(opt.accountNumber), opt.currency || null].filter(Boolean);
  return parts.join(' · ');
};

export interface ReceivingBankTransactionQuery {
  receivingBankAccountId?: string;
  shopId?: string;
  /** Admin — filter rows whose shop is owned by this seller. */
  ownerUserId?: string;
  orderId?: string;
  source?: string;
  from?: string;
  to?: string;
}

export interface ReceivingBankIncomingQuery {
  receivingBankAccountId?: string;
  sourceId?: string;
  sourceAccountId?: string;
  shopId?: string;
  txnSource?: string;
  from?: string;
  to?: string;
}

export interface ReceivingBankIncomingBreakdownItem {
  id?: string | null;
  name: string;
  extra?: string | null;
  /** Net amount (Credit − Debit). */
  amount: number;
  debitAmount?: number;
  creditAmount?: number;
  transactionCount: number;
  orderCount?: number;
}

export interface ReceivingBankIncomingDay {
  date: string;
  amount: number;
  debitAmount?: number;
  creditAmount?: number;
  transactionCount: number;
}

export interface ReceivingBankIncomingSummary {
  /** Net incoming (Credit − Debit). */
  totalAmount: number;
  creditAmount?: number;
  debitAmount?: number;
  transactionCount: number;
  orderCount?: number;
  cardCount: number;
  sourceCount: number;
  settlementPaidAmount: number;
  manualAmount: number;
  byCard: ReceivingBankIncomingBreakdownItem[];
  bySource: ReceivingBankIncomingBreakdownItem[];
  bySourceAccount: ReceivingBankIncomingBreakdownItem[];
  byShop: ReceivingBankIncomingBreakdownItem[];
  days: ReceivingBankIncomingDay[];
}

export interface CreateReceivingBankTransactionRequest {
  shopId: string;
  orderId?: string | null;
  amount: number;
  direction: 'Debit' | 'Credit';
  currency?: string;
  note?: string | null;
  externalTransactionId?: string | null;
  occurredAt?: string;
}

export interface UpdateReceivingBankTransactionRequest {
  shopId: string;
  orderId?: string | null;
  amount: number;
  direction: 'Debit' | 'Credit';
  currency?: string;
  note?: string | null;
  externalTransactionId?: string | null;
  occurredAt: string;
}

export const RECEIVING_ACCOUNT_CURRENCIES = ['USD', 'EUR', 'GBP', 'CAD', 'AUD'] as const;

export const RECEIVING_ACCOUNT_TYPES = [
  { label: 'Checking', value: 'Checking' },
  { label: 'Savings', value: 'Savings' },
] as const;
