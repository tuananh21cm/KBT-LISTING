import { Drawer, Empty, Table, Tag, Typography } from 'antd';
import { ExportOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { Link } from 'react-router-dom';
import { PageContentSpinCenter, useIsMobile } from '@drop/ui';
import type { ReceivingBankTransaction } from '@drop/types/receivingBank';
import {
  receivingBankConfirmationColor,
  receivingBankConfirmationLabel,
} from '@drop/types/receivingBank';
import type { ShopFinanceItem } from '@drop/types/shopFinance';
import { DATETIME_FORMAT_SHORT, formatDateTimeVn } from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import { useShopFinanceDeps } from './ShopFinanceDepsContext';

const { Text } = Typography;

const formatSignedUsd = (amount: number, direction?: string) => {
  const abs = Math.abs(Number(amount) || 0).toLocaleString('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  const isCredit = direction !== 'Debit';
  return { isCredit, text: `${isCredit ? '+' : '-'}$${abs}` };
};

const sourceLabel = (source?: string) => {
  if (source === 'SettlementPaid') return { color: 'green' as const, text: 'Đã TT' };
  if (source === 'TikTokPayout') return { color: 'cyan' as const, text: 'TTS Payout' };
  return { color: 'default' as const, text: 'Thủ công' };
};

type ShopFinanceTxnDrawerProps = {
  shop: ShopFinanceItem | null;
  from?: string;
  to?: string;
  periodLabel?: string;
  transactionsPath: string;
  onClose: () => void;
};

export const ShopFinanceTxnDrawer = ({
  shop,
  from,
  to,
  periodLabel,
  transactionsPath,
  onClose,
}: ShopFinanceTxnDrawerProps) => {
  const deps = useShopFinanceDeps();
  const isMobile = useIsMobile();
  const shopId = shop?.shopId;
  const { data: rows = [], isLoading } = deps.useTransactions(
    { shopId, from, to },
    Boolean(shopId),
  );

  const fullPath = shopId
    ? `${transactionsPath}?shopId=${encodeURIComponent(shopId)}`
    : transactionsPath;

  const subtitle = shop
    ? [shop.profileName, shop.name].filter(Boolean).join(' · ')
    : '';

  const columns: ColumnsType<ReceivingBankTransaction> = isMobile
    ? [
        {
          title: 'Giao dịch',
          key: 'mobile',
          render: (_, row) => {
            const { isCredit, text } = formatSignedUsd(row.amount, row.direction);
            const card = [row.sourceName?.trim(), row.receivingBankAccountNumberMasked]
              .filter(Boolean)
              .join(' ');
            const src = sourceLabel(row.source);
            const status = receivingBankConfirmationLabel(row.confirmationStatus);
            return (
              <div className="shop-finance-txn-mobile">
                <div className="shop-finance-txn-mobile-top">
                  <span className={isCredit ? 'shop-finance-txn-credit' : 'shop-finance-txn-debit'}>
                    {text}
                  </span>
                  <Text type="secondary" className="shop-finance-txn-time">
                    {formatDateTimeVn(row.occurredAt, DATETIME_FORMAT_SHORT)}
                  </Text>
                </div>
                <Text type="secondary" ellipsis={{ tooltip: true }} className="shop-finance-txn-meta">
                  {[card || null, src.text, status, row.note?.trim() || null]
                    .filter(Boolean)
                    .join(' · ')}
                </Text>
              </div>
            );
          },
        },
      ]
    : [
        {
          title: 'Thời gian',
          dataIndex: 'occurredAt',
          width: 118,
          render: (v: string) => (
            <Text type="secondary">{formatDateTimeVn(v, DATETIME_FORMAT_SHORT)}</Text>
          ),
        },
        {
          title: 'Số tiền',
          dataIndex: 'amount',
          width: 100,
          align: 'right',
          render: (amount: number, row) => {
            const { isCredit, text } = formatSignedUsd(amount, row.direction);
            return (
              <span className={isCredit ? 'shop-finance-txn-credit' : 'shop-finance-txn-debit'}>
                {text}
              </span>
            );
          },
        },
        {
          title: 'Thẻ',
          key: 'card',
          width: 200,
          onCell: () => ({ style: { whiteSpace: 'nowrap' } }),
          render: (_, row) => {
            const source = row.sourceName?.trim() || '';
            const masked =
              row.receivingBankAccountNumberMasked || row.receivingBankAccountNumber || '';
            const label = [source, masked].filter(Boolean).join(' ') || '—';
            if (!source && !masked) return '—';
            return (
              <span className="shop-finance-txn-card" title={label}>
                {source ? <span className="shop-finance-txn-card-source">{source}</span> : null}
                {masked ? <span className="shop-finance-txn-card-no">{masked}</span> : null}
              </span>
            );
          },
        },
        {
          title: 'Loại',
          dataIndex: 'source',
          width: 108,
          render: (source: string) => {
            const src = sourceLabel(source);
            return <Tag color={src.color}>{src.text}</Tag>;
          },
        },
        {
          title: 'TT',
          dataIndex: 'confirmationStatus',
          width: 88,
          render: (status: string) => (
            <Tag color={receivingBankConfirmationColor(status)}>
              {receivingBankConfirmationLabel(status)}
            </Tag>
          ),
        },
      ];

  return (
    <Drawer
      title={
        <div className="shop-finance-txn-title">
          <span>Lịch sử tiền về</span>
          {subtitle ? (
            <Text type="secondary" ellipsis={{ tooltip: subtitle }} className="shop-finance-txn-sub">
              {subtitle}
            </Text>
          ) : null}
          {periodLabel ? (
            <Text type="secondary" className="shop-finance-txn-period">
              Kỳ: {periodLabel}
            </Text>
          ) : null}
        </div>
      }
      extra={
        <Link to={fullPath} onClick={onClose} className="shop-finance-txn-full">
          <ExportOutlined />
          {isMobile ? null : ' Trang đầy đủ'}
        </Link>
      }
      open={!!shop}
      onClose={onClose}
      width={isMobile ? '100%' : 720}
      destroyOnHidden
    >
      {isLoading ? (
        <PageContentSpinCenter tip="Đang tải giao dịch…" minHeight={200} />
      ) : rows.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có giao dịch trong kỳ này" />
      ) : (
        <Table<ReceivingBankTransaction>
          size="small"
          rowKey="id"
          columns={columns}
          dataSource={rows}
          showHeader={!isMobile}
          pagination={tablePagination(rows.length, { size: 'small', simple: isMobile, pageSize: 20 })}
        />
      )}
    </Drawer>
  );
};
