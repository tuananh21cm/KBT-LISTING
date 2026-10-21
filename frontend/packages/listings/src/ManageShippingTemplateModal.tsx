import { useEffect, useState } from 'react';
import {
  Button,
  Form,
  Input,
  InputNumber,
  Popconfirm,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd';
import { Modal } from '@drop/ui';
import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useListingsDeps } from './ListingsDepsContext';
import type {
  CreateShippingTemplateRequest,
  ShippingTemplate,
} from '@drop/types/shippingTemplate';
import {
  DIMENSION_UNIT_OPTIONS,
  normalizeDimensionUnit,
  normalizeWeightUnit,
  WEIGHT_UNIT_OPTIONS,
} from './shippingUnits';

const { Text } = Typography;

export interface ShippingTemplateSeed {
  packageWeight?: number;
  packageLength?: number;
  packageWidth?: number;
  packageHeight?: number;
  weightUnit?: string;
  dimensionUnit?: string;
}

interface ManageShippingTemplateModalProps {
  open: boolean;
  onClose: () => void;
  /** Prefill create form from current product shipping fields. */
  seed?: ShippingTemplateSeed;
}

type TemplateFormValues = {
  name: string;
  packageWeight: number;
  packageLength: number;
  packageWidth: number;
  packageHeight: number;
  weightUnit: string;
  dimensionUnit: string;
  isDefault: boolean;
};

export function ManageShippingTemplateModal({
  open,
  onClose,
  seed,
}: ManageShippingTemplateModalProps) {
  const deps = useListingsDeps();
  const { data: templates = [], isLoading, refetch } = deps.useShippingTemplates(open);
  const createMutation = deps.useCreateShippingTemplate();
  const updateMutation = deps.useUpdateShippingTemplate();
  const deleteMutation = deps.useDeleteShippingTemplate();

  const [form] = Form.useForm<TemplateFormValues>();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editorOpen, setEditorOpen] = useState(false);

  useEffect(() => {
    if (!open) {
      setEditorOpen(false);
      setEditingId(null);
      // Do not resetFields here — editor Form is unmounted (destroyOnHidden) and
      // calling reset on a disconnected useForm instance triggers antd warnings.
    }
  }, [open]);

  const openCreate = () => {
    setEditingId(null);
    form.setFieldsValue({
      name: '',
      packageWeight: seed?.packageWeight,
      packageLength: seed?.packageLength,
      packageWidth: seed?.packageWidth,
      packageHeight: seed?.packageHeight,
      weightUnit: normalizeWeightUnit(seed?.weightUnit),
      dimensionUnit: normalizeDimensionUnit(seed?.dimensionUnit),
      isDefault: templates.length === 0,
    });
    setEditorOpen(true);
  };

  const openEdit = (record: ShippingTemplate) => {
    setEditingId(record.id);
    form.setFieldsValue({
      name: record.name,
      packageWeight: record.packageWeight,
      packageLength: record.packageLength,
      packageWidth: record.packageWidth,
      packageHeight: record.packageHeight,
      weightUnit: normalizeWeightUnit(record.weightUnit),
      dimensionUnit: normalizeDimensionUnit(record.dimensionUnit),
      isDefault: !!record.isDefault,
    });
    setEditorOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const payload: CreateShippingTemplateRequest = {
        name: values.name.trim(),
        packageWeight: values.packageWeight,
        packageLength: values.packageLength,
        packageWidth: values.packageWidth,
        packageHeight: values.packageHeight,
        weightUnit: normalizeWeightUnit(values.weightUnit),
        dimensionUnit: normalizeDimensionUnit(values.dimensionUnit),
        isDefault: !!values.isDefault,
      };

      const res = editingId
        ? await updateMutation.mutateAsync({ id: editingId, data: payload })
        : await createMutation.mutateAsync(payload);

      if (res.success) {
        setEditorOpen(false);
        setEditingId(null);
        form.resetFields();
        await refetch();
      }
    } catch {
      // validation errors shown by form
    }
  };

  const setAsDefault = async (record: ShippingTemplate, isDefault: boolean) => {
    const payload: CreateShippingTemplateRequest = {
      name: record.name,
      packageWeight: record.packageWeight,
      packageLength: record.packageLength,
      packageWidth: record.packageWidth,
      packageHeight: record.packageHeight,
      weightUnit: normalizeWeightUnit(record.weightUnit),
      dimensionUnit: normalizeDimensionUnit(record.dimensionUnit),
      isDefault,
    };
    const res = await updateMutation.mutateAsync({ id: record.id, data: payload });
    if (res.success) await refetch();
  };

  const columns: ColumnsType<ShippingTemplate> = [
    {
      title: 'Default',
      width: 72,
      align: 'center',
      render: (_, record) => (
        <Switch
          size="small"
          checked={!!record.isDefault}
          loading={updateMutation.isPending}
          onChange={(checked) => void setAsDefault(record, checked)}
        />
      ),
    },
    {
      title: 'Tên',
      dataIndex: 'name',
      ellipsis: true,
      render: (name: string, record) => (
        <Space size={6}>
          <span>{name}</span>
          {record.isDefault ? <Tag color="orange">Default</Tag> : null}
        </Space>
      ),
    },
    {
      title: 'Weight',
      width: 110,
      render: (_, r) => `${r.packageWeight} ${r.weightUnit}`,
    },
    {
      title: 'Dimensions',
      width: 180,
      render: (_, r) =>
        `${r.packageLength} × ${r.packageWidth} × ${r.packageHeight} ${r.dimensionUnit}`,
    },
    {
      title: '',
      width: 88,
      align: 'right',
      render: (_, record) => (
        <Space size={4}>
          <Button
            type="text"
            size="small"
            icon={<EditOutlined />}
            onClick={() => openEdit(record)}
          />
          <Popconfirm
            title="Xóa template này?"
            okText="Xóa"
            cancelText="Hủy"
            onConfirm={async () => {
              const res = await deleteMutation.mutateAsync(record.id);
              if (res.success) await refetch();
            }}
          >
            <Button type="text" size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Modal
        title="Shipping Templates"
        open={open}
        onCancel={onClose}
        footer={null}
        width={720}
        destroyOnClose
        // Nested above ListingEditPage shipping section / other modals (default 1000).
        zIndex={1100}
      >
        <Space style={{ marginBottom: 12 }} wrap>
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            Tạo template
          </Button>
          <Text type="secondary">Template lưu riêng theo tài khoản của bạn.</Text>
        </Space>
        <Table
          rowKey="id"
          size="small"
          loading={isLoading}
          columns={columns}
          dataSource={templates}
          pagination={false}
          locale={{ emptyText: 'Chưa có template. Tạo mới để autofill Shipping.' }}
        />
      </Modal>

      <Modal
        title={editingId ? 'Sửa Shipping Template' : 'Tạo Shipping Template'}
        open={editorOpen}
        onCancel={() => {
          setEditorOpen(false);
          setEditingId(null);
        }}
        onOk={() => void handleSave()}
        okText="Lưu"
        cancelText="Hủy"
        confirmLoading={createMutation.isPending || updateMutation.isPending}
        forceRender
        destroyOnHidden={false}
        // forceRender mounts early; pin above the manage list modal when opened.
        zIndex={1200}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item
            name="name"
            label="Tên template"
            rules={[{ required: true, message: 'Nhập tên template' }]}
          >
            <Input placeholder="VD: Small box US" maxLength={100} />
          </Form.Item>
          <Form.Item label="Weight with Package" required style={{ marginBottom: 12 }}>
            <Space.Compact style={{ width: '100%' }}>
              <Form.Item
                name="packageWeight"
                noStyle
                rules={[{ required: true, message: 'Nhập weight' }]}
              >
                <InputNumber min={0.01} step={0.01} placeholder="Weight" style={{ width: '70%' }} />
              </Form.Item>
              <Form.Item name="weightUnit" noStyle initialValue="lb">
                <Select style={{ width: '30%' }} options={[...WEIGHT_UNIT_OPTIONS]} />
              </Form.Item>
            </Space.Compact>
          </Form.Item>
          <Form.Item label="Product Dimensions" required style={{ marginBottom: 12 }}>
            <Space.Compact style={{ width: '100%' }}>
              <Form.Item
                name="packageLength"
                noStyle
                rules={[{ required: true, message: 'Length' }]}
              >
                <InputNumber min={0.01} placeholder="L" style={{ width: '22%' }} />
              </Form.Item>
              <Form.Item
                name="packageWidth"
                noStyle
                rules={[{ required: true, message: 'Width' }]}
              >
                <InputNumber min={0.01} placeholder="W" style={{ width: '22%' }} />
              </Form.Item>
              <Form.Item
                name="packageHeight"
                noStyle
                rules={[{ required: true, message: 'Height' }]}
              >
                <InputNumber min={0.01} placeholder="H" style={{ width: '22%' }} />
              </Form.Item>
              <Form.Item name="dimensionUnit" noStyle initialValue="inch">
                <Select style={{ width: '34%' }} options={[...DIMENSION_UNIT_OPTIONS]} />
              </Form.Item>
            </Space.Compact>
          </Form.Item>
          <Form.Item
            name="isDefault"
            label="Đặt làm default"
            valuePropName="checked"
            style={{ marginBottom: 0 }}
          >
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
