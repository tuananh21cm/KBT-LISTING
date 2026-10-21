import { useState } from 'react';
import {
  Avatar,
  Button,
  Form,
  Input,
  Popconfirm,
  Space,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import { Modal } from '../../components/AppModal';
import { PlusOutlined, EditOutlined, DeleteOutlined, ReloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import {
  useAdminPlatforms,
  useCreateAdminPlatform,
  useDeleteAdminPlatform,
  useUpdateAdminPlatform,
} from '../../hooks/useAdminPlatforms';
import { useAuthStore } from '../../stores/authStore';
import { useIsMobile } from '../../hooks/useIsMobile';
import { PageHeader } from '../../components/PageHeader';
import type { Platform } from '../../types/platform';
import { tablePagination } from '../../utils/pagination';
import './platforms.css';

const { Text } = Typography;

interface PlatformFormValues {
  code: string;
  name: string;
  description?: string;
  avatarUrl?: string;
  isActive: boolean;
}

const MOBILE_PLATFORM_COLUMN_KEYS = new Set(['avatarUrl', 'name', 'shopCount', 'isActive', 'actions']);

export const PlatformsPage = () => {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canManage = hasPermission('admin.platforms.manage');
  const isMobile = useIsMobile();

  const { data: platforms = [], isLoading, isFetching, refetch } = useAdminPlatforms();
  const createPlatform = useCreateAdminPlatform();
  const updatePlatform = useUpdateAdminPlatform();
  const deletePlatform = useDeleteAdminPlatform();

  const [modalOpen, setModalOpen] = useState(false);
  const [editingPlatform, setEditingPlatform] = useState<Platform | null>(null);
  const [form] = Form.useForm<PlatformFormValues>();
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10 });

  const openCreate = () => {
    setEditingPlatform(null);
    form.resetFields();
    form.setFieldsValue({ isActive: true });
    setModalOpen(true);
  };

  const openEdit = (platform: Platform) => {
    setEditingPlatform(platform);
    form.setFieldsValue({
      code: platform.code,
      name: platform.name,
      description: platform.description,
      avatarUrl: platform.avatarUrl,
      isActive: platform.isActive,
    });
    setModalOpen(true);
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    if (editingPlatform) {
      await updatePlatform.mutateAsync({
        id: editingPlatform.id,
        data: values,
      });
    } else {
      await createPlatform.mutateAsync(values);
    }
    setModalOpen(false);
  };

  const columns: ColumnsType<Platform> = [
    {
      title: '#',
      key: 'index',
      width: 56,
      align: 'center',
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: isMobile ? '' : 'Avatar',
      dataIndex: 'avatarUrl',
      key: 'avatarUrl',
      width: isMobile ? 40 : 72,
      render: (url: string | undefined, record) => (
        <Avatar src={url} shape="square" size={isMobile ? 28 : 36}>
          {record.name.charAt(0)}
        </Avatar>
      ),
    },
    {
      title: 'Mã',
      dataIndex: 'code',
      key: 'code',
      width: 120,
    },
    {
      title: isMobile ? 'Nền tảng' : 'Tên nền tảng',
      dataIndex: 'name',
      key: 'name',
      ellipsis: true,
      render: (name: string, record) =>
        isMobile ? (
          <div style={{ minWidth: 0 }}>
            <div style={{ fontWeight: 500, lineHeight: 1.35 }}>{name}</div>
            <Text type="secondary" style={{ fontSize: 11 }}>
              {record.code}
            </Text>
          </div>
        ) : (
          name
        ),
    },
    {
      title: 'Mô tả',
      dataIndex: 'description',
      key: 'description',
      render: (v) => v || '—',
    },
    {
      title: isMobile ? 'Shop' : 'Số shop',
      dataIndex: 'shopCount',
      key: 'shopCount',
      width: isMobile ? 56 : 90,
      align: isMobile ? 'center' : undefined,
    },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      key: 'isActive',
      width: isMobile ? 88 : 110,
      render: (active: boolean) => (
        <Tag color={active ? 'green' : 'default'}>{active ? 'Hoạt động' : 'Vô hiệu'}</Tag>
      ),
    },
    {
      title: isMobile ? '' : 'Thao tác',
      key: 'actions',
      width: isMobile ? 72 : 100,
      align: isMobile ? 'right' : undefined,
      render: (_, record) =>
        canManage ? (
          <Space size={isMobile ? 0 : undefined}>
            <Button type="text" icon={<EditOutlined />} onClick={() => openEdit(record)} />
            <Popconfirm
              title="Xóa nền tảng này?"
              description={record.shopCount > 0 ? 'Nền tảng đang có shop, không thể xóa.' : undefined}
              onConfirm={() => deletePlatform.mutate(record.id)}
              disabled={record.shopCount > 0}
            >
              <Button type="text" danger icon={<DeleteOutlined />} disabled={record.shopCount > 0} />
            </Popconfirm>
          </Space>
        ) : null,
    },
  ];

  const visibleColumns = isMobile
    ? columns.filter((col) => MOBILE_PLATFORM_COLUMN_KEYS.has(String(col.key)))
    : columns;

  return (
    <div>
      <PageHeader
        title={isMobile ? 'Nền tảng' : 'Quản lý Nền tảng'}
        extra={
          canManage ? (
            <Tooltip title="Thêm nền tảng">
              <Button type="primary" icon={<PlusOutlined />} onClick={openCreate} aria-label="Thêm nền tảng">
                {isMobile ? null : 'Thêm nền tảng'}
              </Button>
            </Tooltip>
          ) : undefined
        }
      />

      <div className="admin-filters">
        <div className="admin-filters-actions" style={{ marginLeft: 0 }}>
          <Button
            icon={<ReloadOutlined />}
            onClick={() => void refetch()}
            loading={isFetching}
            aria-label="Tải lại"
          >
            {isMobile ? null : 'Tải lại'}
          </Button>
        </div>
      </div>

      <Table
        rowKey="id"
        columns={visibleColumns}
        dataSource={platforms}
        loading={isLoading || isFetching}
        size="small"
        scroll={isMobile ? undefined : { x: 800 }}
        pagination={tablePagination(platforms.length, {
          size: 'small',
          simple: isMobile,
          pageSize: pagination.pageSize,
          current: pagination.current,
          showSizeChanger: !isMobile,
          onChange: (page, pageSize) => setPagination({ current: page, pageSize }),
        })}
      />

      <Modal
        className="plat-edit-modal"
        title={editingPlatform ? 'Sửa nền tảng' : 'Thêm nền tảng'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={handleSubmit}
        confirmLoading={createPlatform.isPending || updatePlatform.isPending}
        destroyOnHidden
        width={isMobile ? '100%' : 520}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            name="code"
            label="Mã nền tảng"
            rules={[
              { required: true, message: 'Nhập mã nền tảng' },
              { pattern: /^[A-Za-z0-9_-]+$/, message: 'Chỉ dùng chữ, số, gạch ngang và gạch dưới' },
            ]}
          >
            <Input placeholder="VD: TikTok, Amazon" disabled={!!editingPlatform} />
          </Form.Item>
          <Form.Item name="name" label="Tên hiển thị" rules={[{ required: true, message: 'Nhập tên nền tảng' }]}>
            <Input placeholder="VD: TikTok Shop" />
          </Form.Item>
          <Form.Item name="avatarUrl" label="URL Avatar">
            <Input placeholder="https://..." />
          </Form.Item>
          <Form.Item name="description" label="Mô tả">
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="isActive" label="Trạng thái" valuePropName="checked">
            <Switch checkedChildren="Hoạt động" unCheckedChildren="Vô hiệu" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};
