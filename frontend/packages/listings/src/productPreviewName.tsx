import { useEffect, useState } from 'react';
import { Button, Input, Space } from 'antd';
import { CheckOutlined, CloseOutlined, EditOutlined } from '@ant-design/icons';

export function ProductPreviewName({
  value,
  onChange,
  placeholder = 'Chưa có tên sản phẩm',
  maxLength = 255,
}: {
  value?: string;
  onChange?: (v: string) => void;
  placeholder?: string;
  maxLength?: number;
}) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(value ?? '');

  useEffect(() => {
    if (!editing) setDraft(value ?? '');
  }, [value, editing]);

  const commit = (next: string) => {
    onChange?.(next);
    setEditing(false);
  };

  const display = value?.trim() || placeholder;
  const isPlaceholder = !value?.trim();

  if (editing) {
    return (
      <div className="fs-product-preview-name fs-product-preview-name--editing">
        <Space.Compact style={{ width: '100%' }}>
          <Input
            autoFocus
            value={draft}
            maxLength={maxLength}
            placeholder={placeholder}
            onChange={(e) => setDraft(e.target.value)}
            onPressEnter={() => commit(draft.trim())}
          />
          <Button type="primary" icon={<CheckOutlined />} onClick={() => commit(draft.trim())} />
          <Button
            icon={<CloseOutlined />}
            onClick={() => {
              setDraft(value ?? '');
              setEditing(false);
            }}
          />
        </Space.Compact>
      </div>
    );
  }

  return (
    <div className="fs-product-preview-name">
      <span
        className={`fs-product-preview-name-text${isPlaceholder ? ' is-placeholder' : ''}`}
        title={isPlaceholder ? undefined : value?.trim()}
      >
        {display}
      </span>
      <Button
        type="text"
        size="small"
        className="fs-product-preview-name-edit"
        icon={<EditOutlined />}
        title="Sửa tên"
        onClick={() => {
          setDraft(value ?? '');
          setEditing(true);
        }}
      />
    </div>
  );
}
