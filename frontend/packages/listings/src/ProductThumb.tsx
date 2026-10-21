import { useEffect, useRef, useState } from 'react';
import { Popover, Spin } from 'antd';
import { INLINE_SPIN_SIZE } from '@drop/ui';

export type ProductThumbProps = {
  src?: string | null;
  loading?: boolean;
  size?: number;
};

/**
 * Thumbnail sản phẩm dùng chung Listings + Listing analytics: lazy load, popover preview,
 * `referrerPolicy="no-referrer"` cho CDN TikTok/Shein.
 */
export function ProductThumb({ src, loading, size = 48 }: ProductThumbProps) {
  const imgRef = useRef<HTMLImageElement | null>(null);
  const [inView, setInView] = useState(false);
  const [previewReady, setPreviewReady] = useState(false);

  useEffect(() => {
    setInView(false);
    setPreviewReady(false);
  }, [src]);

  useEffect(() => {
    if (!src || inView) return;
    const el = imgRef.current;
    if (!el) return;

    if (typeof IntersectionObserver === 'undefined') {
      setInView(true);
      return;
    }

    const io = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setInView(true);
          io.disconnect();
        }
      },
      { rootMargin: '240px 0px', threshold: 0.01 },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [src, inView]);

  const emptyBox = (
    <div
      style={{
        width: size,
        height: size,
        background: 'var(--listing-thumb-bg)',
        borderRadius: 4,
        flexShrink: 0,
        position: 'relative',
      }}
    />
  );

  const thumbImg = !src ? (
    emptyBox
  ) : (
    <img
      ref={imgRef}
      src={inView ? src : undefined}
      alt=""
      width={size}
      height={size}
      referrerPolicy="no-referrer"
      loading="lazy"
      decoding="async"
      style={{
        objectFit: 'cover',
        borderRadius: 4,
        flexShrink: 0,
        display: 'block',
        background: 'var(--listing-thumb-bg)',
        cursor: 'pointer',
      }}
      onError={(e) => {
        e.currentTarget.style.visibility = 'hidden';
      }}
    />
  );

  const thumb = !src ? (
    emptyBox
  ) : (
    <Popover
      content={
        previewReady ? (
          <img
            src={src}
            alt=""
            referrerPolicy="no-referrer"
            decoding="async"
            style={{
              display: 'block',
              maxWidth: 320,
              maxHeight: 320,
              objectFit: 'contain',
              borderRadius: 4,
            }}
          />
        ) : (
          <div
            style={{
              width: 48,
              height: 48,
              background: 'var(--listing-thumb-bg)',
              borderRadius: 4,
            }}
          />
        )
      }
      trigger="hover"
      placement="right"
      mouseEnterDelay={0.25}
      arrow={false}
      styles={{ container: { padding: 1 } }}
      onOpenChange={(open) => {
        if (open) setPreviewReady(true);
      }}
    >
      <span style={{ flexShrink: 0, lineHeight: 0, display: 'inline-block' }}>{thumbImg}</span>
    </Popover>
  );

  if (!loading) return thumb;

  return (
    <div
      style={{
        position: 'relative',
        width: size,
        height: size,
        flexShrink: 0,
        borderRadius: 4,
        overflow: 'hidden',
      }}
    >
      {thumb}
      <div
        style={{
          position: 'absolute',
          inset: 0,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          background: 'var(--listing-spin-overlay)',
          borderRadius: 4,
          pointerEvents: 'none',
        }}
      >
        <Spin size={INLINE_SPIN_SIZE} />
      </div>
    </div>
  );
}
