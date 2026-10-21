import { forwardRef, type ReactNode } from 'react';
import { Button, InputNumber, Space, type InputNumberProps } from 'antd';

export type InputNumberWithUnitProps = Omit<
  InputNumberProps,
  'addonAfter' | 'addonBefore'
> & {
  /** Static unit shown after the input (antd 6: replace deprecated `addonAfter`). */
  unit: ReactNode;
};

/**
 * Form-friendly InputNumber + unit via Space.Compact (forwards value/onChange/ref).
 */
export const InputNumberWithUnit = forwardRef<
  React.ComponentRef<typeof InputNumber>,
  InputNumberWithUnitProps
>(function InputNumberWithUnit({ unit, className, style, ...rest }, ref) {
  return (
    <Space.Compact className={className} style={{ width: '100%', ...style }}>
      <InputNumber ref={ref} style={{ width: '100%' }} {...rest} />
      <Button disabled tabIndex={-1}>
        {unit}
      </Button>
    </Space.Compact>
  );
});
