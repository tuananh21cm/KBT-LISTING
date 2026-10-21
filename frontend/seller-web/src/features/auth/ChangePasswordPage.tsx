import { Button, Card, Form, Input, Typography } from 'antd';
import { LockOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { useChangePassword } from '../../hooks/useAuth';
import type { ChangePasswordRequest } from '../../types/auth';

const { Title } = Typography;

export const ChangePasswordPage = () => {
  const navigate = useNavigate();
  const changePasswordMutation = useChangePassword();
  const [form] = Form.useForm<ChangePasswordRequest>();

  const onFinish = (values: ChangePasswordRequest) => {
    changePasswordMutation.mutate(values, {
      onSuccess: (response) => {
        if (response.success) {
          form.resetFields();
          navigate('/profile');
        }
      },
    });
  };

  return (
    <div>
      <Title level={5} style={{ marginTop: 0 }}>Đổi mật khẩu</Title>
      <Card style={{ maxWidth: 480, borderRadius: 6 }}>
        <Form form={form} layout="vertical" onFinish={onFinish} size="small">
          <Form.Item
            name="currentPassword"
            label="Mật khẩu hiện tại"
            rules={[{ required: true, message: 'Vui lòng nhập mật khẩu hiện tại' }]}
          >
            <Input.Password prefix={<LockOutlined />} />
          </Form.Item>
          <Form.Item
            name="newPassword"
            label="Mật khẩu mới"
            rules={[
              { required: true, message: 'Vui lòng nhập mật khẩu mới' },
              { min: 8, message: 'Mật khẩu phải có ít nhất 8 ký tự' },
            ]}
          >
            <Input.Password prefix={<LockOutlined />} />
          </Form.Item>
          <Form.Item
            name="confirmPassword"
            label="Xác nhận mật khẩu mới"
            dependencies={['newPassword']}
            rules={[
              { required: true, message: 'Vui lòng xác nhận mật khẩu' },
              ({ getFieldValue }) => ({
                validator(_, value: string) {
                  if (!value || getFieldValue('newPassword') === value) {
                    return Promise.resolve();
                  }
                  return Promise.reject(new Error('Xác nhận mật khẩu không khớp'));
                },
              }),
            ]}
          >
            <Input.Password prefix={<LockOutlined />} />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={changePasswordMutation.isPending}>
              Cập nhật mật khẩu
            </Button>
            <Button style={{ marginLeft: 8 }} onClick={() => navigate('/profile')}>
              Hủy
            </Button>
          </Form.Item>
        </Form>
      </Card>
    </div>
  );
};
