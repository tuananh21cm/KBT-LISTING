import { useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Col,
  Form,
  Radio,
  Row,
  Select,
  Table,
  Tabs,
  Tooltip,
  Typography,
} from 'antd';
import { Modal, useIsMobile } from '@drop/ui';
import { ExclamationCircleFilled, SettingOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useListingsDeps } from './ListingsDepsContext';
import type { PricingFormulaInput, PricingTargetMode } from '@drop/types/pricingTemplate';
import type { ListingVariationRow } from './productFormTypes';
import {
  DEFAULT_PRICING_FORMULA,
  calculateNetReceive,
  calculateSalePrice,
  getDefaultPricingTemplate,
  isProfitBelowTotalCost,
  resolvePromoPercent,
  resolveTotalCost,
  resolveTotalImport,
  roundMoney,
  templateToFormulaInput,
} from './pricingBatchUtils';
import { InputNumberWithUnit } from './InputNumberWithUnit';
import { ManagePricingTemplateModal } from './ManagePricingTemplateModal';

const { Text } = Typography;

interface BatchPricingModalProps {
  open: boolean;
  variations: ListingVariationRow[];
  onCancel: () => void;
  onApply: (
    salePrices: Array<number | null>,
    formula: PricingFormulaInput,
    templateId?: string,
  ) => void;
  activeFormula?: PricingFormulaInput;
  activeTemplateId?: string;
  onActiveFormulaChange?: (formula: PricingFormulaInput, templateId?: string) => void;
}

type PreviewRow = {
  key: number;
  label: string;
  cost: number | null | undefined;
  /** Cost + shipping nhập + tax. */
  totalImport: number | null;
  /** totalImport + ads — hiển thị cột Cost. */
  totalCost: number | null;
  costBreakdownTitle: string;
  promoPercent: number;
  salePrice: number | null;
  /** Customer pays after promo: SalePrice × (1 − Promo). */
  netPrice: number | null;
  /** Checkout total: netPrice + TikTok shipping. */
  totalPay: number | null;
  /** Seller net after phí sàn, ads, and import cost. */
  netReceive: number | null;
  error?: string;
};

export function BatchPricingModal({
  open,
  variations,
  onCancel,
  onApply,
  activeFormula,
  activeTemplateId,
  onActiveFormulaChange,
}: BatchPricingModalProps) {
  const deps = useListingsDeps();
  const isMobile = useIsMobile();
  // Always subscribed so templates hydrate from React Query / localStorage before open.
  const { data: templates = [], isLoading, refetch } = deps.usePricingTemplates(true);
  const [form] = Form.useForm<PricingFormulaInput>();
  const [selectedTemplateId, setSelectedTemplateId] = useState<string | undefined>();
  const [manageOpen, setManageOpen] = useState(false);
  const [mobileTab, setMobileTab] = useState('params');

  const formulaValues = Form.useWatch([], form) as PricingFormulaInput | undefined;
  const targetMode = Form.useWatch('targetMode', form) as PricingTargetMode | undefined;

  const notifyFormulaChange = (formula: PricingFormulaInput, templateId?: string) => {
    onActiveFormulaChange?.(formula, templateId);
  };

  useEffect(() => {
    if (!open) {
      if (!activeFormula) setSelectedTemplateId(undefined);
      setMobileTab('params');
      return;
    }
    if (activeFormula) {
      setSelectedTemplateId(activeTemplateId);
      form.setFieldsValue(activeFormula);
      return;
    }
    const defaults = getDefaultPricingTemplate(templates);
    if (defaults) {
      setSelectedTemplateId(defaults.id);
      const next = templateToFormulaInput(defaults);
      form.setFieldsValue(next);
      notifyFormulaChange(next, defaults.id);
    } else {
      setSelectedTemplateId(undefined);
      const next = { ...DEFAULT_PRICING_FORMULA };
      form.setFieldsValue(next);
      notifyFormulaChange(next);
    }
    // Bind default only when opening the modal (not on every templates refetch).
    // eslint-disable-next-line react-hooks/exhaustive-deps -- intentional open-only bind
  }, [open]);

  useEffect(() => {
    if (!open || activeFormula || selectedTemplateId || templates.length === 0) return;
    const defaults = getDefaultPricingTemplate(templates);
    if (!defaults) return;
    setSelectedTemplateId(defaults.id);
    const next = templateToFormulaInput(defaults);
    form.setFieldsValue(next);
    notifyFormulaChange(next, defaults.id);
  }, [open, templates, selectedTemplateId, activeFormula, form]);

  const applyTemplate = (id: string | undefined) => {
    setSelectedTemplateId(id);
    if (!id) return;
    const tpl = templates.find((t) => t.id === id);
    if (!tpl) return;
    const next = templateToFormulaInput(tpl);
    form.setFieldsValue(next);
    notifyFormulaChange(next, id);
  };

  const formula: PricingFormulaInput = useMemo(
    () => templateToFormulaInput(formulaValues ?? DEFAULT_PRICING_FORMULA),
    [formulaValues],
  );

  const activePromo = resolvePromoPercent(formula.discountPercent, formula.fastSalePercent);

  const previewRows: PreviewRow[] = useMemo(
    () =>
      variations.map((row, i) => {
        const result = calculateSalePrice(row.costPrice, formula);
        const totalImport = resolveTotalImport(row.costPrice, formula);
        const totalCost = resolveTotalCost(row.costPrice, formula);
        const sku = row.costPrice != null && row.costPrice > 0 ? Number(row.costPrice) : null;
        const ship = formula.supplierShipping || 0;
        const tax = formula.tax || 0;
        const ads = formula.adsFee || 0;
        const costBreakdownTitle =
          sku != null
            ? `SKU $${sku.toFixed(2)} + Ship $${ship.toFixed(2)} + Tax $${tax.toFixed(2)} + Ads $${ads.toFixed(2)}`
            : 'Thiếu costPrice';
        const netPrice =
          result.salePrice != null
            ? roundMoney(result.salePrice * (1 - result.promoRate))
            : null;
        const totalPay =
          netPrice != null ? roundMoney(netPrice + (formula.tikTokShipping || 0)) : null;
        const netReceive = calculateNetReceive(result.salePrice, row.costPrice, formula);
        return {
          key: i,
          label: row.specValue || row.sellerSku || `Variant ${i + 1}`,
          cost: row.costPrice,
          totalImport,
          totalCost,
          costBreakdownTitle,
          promoPercent: result.promoPercent,
          salePrice: result.salePrice,
          netPrice,
          totalPay,
          netReceive,
          error: result.error,
        };
      }),
    [variations, formula],
  );

  const validCount = previewRows.filter((r) => r.salePrice != null).length;

  const lowProfitRisk = useMemo(() => {
    const rows = previewRows.filter(
      (r) => r.salePrice != null && isProfitBelowTotalCost(r.netReceive, r.totalCost),
    );
    if (rows.length === 0) return null;
    const worst = rows.reduce((a, b) =>
      (a.netReceive ?? 0) <= (b.netReceive ?? 0) ? a : b,
    );
    const profit = worst.netReceive ?? 0;
    const importCost = worst.totalCost ?? 0;
    const ordersToCoverRefund =
      profit > 0 ? Math.max(1, Math.ceil(importCost / profit)) : null;
    return { rows, worst, profit, importCost, ordersToCoverRefund };
  }, [previewRows]);

  const priceRange = useMemo(() => {
    const prices = previewRows
      .map((r) => r.salePrice)
      .filter((p): p is number => p != null);
    if (prices.length === 0) return null;
    const low = Math.min(...prices);
    const high = Math.max(...prices);
    return low === high ? `$${low.toFixed(2)}` : `$${low.toFixed(2)} – $${high.toFixed(2)}`;
  }, [previewRows]);

  const applyPrices = () => {
    onApply(previewRows.map((r) => r.salePrice), formula, selectedTemplateId);
    onCancel();
  };

  const handleApply = () => {
    if (validCount === 0) return;
    if (!lowProfitRisk) {
      applyPrices();
      return;
    }

    const { rows, profit, importCost, ordersToCoverRefund } = lowProfitRisk;
    Modal.confirm({
      title: 'Bạn định bán lỗ à?',
      icon: <ExclamationCircleFilled style={{ color: '#faad14' }} />,
      content: (
        <div>
          <p style={{ marginBottom: 8 }}>
            {rows.length}/{validCount} SKU có thực nhận{' '}
            <Text strong type="danger">
              ${profit.toFixed(2)}
            </Text>{' '}
            nhỏ hơn tổng cost{' '}
            <Text strong>${importCost.toFixed(2)}</Text>
            {rows.length > 1 ? ' (SKU thấp nhất)' : ''}.
          </p>
          <p style={{ marginBottom: 0 }}>
            {ordersToCoverRefund == null
              ? 'Đang lỗ hoặc hòa vốn — một đơn refund là cháy túi.'
              : `Khoảng ${ordersToCoverRefund} đơn thành công mới bù được 1 đơn refund.`}
          </p>
        </div>
      ),
      okText: 'Vẫn áp dụng',
      okButtonProps: { danger: true },
      cancelText: 'Điều chỉnh lại',
      onOk: applyPrices,
    });
  };

  const columns: ColumnsType<PreviewRow> = [
    {
      title: 'Variant',
      dataIndex: 'label',
      ellipsis: true,
    },
    {
      title: (
        <Tooltip title="SKU + Shipping nhập + Tax + Ads">
          <span>Tổng cost</span>
        </Tooltip>
      ),
      width: 92,
      render: (_, r) =>
        r.totalCost != null ? (
          <Text title={r.costBreakdownTitle}>${r.totalCost.toFixed(2)}</Text>
        ) : (
          '—'
        ),
    },
    {
      title: 'Promo',
      width: 62,
      render: (_, r) => `${r.promoPercent}%`,
    },
    {
      title: 'Giá bán',
      width: 88,
      render: (_, r) =>
        r.salePrice != null ? (
          <Text strong>${r.salePrice.toFixed(2)}</Text>
        ) : (
          <Text type="danger">{r.error || '—'}</Text>
        ),
    },
    {
      title: 'Sau giảm',
      width: 82,
      render: (_, r) =>
        r.netPrice != null ? (
          <Text type="secondary" title="Giá thực khách trả sau Discount/Fast Sale">
            ${r.netPrice.toFixed(2)}
          </Text>
        ) : (
          '—'
        ),
    },
    {
      title: 'Khách trả',
      width: 82,
      render: (_, r) =>
        r.totalPay != null ? (
          <Text strong title="Sau giảm + TT Shipping — tổng khách thanh toán">
            ${r.totalPay.toFixed(2)}
          </Text>
        ) : (
          '—'
        ),
    },
    {
      title: 'Thực nhận',
      width: 88,
      render: (_, r) =>
        r.netReceive != null ? (
          <Text
            type={r.netReceive >= 0 ? 'success' : 'danger'}
            title="Sau giảm × (1 − phí sàn) + TT Shipping − phí nhập − Ads"
          >
            ${r.netReceive.toFixed(2)}
          </Text>
        ) : (
          '—'
        ),
    },
  ];

  const promoSpan = isMobile ? 12 : 8;
  const targetModeSpan = isMobile ? 24 : 14;
  const targetValueSpan = isMobile ? 24 : 10;
  const itemGap = isMobile ? 8 : 10;

  const paramsForm = (
    <Form
      form={form}
      layout="vertical"
      className="fs-batch-pricing-form"
      initialValues={DEFAULT_PRICING_FORMULA}
      requiredMark={false}
      onValuesChange={(_, allValues) => {
        notifyFormulaChange(templateToFormulaInput(allValues), selectedTemplateId);
      }}
    >
      <Text strong className="fs-batch-pricing-section-title">
        Chi phí nhập & TikTok
      </Text>
      <Row gutter={[12, 0]}>
        <Col span={12}>
          <Form.Item name="supplierShipping" label="Shipping (nhập)" style={{ marginBottom: itemGap }}>
            <InputNumberWithUnit min={0} step={0.01} unit="USD" className="fs-batch-pricing-input" />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="tax" label="Tax" style={{ marginBottom: itemGap }}>
            <InputNumberWithUnit min={0} step={0.01} unit="USD" className="fs-batch-pricing-input" />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="adsFee" label="Ads Fee" style={{ marginBottom: itemGap }}>
            <InputNumberWithUnit min={0} step={0.01} unit="USD" className="fs-batch-pricing-input" />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="tikTokShipping" label="TT Shipping" style={{ marginBottom: itemGap }}>
            <InputNumberWithUnit min={0} step={0.01} unit="USD" className="fs-batch-pricing-input" />
          </Form.Item>
        </Col>
      </Row>

      <Text strong className="fs-batch-pricing-section-title">
        Khuyến mãi & phí sàn
      </Text>
      <Row gutter={[12, 0]}>
        <Col span={promoSpan}>
          <Form.Item name="discountPercent" label="Discount" style={{ marginBottom: itemGap }}>
            <InputNumberWithUnit min={0} max={99.99} step={0.1} unit="%" className="fs-batch-pricing-input" />
          </Form.Item>
        </Col>
        <Col span={promoSpan}>
          <Form.Item name="fastSalePercent" label="Fast Sale" style={{ marginBottom: itemGap }}>
            <InputNumberWithUnit min={0} max={99.99} step={0.1} unit="%" className="fs-batch-pricing-input" />
          </Form.Item>
        </Col>
        <Col span={isMobile ? 24 : 8}>
          <Form.Item name="platformFeePercent" label="Phí sàn" style={{ marginBottom: itemGap }}>
            <InputNumberWithUnit min={0} max={99.99} step={0.1} unit="%" className="fs-batch-pricing-input" />
          </Form.Item>
        </Col>
      </Row>

      <Text strong className="fs-batch-pricing-section-title">
        Mục tiêu lợi nhuận
      </Text>
      <Row gutter={[12, 0]} align="bottom">
        <Col span={targetModeSpan}>
          <Form.Item name="targetMode" label="Chế độ" style={{ marginBottom: isMobile ? itemGap : 0 }}>
            <Radio.Group
              className="fs-batch-pricing-target"
              optionType="button"
              buttonStyle="solid"
              options={[
                { label: 'Margin %', value: 'margin' },
                { label: 'Profit USD', value: 'profit' },
              ]}
            />
          </Form.Item>
        </Col>
        <Col span={targetValueSpan}>
          {targetMode === 'profit' ? (
            <Form.Item name="profitAmount" label="Profit" style={{ marginBottom: 0 }}>
              <InputNumberWithUnit min={0} step={0.01} unit="USD" className="fs-batch-pricing-input" />
            </Form.Item>
          ) : (
            <Form.Item
              name="marginPercent"
              label={isMobile ? 'Margin' : 'Margin trên phí nhập'}
              style={{ marginBottom: 0 }}
            >
              <InputNumberWithUnit min={0} step={0.1} unit="%" className="fs-batch-pricing-input" />
            </Form.Item>
          )}
        </Col>
      </Row>
    </Form>
  );

  const previewCards = (
    <div className="fs-batch-pricing-preview-cards">
      {previewRows.length === 0 ? (
        <Text type="secondary">Chưa có variant.</Text>
      ) : (
        previewRows.map((r) => (
          <div key={r.key} className="fs-batch-pricing-preview-card">
            <div className="fs-batch-pricing-preview-card-top">
              <Text strong ellipsis style={{ flex: 1, minWidth: 0 }}>
                {r.label}
              </Text>
              <Text type="secondary" style={{ flexShrink: 0 }} title={r.costBreakdownTitle}>
                Tổng cost {r.totalCost != null ? `$${r.totalCost.toFixed(2)}` : '—'}
              </Text>
            </div>
            {r.salePrice == null ? (
              <Text type="danger">{r.error || '—'}</Text>
            ) : (
              <div className="fs-batch-pricing-preview-card-metrics">
                <span>
                  <Text type="secondary">Giá bán</Text>
                  <Text strong>${r.salePrice.toFixed(2)}</Text>
                </span>
                <span>
                  <Text type="secondary">Sau giảm</Text>
                  <Text type="secondary">
                    {r.netPrice != null ? `$${r.netPrice.toFixed(2)}` : '—'}
                  </Text>
                </span>
                <span>
                  <Text type="secondary">Khách trả</Text>
                  <Text strong>{r.totalPay != null ? `$${r.totalPay.toFixed(2)}` : '—'}</Text>
                </span>
                <span>
                  <Text type="secondary">Thực nhận</Text>
                  <Text type={r.netReceive != null && r.netReceive >= 0 ? 'success' : 'danger'} strong>
                    {r.netReceive != null ? `$${r.netReceive.toFixed(2)}` : '—'}
                  </Text>
                </span>
              </div>
            )}
          </div>
        ))
      )}
    </div>
  );

  const previewPanel = (
    <div className="fs-batch-pricing-preview">
      {!isMobile ? (
        <Text strong style={{ display: 'block', marginBottom: 8 }}>
          Preview ({variations.length} SKU)
        </Text>
      ) : null}
      {isMobile ? (
        previewCards
      ) : (
        <Table
          rowKey="key"
          size="small"
          pagination={false}
          columns={columns}
          dataSource={previewRows}
          scroll={{ y: 320 }}
          locale={{ emptyText: 'Chưa có variant.' }}
        />
      )}
    </div>
  );

  return (
    <>
      <Modal
        className={`fs-batch-pricing-modal${isMobile ? ' is-mobile' : ''}`}
        title={isMobile ? 'Tính giá bán' : 'Batch tính giá bán'}
        open={open}
        onCancel={onCancel}
        onOk={handleApply}
        okText={isMobile ? `Áp dụng (${validCount})` : `Áp dụng (${validCount}/${variations.length})`}
        okButtonProps={{ disabled: validCount === 0 }}
        cancelText="Hủy"
        width={isMobile ? '100%' : 1240}
        centered={!isMobile}
        // Mount Form even while closed so Form.useWatch / setFieldsValue stay connected.
        forceRender
        // Keep content mounted after first open so reopen is instant.
        destroyOnHidden={false}
      >
        <div className="fs-batch-pricing-toolbar">
          <Select
            allowClear
            placeholder="Pricing template"
            className="fs-batch-pricing-template-select"
            loading={isLoading && templates.length === 0}
            value={selectedTemplateId}
            options={templates.map((t) => ({
              label: t.isDefault ? `${t.name} (Default)` : t.name,
              value: t.id,
            }))}
            onChange={(id) => applyTemplate(id)}
          />
          <Tooltip title="Quản lý template">
            <Button
              icon={<SettingOutlined />}
              onClick={() => setManageOpen(true)}
              aria-label="Quản lý"
            >
              {isMobile ? null : 'Quản lý'}
            </Button>
          </Tooltip>
          {!isMobile ? (
            <Text type="secondary">Cost lấy từ từng SKU · phí còn lại từ form / template</Text>
          ) : null}
        </div>

        {isMobile && priceRange ? (
          <div className="fs-batch-pricing-summary">
            <Text type="secondary">Giá bán </Text>
            <Text strong>{priceRange}</Text>
            <Text type="secondary"> · Promo {activePromo}%</Text>
          </div>
        ) : null}

        {lowProfitRisk ? (
          <Alert
            className="fs-batch-pricing-low-profit-alert"
            type="warning"
            showIcon
            style={{ marginBottom: 12 }}
            title={
              lowProfitRisk.ordersToCoverRefund == null
                ? `Bạn định bán lỗ à? ${lowProfitRisk.rows.length}/${validCount} SKU thực nhận ≤ $0 — 1 refund là cháy túi.`
                : `Bạn định bán lỗ à? ${lowProfitRisk.rows.length}/${validCount} SKU thực nhận < tổng cost (SKU+ship+tax+ads) — khoảng ${lowProfitRisk.ordersToCoverRefund} đơn success mới bù 1 refund.`
            }
          />
        ) : null}

        {isMobile ? (
          <Tabs
            className="fs-batch-pricing-tabs"
            size="small"
            activeKey={mobileTab}
            onChange={setMobileTab}
            items={[
              {
                key: 'params',
                label: 'Thông số',
                children: paramsForm,
              },
              {
                key: 'preview',
                label: `Preview (${variations.length})`,
                children: previewPanel,
              },
            ]}
          />
        ) : (
          <div className="fs-batch-pricing-body">
            {paramsForm}
            {previewPanel}
          </div>
        )}
      </Modal>

      <ManagePricingTemplateModal
        open={manageOpen}
        onClose={() => {
          setManageOpen(false);
          void refetch();
        }}
        seed={formula}
      />
    </>
  );
}
