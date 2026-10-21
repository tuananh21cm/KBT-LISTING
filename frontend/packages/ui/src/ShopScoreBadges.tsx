import { Tooltip } from 'antd';
import './shopScoreBadges.css';

/** Điểm Health Center của một shop (đồng bộ từ extension). */
export interface ShopScoreValues {
  /** AHR score — càng cao càng nhiều điểm phạt tích luỹ. */
  ahrScore?: number | null;
  /** Shop Performance Score tổng (0–5). */
  shopPerformanceScore?: number | null;
  productSatisfactionScore?: number | null;
  fulfillmentScore?: number | null;
  customerServiceScore?: number | null;
}

type Tone = 'good' | 'warn' | 'bad' | 'neutral';

/** Ngưỡng SPS theo mốc quyền lợi TikTok: 4.0 mở khoá benefit, dưới 3.0 bị hạn chế. */
function performanceTone(score: number): Tone {
  if (score >= 4) return 'good';
  if (score >= 3) return 'warn';
  return 'bad';
}

/**
 * AHR đếm điểm phạt: 0 là sạch, TikTok cảnh báo từ 12 và đình chỉ ở 24 với shop thường.
 * Số lớn (vd 190) là hạn mức mở rộng nên chỉ tô đỏ khi thật sự cao.
 */
function ahrTone(score: number): Tone {
  if (score <= 0) return 'good';
  if (score < 24) return 'warn';
  return 'bad';
}

function formatScore(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}

function TooltipRow({ label, value }: { label: string; value?: number | null }) {
  return (
    <div className="shop-score-tooltip-row">
      <span>{label}</span>
      <span>{value == null ? '—' : formatScore(value)}</span>
    </div>
  );
}

type ScoreChip = {
  key: string;
  label: string;
  value: number;
  tone: Tone;
};

function buildScoreChips(scores: ShopScoreValues, detail: 'compact' | 'full'): ScoreChip[] {
  const chips: ScoreChip[] = [];
  const sps = scores.shopPerformanceScore;
  if (sps != null) {
    chips.push({
      key: 'sps',
      label: 'Shop Performance Score',
      value: sps,
      tone: performanceTone(sps),
    });
  }

  if (detail === 'full') {
    const ps = scores.productSatisfactionScore;
    if (ps != null) {
      chips.push({
        key: 'ps',
        label: 'Product satisfaction',
        value: ps,
        tone: performanceTone(ps),
      });
    }
    const fl = scores.fulfillmentScore;
    if (fl != null) {
      chips.push({
        key: 'fl',
        label: 'Fulfillment and logistics',
        value: fl,
        tone: performanceTone(fl),
      });
    }
    const cs = scores.customerServiceScore;
    if (cs != null) {
      chips.push({
        key: 'cs',
        label: 'Customer service',
        value: cs,
        tone: performanceTone(cs),
      });
    }
  }

  const ahr = scores.ahrScore;
  if (ahr != null) {
    chips.push({
      key: 'ahr',
      label: 'AHR score',
      value: ahr,
      tone: ahrTone(ahr),
    });
  }

  return chips;
}

const CHIP_KEY_LABEL: Record<string, string> = {
  sps: 'SPS',
  ps: 'PS',
  fl: 'FL',
  cs: 'CS',
  ahr: 'AHR',
};

/**
 * Chip điểm Health Center cạnh tên shop.
 *
 * `compact` (mặc định): SPS + AHR, chi tiết 3 nhóm trong tooltip.
 * `full`: SPS + PS + FL + CS + AHR trên một dòng (Shop Tasks).
 *
 * Ẩn hoàn toàn khi shop chưa sync điểm nào.
 */
export function ShopScoreBadges({
  scores,
  className,
  detail = 'compact',
}: {
  scores?: ShopScoreValues | null;
  className?: string;
  /** `full` hiện đủ 5 điểm; `compact` chỉ SPS + AHR. */
  detail?: 'compact' | 'full';
}) {
  if (!scores) return null;

  const chips = buildScoreChips(scores, detail);
  if (chips.length === 0) return null;

  const tooltip =
    detail === 'compact' ? (
      <div>
        <TooltipRow label="Shop Performance Score" value={scores.shopPerformanceScore} />
        <TooltipRow label="Product satisfaction" value={scores.productSatisfactionScore} />
        <TooltipRow label="Fulfillment and logistics" value={scores.fulfillmentScore} />
        <TooltipRow label="Customer service" value={scores.customerServiceScore} />
        <TooltipRow label="AHR score" value={scores.ahrScore} />
      </div>
    ) : null;

  const badges = (
    <span
      className={[
        'shop-score-badges',
        detail === 'full' ? 'shop-score-badges--full' : null,
        className,
      ]
        .filter(Boolean)
        .join(' ')}
    >
      {chips.map((chip) => {
        const badge = (
          <span
            className={`shop-score-badge shop-score-badge--${chip.tone}`}
            aria-label={`${chip.label} ${formatScore(chip.value)}`}
          >
            <span className="shop-score-badge-key">{CHIP_KEY_LABEL[chip.key]}</span>
            {formatScore(chip.value)}
          </span>
        );

        if (detail === 'full') {
          return (
            <Tooltip key={chip.key} title={chip.label}>
              {badge}
            </Tooltip>
          );
        }

        return (
          <span key={chip.key}>
            {badge}
          </span>
        );
      })}
    </span>
  );

  if (!tooltip) return badges;

  return <Tooltip title={tooltip}>{badges}</Tooltip>;
}
