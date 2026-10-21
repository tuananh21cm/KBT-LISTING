import { useEffect, useMemo, useState, type ReactNode } from 'react';
import type { ListingAnalyticsTotals } from '@drop/types/listing';
import { Button, InputNumber } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { formatConversion, formatInt, formatMoney } from './listingAnalyticsFormat';
import {
  defaultCustomRates,
  projectFunnelFromRates,
  resolveFunnelRates,
  type CustomFunnelRates,
  type FunnelSnapshot,
} from './listingAnalyticsFunnelProjection';
import { ListingAnalyticsFunnelGuide } from './ListingAnalyticsFunnelGuide';

const formatDelta = (delta: number): string => {
  const rounded = Math.round(delta);
  if (rounded === 0) return '';
  return rounded > 0 ? `+${formatInt(rounded)}` : formatInt(rounded);
};

const fractionToInputPct = (fraction: number): number => {
  const pct = fraction * 100;
  if (!Number.isFinite(pct)) return 0;
  if (Math.abs(pct) < 0.1) return Math.round(pct * 1000) / 1000;
  if (Math.abs(pct) < 1) return Math.round(pct * 100) / 100;
  return Math.round(pct * 10) / 10;
};

const inputPctToFraction = (pct: number | null): number => {
  if (pct == null || !Number.isFinite(pct)) return 0;
  return Math.max(0, pct) / 100;
};

type EditableBridgeKey = keyof CustomFunnelRates;

const formatCvrDeltaPp = (deltaPp: number, isSimulating: boolean): string => {
  if (!isSimulating || Math.abs(deltaPp) < 0.00005) return '';
  const abs = Math.abs(deltaPp);
  const rounded =
    abs < 0.1 ? Math.round(deltaPp * 1000) / 1000 : Math.round(deltaPp * 10) / 10;
  if (rounded === 0) return '';
  return rounded > 0 ? `+${rounded} pp` : `${rounded} pp`;
};

type ValueWithDeltaProps = {
  display: string;
  delta?: string | null;
  valueClassName?: string;
};

const ValueWithDelta = ({ display, delta, valueClassName }: ValueWithDeltaProps) => {
  const deltaClass =
    delta != null && delta !== ''
      ? delta.startsWith('+') || (delta.startsWith('$') && !delta.startsWith('-$'))
        ? ' is-positive'
        : delta.startsWith('-')
          ? ' is-negative'
          : ''
      : '';

  return (
    <span className="listing-analytics-pipeline-node-value-row">
      <span
        className={`listing-analytics-pipeline-node-value${valueClassName ? ` ${valueClassName}` : ''}`}
      >
        {display}
      </span>
      {delta ? (
        <sup className={`listing-analytics-sim-delta${deltaClass}`}>{delta}</sup>
      ) : null}
    </span>
  );
};

type PipelineNodeProps = {
  label: string;
  value: number;
  baseline?: number;
  showDelta?: boolean;
  valueClassName?: string;
};

const PipelineNodeView = ({
  label,
  value,
  baseline,
  showDelta = false,
  valueClassName,
}: PipelineNodeProps) => {
  const delta =
    showDelta && baseline != null ? Math.round(value) - Math.round(baseline) : 0;

  return (
    <div className="listing-analytics-pipeline-node" role="listitem">
      <span className="listing-analytics-pipeline-node-label">{label}</span>
      <ValueWithDelta
        display={formatInt(value)}
        delta={delta !== 0 ? formatDelta(delta) : null}
        valueClassName={valueClassName}
      />
    </div>
  );
};

type EditableBridgeProps = {
  label: string;
  title: string;
  rateFraction: number;
  isEditing: boolean;
  onStartEdit: () => void;
  onCommit: (value: number | null) => void;
  onEndEdit: () => void;
};

const EditableBridgeView = ({
  label,
  title,
  rateFraction,
  isEditing,
  onStartEdit,
  onCommit,
  onEndEdit,
}: EditableBridgeProps) => (
  <div className="listing-analytics-pipeline-bridge listing-analytics-sim-bridge" title={title}>
    <span className="listing-analytics-pipeline-bridge-line" aria-hidden />
    {isEditing ? (
      <span className="listing-analytics-pipeline-bridge-chip listing-analytics-sim-chip is-editing">
        <span className="listing-analytics-sim-rate-row">
          <InputNumber
            size="small"
            min={0}
            max={100}
            step={0.1}
            controls={false}
            value={fractionToInputPct(rateFraction)}
            onChange={onCommit}
            onBlur={onEndEdit}
            onPressEnter={onEndEdit}
            className="listing-analytics-sim-rate-input"
            autoFocus
          />
          <span className="listing-analytics-sim-rate-suffix">%</span>
        </span>
        <span className="listing-analytics-pipeline-bridge-label">{label}</span>
      </span>
    ) : (
      <button
        type="button"
        className="listing-analytics-pipeline-bridge-chip is-editable"
        onClick={onStartEdit}
        aria-label={`Chỉnh ${label}`}
      >
        <span className="listing-analytics-pipeline-bridge-rate">
          {formatConversion(rateFraction)}
        </span>
        <span className="listing-analytics-pipeline-bridge-label">{label}</span>
      </button>
    )}
  </div>
);

export type ListingAnalyticsTotalsBarProps = {
  totals: ListingAnalyticsTotals;
  currency: string;
  isMobile?: boolean;
  /** Nội dung nằm cuối block, bên phải hướng dẫn funnel (đề xuất hủy list). */
  footerExtra?: ReactNode;
};

/**
 * Tổng theo bộ lọc — funnel có thể chỉnh tỷ lệ (click chip) và xem kết quả ngay.
 */
export function ListingAnalyticsTotalsBar({
  totals,
  currency,
  isMobile = false,
  footerExtra,
}: ListingAnalyticsTotalsBarProps) {
  const snapshot: FunnelSnapshot = useMemo(
    () => ({
      impressions: totals.impressions,
      clicks: totals.clicks,
      addToCartUnits: totals.addToCartUnits ?? 0,
      orders: totals.orders,
    }),
    [totals.impressions, totals.clicks, totals.addToCartUnits, totals.orders],
  );

  const baselineRates = useMemo(() => resolveFunnelRates(snapshot), [snapshot]);

  const baselineCustomRates = useMemo(
    () => defaultCustomRates(baselineRates),
    [baselineRates],
  );

  const [rateOverrides, setRateOverrides] = useState<CustomFunnelRates | null>(null);
  const [editingBridge, setEditingBridge] = useState<EditableBridgeKey | null>(null);

  useEffect(() => {
    setRateOverrides(null);
    setEditingBridge(null);
  }, [totals.impressions, totals.clicks, totals.addToCartUnits, totals.orders]);

  const targetRates = rateOverrides ?? baselineCustomRates;
  const isSimulating = rateOverrides != null;

  const projected =
    totals.impressions > 0 ? projectFunnelFromRates(totals.impressions, targetRates) : null;

  const resetSimulation = () => {
    setRateOverrides(null);
    setEditingBridge(null);
  };

  const actualClicks = totals.clicks;
  const actualAtc = totals.addToCartUnits ?? 0;
  const actualOrders = totals.orders;
  const actualCvr =
    totals.impressions > 0 ? totals.orders / totals.impressions : 0;

  const displayClicks =
    isSimulating && projected ? projected.clicks : actualClicks;
  const displayAtc =
    isSimulating && projected ? projected.addToCartUnits : actualAtc;
  const displayOrders =
    isSimulating && projected ? projected.orders : actualOrders;
  const displayCvr =
    isSimulating && projected ? projected.cvr : actualCvr;

  const aov = totals.orders > 0 ? totals.gmv / totals.orders : null;
  const displayGmv =
    isSimulating && projected && aov != null ? projected.orders * aov : totals.gmv;
  const gmvDelta =
    isSimulating && projected && aov != null ? displayGmv - totals.gmv : null;

  const cvrDeltaPp = (displayCvr - actualCvr) * 100;
  const cvrDeltaLabel = formatCvrDeltaPp(cvrDeltaPp, isSimulating);

  const showCtrBridge = totals.impressions > 0;
  const showAtcBridge = displayClicks > 0;
  const showOrderBridge = displayAtc > 0;

  return (
    <div className={`listing-analytics-summary-block${isMobile ? ' is-mobile' : ''}`}>
      <div className={`listing-analytics-summary${isMobile ? ' is-mobile' : ''}`}>
        {isSimulating ? (
          <Button
            type="text"
            size="small"
            className="listing-analytics-summary-reload"
            icon={<ReloadOutlined />}
            onClick={resetSimulation}
            aria-label="Khôi phục dữ liệu"
            title="Khôi phục dữ liệu"
          />
        ) : null}

        <div className="listing-analytics-summary-main">
          <div
            className={`listing-analytics-pipeline-track${isSimulating ? ' is-simulating' : ''}`}
            role="list"
            aria-label="Luồng chuyển đổi"
          >
            <PipelineNodeView label="Impressions" value={totals.impressions} />

            {showCtrBridge ? (
              <EditableBridgeView
                label="CTR"
                title="Click-through rate = Clicks ÷ Impressions — bấm để chỉnh"
                rateFraction={targetRates.ctr}
                isEditing={editingBridge === 'ctr'}
                onStartEdit={() => setEditingBridge('ctr')}
                onCommit={(value) =>
                  setRateOverrides((prev) => ({
                    ...(prev ?? baselineCustomRates),
                    ctr: inputPctToFraction(value),
                  }))
                }
                onEndEdit={() => setEditingBridge(null)}
              />
            ) : null}

            <PipelineNodeView
              label="Clicks"
              value={displayClicks}
              baseline={actualClicks}
              showDelta={isSimulating}
            />

            {showAtcBridge ? (
              <EditableBridgeView
                label="ATC"
                title="Add-to-cart rate = Add-to-cart ÷ Clicks — bấm để chỉnh"
                rateFraction={targetRates.atcRate}
                isEditing={editingBridge === 'atcRate'}
                onStartEdit={() => setEditingBridge('atcRate')}
                onCommit={(value) =>
                  setRateOverrides((prev) => ({
                    ...(prev ?? baselineCustomRates),
                    atcRate: inputPctToFraction(value),
                  }))
                }
                onEndEdit={() => setEditingBridge(null)}
              />
            ) : null}

            <PipelineNodeView
              label="Add-to-cart"
              value={displayAtc}
              baseline={actualAtc}
              showDelta={isSimulating}
            />

            {showOrderBridge ? (
              <EditableBridgeView
                label="Order"
                title="Order rate = Orders ÷ Add-to-cart — bấm để chỉnh"
                rateFraction={targetRates.orderFromAtc}
                isEditing={editingBridge === 'orderFromAtc'}
                onStartEdit={() => setEditingBridge('orderFromAtc')}
                onCommit={(value) =>
                  setRateOverrides((prev) => ({
                    ...(prev ?? baselineCustomRates),
                    orderFromAtc: inputPctToFraction(value),
                  }))
                }
                onEndEdit={() => setEditingBridge(null)}
              />
            ) : null}

            <PipelineNodeView
              label="Orders"
              value={displayOrders}
              baseline={actualOrders}
              showDelta={isSimulating}
            />

            {totals.impressions > 0 ? (
              <div
                className="listing-analytics-pipeline-endcap"
                role="listitem"
                title="Tỷ lệ chuyển đổi từ hiển thị → đơn = Orders ÷ Impressions"
              >
                <span className="listing-analytics-pipeline-endcap-chip">
                  <span className="listing-analytics-pipeline-endcap-rate-row">
                    <span className="listing-analytics-pipeline-endcap-rate">
                      {formatConversion(displayCvr)}
                    </span>
                    {cvrDeltaLabel ? (
                      <sup
                        className={`listing-analytics-sim-delta${
                          cvrDeltaPp > 0 ? ' is-positive' : cvrDeltaPp < 0 ? ' is-negative' : ''
                        }`}
                      >
                        {cvrDeltaLabel}
                      </sup>
                    ) : null}
                  </span>
                  <span className="listing-analytics-pipeline-endcap-label">CVR</span>
                </span>
                <span className="listing-analytics-pipeline-endcap-hint">Imp → Order</span>
              </div>
            ) : null}
          </div>
        </div>

        <div className="listing-analytics-results">
          <div className="listing-analytics-results-track">
            <div className="listing-analytics-pipeline-node" role="listitem">
              <span className="listing-analytics-pipeline-node-label">GMV</span>
              <ValueWithDelta
                display={formatMoney(displayGmv, currency)}
                delta={
                  isSimulating && gmvDelta != null && gmvDelta !== 0
                    ? gmvDelta > 0
                      ? `+${formatMoney(gmvDelta, currency)}`
                      : formatMoney(gmvDelta, currency)
                    : null
                }
                valueClassName="listing-analytics-results-gmv"
              />
            </div>
            {aov != null ? (
              <div className="listing-analytics-pipeline-node" role="listitem">
                <span className="listing-analytics-pipeline-node-label">AOV</span>
                <span className="listing-analytics-pipeline-node-value">
                  {formatMoney(aov, currency)}
                </span>
              </div>
            ) : null}
            <PipelineNodeView label="Products" value={totals.products} />
          </div>
        </div>
      </div>

      <div className="listing-analytics-summary-footer">
        <ListingAnalyticsFunnelGuide />
        {footerExtra}
      </div>
    </div>
  );
}
