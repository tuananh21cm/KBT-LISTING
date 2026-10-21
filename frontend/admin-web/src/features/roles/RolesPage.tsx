import { useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Checkbox,
  Form,
  Input,  Popconfirm,
  Space,
  Spin,
  Tabs,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import { Modal } from '../../components/AppModal';
import { PlusOutlined, DeleteOutlined, SaveOutlined, ReloadOutlined } from '@ant-design/icons';
import {
  useAdminPermissions,
  useAdminRole,
  useAdminRoles,
  useCreateAdminRole,
  useDeleteAdminRole,
  useSetRolePermissions,
} from '../../hooks/useAdminRoles';
import { useAuthStore } from '../../stores/authStore';
import { useIsMobile } from '../../hooks/useIsMobile';
import { PageHeader } from '../../components/PageHeader';
import type { Permission } from '../../types/role';
import './roles.css';

const { Text } = Typography;

interface RoleFormValues {
  name: string;
  description?: string;
}

export const RolesPage = () => {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canManage = hasPermission('admin.roles.manage');
  const isMobile = useIsMobile();

  const { data: roles = [], isLoading, isFetching, refetch } = useAdminRoles();
  const { data: permissions = [] } = useAdminPermissions();
  const createRole = useCreateAdminRole();
  const deleteRole = useDeleteAdminRole();
  const setRolePermissions = useSetRolePermissions();

  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null);
  const { data: selectedRole, isLoading: isLoadingRole } = useAdminRole(selectedRoleId);
  const [checkedPermissionIds, setCheckedPermissionIds] = useState<string[]>([]);
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm<RoleFormValues>();

  useEffect(() => {
    if (roles.length === 0) {
      setSelectedRoleId(null);
      return;
    }
    if (!selectedRoleId || !roles.some((role) => role.id === selectedRoleId)) {
      setSelectedRoleId(roles[0]!.id);
    }
  }, [roles, selectedRoleId]);

  useEffect(() => {
    if (selectedRole) {
      setCheckedPermissionIds(selectedRole.permissionIds);
    }
  }, [selectedRole]);

  const groupedPermissions = useMemo(() => {
    const groups = new Map<string, Permission[]>();
    for (const permission of permissions) {
      const key = `${permission.application} / ${permission.module}`;
      const list = groups.get(key) ?? [];
      list.push(permission);
      groups.set(key, list);
    }
    return Array.from(groups.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [permissions]);

  const openCreate = () => {
    form.resetFields();
    setModalOpen(true);
  };

  const handleCreateRole = async () => {
    const values = await form.validateFields();
    const response = await createRole.mutateAsync(values);
    if (response.success && response.data) {
      setSelectedRoleId(response.data.id);
    }
    setModalOpen(false);
  };

  const handleSavePermissions = async () => {
    if (!selectedRoleId) return;
    await setRolePermissions.mutateAsync({
      id: selectedRoleId,
      data: { permissionIds: checkedPermissionIds },
    });
  };

  const togglePermission = (permissionId: string, checked: boolean) => {
    setCheckedPermissionIds((prev) =>
      checked ? [...prev, permissionId] : prev.filter((id) => id !== permissionId),
    );
  };

  const toggleGroup = (groupPermissions: Permission[], checked: boolean) => {
    const ids = groupPermissions.map((p) => p.id);
    setCheckedPermissionIds((prev) => {
      if (checked) {
        return [...new Set([...prev, ...ids])];
      }
      return prev.filter((id) => !ids.includes(id));
    });
  };

  const tabItems = roles.map((role) => ({
    key: role.id,
    label: isMobile ? (
      <span>
        {role.name}
        <Tag style={{ marginInlineStart: 6, marginInlineEnd: 0 }}>{role.permissionCount}</Tag>
      </span>
    ) : (
      <Space size={6}>
        <span>{role.name}</span>
        {role.isSystem && (
          <Tag color="blue" style={{ margin: 0 }}>
            Hệ thống
          </Tag>
        )}
        <Tag style={{ margin: 0 }}>{role.permissionCount}</Tag>
      </Space>
    ),
    children:
      selectedRoleId === role.id ? (
        <Spin spinning={isLoadingRole}>
          {selectedRole ? (
            <div>
              <div
                style={{
                  display: 'flex',
                  flexDirection: isMobile ? 'column' : 'row',
                  justifyContent: 'space-between',
                  alignItems: isMobile ? 'stretch' : 'flex-start',
                  marginBottom: 16,
                  gap: isMobile ? 10 : 12,
                }}
              >
                <div style={{ minWidth: 0 }}>
                  {isMobile && selectedRole.isSystem ? (
                    <Tag color="blue" style={{ marginBottom: 6 }}>
                      Hệ thống
                    </Tag>
                  ) : null}
                  <Text type="secondary" style={{ display: 'block' }}>
                    {selectedRole.description || 'Không có mô tả'}
                  </Text>
                </div>
                <div
                  style={{
                    display: 'flex',
                    flexWrap: 'wrap',
                    gap: 8,
                    width: isMobile ? '100%' : undefined,
                  }}
                >
                  {canManage && !selectedRole.isSystem && (
                    <Popconfirm
                      title="Xóa vai trò này?"
                      onConfirm={() => deleteRole.mutate(role.id)}
                    >
                      <Button
                        danger
                        icon={<DeleteOutlined />}
                        aria-label="Xóa vai trò"
                        style={isMobile ? { flex: 1 } : undefined}
                      >
                        {isMobile ? 'Xóa' : 'Xóa vai trò'}
                      </Button>
                    </Popconfirm>
                  )}
                  {canManage && (
                    <Button
                      type="primary"
                      icon={<SaveOutlined />}
                      loading={setRolePermissions.isPending}
                      onClick={handleSavePermissions}
                      aria-label="Lưu phân quyền"
                      style={isMobile ? { flex: 1 } : undefined}
                    >
                      {isMobile ? 'Lưu' : 'Lưu phân quyền'}
                    </Button>
                  )}
                </div>
              </div>

              {selectedRole.name === 'Admin' && (
                <Alert
                  className="roles-alert"
                  type="warning"
                  showIcon
                  title={
                    isMobile
                      ? 'Admin đồng bộ toàn quyền khi seed — có thể bị ghi đè khi restart.'
                      : 'Vai trò Admin được đồng bộ toàn quyền khi seed. Thay đổi có thể bị ghi đè khi khởi động lại hệ thống.'
                  }
                  style={{ marginBottom: 16 }}
                />
              )}

              {groupedPermissions.map(([group, groupPermissions]) => {
                const allChecked = groupPermissions.every((p) =>
                  checkedPermissionIds.includes(p.id),
                );
                const indeterminate =
                  !allChecked &&
                  groupPermissions.some((p) => checkedPermissionIds.includes(p.id));

                return (
                  <div
                    key={group}
                    className="roles-perm-group"
                    style={{ marginBottom: isMobile ? 12 : 16 }}
                  >
                    <Checkbox
                      indeterminate={indeterminate}
                      checked={allChecked}
                      disabled={!canManage}
                      onChange={(e) => toggleGroup(groupPermissions, e.target.checked)}
                    >
                      <Text strong style={{ fontSize: isMobile ? 12 : undefined }}>
                        {group}
                      </Text>
                    </Checkbox>
                    <div style={{ marginLeft: isMobile ? 16 : 24, marginTop: 8 }}>
                      {groupPermissions.map((permission) => (
                        <div key={permission.id} style={{ marginBottom: 4 }}>
                          <Checkbox
                            checked={checkedPermissionIds.includes(permission.id)}
                            disabled={!canManage}
                            onChange={(e) => togglePermission(permission.id, e.target.checked)}
                          >
                            <Text style={{ fontSize: isMobile ? 12 : undefined }}>
                              {permission.name}
                            </Text>
                            {!isMobile ? (
                              <Text type="secondary" style={{ marginLeft: 8, fontSize: 12 }}>
                                ({permission.code})
                              </Text>
                            ) : null}
                          </Checkbox>
                        </div>
                      ))}
                    </div>
                  </div>
                );
              })}
            </div>
          ) : (
            <Text type="secondary">Đang tải phân quyền...</Text>
          )}
        </Spin>
      ) : null,
  }));

  return (
    <div>
      <PageHeader
        title={isMobile ? 'Phân quyền' : 'Quản lý Phân quyền'}
        extra={
          canManage ? (
            <Tooltip title="Thêm vai trò">
              <Button type="primary" icon={<PlusOutlined />} onClick={openCreate} aria-label="Thêm vai trò">
                {isMobile ? null : 'Thêm vai trò'}
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

      {isLoading ? (
        <Spin />
      ) : roles.length === 0 ? (
        <Text type="secondary">Chưa có vai trò nào.</Text>
      ) : (
        <Tabs
          className="admin-status-tabs"
          size="small"
          activeKey={selectedRoleId ?? undefined}
          onChange={setSelectedRoleId}
          items={tabItems}
          destroyOnHidden
        />
      )}

      <Modal
        className="roles-edit-modal"
        title="Thêm vai trò"
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={handleCreateRole}
        confirmLoading={createRole.isPending}
        destroyOnHidden
        width={isMobile ? '100%' : undefined}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            name="name"
            label="Tên vai trò"
            rules={[{ required: true, message: 'Nhập tên vai trò' }]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="description" label="Mô tả">
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};
