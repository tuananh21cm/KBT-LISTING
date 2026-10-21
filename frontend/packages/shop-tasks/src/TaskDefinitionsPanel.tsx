import { useRef, useState, type DragEvent } from 'react';
import {
  Button,
  Form,
  Input,
  InputNumber,
  Popconfirm,
  Result,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd';
import { DeleteOutlined, EditOutlined, HolderOutlined, PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type {
  SaveShopTaskDefinitionPayload,
  ShopTaskDefinition,
} from '@drop/types/shopTask';
import { Modal, PageContentSpin } from '@drop/ui';
import type { ShopTasksPortalConfig } from './portalConfig';
import { useShopTasksDeps } from './ShopTasksDepsContext';

const { Text } = Typography;

const EMPTY_FORM: SaveShopTaskDefinitionPayload = {
  name: '',
  url: '',
  urlMatchPattern: null,
  description: null,
  viewDurationSeconds: 0,
  sortOrder: 0,
  isActive: true,
};

export type TaskDefinitionsPanelProps = {
  config: ShopTasksPortalConfig;
  /** Chỉ fetch khi tab đang mở. */
  active: boolean;
};

/**
 * CRUD nhiệm vụ "truy cập URL". Backend chỉ chấp nhận host trong allowlist của extension nên URL
 * ngoài TikTok Shop sẽ bị từ chối ngay khi lưu.
 */
export function TaskDefinitionsPanel({ config, active }: TaskDefinitionsPanelProps) {
  const deps = useShopTasksDeps();
  const [form] = Form.useForm<SaveShopTaskDefinitionPayload>();
  const [editing, setEditing] = useState<ShopTaskDefinition | null>(null);
  const [open, setOpen] = useState(false);
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const [dragOverId, setDragOverId] = useState<string | null>(null);
  const dragSourceIdRef = useRef<string | null>(null);

  const canManage = deps.hasPermission(config.managePermission);
  const query = deps.useTaskDefinitions?.({ enabled: active });
  const save = deps.useSaveTaskDefinition?.();
  const remove = deps.useDeleteTaskDefinition?.();
  const reorder = deps.useReorderTaskDefinitions?.();

  const definitions = query?.data ?? [];
  const canDragSort = canManage && !!reorder && !reorder.isPending;

  if (!deps.useTaskDefinitions) {
    return <Result status="403" title="Không khả dụng" subTitle="Portal này không cấu hình nhiệm vụ." />;
  }

  const clearDragState = () => {
    dragSourceIdRef.current = null;
    setDraggingId(null);
    setDragOverId(null);
  };

  const handleDropReorder = (targetId: string) => {
    const sourceId = dragSourceIdRef.current;
    clearDragState();
    if (!sourceId || sourceId === targetId || !canDragSort) return;

    const orderedIds = definitions.map((row) => row.id);
    const fromIndex = orderedIds.indexOf(sourceId);
    const toIndex = orderedIds.indexOf(targetId);
    if (fromIndex < 0 || toIndex < 0) return;

    const nextOrderedIds = [...orderedIds];
    const [moved] = nextOrderedIds.splice(fromIndex, 1);
    if (!moved) return;
    nextOrderedIds.splice(toIndex, 0, moved);

    if (nextOrderedIds.every((id, i) => id === orderedIds[i])) return;
    void reorder?.mutateAsync(nextOrderedIds);
  };

  const openCreate = () => {
    setEditing(null);
    form.setFieldsValue(EMPTY_FORM);
    setOpen(true);
  };

  const openEdit = (row: ShopTaskDefinition) => {
    setEditing(row);
    form.setFieldsValue({
      name: row.name,
      url: row.url,
      urlMatchPattern: row.urlMatchPattern ?? null,
      description: row.description ?? null,
      viewDurationSeconds: row.viewDurationSeconds,
      sortOrder: row.sortOrder,
      isActive: row.isActive,
    });
    setOpen(true);
  };

  const submit = async () => {
    const values = await form.validateFields();
    if (!save) return;
    const payload: SaveShopTaskDefinitionPayload = editing
      ? { ...values, sortOrder: editing.sortOrder }
      : { ...values, sortOrder: 0 };
    await save.mutateAsync({ id: editing?.id, payload });
    message.success(editing ? 'Đã cập nhật nhiệm vụ.' : 'Đã tạo nhiệm vụ.');
    setOpen(false);
  };

  const columns: ColumnsType<ShopTaskDefinition> = [
    {
      title: '#',
      key: 'index',
      width: 56,
      align: 'center',
      render: (_, row, index) => {
        const displayIndex = index + 1;
        if (!canManage) return displayIndex;

        return (
          <Tooltip title="Nhấn giữ để kéo thay đổi thứ tự">
            <span
              className={`shop-sort-handle${!canDragSort ? ' is-disabled' : ''}`}
              draggable={canDragSort}
              onDragStart={(e: DragEvent<HTMLSpanElement>) => {
                if (!canDragSort) {
                  e.preventDefault();
                  return;
                }
                dragSourceIdRef.current = row.id;
                setDraggingId(row.id);
                e.dataTransfer.effectAllowed = 'move';
                e.dataTransfer.setData('text/plain', row.id);
              }}
              onDragEnd={clearDragState}
              onDragOver={(e: DragEvent<HTMLSpanElement>) => {
                if (!canDragSort || !dragSourceIdRef.current) return;
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
                if (dragOverId !== row.id) setDragOverId(row.id);
              }}
              onDrop={(e: DragEvent<HTMLSpanElement>) => {
                e.preventDefault();
                e.stopPropagation();
                handleDropReorder(row.id);
              }}
            >
              <HolderOutlined />
              {displayIndex}
            </span>
          </Tooltip>
        );
      },
    },
    {
      title: 'Nhiệm vụ',
      key: 'name',
      render: (_, row) => (
        <div className="shop-task-cell">
          <div className="app-list-line app-list-line--primary">{row.name}</div>
          <div className="app-list-line">
            <Text type="secondary" style={{ fontSize: 11 }}>
              {row.url}
            </Text>
          </div>
        </div>
      ),
    },
    {
      title: 'Xem trang',
      key: 'viewDurationSeconds',
      width: 88,
      align: 'right',
      render: (_, row) =>
        row.viewDurationSeconds > 0 ? (
          <Text style={{ fontSize: 12 }}>{row.viewDurationSeconds}s</Text>
        ) : (
          <Text type="secondary" style={{ fontSize: 11 }}>
            Ngay
          </Text>
        ),
    },
    {
      title: 'Trạng thái',
      key: 'isActive',
      width: 100,
      render: (_, row) => (
        <Tag color={row.isActive ? 'success' : 'default'} className="app-tag-compact">
          {row.isActive ? 'Đang bật' : 'Tắt'}
        </Tag>
      ),
    },
    {
      title: '',
      key: 'actions',
      width: 80,
      render: (_, row) =>
        canManage ? (
          <div className="app-list-card-head-actions">
            <Button
              type="text"
              size="small"
              icon={<EditOutlined />}
              aria-label="Sửa"
              onClick={() => openEdit(row)}
            />
            <Popconfirm
              title="Xoá nhiệm vụ?"
              description="Lịch sử thực hiện của nhiệm vụ này cũng bị xoá."
              okText="Xoá"
              cancelText="Huỷ"
              onConfirm={async () => {
                await remove?.mutateAsync(row.id);
                message.success('Đã xoá nhiệm vụ.');
              }}
            >
              <Button type="text" size="small" danger icon={<DeleteOutlined />} aria-label="Xoá" />
            </Popconfirm>
          </div>
        ) : null,
    },
  ];

  return (
    <div className="shop-task-definitions">
      <div className="admin-filters">
        <Text type="secondary" style={{ fontSize: 12 }}>
          Nhiệm vụ đang bật hiện trên extension của mọi shop Active. Seller tự vào trang (bấm nhiệm
          vụ hoặc đi bằng menu TikTok); extension thấy đúng URL và giữ đủ thời gian xem thì tự đánh
          dấu hoàn thành.
          {canManage ? ' Kéo cột # để đổi thứ tự nhiệm vụ.' : ''}
        </Text>
        <div className="admin-filters-end">
          {canManage ? (
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
              Thêm nhiệm vụ
            </Button>
          ) : null}
        </div>
      </div>
      <PageContentSpin spinning={!!query?.isLoading || !!reorder?.isPending}>
        <Table
          size="small"
          rowKey="id"
          columns={columns}
          dataSource={definitions}
          pagination={false}
          onRow={(record) => ({
            onDragOver: (e: DragEvent<HTMLTableRowElement>) => {
              if (!canDragSort || !dragSourceIdRef.current) return;
              e.preventDefault();
              e.dataTransfer.dropEffect = 'move';
              if (dragOverId !== record.id) setDragOverId(record.id);
            },
            onDrop: (e: DragEvent<HTMLTableRowElement>) => {
              e.preventDefault();
              handleDropReorder(record.id);
            },
            onDragLeave: () => {
              if (dragOverId === record.id) setDragOverId(null);
            },
          })}
          rowClassName={(record) => {
            const classes: string[] = [];
            if (draggingId === record.id) classes.push('shop-row-dragging');
            if (dragOverId === record.id && draggingId !== record.id) {
              classes.push('shop-row-drag-over');
            }
            return classes.join(' ');
          }}
        />
      </PageContentSpin>

      <Modal
        open={open}
        title={editing ? 'Sửa nhiệm vụ' : 'Thêm nhiệm vụ'}
        okText="Lưu"
        cancelText="Huỷ"
        confirmLoading={!!save?.isPending}
        onOk={() => void submit()}
        onCancel={() => setOpen(false)}
      >
        <Form form={form} layout="vertical" initialValues={EMPTY_FORM}>
          <Form.Item
            name="name"
            label="Tên nhiệm vụ"
            rules={[{ required: true, message: 'Nhập tên nhiệm vụ.' }]}
          >
            <Input placeholder="Vào trang chủ Seller Center" />
          </Form.Item>
          <Form.Item
            name="url"
            label="URL"
            rules={[
              { required: true, message: 'Nhập URL.' },
              { type: 'url', message: 'URL không hợp lệ.' },
            ]}
            extra="Phải là https và thuộc seller-us.tiktok.com."
          >
            <Input placeholder="https://seller-us.tiktok.com/homepage" />
          </Form.Item>
          <Form.Item
            name="urlMatchPattern"
            label="Prefix so khớp (tuỳ chọn)"
            extra="Bỏ trống thì so khớp theo origin + path của URL trên."
          >
            <Input placeholder="https://seller-us.tiktok.com/homepage" />
          </Form.Item>
          <Form.Item name="description" label="Mô tả">
            <Input.TextArea rows={2} placeholder="Vì sao shop cần làm việc này mỗi ngày." />
          </Form.Item>
          <Form.Item
            name="viewDurationSeconds"
            label="Thời gian xem trang (giây)"
            extra="Sau khi trang load xong, seller phải ở lại đủ số giây này. Để 0 nếu chỉ cần mở trang."
          >
            <InputNumber min={0} max={600} style={{ width: '100%' }} addonAfter="giây" />
          </Form.Item>
          <Form.Item name="isActive" label="Đang bật" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
