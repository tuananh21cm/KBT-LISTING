import { useState } from 'react';
import {
  Avatar,
  Button,
  Form,
  Input,  Popconfirm,
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
  useAdminProductSources,
  useCreateAdminProductSource,
  useDeleteAdminProductSource,
  useUpdateAdminProductSource,
} from '../../hooks/useAdminProductSources';
import { useAuthStore } from '../../stores/authStore';
import { useIsMobile } from '../../hooks/useIsMobile';
import { PageHeader } from '../../components/PageHeader';
import type { ProductSource } from '../../types/productSource';
import { tablePagination } from '../../utils/pagination';

const { Text } = Typography;

interface ProductSourceFormValues {
  code: string;
  name: string;
  logoUrl?: string;
  domains?: string;
  description?: string;
  isActive: boolean;
}

const MOBILE_SOURCE_COLUMN_KEYS = new Set(['logoUrl', 'name', 'isActive', 'actions']);

export const ProductSourcesPage = () => {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canManage = hasPermission('admin.product-sources.manage');
  const isMobile = useIsMobile();

  const { data: sources = [], isLoading, isFetching, refetch } = useAdminProductSources();
  const createSource = useCreateAdminProductSource();
  const updateSource = useUpdateAdminProductSource();
  const deleteSource = useDeleteAdminProductSource();

  const [modalOpen, setModalOpen] = useState(false);
  const [editingSource, setEditingSource] = useState<ProductSource | null>(null);
  const [form] = Form.useForm<ProductSourceFormValues>();
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10 });

  const openCreate = () => {
    setEditingSource(null);
    form.resetFields();
    form.setFieldsValue({ isActive: true });
    setModalOpen(true);
  };

  const openEdit = (source: ProductSource) => {
    setEditingSource(source);
    form.setFieldsValue({
      code: source.code,
      name: source.name,
      logoUrl: source.logoUrl,
      domains: source.domains,
      description: source.description,
      isActive: source.isActive,
    });
    setModalOpen(true);
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    if (editingSource) {
      await updateSource.mutateAsync({
        id: editingSource.id,
        data: values,
      });
    } else {
      await createSource.mutateAsync(values);
    }
    setModalOpen(false);
  };

  const avatarSize = isMobile ? 28 : 36;

  const columns: ColumnsType<ProductSource> = [
    {
      title: '#',
      key: 'index',
      width: 56,
      align: 'center',
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: isMobile ? '' : 'Logo',
      dataIndex: 'logoUrl',
      key: 'logoUrl',
      width: isMobile ? 40 : 72,
      render: (url: string | undefined, record) => (
        <Avatar src={url} shape="square" size={avatarSize}>
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
      title: isMobile ? 'Nguồn' : 'Tên nguồn hàng',
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
      title: isMobile ? 'TT' : 'Trạng thái',
      dataIndex: 'isActive',
      key: 'isActive',
      width: isMobile ? 72 : 110,
      render: (active: boolean) => (
        <Tag color={active ? 'green' : 'default'}>
          {isMobile ? (active ? 'ON' : 'OFF') : active ? 'Hoạt động' : 'Vô hiệu'}
        </Tag>
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
            <Popconfirm title="Xóa nguồn hàng này?" onConfirm={() => deleteSource.mutate(record.id)}>
              <Button type="text" danger icon={<DeleteOutlined />} />
            </Popconfirm>
          </Space>
        ) : null,
    },
  ];

  const visibleColumns = isMobile
    ? columns.filter((col) => MOBILE_SOURCE_COLUMN_KEYS.has(String(col.key)))
    : columns;

  return (
    <div>
      <PageHeader
        title={isMobile ? 'Nguồn hàng' : 'Quản lý Nguồn hàng'}
        extra={
          canManage ? (
            <Tooltip title="Thêm nguồn hàng">
              <Button
                type="primary"
                icon={<PlusOutlined />}
                onClick={openCreate}
                aria-label="Thêm nguồn hàng"
              >
                {isMobile ? null : 'Thêm nguồn hàng'}
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
        dataSource={sources}
        loading={isLoading || isFetching}
        size="small"
        scroll={isMobile ? undefined : { x: 800 }}
        pagination={tablePagination(sources.length, {
          size: 'small',
          simple: isMobile,
          pageSize: pagination.pageSize,
          current: pagination.current,
          showSizeChanger: !isMobile,
          onChange: (page, pageSize) => setPagination({ current: page, pageSize }),
        })}
      />

      <Modal
        title={editingSource ? 'Sửa nguồn hàng' : 'Thêm nguồn hàng'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={handleSubmit}
        confirmLoading={createSource.isPending || updateSource.isPending}
        destroyOnHidden
        width={isMobile ? '100%' : 520}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            name="code"
            label="Mã nguồn hàng"
            rules={[
              { required: true, message: 'Nhập mã nguồn hàng' },
              { pattern: /^[A-Za-z0-9_-]+$/, message: 'Chỉ dùng chữ, số, gạch ngang và gạch dưới' },
            ]}
          >
            <Input placeholder="VD: SHEIN, TEMU, CJ" disabled={!!editingSource} />
          </Form.Item>
          <Form.Item
            name="name"
            label="Tên hiển thị"
            rules={[{ required: true, message: 'Nhập tên nguồn hàng' }]}
          >
            <Input placeholder="VD: Shein, Temu, CJDropshipping" />
          </Form.Item>
          <Form.Item name="logoUrl" label="URL Logo">
            <Input placeholder="https://..." />
          </Form.Item>
          <Form.Item
            name="domains"
            label="Domain nguồn (cho extension)"
            tooltip="Các host của site nguồn, phân tách bằng dấu phẩy hoặc xuống dòng. Extension dùng để nhận diện trang sản phẩm và hiện nút Thêm sản phẩm."
          >
            <Input.TextArea rows={2} placeholder="VD: shein.com, us.shein.com" />
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
