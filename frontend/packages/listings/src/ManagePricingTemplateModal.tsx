import { useEffect, useState } from 'react';
import {
  Button,
  Col,
  Form,
  Input,
  Popconfirm,
  Radio,
  Row,
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
  CreatePricingTemplateRequest,
  PricingTargetMode,
  PricingTemplate,
} from '@drop/types/pricingTemplate';
import { DEFAULT_PRICING_FORMULA } from './pricingBatchUtils';
import { InputNumberWithUnit } from './InputNumberWithUnit';

const { Text } = Typography;

export type PricingTemplateSeed = Partial<CreatePricingTemplateRequest>;

interface ManagePricingTemplateModalProps {
  open: boolean;
  onClose: () => void;
  /** Prefill create form from current calculator values. */
  seed?: PricingTemplateSeed;
}

type TemplateFormValues = CreatePricingTemplateRequest;

export function ManagePricingTemplateModal({
  open,
  onClose,
  seed,
}: ManagePricingTemplateModalProps) {
  const deps = useListingsDeps();
  const { data: templates = [], isLoading, refetch } = deps.usePricingTemplates(true);
  const createMutation = deps.useCreatePricingTemplate();
  const updateMutation = deps.useUpdatePricingTemplate();
  const deleteMutation = deps.useDeletePricingTemplate();

  const [form] = Form.useForm<TemplateFormValues>();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editorOpen, setEditorOpen] = useState(false);
  const targetMode = Form.useWatch('targetMode', form) as PricingTargetMode | undefined;

  useEffect(() => {
    if (!open) {
      setEditorOpen(false);
      setEditingId(null);
      // Do not resetFields here — editor Form is unmounted while closed and
      // calling reset on a disconnected useForm instance triggers antd warnings.
    }
  }, [open]);

  const openCreate = () => {
    setEditingId(null);
    form.setFieldsValue({
      name: '',
      ...DEFAULT_PRICING_FORMULA,
      ...seed,
      targetMode: seed?.targetMode === 'profit' ? 'profit' : 'margin',
      isDefault: false,
    });
    setEditorOpen(true);
  };

  const openEdit = (record: PricingTemplate) => {
    setEditingId(record.id);
    form.setFieldsValue({
      name: record.name,
      supplierShipping: record.supplierShipping,
      tax: record.tax,
      discountPercent: record.discountPercent,
      fastSalePercent: record.fastSalePercent,
      adsFee: record.adsFee,
      tikTokShipping: record.tikTokShipping,
      platformFeePercent: record.platformFeePercent,
      targetMode: record.targetMode === 'profit' ? 'profit' : 'margin',
      marginPercent: record.marginPercent,
      profitAmount: record.profitAmount,
      isDefault: record.isDefault,
    });
    setEditorOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const payload: CreatePricingTemplateRequest = {
        name: values.name.trim(),
        supplierShipping: values.supplierShipping,
        tax: values.tax,
        discountPercent: values.discountPercent,
        fastSalePercent: values.fastSalePercent,
        adsFee: values.adsFee,
        tikTokShipping: values.tikTokShipping,
        platformFeePercent: values.platformFeePercent,
        targetMode: values.targetMode === 'profit' ? 'profit' : 'margin',
        marginPercent: values.marginPercent ?? 0,
        profitAmount: values.profitAmount ?? 0,
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

  const setAsDefault = async (record: PricingTemplate, isDefault: boolean) => {
    const payload: CreatePricingTemplateRequest = {
      name: record.name,
      supplierShipping: record.supplierShipping,
      tax: record.tax,
      discountPercent: record.discountPercent,
      fastSalePercent: record.fastSalePercent,
      adsFee: record.adsFee,
      tikTokShipping: record.tikTokShipping,
      platformFeePercent: record.platformFeePercent,
      targetMode: record.targetMode === 'profit' ? 'profit' : 'margin',
      marginPercent: record.marginPercent,
      profitAmount: record.profitAmount,
      isDefault,
    };
    const res = await updateMutation.mutateAsync({ id: record.id, data: payload });
    if (res.success) await refetch();
  };

  const columns: ColumnsType<PricingTemplate> = [
    {
      title: 'Default',
      width: 72,
      align: 'center',
      render: (_, record) => (
        <Switch
          size="small"
          checked={record.isDefault}
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
      title: 'Promo',
      width: 72,
      render: (_, r) => `${Math.max(r.discountPercent, r.fastSalePercent)}%`,
    },
    {
      title: 'Target',
      width: 110,
      render: (_, r) =>
        r.targetMode === 'profit' ? `Profit $${r.profitAmount}` : `Margin ${r.marginPercent}%`,
    },
    {
      title: 'Phí sàn',
      width: 68,
      render: (_, r) => `${r.platformFeePercent}%`,
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
        title="Pricing Templates"
        open={open}
        onCancel={onClose}
        footer={null}
        width={760}
        destroyOnHidden
        // Above BatchPricingModal (default 1000); editor sits higher still.
        zIndex={1100}
      >
        <Space style={{ marginBottom: 12 }} wrap>
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            Tạo template
          </Button>
          <Text type="secondary">Default xếp đầu · dùng tự động khi thêm sản phẩm (có cost).</Text>
        </Space>
        <Table
          rowKey="id"
          size="small"
          loading={isLoading}
          columns={columns}
          dataSource={templates}
          pagination={false}
          locale={{ emptyText: 'Chưa có template. Tạo mới để batch tính giá bán SKU.' }}
        />
      </Modal>

      <Modal
        title={editingId ? 'Sửa Pricing Template' : 'Tạo Pricing Template'}
        open={editorOpen}
        onCancel={() => {
          setEditorOpen(false);
          setEditingId(null);
        }}
        onOk={() => void handleSave()}
        okText="Lưu"
        cancelText="Hủy"
        confirmLoading={createMutation.isPending || updateMutation.isPending}
        width={560}
        forceRender
        destroyOnHidden={false}
        // forceRender mounts early (below Batch/Manage); pin above both when opened.
        zIndex={1200}
        className="fs-pricing-template-editor"
      >
        <Form form={form} layout="vertical" className="fs-batch-pricing-form fs-pricing-template-editor-form" requiredMark={false}>
          <Form.Item
            name="name"
            label="Tên template"
            rules={[{ required: true, message: 'Nhập tên template' }]}
            style={{ marginBottom: 12 }}
          >
            <Input placeholder="VD: Shein → TT US" maxLength={100} />
          </Form.Item>

          <Text strong className="fs-batch-pricing-section-title">
            Chi phí nhập & TikTok
          </Text>
          <Row gutter={[12, 0]}>
            <Col span={12}>
              <Form.Item
                name="supplierShipping"
                label="Shipping (nhập)"
                rules={[{ required: true, message: 'Bắt buộc' }]}
                style={{ marginBottom: 10 }}
              >
                <InputNumberWithUnit min={0} step={0.01} unit="USD" className="fs-batch-pricing-input" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name="tax"
                label="Tax"
                rules={[{ required: true, message: 'Bắt buộc' }]}
                style={{ marginBottom: 10 }}
              >
                <InputNumberWithUnit min={0} step={0.01} unit="USD" className="fs-batch-pricing-input" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name="adsFee"
                label="Ads Fee"
                rules={[{ required: true, message: 'Bắt buộc' }]}
                style={{ marginBottom: 10 }}
              >
                <InputNumberWithUnit min={0} step={0.01} unit="USD" className="fs-batch-pricing-input" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name="tikTokShipping"
                label="TT Shipping (+ DT)"
                rules={[{ required: true, message: 'Bắt buộc' }]}
                style={{ marginBottom: 10 }}
              >
                <InputNumberWithUnit min={0} step={0.01} unit="USD" className="fs-batch-pricing-input" />
              </Form.Item>
            </Col>
          </Row>

          <Text strong className="fs-batch-pricing-section-title">
            Khuyến mãi & phí sàn
          </Text>
          <Row gutter={[12, 0]}>
            <Col span={8}>
              <Form.Item
                name="discountPercent"
                label="Discount"
                rules={[{ required: true, message: 'Bắt buộc' }]}
                style={{ marginBottom: 10 }}
              >
                <InputNumberWithUnit min={0} max={99.99} step={0.1} unit="%" className="fs-batch-pricing-input" />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item
                name="fastSalePercent"
                label="Fast Sale"
                rules={[{ required: true, message: 'Bắt buộc' }]}
                style={{ marginBottom: 10 }}
              >
                <InputNumberWithUnit min={0} max={99.99} step={0.1} unit="%" className="fs-batch-pricing-input" />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item
                name="platformFeePercent"
                label="Phí sàn"
                rules={[{ required: true, message: 'Bắt buộc' }]}
                style={{ marginBottom: 10 }}
              >
                <InputNumberWithUnit min={0} max={99.99} step={0.1} unit="%" className="fs-batch-pricing-input" />
              </Form.Item>
            </Col>
          </Row>

          <Text strong className="fs-batch-pricing-section-title">
            Mục tiêu lợi nhuận
          </Text>
          <Row gutter={[12, 0]} align="bottom">
            <Col span={14}>
              <Form.Item name="targetMode" label="Chế độ" initialValue="margin" style={{ marginBottom: 0 }}>
                <Radio.Group
                  className="fs-batch-pricing-target"
                  optionType="button"
                  buttonStyle="solid"
                  options={[
                    { label: 'Margin %', value: 'margin' },
                    { label: 'Profit USD', value: 'profit' },
                  ]}
                />
              </Form.Item>
            </Col>
            <Col span={10}>
              {targetMode === 'profit' ? (
                <Form.Item
                  name="profitAmount"
                  label="Profit"
                  rules={[{ required: true, message: 'Nhập profit' }]}
                  style={{ marginBottom: 0 }}
                >
                  <InputNumberWithUnit min={0} step={0.01} unit="USD" className="fs-batch-pricing-input" />
                </Form.Item>
              ) : (
                <Form.Item
                  name="marginPercent"
                  label="Margin trên phí nhập"
                  rules={[{ required: true, message: 'Nhập margin' }]}
                  style={{ marginBottom: 0 }}
                >
                  <InputNumberWithUnit min={0} step={0.1} unit="%" className="fs-batch-pricing-input" />
                </Form.Item>
              )}
            </Col>
          </Row>

          <Form.Item
            name="isDefault"
            label="Default"
            valuePropName="checked"
            style={{ marginTop: 14, marginBottom: 0 }}
            extra="Default xếp đầu danh sách và tự bind giá bán khi thêm sản phẩm."
          >
            <Switch checkedChildren="ON" unCheckedChildren="OFF" />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
