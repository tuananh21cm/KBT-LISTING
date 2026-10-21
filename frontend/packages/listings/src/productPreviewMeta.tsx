import { Avatar } from 'antd';
import type { ProductSourceOption } from './localTypes';

export interface SourceDisplayInfo {
  name: string;
  logoUrl?: string;
}

function truncateUrl(url: string, max = 72): string {
  if (url.length <= max) return url;
  const head = Math.floor(max * 0.65);
  const tail = max - head - 1;
  return `${url.slice(0, head)}…${url.slice(-tail)}`;
}

export function resolveProductSource(
  sourceUrl: string | undefined,
  sourceId: string | undefined,
  options: ProductSourceOption[],
  fallback?: SourceDisplayInfo | null,
): SourceDisplayInfo | null {
  if (sourceId) {
    const hit = options.find((o) => o.id === sourceId);
    if (hit) return { name: hit.name, logoUrl: hit.logoUrl };
  }
  if (fallback?.name) return fallback;

  const url = sourceUrl?.trim();
  if (!url) return null;

  try {
    const host = new URL(url).hostname.toLowerCase();
    const hit = options.find((o) => host.includes(o.code.toLowerCase()));
    if (hit) return { name: hit.name, logoUrl: hit.logoUrl };
  } catch {
    /* ignore invalid URL */
  }

  return null;
}

export function ProductPreviewMeta({
  skuLabel,
  sourceUrl,
  sourceId,
  sourceOptions = [],
  fallbackSource,
}: {
  skuLabel: string;
  sourceUrl?: string;
  sourceId?: string;
  sourceOptions?: ProductSourceOption[];
  fallbackSource?: SourceDisplayInfo | null;
}) {
  const href = sourceUrl?.trim() ?? '';
  const source = resolveProductSource(href, sourceId, sourceOptions, fallbackSource);

  return (
    <div className="fs-product-preview-meta">
      <span className="fs-product-preview-meta-sku">SKU: {skuLabel}</span>
      {href ? (
        <>
          <span className="fs-product-preview-meta-sep">·</span>
          <span className="fs-product-preview-meta-source">
            {source ? (
              <Avatar src={source.logoUrl} shape="square" size={16}>
                {source.name.charAt(0)}
              </Avatar>
            ) : null}
            <a
              href={href}
              target="_blank"
              rel="noreferrer"
              title={href}
              className="fs-product-preview-source-link"
            >
              {truncateUrl(href)}
            </a>
          </span>
        </>
      ) : null}
    </div>
  );
}
