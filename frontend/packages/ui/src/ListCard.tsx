import type { CSSProperties, HTMLAttributes, ReactNode } from 'react';

type DivProps = HTMLAttributes<HTMLDivElement>;

export const ListCard = ({
  selected,
  mobile,
  className,
  children,
  ...rest
}: DivProps & { selected?: boolean; mobile?: boolean }) => (
  <div
    className={[
      'app-list-card',
      selected ? 'is-selected' : '',
      mobile ? 'is-mobile' : '',
      className,
    ]
      .filter(Boolean)
      .join(' ')}
    {...rest}
  >
    {children}
  </div>
);

export const ListCardHead = ({ className, mobile, children, ...rest }: DivProps & { mobile?: boolean }) => (
  <div
    className={['app-list-card-head', mobile ? 'app-list-card-head--mobile' : '', className]
      .filter(Boolean)
      .join(' ')}
    {...rest}
  >
    {children}
  </div>
);

export const ListCardBody = ({
  className,
  columns,
  style,
  children,
  ...rest
}: DivProps & { columns?: string }) => (
  <div
    className={['app-list-card-body', 'admin-card-grid', className].filter(Boolean).join(' ')}
    style={{ ...(columns ? { gridTemplateColumns: columns } : null), ...style }}
    {...rest}
  >
    {children}
  </div>
);

export const ListHeader = ({
  className,
  columns,
  style,
  children,
  ...rest
}: DivProps & { columns?: string }) => (
  <div
    className={['app-list-header', className].filter(Boolean).join(' ')}
    style={{ ...(columns ? { gridTemplateColumns: columns } : null), ...style }}
    {...rest}
  >
    {children}
  </div>
);

export const ListLine = ({
  primary,
  isTitle,
  className,
  children,
  ...rest
}: DivProps & { primary?: boolean; isTitle?: boolean }) => (
  <div
    className={[
      'app-list-line',
      primary ? 'app-list-line--primary' : '',
      isTitle ? 'app-list-line--title' : '',
      className,
    ]
      .filter(Boolean)
      .join(' ')}
    {...rest}
  >
    {children}
  </div>
);

export const ListCol = ({ className, children, ...rest }: DivProps) => (
  <div className={['app-list-col', 'admin-col-meta', className].filter(Boolean).join(' ')} {...rest}>
    {children}
  </div>
);

export const ListPagination = ({
  className,
  mobile,
  style,
  children,
  ...rest
}: DivProps & { mobile?: boolean }) => (
  <div
    className={['app-list-pagination', className].filter(Boolean).join(' ')}
    style={{ justifyContent: mobile ? 'center' : undefined, ...style } as CSSProperties}
    {...rest}
  >
    {children}
  </div>
);

export const StatusDot = ({
  color,
  label,
  className,
  style,
  ...rest
}: DivProps & { color: string; label?: ReactNode }) => (
  <span
    className={['app-status-dot', className].filter(Boolean).join(' ')}
    aria-label={typeof label === 'string' ? label : undefined}
    style={{ background: color, ...style }}
    {...rest}
  />
);
