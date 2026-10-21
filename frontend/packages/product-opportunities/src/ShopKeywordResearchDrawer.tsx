import { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Drawer, Space, Tag, Tooltip, Typography } from 'antd';
import { BulbOutlined, CaretDownOutlined, CaretUpOutlined, LoadingOutlined, ReloadOutlined } from '@ant-design/icons';
import type {
  KeywordMatchPhrase,
  ShopKeywordResearch,
  ShopKeywordResearchItem,
} from '@drop/types/productOpportunity';
import { formatBodySizeLabel, formatStyleLabel } from '@drop/types/shop';
import { formatDateTimeVn } from '@drop/utils/datetime';
import { useProductOpportunitiesDeps } from './ProductOpportunitiesDepsContext';
import { patchShopResearchSaved, removeShopResearchKeywords } from './shopKeywordResearchCache';
import {
  buildSheinSearchUrl,
  isSameSearchQuery,
} from './audienceSearchQuery';
import { SheinSearchLink, copySourcingQuery } from './KeywordSourcing';

const { Text } = Typography;

const formatVolume = (value: number) => value.toLocaleString('en-US');

const stripDuplicateVolumeMetric = (text: string) => {
  const raw = text.trim();
  if (!raw) return raw;
  const hadTerminal = /[.!?]$/.test(raw);
  let next = raw.replace(/[\s,;:—–-]*\(?\bvol(?:ume)?\s*:?\s*\d[\d.,]*\)?/gi, '').trim();
  next = next.replace(/[,;:—–\-\s]+$/g, '').trim();
  if (next && hadTerminal && !/[.!?]$/.test(next)) next += '.';
  return next;
};

const isAudienceSuggest = (source?: string | null) => source === 'audienceSuggest';

const snapshotChangedHint = (suggestMode: boolean, snapshotCount: number) => {
  if (!suggestMode) return 'Snapshot keyword đã đổi sau lần phân tích — có thể Phân tích lại.';
  if (snapshotCount > 0) return 'Snapshot keyword đã đổi — Phân tích lại có thể chấm được keyword thật.';
  return 'Shop đã có snapshot Trending TikTok — Phân tích lại để chấm keyword thật.';
};

const researchScanHint = (
  suggestMode: boolean,
  snapshotCount: number,
  droppedGeneric: number,
  candidatesScored: number,
) => {
  if (suggestMode && snapshotCount <= 0) return 'Đề xuất theo ngách (chưa có snapshot TikTok)';
  return `Quét ${snapshotCount} · loại generic ${droppedGeneric} · chấm ${candidatesScored}${suggestMode ? ' · đề xuất ngách' : ''}`;
};

const formatScoreTooltip = (row: ShopKeywordResearchItem): string | undefined => {
  const parts = row.scoreBreakdown;
  if (!parts || row.score == null) return undefined;
  const lines = [
    `Điểm ${row.score}/100`,
    `Khớp ngách ${parts.audience}`,
    `Nhu cầu ${parts.demand}`,
  ];
  if (parts.growth != null) lines.push(`Tăng trưởng ${parts.growth}`);
  lines.push(`Mùa ${parts.season}`, `Cạnh tranh ${parts.competition}`);
  return lines.join(' · ');
};

type RingDir = 'up' | 'down' | 'flat';

const parseRing = (ring?: string | null): { dir: RingDir; label: string } | null => {
  const raw = ring?.trim() ?? '';
  if (!raw) return null;
  const compact = raw.replace(/,/g, '').replace(/−/g, '-').replace(/\s/g, '');
  const match = compact.match(/^([+-])?(\d+(?:\.\d+)?)(%?)$/);
  if (!match) return { dir: 'flat', label: raw };
  const sign = match[1];
  const n = Number(match[2]);
  const label = `${match[2]}${match[3] ?? ''}`;
  if (sign === '-' || n < 0) return { dir: n === 0 ? 'flat' : 'down', label };
  if (n === 0) return { dir: 'flat', label };
  return { dir: 'up', label };
};

const SubscribeIcon = ({ filled }: { filled: boolean }) => (
  <svg viewBox="0 0 48 48" width="12" height="12" aria-hidden>
    {filled ? (
      <path
        fill="currentColor"
        d="M10 6c0-1.1.9-2 2-2h24a2 2 0 0 1 2 2v36.14c0 1.3-1.47 2.04-2.51 1.25L24.51 35.01a.8.8 0 0 0-1.02 0L12.51 43.39C11.47 44.18 10 43.44 10 42.14V6Z"
      />
    ) : (
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth="3.5"
        strokeLinejoin="round"
        d="M12 4.5h24A1.5 1.5 0 0 1 37.5 6v35.9c0 .9-1.02 1.42-1.74.87L24.35 34.7a.7.7 0 0 0-.7 0L12.24 42.77c-.72.55-1.74.03-1.74-.87V6A1.5 1.5 0 0 1 12 4.5Z"
      />
    )}
  </svg>
);

const synonymHint = (phrase: KeywordMatchPhrase) => {
  const extras = (phrase.anyOf ?? []).filter(
    (x) => x.trim().toLowerCase() !== phrase.phrase.trim().toLowerCase(),
  );
  if (extras.length === 0) return phrase.phrase;
  return `${phrase.phrase} · đồng nghĩa: ${extras.join(', ')}`;
};

const GARMENT_CHIP_NOUNS = new Set([
  'dress', 'dresses', 'gown', 'sundress',
  'top', 'tops', 'shirt', 'shirts', 'tee', 't-shirt', 'tshirt', 'blouse', 'blouses',
  'pant', 'pants', 'trouser', 'trousers', 'jean', 'jeans',
  'skirt', 'skirts', 'shorts',
  'set', 'sets', 'sweater', 'sweaters', 'jumper', 'jumpers',
  'jacket', 'jackets', 'coat', 'coats', 'blazer', 'blazers',
  'vest', 'vests', 'waistcoat', 'waistcoats',
  'romper', 'rompers', 'jumpsuit', 'jumpsuits', 'bodysuit', 'bodysuits',
  'hoodie', 'hoodies', 'cardigan', 'cardigans',
  'legging', 'leggings', 'overall', 'overalls', 'dungaree', 'dungarees',
  'pinafore', 'pinafores', 'cami', 'camisole', 'tank',
  'kimono', 'poncho', 'trench', 'parka',
  'swimsuit', 'bikini', 'coverup', 'cover-up',
]);

const isGarmentChip = (phrase?: string | null) =>
  GARMENT_CHIP_NOUNS.has(String(phrase || '').trim().toLowerCase());

const MatchPhraseChips = ({ row }: { row: ShopKeywordResearchItem }) => {
  const must = (row.mustPhrases ?? []).filter((p) => p.phrase?.trim());
  const should = (row.shouldPhrases ?? []).filter((p) => p.phrase?.trim());
  if (must.length === 0 && should.length === 0) return null;
  const distinctiveN = must.filter((p) => !isGarmentChip(p.phrase)).length;
  const minHits =
    row.minDistinctiveHits != null && Number.isFinite(row.minDistinctiveHits)
      ? Math.max(0, Math.floor(row.minDistinctiveHits))
      : Math.ceil(distinctiveN / 2);
  const coverageHint = `Khớp ≥ ${minHits}/${distinctiveN} từ định danh + loại SP`;
  return (
    <div className="shop-keyword-research-match">
      {must.map((p) => (
        <Tooltip
          key={`must-${p.phrase}`}
          title={`${coverageHint}. ${synonymHint(p)}`}
        >
          <Tag className="shop-keyword-research-match-must" bordered={false}>
            {p.phrase}
          </Tag>
        </Tooltip>
      ))}
      {should.map((p) => (
        <Tooltip
          key={`should-${p.phrase}`}
          title={`Ưu tiên khi chấm điểm. ${synonymHint(p)}`}
        >
          <Tag className="shop-keyword-research-match-should" bordered={false}>
            {p.phrase}
          </Tag>
        </Tooltip>
      ))}
    </div>
  );
};

const RingMeta = ({ ring }: { ring?: string | null }) => {
  const parsed = parseRing(ring);
  if (!parsed) return null;
  return (
    <span className={`product-opportunities-meta product-opportunities-ring-${parsed.dir}`}>
      {parsed.dir === 'up' ? <CaretUpOutlined /> : null}
      {parsed.dir === 'down' ? <CaretDownOutlined /> : null}
      <span>{parsed.label}</span>
    </span>
  );
};

const KeywordRow = ({
  row,
  saving,
  showMetrics,
  onToggleSaved,
}: {
  row: ShopKeywordResearchItem;
  saving: boolean;
  showMetrics: boolean;
  onToggleSaved: (id: string, keyword: string, saved: boolean) => void;
}) => {
  const saved = !!row.isSaved;
  const online =
    row.onlineProducts != null ? formatVolume(row.onlineProducts) : (row.onlineProductsRaw ?? '—');
  const tags = (row.leadTagTexts ?? []).map((t) => t.trim()).filter(Boolean);

  const rationale = stripDuplicateVolumeMetric(row.rationale?.trim() ?? '');
  const fit = row.fit?.trim() ?? '';
  const searchQuery = row.searchQuery?.trim() || row.keyword.trim();
  const sheinUrl = row.sheinUrl?.trim() || (searchQuery ? buildSheinSearchUrl(searchQuery) : '');
  const showSourcing = searchQuery.length > 0 && !isSameSearchQuery(row.keyword, searchQuery);
  const rank = row.rank != null && row.rank > 0 ? row.rank : null;
  const score = row.score != null && row.score >= 0 ? row.score : null;
  const scoreTip = formatScoreTooltip(row);

  return (
    <div className="shop-keyword-research-row">
      <div className="shop-keyword-research-row-main">
        <div className="shop-keyword-research-row-title">
          {rank != null ? (
            <span className="shop-keyword-research-rank" aria-label={`Hạng ${rank}`}>
              #{rank}
            </span>
          ) : null}
          <Tooltip title={saved ? 'Bỏ lưu keyword' : 'Lưu keyword'}>
            <button
              type="button"
              className={`product-opportunities-subscribe${saved ? ' is-saved' : ''}`}
              aria-label={saved ? 'Bỏ lưu keyword' : 'Lưu keyword'}
              disabled={saving}
              onClick={() => onToggleSaved(row.id, row.keyword, !saved)}
            >
              {saving ? <LoadingOutlined /> : <SubscribeIcon filled={saved} />}
            </button>
          </Tooltip>
          <Tooltip title="Copy câu tìm Shein">
            <span
              className="product-opportunities-keyword-text"
              role="button"
              tabIndex={0}
              onClick={() => void copySourcingQuery(searchQuery, row.keyword)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  void copySourcingQuery(searchQuery, row.keyword);
                }
              }}
            >
              {row.keyword}
            </span>
          </Tooltip>
          <SheinSearchLink url={sheinUrl} />
        </div>
        {showMetrics || tags.length > 0 || score != null ? (
          <div className="shop-keyword-research-row-side">
            {score != null ? (
              <Tooltip title={scoreTip}>
                <span className="shop-keyword-research-score">{score}</span>
              </Tooltip>
            ) : null}
            {showMetrics ? (
              <>
                <span>Vol {formatVolume(row.searchVolume)}</span>
                <RingMeta ring={row.searchVolumeRingRatio} />
                <span>SP {online}</span>
              </>
            ) : null}
            {tags.map((tag) => (
              <Tag key={tag} className="app-tag-compact" bordered={false}>
                {tag}
              </Tag>
            ))}
          </div>
        ) : null}
      </div>
      <MatchPhraseChips row={row} />
      {showSourcing ? (
        <div className="shop-keyword-research-sourcing">
          <Tooltip title="Copy câu tìm Shein">
            <span
              role="button"
              tabIndex={0}
              onClick={() => void copySourcingQuery(searchQuery, row.keyword)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  void copySourcingQuery(searchQuery, row.keyword);
                }
              }}
            >
              Tìm: {searchQuery}
            </span>
          </Tooltip>
        </div>
      ) : null}
      {rationale || fit ? (
        <div className="shop-keyword-research-row-note">
          {rationale ? (
            <div className="shop-keyword-research-rationale">
              <Tooltip title={rationale}>
                <span>{rationale}</span>
              </Tooltip>
            </div>
          ) : (
            <span className="shop-keyword-research-rationale" />
          )}
          {fit ? (
            <div className="shop-keyword-research-fit">
              <Tooltip title={fit}>
                <span>{fit}</span>
              </Tooltip>
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
};

export type ShopKeywordResearchDrawerProps = {
  open: boolean;
  shopId?: string;
  isMobile: boolean;
  /** True when the current shop list has at least one trending keyword (loading copy). */
  hasSnapshotKeywords?: boolean;
  onClose: () => void;
};

export const ShopKeywordResearchDrawer = ({
  open,
  shopId,
  isMobile,
  hasSnapshotKeywords = false,
  onClose,
}: ShopKeywordResearchDrawerProps) => {
  const deps = useProductOpportunitiesDeps();
  const researchMutation = deps.useShopResearch();
  const cacheQuery = deps.useGetShopResearch(shopId, open);
  const setSavedMutation = deps.useSetSaved();
  const [data, setData] = useState<ShopKeywordResearch | null>(null);
  const [error, setError] = useState<string | null>(null);
  const autoRanForShop = useRef<string | null>(null);

  const runResearch = useCallback(
    async (id: string) => {
      setError(null);
      try {
        const response = await researchMutation.mutateAsync(id);
        if (!response.success || !response.data) {
          throw new Error(response.message || 'Phân tích thất bại.');
        }
        setData(response.data);
      } catch (err) {
        const msg = err instanceof Error ? err.message : 'Phân tích thất bại.';
        setError(msg);
      }
    },
    [researchMutation],
  );

  useEffect(() => {
    setData(null);
    setError(null);
  }, [shopId]);

  useEffect(() => {
    if (!open || !shopId) return;
    if (cacheQuery.isLoading || cacheQuery.isFetching) return;
    if (researchMutation.isPending || setSavedMutation.isPending) return;
    if (cacheQuery.isError) {
      const msg =
        cacheQuery.error instanceof Error
          ? cacheQuery.error.message
          : 'Không tải được phân tích.';
      setError(msg);
      return;
    }
    if (cacheQuery.data) {
      setData(cacheQuery.data);
      setError(null);
    }
  }, [
    open,
    shopId,
    cacheQuery.isLoading,
    cacheQuery.isFetching,
    cacheQuery.isError,
    cacheQuery.error,
    cacheQuery.data,
    researchMutation.isPending,
    setSavedMutation.isPending,
  ]);

  useEffect(() => {
    if (!open || !shopId) return;
    if (cacheQuery.isLoading || cacheQuery.isFetching) return;
    if (cacheQuery.isError) return;
    if (cacheQuery.data) return;
    if (!cacheQuery.isSuccess) return;
    if (researchMutation.isPending) return;
    if (data) return;
    if (autoRanForShop.current === shopId) return;
    autoRanForShop.current = shopId;
    void runResearch(shopId);
    // mutateAsync identity is unstable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    open,
    shopId,
    cacheQuery.isLoading,
    cacheQuery.isFetching,
    cacheQuery.isSuccess,
    cacheQuery.isError,
    cacheQuery.data,
    researchMutation.isPending,
    data,
  ]);

  const handleToggleSaved = async (id: string, keyword: string, saved: boolean) => {
    try {
      const suggest = isAudienceSuggest(data?.source);
      const response = await setSavedMutation.mutateAsync(
        suggest && shopId ? { id, saved, shopId, keyword } : { id, saved, shopId },
      );
      const nextSaved = response.data?.saved ?? saved;
      setData((prev) => {
        if (!prev) return prev;
        if (!nextSaved && suggest) {
          return removeShopResearchKeywords(prev, keyword, id);
        }
        return patchShopResearchSaved(prev, id, nextSaved);
      });
    } catch {
      /* toast from mutation onError */
    }
  };

  const ageLabel =
    data?.ageMin != null && data?.ageMax != null ? `${data.ageMin}–${data.ageMax}` : null;
  const listingLabel =
    data?.publishLimitPublished != null || data?.publishLimitTotal != null
      ? `${data.publishLimitPublished ?? 0}/${data.publishLimitTotal ?? '—'}`
      : null;

  const loadingCache = open && !!shopId && cacheQuery.isLoading && !data;
  const loadingAi = researchMutation.isPending && !data;
  const suggestMode = isAudienceSuggest(data?.source);

  return (
    <Drawer
      title="Shop này bán gì"
      open={open}
      onClose={onClose}
      size={isMobile ? '100%' : 720}
      destroyOnClose={false}
      extra={
        <Button
          size="small"
          icon={<ReloadOutlined />}
          loading={researchMutation.isPending}
          disabled={!shopId || researchMutation.isPending}
          onClick={() => {
            if (shopId) {
              autoRanForShop.current = shopId;
              void runResearch(shopId);
            }
          }}
        >
          Phân tích lại
        </Button>
      }
    >
      {loadingCache ? (
        <Text type="secondary" style={{ fontSize: 12 }}>
          Đang tải phân tích đã lưu…
        </Text>
      ) : null}
      {loadingAi ? (
        <Text type="secondary" style={{ fontSize: 12 }}>
          {hasSnapshotKeywords
            ? 'Đang phân tích toàn bộ keyword của shop…'
            : 'Đang đề xuất keyword theo tệp khách…'}
        </Text>
      ) : null}
      {error ? (
        <Text type="danger" style={{ fontSize: 12, display: 'block', marginBottom: data ? 8 : 0 }}>
          {error}
        </Text>
      ) : null}
      {data ? (
        <div className="shop-keyword-research">
          <div className="shop-keyword-research-chips">
            {(data.categories ?? []).map((name) => (
              <Tag key={name} className="app-tag-compact" bordered={false}>
                {name}
              </Tag>
            ))}
            {ageLabel ? (
              <Tag className="app-tag-compact" bordered={false}>
                {ageLabel}
              </Tag>
            ) : null}
            {data.bodySizeCode ? (
              <Tag className="app-tag-compact" bordered={false}>
                {formatBodySizeLabel(data.bodySizeCode)}
              </Tag>
            ) : null}
            {(data.styleCodes ?? []).map((code) => (
              <Tag key={code} className="app-tag-compact" bordered={false}>
                {formatStyleLabel(code)}
              </Tag>
            ))}
            {suggestMode ? (
              <Tag className="app-tag-compact" bordered={false}>
                Đề xuất
              </Tag>
            ) : null}
            {listingLabel ? (
              <Tag className="app-tag-compact" bordered={false}>
                Listing {listingLabel}
              </Tag>
            ) : null}
          </div>
          {data.weatherNote ? (
            <div className="shop-keyword-research-meta">{data.weatherNote}</div>
          ) : null}
          {data.analyzedAt ? (
            <div className="shop-keyword-research-meta">
              Phân tích {formatDateTimeVn(data.analyzedAt)}
            </div>
          ) : null}
          {data.snapshotChanged ? (
            <div className="shop-keyword-research-warn">
              {snapshotChangedHint(suggestMode, data.snapshotCount)}
            </div>
          ) : null}
          <div className="shop-keyword-research-meta">
            {researchScanHint(
              suggestMode,
              data.snapshotCount,
              data.droppedGeneric,
              data.candidatesScored,
            )}
          </div>
          {data.upcomingEvents.length > 0 ? (
            <div className="shop-keyword-research-meta">
              Sự kiện: {data.upcomingEvents.join('; ')}
            </div>
          ) : null}
          {(data.warnings ?? []).map((w) => (
            <div key={w} className="shop-keyword-research-warn">
              {w}
            </div>
          ))}
          <ul className="shop-keyword-research-points">
            {(data.analysisPoints ?? []).map((point) => (
              <li key={point}>{point}</li>
            ))}
          </ul>
          <div className="shop-keyword-research-section is-now">
            <span className="shop-keyword-research-section-label">
              <span className="shop-keyword-research-section-dot" aria-hidden />
              List ngay
            </span>
            <span className="shop-keyword-research-section-count">{data.listNow.length}</span>
          </div>
          {data.listNow.length === 0 ? (
            <Text type="secondary" style={{ fontSize: 12 }}>
              Không có keyword khớp để list ngay.
            </Text>
          ) : (
            data.listNow.map((row) => (
              <KeywordRow
                key={row.id}
                row={row}
                showMetrics={!suggestMode}
                saving={setSavedMutation.isPending && setSavedMutation.variables?.id === row.id}
                onToggleSaved={(id, keyword, saved) => void handleToggleSaved(id, keyword, saved)}
              />
            ))
          )}
          <div className="shop-keyword-research-section is-upcoming">
            <span className="shop-keyword-research-section-label">
              <span className="shop-keyword-research-section-dot" aria-hidden />
              Đón đầu
            </span>
            <span className="shop-keyword-research-section-count">{data.upcoming.length}</span>
          </div>
          {data.upcoming.length === 0 ? (
            <Text type="secondary" style={{ fontSize: 12 }}>
              Không có keyword đón mùa / sự kiện.
            </Text>
          ) : (
            data.upcoming.map((row) => (
              <KeywordRow
                key={row.id}
                row={row}
                showMetrics={!suggestMode}
                saving={setSavedMutation.isPending && setSavedMutation.variables?.id === row.id}
                onToggleSaved={(id, keyword, saved) => void handleToggleSaved(id, keyword, saved)}
              />
            ))
          )}
        </div>
      ) : null}
      {!researchMutation.isPending && !loadingCache && !data && !error ? (
        <Space>
          <BulbOutlined />
          <Text type="secondary" style={{ fontSize: 12 }}>
            Bấm Phân tích lại để chạy AI.
          </Text>
        </Space>
      ) : null}
    </Drawer>
  );
};
