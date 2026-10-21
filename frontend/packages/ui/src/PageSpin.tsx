import { Spin, type SpinProps } from 'antd';
import type { CSSProperties, ReactNode } from 'react';

/** Overlay / vùng list·dashboard trên trang chính (đồng bộ mọi module). */
export const PAGE_CONTENT_SPIN_SIZE: NonNullable<SpinProps['size']> = 'medium';

/** Full-page boot (route Suspense, auth hydrate, edit form lần đầu). */
export const PAGE_BOOT_SPIN_SIZE: NonNullable<SpinProps['size']> = 'large';

/** Thumbnail / inline control. */
export const INLINE_SPIN_SIZE: NonNullable<SpinProps['size']> = 'small';

type PageContentSpinProps = {
  spinning?: boolean;
  tip?: string;
  children?: ReactNode;
  style?: CSSProperties;
  className?: string;
};

/**
 * Spinner chuẩn cho vùng dữ liệu trang chính (list, KPI, chart).
 * Không dùng size khác trên từng trang — tránh lệch visual.
 * Class `page-content-spin` + CSS: indicator neo trong viewport (antd 6 mặc định
 * `top: 50%` theo chiều cao children → trang dài thì spinner xuống cuối, không thấy).
 */
export const PageContentSpin = ({
  spinning = false,
  tip,
  children,
  style,
  className,
}: PageContentSpinProps) => (
  <Spin
    spinning={spinning}
    size={PAGE_CONTENT_SPIN_SIZE}
    description={tip}
    style={style}
    className={['page-content-spin', className].filter(Boolean).join(' ')}
  >
    {children}
  </Spin>
);

/** Spinner giữa khối rỗng (drawer body, section chưa có children). */
export const PageContentSpinCenter = ({
  tip,
  minHeight = 240,
}: {
  tip?: string;
  minHeight?: number;
}) => (
  <div
    style={{
      minHeight,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      width: '100%',
    }}
  >
    <Spin size={PAGE_CONTENT_SPIN_SIZE} description={tip} />
  </div>
);

/** Full viewport / route chunk load — căn giữa vùng content nhìn thấy. */
export const PageBootSpin = ({ tip = 'Đang tải…' }: { tip?: string }) => (
  <div className="page-boot-spin">
    <Spin size={PAGE_BOOT_SPIN_SIZE} description={tip} />
  </div>
);
